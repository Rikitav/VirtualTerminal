using VirtualTerminal.Buffer;
using VirtualTerminal.Enums;
using VirtualTerminal.Serialization;
using VirtualTerminal.Tests.Harness;
using Xunit;

namespace VirtualTerminal.Tests;

public sealed class CursorTests
{
    [Theory]
    [InlineData(0, CursorShape.Block, true)]
    [InlineData(1, CursorShape.Block, true)]
    [InlineData(2, CursorShape.Block, false)]
    [InlineData(3, CursorShape.Underline, true)]
    [InlineData(4, CursorShape.Underline, false)]
    [InlineData(5, CursorShape.Bar, true)]
    [InlineData(6, CursorShape.Bar, false)]
    public void Decscusr_SetsCursorShapeAndBlink(int param, CursorShape expectedShape, bool expectedBlink)
    {
        var t = new EscTestDriver();
        t.Write($"\x1b[{param} q");

        Assert.Equal(expectedShape, t.Decoder.State.Modes.CursorShape);
        Assert.Equal(expectedBlink, t.Decoder.State.Modes.CursorBlink);
    }

    [Fact]
    public void Decrqss_QueriesCursorStyle()
    {
        var t = new EscTestDriver();

        // Set steady underline (4)
        t.Write("\x1b[4 q");
        t.Write("\x1bP$q q\x1b\\");
        t.AssertResponse("\x1bP1$r4 q\x1b\\");

        // Set blinking bar (5)
        t.Write("\x1b[5 q");
        t.Write("\x1bP$q q\x1b\\");
        t.AssertResponse("\x1bP1$r5 q\x1b\\");
    }

    [Fact]
    public void Snapshot_PreservesCursorShape_JsonAndBinary()
    {
        var buffer = new TerminalScreenBuffer(80, 24);
        var state = new TerminalState();
        state.Modes.CursorShape = CursorShape.Bar;
        state.Modes.CursorBlink = false;

        // JSON
        string json = TerminalSnapshotSerializer.SerializeJson(buffer, state);
        var (bJson, sJson) = TerminalSnapshotSerializer.DeserializeJson(json);
        Assert.Equal(CursorShape.Bar, sJson.Modes.CursorShape);
        Assert.False(sJson.Modes.CursorBlink);

        // Binary
        byte[] bin = TerminalSnapshotSerializer.SerializeBinary(buffer, state);
        var (bBin, sBin) = TerminalSnapshotSerializer.DeserializeBinary(bin);
        Assert.Equal(CursorShape.Bar, sBin.Modes.CursorShape);
        Assert.False(sBin.Modes.CursorBlink);
    }

    [Fact]
    public void Modes_Clone_PreservesCursorShape()
    {
        var modes = new TerminalModes
        {
            CursorShape = CursorShape.Underline,
            CursorBlink = false,
        };

        var clone = modes.Clone();
        Assert.Equal(CursorShape.Underline, clone.CursorShape);
        Assert.False(clone.CursorBlink);
    }
}
