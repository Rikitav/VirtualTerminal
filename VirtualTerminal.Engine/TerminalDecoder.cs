using System.Diagnostics;
using System.Drawing;
using System.Windows.Media;
using VirtualTerminal.Engine.Components;
using Color = System.Windows.Media.Color;

namespace VirtualTerminal.Engine;

public class TerminalDecoder : EscapeSequenceDecoder, IProxingDecoder
{
    private const int defaultCols = 512;
    private const int defaultRows = 1024;

    private bool currentBold = false;
    private bool currentFaint = false;
    private bool currentItalic = false;
    private Underline currentUnderline = Underline.None;
    private Blink currentBlink = Blink.None;
    private bool currentConceal = false;
    private Color currentForeground = Colors.White;
    private Color currentBackground = Colors.Black;
    private Point savedCursorPosition = new Point(0, 0);
    private bool lineWrapEnabled = false;

    public TerminalScreenBuffer Buffer
    {
        get;
        private set;
    }

    public Point CursorPosition
    {
        get;
        private set;
    }

    public ITerminalScreenView? OuterView
    {
        get => field;
        set
        {
            field = value;
            field?.BufferChanged(this, Buffer);
        }
    }

    public TerminalDecoder()
    {
        Buffer = new TerminalScreenBuffer(defaultCols, defaultRows);
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
                    MoveCursorTo(this, new Point(parameters.At(1, 1) - 1, parameters.At(0, 1) - 1));
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
                    GraphicRendition[] commands = [.. parameters.Select(p => (GraphicRendition)p)];
                    SetGraphicRendition(this, commands);
                    break;
                }

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

            case 'X':
                {
                    EraseCharacters(this, parameters.At(0, 1));
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
        //throw new InvalidOperationException();
    }

    protected override void OnCharacters(ReadOnlySpan<char> characters)
    {
        Characters(this, characters);
    }

