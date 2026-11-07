using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace dotnes;

/// <summary>
/// Recovers PPU palette VRAM-buffer records and the soft-CPU pointer tables that select them,
/// then rewrites peeks to read through those arrays (original <c>(ptr),Y</c> shape, O(1) after bind).
/// </summary>
static class PaletteTableRecovery
{
    /// <summary>Accepted color counts in the length byte (low 6 bits).</summary>
    static readonly HashSet<int> AcceptedColorCounts = [4, 8, 16, 32];

    public readonly record struct PaletteTable(int PrgOffset, ushort CpuAddress, byte LengthByte, int ColorCount)
    {
        public int ByteLength => 3 + ColorCount;
    }

    /// <summary>Paired lo/hi ROM tables loaded into <c>vram_ptr_lo/hi</c> to select a VRAM buffer.</summary>
    public readonly record struct VramPointerTable(ushort LoAddr, ushort HiAddr, ushort[] Pointers);

    /// <summary>
    /// Find non-overlapping VRAM-buffer palette records in PRG.
    /// Pattern: <c>3F 00 LL</c> then <c>LL &amp; 0x3F</c> bytes each ≤ <c>0x3D</c>.
    /// </summary>
    public static List<PaletteTable> FindVramBufferPalettes(ReadOnlySpan<byte> prg)
    {
        var found = new List<PaletteTable>();
        int i = 0;
        while (i + 3 < prg.Length)
        {
            if (prg[i] != 0x3F || prg[i + 1] != 0x00)
            {
                i++;
                continue;
            }

            byte lengthByte = prg[i + 2];
            int colorCount = lengthByte & 0x3F;
            if (!AcceptedColorCounts.Contains(colorCount) || i + 3 + colorCount > prg.Length)
            {
                i++;
                continue;
            }

            bool colorsOk = true;
            for (int c = 0; c < colorCount; c++)
            {
                if (prg[i + 3 + c] > 0x3D)
                {
                    colorsOk = false;
                    break;
                }
            }

            if (!colorsOk)
            {
                i++;
                continue;
            }

            found.Add(new PaletteTable(i, CpuAddressForPrgOffset(i, prg.Length), lengthByte, colorCount));
            i += 3 + colorCount;
        }

        return found;
    }

    /// <summary>
    /// From soft-CPU bodies, find <c>LDA lo,X / STA $00 / LDA hi,X / STA $01</c> pairs whose
    /// tables contain at least one recovered palette base, and read those pointer tables from PRG.
    /// </summary>
    public static List<VramPointerTable> FindVramPointerTables(
        ReadOnlySpan<byte> prg,
        IReadOnlyList<PaletteTable> palettes,
        IEnumerable<List<string>> bodies)
    {
        var results = new List<VramPointerTable>();
        if (palettes.Count == 0)
            return results;

        var paletteBases = new HashSet<ushort>();
        foreach (var p in palettes)
            paletteBases.Add(p.CpuAddress);

        var seen = new HashSet<(ushort Lo, ushort Hi)>();
        foreach (var body in bodies)
        {
            for (int i = 0; i < body.Count; i++)
            {
                if (!TryMatchIndexedPeek(body[i].TrimStart(), out ushort loAddr, out string idx, out _))
                    continue;
                if (i + 1 >= body.Count || !IsPokeZp00(body[i + 1].TrimStart()))
                    continue;

                // Allow a few intervening lines between STA $00 and the hi-byte load.
                int hiAt = -1;
                ushort hiAddr = 0;
                for (int j = i + 2; j < body.Count && j <= i + 6; j++)
                {
                    if (!TryMatchIndexedPeek(body[j].TrimStart(), out hiAddr, out string idx2, out _))
                        continue;
                    if (idx2 != idx)
                        continue;
                    if (j + 1 >= body.Count || !IsPokeZp01(body[j + 1].TrimStart()))
                        continue;
                    hiAt = j;
                    break;
                }

                if (hiAt < 0 || hiAddr <= loAddr)
                    continue;
                if (!seen.Add((loAddr, hiAddr)))
                    continue;

                int len = hiAddr - loAddr;
                if (len is < 2 or > 64)
                    continue;
                if (!TryReadPointerTable(prg, loAddr, hiAddr, len, out ushort[] ptrs))
                    continue;

                int hits = 0;
                foreach (ushort p in ptrs)
                {
                    if (paletteBases.Contains(p))
                        hits++;
                }

                if (hits == 0)
                    continue;

                results.Add(new VramPointerTable(loAddr, hiAddr, ptrs));
            }
        }

        return results;
    }

