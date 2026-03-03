using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Windows.Media;

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

public enum TextColor
{
    Black,
    Red,
    Green,
    Yellow,
    Blue,
    Magenta,
    Cyan,
    White,
    BrightBlack,
    BrightRed,
    BrightGreen,
    BrightYellow,
    BrightBlue,
    BrightMagenta,
    BrightCyan,
    BrightWhite,
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
    public TextColor Foreground { get; set; } = TextColor.White;
    public TextColor Background { get; set; } = TextColor.Black;

    public void Reset()
    {
        Bold = false;
        Faint = false;
        Italic = false;
        Underline = Underline.None;
        Blink = Blink.None;
        Conceal = false;
        Foreground = TextColor.White;
        Background = TextColor.Black;
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

    public static Color TextColorToColor(TextColor textColor)
    {
        return textColor switch
        {
            TextColor.Black => Colors.Black,
            TextColor.Red => Colors.DarkRed,
            TextColor.Green => Colors.Green,
            TextColor.Yellow => Colors.Yellow,
            TextColor.Blue => Colors.Blue,
            TextColor.Magenta => Colors.DarkMagenta,
            TextColor.Cyan => Colors.Cyan,
            TextColor.White => Colors.White,
            TextColor.BrightBlack => Colors.Gray,
            TextColor.BrightRed => Colors.Red,
            TextColor.BrightGreen => Colors.LightGreen,
            TextColor.BrightYellow => Colors.LightYellow,
            TextColor.BrightBlue => Colors.LightBlue,
            TextColor.BrightMagenta => Colors.DarkMagenta,
            TextColor.BrightCyan => Colors.LightCyan,
            TextColor.BrightWhite => Colors.Gray,
            _ => throw new ArgumentOutOfRangeException(nameof(textColor), "Unknown color value."),
        };
    }
}

public class TerminalScreenBuffer(int initCols, int initRows) : IDisposable, IEnumerable<Span<TerminalCellInfo>>
{
    private int version = 0;

    public int ColumnsCount { get; set; } = initCols;
    public int RowsCount { get; set; } = initRows;
    public TerminalCellInfo[] Cells { get; } = new TerminalCellInfo[initCols * initRows];

    public Encoding Encoding => Encoding.ASCII;

    public TerminalScreenBuffer()
        : this(30, 120) { }

    public void Resize(int cols, int rows)
    {
        version += 1;
        throw new NotImplementedException();
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

                return buffer.Cells.AsSpan(position, buffer.ColumnsCount);
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

            position += position == -1 ? 1 : buffer.ColumnsCount;
            return position >= buffer.Cells.Length;
        }

        public void Dispose()
        {

        }
    }
}
