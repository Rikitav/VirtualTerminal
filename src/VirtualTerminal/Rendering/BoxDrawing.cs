using System.Text;

namespace VirtualTerminal.Rendering;

/// <summary>
/// Thickness/style of a box-drawing arm.
/// </summary>
public enum BoxLineStyle : byte
{
    /// <summary>No line arm.</summary>
    None = 0,
    /// <summary>Single light line (1px or 1/12 cell width).</summary>
    Light = 1,
    /// <summary>Single heavy line (2-3px or 1/6 cell width).</summary>
    Heavy = 2,
    /// <summary>Double line.</summary>
    Double = 3,
}

/// <summary>
/// Directional line arms and corner traits of a box-drawing character.
/// </summary>
public readonly struct BoxArms
{
    /// <summary>Left arm style.</summary>
    public BoxLineStyle Left { get; init; }
    /// <summary>Right arm style.</summary>
    public BoxLineStyle Right { get; init; }
    /// <summary>Top arm style.</summary>
    public BoxLineStyle Top { get; init; }
    /// <summary>Bottom arm style.</summary>
    public BoxLineStyle Bottom { get; init; }
    /// <summary>Whether this corner is rounded (arcs instead of right angles).</summary>
    public bool IsRounded { get; init; }
    /// <summary>Diagonal forward slash (/ from bottom-left to top-right).</summary>
    public bool DiagonalSlash { get; init; }
    /// <summary>Diagonal backslash (\ from top-left to bottom-right).</summary>
    public bool DiagonalBackslash { get; init; }
}

/// <summary>
/// Normalized relative sub-rectangle `[0.0 .. 1.0]` for procedural block elements.
/// </summary>
public readonly struct BlockRect
{
    /// <summary>Normalized X offset (0.0 to 1.0).</summary>
    public double X { get; init; }
    /// <summary>Normalized Y offset (0.0 to 1.0).</summary>
    public double Y { get; init; }
    /// <summary>Normalized width (0.0 to 1.0).</summary>
    public double Width { get; init; }
    /// <summary>Normalized height (0.0 to 1.0).</summary>
    public double Height { get; init; }
    /// <summary>Alpha multiplier (1.0 for solid, 0.25/0.5/0.75 for shades).</summary>
    public double Alpha { get; init; }

    /// <summary>Initializes a normalized block rect.</summary>
    public BlockRect(double x, double y, double width, double height, double alpha = 1.0)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Alpha = alpha;
    }
}

/// <summary>
/// High-performance algorithmic procedural synthesizer for Unicode Box Drawing (U+2500..U+257F)
/// and Block Elements (U+2580..U+259F). Renders seamless, gap-free borders and blocks across any font size.
/// </summary>
public static class BoxDrawing
{
    /// <summary>Determines whether the rune is within the Unicode Box Drawing range (U+2500..U+257F).</summary>
    public static bool IsBoxDrawing(Rune rune) => rune.Value is >= 0x2500 and <= 0x257F;

    /// <summary>Determines whether the rune is within the Unicode Block Elements range (U+2580..U+259F).</summary>
    public static bool IsBlockElement(Rune rune) => rune.Value is >= 0x2580 and <= 0x259F;

    /// <summary>Determines whether the rune can be procedurally synthesized (box drawing or block element).</summary>
    public static bool IsProcedural(Rune rune) => rune.Value is >= 0x2500 and <= 0x259F;

