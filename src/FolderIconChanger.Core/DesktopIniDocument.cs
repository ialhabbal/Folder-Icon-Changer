using System.Globalization;
using System.Text;

namespace FolderIconChanger.Core;

internal sealed class DesktopIniDocument
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private DesktopIniDocument(
        List<string> lines,
        Encoding encoding,
        bool hasByteOrderMark,
        string lineEnding,
        bool hasTrailingLineEnding)
    {
        Lines = lines;
        Encoding = encoding;
        HasByteOrderMark = hasByteOrderMark;
        LineEnding = lineEnding;
        HasTrailingLineEnding = hasTrailingLineEnding;
    }

    public List<string> Lines { get; }
    public Encoding Encoding { get; }
    public bool HasByteOrderMark { get; }
    public string LineEnding { get; }
    public bool HasTrailingLineEnding { get; set; }

    public static DesktopIniDocument Read(string path, bool exists, byte[]? bytes)
    {
        if (!exists || bytes is null)
        {
            return new DesktopIniDocument(
                [],
                new UnicodeEncoding(false, true),
                true,
                "\r\n",
                true);
        }

        var encoding = DetectEncoding(bytes, out var bomLength, out var hasBom);
        var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        var lineEnding = text.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : text.Contains('\n', StringComparison.Ordinal) ? "\n"
            : text.Contains('\r', StringComparison.Ordinal) ? "\r"
            : "\r\n";
        var hasTrailingLineEnding = text.EndsWith('\n') || text.EndsWith('\r');
        var normalizedText = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalizedText.Split('\n').ToList();
        if (hasTrailingLineEnding && lines.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new DesktopIniDocument(
            lines,
            encoding,
            hasBom,
            lineEnding,
            hasTrailingLineEnding);
    }

    public byte[] ToBytes()
    {
        var text = string.Join(LineEnding, Lines);
        if (HasTrailingLineEnding)
        {
            text += LineEnding;
        }

        var content = Encoding.GetBytes(text);
        if (!HasByteOrderMark)
        {
            return content;
        }

        var preamble = Encoding.GetPreamble();
        var bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    public bool HasMeaningfulContentExceptShellClassInfo()
    {
        return Lines.Any(line =>
            !string.IsNullOrWhiteSpace(line)
            && !IsSectionHeader(line, ".ShellClassInfo"));
    }

    public static bool IsSectionHeader(string line, string sectionName)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 2
            && trimmed[0] == '['
            && trimmed[^1] == ']'
            && string.Equals(
                trimmed[1..^1].Trim(),
                sectionName,
                StringComparison.OrdinalIgnoreCase);
    }

    public static string? ReadKeyName(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
        {
            return null;
        }

        var equalsIndex = trimmed.IndexOf('=');
        return equalsIndex <= 0 ? null : trimmed[..equalsIndex].Trim();
    }

    public static (int Start, int End)? FindSection(List<string> lines, string sectionName)
    {
        var start = lines.FindIndex(line => IsSectionHeader(line, sectionName));
        if (start < 0)
        {
            return null;
        }

        var end = lines.FindIndex(start + 1, line =>
            IsSectionHeader(line, sectionName)
            || IsAnySectionHeader(line));
        return (start, end < 0 ? lines.Count : end);
    }

    public static string? ReadKey(List<string> lines, string sectionName, string keyName)
    {
        var section = FindSection(lines, sectionName);
        if (section is null)
        {
            return null;
        }

        for (var index = section.Value.Start + 1; index < section.Value.End; index++)
        {
            if (!string.Equals(ReadKeyName(lines[index]), keyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var equalsIndex = lines[index].IndexOf('=');
            return lines[index][(equalsIndex + 1)..].Trim();
        }

        return null;
    }

    public static void SetKey(
        List<string> lines,
        string sectionName,
        string keyName,
        string value)
    {
        var section = FindSection(lines, sectionName);
        if (section is null)
        {
            AddSection(lines, sectionName);
            section = FindSection(lines, sectionName);
        }

        var range = section!.Value;
        var found = false;
        for (var index = range.Start + 1; index < range.End; index++)
        {
            if (!string.Equals(ReadKeyName(lines[index]), keyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var equalsIndex = lines[index].IndexOf('=');
            lines[index] = lines[index][..equalsIndex].TrimEnd() + "=" + value;
            found = true;
        }

        if (!found)
        {
            lines.Insert(range.End, keyName + "=" + value);
        }
    }

    public static Dictionary<string, List<string>> CaptureIconKeys(List<string> lines)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var section = FindSection(lines, ".ShellClassInfo");
        if (section is null)
        {
            return result;
        }

        for (var index = section.Value.Start + 1; index < section.Value.End; index++)
        {
            var keyName = ReadKeyName(lines[index]);
            if (keyName is null || !IsIconKey(keyName))
            {
                continue;
            }

            if (!result.TryGetValue(keyName, out var values))
            {
                values = [];
                result.Add(keyName, values);
            }

            values.Add(lines[index]);
        }

        return result;
    }

    public static void SetCurrentIcon(
        List<string> lines,
        string iconFileName)
    {
        var section = FindSection(lines, ".ShellClassInfo");
        if (section is null)
        {
            AddSection(lines, ".ShellClassInfo");
            section = FindSection(lines, ".ShellClassInfo");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IconResource"] = iconFileName + ",0",
            ["IconFile"] = iconFileName,
            ["IconIndex"] = "0"
        };
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var range = section!.Value;

        for (var index = range.Start + 1; index < range.End; index++)
        {
            var keyName = ReadKeyName(lines[index]);
            if (keyName is null || !values.TryGetValue(keyName, out var value))
            {
                continue;
            }

            var equalsIndex = lines[index].IndexOf('=');
            lines[index] = lines[index][..equalsIndex].TrimEnd() + "=" + value;
            found.Add(keyName);
        }

        foreach (var pair in values)
        {
            if (!found.Contains(pair.Key))
            {
                lines.Insert(range.End++, pair.Key + "=" + pair.Value);
            }
        }
    }

    public static bool RestoreIconKeys(
        List<string> lines,
        string currentIconFile,
        IReadOnlyDictionary<string, List<string>> originalLines)
    {
        var section = FindSection(lines, ".ShellClassInfo");
        if (section is null)
        {
            return originalLines.Count == 0;
        }

        var unchanged = true;
        foreach (var keyName in new[] { "IconResource", "IconFile", "IconIndex" })
        {
            var currentLines = FindKeyLines(lines, section.Value, keyName);
            if (currentLines.Count == 0 || currentLines.Any(line =>
                !IsCurrentIconValue(keyName, line.Value, currentIconFile)))
            {
                unchanged = false;
                continue;
            }

            var insertionIndex = currentLines[0].Index;
            foreach (var entry in currentLines.OrderByDescending(entry => entry.Index))
            {
                lines.RemoveAt(entry.Index);
            }

            if (originalLines.TryGetValue(keyName, out var priorLines))
            {
                lines.InsertRange(insertionIndex, priorLines);
            }

            section = FindSection(lines, ".ShellClassInfo");
            if (section is null)
            {
                break;
            }
        }

        return unchanged;
    }

    public static void RemoveIconKeys(List<string> lines)
    {
        var section = FindSection(lines, ".ShellClassInfo");
        if (section is null)
        {
            return;
        }

        for (var index = section.Value.End - 1; index > section.Value.Start; index--)
        {
            var keyName = ReadKeyName(lines[index]);
            if (keyName is not null && IsIconKey(keyName))
            {
                lines.RemoveAt(index);
            }
        }
    }

    public static void RemoveSection(List<string> lines, string sectionName)
    {
        var section = FindSection(lines, sectionName);
        if (section is null)
        {
            return;
        }

        lines.RemoveRange(section.Value.Start, section.Value.End - section.Value.Start);
    }

    public static bool RemoveEmptySection(
        List<string> lines,
        string sectionName,
        bool removePrecedingBlankLine)
    {
        var section = FindSection(lines, sectionName);
        if (section is null || lines
            .Skip(section.Value.Start + 1)
            .Take(section.Value.End - section.Value.Start - 1)
            .Any(line => !string.IsNullOrWhiteSpace(line)))
        {
            return false;
        }

        var start = section.Value.Start;
        var count = section.Value.End - section.Value.Start;
        if (removePrecedingBlankLine && start > 0 && string.IsNullOrWhiteSpace(lines[start - 1]))
        {
            start--;
            count++;
        }

        lines.RemoveRange(start, count);
        return true;
    }

    private static void AddSection(List<string> lines, string sectionName)
    {
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.Add(string.Empty);
        }

        lines.Add("[" + sectionName + "]");
    }

    private static bool IsAnySectionHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 2 && trimmed[0] == '[' && trimmed[^1] == ']';
    }

    private static bool IsIconKey(string keyName)
    {
        return keyName.Equals("IconResource", StringComparison.OrdinalIgnoreCase)
            || keyName.Equals("IconFile", StringComparison.OrdinalIgnoreCase)
            || keyName.Equals("IconIndex", StringComparison.OrdinalIgnoreCase);
    }

    private static List<(int Index, string Value)> FindKeyLines(
        List<string> lines,
        (int Start, int End) section,
        string keyName)
    {
        var result = new List<(int Index, string Value)>();
        for (var index = section.Start + 1; index < section.End; index++)
        {
            if (!string.Equals(ReadKeyName(lines[index]), keyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var equalsIndex = lines[index].IndexOf('=');
            result.Add((index, lines[index][(equalsIndex + 1)..].Trim()));
        }

        return result;
    }

    private static bool IsCurrentIconValue(string keyName, string value, string iconFileName)
    {
        if (keyName.Equals("IconResource", StringComparison.OrdinalIgnoreCase))
        {
            return value.Equals(iconFileName + ",0", StringComparison.OrdinalIgnoreCase);
        }

        if (keyName.Equals("IconFile", StringComparison.OrdinalIgnoreCase))
        {
            return value.Equals(iconFileName, StringComparison.OrdinalIgnoreCase);
        }

        return value == "0";
    }

    private static Encoding DetectEncoding(byte[] bytes, out int bomLength, out bool hasBom)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()))
        {
            bomLength = Encoding.UTF8.GetPreamble().Length;
            hasBom = true;
            return new UTF8Encoding(true, true);
        }

        if (bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()))
        {
            bomLength = Encoding.Unicode.GetPreamble().Length;
            hasBom = true;
            return new UnicodeEncoding(false, true, true);
        }

        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.GetPreamble()))
        {
            bomLength = Encoding.BigEndianUnicode.GetPreamble().Length;
            hasBom = true;
            return new UnicodeEncoding(true, true, true);
        }

        bomLength = 0;
        hasBom = false;
        try
        {
            _ = StrictUtf8.GetString(bytes);
            return StrictUtf8;
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
    }
}
