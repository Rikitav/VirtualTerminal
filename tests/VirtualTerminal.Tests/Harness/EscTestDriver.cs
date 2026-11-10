using System.Text;
using VirtualTerminal.Buffer;
using VirtualTerminal.Model;
using Xunit;

namespace VirtualTerminal.Tests.Harness;

/// <summary>
/// Conformance driver for executing VT/ANSI escape sequences and asserting
/// against screen buffer state and terminal responses, modelled after Thomas Dickey's esctest2.
/// </summary>
public sealed class EscTestDriver
{
    private readonly StringBuilder _responseBuffer = new();
    private readonly object _responseLock = new();

    public TerminalScreenBuffer Buffer { get; }
    public TerminalDecoder Decoder { get; }

    public int Columns => Buffer.Columns;
    public int Rows => Buffer.Rows;

    /// <summary>Current 1-based cursor row (matching VT/DEC 1-based indexing).</summary>
    public int CursorRow => Decoder.State.CursorY + 1;

    /// <summary>Current 1-based cursor column (matching VT/DEC 1-based indexing).</summary>
    public int CursorCol => Decoder.State.CursorX + 1;

    public EscTestDriver(int columns = 80, int rows = 24, int maxScrollback = 1000)
    {
        Buffer = new TerminalScreenBuffer(columns, rows, maxScrollback);
        Decoder = new TerminalDecoder(Buffer);
        Decoder.SendOutput = OnSendOutput;
    }

    private void OnSendOutput(ReadOnlySpan<byte> bytes)
    {
        lock (_responseLock)
        {
            _responseBuffer.Append(Encoding.UTF8.GetString(bytes));
        }
    }

    /// <summary>Sends a raw escape or text sequence to the terminal decoder.</summary>
    public EscTestDriver Write(string sequence)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);
        Decoder.Write(bytes);
        return this;
    }

    /// <summary>Sends raw bytes to the terminal decoder.</summary>
    public EscTestDriver Write(ReadOnlySpan<byte> bytes)
    {
        Decoder.Write(bytes);
        return this;
    }

    /// <summary>Retrieves all response bytes sent by the terminal since the last check, clearing the response buffer.</summary>
    public string ReadResponse()
    {
        lock (_responseLock)
        {
            string resp = _responseBuffer.ToString();
            _responseBuffer.Clear();
            return resp;
        }
    }

    /// <summary>Asserts that the terminal emitted the expected response string.</summary>
    public void AssertResponse(string expectedResponse)
    {
        string actual = ReadResponse();
        Assert.Equal(expectedResponse, actual);
    }

    /// <summary>Asserts that the terminal emitted a response containing the expected substring.</summary>
    public void AssertResponseContains(string expectedSubstring)
    {
        string actual = ReadResponse();
        Assert.Contains(expectedSubstring, actual);
    }

    /// <summary>Asserts that the cursor is located at the specified 1-based (row, col) coordinates.</summary>
    public void AssertCursor(int expectedRow, int expectedCol)
    {
        Assert.True(
            CursorRow == expectedRow && CursorCol == expectedCol,
            $"Expected cursor at ({expectedRow}, {expectedCol}), but was at ({CursorRow}, {CursorCol}).");
    }

    /// <summary>Retrieves the character text for the given 1-based row.</summary>
    public string GetLineText(int row, bool trimTrailing = true)
    {
        if (row < 1 || row > Rows)
            throw new ArgumentOutOfRangeException(nameof(row));

        Span<TerminalCellInfo> cells = Buffer.GetRow(row - 1);
        char[] chars = new char[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            chars[i] = (char)cells[i].Character.Value;
        }

        string line = new(chars);
        return trimTrailing ? line.TrimEnd() : line;
    }

    /// <summary>Retrieves a cell by 1-based (row, col) coordinates.</summary>
    public TerminalCellInfo GetCell(int row, int col)
    {
        if (row < 1 || row > Rows)
            throw new ArgumentOutOfRangeException(nameof(row));
        if (col < 1 || col > Columns)
            throw new ArgumentOutOfRangeException(nameof(col));

        return Buffer.GetCell(row - 1, col - 1);
    }

    /// <summary>Asserts that a 1-based row matches the expected string.</summary>
    public void AssertLine(int row, string expectedText, bool trimTrailing = true)
    {
        string actual = GetLineText(row, trimTrailing);
        Assert.Equal(expectedText, actual);
    }

    /// <summary>Asserts that the screen starting from row 1 matches the provided expected lines.</summary>
    public void AssertScreen(params string[] expectedLines)
    {
        for (int i = 0; i < expectedLines.Length; i++)
        {
            int row = i + 1;
            string actual = GetLineText(row, trimTrailing: true);
            Assert.True(
                actual == expectedLines[i],
                $"Row {row} mismatch.\nExpected: \"{expectedLines[i]}\"\nActual:   \"{actual}\"");
        }
    }
}
