using VirtualTerminal.Buffer;
using VirtualTerminal.Model;
using VirtualTerminal.Tests.Harness;
using Xunit;

namespace VirtualTerminal.Tests;

/// <summary>
/// Conformance and compliance test suite for VT/ANSI emulation,
/// ported from https://github.com/ThomasDickey/esctest2.
/// </summary>
public class EscTest2ConformanceTests
{
    // ==========================================
    // 1. Cursor Positioning (CUP, HVP, CHA, VPA)
    // ==========================================

    [Fact]
    public void Test_CUP_DefaultParameters_MovesToTopLeft()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[10;15H");
        t.AssertCursor(10, 15);

        t.Write("\x1b[H"); // CUP with default params -> (1, 1)
        t.AssertCursor(1, 1);
    }

    [Fact]
    public void Test_CUP_ZeroTreatedAsOne()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[0;0H");
        t.AssertCursor(1, 1);

        t.Write("\x1b[0;5H");
        t.AssertCursor(1, 5);

        t.Write("\x1b[7;0H");
        t.AssertCursor(7, 1);
    }

    [Fact]
    public void Test_CUP_ClampsToScreenDimensions()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[999;999H");
        t.AssertCursor(24, 80);
    }

    [Fact]
    public void Test_HVP_HorizontalAndVerticalPositioning()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[5;12f");
        t.AssertCursor(5, 12);
    }

    [Fact]
    public void Test_CHA_CursorHorizontalAbsolute()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[5;5H");
        t.Write("\x1b[20G");
        t.AssertCursor(5, 20);

        t.Write("\x1b[0G"); // 0 treated as 1
        t.AssertCursor(5, 1);

        t.Write("\x1b[999G"); // Clamps to Columns
        t.AssertCursor(5, 80);
    }

    [Fact]
    public void Test_VPA_VerticalPositionAbsolute()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[5;10H");
        t.Write("\x1b[15d");
        t.AssertCursor(15, 10);

        t.Write("\x1b[0d"); // 0 treated as 1
        t.AssertCursor(1, 10);

        t.Write("\x1b[999d"); // Clamps to Rows
        t.AssertCursor(24, 10);
    }

    // ==========================================
    // 2. Cursor Relative Movement (CUU, CUD, CUF, CUB, CNL, CPL)
    // ==========================================

    [Fact]
    public void Test_CursorRelativeMoves_ClampedToBoundaries()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[5;5H");

        t.Write("\x1b[2A"); // Up 2
        t.AssertCursor(3, 5);

        t.Write("\x1b[4B"); // Down 4
        t.AssertCursor(7, 5);

        t.Write("\x1b[3C"); // Forward 3
        t.AssertCursor(7, 8);

        t.Write("\x1b[5D"); // Back 5
        t.AssertCursor(7, 3);

        t.Write("\x1b[100A"); // Up beyond top -> clamps to 1
        t.AssertCursor(1, 3);

        t.Write("\x1b[100D"); // Back beyond left -> clamps to 1
        t.AssertCursor(1, 1);
    }

    [Fact]
    public void Test_CNL_And_CPL_NextAndPreviousLine()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[5;10H");

        t.Write("\x1b[2E"); // Down 2 lines, column 1
        t.AssertCursor(7, 1);

        t.Write("\x1b[3F"); // Up 3 lines, column 1
        t.AssertCursor(4, 1);
    }

    // ==========================================
    // 3. Erase in Display (ED) & Erase in Line (EL)
    // ==========================================

    [Fact]
    public void Test_ED_Mode0_CursorToEndOfScreen()
    {
        EscTestDriver t = new(10, 4);
        // Fill 4 lines with 'X'
        t.Write("XXXXXXXXXX\r\nXXXXXXXXXX\r\nXXXXXXXXXX\r\nXXXXXXXXXX");
        t.Write("\x1b[2;6H"); // Row 2, Col 6
        t.Write("\x1b[0J"); // Clear from cursor to end

        t.AssertLine(1, "XXXXXXXXXX");
        t.AssertLine(2, "XXXXX");
        t.AssertLine(3, "");
        t.AssertLine(4, "");
    }

