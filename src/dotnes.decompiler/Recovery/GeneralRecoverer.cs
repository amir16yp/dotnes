using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using dotnes.Disasm;
using NES;

namespace dotnes;

/// <summary>
/// Best-effort C# recovery for arbitrary (non-dotnes) ROMs from bank-aware disassembly.
/// Emits poke/peek, register temps, real goto/labels, and calls as Program.cs game logic.
/// Keeps all discovered routines (no dead-code elimination). RawPrg packs via prg.bin.
/// </summary>
sealed class GeneralRecoverer
{
    readonly NESRomReader _rom;
    readonly Disassembler _disasm;
    readonly ILogger _logger;
    readonly RecoveryStyle _style;
    readonly HashSet<(int Bank, ushort Address)> _stubRoutines = new();

    public GeneralRecoverer(
        NESRomReader rom,
        Disassembler disasm,
        ILogger? logger = null,
        RecoveryStyle style = RecoveryStyle.Optimized)
    {
        _rom = rom;
        _disasm = disasm;
        _logger = logger ?? new NullLogger();
        _style = style;
    }

    bool Optimize => _style == RecoveryStyle.Optimized;

    public string Decompile()
    {
        var routines = new Dictionary<(int Bank, ushort Address), string>();
        var order = new List<(int Bank, ushort Address)>();

        void AddRoutine(int bank, ushort addr, string name)
        {
            if (addr < 0x8000)
                return;
            if (!_disasm.TryGetInstruction(bank, addr, out var entry) || !entry.IsOfficial)
                return;
            if (routines.TryAdd((bank, addr), name))
                order.Add((bank, addr));
        }

        int fixedBank = _disasm.Banks.FixedBank;
        AddRoutine(fixedBank, _rom.ResetVector, "reset");
        if (_rom.NmiVector != _rom.ResetVector)
            AddRoutine(fixedBank, _rom.NmiVector, "nmi");
        if (_rom.IrqVector != _rom.ResetVector && _rom.IrqVector != _rom.NmiVector)
            AddRoutine(fixedBank, _rom.IrqVector, "irq");

        foreach (var (bank, target) in CollectCallTargets().OrderBy(t => t.Bank).ThenBy(t => t.Address))
            AddRoutine(bank, target, RoutineName(bank, target));

        // Absolute JMP targets (including bank-switched $8000 entries like Mega Man $9000).
        // Do NOT promote every branch target to a routine — that fragments bodies and drops callees.
        foreach (var (bank, target) in CollectJmpTargets().OrderBy(t => t.Bank).ThenBy(t => t.Address))
            AddRoutine(bank, target, RoutineName(bank, target));

        _logger.WriteLine(
            $"General recovery ({_style}): {routines.Count} routines, {_disasm.Instructions.Count} instruction sites");

        var emitted = new List<(int Bank, ushort Address, string Name, List<string> Body)>();
        foreach (var site in order)
        {
            string name = routines[site];
            emitted.Add((site.Bank, site.Address, name, DecompileRoutine(site.Bank, site.Address, routines)));
        }

        // Stubs for JSR/JMP targets we named but never recovered a body for.
        foreach (var site in _stubRoutines.OrderBy(t => t.Bank).ThenBy(t => t.Address))
        {
            if (routines.ContainsKey(site))
                continue;
            emitted.Add((site.Bank, site.Address, RoutineName(site.Bank, site.Address), []));
        }

        if (Optimize)
        {
            var bodies = emitted.Select(e => e.Body).ToList();
            var padZps = CollectPadZeroPages(bodies);
            var (maskZps, ctrlZps) = CollectPpuShadowZeroPages(bodies);
            foreach (var body in bodies)
                FormatTypedZpLiterals(body, padZps, maskZps, ctrlZps);
        }

        var access = TallyMemoryAccess(emitted.SelectMany(e => e.Body));

        var sb = new StringBuilder();
        if (Optimize)
        {
            sb.AppendLine("// Decompiled game logic (optimized soft-CPU recovery — default).");
            sb.AppendLine("// Same control-flow / register semantics; structured for reading.");
            sb.AppendLine("// Not a byte-faithful PRG encoding. Pass --exact for literal recovery.");
        }
        else
        {
            sb.AppendLine("// Decompiled game logic (exact soft-CPU recovery — --exact).");
            sb.AppendLine("// More literal goto/labels; less structuring.");
        }
        sb.AppendLine("// Pack mode: RawPrg — header.bin + prg.bin rebuild a byte-identical .nes.");
        sb.AppendLine($"// Mapper={_rom.Mapper}, PRG banks={_rom.PrgBanks}, CHR banks={_rom.ChrBanks},");
        sb.AppendLine($"// Mirroring={(_rom.VerticalMirroring ? "Vertical" : "Horizontal")}, Battery={_rom.HasBattery}");
        sb.AppendLine($"// Vectors: NMI=${_rom.NmiVector:X4} RESET=${_rom.ResetVector:X4} IRQ=${_rom.IrqVector:X4}");
        sb.AppendLine($"// Recovered {_disasm.Instructions.Count} instruction sites across {routines.Count} routines.");
        AppendMemoryAccessComment(sb, access);
        sb.AppendLine();
        // Soft-CPU recovery is readable analysis; keep every discovered routine (no DCE).
        sb.AppendLine("#nullable disable");
        sb.AppendLine("#pragma warning disable CS8321 // unused local function (keep all discovered routines)");
        sb.AppendLine("#pragma warning disable CS0162 // unreachable code from literal branches");
        sb.AppendLine("#pragma warning disable CS0164 // unreferenced labels from partial CFG");
        sb.AppendLine("byte a = 0, x = 0, y = 0;");
        sb.AppendLine("bool carry = false, zero = false, negative = false;");
        sb.AppendLine("var cpuStack = new Stack<byte>();");
        sb.AppendLine("void push(byte v) => cpuStack.Push(v);");
        sb.AppendLine("byte pop() => cpuStack.Pop();");
        sb.AppendLine();
        sb.AppendLine("reset();");
        sb.AppendLine("while (true) ;");
        sb.AppendLine();

        foreach (var (bank, address, name, body) in emitted)
        {
            bool stub = body.Count == 0 && name.StartsWith("func_", StringComparison.Ordinal)
                && _stubRoutines.Contains((bank, address)) && !routines.ContainsKey((bank, address));
            if (stub)
            {
                sb.AppendLine($"// bank={bank} ${address:X4} (unrecovered stub — keep for callers)");
                sb.AppendLine($"void {name}() {{ }}");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine($"// bank={bank} ${address:X4}");
            sb.AppendLine($"void {name}()");
            sb.AppendLine("{");
            foreach (string line in body)
                sb.AppendLine("    " + line);
            if (body.Count == 0)
                sb.AppendLine("    // empty / not reachable in disassembly window");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static void AppendMemoryAccessComment(
        StringBuilder sb,
        IReadOnlyDictionary<string, (int Peeks, int Pokes)> access)
    {
        if (access.Count == 0)
            return;

        sb.AppendLine("// Memory accesses (peek / poke counts in recovered code):");
        const int maxLabel = 28;
        int width = Math.Min(maxLabel, access.Keys.Max(k => Math.Min(k.Length, maxLabel)));
        foreach (var (addr, counts) in access.OrderBy(kv => MemoryAccessSortKey(kv.Key)))
        {
            string label = addr.Length <= maxLabel ? addr : addr[..(maxLabel - 3)] + "...";
            sb.Append("//   ");
            sb.Append(label.PadRight(width));
            sb.Append("  peek×");
            sb.Append(counts.Peeks);
            sb.Append("  poke×");
            sb.Append(counts.Pokes);
            sb.AppendLine();
        }
    }

    static (int Kind, ushort Num, string Key) MemoryAccessSortKey(string key)
    {
        // Named MMIO first (by well-known address), then numeric $xxxx, then other exprs.
        ushort? known = key switch
        {
            "PPU_CTRL" => NESLib.PPU_CTRL,
            "PPU_MASK" => NESLib.PPU_MASK,
            "PPU_STATUS" => NESLib.PPU_STATUS,
            "PPU_OAM_ADDR" => NESLib.PPU_OAM_ADDR,
            "PPU_OAM_DATA" => NESLib.PPU_OAM_DATA,
            "PPU_SCROLL" => NESLib.PPU_SCROLL,
            "PPU_ADDR" => NESLib.PPU_ADDR,
            "PPU_DATA" => NESLib.PPU_DATA,
            "PPU_OAM_DMA" => NESLib.PPU_OAM_DMA,
            "APU_STATUS" => NESLib.APU_STATUS,
            "APU_PULSE1_CTRL" => NESLib.APU_PULSE1_CTRL,
            "APU_PULSE2_CTRL" => NESLib.APU_PULSE2_CTRL,
            "APU_NOISE_CTRL" => NESLib.APU_NOISE_CTRL,
            "APU_FRAME" => NESLib.APU_FRAME,
            "JOYPAD1" => NESLib.JOYPAD1,
            "JOYPAD2" => NESLib.JOYPAD2,
            _ => null,
        };
        if (known.HasValue)
            return (0, known.Value, key);
        if (key.Length >= 2 && key[0] == '$')
        {
            ReadOnlySpan<char> hex = key.AsSpan(1);
            if (hex.EndsWith("+x", StringComparison.Ordinal) || hex.EndsWith("+y", StringComparison.Ordinal))
                hex = hex[..^2];
            if (ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort num))
                return (1, num, key);
        }
        return (2, 0, key);
    }

    static Dictionary<string, (int Peeks, int Pokes)> TallyMemoryAccess(IEnumerable<string> lines)
    {
        var map = new Dictionary<string, (int Peeks, int Pokes)>(StringComparer.Ordinal);
        foreach (string line in lines)
            TallyMemoryAccessLine(line.Trim(), map);
        return map;
    }

    static void TallyMemoryAccessLine(string line, Dictionary<string, (int Peeks, int Pokes)> map)
    {
        // Lifted NESLib calls still count as the underlying MMIO traffic.
        if (line == "waitvsync();")
        {
            AddAccess(map, "PPU_STATUS", peek: true);
            return;
        }
        if (line.StartsWith("ppu_scroll(", StringComparison.Ordinal))
        {
            AddAccess(map, "PPU_SCROLL", peek: false);
            AddAccess(map, "PPU_SCROLL", peek: false);
            return;
        }
        if (line.StartsWith("oam_dma(", StringComparison.Ordinal))
        {
            AddAccess(map, "PPU_OAM_ADDR", peek: false);
            AddAccess(map, "PPU_OAM_DMA", peek: false);
            return;
        }
        if (line.Contains("ppu_status()", StringComparison.Ordinal))
        {
            AddAccess(map, "PPU_STATUS", peek: true);
            return;
        }
        if (line.StartsWith("vram_adr(", StringComparison.Ordinal))
        {
            AddAccess(map, "PPU_ADDR", peek: false);
            AddAccess(map, "PPU_ADDR", peek: false);
            return;
        }
        if (line.StartsWith("vram_put(", StringComparison.Ordinal))
        {
            AddAccess(map, "PPU_DATA", peek: false);
            return;
        }
        if (line.StartsWith("ppu_ctrl(", StringComparison.Ordinal))
        {
            AddAccess(map, "PPU_CTRL", peek: false);
            return;
        }
        if (line.StartsWith("ppu_mask_write(", StringComparison.Ordinal))
        {
            AddAccess(map, "PPU_MASK", peek: false);
            return;
        }
        if (line.Contains("joypad1_read()", StringComparison.Ordinal))
        {
            AddAccess(map, "JOYPAD1", peek: true);
            return;
        }
        if (line.Contains("joypad2_read()", StringComparison.Ordinal))
        {
            AddAccess(map, "JOYPAD2", peek: true);
            return;
        }
        if (line.StartsWith("joypad1_write(", StringComparison.Ordinal))
        {
            AddAccess(map, "JOYPAD1", peek: false);
            return;
        }
        if (line.StartsWith("apu_frame(", StringComparison.Ordinal))
        {
            AddAccess(map, "APU_FRAME", peek: false);
            return;
        }
        if (line.StartsWith("apu_status_write(", StringComparison.Ordinal))
        {
            AddAccess(map, "APU_STATUS", peek: false);
            return;
        }

        for (int i = 0; i < line.Length; i++)
        {
            if (StartsWithAt(line, i, "peek("))
            {
                if (TryReadCallArg(line, i + "peek(".Length, out string arg, out int end)
                    && NormalizeAccessAddress(arg, out string key))
                    AddAccess(map, key, peek: true);
                i = Math.Max(i, end - 1);
                continue;
            }
            if (StartsWithAt(line, i, "poke("))
            {
                if (TryReadPokeAddr(line, i + "poke(".Length, out string arg, out int end)
                    && NormalizeAccessAddress(arg, out string key))
                    AddAccess(map, key, peek: false);
                i = Math.Max(i, end - 1);
            }
        }
    }

    static void AddAccess(Dictionary<string, (int Peeks, int Pokes)> map, string key, bool peek)
    {
        map.TryGetValue(key, out var cur);
        map[key] = peek ? (cur.Peeks + 1, cur.Pokes) : (cur.Peeks, cur.Pokes + 1);
    }

    static bool StartsWithAt(string s, int i, string token) =>
        i + token.Length <= s.Length
        && string.Compare(s, i, token, 0, token.Length, StringComparison.Ordinal) == 0;

    static bool TryReadCallArg(string line, int start, out string arg, out int end)
    {
        arg = null!;
        end = start;
        int depth = 1;
        for (int i = start; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    arg = line[start..i].Trim();
                    end = i + 1;
                    return arg.Length > 0;
                }
            }
        }
        return false;
    }

