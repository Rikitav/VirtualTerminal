namespace VirtualTerminal.Engine;

public struct Coord(int x, int y) : IEquatable<Coord>
{
    public static readonly Coord Invalid = new Coord(-1, -1);

    public int X = x;
    public int Y = y;

    public readonly bool Equals(Coord other)
        => X == other.X && Y == other.Y;

    public override readonly bool Equals(object? obj)
        => obj is Coord coord && Equals(coord);

    public static bool operator ==(Coord left, Coord right)
        => left.Equals(right);

    public static bool operator !=(Coord left, Coord right)
        => !(left == right);

    public override readonly int GetHashCode()
        => HashCode.Combine(X.GetHashCode(), Y.GetHashCode());
}
