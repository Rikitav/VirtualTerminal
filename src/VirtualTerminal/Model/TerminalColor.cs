using System.Diagnostics.CodeAnalysis;

namespace VirtualTerminal.Model;

/// <summary>
/// A platform-independent 32-bit ARGB color. Replaces the previous <see cref="System.Drawing.Color"/>
/// usage in the core engine so <c>VirtualTerminal</c> can target <c>net10.0</c> without
/// Windows-only dependencies.
/// </summary>
public readonly struct TerminalColor : IEquatable<TerminalColor>
{
    private readonly uint _value;

    /// <summary>Gets the alpha component.</summary>
    public byte A => (byte)(_value >> 24);

    /// <summary>Gets the red component.</summary>
    public byte R => (byte)(_value >> 16);

    /// <summary>Gets the green component.</summary>
    public byte G => (byte)(_value >> 8);

    /// <summary>Gets the blue component.</summary>
    public byte B => (byte)_value;

    /// <summary>Initializes a new <see cref="TerminalColor"/>.</summary>
    public TerminalColor(byte a, byte r, byte g, byte b)
    {
        _value = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }

    private TerminalColor(uint value)
    {
        _value = value;
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
        => new(unchecked((uint)argb));

    /// <summary>Gets the color as a packed 32-bit ARGB value.</summary>
    public uint ToArgb()
        => _value;

    /// <summary>Opaque black.</summary>
    public static TerminalColor Black { get; } = new(0xFF000000);

    /// <summary>Opaque white.</summary>
    public static TerminalColor White { get; } = new(0xFFFFFFFF);

    /// <summary>Opaque red.</summary>
    public static TerminalColor Red { get; } = new(0xFFFF0000);

    /// <summary>Opaque green.</summary>
    public static TerminalColor Green { get; } = new(0xFF008000);

    /// <summary>Opaque blue.</summary>
    public static TerminalColor Blue { get; } = new(0xFF0000FF);

    /// <summary>Opaque hot pink.</summary>
    public static TerminalColor HotPink { get; } = new(0xFFFF69B4);

    /// <summary>Opaque cyan.</summary>
    public static TerminalColor Cyan { get; } = new(0xFF00FFFF);

    /// <summary>Opaque yellow.</summary>
    public static TerminalColor Yellow { get; } = new(0xFFFFFF00);

    /// <summary>Opaque magenta.</summary>
    public static TerminalColor Magenta { get; } = new(0xFFFF00FF);

    /// <inheritdoc />
    public bool Equals(TerminalColor other)
        => _value == other._value;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is TerminalColor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => unchecked((int)_value);

    /// <inheritdoc />
    public override string ToString()
        => $"#{_value:X8}";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TerminalColor left, TerminalColor right)
        => left._value == right._value;

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TerminalColor left, TerminalColor right)
        => left._value != right._value;

    private static byte Clamp(int value)
        => (byte)Math.Clamp(value, 0, 255);
}
