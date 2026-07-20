using System.Drawing;

namespace VirtualTerminal.Model;

/// <summary>
/// The 256-entry ANSI color palette (16 system + 216 cube + 24 grayscale). Mutable so
/// OSC 4 / 10 / 11 / 12 can recolor entries at runtime.
/// </summary>
public sealed class TerminalPalette
{
    private static readonly byte[] CubeLevels = [0, 95, 135, 175, 215, 255];

    private readonly Color[] _colors = new Color[256];

    /// <summary>Initializes a new instance with the standard xterm 256-color palette.</summary>
    public TerminalPalette() => LoadXtermDefaults();

    /// <summary>Gets or sets the color at the given ANSI index (0–255).</summary>
    public Color this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 256);
            return _colors[index];
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 256);
            _colors[index] = value;
        }
    }

    /// <summary>Returns the underlying palette colors as a span.</summary>
    /// <returns>A span over the 256-entry palette.</returns>
    public Span<Color> AsSpan() => _colors.AsSpan();

    /// <summary>Resolves an ANSI index to its <see cref="Color"/> (clamped to 0–255).</summary>
    public Color GetColor(int index)
    {
        if (index < 0)
            return _colors[0];

        if (index > 255)
            return _colors[255];

        return _colors[index];
    }

    /// <summary>Sets the color at the given ANSI index.</summary>
    /// <param name="index">The ANSI color index (0–255).</param>
    /// <param name="color">The color to assign.</param>
    public void SetAnsiColor(int index, Color color)
        => this[index] = color;

    /// <summary>Loads the standard xterm palette: 16 system colors, 6×6×6 cube, 24 grays.</summary>
    public void LoadXtermDefaults()
    {
        _colors[0] = Color.FromArgb(0x00, 0x00, 0x00);
        _colors[1] = Color.FromArgb(0xCD, 0x00, 0x00);
        _colors[2] = Color.FromArgb(0x00, 0xCD, 0x00);
        _colors[3] = Color.FromArgb(0xCD, 0xCD, 0x00);
        _colors[4] = Color.FromArgb(0x00, 0x00, 0xEE);
        _colors[5] = Color.FromArgb(0xCD, 0x00, 0xCD);
        _colors[6] = Color.FromArgb(0x00, 0xCD, 0xCD);
        _colors[7] = Color.FromArgb(0xE5, 0xE5, 0xE5);
        _colors[8] = Color.FromArgb(0x7F, 0x7F, 0x7F);
        _colors[9] = Color.FromArgb(0xFF, 0x00, 0x00);
        _colors[10] = Color.FromArgb(0x00, 0xFF, 0x00);
        _colors[11] = Color.FromArgb(0xFF, 0xFF, 0x00);
        _colors[12] = Color.FromArgb(0x5C, 0x5C, 0xFF);
        _colors[13] = Color.FromArgb(0xFF, 0x00, 0xFF);
        _colors[14] = Color.FromArgb(0x00, 0xFF, 0xFF);
        _colors[15] = Color.FromArgb(0xFF, 0xFF, 0xFF);

        for (int i = 0; i < 216; i++)
        {
            int r = CubeLevels[i / 36 % 6];
            int g = CubeLevels[i / 6 % 6];
            int b = CubeLevels[i % 6];
            _colors[16 + i] = Color.FromArgb((byte)r, (byte)g, (byte)b);
        }

        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            _colors[232 + i] = Color.FromArgb(v, v, v);
        }
    }
}
