using System.Diagnostics.CodeAnalysis;

namespace VirtualTerminal.Model;

/// <summary>
/// A platform-independent integer size. Replaces the previous <see cref="System.Drawing.Size"/>
/// usage in the core engine.
/// </summary>
public readonly struct TerminalSize : IEquatable<TerminalSize>
{
    /// <summary>Gets the width.</summary>
    public int Width { get; }

    /// <summary>Gets the height.</summary>
    public int Height { get; }

    /// <summary>Initializes a new <see cref="TerminalSize"/>.</summary>
    public TerminalSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <inheritdoc />
    public bool Equals(TerminalSize other)
        => Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is TerminalSize other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Width, Height);

    /// <inheritdoc />
    public override string ToString()
        => $"{Width}x{Height}";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TerminalSize left, TerminalSize right)
        => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TerminalSize left, TerminalSize right)
        => !left.Equals(right);
}
