global using VirtualTerminal.Buffer;
using VirtualTerminal.Input;

namespace VirtualTerminal.Tests;

public class MouseEncoderTests
{
    [Fact]
    public void EncodeButton_LeftPress_Sgr_SendsDefaultCode()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.Left,
            pressed: true,
            x: 0,
            y: 0,
            TerminalModifier.None,
            sgrEncoding: true);

        Assert.Equal("\x1b[<0;1;1M", seq);
    }

    [Fact]
    public void EncodeButton_LeftRelease_Sgr_SendsLowercaseSuffix()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.Left,
            pressed: false,
            x: 0,
            y: 0,
            TerminalModifier.None,
            sgrEncoding: true);

        Assert.Equal("\x1b[<0;1;1m", seq);
    }

    [Fact]
    public void EncodeButton_RightWithShift_Sgr_IncludesShiftFlag()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.Right,
            pressed: true,
            x: 4,
            y: 9,
            TerminalModifier.Shift,
            sgrEncoding: true);

        Assert.Equal("\x1b[<6;5;10M", seq);
    }

    [Fact]
    public void EncodeButton_MiddleWithControl_Sgr_IncludesControlFlag()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.Middle,
            pressed: true,
            x: 2,
            y: 3,
            TerminalModifier.Control,
            sgrEncoding: true);

        Assert.Equal("\x1b[<17;3;4M", seq);
    }

    [Fact]
    public void EncodeButton_XButton1_Sgr_UsesExtendedCode()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.XButton1,
            pressed: true,
            x: 7,
            y: 11,
            TerminalModifier.None,
            sgrEncoding: true);

        Assert.Equal("\x1b[<8;8;12M", seq);
    }

    [Fact]
    public void EncodeButton_ReleaseWithNone_Sgr_SendsReleaseCode()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.None,
            pressed: false,
            x: 1,
            y: 1,
            TerminalModifier.None,
            sgrEncoding: true);

        Assert.Equal("\x1b[<3;2;2m", seq);
    }

    [Fact]
    public void EncodeButton_LeftPressWithShiftAndControl_Sgr_IncludesBothFlags()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.Left,
            pressed: true,
            x: 0,
            y: 0,
            TerminalModifier.Shift | TerminalModifier.Control,
            sgrEncoding: true);

        Assert.Equal("\x1b[<20;1;1M", seq);
    }

    [Fact]
    public void EncodeWheel_Up_Sgr_SendsWheelUpCode()
    {
        string seq = MouseEncoder.EncodeWheel(
            up: true,
            x: 5,
            y: 5,
            TerminalModifier.None,
            sgrEncoding: true);

        Assert.Equal("\x1b[<64;6;6M", seq);
    }

    [Fact]
    public void EncodeWheel_DownWithAlt_Sgr_IncludesAltFlag()
    {
        string seq = MouseEncoder.EncodeWheel(
            up: false,
            x: 0,
            y: 2,
            TerminalModifier.Alt,
            sgrEncoding: true);

        Assert.Equal("\x1b[<73;1;3M", seq);
    }

    [Fact]
    public void EncodeMotion_LeftButton_Sgr_SetsMotionBit()
    {
        string seq = MouseEncoder.EncodeMotion(
            TerminalMouseButton.Left,
            x: 1,
            y: 2,
            TerminalModifier.None,
            sgrEncoding: true);

        Assert.Equal("\x1b[<32;2;3M", seq);
    }

    [Fact]
    public void EncodeMotion_NoButton_Sgr_UsesNoButtonCode()
    {
        string seq = MouseEncoder.EncodeMotion(
            button: null,
            x: 3,
            y: 4,
            TerminalModifier.None,
            sgrEncoding: true);

        Assert.Equal("\x1b[<35;4;5M", seq);
    }

    [Fact]
    public void EncodeMotion_RightButtonWithShift_Sgr_SetsMotionBitAndFlag()
    {
        string seq = MouseEncoder.EncodeMotion(
            TerminalMouseButton.Right,
            x: 0,
            y: 0,
            TerminalModifier.Shift,
            sgrEncoding: true);

        Assert.Equal("\x1b[<38;1;1M", seq);
    }

    [Fact]
    public void EncodeButton_LeftPress_Legacy_EncodesButtonAsCharacter()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.Left,
            pressed: true,
            x: 0,
            y: 0,
            TerminalModifier.None,
            sgrEncoding: false);

        Assert.Equal("\x1b[M   ", seq);
    }

    [Fact]
    public void EncodeButton_RightPress_Legacy_OffsetsCoordinates()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.Right,
            pressed: true,
            x: 10,
            y: 20,
            TerminalModifier.None,
            sgrEncoding: false);

        Assert.Equal("\x1b[M\"*4", seq);
    }

    [Fact]
    public void EncodeWheel_Down_Legacy_EncodesWheelCode()
    {
        string seq = MouseEncoder.EncodeWheel(
            up: false,
            x: 0,
            y: 0,
            TerminalModifier.None,
            sgrEncoding: false);

        Assert.Equal("\x1b[Ma  ", seq);
    }

    [Fact]
    public void EncodeMotion_RightButton_Legacy_SetsMotionBit()
    {
        string seq = MouseEncoder.EncodeMotion(
            TerminalMouseButton.Right,
            x: 10,
            y: 20,
            TerminalModifier.None,
            sgrEncoding: false);

        // code = 2 | 32 = 34, +32 = 66 ('B')
        Assert.Equal("\x1b[MB*4", seq);
    }

    [Fact]
    public void Legacy_ClampsCoordinatesAboveRange()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.None,
            pressed: false,
            x: 99,
            y: 199,
            TerminalModifier.None,
            sgrEncoding: false);

        // code = 3, +32 = 35 ('#'), coordinates clamped to 95 -> char 126 ('~')
        Assert.Equal("\x1b[M#~~", seq);
    }

    [Fact]
    public void Legacy_ClampsCoordinatesBelowRange()
    {
        string seq = MouseEncoder.EncodeButton(
            TerminalMouseButton.Middle,
            pressed: true,
            x: -3,
            y: -2,
            TerminalModifier.None,
            sgrEncoding: false);

        // code = 1, +32 = 33 ('!'), coordinates below 1 are clamped to 1 -> char 0x01
        Assert.Equal("\x1b[M!\x01\x01", seq);
    }
}
