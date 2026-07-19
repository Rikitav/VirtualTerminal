using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VirtualTerminal.Vt;

/// <summary>
/// A table-driven VT500-family state machine that parses a raw byte stream into structured
/// events delivered to an <see cref="IVtHandler"/>. Reference: Paul Williams' VT500 state
/// machine (https://vt100.net/emu/dec_ansi_parser), xterm ctlseqs, ECMA-48.
///
/// <para>
/// Robustness contract: it <b>never throws</b> on malformed/unknown input — unknown
/// sequences are consumed and the machine returns to <see cref="State.Ground"/>. This is the
/// opposite of the legacy parser, which threw <c>InvalidByteException</c>.
/// </para>
/// </summary>
public sealed class VtDecoder(IVtHandler handler) : IDisposable
{
    private enum State : byte
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        OscString,
        DcsEntry,
        DcsParam,
        DcsIntermediate,
        DcsPassthrough,
        SosPmApcString,
    }

    private const byte ESC = 0x1B;
    private const byte BEL = 0x07;
    private const byte Backslash = (byte)'\\';

    private readonly IVtHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    // UTF-8 accumulation (Ground prints decoded runes).
    private readonly List<byte> _utf8 = [];

    // CSI / DCS parameter accumulation.
    private int[] _paramValues = new int[8];
    private bool[] _paramSub = new bool[8];
    private int _paramCount;
    private int _accumulator;
    private bool _hasParam;
    private bool _currentIsSub;   // is the value currently being accumulated a ':' sub-param?
    private bool _trailingSep;    // last token before final was a separator (→ trailing empty param)

    // Intermediate bytes (0x20-0x2F).
    private byte[] _intermediates = new byte[4];
    private int _interCount;

    // DEC private marker ('?', '>', '=', '<', '!') or '\0'.
    private char _privateMarker;

    // String payloads (OSC / DCS / SOS-PM-APC).
    private readonly List<byte> _stringBuf = [];
    private bool _stringSawEsc;   // for ESC \ (ST) termination inside a string state

    // DCS dispatch capture (final byte + private marker captured at hook time).
    private byte _dcsFinal;

    private State _state = State.Ground;
    private bool _disposed;

    public Encoding Encoding
    {
        get => Encoding.UTF8;
    }

    /// <summary>Feeds a raw byte stream through the state machine.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            try
            {
                ProcessByte(b);
            }
            catch (Exception ex)
            {
                // Safety net only: the machine must survive any input.
                Debug.WriteLine($"VtDecoder recovered from an unexpected error processing byte {b:X2}: {ex}");
                ResetToGround();
            }
        }
    }

    private void ProcessByte(byte b)
    {
        switch (_state)
        {
            case State.Ground:
                ProcessGround(b);
                break;
            case State.Escape:
                ProcessEscape(b);
                break;
            case State.EscapeIntermediate:
                ProcessEscapeIntermediate(b);
                break;
            case State.CsiEntry:
                ProcessCsiEntry(b);
                break;
            case State.CsiParam:
                ProcessCsiParam(b);
                break;
            case State.CsiIntermediate:
                ProcessCsiIntermediate(b);
                break;
            case State.OscString:
                ProcessOscString(b);
                break;
            case State.DcsEntry:
                ProcessDcsEntry(b);
                break;
            case State.DcsParam:
                ProcessDcsParam(b);
                break;
            case State.DcsIntermediate:
                ProcessDcsIntermediate(b);
                break;
            case State.DcsPassthrough:
                ProcessDcsPassthrough(b);
                break;
            case State.SosPmApcString:
                ProcessSosPmApc(b);
                break;
        }
    }

    // ---- Ground ----
    private void ProcessGround(byte b)
    {
        if (b == ESC)
        {
            FlushUtf8();
            _state = State.Escape;
            return;
        }

        if (b < 0x20 || b == 0x7F)
        {
            // C0 control or DEL: execute. (0x18 CAN / 0x1A SUB cancel; treat as execute→ground.)
            FlushUtf8();
            _handler.Execute(b);
            return;
        }

        // Printable or multibyte continuation: accumulate and decode.
        _utf8.Add(b);
        DecodeUtf8();
    }

    // ---- Escape ----
    private void ProcessEscape(byte b)
    {
        switch (b)
        {
            case (byte)'[':
                BeginCsi();
                _state = State.CsiEntry;
                return;
            case (byte)']':
                BeginString();
                _state = State.OscString;
                return;
            case (byte)'P':
                BeginCsi();
                _state = State.DcsEntry;
                return;
            case (byte)'X':  // SOS
            case (byte)'^':  // PM
            case (byte)'_':  // APC
                BeginString();
                _dcsFinal = b;  // reuse as the introducer kind for SosPmApcDispatch
                _state = State.SosPmApcString;
                return;
            case ESC:
                // Nested escape: restart.
                return;
            case >= 0x20 and <= 0x2F:
                PushIntermediate(b);
                _state = State.EscapeIntermediate;
                return;
            case >= 0x30 and <= 0x7E:
                _handler.EscDispatch(b, IntermediatesSpan());
                ResetToGround();
                return;
            default:
                if (b < 0x20)
                    _handler.Execute(b);  // C0 executes even mid-escape
                // else ignore
                return;
        }
    }

    private void ProcessEscapeIntermediate(byte b)
    {
        switch (b)
        {
            case ESC:
                ResetToGround();
                _state = State.Escape;
                return;
            case >= 0x20 and <= 0x2F:
                PushIntermediate(b);
                return;
            case >= 0x30 and <= 0x7E:
                _handler.EscDispatch(b, IntermediatesSpan());
                ResetToGround();
                return;
            default:
                if (b < 0x20)
                    _handler.Execute(b);
                return;
        }
    }

    // ---- CSI ----
    private void ProcessCsiEntry(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateDigit(b);
            _state = State.CsiParam;
            return;
        }
        if (b is (byte)';' or (byte)':')
        {
            OnSeparator(b);
            _state = State.CsiParam;
            return;
        }
        if (b is >= 0x3C and <= 0x3F)  // < = > ?  (private markers)
        {
            _privateMarker = (char)b;
            _state = State.CsiParam;
            return;
        }
        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            _state = State.CsiIntermediate;
            return;
        }
        if (b is >= 0x40 and <= 0x7E)
        {
            DispatchCsi(b);
            return;
        }
        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
            return;
        }
        if (b < 0x20)
            _handler.Execute(b);
        // else ignore unknown
    }

    private void ProcessCsiParam(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateDigit(b);
            return;
        }
        if (b is (byte)';' or (byte)':')
        {
            OnSeparator(b);
            return;
        }
        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            _state = State.CsiIntermediate;
            return;
        }
        if (b is >= 0x40 and <= 0x7E)
        {
            DispatchCsi(b);
            return;
        }
        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
            return;
        }
        if (b < 0x20)
            _handler.Execute(b);
    }

    private void ProcessCsiIntermediate(byte b)
    {
        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            return;
        }
        if (b is >= 0x40 and <= 0x7E)
        {
            DispatchCsi(b);
            return;
        }
        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
            return;
        }
        if (b < 0x20)
            _handler.Execute(b);
    }

    private void DispatchCsi(byte final)
    {
        PushTrailingParam();
        _handler.CsiDispatch(final, IntermediatesSpan(), new CsiParams(_paramValues, _paramSub, _paramCount), _privateMarker);
        ResetToGround();
    }

    // ---- OSC ----
    private void ProcessOscString(byte b)
    {
        if (_stringSawEsc)
        {
            _stringSawEsc = false;
            if (b == Backslash)
            {
                _handler.OscDispatch(_stringBuf.ToArray());
                ResetToGround();
                return;
            }
            // ESC was the start of a new sequence; terminate OSC and reprocess from Escape.
            _handler.OscDispatch(_stringBuf.ToArray());
            ResetToGround();
            _state = State.Escape;
            ProcessEscape(b);
            return;
        }

        if (b == BEL)
        {
            _handler.OscDispatch(_stringBuf.ToArray());
            ResetToGround();
            return;
        }

        if (b == ESC)
        {
            _stringSawEsc = true;
            return;
        }

        _stringBuf.Add(b);
    }

    // ---- DCS ----
    private void ProcessDcsEntry(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateDigit(b);
            _state = State.DcsParam;
            return;
        }
        if (b is (byte)';' or (byte)':')
        {
            OnSeparator(b);
            _state = State.DcsParam;
            return;
        }
        if (b is >= 0x3C and <= 0x3F)
        {
            _privateMarker = (char)b;
            _state = State.DcsParam;
            return;
        }
        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            _state = State.DcsIntermediate;
            return;
        }
        if (b is >= 0x40 and <= 0x7E)
        {
            EnterDcsPassthrough(b);
            return;
        }
        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
        }
    }

    private void ProcessDcsParam(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateDigit(b);
            return;
        }
        if (b is (byte)';' or (byte)':')
        {
            OnSeparator(b);
            return;
        }
        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            _state = State.DcsIntermediate;
            return;
        }
        if (b is >= 0x40 and <= 0x7E)
        {
            EnterDcsPassthrough(b);
            return;
        }
        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
        }
    }

    private void ProcessDcsIntermediate(byte b)
    {
        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            return;
        }
        if (b is >= 0x40 and <= 0x7E)
        {
            EnterDcsPassthrough(b);
            return;
        }
        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
        }
    }

    private void EnterDcsPassthrough(byte final)
    {
        PushTrailingParam();
        _dcsFinal = final;
        _handler.DcsHook(final, IntermediatesSpan(), new CsiParams(_paramValues, _paramSub, _paramCount), _privateMarker);
        _stringBuf.Clear();
        _stringSawEsc = false;
        _state = State.DcsPassthrough;
    }

    private void ProcessDcsPassthrough(byte b)
    {
        if (_stringSawEsc)
        {
            _stringSawEsc = false;
            if (b == Backslash)
            {
                _handler.DcsUnhook();
                ResetToGround();
                return;
            }
            // ESC begins a new sequence; end DCS, reprocess.
            _handler.DcsUnhook();
            ResetToGround();
            _state = State.Escape;
            ProcessEscape(b);
            return;
        }

        if (b == ESC)
        {
            _stringSawEsc = true;
            return;
        }

        _handler.DcsPut(b);
    }

    // ---- SOS / PM / APC ----
    private void ProcessSosPmApc(byte b)
    {
        if (_stringSawEsc)
        {
            _stringSawEsc = false;
            if (b == Backslash)
            {
                _handler.SosPmApcDispatch(_dcsFinal, _stringBuf.ToArray());
                ResetToGround();
                return;
            }
            _handler.SosPmApcDispatch(_dcsFinal, _stringBuf.ToArray());
            ResetToGround();
            _state = State.Escape;
            ProcessEscape(b);
            return;
        }

        if (b == BEL)
        {
            _handler.SosPmApcDispatch(_dcsFinal, _stringBuf.ToArray());
            ResetToGround();
            return;
        }

        if (b == ESC)
        {
            _stringSawEsc = true;
            return;
        }

        _stringBuf.Add(b);
    }

    // ---- Accumulation helpers ----
    private static bool IsDigit(byte b) => b >= 0x30 && b <= 0x39;

    private void AccumulateDigit(byte b)
    {
        _accumulator = _accumulator * 10 + (b - 0x30);
        _hasParam = true;
        _trailingSep = false;
    }

    private void OnSeparator(byte sep)
    {
        PushCurrentParam();
        _currentIsSub = sep == ':';
        _trailingSep = true;
    }

    private void PushCurrentParam()
    {
        EnsureParamCapacity();
        _paramValues[_paramCount] = _hasParam ? _accumulator : 0;
        _paramSub[_paramCount] = _currentIsSub;
        _paramCount++;
        _accumulator = 0;
        _hasParam = false;
    }

    private void PushTrailingParam()
    {
        if (_hasParam || _trailingSep)
            PushCurrentParam();
    }

    private void EnsureParamCapacity()
    {
        if (_paramCount >= _paramValues.Length)
        {
            int newSize = _paramValues.Length * 2;
            Array.Resize(ref _paramValues, newSize);
            Array.Resize(ref _paramSub, newSize);
        }
    }

    private void PushIntermediate(byte b)
    {
        if (_interCount >= _intermediates.Length)
            Array.Resize(ref _intermediates, _intermediates.Length * 2);
        _intermediates[_interCount++] = b;
    }

    private ReadOnlySpan<byte> IntermediatesSpan() =>
        _interCount > 0 ? _intermediates.AsSpan(0, _interCount) : ReadOnlySpan<byte>.Empty;

    private void BeginCsi()
    {
        _paramCount = 0;
        _accumulator = 0;
        _hasParam = false;
        _currentIsSub = false;
        _trailingSep = false;
        _interCount = 0;
        _privateMarker = '\0';
    }

    private void BeginString()
    {
        _stringBuf.Clear();
        _stringSawEsc = false;
    }

    private void ResetToGround()
    {
        _state = State.Ground;
        _paramCount = 0;
        _accumulator = 0;
        _hasParam = false;
        _currentIsSub = false;
        _trailingSep = false;
        _interCount = 0;
        _privateMarker = '\0';
        _stringBuf.Clear();
        _stringSawEsc = false;
    }

    // ---- UTF-8 decoding ----
    private void DecodeUtf8()
    {
        if (_utf8.Count == 0)
            return;

        int totalConsumed = 0;
        ReadOnlySpan<byte> buffer = CollectionsMarshal.AsSpan(_utf8);

        while (!buffer.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf8(buffer, out Rune rune, out int consumed);
            if (status == OperationStatus.NeedMoreData)
                break;

            if (consumed <= 0)
            {
                consumed = 1;
                rune = new Rune(0xFFFD);
            }

            // Rune.DecodeFromUtf8 already substitutes U+FFFD for InvalidData.
            _handler.Print(rune);
            buffer = buffer[consumed..];
            totalConsumed += consumed;
        }

        if (totalConsumed > 0)
            _utf8.RemoveRange(0, totalConsumed);
    }

    private void FlushUtf8()
    {
        if (_utf8.Count == 0)
            return;

        ReadOnlySpan<byte> buffer = CollectionsMarshal.AsSpan(_utf8);
        while (!buffer.IsEmpty)
        {
            // Discard the status: Rune.DecodeFromUtf8 substitutes U+FFFD for InvalidData and
            // returns Done otherwise, so the rune is always safe to print.
            _ = Rune.DecodeFromUtf8(buffer, out Rune rune, out int consumed);
            if (consumed <= 0)
            {
                consumed = 1;
                rune = new Rune(0xFFFD);
            }

            _handler.Print(rune);
            buffer = buffer[consumed..];
        }

        _utf8.Clear();
    }

    /// <summary>Resets the parser to <see cref="State.Ground"/> and discards any in-flight sequence.</summary>
    public void Reset()
    {
        FlushUtf8();
        ResetToGround();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Reset();
        _disposed = true;
    }
}