    /// <summary>
    /// Attempts to decompose a box-drawing rune into directional arm styles and corner traits.
    /// </summary>
    public static bool TryGetBoxArms(Rune rune, out BoxArms arms)
    {
        int v = rune.Value;
        if (v is < 0x2500 or > 0x257F)
        {
            arms = default;
            return false;
        }

        arms = v switch
        {
            // Horizontals
            0x2500 or 0x2504 or 0x2508 => new BoxArms { Left = BoxLineStyle.Light, Right = BoxLineStyle.Light },
            0x2501 or 0x2505 or 0x2509 => new BoxArms { Left = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy },
            0x2550 => new BoxArms { Left = BoxLineStyle.Double, Right = BoxLineStyle.Double },

            // Verticals
            0x2502 or 0x2506 or 0x250A => new BoxArms { Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light },
            0x2503 or 0x2507 or 0x250B => new BoxArms { Top = BoxLineStyle.Heavy, Bottom = BoxLineStyle.Heavy },
            0x2551 => new BoxArms { Top = BoxLineStyle.Double, Bottom = BoxLineStyle.Double },

            // Corners: Top-Left (Down and Right)
            0x250C => new BoxArms { Bottom = BoxLineStyle.Light, Right = BoxLineStyle.Light },
            0x250D => new BoxArms { Bottom = BoxLineStyle.Light, Right = BoxLineStyle.Heavy },
            0x250E => new BoxArms { Bottom = BoxLineStyle.Heavy, Right = BoxLineStyle.Light },
            0x250F => new BoxArms { Bottom = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy },
            0x2552 => new BoxArms { Bottom = BoxLineStyle.Light, Right = BoxLineStyle.Double },
            0x2553 => new BoxArms { Bottom = BoxLineStyle.Double, Right = BoxLineStyle.Light },
            0x2554 => new BoxArms { Bottom = BoxLineStyle.Double, Right = BoxLineStyle.Double },
            0x256D => new BoxArms { Bottom = BoxLineStyle.Light, Right = BoxLineStyle.Light, IsRounded = true },

            // Corners: Top-Right (Down and Left)
            0x2510 => new BoxArms { Bottom = BoxLineStyle.Light, Left = BoxLineStyle.Light },
            0x2511 => new BoxArms { Bottom = BoxLineStyle.Light, Left = BoxLineStyle.Heavy },
            0x2512 => new BoxArms { Bottom = BoxLineStyle.Heavy, Left = BoxLineStyle.Light },
            0x2513 => new BoxArms { Bottom = BoxLineStyle.Heavy, Left = BoxLineStyle.Heavy },
            0x2555 => new BoxArms { Bottom = BoxLineStyle.Light, Left = BoxLineStyle.Double },
            0x2556 => new BoxArms { Bottom = BoxLineStyle.Double, Left = BoxLineStyle.Light },
            0x2557 => new BoxArms { Bottom = BoxLineStyle.Double, Left = BoxLineStyle.Double },
            0x256E => new BoxArms { Bottom = BoxLineStyle.Light, Left = BoxLineStyle.Light, IsRounded = true },

            // Corners: Bottom-Left (Up and Right)
            0x2514 => new BoxArms { Top = BoxLineStyle.Light, Right = BoxLineStyle.Light },
            0x2515 => new BoxArms { Top = BoxLineStyle.Light, Right = BoxLineStyle.Heavy },
            0x2516 => new BoxArms { Top = BoxLineStyle.Heavy, Right = BoxLineStyle.Light },
            0x2517 => new BoxArms { Top = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy },
            0x2558 => new BoxArms { Top = BoxLineStyle.Light, Right = BoxLineStyle.Double },
            0x2559 => new BoxArms { Top = BoxLineStyle.Double, Right = BoxLineStyle.Light },
            0x255A => new BoxArms { Top = BoxLineStyle.Double, Right = BoxLineStyle.Double },
            0x2570 => new BoxArms { Top = BoxLineStyle.Light, Right = BoxLineStyle.Light, IsRounded = true },

            // Corners: Bottom-Right (Up and Left)
            0x2518 => new BoxArms { Top = BoxLineStyle.Light, Left = BoxLineStyle.Light },
            0x2519 => new BoxArms { Top = BoxLineStyle.Light, Left = BoxLineStyle.Heavy },
            0x251A => new BoxArms { Top = BoxLineStyle.Heavy, Left = BoxLineStyle.Light },
            0x251B => new BoxArms { Top = BoxLineStyle.Heavy, Left = BoxLineStyle.Heavy },
            0x255B => new BoxArms { Top = BoxLineStyle.Light, Left = BoxLineStyle.Double },
            0x255C => new BoxArms { Top = BoxLineStyle.Double, Left = BoxLineStyle.Light },
            0x255D => new BoxArms { Top = BoxLineStyle.Double, Left = BoxLineStyle.Double },
            0x256F => new BoxArms { Top = BoxLineStyle.Light, Left = BoxLineStyle.Light, IsRounded = true },

            // Tees: Left (Vertical and Right)
            0x251C => new BoxArms { Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light, Right = BoxLineStyle.Light },
            0x251D => new BoxArms { Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light, Right = BoxLineStyle.Heavy },
            0x2520 => new BoxArms { Top = BoxLineStyle.Heavy, Bottom = BoxLineStyle.Heavy, Right = BoxLineStyle.Light },
            0x2523 => new BoxArms { Top = BoxLineStyle.Heavy, Bottom = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy },
            0x255E => new BoxArms { Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light, Right = BoxLineStyle.Double },
            0x255F => new BoxArms { Top = BoxLineStyle.Double, Bottom = BoxLineStyle.Double, Right = BoxLineStyle.Light },
            0x2560 => new BoxArms { Top = BoxLineStyle.Double, Bottom = BoxLineStyle.Double, Right = BoxLineStyle.Double },

            // Tees: Right (Vertical and Left)
            0x2524 => new BoxArms { Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light, Left = BoxLineStyle.Light },
            0x2525 => new BoxArms { Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light, Left = BoxLineStyle.Heavy },
            0x2528 => new BoxArms { Top = BoxLineStyle.Heavy, Bottom = BoxLineStyle.Heavy, Left = BoxLineStyle.Light },
            0x252B => new BoxArms { Top = BoxLineStyle.Heavy, Bottom = BoxLineStyle.Heavy, Left = BoxLineStyle.Heavy },
            0x2561 => new BoxArms { Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light, Left = BoxLineStyle.Double },
            0x2562 => new BoxArms { Top = BoxLineStyle.Double, Bottom = BoxLineStyle.Double, Left = BoxLineStyle.Light },
            0x2563 => new BoxArms { Top = BoxLineStyle.Double, Bottom = BoxLineStyle.Double, Left = BoxLineStyle.Double },

            // Tees: Top (Horizontal and Down)
            0x252C => new BoxArms { Left = BoxLineStyle.Light, Right = BoxLineStyle.Light, Bottom = BoxLineStyle.Light },
            0x252F => new BoxArms { Left = BoxLineStyle.Light, Right = BoxLineStyle.Light, Bottom = BoxLineStyle.Heavy },
            0x2530 => new BoxArms { Left = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy, Bottom = BoxLineStyle.Light },
            0x2533 => new BoxArms { Left = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy, Bottom = BoxLineStyle.Heavy },
            0x2564 => new BoxArms { Left = BoxLineStyle.Light, Right = BoxLineStyle.Light, Bottom = BoxLineStyle.Double },
            0x2565 => new BoxArms { Left = BoxLineStyle.Double, Right = BoxLineStyle.Double, Bottom = BoxLineStyle.Light },
            0x2566 => new BoxArms { Left = BoxLineStyle.Double, Right = BoxLineStyle.Double, Bottom = BoxLineStyle.Double },

            // Tees: Bottom (Horizontal and Up)
            0x2534 => new BoxArms { Left = BoxLineStyle.Light, Right = BoxLineStyle.Light, Top = BoxLineStyle.Light },
            0x2537 => new BoxArms { Left = BoxLineStyle.Light, Right = BoxLineStyle.Light, Top = BoxLineStyle.Heavy },
            0x2538 => new BoxArms { Left = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy, Top = BoxLineStyle.Light },
            0x253B => new BoxArms { Left = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy, Top = BoxLineStyle.Heavy },
            0x2567 => new BoxArms { Left = BoxLineStyle.Light, Right = BoxLineStyle.Light, Top = BoxLineStyle.Double },
            0x2568 => new BoxArms { Left = BoxLineStyle.Double, Right = BoxLineStyle.Double, Top = BoxLineStyle.Light },
            0x2569 => new BoxArms { Left = BoxLineStyle.Double, Right = BoxLineStyle.Double, Top = BoxLineStyle.Double },

            // Crosses
            0x253C => new BoxArms { Left = BoxLineStyle.Light, Right = BoxLineStyle.Light, Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light },
            0x2542 => new BoxArms { Left = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy, Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light },
            0x254B => new BoxArms { Left = BoxLineStyle.Heavy, Right = BoxLineStyle.Heavy, Top = BoxLineStyle.Heavy, Bottom = BoxLineStyle.Heavy },
            0x256A => new BoxArms { Left = BoxLineStyle.Light, Right = BoxLineStyle.Light, Top = BoxLineStyle.Double, Bottom = BoxLineStyle.Double },
            0x256B => new BoxArms { Left = BoxLineStyle.Double, Right = BoxLineStyle.Double, Top = BoxLineStyle.Light, Bottom = BoxLineStyle.Light },
            0x256C => new BoxArms { Left = BoxLineStyle.Double, Right = BoxLineStyle.Double, Top = BoxLineStyle.Double, Bottom = BoxLineStyle.Double },

            // Half-lines
            0x2574 => new BoxArms { Left = BoxLineStyle.Light },
            0x2575 => new BoxArms { Top = BoxLineStyle.Light },
            0x2576 => new BoxArms { Right = BoxLineStyle.Light },
            0x2577 => new BoxArms { Bottom = BoxLineStyle.Light },
            0x2578 => new BoxArms { Left = BoxLineStyle.Heavy },
            0x2579 => new BoxArms { Top = BoxLineStyle.Heavy },
            0x257A => new BoxArms { Right = BoxLineStyle.Heavy },
            0x257B => new BoxArms { Bottom = BoxLineStyle.Heavy },

            // Diagonals
            0x2571 => new BoxArms { DiagonalSlash = true },
            0x2572 => new BoxArms { DiagonalBackslash = true },
            0x2573 => new BoxArms { DiagonalSlash = true, DiagonalBackslash = true },

            _ => default,
        };

        return arms.Left != BoxLineStyle.None || arms.Right != BoxLineStyle.None ||
               arms.Top != BoxLineStyle.None || arms.Bottom != BoxLineStyle.None ||
               arms.DiagonalSlash || arms.DiagonalBackslash;
    }

