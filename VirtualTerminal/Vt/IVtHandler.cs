using System;
using System.Text;

namespace VirtualTerminal.Vt;

/// <summary>
/// Sink for parsed VT/ANSI events. Implementations own the terminal state (cursor, modes,
/// attributes) and mutate the screen buffer. The decoder calls these synchronously during
/// <see cref="VtDecoder.Write"/>, so parameters are only valid for the duration of the call.
/// Reference: Paul Williams' VT500 state machine (vt100.net), xterm ctlseqs, ECMA-48.
/// </summary>
public interface IVtHandler
{
    /// <summary>A printable codepoint was received.</summary>
    void Print(Rune rune);

    /// <summary>A C0 control character (0x00–0x1F) was received in an executable context.</summary>
    void Execute(byte control);

    /// <summary>An <c>ESC</c> sequence completed (e.g. <c>ESC 7</c>, <c>ESC D</c>, <c>ESC ( B</c>).</summary>
    void EscDispatch(byte final, ReadOnlySpan<byte> intermediates);

    /// <summary>A <c>CSI</c> sequence completed.</summary>
    /// <param name="final">Final byte (0x40–0x7E).</param>
    /// <param name="intermediates">Intermediate bytes (0x20–0x2F), if any.</param>
    /// <param name="parameters">Sub-parameter-aware parameter list.</param>
    /// <param name="privateMarker">The DEC private marker ('?', '&gt;', '=', '&lt;', '!') if present, else <c>'\0'</c>.</param>
    void CsiDispatch(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker);

    /// <summary>An <c>OSC</c> string completed (BEL or ST terminated). Body is the raw bytes after the OSC introducer.</summary>
    void OscDispatch(ReadOnlySpan<byte> data);

    /// <summary>A <c>DCS</c> sequence began. Phase 1 collects the payload; streaming is a Phase 3 concern.</summary>
    void DcsHook(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker);

    /// <summary>A payload byte for the current <c>DCS</c> sequence.</summary>
    void DcsPut(byte b);

    /// <summary>The current <c>DCS</c> sequence ended (ST).</summary>
    void DcsUnhook();

    /// <summary>An <c>SOS</c>/<c>PM</c>/<c>APC</c> string completed. <paramref name="kind"/> is the introducer byte ('X','^','_').</summary>
    void SosPmApcDispatch(byte kind, ReadOnlySpan<byte> data);
}
