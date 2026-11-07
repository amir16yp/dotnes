using System.Text;

namespace dotnes;

/// <summary>
/// Recovers null-terminated in-ROM string tables by discovering a contiguous
/// tile-font alphabet (common on NES: <c>A..Z</c> as <c>base..base+25</c>, optional
/// digits immediately below, space as a frequent non-letter pad byte).
/// Works across games — no per-title charset tables.
/// </summary>
static class GameTextTableRecovery
{
    public readonly record struct TileCharset(byte LetterBase, byte Space, string Description)
    {
        public byte DigitBase => LetterBase >= 10 ? (byte)(LetterBase - 10) : (byte)0;
        public bool HasDigits => LetterBase >= 10;
    }

    public readonly record struct GameString(
        int Bank, ushort CpuAddress, int PrgOffset, byte[] Data, string Decoded, TileCharset Charset);

    /// <summary>
    /// Scan PRG banks for the charset + string clusters that score best as English-like text.
    /// </summary>
    public static List<GameString> FindTextTables(ReadOnlySpan<byte> prg, int maxStrings = 256)
    {
        var found = new List<GameString>();
        if (prg.Length < 0x4000)
            return found;

        // Pick charset with a cheap score pass, then collect once.
        TileCharset? bestCharset = null;
        int bestScore = 0;
        foreach (var cs in CandidateCharsets())
        {
            int score = ScoreCharsetQuick(prg, cs);
            if (score > bestScore)
            {
                bestScore = score;
                bestCharset = cs;
            }
        }

        // Weak / non-English scores → no gameText table (call sites keep raw byte[]).
        if (bestCharset is null || bestScore < 60)
            return found;

        var csWin = bestCharset.Value;
        int banks = prg.Length / 0x4000;
        for (int bank = 0; bank < banks && found.Count < maxStrings; bank++)
        {
            int baseOff = bank * 0x4000;
            var bankSpan = prg.Slice(baseOff, Math.Min(0x4000, prg.Length - baseOff));
            CollectClusters(bankSpan, bank, baseOff, csWin, found, maxStrings);
        }

        return found;
    }

    /// <summary>
    /// Rank charset by its best English hits only — volume of weak garbage must not win.
    /// </summary>
    static int ScoreCharsetQuick(ReadOnlySpan<byte> prg, TileCharset cs)
    {
        // Keep top-N hit scores without sorting a huge list.
        Span<int> top = stackalloc int[24];
        int topCount = 0;
        int banks = prg.Length / 0x4000;
        for (int bank = 0; bank < banks; bank++)
        {
            int baseOff = bank * 0x4000;
            var bankSpan = prg.Slice(baseOff, Math.Min(0x4000, prg.Length - baseOff));
            int i = 0;
            int hits = 0;
            while (i < bankSpan.Length && hits < 64)
            {
                if (TryReadString(bankSpan, i, cs, out int len, out string decoded) && IsStrong(decoded))
                {
                    int e = Englishness(decoded);
                    if (e > 0)
                    {
                        InsertTop(top, ref topCount, e);
                        hits++;
                    }
                    i += len;
                }
                else
                    i++;
            }
        }

        // Need a small cluster of solid words; a lone lucky hit is not a text table.
        if (topCount < 3)
            return 0;
        int score = 0;
        for (int i = 0; i < topCount; i++)
            score += top[i];
        return score;
    }

    static void InsertTop(Span<int> top, ref int count, int value)
    {
        if (count < top.Length)
        {
            top[count++] = value;
            return;
        }
        int minIdx = 0;
        for (int i = 1; i < count; i++)
        {
            if (top[i] < top[minIdx])
                minIdx = i;
        }
        if (value > top[minIdx])
            top[minIdx] = value;
    }

    public static List<string> FormatDeclarations(IReadOnlyList<GameString> strings)
    {
        var lines = new List<string>();
        if (strings.Count == 0)
            return lines;

        var cs = strings[0].Charset;
        lines.Add($"// Game text tables (null-terminated). Discovered tile font: {cs.Description}");
        lines.Add("(int Bank, ushort Address, byte[] Data, string Text)[] gameText =");
        lines.Add("{");
        for (int i = 0; i < strings.Count; i++)
        {
            var s = strings[i];
            string comma = i < strings.Count - 1 ? "," : "";
            lines.Add(
                $"    ({s.Bank}, 0x{s.CpuAddress:X4}, {FormatBytes(s.Data)}, \"{Escape(s.Decoded)}\"){comma}");
        }
        lines.Add("};");
        return lines;
    }

