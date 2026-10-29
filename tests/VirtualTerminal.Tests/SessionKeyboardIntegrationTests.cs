using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Input;
using VirtualTerminal.Interfaces;
using VirtualTerminal.Session;

namespace VirtualTerminal.Tests;

public class SessionKeyboardIntegrationTests
{
    private sealed class TestableSession : TerminalSession
    {
        protected override void Dispose(bool disposing)
        {
        }
    }

    private static string? Encode(TerminalKey key, TerminalModifier modifiers, TerminalModes? modes = null)
        => KeyboardEncoder.Encode(key, modifiers, modes ?? new TerminalModes());

    private static TerminalState StateOf(ITerminalSession session)
        => ((TerminalDecoder)session.Decoder).State;

    // ---- Base session behavior ----

    [Fact]
    public void Write_RaisesBufferUpdated()
    {
        using DummyTerminalSession session = new();
        bool raised = false;
        session.BufferUpdated += (_, _) => raised = true;

        session.Write("hi"u8);

        Assert.True(raised);
    }

    [Fact]
    public void DeviceStatusReport_RaisesInputAvailable()
    {
        using DummyTerminalSession session = new();
        bool raised = false;
        session.InputAvailable += (_, _) => raised = true;

        session.Write("\x1b[5n"u8);

        Assert.True(raised);
        Assert.True(session.AvailableDataLength > 0);
    }

    [Fact]
    public void Read_PullsQueuedReportBytes()
    {
        using DummyTerminalSession session = new();
        session.Write("\x1b[5n"u8);

        byte[] buffer = new byte[64];
        int read = session.Read(buffer);

        Assert.True(read > 0);
        string response = Encoding.UTF8.GetString(buffer, 0, read);
        Assert.Equal("\x1b[0n", response);
    }

    [Fact]
    public void ReadAll_ReturnsAllBytesAndClearsQueue()
    {
        using DummyTerminalSession session = new();
        session.Write("\x1b[5n"u8);

        byte[] all = session.ReadAll();

        Assert.True(all.Length > 0);
        Assert.Equal(0, session.AvailableDataLength);
    }

    [Fact]
    public void AvailableDataLength_ReflectsQueuedBytes()
    {
        using DummyTerminalSession session = new();
        Assert.Equal(0, session.AvailableDataLength);

        session.Write("\x1b[5n"u8);

        Assert.True(session.AvailableDataLength > 0);
    }

    [Fact]
    public void Resize_UpdatesBufferDimensions()
    {
        using DummyTerminalSession session = new();
        session.Resize(100, 30);

        Assert.Equal(100, session.Buffer.Columns);
        Assert.Equal(30, session.Buffer.Rows);
    }

    [Fact]
    public void OscTitle_UpdatesSessionTitle()
    {
        using TestableSession session = new();

        session.Write("\x1b]2;New Title\x07"u8);

        Assert.Equal("New Title", session.Title);
    }

