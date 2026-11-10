using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualTerminal.Buffer;
using VirtualTerminal.Enums;
using VirtualTerminal.Input;
using VirtualTerminal.Model;

namespace VirtualTerminal.Serialization;

/// <summary>
/// Provides high-fidelity serialization and restoration of terminal screen buffers,
/// cursor positions, modes, styles, and scrollback history in binary and JSON formats.
/// </summary>
public static class TerminalSnapshotSerializer
{
    private const uint Magic = 0x53535456; // "VTSS"
    private const byte Version = 1;

    #region Binary Serialization

    /// <summary>Serializes the terminal buffer and state into a byte array in compact binary format.</summary>
    public static byte[] SerializeBinary(TerminalScreenBuffer buffer, TerminalState state)
    {
        using MemoryStream ms = new();
        SerializeBinary(buffer, state, ms);
        return ms.ToArray();
    }

    /// <summary>Serializes the terminal buffer and state into the specified stream in compact binary format.</summary>
    public static void SerializeBinary(TerminalScreenBuffer buffer, TerminalState state, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stream);

        lock (buffer.SyncRoot)
        {
            using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(Magic);
            writer.Write(Version);

            // Buffer geometry
            writer.Write(buffer.Columns);
            writer.Write(buffer.Rows);
            writer.Write(buffer.ScrollbackMax);
            writer.Write(buffer.IsAlternate);
            writer.Write(buffer.ScrollTop);
            writer.Write(buffer.ScrollBottom);
            writer.Write(buffer.ScrollLeft);
            writer.Write(buffer.ScrollRight);

            // Primary grid
            for (int y = 0; y < buffer.Rows; y++)
            {
                TerminalRow row = buffer.GetPrimaryRow(y);
                writer.Write(row.Wrapped);
                for (int x = 0; x < buffer.Columns; x++)
                {
                    WriteCell(writer, row[x]);
                }
            }

            // Alternate grid
            if (buffer.IsAlternate)
            {
                for (int y = 0; y < buffer.Rows; y++)
                {
                    TerminalRow row = buffer.GetAltRow(y) ?? buffer.GetRowObject(y);
                    writer.Write(row.Wrapped);
                    for (int x = 0; x < buffer.Columns; x++)
                    {
                        WriteCell(writer, row[x]);
                    }
                }
            }

            // Scrollback
            var scrollback = buffer.GetScrollback();
            writer.Write(scrollback.Count);
            foreach (TerminalRow row in scrollback)
            {
                writer.Write(row.Length);
                writer.Write(row.Wrapped);
                for (int x = 0; x < row.Length; x++)
                {
                    WriteCell(writer, row[x]);
                }
            }

            // State
            WriteState(writer, state);
        }
    }

    /// <summary>Deserializes terminal buffer and state from a byte span in binary format.</summary>
    public static (TerminalScreenBuffer Buffer, TerminalState State) DeserializeBinary(ReadOnlySpan<byte> data)
    {
        using MemoryStream ms = new(data.ToArray());
        return DeserializeBinary(ms);
    }

    /// <summary>Deserializes terminal buffer and state from a stream in binary format.</summary>
    public static (TerminalScreenBuffer Buffer, TerminalState State) DeserializeBinary(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        uint magic = reader.ReadUInt32();
        if (magic != Magic)
            throw aerial_InvalidData("Invalid VT snapshot magic header.");

        byte version = reader.ReadByte();
        if (version != Version)
            throw aerial_InvalidData($"Unsupported snapshot version: {version}.");

        int columns = reader.ReadInt32();
        int rows = reader.ReadInt32();
        int scrollbackMax = reader.ReadInt32();
        bool isAlt = reader.ReadBoolean();
        int scrollTop = reader.ReadInt32();
        int scrollBottom = reader.ReadInt32();
        int scrollLeft = reader.ReadInt32();
        int scrollRight = reader.ReadInt32();

        TerminalScreenBuffer buffer = new(columns, rows, scrollbackMax);
        buffer.SetScrollRegion(scrollTop, scrollBottom);
        buffer.SetHorizontalScrollRegion(scrollLeft, scrollRight);

        // Read primary grid
        for (int y = 0; y < rows; y++)
        {
            TerminalRow row = buffer.GetPrimaryRow(y);
            row.Wrapped = reader.ReadBoolean();
            for (int x = 0; x < columns; x++)
            {
                row[x] = ReadCell(reader);
            }
        }

        // Read alternate grid
        if (isAlt)
        {
            buffer.EnterAlternateScreen();
            for (int y = 0; y < rows; y++)
            {
                TerminalRow row = buffer.GetRowObject(y);
                row.Wrapped = reader.ReadBoolean();
                for (int x = 0; x < columns; x++)
                {
                    row[x] = ReadCell(reader);
                }
            }
        }

        // Read scrollback
        int scrollbackCount = reader.ReadInt32();
        buffer.ClearScrollback();
        for (int i = 0; i < scrollbackCount; i++)
        {
            int rowLength = reader.ReadInt32();
            bool wrapped = reader.ReadBoolean();
            TerminalRow sbRow = new(rowLength) { Wrapped = wrapped };
            for (int x = 0; x < rowLength; x++)
            {
                sbRow[x] = ReadCell(reader);
            }
            buffer.PushScrollback(sbRow);
        }

        TerminalState state = ReadState(reader);
        return (buffer, state);
    }

    /// <summary>Restores an existing buffer and state in-place from a binary snapshot span.</summary>
    public static void RestoreBinary(ReadOnlySpan<byte> data, TerminalScreenBuffer targetBuffer, TerminalState targetState)
    {
        var (deserializedBuffer, deserializedState) = DeserializeBinary(data);
        ApplyBufferAndState(deserializedBuffer, deserializedState, targetBuffer, targetState);
    }

    #endregion

    #region JSON Serialization

    /// <summary>Serializes the terminal buffer and state into a human-readable JSON string.</summary>
    public static string SerializeJson(TerminalScreenBuffer buffer, TerminalState state, bool indented = false)
    {
        TerminalSnapshotModel model = ToModel(buffer, state);
        JsonSerializerOptions options = indented
            ? new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = TerminalSnapshotJsonContext.Default }
            : TerminalSnapshotJsonContext.Default.Options;

        return JsonSerializer.Serialize(model, typeof(TerminalSnapshotModel), options);
    }

    /// <summary>Deserializes terminal buffer and state from a JSON string.</summary>
    public static (TerminalScreenBuffer Buffer, TerminalState State) DeserializeJson(string json)
    {
        TerminalSnapshotModel? model = (TerminalSnapshotModel?)JsonSerializer.Deserialize(
            json,
            typeof(TerminalSnapshotModel),
            TerminalSnapshotJsonContext.Default);

        if (model == null)
            throw aerial_InvalidData("Failed to parse JSON terminal snapshot.");

        return FromModel(model);
    }

    /// <summary>Restores an existing buffer and state in-place from a JSON string.</summary>
    public static void RestoreJson(string json, TerminalScreenBuffer targetBuffer, TerminalState targetState)
    {
        var (deserializedBuffer, deserializedState) = DeserializeJson(json);
        ApplyBufferAndState(deserializedBuffer, deserializedState, targetBuffer, targetState);
    }

    #endregion

    #region Model Conversion Helpers

    private static TerminalSnapshotModel ToModel(TerminalScreenBuffer buffer, TerminalState state)
    {
        lock (buffer.SyncRoot)
        {
            TerminalSnapshotModel model = new()
            {
                Columns = buffer.Columns,
                Rows = buffer.Rows,
                ScrollbackMax = buffer.ScrollbackMax,
                IsAlternate = buffer.IsAlternate,
                ScrollTop = buffer.ScrollTop,
                ScrollBottom = buffer.ScrollBottom,
                ScrollLeft = buffer.ScrollLeft,
                ScrollRight = buffer.ScrollRight,
                State = ToStateModel(state),
            };

            for (int y = 0; y < buffer.Rows; y++)
            {
                TerminalRow row = buffer.GetPrimaryRow(y);
                model.PrimaryGrid.Add(ToRowModel(row));
            }

            if (buffer.IsAlternate)
            {
                model.AltGrid = new List<RowSnapshotModel>(buffer.Rows);
                for (int y = 0; y < buffer.Rows; y++)
                {
                    TerminalRow row = buffer.GetAltRow(y) ?? buffer.GetRowObject(y);
                    model.AltGrid.Add(ToRowModel(row));
                }
            }

            foreach (TerminalRow row in buffer.GetScrollback())
            {
                model.Scrollback.Add(ToRowModel(row));
            }

            return model;
        }
    }

    private static (TerminalScreenBuffer Buffer, TerminalState State) FromModel(TerminalSnapshotModel model)
    {
        TerminalScreenBuffer buffer = new(model.Columns, model.Rows, model.ScrollbackMax);
        buffer.SetScrollRegion(model.ScrollTop, model.ScrollBottom);
        buffer.SetHorizontalScrollRegion(model.ScrollLeft, model.ScrollRight);

        for (int y = 0; y < Math.Min(model.Rows, model.PrimaryGrid.Count); y++)
        {
            TerminalRow targetRow = buffer.GetPrimaryRow(y);
            ApplyRowModel(model.PrimaryGrid[y], targetRow);
        }

        if (model.IsAlternate)
        {
            buffer.EnterAlternateScreen();
            if (model.AltGrid != null)
            {
                for (int y = 0; y < Math.Min(model.Rows, model.AltGrid.Count); y++)
                {
                    TerminalRow targetRow = buffer.GetRowObject(y);
                    ApplyRowModel(model.AltGrid[y], targetRow);
                }
            }
        }

        buffer.ClearScrollback();
        foreach (RowSnapshotModel rowModel in model.Scrollback)
        {
            TerminalRow sbRow = new(rowModel.Cells.Count) { Wrapped = rowModel.Wrapped };
            for (int x = 0; x < rowModel.Cells.Count; x++)
            {
                sbRow[x] = FromCellModel(rowModel.Cells[x]);
            }
            buffer.PushScrollback(sbRow);
        }

        TerminalState state = FromStateModel(model.State);
        return (buffer, state);
    }

    private static RowSnapshotModel ToRowModel(TerminalRow row)
    {
        RowSnapshotModel rowModel = new() { Wrapped = row.Wrapped };
        for (int x = 0; x < row.Length; x++)
        {
            TerminalCellInfo cell = row[x];
            rowModel.Cells.Add(new CellSnapshotModel
            {
                Rune = cell.Character.Value,
                Fg = cell.Style.Foreground.ToArgb(),
                Bg = cell.Style.Background.ToArgb(),
                Ul = cell.Style.UnderlineColor.ToArgb(),
                Flags = (uint)cell.Style.Flags,
                Underline = (byte)cell.Style.UnderlineStyle,
                Hyperlink = cell.Style.HyperlinkId,
            });
        }
        return rowModel;
    }

    private static void ApplyRowModel(RowSnapshotModel rowModel, TerminalRow targetRow)
    {
        targetRow.Wrapped = rowModel.Wrapped;
        targetRow.Dirty = true;
        int count = Math.Min(targetRow.Length, rowModel.Cells.Count);
        for (int x = 0; x < count; x++)
        {
            targetRow[x] = FromCellModel(rowModel.Cells[x]);
        }
    }

    private static TerminalCellInfo FromCellModel(CellSnapshotModel m)
    {
        CellRenderStyle style = new()
        {
            Foreground = TerminalColor.FromArgb(unchecked((int)m.Fg)),
            Background = TerminalColor.FromArgb(unchecked((int)m.Bg)),
            UnderlineColor = TerminalColor.FromArgb(unchecked((int)m.Ul)),
            Flags = (CellStyleFlags)m.Flags,
            UnderlineStyle = (Underline)m.Underline,
            HyperlinkId = m.Hyperlink,
        };
        return new TerminalCellInfo(new Rune(m.Rune), style);
    }

    private static StateSnapshotModel ToStateModel(TerminalState state)
    {
        return new StateSnapshotModel
        {
            CursorX = state.CursorX,
            CursorY = state.CursorY,
            WrapPending = state.WrapPending,
            SavedCursorX = state.SavedCursorX,
            SavedCursorY = state.SavedCursorY,
            SavedOriginMode = state.SavedOriginMode,
            SavedWrapPending = state.SavedWrapPending,
            Modes = new ModesSnapshotModel
            {
                AutoWrap = state.Modes.AutoWrap,
                OriginMode = state.Modes.OriginMode,
                ApplicationCursorKeys = state.Modes.ApplicationCursorKeys,
                InsertMode = state.Modes.InsertMode,
                CursorBlink = state.Modes.CursorBlink,
                CursorVisible = state.Modes.CursorVisible,
                ReverseVideo = state.Modes.ReverseVideo,
                ApplicationKeypad = state.Modes.ApplicationKeypad,
                BackspaceSendsControlH = state.Modes.BackspaceSendsControlH,
                LeftRightMarginMode = state.Modes.LeftRightMarginMode,
                LineFeedNewLine = state.Modes.LineFeedNewLine,
                BracketedPaste = state.Modes.BracketedPaste,
                SynchronizedOutput = state.Modes.SynchronizedOutput,
                FocusReporting = state.Modes.FocusReporting,
                MouseTracking = (int)state.Modes.MouseTracking,
                SgrMouseEncoding = state.Modes.SgrMouseEncoding,
                KittyKeyboardFlags = (int)state.Modes.KittyKeyboardFlags,
            },
        };
    }

    private static TerminalState FromStateModel(StateSnapshotModel m)
    {
        TerminalState state = new()
        {
            CursorX = m.CursorX,
            CursorY = m.CursorY,
            WrapPending = m.WrapPending,
            SavedCursorX = m.SavedCursorX,
            SavedCursorY = m.SavedCursorY,
            SavedOriginMode = m.SavedOriginMode,
            SavedWrapPending = m.SavedWrapPending,
        };

        if (m.Modes != null)
        {
            state.Modes.AutoWrap = m.Modes.AutoWrap;
            state.Modes.OriginMode = m.Modes.OriginMode;
            state.Modes.ApplicationCursorKeys = m.Modes.ApplicationCursorKeys;
            state.Modes.InsertMode = m.Modes.InsertMode;
            state.Modes.CursorBlink = m.Modes.CursorBlink;
            state.Modes.CursorVisible = m.Modes.CursorVisible;
            state.Modes.ReverseVideo = m.Modes.ReverseVideo;
            state.Modes.ApplicationKeypad = m.Modes.ApplicationKeypad;
            state.Modes.BackspaceSendsControlH = m.Modes.BackspaceSendsControlH;
            state.Modes.LeftRightMarginMode = m.Modes.LeftRightMarginMode;
            state.Modes.LineFeedNewLine = m.Modes.LineFeedNewLine;
            state.Modes.BracketedPaste = m.Modes.BracketedPaste;
            state.Modes.SynchronizedOutput = m.Modes.SynchronizedOutput;
            state.Modes.FocusReporting = m.Modes.FocusReporting;
            state.Modes.MouseTracking = (MouseTrackingMode)m.Modes.MouseTracking;
            state.Modes.SgrMouseEncoding = m.Modes.SgrMouseEncoding;
            state.Modes.KittyKeyboardFlags = (KittyKeyboardFlags)m.Modes.KittyKeyboardFlags;
        }

        return state;
    }

    private static void ApplyBufferAndState(
        TerminalScreenBuffer sourceBuffer,
        TerminalState sourceState,
        TerminalScreenBuffer targetBuffer,
        TerminalState targetState)
    {
        lock (targetBuffer.SyncRoot)
        {
            targetBuffer.Resize(sourceBuffer.Columns, sourceBuffer.Rows);
            targetBuffer.SetScrollRegion(sourceBuffer.ScrollTop, sourceBuffer.ScrollBottom);
            targetBuffer.SetHorizontalScrollRegion(sourceBuffer.ScrollLeft, sourceBuffer.ScrollRight);

            // Copy primary grid
            for (int y = 0; y < sourceBuffer.Rows; y++)
            {
                TerminalRow srcRow = sourceBuffer.GetPrimaryRow(y);
                TerminalRow dstRow = targetBuffer.GetPrimaryRow(y);
                dstRow.Wrapped = srcRow.Wrapped;
                dstRow.Dirty = true;
                srcRow.Cells.AsSpan(0, Math.Min(srcRow.Length, dstRow.Length)).CopyTo(dstRow.Cells);
            }

            // Copy alternate grid
            if (sourceBuffer.IsAlternate)
            {
                targetBuffer.EnterAlternateScreen();
                for (int y = 0; y < sourceBuffer.Rows; y++)
                {
                    TerminalRow srcRow = sourceBuffer.GetAltRow(y) ?? sourceBuffer.GetRowObject(y);
                    TerminalRow dstRow = targetBuffer.GetRowObject(y);
                    dstRow.Wrapped = srcRow.Wrapped;
                    dstRow.Dirty = true;
                    srcRow.Cells.AsSpan(0, Math.Min(srcRow.Length, dstRow.Length)).CopyTo(dstRow.Cells);
                }
            }
            else
            {
                targetBuffer.LeaveAlternateScreen();
            }

            // Copy scrollback
            targetBuffer.ClearScrollback();
            foreach (TerminalRow row in sourceBuffer.GetScrollback())
            {
                TerminalRow copy = new(row.Length) { Wrapped = row.Wrapped };
                row.Cells.CopyTo(copy.Cells, 0);
                targetBuffer.PushScrollback(copy);
            }

            targetBuffer.MarkAllDirty();

            // Copy state
            targetState.CursorX = sourceState.CursorX;
            targetState.CursorY = sourceState.CursorY;
            targetState.WrapPending = sourceState.WrapPending;
            targetState.SavedCursorX = sourceState.SavedCursorX;
            targetState.SavedCursorY = sourceState.SavedCursorY;
            targetState.SavedOriginMode = sourceState.SavedOriginMode;
            targetState.SavedWrapPending = sourceState.SavedWrapPending;
            targetState.Attributes = sourceState.Attributes;
            targetState.SavedAttributes = sourceState.SavedAttributes;
            targetState.Modes = sourceState.Modes.Clone();
        }
    }

    #endregion

    #region Binary Helpers

    private static void WriteCell(BinaryWriter writer, in TerminalCellInfo cell)
    {
        writer.Write(cell.Character.Value);
        writer.Write(cell.Style.Foreground.ToArgb());
        writer.Write(cell.Style.Background.ToArgb());
        writer.Write(cell.Style.UnderlineColor.ToArgb());
        writer.Write((uint)cell.Style.Flags);
        writer.Write((byte)cell.Style.UnderlineStyle);
        writer.Write(cell.Style.HyperlinkId);
    }

    private static TerminalCellInfo ReadCell(BinaryReader reader)
    {
        int runeVal = reader.ReadInt32();
        uint fg = reader.ReadUInt32();
        uint bg = reader.ReadUInt32();
        uint ul = reader.ReadUInt32();
        uint flags = reader.ReadUInt32();
        byte ulStyle = reader.ReadByte();
        byte hyperlink = reader.ReadByte();

        CellRenderStyle style = new()
        {
            Foreground = TerminalColor.FromArgb(unchecked((int)fg)),
            Background = TerminalColor.FromArgb(unchecked((int)bg)),
            UnderlineColor = TerminalColor.FromArgb(unchecked((int)ul)),
            Flags = (CellStyleFlags)flags,
            UnderlineStyle = (Underline)ulStyle,
            HyperlinkId = hyperlink,
        };

        return new TerminalCellInfo(new Rune(runeVal), style);
    }

    private static void WriteState(BinaryWriter writer, TerminalState state)
    {
        writer.Write(state.CursorX);
        writer.Write(state.CursorY);
        writer.Write(state.WrapPending);
        writer.Write(state.SavedCursorX);
        writer.Write(state.SavedCursorY);
        writer.Write(state.SavedOriginMode);
        writer.Write(state.SavedWrapPending);

        // Modes
        TerminalModes modes = state.Modes;
        writer.Write(modes.AutoWrap);
        writer.Write(modes.OriginMode);
        writer.Write(modes.ApplicationCursorKeys);
        writer.Write(modes.InsertMode);
        writer.Write(modes.CursorBlink);
        writer.Write(modes.CursorVisible);
        writer.Write(modes.ReverseVideo);
        writer.Write(modes.ApplicationKeypad);
        writer.Write(modes.BackspaceSendsControlH);
        writer.Write(modes.LeftRightMarginMode);
        writer.Write(modes.LineFeedNewLine);
        writer.Write(modes.BracketedPaste);
        writer.Write(modes.SynchronizedOutput);
        writer.Write(modes.FocusReporting);
        writer.Write((int)modes.MouseTracking);
        writer.Write(modes.SgrMouseEncoding);
        writer.Write((int)modes.KittyKeyboardFlags);
    }

    private static TerminalState ReadState(BinaryReader reader)
    {
        TerminalState state = new()
        {
            CursorX = reader.ReadInt32(),
            CursorY = reader.ReadInt32(),
            WrapPending = reader.ReadBoolean(),
            SavedCursorX = reader.ReadInt32(),
            SavedCursorY = reader.ReadInt32(),
            SavedOriginMode = reader.ReadBoolean(),
            SavedWrapPending = reader.ReadBoolean(),
        };

        TerminalModes modes = state.Modes;
        modes.AutoWrap = reader.ReadBoolean();
        modes.OriginMode = reader.ReadBoolean();
        modes.ApplicationCursorKeys = reader.ReadBoolean();
        modes.InsertMode = reader.ReadBoolean();
        modes.CursorBlink = reader.ReadBoolean();
        modes.CursorVisible = reader.ReadBoolean();
        modes.ReverseVideo = reader.ReadBoolean();
        modes.ApplicationKeypad = reader.ReadBoolean();
        modes.BackspaceSendsControlH = reader.ReadBoolean();
        modes.LeftRightMarginMode = reader.ReadBoolean();
        modes.LineFeedNewLine = reader.ReadBoolean();
        modes.BracketedPaste = reader.ReadBoolean();
        modes.SynchronizedOutput = reader.ReadBoolean();
        modes.FocusReporting = reader.ReadBoolean();
        modes.MouseTracking = (MouseTrackingMode)reader.ReadInt32();
        modes.SgrMouseEncoding = reader.ReadBoolean();
        modes.KittyKeyboardFlags = (KittyKeyboardFlags)reader.ReadInt32();

        return state;
    }

    private static InvalidDataException aerial_InvalidData(string message) => new(message);

    #endregion
}

