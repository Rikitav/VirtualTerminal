using System.Windows;

namespace VirtualTerminal.Engine.Components;

public interface ITerminalScreenView
{
    void Characters(IBufferedDecoder sender, ReadOnlySpan<char> chars);
    void SaveCursor(IBufferedDecoder sernder);
    void RestoreCursor(IBufferedDecoder sender);
    Size GetSize(IBufferedDecoder sender);
    void MoveCursor(IBufferedDecoder sender, Direction _direction, int amount);
    void MoveCursorToBeginningOfLineBelow(IBufferedDecoder sender, int lineNumberRelativeToCurrentLine);
    void MoveCursorToBeginningOfLineAbove(IBufferedDecoder sender, int lineNumberRelativeToCurrentLine);
    void MoveCursorToColumn(IBufferedDecoder sender, int columnNumber);
    void MoveCursorTo(IBufferedDecoder sender, Coord position);
    void ClearScreen(IBufferedDecoder sender, ClearDirection direction);
    void ClearLine(IBufferedDecoder sender, ClearDirection _direction);
    void ScrollPageUpwards(IBufferedDecoder sender, int linesToScroll);
    void ScrollPageDownwards(IBufferedDecoder sender, int linesToScroll);
    Coord GetCursorPosition(IBufferedDecoder sender);
    void SetGraphicRendition(IBufferedDecoder sender, GraphicRendition[] commands);
    void ModeChanged(IBufferedDecoder sender, AnsiMode mode);
}
