using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;

namespace VirtualTerminal.Automation;

/// <summary>
/// A UI Automation text range over a snapshot of the terminal's scrollback + visible buffer.
/// This is a read-only range: selection methods are no-ops.
/// </summary>
internal sealed class TerminalTextRangeProvider : ITextRangeProvider
{
    private readonly TerminalControlAutomationPeer _peer;
    private readonly string _text;
    private int _start;
    private int _end;

    public TerminalTextRangeProvider(TerminalControlAutomationPeer peer, string text, int start, int end)
    {
        _peer = peer;
        _text = text;
        _start = Math.Clamp(start, 0, text.Length);
        _end = Math.Clamp(Math.Max(start, end), 0, text.Length);
    }

    public ITextRangeProvider Clone()
        => new TerminalTextRangeProvider(_peer, _text, _start, _end);

    public bool Compare(ITextRangeProvider range)
    {
        if (range is not TerminalTextRangeProvider other)
            return false;

        return ReferenceEquals(_peer, other._peer)
            && _text == other._text
            && _start == other._start
            && _end == other._end;
    }

    public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not TerminalTextRangeProvider other)
            throw new ArgumentException("Range must belong to the same terminal peer.", nameof(targetRange));

        int thisPoint = endpoint == TextPatternRangeEndpoint.Start ? _start : _end;
        int otherPoint = targetEndpoint == TextPatternRangeEndpoint.Start ? other._start : other._end;
        return thisPoint.CompareTo(otherPoint);
    }

    public void ExpandToEnclosingUnit(TextUnit unit)
    {
        switch (unit)
        {
            case TextUnit.Character:
                break;

            case TextUnit.Word:
                _start = MoveToWordBoundary(_start, -1);
                _end = MoveToWordBoundary(_end, 1);
                break;

            case TextUnit.Line:
            case TextUnit.Paragraph:
                _start = StartOfLine(_start);
                _end = EndOfLine(_end);
                break;

            case TextUnit.Document:
            default:
                _start = 0;
                _end = _text.Length;
                break;
        }
    }

    public ITextRangeProvider? FindAttribute(int attribute, object? value, bool backward)
        => null;

    public ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int searchLength = _end - _start;
        if (searchLength <= 0)
            return null;

        if (backward)
        {
            int startIndex = Math.Max(_start, _end - text.Length);
            int idx = _text.LastIndexOf(text, startIndex, startIndex - _start + text.Length, comparison);
            if (idx < 0)
                return null;

            return new TerminalTextRangeProvider(_peer, _text, idx, idx + text.Length);
        }
        else
        {
            int idx = _text.IndexOf(text, _start, searchLength, comparison);
            if (idx < 0)
                return null;

            return new TerminalTextRangeProvider(_peer, _text, idx, idx + text.Length);
        }
    }

    public object? GetAttributeValue(int attribute)
        => null;

    public double[] GetBoundingRectangles()
        => [];

    public IRawElementProviderSimple[] GetChildren()
        => [];

    public IRawElementProviderSimple GetEnclosingElement()
        => _peer.GetRawProvider();

    public string GetText(int maxLength)
    {
        int length = _end - _start;
        if (maxLength >= 0)
            length = Math.Min(length, maxLength);

        return length == 0 ? string.Empty : _text.Substring(_start, length);
    }

    public int Move(TextUnit unit, int count)
    {
        if (count == 0)
            return 0;

        int startMoved = MoveEndpointByUnitImpl(ref _start, unit, count, out bool startHitBoundary);
        MoveEndpointByUnitImpl(ref _end, unit, count, out bool endHitBoundary);

        if (unit == TextUnit.Document && count != 0)
        {
            _start = count < 0 ? 0 : _text.Length;
            _end = _start;
            return startHitBoundary || endHitBoundary ? 0 : 1;
        }

        return startMoved;
    }

    public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
    {
        if (count == 0)
            return 0;

        int moved;
        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            moved = MoveEndpointByUnitImpl(ref _start, unit, count, out _);
            _end = Math.Max(_end, _start);
        }
        else
        {
            moved = MoveEndpointByUnitImpl(ref _end, unit, count, out _);
            _start = Math.Min(_start, _end);
        }

        return moved;
    }

    public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not TerminalTextRangeProvider other)
            throw new ArgumentException("Range must belong to the same terminal peer.", nameof(targetRange));

        int targetPoint = targetEndpoint == TextPatternRangeEndpoint.Start ? other._start : other._end;

        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            _start = targetPoint;
            if (_start > _end)
                _end = _start;
        }
        else
        {
            _end = targetPoint;
            if (_end < _start)
                _start = _end;
        }
    }

    public void Select()
    {
    }

    public void AddToSelection()
    {
    }

    public void RemoveFromSelection()
    {
    }

    public void ScrollIntoView(bool alignToTop)
    {
    }

    private int MoveEndpointByUnitImpl(ref int endpoint, TextUnit unit, int count, out bool hitBoundary)
    {
        hitBoundary = false;
        int direction = Math.Sign(count);
        int remaining = Math.Abs(count);
        int moved = 0;

        while (remaining > 0)
        {
            int next = unit switch
            {
                TextUnit.Character => endpoint + direction,
                TextUnit.Word => MoveToWordBoundary(endpoint, direction),
                TextUnit.Line or TextUnit.Paragraph => MoveToLineBoundary(endpoint, direction),
                TextUnit.Document => direction < 0 ? 0 : _text.Length,
                _ => endpoint + direction,
            };

            if (next == endpoint)
            {
                hitBoundary = true;
                break;
            }

            endpoint = next;
            moved++;
            remaining--;
        }

        endpoint = Math.Clamp(endpoint, 0, _text.Length);
        return moved * direction;
    }

    private int StartOfLine(int position)
    {
        if (position <= 0)
            return 0;

        int idx = _text.LastIndexOf('\n', position - 1);
        return idx < 0 ? 0 : idx + 1;
    }

    private int EndOfLine(int position)
    {
        if (position >= _text.Length)
            return _text.Length;

        int idx = _text.IndexOf('\n', position);
        return idx < 0 ? _text.Length : idx + 1;
    }

    private int MoveToLineBoundary(int position, int direction)
    {
        if (direction < 0)
        {
            if (position <= 0)
                return 0;

            int idx = _text.LastIndexOf('\n', position - 1);
            return idx < 0 ? 0 : idx + 1;
        }
        else
        {
            if (position >= _text.Length)
                return _text.Length;

            int idx = _text.IndexOf('\n', position);
            return idx < 0 ? _text.Length : idx + 1;
        }
    }

    private int MoveToWordBoundary(int position, int direction)
    {
        if (direction < 0)
        {
            while (position > 0 && IsWordChar(_text[position - 1]))
                position--;

            while (position > 0 && char.IsWhiteSpace(_text[position - 1]))
                position--;

            return position;
        }
        else
        {
            while (position < _text.Length && char.IsWhiteSpace(_text[position]))
                position++;

            while (position < _text.Length && IsWordChar(_text[position]))
                position++;

            return position;
        }
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';
}