[JsonSerializable(typeof(TerminalSnapshotModel))]
[JsonSerializable(typeof(RowSnapshotModel))]
[JsonSerializable(typeof(CellSnapshotModel))]
[JsonSerializable(typeof(StateSnapshotModel))]
[JsonSerializable(typeof(ModesSnapshotModel))]
internal sealed partial class TerminalSnapshotJsonContext : JsonSerializerContext
{
}

#region DTO Classes

/// <summary>Root model for JSON serialization of terminal state snapshots.</summary>
public sealed class TerminalSnapshotModel
{
    /// <summary>Column count.</summary>
    public int Columns { get; set; }
    /// <summary>Row count.</summary>
    public int Rows { get; set; }
    /// <summary>Max scrollback capacity.</summary>
    public int ScrollbackMax { get; set; }
    /// <summary>Whether alternate screen is currently active.</summary>
    public bool IsAlternate { get; set; }
    /// <summary>Scroll top margin.</summary>
    public int ScrollTop { get; set; }
    /// <summary>Scroll bottom margin.</summary>
    public int ScrollBottom { get; set; }
    /// <summary>Left margin.</summary>
    public int ScrollLeft { get; set; }
    /// <summary>Right margin.</summary>
    public int ScrollRight { get; set; }

    /// <summary>Primary grid lines.</summary>
    public List<RowSnapshotModel> PrimaryGrid { get; set; } = new();
    /// <summary>Alternate grid lines (when active).</summary>
    public List<RowSnapshotModel>? AltGrid { get; set; }
    /// <summary>Scrollback history lines.</summary>
    public List<RowSnapshotModel> Scrollback { get; set; } = new();

