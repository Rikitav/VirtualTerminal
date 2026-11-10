using VirtualTerminal.Buffer;
using VirtualTerminal.Input;
using VirtualTerminal.Tests.Harness;

namespace VirtualTerminal.Tests;

public class KittyKeyboardTests
{
    [Fact]
    public void KittyKeyboard_Query_RespondsWithCurrentFlags()
    {
        EscTestDriver t = new(80, 24);

        t.Write("\x1b[?u");
        Assert.Equal("\x1b[?0u", t.ReadResponse());

        t.Decoder.State.Modes.KittyKeyboardFlags = KittyKeyboardFlags.DisambiguateEscapeCodes | KittyKeyboardFlags.ReportEventTypes;
        t.Write("\x1b[?u");
        Assert.Equal("\x1b[?3u", t.ReadResponse());
    }

    [Fact]
    public void KittyKeyboard_PushAndPop_ManagesFlagStack()
    {
        EscTestDriver t = new(80, 24);

        // Push flags 3 (1 | 2)
        t.Write("\x1b[>3u");
        Assert.Equal(KittyKeyboardFlags.DisambiguateEscapeCodes | KittyKeyboardFlags.ReportEventTypes, t.Decoder.State.Modes.KittyKeyboardFlags);

        // Push flags 8
        t.Write("\x1b[>8u");
        Assert.Equal(KittyKeyboardFlags.ReportAllKeysAsEscapeCodes, t.Decoder.State.Modes.KittyKeyboardFlags);

        // Pop 1 entry -> restores 3
        t.Write("\x1b[<1u");
        Assert.Equal(KittyKeyboardFlags.DisambiguateEscapeCodes | KittyKeyboardFlags.ReportEventTypes, t.Decoder.State.Modes.KittyKeyboardFlags);

        // Pop 1 entry -> restores initial 0
        t.Write("\x1b[<u");
        Assert.Equal(KittyKeyboardFlags.None, t.Decoder.State.Modes.KittyKeyboardFlags);
    }

    [Fact]
    public void KittyKeyboard_SetFlags_ModeOperations()
    {
        EscTestDriver t = new(80, 24);

        // Set mode 1: replace
        t.Write("\x1b[=5;1u");
        Assert.Equal((KittyKeyboardFlags)5, t.Decoder.State.Modes.KittyKeyboardFlags);

        // Set mode 2: union bits (| 2)
        t.Write("\x1b[=2;2u");
        Assert.Equal((KittyKeyboardFlags)7, t.Decoder.State.Modes.KittyKeyboardFlags);

        // Set mode 3: clear bits (& ~1)
        t.Write("\x1b[=1;3u");
        Assert.Equal((KittyKeyboardFlags)6, t.Decoder.State.Modes.KittyKeyboardFlags);
    }

    [Fact]
    public void KeyboardEncoder_LegacyMode_EncodesStandardSequences()
    {
        TerminalModes modes = new() { LineFeedNewLine = false };

        Assert.Equal("\r", KeyboardEncoder.Encode(TerminalKey.Enter, TerminalModifier.None, in modes));
        Assert.Null(KeyboardEncoder.Encode(TerminalKey.Enter, TerminalModifier.None, in modes, TerminalKeyEventType.Release));
        Assert.Equal("\x1b[A", KeyboardEncoder.Encode(TerminalKey.Up, TerminalModifier.None, in modes));
        Assert.Equal("\x1b[1;2A", KeyboardEncoder.Encode(TerminalKey.Up, TerminalModifier.Shift, in modes));
        Assert.Null(KeyboardEncoder.Encode(TerminalKey.Up, TerminalModifier.None, in modes, TerminalKeyEventType.Release));
    }

    [Fact]
    public void KeyboardEncoder_DisambiguateFlag_EncodesDisambiguatedKeys()
    {
        TerminalModes modes = new()
        {
            KittyKeyboardFlags = KittyKeyboardFlags.DisambiguateEscapeCodes,
        };

        // Shift + Enter -> CSI 13 ; 2 u
        Assert.Equal("\x1b[13;2u", KeyboardEncoder.Encode(TerminalKey.Enter, TerminalModifier.Shift, in modes));

        // Shift + Tab -> CSI 9 ; 2 u
        Assert.Equal("\x1b[9;2u", KeyboardEncoder.Encode(TerminalKey.Tab, TerminalModifier.Shift, in modes));

        // Alt + Backspace -> CSI 127 ; 3 u
        Assert.Equal("\x1b[127;3u", KeyboardEncoder.Encode(TerminalKey.Back, TerminalModifier.Alt, in modes));

        // Alt + Escape -> CSI 27 ; 3 u
        Assert.Equal("\x1b[27;3u", KeyboardEncoder.Encode(TerminalKey.Escape, TerminalModifier.Alt, in modes));
    }

    [Fact]
    public void KeyboardEncoder_ReportEventTypesFlag_EncodesEventsAndReleases()
    {
        TerminalModes modes = new()
        {
            KittyKeyboardFlags = KittyKeyboardFlags.ReportEventTypes,
        };

        // Arrow release without modifiers: CSI 1 ; 1 : 3 A
        Assert.Equal("\x1b[1;1:3A", KeyboardEncoder.Encode(TerminalKey.Up, TerminalModifier.None, in modes, TerminalKeyEventType.Release));

        // Shift + Arrow repeat: CSI 1 ; 2 : 2 A
        Assert.Equal("\x1b[1;2:2A", KeyboardEncoder.Encode(TerminalKey.Up, TerminalModifier.Shift, in modes, TerminalKeyEventType.Repeat));

        // PageDown release: CSI 6 ; 1 : 3 ~
        Assert.Equal("\x1b[6;1:3~", KeyboardEncoder.Encode(TerminalKey.PageDown, TerminalModifier.None, in modes, TerminalKeyEventType.Release));
    }

    [Fact]
    public void KeyboardEncoder_ReportAllKeysFlag_EncodesAllKeysAsEscapeCodes()
    {
        TerminalModes modes = new()
        {
            KittyKeyboardFlags = KittyKeyboardFlags.ReportAllKeysAsEscapeCodes,
        };

        // Unmodified Enter press -> CSI 13 u
        Assert.Equal("\x1b[13u", KeyboardEncoder.Encode(TerminalKey.Enter, TerminalModifier.None, in modes));

        // Text character press -> CSI 97 u
        Assert.Equal("\x1b[97u", KeyboardEncoder.Encode(TerminalKey.None, TerminalModifier.None, in modes, text: 'a'));

        // Text character with Ctrl -> CSI 97 ; 5 u
        Assert.Equal("\x1b[97;5u", KeyboardEncoder.Encode(TerminalKey.None, TerminalModifier.Control, in modes, text: 'a'));
    }
}