    /// <summary>One table row: <c>new byte[] { 0x3F, 0x00, … }</c>.</summary>
    public static string FormatTableInitializer(ReadOnlySpan<byte> prg, PaletteTable table)
    {
        var sb = new StringBuilder();
        sb.Append("new byte[] { ");
        sb.Append($"0x{prg[table.PrgOffset]:X2}, 0x{prg[table.PrgOffset + 1]:X2}, 0x{table.LengthByte:X2}");
        for (int c = 0; c < table.ColorCount; c++)
        {
            byte color = prg[table.PrgOffset + 3 + c];
            sb.Append(", ");
            sb.Append(NesLibEnumFormatter.FormatNesColor(color));
        }
        sb.Append(" }");
        return sb.ToString();
    }

    /// <summary>Emit <c>(ushort Base, byte[] Data)[] palette</c>.</summary>
    public static List<string> FormatDeclarations(ReadOnlySpan<byte> prg, IReadOnlyList<PaletteTable> tables)
    {
        var lines = new List<string>();
        if (tables.Count == 0)
            return lines;

        lines.Add("(ushort Base, byte[] Data)[] palette =");
        lines.Add("{");
        for (int i = 0; i < tables.Count; i++)
        {
            string comma = i < tables.Count - 1 ? "," : "";
            lines.Add($"    (0x{tables[i].CpuAddress:X4}, {FormatTableInitializer(prg, tables[i])}){comma}");
        }
        lines.Add("};");
        return lines;
    }