    /// <summary>
    /// Decode bytes with an auto-picked charset for comments / vram_write hints.
    /// Returns false when no English-like reading exists — callers should keep raw <c>byte[]</c>.
    /// </summary>
    public static bool TryDecodeTiles(IReadOnlyList<byte> bytes, out string text, out TileCharset charset)
    {
        text = "";
        charset = default;
        int bestScore = 0;
        string best = "";
        TileCharset bestCs = default;
        foreach (var cs in CandidateCharsets())
        {
            if (!TryDecode(bytes, cs, out string decoded))
                continue;
            if (!IsUsable(decoded) || !HasVowel(decoded))
                continue;
            int score = Englishness(decoded);
            if (score > bestScore)
            {
                bestScore = score;
                best = decoded;
                bestCs = cs;
            }
        }
        // Require real English signal (vowels + letters/bigrams), not consonant soup.
        if (bestScore < 12)
            return false;
        text = best;
        charset = bestCs;
        return true;
    }

    static IEnumerable<TileCharset> CandidateCharsets()
    {
        // Contiguous A..Z bases seen across commercial NES titles + ASCII.
        byte[] letterBases =
        [
            0x0A, // SMB status / many tile HUDs (0=digit0, A=$0A)
            0x01, 0x41, 0x61,
            0x80, 0x81, 0x85, 0x8A, 0x90, 0x99, 0xA0, 0xA1,
        ];
        byte[] spaces = [0xFF, 0x24, 0x20, 0x7A]; // terminator $00 is never a space glyph

        foreach (byte letterBase in letterBases)
        {
            if (letterBase > 0xFF - 25)
                continue;
            foreach (byte space in spaces)
            {
                if (space >= letterBase && space <= letterBase + 25)
                    continue; // space can't sit inside A..Z
                if (letterBase >= 10)
                {
                    byte dig0 = (byte)(letterBase - 10);
                    if (space >= dig0 && space < letterBase)
                        continue; // space inside digit run
                }
                string desc = $"A=${letterBase:X2}…Z=${letterBase + 25:X2}, space=${space:X2}, end=$00";
                if (letterBase >= 10)
                    desc = $"digits=${letterBase - 10:X2}…${letterBase - 1:X2}, " + desc;
                yield return new TileCharset(letterBase, space, desc);
            }
        }
    }

    static void CollectClusters(
        ReadOnlySpan<byte> bank,
        int bankIndex,
        int fileBase,
        TileCharset cs,
        List<GameString> dest,
        int maxStrings)
    {
        int i = 0;
        while (i < bank.Length && dest.Count < maxStrings)
        {
            if (!TryReadString(bank, i, cs, out int len, out string decoded) || !IsStrong(decoded))
            {
                i++;
                continue;
            }

            int clusterStart = i;
            int clusterEnd = i + len;
            int back = clusterStart - 1;
            while (back > 0 && clusterStart - back < 8)
            {
                int t = back;
                while (t > 0 && bank[t] != 0)
                    t--;
                int start = bank[t] == 0 ? t + 1 : t;
                if (start < back
                    && TryReadString(bank, start, cs, out int plen, out string pdec)
                    && IsStrong(pdec)
                    && start + plen == clusterStart)
                {
                    clusterStart = start;
                    back = clusterStart - 1;
                    continue;
                }
                break;
            }

            var cluster = new List<GameString>();
            int p = clusterStart;
            while (p < bank.Length && dest.Count + cluster.Count < maxStrings)
            {
                while (p < bank.Length && bank[p] == 0)
                    p++;
                if (p >= bank.Length)
                    break;
                if (!TryReadString(bank, p, cs, out int slen, out string sdec))
                    break;
                // Cluster members must themselves look English — no padding with alphabet junk.
                if (!IsStrong(sdec))
                    break;

                ushort cpu = (ushort)(0x8000 + p);
                byte[] data = bank.Slice(p, slen).ToArray();
                cluster.Add(new GameString(bankIndex, cpu, fileBase + p, data, sdec, cs));
                p += slen;
                clusterEnd = p;

                int pad = 0;
                while (p + pad < bank.Length && bank[p + pad] == 0 && pad < 3)
                    pad++;
                if (pad > 0 && p + pad < bank.Length && IsLetter(bank[p + pad], cs))
                {
                    p += pad;
                    continue;
                }
                if (pad > 0)
                    break;
            }

            if (cluster.Count >= 3)
            {
                foreach (var s in cluster)
                    dest.Add(s);
                i = Math.Max(i + 1, clusterEnd);
            }
            else
                i++;
        }
    }

