using System.Diagnostics.CodeAnalysis;

namespace VirtualTerminal.Model;

/// <summary>
/// A platform-independent 32-bit ARGB color. Replaces the previous <see cref="System.Drawing.Color"/>
/// usage in the core engine so <c>VirtualTerminal</c> can target <c>net10.0</c> without
/// Windows-only dependencies.
/// </summary>
public readonly struct TerminalColor : IEquatable<TerminalColor>
{
    /// <summary>Gets the alpha component.</summary>
    public byte A { get; }

    /// <summary>Gets the red component.</summary>
    public byte R { get; }

    /// <summary>Gets the green component.</summary>
    public byte G { get; }

    /// <summary>Gets the blue component.</summary>
    public byte B { get; }

    /// <summary>Initializes a new <see cref="TerminalColor"/>.</summary>
    public TerminalColor(byte a, byte r, byte g, byte b)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }

    /// <summary>Creates a color from ARGB byte components.</summary>
    public static TerminalColor FromArgb(byte a, byte r, byte g, byte b)
        => new(a, r, g, b);

    /// <summary>Creates a color from ARGB integer components, clamped to byte range.</summary>
    public static TerminalColor FromArgb(int a, int r, int g, int b)
        => new(Clamp(a), Clamp(r), Clamp(g), Clamp(b));

    /// <summary>Creates an opaque color from RGB integer components, clamped to byte range.</summary>
    public static TerminalColor FromArgb(int r, int g, int b)
        => new(255, Clamp(r), Clamp(g), Clamp(b));

    /// <summary>Creates a color from a packed 32-bit ARGB value.</summary>
    public static TerminalColor FromArgb(int argb)
    {
        uint value = unchecked((uint)argb);
        return new(
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value);
    }

    /// <summary>Gets the color as a packed 32-bit ARGB value.</summary>
    public uint ToArgb()
        => (uint)((A << 24) | (R << 16) | (G << 8) | B);

    /// <summary>Opaque black.</summary>
    public static TerminalColor Black { get; } = new(255, 0, 0, 0);

    /// <summary>Opaque white.</summary>
    public static TerminalColor White { get; } = new(255, 255, 255, 255);

    /// <summary>Opaque red.</summary>
    public static TerminalColor Red { get; } = new(255, 255, 0, 0);

    /// <summary>Opaque green.</summary>
    public static TerminalColor Green { get; } = new(255, 0, 128, 0);

    /// <summary>Opaque blue.</summary>
    public static TerminalColor Blue { get; } = new(255, 0, 0, 255);

    /// <summary>Opaque hot pink.</summary>
    public static TerminalColor HotPink { get; } = new(255, 255, 105, 180);

    /// <summary>Opaque cyan.</summary>
    public static TerminalColor Cyan { get; } = new(255, 0, 255, 255);

    /// <summary>Opaque yellow.</summary>
    public static TerminalColor Yellow { get; } = new(255, 255, 255, 0);

    /// <summary>Opaque magenta.</summary>
    public static TerminalColor Magenta { get; } = new(255, 255, 0, 255);

    /// <inheritdoc />
    public bool Equals(TerminalColor other)
        => A == other.A && R == other.R && G == other.G && B == other.B;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is TerminalColor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(A, R, G, B);

    /// <inheritdoc />
    public override string ToString()
        => $"#{ToArgb():X8}";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TerminalColor left, TerminalColor right)
        => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TerminalColor left, TerminalColor right)
        => !left.Equals(right);

    private static byte Clamp(int value)
        => (byte)Math.Clamp(value, 0, 255);
}