    [Fact]
    public void Dispose_PreventsSubsequentAccess()
    {
        DummyTerminalSession session = new();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Buffer);
        Assert.Throws<ObjectDisposedException>(() => session.Decoder);
    }

    [Fact]
    public void Dispose_ClearsInputQueue()
    {
        DummyTerminalSession session = new();
        session.Write("\x1b[5n"u8);
        Assert.True(session.AvailableDataLength > 0);

        session.Dispose();

        Assert.Equal(0, session.AvailableDataLength);
    }

    // ---- Keyboard encoder ----

    [Fact]
    public void Arrow_Down_SendsEscBracketB()
    {
        string? seq = Encode(TerminalKey.Down, TerminalModifier.None);
        Assert.Equal("\x1b[B", seq);
    }

    [Fact]
    public void Arrow_Left_SendsEscBracketD()
    {
        string? seq = Encode(TerminalKey.Left, TerminalModifier.None);
        Assert.Equal("\x1b[D", seq);
    }

    [Fact]
    public void Arrow_Right_SendsEscBracketC()
    {
        string? seq = Encode(TerminalKey.Right, TerminalModifier.None);
        Assert.Equal("\x1b[C", seq);
    }

    [Fact]
    public void ApplicationCursor_ArrowUp_SendsSs3()
    {
        TerminalModes modes = new() { ApplicationCursorKeys = true };
        string? seq = Encode(TerminalKey.Up, TerminalModifier.None, modes);
        Assert.Equal("\x1bOA", seq);
    }

    [Fact]
    public void ShiftTab_SendsReverseTab()
    {
        string? seq = Encode(TerminalKey.Tab, TerminalModifier.Shift);
        Assert.Equal("\x1b[Z", seq);
    }

    [Fact]
    public void LineFeedNewLine_Enter_SendsCrLf()
    {
        TerminalModes modes = new() { LineFeedNewLine = true };
        string? seq = Encode(TerminalKey.Enter, TerminalModifier.None, modes);
        Assert.Equal("\r\n", seq);
    }

    [Fact]
    public void NoLineFeedNewLine_Enter_SendsCrOnly()
    {
        TerminalModes modes = new() { LineFeedNewLine = false };
        string? seq = Encode(TerminalKey.Enter, TerminalModifier.None, modes);
        Assert.Equal("\r", seq);
    }

    [Fact]
    public void BackspaceSendsControlH_Backspace_SendsControlH()
    {
        TerminalModes modes = new() { BackspaceSendsControlH = true };
        string? seq = Encode(TerminalKey.Back, TerminalModifier.None, modes);
        Assert.Equal("\b", seq);
    }

    [Fact]
    public void FunctionKey_F1_SendsSs3P()
    {
        string? seq = Encode(TerminalKey.F1, TerminalModifier.None);
        Assert.Equal("\x1bOP", seq);
    }

    [Fact]
    public void ModifiedF1_Control_SendsCsiWithModifier()
    {
        string? seq = Encode(TerminalKey.F1, TerminalModifier.Control);
        Assert.Equal("\x1b[1;5P", seq);
    }

    [Fact]
    public void Home_SendsTildeSequence()
    {
        string? seq = Encode(TerminalKey.Home, TerminalModifier.None);
        Assert.Equal("\x1b[1~", seq);
    }

    [Fact]
    public void ModifiedHome_Alt_SendsCsiTildeWithModifier()
    {
        string? seq = Encode(TerminalKey.Home, TerminalModifier.Alt);
        Assert.Equal("\x1b[1;3~", seq);
    }

    [Fact]
    public void Delete_SendsTildeSequence()
    {
        string? seq = Encode(TerminalKey.Delete, TerminalModifier.None);
        Assert.Equal("\x1b[3~", seq);
    }

    [Fact]
    public void PlainKey_ReturnsNull()
    {
        string? seq = Encode(TerminalKey.None, TerminalModifier.None);
        Assert.Null(seq);
    }

    [Fact]
    public void CombinedModifiers_ShiftControl_ProducesCorrectCode()
    {
        string? seq = Encode(TerminalKey.Up, TerminalModifier.Shift | TerminalModifier.Control);
        Assert.Equal("\x1b[1;6A", seq);
    }

    [Fact]
    public void BracketedPaste_Enabled_WrapsText()
    {
        string wrapped = KeyboardEncoder.WrapBracketedPaste("paste", bracketedPaste: true);
        Assert.Equal("\x1b[200~paste\x1b[201~", wrapped);
    }

    [Fact]
    public void BracketedPaste_Disabled_ReturnsPlainText()
    {
        string wrapped = KeyboardEncoder.WrapBracketedPaste("paste", bracketedPaste: false);
        Assert.Equal("paste", wrapped);
    }

    // ---- Keyboard -> decoder integration ----

    [Fact]
    public void ArrowRight_EncodedAndWritten_MovesCursorRight()
    {
        using DummyTerminalSession session = new();
        string? seq = Encode(TerminalKey.Right, TerminalModifier.None);

        session.Write(Encoding.UTF8.GetBytes(seq!));

        Assert.Equal(1, StateOf(session).CursorX);
        Assert.Equal(0, StateOf(session).CursorY);
    }

    [Fact]
    public void ArrowDown_EncodedAndWritten_MovesCursorDown()
    {
        using DummyTerminalSession session = new();
        string? seq = Encode(TerminalKey.Down, TerminalModifier.None);

        session.Write(Encoding.UTF8.GetBytes(seq!));

        Assert.Equal(0, StateOf(session).CursorX);
        Assert.Equal(1, StateOf(session).CursorY);
    }

    [Fact]
    public void Enter_EncodedAndWritten_MovesToNextLine()
    {
        using DummyTerminalSession session = new();
        string? seq = Encode(TerminalKey.Enter, TerminalModifier.None);

        session.Write(Encoding.UTF8.GetBytes(seq!));
        // A trailing newline is debounced until further bytes arrive; any subsequent output
        // applies it (here the space lands at the start of the next line).
        session.Write(" "u8);

        Assert.Equal(1, StateOf(session).CursorX);
        Assert.Equal(1, StateOf(session).CursorY);
    }

    [Fact]
    public void BackspaceControlH_EncodedAndWritten_MovesCursorLeft()
    {
        using DummyTerminalSession session = new();
        session.Write("AB"u8);
        Assert.Equal(2, StateOf(session).CursorX);

        TerminalModes modes = new() { BackspaceSendsControlH = true };
        string? seq = Encode(TerminalKey.Back, TerminalModifier.None, modes);
        Assert.Equal("\b", seq);

        session.Write(Encoding.UTF8.GetBytes(seq!));

        Assert.Equal(1, StateOf(session).CursorX);
    }

    [Fact]
    public void Tab_EncodedAndWritten_AdvancesCursor()
    {
        using DummyTerminalSession session = new();
        string? seq = Encode(TerminalKey.Tab, TerminalModifier.None);

        session.Write(Encoding.UTF8.GetBytes(seq!));

        Assert.True(StateOf(session).CursorX > 0);
    }

    // ---- Keyboard -> canonical session integration ----

    [Fact]
    public void CanonicalSession_TypedText_AppendsToBuffer()
    {
        using CanonicalTerminalSession session = new();
        session.Write("hi"u8);

        TerminalScreenBuffer buffer = session.Buffer;
        Assert.Equal('>', buffer.GetCell(0, 0).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 1).Character.Value);
        Assert.Equal('h', buffer.GetCell(0, 2).Character.Value);
        Assert.Equal('i', buffer.GetCell(0, 3).Character.Value);
    }

    [Fact]
    public void CanonicalSession_Backspace_RemovesCharacter()
    {
        using CanonicalTerminalSession session = new();
        session.Write("hi"u8);
        session.Write("\x7f"u8);

        TerminalScreenBuffer buffer = session.Buffer;
        Assert.Equal('h', buffer.GetCell(0, 2).Character.Value);
        Assert.Equal(' ', buffer.GetCell(0, 3).Character.Value);
    }

    [Fact]
    public void CanonicalSession_HomeKey_MovesCursorToStart()
    {
        using CanonicalTerminalSession session = new();
        session.Write("abc"u8);
        session.Write("\x1b[H"u8);
        session.Write("X"u8);

        TerminalScreenBuffer buffer = session.Buffer;
        Assert.Equal('X', buffer.GetCell(0, 2).Character.Value);
        Assert.Equal('a', buffer.GetCell(0, 3).Character.Value);
        Assert.Equal('b', buffer.GetCell(0, 4).Character.Value);
    }

    [Fact]
    public void CanonicalSession_EndKey_MovesCursorToEnd()
    {
        using CanonicalTerminalSession session = new();
        session.Write("ab"u8);
        session.Write("\x1b[F"u8);
        session.Write("X"u8);

        TerminalScreenBuffer buffer = session.Buffer;
        Assert.Equal('a', buffer.GetCell(0, 2).Character.Value);
        Assert.Equal('b', buffer.GetCell(0, 3).Character.Value);
        Assert.Equal('X', buffer.GetCell(0, 4).Character.Value);
    }

    [Fact]
    public async Task CanonicalSession_Enter_CompletesReadLine()
    {
        using CanonicalTerminalSession session = new();
        Task<string> readTask = session.ReadLineAsync();

        session.Write("hello"u8);
        session.Write("\r"u8);

        string line = await readTask;
        Assert.Equal("hello", line);
    }
}