    [Fact]
    public void Test_ED_Mode1_StartOfScreenToCursor()
    {
        EscTestDriver t = new(10, 4);
        t.Write("XXXXXXXXXX\r\nXXXXXXXXXX\r\nXXXXXXXXXX\r\nXXXXXXXXXX");
        t.Write("\x1b[2;5H"); // Row 2, Col 5
        t.Write("\x1b[1J"); // Clear from start of screen to cursor

        t.AssertLine(1, "");
        t.AssertLine(2, "     XXXXX", trimTrailing: false);
        t.AssertLine(3, "XXXXXXXXXX");
        t.AssertLine(4, "XXXXXXXXXX");
    }

    [Fact]
    public void Test_ED_Mode2_EntireScreen()
    {
        EscTestDriver t = new(10, 3);
        t.Write("AAAAAAAAAA\r\nBBBBBBBBBB\r\nCCCCCCCCCC");
        t.Write("\x1b[2;5H");
        t.Write("\x1b[2J");

        t.AssertLine(1, "");
        t.AssertLine(2, "");
        t.AssertLine(3, "");
        // ED 2 does not move the cursor
        t.AssertCursor(2, 5);
    }

    [Fact]
    public void Test_EL_Modes()
    {
        EscTestDriver t = new(10, 3);
        t.Write("0123456789\r\n0123456789\r\n0123456789");

        // EL 0: Cursor to end of line
        t.Write("\x1b[1;5H\x1b[0K");
        t.AssertLine(1, "0123");

        // EL 1: Start of line to cursor (inclusive)
        t.Write("\x1b[2;5H\x1b[1K");
        t.AssertLine(2, "     56789", trimTrailing: false);

        // EL 2: Entire line
        t.Write("\x1b[3;5H\x1b[2K");
        t.AssertLine(3, "");
    }

    // ==========================================
    // 4. Character & Line Insert / Delete (ICH, DCH, IL, DL, ECH)
    // ==========================================

    [Fact]
    public void Test_ICH_InsertCharacters()
    {
        EscTestDriver t = new(10, 2);
        t.Write("ABCDEF");
        t.Write("\x1b[1;3H"); // Cursor at 'C' (col 3)
        t.Write("\x1b[2@"); // Insert 2 blanks

        t.AssertLine(1, "AB  CDEF");
        t.AssertCursor(1, 3);
    }

    [Fact]
    public void Test_DCH_DeleteCharacters()
    {
        EscTestDriver t = new(10, 2);
        t.Write("ABCDEFGHIJ");
        t.Write("\x1b[1;3H"); // Cursor at 'C' (col 3)
        t.Write("\x1b[3P"); // Delete 3 characters ('CDE')

        t.AssertLine(1, "ABFGHIJ");
        t.AssertCursor(1, 3);
    }

    [Fact]
    public void Test_ECH_EraseCharacters()
    {
        EscTestDriver t = new(10, 2);
        t.Write("ABCDEFGHIJ");
        t.Write("\x1b[1;3H");
        t.Write("\x1b[4X"); // Erase 4 characters without shifting

        t.AssertLine(1, "AB    GHIJ");
        t.AssertCursor(1, 3);
    }

    [Fact]
    public void Test_IL_InsertLines()
    {
        EscTestDriver t = new(10, 4);
        t.Write("Line 1\r\nLine 2\r\nLine 3\r\nLine 4");
        t.Write("\x1b[2;1H");
        t.Write("\x1b[1L"); // Insert 1 line at row 2

        t.AssertLine(1, "Line 1");
        t.AssertLine(2, "");
        t.AssertLine(3, "Line 2");
        t.AssertLine(4, "Line 3");
    }

    [Fact]
    public void Test_DL_DeleteLines()
    {
        EscTestDriver t = new(10, 4);
        t.Write("Line 1\r\nLine 2\r\nLine 3\r\nLine 4");
        t.Write("\x1b[2;1H");
        t.Write("\x1b[1M"); // Delete 1 line at row 2

        t.AssertLine(1, "Line 1");
        t.AssertLine(2, "Line 3");
        t.AssertLine(3, "Line 4");
        t.AssertLine(4, "");
    }

