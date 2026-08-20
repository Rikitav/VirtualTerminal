namespace VirtualTerminal.Rendering;

/// <summary>Represents a rectangular selection between two buffer coordinates.</summary>
public readonly struct TerminalSelection
{
    /// <summary>Starting column of the selection.</summary>
    public int StartX { get; }

    /// <summary>Starting row of the selection.</summary>
    public int StartY { get; }

    /// <summary>Ending column of the selection.</summary>
    public int EndX { get; }

    /// <summary>Ending row of the selection.</summary>
    public int EndY { get; }

    /// <summary>Initializes a selection from two buffer coordinates and normalizes them.</summary>
    public TerminalSelection(int x1, int y1, int x2, int y2)
    {
        if (y1 < y2 || (y1 == y2 && x1 <= x2))
        {
            StartY = y1;
            StartX = x1;
            EndY = y2;
            EndX = x2;
        }
        else
        {
            StartY = y2;
            StartX = x2;
            EndY = y1;
            EndX = x1;
        }
    }

    /// <summary>Determines whether the specified cell lies inside the selection.</summary>
    public bool Contains(int y, int x)
    {
        if (y < StartY || y > EndY)
            return false;

        if (y == StartY && x < StartX)
            return false;

        if (y == EndY && x > EndX)
            return false;

        return true;
    }
}
