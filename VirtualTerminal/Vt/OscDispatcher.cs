using System;
using System.Globalization;
using System.Text;
using VirtualTerminal.Model;

namespace VirtualTerminal.Vt;

/// <summary>
/// Processes OSC (Operating System Command) payloads: window title (0/1/2), palette recolor
/// (4), default colors (10/11/12), hyperlinks (8), desktop notifications (9), clipboard (52),
/// and shell integration marks (133/1337).
/// </summary>
internal static class OscDispatcher
{
    public static void ProcessOsc(TerminalDecoder d, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        string payload = Encoding.UTF8.GetString(data);
        int firstSemi = payload.IndexOf(';');
        string command = firstSemi < 0 ? payload : payload[..firstSemi];
        string rest = firstSemi < 0 ? string.Empty : payload[(firstSemi + 1)..];

        switch (command)
        {
            case "0":
            case "1":
            case "2": // set title (and icon name)
                d.SetTitle(rest);
                break;

            case "4": // set palette entry: 4 ; c ; spec
                {
                    int semi = rest.IndexOf(';');
                    if (semi > 0 && int.TryParse(rest[..semi], NumberStyles.Integer, CultureInfo.InvariantCulture, out int c))
                    {
                        TerminalColor? col = ParseColorSpec(rest[(semi + 1)..]);
                        if (col.HasValue && c >= 0 && c < 256)
                            d.Options.Palette[c] = col.Value;
                    }
                    break;
                }

            case "10":
            case "11":
            case "12": // default fg / bg / cursor
                {
                    if (rest == "?")
                        break; // query → Phase 2 (gated by AllowOSCColorQueries)
                    TerminalColor? col = ParseColorSpec(rest);
                    if (!col.HasValue)
                        break;
                    if (command == "10") d.Options.DefaultForeground = col.Value;
                    else if (command == "11") d.Options.DefaultBackground = col.Value;
                    else d.Options.DefaultCursorColor = col.Value;
                    break;
                }

            case "8": // hyperlink: 8 ; params ; uri
                {
                    int semi = rest.IndexOf(';');
                    string uri = semi < 0 ? rest : rest[(semi + 1)..];
                    d.State.Attributes.HyperlinkId = string.IsNullOrEmpty(uri) ? (byte)0 : d.RegisterHyperlink(uri);
                    break;
                }

            case "9": // desktop notification: 9 ; message
                {
                    if (!string.IsNullOrEmpty(rest))
                        d.RaiseNotification(rest);
                    break;
                }

            case "52": // clipboard: 52 ; selection ; base64-data-or-?
                {
                    if (!d.Options.AllowClipboard)
                        break;

                    int semi = rest.IndexOf(';');
                    string selection = semi < 0 ? "c" : rest[..semi];
                    string clipData = semi < 0 ? rest : rest[(semi + 1)..];
                    d.RaiseClipboardRequested(selection, string.IsNullOrEmpty(clipData) ? null : clipData);
                    break;
                }

            case "133": // shell integration mark: 133 ; A|B|C|D ; ...
                {
                    d.RaiseShellIntegrationMark(rest);
                    break;
                }

            case "1337": // iTerm2 annotation: 1337 ; key=value ; ...
                {
                    d.RaiseIterm2Annotation(rest);
                    break;
                }

            default:
                break;
        }
    }

    /// <summary>Parses an XParseColor-style spec: <c>#RRGGBB</c>, <c>#RGB</c>, or <c>rgb:RR/GG/BB</c>.</summary>
    internal static TerminalColor? ParseColorSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return null;

        spec = spec.Trim();

        if (spec.StartsWith('#'))
        {
            string hex = spec[1..];
            return hex.Length switch
            {
                3 => TerminalColor.FromArgb((byte)(Hex(hex, 0) * 17), (byte)(Hex(hex, 1) * 17), (byte)(Hex(hex, 2) * 17)),
                6 => TerminalColor.FromArgb(Hex(hex, 0), Hex(hex, 4), Hex(hex, 8)),
                _ => null,
            };
        }

        if (spec.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase))
        {
            // Canonical XParseColor uses '/', but accept ':' too for robustness.
            string[] parts = spec[4..].Split('/', ':');
            if (parts.Length != 3)
                return null;
            return TerminalColor.FromArgb(ScaledByte(parts[0]), ScaledByte(parts[1]), ScaledByte(parts[2]));
        }

        return null;
    }

    private static byte Hex(string s, int offset)
    {
        return (byte)((HexDigit(s[offset]) << 4) | HexDigit(s[offset + 1]));
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };

    private static byte ScaledByte(string component)
    {
        // Components may be 1–4 hex digits; normalize to 8 bits.
        int v = 0;
        foreach (char c in component)
            v = (v << 4) | HexDigit(c);

        return component.Length switch
        {
            0 => 0,
            1 => (byte)(v * 17),
            2 => (byte)v,
            3 => (byte)(v >> 4),
            4 => (byte)(v >> 8),
            _ => (byte)(v >> ((component.Length - 2) * 4)),
        };
    }
}