    /// <summary>Emit recovered VRAM buffer pointer table(s) that select among palettes / RAM buffers.</summary>
    public static List<string> FormatVramPointerDeclarations(IReadOnlyList<VramPointerTable> tables)
    {
        var lines = new List<string>();
        for (int t = 0; t < tables.Count; t++)
        {
            var table = tables[t];
            string name = tables.Count == 1 ? "vramBufPtr" : $"vramBufPtr_{table.LoAddr:X4}";
            lines.Add($"// VRAM buffer pointers (lo${table.LoAddr:X4}/hi${table.HiAddr:X4}) — index selects RAM buffer or palette.Base.");
            var sb = new StringBuilder($"ushort[] {name} = {{ ");
            for (int i = 0; i < table.Pointers.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"0x{table.Pointers[i]:X4}");
            }
            sb.Append(" };");
            lines.Add(sb.ToString());
        }
        return lines;
    }

    /// <summary>
    /// Map a PRG file offset to the usual CPU window ($8000+) for NROM-sized images.
    /// </summary>
    public static ushort CpuAddressForPrgOffset(int prgOffset, int prgLength)
    {
        if (prgLength <= 0)
            return (ushort)prgOffset;
        if (prgLength == 0x4000)
            return (ushort)(0x8000 + (prgOffset % 0x4000));
        if (prgLength <= 0x8000)
            return (ushort)(0x8000 + prgOffset);
        int windowBase = prgLength - 0x8000;
        if (prgOffset >= windowBase)
            return (ushort)(0x8000 + (prgOffset - windowBase));
        return (ushort)(0x8000 + (prgOffset % 0x8000));
    }

    /// <summary>
    /// Helpers: bind <c>zp</c> to a recovered palette (and optional text) row once, then
    /// <c>(zp),Y</c> is a direct index (same shape as the original indirect peek).
    /// </summary>
    public static IEnumerable<string> FormatRuntimeHelpers(bool includeTextBuffers = false)
    {
        yield return "byte[] _vramData = null;";
        yield return "ushort _vramBase = 0;";
        yield return "void BindVramFromZp()";
        yield return "{";
        yield return "    ushort p = (ushort)(vram_ptr_lo | vram_ptr_hi << 8);";
        yield return "    if (_vramData != null && p >= _vramBase && p < _vramBase + _vramData.Length) return;";
        yield return "    _vramData = null;";
        yield return "    _vramBase = p;";
        yield return "    for (int i = 0; i < palette.Length; i++)";
        yield return "    {";
        yield return "        if (palette[i].Base == p) { _vramData = palette[i].Data; return; }";
        yield return "    }";
        if (includeTextBuffers)
        {
            foreach (string line in VramTextBufferRecovery.FormatBindTextExtension())
                yield return line;
        }
        yield return "}";
        yield return "byte PeekVramBufY()";
        yield return "{";
        yield return "    ushort addr = (ushort)((vram_ptr_lo | vram_ptr_hi << 8) + y);";
        yield return "    if (_vramData == null || addr < _vramBase || addr >= _vramBase + _vramData.Length)";
        yield return "        BindVramFromZp();";
        yield return "    if (_vramData != null)";
        yield return "    {";
        yield return "        int i = addr - _vramBase;";
        yield return "        if ((uint)i < (uint)_vramData.Length) return _vramData[i];";
        yield return "    }";
        yield return "    return peek(addr);";
        yield return "}";
    }

    /// <summary>
    /// Rewrite peeks/loads so recovered palettes participate like the original:
    /// pointer-table loads → <c>vramBufPtr[i]</c> + bind; absolute peeks → <c>palette[i].Data[…]</c>;
    /// <c>(vram_ptr_lo),Y</c> → <c>PeekVramBufY()</c>.
    /// </summary>
    public static void RewriteSoftCpuUsages(
        List<string> statements,
        IReadOnlyList<PaletteTable> palettes,
        IReadOnlyList<VramPointerTable> ptrTables)
    {
        if (statements.Count == 0)
            return;

        if (ptrTables.Count > 0)
            RewritePointerTableLoads(statements, ptrTables);

        if (palettes.Count == 0)
            return;

        var byAddr = new Dictionary<ushort, (int Index, int Offset)>();
        for (int i = 0; i < palettes.Count; i++)
        {
            var t = palettes[i];
            for (int o = 0; o < t.ByteLength; o++)
                byAddr[(ushort)(t.CpuAddress + o)] = (i, o);
        }

        for (int i = 0; i < statements.Count; i++)
        {
            string indent = LeadingIndent(statements[i]);
            string line = statements[i].TrimStart();

            if (TryRewriteAbsolutePeek(line, byAddr, out string rewritten))
            {
                statements[i] = indent + rewritten;
                continue;
            }

            if (TryRewriteZp00YPeek(line, out rewritten))
                statements[i] = indent + rewritten;
        }
    }

    static void RewritePointerTableLoads(List<string> statements, IReadOnlyList<VramPointerTable> ptrTables)
    {
        var byLo = new Dictionary<ushort, VramPointerTable>();
        foreach (var t in ptrTables)
            byLo[t.LoAddr] = t;

        for (int i = 0; i < statements.Count; i++)
        {
            string line = statements[i].TrimStart();
            if (!TryMatchIndexedPeek(line, out ushort loAddr, out string idx, out string lhs))
                continue;
            if (!byLo.TryGetValue(loAddr, out var table))
                continue;
            if (i + 1 >= statements.Count || !IsPokeZp00(statements[i + 1].TrimStart()))
                continue;

            int hiAt = -1;
            for (int j = i + 2; j < statements.Count && j <= i + 6; j++)
            {
                if (!TryMatchIndexedPeek(statements[j].TrimStart(), out ushort hiAddr, out string idx2, out _))
                    continue;
                if (idx2 != idx || hiAddr != table.HiAddr)
                    continue;
                if (j + 1 >= statements.Count || !IsPokeZp01(statements[j + 1].TrimStart()))
                    continue;
                hiAt = j;
                break;
            }

            if (hiAt < 0)
                continue;

            string indent = LeadingIndent(statements[i]);
            string name = ptrTables.Count == 1 ? "vramBufPtr" : $"vramBufPtr_{table.LoAddr:X4}";
            // Replace lo-peek through hi-poke with one pointer-table load + bind (original STA $00/$01 shape).
            var replacement = new List<string>
            {
                indent + "{",
                indent + $"    ushort _p = {name}[{idx}];",
                indent + "    vram_ptr_lo = (byte)_p;",
                indent + "    vram_ptr_hi = (byte)(_p >> 8);",
                indent + "}",
                indent + "BindVramFromZp();",
            };

            statements.RemoveRange(i, hiAt + 2 - i);
            statements.InsertRange(i, replacement);
            i += replacement.Count - 1;
            _ = lhs; // original lhs was always 'a'
        }
    }

    static readonly Regex AbsPeek = new(
        @"^(?<lhs>\w+)\s*=\s*peek\(\(ushort\)0x(?<addr>[0-9A-Fa-f]{1,4})\);$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Accept double-cast emit and SugarIndexedPeeks collapse: (ushort)((ushort)0x… + i) or (ushort)(0x… + i).
    static readonly Regex IndexedPeek = new(
        @"^(?<lhs>\w+)\s*=\s*peek\(\(ushort\)\((?:\(ushort\))?0x(?<addr>[0-9A-Fa-f]{1,4})\s*\+\s*(?<idx>x|y)\)\);$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex Zp00YPeek = new(
        @"^(?<lhs>\w+)\s*=\s*peek\(\(ushort\)\(\((?:peek\((?:vram_ptr_lo|zp_00|\(ushort\)0x00)\)|vram_ptr_lo|zp_00)\s*\|\s*(?:peek\((?:vram_ptr_hi|zp_01|\(ushort\)0x01)\)|vram_ptr_hi|zp_01)\s*<<\s*8\)\s*\+\s*y\)\);$",
        RegexOptions.Compiled);

    static bool TryMatchIndexedPeek(string line, out ushort addr, out string idx, out string lhs)
    {
        addr = 0;
        idx = "";
        lhs = "";
        var m = IndexedPeek.Match(line);
        if (!m.Success)
            return false;
        if (!ushort.TryParse(m.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return false;
        idx = m.Groups["idx"].Value;
        lhs = m.Groups["lhs"].Value;
        return true;
    }

    static bool TryRewriteAbsolutePeek(
        string line,
        Dictionary<ushort, (int Index, int Offset)> byAddr,
        out string rewritten)
    {
        rewritten = null!;
        var m = AbsPeek.Match(line);
        if (m.Success
            && ushort.TryParse(m.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort addr)
            && byAddr.TryGetValue(addr, out var hit))
        {
            rewritten = $"{m.Groups["lhs"].Value} = palette[{hit.Index}].Data[{hit.Offset}];";
            return true;
        }

        m = IndexedPeek.Match(line);
        if (m.Success
            && ushort.TryParse(m.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr)
            && byAddr.TryGetValue(addr, out hit)
            && hit.Offset == 0)
        {
            rewritten = $"{m.Groups["lhs"].Value} = palette[{hit.Index}].Data[{m.Groups["idx"].Value}];";
            return true;
        }

        return false;
    }

    static bool TryRewriteZp00YPeek(string line, out string rewritten)
    {
        rewritten = null!;
        var m = Zp00YPeek.Match(line);
        if (!m.Success)
            return false;
        rewritten = $"{m.Groups["lhs"].Value} = PeekVramBufY();";
        return true;
    }

    static bool IsPokeZp00(string line) =>
        line.StartsWith("poke(vram_ptr_lo,", StringComparison.Ordinal)
        || line.StartsWith("poke(zp_00,", StringComparison.Ordinal)
        || line.StartsWith("poke((ushort)0x00,", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("vram_ptr_lo =", StringComparison.Ordinal)
        || line.StartsWith("zp_00 =", StringComparison.Ordinal);

    static bool IsPokeZp01(string line) =>
        line.StartsWith("poke(vram_ptr_hi,", StringComparison.Ordinal)
        || line.StartsWith("poke(zp_01,", StringComparison.Ordinal)
        || line.StartsWith("poke((ushort)0x01,", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("vram_ptr_hi =", StringComparison.Ordinal)
        || line.StartsWith("zp_01 =", StringComparison.Ordinal);

    static bool TryReadPointerTable(
        ReadOnlySpan<byte> prg,
        ushort loAddr,
        ushort hiAddr,
        int len,
        out ushort[] ptrs)
    {
        ptrs = Array.Empty<ushort>();
        if (!TryPrgOffsetForCpuAddress(loAddr, prg.Length, out int loOff))
            return false;
        if (!TryPrgOffsetForCpuAddress(hiAddr, prg.Length, out int hiOff))
            return false;
        if (loOff + len > prg.Length || hiOff + len > prg.Length)
            return false;

        ptrs = new ushort[len];
        for (int i = 0; i < len; i++)
            ptrs[i] = (ushort)(prg[loOff + i] | (prg[hiOff + i] << 8));
        return true;
    }

    static bool TryPrgOffsetForCpuAddress(ushort cpu, int prgLength, out int offset)
    {
        offset = -1;
        if (prgLength <= 0)
            return false;
        if (cpu < 0x8000)
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

        // Fixed last 32KB window.
        offset = prgLength - 0x8000 + (cpu - 0x8000);
        return offset >= 0 && offset < prgLength;
    }

    static string LeadingIndent(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t')
            i++;
        return line[..i];
    }
}
