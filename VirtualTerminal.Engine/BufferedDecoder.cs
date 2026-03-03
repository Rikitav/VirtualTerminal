using System.Diagnostics;
using System.Windows;
using VirtualTerminal.Engine.Components;

namespace VirtualTerminal.Engine;

public class BufferedDecoder() : EscapeSequenceDecoder(), IBufferedDecoder
{
    private const int defaultCols = 120;
    private const int defaultRows = 30;

    private TerminalScreenBuffer buffer = new TerminalScreenBuffer(defaultCols, defaultRows);
    private Coord savedCursorPosition = new Coord(0, 0);

    // Current text attributes
    private bool currentBold = false;
    private bool currentFaint = false;
    private bool currentItalic = false;
    private Underline currentUnderline = Underline.None;
    private Blink currentBlink = Blink.None;
    private bool currentConceal = false;
    private TextColor currentForeground = TextColor.White;
    private TextColor currentBackground = TextColor.Black;

    private bool lineWrapEnabled = false;

    public TerminalScreenBuffer Buffer
    {
        get => buffer;
    }

    public ITerminalScreenView? OuterView
    {
        get;
        set;
    }

    public Coord CursorPosition
    {
        get;
        set;
    }

    protected override void ProcessCsiCommand(byte command, ReadOnlySpan<int> parameters, bool privateMode)
    {
        switch ((char)command)
        {
            case 'A':
                {
                    MoveCursor(this, Direction.Up, parameters.At(0, 1));
                    break;
                }

            case 'B':
                {
                    MoveCursor(this, Direction.Down, parameters.At(0, 1));
                    break;
                }

            case 'C':
                {
                    MoveCursor(this, Direction.Forward, parameters.At(0, 1));
                    break;
                }

            case 'D':
                {
                    MoveCursor(this, Direction.Backward, parameters.At(0, 1));
                    break;
                }

            case 'E':
                {
                    MoveCursorToBeginningOfLineBelow(this, parameters.At(0, 1));
                    break;
                }

            case 'F':
                {
                    MoveCursorToBeginningOfLineAbove(this, parameters.At(0, 1));
                    break;
                }

            case 'G':
                {
                    MoveCursorToColumn(this, parameters.At(0, 1) - 1);
                    break;
                }

            case 'H':
            case 'f':
                {
                    MoveCursorTo(this, new Coord(parameters.At(1, 1) - 1, parameters.At(0, 1) - 1));
                    break;
                }

            case 'J':
                {
                    ClearScreen(this, (ClearDirection)parameters.At(0, 0));
                    break;
                }

            case 'K':
                {
                    ClearLine(this, (ClearDirection)parameters.At(0, 0));
                    break;
                }

            case 'S':
                {
                    ScrollPageUpwards(this, parameters.At(0, 1));
                    break;
                }

            case 'T':
                {
                    ScrollPageDownwards(this, parameters.At(0, 1));
                    break;
                }

            case 'm':
                {
                    GraphicRendition[] commands = parameters.Select(p => (GraphicRendition)p).ToArray();
                    SetGraphicRendition(this, commands);
                    break;
                }

                /*
            case 'n':
                {
                    if (parameters is [6])
                        break;

                    Point cursorPosition = GetCursorPosition(this);
                    cursorPosition.X++;
                    cursorPosition.Y++;

                    string row = cursorPosition.Y.ToString();
                    string column = cursorPosition.X.ToString();

                    byte[] output = new byte[2 + row.Length + 1 + column.Length + 1];
                    int i = 0;
                    output[i++] = EscapeCharacter;
                    output[i++] = LeftBracketCharacter;
                    
                    foreach (char c in row)
                    {
                        output[i++] = (byte)c;
                    }
                    
                    output[i++] = (byte)';';
                    foreach (char c in column)
                    {
                        output[i++] = (byte)c;
                    }

                    output[i++] = (byte)'R';
                    OnOutput(output);
                    break;
                }
                */

            case 's':
                {
                    SaveCursor(this);
                    break;
                }

            case 'u':
                {
                    RestoreCursor(this);
                    break;
                }

            case 'l':
                {
                    ProcessLCommand(command, parameters, privateMode);
                    break;
                }

            case 'h':
                {
                    ProcessHCommand(command, parameters, privateMode);
                    break;
                }

            case '>':
                {
                    // Set numeric keypad mode
                    ModeChanged(this, AnsiMode.NumericKeypad);
                    break;
                }

            case '=':
                {
                    // Set alternate keypad mode (rto: non-numeric, presumably)
                    ModeChanged(this, AnsiMode.AlternateKeypad);
                    break;
                }

            default:
                throw new InvalidCommandException(command, "");
        }
    }

