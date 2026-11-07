using System.Globalization;
using System.Text;

namespace dotnes;

/// <summary>
/// Recovers nametable VRAM-update streams that carry tile-font text (SMB-style):
/// <c>ppuHi, ppuLo, len, tiles…</c> repeated until <c>0x00</c>.
/// Decodes common NES status-bar fonts (digits <c>0–9</c>, letters <c>A–Z</c>, space <c>$24</c>).
/// </summary>
static class VramTextBufferRecovery
{
    public readonly record struct TextRecord(ushort PpuAddress, byte LengthByte, byte[] Tiles, string Decoded);

    public readonly record struct TextBuffer(ushort CpuAddress, int PrgOffset, int ByteLength, IReadOnlyList<TextRecord> Records)
    {
        public string Summary
        {
            get
            {
                if (Records.Count == 0)
                    return "";
                if (Records.Count == 1)
                    return Records[0].Decoded;
                return string.Join(" | ", Records.Select(r => r.Decoded));
            }
        }
    }

    /// <summary>
    /// Collect text buffers pointed at by recovered <c>vramBufPtr</c> ROM entries, plus
    /// other high-confidence streams (e.g. title HUD at <c>$8752</c>).
    /// </summary>
    public static List<TextBuffer> FindTextBuffers(
        ReadOnlySpan<byte> prg,
        IReadOnlyList<PaletteTableRecovery.VramPointerTable> ptrTables)
    {
        var byAddr = new Dictionary<ushort, TextBuffer>();
        var seeds = new HashSet<ushort>();

        foreach (var table in ptrTables)
        {
            foreach (ushort p in table.Pointers)
            {
                if (p >= 0x8000)
                    seeds.Add(p);
            }
        }

        // Title / HUD streams often sit next to status text and are copied by absolute pointer.
        foreach (ushort extra in FindHighConfidenceTextStarts(prg.ToArray()))
            seeds.Add(extra);

        foreach (ushort cpu in seeds.OrderBy(a => a))
        {
            if (!TryParseTextBuffer(prg, cpu, out var buf))
                continue;
            byAddr[cpu] = buf;
        }

        return byAddr.Values.OrderBy(b => b.CpuAddress).ToList();
    }

    public static List<string> FormatDeclarations(ReadOnlySpan<byte> prg, IReadOnlyList<TextBuffer> buffers)
    {
        var lines = new List<string>();
        if (buffers.Count == 0)
            return lines;

        lines.Add("// Nametable VRAM text buffers (ppuHi,ppuLo,len,tiles…,00).");
        lines.Add("// Text string only when a tile font decodes to English; otherwise Data is the source of truth.");
        lines.Add("(ushort Base, byte[] Data, string Text)[] vramText =");
        lines.Add("{");
        for (int i = 0; i < buffers.Count; i++)
        {
            var b = buffers[i];
            string comma = i < buffers.Count - 1 ? "," : "";
            string summary = EnglishSummary(b);
            string textLit = EscapeCSharpString(summary);
            lines.Add($"    (0x{b.CpuAddress:X4}, {FormatRawBytes(prg, b)}, \"{textLit}\"){comma}");
        }
        lines.Add("};");
        return lines;
    }

    /// <summary>Prefer discovered English tile decode; empty when nothing English-like.</summary>
    static string EnglishSummary(TextBuffer b)
    {
        if (b.Records.Count == 0)
            return "";
        var parts = new List<string>();
        foreach (var r in b.Records)
        {
            if (GameTextTableRecovery.TryDecodeTiles(r.Tiles, out string tiled, out _))
                parts.Add(tiled);
            else if (LooksEnglish(r.Decoded))
                parts.Add(r.Decoded);
        }
        return parts.Count == 0 ? "" : string.Join(" | ", parts);
    }