    static bool TryReadPokeAddr(string line, int start, out string arg, out int end)
    {
        arg = null!;
        end = start;
        int depth = 0;
        for (int i = start; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                arg = line[start..i].Trim();
                end = i;
                return arg.Length > 0;
            }
        }
        return false;
    }

    static bool NormalizeAccessAddress(string expr, out string key)
    {
        key = null!;
        string e = expr.Trim();
        if (e.Length == 0)
            return false;

        // (ushort)(BASE + x|y)  or  BASE + x
        string index = "";
        if (e.EndsWith(" + x", StringComparison.Ordinal))
        {
            index = "+x";
            e = e[..^4].Trim();
        }
        else if (e.EndsWith(" + y", StringComparison.Ordinal))
        {
            index = "+y";
            e = e[..^4].Trim();
        }

        if (e.StartsWith("(ushort)(", StringComparison.Ordinal) && e.EndsWith(')'))
            e = e["(ushort)(".Length..^1].Trim();
        else if (e.StartsWith("(ushort)", StringComparison.Ordinal))
            e = e["(ushort)".Length..].Trim();

        if (Regex.IsMatch(e, @"^[A-Z][A-Z0-9_]*$"))
        {
            key = e + index;
            return true;
        }

        if (e.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(e.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex))
        {
            key = hex <= 0xFF ? $"${hex:X2}{index}" : $"${hex:X4}{index}";
            return true;
        }

        // Indirect / messy — keep a shortened form.
        if (e.Length > 48)
            e = e[..45] + "...";
        key = e + index;
        return true;
    }

    string RoutineName(int bank, ushort addr) =>
        _disasm.Banks.IsFixedCpuAddress(addr) ? $"func_{addr:X4}" : $"func_{bank:X2}_{addr:X4}";

    static string LabelName(ushort addr) => $"L_{addr:X4}";

    HashSet<(int Bank, ushort Address)> CollectCallTargets() =>
        CollectAbsTargets(opcode: 0x20);

    HashSet<(int Bank, ushort Address)> CollectJmpTargets() =>
        CollectAbsTargets(opcode: 0x4C);

    HashSet<(int Bank, ushort Address)> CollectAbsTargets(byte opcode)
    {
        var targets = new HashSet<(int Bank, ushort Address)>();
        foreach (var insn in _disasm.Instructions.Values)
        {
            if (insn.Opcode != opcode || !insn.Op1.HasValue || !insn.Op2.HasValue)
                continue;
            ushort target = insn.AbsoluteTarget;
            if (target == insn.Address)
                continue; // self-JMP = while(true), not a routine entry

            // Switchable targets use the mapper window bank in effect at the source insn
            // (UxROM: LDA #bank / STA $8000+ updates SwitchableBank before JMP/JSR).
            int bank = _disasm.Banks.IsFixedCpuAddress(target)
                ? _disasm.Banks.SiteBank(target, insn.SwitchableBank)
                : insn.SwitchableBank;
            if (_disasm.TryGetInstruction(bank, target, out var at) && at.IsOfficial)
                targets.Add((bank, target));
        }
        return targets;
    }

    List<string> DecompileRoutine(int startBank, ushort start, IReadOnlyDictionary<(int Bank, ushort Address), string> routines)
    {
        // BFS: all instructions reachable inside this routine (stops at other entries / RTS / RTI)
        var reachable = new List<DecodedInstruction>();
        var seen = new HashSet<int>();
        var work = new Queue<(ushort Pc, int SwitchBank)>();
        var labelTargets = new HashSet<ushort>();
        work.Enqueue((start, startBank));

        while (work.Count > 0 && reachable.Count < 8192)
        {
            var (pc, switchBank) = work.Dequeue();
            int safety = 0;
            while (safety++ < 4096)
            {
                int siteBank = _disasm.Banks.SiteBank(pc, switchBank);
                if (!_disasm.TryGetInstruction(siteBank, pc, out var insn))
                    break;
                if (!seen.Add(insn.PrgOffset))
                    break;
                if (pc != start && routines.ContainsKey((siteBank, pc)))
                    break;

                reachable.Add(insn);
                byte op = insn.Opcode;

                if (op == 0x4C && insn.Op1.HasValue && insn.Op2.HasValue)
                {
                    ushort target = insn.AbsoluteTarget;
                    if (target == pc)
                        break;
                    if (target == start)
                    {
                        labelTargets.Add(start);
                        break;
                    }
                    int tBank = _disasm.Banks.SiteBank(target, switchBank);
                    if (routines.ContainsKey((tBank, target)))
                        break;
                    if (_disasm.TryGetInstruction(tBank, target, out _))
                    {
                        labelTargets.Add(target);
                        work.Enqueue((target, switchBank));
                    }
                    break;
                }

                if (IsBranch(op) && insn.Op1.HasValue)
                {
                    ushort target = (ushort)(pc + 2 + (sbyte)insn.Op1.Value);
                    int tBank = _disasm.Banks.SiteBank(target, switchBank);
                    // Always label branch targets inside this routine (including entry for loops)
                    if (target == start || !routines.ContainsKey((tBank, target)))
                    {
                        labelTargets.Add(target);
                        if (target != start)
                            work.Enqueue((target, switchBank));
                    }
                    // fall-through continues below
                }

                if (IsTerminator(op))
                    break;

                pc = (ushort)(pc + insn.Size);
            }
        }

        reachable.Sort((a, b) => a.Address.CompareTo(b.Address));

        var statements = new List<string>();
        byte? lastImmA = null, lastImmX = null, lastImmY = null;
        int bank = startBank;
        var indexByAddr = new Dictionary<ushort, int>();
        for (int i = 0; i < reachable.Count; i++)
            indexByAddr[reachable[i].Address] = i;

        for (int i = 0; i < reachable.Count; i++)
        {
            var insn = reachable[i];
            ushort pc = insn.Address;
            if (labelTargets.Contains(pc))
                statements.Add($"{LabelName(pc)}: ;");

            EmitInstruction(insn, pc, start, startBank, routines, indexByAddr, statements,
                ref lastImmA, ref lastImmX, ref lastImmY, ref bank);
        }

        // Hoist entry label to the top when targeted (branch targets may sort before entry).
        // Remove the mid-body copy so we never emit duplicate labels (CS0140).
        if (labelTargets.Contains(start))
        {
            string entry = LabelName(start);
            for (int i = statements.Count - 1; i >= 0; i--)
            {
                if (IsLabel(statements[i], entry))
                    statements.RemoveAt(i);
            }
            statements.Insert(0, $"{entry}: ;");
        }

        FoldImmPokes(statements);
        // Optimizer (default): structure spin loops / while(true). Exact keeps raw goto/labels.
        var structured = Optimize ? StructureLoops(statements) : statements;
        if (Optimize)
            LiftNesLibPatterns(structured);
        // C# forbids a label as the last statement in a block — ensure every label has a stmt.
        for (int i = 0; i < structured.Count; i++)
        {
            if (TryGetLabelName(structured[i], out string name) && structured[i].TrimEnd() == name + ":")
                structured[i] = name + ": ;";
        }
        return structured;
    }

    /// <summary>
    /// Replace soft-CPU idioms with NESLib calls when semantics match.
    /// Does not invent helpers that do extra work (e.g. poke CTRL/MASK ≠ ppu_off).
    /// </summary>
    static void LiftNesLibPatterns(List<string> statements)
    {
        for (int i = 0; i < statements.Count; i++)
        {
            string indent = LeadingIndent(statements[i]);
            string line = statements[i].TrimStart();

            // do { a = peek(PPU_STATUS); } while (!negative);  →  waitvsync();
            if (IsOneLineWaitVsync(line))
            {
                statements[i] = indent + "waitvsync();";
                continue;
            }

            // Multi-line structured form of the same spin.
            if (i + 4 < statements.Count
                && line == "do"
                && statements[i + 1].TrimStart() == "{"
                && IsPeekPpuStatus(statements[i + 2].Trim())
                && statements[i + 3].TrimStart() == "}"
                && statements[i + 4].TrimStart() == "while (!negative);")
            {
                statements.RemoveRange(i, 5);
                statements.Insert(i, indent + "waitvsync();");
                continue;
            }

            // poke(PPU_SCROLL, x); … poke(PPU_SCROLL, y);  →  ppu_scroll(x, y)
            // Allow non-PPU statements between (e.g. shadow ZP writes); PPU still sees both $2005 writes.
            if (TryParsePoke(line, out string scrollAddr1, out string sx)
                && scrollAddr1 == "PPU_SCROLL"
                && TryFindNextPpuScroll(statements, i + 1, out int scrollJ, out string sy))
            {
                statements.RemoveAt(scrollJ);
                statements[i] = indent + $"ppu_scroll({sx}, {sy});";
                continue;
            }

            // poke(PPU_OAM_ADDR, 0); poke(PPU_OAM_DMA, page);  →  oam_dma(page);
            if (i + 1 < statements.Count
                && TryParsePoke(line, out string oamAddr, out string oamZero)
                && TryParsePoke(statements[i + 1].TrimStart(), out string oamDma, out string page)
                && oamAddr == "PPU_OAM_ADDR" && oamZero is "0" or "0x00"
                && oamDma == "PPU_OAM_DMA")
            {
                statements.RemoveRange(i, 2);
                statements.Insert(i, indent + $"oam_dma({page});");
                continue;
            }

            // poke(PPU_ADDR, hi); poke(PPU_ADDR, lo);  →  vram_adr(...)
            if (i + 1 < statements.Count
                && TryParsePoke(line, out string adrHiReg, out string hi)
                && TryParsePoke(statements[i + 1].TrimStart(), out string adrLoReg, out string lo)
                && adrHiReg == "PPU_ADDR" && adrLoReg == "PPU_ADDR")
            {
                statements.RemoveRange(i, 2);
                statements.Insert(i, indent + FormatLiftedVramAdr(hi, lo));
                continue;
            }

            // poke(PPU_DATA, v);  →  vram_put(v);  (palette colors refined later)
            if (TryParsePoke(line, out string dataReg, out string dataVal)
                && dataReg == "PPU_DATA")
            {
                statements[i] = indent + $"vram_put({dataVal});";
                continue;
            }

            // poke(PPU_CTRL, v);  →  ppu_ctrl(PPUCTRL…);
            if (TryParsePoke(line, out string ctrlReg, out string ctrlVal)
                && ctrlReg == "PPU_CTRL")
            {
                statements[i] = indent + FormatLiftedEnumCall("ppu_ctrl", ctrlVal, NesLibEnumFormatter.FormatPpuCtrl, "PPUCTRL");
                continue;
            }

            // poke(PPU_MASK, v);  →  ppu_mask_write(MASK…);  (not ppu_mask — that is the NMI shadow)
            if (TryParsePoke(line, out string maskReg, out string maskVal)
                && maskReg == "PPU_MASK")
            {
                statements[i] = indent + FormatLiftedEnumCall("ppu_mask_write", maskVal, NesLibEnumFormatter.FormatMask, "MASK");
                continue;
            }

            // poke(APU_STATUS, v);  →  apu_status_write(APUSTATUS…);
            if (TryParsePoke(line, out string apuStatReg, out string apuStatVal)
                && apuStatReg == "APU_STATUS")
            {
                statements[i] = indent + FormatLiftedEnumCall("apu_status_write", apuStatVal, NesLibEnumFormatter.FormatApuStatus, "APUSTATUS");
                continue;
            }

            // a = peek(PPU_STATUS);  →  a = ppu_status();
            if (IsPeekPpuStatus(line))
            {
                statements[i] = indent + "a = ppu_status();";
                continue;
            }

            // peek(JOYPAD1/2) → joypad*_read();  poke(JOYPAD1) → joypad1_write;
            // poke($4017) is APU frame counter (same addr as JOYPAD2 on read).
            if (TryLiftJoypadOrApuLine(line, out string liftedJoy))
                statements[i] = indent + liftedJoy;
        }
    }

    static bool TryLiftJoypadOrApuLine(string line, out string lifted)
    {
        lifted = null!;
        if (TryParseAssignPeek(line, out string dest, out string peekAddr))
        {
            if (peekAddr == "JOYPAD1")
            {
                lifted = $"{dest} = joypad1_read();";
                return true;
            }
            if (peekAddr == "JOYPAD2")
            {
                lifted = $"{dest} = joypad2_read();";
                return true;
            }
        }
        if (TryParsePoke(line, out string pokeAddr, out string pokeVal))
        {
            if (pokeAddr == "JOYPAD1")
            {
                lifted = $"joypad1_write({pokeVal});";
                return true;
            }
            if (pokeAddr is "JOYPAD2" or "APU_FRAME")
            {
                lifted = FormatLiftedEnumCall("apu_frame", pokeVal, NesLibEnumFormatter.FormatApuFrame, "APUFRAME");
                return true;
            }
        }
        return false;
    }

    static bool TryParseAssignPeek(string line, out string dest, out string addr)
    {
        dest = addr = null!;
        // a = peek(JOYPAD1);
        const string mid = " = peek(";
        if (!line.EndsWith(");", StringComparison.Ordinal))
            return false;
        int at = line.IndexOf(mid, StringComparison.Ordinal);
        if (at < 0)
            return false;
        dest = line[..at].Trim();
        if (dest is not ("a" or "x" or "y"))
            return false;
        addr = line[(at + mid.Length)..^2].Trim();
        return addr.Length > 0;
    }

    /// <summary>
    /// Zero pages used as packed controller state: shifted in after joypad*_read, then copied/masked.
    /// </summary>
    static HashSet<byte> CollectPadZeroPages(IEnumerable<List<string>> bodies)
    {
        var pads = new HashSet<byte>();
        var bodyList = bodies.ToList();

        foreach (var body in bodyList)
        {
            int joyWindow = 0;
            foreach (string raw in body)
            {
                string t = raw.TrimStart();
                if (t.Contains("joypad1_read()", StringComparison.Ordinal)
                    || t.Contains("joypad2_read()", StringComparison.Ordinal))
                    joyWindow = 32;

                if (joyWindow > 0)
                {
                    joyWindow--;
                    // Skip $00–$0F: commercial games often shift into ZP temps that are
                    // also general pointers (Tetris ROL $00/$01), which must not be typed as PAD.
                    foreach (byte zp in ExtractZpAddresses(t))
                    {
                        if (zp >= 0x10)
                            pads.Add(zp);
                    }
                    if (t.StartsWith("return", StringComparison.Ordinal))
                        joyWindow = 0;
                }
            }
        }

        // Propagate: poke(zp, a/y) while A/Y still holds pad bits.
        bool changed;
        do
        {
            changed = false;
            foreach (var body in bodyList)
            {
                bool aPad = false, yPad = false;
                foreach (string raw in body)
                {
                    string t = raw.TrimStart();
                    if (TryParseAssignPeekZp(t, out string dest, out byte srcZp))
                    {
                        bool fromPad = pads.Contains(srcZp);
                        if (dest == "a") aPad = fromPad;
                        else if (dest == "y") yPad = fromPad;
                        continue;
                    }

                    if (t.StartsWith("a = (byte)(a & peek(", StringComparison.Ordinal)
                        || t.StartsWith("a = (byte)(a | peek(", StringComparison.Ordinal)
                        || t.StartsWith("a = (byte)(a ^ peek(", StringComparison.Ordinal))
                    {
                        foreach (byte zp in ExtractZpAddresses(t))
                        {
                            if (pads.Contains(zp))
                                aPad = true;
                        }
                        continue;
                    }

                    if (t.StartsWith("a = (byte)(a & ", StringComparison.Ordinal)
                        || t.StartsWith("a = (byte)(a | ", StringComparison.Ordinal)
                        || t.StartsWith("a = (byte)(a ^ ", StringComparison.Ordinal))
                    {
                        // Keep pad-ness through immediate mask/or.
                        continue;
                    }

                    if (t == "y = a;")
                    {
                        yPad = aPad;
                        continue;
                    }
                    if (t == "a = y;")
                    {
                        aPad = yPad;
                        continue;
                    }

                    if (TryParsePokeZp(t, out byte dstZp, out string val))
                    {
                        bool fromPad = val switch
                        {
                            "a" => aPad,
                            "y" => yPad,
                            _ => false,
                        };
                        if (fromPad && pads.Add(dstZp))
                            changed = true;
                        continue;
                    }

                    if (t.StartsWith("a = ", StringComparison.Ordinal))
                        aPad = false;
                    else if (t.StartsWith("y = ", StringComparison.Ordinal))
                        yPad = false;
                }
            }
        } while (changed);

        // Nearby high-ZP shadows (e.g. Tetris $F7 previous-buttons next to $F5/$F6).
        // Require adjacency so PPU CTRL/MASK shadows at $FE/$FF are not typed as PAD.
        if (pads.Count > 0)
        {
            foreach (var body in bodyList)
            {
                byte? lastPeekZp = null;
                foreach (string raw in body)
                {
                    string t = raw.TrimStart();
                    if (TryParseAssignPeekZp(t, out string dest, out byte srcZp) && dest == "a")
                    {
                        lastPeekZp = srcZp;
                        continue;
                    }

                    if (lastPeekZp is byte zp && zp >= 0xF0 && !pads.Contains(zp)
                        && pads.Any(p => Math.Abs(p - zp) <= 2)
                        && LooksLikePadImmediateUse(t))
                        pads.Add(zp);

                    if (t.StartsWith("a = ", StringComparison.Ordinal))
                        lastPeekZp = null;
                }
            }
        }

        return pads;
    }

    static bool LooksLikePadImmediateUse(string line)
    {
        foreach (string op in new[] { "&", "|", "^" })
        {
            string prefix = $"a = (byte)(a {op} ";
            if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(");", StringComparison.Ordinal))
            {
                string imm = line[prefix.Length..^2].Trim();
                return TryParseByteLiteral(imm, out byte b)
                    && NesLibEnumFormatter.TryFormatPadByteOperand(b, out _);
            }
        }

        const string zeroPrefix = "zero = a == ";
        if (!line.StartsWith(zeroPrefix, StringComparison.Ordinal))
            return false;
        int semi = line.IndexOf(';');
        if (semi < 0)
            return false;
        string cmp = line[zeroPrefix.Length..semi].Trim();
        return TryParseByteLiteral(cmp, out byte c) && NesLibEnumFormatter.IsExactPadFlags(c);
    }

    static (HashSet<byte> Mask, HashSet<byte> Ctrl) CollectPpuShadowZeroPages(IEnumerable<List<string>> bodies)
    {
        var mask = new HashSet<byte>();
        var ctrl = new HashSet<byte>();
        foreach (var body in bodies)
        {
            byte? lastPeekZp = null;
            foreach (string raw in body)
            {
                string t = raw.TrimStart();
                if (TryParseAssignPeekZp(t, out string dest, out byte zp) && dest == "a")
                {
                    lastPeekZp = zp;
                    continue;
                }

                if (lastPeekZp is byte shadow)
                {
                    if (t.StartsWith("ppu_mask_write(", StringComparison.Ordinal))
                        mask.Add(shadow);
                    else if (t.StartsWith("ppu_ctrl(", StringComparison.Ordinal))
                        ctrl.Add(shadow);
                }

                // Keep shadow through AND/OR of immediates before the write.
                if (t.StartsWith("a = (byte)(a & ", StringComparison.Ordinal)
                    || t.StartsWith("a = (byte)(a | ", StringComparison.Ordinal)
                    || t.StartsWith("a = (byte)(a ^ ", StringComparison.Ordinal))
                    continue;

                if (t.StartsWith("a = ", StringComparison.Ordinal)
                    || t.StartsWith("ppu_", StringComparison.Ordinal)
                    || t.StartsWith("poke(", StringComparison.Ordinal))
                    lastPeekZp = null;
            }
        }
        return (mask, ctrl);
    }

    const byte AKindNone = 0, AKindPad = 1, AKindMask = 2, AKindCtrl = 3;

    static void FormatTypedZpLiterals(
        List<string> statements,
        HashSet<byte> padZps,
        HashSet<byte> maskZps,
        HashSet<byte> ctrlZps)
    {
        bool writingPalette = false;
        byte aKind = AKindNone;

        for (int i = 0; i < statements.Count; i++)
        {
            string indent = LeadingIndent(statements[i]);
            string line = statements[i].TrimStart();

            if (TryParseVramAdrConstant(line, out ushort vaddr))
            {
                writingPalette = (vaddr & 0xFF00) == 0x3F00;
            }
            else if (line.StartsWith("vram_adr(", StringComparison.Ordinal))
            {
                writingPalette = false;
            }

            if (line.StartsWith("vram_put(", StringComparison.Ordinal) && writingPalette
                && TryParseVramPutLiteral(line, out byte color))
            {
                statements[i] = indent + $"vram_put({NesLibEnumFormatter.FormatNesColor(color)});";
                continue;
            }

            if (TryParseAssignPeekZp(line, out string dest, out byte srcZp))
            {
                if (dest == "a")
                {
                    aKind = padZps.Contains(srcZp) ? AKindPad
                        : maskZps.Contains(srcZp) ? AKindMask
                        : ctrlZps.Contains(srcZp) ? AKindCtrl
                        : AKindNone;
                }
                continue;
            }

            if (aKind != AKindNone && TryRewriteTypedImmediateLine(line, aKind, out string rewritten))
            {
                statements[i] = indent + rewritten;
                continue;
            }

            if (line.StartsWith("a = (byte)(a & peek(", StringComparison.Ordinal)
                || line.StartsWith("a = (byte)(a | peek(", StringComparison.Ordinal))
            {
                foreach (byte zp in ExtractZpAddresses(line))
                {
                    if (padZps.Contains(zp)) aKind = AKindPad;
                    else if (maskZps.Contains(zp)) aKind = AKindMask;
                    else if (ctrlZps.Contains(zp)) aKind = AKindCtrl;
                }
                continue;
            }

            if (line.StartsWith("a = ", StringComparison.Ordinal)
                && !line.StartsWith("a = (byte)(a & ", StringComparison.Ordinal)
                && !line.StartsWith("a = (byte)(a | ", StringComparison.Ordinal)
                && !line.StartsWith("a = (byte)(a ^ ", StringComparison.Ordinal))
            {
                aKind = AKindNone;
            }
        }
    }

    static bool TryRewriteTypedImmediateLine(string line, byte kind, out string rewritten)
    {
        rewritten = null!;

        Func<byte, string?> formatOp = kind switch
        {
            AKindPad => b => NesLibEnumFormatter.TryFormatPadByteOperand(b, out string e) ? e : null,
            AKindMask => b => FormatFlagsByteOperand(b, NesLibEnumFormatter.FormatMask, tryClear: true),
            AKindCtrl => b => FormatFlagsByteOperand(b, NesLibEnumFormatter.FormatPpuCtrl, tryClear: true),
            _ => _ => null,
        };

        foreach (string op in new[] { "&", "|", "^" })
        {
            string prefix = $"a = (byte)(a {op} ";
            if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(");", StringComparison.Ordinal))
            {
                string imm = line[prefix.Length..^2].Trim();
                if (!TryParseByteLiteral(imm, out byte b))
                    return false;
                string? expr = formatOp(b);
                if (expr == null)
                    return false;
                rewritten = $"{prefix}{expr});";
                return true;
            }
        }

        if (kind != AKindPad)
            return false;

        const string zeroPrefix = "zero = a == ";
        if (!line.StartsWith(zeroPrefix, StringComparison.Ordinal))
            return false;
        int semi = line.IndexOf(';');
        if (semi < 0)
            return false;
        string immCmp = line[zeroPrefix.Length..semi].Trim();
        if (!TryParseByteLiteral(immCmp, out byte cb) || !NesLibEnumFormatter.IsExactPadFlags(cb))
            return false;
        if (!NesLibEnumFormatter.TryFormatPadByteOperand(cb, out string padExpr))
            return false;

        rewritten = line.Replace($"a == {immCmp}", $"a == {padExpr}")
            .Replace($"a >= {immCmp}", $"a >= {padExpr}")
            .Replace($"(a - {immCmp})", $"(a - {padExpr})");
        return true;
    }

    static string? FormatFlagsByteOperand(byte value, Func<byte, string> formatFlags, bool tryClear)
    {
        if (tryClear)
        {
            byte cleared = (byte)~value;
            if (cleared != 0 && BitCount(cleared) <= 2)
            {
                string clearedFmt = formatFlags(cleared);
                if (!clearedFmt.Contains("0x", StringComparison.Ordinal) && clearedFmt != "0")
                {
                    return clearedFmt.Contains('|', StringComparison.Ordinal)
                        ? $"(byte)~({clearedFmt})"
                        : $"(byte)~{clearedFmt}";
                }
            }
        }

        if (value == 0)
            return "0";
        string formatted = formatFlags(value);
        if (formatted.Contains("0x", StringComparison.Ordinal))
            return null; // leftover unknown bits — keep raw hex
        return formatted.Contains('|', StringComparison.Ordinal) || formatted == "0"
            ? $"(byte)({formatted})"
            : $"(byte){formatted}";
    }

    static int BitCount(byte value)
    {
        int n = 0;
        while (value != 0)
        {
            n += value & 1;
            value >>= 1;
        }
        return n;
    }

    static bool TryParseAssignPeekZp(string line, out string dest, out byte zp)
    {
        dest = null!;
        zp = 0;
        // a = peek((ushort)0xF5);
        const string mid = " = peek((ushort)0x";
        if (!line.EndsWith(");", StringComparison.Ordinal))
            return false;
        int at = line.IndexOf(mid, StringComparison.Ordinal);
        if (at < 0)
            return false;
        dest = line[..at].Trim();
        if (dest is not ("a" or "x" or "y"))
            return false;
        string hex = line[(at + mid.Length)..^2];
        return byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out zp);
    }

    static bool TryParsePokeZp(string line, out byte zp, out string value)
    {
        zp = 0;
        value = null!;
        // poke((ushort)0xF5, a);
        const string prefix = "poke((ushort)0x";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(");", StringComparison.Ordinal))
            return false;
        int comma = line.IndexOf(',', prefix.Length);
        if (comma < 0)
            return false;
        string hex = line[prefix.Length..comma];
        if (!byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out zp))
            return false;
        value = line[(comma + 1)..^2].Trim();
        return value.Length > 0;
    }

    static IEnumerable<byte> ExtractZpAddresses(string line)
    {
        // (ushort)0xF5 or (ushort)0xf5
        int i = 0;
        while (i < line.Length)
        {
            int at = line.IndexOf("(ushort)0x", i, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                yield break;
            int hexStart = at + "(ushort)0x".Length;
            int hexEnd = hexStart;
            while (hexEnd < line.Length && Uri.IsHexDigit(line[hexEnd]))
                hexEnd++;
            string hex = line[hexStart..hexEnd];
            if ((hex.Length is 1 or 2)
                && byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte zp))
                yield return zp;
            i = hexEnd;
        }
    }

    static bool TryParseVramAdrConstant(string line, out ushort addr)
    {
        addr = 0;
        // vram_adr(0x3F00);  or vram_adr(NTADR_…) — only raw hex / ushort cast of constant
        if (!line.StartsWith("vram_adr(", StringComparison.Ordinal) || !line.EndsWith(");", StringComparison.Ordinal))
            return false;
        string inner = line["vram_adr(".Length..^2].Trim();
        if (inner.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(inner.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
        if (inner.StartsWith("(ushort)0x", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(inner.AsSpan("(ushort)0x".Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
        return false;
    }

    static bool TryParseVramPutLiteral(string line, out byte value)
    {
        value = 0;
        if (!line.StartsWith("vram_put(", StringComparison.Ordinal) || !line.EndsWith(");", StringComparison.Ordinal))
            return false;
        return TryParseByteLiteral(line["vram_put(".Length..^2].Trim(), out value);
    }

    /// <summary>
    /// Emit <c>fn(EnumFlags)</c> for byte literals, or <c>fn((Enum)reg)</c> for dynamic values.
    /// </summary>
    static string FormatLiftedEnumCall(string fn, string value, Func<byte, string> formatFlags, string enumName)
    {
        if (TryParseByteLiteral(value, out byte b))
        {
            string formatted = formatFlags(b);
            return $"{fn}({formatted});";
        }
        return $"{fn}(({enumName}){value});";
    }

    static string LeadingIndent(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t')
            i++;
        return line[..i];
    }

    static bool TryFindNextPpuScroll(List<string> statements, int start, out int index, out string value)
    {
        value = null!;
        index = -1;
        for (int j = start; j < statements.Count && j < start + 8; j++)
        {
            string t = statements[j].TrimStart();
            if (TryParsePoke(t, out string addr, out value) && addr == "PPU_SCROLL")
            {
                index = j;
                return true;
            }
            // Latch-resetting or other PPU traffic between scroll halves — don't fuse.
            if (t.Contains("PPU_", StringComparison.Ordinal)
                || t.Contains("ppu_status", StringComparison.Ordinal)
                || t.Contains("ppu_ctrl", StringComparison.Ordinal)
                || t.Contains("ppu_mask", StringComparison.Ordinal)
                || t.Contains("waitvsync", StringComparison.Ordinal)
                || t.Contains("vram_", StringComparison.Ordinal))
                return false;
        }
        return false;
    }

    static string FormatLiftedVramAdr(string hi, string lo)
    {
        if (TryParseByteLiteral(hi, out byte hiB) && TryParseByteLiteral(lo, out byte loB))
            return NesLibCallFormatter.FormatVramAdr(loB, hiB);
        if (lo is "0" or "0x00")
            return $"vram_adr((ushort)({hi} << 8));";
        return $"vram_adr((ushort)(({hi} << 8) | {lo}));";
    }

    static bool TryParseByteLiteral(string expr, out byte value)
    {
        value = 0;
        string e = expr.Trim();
        if (e.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.TryParse(e.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return byte.TryParse(e, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    static bool IsOneLineWaitVsync(string line) =>
        line == "do { a = peek(PPU_STATUS); } while (!negative);";

    static bool IsPeekPpuStatus(string line) =>
        line == "a = peek(PPU_STATUS);";

    static bool TryParsePoke(string line, out string addr, out string value)
    {
        addr = value = null!;
        const string prefix = "poke(";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(");", StringComparison.Ordinal))
            return false;
        string inner = line[prefix.Length..^2];
        int comma = FindTopLevelComma(inner);
        if (comma < 0)
            return false;
        addr = inner[..comma].Trim();
        value = inner[(comma + 1)..].Trim();
        return addr.Length > 0 && value.Length > 0;
    }

    static int FindTopLevelComma(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
                return i;
        }
        return -1;
    }

    static bool IsLabel(string line, string name) =>
        line == name + ":" || line == name + ": ;" || line.StartsWith(name + ":", StringComparison.Ordinal);

    static bool TryGetLabelName(string line, out string name)
    {
        name = null!;
        string t = line.Trim();
        if (t.EndsWith(": ;", StringComparison.Ordinal))
        {
            name = t[..^3].Trim();
            return name.StartsWith("L_", StringComparison.Ordinal);
        }
        if (t.EndsWith(':') && !t.Contains(' ', StringComparison.Ordinal))
        {
            name = t[..^1];
            return name.StartsWith("L_", StringComparison.Ordinal);
        }
        return false;
    }

    void EmitInstruction(
        DecodedInstruction insn, ushort pc, ushort routineStart, int routineBank,
        IReadOnlyDictionary<(int Bank, ushort Address), string> routines,
        Dictionary<ushort, int> indexByAddr,
        List<string> statements,
        ref byte? lastImmA, ref byte? lastImmX, ref byte? lastImmY, ref int bank)
    {
        byte op = insn.Opcode;
        byte? o1 = insn.Op1;
        byte? o2 = insn.Op2;

        switch (op)
        {
            case 0xA9 when o1.HasValue: // LDA #
                lastImmA = o1.Value;
                statements.Add($"a = {FormatByte(o1.Value)};");
                return;
            case 0xA2 when o1.HasValue: // LDX #
                lastImmX = o1.Value;
                statements.Add($"x = {FormatByte(o1.Value)};");
                return;
            case 0xA0 when o1.HasValue: // LDY #
                lastImmY = o1.Value;
                statements.Add($"y = {FormatByte(o1.Value)};");
                return;
            case 0xA5 when o1.HasValue: // LDA zp
                lastImmA = null;
                statements.Add($"a = peek({FormatZp(o1.Value)});");
                return;
            case 0xA6 when o1.HasValue: // LDX zp
                lastImmX = null;
                statements.Add($"x = peek({FormatZp(o1.Value)});");
                return;
            case 0xA4 when o1.HasValue: // LDY zp
                lastImmY = null;
                statements.Add($"y = peek({FormatZp(o1.Value)});");
                return;
            case 0xAD when o1.HasValue && o2.HasValue: // LDA abs
                lastImmA = null;
                statements.Add($"a = peek({FormatAddr(Abs(o1, o2))});");
                return;
            case 0xAE when o1.HasValue && o2.HasValue: // LDX abs
                lastImmX = null;
                statements.Add($"x = peek({FormatAddr(Abs(o1, o2))});");
                return;
            case 0xAC when o1.HasValue && o2.HasValue: // LDY abs
                lastImmY = null;
                statements.Add($"y = peek({FormatAddr(Abs(o1, o2))});");
                return;
            case 0xBD when o1.HasValue && o2.HasValue: // LDA abs,X
                lastImmA = null;
                statements.Add($"a = peek({IndexAddr(FormatAddr(Abs(o1, o2)), "x")});");
                return;
            case 0xB9 when o1.HasValue && o2.HasValue: // LDA abs,Y
                lastImmA = null;
                statements.Add($"a = peek({IndexAddr(FormatAddr(Abs(o1, o2)), "y")});");
                return;
            case 0xB5 when o1.HasValue: // LDA zp,X
                lastImmA = null;
                statements.Add($"a = peek({IndexAddr(FormatZp(o1.Value), "x")});");
                return;
            case 0xB1 when o1.HasValue: // LDA (zp),Y
                lastImmA = null;
                statements.Add($"a = peek({IndexAddr(IndirectZp(o1.Value), "y")});");
                return;
            case 0x85 when o1.HasValue: // STA zp
                statements.Add(Store("a", lastImmA, FormatZp(o1.Value)));
                return;
            case 0x86 when o1.HasValue: // STX zp
                statements.Add(Store("x", lastImmX, FormatZp(o1.Value)));
                return;
            case 0x84 when o1.HasValue: // STY zp
                statements.Add(Store("y", lastImmY, FormatZp(o1.Value)));
                return;
            case 0x95 when o1.HasValue: // STA zp,X
                statements.Add($"poke({IndexAddr(FormatZp(o1.Value), "x")}, a);");
                lastImmA = null;
                return;
            case 0x91 when o1.HasValue: // STA (zp),Y
                statements.Add($"poke({IndexAddr(IndirectZp(o1.Value), "y")}, a);");
                lastImmA = null;
                return;
            case 0x8D when o1.HasValue && o2.HasValue: // STA abs
            {
                ushort addr = Abs(o1, o2);
                statements.Add(Store("a", lastImmA, addr));
                if (lastImmA.HasValue)
                    bank = ApplyBankSwitch(bank, addr, lastImmA.Value);
                return;
            }
            case 0x8E when o1.HasValue && o2.HasValue: // STX abs
            {
                ushort addr = Abs(o1, o2);
                statements.Add(Store("x", lastImmX, addr));
                if (lastImmX.HasValue)
                    bank = ApplyBankSwitch(bank, addr, lastImmX.Value);
                return;
            }
            case 0x8C when o1.HasValue && o2.HasValue: // STY abs
            {
                ushort addr = Abs(o1, o2);
                statements.Add(Store("y", lastImmY, addr));
                if (lastImmY.HasValue)
                    bank = ApplyBankSwitch(bank, addr, lastImmY.Value);
                return;
            }
            case 0x9D when o1.HasValue && o2.HasValue: // STA abs,X
                statements.Add($"poke({IndexAddr(FormatAddr(Abs(o1, o2)), "x")}, a);");
                lastImmA = null;
                return;
            case 0x99 when o1.HasValue && o2.HasValue: // STA abs,Y
                statements.Add($"poke({IndexAddr(FormatAddr(Abs(o1, o2)), "y")}, a);");
                lastImmA = null;
                return;
            case 0xE6 when o1.HasValue: // INC zp
                statements.Add($"poke({FormatZp(o1.Value)}, (byte)(peek({FormatZp(o1.Value)}) + 1));");
                lastImmA = lastImmX = lastImmY = null;
                return;
            case 0xC6 when o1.HasValue: // DEC zp
                statements.Add($"poke({FormatZp(o1.Value)}, (byte)(peek({FormatZp(o1.Value)}) - 1));");
                lastImmA = lastImmX = lastImmY = null;
                return;
            case 0xEE when o1.HasValue && o2.HasValue: // INC abs
            {
                string a = FormatAddr(Abs(o1, o2));
                statements.Add($"poke({a}, (byte)(peek({a}) + 1));");
                lastImmA = lastImmX = lastImmY = null;
                return;
            }
            case 0xCE when o1.HasValue && o2.HasValue: // DEC abs
            {
                string a = FormatAddr(Abs(o1, o2));
                statements.Add($"poke({a}, (byte)(peek({a}) - 1));");
                lastImmA = lastImmX = lastImmY = null;
                return;
            }
            case 0x69 when o1.HasValue: // ADC #
                statements.Add($"a = (byte)(a + {FormatByte(o1.Value)} + (carry ? 1 : 0));");
                lastImmA = null;
                return;
            case 0x65 when o1.HasValue: // ADC zp
                statements.Add($"a = (byte)(a + peek({FormatZp(o1.Value)}) + (carry ? 1 : 0));");
                lastImmA = null;
                return;
            case 0x6D when o1.HasValue && o2.HasValue: // ADC abs
                statements.Add($"a = (byte)(a + peek({FormatAddr(Abs(o1, o2))}) + (carry ? 1 : 0));");
                lastImmA = null;
                return;
            case 0xE9 when o1.HasValue: // SBC #
                statements.Add($"a = (byte)(a - {FormatByte(o1.Value)} - (carry ? 0 : 1));");
                lastImmA = null;
                return;
            case 0xE5 when o1.HasValue: // SBC zp
                statements.Add($"a = (byte)(a - peek({FormatZp(o1.Value)}) - (carry ? 0 : 1));");
                lastImmA = null;
                return;
            case 0xC9 when o1.HasValue: // CMP #
                statements.Add($"zero = a == {FormatByte(o1.Value)}; carry = a >= {FormatByte(o1.Value)}; negative = (byte)(a - {FormatByte(o1.Value)}) >= 0x80;");
                return;
            case 0xC5 when o1.HasValue: // CMP zp
                statements.Add($"{{ byte _v = peek({FormatZp(o1.Value)}); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }}");
                return;
            case 0xCD when o1.HasValue && o2.HasValue: // CMP abs
                statements.Add($"{{ byte _v = peek({FormatAddr(Abs(o1, o2))}); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }}");
                return;
            case 0xE0 when o1.HasValue: // CPX #
                statements.Add($"zero = x == {FormatByte(o1.Value)}; carry = x >= {FormatByte(o1.Value)};");
                return;
            case 0xC0 when o1.HasValue: // CPY #
                statements.Add($"zero = y == {FormatByte(o1.Value)}; carry = y >= {FormatByte(o1.Value)};");
                return;
            case 0x29 when o1.HasValue: // AND #
                statements.Add($"a = (byte)(a & {FormatByte(o1.Value)});");
                lastImmA = null;
                return;
            case 0x25 when o1.HasValue: // AND zp
                statements.Add($"a = (byte)(a & peek({FormatZp(o1.Value)}));");
                lastImmA = null;
                return;
            case 0x09 when o1.HasValue: // ORA #
                statements.Add($"a = (byte)(a | {FormatByte(o1.Value)});");
                lastImmA = null;
                return;
            case 0x05 when o1.HasValue: // ORA zp
                statements.Add($"a = (byte)(a | peek({FormatZp(o1.Value)}));");
                lastImmA = null;
                return;
            case 0x49 when o1.HasValue: // EOR #
                statements.Add($"a = (byte)(a ^ {FormatByte(o1.Value)});");
                lastImmA = null;
                return;
            case 0x0A: // ASL A
                statements.Add("carry = (a & 0x80) != 0; a <<= 1;");
                lastImmA = null;
                return;
            case 0x06 when o1.HasValue: // ASL zp
                statements.Add($"{{ byte _m = peek({FormatZp(o1.Value)}); carry = (_m & 0x80) != 0; poke({FormatZp(o1.Value)}, (byte)(_m << 1)); }}");
                return;
            case 0x4A: // LSR A
                statements.Add("carry = (a & 1) != 0; a >>= 1;");
                lastImmA = null;
                return;
            case 0x46 when o1.HasValue: // LSR zp
                statements.Add($"{{ byte _m = peek({FormatZp(o1.Value)}); carry = (_m & 1) != 0; poke({FormatZp(o1.Value)}, (byte)(_m >> 1)); }}");
                return;
            case 0x2A: // ROL A
                statements.Add("{ bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }");
                lastImmA = null;
                return;
            case 0x26 when o1.HasValue: // ROL zp
                statements.Add($"{{ bool _c = carry; byte _m = peek({FormatZp(o1.Value)}); carry = (_m & 0x80) != 0; poke({FormatZp(o1.Value)}, (byte)((_m << 1) | (_c ? 1 : 0))); }}");
                return;
            case 0x6A: // ROR A
                statements.Add("{ bool _c = carry; carry = (a & 1) != 0; a = (byte)((a >> 1) | (_c ? 0x80 : 0)); }");
                lastImmA = null;
                return;
            case 0x66 when o1.HasValue: // ROR zp
                statements.Add($"{{ bool _c = carry; byte _m = peek({FormatZp(o1.Value)}); carry = (_m & 1) != 0; poke({FormatZp(o1.Value)}, (byte)((_m >> 1) | (_c ? 0x80 : 0))); }}");
                return;
            case 0x48: // PHA
                statements.Add("push(a);");
                return;
            case 0x68: // PLA
                statements.Add("a = pop();");
                lastImmA = null;
                return;
            case 0x08: // PHP
                statements.Add("php();");
                return;
            case 0x28: // PLP
                statements.Add("plp();");
                return;
            case 0x9A: // TXS
                statements.Add("txs();");
                return;
            case 0xBA: // TSX
                statements.Add("tsx();");
                return;
            case 0x20 when o1.HasValue && o2.HasValue: // JSR
                statements.Add($"{ResolveRoutineName(bank, Abs(o1, o2), routines)}();");
                lastImmA = lastImmX = lastImmY = null;
                return;
            case 0x4C when o1.HasValue && o2.HasValue: // JMP abs
            {
                ushort target = Abs(o1, o2);
                if (target == pc)
                {
                    statements.Add("while (true) ;");
                    return;
                }
                // Backward/forward JMP within this routine (including to entry) → goto, not call
                if (target == routineStart || indexByAddr.ContainsKey(target))
                {
                    statements.Add($"goto {LabelName(target)};");
                    return;
                }
                int tBank = _disasm.Banks.SiteBank(target, bank);
                if (routines.TryGetValue((tBank, target), out var jmpName)
                    && !(tBank == routineBank && target == routineStart))
                    statements.Add($"{jmpName}(); return;");
                else if (_disasm.Banks.IsSwitchableCpuAddress(target)
                    && TryFindRoutine(target, routines, out jmpName))
                    statements.Add($"{jmpName}(); return;");
                else
                    statements.Add($"// JMP ${target:X4} (unmapped)");
                return;
            }
            case 0x60:
                statements.Add("return;");
                return;
            case 0x40:
                statements.Add("return; // RTI");
                return;
            case 0x18: statements.Add("carry = false;"); return;
            case 0x38: statements.Add("carry = true;"); return;
            case 0x58: statements.Add("cli();"); return;
            case 0x78: statements.Add("sei();"); return;
            case 0xEA: return;
            case 0xAA: statements.Add("x = a;"); return;
            case 0x8A:
                statements.Add("a = x;");
                lastImmA = lastImmX;
                return;
            case 0xA8: statements.Add("y = a;"); return;
            case 0x98:
                statements.Add("a = y;");
                lastImmA = lastImmY;
                return;
            case 0xE8: statements.Add("x++;"); return;
            case 0xC8: statements.Add("y++;"); return;
            case 0xCA: statements.Add("x--;"); return;
            case 0x88: statements.Add("y--;"); return;
            case 0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0 when o1.HasValue:
            {
                ushort target = (ushort)(pc + 2 + (sbyte)o1.Value);
                string cond = BranchCondition(op);
                // Branch back to this routine's entry (or any in-body label) → goto for loop recovery
                if (target == routineStart || indexByAddr.ContainsKey(target))
                {
                    statements.Add($"if ({cond}) goto {LabelName(target)};");
                    return;
                }
                int tBank = _disasm.Banks.SiteBank(target, bank);
                if (routines.TryGetValue((tBank, target), out var brName)
                    && !(tBank == routineBank && target == routineStart))
                    statements.Add($"if ({cond}) {{ {brName}(); return; }}");
                else
                    statements.Add($"if ({cond}) goto {LabelName(target)};");
                return;
            }
            default:
                statements.Add($"// {FormatAsm(insn)}");
                lastImmA = lastImmX = lastImmY = null;
                return;
        }
    }

    static string Store(string reg, byte? imm, ushort absAddr) =>
        Store(reg, imm, FormatAddr(absAddr, writing: true));

    static string Store(string reg, byte? imm, string addr) =>
        imm.HasValue ? $"poke({addr}, {FormatByte(imm.Value)});" : $"poke({addr}, {reg});";

    // Numeric literals are int; peek/poke take ushort.
    static string FormatZp(byte zp) => $"(ushort)0x{zp:X2}";

    static string IndirectZp(byte zp) =>
        $"(peek({FormatZp(zp)}) | peek({FormatZp((byte)(zp + 1))}) << 8)";

    static string IndexAddr(string baseAddr, string index) => $"(ushort)({baseAddr} + {index})";

    /// <summary>
    /// Drop redundant <c>a = N;</c> when the next stmt is <c>poke(addr, N);</c> (imm already folded).
    /// </summary>
    static void FoldImmPokes(List<string> statements)
    {
        for (int i = 0; i < statements.Count - 1; i++)
        {
            string cur = statements[i];
            string next = statements[i + 1];
            if (cur.StartsWith("a = ", StringComparison.Ordinal) && next.StartsWith("poke(", StringComparison.Ordinal))
            {
                // a = 5; poke(X, 5); → keep poke only when poke uses a literal
                int eq = cur.IndexOf('=');
                string imm = cur[(eq + 1)..].Trim().TrimEnd(';').Trim();
                if (next.Contains($", {imm})", StringComparison.Ordinal))
                {
                    statements.RemoveAt(i);
                    i--;
                }
            }
            else if ((cur.StartsWith("x = ", StringComparison.Ordinal) || cur.StartsWith("y = ", StringComparison.Ordinal))
                && next.StartsWith("poke(", StringComparison.Ordinal))
            {
                int eq = cur.IndexOf('=');
                string imm = cur[(eq + 1)..].Trim().TrimEnd(';').Trim();
                if (next.Contains($", {imm})", StringComparison.Ordinal))
                {
                    statements.RemoveAt(i);
                    i--;
                }
            }
        }
    }

    /// <summary>
    /// Rewrite simple backward goto loops into <c>while (true)</c> / <c>do…while</c>.
    /// </summary>
    static List<string> StructureLoops(List<string> statements)
    {
        var result = new List<string>(statements);
        bool changed;
        do
        {
            changed = false;
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < result.Count; i++)
            {
                if (TryGetLabelName(result[i], out string foundLabel))
                    labels[foundLabel] = i;
            }

            // Prefer innermost loops (shortest nearest-back-edge span) first.
            // A label may have an outer back-edge too (e.g. PPU wait inside a counted
            // loop); we only require the nearest back-edge's body to be free of other
            // gotos to the same label.
            var candidates = new List<(int LabelIdx, int BackIdx, string Label, string? Cond, bool KeepLabel)>();
            foreach (var (label, li) in labels)
            {
                int nearest = -1;
                string? nearestCond = null;
                for (int j = li + 1; j < result.Count; j++)
                {
                    if (!TryParseGoto(result[j], out string t, out string? c) || t != label)
                        continue;
                    nearest = j;
                    nearestCond = c;
                    break;
                }
                if (nearest < 0)
                    continue;
                // Body must not contain another goto to the same label
                bool innerEdge = false;
                for (int j = li + 1; j < nearest; j++)
                {
                    if (TryParseGoto(result[j], out string t, out _) && t == label)
                    {
                        innerEdge = true;
                        break;
                    }
                }
                if (innerEdge || !IsSimpleLoopBody(result, li, nearest, labels))
                    continue;

                // Keep the header label when anything outside the span still jumps to it
                // (forward entries or later edges). Removing it breaks CS0159.
                bool externalToHeader = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if (j >= li && j <= nearest)
                        continue;
                    if (TryParseGoto(result[j], out string t, out _) && t == label)
                    {
                        externalToHeader = true;
                        break;
                    }
                }
                candidates.Add((li, nearest, label, nearestCond, externalToHeader));
            }

            if (candidates.Count == 0)
                break;

            candidates.Sort((a, b) => (a.BackIdx - a.LabelIdx).CompareTo(b.BackIdx - b.LabelIdx));
            var (labelIdx, backIdx, labelName, condition, keepLabel) = candidates[0];
            var body = result.GetRange(labelIdx + 1, backIdx - labelIdx - 1);
            var replacement = new List<string>();
            if (keepLabel)
                replacement.Add($"{labelName}: ;");

            if (condition is null)
            {
                if (body.Count == 0)
                {
                    replacement.Add("while (true) ;");
                }
                else
                {
                    replacement.Add("while (true)");
                    replacement.Add("{");
                    foreach (string line in body)
                        replacement.Add("    " + line);
                    replacement.Add("}");
                }
            }
            else if (body.Count == 0)
            {
                replacement.Add($"while ({condition}) ;");
            }
            else if (body.Count == 1 && !body[0].StartsWith("if ", StringComparison.Ordinal)
                && !body[0].StartsWith("while ", StringComparison.Ordinal)
                && !body[0].StartsWith("do ", StringComparison.Ordinal)
                && body[0] is not "{" and not "}")
            {
                replacement.Add($"do {{ {body[0]} }} while ({condition});");
            }
            else
            {
                replacement.Add("do");
                replacement.Add("{");
                foreach (string line in body)
                    replacement.Add("    " + line);
                replacement.Add("}");
                replacement.Add($"while ({condition});");
            }

            result.RemoveRange(labelIdx, backIdx - labelIdx + 1);
            result.InsertRange(labelIdx, replacement);
            changed = true;
        } while (changed);

        return result;
    }

    static bool IsSimpleLoopBody(
        List<string> stmts, int labelIdx, int backIdx,
        Dictionary<string, int> labels)
    {
        // Only rewrite spin / counted loops: no nested labels or mid-body gotos.
        // Complex CFGs become invalid C# once labels move into nested do/while scopes.
        for (int j = labelIdx + 1; j < backIdx; j++)
        {
            if (TryGetLabelName(stmts[j], out _))
                return false;
            if (TryParseGoto(stmts[j], out _, out _))
                return false;
        }

        // Reject if any label inside (labelIdx, backIdx) is targeted from outside the span
        foreach (var (name, idx) in labels)
        {
            if (idx <= labelIdx || idx >= backIdx)
                continue;
            for (int j = 0; j < stmts.Count; j++)
            {
                if (j >= labelIdx && j <= backIdx)
                    continue;
                if (TryParseGoto(stmts[j], out string t, out _) && t == name)
                    return false;
            }
        }
        return true;
    }

    static bool TryParseGoto(string line, out string target, out string? cond)
    {
        target = null!;
        cond = null;
        // goto L_XXXX;
        if (line.StartsWith("goto ", StringComparison.Ordinal) && line.EndsWith(';'))
        {
            target = line["goto ".Length..].TrimEnd(';').Trim();
            return target.StartsWith("L_", StringComparison.Ordinal);
        }
        // if (cond) goto L_XXXX;
        const string mid = ") goto ";
        if (line.StartsWith("if (", StringComparison.Ordinal) && line.Contains(mid, StringComparison.Ordinal) && line.EndsWith(';'))
        {
            int midAt = line.IndexOf(mid, StringComparison.Ordinal);
            cond = line["if (".Length..midAt];
            target = line[(midAt + mid.Length)..].TrimEnd(';').Trim();
            return target.StartsWith("L_", StringComparison.Ordinal);
        }
        return false;
    }

    int ApplyBankSwitch(int current, ushort storeAddr, byte imm)
    {
        if (_disasm.Banks.BusConflictBankSwitch && storeAddr >= 0x8000)
            return _disasm.Banks.ExtractPrgBankFromBusWrite(imm);
        if (_disasm.Banks.IsMmc2StylePrgRegisterWrite(storeAddr))
            return _disasm.Banks.MaskBank(imm);
        return current;
    }

    string ResolveRoutineName(int bank, ushort target, IReadOnlyDictionary<(int Bank, ushort Address), string> routines)
    {
        int tBank = _disasm.Banks.SiteBank(target, bank);
        if (routines.TryGetValue((tBank, target), out var name))
            return name;
        if (_disasm.Banks.IsSwitchableCpuAddress(target) && TryFindRoutine(target, routines, out name))
            return name;
        // Name a stub so callers compile; body filled later if unrecovered.
        _stubRoutines.Add((tBank, target));
        return RoutineName(tBank, target);
    }

    static bool TryFindRoutine(ushort target, IReadOnlyDictionary<(int Bank, ushort Address), string> routines, out string name)
    {
        foreach (var kv in routines)
        {
            if (kv.Key.Address == target)
            {
                name = kv.Value;
                return true;
            }
        }
        name = null!;
        return false;
    }

    bool TryFindInstruction(ushort target, out DecodedInstruction insn, out int bank)
    {
        for (int b = 0; b < _disasm.Banks.PrgBanks; b++)
        {
            if (_disasm.TryGetInstruction(b, target, out insn))
            {
                bank = b;
                return true;
            }
        }
        insn = default;
        bank = 0;
        return false;
    }

    static ushort Abs(byte? lo, byte? hi) => (ushort)(lo!.Value | (hi!.Value << 8));

    static bool IsTerminator(byte opcode) =>
        opcode is 0x00 or 0x40 or 0x60 or 0x4C or 0x6C;

    static bool IsBranch(byte opcode) =>
        opcode is 0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0;

    static string BranchCondition(byte opcode) => opcode switch
    {
        0xF0 => "zero",
        0xD0 => "!zero",
        0xB0 => "carry",
        0x90 => "!carry",
        0x30 => "negative",
        0x10 => "!negative",
        0x70 => "/* overflow */ false",
        0x50 => "/* !overflow */ true",
        _ => "true",
    };

    static string FormatByte(byte b) => b <= 9 ? b.ToString() : $"0x{b:X2}";

    // $4017 is JOYPAD2 on read and APU_FRAME on write.
    static string FormatAddr(ushort addr, bool writing = false) => addr switch
    {
        NESLib.PPU_CTRL => "PPU_CTRL",
        NESLib.PPU_MASK => "PPU_MASK",
        NESLib.PPU_STATUS => "PPU_STATUS",
        NESLib.PPU_SCROLL => "PPU_SCROLL",
        NESLib.PPU_ADDR => "PPU_ADDR",
        NESLib.PPU_DATA => "PPU_DATA",
        NESLib.PPU_OAM_ADDR => "PPU_OAM_ADDR",
        NESLib.PPU_OAM_DATA => "PPU_OAM_DATA",
        NESLib.PPU_OAM_DMA => "PPU_OAM_DMA",
        NESLib.APU_STATUS => "APU_STATUS",
        NESLib.APU_PULSE1_CTRL => "APU_PULSE1_CTRL",
        NESLib.APU_PULSE2_CTRL => "APU_PULSE2_CTRL",
        NESLib.APU_NOISE_CTRL => "APU_NOISE_CTRL",
        NESLib.APU_NOISE_PERIOD => "APU_NOISE_PERIOD",
        NESLib.APU_NOISE_LENGTH => "APU_NOISE_LENGTH",
        NESLib.APU_TRIANGLE_TIMER_LO => "APU_TRIANGLE_TIMER_LO",
        NESLib.APU_TRIANGLE_TIMER_HI => "APU_TRIANGLE_TIMER_HI",
        NESLib.APU_DMC_FREQ => "APU_DMC_FREQ",
        NESLib.APU_DMC_RAW => "APU_DMC_RAW",
        NESLib.APU_DMC_START => "APU_DMC_START",
        NESLib.APU_DMC_LEN => "APU_DMC_LEN",
        NESLib.JOYPAD1 => "JOYPAD1",
        NESLib.APU_FRAME when writing => "APU_FRAME",
        NESLib.JOYPAD2 => "JOYPAD2",
        _ => $"(ushort)0x{addr:X4}",
    };

    static string FormatAsm(DecodedInstruction insn)
    {
        string m = insn.Mnemonic;
        int size = insn.Size;
        if (size == 1) return m;
        if (size == 2 && insn.Op1.HasValue) return $"{m} ${insn.Op1.Value:X2}";
        if (size == 3 && insn.Op1.HasValue && insn.Op2.HasValue)
            return $"{m} ${insn.AbsoluteTarget:X4}";
        return m;
    }
}