    protected virtual void ProcessLCommand(byte command, ReadOnlySpan<int> parameters, bool privateMode)
    {
        int param = parameters.At(0, 0);
        switch (param)
        {
            case 20:
                {
                    // Set line feed mode
                    ModeChanged(this, AnsiMode.LineFeed);
                    break;
                }

            case 1:
                {
                    if (privateMode)
                    {
                        // Set cursor key to cursor  DECCKM 
                        ModeChanged(this, AnsiMode.CursorKeyToCursor);
                    }

                    break;
                }

            case 2:
                {
                    if (privateMode)
                    {
                        // Set ANSI (versus VT52)  DECANM
                        ModeChanged(this, AnsiMode.VT52);
                    }

                    break;
                }

            case 3:
                {
                    if (privateMode)
                    {
                        // Set number of columns to 80  DECCOLM 
                        ModeChanged(this, AnsiMode.Columns80);
                    }
                    
                    break;
                }

            case 4:
                {
                    if (privateMode)
                    {
                        // Set jump scrolling  DECSCLM 
                        ModeChanged(this, AnsiMode.JumpScrolling);
                    }
                    
                    break;
                }

            case 5:
                {
                    if (privateMode)
                    {
                        // Set normal video on screen  DECSCNM 
                        ModeChanged(this, AnsiMode.NormalVideo);
                    }

                    break;
                }

            case 6:
                {
                    if (privateMode)
                    {
                        // Set origin to absolute  DECOM 
                        ModeChanged(this, AnsiMode.OriginIsAbsolute);
                    }
                    
                    break;
                }

            case 7:
                {
                    if (privateMode)
                    {
                        // Reset auto-wrap mode  DECAWM 
                        // Disable line wrap
                        ModeChanged(this, AnsiMode.DisableLineWrap);
                    }
                    
                    break;
                }

            case 8:
                {
                    if (privateMode)
                    {
                        // Reset auto-repeat mode  DECARM 
                        ModeChanged(this, AnsiMode.DisableAutoRepeat);
                    }
                    
                    break;
                }

            case 9:
                {
                    if (privateMode)
                    {
                        // Reset interlacing mode  DECINLM 
                        ModeChanged(this, AnsiMode.DisableInterlacing);
                    }

                    break;
                }

            case 25:
                {
                    if (privateMode)
                    {
                        ModeChanged(this, AnsiMode.HideCursor);
                    }
                    
                    break;
                }

            default:
                throw new InvalidParameterException(command, param.ToString());
        }
    }