    static bool LooksEnglish(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Contains('.', StringComparison.Ordinal))
            return false;
        int letters = 0, vowels = 0;
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z')
            {
                letters++;
                if (c is 'A' or 'E' or 'I' or 'O' or 'U')
                    vowels++;
            }
        }
        return letters >= 3 && vowels >= 1;
    }

    /// <summary>Annotate pointer-table entries that land on recovered text buffers.</summary>
    public static List<string> FormatAnnotatedVramPointerDeclarations(
        IReadOnlyList<PaletteTableRecovery.VramPointerTable> tables,
        IReadOnlyList<TextBuffer> textBuffers)
    {
        var byAddr = textBuffers.ToDictionary(t => t.CpuAddress);
        var lines = new List<string>();
        for (int t = 0; t < tables.Count; t++)
        {
            var table = tables[t];
            string name = tables.Count == 1 ? "vramBufPtr" : $"vramBufPtr_{table.LoAddr:X4}";
            lines.Add($"// VRAM buffer pointers (lo${table.LoAddr:X4}/hi${table.HiAddr:X4}) — RAM, palette, or text stream.");
            lines.Add($"ushort[] {name} =");
            lines.Add("{");
            for (int i = 0; i < table.Pointers.Length; i++)
            {
                ushort p = table.Pointers[i];
                string comma = i < table.Pointers.Length - 1 ? "," : "";
                if (byAddr.TryGetValue(p, out var text))
                {
                    string summary = EnglishSummary(text);
                    if (summary.Length > 0)
                    {
                        if (summary.Length > 48)
                            summary = summary[..45] + "...";
                        lines.Add($"    0x{p:X4}{comma} // \"{EscapeCSharpString(summary)}\"");
                    }
                    else
                        lines.Add($"    0x{p:X4}{comma} // text stream");
                }
                else if (p < 0x8000)
                    lines.Add($"    0x{p:X4}{comma} // RAM update buffer");
                else
                    lines.Add($"    0x{p:X4}{comma}");
            }
            lines.Add("};");
        }
        return lines;
    }

    /// <summary>Extend <see cref="PaletteTableRecovery.FormatRuntimeHelpers"/> bind to also match text rows.</summary>
    public static IEnumerable<string> FormatBindTextExtension()
    {
        yield return "    for (int i = 0; i < vramText.Length; i++)";
        yield return "    {";
        yield return "        if (vramText[i].Base == p) { _vramData = vramText[i].Data; return; }";
        yield return "    }";
    }

    static string FormatRawBytes(ReadOnlySpan<byte> prg, TextBuffer buf)
    {
        var sb = new StringBuilder("new byte[] { ");
        for (int i = 0; i < buf.ByteLength; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"0x{prg[buf.PrgOffset + i]:X2}");
        }
        sb.Append(" }");
        return sb.ToString();
    }

    static List<ushort> FindHighConfidenceTextStarts(byte[] prg)
    {
        // MARIO tile run after a nametable address — classic SMB HUD / title.
        byte[] mario = [0x16, 0x0A, 0x1B, 0x12, 0x18];
        var found = new List<ushort>();
        for (int i = 0; i + 8 < prg.Length; i++)
        {
            if (prg[i] is < 0x20 or > 0x27)
                continue;
            if ((prg[i + 2] & 0x3F) != 5)
                continue;
            if (!prg.AsSpan(i + 3, 5).SequenceEqual(mario))
                continue;
            found.Add(PaletteTableRecovery.CpuAddressForPrgOffset(i, prg.Length));
        }
        return found;
    }

    static bool TryParseTextBuffer(ReadOnlySpan<byte> prg, ushort cpu, out TextBuffer buffer)
    {
        buffer = default!;
        if (!TryPrgOffset(cpu, prg.Length, out int start))
            return false;

        var records = new List<TextRecord>();
        int i = start;
        int textishRecords = 0;
        while (i < prg.Length)
        {
            byte hi = prg[i];
            // $00 ends a buffer. $FF separates multi-screen title blobs (SMB).
            // Note: $00 also appears as tile digit '0' *inside* a record's payload.
            if (hi == 0)
            {
                i++; // include terminator in Data
                break;
            }
            if (hi == 0xFF)
            {
                // End this buffer before the separator; do not consume $FF.
                break;
            }

            // Palette rows are recovered separately.
            if (hi == 0x3F)
            {
                if (textishRecords > 0)
                    break; // soft end before palette
                return false;
            }

            if (i + 2 >= prg.Length)
                break;

            byte lo = prg[i + 1];
            byte lenByte = prg[i + 2];
            int len = lenByte & 0x3F;
            bool rle = (lenByte & 0x40) != 0; // SMB: bit6 → one tile repeated `len` times
            int payload = rle ? 1 : len;
            if (len == 0 || payload == 0 || i + 3 + payload > prg.Length)
                break;

            // Reasonable PPU destinations: nametable / attribute ($2000–$2FFF) and
            // SMB’s extended highs used by some ending screens ($25xx–$27xx).
            ushort ppu = (ushort)((hi << 8) | lo);
            bool addrOk = (ppu >= 0x2000 && ppu < 0x3000)
                || hi is >= 0x20 and <= 0x27;
            if (!addrOk)
            {
                // Soft end once we already have text (title streams abut code/other data).
                if (textishRecords > 0)
                    break;
                return false;
            }

            byte[] tiles;
            if (rle)
            {
                tiles = new byte[len];
                Array.Fill(tiles, prg[i + 3]);
            }
            else
                tiles = prg.Slice(i + 3, len).ToArray();

            string decoded = DecodeTileString(tiles);
            // Skip pure attribute / RLE fills in the summary, but keep them in Data.
            if (IsTextish(tiles))
                textishRecords++;
            // Only surface text-ish (or short) records in the decoded summary.
            if (IsTextish(tiles) || (!rle && len <= 16 && tiles.Any(b => b <= 0x24)))
                records.Add(new TextRecord(ppu, lenByte, tiles, decoded));
            else if (records.Count == 0)
            {
                // Leading non-text (shouldn't happen for our seeds) — still track length.
            }

            i += 3 + payload;

            if (records.Count > 24 || i - start > 192)
                break;
        }

        if (records.Count == 0 || textishRecords == 0)
            return false;

        int byteLen = i - start;
        if (byteLen < 4)
            return false;

        buffer = new TextBuffer(cpu, start, byteLen, records);
        return true;
    }

    static bool IsTextish(ReadOnlySpan<byte> tiles)
    {
        if (tiles.Length < 3)
            return false;
        int letters = 0, spaces = 0, digits = 0, other = 0;
        foreach (byte b in tiles)
        {
            if (b <= 0x09) digits++;
            else if (b is >= 0x0A and <= 0x23) letters++;
            else if (b == 0x24) spaces++;
            else if (b is 0x28 or 0x2B) spaces++; // coin / bang treated as punctuation
            else other++;
        }
        int known = letters + spaces + digits;
        return letters >= 3 && known >= tiles.Length * 3 / 4 && other <= tiles.Length / 4;
    }

    /// <summary>SMB / common status-bar tile font → ASCII.</summary>
    public static string DecodeTileString(ReadOnlySpan<byte> tiles)
    {
        var sb = new StringBuilder(tiles.Length);
        foreach (byte b in tiles)
        {
            if (b <= 0x09)
                sb.Append((char)('0' + b));
            else if (b is >= 0x0A and <= 0x23)
                sb.Append((char)('A' + (b - 0x0A)));
            else if (b == 0x24)
                sb.Append(' ');
            else if (b == 0x28)
                sb.Append('*'); // coin glyph stand-in
            else if (b == 0x2B)
                sb.Append('!');
            else
                sb.Append('.');
        }
        return sb.ToString();
    }

    static bool TryPrgOffset(ushort cpu, int prgLength, out int offset)
    {
        offset = -1;
        if (cpu < 0x8000 || prgLength <= 0)
            return false;
        if (prgLength == 0x4000)
        {
            offset = (cpu - 0x8000) % 0x4000;
            return offset >= 0 && offset < prgLength;
        }
        if (prgLength <= 0x8000)
        {
            offset = cpu - 0x8000;
            return offset >= 0 && offset < prgLength;
        }
        offset = prgLength - 0x8000 + (cpu - 0x8000);
        return offset >= 0 && offset < prgLength;
    }

    static string EscapeCSharpString(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
