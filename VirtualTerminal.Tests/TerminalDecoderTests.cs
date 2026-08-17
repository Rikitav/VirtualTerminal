using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Model;
using VirtualTerminal.Vt;

namespace VirtualTerminal.Tests;

public class TerminalDecoderTests
{
    private static TerminalDecoder CreateDecoder(int columns = 80, int rows = 24)
    {
        TerminalScreenBuffer buffer = new(columns, rows);
        return new TerminalDecoder(buffer);
    }

    [Fact]
    public void Print_PlacesNormalCharacterAtCursor()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write("A"u8);

        Assert.Equal('A', decoder.Buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(1, decoder.State.CursorX);
        Assert.False(decoder.Buffer.GetCell(0, 0).Style.Wide);
    }

    [Fact]
    public void Print_WideCharacter_MarksFirstCellWideAndSecondContinuation()
    {
        TerminalDecoder decoder = CreateDecoder();

        // U+4E00 (CJK ideograph) is wide.
        decoder.Write(Encoding.UTF8.GetBytes("\u4e00"));

        Assert.True(decoder.Buffer.GetCell(0, 0).Style.Wide);
        Assert.True(decoder.Buffer.GetCell(0, 1).Style.Continuation);
        Assert.Equal(2, decoder.State.CursorX);
    }

    [Fact]
    public void Csi_Cup_MovesCursor()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[5;10H"));

        Assert.Equal(9, decoder.State.CursorX);
        Assert.Equal(4, decoder.State.CursorY);
    }

    [Fact]
    public void Csi_EraseDisplay_ClearsScreen()
    {
        TerminalDecoder decoder = CreateDecoder();
        decoder.Write(Encoding.UTF8.GetBytes("hello\x1b[2J"));

        Span<TerminalCellInfo> row = decoder.Buffer.GetRow(0);
        foreach (ref readonly TerminalCellInfo cell in row)
            Assert.Equal(' ', cell.Character.Value);
    }

    [Fact]
    public void Sgr_Reset_ClearsAttributes()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[1;31mA\x1b[0mB"));

        Assert.True(decoder.Buffer.GetCell(0, 0).Style.Bold);
        Assert.False(decoder.Buffer.GetCell(0, 1).Style.Bold);
    }

    [Fact]
    public void Sgr_ForegroundTrueColor_AppliesColor()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[38;2;255;128;64mA"));

        var style = decoder.Buffer.GetCell(0, 0).Style;
        Assert.Equal(255, style.Foreground.R);
        Assert.Equal(128, style.Foreground.G);
        Assert.Equal(64, style.Foreground.B);
    }

    [Fact]
    public void Decrqss_Sgr_RespondsWithCurrentAttributes()
    {
        TerminalDecoder decoder = CreateDecoder();
        string? response = null;
        decoder.SendOutput = bytes => response = Encoding.UTF8.GetString(bytes);

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[1;31m\x1b[?1$p"));

        Assert.NotNull(response);
        Assert.StartsWith("\x1bP1$r1;31", response);
        Assert.EndsWith("\x1b\\", response);
    }

    [Fact]
    public void Decrqss_CursorStyle_RespondsWithDecscusrSequence()
    {
        TerminalDecoder decoder = CreateDecoder();
        decoder.State.Modes.CursorShape = CursorShape.Underline;
        decoder.State.Modes.CursorBlinking = false;

        string? response = null;
        decoder.SendOutput = bytes => response = Encoding.UTF8.GetString(bytes);

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[?12$p"));

        Assert.Equal("\x1bP1$r4 q\x1b\\", response);
    }

    [Fact]
    public void Decrqss_Unknown_RespondsWithInvalid()
    {
        TerminalDecoder decoder = CreateDecoder();
        string? response = null;
        decoder.SendOutput = bytes => response = Encoding.UTF8.GetString(bytes);

        decoder.Write(Encoding.UTF8.GetBytes("\x1b[?999$p"));

        Assert.Equal("\x1bP0$r\x1b\\", response);
    }

    [Fact]
    public void Osc9_RaisesNotificationReceived()
    {
        TerminalDecoder decoder = CreateDecoder();
        string? received = null;
        decoder.NotificationReceived += (_, message) => received = message;

        decoder.Write(Encoding.UTF8.GetBytes("\x1b]9;hello world\x07"));

        Assert.Equal("hello world", received);
    }

    [Fact]
    public void Osc52_WhenAllowed_RaisesClipboardRequested()
    {
        TerminalDecoder decoder = CreateDecoder();
        decoder.Options.AllowClipboard = true;
        ClipboardRequestEventArgs? received = null;
        decoder.ClipboardRequested += (_, e) => received = e;

        decoder.Write(Encoding.UTF8.GetBytes("\x1b]52;c;aGVsbG8=\x07"));

        Assert.NotNull(received);
        Assert.Equal("c", received.Selection);
        Assert.Equal("aGVsbG8=", received.Data);
    }

    [Fact]
    public void Osc52_WhenDisallowed_DoesNotRaise()
    {
        TerminalDecoder decoder = CreateDecoder();
        decoder.Options.AllowClipboard = false;
        ClipboardRequestEventArgs? received = null;
        decoder.ClipboardRequested += (_, e) => received = e;

        decoder.Write(Encoding.UTF8.GetBytes("\x1b]52;c;aGVsbG8=\x07"));

        Assert.Null(received);
    }

    [Fact]
    public void Osc133_RaisesShellIntegrationMark()
    {
        TerminalDecoder decoder = CreateDecoder();
        string? received = null;
        decoder.ShellIntegrationMark += (_, args) => received = args;

        decoder.Write(Encoding.UTF8.GetBytes("\x1b]133;A\x07"));

        Assert.Equal("A", received);
    }

    [Fact]
    public void Osc1337_RaisesIterm2Annotation()
    {
        TerminalDecoder decoder = CreateDecoder();
        string? received = null;
        decoder.Iterm2Annotation += (_, args) => received = args;

        decoder.Write(Encoding.UTF8.GetBytes("\x1b]1337;SetMark\x07"));

        Assert.Equal("SetMark", received);
    }

    [Fact]
    public void CharsetDesignation_IsConsumedWithoutError()
    {
        TerminalDecoder decoder = CreateDecoder();

        decoder.Write(Encoding.UTF8.GetBytes("\x1b(Bhello"));

        Assert.Equal('h', decoder.Buffer.GetCell(0, 0).Character.Value);
    }
}
