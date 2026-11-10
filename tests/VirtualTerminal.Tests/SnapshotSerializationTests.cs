using VirtualTerminal.Buffer;
using VirtualTerminal.Input;
using VirtualTerminal.Model;
using VirtualTerminal.Serialization;
using VirtualTerminal.Session;

namespace VirtualTerminal.Tests;

public class SnapshotSerializationTests
{
    private sealed class TestSession : TerminalSession
    {
        public TestSession() : base() { }
        public new TerminalDecoder Decoder => (TerminalDecoder)base.Decoder;
        protected override void Dispose(bool disposing) { }
    }

    [Fact]
    public void BinarySerialization_RoundTrip_PreservesAllStateAndHistory()
    {
        using TestSession original = new();

        // Write colored and styled text
        original.Write("\x1b[1;31mHello \x1b[4;32mWorld\x1b[0m\r\n"u8);
        original.Write("Second Line Here"u8);

        // Set cursor, margins, and modes
        original.Decoder.State.CursorX = 15;
        original.Decoder.State.CursorY = 5;
        original.Decoder.State.Modes.AutoWrap = false;
        original.Decoder.State.Modes.OriginMode = true;
        original.Decoder.State.Modes.KittyKeyboardFlags = KittyKeyboardFlags.DisambiguateEscapeCodes | KittyKeyboardFlags.ReportEventTypes;
        original.Buffer.SetScrollRegion(2, 10);
        original.Buffer.SetHorizontalScrollRegion(5, 40);

        // Push a scrollback row
        TerminalRow sbRow = new(original.Buffer.Columns) { Wrapped = true };
        sbRow[0] = new TerminalCellInfo(new System.Text.Rune('H'), CellRenderStyle.Default);
        sbRow[1] = new TerminalCellInfo(new System.Text.Rune('i'), CellRenderStyle.Default);
        original.Buffer.PushScrollback(sbRow);

        // Serialize to binary
        byte[] snapshot = original.CreateSnapshot();
        Assert.NotEmpty(snapshot);

        // Restore into new session
        using TestSession restored = new();
        restored.RestoreSnapshot(snapshot);

        // Verify buffer geometry and margins
        Assert.Equal(original.Buffer.Columns, restored.Buffer.Columns);
        Assert.Equal(original.Buffer.Rows, restored.Buffer.Rows);
        Assert.Equal(original.Buffer.ScrollTop, restored.Buffer.ScrollTop);
        Assert.Equal(original.Buffer.ScrollBottom, restored.Buffer.ScrollBottom);
        Assert.Equal(original.Buffer.ScrollLeft, restored.Buffer.ScrollLeft);
        Assert.Equal(original.Buffer.ScrollRight, restored.Buffer.ScrollRight);

        // Verify cell characters and styles
        Assert.Equal('H', restored.Buffer.GetCell(0, 0).Character.Value);
        Assert.True(restored.Buffer.GetCell(0, 0).Style.Bold);
        Assert.Equal('W', restored.Buffer.GetCell(0, 6).Character.Value);
        Assert.Equal('S', restored.Buffer.GetCell(1, 0).Character.Value);

        // Verify scrollback
        var restoredSb = restored.Buffer.GetScrollback();
        Assert.Single(restoredSb);
        Assert.True(restoredSb[0].Wrapped);
        Assert.Equal('H', restoredSb[0][0].Character.Value);
        Assert.Equal('i', restoredSb[0][1].Character.Value);

        // Verify state and modes
        Assert.Equal(15, restored.Decoder.State.CursorX);
        Assert.Equal(5, restored.Decoder.State.CursorY);
        Assert.False(restored.Decoder.State.Modes.AutoWrap);
        Assert.True(restored.Decoder.State.Modes.OriginMode);
        Assert.Equal(KittyKeyboardFlags.DisambiguateEscapeCodes | KittyKeyboardFlags.ReportEventTypes, restored.Decoder.State.Modes.KittyKeyboardFlags);
    }

    [Fact]
    public void JsonSerialization_RoundTrip_PreservesAllStateAndHistory()
    {
        using TestSession original = new();

        original.Write("\x1b[33mYellow Text\x1b[0m\r\n"u8);
        original.Decoder.State.CursorX = 10;
        original.Decoder.State.CursorY = 3;
        original.Decoder.State.Modes.KittyKeyboardFlags = KittyKeyboardFlags.ReportAllKeysAsEscapeCodes;

        string json = original.CreateSnapshotJson(indented: true);
        Assert.Contains("\"KittyKeyboardFlags\": 8", json);

        using TestSession restored = new();
        restored.RestoreSnapshotJson(json);

        Assert.Equal('Y', restored.Buffer.GetCell(0, 0).Character.Value);
        Assert.Equal('e', restored.Buffer.GetCell(0, 1).Character.Value);
        Assert.Equal(10, restored.Decoder.State.CursorX);
        Assert.Equal(3, restored.Decoder.State.CursorY);
        Assert.Equal(KittyKeyboardFlags.ReportAllKeysAsEscapeCodes, restored.Decoder.State.Modes.KittyKeyboardFlags);
    }

    [Fact]
    public void Snapshot_WithAlternateScreen_PreservesBothScreens()
    {
        using TestSession original = new();

        // Primary screen text
        original.Write("Primary Screen Text"u8);

        // Enter alternate screen (?1049h) and home cursor
        original.Write("\x1b[?1049h\x1b[H"u8);
        Assert.True(original.Buffer.IsAlternate);

        // Alternate screen text
        original.Write("Alternate Screen Text"u8);

        byte[] snapshot = original.CreateSnapshot();

        using TestSession restored = new();
        restored.RestoreSnapshot(snapshot);

        // Restored should be in alternate screen
        Assert.True(restored.Buffer.IsAlternate);
        Assert.Equal('A', restored.Buffer.GetCell(0, 0).Character.Value);

        // Leave alternate screen (?1049l)
        restored.Write("\x1b[?1049l"u8);
        Assert.False(restored.Buffer.IsAlternate);
        Assert.Equal('P', restored.Buffer.GetCell(0, 0).Character.Value);
    }
}
