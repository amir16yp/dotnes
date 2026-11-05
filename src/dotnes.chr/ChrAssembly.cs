using System.Text;

namespace dotnes;

/// <summary>
/// Parse and emit ca65-style CHR segment assembly used by dotnes (.segment "CHARS" + .byte).
/// </summary>
public static class ChrAssembly
{
    const string SegmentInstruction = ".segment ";
    const string ByteInstruction = ".byte ";

    /// <summary>
    /// Reads CHARS segment bytes from assembly source. Returns empty if no CHARS data.
    /// </summary>
    public static byte[] Parse(string source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        using var reader = new StringReader(source);
        string? name = null;
        var bytes = new List<byte>();
        string? line;
        byte[]? chars = null;

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrEmpty(line) || line[0] == ';')
                continue;

            if (line.StartsWith(SegmentInstruction, StringComparison.Ordinal))
            {
                if (name == "CHARS" && bytes.Count > 0)
                    chars = bytes.ToArray();

                name = line.Substring(SegmentInstruction.Length + 1, line.Length - SegmentInstruction.Length - 2);
                bytes.Clear();
                continue;
            }

            if (line.StartsWith(ByteInstruction, StringComparison.Ordinal))
                ParseByteInstruction(line, bytes);
        }

        if (name == "CHARS" && bytes.Count > 0)
            chars = bytes.ToArray();

        return chars ?? Array.Empty<byte>();
    }

    /// <summary>
    /// Reads CHARS segment bytes from a .s file.
    /// </summary>
    public static byte[] ParseFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// Emits a ca65 .s file for CHR data (16 bytes per .byte line).
    /// </summary>
    public static string Format(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(".segment \"CHARS\"");

        for (int i = 0; i < data.Length; i += 16)
        {
            sb.Append(".byte ");
            int count = Math.Min(16, data.Length - i);
            for (int j = 0; j < count; j++)
            {
                if (j > 0)
                    sb.Append(',');
                sb.Append($"${data[i + j]:X2}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// True if the source appears to be a CHR tileset (.segment "CHARS" present).
    /// </summary>
    public static bool IsCharsDocument(string source) =>
        source.Contains(".segment \"CHARS\"", StringComparison.Ordinal)
        || source.Contains(".segment 'CHARS'", StringComparison.Ordinal);

    static void ParseByteInstruction(string line, List<byte> bytes)
    {
        var span = line.AsSpan().Slice(ByteInstruction.Length);
        for (int i = 0; i + 2 < span.Length; i += 4)
        {
            if (span[i] != '$')
                continue;
            byte firstNibble = ToHexValue(span[i + 1]);
            byte secondNibble = ToHexValue(span[i + 2]);
            bytes.Add((byte)(firstNibble << 4 | secondNibble));
        }
    }

    static byte ToHexValue(char hex) =>
        (byte)(hex - (hex < 58 ? 48 : (hex < 97 ? 55 : 87)));
}