    protected virtual void ProcessHCommand(byte command, ReadOnlySpan<int> parameters, bool privateMode)
    {
        if (parameters.Length == 0)
        {
            //Set ANSI (versus VT52)  DECANM
            ModeChanged(this, AnsiMode.ANSI);
            return;
        }

        int param = parameters.At(0, 0);
        switch (param)
        {
            case 20:
                {
                    // Set new line mode
                    ModeChanged(this, AnsiMode.NewLine);
                    break;
                }

            case 1:
                {
                    if (privateMode)
                    {
                        // Set cursor key to application  DECCKM
                        ModeChanged(this, AnsiMode.CursorKeyToApplication);
                    }
                    
                    break;
                }

            case 3:
                {
                    if (privateMode)
                    {
                        // Set number of columns to 132  DECCOLM
                        ModeChanged(this, AnsiMode.Columns132);
                    }
                    
                    break;
                }

            case 4:
                {
                    if (privateMode)
                    {
                        // Set smooth scrolling  DECSCLM
                        ModeChanged(this, AnsiMode.SmoothScrolling);
                    }
                    
                    break;
                }

            case 5:
				{	
                    if (privateMode)
                    {
                        // Set reverse video on screen  DECSCNM
                        ModeChanged(this, AnsiMode.ReverseVideo);
                    }
                    
                    break;
				}

            case 6:
				{
                    if (privateMode)
                    {
                        // Set origin to relative  DECOM
                        ModeChanged(this, AnsiMode.OriginIsRelative);
                    }
                    
                    break;
                }

            case 7:
                {
                    if (privateMode)
                    {
                        // Set auto-wrap mode  DECAWM
                        // Enable line wrap
                        ModeChanged(this, AnsiMode.LineWrap);
                    }
                    
                    break;
                }

            case 8:
                {
                    if (privateMode)
                    {
                        // Set auto-repeat mode  DECARM
                        ModeChanged(this, AnsiMode.AutoRepeat);
                    }
                    
                    break;
                }

            case 9:
                {
                    if (privateMode)
                    {
                        /// Set interlacing mode 
                        ModeChanged(this, AnsiMode.Interlacing);
                    }
                    
                    break;
                }

            case 25:
                {
                    if (privateMode)
                    {
                        ModeChanged(this, AnsiMode.ShowCursor);
                    }
                    
                    break;
                }

            default:
                throw new InvalidParameterException(command, param.ToString());
        }
    }

    protected override void ProcessOscCommand(ReadOnlySpan<int> parameters, string payload)
    {
        //throw new NotImplementedException();
    }

    protected override void OnCharacters(ReadOnlySpan<char> characters)
    {
        Characters(this, characters);
    }

    public void Characters(IBufferedDecoder sender, ReadOnlySpan<char> chars)
    {
        try
        {
            foreach (char ch in chars)
            {
                switch (ch)
                {
                    case '\r':
                        {
                            // Carriage return - move to beginning of line
                            CursorPosition = new Coord(0, CursorPosition.Y);
                            break;
                        }

                    case '\n':
                        {
                            // Line feed - move to next line
                            if (CursorPosition.Y < Buffer.RowsCount - 1)
                            {
                                CursorPosition = new Coord(CursorPosition.X, CursorPosition.Y + 1);
                                break;
                            }

                            // At bottom, scroll up
                            ScrollPageUpwards(this, 1);
                            break;
                        }

                    case '\t':
                        {
                            // Tab - move to next tab stop (every 8 columns)
                            int nextTabStop = ((CursorPosition.X / 8) + 1) * 8;
                            if (nextTabStop >= Buffer.ColumnsCount)
                            {
                                nextTabStop = Buffer.ColumnsCount - 1;
                            }

                            CursorPosition = new Coord(nextTabStop, CursorPosition.Y);
                            break;
                        }

                    case '\b':
                        {
                            // Backspace - move cursor back one position
                            if (CursorPosition.X > 0)
                                CursorPosition = new Coord(CursorPosition.X - 1, CursorPosition.Y);

                            break;
                        }

                    default:
                        {
                            // Regular character
                            int linearPos = GetLinearCursorPosition();
                            if (!(linearPos >= 0 && linearPos < Buffer.Cells.Length))
                                break;

                            Buffer.Cells[linearPos].Character = ch;
                            ApplyCurrentAttributes(ref Buffer.Cells[linearPos]);

                            // Move cursor forward
                            if (CursorPosition.X < Buffer.ColumnsCount - 1)
                            {
                                CursorPosition = new Coord(CursorPosition.X + 1, CursorPosition.Y);
                                break;
                            }

                            // At end of line
                            if (!lineWrapEnabled)
                            {
                                // No wrap - stay at end of line
                                CursorPosition = new Coord(Buffer.ColumnsCount - 1, CursorPosition.Y);
                                break;
                            }

                            // Wrap to next line
                            if (CursorPosition.Y < Buffer.RowsCount - 1)
                            {
                                CursorPosition = new Coord(0, CursorPosition.Y + 1);
                                break;
                            }

                            // At bottom, scroll up and wrap
                            ScrollPageUpwards(this, 1);
                            CursorPosition = new Coord(0, Buffer.RowsCount - 1);
                            break;
                        }
                }
            }
        }
        finally
        {
            OuterView?.Characters(sender, chars);
        }
    }

