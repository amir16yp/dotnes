using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace dotnes;

/// <summary>
/// SoftCpu-side recovery of coherent RAM buffers from indexed peek/poke traffic.
/// Roles: OAM (oam_dma page), VRAM update buffer (vramBufPtr RAM targets), generic buf_XXXX.
/// </summary>
static class BufferRecovery
{
    public readonly record struct RamBuffer(ushort Base, int Size, string Name);

    const int MinIndexedAccesses = 3;

    static readonly Regex IndexedHex = new(
        @"\(ushort\)\((?:\(ushort\))?0x(?<addr>[0-9A-Fa-f]{1,4})\s*\+\s*(?<idx>x|y)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex IndexedNamed = new(
        @"\(ushort\)\((?<name>[A-Za-z_]\w*)\s*\+\s*(?<idx>x|y)\)|(?<name>[A-Za-z_]\w*)\s*\+\s*(?<idx>x|y)",
        RegexOptions.Compiled);

    static readonly Regex OamDmaCall = new(
        @"oam_dma\s*\(\s*(?<page>0x[0-9A-Fa-f]+|\d+)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Discover RAM buffers from soft-CPU bodies + optional VRAM pointer table RAM targets.
    /// </summary>
    public static List<RamBuffer> FindBuffers(
        IEnumerable<List<string>> bodies,
        IReadOnlyDictionary<ushort, string> nameToAddr,
        IReadOnlyList<PaletteTableRecovery.VramPointerTable>? vramPtrTables = null)
    {
        var accessCounts = new Dictionary<ushort, int>();
        var maxIndex = new Dictionary<ushort, int>(); // exclusive upper bound hint (max index + 1)
        var bodyList = bodies as IList<List<string>> ?? bodies.ToList();

        foreach (var body in bodyList)
        {
            foreach (string raw in body)
                NoteIndexedAccesses(raw, nameToAddr, accessCounts, maxIndex);
        }

        var oamPages = CollectOamDmaPages(bodyList);
        var vramRamTargets = CollectVramBufRamTargets(vramPtrTables);
        bool vramPageHot = vramRamTargets.Any(a => a >= 0x0300 && a < 0x0400)
            || accessCounts.Where(kv => kv.Key >= 0x0300 && kv.Key < 0x0400).Sum(kv => kv.Value) >= MinIndexedAccesses;

        var candidates = new HashSet<ushort>();
        foreach (var (addr, count) in accessCounts)
        {
            if (addr >= 0x2000)
                continue;
            if (count < MinIndexedAccesses
                && !(oamPages.Contains((byte)(addr >> 8)))
                && !vramRamTargets.Contains(addr))
                continue;
            candidates.Add(CanonicalBase(addr, oamPages, vramPageHot, vramRamTargets));
        }

        foreach (byte page in oamPages)
            candidates.Add((ushort)(page << 8));
        if (vramPageHot)
            candidates.Add(VramBufBase(vramRamTargets));
        foreach (ushort v in vramRamTargets)
            candidates.Add(CanonicalBase(v, oamPages, vramPageHot, vramRamTargets));

        if (candidates.Count == 0)
            return [];

        // Merge mid-buffer indexed hits into OAM page / vram_buf / denser earlier bases.
        var bases = NormalizeBases(candidates, oamPages, vramPageHot, vramRamTargets, accessCounts);
        var ordered = bases.OrderBy(a => a).ToList();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<RamBuffer>();

        for (int i = 0; i < ordered.Count; i++)
        {
            ushort bas = ordered[i];
            ushort next = i + 1 < ordered.Count ? ordered[i + 1] : (ushort)0x2000;
            int gap = Math.Max(1, next - bas);

            byte page = (byte)(bas >> 8);
            string name;
            int size;
            if (oamPages.Contains(page) && (bas & 0xFF) == 0)
            {
                name = "oam";
                size = 256;
            }
            else if (vramPageHot && bas == VramBufBase(vramRamTargets))
            {
                name = "vram_buf";
                // Cover all $03xx traffic and RAM pointer targets; stop at next buffer / page end.
                size = SizeFromTraffic(bas, Math.Min(gap, 0x0400 - bas), accessCounts, maxIndex);
                size = Math.Clamp(size, 1, Math.Min(256, gap));
            }
            else if (vramRamTargets.Contains(bas))
            {
                name = "vram_buf";
                size = SizeFromTraffic(bas, Math.Min(gap, 256), accessCounts, maxIndex);
                size = Math.Clamp(size, 1, Math.Min(256, gap));
            }
            else
            {
                name = $"buf_{bas:X4}";
                // Prefer traffic span over full gap so sparse ZP bases stay tight.
                size = SizeFromTraffic(bas, Math.Min(gap, 256), accessCounts, maxIndex);
                size = Math.Clamp(size, 1, Math.Min(256, gap));
            }

            if (!usedNames.Add(name))
            {
                name = $"{name}_{bas:X4}";
                usedNames.Add(name);
            }

            result.Add(new RamBuffer(bas, size, name));
        }

        return result;
    }

    static ushort VramBufBase(HashSet<ushort> vramRamTargets)
    {
        ushort best = 0x0300;
        foreach (ushort t in vramRamTargets)
        {
            if (t >= 0x0300 && t < 0x0400 && t < best)
                best = t;
        }
        return best;
    }

    static ushort CanonicalBase(
        ushort addr,
        HashSet<byte> oamPages,
        bool vramPageHot,
        HashSet<ushort> vramRamTargets)
    {
        byte page = (byte)(addr >> 8);
        if (oamPages.Contains(page))
            return (ushort)(page << 8);
        if (vramPageHot && addr >= 0x0300 && addr < 0x0400)
            return VramBufBase(vramRamTargets);
        return addr;
    }

    static int SizeFromTraffic(
        ushort bas,
        int maxSize,
        Dictionary<ushort, int> accessCounts,
        Dictionary<ushort, int> maxIndex)
    {
        int size = 1;
        if (maxIndex.TryGetValue(bas, out int need))
            size = Math.Max(size, need);
        foreach (var (addr, count) in accessCounts)
        {
            if (count <= 0 || addr < bas || addr >= bas + maxSize)
                continue;
            int span = (addr - bas) + 1;
            if (maxIndex.TryGetValue(addr, out int need2))
                span = (addr - bas) + need2;
            size = Math.Max(size, span);
        }
        return size;
    }

    static List<ushort> NormalizeBases(
        HashSet<ushort> candidates,
        HashSet<byte> oamPages,
        bool vramPageHot,
        HashSet<ushort> vramRamTargets,
        Dictionary<ushort, int> accessCounts)
    {
        ushort vramBase = VramBufBase(vramRamTargets);
        var list = candidates
            .Select(a => CanonicalBase(a, oamPages, vramPageHot, vramRamTargets))
            .Distinct()
            .OrderBy(a => a)
            .ToList();
        var kept = new List<ushort>();
        foreach (ushort addr in list)
        {
            byte page = (byte)(addr >> 8);
            if (oamPages.Contains(page) && (addr & 0xFF) != 0)
                continue;
            if (vramPageHot && addr > vramBase && addr < vramBase + 256 && addr < 0x0400)
                continue;

            bool absorbed = false;
            foreach (ushort earlier in kept)
            {
                int earlierSpan = oamPages.Contains((byte)(earlier >> 8)) && (earlier & 0xFF) == 0 ? 256
                    : vramPageHot && earlier == vramBase ? Math.Min(256, 0x0400 - earlier)
                    : 16; // tight generic absorb window; sizing expands via traffic
                if (addr > earlier && addr < earlier + earlierSpan
                    && accessCounts.GetValueOrDefault(earlier) >= accessCounts.GetValueOrDefault(addr))
                {
                    absorbed = true;
                    break;
                }
            }
            if (!absorbed)
                kept.Add(addr);
        }
        return kept;
    }

    static HashSet<byte> CollectOamDmaPages(IEnumerable<List<string>> bodies)
    {
        var pages = new HashSet<byte>();
        foreach (var body in bodies)
        {
            foreach (string raw in body)
            {
                foreach (Match m in OamDmaCall.Matches(raw))
                {
                    if (TryParseByteLiteral(m.Groups["page"].Value, out byte page))
                        pages.Add(page);
                }
            }
        }
        return pages;
    }

    static HashSet<ushort> CollectVramBufRamTargets(
        IReadOnlyList<PaletteTableRecovery.VramPointerTable>? tables)
    {
        var set = new HashSet<ushort>();
        if (tables == null)
            return set;
        foreach (var t in tables)
        {
            foreach (ushort p in t.Pointers)
            {
                if (p < 0x2000)
                    set.Add(p);
            }
        }
        return set;
    }

    static void NoteIndexedAccesses(
        string line,
        IReadOnlyDictionary<ushort, string> nameToAddr,
        Dictionary<ushort, int> accessCounts,
        Dictionary<ushort, int> maxIndex)
    {
        // Reverse map name → addr for named forms after promote.
        var addrByName = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var (addr, name) in nameToAddr)
            addrByName[name] = addr;

        foreach (Match m in IndexedHex.Matches(line))
        {
            if (!ushort.TryParse(m.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort addr))
                continue;
            if (addr >= 0x2000)
                continue;
            accessCounts[addr] = accessCounts.GetValueOrDefault(addr) + 1;
            // Unknown max index — at least 1 element of traffic.
            if (!maxIndex.ContainsKey(addr))
                maxIndex[addr] = 1;
        }

        foreach (Match m in IndexedNamed.Matches(line))
        {
            string name = m.Groups["name"].Value;
            if (name is "a" or "x" or "y" or "ushort")
                continue;
            if (!addrByName.TryGetValue(name, out ushort addr) && !TryAddrFromName(name, out addr))
                continue;
            if (addr >= 0x2000)
                continue;
            accessCounts[addr] = accessCounts.GetValueOrDefault(addr) + 1;
            if (!maxIndex.ContainsKey(addr))
                maxIndex[addr] = 1;
        }

        // for (x = 0; x != N; x++) / x < N near indexed use — bump size hint when N is small.
        var forBound = Regex.Match(line,
            @"for\s*\(\s*(x|y)\s*=\s*(?:0x[0-9A-Fa-f]+|\d+)\s*;\s*\1\s*(?:!=|<)\s*(0x[0-9A-Fa-f]+|\d+)");
        if (forBound.Success && TryParseByteLiteral(forBound.Groups[2].Value, out byte bound) && bound > 0)
        {
            // Apply to all bases touched on nearby lines is hard here; stash as soft global hint via maxIndex keys later.
            _ = bound;
        }
    }