    public void Characters(ITerminalDecoder sender, ReadOnlySpan<char> chars)
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
                            CursorPosition = new Point(0, CursorPosition.Y);
                            break;
                        }

                    case '\n':
                        {
                            // Line feed - move to next line
                            if (CursorPosition.Y < Buffer.GridSize.Height - 1)
                            {
                                CursorPosition = new Point(CursorPosition.X, CursorPosition.Y + 1);
                                if (CursorPosition.Y + 1 > Buffer.Rows.Count)
                                {
                                    for (int i = Buffer.Rows.Count; i < CursorPosition.Y + 1; i++)
                                        Buffer.AppendRow();
                                }

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
                            if (nextTabStop >= Buffer.GridSize.Width)
                            {
                                nextTabStop = Buffer.GridSize.Width - 1;
                            }

                            CursorPosition = new Point(nextTabStop, CursorPosition.Y);
                            break;
                        }

                    case '\b':
                        {
                            // Backspace - move cursor back one position
                            if (CursorPosition.X > 0)
                                CursorPosition = new Point(CursorPosition.X - 1, CursorPosition.Y);

                            break;
                        }

                    default:
                        {
                            Buffer[CursorPosition].Character = ch;
                            ApplyCurrentAttributes(ref Buffer[CursorPosition]);

                            // Move cursor forward
                            if (CursorPosition.X < Buffer.GridSize.Width - 1)
                            {
                                CursorPosition = new Point(CursorPosition.X + 1, CursorPosition.Y);
                                break;
                            }

                            // At end of line
                            if (!lineWrapEnabled)
                            {
                                // No wrap - stay at end of line
                                CursorPosition = new Point(Buffer.GridSize.Width - 1, CursorPosition.Y);
                                break;
                            }

                            // Wrap to next line
                            if (CursorPosition.Y < Buffer.GridSize.Height - 1)
                            {
                                CursorPosition = new Point(0, CursorPosition.Y + 1);
                                if (CursorPosition.Y + 1 > Buffer.Rows.Count)
                                {
                                    for (int i = Buffer.Rows.Count; i < CursorPosition.Y + 1; i++)
                                        Buffer.AppendRow();
                                }

                                break;
                            }

                            // At bottom, scroll up and wrap
                            ScrollPageUpwards(this, 1);
                            CursorPosition = new Point(0, Buffer.GridSize.Height - 1);
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

    public void SaveCursor(ITerminalDecoder sender)
    {
        try
        {
            savedCursorPosition = CursorPosition;
        }
        finally
        {
            OuterView?.SaveCursor(sender);
        }
    }

    public void RestoreCursor(ITerminalDecoder sender)
    {
        try
        {
            CursorPosition = savedCursorPosition;
            savedCursorPosition = new Point(0, 0);
        }
        finally
        {
            OuterView?.RestoreCursor(sender);
        }
    }

    public Size GetSize(ITerminalDecoder sender)
    {
        try
        {
            return new Size(Buffer.GridSize.Width, Buffer.GridSize.Height);
        }
        finally
        {
            OuterView?.GetSize(sender);
        }
    }

    public void MoveCursor(ITerminalDecoder sender, Direction direction, int amount)
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
                    newY = Math.Min(Buffer.GridSize.Height - 1, CursorPosition.Y + amount);
                    break;

                case Direction.Forward:
                    newX = Math.Min(Buffer.GridSize.Width - 1, CursorPosition.X + amount);
                    break;

                case Direction.Backward:
                    newX = Math.Max(0, CursorPosition.X - amount);
                    break;
            }

            CursorPosition = new Point(newX, newY);
            if (CursorPosition.Y + 1 > Buffer.Rows.Count)
            {
                for (int i = Buffer.Rows.Count; i < CursorPosition.Y + 1; i++)
                    Buffer.AppendRow();
            }
        }
        finally
        {
            OuterView?.MoveCursor(sender, direction, amount);
        }
    }

    public void MoveCursorToBeginningOfLineBelow(ITerminalDecoder sender, int lineNumberRelativeToCurrentLine)
    {
        if (lineNumberRelativeToCurrentLine <= 0)
            lineNumberRelativeToCurrentLine = 1;

        int newY = Math.Min(Buffer.GridSize.Height - 1, CursorPosition.Y + lineNumberRelativeToCurrentLine);
        CursorPosition = new Point(0, newY);
        OuterView?.MoveCursorToBeginningOfLineBelow(sender, lineNumberRelativeToCurrentLine);
    }

    public void MoveCursorToBeginningOfLineAbove(ITerminalDecoder sender, int lineNumberRelativeToCurrentLine)
    {
        if (lineNumberRelativeToCurrentLine <= 0)
            lineNumberRelativeToCurrentLine = 1;

        int newY = Math.Max(0, CursorPosition.Y - lineNumberRelativeToCurrentLine);
        CursorPosition = new Point(0, newY);
        OuterView?.MoveCursorToBeginningOfLineAbove(sender, lineNumberRelativeToCurrentLine);
    }

    public void MoveCursorToColumn(ITerminalDecoder sender, int columnNumber)
    {
        int newX = Math.Max(0, Math.Min(Buffer.GridSize.Width - 1, columnNumber));
        CursorPosition = new Point(newX, CursorPosition.Y);
        OuterView?.MoveCursorToColumn(sender, columnNumber);
    }

    public void MoveCursorTo(ITerminalDecoder sender, Point position)
    {
        int newX = Math.Max(0, Math.Min(Buffer.GridSize.Width - 1, (int)position.X));
        int newY = Math.Max(0, Math.Min(Buffer.GridSize.Height - 1, (int)position.Y));
        CursorPosition = new Point(newX, newY);

        if (CursorPosition.Y + 1 > Buffer.Rows.Count)
        {
            for (int i = Buffer.Rows.Count; i < CursorPosition.Y + 1; i++)
                Buffer.AppendRow();
        }

        OuterView?.MoveCursorTo(sender, position);
    }

    public void ClearScreen(ITerminalDecoder sender, ClearDirection direction)
    {
        switch (direction)
        {
            case ClearDirection.Forward:  // Clear from cursor to end of screen
                ClearCells(GetLinearCursorPosition(), Buffer.Length - 1);
                break;

            case ClearDirection.Backward: // Clear from beginning to cursor
                ClearCells(0, GetLinearCursorPosition());
                break;

            case ClearDirection.Both: // Clear entire screen
                ClearCells(0, Buffer.Length - 1);
                break;
        }

        OuterView?.ClearScreen(sender, direction);
    }

    public void ClearLine(ITerminalDecoder sender, ClearDirection direction)
    {
        int lineStart = CursorPosition.Y * Buffer.GridSize.Width;
        int lineEnd = lineStart + Buffer.GridSize.Width - 1;
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

    public void EraseCharacters(ITerminalDecoder sender, int count)
    {
        if (count <= 0)
            count = 1;

        int limit = Buffer.GridSize.Width - CursorPosition.X;
        if (count > limit)
            count = limit;

        for (int i = 0; i < count; i++)
        {
            ref TerminalCellInfo cell = ref Buffer[CursorPosition.Y, CursorPosition.X + i];
            cell.Character = ' ';
            ApplyCurrentAttributes(ref cell);
        }
    }

    public void ScrollPageUpwards(ITerminalDecoder sender, int linesToScroll)
    {
        if (linesToScroll <= 0)
            linesToScroll = 1;

        linesToScroll = Math.Min(linesToScroll, Buffer.GridSize.Height);
        
        /*
        // Move lines up
        for (int i = 0; i < Buffer.GridSize.Height - linesToScroll; i++)
        {
            int srcStart = (i + linesToScroll) * Buffer.GridSize.Width;
            int dstStart = i * Buffer.GridSize.Width;
            
            for (int j = 0; j < Buffer.GridSize.Width; j++)
            {
                int srcIdx = srcStart + j;
                int dstIdx = dstStart + j;
                
                if (srcIdx < Buffer.Length && dstIdx < Buffer.Length)
                {
                    CopyCell(ref Buffer[srcIdx], ref Buffer[dstIdx]);
                }
            }
        }
        
        // Clear bottom lines
        int clearStart = (Buffer.GridSize.Height - linesToScroll) * Buffer.GridSize.Width;
        ClearCells(clearStart, Buffer.Length - 1);
        */
        
        OuterView?.ScrollPageUpwards(sender, linesToScroll);
    }

    public void ScrollPageDownwards(ITerminalDecoder sender, int linesToScroll)
    {
        if (linesToScroll <= 0)
            linesToScroll = 1;
        
        linesToScroll = Math.Min(linesToScroll, Buffer.GridSize.Height);
        
        /*
        // Move lines down
        for (int i = Buffer.GridSize.Height - 1; i >= linesToScroll; i--)
        {
            int srcStart = (i - linesToScroll) * Buffer.GridSize.Width;
            int dstStart = i * Buffer.GridSize.Width;
            
            for (int j = 0; j < Buffer.GridSize.Width; j++)
            {
                int srcIdx = srcStart + j;
                int dstIdx = dstStart + j;
                
                if (srcIdx < Buffer.Length && dstIdx < Buffer.Length)
                {
                    CopyCell(ref Buffer[srcIdx], ref Buffer[dstIdx]);
                }
            }
        }
        
        // Clear top lines
        int clearEnd = (linesToScroll * Buffer.GridSize.Width) - 1;
        ClearCells(0, clearEnd);
        */
        
        OuterView?.ScrollPageDownwards(sender, linesToScroll);
    }

    public Point GetCursorPosition(ITerminalDecoder sender)
    {
        OuterView?.GetCursorPosition(sender);
        return CursorPosition;
    }

    private static readonly Color[] SystemColors = new Color[]
    {
        Color.FromRgb(0, 0, 0),       // 0: Black
        Color.FromRgb(128, 0, 0),     // 1: Red
        Color.FromRgb(0, 128, 0),     // 2: Green
        Color.FromRgb(128, 128, 0),   // 3: Yellow
        Color.FromRgb(0, 0, 128),     // 4: Blue
        Color.FromRgb(128, 0, 128),   // 5: Magenta
        Color.FromRgb(0, 128, 128),   // 6: Cyan
        Color.FromRgb(192, 192, 192), // 7: White
        Color.FromRgb(128, 128, 128), // 8: Bright Black (Gray)
        Color.FromRgb(255, 0, 0),     // 9: Bright Red
        Color.FromRgb(0, 255, 0),     // 10: Bright Green
        Color.FromRgb(255, 255, 0),   // 11: Bright Yellow
        Color.FromRgb(0, 0, 255),     // 12: Bright Blue
        Color.FromRgb(255, 0, 255),   // 13: Bright Magenta
        Color.FromRgb(0, 255, 255),   // 14: Bright Cyan
        Color.FromRgb(255, 255, 255)  // 15: Bright White
    };

    private static readonly byte[] CubeLevels = { 0, 95, 135, 175, 215, 255 };

    public static Color GetColor(int index)
    {
        if (index < 0 || index > 255)
            throw new ArgumentOutOfRangeException(nameof(index), "Индекс должен быть от 0 до 255.");

        // 1. Системные цвета
        if (index < 16)
        {
            return SystemColors[index];
        }

        // 2. Цветовой куб 6x6x6 (индексы 16-231)
        if (index < 232)
        {
            int colorIndex = index - 16;
            byte r = CubeLevels[(colorIndex / 36) % 6];
            byte g = CubeLevels[(colorIndex / 6) % 6];
            byte b = CubeLevels[colorIndex % 6];
            return Color.FromRgb(r, g, b);
        }

        // 3. Градация серого (индексы 232-255)
        // Формула: 8 + (index - 232) * 10
        byte grayValue = (byte)(8 + (index - 232) * 10);
        return Color.FromRgb(grayValue, grayValue, grayValue);
    }

    public void SetGraphicRendition(ITerminalDecoder sender, GraphicRendition[] commands)
    {
        if (commands.Length == 0)
        {
            currentBold = false;
            currentFaint = false;
            currentItalic = false;
            currentUnderline = Underline.None;
            currentBlink = Blink.None;
            currentConceal = false;
            currentForeground = Colors.White;
            currentBackground = Colors.Black;
        }
        else if (commands is [GraphicRendition.AixtermSetBackground, GraphicRendition.AixtermColors, ..])
        {
            currentBackground = GetColor((int)commands[2]);
        }
        else if (commands is [GraphicRendition.AixtermSetForeground, GraphicRendition.AixtermColors, ..])
        {
            currentForeground = GetColor((int)commands[2]);
        }
        else if (commands is [GraphicRendition.AixtermSetBackground, GraphicRendition.TrueColors, ..])
        {
            currentBackground = Color.FromRgb((byte)commands[2], (byte)commands[3], (byte)commands[4]);
        }
        else if (commands is [GraphicRendition.AixtermSetForeground, GraphicRendition.TrueColors, ..])
        {
            currentForeground = Color.FromRgb((byte)commands[2], (byte)commands[3], (byte)commands[4]);
        }
        else
        {
            foreach (GraphicRendition cmd in commands)
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
                        currentForeground = Colors.White;
                        currentBackground = Colors.Black;
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
                        currentForeground = Colors.Black;
                        break;

                    case GraphicRendition.ForegroundNormalRed:
                        currentForeground = Colors.DarkRed;
                        break;

                    case GraphicRendition.ForegroundNormalGreen:
                        currentForeground = Colors.Green;
                        break;

                    case GraphicRendition.ForegroundNormalYellow:
                        currentForeground = Colors.Yellow;
                        break;

                    case GraphicRendition.ForegroundNormalBlue:
                        currentForeground = Colors.Blue;
                        break;

                    case GraphicRendition.ForegroundNormalMagenta:
                        currentForeground = Colors.DarkMagenta;
                        break;

                    case GraphicRendition.ForegroundNormalCyan:
                        currentForeground = Colors.Cyan;
                        break;

                    case GraphicRendition.ForegroundNormalWhite:
                        currentForeground = Colors.White;
                        break;

                    case GraphicRendition.ForegroundNormalReset:
                        currentForeground = Colors.White;
                        break;

                    case GraphicRendition.BackgroundNormalBlack:
                        currentBackground = Colors.Black;
                        break;

                    case GraphicRendition.BackgroundNormalRed:
                        currentBackground = Colors.DarkRed;
                        break;

                    case GraphicRendition.BackgroundNormalGreen:
                        currentBackground = Colors.Green;
                        break;

                    case GraphicRendition.BackgroundNormalYellow:
                        currentBackground = Colors.Yellow;
                        break;

                    case GraphicRendition.BackgroundNormalBlue:
                        currentBackground = Colors.Blue;
                        break;

                    case GraphicRendition.BackgroundNormalMagenta:
                        currentBackground = Colors.DarkMagenta;
                        break;

                    case GraphicRendition.BackgroundNormalCyan:
                        currentBackground = Colors.Cyan;
                        break;

                    case GraphicRendition.BackgroundNormalWhite:
                        currentBackground = Colors.White;
                        break;

                    case GraphicRendition.BackgroundNormalReset:
                        currentBackground = Colors.Black;
                        break;

                    case GraphicRendition.ForegroundBrightBlack:
                        currentForeground = Colors.Gray;
                        break;

                    case GraphicRendition.ForegroundBrightRed:
                        currentForeground = Colors.Red;
                        break;

                    case GraphicRendition.ForegroundBrightGreen:
                        currentForeground = Colors.LightGreen;
                        break;

                    case GraphicRendition.ForegroundBrightYellow:
                        currentForeground = Colors.LightYellow;
                        break;

                    case GraphicRendition.ForegroundBrightBlue:
                        currentForeground = Colors.LightBlue;
                        break;

                    case GraphicRendition.ForegroundBrightMagenta:
                        currentForeground = Colors.Magenta;
                        break;

                    case GraphicRendition.ForegroundBrightCyan:
                        currentForeground = Colors.LightCyan;
                        break;

                    case GraphicRendition.ForegroundBrightWhite:
                        currentForeground = Colors.Gray;
                        break;

                    case GraphicRendition.ForegroundBrightReset:
                        currentForeground = Colors.White;
                        break;

                    case GraphicRendition.BackgroundBrightBlack:
                        currentBackground = Colors.Gray;
                        break;

                    case GraphicRendition.BackgroundBrightRed:
                        currentBackground = Colors.Red;
                        break;

                    case GraphicRendition.BackgroundBrightGreen:
                        currentBackground = Colors.LightGreen;
                        break;

                    case GraphicRendition.BackgroundBrightYellow:
                        currentBackground = Colors.LightYellow;
                        break;

                    case GraphicRendition.BackgroundBrightBlue:
                        currentBackground = Colors.LightBlue;
                        break;

                    case GraphicRendition.BackgroundBrightMagenta:
                        currentBackground = Colors.DarkMagenta;
                        break;

                    case GraphicRendition.BackgroundBrightCyan:
                        currentBackground = Colors.LightCyan;
                        break;

                    case GraphicRendition.BackgroundBrightWhite:
                        currentBackground = Colors.Gray;
                        break;

                    case GraphicRendition.BackgroundBrightReset:
                        currentBackground = Colors.Black;
                        break;
                }
            }
        }

        OuterView?.SetGraphicRendition(sender, commands);
    }

    public void ModeChanged(ITerminalDecoder sender, AnsiMode mode)
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
        return CursorPosition.Y * Buffer.GridSize.Width + CursorPosition.X;
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

        if (endIndex >= Buffer.Length)
            endIndex = Buffer.Length - 1;

        if (startIndex > endIndex)
            return;
        
        for (int i = startIndex; i <= endIndex; i++)
        {
            Buffer[i].Character = ' ';
            Buffer[i].Reset();
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

    public void BufferChanged(ITerminalDecoder sender, TerminalScreenBuffer buffer) => throw new NotImplementedException();
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