    /// <summary>Terminal decoder cursor and mode state.</summary>
    public StateSnapshotModel State { get; set; } = new();
}

/// <summary>Model representing a single terminal grid line.</summary>
public sealed class RowSnapshotModel
{
    /// <summary>Whether this row wraps to the next line.</summary>
    public bool Wrapped { get; set; }
    /// <summary>Cells in this row.</summary>
    public List<CellSnapshotModel> Cells { get; set; } = new();
}

/// <summary>Model representing a single cell's rune and render style.</summary>
public sealed class CellSnapshotModel
{
    /// <summary>Codepoint.</summary>
    public int Rune { get; set; }
    /// <summary>Foreground ARGB.</summary>
    public uint Fg { get; set; }
    /// <summary>Background ARGB.</summary>
    public uint Bg { get; set; }
    /// <summary>Underline ARGB.</summary>
    public uint Ul { get; set; }
    /// <summary>Style flags.</summary>
    public uint Flags { get; set; }
    /// <summary>Underline style enum.</summary>
    public byte Underline { get; set; }
    /// <summary>Hyperlink ID.</summary>
    public byte Hyperlink { get; set; }
}

/// <summary>Model representing terminal cursor and modes.</summary>
public sealed class StateSnapshotModel
{
    /// <summary>Cursor column.</summary>
    public int CursorX { get; set; }
    /// <summary>Cursor row.</summary>
    public int CursorY { get; set; }
    /// <summary>Pending wrap flag.</summary>
    public bool WrapPending { get; set; }
    /// <summary>Saved cursor column.</summary>
    public int SavedCursorX { get; set; }
    /// <summary>Saved cursor row.</summary>
    public int SavedCursorY { get; set; }
    /// <summary>Saved origin mode.</summary>
    public bool SavedOriginMode { get; set; }
    /// <summary>Saved wrap pending flag.</summary>
    public bool SavedWrapPending { get; set; }
    /// <summary>Mode flags.</summary>
    public ModesSnapshotModel Modes { get; set; } = new();
}

