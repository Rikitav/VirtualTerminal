using System.Text;
using VirtualTerminal.Rendering;

namespace VirtualTerminal.Tests;

public class BoxDrawingTests
{
    [Fact]
    public void IsBoxDrawing_IdentifiesBoxDrawingRunes()
    {
        Assert.True(BoxDrawing.IsBoxDrawing(new Rune('─')));
        Assert.True(BoxDrawing.IsBoxDrawing(new Rune('│')));
        Assert.True(BoxDrawing.IsBoxDrawing(new Rune('┌')));
        Assert.True(BoxDrawing.IsBoxDrawing(new Rune('╬')));
        Assert.True(BoxDrawing.IsBoxDrawing(new Rune('╭')));
        Assert.False(BoxDrawing.IsBoxDrawing(new Rune('A')));
        Assert.False(BoxDrawing.IsBoxDrawing(new Rune('█')));
    }

    [Fact]
    public void IsBlockElement_IdentifiesBlockElementRunes()
    {
        Assert.True(BoxDrawing.IsBlockElement(new Rune('█')));
        Assert.True(BoxDrawing.IsBlockElement(new Rune('▀')));
        Assert.True(BoxDrawing.IsBlockElement(new Rune('▄')));
        Assert.True(BoxDrawing.IsBlockElement(new Rune('░')));
        Assert.False(BoxDrawing.IsBlockElement(new Rune('─')));
        Assert.False(BoxDrawing.IsBlockElement(new Rune('Z')));
    }

    [Fact]
    public void TryGetBoxArms_DecomposesLightAndHeavyLines()
    {
        // Horizontal light
        Assert.True(BoxDrawing.TryGetBoxArms(new Rune('─'), out BoxArms horiz));
        Assert.Equal(BoxLineStyle.Light, horiz.Left);
        Assert.Equal(BoxLineStyle.Light, horiz.Right);
        Assert.Equal(BoxLineStyle.None, horiz.Top);
        Assert.Equal(BoxLineStyle.None, horiz.Bottom);

        // Vertical heavy
        Assert.True(BoxDrawing.TryGetBoxArms(new Rune('┃'), out BoxArms vertHeavy));
        Assert.Equal(BoxLineStyle.Heavy, vertHeavy.Top);
        Assert.Equal(BoxLineStyle.Heavy, vertHeavy.Bottom);
        Assert.Equal(BoxLineStyle.None, vertHeavy.Left);

        // Double cross
        Assert.True(BoxDrawing.TryGetBoxArms(new Rune('╬'), out BoxArms cross));
        Assert.Equal(BoxLineStyle.Double, cross.Left);
        Assert.Equal(BoxLineStyle.Double, cross.Right);
        Assert.Equal(BoxLineStyle.Double, cross.Top);
        Assert.Equal(BoxLineStyle.Double, cross.Bottom);

        // Rounded top-left corner
        Assert.True(BoxDrawing.TryGetBoxArms(new Rune('╭'), out BoxArms rounded));
        Assert.True(rounded.IsRounded);
        Assert.Equal(BoxLineStyle.Light, rounded.Right);
        Assert.Equal(BoxLineStyle.Light, rounded.Bottom);
    }

    [Fact]
    public void TryGetBlockRects_DecomposesBlocksAndShades()
    {
        // Full block
        Assert.True(BoxDrawing.TryGetBlockRects(new Rune('█'), out BlockRect[] full));
        Assert.Single(full);
        Assert.Equal(0, full[0].X);
        Assert.Equal(0, full[0].Y);
        Assert.Equal(1.0, full[0].Width);
        Assert.Equal(1.0, full[0].Height);
        Assert.Equal(1.0, full[0].Alpha);

        // Upper half
        Assert.True(BoxDrawing.TryGetBlockRects(new Rune('▀'), out BlockRect[] upper));
        Assert.Single(upper);
        Assert.Equal(0, upper[0].X);
        Assert.Equal(0, upper[0].Y);
        Assert.Equal(1.0, upper[0].Width);
        Assert.Equal(0.5, upper[0].Height);

        // Light shade
        Assert.True(BoxDrawing.TryGetBlockRects(new Rune('░'), out BlockRect[] shade));
        Assert.Single(shade);
        Assert.Equal(0.25, shade[0].Alpha);

        // Quadrant
        Assert.True(BoxDrawing.TryGetBlockRects(new Rune('▘'), out BlockRect[] quad));
        Assert.Single(quad);
        Assert.Equal(0, quad[0].X);
        Assert.Equal(0, quad[0].Y);
        Assert.Equal(0.5, quad[0].Width);
        Assert.Equal(0.5, quad[0].Height);
    }
}