    public void SaveCursor(IBufferedDecoder sernder)
    {
        try
        {
            savedCursorPosition = CursorPosition;
        }
        finally
        {
            OuterView?.SaveCursor(sernder);
        }
    }

    public void RestoreCursor(IBufferedDecoder sender)
    {
        try
        {
            CursorPosition = savedCursorPosition;
            savedCursorPosition = new Coord(0, 0);
        }
        finally
        {
            OuterView?.RestoreCursor(sender);
        }
    }

    public Size GetSize(IBufferedDecoder sender)
    {
        try
        {
            return new Size(Buffer.ColumnsCount, Buffer.RowsCount);
        }
        finally
        {
            OuterView?.GetSize(sender);
        }
    }

    public void MoveCursor(IBufferedDecoder sender, Direction direction, int amount)
    {
        try
        {
            if (amount <= 0)
                amount = 1;

            int newX = CursorPosition.X;
            int newY = CursorPosition.Y;

            switch (direction)
            {
                case Direction.Up:
                    newY = Math.Max(0, CursorPosition.Y - amount);
                    break;

                case Direction.Down:
                    newY = Math.Min(Buffer.RowsCount - 1, CursorPosition.Y + amount);
                    break;

                case Direction.Forward:
                    newX = Math.Min(Buffer.ColumnsCount - 1, CursorPosition.X + amount);
                    break;

                case Direction.Backward:
                    newX = Math.Max(0, CursorPosition.X - amount);
                    break;
            }

            CursorPosition = new Coord(newX, newY);
        }
        finally
        {
            OuterView?.MoveCursor(sender, direction, amount);
        }
    }

    public void MoveCursorToBeginningOfLineBelow(IBufferedDecoder sender, int lineNumberRelativeToCurrentLine)
    {
        if (lineNumberRelativeToCurrentLine <= 0)
            lineNumberRelativeToCurrentLine = 1;

        int newY = Math.Min(Buffer.RowsCount - 1, CursorPosition.Y + lineNumberRelativeToCurrentLine);
        CursorPosition = new Coord(0, newY);
        OuterView?.MoveCursorToBeginningOfLineBelow(sender, lineNumberRelativeToCurrentLine);
    }

    public void MoveCursorToBeginningOfLineAbove(IBufferedDecoder sender, int lineNumberRelativeToCurrentLine)
    {
        if (lineNumberRelativeToCurrentLine <= 0)
            lineNumberRelativeToCurrentLine = 1;

        int newY = Math.Max(0, CursorPosition.Y - lineNumberRelativeToCurrentLine);
        CursorPosition = new Coord(0, newY);
        OuterView?.MoveCursorToBeginningOfLineAbove(sender, lineNumberRelativeToCurrentLine);
    }

    public void MoveCursorToColumn(IBufferedDecoder sender, int columnNumber)
    {
        int newX = Math.Max(0, Math.Min(Buffer.ColumnsCount - 1, columnNumber));
        CursorPosition = new Coord(newX, CursorPosition.Y);
        OuterView?.MoveCursorToColumn(sender, columnNumber);
    }

    public void MoveCursorTo(IBufferedDecoder sender, Coord position)
    {
        int newX = Math.Max(0, Math.Min(Buffer.ColumnsCount - 1, (int)position.X));
        int newY = Math.Max(0, Math.Min(Buffer.RowsCount - 1, (int)position.Y));
        CursorPosition = new Coord(newX, newY);
        OuterView?.MoveCursorTo(sender, position);
    }

    public void ClearScreen(IBufferedDecoder sender, ClearDirection direction)
    {
        switch (direction)
        {
            case ClearDirection.Forward:  // Clear from cursor to end of screen
                ClearCells(GetLinearCursorPosition(), Buffer.Cells.Length - 1);
                break;

            case ClearDirection.Backward: // Clear from beginning to cursor
                ClearCells(0, GetLinearCursorPosition());
                break;

            case ClearDirection.Both: // Clear entire screen
                ClearCells(0, Buffer.Cells.Length - 1);
                break;
        }
        OuterView?.ClearScreen(sender, direction);
    }

