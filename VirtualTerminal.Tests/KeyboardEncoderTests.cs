using VirtualTerminal.Buffer;
using VirtualTerminal.Input;

namespace VirtualTerminal.Tests;

public class KeyboardEncoderTests
{
    [Fact]
    public void Arrow_Up_ApplicationCursorOff_SendsEscBracketA()
    {
        TerminalModes modes = new();

        string? seq = KeyboardEncoder.Encode(TerminalKey.Up, TerminalModifier.None, modes);

        Assert.Equal("\x1b[A", seq);
    }

    [Fact]
    public void Arrow_Up_ApplicationCursorOn_SendsEscOA()
    {
        TerminalModes modes = new() { ApplicationCursorKeys = true };

        string? seq = KeyboardEncoder.Encode(TerminalKey.Up, TerminalModifier.None, modes);

        Assert.Equal("\x1bOA", seq);
    }

    [Fact]
    public void Arrow_WithShift_SendsModifiedSequence()
    {
        TerminalModes modes = new();

        string? seq = KeyboardEncoder.Encode(TerminalKey.Up, TerminalModifier.Shift, modes);

        Assert.Equal("\x1b[1;2A", seq);
    }

    [Fact]
    public void BracketedPaste_WrapsText()
    {
        string wrapped = KeyboardEncoder.WrapBracketedPaste("hello", bracketedPaste: true);

        Assert.Equal("\x1b[200~hello\x1b[201~", wrapped);
    }

    [Fact]
    public void BracketedPaste_Disabled_ReturnsPlainText()
    {
        string wrapped = KeyboardEncoder.WrapBracketedPaste("hello", bracketedPaste: false);

        Assert.Equal("hello", wrapped);
    }
}
