using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace VirtualTerminal.Engine;

public enum Blink
{
    None,
    Slow,
    Rapid,
}

public enum Underline
{
    None,
    Single,
    Double,
}

[DebuggerDisplay("'{Character}'")]
public struct TerminalCellInfo() : IEquatable<TerminalCellInfo>
{
    public char Character { get; set; } = ' ';
    public bool Bold { get; set; }
    public bool Faint { get; set; }
    public bool Italic { get; set; }
    public Underline Underline { get; set; }
    public Blink Blink { get; set; }
    public bool Conceal { get; set; }
    public Color Foreground { get; set; } = Colors.White;
    public Color Background { get; set; } = Colors.Black;

    public void Reset()
    {
        Bold = false;
        Faint = false;
        Italic = false;
        Underline = Underline.None;
        Blink = Blink.None;
        Conceal = false;
        Foreground = Colors.White;
        Background = Colors.Black;
    }

    public readonly bool Equals(TerminalCellInfo other)
    {
        return Foreground == other.Foreground
            && Background == other.Background
            && Bold == other.Bold
            && Faint == other.Faint
            && Italic == other.Italic
            && Underline == other.Underline
            && Blink == other.Blink
            && Conceal == other.Conceal;
    }

    public override readonly bool Equals(object? obj)
        => obj is TerminalCellInfo info && Equals(info);

    public static bool operator ==(TerminalCellInfo left, TerminalCellInfo right)
        => left.Equals(right);

    public static bool operator !=(TerminalCellInfo left, TerminalCellInfo right)
        => !(left == right);

    public override int GetHashCode()
        => Character.GetHashCode();
}

public class TerminalScreenBuffer(ushort initCols, ushort initRows) : IDisposable, IEnumerable<Span<TerminalCellInfo>>
{
    private int version = 0;

    public Size GridSize { get; private set; } = new Size(initCols, initRows);
    public TerminalCellInfo[] Cells { get; private set; } = new TerminalCellInfo[initCols * initRows];

    public static Encoding Encoding => Encoding.ASCII;

    public void Resize(ushort cols, ushort rows)
    {
        /*
        if (GridSize.Width == cols && GridSize.Height == rows)
            return;

        TerminalCellInfo[] newCells = new TerminalCellInfo[cols * rows];
        int rowsToCopy = Math.Min(rows, GridSize.Height);
        int colsToCopy = Math.Min(cols, GridSize.Width);

        for (int y = 0; y < rowsToCopy; y++)
            Array.Copy(Cells, y * GridSize.Width, newCells, y * cols, colsToCopy);

        Cells = newCells;
        GridSize = new Size(cols, rows);
        version += 1;
        */
    }

    public void Dispose()
    {

    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public IEnumerator<Span<TerminalCellInfo>> GetEnumerator() => new TerminalScreenBufferEnumerator(this);

    private class TerminalScreenBufferEnumerator(TerminalScreenBuffer buffer) : IEnumerator<Span<TerminalCellInfo>>
    {
        private int version = buffer.version;
        private int position = -1;

        object IEnumerator.Current => throw new NotImplementedException();
        
        public Span<TerminalCellInfo> Current
        {
            get
            {
                if (version != buffer.version)
                    throw new InvalidOperationException();

                return buffer.Cells.AsSpan(position, buffer.GridSize.Width);
            }
        }

        public void Reset()
        {
            position = 0;
        }

        public bool MoveNext()
        {
            if (version != buffer.version)
                throw new InvalidOperationException();

            position += position == -1 ? 1 : buffer.GridSize.Width;
            return position >= buffer.Cells.Length;
        }

        public void Dispose()
        {

        }
    }
}
