using System.Diagnostics.CodeAnalysis;

namespace VirtualTerminal.Model;

/// <summary>
/// A platform-independent integer point. Replaces the previous <see cref="System.Drawing.Point"/>
/// usage in the core engine.
/// </summary>
public readonly struct TerminalPoint : IEquatable<TerminalPoint>
{
    /// <summary>Gets the X coordinate.</summary>
    public int X { get; }

    /// <summary>Gets the Y coordinate.</summary>
    public int Y { get; }

    /// <summary>Initializes a new <see cref="TerminalPoint"/>.</summary>
    public TerminalPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <inheritdoc />
    public bool Equals(TerminalPoint other)
        => X == other.X && Y == other.Y;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is TerminalPoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(X, Y);

    /// <inheritdoc />
    public override string ToString()
        => $"({X}, {Y})";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TerminalPoint left, TerminalPoint right)
        => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TerminalPoint left, TerminalPoint right)
        => !left.Equals(right);
}