    // ==========================================
    // 5. Terminal Reports & Queries (DSR, CPR, DA)
    // ==========================================

    [Fact]
    public void Test_DSR_CursorPositionReport()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[12;34H");
        t.Write("\x1b[6n"); // DSR CPR request

        t.AssertResponse("\x1b[12;34R");
    }

    [Fact]
    public void Test_DSR_StatusReport()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[5n"); // Device status inquiry

        t.AssertResponse("\x1b[0n"); // Response: Terminal OK
    }

    [Fact]
    public void Test_DA_PrimaryDeviceAttributes()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[c"); // Primary DA

        t.AssertResponseContains("\x1b[?");
    }

    // ==========================================
    // 6. Scrolling Margins (DECSTBM & DECSLRM)
    // ==========================================

    [Fact]
    public void Test_DECSTBM_RestrictsScrolling()
    {
        EscTestDriver t = new(10, 5);
        t.Write("Row 1\r\nRow 2\r\nRow 3\r\nRow 4\r\nRow 5");

        // Set top/bottom margin to rows 2..4 (1-based)
        t.Write("\x1b[2;4r");
        // DECSTBM homes cursor to (1, 1)
        t.AssertCursor(1, 1);

        // Position cursor at row 4 and feed newline to scroll within margins
        t.Write("\x1b[4;1H\r\nNEW");

        // Row 1 and Row 5 must remain unchanged
        t.AssertLine(1, "Row 1");
        t.AssertLine(2, "Row 3");
        t.AssertLine(3, "Row 4");
        t.AssertLine(4, "NEW");
        t.AssertLine(5, "Row 5");
    }

    [Fact]
    public void Test_DECOM_OriginModeRespectsMargins()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[5;15r"); // Set vertical margin 5..15
        t.Write("\x1b[?6h");   // Enable Origin Mode (DECOM)

        // With DECOM active, (1, 1) refers to (MarginTop, 1) = (5, 1)
        t.AssertCursor(5, 1);

        t.Write("\x1b[2;3H"); // Row 2 inside margins = row 5 + 1 = 6
        t.AssertCursor(6, 3);

        t.Write("\x1b[?6l");   // Disable Origin Mode
        t.Write("\x1b[1;1H");
        t.AssertCursor(1, 1);
    }

    [Fact]
    public void Test_DECSLRM_LeftRightMargins_And_OriginMode()
    {
        EscTestDriver t = new(80, 24);
        t.Write("\x1b[?69h");  // Enable DECLRMM (Left/Right Margin Mode)
        t.Write("\x1b[10;20s"); // Set left margin 10, right margin 20
        t.Write("\x1b[5;15r");  // Set top margin 5, bottom margin 15
        t.Write("\x1b[?6h");    // Enable Origin Mode (DECOM)

        // Cursor homes to (TopMargin, LeftMargin) = (5, 10)
        t.AssertCursor(5, 10);

        // Move to (2, 3) relative to margins -> row 5 + 1 = 6, col 10 + 2 = 12
        t.Write("\x1b[2;3H");
        t.AssertCursor(6, 12);

        // CPR inquiry (\x1b[6n) must report relative to origin in DECOM
        t.Write("\x1b[6n");
        t.AssertResponse("\x1b[2;3R");
    }

    [Fact]
    public void Test_DECSLRM_RectangularScrolling()
    {
        EscTestDriver t = new(10, 5);
        t.Write("0123456789\r\n0123456789\r\n0123456789\r\n0123456789\r\n0123456789");

        t.Write("\x1b[?69h");  // Enable DECLRMM
        t.Write("\x1b[2;4r");   // Vertical margin: rows 2..4
        t.Write("\x1b[3;8s");   // Horizontal margin: cols 3..8

        // Scroll up 1 line
        t.Write("\x1b[1S");

        // Row 1 and 5 untouched
        t.AssertLine(1, "0123456789");
        t.AssertLine(5, "0123456789");

        // Row 2..4 columns 1..2 and 9..10 untouched
        Assert.Equal("01", t.GetLineText(2).Substring(0, 2));
        Assert.Equal("89", t.GetLineText(2).Substring(8, 2));
    }
}