/// <summary>Model representing terminal mode flags.</summary>
public sealed class ModesSnapshotModel
{
    /// <summary>Auto-wrap mode.</summary>
    public bool AutoWrap { get; set; }
    /// <summary>Origin mode.</summary>
    public bool OriginMode { get; set; }
    /// <summary>Application cursor keys.</summary>
    public bool ApplicationCursorKeys { get; set; }
    /// <summary>Insert mode.</summary>
    public bool InsertMode { get; set; }
    /// <summary>Cursor blink.</summary>
    public bool CursorBlink { get; set; }
    /// <summary>Cursor visible.</summary>
    public bool CursorVisible { get; set; }
    /// <summary>Reverse video.</summary>
    public bool ReverseVideo { get; set; }
    /// <summary>Application keypad.</summary>
    public bool ApplicationKeypad { get; set; }
    /// <summary>Backspace sends Control-H.</summary>
    public bool BackspaceSendsControlH { get; set; }
    /// <summary>Left/right margin mode.</summary>
    public bool LeftRightMarginMode { get; set; }
    /// <summary>Line feed new line.</summary>
    public bool LineFeedNewLine { get; set; }
    /// <summary>Bracketed paste.</summary>
    public bool BracketedPaste { get; set; }
    /// <summary>Synchronized output.</summary>
    public bool SynchronizedOutput { get; set; }
    /// <summary>Focus reporting.</summary>
    public bool FocusReporting { get; set; }
    /// <summary>Mouse tracking mode.</summary>
    public int MouseTracking { get; set; }
    /// <summary>SGR mouse encoding.</summary>
    public bool SgrMouseEncoding { get; set; }
    /// <summary>Kitty Keyboard Protocol flags.</summary>
    public int KittyKeyboardFlags { get; set; }
}

#endregion