    public void ClearLine(IBufferedDecoder sender, ClearDirection direction)
    {
        int lineStart = CursorPosition.Y * Buffer.ColumnsCount;
        int lineEnd = lineStart + Buffer.ColumnsCount - 1;
        int cursorPos = GetLinearCursorPosition();
        
        switch (direction)
        {
            case ClearDirection.Forward: // Clear from cursor to end of line
                ClearCells(cursorPos, lineEnd);
                break;

            case ClearDirection.Backward: // Clear from beginning of line to cursor
                ClearCells(lineStart, cursorPos);
                break;

            case ClearDirection.Both: // Clear entire line
                ClearCells(lineStart, lineEnd);
                break;
        }
        OuterView?.ClearLine(sender, direction);
    }

    public void ScrollPageUpwards(IBufferedDecoder sender, int linesToScroll)
    {
        if (linesToScroll <= 0) linesToScroll = 1;
        linesToScroll = Math.Min(linesToScroll, Buffer.RowsCount);
        
        // Move lines up
        for (int i = 0; i < Buffer.RowsCount - linesToScroll; i++)
        {
            int srcStart = (i + linesToScroll) * Buffer.ColumnsCount;
            int dstStart = i * Buffer.ColumnsCount;
            
            for (int j = 0; j < Buffer.ColumnsCount; j++)
            {
                int srcIdx = srcStart + j;
                int dstIdx = dstStart + j;
                
                if (srcIdx < Buffer.Cells.Length && dstIdx < Buffer.Cells.Length)
                {
                    CopyCell(ref Buffer.Cells[srcIdx], ref Buffer.Cells[dstIdx]);
                }
            }
        }
        
        // Clear bottom lines
        int clearStart = (Buffer.RowsCount - linesToScroll) * Buffer.ColumnsCount;
        ClearCells(clearStart, Buffer.Cells.Length - 1);
        
        OuterView?.ScrollPageUpwards(sender, linesToScroll);
    }

    public void ScrollPageDownwards(IBufferedDecoder sender, int linesToScroll)
    {
        if (linesToScroll <= 0) linesToScroll = 1;
        linesToScroll = Math.Min(linesToScroll, Buffer.RowsCount);
        
        // Move lines down
        for (int i = Buffer.RowsCount - 1; i >= linesToScroll; i--)
        {
            int srcStart = (i - linesToScroll) * Buffer.ColumnsCount;
            int dstStart = i * Buffer.ColumnsCount;
            
            for (int j = 0; j < Buffer.ColumnsCount; j++)
            {
                int srcIdx = srcStart + j;
                int dstIdx = dstStart + j;
                
                if (srcIdx < Buffer.Cells.Length && dstIdx < Buffer.Cells.Length)
                {
                    CopyCell(ref Buffer.Cells[srcIdx], ref Buffer.Cells[dstIdx]);
                }
            }
        }
        
        // Clear top lines
        int clearEnd = linesToScroll * Buffer.ColumnsCount - 1;
        ClearCells(0, clearEnd);
        
        OuterView?.ScrollPageDownwards(sender, linesToScroll);
    }

    public Coord GetCursorPosition(IBufferedDecoder sender)
    {
        OuterView?.GetCursorPosition(sender);
        return CursorPosition;
    }