    /// <summary>
    /// Attempts to decompose a block-element rune into one or more normalized relative sub-rectangles.
    /// </summary>
    public static bool TryGetBlockRects(Rune rune, out BlockRect[] rects)
    {
        int v = rune.Value;
        if (v is < 0x2580 or > 0x259F)
        {
            rects = [];
            return false;
        }

        rects = v switch
        {
            // Full block
            0x2588 => [new BlockRect(0, 0, 1.0, 1.0)],

            // Upper half
            0x2580 => [new BlockRect(0, 0, 1.0, 0.5)],

            // Lower blocks: 1/8 to 7/8
            0x2581 => [new BlockRect(0, 7.0 / 8.0, 1.0, 1.0 / 8.0)],
            0x2582 => [new BlockRect(0, 6.0 / 8.0, 1.0, 2.0 / 8.0)],
            0x2583 => [new BlockRect(0, 5.0 / 8.0, 1.0, 3.0 / 8.0)],
            0x2584 => [new BlockRect(0, 4.0 / 8.0, 1.0, 4.0 / 8.0)], // lower half
            0x2585 => [new BlockRect(0, 3.0 / 8.0, 1.0, 5.0 / 8.0)],
            0x2586 => [new BlockRect(0, 2.0 / 8.0, 1.0, 6.0 / 8.0)],
            0x2587 => [new BlockRect(0, 1.0 / 8.0, 1.0, 7.0 / 8.0)],

            // Left blocks: 7/8 down to 1/8
            0x2589 => [new BlockRect(0, 0, 7.0 / 8.0, 1.0)],
            0x258A => [new BlockRect(0, 0, 6.0 / 8.0, 1.0)],
            0x258B => [new BlockRect(0, 0, 5.0 / 8.0, 1.0)],
            0x258C => [new BlockRect(0, 0, 4.0 / 8.0, 1.0)], // left half
            0x258D => [new BlockRect(0, 0, 3.0 / 8.0, 1.0)],
            0x258E => [new BlockRect(0, 0, 2.0 / 8.0, 1.0)],
            0x258F => [new BlockRect(0, 0, 1.0 / 8.0, 1.0)],

            // Right half and right 1/8
            0x2590 => [new BlockRect(0.5, 0, 0.5, 1.0)],
            0x2595 => [new BlockRect(7.0 / 8.0, 0, 1.0 / 8.0, 1.0)],

            // Upper 1/8
            0x2594 => [new BlockRect(0, 0, 1.0, 1.0 / 8.0)],

            // Shades
            0x2591 => [new BlockRect(0, 0, 1.0, 1.0, alpha: 0.25)],
            0x2592 => [new BlockRect(0, 0, 1.0, 1.0, alpha: 0.50)],
            0x2593 => [new BlockRect(0, 0, 1.0, 1.0, alpha: 0.75)],

            // Quadrant blocks
            0x2596 => [new BlockRect(0, 0.5, 0.5, 0.5)],                         // Quadrant lower left
            0x2597 => [new BlockRect(0.5, 0.5, 0.5, 0.5)],                       // Quadrant lower right
            0x2598 => [new BlockRect(0, 0, 0.5, 0.5)],                           // Quadrant upper left
            0x259D => [new BlockRect(0.5, 0, 0.5, 0.5)],                         // Quadrant upper right
            0x2599 => [new BlockRect(0, 0, 0.5, 0.5), new BlockRect(0, 0.5, 1.0, 0.5)], // Upper left and lower left/right
            0x259A => [new BlockRect(0, 0, 0.5, 0.5), new BlockRect(0.5, 0.5, 0.5, 0.5)], // Upper left and lower right (diagonal)
            0x259B => [new BlockRect(0, 0, 1.0, 0.5), new BlockRect(0, 0.5, 0.5, 0.5)], // Upper left/right and lower left
            0x259C => [new BlockRect(0, 0, 1.0, 0.5), new BlockRect(0.5, 0.5, 0.5, 0.5)], // Upper left/right and lower right
            0x259E => [new BlockRect(0.5, 0, 0.5, 0.5), new BlockRect(0, 0.5, 0.5, 0.5)], // Upper right and lower left (diagonal)
            0x259F => [new BlockRect(0.5, 0, 0.5, 0.5), new BlockRect(0, 0.5, 1.0, 0.5)], // Upper right and lower left/right

            _ => [],
        };

        return rects.Length > 0;
    }
}