    static bool TryReadString(
        ReadOnlySpan<byte> bank, int start, TileCharset cs,
        out int lengthWithNul, out string decoded)
    {
        lengthWithNul = 0;
        decoded = "";
        if ((uint)start >= (uint)bank.Length)
            return false;

        int j = start;
        while (j < bank.Length && bank[j] != 0)
        {
            j++;
            if (j - start > 48)
                return false;
        }
        if (j >= bank.Length)
            return false;

        int payload = j - start;
        if (payload < 2)
            return false;

        if (!TryDecode(bank.Slice(start, payload), cs, out decoded))
            return false;

        lengthWithNul = payload + 1;
        return true;
    }

    static bool TryDecode(IReadOnlyList<byte> bytes, TileCharset cs, out string text)
    {
        var tmp = new byte[bytes.Count];
        for (int i = 0; i < bytes.Count; i++)
            tmp[i] = bytes[i];
        return TryDecode(tmp.AsSpan(), cs, out text);
    }

    static bool TryDecode(ReadOnlySpan<byte> bytes, TileCharset cs, out string text)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            if (cs.Space != 0 && b == cs.Space)
            {
                sb.Append(' ');
                continue;
            }
            if (IsLetter(b, cs))
            {
                sb.Append((char)('A' + (b - cs.LetterBase)));
                continue;
            }
            if (cs.HasDigits && b >= cs.DigitBase && b < cs.LetterBase)
            {
                sb.Append((char)('0' + (b - cs.DigitBase)));
                continue;
            }
            // Unknown glyph — reject (keeps charset discovery honest).
            text = "";
            return false;
        }
        text = sb.ToString();
        return true;
    }

    static bool IsLetter(byte b, TileCharset cs) =>
        b >= cs.LetterBase && b <= cs.LetterBase + 25;

    static bool IsStrong(string decoded)
    {
        int letters = 0, spaces = 0;
        foreach (char c in decoded)
        {
            if (c is >= 'A' and <= 'Z')
                letters++;
            else if (c == ' ')
                spaces++;
            else if (c is < '0' or > '9')
                return false;
        }
        int bigrams = CountBigrams(decoded);
        int pairs = Math.Max(1, letters - 1);
        // Real words: a healthy fraction of adjacent pairs are common English bigrams.
        return letters >= 3
            && letters <= 16 // dialogue words / spell names; long runs are tables/fills
            && HasVowel(decoded)
            && UniqueLetters(decoded) >= 3
            && !IsMonotone(decoded)
            && !IsAlphabetRun(decoded)
            && bigrams >= 1
            && bigrams * 2 >= pairs
            && letters >= (decoded.Length - spaces) * 2 / 3;
    }

    static bool IsUsable(string decoded)
    {
        int letters = 0, spaces = 0;
        foreach (char c in decoded)
        {
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                letters++;
            else if (c == ' ')
                spaces++;
            else
                return false;
        }
        int payload = decoded.Length - spaces;
        return letters >= 3
            && payload >= 3
            && letters >= payload * 2 / 3
            && UniqueLetters(decoded) >= 2
            && !IsMonotone(decoded)
            && !IsAlphabetRun(decoded);
    }

    /// <summary>Reject sorted fill like ABC / YXWV that low letter-bases invent from counters.</summary>
    static bool IsAlphabetRun(string s)
    {
        var letters = new List<char>();
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z')
                letters.Add(c);
        }
        if (letters.Count < 3)
            return false;
        int asc = 0, desc = 0;
        for (int i = 1; i < letters.Count; i++)
        {
            int d = letters[i] - letters[i - 1];
            if (d == 1) asc++;
            else if (d == -1) desc++;
        }
        int steps = letters.Count - 1;
        return asc * 2 >= steps || desc * 2 >= steps;
    }

    static readonly string[] CommonBigrams =
    [
        "TH", "HE", "IN", "ER", "AN", "RE", "ON", "AT", "EN", "ND",
        "ST", "ES", "OF", "TE", "OR", "TI", "HI", "AS", "TO", "IS",
        "IT", "NG", "AR", "SE", "AL", "ED", "LE", "OU", "VE", "ME",
        "CU", "UR", "FI", "IR", "HA", "IC", "CE", "MA", "AG",
        "LO", "CK", "MU", "UT", "FR", "OS", "EA", "GL", "FL", "SC",
        "CR", "SQ", "BL", "ZZ", "LA", "RA", "SH", "CH", "QU", "WH",
        "GH", "LY", "ND", "RD", "LD", "NT", "RT", "CT", "PT", "FT",
        "RK", "SK", "SP", "TR", "BR", "DR", "GR", "PR", "WR", "CL",
        "PL", "SL", "SM", "SN", "SW", "TW", "EE", "OO", "AI", "AY",
        "OY", "OI", "AU", "AW", "OW", "EW", "IE", "EI", "UE", "UI",
    ];

    static int CountBigrams(string s)
    {
        int n = 0;
        foreach (string bg in CommonBigrams)
        {
            int idx = 0;
            while ((idx = s.IndexOf(bg, idx, StringComparison.Ordinal)) >= 0)
            {
                n++;
                idx += bg.Length;
            }
        }
        return n;
    }

    static int UniqueLetters(string s)
    {
        int mask = 0;
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z')
                mask |= 1 << (c - 'A');
        }
        int n = 0;
        while (mask != 0)
        {
            n += mask & 1;
            mask >>= 1;
        }
        return n;
    }

    /// <summary>True when one letter dominates (fill/pattern data decoded as fake words).</summary>
    static bool IsMonotone(string s)
    {
        int letters = 0;
        int[] hist = new int[26];
        foreach (char c in s)
        {
            if (c is < 'A' or > 'Z')
                continue;
            letters++;
            hist[c - 'A']++;
        }
        if (letters < 3)
            return false;
        int max = 0;
        foreach (int h in hist)
        {
            if (h > max)
                max = h;
        }
        return max * 3 >= letters * 2; // ≥⅔ same letter → fill/pattern junk
    }

    static int ScoreTable(List<GameString> strings)
    {
        int score = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in strings)
        {
            if (!seen.Add(s.Decoded))
            {
                score += 1; // duplicate still evidence of a real table
                continue;
            }
            score += Englishness(s.Decoded);
        }
        return score;
    }

    /// <summary>
    /// Rank a decoded payload by how English-like it looks so contiguous-tile
    /// alphabets that merely fit the bytes lose to ones that produce real words.
    /// </summary>
    static bool HasVowel(string s)
    {
        foreach (char c in s)
        {
            if (c is 'A' or 'E' or 'I' or 'O' or 'U')
                return true;
        }
        return false;
    }

    static int Englishness(string s)
    {
        int letters = 0, vowels = 0, spaces = 0;
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z')
            {
                letters++;
                if (c is 'A' or 'E' or 'I' or 'O' or 'U')
                    vowels++;
            }
            else if (c == ' ')
                spaces++;
        }
        if (letters < 2)
            return 0;

        if (vowels == 0 || IsMonotone(s))
            return 0;

        int bigrams = CountBigrams(s);
        if (bigrams == 0)
            return 0;

        int unique = UniqueLetters(s);
        int score = letters + vowels * 3 + unique * 2 + bigrams * 6;

        if (spaces > 0)
            score += spaces;
        if (letters >= 4 && letters <= 12 && spaces == 0)
            score += 3; // typical single dictionary words
        return score;
    }

    static int CountLetters(string s)
    {
        int n = 0;
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z')
                n++;
        }
        return n;
    }

    static string FormatBytes(byte[] data)
    {
        var sb = new StringBuilder("new byte[] { ");
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"0x{data[i]:X2}");
        }
        sb.Append(" }");
        return sb.ToString();
    }

    static string Escape(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