    static bool TryAddrFromName(string name, out ushort addr)
    {
        addr = 0;
        if (name.StartsWith("ram_", StringComparison.Ordinal)
            && ushort.TryParse(name.AsSpan(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        if (name.StartsWith("zp_", StringComparison.Ordinal)
            && ushort.TryParse(name.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        if (name.StartsWith("buf_", StringComparison.Ordinal)
            && ushort.TryParse(name.AsSpan(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        return false;
    }

    static bool TryParseByteLiteral(string text, out byte value)
    {
        value = 0;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Rewrite <c>peek((ushort)(base + i))</c> / <c>poke((ushort)(base + i), v)</c> to <c>buf[i]</c> forms.
    /// Also rewrites named const bases that map into a buffer (including mid-buffer offsets).
    /// </summary>
    public static void RewriteBodies(
        List<List<string>> bodies,
        IReadOnlyList<RamBuffer> buffers,
        IReadOnlyDictionary<ushort, string> addrConsts)
    {
        if (buffers.Count == 0)
            return;

        // addr → (buffer name, offset within buffer)
        var map = new Dictionary<ushort, (string Name, int Offset)>();
        foreach (var b in buffers)
        {
            for (int o = 0; o < b.Size; o++)
                map[(ushort)(b.Base + o)] = (b.Name, o);
        }

        // Also resolve const names already in bodies.
        var nameToAbs = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var (addr, name) in addrConsts)
            nameToAbs[name] = addr;
        foreach (var b in buffers)
            nameToAbs[b.Name] = b.Base;

        foreach (var body in bodies)
        {
            for (int i = 0; i < body.Count; i++)
                body[i] = RewriteLine(body[i], map, nameToAbs);
        }
    }

    static string RewriteLine(
        string line,
        Dictionary<ushort, (string Name, int Offset)> map,
        Dictionary<string, ushort> nameToAbs)
    {
        // poke((ushort)(BASE + idx), VAL) → buf[idx] = VAL  or buf[idx + off] = VAL
        line = RewriteIndexedPokes(line, map, nameToAbs);
        line = RewriteIndexedPeeks(line, map, nameToAbs);
        // Absolute mid-buffer: peek(ram_0300) / poke(ram_0300, v) → vram_buf[0]
        line = RewriteAbsolutePokes(line, map, nameToAbs);
        line = RewriteAbsolutePeeks(line, map, nameToAbs);
        return line;
    }

    static string FormatBufIndex((string Name, int Offset) hit) =>
        hit.Offset == 0 ? $"{hit.Name}[0]" : $"{hit.Name}[0x{hit.Offset:X2}]";

    static string RewriteIndexedPeeks(
        string line,
        Dictionary<ushort, (string Name, int Offset)> map,
        Dictionary<string, ushort> nameToAbs)
    {
        // peek((ushort)(0x0200 + y)) / peek((ushort)(ram_0200 + y)) / peek((ushort)((ushort)0x0200 + y))
        return Regex.Replace(line,
            @"peek\(\(ushort\)\((?:\(ushort\))?(?<base>0x[0-9A-Fa-f]+|\w+)\s*\+\s*(?<idx>x|y)\)\)",
            m =>
            {
                if (!TryResolveBase(m.Groups["base"].Value, nameToAbs, out ushort addr))
                    return m.Value;
                if (!map.TryGetValue(addr, out var hit))
                    return m.Value;
                string idx = m.Groups["idx"].Value;
                return hit.Offset == 0
                    ? $"{hit.Name}[{idx}]"
                    : $"{hit.Name}[{idx} + {hit.Offset}]";
            });
    }

    static string RewriteAbsolutePeeks(
        string line,
        Dictionary<ushort, (string Name, int Offset)> map,
        Dictionary<string, ushort> nameToAbs)
    {
        return Regex.Replace(line,
            @"peek\((?:\(ushort\))?(?<base>0x[0-9A-Fa-f]+|\w+)\)",
            m =>
            {
                if (!TryResolveBase(m.Groups["base"].Value, nameToAbs, out ushort addr))
                    return m.Value;
                if (!map.TryGetValue(addr, out var hit))
                    return m.Value;
                return FormatBufIndex(hit);
            });
    }

    static string RewriteIndexedPokes(
        string line,
        Dictionary<ushort, (string Name, int Offset)> map,
        Dictionary<string, ushort> nameToAbs)
    {
        // Must balance value arg — reuse simple pattern for common cases.
        var sb = new StringBuilder(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            int at = line.IndexOf("poke(", i, StringComparison.Ordinal);
            if (at < 0)
            {
                sb.Append(line, i, line.Length - i);
                break;
            }
            sb.Append(line, i, at - i);
            int argStart = at + "poke(".Length;
            if (!TryReadIndexedBaseArg(line, argStart, out string baseTok, out string idx, out int afterBaseComma)
                || !TryReadBalancedArg(line, afterBaseComma, out string val, out int afterVal)
                || afterVal >= line.Length || line[afterVal] != ')')
            {
                sb.Append(line[at]);
                i = at + 1;
                continue;
            }

            if (!TryResolveBase(baseTok, nameToAbs, out ushort addr) || !map.TryGetValue(addr, out var hit))
            {
                sb.Append(line, at, afterVal + 1 - at);
                i = afterVal + 1;
                continue;
            }

            string indexExpr = hit.Offset == 0 ? idx : $"{idx} + {hit.Offset}";
            sb.Append(hit.Name).Append('[').Append(indexExpr).Append("] = ").Append(val);
            i = afterVal + 1;
        }
        return sb.ToString();
    }

    static string RewriteAbsolutePokes(
        string line,
        Dictionary<ushort, (string Name, int Offset)> map,
        Dictionary<string, ushort> nameToAbs)
    {
        var sb = new StringBuilder(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            int at = line.IndexOf("poke(", i, StringComparison.Ordinal);
            if (at < 0)
            {
                sb.Append(line, i, line.Length - i);
                break;
            }
            sb.Append(line, i, at - i);
            int argStart = at + "poke(".Length;
            // Skip already-indexed forms (ushort)(
            if (StartsWithAt(line, argStart, "(ushort)("))
            {
                sb.Append(line[at]);
                i = at + 1;
                continue;
            }

            int comma = -1;
            int depth = 0;
            for (int j = argStart; j < line.Length; j++)
            {
                char c = line[j];
                if (c == '(') depth++;
                else if (c == ')') { if (depth == 0) break; depth--; }
                else if (c == ',' && depth == 0) { comma = j; break; }
            }
            if (comma < 0)
            {
                sb.Append(line[at]);
                i = at + 1;
                continue;
            }

            string baseTok = line[argStart..comma].Trim();
            if (baseTok.StartsWith("(ushort)", StringComparison.Ordinal))
                baseTok = baseTok["(ushort)".Length..].Trim();
            int valStart = comma + 1;
            while (valStart < line.Length && char.IsWhiteSpace(line[valStart]))
                valStart++;
            if (!TryReadBalancedArg(line, valStart, out string val, out int afterVal)
                || afterVal >= line.Length || line[afterVal] != ')')
            {
                sb.Append(line[at]);
                i = at + 1;
                continue;
            }

            if (!TryResolveBase(baseTok, nameToAbs, out ushort addr) || !map.TryGetValue(addr, out var hit))
            {
                sb.Append(line, at, afterVal + 1 - at);
                i = afterVal + 1;
                continue;
            }

            sb.Append(FormatBufIndex(hit)).Append(" = ").Append(val);
            i = afterVal + 1;
        }
        return sb.ToString();
    }

    static bool TryReadIndexedBaseArg(
        string line, int start, out string baseTok, out string idx, out int afterComma)
    {
        baseTok = "";
        idx = "";
        afterComma = start;
        // Expect (ushort)(BASE + x|y),
        if (!StartsWithAt(line, start, "(ushort)("))
            return false;
        int inner = start + "(ushort)(".Length;
        // Optional nested (ushort)
        if (StartsWithAt(line, inner, "(ushort)"))
            inner += "(ushort)".Length;
        int plus = line.IndexOf('+', inner);
        if (plus < 0)
            return false;
        baseTok = line[inner..plus].Trim();
        int idxStart = plus + 1;
        while (idxStart < line.Length && char.IsWhiteSpace(line[idxStart]))
            idxStart++;
        if (idxStart >= line.Length)
            return false;
        if (line[idxStart] is not ('x' or 'y'))
            return false;
        idx = line[idxStart].ToString();
        int close = idxStart + 1;
        while (close < line.Length && char.IsWhiteSpace(line[close]))
            close++;
        if (close >= line.Length || line[close] != ')')
            return false;
        close++; // past )
        while (close < line.Length && char.IsWhiteSpace(line[close]))
            close++;
        if (close >= line.Length || line[close] != ',')
            return false;
        afterComma = close + 1;
        while (afterComma < line.Length && char.IsWhiteSpace(line[afterComma]))
            afterComma++;
        return true;
    }

    static bool StartsWithAt(string s, int i, string lit) =>
        i + lit.Length <= s.Length && string.CompareOrdinal(s, i, lit, 0, lit.Length) == 0;

    static bool TryReadBalancedArg(string line, int start, out string arg, out int end)
    {
        arg = "";
        end = start;
        if (start >= line.Length)
            return false;
        int depth = 0;
        int i = start;
        for (; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '(')
                depth++;
            else if (c == ')')
            {
                if (depth == 0)
                    break;
                depth--;
            }
            else if (c == ',' && depth == 0)
                break;
        }
        arg = line[start..i].Trim();
        end = i;
        return arg.Length > 0;
    }

    static bool TryResolveBase(string tok, Dictionary<string, ushort> nameToAbs, out ushort addr)
    {
        addr = 0;
        tok = tok.Trim();
        if (tok.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(tok.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        if (nameToAbs.TryGetValue(tok, out addr))
            return true;
        return TryAddrFromName(tok, out addr);
    }

    /// <summary>Remove addr consts / ram vars absorbed into buffers; keep buffer bases out of const ushort list.</summary>
    public static void AbsorbedAddresses(
        IReadOnlyList<RamBuffer> buffers,
        out HashSet<ushort> covered)
    {
        covered = new HashSet<ushort>();
        foreach (var b in buffers)
        {
            for (int o = 0; o < b.Size; o++)
                covered.Add((ushort)(b.Base + o));
        }
    }
}
