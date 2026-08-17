using System.Text;
using VirtualTerminal.Vt;

namespace VirtualTerminal.Tests;

public class VtDecoderParserTests
{
    private static VtDecoder CreateDecoder(out CollectingHandler handler)
    {
        handler = new CollectingHandler();
        return new VtDecoder(handler);
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Ground_PrintableAscii_DispatchesPrint()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        decoder.Write("ABC"u8);

        Assert.Equal(["A", "B", "C"], handler.Prints.Select(r => r.ToString()));
    }

    [Fact]
    public void Ground_C0Controls_DispatchesExecute()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        decoder.Write([0x07, 0x08, 0x09, 0x0A, 0x0D]);

        Assert.Equal([0x07, 0x08, 0x09, 0x0A, 0x0D], handler.Executes);
    }

    [Fact]
    public void Ground_Utf8MultiByte_SplitAcrossWrites_DispatchesSingleRune()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // U+4E00 is three bytes in UTF-8: E4 B8 80
        decoder.Write([0xE4]);
        decoder.Write([0xB8]);
        decoder.Write([0x80]);

        Assert.Single(handler.Prints);
        Assert.Equal("\u4e00", handler.Prints[0].ToString());
    }

    [Fact]
    public void Ground_InvalidUtf8_PrintsReplacementRune()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // Invalid leading byte followed by ASCII should produce U+FFFD then 'A'.
        decoder.Write([0xFF, 0x41]);

        Assert.Equal(2, handler.Prints.Count);
        Assert.Equal(0xFFFD, handler.Prints[0].Value);
        Assert.Equal('A', handler.Prints[1].Value);
    }

    [Fact]
    public void Esc_SimpleDispatch_CallsEscDispatch()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        decoder.Write(Bytes("\x1b\x37"));

        Assert.Single(handler.Escapes);
        Assert.Equal('7', (char)handler.Escapes[0].Final);
        Assert.Empty(handler.Escapes[0].Intermediates);
    }

    [Fact]
    public void Esc_WithIntermediate_CapturesIntermediateBytes()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // DECALN: ESC # 8
        decoder.Write(Bytes("\x1b#8"));

        Assert.Single(handler.Escapes);
        Assert.Equal('8', (char)handler.Escapes[0].Final);
        Assert.Equal("#", Encoding.ASCII.GetString(handler.Escapes[0].Intermediates));
    }

    [Fact]
    public void Csi_NoParameters_UsesDefaults()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // CUU with no parameter should default to 1.
        decoder.Write(Bytes("\x1b[A"));

        Assert.Single(handler.Csis);
        (byte final, byte[] intermediates, ParsedCsi parameters, char privateMarker) = handler.Csis[0];
        Assert.Equal('A', (char)final);
        Assert.True(parameters.IsEmpty);
        Assert.Equal('\0', privateMarker);
        Assert.Empty(intermediates);
    }

    [Fact]
    public void Csi_ExplicitParameters_ParsesValues()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // CUP: CSI 5 ; 10 H
        decoder.Write(Bytes("\x1b[5;10H"));

        Assert.Single(handler.Csis);
        ParsedCsi p = handler.Csis[0].Parameters;
        Assert.Equal(2, p.Length);
        Assert.Equal(5, p.Values[0]);
        Assert.Equal(10, p.Values[1]);
    }

    [Fact]
    public void Csi_PrivateMarker_Captured()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // DECSET: CSI ? 25 h
        decoder.Write(Bytes("\x1b[?25h"));

        Assert.Single(handler.Csis);
        Assert.Equal('?', handler.Csis[0].PrivateMarker);
        Assert.Equal(25, handler.Csis[0].Parameters.Values[0]);
    }

    [Fact]
    public void Csi_SubParameters_MarkedCorrectly()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // SGR curly underline: CSI 4 : 3 m
        decoder.Write(Bytes("\x1b[4:3m"));

        Assert.Single(handler.Csis);
        ParsedCsi p = handler.Csis[0].Parameters;
        Assert.Equal(2, p.Length);
        Assert.Equal(4, p.Values[0]);
        Assert.False(p.SubParams[0]);
        Assert.Equal(3, p.Values[1]);
        Assert.True(p.SubParams[1]);
    }

    [Fact]
    public void Csi_TrailingSeparator_EmitsDefaultZero()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // CSI ; ; m -> three empty parameters, each defaulting to 0.
        decoder.Write(Bytes("\x1b[;;m"));

        Assert.Single(handler.Csis);
        ParsedCsi p = handler.Csis[0].Parameters;
        Assert.Equal(3, p.Length);
        Assert.All(p.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Csi_LongParameterList_ExpandsCapacity()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // SGR with more than the initial 8 parameters.
        string seq = "\x1b[" + string.Join(";", Enumerable.Range(1, 20)) + "m";
        decoder.Write(Bytes(seq));

        Assert.Single(handler.Csis);
        ParsedCsi p = handler.Csis[0].Parameters;
        Assert.Equal(20, p.Length);
        Assert.Equal(20, p.Values[^1]);
    }

    [Fact]
    public void Osc_Title_BelTerminated()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        decoder.Write(Bytes("\x1b]0;hello\x07"));

        Assert.Single(handler.Oscs);
        Assert.Equal("0;hello", Encoding.UTF8.GetString(handler.Oscs[0]));
    }

    [Fact]
    public void Osc_Title_StTerminated()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        decoder.Write(Bytes("\x1b]2;title\x1b\\"));

        Assert.Single(handler.Oscs);
        Assert.Equal("2;title", Encoding.UTF8.GetString(handler.Oscs[0]));
    }

    [Fact]
    public void Dcs_Payload_DispatchesHookPutUnhook()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // DCS + q / query (XTGETTCAP) with ST termination.
        decoder.Write(Bytes("\x1bP+q5463\x1b\\"));

        Assert.Single(handler.DcsHooks);
        Assert.Equal('q', (char)handler.DcsHooks[0].Final);
        Assert.Equal("+", Encoding.ASCII.GetString(handler.DcsHooks[0].Intermediates));
        Assert.Single(handler.DcsUnhooks);
        Assert.Equal("5463", Encoding.ASCII.GetString(handler.DcsPayload.ToArray()));
    }

    [Fact]
    public void Apc_Payload_Dispatched()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // APC string terminated by ST.
        decoder.Write(Bytes("\x1b_data\x1b\\"));

        Assert.Single(handler.SosPmApcs);
        Assert.Equal('_', (char)handler.SosPmApcs[0].Kind);
        Assert.Equal("data", Encoding.UTF8.GetString(handler.SosPmApcs[0].Data));
    }

    [Fact]
    public void NestedEscape_CancelsPartialSequence()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        // Start CSI, then nested ESC, then a simple escape sequence.
        decoder.Write(Bytes("\x1b[\x1b\x37"));

        // Only DECSC should be dispatched.
        Assert.Single(handler.Escapes);
        Assert.Equal('7', (char)handler.Escapes[0].Final);
        Assert.Empty(handler.Csis);
    }

    [Fact]
    public void Reset_AbandonsPartialSequence()
    {
        using VtDecoder decoder = CreateDecoder(out CollectingHandler handler);

        decoder.Write(Bytes("\x1b[1;2"));
        decoder.Reset();
        decoder.Write(Bytes("A"));

        Assert.Empty(handler.Csis);
        Assert.Single(handler.Prints);
        Assert.Equal('A', handler.Prints[0].Value);
    }

    private sealed class CollectingHandler : IVtHandler
    {
        public List<Rune> Prints { get; } = [];
        public List<byte> Executes { get; } = [];
        public List<(byte Final, byte[] Intermediates)> Escapes { get; } = [];
        public List<(byte Final, byte[] Intermediates, ParsedCsi Parameters, char PrivateMarker)> Csis { get; } = [];
        public List<byte[]> Oscs { get; } = [];
        public List<(byte Final, byte[] Intermediates, ParsedCsi Parameters, char PrivateMarker)> DcsHooks { get; } = [];
        public List<byte> DcsPayload { get; } = [];
        public List<bool> DcsUnhooks { get; } = [];
        public List<(byte Kind, byte[] Data)> SosPmApcs { get; } = [];

        public void Print(Rune rune) => Prints.Add(rune);

        public void Execute(byte control) => Executes.Add(control);

        public void EscDispatch(byte final, ReadOnlySpan<byte> intermediates)
            => Escapes.Add((final, intermediates.ToArray()));

        public void CsiDispatch(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker)
            => Csis.Add((final, intermediates.ToArray(), new ParsedCsi(parameters), privateMarker));

        public void OscDispatch(ReadOnlySpan<byte> data) => Oscs.Add(data.ToArray());

        public void DcsHook(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker)
            => DcsHooks.Add((final, intermediates.ToArray(), new ParsedCsi(parameters), privateMarker));

        public void DcsPut(byte b) => DcsPayload.Add(b);

        public void DcsUnhook() => DcsUnhooks.Add(true);

        public void SosPmApcDispatch(byte kind, ReadOnlySpan<byte> data) => SosPmApcs.Add((kind, data.ToArray()));
    }

    private sealed class ParsedCsi
    {
        public int[] Values { get; }
        public bool[] SubParams { get; }
        public int Length { get; }
        public bool IsEmpty { get; }

        public ParsedCsi(CsiParams parameters)
        {
            Length = parameters.Length;
            IsEmpty = parameters.IsEmpty;
            Values = new int[Length];
            SubParams = new bool[Length];

            foreach ((int index, int value, bool isSubParam) in parameters)
            {
                Values[index] = value;
                SubParams[index] = isSubParam;
            }
        }
    }
}
