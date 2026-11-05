using System.Text;
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

        foreach (var site in order)
        {
            string name = routines[site];
            var body = DecompileRoutine(site.Bank, site.Address, routines);
            // Keep empty stubs too — callers may still reference them.

            sb.AppendLine($"// bank={site.Bank} ${site.Address:X4}");
            sb.AppendLine($"void {name}()");
            sb.AppendLine("{");
            foreach (string line in body)
                sb.AppendLine("    " + line);
            if (body.Count == 0)
                sb.AppendLine("    // empty / not reachable in disassembly window");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Stubs for JSR/JMP targets we named but never recovered a body for.
        foreach (var site in _stubRoutines.OrderBy(t => t.Bank).ThenBy(t => t.Address))
        {
            if (routines.ContainsKey(site))
                continue;
            string name = RoutineName(site.Bank, site.Address);
            sb.AppendLine($"// bank={site.Bank} ${site.Address:X4} (unrecovered stub — keep for callers)");
            sb.AppendLine($"void {name}() {{ }}");
            sb.AppendLine();
        }

        return sb.ToString();
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
        // C# forbids a label as the last statement in a block — ensure every label has a stmt.
        for (int i = 0; i < structured.Count; i++)
        {
            if (TryGetLabelName(structured[i], out string name) && structured[i].TrimEnd() == name + ":")
                structured[i] = name + ": ;";
        }
        return structured;
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
                statements.Add(Store("a", lastImmA, FormatAddr(addr)));
                if (lastImmA.HasValue)
                    bank = ApplyBankSwitch(bank, addr, lastImmA.Value);
                return;
            }
            case 0x8E when o1.HasValue && o2.HasValue: // STX abs
            {
                ushort addr = Abs(o1, o2);
                statements.Add(Store("x", lastImmX, FormatAddr(addr)));
                if (lastImmX.HasValue)
                    bank = ApplyBankSwitch(bank, addr, lastImmX.Value);
                return;
            }
            case 0x8C when o1.HasValue && o2.HasValue: // STY abs
            {
                ushort addr = Abs(o1, o2);
                statements.Add(Store("y", lastImmY, FormatAddr(addr)));
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
            case 0x4A: // LSR A
                statements.Add("carry = (a & 1) != 0; a >>= 1;");
                lastImmA = null;
                return;
            case 0x2A: // ROL A
                statements.Add("{ bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }");
                lastImmA = null;
                return;
            case 0x6A: // ROR A
                statements.Add("{ bool _c = carry; carry = (a & 1) != 0; a = (byte)((a >> 1) | (_c ? 0x80 : 0)); }");
                lastImmA = null;
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

    static string FormatAddr(ushort addr) => addr switch
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
        NESLib.JOYPAD1 => "JOYPAD1",
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
