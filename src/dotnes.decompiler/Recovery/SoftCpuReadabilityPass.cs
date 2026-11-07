using System.Globalization;
using System.Text.RegularExpressions;

namespace dotnes;

/// <summary>
/// Post-buffer SoftCpu readability: size-1 demotion, OAM fields, RMW, carry/flags,
/// OAM clear fors, switch chains, indirect ptr[y], hot buffer slots, goto cleanup.
/// </summary>
static class SoftCpuReadabilityPass
{
    static readonly string[] OamFields = ["oam_y", "oam_tile", "oam_attr", "oam_x"];

    public readonly record struct RomTable(ushort Base, int Length, string Name, byte[] Bytes);

    /// <summary>
    /// Demote <c>buf_XXXX</c> of size 1 (no indexed x|y use) into scalar RAM vars.
    /// Size-1 buffers that <em>are</em> indexed get expanded to the gap before the next buffer
    /// (clamp 2..16) so the decl matches abs,X/Y traffic.
    /// </summary>
    public static List<BufferRecovery.RamBuffer> DemoteSize1Buffers(
        List<List<string>> bodies,
        List<BufferRecovery.RamBuffer> buffers,
        Dictionary<ushort, string> ramVars,
        out Dictionary<ushort, string> promotedScalars)
    {
        promotedScalars = new Dictionary<ushort, string>();
        if (buffers.Count == 0)
            return buffers;

        var ordered = buffers.OrderBy(b => b.Base).ToList();
        var keep = new List<BufferRecovery.RamBuffer>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            if (b.Size != 1 || b.Name is "oam" or "vram_buf" || b.Name.StartsWith("oam_", StringComparison.Ordinal))
            {
                keep.Add(b);
                continue;
            }

            if (BodiesUseIndexedBuffer(bodies, b.Name))
            {
                ushort next = i + 1 < ordered.Count ? ordered[i + 1].Base : (ushort)(b.Base + 16);
                int gap = Math.Max(2, next - b.Base);
                int size = Math.Clamp(gap, 2, 16);
                keep.Add(b with { Size = size });
                continue;
            }

            // Rewrite buf[0] / absolute forms → scalar name.
            string scalar = b.Name.StartsWith("buf_", StringComparison.Ordinal)
                ? (b.Base <= 0xFF ? $"zp_{b.Base:X2}" : $"ram_{b.Base:X4}")
                : b.Name;
            RewriteSize1ToScalar(bodies, b.Name, scalar);
            ramVars[b.Base] = scalar;
            promotedScalars[b.Base] = scalar;
        }
        return keep.OrderBy(b => b.Base).ToList();
    }

    /// <summary>
    /// Rename generic <c>buf_XXXX</c> from usage: pad page, score digits, dense game-state block.
    /// </summary>
    public static List<BufferRecovery.RamBuffer> ApplyBufferRoleNames(
        List<List<string>> bodies,
        List<BufferRecovery.RamBuffer> buffers,
        HashSet<ushort>? padAddrs)
    {
        if (buffers.Count == 0)
            return buffers;

        var renamed = new List<BufferRecovery.RamBuffer>();
        var used = new HashSet<string>(buffers.Select(b => b.Name), StringComparer.Ordinal);
        foreach (var b in buffers)
        {
            if (b.Name is "oam" or "vram_buf" || b.Name.StartsWith("oam_", StringComparison.Ordinal)
                || !b.Name.StartsWith("buf_", StringComparison.Ordinal))
            {
                renamed.Add(b);
                continue;
            }

            string? role = null;
            if (padAddrs != null && padAddrs.Any(p => p >= b.Base && p < b.Base + b.Size))
                role = "pad_buf";
            else if (b.Base == 0x07D7 || (b.Base >= 0x07D0 && b.Base < 0x07E0 && b.Size >= 4))
                role = "score";
            else if (b.Base == 0x06FC || (b.Base >= 0x06F0 && b.Base <= 0x06FC && b.Size >= 32))
                role = "game";
            else if (b.Base >= 0x001E && b.Base < 0x0100 && b.Size >= 8
                     && BodiesUseIndexedBuffer(bodies, b.Name))
                role = "obj"; // ZP object/slot arrays (SMB-style)

            if (role == null)
            {
                renamed.Add(b);
                continue;
            }

            string neu = role;
            if (!used.Add(neu))
                neu = $"{role}_{b.Base:X4}";
            used.Add(neu);
            RenameBufferInBodies(bodies, b.Name, neu);
            renamed.Add(b with { Name = neu });
        }
        return renamed;
    }

    static void RenameBufferInBodies(List<List<string>> bodies, string oldName, string newName)
    {
        var rx = new Regex(@"\b" + Regex.Escape(oldName) + @"\b", RegexOptions.Compiled);
        foreach (var body in bodies)
        {
            for (int i = 0; i < body.Count; i++)
                body[i] = rx.Replace(body[i], newName);
        }
    }

    static bool BodiesUseIndexedBuffer(List<List<string>> bodies, string name)
    {
        var rx = new Regex(@"\b" + Regex.Escape(name) + @"\[(?:x|y)\b", RegexOptions.Compiled);
        foreach (var body in bodies)
        {
            foreach (string line in body)
            {
                if (rx.IsMatch(line))
                    return true;
            }
        }
        return false;
    }

    static void RewriteSize1ToScalar(List<List<string>> bodies, string bufName, string scalar)
    {
        var idx0 = new Regex(@"\b" + Regex.Escape(bufName) + @"\[(?:0|0x0+)\]", RegexOptions.Compiled);
        var bare = new Regex(@"\b" + Regex.Escape(bufName) + @"\b", RegexOptions.Compiled);
        foreach (var body in bodies)
        {
            for (int i = 0; i < body.Count; i++)
            {
                string line = idx0.Replace(body[i], scalar);
                // Only replace remaining bare name if no index left.
                if (line.Contains(bufName + "[", StringComparison.Ordinal))
                {
                    body[i] = line;
                    continue;
                }
                body[i] = bare.Replace(line, scalar);
            }
        }
    }

    /// <summary>
    /// <c>oam[y]</c> / <c>oam[y + 1]</c> → <c>oam_y[y]</c> / <c>oam_tile[y]</c> (index stays byte offset).
    /// </summary>
    public static void RewriteOamFields(List<List<string>> bodies, List<BufferRecovery.RamBuffer> buffers)
    {
        if (!buffers.Any(b => b.Name == "oam"))
            return;

        // oam[idx + N] / oam[idx]
        var rx = new Regex(
            @"\boam\[(?<idx>x|y)(?:\s*\+\s*(?<off>0x[0-9A-Fa-f]+|\d+))?\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var abs = new Regex(
            @"\boam\[(?<off>0x[0-9A-Fa-f]+|\d+)\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        foreach (var body in bodies)
        {
            for (int i = 0; i < body.Count; i++)
            {
                body[i] = rx.Replace(body[i], m =>
                {
                    int off = 0;
                    if (m.Groups["off"].Success)
                        TryParseInt(m.Groups["off"].Value, out off);
                    int field = off & 3;
                    int rest = off - field;
                    string name = OamFields[field];
                    string idx = m.Groups["idx"].Value;
                    return rest == 0 ? $"{name}[{idx}]" : $"{name}[{idx} + {rest}]";
                });
                body[i] = abs.Replace(body[i], m =>
                {
                    if (!TryParseInt(m.Groups["off"].Value, out int off))
                        return m.Value;
                    int field = off & 3;
                    int spriteByte = off - field; // keep absolute sprite-row base in index
                    string name = OamFields[field];
                    return spriteByte == 0 ? $"{name}[0]" : $"{name}[0x{spriteByte:X2}]";
                });
            }
        }

        // SoftCpu resolves oam_y/tile/attr/x via name→$0200+field; keep single oam buffer for coverage.
    }

    public static IEnumerable<string> FormatOamFieldDecls(IReadOnlyList<BufferRecovery.RamBuffer> buffers)
    {
        foreach (var b in buffers)
        {
            if (b.Name != "oam")
                continue;
            yield return $"byte[] oam_y = new byte[256]; // ${b.Base:X4} (OAM Y)";
            yield return $"byte[] oam_tile = new byte[256]; // ${(b.Base + 1):X4} (OAM tile)";
            yield return $"byte[] oam_attr = new byte[256]; // ${(b.Base + 2):X4} (OAM attr)";
            yield return $"byte[] oam_x = new byte[256]; // ${(b.Base + 3):X4} (OAM X)";
            yield break;
        }
    }

    /// <summary><c>buf[i] = (byte)(buf[i] ± 1)</c> → <c>buf[i]++/--</c>.</summary>
    public static void SugarBufferRmw(List<string> body)
    {
        var rx = new Regex(
            @"^(\s*)([A-Za-z_]\w*\[[^\]]+\])\s*=\s*\(byte\)\(\s*\2\s*([+-])\s*1\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        for (int i = 0; i < body.Count; i++)
        {
            var m = rx.Match(body[i]);
            if (!m.Success)
                continue;
            body[i] = m.Groups[1].Value + m.Groups[2].Value + (m.Groups[3].Value == "+" ? "++;" : "--;");
        }
    }

    /// <summary>SoftCpu-readable ADC #0; cast keeps <c>byte a</c> compiling (ternary is <c>int</c>).</summary>
    public const string Adc0Sugar = "a += (byte)(carry ? 1 : 0);";

    /// <summary>
    /// <c>carry = false; a = (byte)(a + N + (carry ? 1 : 0));</c> → <c>a += N;</c>
    /// <c>a = (byte)(a + 0 + (carry ? 1 : 0));</c> → <c>a += (byte)(carry ? 1 : 0);</c> (ADC #0 / hi-byte).
    /// SoftCpu: <c>a += N</c> → CLC+ADC; ADC#0 sugar → ADC #0.
    /// </summary>
    public static void CollapseCarryAdc(List<string> body)
    {
        var adc = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*a\s*\+\s*(?<n>0x[0-9A-Fa-f]+|\d+)\s*\+\s*\(carry\s*\?\s*1\s*:\s*0\)\s*\)\s*;\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        for (int i = 0; i < body.Count; i++)
        {
            string line = body[i].TrimStart();
            string indent = LeadingIndent(body[i]);
            // Normalize uncast form from older emits / hand edits.
            if (line is "a += carry ? 1 : 0;")
            {
                body[i] = indent + Adc0Sugar;
                continue;
            }
            var m = adc.Match(line);
            if (!m.Success)
                continue;
            string n = m.Groups["n"].Value;
            bool isZero = n is "0" or "0x0" or "0x00";
            if (i > 0 && body[i - 1].TrimStart() == "carry = false;")
            {
                body.RemoveAt(i - 1);
                i--;
                body[i] = indent + (isZero ? "a += 0;" : $"a += {n};");
                continue;
            }
            // Live carry (typical hi-byte after a += loImm): keep carry in the sugar.
            if (isZero)
                body[i] = indent + Adc0Sugar;
        }
    }

    static int _add16Seq;

    /// <summary>
    /// 16-bit add through A/carry:
    /// <c>a=lo; a+=N; [a&amp;=M;] destLo=a; a=hi; a+=carry?1:0; destHi=a;</c>
    /// → <c>ushort _uK = (ushort)(lo|(hi&lt;&lt;8)); _uK+=N; destLo=(byte)(_uK[&amp;M]); destHi=(byte)(_uK&gt;&gt;8);</c>
    /// </summary>
    public static void SugarAdd16ThroughA(List<string> body)
    {
        var load = new Regex(@"^a\s*=\s*(?<src>[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*;\s*$", RegexOptions.Compiled);
        var addImm = new Regex(@"^a\s*\+=\s*(?<n>0x[0-9A-Fa-f]+|\d+)\s*;\s*$", RegexOptions.Compiled);
        var andImm = new Regex(@"^a\s*\&=\s*(?<m>0x[0-9A-Fa-f]+|\d+)\s*;\s*$", RegexOptions.Compiled);
        var storeA = new Regex(@"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$", RegexOptions.Compiled);
        var adcCarry = new Regex(
            @"^a\s*\+=\s*(?:\(byte\))?\(?\s*carry\s*\?\s*1\s*:\s*0\s*\)?\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 5 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var loLoad = load.Match(body[i].TrimStart());
            var add = addImm.Match(body[i + 1].TrimStart());
            if (!loLoad.Success || !add.Success)
                continue;

            int at = i + 2;
            string? mask = null;
            var and = andImm.Match(body[at].TrimStart());
            if (and.Success)
            {
                mask = and.Groups["m"].Value;
                at++;
            }

            if (at >= body.Count || !storeA.IsMatch(body[at].TrimStart()))
                continue;
            string destLo = storeA.Match(body[at].TrimStart()).Groups["dst"].Value;
            at++;
            if (at + 1 >= body.Count)
                continue;
            var hiLoad = load.Match(body[at].TrimStart());
            if (!hiLoad.Success || !adcCarry.IsMatch(body[at + 1].TrimStart()))
                continue;
            at += 2;

            var hiStores = new List<string>();
            while (at < body.Count && storeA.IsMatch(body[at].TrimStart()))
            {
                hiStores.Add(storeA.Match(body[at].TrimStart()).Groups["dst"].Value);
                at++;
            }
            if (hiStores.Count == 0)
                continue;

            string lo = loLoad.Groups["src"].Value;
            string hi = hiLoad.Groups["src"].Value;
            string imm = add.Groups["n"].Value;
            if (lo is "a" or "x" or "y" || hi is "a" or "x" or "y")
                continue;

            string u = $"_u{_add16Seq++}";
            var repl = new List<string>
            {
                indent + $"ushort {u} = (ushort)({lo} | ({hi} << 8));",
                indent + $"{u} += {imm};",
            };
            if (mask != null)
                repl.Add(indent + $"{destLo} = (byte)({u} & {mask});");
            else
                repl.Add(indent + $"{destLo} = (byte){u};");
            foreach (string d in hiStores)
                repl.Add(indent + $"{d} = (byte)({u} >> 8);");

            body.RemoveRange(i, at - i);
            body.InsertRange(i, repl);
        }
    }

    /// <summary>
    /// <c>a &lt;&lt;= n</c> / ASL-A → <c>a *= (1&lt;&lt;n)</c> for n=1..7 (SoftCpu emits n×ASL).
    /// Run after <see cref="SugarConstMul35"/> so live-carry *3 is not broken.
    /// </summary>
    public static void SugarPow2Mul(List<string> body)
    {
        var shift = new Regex(@"^a\s*<<=\s*(?<n>[1-7])\s*;\s*$", RegexOptions.Compiled);
        for (int i = 0; i < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var m = shift.Match(body[i].TrimStart());
            if (!m.Success)
                continue;
            int n = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            body[i] = indent + $"a *= {1 << n};";
        }
        const string asl1 = "carry = (a & 0x80) != 0; a <<= 1;";
        for (int i = 0; i < body.Count; i++)
        {
            if (body[i].TrimStart() != asl1)
                continue;
            body[i] = LeadingIndent(body[i]) + "a *= 2;";
        }
    }

    /// <summary>
    /// <c>t=a; a&lt;&lt;=1|2; [carry=false;] a+=t</c> → <c>a*=3</c> / <c>a*=5</c>.
    /// Live-carry <c>ASL; ADC t</c> → <c>a*=3</c>.
    /// </summary>
    public static void SugarConstMul35(List<string> body)
    {
        var save = new Regex(@"^(?<t>[A-Za-z_]\w*)\s*=\s*a\s*;\s*$", RegexOptions.Compiled);
        var shl = new Regex(@"^a\s*(?:\*=\s*(?<p2>2|4)|<<=\s*(?<n>1|2))\s*;\s*$", RegexOptions.Compiled);
        var addT = new Regex(@"^a\s*\+=\s*(?<t>[A-Za-z_]\w*)\s*;\s*$", RegexOptions.Compiled);
        var adcT = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*a\s*\+\s*(?<t>[A-Za-z_]\w*)\s*\+\s*\(carry\s*\?\s*1\s*:\s*0\)\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        const string asl1 = "carry = (a & 0x80) != 0; a <<= 1;";

        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var s = save.Match(body[i].TrimStart());
            if (!s.Success)
                continue;
            string t = s.Groups["t"].Value;
            if (t is "a" or "x" or "y" or "carry" or "zero" or "negative")
                continue;

            int at = i + 1;
            string shLine = body[at].TrimStart();
            int factor;
            bool liveCarryShift = shLine == asl1;
            if (liveCarryShift)
            {
                factor = 3;
            }
            else
            {
                var sh = shl.Match(shLine);
                if (!sh.Success)
                    continue;
                if (sh.Groups["p2"].Success)
                    factor = int.Parse(sh.Groups["p2"].Value, CultureInfo.InvariantCulture) + 1;
                else
                    factor = (1 << int.Parse(sh.Groups["n"].Value, CultureInfo.InvariantCulture)) + 1;
            }
            if (factor is not (3 or 5))
                continue;
            at++;

            bool cleared = false;
            if (at < body.Count && body[at].TrimStart() == "carry = false;")
            {
                cleared = true;
                at++;
            }

            if (at >= body.Count)
                continue;
            string addLine = body[at].TrimStart();
            var add = addT.Match(addLine);
            var adc = adcT.Match(addLine);
            string? addName = add.Success ? add.Groups["t"].Value
                : adc.Success ? adc.Groups["t"].Value : null;
            if (addName != t)
                continue;
            // Live-carry ADC: only ASL;ADC (*3). Cleared-carry + add: *3/*5.
            if (adc.Success && !cleared && !liveCarryShift)
                continue;
            if (adc.Success && liveCarryShift && factor != 3)
                continue;
            if (add.Success && liveCarryShift && !cleared)
                continue; // need CLC before plain += after ASL, or use ADC form

            body.RemoveRange(i, at - i + 1);
            body.Insert(i, indent + $"a *= {factor};");
        }
    }

    /// <summary>
    /// <c>a = src|lit|peek|x|y; dest = a;</c> → <c>dest = src…;</c> (optional alias copies).
    /// SoftCpu: mem/peek/imm→mem or LDX/LDY. Dest may be <c>x</c>/<c>y</c>.
    /// </summary>
    public static void FoldPassThroughA(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)pad_poll(?:_msb)?\([^)]*\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);
        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            // Bare "a" only — (byte)(a ± …) is a raised residual expr SoftCpu can lower.
            if (src is "a")
                continue;

            int sAt = NextCodeIndex(body, i + 1);
            if (sAt < 0)
                continue;
            int braceAt = -1;
            // a = src; } dest = a; — fold across a single closing brace.
            if (body[sAt].TrimStart() == "}")
            {
                braceAt = sAt;
                sAt = NextCodeIndex(body, sAt + 1);
                if (sAt < 0)
                    continue;
            }

            // Skip inert x/y / a-free stores / carry-ifs / labels between load and store.
            // Do not skip zero|negative ifs — those NZ flags belong to the LDA of src.
            var kept = new List<string>();
            if (braceAt < 0)
            {
                int guard = 0;
                while (sAt >= 0 && guard++ < 64)
                {
                    string t = body[sAt].TrimStart();
                    if (IsLabelLine(t))
                    {
                        kept.Add(body[sAt]);
                        sAt = NextCodeIndex(body, sAt + 1);
                        continue;
                    }
                    if ((Regex.IsMatch(t, @"^[xy]\s*=") && !bareA.IsMatch(t))
                        || Regex.IsMatch(t, @"^[xy]\s*(\+\+|--)\s*;"))
                    {
                        kept.Add(body[sAt]);
                        sAt = NextCodeIndex(body, sAt + 1);
                        continue;
                    }
                    if (!t.StartsWith("a =", StringComparison.Ordinal)
                        && !t.StartsWith("a=", StringComparison.Ordinal)
                        && Regex.IsMatch(t, @"^[A-Za-z_]\w*(?:\[[^\]]+\])?\s*=")
                        && !bareA.IsMatch(t)
                        && !t.StartsWith("if", StringComparison.Ordinal)
                        && !t.StartsWith("for", StringComparison.Ordinal)
                        && !t.StartsWith("while", StringComparison.Ordinal)
                        && !t.StartsWith("switch", StringComparison.Ordinal)
                        && !t.StartsWith("do", StringComparison.Ordinal))
                    {
                        kept.Add(body[sAt]);
                        sAt = NextCodeIndex(body, sAt + 1);
                        continue;
                    }
                    // One-liner if only (no multi-line FindStatementEnd — SMB cost).
                    if (t.StartsWith("if", StringComparison.Ordinal)
                        && !bareA.IsMatch(t)
                        && !Regex.IsMatch(t, @"\b(zero|negative)\b")
                        && t.Contains('{', StringComparison.Ordinal)
                        && t.Contains('}', StringComparison.Ordinal))
                    {
                        kept.Add(body[sAt]);
                        sAt = NextCodeIndex(body, sAt + 1);
                        continue;
                    }
                    break;
                }
            }
            else
            {
                while (sAt >= 0 && IsLabelLine(body[sAt]))
                {
                    kept.Add(body[sAt]);
                    sAt = NextCodeIndex(body, sAt + 1);
                }
            }
            if (sAt < 0)
                continue;

            var sm = store.Match(body[sAt].TrimStart());
            if (!sm.Success)
                continue;
            string dst = sm.Groups["dst"].Value;
            if (dst is "a" or "carry" or "zero" or "negative")
                continue;
            if (braceAt >= 0 && AccUsedAfter(body, sAt + 1, bareA))
                continue;

            int at = sAt + 1;
            var extra = new List<string>();
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                var more = store.Match(body[code].TrimStart());
                if (!more.Success)
                    break;
                string d = more.Groups["dst"].Value;
                if (d is "a" or "carry" or "zero" or "negative")
                    break;
                // Don't chain through x/y aliases for extras (TAX clobber).
                if (d is "x" or "y")
                    break;
                extra.Add(d);
                at = code + 1;
            }

            if (braceAt >= 0)
            {
                string storeIndent = LeadingIndent(body[sAt]);
                var repl = new List<string> { body[braceAt] };
                repl.AddRange(kept);
                repl.Add(storeIndent + $"{dst} = {src};");
                foreach (string d in extra)
                    repl.Add(storeIndent + $"{d} = {dst};");
                body.RemoveRange(i, at - i);
                body.InsertRange(i, repl);
            }
            else
            {
                string storeIndent = kept.Count > 0 ? LeadingIndent(body[sAt]) : indent;
                var repl = new List<string>(kept) { storeIndent + $"{dst} = {src};" };
                foreach (string d in extra)
                    repl.Add(storeIndent + $"{d} = {dst};");
                body.RemoveRange(i, at - i);
                body.InsertRange(i, repl);
            }
        }

        // a = src; poke(addr, a); → poke(addr, src);
        var pokeA = new Regex(
            @"^poke\((?<addr>.+),\s*a\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            int pAt = NextCodeIndex(body, i + 1);
            if (pAt < 0)
                continue;
            var pm = pokeA.Match(body[pAt].TrimStart());
            if (!pm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;
            body.RemoveRange(i, pAt - i + 1);
            body.Insert(i, indent + $"poke({pm.Groups["addr"].Value}, {src});");
        }

        // a = src; carry = true|false; dest = a; → carry = …; dest = src;
        // a = src; carry = true|false; poke(addr, a); → carry = …; poke(addr, src);
        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            int cAt = NextCodeIndex(body, i + 1);
            if (cAt < 0)
                continue;
            string cLine = body[cAt].TrimStart();
            if (cLine is not ("carry = false;" or "carry = true;"))
                continue;
            int sAt = NextCodeIndex(body, cAt + 1);
            if (sAt < 0)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            var sm = store.Match(body[sAt].TrimStart());
            if (sm.Success)
            {
                string dst = sm.Groups["dst"].Value;
                if (dst is "a" or "carry" or "zero" or "negative")
                    continue;
                var repl = new List<string>
                {
                    indent + cLine,
                    indent + $"{dst} = {src};",
                };
                body.RemoveRange(i, sAt - i + 1);
                body.InsertRange(i, repl);
                continue;
            }
            var pm = pokeA.Match(body[sAt].TrimStart());
            if (!pm.Success)
                continue;
            var pokeRepl = new List<string>
            {
                indent + cLine,
                indent + $"poke({pm.Groups["addr"].Value}, {src});",
            };
            body.RemoveRange(i, sAt - i + 1);
            body.InsertRange(i, pokeRepl);
        }
    }

    /// <summary>
    /// <c>a = src; a &amp;= mask; if (a op imm)…</c> → <c>if ((src &amp; mask) op imm)…</c>.
    /// SoftCpu: LDA; AND #; [CMP #]; Bxx.
    /// </summary>
    public static void RaiseAndMaskTest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var andm = new Regex(
            @"^a\s*\&=\s*(?<m>0x[0-9A-Fa-f]+|\d+|\(byte\)~?[A-Za-z_][\w.]*(?:\s*\|\s*[A-Za-z_][\w.]*)*|\(byte\)\([^;]+\)|[A-Za-z_][\w.]*)\s*;\s*$",
            RegexOptions.Compiled);
        var ifA = new Regex(
            @"^if\s*\(\s*a\s*(?<op>==|!=|>=|<=|>|<)\s*(?<rhs>0x[0-9A-Fa-f]+|\d+)\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            int aAt = NextCodeIndex(body, i + 1);
            if (aAt < 0)
                continue;
            var am = andm.Match(body[aAt].TrimStart());
            if (!am.Success)
                continue;
            int iAt = NextCodeIndex(body, aAt + 1);
            if (iAt < 0)
                continue;
            var im = ifA.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;
            body.RemoveRange(i, iAt - i);
            body[i] = indent
                + $"if (({src} & {am.Groups["m"].Value}) {im.Groups["op"].Value} {im.Groups["rhs"].Value}){im.Groups["rest"].Value}";
        }
    }

    /// <summary>
    /// <c>a = src; call(… a …);</c> / <c>a = src; a op= n; call(… a …);</c>
    /// → call with src / <c>(byte)(src op n)</c> directly.
    /// </summary>
    public static void RaiseAccIntoCall(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|PeekVramBufY\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\(\))?|[A-Za-z_]\w*\[[^\]]+\]|0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var alu = new Regex(
            @"^a\s*(?<op>\+=|&=|\|=|\^=|\*=)\s*(?<n>0x[0-9A-Fa-f]+|\d+|\(byte\)~\([^;]+\)|\(byte\)~?[A-Za-z_][\w.]*(?:\s*\|\s*[A-Za-z_][\w.]*)*|\(byte\)\([^;]+\)|[A-Za-z_][\w.]*)\s*;\s*$",
            RegexOptions.Compiled);
        // Calls that take a as a byte/enum arg (neslib / poke / vram_put / apu_*).
        var callA = new Regex(
            @"^(?<head>ppu_ctrl\(\(PPUCTRL\)|ppu_mask_write\(\(MASK\)|ppu_scroll\(|vram_put\(|poke\([^,]+,\s*|apu_[A-Za-z0-9_]+\()a(?<tail>\s*[,)].*)$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int at = i + 1;
            var ops = new List<(string Op, string N)>();
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                var am = alu.Match(body[code].TrimStart());
                if (!am.Success)
                    break;
                string infix = am.Groups["op"].Value switch
                {
                    "+=" => "+",
                    "&=" => "&",
                    "|=" => "|",
                    "^=" => "^",
                    "*=" => "*",
                    _ => "+",
                };
                ops.Add((infix, am.Groups["n"].Value));
                at = code + 1;
            }

            int cAt = NextCodeIndex(body, at);
            if (cAt < 0)
                continue;
            var keptLabels = new List<string>();
            while (cAt >= 0 && IsLabelLine(body[cAt]))
            {
                keptLabels.Add(body[cAt]);
                cAt = NextCodeIndex(body, cAt + 1);
            }
            if (cAt < 0)
                continue;
            var cm = callA.Match(body[cAt].TrimStart());
            if (!cm.Success)
                continue;

            string expr = src;
            foreach (var (op, n) in ops)
                expr = $"(byte)({expr} {op} {n})";

            string head = cm.Groups["head"].Value;
            string tail = cm.Groups["tail"].Value;
            // ppu_scroll(a, a) → ppu_scroll(src, src)
            if (head == "ppu_scroll(" && tail.StartsWith(", a)", StringComparison.Ordinal))
                tail = $", {expr})" + tail[", a)".Length..];

            string callIndent = LeadingIndent(body[cAt]);
            var repl = new List<string>(keptLabels) { callIndent + head + expr + tail };
            body.RemoveRange(i, cAt - i + 1);
            body.InsertRange(i, repl);
        }

        // SoftCpu ABI: a = src; Name(); → Name(src); when a is unused after the call.
        // Also Name(); return; → Name(src); return; (SoftCpu: LDA; JMP).
        // Any recovered routine name (not neslib / SoftCpu builtins).
        var abiCall = new Regex(
            @"^(?<name>(?!ppu_|vram_|oam_|pad_|apu_|sei$|cli$|cld$|sed$|pha$|pla$|push$|pop$|poke$|peek$|waitvsync$)[A-Za-z_]\w*)\(\)\s*;\s*(?<ret>return\s*;)?\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);
        for (int i = 0; i + 1 < body.Count; i++)
        {
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a" || bareA.IsMatch(src))
                continue; // needs prior collapse of live-a expr first

            int at = i + 1;
            var ops = new List<(string Op, string N)>();
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                var am = alu.Match(body[code].TrimStart());
                if (!am.Success)
                    break;
                string infix = am.Groups["op"].Value switch
                {
                    "+=" => "+",
                    "&=" => "&",
                    "|=" => "|",
                    "^=" => "^",
                    "*=" => "*",
                    _ => "+",
                };
                ops.Add((infix, am.Groups["n"].Value));
                at = code + 1;
            }

            var kept = new List<string>();
            int cAt = SkipInertPreservingAcc(body, at, bareA, kept);
            if (cAt < 0)
                continue;
            var cm = abiCall.Match(body[cAt].TrimStart());
            if (!cm.Success)
                continue;
            if (AccUsedAfter(body, cAt + 1, bareA))
                continue;

            string expr = src;
            foreach (var (op, n) in ops)
                expr = $"(byte)({expr} {op} {n})";

            string ret = cm.Groups["ret"].Success && cm.Groups["ret"].Length > 0
                ? " return;"
                : "";
            string callIndent = LeadingIndent(body[cAt]);
            var repl = new List<string>(kept)
            {
                callIndent + $"{cm.Groups["name"].Value}({expr});{ret}",
            };
            body.RemoveRange(i, cAt - i + 1);
            body.InsertRange(i, repl);
        }
    }

    /// <summary>
    /// <c>a = lit; [x|y=…;] if (xy-cond) { a = lit2; } Name();</c>
    /// → <c>if (cond) Name(lit2); else Name(lit);</c> when a unused after.
    /// SoftCpu: Bxx; LDA #; JSR / JMP.
    /// </summary>
    public static void RaiseCondAccIntoCall(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<lit>0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var inertXy = new Regex(
            @"^[xy]\s*=\s*(?!a\b).+\s*;\s*$",
            RegexOptions.Compiled);
        var ifAssign = new Regex(
            @"^if\s*\(\s*(?<cond>[xy]\s*(?:==|!=|>=|<=|>|<)\s*(?:0x[0-9A-Fa-f]+|\d+))\s*\)\s*\{\s*a\s*=\s*(?<v>0x[0-9A-Fa-f]+|\d+)\s*;\s*\}\s*;?\s*$",
            RegexOptions.Compiled);
        var abiCall = new Regex(
            @"^(?<name>(?!ppu_|vram_|oam_|pad_|apu_|sei$|cli$|cld$|sed$|pha$|pla$|push$|pop$|poke$|peek$|waitvsync$)[A-Za-z_]\w*)\(\)\s*;\s*(?<ret>return\s*;)?\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string lit = lm.Groups["lit"].Value;

            int at = i + 1;
            var kept = new List<string>();
            int iAt = -1;
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                string t = body[code].TrimStart();
                if (IsLabelLine(t) || inertXy.IsMatch(t))
                {
                    kept.Add(body[code]);
                    at = code + 1;
                    continue;
                }
                iAt = code;
                break;
            }
            if (iAt < 0)
                continue;
            var im = ifAssign.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;

            int cAt = NextCodeIndex(body, iAt + 1);
            if (cAt < 0)
                continue;
            var cm = abiCall.Match(body[cAt].TrimStart());
            if (!cm.Success)
                continue;
            if (AccUsedAfter(body, cAt + 1, bareA))
                continue;

            string name = cm.Groups["name"].Value;
            string ret = cm.Groups["ret"].Success && cm.Groups["ret"].Length > 0
                ? " return;"
                : "";
            string cond = im.Groups["cond"].Value;
            string thenLit = im.Groups["v"].Value;
            string callIndent = LeadingIndent(body[cAt]);
            string ifIndent = LeadingIndent(body[iAt]);
            var repl = new List<string>(kept)
            {
                ifIndent + $"if ({cond}) {{ {name}({thenLit});{ret} }}",
                callIndent + $"else {{ {name}({lit});{ret} }}",
            };
            body.RemoveRange(i, cAt - i + 1);
            body.InsertRange(i, repl);
        }
    }

    /// <summary>
    /// <c>a = src; [a op= …;] pha();</c> → <c>pha(src…)</c> when <c>a</c> is not read after the push,
    /// or when the only immediate consumer is <c>dst = a</c> / a call-arg (rewritten to src).
    /// SoftCpu: load expr into A; PHA.
    /// </summary>
    public static void RaiseAccIntoPha(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|PeekVramBufY\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\(\))?|[A-Za-z_]\w*\[[^\]]+\]|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var alu = new Regex(
            @"^a\s*(?<op>\+=|&=|\|=|\^=|\*=|>>=|<<=)\s*(?<n>0x[0-9A-Fa-f]+|\d+|\(byte\)~\([^;]+\)|\(byte\)~?[A-Za-z_][\w.]*(?:\s*\|\s*[A-Za-z_][\w.]*)*|\(byte\)\([^;]+\)|[A-Za-z_][\w.]*)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);
        var callA = new Regex(
            @"^(?<head>ppu_ctrl\(\(PPUCTRL\)|ppu_mask_write\(\(MASK\)|ppu_scroll\(|vram_put\(|poke\([^,]+,\s*|apu_[A-Za-z0-9_]+\()a(?<tail>\s*[,)].*)$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int at = i + 1;
            var ops = new List<(string Op, string N)>();
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                var am = alu.Match(body[code].TrimStart());
                if (!am.Success)
                    break;
                string infix = am.Groups["op"].Value switch
                {
                    "+=" => "+",
                    "&=" => "&",
                    "|=" => "|",
                    "^=" => "^",
                    "*=" => "*",
                    ">>=" => ">>",
                    "<<=" => "<<",
                    _ => "+",
                };
                ops.Add((infix, am.Groups["n"].Value));
                at = code + 1;
            }

            int pAt = NextCodeIndex(body, at);
            if (pAt < 0)
                continue;
            string pLine = body[pAt].TrimStart();
            if (pLine is not ("pha();" or "push(a);"))
                continue;

            string expr = src;
            foreach (var (op, n) in ops)
                expr = $"(byte)({expr} {op} {n})";

            // Dead after pha → pha(expr);
            if (!AccUsedAfter(body, pAt + 1, bareA))
            {
                body.RemoveRange(i, pAt - i);
                body[i] = indent + $"pha({expr});";
                continue;
            }

            // Live read-only uses of a after pha → rewrite to src, then pha(expr).
            // Refuse if a is mutated (a &= / a = (byte)(a …)) — that must stay on A.
            if (!AccMutatedBeforeReassign(body, pAt + 1, bareA)
                && TrySubstituteAccWithSrc(body, pAt + 1, expr, bareA)
                && !AccUsedAfter(body, pAt + 1, bareA))
            {
                body.RemoveRange(i, pAt - i);
                body[i] = indent + $"pha({expr});";
                continue;
            }

            // Mutating uses after pha: SoftCpu pha(expr) leaves A=expr, so re-seed a=.
            if (AccMutatedBeforeReassign(body, pAt + 1, bareA))
            {
                // Remove a= and the original pha(); replace with pha(expr); a = expr;
                body.RemoveRange(i, pAt - i + 1);
                body.InsertRange(i, new[]
                {
                    indent + $"pha({expr});",
                    indent + $"a = {expr};",
                });
                continue;
            }

            // pha(); dst = a; … and a unused after store → pha(expr); dst = expr;
            int cAt = NextCodeIndex(body, pAt + 1);
            if (cAt < 0)
                continue;
            var sm = store.Match(body[cAt].TrimStart());
            if (sm.Success)
            {
                string dst = sm.Groups["dst"].Value;
                if (dst is "a" or "carry" or "zero" or "negative")
                    continue;
                if (AccUsedAfter(body, cAt + 1, bareA))
                    continue;
                var repl = new List<string>
                {
                    indent + $"pha({expr});",
                    LeadingIndent(body[cAt]) + $"{dst} = {expr};",
                };
                body.RemoveRange(i, cAt - i + 1);
                body.InsertRange(i, repl);
                continue;
            }

            // pha(); call(…a…) → pha(expr); call(…expr…)
            var cm = callA.Match(body[cAt].TrimStart());
            if (!cm.Success)
                continue;
            if (AccUsedAfter(body, cAt + 1, bareA))
                continue;
            string head = cm.Groups["head"].Value;
            string tail = cm.Groups["tail"].Value;
            if (head == "ppu_scroll(" && tail.StartsWith(", a)", StringComparison.Ordinal))
                tail = $", {expr})" + tail[", a)".Length..];
            var callRepl = new List<string>
            {
                indent + $"pha({expr});",
                LeadingIndent(body[cAt]) + head + expr + tail,
            };
            body.RemoveRange(i, cAt - i + 1);
            body.InsertRange(i, callRepl);
        }
    }

    /// <summary>True if bare <c>a</c> is read before the next full reassign of <c>a</c>.</summary>
    static bool AccUsedAfter(List<string> body, int from, Regex bareA)
    {
        for (int j = from; j < body.Count; j++)
        {
            string t = body[j].TrimStart();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal) || IsLabelLine(t))
                continue;
            // return ends the live range (unlike '}' which may close an else before dest=a).
            if (t is "return;" || (t.StartsWith("return ", StringComparison.Ordinal) && !bareA.IsMatch(t)))
                return false;
            var m = Regex.Match(t, @"^a\s*=\s*(.+)\s*;\s*$");
            if (m.Success)
                return bareA.IsMatch(m.Groups[1].Value);
            if (bareA.IsMatch(t))
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>a = src; carry = (a &amp; 1) != 0; a &gt;&gt;= 1; if (carry|!carry)…</c>
    /// → bit0 test on src; optional <c>a = (byte)(src &gt;&gt; 1)</c> when shifted a stays live.
    /// </summary>
    public static void RaiseLsrBitTest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var lsr1 = new Regex(
            @"^carry\s*=\s*\(a\s*&\s*1\)\s*!=\s*0\s*;\s*a\s*>>=\s*1\s*;\s*$",
            RegexOptions.Compiled);
        var ifCarry = new Regex(
            @"^if\s*\(\s*(?<not>!+)?\(?\s*carry\s*\)?\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int lAt = NextCodeIndex(body, i + 1);
            if (lAt < 0 || !lsr1.IsMatch(body[lAt].TrimStart()))
                continue;
            int iAt = NextCodeIndex(body, lAt + 1);
            if (iAt < 0)
                continue;
            var im = ifCarry.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;

            bool negated = im.Groups["not"].Success;
            // if (!carry) → bit0 == 0; if (carry) → bit0 != 0
            string op = negated ? "==" : "!=";
            string rest = im.Groups["rest"].Value;

            // Is shifted a still live after the if (fallthrough)?
            bool aLive = false;
            int after = iAt + 1;
            string restTrim = rest.TrimStart();
            bool hasInlineBlock = restTrim.StartsWith("{", StringComparison.Ordinal);
            bool hasNextLineBlock = !hasInlineBlock
                && after < body.Count
                && body[after].TrimStart().StartsWith("{", StringComparison.Ordinal);
            if (hasInlineBlock || hasNextLineBlock)
            {
                int start = hasInlineBlock ? iAt : after;
                int depth = 0;
                for (int j = start; j < body.Count; j++)
                {
                    foreach (char ch in body[j])
                    {
                        if (ch == '{') depth++;
                        else if (ch == '}') depth--;
                    }
                    if (depth == 0)
                    {
                        after = j + 1;
                        break;
                    }
                }
            }
            int next = NextCodeIndex(body, after);
            if (next >= 0)
            {
                string t = body[next].TrimStart();
                if (Regex.IsMatch(t, @"=\s*a\s*;")
                    || t.Contains("(a)")
                    || t.Contains(", a)")
                    || (Regex.IsMatch(t, @"\ba\b")
                        && !t.StartsWith("a =", StringComparison.Ordinal)
                        && !lsr1.IsMatch(t)))
                    aLive = true;
            }

            body.RemoveRange(i, iAt - i); // leave the if line
            body[i] = indent + $"if (({src} & 1) {op} 0){rest}";
            if (aLive)
            {
                // Insert shifted a after the if-statement / block.
                int insertAt = i + 1;
                if (hasInlineBlock || hasNextLineBlock)
                {
                    int start = i;
                    int depth = 0;
                    for (int j = start; j < body.Count; j++)
                    {
                        foreach (char ch in body[j])
                        {
                            if (ch == '{') depth++;
                            else if (ch == '}') depth--;
                        }
                        if (depth == 0 && j > start)
                        {
                            insertAt = j + 1;
                            break;
                        }
                    }
                }
                body.Insert(insertAt, indent + $"a = (byte)({src} >> 1);");
            }
        }
    }

    /// <summary>
    /// <c>a = src; [carry = (a &amp; 1) != 0; a &gt;&gt;= 1;]+ dest = a;</c>
    /// → <c>dest = (byte)(src &gt;&gt; n);</c>. SoftCpu: LDA; n×LSR; STA (carry from last LSR).
    /// </summary>
    public static void RaiseLsrToDest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var lsr1 = new Regex(
            @"^carry\s*=\s*\(a\s*&\s*1\)\s*!=\s*0\s*;\s*a\s*>>=\s*1\s*;\s*$",
            RegexOptions.Compiled);
        var lsrN = new Regex(
            @"^a\s*>>=\s*(?<n>[1-7])\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int at = i + 1;
            int shifts = 0;
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                string t = body[code].TrimStart();
                if (lsr1.IsMatch(t))
                {
                    shifts++;
                    at = code + 1;
                    continue;
                }
                var nm = lsrN.Match(t);
                if (nm.Success)
                {
                    shifts += int.Parse(nm.Groups["n"].Value, CultureInfo.InvariantCulture);
                    at = code + 1;
                    continue;
                }
                break;
            }
            if (shifts is 0 or > 7)
                continue;
            int shiftsEnd = at;
            at = NextCodeIndex(body, at);
            if (at >= 0)
            {
                var sm = store.Match(body[at].TrimStart());
                if (sm.Success)
                {
                    string dst = sm.Groups["dst"].Value;
                    if (dst is not ("a" or "carry" or "zero" or "negative"))
                    {
                        body.RemoveRange(i, at - i + 1);
                        body.Insert(i, indent + $"{dst} = (byte)({src} >> {shifts});");
                        continue;
                    }
                }
            }
            // No store — collapse into live a= for subsequent bit tests / ALU.
            body.RemoveRange(i, shiftsEnd - i);
            body.Insert(i, indent + $"a = (byte)({src} >> {shifts});");
        }
    }

    /// <summary>
    /// <c>[a = src;] carry = false|true; a = (byte)(a ± rhs ± borrow); dest = a;</c>
    /// → <c>dest = (byte)(src|a ± rhs);</c> (CLC+ADC / SEC+SBC).
    /// </summary>
    public static void RaiseCarryAluToDest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var adc = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*a\s*\+\s*(?<rhs>.+?)\s*\+\s*\(carry\s*\?\s*1\s*:\s*0\)\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        var sbc = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*a\s*-\s*(?<rhs>.+?)\s*-\s*\(carry\s*\?\s*0\s*:\s*1\)\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            string src = "a";
            int cAt = i;
            var lm = load.Match(body[i].TrimStart());
            if (lm.Success)
            {
                src = lm.Groups["src"].Value;
                if (src is "a")
                    continue;
                cAt = NextCodeIndex(body, i + 1);
                if (cAt < 0)
                    continue;
            }

            string cLine = body[cAt].TrimStart();
            bool clc = cLine == "carry = false;";
            bool sec = cLine == "carry = true;";
            if (!clc && !sec)
                continue;

            int aAt = NextCodeIndex(body, cAt + 1);
            if (aAt < 0)
                continue;

            // dest = (byte)((byte)(a ± rhs ± borrow) & mask) right after CLC/SEC
            var masked = Regex.Match(body[aAt].TrimStart(),
                @"^(?<dst>[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*=\s*\(byte\)\(\s*\(byte\)\(\s*a\s*(?<mop>[+-])\s*(?<mr>.+?)\s*(?:\+\s*\(carry\s*\?\s*1\s*:\s*0\)|-\s*\(carry\s*\?\s*0\s*:\s*1\))\s*\)\s*&\s*(?<mask>.+)\s*\)\s*;\s*$");
            if (masked.Success)
            {
                string mop = masked.Groups["mop"].Value;
                if ((clc && mop == "+") || (sec && mop == "-"))
                {
                    string mr = masked.Groups["mr"].Value.Trim();
                    string raisedM = $"(byte)({src} {mop} {mr})";
                    body.RemoveRange(i, aAt - i + 1);
                    body.Insert(i, indent
                        + $"{masked.Groups["dst"].Value} = (byte)({raisedM} & {masked.Groups["mask"].Value.Trim()});");
                    continue;
                }
            }

            var adcM = adc.Match(body[aAt].TrimStart());
            var sbcM = sbc.Match(body[aAt].TrimStart());
            if (clc && !adcM.Success)
                continue;
            if (sec && !sbcM.Success)
                continue;

            string rhs = clc ? adcM.Groups["rhs"].Value.Trim() : sbcM.Groups["rhs"].Value.Trim();
            string op = clc ? "+" : "-";
            string raised = $"(byte)({src} {op} {rhs})";

            int sAt = NextCodeIndex(body, aAt + 1);
            if (sAt >= 0)
            {
                var sm = store.Match(body[sAt].TrimStart());
                if (sm.Success)
                {
                    string dst = sm.Groups["dst"].Value;
                    if (dst is not ("a" or "carry" or "zero" or "negative"))
                    {
                        body.RemoveRange(i, sAt - i + 1);
                        body.Insert(i, indent + $"{dst} = {raised};");
                        continue;
                    }
                }
            }

            // No store — collapse into live a= (call / return consumers).
            body.RemoveRange(i, aAt - i + 1);
            body.Insert(i, indent + $"a = {raised};");
        }
    }

    /// <summary>
    /// <c>a = (byte)(src &gt;&gt; 1); if (!carry)|if (carry)…</c>
    /// → bit0 test on src (LSR carry). Drops a= when shifted value is dead.
    /// </summary>
    public static void RaiseShiftedACarryTest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*>>\s*1\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        var ifCarry = new Regex(
            @"^if\s*\(\s*(?<not>!+)?\(?\s*carry\s*\)?\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            int iAt = NextCodeIndex(body, i + 1);
            if (iAt < 0)
                continue;
            var im = ifCarry.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;

            bool negated = im.Groups["not"].Success;
            string op = negated ? "==" : "!=";
            string rest = im.Groups["rest"].Value;

            bool aLive = false;
            int after = iAt + 1;
            string restTrim = rest.TrimStart();
            bool hasInlineBlock = restTrim.StartsWith("{", StringComparison.Ordinal);
            bool hasNextLineBlock = !hasInlineBlock
                && after < body.Count
                && body[after].TrimStart().StartsWith("{", StringComparison.Ordinal);
            if (hasInlineBlock || hasNextLineBlock)
            {
                int start = hasInlineBlock ? iAt : after;
                int depth = 0;
                bool sawBrace = false;
                for (int j = start; j < body.Count; j++)
                {
                    foreach (char ch in body[j])
                    {
                        if (ch == '{') { depth++; sawBrace = true; }
                        else if (ch == '}') depth--;
                    }
                    if (sawBrace && depth == 0)
                    {
                        after = j + 1;
                        break;
                    }
                }
            }
            int next = NextCodeIndex(body, after);
            if (next >= 0)
            {
                string t = body[next].TrimStart();
                if (Regex.IsMatch(t, @"=\s*a\s*;")
                    || t.Contains("(a)")
                    || t.Contains(", a)")
                    || (Regex.IsMatch(t, @"\ba\b") && !t.StartsWith("a =", StringComparison.Ordinal)))
                    aLive = true;
            }

            body.RemoveRange(i, iAt - i);
            body[i] = indent + $"if (({src} & 1) {op} 0){rest}";
            if (aLive)
            {
                int insertAt = i + 1;
                if (hasInlineBlock || hasNextLineBlock)
                {
                    int depth = 0;
                    for (int j = i; j < body.Count; j++)
                    {
                        foreach (char ch in body[j])
                        {
                            if (ch == '{') depth++;
                            else if (ch == '}') depth--;
                        }
                        if (depth == 0 && j > i)
                        {
                            insertAt = j + 1;
                            break;
                        }
                    }
                }
                body.Insert(insertAt, indent + $"a = (byte)({src} >> 1);");
            }
        }
    }

    /// <summary>
    /// <c>a = (byte)(src * 2|<< 1); if (carry|!carry)…</c>
    /// → bit7 test on src (ASL carry). Drops a= when shifted value is dead.
    /// SoftCpu: LDA; AND #$80; Bxx — skip when a subsequent <c>if (zero)</c> needs ASL Z.
    /// </summary>
    public static void RaiseAslBitTest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|PeekVramBufY\(\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*(?:\*\s*2|<<\s*1)\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        var ifCarry = new Regex(
            @"^if\s*\(\s*(?<not>!+)?\(?\s*carry\s*\)?\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            // pla()/pop() bit test would require a second pop — keep residual.
            if (src is "pla()" or "pop()" or "PeekVramBufY()")
                continue;

            int iAt = NextCodeIndex(body, i + 1);
            if (iAt < 0)
                continue;
            var im = ifCarry.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;

            // if (zero) after ASL uses shifted Z — don't raise to AND.
            int afterIf = iAt + 1;
            string restTrim = im.Groups["rest"].Value.TrimStart();
            bool hasInlineBlock = restTrim.StartsWith("{", StringComparison.Ordinal);
            bool hasNextLineBlock = !hasInlineBlock
                && afterIf < body.Count
                && body[afterIf].TrimStart().StartsWith("{", StringComparison.Ordinal);
            if (hasInlineBlock || hasNextLineBlock)
            {
                int start = hasInlineBlock ? iAt : afterIf;
                int depth = 0;
                bool sawBrace = false;
                for (int j = start; j < body.Count; j++)
                {
                    foreach (char ch in body[j])
                    {
                        if (ch == '{') { depth++; sawBrace = true; }
                        else if (ch == '}') depth--;
                    }
                    if (sawBrace && depth == 0)
                    {
                        afterIf = j + 1;
                        break;
                    }
                }
            }
            int next = NextCodeIndex(body, afterIf);
            if (next >= 0)
            {
                string nt = body[next].TrimStart();
                if (Regex.IsMatch(nt, @"^if\s*\(\s*!?\(?\s*zero\s*\)?\s*\)"))
                    continue;
            }

            bool negated = im.Groups["not"].Success;
            string op = negated ? "==" : "!=";
            string rest = im.Groups["rest"].Value;

            bool aLive = false;
            if (next >= 0)
            {
                string t = body[next].TrimStart();
                if (Regex.IsMatch(t, @"=\s*a\s*;")
                    || t.Contains("(a)", StringComparison.Ordinal)
                    || t.Contains(", a)", StringComparison.Ordinal)
                    || (Regex.IsMatch(t, @"\ba\b") && !t.StartsWith("a =", StringComparison.Ordinal)))
                    aLive = true;
            }
            // Body of if may assign a (a |= …)
            if (rest.Contains("a |=", StringComparison.Ordinal)
                || rest.Contains("a &=", StringComparison.Ordinal)
                || rest.Contains("a =", StringComparison.Ordinal)
                || Regex.IsMatch(rest, @"\ba\b"))
                aLive = true;
            if (hasInlineBlock || hasNextLineBlock)
            {
                int start = hasInlineBlock ? iAt : iAt + 1;
                int depth = 0;
                bool sawBrace = false;
                for (int j = start; j < body.Count; j++)
                {
                    string line = body[j];
                    foreach (char ch in line)
                    {
                        if (ch == '{') { depth++; sawBrace = true; }
                        else if (ch == '}') depth--;
                    }
                    if (Regex.IsMatch(line, @"\ba\b"))
                        aLive = true;
                    if (sawBrace && depth == 0)
                        break;
                }
            }

            body.RemoveRange(i, iAt - i);
            body[i] = indent + $"if (({src} & 0x80) {op} 0){rest}";
            if (aLive)
            {
                int insertAt = i + 1;
                if (hasInlineBlock || hasNextLineBlock)
                {
                    int depth = 0;
                    for (int j = i; j < body.Count; j++)
                    {
                        foreach (char ch in body[j])
                        {
                            if (ch == '{') depth++;
                            else if (ch == '}') depth--;
                        }
                        if (depth == 0 && j > i)
                        {
                            insertAt = j + 1;
                            break;
                        }
                    }
                }
                body.Insert(insertAt, indent + $"a = (byte)({src} << 1);");
            }
        }
    }

    /// <summary>
    /// <c>a = (byte)(src &gt;&gt; n); dest = (byte)(a + imm + (carry ? 1 : 0));</c>
    /// → <c>dest = (byte)((byte)(src &gt;&gt; n) + imm + (carry ? 1 : 0));</c>.
    /// SoftCpu: LDA; n×LSR; ADC #imm; STA (carry from last LSR).
    /// </summary>
    public static void RaiseShiftAdcToDest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*(?<src>peek\(.+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*>>\s*(?<n>[1-7])\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*=\s*\(byte\)\(\s*a\s*\+\s*(?<imm>0x[0-9A-Fa-f]+|\d+)\s*\+\s*\(carry\s*\?\s*1\s*:\s*0\)\s*\)\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            int sAt = NextCodeIndex(body, i + 1);
            if (sAt < 0)
                continue;
            var sm = store.Match(body[sAt].TrimStart());
            if (!sm.Success)
                continue;
            string dst = sm.Groups["dst"].Value;
            if (dst is "a" or "carry" or "zero" or "negative")
                continue;
            body.RemoveRange(i, sAt - i + 1);
            body.Insert(i, indent
                + $"{dst} = (byte)((byte)({lm.Groups["src"].Value} >> {lm.Groups["n"].Value}) + {sm.Groups["imm"].Value} + (carry ? 1 : 0));");
        }
    }

    /// <summary>
    /// <c>a = peek(addr); poke(addr, (byte)(a + imm + (carry ? 1 : 0)));</c>
    /// → <c>poke(addr, (byte)(peek(addr) + imm + (carry ? 1 : 0)));</c>.
    /// </summary>
    public static void RaisePeekPokeCarryAdc(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*peek\((?<addr>.+)\)\s*;\s*$",
            RegexOptions.Compiled);
        var poke = new Regex(
            @"^poke\((?<addr>.+),\s*\(byte\)\(\s*a\s*\+\s*(?<imm>0x[0-9A-Fa-f]+|\d+)\s*\+\s*\(carry\s*\?\s*1\s*:\s*0\)\s*\)\s*\)\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            int pAt = NextCodeIndex(body, i + 1);
            if (pAt < 0)
                continue;
            var pm = poke.Match(body[pAt].TrimStart());
            if (!pm.Success)
                continue;
            // Peek/poke addr spellings often differ ((ushort)(0x417+x) vs (ushort)((ushort)0x417+x)).
            string peekAddr = lm.Groups["addr"].Value.Trim();
            string pokeAddr = pm.Groups["addr"].Value.Trim();
            body.RemoveRange(i, pAt - i + 1);
            body.Insert(i, indent
                + $"poke({pokeAddr}, (byte)(peek({peekAddr}) + {pm.Groups["imm"].Value} + (carry ? 1 : 0)));");
        }
    }

    /// <summary>
    /// Drop pure <c>a = …;</c> immediately before <c>}</c>/<c>return;</c> when unused after.
    /// <c>pla</c>/<c>pop</c> become side-effect-only <c>pla();</c>. SoftCpu: PLA without TAX/use.
    /// </summary>
    public static void DropDeadAccAtBlockEnd(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>.+)\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value.Trim();
            int n = NextCodeIndex(body, i + 1);
            if (n < 0)
                continue;
            string next = body[n].TrimStart();
            if (next is not ("}" or "return;") && !next.StartsWith("return ", StringComparison.Ordinal))
                continue;
            // Return of a value — keep.
            if (next.StartsWith("return ", StringComparison.Ordinal)
                && bareA.IsMatch(next))
                continue;
            // a may still be read after this closing brace (e.g. dest = a outside else).
            if (AccUsedAfter(body, n + 1, bareA))
                continue;
            if (src is "ppu_status()" or "apu_status()"
                || src.StartsWith("pad_poll", StringComparison.Ordinal)
                || src.StartsWith("peek(", StringComparison.Ordinal))
                continue;
            if (src is "pla()" or "pop()"
                || src.Contains("pla()", StringComparison.Ordinal)
                || src.Contains("pop()", StringComparison.Ordinal))
            {
                body[i] = indent + "pla();";
                continue;
            }
            body.RemoveAt(i);
            i--;
        }
    }

    /// <summary>
    /// <c>a = lit; x|y = N; do { dest[x|y (+k)] = a; x--|y--; } while…</c>
    /// → store <c>lit</c> directly. SoftCpu: LDA #lit; STA abs,X each iter (or reuse A).
    /// </summary>
    public static void RaiseLiteralFillThroughA(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<lit>0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var idx = new Regex(
            @"^(?<r>x|y)\s*=\s*(?<n>0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var storeA = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\])\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);
        // if (zero) { dest = a; } — SoftCpu: BEQ; STA (A already lit).
        var storeAInIf = new Regex(
            @"^if\s*\(\s*(?<cond>!?\(?\s*(?:zero|carry|negative)\s*\)?)\s*\)\s*\{\s*(?<dst>[A-Za-z_]\w*\[[^\]]+\])\s*=\s*a\s*;\s*\}\s*;?\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 3 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string lit = lm.Groups["lit"].Value;
            int xAt = NextCodeIndex(body, i + 1);
            if (xAt < 0)
                continue;
            var xm = idx.Match(body[xAt].TrimStart());
            if (!xm.Success)
                continue;
            int dAt = NextCodeIndex(body, xAt + 1);
            if (dAt < 0)
                continue;
            string dLine = body[dAt].TrimStart();
            if (dLine is not ("do" or "do {") && !dLine.StartsWith("do", StringComparison.Ordinal))
            {
                // Also: for-less immediate store loop opening with {
                if (dLine != "{")
                    continue;
            }

            // Rewrite = a stores to = lit inside the following do/while or brace block;
            // drop a= only when no other bare a remains until after the loop.
            int depth = 0;
            bool sawBlock = false;
            int end = dAt;
            var storeIdxs = new List<(int Idx, bool InIf, string Cond, string Dst)>();
            for (int j = dAt; j < body.Count; j++)
            {
                string t = body[j];
                foreach (char ch in t)
                {
                    if (ch == '{') { depth++; sawBlock = true; }
                    else if (ch == '}') depth--;
                }
                string trim = body[j].TrimStart();
                var sm = storeA.Match(trim);
                if (sm.Success)
                    storeIdxs.Add((j, false, "", sm.Groups["dst"].Value));
                else
                {
                    var im = storeAInIf.Match(trim);
                    if (im.Success)
                        storeIdxs.Add((j, true, im.Groups["cond"].Value, im.Groups["dst"].Value));
                }
                if (sawBlock && depth == 0)
                {
                    end = j;
                    break;
                }
            }
            if (storeIdxs.Count == 0)
                continue;

            // Ensure every bare-a use in the loop is a pure store of a (plain or if-wrapped).
            bool ok = true;
            for (int j = dAt; j <= end; j++)
            {
                string t = body[j].TrimStart();
                if (!bareA.IsMatch(t))
                    continue;
                if (!storeA.IsMatch(t) && !storeAInIf.IsMatch(t))
                {
                    ok = false;
                    break;
                }
            }
            if (!ok)
                continue;

            foreach (var (s, inIf, cond, dst) in storeIdxs)
            {
                if (inIf)
                    body[s] = LeadingIndent(body[s]) + $"if ({cond}) {{ {dst} = {lit}; }}";
                else
                    body[s] = LeadingIndent(body[s]) + $"{dst} = {lit};";
            }
            // Drop a= if unused after the loop as well.
            if (!AccUsedAfter(body, end + 1, bareA))
            {
                body.RemoveAt(i);
                i--;
            }
            _ = indent;
            _ = xm;
        }
    }

    /// <summary>
    /// <c>a = src;</c> when the value is never read and no later <c>if (carry|zero|negative)</c>
    /// consumes flags from this load → drop. SoftCpu: dead LDA.
    /// Preserves <c>pla</c>/<c>ppu_status</c>/<c>pad_poll</c> side effects and ASL/LSR flag producers
    /// that still feed a flag branch before reassign.
    /// </summary>
    public static void DropDeadAccWhenUnused(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>.+)\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);
        var flagBranch = new Regex(
            @"\b(carry|zero|negative)\b",
            RegexOptions.Compiled);

        for (int i = 0; i < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value.Trim();
            if (src is "a")
                continue;
            if (src is "ppu_status()" or "apu_status()"
                || src.StartsWith("pad_poll", StringComparison.Ordinal))
                continue;
            // Keep stack pops as side effects (value may be discarded).
            if (src is "pla()" or "pop()"
                || src.Contains("pla()", StringComparison.Ordinal)
                || src.Contains("pop()", StringComparison.Ordinal))
                continue;

            if (AccUsedAfter(body, i + 1, bareA))
                continue;
            if (FlagsUsedBeforeAccReassign(body, i + 1, flagBranch))
                continue;
            if (CallBeforeAccReassign(body, i + 1))
                continue; // SoftCpu ABI: JSR may consume A

            body.RemoveAt(i);
            i--;
            _ = indent;
        }
    }

    /// <summary>True if carry/zero/negative is read before the next <c>a =</c> reassign.</summary>
    /// Index loads/inc/dec and SoftCpu calls clobber NZ (and often C); flags after those
    /// are not from the preceding <c>a =</c>.
    static bool FlagsUsedBeforeAccReassign(List<string> body, int from, Regex flagBranch)
    {
        for (int j = from; j < body.Count; j++)
        {
            string t = body[j].TrimStart();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal) || IsLabelLine(t))
                continue;
            if (Regex.IsMatch(t, @"^a\s*="))
                return false;
            // Flag writes (CMP triples) replace prior flags — stop.
            if (Regex.IsMatch(t, @"^(zero|carry|negative)\s*="))
                return false;
            // DEX/INX/LDX/LDY/JSR clobber NZ from the prior LDA.
            if (Regex.IsMatch(t, @"^[xy]\s*(\+\+|--)\s*;")
                || Regex.IsMatch(t, @"^[xy]\s*=")
                || (Regex.IsMatch(t, @"^[A-Za-z_]\w*\(.*\)\s*;")
                    && !t.StartsWith("if", StringComparison.Ordinal)
                    && !t.StartsWith("peek(", StringComparison.Ordinal)
                    && !t.StartsWith("poke(", StringComparison.Ordinal)))
                return false;
            if (flagBranch.IsMatch(t))
                return true;
        }
        return false;
    }

    /// <summary>True if a SoftCpu call appears before the next <c>a =</c> reassign.</summary>
    static bool CallBeforeAccReassign(List<string> body, int from)
    {
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);
        for (int j = from; j < body.Count; j++)
        {
            string t = body[j].TrimStart();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal) || IsLabelLine(t))
                continue;
            if (Regex.IsMatch(t, @"^a\s*="))
                return false;
            if (t.StartsWith("if", StringComparison.Ordinal)
                || t.StartsWith("for", StringComparison.Ordinal)
                || t.StartsWith("while", StringComparison.Ordinal)
                || t.StartsWith("switch", StringComparison.Ordinal)
                || t.StartsWith("else", StringComparison.Ordinal)
                || t.StartsWith("do", StringComparison.Ordinal)
                || t is "{" or "}" or "return;" || t.StartsWith("return ", StringComparison.Ordinal)
                || t.StartsWith("goto ", StringComparison.Ordinal)
                || t.StartsWith("pha", StringComparison.Ordinal)
                || t.StartsWith("push(", StringComparison.Ordinal))
                continue;
            // name(...) — SoftCpu call / neslib; may take A.
            var call = Regex.Match(t, @"^[A-Za-z_]\w*\((.*)\)\s*;");
            if (call.Success)
            {
                string args = call.Groups[1].Value.Trim();
                // Name(); — ABI consumes A. Name(…a…) — arg uses A.
                // Name(lit/mem) — SoftCpu reloads; prior a= is dead.
                if (args.Length == 0 || bareA.IsMatch(args))
                    return true;
                continue;
            }
        }
        return false;
    }

    /// <summary>
    /// <c>a = src; a = other;</c> when <c>other</c> does not read <c>a</c> → drop the first load.
    /// SoftCpu: dead LDA before another LDA. Preserves <c>pla</c>/<c>ppu_status</c> side effects.
    /// </summary>
    public static void DropDeadAccBeforeReassign(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>.+)\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value.Trim();
            int n = NextCodeIndex(body, i + 1);
            if (n < 0)
                continue;
            var nm = load.Match(body[n].TrimStart());
            if (!nm.Success)
                continue;
            string rhs2 = nm.Groups["src"].Value;
            if (bareA.IsMatch(rhs2))
                continue; // second assign consumes a

            // HW / stack side effects must remain.
            if (src is "ppu_status()" or "apu_status()"
                || src.StartsWith("pad_poll", StringComparison.Ordinal))
                continue;
            if (src is "pla()" or "pop()")
            {
                body[i] = indent + "pla();";
                continue;
            }
            // (byte)(pla() …) / (byte)(pop() …) — keep the pop, drop the value.
            if (src.Contains("pla()", StringComparison.Ordinal)
                || src.Contains("pop()", StringComparison.Ordinal))
            {
                body[i] = indent + "pla();";
                continue;
            }

            body.RemoveAt(i);
            i--;
        }
    }

    /// <summary>
    /// <c>a = src; zero = a == N; carry = a &gt;= N; negative = …;</c> then <c>a</c> overwritten
    /// before any read → rewrite CMP flags onto <c>src</c> and drop the load.
    /// SoftCpu: LDA src; CMP #N.
    /// </summary>
    public static void RaiseLoadIntoCmpFlags(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var flags = new Regex(
            @"^zero\s*=\s*a\s*==\s*(?<n>0x[0-9A-Fa-f]+|\d+)\s*;\s*carry\s*=\s*a\s*>=\s*\k<n>\s*;\s*negative\s*=\s*\(byte\)\(a\s*-\s*\k<n>\)\s*>=\s*0x80\s*;\s*$",
            RegexOptions.Compiled);
        var flagIf = new Regex(
            @"^if\s*\(\s*!?\(?\s*(carry|zero|negative)\s*\)?\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);
        var xyImm = new Regex(
            @"^[xy]\s*=\s*(?:0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int fAt = NextCodeIndex(body, i + 1);
            if (fAt < 0)
                continue;
            var fm = flags.Match(body[fAt].TrimStart());
            if (!fm.Success)
                continue;

            int j = fAt + 1;
            bool ok = false;
            while (true)
            {
                int c = NextCodeIndex(body, j);
                if (c < 0)
                {
                    ok = true; // EOF after flags — SoftCpu LDA src; CMP
                    break;
                }
                string t = body[c].TrimStart();
                if (t.StartsWith("a =", StringComparison.Ordinal) || t.StartsWith("a=", StringComparison.Ordinal))
                {
                    ok = true;
                    break;
                }
                // Tail CMP before } / return — A remains src for RTS ABI.
                if (t is "}" or "return;")
                {
                    ok = true;
                    break;
                }
                if (flagIf.IsMatch(t) || xyImm.IsMatch(t) || IsLabelLine(t))
                {
                    j = c + 1;
                    continue;
                }
                break;
            }
            if (!ok)
                continue;

            string n = fm.Groups["n"].Value;
            body.RemoveAt(i); // drop a = src
            int flagsIdx = NextCodeIndex(body, i);
            if (flagsIdx < 0)
                continue;
            body[flagsIdx] = indent
                + $"zero = {src} == {n}; carry = {src} >= {n}; negative = (byte)({src} - {n}) >= 0x80;";
            i--;
        }

        // Wider: a = src; flags; if (flag)…; <no a read> → flags on src (drop a=).
        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;
            int fAt = NextCodeIndex(body, i + 1);
            if (fAt < 0)
                continue;
            var fm = flags.Match(body[fAt].TrimStart());
            if (!fm.Success)
                continue;

            int j = fAt + 1;
            int c = NextCodeIndex(body, j);
            while (c >= 0 && IsLabelLine(body[c]))
                c = NextCodeIndex(body, c + 1);
            if (c >= 0 && flagIf.IsMatch(body[c].TrimStart()))
                j = c + 1;
            int nAt = NextCodeIndex(body, j);
            if (nAt < 0)
            {
                // EOF after flags
                string nEof = fm.Groups["n"].Value;
                body.RemoveAt(i);
                int flagsEof = NextCodeIndex(body, i);
                if (flagsEof < 0)
                    continue;
                body[flagsEof] = indent
                    + $"zero = {src} == {nEof}; carry = {src} >= {nEof}; negative = (byte)({src} - {nEof}) >= 0x80;";
                i--;
                continue;
            }
            string next = body[nAt].TrimStart();
            // return/} OK: SoftCpu LDA src; CMP leaves A=src for RTS ABI.
            if (Regex.IsMatch(next, @"(?<![\w.])a(?![\w.])")
                && !Regex.IsMatch(next, @"^(zero|carry|negative)\s*=")
                && next is not ("return;" or "}"))
                continue;
            if (next.StartsWith("return ", StringComparison.Ordinal)
                && Regex.IsMatch(next, @"(?<![\w.])a(?![\w.])"))
                continue;
            if (Regex.IsMatch(next, @"^[A-Za-z_]\w*\s*\(") && !next.StartsWith("if", StringComparison.Ordinal))
                continue; // call may consume a

            string n = fm.Groups["n"].Value;
            body.RemoveAt(i);
            int flagsIdx = NextCodeIndex(body, i);
            if (flagsIdx < 0)
                continue;
            body[flagsIdx] = indent
                + $"zero = {src} == {n}; carry = {src} >= {n}; negative = (byte)({src} - {n}) >= 0x80;";
            i--;
        }
    }

    /// <summary>
    /// <c>a = src; { byte _v = rhs; zero = a == _v; … }</c> → cmp block on <c>src</c>.
    /// When <c>a</c> is still read afterward and <c>src</c> is pure, rewrite those reads to
    /// <c>src</c> and drop the load. SoftCpu: LDA src; CMP rhs (A still holds src).
    /// </summary>
    public static void RaiseLoadIntoMemCmpBlock(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var block = new Regex(
            @"^\{\s*byte\s+_v\s*=\s*(?<rhs>.+?);\s*zero\s*=\s*a\s*==\s*_v;\s*carry\s*=\s*a\s*>=\s*_v;\s*negative\s*=\s*\(byte\)\(a\s*-\s*_v\)\s*>=\s*0x80;\s*\}\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;
            // Side-effecting loads can't be re-read as src substitutes.
            if (src is "pla()" or "pop()" or "ppu_status()" or "apu_status()"
                || src.StartsWith("pad_poll", StringComparison.Ordinal)
                || src.Contains("pla()", StringComparison.Ordinal)
                || src.Contains("pop()", StringComparison.Ordinal))
                continue;
            // Allow inert (non-a, non-call) statements between load and cmp block.
            int bAt = -1;
            int scan = i + 1;
            for (int step = 0; step < 12; step++)
            {
                int code = NextCodeIndex(body, scan);
                if (code < 0)
                    break;
                string t = body[code].TrimStart();
                var bmScan = block.Match(t);
                if (bmScan.Success)
                {
                    bAt = code;
                    break;
                }
                if (bareA.IsMatch(t))
                    break;
                if (Regex.IsMatch(t, @"^[A-Za-z_]\w*\s*\(")
                    && !t.StartsWith("if", StringComparison.Ordinal)
                    && !t.StartsWith("for", StringComparison.Ordinal)
                    && !t.StartsWith("while", StringComparison.Ordinal)
                    && !t.StartsWith("switch", StringComparison.Ordinal)
                    && !t.StartsWith("peek(", StringComparison.Ordinal)
                    && !t.StartsWith("poke(", StringComparison.Ordinal))
                    break; // call may clobber A before CMP
                scan = code + 1;
            }
            if (bAt < 0)
                continue;
            var bm = block.Match(body[bAt].TrimStart());
            if (!bm.Success)
                continue;

            string rhs = bm.Groups["rhs"].Value.Trim();
            if (AccUsedAfter(body, bAt + 1, bareA))
            {
                if (!TrySubstituteAccWithSrc(body, bAt + 1, src, bareA))
                    continue;
            }

            body.RemoveAt(i);
            int blockIdx = NextCodeIndex(body, i);
            // Skip inert lines that were between load and block.
            while (blockIdx >= 0 && blockIdx < body.Count && !block.IsMatch(body[blockIdx].TrimStart()))
                blockIdx = NextCodeIndex(body, blockIdx + 1);
            if (blockIdx < 0)
                continue;
            body[blockIdx] = LeadingIndent(body[blockIdx])
                + $"{{ byte _v = {rhs}; zero = {src} == _v; carry = {src} >= _v; negative = (byte)({src} - _v) >= 0x80; }}";
            i--;
        }
    }

    /// <summary>
    /// Rewrite bare <c>a</c> reads to <paramref name="src"/> until the next <c>a =</c> reassign.
    /// Returns false if a call may consume A (SoftCpu ABI) before reassign.
    /// </summary>
    static bool TrySubstituteAccWithSrc(List<string> body, int from, string src, Regex bareA)
    {
        for (int j = from; j < body.Count; j++)
        {
            string raw = body[j];
            string t = raw.TrimStart();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal))
                continue;
            var m = Regex.Match(t, @"^a\s*=\s*(.+)\s*;\s*$");
            if (m.Success)
            {
                if (bareA.IsMatch(m.Groups[1].Value))
                    body[j] = LeadingIndent(raw) + $"a = {bareA.Replace(m.Groups[1].Value, src)};";
                return true; // stop at reassign
            }
            // SoftCpu LDA src; CMP leaves A=src, so bare calls still see the right A.
            if (!bareA.IsMatch(t))
                continue;
            body[j] = LeadingIndent(raw) + bareA.Replace(t, src);
        }
        return true;
    }

    /// <summary>True if <c>a</c> is mutated (compound assign / RMW) before the next full <c>a =</c>.</summary>
    static bool AccMutatedBeforeReassign(List<string> body, int from, Regex bareA)
    {
        for (int j = from; j < body.Count; j++)
        {
            string t = body[j].TrimStart();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal) || IsLabelLine(t))
                continue;
            if (Regex.IsMatch(t, @"^a\s*="))
                return bareA.IsMatch(Regex.Match(t, @"^a\s*=\s*(.+)\s*;").Groups[1].Value);
            if (Regex.IsMatch(t, @"^a\s*(\+\+|--|<<|>>|[+\-*/&|^%])"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Advance past labels / x|y loads / a-free stores / a-free if-blocks.
    /// Accumulates skipped lines into <paramref name="kept"/>. Returns next code index or -1.
    /// </summary>
    static int SkipInertPreservingAcc(List<string> body, int from, Regex bareA, List<string> kept)
    {
        int at = from;
        while (true)
        {
            int code = NextCodeIndex(body, at);
            if (code < 0)
                return -1;
            string t = body[code].TrimStart();
            if (IsLabelLine(t))
            {
                kept.Add(body[code]);
                at = code + 1;
                continue;
            }
            if ((Regex.IsMatch(t, @"^[xy]\s*=") && !bareA.IsMatch(t))
                || Regex.IsMatch(t, @"^[xy]\s*(\+\+|--)\s*;"))
            {
                kept.Add(body[code]);
                at = code + 1;
                continue;
            }
            // dest = expr; with no bare a (and not a = …)
            if (!t.StartsWith("a =", StringComparison.Ordinal)
                && !t.StartsWith("a=", StringComparison.Ordinal)
                && Regex.IsMatch(t, @"^[A-Za-z_]\w*(?:\[[^\]]+\])?\s*=")
                && !bareA.IsMatch(t)
                && !t.StartsWith("if", StringComparison.Ordinal)
                && !t.StartsWith("for", StringComparison.Ordinal)
                && !t.StartsWith("while", StringComparison.Ordinal)
                && !t.StartsWith("switch", StringComparison.Ordinal)
                && !t.StartsWith("do", StringComparison.Ordinal))
            {
                kept.Add(body[code]);
                at = code + 1;
                continue;
            }
            if (t.StartsWith("if", StringComparison.Ordinal) && !bareA.IsMatch(t))
            {
                int end = FindStatementEnd(body, code);
                if (end < 0)
                    return code;
                // Don't skip blocks that read a or contain SoftCpu ABI calls (may consume A).
                for (int j = code; j <= end; j++)
                {
                    string bt = body[j].TrimStart();
                    if (bareA.IsMatch(bt))
                        return code;
                    if (Regex.IsMatch(bt, @"^[A-Za-z_]\w*\(\)\s*;")
                        && !bt.StartsWith("if", StringComparison.Ordinal))
                        return code;
                }
                for (int j = code; j <= end; j++)
                    kept.Add(body[j]);
                at = end + 1;
                continue;
            }
            if (t is "{" or "}")
            {
                kept.Add(body[code]);
                at = code + 1;
                continue;
            }
            return code;
        }
    }

    /// <summary>End index of a one-liner or brace-balanced statement starting at <paramref name="start"/>.</summary>
    static int FindStatementEnd(List<string> body, int start)
    {
        string t = body[start].TrimStart();
        int depth = 0;
        bool saw = false;
        foreach (char ch in t)
        {
            if (ch == '{') { depth++; saw = true; }
            else if (ch == '}') depth--;
        }
        if (saw && depth == 0)
            return start; // one-liner { … }

        // if (cond)\n{ … } — brace opens on the next code line.
        if (!saw)
        {
            int next = NextCodeIndex(body, start + 1);
            if (next >= 0 && body[next].TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                for (int j = next; j < body.Count; j++)
                {
                    foreach (char ch in body[j])
                    {
                        if (ch == '{') { depth++; saw = true; }
                        else if (ch == '}') depth--;
                    }
                    if (saw && depth == 0)
                        return j;
                }
                return -1;
            }
            return start; // if (x) goto L; / if (x) return;
        }

        for (int j = start; j < body.Count; j++)
        {
            foreach (char ch in body[j])
            {
                if (ch == '{') { depth++; saw = true; }
                else if (ch == '}') depth--;
            }
            if (saw && depth == 0)
                return j;
        }
        return -1;
    }

    /// <summary>
    /// <c>a = src; a = (byte)(a ± rhs ± carry);</c> → single live <c>a = (byte)(src ± …);</c>.
    /// SoftCpu: LDA src; ADC/SBC (live carry).
    /// </summary>
    public static void RaiseLiveCarryAlu(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var alu = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*a\s*(?<op>[+-])\s*(?<rhs>.+?)\s*(?<carry>\+\s*\(carry\s*\?\s*1\s*:\s*0\)|-\s*\(carry\s*\?\s*0\s*:\s*1\))\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;
            int aAt = NextCodeIndex(body, i + 1);
            if (aAt < 0)
                continue;
            var am = alu.Match(body[aAt].TrimStart());
            if (!am.Success)
                continue;
            string op = am.Groups["op"].Value;
            string carry = am.Groups["carry"].Value.Trim();
            if ((op == "+" && !carry.StartsWith("+", StringComparison.Ordinal))
                || (op == "-" && !carry.StartsWith("-", StringComparison.Ordinal)))
                continue;

            string raised = $"(byte)({src} {op} {am.Groups["rhs"].Value.Trim()} {carry})";
            int sAt = NextCodeIndex(body, aAt + 1);
            if (sAt >= 0)
            {
                var sm = store.Match(body[sAt].TrimStart());
                if (sm.Success)
                {
                    string dst = sm.Groups["dst"].Value;
                    if (dst is not ("a" or "carry" or "zero" or "negative"))
                    {
                        body.RemoveRange(i, sAt - i + 1);
                        body.Insert(i, indent + $"{dst} = {raised};");
                        continue;
                    }
                }
            }

            body.RemoveRange(i, aAt - i + 1);
            body.Insert(i, indent + $"a = {raised};");
        }
    }

    /// <summary>
    /// <c>a = src; if ((byte)(a ± rhs ± carry) op imm)…</c>
    /// → <c>if ((byte)(src ± …) op imm)…</c> when a is not otherwise read.
    /// SoftCpu: LDA; ADC/SBC; CMP; Bxx.
    /// </summary>
    public static void RaiseLoadIntoCarryCompare(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var ifCarryAlu = new Regex(
            @"^if\s*\(\s*(?<not>!)?\(?\s*\(byte\)\(\s*a\s*(?<op>[+-])\s*(?<rhs>.+?)\s*(?<carry>\+\s*\(carry\s*\?\s*1\s*:\s*0\)|-\s*\(carry\s*\?\s*0\s*:\s*1\))\s*\)\s*(?<cmp>==|!=|>=|<=|<|>)\s*(?<imm>0x[0-9A-Fa-f]+|\d+)\s*\)?\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;
            int iAt = NextCodeIndex(body, i + 1);
            if (iAt < 0)
                continue;
            var im = ifCarryAlu.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;

            // Then-body / sequel must not need the pre-ALU a value (only a= overwrite OK).
            string rest = im.Groups["rest"].Value;
            if (Regex.IsMatch(rest, @"(?<![\w.])a(?![\w.])")
                && !Regex.IsMatch(rest, @"\ba\s*="))
            {
                // a used as value in inline then — keep
                if (!Regex.IsMatch(rest, @"\{\s*a\s*="))
                    continue;
            }

            string expr =
                $"(byte)({src} {im.Groups["op"].Value} {im.Groups["rhs"].Value.Trim()} {im.Groups["carry"].Value.Trim()})";
            string cmp = $"{expr} {im.Groups["cmp"].Value} {im.Groups["imm"].Value}";
            string cond = im.Groups["not"].Success ? $"!({cmp})" : cmp;

            body.RemoveRange(i, iAt - i);
            body[i] = indent + $"if ({cond}){rest}";
        }
    }

    /// <summary>Collapse consecutive identical CMP-flag triples (decompiler noise).</summary>
    public static void DropDuplicateCmpFlags(List<string> body)
    {
        var flags = new Regex(
            @"^zero\s*=\s*.+;\s*carry\s*=\s*.+;(?:\s*negative\s*=\s*.+;)?\s*$",
            RegexOptions.Compiled);
        for (int i = 0; i + 1 < body.Count; i++)
        {
            string a = body[i].TrimStart();
            if (!flags.IsMatch(a))
                continue;
            int n = NextCodeIndex(body, i + 1);
            if (n < 0)
                continue;
            if (body[n].TrimStart() != a)
                continue;
            body.RemoveAt(n);
            i--;
        }
    }

    /// <summary>
    /// <c>a = x|y; a +=|&amp;= n; x|y = a;</c> → <c>x += n;</c> / <c>x &amp;= n;</c>.
    /// SoftCpu: TXA/TYA; ALU; TAX/TAY.
    /// </summary>
    public static void RaiseIndexAlu(List<string> body)
    {
        var load = new Regex(@"^a\s*=\s*(?<r>x|y)\s*;\s*$", RegexOptions.Compiled);
        var alu = new Regex(
            @"^a\s*(?<op>\+=|&=|\|=|\^=)\s*(?<n>0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(@"^(?<r>x|y)\s*=\s*a\s*;\s*$", RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            var am = alu.Match(body[i + 1].TrimStart());
            var sm = store.Match(body[i + 2].TrimStart());
            if (!lm.Success || !am.Success || !sm.Success)
                continue;
            if (lm.Groups["r"].Value != sm.Groups["r"].Value)
                continue;
            string r = lm.Groups["r"].Value;
            body.RemoveRange(i, 3);
            body.Insert(i, indent + $"{r} {am.Groups["op"].Value} {am.Groups["n"].Value};");
        }
    }

    /// <summary>
    /// <c>a = src; a = (byte)(a op rest); dest = a;</c> → <c>dest = (byte)(src op rest);</c>.
    /// </summary>
    public static void RaiseCompoundAluToDest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var alu = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*a\s*(?<op>[+\-&|^])\s*(?<rest>.+)\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            int aAt = NextCodeIndex(body, i + 1);
            if (aAt < 0)
                continue;
            var am = alu.Match(body[aAt].TrimStart());
            if (!am.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int at = NextCodeIndex(body, aAt + 1);
            if (at < 0)
                continue;
            var sm = store.Match(body[at].TrimStart());
            if (!sm.Success)
                continue;
            string dst = sm.Groups["dst"].Value;
            if (dst is "a" or "carry" or "zero" or "negative")
                continue;
            bool indexDest = dst is "x" or "y";
            at++;

            var extra = new List<string>();
            if (!indexDest)
            {
                while (true)
                {
                    int code = NextCodeIndex(body, at);
                    if (code < 0)
                        break;
                    var more = store.Match(body[code].TrimStart());
                    if (!more.Success)
                        break;
                    string d = more.Groups["dst"].Value;
                    if (d is "a" or "x" or "y" or "carry" or "zero" or "negative")
                        break;
                    extra.Add(d);
                    at = code + 1;
                }
            }

            string expr = $"(byte)({src} {am.Groups["op"].Value} {am.Groups["rest"].Value})";
            var repl = new List<string> { indent + $"{dst} = {expr};" };
            foreach (string d in extra)
                repl.Add(indent + $"{d} = {dst};");
            body.RemoveRange(i, at - i);
            body.InsertRange(i, repl);
        }
    }

    /// <summary>
    /// <c>a = src; if ((a &amp; mask) op imm)…</c> → <c>if ((src &amp; mask) op imm)…</c>.
    /// </summary>
    /// <summary>
    /// <c>a = expr; [carry = true|false;] dest = (byte)(a ± … ± carry…);</c>
    /// → store folds expr into the ADC/SBC tree. SoftCpu: LDA expr; ADC/SBC; STA.
    /// </summary>
    public static void RaiseAccIntoAdcExprDest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        // dest = (byte)(…a…carry…); — ADC/SBC-shaped store using live a.
        var adcStore = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*(?<expr>\(byte\)\([^;]*\ba\b[^;]*\))\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a" || bareA.IsMatch(src))
                continue;

            int at = i + 1;
            var kept = new List<string>();
            int sAt = -1;
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                string t = body[code].TrimStart();
                if (IsLabelLine(t))
                {
                    kept.Add(body[code]);
                    at = code + 1;
                    continue;
                }
                if (t is "carry = true;" or "carry = false;")
                {
                    kept.Add(body[code]);
                    at = code + 1;
                    continue;
                }
                sAt = code;
                break;
            }
            if (sAt < 0)
                continue;
            var sm = adcStore.Match(body[sAt].TrimStart());
            if (!sm.Success)
                continue;
            string dst = sm.Groups["dst"].Value;
            if (dst is "a" or "x" or "y" or "carry" or "zero" or "negative")
                continue;
            string expr = sm.Groups["expr"].Value;
            if (!bareA.IsMatch(expr))
                continue;
            // Only ADC/SBC-shaped trees (must mention carry ternary).
            if (!expr.Contains("carry", StringComparison.Ordinal))
                continue;

            string raised = bareA.Replace(expr, src);
            bool aLive = AccUsedAfter(body, sAt + 1, bareA);
            var repl = new List<string>(kept)
            {
                LeadingIndent(body[sAt]) + $"{dst} = {raised};",
            };
            if (aLive)
                repl.Insert(0, indent + $"a = {src};");
            body.RemoveRange(i, sAt - i + 1);
            body.InsertRange(i, repl);
        }
    }

    /// <summary>
    /// <c>a = lit; y = mem; if (y != 0) { if (negative) { a &lt;&lt;= 1; } dest = a; }</c>
    /// → <c>y = mem; if (y != 0) dest = ((y &amp; 0x80) != 0) ? lit&lt;&lt;1 : lit;</c>.
    /// SoftCpu: LDY; BEQ; LDA #lit; [BMI ASL]; STA. N from Y, not from lit.
    /// </summary>
    public static void RaiseCondLitAslOnIndexNeg(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<lit>0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var yLoad = new Regex(
            @"^y\s*=\s*(?<ysrc>(?!a\b).+)\s*;\s*$",
            RegexOptions.Compiled);
        var ifY = new Regex(
            @"^if\s*\(\s*y\s*(?<op>!=|==)\s*(?<rhs>0x[0-9A-Fa-f]+|\d+)\s*\)\s*$",
            RegexOptions.Compiled);
        var aslA = new Regex(
            @"^if\s*\(\s*negative\s*\)\s*\{\s*carry\s*=\s*\(a\s*&\s*0x80\)\s*!=\s*0\s*;\s*a\s*<<=\s*1\s*;\s*\}\s*;?\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 4 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            if (!TryParseInt(lm.Groups["lit"].Value, out int lit) || lit is < 0 or > 255)
                continue;

            int yAt = NextCodeIndex(body, i + 1);
            if (yAt < 0)
                continue;
            var ym = yLoad.Match(body[yAt].TrimStart());
            if (!ym.Success)
                continue;
            string ysrc = ym.Groups["ysrc"].Value.Trim();

            int iAt = NextCodeIndex(body, yAt + 1);
            if (iAt < 0)
                continue;
            var im = ifY.Match(body[iAt].TrimStart());
            if (!im.Success || im.Groups["op"].Value != "!=" || im.Groups["rhs"].Value is not ("0" or "0x0" or "0x00"))
                continue;

            int brace = NextCodeIndex(body, iAt + 1);
            if (brace < 0 || body[brace].TrimStart() != "{")
                continue;
            int close = FindMatchingBrace(body, brace);
            if (close < 0)
                continue;

            // Inside: optional if (negative) { ASL a }; dest = a;
            int aslAt = -1;
            int sAt = -1;
            for (int j = brace + 1; j < close; j++)
            {
                string t = body[j].TrimStart();
                if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (aslA.IsMatch(t))
                {
                    aslAt = j;
                    continue;
                }
                var sm = store.Match(t);
                if (sm.Success)
                {
                    sAt = j;
                    string dst = sm.Groups["dst"].Value;
                    if (dst is "a" or "x" or "y" or "carry" or "zero" or "negative")
                    {
                        sAt = -1;
                        break;
                    }
                    // Only this store of a inside the block.
                    for (int k = j + 1; k < close; k++)
                    {
                        if (bareA.IsMatch(body[k].TrimStart()))
                        {
                            sAt = -1;
                            break;
                        }
                    }
                    break;
                }
                if (bareA.IsMatch(t))
                {
                    sAt = -1;
                    break;
                }
            }
            if (sAt < 0 || aslAt < 0)
                continue;
            if (AccUsedAfter(body, close + 1, bareA))
                continue;

            string dstName = store.Match(body[sAt].TrimStart()).Groups["dst"].Value;
            int litAsl = (lit << 1) & 0xFF;
            string yIndent = LeadingIndent(body[yAt]);
            string ifIndent = LeadingIndent(body[iAt]);
            string storeIndent = LeadingIndent(body[sAt]);
            string litText = FormatByte(lit);
            string aslText = FormatByte(litAsl);
            var repl = new List<string>
            {
                yIndent + $"y = {ysrc};",
                ifIndent + $"if (y != 0)",
                body[brace],
                storeIndent + $"{dstName} = {litText};",
                storeIndent + $"if ((y & 0x80) != 0) {{ {dstName} = {aslText}; }}",
                body[close],
            };
            body.RemoveRange(i, close - i + 1);
            body.InsertRange(i, repl);
            _ = indent;
        }
    }

    static string FormatByte(int value)
        => value <= 9 ? value.ToString(CultureInfo.InvariantCulture) : $"0x{value:X2}";

    /// <summary>
    /// <c>a = src; if (negative|!negative) { … }</c> → <c>if ((src &amp; 0x80) !=|== 0) { … }</c>
    /// (same for zero → <c>src ==|!= 0</c>). SoftCpu: LDA; AND #80 / CMP #0; Bxx.
    /// Only when the if is adjacent (comments/labels only) so NZ still belongs to <c>src</c>.
    /// Substitutes bare <c>a</c> inside the if body with <c>src</c>.
    /// </summary>
    public static void RaiseLoadIntoNegFlagIf(List<string> body)
    {
        // SoftCpu TryEmitLoadAFromMemExpr / TXA/TYA — no peek here.
        var load = new Regex(
            @"^a\s*=\s*(?<src>[A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var ifNz = new Regex(
            @"^if\s*\(\s*(?<not>!)?\s*(?<flag>negative|zero)\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int iAt = NextCodeIndex(body, i + 1);
            if (iAt < 0)
                continue;
            var im = ifNz.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;

            bool not = im.Groups["not"].Success;
            string flag = im.Groups["flag"].Value;
            string rest = im.Groups["rest"].Value;
            string cond = flag == "negative"
                ? (not ? $"({src} & 0x80) == 0" : $"({src} & 0x80) != 0")
                : (not ? $"{src} != 0" : $"{src} == 0");

            int end = FindStatementEnd(body, iAt);
            if (end < 0)
                continue;

            // Rewrite if header; substitute a→src in the if span.
            body[iAt] = LeadingIndent(body[iAt]) + $"if ({cond}){rest}";
            for (int j = iAt; j <= end; j++)
            {
                string t = body[j].TrimStart();
                if (!bareA.IsMatch(t))
                    continue;
                // Don't rewrite a= reassigns inside the block.
                if (Regex.IsMatch(t, @"^a\s*="))
                    break;
                body[j] = LeadingIndent(body[j]) + bareA.Replace(t, src);
            }

            // a = src; if ((src&0x80)…); [a-free one-liner stores]; dest = a;
            // → keep raised if + stores; dest = src. SoftCpu reloads for STA.
            var store = new Regex(
                @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
                RegexOptions.Compiled);
            int scan = NextCodeIndex(body, end + 1);
            var keptStores = new List<string>();
            while (scan >= 0)
            {
                string t = body[scan].TrimStart();
                if (!t.StartsWith("a =", StringComparison.Ordinal)
                    && !t.StartsWith("a=", StringComparison.Ordinal)
                    && Regex.IsMatch(t, @"^[A-Za-z_]\w*(?:\[[^\]]+\])?\s*=")
                    && !bareA.IsMatch(t)
                    && !t.StartsWith("if", StringComparison.Ordinal))
                {
                    keptStores.Add(body[scan]);
                    scan = NextCodeIndex(body, scan + 1);
                    continue;
                }
                break;
            }
            if (scan >= 0)
            {
                var sm = store.Match(body[scan].TrimStart());
                if (sm.Success)
                {
                    string dst = sm.Groups["dst"].Value;
                    if (dst is not ("a" or "x" or "y" or "carry" or "zero" or "negative")
                        && !AccUsedAfter(body, scan + 1, bareA))
                    {
                        string storeIndent = LeadingIndent(body[scan]);
                        var repl = new List<string>();
                        for (int j = iAt; j <= end; j++)
                            repl.Add(body[j]);
                        repl.AddRange(keptStores);
                        repl.Add(storeIndent + $"{dst} = {src};");
                        body.RemoveRange(i, scan - i + 1);
                        body.InsertRange(i, repl);
                        continue;
                    }
                }
            }

            // Drop a= when unused after (SoftCpu reloads src for the if).
            if (!AccUsedAfter(body, i + 1, bareA)
                && !FlagsUsedBeforeAccReassign(body, i + 1, new Regex(@"\b(carry|zero|negative)\b"))
                && !CallBeforeAccReassign(body, i + 1))
            {
                body.RemoveAt(i);
                i--;
            }
            _ = indent;
        }
    }

    /// <summary>
    /// <c>a = lit; if (negative|!negative|zero|!zero) { dest = a; } [dest2 = a;]…</c>
    /// → flag if stores lit, then trailing stores of lit. SoftCpu: BPL/BMI/BEQ first (prior NZ),
    /// then LDA #; STA — must not LDA before the branch.
    /// </summary>
    public static void RaiseCondLitStoreOnNzFlag(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<lit>0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var ifStore = new Regex(
            @"^if\s*\(\s*(?<cond>!?\(?\s*(?:negative|zero)\s*\)?)\s*\)\s*\{\s*(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*\}\s*;?\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string lit = lm.Groups["lit"].Value;

            int iAt = NextCodeIndex(body, i + 1);
            if (iAt < 0)
                continue;
            var im = ifStore.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;
            string cond = im.Groups["cond"].Value.Trim();
            string dst = im.Groups["dst"].Value;
            if (dst is "a" or "x" or "y" or "carry" or "zero" or "negative")
                continue;

            int at = iAt + 1;
            var extras = new List<(int Idx, string Dst)>();
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                var sm = store.Match(body[code].TrimStart());
                if (!sm.Success)
                    break;
                string d = sm.Groups["dst"].Value;
                if (d is "a" or "x" or "y" or "carry" or "zero" or "negative")
                    break;
                extras.Add((code, d));
                at = code + 1;
            }

            // Require a unused after the last store we fold (else keep a= for later RMW).
            int after = extras.Count > 0 ? extras[^1].Idx + 1 : iAt + 1;
            if (AccUsedAfter(body, after, bareA))
                continue;

            string ifIndent = LeadingIndent(body[iAt]);
            body[iAt] = ifIndent + $"if ({cond}) {{ {dst} = {lit}; }}";
            foreach (var (idx, d) in extras)
                body[idx] = LeadingIndent(body[idx]) + $"{d} = {lit};";
            body.RemoveAt(i);
            i--;
        }
    }

    /// <summary>
    /// <c>a = src; if (carry|!carry) { … stores/uses of a … }</c> → substitute <c>src</c> inside
    /// the carry block. SoftCpu: BCx then LDA src; STA (carry preserved across LDA).
    /// Keeps <c>a = src</c> when a is still live after the block.
    /// </summary>
    public static void RaiseLoadThroughCarryIfStores(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var ifCarry = new Regex(
            @"^if\s*\(\s*(?<not>!)?\s*carry\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a" || bareA.IsMatch(src))
                continue;

            int iAt = NextCodeIndex(body, i + 1);
            if (iAt < 0)
                continue;
            if (!ifCarry.IsMatch(body[iAt].TrimStart()))
                continue;

            int end = FindStatementEnd(body, iAt);
            if (end < 0 || end <= iAt)
                continue;

            bool any = false;
            for (int j = iAt + 1; j <= end; j++)
            {
                string t = body[j].TrimStart();
                if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal) || IsLabelLine(t))
                    continue;
                if (Regex.IsMatch(t, @"^a\s*="))
                    break; // reassign inside — stop substituting
                if (Regex.IsMatch(t, @"^a\s*(\+\+|--|<<|>>|[+\-*/&|^%])"))
                    break; // mutation — SoftCpu may need live A
                if (!bareA.IsMatch(t))
                    continue;
                body[j] = LeadingIndent(body[j]) + bareA.Replace(t, src);
                any = true;
            }
            if (!any)
                continue;

            if (!AccUsedAfter(body, end + 1, bareA)
                && !CallBeforeAccReassign(body, end + 1))
            {
                body.RemoveAt(i);
                i--;
            }
        }
    }

    public static void RaiseLoadIntoMaskedIf(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var ifMask = new Regex(
            @"^if\s*\(\s*\(\s*a\s*&\s*(?<m>0x[0-9A-Fa-f]+|\d+)\s*\)\s*(?<op>==|!=)\s*(?<rhs>0x[0-9A-Fa-f]+|\d+)\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;
            int nAt = NextCodeIndex(body, i + 1);
            if (nAt < 0)
                continue;
            var im = ifMask.Match(body[nAt].TrimStart());
            if (!im.Success)
                continue;
            body.RemoveRange(i, nAt - i);
            body[i] = indent
                + $"if (({src} & {im.Groups["m"].Value}) {im.Groups["op"].Value} {im.Groups["rhs"].Value}){im.Groups["rest"].Value}";
        }
    }

    /// <summary>
    /// <c>a = src; if (a op rhs)…</c> / <c>switch (a)</c> → compare/switch on <paramref name="src"/> directly.
    /// SoftCpu: LDA src; CMP / branch. Drops the live accumulator assign.
    /// </summary>
    /// <summary>
    /// <c>a = src; if (!carry|carry) { a ^=|&amp;=||= n; } [a op= …;] dest = a;</c>
    /// → <c>dest = src; if (…) dest = (byte)(dest op n); dest = (byte)(…);</c>.
    /// SoftCpu: LDA; BCx; EOR/AND/ORA; more ALU; STA. Carry preserved across LDA.
    /// </summary>
    public static void RaiseCarryCondAluToDest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var ifCarryOp = new Regex(
            @"^if\s*\(\s*(?<not>!)?\(?\s*carry\s*\)?\s*\)\s*\{\s*a\s*(?<op>\^=|&=|\|=)\s*(?<n>0x[0-9A-Fa-f]+|\d+)\s*;\s*\}\s*;?\s*$",
            RegexOptions.Compiled);
        var alu = new Regex(
            @"^a\s*(?<op>\+=|&=|\|=|\^=|\*=|>>=|<<=)\s*(?<n>0x[0-9A-Fa-f]+|\d+|\(byte\)~\([^;]+\)|\(byte\)~?[A-Za-z_][\w.]*(?:\s*\|\s*[A-Za-z_][\w.]*)*|\(byte\)\([^;]+\)|[A-Za-z_][\w.]*)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 3 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int iAt = NextCodeIndex(body, i + 1);
            if (iAt < 0)
                continue;
            var im = ifCarryOp.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;

            string cond = im.Groups["not"].Success ? "!carry" : "carry";
            string cOp = im.Groups["op"].Value[0].ToString(); // ^ & |
            string cN = im.Groups["n"].Value;

            int at = iAt + 1;
            var ops = new List<(string Op, string N)>();
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                var am = alu.Match(body[code].TrimStart());
                if (!am.Success)
                    break;
                string infix = am.Groups["op"].Value switch
                {
                    "+=" => "+",
                    "&=" => "&",
                    "|=" => "|",
                    "^=" => "^",
                    "*=" => "*",
                    ">>=" => ">>",
                    "<<=" => "<<",
                    _ => "+",
                };
                ops.Add((infix, am.Groups["n"].Value));
                at = code + 1;
            }

            int sAt = NextCodeIndex(body, at);
            if (sAt < 0)
                continue;
            var sm = store.Match(body[sAt].TrimStart());
            if (!sm.Success)
                continue;
            string dst = sm.Groups["dst"].Value;
            if (dst is "a" or "x" or "y" or "carry" or "zero" or "negative")
                continue;

            bool aLive = AccUsedAfter(body, sAt + 1, bareA);

            string storeIndent = LeadingIndent(body[sAt]);
            var repl = new List<string>
            {
                indent + $"{dst} = {src};",
                LeadingIndent(body[iAt]) + $"if ({cond}) {{ {dst} = (byte)({dst} {cOp} {cN}); }}",
            };
            string expr = dst;
            foreach (var (op, n) in ops)
                expr = $"(byte)({expr} {op} {n})";
            if (ops.Count > 0)
                repl.Add(storeIndent + $"{dst} = {expr};");
            body.RemoveRange(i, sAt - i + 1);
            body.InsertRange(i, repl);
            if (aLive)
            {
                int after = i + repl.Count;
                // SoftCpu STA leaves A=dest; rewrite later a reads/RMW to dest.
                if (!TrySubstituteAccWithSrc(body, after, dst, bareA))
                    body.Insert(after, storeIndent + $"a = {dst};");
            }
        }
    }

    /// <summary>
    /// <c>a = lit; [x|y = …;] if (xy/carry-cond) { a = lit2 | a op= n; } dest = a;</c>
    /// → store lit then conditional update on <c>dest</c>. SoftCpu: LDA #; STA; Bxx; LDA #/ORA; STA.
    /// Skips flag-NZ conditions (LDA would clobber them).
    /// </summary>
    public static void RaiseCondAccToDest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<lit>0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var inertXy = new Regex(
            @"^[xy]\s*=\s*(?!a\b).+\s*;\s*$",
            RegexOptions.Compiled);
        // Safe conds: carry preserved across LDA; x/y compares.
        var ifSafe = new Regex(
            @"^if\s*\(\s*(?<cond>!?\(?\s*carry\s*\)?|[xy]\s*(?:==|!=|>=|<=|>|<)\s*(?:0x[0-9A-Fa-f]+|\d+))\s*\)\s*\{\s*(?<body>.+?)\s*\}\s*;?\s*$",
            RegexOptions.Compiled);
        var assignA = new Regex(
            @"^a\s*=\s*(?<v>0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var aluA = new Regex(
            @"^a\s*(?<op>\|=|&=|\^=)\s*(?<n>0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string lit = lm.Groups["lit"].Value;

            int at = i + 1;
            var kept = new List<string>();
            int iAt = -1;
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                string t = body[code].TrimStart();
                if (IsLabelLine(t) || inertXy.IsMatch(t))
                {
                    kept.Add(body[code]);
                    at = code + 1;
                    continue;
                }
                iAt = code;
                break;
            }
            if (iAt < 0)
                continue;
            var im = ifSafe.Match(body[iAt].TrimStart());
            if (!im.Success)
                continue;
            string cond = im.Groups["cond"].Value.Trim();
            string ifBody = im.Groups["body"].Value.Trim();
            string? raisedThen;
            var am = assignA.Match(ifBody);
            if (am.Success)
                raisedThen = am.Groups["v"].Value;
            else
            {
                var om = aluA.Match(ifBody);
                if (!om.Success)
                    continue;
                raisedThen = $"(byte)({lit} {om.Groups["op"].Value[0]} {om.Groups["n"].Value})";
            }

            int sAt = NextCodeIndex(body, iAt + 1);
            if (sAt < 0)
                continue;
            var sm = store.Match(body[sAt].TrimStart());
            if (!sm.Success)
                continue;
            string dst = sm.Groups["dst"].Value;
            if (dst is "a" or "x" or "y" or "carry" or "zero" or "negative")
                continue;
            if (AccUsedAfter(body, sAt + 1, bareA))
                continue;

            string storeIndent = LeadingIndent(body[sAt]);
            var repl = new List<string>(kept)
            {
                storeIndent + $"{dst} = {lit};",
                LeadingIndent(body[iAt]) + $"if ({cond}) {{ {dst} = {raisedThen}; }}",
            };
            body.RemoveRange(i, sAt - i + 1);
            body.InsertRange(i, repl);
        }
    }

    public static void RaiseLoadIntoCompare(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\(\))?|[A-Za-z_]\w*\[[^\]]+\]|0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
            RegexOptions.Compiled);
        // if (a == N) / if (a != 0) / if (a >= …) — one-liner or header
        var ifA = new Regex(
            @"^if\s*\(\s*a\s*(?<op>==|!=|>=|<=|>|<)\s*(?<rhs>.+?)\s*\)(?<rest>\s*.*)$",
            RegexOptions.Compiled);
        var switchA = new Regex(@"^switch\s*\(\s*a\s*\)\s*$", RegexOptions.Compiled);
        // SoftCpu: LDA; LDX/LDY; CMP still leaves A=src.
        var inertXy = new Regex(
            @"^[xy]\s*=\s*(?!a\b).+\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int at = i + 1;
            var kept = new List<string>();
            int nAt = -1;
            bool clobbersSrcIndex = false;
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                string t = body[code].TrimStart();
                if (IsLabelLine(t))
                {
                    kept.Add(body[code]);
                    at = code + 1;
                    continue;
                }
                if (inertXy.IsMatch(t))
                {
                    // peek(...+ x); x = …; if (a) — can't re-peek with new x.
                    if ((t.StartsWith("x =", StringComparison.Ordinal) && Regex.IsMatch(src, @"(?<![\w.])x(?![\w.])"))
                        || (t.StartsWith("y =", StringComparison.Ordinal) && Regex.IsMatch(src, @"(?<![\w.])y(?![\w.])")))
                    {
                        clobbersSrcIndex = true;
                        break;
                    }
                    kept.Add(body[code]);
                    at = code + 1;
                    continue;
                }
                // CMP ignores prior C; drop orphan SEC/CLC before if (a op imm).
                if (t is "carry = true;" or "carry = false;")
                {
                    at = code + 1;
                    continue;
                }
                nAt = code;
                break;
            }
            if (clobbersSrcIndex || nAt < 0)
                continue;
            string next = body[nAt].TrimStart();
            var im = ifA.Match(next);
            if (im.Success)
            {
                // SoftCpu: LDA src; [LDX/LDY]; CMP — A remains src for later readers.
                var repl = new List<string>(kept)
                {
                    LeadingIndent(body[nAt])
                        + $"if ({src} {im.Groups["op"].Value} {im.Groups["rhs"].Value}){im.Groups["rest"].Value}",
                };
                body.RemoveRange(i, nAt - i + 1);
                body.InsertRange(i, repl);
                continue;
            }
            if (switchA.IsMatch(next))
            {
                var repl = new List<string>(kept) { LeadingIndent(body[nAt]) + $"switch ({src})" };
                body.RemoveRange(i, nAt - i + 1);
                body.InsertRange(i, repl);
            }
        }
    }

    /// <summary>
    /// <c>a = src|lit|x|y; a +=|&amp;=||=|^= n|enum; […]; dest = a;</c>
    /// → <c>dest = (byte)(((src op n) …));</c> plus alias copies.
    /// SoftCpu: LDA/imm/TXA; ADC/AND/ORA/EOR; STA — no live <c>a</c> in Program.cs.
    /// </summary>
    public static void RaiseAccAluToDest(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|PeekVramBufY\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        // Imm or (byte)(~ENUM) / (byte)(E|F) SoftCpu-evalable masks
        var alu = new Regex(
            @"^a\s*(?<op>\+=|&=|\|=|\^=|\*=|>>=|<<=)\s*(?<n>0x[0-9A-Fa-f]+|\d+|\(byte\)~\([^;]+\)|\(byte\)~?[A-Za-z_][\w.]*(?:\s*\|\s*[A-Za-z_][\w.]*)*|\(byte\)\([^;]+\)|[A-Za-z_][\w.]*)\s*;\s*$",
            RegexOptions.Compiled);
        // Compound without live-carry ternaries (those need CLC/SEC context).
        var compound = new Regex(
            @"^a\s*=\s*\(byte\)\(\s*a\s*(?<op>[+\-&|^])\s*(?<rest>(?!.*carry\s*\?).+)\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;

            int at = i + 1;
            var ops = new List<(string Op, string N)>();
            var kept = new List<string>(); // inert lines between load/ops (no bare a)
            var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);
            while (at < body.Count)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                string t = body[code].TrimStart();
                // Orphan CLC/SEC before bitwise RMW (ADC was optimized away) — drop with raise.
                if (t is "carry = false;" or "carry = true;")
                {
                    int peek = NextCodeIndex(body, code + 1);
                    if (peek < 0)
                        break;
                    string peekT = body[peek].TrimStart();
                    var peekAlu = alu.Match(peekT);
                    if (peekAlu.Success && peekAlu.Groups["op"].Value is "&=" or "|=" or "^=" or ">>=" or "<<=")
                    {
                        at = code + 1;
                        continue;
                    }
                    break;
                }
                var am = alu.Match(t);
                if (am.Success)
                {
                    string infix = am.Groups["op"].Value switch
                    {
                        "+=" => "+",
                        "&=" => "&",
                        "|=" => "|",
                        "^=" => "^",
                        "*=" => "*",
                        ">>=" => ">>",
                        "<<=" => "<<",
                        _ => "+",
                    };
                    ops.Add((infix, am.Groups["n"].Value));
                    at = code + 1;
                    continue;
                }
                var cm = compound.Match(t);
                if (cm.Success)
                {
                    ops.Add((cm.Groups["op"].Value, cm.Groups["rest"].Value.Trim()));
                    at = code + 1;
                    continue;
                }
                // Skip inert index/mem assigns that don't read a (e.g. y=(byte)(src+1); zp=y;).
                if (!bareA.IsMatch(t)
                    && !t.StartsWith("if", StringComparison.Ordinal)
                    && !t.StartsWith("for", StringComparison.Ordinal)
                    && !t.StartsWith("while", StringComparison.Ordinal)
                    && !t.StartsWith("do", StringComparison.Ordinal)
                    && !t.StartsWith("return", StringComparison.Ordinal)
                    && !t.StartsWith("goto", StringComparison.Ordinal)
                    && t is not ("{" or "}")
                    && !(Regex.IsMatch(t, @"^[A-Za-z_]\w*\s*\(")
                        && !t.StartsWith("peek(", StringComparison.Ordinal)
                        && !t.StartsWith("poke(", StringComparison.Ordinal)))
                {
                    kept.Add(body[code]);
                    at = code + 1;
                    continue;
                }
                break;
            }
            if (ops.Count == 0)
                continue;

            int opsEnd = at; // first index after last consumed alu/kept
            at = NextCodeIndex(body, at);

            string expr = src;
            foreach (var (op, n) in ops)
                expr = $"(byte)({expr} {op} {n})";

            // Store may sit just outside a closing brace (else { … alu… } dest = a;).
            int braceAt = -1;
            if (at >= 0 && body[at].TrimStart() == "}")
            {
                braceAt = at;
                int afterBrace = NextCodeIndex(body, at + 1);
                if (afterBrace >= 0 && store.IsMatch(body[afterBrace].TrimStart()))
                    at = afterBrace;
            }

            // No store: collapse load+ops into a single live a= (call/pha/flag consumer).
            if (at < 0 || !store.IsMatch(body[at].TrimStart()))
            {
                var collapse = new List<string>();
                collapse.AddRange(kept);
                collapse.Add(indent + $"a = {expr};");
                body.RemoveRange(i, opsEnd - i);
                body.InsertRange(i, collapse);
                continue;
            }

            var sm = store.Match(body[at].TrimStart());
            string dst = sm.Groups["dst"].Value;
            if (dst is "a" or "carry" or "zero" or "negative")
            {
                var collapse = new List<string>();
                collapse.AddRange(kept);
                collapse.Add(indent + $"a = {expr};");
                body.RemoveRange(i, opsEnd - i);
                body.InsertRange(i, collapse);
                continue;
            }
            // x/y as sole dest OK (TAX/TAY); don't chain extras after index dest.
            bool indexDest = dst is "x" or "y";
            int storeEnd = at + 1;

            var extra = new List<string>();
            if (!indexDest)
            {
                while (true)
                {
                    int code = NextCodeIndex(body, storeEnd);
                    if (code < 0)
                        break;
                    var more = store.Match(body[code].TrimStart());
                    if (!more.Success)
                        break;
                    string d = more.Groups["dst"].Value;
                    if (d is "a" or "x" or "y" or "carry" or "zero" or "negative")
                        break;
                    extra.Add(d);
                    storeEnd = code + 1;
                }
            }

            var repl = new List<string>();
            repl.AddRange(kept);
            if (braceAt >= 0)
            {
                // Keep lines from opsEnd through brace, then raised store after.
                for (int k = opsEnd; k <= braceAt; k++)
                    repl.Add(body[k]);
                string storeIndent = LeadingIndent(body[at]);
                repl.Add(storeIndent + $"{dst} = {expr};");
                foreach (string d in extra)
                    repl.Add(storeIndent + $"{d} = {dst};");
                body.RemoveRange(i, storeEnd - i);
                body.InsertRange(i, repl);
            }
            else
            {
                repl.Add(indent + $"{dst} = {expr};");
                foreach (string d in extra)
                    repl.Add(indent + $"{d} = {dst};");
                body.RemoveRange(i, storeEnd - i);
                body.InsertRange(i, repl);
            }
        }
    }

    /// <summary>
    /// <c>a = src; y|x = (byte)(a ± 1);</c> → <c>y|x = (byte)(src ± 1);</c>
    /// (keeps <c>a = src</c> when a stays live).
    /// </summary>
    public static void RaiseLoadIntoIndexInc(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>peek\(.+\)|pla\(\)|pop\(\)|\(byte\)\([^;]+\)|[A-Za-z_]\w*(?:\[[^\]]+\])?|0x[0-9A-Fa-f]+|\d+|x|y)\s*;\s*$",
            RegexOptions.Compiled);
        var inc = new Regex(
            @"^(?<r>x|y)\s*=\s*\(byte\)\(\s*a\s*(?<op>[+-])\s*1\s*\)\s*;\s*$",
            RegexOptions.Compiled);
        var bareA = new Regex(@"(?<![\w.])a(?![\w.])", RegexOptions.Compiled);

        for (int i = 0; i + 1 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            if (src is "a")
                continue;
            int nAt = NextCodeIndex(body, i + 1);
            if (nAt < 0)
                continue;
            var im = inc.Match(body[nAt].TrimStart());
            if (!im.Success)
                continue;

            bool aLive = AccUsedAfter(body, nAt + 1, bareA);

            string line = indent
                + $"{im.Groups["r"].Value} = (byte)({src} {im.Groups["op"].Value} 1);";
            if (aLive)
            {
                body[nAt] = line;
            }
            else
            {
                body.RemoveRange(i, nAt - i);
                body[i] = line;
            }
        }
    }

    /// <summary>
    /// <c>a = src; a += (byte)(carry ? 1 : 0); dest = a;</c>
    /// → <c>dest += (byte)(carry ? 1 : 0);</c> when src==dest,
    /// else <c>dest = (byte)(src + (carry ? 1 : 0));</c>.
    /// SoftCpu: LDA; ADC #0; STA.
    /// </summary>
    public static void FoldAdc0ThroughA(List<string> body)
    {
        var load = new Regex(
            @"^a\s*=\s*(?<src>[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*;\s*$",
            RegexOptions.Compiled);
        var adc0 = new Regex(
            @"^a\s*\+=\s*(?:\(byte\))?\(?\s*carry\s*\?\s*1\s*:\s*0\s*\)?\s*;\s*$",
            RegexOptions.Compiled);
        var store = new Regex(
            @"^(?<dst>[A-Za-z_]\w*\[[^\]]+\]|[A-Za-z_]\w*)\s*=\s*a\s*;\s*$",
            RegexOptions.Compiled);

        for (int i = 0; i + 2 < body.Count; i++)
        {
            string indent = LeadingIndent(body[i]);
            var lm = load.Match(body[i].TrimStart());
            if (!lm.Success)
                continue;
            int aAt = NextCodeIndex(body, i + 1);
            if (aAt < 0 || !adc0.IsMatch(body[aAt].TrimStart()))
                continue;
            int sAt = NextCodeIndex(body, aAt + 1);
            if (sAt < 0)
                continue;
            var sm = store.Match(body[sAt].TrimStart());
            if (!sm.Success)
                continue;
            string src = lm.Groups["src"].Value;
            string dst = sm.Groups["dst"].Value;
            if (src is "a" or "x" or "y"
                || dst is "a" or "x" or "y" or "carry" or "zero" or "negative")
                continue;

            // Extra stores of the same a (e.g. scroll = a) — fold only when next lines are pure a-stores.
            int at = sAt + 1;
            var extra = new List<string>();
            while (true)
            {
                int code = NextCodeIndex(body, at);
                if (code < 0)
                    break;
                var more = store.Match(body[code].TrimStart());
                if (!more.Success)
                    break;
                string d = more.Groups["dst"].Value;
                if (d is "a" or "x" or "y" or "carry" or "zero" or "negative")
                    break;
                extra.Add(d);
                at = code + 1;
            }

            var repl = new List<string>();
            if (src == dst)
                repl.Add(indent + $"{dst} += (byte)(carry ? 1 : 0);");
            else
                repl.Add(indent + $"{dst} = (byte)({src} + (carry ? 1 : 0));");
            foreach (string d in extra)
                repl.Add(indent + $"{d} = {dst};");
            body.RemoveRange(i, at - i);
            body.InsertRange(i, repl);
        }
    }

    /// <summary>After LDA/transfer, <c>if (!negative)</c> → <c>if ((sbyte)reg >= 0)</c> style via <c>reg &lt; 0x80</c>.</summary>
    public static void SugarNegativeFlagTests(List<string> body)
    {
        for (int i = 1; i < body.Count; i++)
        {
            string prev = body[i - 1].TrimStart();
            if (!TryLastReg(prev, out string reg))
                continue;
            string indent = LeadingIndent(body[i]);
            string line = body[i].TrimStart();

            if (line is "if (negative) return;")
            {
                body[i] = indent + $"if ({reg} >= 0x80) return;";
                continue;
            }
            if (line is "if (!negative) return;")
            {
                body[i] = indent + $"if ({reg} < 0x80) return;";
                continue;
            }
            var g = Regex.Match(line, @"^if\s*\(\s*(!)?\s*negative\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$");
            if (g.Success)
            {
                string op = g.Groups[1].Success ? "<" : ">=";
                body[i] = indent + $"if ({reg} {op} 0x80) goto {g.Groups[2].Value};";
                continue;
            }
            if (line is "if (negative)" or "if (!negative)")
            {
                string op = line.Contains('!', StringComparison.Ordinal) ? "<" : ">=";
                body[i] = indent + $"if ({reg} {op} 0x80)";
            }
        }
    }

    static bool TryLastReg(string line, out string reg)
    {
        reg = "a";
        var m = Regex.Match(line, @"^(a|x|y)\s*=");
        if (m.Success) { reg = m.Groups[1].Value; return true; }
        m = Regex.Match(line, @"^(a|x|y)(\+\+|--)\s*;");
        if (m.Success) { reg = m.Groups[1].Value; return true; }
        return false;
    }

    /// <summary>
    /// OAM hide loop: <c>y=0; a=0xF8; do { oam_y[y]=a; y++;×4 } while (!zero|y!=0);</c>
    /// → <c>y=0; do { oam_y[y]=0xF8; y++;×4 } while (y != 0);</c> — no live <c>a</c>.
    /// </summary>
    public static void StructureOamClearLoops(List<string> body)
    {
        for (int i = 0; i + 8 < body.Count; i++)
        {
            if (body[i].TrimStart() != "do")
                continue;
            if (i + 1 >= body.Count || body[i + 1].TrimStart() != "{")
                continue;

            int close = FindMatchingBrace(body, i + 1);
            if (close < 0 || close + 1 >= body.Count)
                continue;

            var inner = body.Skip(i + 2).Take(close - (i + 2)).Select(l => l.TrimStart()).ToList();
            if (inner.Count < 5)
                continue;

            if (inner[^1] != "y++;" || inner[^2] != "y++;" || inner[^3] != "y++;" || inner[^4] != "y++;")
                continue;

            string store = inner[0];
            var sm = Regex.Match(store, @"^(oam(?:_y)?)\s*\[\s*y\s*\]\s*=\s*(.+?)\s*;\s*$");
            if (!sm.Success)
                continue;
            if (inner.Count != 5)
                continue;

            string whileLine = body[close + 1].TrimStart();
            if (whileLine is not ("while (!zero);" or "while (zero);" or "while (y != 0);" or "while (y == 0);"))
                continue;

            int yInit = -1, aInit = -1;
            string yExpr = "0";
            string aExpr = sm.Groups[2].Value;
            for (int j = i - 1; j >= 0 && j >= i - 6; j--)
            {
                string t = body[j].TrimStart();
                var ym = Regex.Match(t, @"^y\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*$");
                if (ym.Success)
                {
                    yInit = j;
                    yExpr = ym.Groups[1].Value;
                }
                var am = Regex.Match(t, @"^a\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*$");
                if (am.Success && aExpr is "a")
                {
                    aInit = j;
                    aExpr = am.Groups[1].Value;
                }
            }
            if (yInit < 0)
                continue;
            // Must resolve hide value to a literal — otherwise keep residual a.
            if (aExpr is "a")
                continue;

            string indent = LeadingIndent(body[i]);
            string target = sm.Groups[1].Value == "oam" ? "oam_y" : sm.Groups[1].Value;
            string whileCond = whileLine is "while (y == 0);" or "while (zero);"
                ? "y == 0"
                : "y != 0";
            var replacement = new List<string>
            {
                indent + $"y = {yExpr};",
                indent + "do",
                indent + "{",
                indent + $"    {target}[y] = {aExpr};",
                indent + "    y++; y++; y++; y++;",
                indent + "}",
                indent + $"while ({whileCond});",
            };

            int removeFrom = yInit;
            if (aInit >= 0)
                removeFrom = Math.Min(aInit, yInit);

            int removeCount = (close + 2) - removeFrom;
            body.RemoveRange(removeFrom, removeCount);
            body.InsertRange(removeFrom, replacement);
            return;
        }
    }

    /// <summary>
    /// Dense <c>if (expr == imm) {…; return;}</c> chains → <c>switch (expr)</c>
    /// (expr is a/x/y or a raised mem/peek form).
    /// </summary>
    public static void StructureSwitchChains(List<string> body)
    {
        // LHS: register or simple mem / indexed / peek(…)
        const string lhs =
            @"(?<lhs>a|x|y|[A-Za-z_]\w*(?:\[[^\]]+\])?|peek\([^)]+\))";
        for (int i = 0; i < body.Count; i++)
        {
            var cases = new List<(string Imm, List<string> Stmts)>();
            int at = i;
            string indent = LeadingIndent(body[i]);
            string? switchExpr = null;
            while (at < body.Count)
            {
                string line = body[at].TrimStart();
                var one = Regex.Match(line,
                    @"^if\s*\(\s*" + lhs + @"\s*==\s*(?<imm>0x[0-9A-Fa-f]+|\d+)\s*\)\s*\{\s*(?<stmt>.+)\s*\}\s*$");
                if (one.Success)
                {
                    string e = one.Groups["lhs"].Value;
                    if (switchExpr != null && e != switchExpr)
                        break;
                    switchExpr = e;
                    string stmt = one.Groups["stmt"].Value.Trim().TrimEnd(';');
                    cases.Add((one.Groups["imm"].Value, [stmt + ";"]));
                    at++;
                    continue;
                }

                // Multi-line: if (expr == N) { … return; }
                var head = Regex.Match(line,
                    @"^if\s*\(\s*" + lhs + @"\s*==\s*(?<imm>0x[0-9A-Fa-f]+|\d+)\s*\)\s*$");
                if (head.Success && at + 1 < body.Count && body[at + 1].TrimStart() == "{")
                {
                    string e = head.Groups["lhs"].Value;
                    if (switchExpr != null && e != switchExpr)
                        break;
                    int br = FindMatchingBrace(body, at + 1);
                    if (br < 0)
                        break;
                    var stmts = body.Skip(at + 2).Take(br - (at + 2))
                        .Select(s => s.TrimStart()).Where(s => s.Length > 0).ToList();
                    if (stmts.Count == 0 || stmts[^1] is not ("return;" or "return"))
                        break;
                    switchExpr = e;
                    cases.Add((head.Groups["imm"].Value, stmts));
                    at = br + 1;
                    continue;
                }
                break;
            }

            if (cases.Count < 3 || switchExpr == null)
                continue;

            var block = new List<string> { indent + $"switch ({switchExpr})", indent + "{" };
            foreach (var (imm, stmts) in cases)
            {
                block.Add(indent + $"    case {imm}:");
                foreach (string s in stmts)
                    block.Add(indent + "        " + s);
                string last = stmts[^1];
                if (last != "return;" && last != "return" && last != "break;")
                    block.Add(indent + "        break;");
            }
            block.Add(indent + "}");
            body.RemoveRange(i, at - i);
            body.InsertRange(i, block);
        }
    }

    /// <summary>
    /// <c>peek((ushort)((vram_ptr_lo | vram_ptr_hi &lt;&lt; 8) + y))</c> → <c>vram_ptr[y]</c>
    /// (and poke → store). SoftCpu lowers to (zp),Y.
    /// </summary>
    public static void RewriteIndirectPtr(List<string> body)
    {
        var peek = new Regex(
            @"peek\(\(ushort\)\(\(\s*(?<lo>vram_ptr_lo|zp_00)\s*\|\s*(?<hi>vram_ptr_hi|zp_01)\s*<<\s*8\s*\)\s*\+\s*(?<idx>x|y)\s*\)\)",
            RegexOptions.Compiled);
        var poke = new Regex(
            @"poke\(\(ushort\)\(\(\s*(?<lo>vram_ptr_lo|zp_00)\s*\|\s*(?<hi>vram_ptr_hi|zp_01)\s*<<\s*8\s*\)\s*\+\s*(?<idx>x|y)\s*\)\s*,\s*(?<val>.+?)\)",
            RegexOptions.Compiled);

        for (int i = 0; i < body.Count; i++)
        {
            body[i] = peek.Replace(body[i], m => $"vram_ptr[{m.Groups["idx"].Value}]");
            body[i] = poke.Replace(body[i], m => $"vram_ptr[{m.Groups["idx"].Value}] = {m.Groups["val"].Value}");
        }
    }

    /// <summary>
    /// Hot absolute slots <c>buf[0xNN]</c> (no index reg) → named scalars beside the buffer.
    /// Returns addresses that must stay in <c>ramVars</c> (including offset 0).
    /// </summary>
    public static HashSet<ushort> PromoteHotBufferSlots(
        List<List<string>> bodies,
        List<BufferRecovery.RamBuffer> buffers,
        Dictionary<ushort, string> ramVars)
    {
        var promoted = new HashSet<ushort>();
        var counts = new Dictionary<(string Buf, int Off), int>();
        var absRx = new Regex(@"\b([A-Za-z_]\w*)\[(0x[0-9A-Fa-f]+|\d+)\]", RegexOptions.Compiled);

        foreach (var body in bodies)
        {
            foreach (string line in body)
            {
                foreach (Match m in absRx.Matches(line))
                {
                    if (!TryParseInt(m.Groups[2].Value, out int off))
                        continue;
                    var key = (m.Groups[1].Value, off);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }
        }

        var byName = buffers.ToDictionary(b => b.Name, StringComparer.Ordinal);
        foreach (var ((buf, off), n) in counts.OrderByDescending(kv => kv.Value))
        {
            if (n < 4)
                continue;
            if (buf is "oam" or "oam_y" or "oam_tile" or "oam_attr" or "oam_x" or "vram_buf" or "vram_ptr")
                continue;
            if (!byName.TryGetValue(buf, out var b))
                continue;
            if (off < 0 || off >= b.Size)
                continue;

            ushort addr = (ushort)(b.Base + off);
            if (ramVars.ContainsKey(addr) && !promoted.Contains(addr))
                continue;
            string name = $"{buf}_{off:X2}";
            ramVars[addr] = name;
            promoted.Add(addr);

            var slotRx = new Regex(@"\b" + Regex.Escape(buf) + @"\[(?:" + off + "|0x" + off.ToString("X", CultureInfo.InvariantCulture) + @")\]",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            foreach (var body in bodies)
            {
                for (int i = 0; i < body.Count; i++)
                    body[i] = slotRx.Replace(body[i], name);
            }
        }
        return promoted;
    }

    /// <summary>Convert simple local forward gotos after compare into structured if when safe.</summary>
    public static void ReduceLocalGotoIslands(List<string> body)
    {
        // if (cond) goto L; …; L: ;  where L has exactly one inbound goto → if (!cond) { … }
        for (int i = 0; i < body.Count; i++)
        {
            string line = body[i].TrimStart();
            var g = Regex.Match(line,
                @"^if\s*\((.+)\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$");
            if (!g.Success)
                continue;
            string cond = g.Groups[1].Value.Trim();
            string label = g.Groups[2].Value;

            // Refuse if any other goto targets the same label (would leave CS0159).
            int inbound = 0;
            for (int k = 0; k < body.Count; k++)
            {
                string t = body[k].TrimStart();
                if (Regex.IsMatch(t, @"\bgoto\s+" + Regex.Escape(label) + @"\s*;"))
                    inbound++;
            }
            if (inbound != 1)
                continue;

            int lab = -1;
            for (int j = i + 1; j < body.Count && j < i + 24; j++)
            {
                if (TryLabel(body[j], out string ln) && ln == label)
                {
                    lab = j;
                    break;
                }
                string t = body[j].TrimStart();
                if (t.StartsWith("goto ", StringComparison.Ordinal) || TryLabel(body[j], out _))
                {
                    lab = -2;
                    break;
                }
            }
            if (lab < i + 1)
                continue;

            string indent = LeadingIndent(body[i]);
            var mid = body.Skip(i + 1).Take(lab - (i + 1)).ToList();
            if (mid.Count == 0)
                continue;

            // if (cond) goto L; mid; L:  means skip mid when cond → if (!cond) { mid }
            string neg = NegateCond(cond);
            var block = new List<string> { indent + $"if ({neg})", indent + "{" };
            foreach (string m in mid)
                block.Add(indent + "    " + m.TrimStart());
            block.Add(indent + "}");
            body.RemoveRange(i, lab - i + 1); // remove through label
            body.InsertRange(i, block);
        }
    }

    static string NegateCond(string cond)
    {
        cond = cond.Trim();
        var m = Regex.Match(cond, @"^(a|x|y)\s*(==|!=|>=|<|<=|>)\s*(.+)$");
        if (m.Success)
        {
            string op = m.Groups[2].Value switch
            {
                "==" => "!=",
                "!=" => "==",
                ">=" => "<",
                "<" => ">=",
                "<=" => ">",
                ">" => "<=",
                _ => m.Groups[2].Value,
            };
            return $"{m.Groups[1].Value} {op} {m.Groups[3].Value.Trim()}";
        }
        if (cond.StartsWith('!'))
            return cond[1..].Trim();
        return $"!({cond})";
    }

    static bool TryLabel(string line, out string name)
    {
        name = "";
        var m = Regex.Match(line.TrimStart(), @"^(L_[0-9A-Fa-f]+)\s*:\s*;?\s*$");
        if (!m.Success)
            return false;
        name = m.Groups[1].Value;
        return true;
    }

    /// <summary>Heuristic routine renames from body patterns (call sites updated via map).</summary>
    public static Dictionary<string, string> SuggestRoutineNames(
        IReadOnlyList<(int Bank, ushort Address, string Name, List<string> Body)> emitted)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(emitted.Select(e => e.Name), StringComparer.Ordinal);

        foreach (var (bank, addr, name, body) in emitted)
        {
            if (name is "reset" or "nmi" or "irq")
                continue;
            if (!name.StartsWith("func_", StringComparison.Ordinal))
                continue;

            string? hint = null;
            string text = string.Join('\n', body);
            if (body.Any(l => l.Contains("oam_dma(", StringComparison.Ordinal)))
                hint = "DoOamDma";
            else if (Regex.IsMatch(text, @"oam_y\s*\[\s*y\s*\]\s*=\s*0xF8")
                     || Regex.IsMatch(text, @"oam(?:_y)?\s*\[\s*y\s*\]\s*=\s*(?:a|0xF8)"))
                hint = "ClearOam";
            else if (body.Any(l => l.Contains("pad_poll(", StringComparison.Ordinal) || l.Contains("PAD.", StringComparison.Ordinal)))
                hint = "ReadJoypad";
            else if (body.Count(l => l.Contains("vram_buf[", StringComparison.Ordinal)) >= 4)
                hint = "FlushVramBuf";
            else if (body.Any(l => l.Contains("waitvsync(", StringComparison.Ordinal)))
                hint = "WaitVBlank";

            if (hint == null)
                continue;
            string candidate = $"{hint}_{addr:X4}";
            if (!used.Add(candidate))
                continue;
            map[name] = candidate;
        }
        return map;
    }

    public static void ApplyRoutineRenames(
        List<(int Bank, ushort Address, string Name, List<string> Body)> emitted,
        Dictionary<string, string> renames)
    {
        if (renames.Count == 0)
            return;
        for (int i = 0; i < emitted.Count; i++)
        {
            var e = emitted[i];
            if (renames.TryGetValue(e.Name, out string? neu))
                emitted[i] = (e.Bank, e.Address, neu, e.Body);
        }
        foreach (var (_, _, _, body) in emitted)
        {
            for (int i = 0; i < body.Count; i++)
            {
                foreach (var (old, neu) in renames)
                {
                    if (body[i].Contains(old + "(", StringComparison.Ordinal))
                        body[i] = body[i].Replace(old + "(", neu + "(", StringComparison.Ordinal);
                }
            }
        }
    }

    /// <summary>Find dense ROM abs,X/Y tables from soft-CPU peeks.</summary>
    public static List<RomTable> FindRomTables(
        ReadOnlySpan<byte> prg,
        IEnumerable<List<string>> bodies,
        int prgBanks)
    {
        var bases = new Dictionary<ushort, int>();
        var rx = new Regex(
            @"peek\(\(ushort\)\((?:\(ushort\))?(?:0x(?<h>[0-9A-Fa-f]{4})|(?<n>rom_[0-9A-Fa-f]{4}))\s*\+\s*(?:x|y)\)\)",
            RegexOptions.Compiled);

        foreach (var body in bodies)
        {
            foreach (string line in body)
            {
                foreach (Match m in rx.Matches(line))
                {
                    ushort addr;
                    if (m.Groups["h"].Success)
                        ushort.TryParse(m.Groups["h"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
                    else if (m.Groups["n"].Success)
                        ushort.TryParse(m.Groups["n"].Value.AsSpan(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
                    else
                        continue;
                    if (addr < 0x8000)
                        continue;
                    bases[addr] = bases.GetValueOrDefault(addr) + 1;
                }
            }
        }

        // Prefer longer spans; drop bases that fall inside a denser earlier table.
        var raw = new List<(ushort Addr, int Count)>();
        foreach (var (addr, count) in bases.Where(kv => kv.Value >= 3).OrderBy(kv => kv.Key))
            raw.Add((addr, count));

        var keptBases = new List<ushort>();
        foreach (var (addr, count) in raw)
        {
            bool absorbed = false;
            foreach (ushort earlier in keptBases)
            {
                if (addr > earlier && addr - earlier < 64)
                {
                    absorbed = true;
                    break;
                }
            }
            if (!absorbed)
                keptBases.Add(addr);
        }

        var tables = new List<RomTable>();
        for (int i = 0; i < keptBases.Count; i++)
        {
            ushort addr = keptBases[i];
            if (!TryCpuToPrg(addr, prg.Length, out int off))
                continue;
            ushort next = i + 1 < keptBases.Count ? keptBases[i + 1] : (ushort)(addr + 64);
            // Also absorb traffic-only mid bases into this span length.
            int len = Math.Clamp(next - addr, 4, 64);
            foreach (var (mid, _) in raw)
            {
                if (mid > addr && mid < addr + len)
                    len = Math.Max(len, Math.Min(64, (mid - addr) + 4));
            }
            if (off + len > prg.Length)
                len = prg.Length - off;
            if (len < 4)
                continue;
            var bytes = prg.Slice(off, len).ToArray();
            tables.Add(new RomTable(addr, len, $"rom_{addr:X4}", bytes));
        }
        return tables;
    }

    public static void RewriteRomTablePeeks(List<List<string>> bodies, IReadOnlyList<RomTable> tables)
    {
        if (tables.Count == 0)
            return;

        // Peeks at bases inside a table span → table[idx + delta].
        var peekRx = new Regex(
            @"peek\(\(ushort\)\((?:\(ushort\))?(?:0x(?<h>[0-9A-Fa-f]{4})|rom_(?<n>[0-9A-Fa-f]{4}))\s*\+\s*(?<idx>x|y)\)\)",
            RegexOptions.Compiled);

        foreach (var body in bodies)
        {
            for (int i = 0; i < body.Count; i++)
            {
                body[i] = peekRx.Replace(body[i], m =>
                {
                    ushort addr;
                    if (m.Groups["h"].Success)
                        ushort.TryParse(m.Groups["h"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
                    else
                        ushort.TryParse(m.Groups["n"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
                    foreach (var t in tables)
                    {
                        if (addr >= t.Base && addr < t.Base + t.Length)
                        {
                            int delta = addr - t.Base;
                            string idx = m.Groups["idx"].Value;
                            return delta == 0 ? $"{t.Name}[{idx}]" : $"{t.Name}[{idx} + {delta}]";
                        }
                    }
                    return m.Value;
                });
            }
        }
    }

    public static IEnumerable<string> FormatRomTableDecls(IReadOnlyList<RomTable> tables)
    {
        if (tables.Count == 0)
            yield break;
        yield return "// ROM tables recovered from indexed peeks (SoftCpu maps name → $addr).";
        foreach (var t in tables)
        {
            string bytes = string.Join(", ", t.Bytes.Select(b => $"0x{b:X2}"));
            yield return $"byte[] {t.Name} = new byte[] {{ {bytes} }}; // ${t.Base:X4}";
        }
    }

    static bool TryCpuToPrg(ushort cpu, int prgLen, out int off)
    {
        off = 0;
        if (cpu < 0x8000)
            return false;
        int rel = cpu - 0x8000;
        if (prgLen == 0x4000)
        {
            // NROM-128 mirrored
            off = rel & 0x3FFF;
            return off < prgLen;
        }
        off = rel;
        return off < prgLen;
    }

    static int FindMatchingBrace(List<string> body, int openAt)
    {
        int depth = 0;
        for (int i = openAt; i < body.Count; i++)
        {
            string t = body[i].TrimStart();
            if (t == "{") depth++;
            else if (t == "}")
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    static string LeadingIndent(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t')
            i++;
        return line[..i];
    }

    /// <summary>Next non-empty, non-comment statement index, or -1.</summary>
    static int NextCodeIndex(List<string> body, int from)
    {
        for (int j = from; j < body.Count; j++)
        {
            string t = body[j].TrimStart();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal))
                continue;
            return j;
        }
        return -1;
    }

    static bool IsLabelLine(string line)
        => Regex.IsMatch(line.TrimStart(), @"^L_[A-Za-z0-9_]+:\s*;?\s*$");

    static bool TryParseInt(string text, out int value)
    {
        value = 0;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Run all body-local passes that do not need cross-body state.</summary>
    public static void ApplyBodyLocalPasses(List<string> body)
    {
        for (int n = 0; n < 3; n++)
            StructureOamClearLoops(body);
        SugarBufferRmw(body);
        CollapseCarryAdc(body);
        SugarAdd16ThroughA(body); // before ALU/ADC0 raise so lo/hi pair stays intact
        FoldAdc0ThroughA(body);
        RaiseCarryAluToDest(body);
        RaiseLsrToDest(body);
        RaiseLsrBitTest(body);
        RaiseShiftedACarryTest(body);
        RaiseAslBitTest(body);
        RaiseShiftAdcToDest(body);
        RaisePeekPokeCarryAdc(body);
        RaiseLiveCarryAlu(body);
        RaiseAccAluToDest(body);
        RaiseCompoundAluToDest(body);
        RaiseAccIntoAdcExprDest(body);
        RaiseCondLitAslOnIndexNeg(body);
        // AccAlu may emit a=(byte)(src*2) after the early AslBitTest — re-raise.
        RaiseAslBitTest(body);
        RaiseShiftedACarryTest(body);
        RaiseLoadIntoIndexInc(body);
        RaiseIndexAlu(body);
        RaiseAndMaskTest(body);
        RaiseLoadIntoNegFlagIf(body);
        RaiseCondLitStoreOnNzFlag(body);
        RaiseLoadThroughCarryIfStores(body);
        RaiseLoadIntoMaskedIf(body);
        RaiseLoadIntoCarryCompare(body);
        RaiseAccIntoCall(body);
        RaiseCondAccIntoCall(body);
        RaiseAccIntoPha(body);
        FoldPassThroughA(body);
        RaiseCarryCondAluToDest(body);
        RaiseCondAccToDest(body);
        RaiseLoadIntoCompare(body);
        RaiseLoadIntoCmpFlags(body);
        RaiseLoadIntoMemCmpBlock(body);
        DropDuplicateCmpFlags(body);
        DropDeadAccBeforeReassign(body);
        DropDeadAccWhenUnused(body);
        RaiseLiteralFillThroughA(body);
        SugarConstMul35(body);
        SugarPow2Mul(body);
        SugarNegativeFlagTests(body);
        RewriteIndirectPtr(body);
        StructureSwitchChains(body);
        RaiseLoadIntoNegFlagIf(body);
        RaiseCondLitStoreOnNzFlag(body);
        RaiseLoadThroughCarryIfStores(body);
        RaiseLoadIntoCompare(body);
        RaiseLoadIntoMaskedIf(body);
        RaiseLoadIntoCarryCompare(body);
        RaiseLoadIntoCmpFlags(body);
        RaiseLoadIntoMemCmpBlock(body);
        DropDuplicateCmpFlags(body);
        DropDeadAccBeforeReassign(body);
        DropDeadAccWhenUnused(body);
        RaiseLiteralFillThroughA(body);
        ReduceLocalGotoIslands(body);
        DropDeadAccAtBlockEnd(body);
        // Second pass
        SugarBufferRmw(body);
        CollapseCarryAdc(body);
        SugarAdd16ThroughA(body);
        FoldAdc0ThroughA(body);
        RaiseCarryAluToDest(body);
        RaiseLsrToDest(body);
        RaiseLsrBitTest(body);
        RaiseShiftedACarryTest(body);
        RaiseAslBitTest(body);
        RaiseShiftAdcToDest(body);
        RaisePeekPokeCarryAdc(body);
        RaiseLiveCarryAlu(body);
        RaiseAccAluToDest(body);
        RaiseCompoundAluToDest(body);
        RaiseAccIntoAdcExprDest(body);
        RaiseCondLitAslOnIndexNeg(body);
        RaiseAslBitTest(body);
        RaiseShiftedACarryTest(body);
        RaiseLoadIntoIndexInc(body);
        RaiseIndexAlu(body);
        RaiseAndMaskTest(body);
        RaiseLoadIntoNegFlagIf(body);
        RaiseCondLitStoreOnNzFlag(body);
        RaiseLoadThroughCarryIfStores(body);
        RaiseLoadIntoMaskedIf(body);
        RaiseLoadIntoCarryCompare(body);
        RaiseAccIntoCall(body);
        RaiseCondAccIntoCall(body);
        RaiseAccIntoPha(body);
        FoldPassThroughA(body);
        RaiseCarryCondAluToDest(body);
        RaiseCondAccToDest(body);
        RaiseLoadIntoCompare(body);
        RaiseLoadIntoCmpFlags(body);
        RaiseLoadIntoMemCmpBlock(body);
        DropDuplicateCmpFlags(body);
        DropDeadAccBeforeReassign(body);
        DropDeadAccWhenUnused(body);
        RaiseLiteralFillThroughA(body);
        SugarConstMul35(body);
        SugarPow2Mul(body);
        DropDeadAccAtBlockEnd(body);
    }

    /// <summary>
    /// Safety net: any zp_/ram_/f_*/ptr_*/pad_*/nmi_*/scroll* / buf_*_XX ident used in bodies
    /// but missing from <paramref name="ramVars"/> is re-added from its hex suffix.
    /// </summary>
    public static void EnsureReferencedRamDecls(
        IEnumerable<List<string>> bodies,
        Dictionary<ushort, string> ramVars,
        IReadOnlyDictionary<ushort, string> addrConsts,
        IReadOnlyList<BufferRecovery.RamBuffer> buffers)
    {
        var declared = new HashSet<string>(ramVars.Values, StringComparer.Ordinal);
        foreach (var b in buffers)
            declared.Add(b.Name);
        foreach (string n in addrConsts.Values)
            declared.Add(n);
        foreach (string f in OamFields)
            declared.Add(f);
        declared.Add("oam");
        declared.Add("vram_buf");
        declared.Add("vram_ptr");
        declared.Add("vram_ptr_lo");
        declared.Add("vram_ptr_hi");

        var used = new HashSet<string>(StringComparer.Ordinal);
        var ident = new Regex(@"\b([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);
        foreach (var body in bodies)
        {
            foreach (string line in body)
            {
                foreach (Match m in ident.Matches(line))
                {
                    string name = m.Groups[1].Value;
                    if (name is "a" or "x" or "y" or "carry" or "zero" or "negative"
                        or "byte" or "ushort" or "true" or "false" or "return" or "goto"
                        or "if" or "else" or "for" or "do" or "while" or "switch" or "case"
                        or "break" or "peek" or "poke" or "void" or "null")
                        continue;
                    if (name.StartsWith("L_", StringComparison.Ordinal))
                        continue;
                    if (name.StartsWith("func_", StringComparison.Ordinal)
                        || name.StartsWith("ClearOam_", StringComparison.Ordinal)
                        || name.StartsWith("FlushVram", StringComparison.Ordinal)
                        || name.StartsWith("ReadJoypad_", StringComparison.Ordinal)
                        || name.StartsWith("DoOamDma_", StringComparison.Ordinal)
                        || name.StartsWith("WaitVBlank_", StringComparison.Ordinal)
                        || name is "reset" or "nmi" or "irq"
                        || name.StartsWith("rom_", StringComparison.Ordinal)
                        || name.StartsWith("PAD", StringComparison.Ordinal)
                        || name.StartsWith("PPU_", StringComparison.Ordinal)
                        || name.StartsWith("APU_", StringComparison.Ordinal)
                        || name.StartsWith("NESColor", StringComparison.Ordinal)
                        || name.StartsWith("MASK", StringComparison.Ordinal)
                        || name.StartsWith("CTRL", StringComparison.Ordinal))
                        continue;
                    used.Add(name);
                }
            }
        }

        foreach (string name in used)
        {
            if (declared.Contains(name))
                continue;
            if (!TryParseAddrFromIdent(name, out ushort addr))
                continue;
            if (ramVars.ContainsKey(addr))
                continue; // different name already owns addr
            ramVars[addr] = name;
            declared.Add(name);
        }
    }

    static bool TryParseAddrFromIdent(string name, out ushort addr)
    {
        addr = 0;
        if (name is "vram_ptr_lo") { addr = 0x00; return true; }
        if (name is "vram_ptr_hi") { addr = 0x01; return true; }
        // Trailing hex after last '_' (f_C0CC_06D9, ptr_E9_lo → fail, zp_EB, ram_079F, buf_0086_00)
        if (name.EndsWith("_lo", StringComparison.Ordinal) || name.EndsWith("_hi", StringComparison.Ordinal))
        {
            // ptr_E9_lo / ptr_04_lo — hex before _lo/_hi
            int under = name.LastIndexOf('_');
            if (under > 0)
            {
                int prev = name.LastIndexOf('_', under - 1);
                string hex = prev >= 0 ? name[(prev + 1)..under] : name[..under];
                if (ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
                {
                    if (name.EndsWith("_hi", StringComparison.Ordinal))
                        addr++;
                    return true;
                }
            }
            return false;
        }
        int u = name.LastIndexOf('_');
        if (u > 0 && u < name.Length - 1)
        {
            string hex = name[(u + 1)..];
            if (hex.Length is >= 1 and <= 4
                && ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
                return true;
        }
        return false;
    }
}