    public void SetGraphicRendition(IBufferedDecoder sender, GraphicRendition[] commands)
    {
        foreach (var cmd in commands)
        {
            switch (cmd)
            {
                case GraphicRendition.Reset:
                    currentBold = false;
                    currentFaint = false;
                    currentItalic = false;
                    currentUnderline = Underline.None;
                    currentBlink = Blink.None;
                    currentConceal = false;
                    currentForeground = TextColor.White;
                    currentBackground = TextColor.Black;
                    break;
                
                case GraphicRendition.Bold:
                    currentBold = true;
                    currentFaint = false;
                    break;
                
                case GraphicRendition.Faint:
                    currentFaint = true;
                    currentBold = false;
                    break;
                
                case GraphicRendition.Italic:
                    currentItalic = true;
                    break;
                
                case GraphicRendition.Underline:
                    currentUnderline = Underline.Single;
                    break;
                
                case GraphicRendition.BlinkSlow:
                    currentBlink = Blink.Slow;
                    break;
                
                case GraphicRendition.BlinkRapid:
                    currentBlink = Blink.Rapid;
                    break;

                case GraphicRendition.Inverse: // Swap foreground and background
                    (currentForeground, currentBackground) = (currentBackground, currentForeground);
                    break;

                case GraphicRendition.Conceal:
                    currentConceal = true;
                    break;

                case GraphicRendition.UnderlineDouble:
                    currentUnderline = Underline.Double;
                    break;

                case GraphicRendition.NormalIntensity:
                    currentBold = false;
                    currentFaint = false;
                    break;

                case GraphicRendition.NoUnderline:
                    currentUnderline = Underline.None;
                    break;

                case GraphicRendition.NoBlink:
                    currentBlink = Blink.None;
                    break;

                case GraphicRendition.Positive: // Undo inverse - swap back
                    (currentForeground, currentBackground) = (currentBackground, currentForeground);
                    break;

                case GraphicRendition.Reveal:
                    currentConceal = false;
                    break;

                case GraphicRendition.ForegroundNormalBlack:
                    currentForeground = TextColor.Black;
                    break;

                case GraphicRendition.ForegroundNormalRed:
                    currentForeground = TextColor.Red;
                    break;

                case GraphicRendition.ForegroundNormalGreen:
                    currentForeground = TextColor.Green;
                    break;

                case GraphicRendition.ForegroundNormalYellow:
                    currentForeground = TextColor.Yellow;
                    break;

                case GraphicRendition.ForegroundNormalBlue:
                    currentForeground = TextColor.Blue;
                    break;

                case GraphicRendition.ForegroundNormalMagenta:
                    currentForeground = TextColor.Magenta;
                    break;

                case GraphicRendition.ForegroundNormalCyan:
                    currentForeground = TextColor.Cyan;
                    break;

                case GraphicRendition.ForegroundNormalWhite:
                    currentForeground = TextColor.White;
                    break;

                case GraphicRendition.ForegroundNormalReset:
                    currentForeground = TextColor.White;
                    break;

                case GraphicRendition.BackgroundNormalBlack:
                    currentBackground = TextColor.Black;
                    break;

                case GraphicRendition.BackgroundNormalRed:
                    currentBackground = TextColor.Red;
                    break;

                case GraphicRendition.BackgroundNormalGreen:
                    currentBackground = TextColor.Green;
                    break;

                case GraphicRendition.BackgroundNormalYellow:
                    currentBackground = TextColor.Yellow;
                    break;

                case GraphicRendition.BackgroundNormalBlue:
                    currentBackground = TextColor.Blue;
                    break;

                case GraphicRendition.BackgroundNormalMagenta:
                    currentBackground = TextColor.Magenta;
                    break;

                case GraphicRendition.BackgroundNormalCyan:
                    currentBackground = TextColor.Cyan;
                    break;

                case GraphicRendition.BackgroundNormalWhite:
                    currentBackground = TextColor.White;
                    break;

                case GraphicRendition.BackgroundNormalReset:
                    currentBackground = TextColor.Black;
                    break;

                case GraphicRendition.ForegroundBrightBlack:
                    currentForeground = TextColor.BrightBlack;
                    break;

                case GraphicRendition.ForegroundBrightRed:
                    currentForeground = TextColor.BrightRed;
                    break;

                case GraphicRendition.ForegroundBrightGreen:
                    currentForeground = TextColor.BrightGreen;
                    break;

                case GraphicRendition.ForegroundBrightYellow:
                    currentForeground = TextColor.BrightYellow;
                    break;

                case GraphicRendition.ForegroundBrightBlue:
                    currentForeground = TextColor.BrightBlue;
                    break;

                case GraphicRendition.ForegroundBrightMagenta:
                    currentForeground = TextColor.BrightMagenta;
                    break;

                case GraphicRendition.ForegroundBrightCyan:
                    currentForeground = TextColor.BrightCyan;
                    break;

                case GraphicRendition.ForegroundBrightWhite:
                    currentForeground = TextColor.BrightWhite;
                    break;

                case GraphicRendition.ForegroundBrightReset:
                    currentForeground = TextColor.White;
                    break;

                case GraphicRendition.BackgroundBrightBlack:
                    currentBackground = TextColor.BrightBlack;
                    break;

                case GraphicRendition.BackgroundBrightRed:
                    currentBackground = TextColor.BrightRed;
                    break;

                case GraphicRendition.BackgroundBrightGreen:
                    currentBackground = TextColor.BrightGreen;
                    break;

                case GraphicRendition.BackgroundBrightYellow:
                    currentBackground = TextColor.BrightYellow;
                    break;

                case GraphicRendition.BackgroundBrightBlue:
                    currentBackground = TextColor.BrightBlue;
                    break;

                case GraphicRendition.BackgroundBrightMagenta:
                    currentBackground = TextColor.BrightMagenta;
                    break;

                case GraphicRendition.BackgroundBrightCyan:
                    currentBackground = TextColor.BrightCyan;
                    break;

                case GraphicRendition.BackgroundBrightWhite:
                    currentBackground = TextColor.BrightWhite;
                    break;

                case GraphicRendition.BackgroundBrightReset:
                    currentBackground = TextColor.Black;
                    break;
            }
        }
        
        OuterView?.SetGraphicRendition(sender, commands);
    }

    public void ModeChanged(IBufferedDecoder sender, AnsiMode mode)
    {
        switch (mode)
        {
            case AnsiMode.LineWrap:
                lineWrapEnabled = true;
                break;

            case AnsiMode.DisableLineWrap:
                lineWrapEnabled = false;
                break;

             // Other modes can be stored if needed, but for now we just track line wrap
        }
        
        OuterView?.ModeChanged(sender, mode);
    }

    private int GetLinearCursorPosition()
    {
        return CursorPosition.Y * Buffer.ColumnsCount + CursorPosition.X;
    }

    [DebuggerStepThrough]
    private void ApplyCurrentAttributes(ref TerminalCellInfo cell)
    {
        cell.Bold = currentBold;
        cell.Faint = currentFaint;
        cell.Italic = currentItalic;
        cell.Underline = currentUnderline;
        cell.Blink = currentBlink;
        cell.Conceal = currentConceal;
        cell.Foreground = currentForeground;
        cell.Background = currentBackground;
    }

    private void ClearCells(int startIndex, int endIndex)
    {
        if (startIndex < 0)
            startIndex = 0;
        
        if (endIndex >= Buffer.Cells.Length)
            endIndex = Buffer.Cells.Length - 1;
        
        if (startIndex > endIndex)
            return;
        
        for (int i = startIndex; i <= endIndex; i++)
        {
            Buffer.Cells[i].Character = ' ';
            Buffer.Cells[i].Reset();
        }
    }

    [DebuggerStepThrough]
    private static void CopyCell(ref TerminalCellInfo source, ref TerminalCellInfo destination)
    {
        destination.Character = source.Character;
        destination.Bold = source.Bold;
        destination.Faint = source.Faint;
        destination.Italic = source.Italic;
        destination.Underline = source.Underline;
        destination.Blink = source.Blink;
        destination.Conceal = source.Conceal;
        destination.Foreground = source.Foreground;
        destination.Background = source.Background;
    }
}

internal static class ParamsHelpers
{
    [DebuggerStepThrough]
    public static int At(this ReadOnlySpan<int> parameters, int index, int defaultValue)
    {
        if (index < 0 || index >= parameters.Length)
            return defaultValue;

        return parameters[index];
    }

    [DebuggerStepThrough]
    public static IEnumerable<T> Select<Q, T>(this ReadOnlySpan<Q> source, Func<Q, T> transform)
    {
        List<T> result = [];
        foreach (Q item in source)
            result.Add(transform(item));

        return result;
    }
}