using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using dotnes.Disasm;
using NES;

namespace dotnes;

/// <summary>
/// Best-effort C# recovery for arbitrary (non-dotnes) ROMs from bank-aware disassembly.
/// Emits poke/peek, register temps, real goto/labels, and calls as Program.cs game logic.
/// Keeps all discovered routines (no dead-code elimination). SoftCpu lowers via Program.cs; prg.bin fills gaps.
/// </summary>
sealed class GeneralRecoverer
{
    readonly NESRomReader _rom;
    readonly Disassembler _disasm;
    readonly ILogger _logger;
    readonly RecoveryStyle _style;
    readonly HashSet<(int Bank, ushort Address)> _stubRoutines = new();
    List<SoftCpuReadabilityPass.RomTable> _romTablesForEmit = [];

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

        void AddVectorRoutine(ushort addr, string name)
        {
            if (_disasm.TryGetAny(addr, out var insn) && insn.IsOfficial)
            {
                AddRoutine(insn.Bank, addr, name);
                return;
            }
            AddRoutine(_disasm.Banks.SiteBank(addr, 0), addr, name);
        }

        // Vectors may land in the low window ($8000) on NROM-256 — use the disassembler's
        // site bank, not FixedBank alone (that only covers the last 16KB / $C000+).
        AddVectorRoutine(_rom.ResetVector, "reset");
        if (_rom.NmiVector != _rom.ResetVector)
            AddVectorRoutine(_rom.NmiVector, "nmi");
        if (_rom.IrqVector != _rom.ResetVector && _rom.IrqVector != _rom.NmiVector)
            AddVectorRoutine(_rom.IrqVector, "irq");

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

        // Soft-CPU joypad recovery often finds controller RAM ($06FC) before the game's
        // readers (SMB $B104 A/B/dpad unpack) are reachable via vectors alone. Seed those.
        if (Optimize)
        {
            RemoveRedundantStrobesBeforePadPollCalls(emitted);
            var seedPads = CollectPadAddresses(emitted.Select(e => e.Body));
            var padLoadEntries = FindPrgAbsoluteLoadsOf(seedPads).ToList();
            if (padLoadEntries.Count > 0)
            {
                _disasm.ContinueFrom(padLoadEntries);
                int orderBefore = order.Count;
                foreach (var (cpu, bank) in padLoadEntries)
                    AddRoutine(bank, cpu, RoutineName(bank, cpu));
                foreach (var (bank, target) in CollectCallTargets().OrderBy(t => t.Bank).ThenBy(t => t.Address))
                    AddRoutine(bank, target, RoutineName(bank, target));
                foreach (var (bank, target) in CollectJmpTargets().OrderBy(t => t.Bank).ThenBy(t => t.Address))
                    AddRoutine(bank, target, RoutineName(bank, target));

                for (int i = orderBefore; i < order.Count; i++)
                {
                    var site = order[i];
                    emitted.Add((site.Bank, site.Address, routines[site],
                        DecompileRoutine(site.Bank, site.Address, routines)));
                }

                if (order.Count > orderBefore)
                {
                    _logger.WriteLine(
                        $"Pad-load seeding: +{order.Count - orderBefore} routines from {seedPads.Count} controller RAM addr(s).");
                }
            }
        }

        IReadOnlyDictionary<ushort, string> addrConsts = new Dictionary<ushort, string>();
        IReadOnlyDictionary<ushort, string> ramVars = new Dictionary<ushort, string>();
        List<BufferRecovery.RamBuffer> ramBuffers = [];
        if (Optimize)
        {
            RemoveRedundantStrobesBeforePadPollCalls(emitted);
            var bodies = emitted.Select(e => e.Body).ToList();
            var padAddrs = CollectPadAddresses(bodies);
            bool padMsb = BodiesUseMsbPadPacking(bodies);
            var (maskShadows, ctrlShadows) = CollectPpuShadows(bodies);
            var (scrollAddrs, scrollX, scrollY) = CollectScrollShadows(bodies);
            var ptrLos = CollectIndirectPointerLos(bodies);
            var singleScope = CollectSingleRoutineScopes(emitted);
            // Type PAD/PADMSB/MASK/CTRL immediates before hot-promote while addresses are still (ushort)0x….
            foreach (var body in bodies)
                FormatTypedZpLiterals(body, padAddrs, padMsb, maskShadows, ctrlShadows);
            var hot = PromoteHotAddresses(
                bodies, padAddrs, maskShadows, ctrlShadows, scrollAddrs, scrollX, scrollY, ptrLos, singleScope);
            (ramVars, addrConsts) = SplitHotRamIntoVariables(bodies, hot);
        }

        var paletteTables = PaletteTableRecovery.FindVramBufferPalettes(_rom.PrgRom);
        var vramPtrTables = PaletteTableRecovery.FindVramPointerTables(
            _rom.PrgRom,
            paletteTables,
            emitted.Select(e => e.Body));
        var textBuffers = Optimize
            ? VramTextBufferRecovery.FindTextBuffers(_rom.PrgRom, vramPtrTables)
            : [];
        var gameText = Optimize
            ? GameTextTableRecovery.FindTextTables(_rom.PrgRom)
            : [];
        if (paletteTables.Count > 0 || vramPtrTables.Count > 0)
        {
            foreach (var (_, _, _, body) in emitted)
                PaletteTableRecovery.RewriteSoftCpuUsages(body, paletteTables, vramPtrTables);
        }
        if (Optimize)
        {
            var ramMut = ramVars as Dictionary<ushort, string> ?? new Dictionary<ushort, string>(ramVars);
            var constMut = addrConsts as Dictionary<ushort, string> ?? new Dictionary<ushort, string>(addrConsts);
            var bodies = emitted.Select(e => e.Body).ToList();
            if (paletteTables.Count > 0 || vramPtrTables.Count > 0 || textBuffers.Count > 0)
            {
                EnsureRamVar(ramMut, constMut, 0x00, "vram_ptr_lo");
                EnsureRamVar(ramMut, constMut, 0x01, "vram_ptr_hi");
                // Bodies may still say zp_00/zp_01 from earlier promote — rename in place.
                RenameRamVar(bodies, ramMut, 0x00, "vram_ptr_lo");
                RenameRamVar(bodies, ramMut, 0x01, "vram_ptr_hi");
            }

            // Coherent RAM buffers (OAM / vram_buf / buf_XXXX) before scalar peek→var rewrite.
            var hotNames = new Dictionary<ushort, string>(ramMut);
            foreach (var (a, n) in constMut)
                hotNames[a] = n;
            // Pad addrs before buffer rewrite (rewrite turns pad peeks into buf[i]).
            var padForRoles = CollectPadAddresses(bodies);
            ramBuffers = BufferRecovery.FindBuffers(bodies, hotNames, vramPtrTables);
            if (ramBuffers.Count > 0)
            {
                BufferRecovery.RewriteBodies(bodies, ramBuffers, constMut);
                SoftCpuReadabilityPass.RewriteOamFields(bodies, ramBuffers);
                ramBuffers = SoftCpuReadabilityPass.DemoteSize1Buffers(bodies, ramBuffers, ramMut, out var demoted);
                ramBuffers = SoftCpuReadabilityPass.ApplyBufferRoleNames(bodies, ramBuffers, padForRoles);
                var promotedSlots = SoftCpuReadabilityPass.PromoteHotBufferSlots(bodies, ramBuffers, ramMut);
                _ = demoted;
                _ = promotedSlots;
                BufferRecovery.AbsorbedAddresses(ramBuffers, out var covered);
                foreach (ushort addr in covered)
                {
                    // Drop address consts absorbed into buffers. Keep ramVars even when their
                    // address sits inside a buffer: bodies still use scoped names
                    // (f_*/zp_*/ptr_*) and stripping decls causes CS0103.
                    constMut.Remove(addr);
                }
            }

            _romTablesForEmit = SoftCpuReadabilityPass.FindRomTables(_rom.PrgRom, bodies, _rom.PrgBanks);
            SoftCpuReadabilityPass.RewriteRomTablePeeks(bodies, _romTablesForEmit);

            RewritePeekPokeToVariables(bodies, ramMut);
            foreach (var body in bodies)
            {
                SugarMemIncDec(body);
                // After poke→var/-- rewrite: mem--; if (zero) → if (mem == 0)
                SugarZeroAfterMemDec(body);
                StructureMemVarZeroSkipArms(body);
                // Named RAM countdowns only exist after var promote + name-- sugar.
                RewriteStructuredMemCountdownDos(body);
                StructureForLoops(body);
                SoftCpuReadabilityPass.ApplyBodyLocalPasses(body);
                // Readability can fold `a=…; poke(APU_*, a)` after the early LiftNesLib pass.
                RewriteApuPokesInBody(body);
                // Goto islands may drop labels after DecompileRoutine's CommentGotos pass.
                CommentGotosToMissingLabels(body);
                RepairBrokenByteCasts(body);
            }
            SoftCpuReadabilityPass.EnsureReferencedRamDecls(bodies, ramMut, constMut, ramBuffers);
            ramVars = ramMut;
            addrConsts = constMut;

            SoftCpuReadabilityPass.ApplyRoutineRenames(emitted, SoftCpuReadabilityPass.SuggestRoutineNames(emitted));
        }
        var access = TallyMemoryAccess(emitted.SelectMany(e => e.Body));

        var sb = new StringBuilder();
        if (Optimize)
        {
            sb.AppendLine("// SoftCpu recovery (optimized) — game RAM, buffers, control flow.");
            sb.AppendLine("// Goal: no 6502 register surface. a/x/y/flags only where not raised yet.");
            sb.AppendLine("// SoftCpu packer lowers this C# to 6502; prg.bin fills unrecovered gaps.");
            sb.AppendLine("// Pass --exact for less structuring / more literal gotos.");
        }
        else
        {
            sb.AppendLine("// SoftCpu recovery (--exact) — more literal gotos; less structuring.");
            sb.AppendLine("// Goal: no 6502 register surface. a/x/y/flags only where not raised yet.");
            sb.AppendLine("// SoftCpu packer lowers this C# to 6502; prg.bin fills unrecovered gaps.");
        }
        sb.AppendLine($"// Mapper={_rom.Mapper}, PRG banks={_rom.PrgBanks}, CHR banks={_rom.ChrBanks},");
        sb.AppendLine($"// Mirroring={(_rom.VerticalMirroring ? "Vertical" : "Horizontal")}, Battery={_rom.HasBattery}");
        sb.AppendLine($"// Vectors: NMI=${_rom.NmiVector:X4} RESET=${_rom.ResetVector:X4} IRQ=${_rom.IrqVector:X4}");
        sb.AppendLine($"// Recovered {_disasm.Instructions.Count} instruction sites across {routines.Count} routines.");
        if (paletteTables.Count > 0)
            sb.AppendLine($"// Recovered {paletteTables.Count} PPU palette VRAM-buffer table(s) from PRG.");
        if (vramPtrTables.Count > 0)
            sb.AppendLine($"// Recovered {vramPtrTables.Count} VRAM buffer pointer table(s) that select palettes.");
        if (textBuffers.Count > 0)
            sb.AppendLine($"// Recovered {textBuffers.Count} nametable text VRAM-buffer stream(s) (tile font).");
        if (gameText.Count > 0)
            sb.AppendLine($"// Recovered {gameText.Count} game text string(s) (discovered tile-font tables).");
        if (ramBuffers.Count > 0)
            sb.AppendLine($"// Recovered {ramBuffers.Count} coherent RAM buffer(s) (OAM / VRAM update / generic).");
        AppendMemoryAccessComment(sb, access);
        sb.AppendLine();
        // Soft-CPU recovery is readable analysis; keep every discovered routine (no DCE).
        sb.AppendLine("#nullable disable");
        sb.AppendLine("#pragma warning disable CS8321 // unused local function (keep all discovered routines)");
        sb.AppendLine("#pragma warning disable CS0162 // unreachable code from literal branches");
        sb.AppendLine("#pragma warning disable CS0164 // unreferenced labels from partial CFG");
        if (ramBuffers.Count > 0)
        {
            sb.AppendLine("// Coherent RAM buffers (SoftCpu maps name → base via // $…. ; name[x|y] → abs,X/Y).");
            foreach (var b in ramBuffers.OrderBy(b => b.Base))
                sb.AppendLine($"byte[] {b.Name} = new byte[{b.Size}]; // ${b.Base:X4}");
            foreach (string line in SoftCpuReadabilityPass.FormatOamFieldDecls(ramBuffers))
                sb.AppendLine(line);
            sb.AppendLine();
        }
        if (_romTablesForEmit.Count > 0)
        {
            foreach (string line in SoftCpuReadabilityPass.FormatRomTableDecls(_romTablesForEmit))
                sb.AppendLine(line);
            sb.AppendLine();
        }
        if (ramVars.Count > 0)
        {
            sb.AppendLine("// Hot RAM as variables (SoftCpu maps each name → address via // $…. ).");
            foreach (var (addr, name) in ramVars.OrderBy(kv => kv.Key))
            {
                string hex = addr <= 0xFF ? $"{addr:X2}" : $"{addr:X4}";
                // Top-level locals need an initializer (CS0165 on first read / ++).
                sb.AppendLine($"byte {name} = 0; // ${hex}");
            }
            sb.AppendLine();
        }
        if (addrConsts.Count > 0)
        {
            sb.AppendLine("// Address constants (indexed bases / ROM / mapper — keep peek/poke).");
            foreach (var (addr, name) in addrConsts.OrderBy(kv => kv.Key))
                sb.AppendLine($"const ushort {name} = 0x{addr:X4};");
            sb.AppendLine();
        }
        if (paletteTables.Count > 0)
        {
            sb.AppendLine("// PPU palette VRAM-buffer records recovered from PRG ($3F,$00,$len + colors).");
            foreach (string line in PaletteTableRecovery.FormatDeclarations(_rom.PrgRom, paletteTables))
                sb.AppendLine(line);
            sb.AppendLine();
        }
        if (textBuffers.Count > 0)
        {
            foreach (string line in VramTextBufferRecovery.FormatDeclarations(_rom.PrgRom, textBuffers))
                sb.AppendLine(line);
            sb.AppendLine();
        }
        if (gameText.Count > 0)
        {
            foreach (string line in GameTextTableRecovery.FormatDeclarations(gameText))
                sb.AppendLine(line);
            sb.AppendLine();
        }
        if (vramPtrTables.Count > 0)
        {
            IEnumerable<string> ptrLines = textBuffers.Count > 0
                ? VramTextBufferRecovery.FormatAnnotatedVramPointerDeclarations(vramPtrTables, textBuffers)
                : PaletteTableRecovery.FormatVramPointerDeclarations(vramPtrTables);
            foreach (string line in ptrLines)
                sb.AppendLine(line);
            sb.AppendLine();
        }
        // Residual 6502 state still referenced by unrecovered sequences / SoftCpu index forms.
        // Prefer raising these away (mem exprs, fors, ushort math). SoftCpu lowers leftovers.
        sb.AppendLine("// residual SoftCpu IR (raise away):");
        sb.AppendLine("byte a = 0, x = 0, y = 0;");
        sb.AppendLine("bool carry = false, zero = false, negative = false;");
        if (paletteTables.Count > 0 || textBuffers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("// Bind vram_ptr_* to a palette/text row once; (vram_ptr_lo),Y then indexes Data.");
            // PeekVramBufY helpers reference palette[]; emit empty palette when only text was found.
            if (paletteTables.Count == 0)
            {
                sb.AppendLine("(ushort Base, byte[] Data)[] palette = { };");
                sb.AppendLine();
            }
            foreach (string helper in PaletteTableRecovery.FormatRuntimeHelpers(includeTextBuffers: textBuffers.Count > 0))
                sb.AppendLine(helper);
        }
        sb.AppendLine();
        bool hasReset = emitted.Any(e => e.Name == "reset");
        if (hasReset)
            sb.AppendLine("reset();");
        else
            sb.AppendLine("// reset vector was not recovered as a routine");
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
                    // Label only resolvable in-body targets (including entry for loops).
                    // Never enqueue $7xxx / unmapped sites — that yields goto L_XXXX with no label (CS0159).
                    if (target == start)
                    {
                        labelTargets.Add(target);
                    }
                    else if (!routines.ContainsKey((tBank, target))
                        && _disasm.TryGetInstruction(tBank, target, out _))
                    {
                        labelTargets.Add(target);
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
        {
            // Flag-based diamonds first (need if (zero) form), then sugar remaining tests to a==0.
            SugarCompareBranches(structured);
            SugarMemCompareBranches(structured);
            SugarExitGotos(structured);
            SugarForcedGotos(structured);
            StructureIfElseDiamonds(structured);
            SugarZeroTestsOnA(structured);
            SugarZeroAfterMemDec(structured);
            StructureRegCompareSkipArms(structured);
            StructureMemCompareSkipArms(structured);
            StructureMemVarZeroSkipArms(structured);
            StructureBitTestSkipArms(structured);
            // NesLib lifts match `y--; } while (!zero)` — run before countdown --reg sugar.
            LiftNesLibPatterns(structured);
            StructureCountdownLoops(structured);
            RewriteStructuredMemCountdownDos(structured);
            StructureCompareBoundLoops(structured);
            StructureForLoops(structured);
            SugarAluAssigns(structured);
            SugarTransferInc(structured);
            SugarMultiShifts(structured);
            SugarDiscardedShiftBitTests(structured);
            SugarIndexedPeeks(structured);
        }
        // C# forbids a label as the last statement in a block — ensure every label has a stmt.
        for (int i = 0; i < structured.Count; i++)
        {
            if (TryGetLabelName(structured[i], out string name) && structured[i].TrimEnd() == name + ":")
                structured[i] = name + ": ;";
        }
        CommentGotosToMissingLabels(structured);
        return structured;
    }

    /// <summary>
    /// Defense in depth: rewrite any <c>goto L_XXXX</c> whose label was never emitted
    /// (CS0159). Happens when a relative branch targets unmapped / non-PRG addresses.
    /// </summary>
    static void CommentGotosToMissingLabels(List<string> statements)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in statements)
        {
            if (TryGetLabelName(raw.TrimStart(), out string name))
                labels.Add(name);
        }

        for (int i = 0; i < statements.Count; i++)
        {
            string indent = LeadingIndent(statements[i]);
            string line = statements[i].TrimStart();
            if (!TryParseGoto(line, out string target, out _) || labels.Contains(target))
                continue;
            statements[i] = indent + $"// {line.TrimEnd(';')} // label not in this routine";
        }
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

            // Sprite-0 hit spins (after ppu_status lift below would already be applied on a prior pass —
            // match both peek and ppu_status forms).
            if (TryMatchSprite0Spin(statements, i, out int sprite0Len, out bool waitUntilHit))
            {
                // Pair clear-wait + hit-wait → sprite0_wait() (stock split semantics).
                if (!waitUntilHit
                    && TryMatchSprite0Spin(statements, i + sprite0Len, out int hitLen, out bool nextIsHit)
                    && nextIsHit)
                {
                    statements.RemoveRange(i, sprite0Len + hitLen);
                    statements.Insert(i, indent + "sprite0_wait();");
                    continue;
                }

                statements.RemoveRange(i, sprite0Len);
                statements.Insert(i, indent + (waitUntilHit
                    ? "while (!sprite0_hit()) ;"
                    : "while (sprite0_hit()) ;"));
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

            // poke(PPU_OAM_ADDR, *); poke(PPU_OAM_DMA, page);  →  oam_dma(page);
            // Games often LDA #0 / STA $2003, but some leave A=0 from prior work (STA $2003 with a).
            if (TryParsePoke(line, out string oamAddr, out _)
                && oamAddr == "PPU_OAM_ADDR"
                && TryFindNextPoke(statements, i + 1, "PPU_OAM_DMA", out int dmaJ, out string page,
                    stopOnPpuTraffic: true))
            {
                statements.RemoveAt(dmaJ);
                statements[i] = indent + $"oam_dma({page});";
                continue;
            }

            // poke(PPU_ADDR, hi); poke(PPU_ADDR, lo);  →  vram_adr(...)
            // Only fuse across a gap when both halves are literals — register `a` is often
            // reloaded between STA $2006s (SMB VRAM buffer), and C# would read the new `a` twice.
            if (TryParsePoke(line, out string adrHiReg, out string hi)
                && adrHiReg == "PPU_ADDR")
            {
                if (i + 1 < statements.Count
                    && TryParsePoke(statements[i + 1].TrimStart(), out string adrLoReg, out string loAdjacent)
                    && adrLoReg == "PPU_ADDR")
                {
                    statements.RemoveAt(i + 1);
                    statements[i] = indent + FormatLiftedVramAdr(hi, loAdjacent);
                    continue;
                }

                if (TryParseByteLiteral(hi, out _)
                    && TryFindNextPoke(statements, i + 1, "PPU_ADDR", out int adrLoJ, out string loGap,
                        stopOnPpuTraffic: true)
                    && TryParseByteLiteral(loGap, out _))
                {
                    statements.RemoveAt(adrLoJ);
                    statements[i] = indent + FormatLiftedVramAdr(hi, loGap);
                    continue;
                }
            }

            // Nested nametable wipe: x=4; y=$C0; do { do { vram_put(c); y--; } while(!zero); x--; } while(!zero);
            // → vram_fill(c, 960)  ($C0 + 3×$100).
            if (TryMatchNestedVramFill(statements, i, out int nestLen, out string nestByte, out string nestCount))
            {
                statements.RemoveRange(i, nestLen);
                statements.Insert(i, indent + $"vram_fill({nestByte}, {nestCount});");
                continue;
            }

            // y|x = N; do { vram_put(c); y--|x--; } while (!zero);  →  vram_fill(c, N);
            // When pokes sit between count= and do, only rewrite the loop (keep the pokes).
            if (TryMatchVramFill(statements, i, out int countIdx, out int loopStart, out int loopLen,
                    out string fillByte, out string fillCount))
            {
                string fillIndent = LeadingIndent(statements[loopStart]);
                statements.RemoveRange(loopStart, loopLen);
                statements.Insert(loopStart, fillIndent + $"vram_fill({fillByte}, {fillCount});");
                if (countIdx >= 0 && countIdx < loopStart)
                    statements.RemoveAt(countIdx);
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

            // poke(APU_*, v) anywhere on the line (incl. if (cond) { poke(...); }) → neslib writers.
            if (TryRewriteApuPokesInLine(statements[i], out string apuRewritten))
            {
                statements[i] = apuRewritten;
                continue;
            }

            // a/x/y = peek(PPU_STATUS);  →  *_ = ppu_status();
            if (TryParseAssignPeek(line, out string statusDest, out string statusAddr)
                && statusAddr == "PPU_STATUS"
                && statusDest is "a" or "x" or "y")
            {
                statements[i] = indent + $"{statusDest} = ppu_status();";
                continue;
            }

            // a/x/y = peek(APU_STATUS);  →  *_ = apu_status();
            if (TryParseAssignPeek(line, out string apuStatusDest, out string apuStatusAddr)
                && apuStatusAddr == "APU_STATUS"
                && apuStatusDest is "a" or "x" or "y")
            {
                statements[i] = indent + $"{apuStatusDest} = apu_status();";
                continue;
            }

            // peek(JOYPAD1/2) → joypad*_read();  poke(JOYPAD1) → joypad1_write;
            // poke($4017) is APU frame counter (same addr as JOYPAD2 on read).
            if (TryLiftJoypadOrApuLine(line, out string liftedJoy))
                statements[i] = indent + liftedJoy;
        }

        // y = 8; do { … peek(JOYPAD*) … ROL/ROR … } while (!zero);  →  pad_poll / pad_poll_msb
        LiftPadPollShiftLoops(statements);

        // After poke→vram_put: fuse literal tile/text runs into vram_write.
        FuseLiteralVramPutRuns(statements);
    }

    /// <summary>
    /// <c>vram_put(0x48); vram_put(0x49); …</c> → <c>vram_write("HI…")</c> / byte array.
    /// Skips <c>NESColor</c> palette writes.
    /// </summary>
    static void FuseLiteralVramPutRuns(List<string> statements)
    {
        for (int i = 0; i < statements.Count;)
        {
            string indent = LeadingIndent(statements[i]);
            if (!TryMatchLiteralVramPutRun(statements, i, out int runLen, out List<byte> bytes))
            {
                i++;
                continue;
            }
            statements.RemoveRange(i, runLen);
            statements.Insert(i, indent + NesLibCallFormatter.FormatVramWriteFromBytes(bytes));
            i++;
        }
    }

    static bool TryMatchLiteralVramPutRun(
        List<string> statements, int i, out int length, out List<byte> bytes)
    {
        length = 0;
        bytes = new List<byte>();
        while (i + length < statements.Count
            && TryParseLiteralVramPut(statements[i + length].TrimStart(), out byte b))
        {
            bytes.Add(b);
            length++;
        }
        // Need at least 2 tiles/chars to beat a pair of vram_put lines.
        return length >= 2;
    }

    static bool TryParseLiteralVramPut(string line, out byte value)
    {
        value = 0;
        if (!line.StartsWith("vram_put(", StringComparison.Ordinal) || !line.EndsWith(");", StringComparison.Ordinal))
            return false;
        string arg = line["vram_put(".Length..^2].Trim();
        // Palette path uses NESColor.* — leave those as individual puts.
        if (arg.StartsWith("NESColor.", StringComparison.Ordinal))
            return false;
        return TryParseByteLiteral(arg, out value);
    }

    /// <summary>
    /// <c>do { a = ppu_status()/peek(PPU_STATUS); a &amp;= 0x40; } while (zero|!zero);</c>
    /// </summary>
    static bool TryMatchSprite0Spin(List<string> statements, int i, out int length, out bool waitUntilHit)
    {
        length = 0;
        waitUntilHit = false;
        if (i + 4 >= statements.Count || statements[i].TrimStart() != "do")
            return false;
        if (statements[i + 1].TrimStart() != "{")
            return false;

        string read = statements[i + 2].TrimStart();
        string mask = statements[i + 3].TrimStart();
        string close = statements[i + 4].TrimStart();
        if (close != "}")
            return false;
        if (i + 5 >= statements.Count)
            return false;
        string whileLine = statements[i + 5].TrimStart();

        bool statusRead = IsPeekPpuStatus(read) || read == "a = ppu_status();";
        if (!statusRead || !IsSprite0MaskLine(mask))
            return false;

        if (whileLine is "while (zero);" or "while(zero);")
        {
            waitUntilHit = true; // spin while flag clear
            length = 6;
            return true;
        }

        if (whileLine is "while (!zero);" or "while(!zero);")
        {
            waitUntilHit = false; // spin while flag set
            length = 6;
            return true;
        }

        return false;
    }

    static bool IsSprite0MaskLine(string line) =>
        line is "a = (byte)(a & 0x40);" or "a = (byte)(a & 64);" or "a &= 0x40;" or "a &= 64;"
        || line.Equals("a = (byte)(a & 0x40);", StringComparison.OrdinalIgnoreCase)
        || line.Equals("a &= 0x40;", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// SMB nametable clear: <c>x=4; y=$C0; [a=c;] do { do { vram_put(c); y--; } while(!zero); x--; } while(!zero);</c>
    /// → <c>vram_fill(c, 960)</c>.
    /// </summary>
    static bool TryMatchNestedVramFill(
        List<string> statements, int i, out int length, out string fillByte, out string fillCount)
    {
        length = 0;
        fillByte = fillCount = null!;
        if (i + 12 >= statements.Count)
            return false;
        if (!TryParseAssignLiteral(statements[i].TrimStart(), "x", out string xCount))
            return false;
        if (!TryParseAssignLiteral(statements[i + 1].TrimStart(), "y", out string yCount))
            return false;

        int bodyAt = i + 2;
        // optional a = c;
        if (TryParseAssignLiteral(statements[bodyAt].TrimStart(), "a", out fillByte))
            bodyAt++;
        else
            fillByte = null!;

        // do { do { vram_put(...); y--; } while (!zero); x--; } while (!zero);
        if (statements[bodyAt].TrimStart() != "do" || statements[bodyAt + 1].TrimStart() != "{")
            return false;
        if (statements[bodyAt + 2].TrimStart() != "do" || statements[bodyAt + 3].TrimStart() != "{")
            return false;

        string put = statements[bodyAt + 4].TrimStart();
        if (!TryExtractFillByte(put, out string putByte))
            return false;
        if (putByte is "a" or "x" or "y")
        {
            if (fillByte is null)
                return false;
        }
        else
            fillByte = putByte;

        if (statements[bodyAt + 5].TrimStart() is not ("y--;" or "y = (byte)(y - 1);" or "dey();"))
            return false;
        if (statements[bodyAt + 6].TrimStart() != "}")
            return false;
        if (statements[bodyAt + 7].TrimStart() is not ("while (!zero);" or "while(!zero);"))
            return false;
        if (statements[bodyAt + 8].TrimStart() is not ("x--;" or "x = (byte)(x - 1);" or "dex();"))
            return false;
        if (statements[bodyAt + 9].TrimStart() != "}")
            return false;
        if (statements[bodyAt + 10].TrimStart() is not ("while (!zero);" or "while(!zero);"))
            return false;

        if (!TryParseByteLiteral(xCount, out byte xN) || !TryParseByteLiteral(yCount, out byte yN))
            return false;
        // First pass: yN puts; remaining (xN-1) passes: 256 puts each (Y wraps from 0).
        int total = yN + (xN - 1) * 256;
        if (total <= 0 || total > 4096)
            return false;
        fillCount = total.ToString(CultureInfo.InvariantCulture);
        length = bodyAt + 11 - i;
        return true;
    }

    /// <summary>
    /// <c>y|x = N; [optional peeks/pokes]; do { [a = c;] vram_put(c|a); y--|x--; } while (!zero);</c>
    /// </summary>
    static bool TryMatchVramFill(
        List<string> statements, int i,
        out int countIdx, out int loopStart, out int loopLen,
        out string fillByte, out string fillCount)
    {
        countIdx = loopStart = loopLen = -1;
        fillByte = fillCount = null!;
        string counter = "";

        // Match starting at y=/x=… or at do (look back for counter assign).
        int doAt;
        string head = statements[i].TrimStart();
        if (TryParseAssignLiteral(head, "y", out fillCount))
        {
            counter = "y";
            countIdx = i;
            doAt = FindDoAfter(statements, i, fillCount);
            if (doAt < 0)
                return false;
        }
        else if (TryParseAssignLiteral(head, "x", out fillCount))
        {
            counter = "x";
            countIdx = i;
            doAt = FindDoAfter(statements, i, fillCount);
            if (doAt < 0)
                return false;
        }
        else if (head == "do" || head.StartsWith("do {", StringComparison.Ordinal))
        {
            doAt = i;
            countIdx = -1;
            for (int j = i - 1; j >= 0 && j >= i - 6; j--)
            {
                string t = statements[j].TrimStart();
                if (TryParseAssignLiteral(t, "y", out fillCount))
                {
                    counter = "y";
                    countIdx = j;
                    break;
                }
                if (TryParseAssignLiteral(t, "x", out fillCount))
                {
                    counter = "x";
                    countIdx = j;
                    break;
                }
                if (t.StartsWith("y =", StringComparison.Ordinal) || t.StartsWith("x =", StringComparison.Ordinal)
                    || t is "if" or "while" or "{" or "}" or "do")
                    return false;
            }
            if (countIdx < 0)
                return false;
        }
        else
            return false;

        loopStart = doAt;
        string decY = counter == "y"
            ? "y--;"
            : "x--;";
        string decYAlt = counter == "y"
            ? "y = (byte)(y - 1);"
            : "x = (byte)(x - 1);";
        string decOp = counter == "y" ? "dey();" : "dex();";

        // One-liner: do { vram_put(c); y--; } while (!zero);
        string next = statements[doAt].TrimStart();
        if (next.StartsWith("do {", StringComparison.Ordinal)
            && (next.Contains(decY, StringComparison.Ordinal) || next.Contains(decOp, StringComparison.Ordinal))
            && (next.Contains("while (!zero)", StringComparison.Ordinal)
                || next.Contains("while(!zero)", StringComparison.Ordinal))
            && TryExtractFillByte(next, out fillByte)
            && fillByte is not "a" and not "x" and not "y")
        {
            loopLen = 1;
            return true;
        }

        // Multi-line: do { vram_put(c); y--; } while (!zero);
        if (doAt + 5 < statements.Count
            && statements[doAt].TrimStart() == "do"
            && statements[doAt + 1].TrimStart() == "{")
        {
            string put = statements[doAt + 2].TrimStart();
            string dec = statements[doAt + 3].TrimStart();
            if (TryExtractFillByte(put, out fillByte)
                && fillByte is not "a" and not "x" and not "y"
                && dec is var d && (d == decY || d == decYAlt || d == decOp)
                && statements[doAt + 4].TrimStart() == "}"
                && statements[doAt + 5].TrimStart() is "while (!zero);" or "while(!zero);")
            {
                loopLen = 6;
                return true;
            }

            // do { a = c; vram_put(a); y--; } while (!zero);
            if (doAt + 6 < statements.Count
                && TryParseAssignLiteral(put, "a", out fillByte)
                && IsVramPutOfA(dec)
                && statements[doAt + 4].TrimStart() is var d2
                && (d2 == decY || d2 == decYAlt || d2 == decOp)
                && statements[doAt + 5].TrimStart() == "}"
                && statements[doAt + 6].TrimStart() is "while (!zero);" or "while(!zero);")
            {
                loopLen = 7;
                return true;
            }
        }

        return false;
    }

    static int FindDoAfter(List<string> statements, int i, string _)
    {
        for (int j = i + 1; j < statements.Count && j <= i + 6; j++)
        {
            string t = statements[j].TrimStart();
            if (t == "do" || t.StartsWith("do {", StringComparison.Ordinal))
                return j;
            if (t.StartsWith("y =", StringComparison.Ordinal) || t.StartsWith("x =", StringComparison.Ordinal))
                return -1;
            if (t is "if" or "while" or "{" or "}")
                return -1;
        }
        return -1;
    }

    static bool IsVramPutOfA(string line) =>
        line is "vram_put(a);"
        || (TryParsePoke(line, out string addr, out string val) && addr == "PPU_DATA" && val == "a");

    static bool TryExtractFillByte(string line, out string fillByte)
    {
        fillByte = null!;
        if (line.StartsWith("vram_put(", StringComparison.Ordinal) && line.EndsWith(");", StringComparison.Ordinal))
        {
            fillByte = line["vram_put(".Length..^2].Trim();
            return fillByte.Length > 0;
        }
        if (TryParsePoke(line, out string addr, out string val) && addr == "PPU_DATA")
        {
            fillByte = val;
            return true;
        }
        // Inside "do { vram_put(c); y--; } while…"
        int put = line.IndexOf("vram_put(", StringComparison.Ordinal);
        if (put >= 0)
        {
            int open = put + "vram_put(".Length;
            int close = line.IndexOf(')', open);
            if (close > open)
            {
                fillByte = line[open..close].Trim();
                return fillByte.Length > 0;
            }
        }
        int poke = line.IndexOf("poke(PPU_DATA,", StringComparison.Ordinal);
        if (poke >= 0)
        {
            int open = poke + "poke(PPU_DATA,".Length;
            int close = line.IndexOf(')', open);
            if (close > open)
            {
                fillByte = line[open..close].Trim();
                return fillByte.Length > 0;
            }
        }
        return false;
    }

    static bool TryParseAssignLiteral(string line, string reg, out string literal)
    {
        literal = null!;
        string prefix = reg + " = ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(';'))
            return false;
        literal = line[prefix.Length..^1].Trim();
        return TryParseByteLiteral(literal, out _);
    }

    /// <summary>
    /// Strobe + 8× serial shift into A → <c>pad_poll</c> (ROR/LSB) or <c>pad_poll_msb</c> (ROL/MSB).
    /// When the ROM rolled into RAM (e.g. SMB <c>ROL $06FC</c>), also <c>poke</c> the poll result there.
    /// </summary>
    static void LiftPadPollShiftLoops(List<string> statements)
    {
        for (int i = 0; i < statements.Count; i++)
        {
            string indent = LeadingIndent(statements[i]);
            if (!IsAssignYEight(statements[i].TrimStart()))
                continue;
            if (!TryMatchJoypadShiftDoWhile(statements, i + 1, out int whileAt, out string padArg, out bool msbFirst, out ushort? assembleAddr)
                && !TryMatchJoypadShiftGotoLoop(statements, i, out whileAt, out padArg, out msbFirst, out assembleAddr))
                continue;

            string poll = msbFirst ? "pad_poll_msb" : "pad_poll";
            int removeCount = whileAt - i + 1;
            statements.RemoveRange(i, removeCount);
            var inserted = new List<string> { indent + $"a = (byte){poll}({padArg});" };
            if (assembleAddr is ushort dest)
                inserted.Add(indent + $"poke({FormatAddr(dest)}, a);");
            statements.InsertRange(i, inserted);
            RemovePrecedingJoypadStrobe(statements, i);
        }
    }

    static bool IsAssignYEight(string line) =>
        line is "y = 8;" or "y = 0x08;" or "y = 0x8;";

    /// <summary>True when any joypad 8-bit assemble loop uses ROL (MSB-first / SMB packing).</summary>
    static bool BodiesUseMsbPadPacking(IEnumerable<List<string>> bodies)
    {
        foreach (var body in bodies)
        {
            // After a successful lift the y=8 loop is gone — check the call first.
            if (body.Any(l => l.Contains("pad_poll_msb(", StringComparison.Ordinal)))
                return true;

            for (int i = 0; i < body.Count; i++)
            {
                if (!IsAssignYEight(body[i].TrimStart()))
                    continue;
                if (TryMatchJoypadShiftDoWhile(body, i + 1, out _, out _, out bool msb, out _) && msb)
                    return true;
                if (TryMatchJoypadShiftGotoLoop(body, i, out _, out _, out msb, out _) && msb)
                    return true;
            }

            // ROL into A or RAM near a JOYPAD peek (even if loop shape was missed).
            bool sawJoy = false;
            foreach (string raw in body)
            {
                string t = raw.TrimStart();
                if (TryGetPadPollArgFromJoyRead(t, out _) || IsJoypadDataRead(t))
                    sawJoy = true;
                if (sawJoy && (IsRolAssembleIntoA(t) || TryGetRolMemoryAssembleAddr(t, out _)))
                    return true;
            }
        }
        return false;
    }

    static bool IsRolAssembleIntoA(string line) =>
        line.Contains("(a << 1)", StringComparison.Ordinal)
        && (line.Contains("? 1 : 0", StringComparison.Ordinal) || line.Contains("?1:0", StringComparison.Ordinal));

    static bool IsRorAssembleIntoA(string line) =>
        line.Contains("(a >> 1)", StringComparison.Ordinal)
        && line.Contains("0x80", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Soft-CPU zp/abs ROL, or unrecovered <c>// ROL $xxxx</c> comment.
    /// </summary>
    static bool TryGetRolMemoryAssembleAddr(string line, out ushort addr)
    {
        addr = 0;
        if (TryParseAbsRmwComment(line, out addr)
            && line.Contains("ROL", StringComparison.OrdinalIgnoreCase))
            return addr >= 0x10 && addr < 0x2000;

        // { bool _c = carry; byte _m = peek(ADDR); … poke(ADDR, (byte)((_m << 1) | (_c ? 1 : 0))); }
        if (!line.Contains("(_m << 1)", StringComparison.Ordinal)
            || !line.Contains("_c ? 1 : 0", StringComparison.Ordinal)
            || !line.Contains("poke(", StringComparison.Ordinal))
            return false;

        int poke = line.IndexOf("poke(", StringComparison.Ordinal);
        int comma = line.IndexOf(',', poke);
        if (poke < 0 || comma < 0)
            return false;
        string addrExpr = line[(poke + "poke(".Length)..comma].Trim();
        return TryParseUshortAddrExpr(addrExpr, out addr) && addr >= 0x10 && addr < 0x2000;
    }

    static bool TryParseUshortAddrExpr(string expr, out ushort addr)
    {
        addr = 0;
        expr = expr.Trim();
        if (expr.StartsWith("(ushort)", StringComparison.Ordinal))
            expr = expr["(ushort)".Length..].Trim();
        if (expr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(expr.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        return ushort.TryParse(expr, NumberStyles.Integer, CultureInfo.InvariantCulture, out addr);
    }

    static bool TryMatchJoypadShiftDoWhile(
        List<string> statements,
        int doAt,
        out int whileAt,
        out string padArg,
        out bool msbFirst,
        out ushort? assembleAddr)
    {
        whileAt = -1;
        padArg = "0";
        msbFirst = false;
        assembleAddr = null;
        if (doAt >= statements.Count || statements[doAt].TrimStart() != "do")
            return false;
        if (doAt + 1 >= statements.Count || statements[doAt + 1].TrimStart() != "{")
            return false;

        int depth = 0;
        int closeBrace = -1;
        for (int j = doAt + 1; j < statements.Count; j++)
        {
            string t = statements[j].TrimStart();
            if (t == "{")
                depth++;
            else if (t == "}")
            {
                depth--;
                if (depth == 0)
                {
                    closeBrace = j;
                    break;
                }
            }
        }

        if (closeBrace < 0 || closeBrace + 1 >= statements.Count)
            return false;
        string whileLine = statements[closeBrace + 1].TrimStart();
        if (!whileLine.StartsWith("while (!zero)", StringComparison.Ordinal)
            && !whileLine.StartsWith("while(!zero)", StringComparison.Ordinal))
            return false;

        bool sawJoy = false;
        bool sawYDec = false;
        bool sawRol = false;
        bool sawRor = false;
        for (int j = doAt + 2; j < closeBrace; j++)
        {
            string t = statements[j].TrimStart();
            if (TryGetPadPollArgFromJoyRead(t, out string arg))
            {
                sawJoy = true;
                padArg = arg;
            }
            if (t is "y--;" or "y = (byte)(y - 1);" or "dey();")
                sawYDec = true;
            NotePadShiftAssemble(t, ref sawRol, ref sawRor, ref assembleAddr);
        }

        if (!sawJoy || !sawYDec)
            return false;
        // ROL assemble → MSB (SMB); ROR or unknown → LSB (neslib).
        msbFirst = sawRol;
        _ = sawRor;
        whileAt = closeBrace + 1;
        return true;
    }

    /// <summary>
    /// <c>y = 8; L: … joy … y--; if (!zero) goto L;</c>
    /// </summary>
    static bool TryMatchJoypadShiftGotoLoop(
        List<string> statements,
        int yAt,
        out int endAt,
        out string padArg,
        out bool msbFirst,
        out ushort? assembleAddr)
    {
        endAt = -1;
        padArg = "0";
        msbFirst = false;
        assembleAddr = null;
        if (yAt + 2 >= statements.Count)
            return false;

        // Optional label after y = 8
        int bodyStart = yAt + 1;
        string? loopLabel = null;
        if (TryGetLabelName(statements[bodyStart], out string lab))
        {
            loopLabel = lab;
            bodyStart++;
        }

        bool sawJoy = false;
        bool sawRol = false;
        bool sawRor = false;
        for (int j = bodyStart; j < statements.Count && j < bodyStart + 24; j++)
        {
            string t = statements[j].TrimStart();
            if (TryGetPadPollArgFromJoyRead(t, out string arg))
            {
                sawJoy = true;
                padArg = arg;
            }
            NotePadShiftAssemble(t, ref sawRol, ref sawRor, ref assembleAddr);

            if (t is not ("y--;" or "y = (byte)(y - 1);" or "dey();"))
                continue;
            if (j + 1 >= statements.Count)
                return false;
            string branch = statements[j + 1].TrimStart();
            // if (!zero) goto L_xxxx;
            if (!branch.StartsWith("if (!zero) goto ", StringComparison.Ordinal))
                return false;
            if (loopLabel != null && !branch.Contains(loopLabel, StringComparison.Ordinal))
                return false;
            if (!sawJoy)
                return false;
            msbFirst = sawRol;
            _ = sawRor;
            endAt = j + 1;
            return true;
        }
        return false;
    }

    static void NotePadShiftAssemble(string line, ref bool sawRol, ref bool sawRor, ref ushort? assembleAddr)
    {
        if (IsRolAssembleIntoA(line))
        {
            sawRol = true;
            return;
        }
        if (TryGetRolMemoryAssembleAddr(line, out ushort rolAddr))
        {
            sawRol = true;
            assembleAddr = rolAddr;
            return;
        }
        if (IsRorAssembleIntoA(line))
        {
            sawRor = true;
            return;
        }
        if (TryGetRorMemoryAssembleAddr(line, out ushort rorAddr))
        {
            sawRor = true;
            assembleAddr = rorAddr;
        }
    }

    static bool TryGetPadPollArgFromJoyRead(string line, out string padArg)
    {
        padArg = "0";
        // a = peek((ushort)(JOYPAD1 + x));
        if (line.Contains("JOYPAD1 + x", StringComparison.Ordinal)
            || line.Contains("JOYPAD1+x", StringComparison.Ordinal))
        {
            padArg = "x";
            return line.Contains("peek(", StringComparison.Ordinal)
                || line.Contains("joypad", StringComparison.OrdinalIgnoreCase);
        }
        if (line.Contains("JOYPAD2 + x", StringComparison.Ordinal)
            || line.Contains("JOYPAD2+x", StringComparison.Ordinal))
        {
            // $4016,X with X selecting port — same as pad_poll(x) when x is 0/1.
            padArg = "x";
            return true;
        }
        if (line.Contains("joypad1_read()", StringComparison.Ordinal)
            || (line.Contains("peek(", StringComparison.Ordinal) && line.Contains("JOYPAD1", StringComparison.Ordinal)))
        {
            padArg = "0";
            return true;
        }
        if (line.Contains("joypad2_read()", StringComparison.Ordinal)
            || (line.Contains("peek(", StringComparison.Ordinal) && line.Contains("JOYPAD2", StringComparison.Ordinal)))
        {
            padArg = "1";
            return true;
        }
        return false;
    }

    static void RemovePrecedingJoypadStrobe(List<string> statements, int padPollAt)
    {
        // Walk back: optional LSR/flag noise, then Shift write, optional noise, Latch write.
        int i = padPollAt - 1;
        while (i >= 0 && IsIgnorableStrobeGap(statements[i].TrimStart()))
            i--;
        if (i < 0 || !IsJoypadShiftWrite(statements[i].TrimStart()))
            return;
        int shiftAt = i;
        i--;
        while (i >= 0 && IsIgnorableStrobeGap(statements[i].TrimStart()))
            i--;
        if (i < 0 || !IsJoypadLatchWrite(statements[i].TrimStart()))
            return;
        int latchAt = i;
        statements.RemoveRange(latchAt, padPollAt - latchAt);
    }

    static bool IsIgnorableStrobeGap(string line) =>
        line.Length == 0
        || line.StartsWith("//", StringComparison.Ordinal)
        || line.StartsWith("carry = ", StringComparison.Ordinal)
        || line is "a >>= 1;" or "a <<= 1;"
        || (line.StartsWith("a = (byte)(a ", StringComparison.Ordinal) && line.Contains(">>"));

    static bool IsJoypadLatchWrite(string line) =>
        line.Contains("joypad1_write(", StringComparison.Ordinal)
        && (line.Contains("JOYSTROBE.Latch", StringComparison.Ordinal)
            || line.Contains("(JOYSTROBE)1", StringComparison.Ordinal));

    static bool IsJoypadShiftWrite(string line) =>
        line.Contains("joypad1_write(", StringComparison.Ordinal)
        && (line.Contains("JOYSTROBE.Shift", StringComparison.Ordinal)
            || line.Contains("(JOYSTROBE)0", StringComparison.Ordinal)
            || line.Contains("(JOYSTROBE)a", StringComparison.Ordinal));

    /// <summary>
    /// Drop Latch/Shift pairs that immediately precede a call whose body already starts with <c>pad_poll</c>.
    /// </summary>
    static void RemoveRedundantStrobesBeforePadPollCalls(
        List<(int Bank, ushort Address, string Name, List<string> Body)> emitted)
    {
        var pollCallees = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in emitted)
        {
            foreach (string raw in e.Body)
            {
                string t = raw.TrimStart();
                if (t.Contains("pad_poll(", StringComparison.Ordinal)
                    || t.Contains("pad_poll_msb(", StringComparison.Ordinal))
                {
                    pollCallees.Add(e.Name);
                    break;
                }
            }
        }

        if (pollCallees.Count == 0)
            return;

        foreach (var e in emitted)
        {
            var body = e.Body;
            for (int i = 0; i < body.Count; i++)
            {
                string t = body[i].TrimStart();
                if (!IsJoypadLatchWrite(t))
                    continue;
                int shiftAt = -1;
                int callAt = -1;
                for (int j = i + 1; j < body.Count && j <= i + 6; j++)
                {
                    string u = body[j].TrimStart();
                    if (shiftAt < 0)
                    {
                        if (IsJoypadShiftWrite(u))
                        {
                            shiftAt = j;
                            continue;
                        }
                        if (IsIgnorableStrobeGap(u))
                            continue;
                        break;
                    }

                    if (IsIgnorableStrobeGap(u))
                        continue;
                    // func_XX();
                    if (u.Length > 3 && u.EndsWith("();", StringComparison.Ordinal)
                        && !u.Contains('=', StringComparison.Ordinal)
                        && !u.Contains(' ', StringComparison.Ordinal))
                    {
                        string name = u[..^3];
                        if (pollCallees.Contains(name))
                            callAt = j;
                    }
                    break;
                }

                if (shiftAt > 0 && callAt > 0)
                {
                    body.RemoveRange(i, shiftAt - i + 1);
                    i--;
                }
            }
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
                lifted = FormatLiftedEnumCall("joypad1_write", pokeVal, NesLibEnumFormatter.FormatJoyStrobe, "JOYSTROBE");
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

    /// <summary>Map <c>APU_PULSE1_CTRL</c>-style poke targets to <c>apu_pulse1_ctrl</c> writers.</summary>
    static bool TryMapApuRegisterWrite(string regConst, out string methodName)
    {
        methodName = regConst switch
        {
            "APU_PULSE1_CTRL" => "apu_pulse1_ctrl",
            "APU_PULSE1_SWEEP" => "apu_pulse1_sweep",
            "APU_PULSE1_TIMER_LO" => "apu_pulse1_timer_lo",
            "APU_PULSE1_TIMER_HI" => "apu_pulse1_timer_hi",
            "APU_PULSE2_CTRL" => "apu_pulse2_ctrl",
            "APU_PULSE2_SWEEP" => "apu_pulse2_sweep",
            "APU_PULSE2_TIMER_LO" => "apu_pulse2_timer_lo",
            "APU_PULSE2_TIMER_HI" => "apu_pulse2_timer_hi",
            "APU_TRIANGLE_CTRL" => "apu_triangle_ctrl",
            "APU_TRIANGLE_TIMER_LO" => "apu_triangle_timer_lo",
            "APU_TRIANGLE_TIMER_HI" => "apu_triangle_timer_hi",
            "APU_NOISE_CTRL" => "apu_noise_ctrl",
            "APU_NOISE_PERIOD" => "apu_noise_period",
            "APU_NOISE_LENGTH" => "apu_noise_length",
            "APU_DMC_FREQ" => "apu_dmc_freq",
            "APU_DMC_RAW" => "apu_dmc_raw",
            "APU_DMC_START" => "apu_dmc_start",
            "APU_DMC_LEN" => "apu_dmc_len",
            _ => null!,
        };
        return methodName is not null;
    }

    static void RewriteApuPokesInBody(List<string> body)
    {
        for (int i = 0; i < body.Count; i++)
        {
            if (TryRewriteApuPokesInLine(body[i], out string rewritten))
                body[i] = rewritten;
        }
    }

    /// <summary>
    /// Replace <c>poke(APU_*, …)</c> with neslib writers even when embedded in
    /// <c>if (cond) { poke(...); }</c> one-liners.
    /// </summary>
    static bool TryRewriteApuPokesInLine(string line, out string rewritten)
    {
        rewritten = line;
        bool any = false;
        int search = 0;
        while (true)
        {
            int at = rewritten.IndexOf("poke(APU_", search, StringComparison.Ordinal);
            if (at < 0)
                break;
            int open = rewritten.IndexOf('(', at);
            if (open < 0)
                break;
            int depth = 0;
            int close = -1;
            for (int i = open; i < rewritten.Length; i++)
            {
                char c = rewritten[i];
                if (c == '(')
                    depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        close = i;
                        break;
                    }
                }
            }
            if (close < 0)
                break;

            string inner = rewritten.Substring(open + 1, close - open - 1);
            int comma = FindTopLevelComma(inner);
            if (comma < 0)
            {
                search = at + 1;
                continue;
            }

            string reg = inner[..comma].Trim();
            string val = inner[(comma + 1)..].Trim();
            string? call = FormatApuPokeReplacement(reg, val);
            if (call is null)
            {
                search = close + 1;
                continue;
            }

            rewritten = rewritten[..at] + call + rewritten[(close + 1)..];
            any = true;
            search = at + call.Length;
        }
        return any;
    }

    /// <summary>Expression form (no trailing semicolon) for embedding in one-liners.</summary>
    static string? FormatApuPokeReplacement(string regConst, string value)
    {
        if (regConst == "APU_STATUS")
        {
            if (TryParseByteLiteral(value, out byte b))
                return $"apu_status_write({NesLibEnumFormatter.FormatApuStatus(b)})";
            return $"apu_status_write((APUSTATUS){value})";
        }
        if (regConst == "APU_FRAME")
        {
            if (TryParseByteLiteral(value, out byte b))
                return $"apu_frame({NesLibEnumFormatter.FormatApuFrame(b)})";
            return $"apu_frame((APUFRAME){value})";
        }
        if (!TryMapApuRegisterWrite(regConst, out string methodName))
            return null;
        return $"{methodName}({value})";
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
    /// RAM holding packed controller bytes (e.g. SMB <c>$06FC</c>, Metroid <c>$12/$14/$16</c>).
    /// Only poll assemble/store targets — not every address touched near a joypad read
    /// (Metroid <c>$4D</c> Samus direction / <c>$6B</c> object attrs must stay <c>zp_*</c>).
    /// </summary>
    static HashSet<ushort> CollectPadAddresses(IEnumerable<List<string>> bodies)
    {
        var pads = new HashSet<ushort>();
        var bodyList = bodies.ToList();

        // Seed: ROL/ROR into RAM next to joypad traffic.
        foreach (var body in bodyList)
        {
            bool sawJoy = false;
            foreach (string raw in body)
            {
                string t = raw.TrimStart();
                if (IsJoypadDataRead(t))
                    sawJoy = true;
                if (!sawJoy)
                    continue;
                if (TryGetRolMemoryAssembleAddr(t, out ushort mem)
                    || TryGetRorMemoryAssembleAddr(t, out mem))
                    pads.Add(mem);
            }
        }

        // Seed + copy: pad_poll → poke destinations; keep pad-ness through push/pop and
        // immediate masks. A short "poll store window" still marks poke(a) after A is
        // briefly clobbered (Metroid $12/$16), but not across calls/returns.
        const int pollStoreWindowLines = 24;
        bool changed;
        do
        {
            changed = false;
            foreach (var body in bodyList)
            {
                bool aPad = false, yPad = false;
                int pollStoreLeft = 0;
                int padStack = 0;
                foreach (string raw in body)
                {
                    string t = raw.TrimStart();

                    if (IsPadPollAssign(t))
                    {
                        aPad = true;
                        pollStoreLeft = pollStoreWindowLines;
                        continue;
                    }

                    if (pollStoreLeft > 0)
                        pollStoreLeft--;

                    if (t is "pha();" or "push(a);")
                    {
                        if (aPad)
                            padStack++;
                        continue;
                    }

                    if (t is "a = pla();" or "pla();" or "a = pop();")
                    {
                        // Diamond CFG: one push, two pop paths — don't require perfect stack depth.
                        aPad = padStack > 0 || pollStoreLeft > 0;
                        if (padStack > 0)
                            padStack--;
                        continue;
                    }

                    if (TryParseAssignPeekRam(t, out string dest, out ushort srcAddr))
                    {
                        bool fromPad = pads.Contains(srcAddr);
                        if (dest == "a")
                            aPad = fromPad;
                        else if (dest == "y")
                            yPad = fromPad;
                        continue;
                    }

                    if (IsImmediateLogicOnA(t))
                        continue;

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

                    bool storeA = (aPad || pollStoreLeft > 0) && t.Contains(", a)", StringComparison.Ordinal);
                    bool storeY = yPad && t.Contains(", y)", StringComparison.Ordinal);
                    if (t.StartsWith("poke(", StringComparison.Ordinal) && (storeA || storeY))
                    {
                        foreach (ushort addr in ExtractRamBases(t))
                        {
                            if (addr >= 0x10 && addr < 0x2000 && pads.Add(addr))
                                changed = true;
                        }
                        continue;
                    }

                    if (IsSoftCpuCall(t))
                    {
                        aPad = yPad = false;
                        pollStoreLeft = 0;
                        continue;
                    }

                    if (t.StartsWith("return", StringComparison.Ordinal))
                    {
                        // Soft-CPU often has push / early return / label / pop (SMB $074A).
                        // Keep pollStoreLeft + padStack so the alternate pop path still counts.
                        aPad = yPad = false;
                        continue;
                    }

                    if (t.StartsWith("a = ", StringComparison.Ordinal))
                        aPad = false;
                    else if (t.StartsWith("y = ", StringComparison.Ordinal))
                        yPad = false;
                }
            }
        } while (changed);

        return pads;
    }

    static bool IsPadPollAssign(string line) =>
        line.Contains("pad_poll_msb(", StringComparison.Ordinal)
        || line.Contains("pad_poll(", StringComparison.Ordinal)
        || line.Contains("pad1_poll(", StringComparison.Ordinal)
        || line.Contains("pad2_poll(", StringComparison.Ordinal)
        || line.Contains("pad_trigger(", StringComparison.Ordinal);

    static bool IsImmediateLogicOnA(string line)
    {
        foreach (string op in new[] { "&", "|", "^" })
        {
            string prefix = $"a = (byte)(a {op} ";
            if (line.StartsWith(prefix, StringComparison.Ordinal)
                && !line.Contains("peek(", StringComparison.Ordinal))
                return true;
        }
        foreach (string op in new[] { "&=", "|=", "^=" })
        {
            if (line.StartsWith($"a {op} ", StringComparison.Ordinal)
                && !line.Contains("peek(", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    static bool IsSoftCpuCall(string line)
    {
        if (line.StartsWith("poke(", StringComparison.Ordinal)
            || line.StartsWith("peek(", StringComparison.Ordinal)
            || line.StartsWith("push(", StringComparison.Ordinal)
            || line.StartsWith("pop(", StringComparison.Ordinal)
            || line is "pha();" or "pla();" or "a = pla();" or "php();" or "plp();"
            || line.StartsWith("pha(", StringComparison.Ordinal)
            || line.StartsWith("pla(", StringComparison.Ordinal))
            return false;
        // func_XXXX();  or  known NESLib call
        int open = line.IndexOf('(');
        int close = line.IndexOf(')');
        return open > 0 && close > open && line.EndsWith(");", StringComparison.Ordinal)
            && !line.StartsWith("a = ", StringComparison.Ordinal)
            && !line.StartsWith("x = ", StringComparison.Ordinal)
            && !line.StartsWith("y = ", StringComparison.Ordinal)
            && !line.StartsWith("zero = ", StringComparison.Ordinal)
            && !line.StartsWith("carry = ", StringComparison.Ordinal)
            && !line.StartsWith("negative = ", StringComparison.Ordinal);
    }

    /// <summary>
    /// PRG sites of <c>LDA abs</c> / <c>LDA abs,X</c> / <c>LDA abs,Y</c> targeting known pad RAM.
    /// </summary>
    IEnumerable<(ushort Cpu, int Bank)> FindPrgAbsoluteLoadsOf(HashSet<ushort> pads)
    {
        if (pads.Count == 0)
            yield break;

        byte[] prg = _rom.PrgRom;
        var seen = new HashSet<(ushort, int)>();
        for (int off = 0; off + 2 < prg.Length; off++)
        {
            byte op = prg[off];
            if (op is not (0xAD or 0xBD or 0xB9))
                continue;
            ushort ram = (ushort)(prg[off + 1] | (prg[off + 2] << 8));
            // Absolute loads only — ZP pad mirrors are already reached from the poll routine.
            // Avoid seeding on every LDA abs that happens to share a false-positive pad addr.
            if (!pads.Contains(ram) || ram < 0x100)
                continue;

            foreach (var (cpu, bank) in CpuSitesForPrgOffset(off))
            {
                if (seen.Add((cpu, bank)))
                    yield return (cpu, bank);
            }
        }
    }

    IEnumerable<(ushort Cpu, int Bank)> CpuSitesForPrgOffset(int offset)
    {
        var banks = _disasm.Banks;
        int prgLen = _rom.PrgRom.Length;

        // NROM-128: mirrored $8000/$C000
        if (prgLen == 0x4000)
        {
            ushort within = (ushort)(offset % 0x4000);
            yield return ((ushort)(0x8000 + within), 0);
            yield return ((ushort)(0xC000 + within), 0);
            yield break;
        }

        // NROM-256 / single 32KB image at $8000
        if (_rom.PrgBanks <= 2
            && !banks.HasSwitchable16KWindow
            && !banks.HasSwitchable32KWindow
            && !banks.HasSwitchable8KWindow)
        {
            if (offset >= 0 && offset < Math.Min(prgLen, 0x8000))
                yield return ((ushort)(0x8000 + offset), 0);
            yield break;
        }

        if (banks.HasSwitchable32KWindow)
        {
            int bank = offset / 0x8000;
            int within = offset % 0x8000;
            if (bank < banks.SwitchableBankCount)
                yield return ((ushort)(0x8000 + within), bank);
            yield break;
        }

        if (banks.HasSwitchable16KWindow)
        {
            int bankSize = banks.BankSize;
            int bank = offset / bankSize;
            int within = offset % bankSize;
            if (bank == banks.FixedBank)
                yield return ((ushort)(0xC000 + within), bank);
            else if (bank < banks.SwitchableBankCount)
                yield return ((ushort)(0x8000 + within), bank);
            yield break;
        }

        // Fallback: treat as last 32KB window at $8000
        if (prgLen >= 0x8000 && offset >= prgLen - 0x8000)
            yield return ((ushort)(0x8000 + (offset - (prgLen - 0x8000))), 0);
    }

    static bool TryGetRorMemoryAssembleAddr(string line, out ushort addr)
    {
        addr = 0;
        if (TryParseAbsRmwComment(line, out addr)
            && line.Contains("ROR", StringComparison.OrdinalIgnoreCase))
            return addr >= 0x10 && addr < 0x2000;

        if (!line.Contains("(_m >> 1)", StringComparison.Ordinal)
            || !line.Contains("_c ? 0x80 : 0", StringComparison.OrdinalIgnoreCase)
            || !line.Contains("poke(", StringComparison.Ordinal))
            return false;

        int poke = line.IndexOf("poke(", StringComparison.Ordinal);
        int comma = line.IndexOf(',', poke);
        if (poke < 0 || comma < 0)
            return false;
        return TryParseUshortAddrExpr(line[(poke + "poke(".Length)..comma].Trim(), out addr)
            && addr >= 0x10 && addr < 0x2000;
    }

    static bool IsJoypadDataRead(string line) =>
        line.Contains("joypad1_read()", StringComparison.Ordinal)
        || line.Contains("joypad2_read()", StringComparison.Ordinal)
        || line.Contains("pad_poll_msb(", StringComparison.Ordinal)
        || line.Contains("pad_poll(", StringComparison.Ordinal)
        || line.Contains("pad_trigger(", StringComparison.Ordinal)
        || (line.Contains("peek(", StringComparison.Ordinal)
            && (line.Contains("JOYPAD1", StringComparison.Ordinal)
                || line.Contains("JOYPAD2", StringComparison.Ordinal)));

    /// <summary>e.g. <c>// ROL $06FC</c> / <c>// ASL $F5</c> from unrecovered absolute RMW.</summary>
    static bool TryParseAbsRmwComment(string line, out ushort addr)
    {
        addr = 0;
        if (!line.StartsWith("//", StringComparison.Ordinal))
            return false;
        int dollar = line.IndexOf('$');
        if (dollar < 0)
            return false;
        int hexStart = dollar + 1;
        int hexEnd = hexStart;
        while (hexEnd < line.Length && Uri.IsHexDigit(line[hexEnd]))
            hexEnd++;
        string hex = line[hexStart..hexEnd];
        return hex.Length is >= 2 and <= 4
            && ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
    }

    /// <summary>
    /// True for button masks (≤2 bits or common A/B / dpad / Start+Select groups), not every byte.
    /// </summary>
    static bool LooksLikePadImmediateUse(string line)
    {
        foreach (string op in new[] { "&", "|", "^" })
        {
            string prefix = $"a = (byte)(a {op} ";
            if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(");", StringComparison.Ordinal))
            {
                string imm = line[prefix.Length..^2].Trim();
                return TryParseByteLiteral(imm, out byte b) && IsDistinctivePadMask(b);
            }
        }

        const string zeroPrefix = "zero = a == ";
        if (!line.StartsWith(zeroPrefix, StringComparison.Ordinal))
            return false;
        int semi = line.IndexOf(';');
        if (semi < 0)
            return false;
        string cmp = line[zeroPrefix.Length..semi].Trim();
        return TryParseByteLiteral(cmp, out byte c) && IsDistinctivePadMask(c);
    }

    static bool IsDistinctivePadMask(byte value)
    {
        if (value == 0)
            return false;
        // Single/double button, A|B, dpad pairs, Start|Select, or clear-masks of those.
        byte bits = value;
        byte cleared = (byte)~value;
        static bool ok(byte v) => v != 0 && BitCount(v) <= 2
            || v is 0x03 or 0x0C or 0x30 or 0xC0 or 0x0F or 0xF0;
        return ok(bits) || ok(cleared);
    }

    /// <summary>
    /// RAM bytes loaded into A (with optional immediate AND/OR) then written to PPU CTRL/MASK.
    /// </summary>
    static (HashSet<ushort> Mask, HashSet<ushort> Ctrl) CollectPpuShadows(IEnumerable<List<string>> bodies)
    {
        var mask = new HashSet<ushort>();
        var ctrl = new HashSet<ushort>();
        foreach (var body in bodies)
        {
            ushort? lastPeek = null;
            foreach (string raw in body)
            {
                string t = raw.TrimStart();
                if (TryParseAssignPeekRam(t, out string dest, out ushort addr) && dest == "a")
                {
                    lastPeek = addr;
                    continue;
                }

                if (lastPeek is ushort shadow)
                {
                    if (t.StartsWith("ppu_mask_write(", StringComparison.Ordinal))
                        mask.Add(shadow);
                    else if (t.StartsWith("ppu_ctrl(", StringComparison.Ordinal))
                        ctrl.Add(shadow);
                }

                // Keep shadow through AND/OR of immediates before the write.
                if (IsImmediateLogicOnA(t))
                    continue;

                if (t.StartsWith("a = ", StringComparison.Ordinal)
                    || t.StartsWith("ppu_", StringComparison.Ordinal)
                    || t.StartsWith("poke(", StringComparison.Ordinal))
                    lastPeek = null;
            }
        }
        return (mask, ctrl);
    }

    /// <summary>
    /// Addresses loaded into A immediately before <c>ppu_scroll</c>.
    /// </summary>
    static (HashSet<ushort> Both, HashSet<ushort> X, HashSet<ushort> Y) CollectScrollShadows(
        IEnumerable<List<string>> bodies)
    {
        var both = new HashSet<ushort>();
        var scrollX = new HashSet<ushort>();
        var scrollY = new HashSet<ushort>();
        foreach (var body in bodies)
        {
            ushort? lastA = null;
            foreach (string raw in body)
            {
                string t = raw.TrimStart();
                if (TryParseAssignPeekRam(t, out string dest, out ushort addr) && dest == "a")
                {
                    lastA = addr;
                    continue;
                }

                if (t.StartsWith("ppu_scroll(", StringComparison.Ordinal)
                    && TryParsePpuScrollArgs(t, out string sx, out string sy)
                    && lastA is ushort s)
                {
                    if (sx == "a" && sy == "a")
                        both.Add(s);
                    else if (sx == "a")
                        scrollX.Add(s);
                    else if (sy == "a")
                        scrollY.Add(s);
                }

                if (IsImmediateLogicOnA(t) || t is "x = a;" or "y = a;")
                    continue;

                if (t.StartsWith("a = ", StringComparison.Ordinal)
                    || t.StartsWith("ppu_", StringComparison.Ordinal)
                    || t.StartsWith("poke(", StringComparison.Ordinal))
                {
                    if (!TryParseAssignPeekRam(t, out _, out _))
                        lastA = null;
                }
            }
        }
        return (both, scrollX, scrollY);
    }

    static bool TryParsePpuScrollArgs(string line, out string sx, out string sy)
    {
        sx = sy = "";
        const string prefix = "ppu_scroll(";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(");", StringComparison.Ordinal))
            return false;
        string inner = line[prefix.Length..^2];
        int comma = inner.IndexOf(',');
        if (comma < 0)
            return false;
        sx = inner[..comma].Trim();
        sy = inner[(comma + 1)..].Trim();
        return sx.Length > 0 && sy.Length > 0;
    }

    /// <summary>ZP lo bytes used as <c>(lo)|(lo+1)&lt;&lt;8</c> indirect pointers.</summary>
    static HashSet<byte> CollectIndirectPointerLos(IEnumerable<List<string>> bodies)
    {
        var los = new HashSet<byte>();
        var pat = new Regex(
            @"peek\(\(ushort\)0x(?<lo>[0-9A-Fa-f]{1,2})\)\s*\|\s*peek\(\(ushort\)0x(?<hi>[0-9A-Fa-f]{1,2})\)\s*<<\s*8",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        foreach (var body in bodies)
        {
            foreach (string raw in body)
            {
                foreach (Match m in pat.Matches(raw))
                {
                    if (!byte.TryParse(m.Groups["lo"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte lo))
                        continue;
                    if (!byte.TryParse(m.Groups["hi"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte hi))
                        continue;
                    if (hi == (byte)(lo + 1))
                        los.Add(lo);
                }
            }
        }
        return los;
    }

    /// <summary>Addresses touched in exactly one recovered routine → that routine as name scope.</summary>
    static Dictionary<ushort, string> CollectSingleRoutineScopes(
        List<(int Bank, ushort Address, string Name, List<string> Body)> emitted)
    {
        var uses = new Dictionary<ushort, HashSet<string>>();
        foreach (var e in emitted)
        {
            foreach (string raw in e.Body)
            {
                foreach (ushort addr in ExtractUshortLiterals(raw))
                {
                    if (addr >= 0x2000)
                        continue;
                    if (!uses.TryGetValue(addr, out var set))
                        uses[addr] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(e.Name);
                }
            }
        }

        var single = new Dictionary<ushort, string>();
        foreach (var (addr, names) in uses)
        {
            if (names.Count != 1)
                continue;
            string scope = SanitizeScopePrefix(names.First());
            if (scope.Length > 0)
                single[addr] = scope;
        }
        return single;
    }

    static string SanitizeScopePrefix(string routine)
    {
        if (routine is "nmi" or "reset" or "irq")
            return routine;
        // func_00_ABCD → f_ABCD
        if (routine.StartsWith("func_", StringComparison.Ordinal))
        {
            int last = routine.LastIndexOf('_');
            if (last > 0 && last < routine.Length - 1)
                return "f_" + routine[(last + 1)..];
        }
        // Keep short alphanumeric identifiers only.
        if (routine.Length <= 16 && routine.All(c => char.IsLetterOrDigit(c) || c == '_'))
            return routine;
        return "";
    }

    const byte AKindNone = 0, AKindPad = 1, AKindMask = 2, AKindCtrl = 3, AKindJoySerial = 4, AKindPadMsb = 5;

    static void FormatTypedZpLiterals(
        List<string> statements,
        HashSet<ushort> padAddrs,
        bool padMsb,
        HashSet<ushort> maskShadows,
        HashSet<ushort> ctrlShadows)
    {
        bool writingPalette = false;
        bool expectingPaletteLo = false;
        byte aKind = AKindNone;
        int padOnStack = 0;
        byte padKind = padMsb ? AKindPadMsb : AKindPad;

        for (int i = 0; i < statements.Count; i++)
        {
            string indent = LeadingIndent(statements[i]);
            string line = statements[i].TrimStart();

            TryUpdatePaletteWriteContext(line, ref writingPalette, ref expectingPaletteLo);

            if (line.StartsWith("vram_put(", StringComparison.Ordinal) && writingPalette
                && TryParseVramPutLiteral(line, out byte color))
            {
                statements[i] = indent + $"vram_put({NesLibEnumFormatter.FormatNesColor(color)});";
                continue;
            }

            if (writingPalette && line.StartsWith("vram_write(new byte[]", StringComparison.Ordinal)
                && TryRewriteVramWriteColors(line, out string coloredWrite))
            {
                statements[i] = indent + coloredWrite;
                continue;
            }

            // Assembled poll result — LSB vs MSB from which poll was lifted.
            if (line.Contains("pad_poll_msb(", StringComparison.Ordinal))
            {
                aKind = AKindPadMsb;
                continue;
            }
            if (line.Contains("pad_poll(", StringComparison.Ordinal)
                || line.Contains("pad1_poll(", StringComparison.Ordinal)
                || line.Contains("pad2_poll(", StringComparison.Ordinal)
                || line.Contains("pad_trigger(", StringComparison.Ordinal)
                || line.Contains("pad_state(", StringComparison.Ordinal))
            {
                aKind = AKindPad;
                continue;
            }

            // Raw serial joypad reads: only bit 0 is button data (JOYPADBIT), not PAD.*.
            if (IsJoypadDataRead(line) && line.StartsWith("a = ", StringComparison.Ordinal)
                && !line.Contains("pad_poll", StringComparison.Ordinal))
            {
                aKind = AKindJoySerial;
                continue;
            }

            if (TryParseAssignPeekRam(line, out string dest, out ushort srcAddr))
            {
                if (dest == "a")
                {
                    aKind = padAddrs.Contains(srcAddr) ? padKind
                        : maskShadows.Contains(srcAddr) ? AKindMask
                        : ctrlShadows.Contains(srcAddr) ? AKindCtrl
                        : AKindNone;
                }
                continue;
            }

            if (line is "pha();" or "push(a);")
            {
                if (aKind is AKindPad or AKindPadMsb)
                    padOnStack++;
                continue;
            }

            if (line is "a = pla();" or "pla();" or "a = pop();")
            {
                aKind = padOnStack > 0 ? padKind : AKindNone;
                if (padOnStack > 0)
                    padOnStack--;
                continue;
            }

            if (TryParsePokeRam(line, out ushort pokeAddr, out string pokeVal)
                && pokeVal == "a"
                && padAddrs.Contains(pokeAddr))
            {
                aKind = padKind;
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
                foreach (ushort addr in ExtractRamBases(line))
                {
                    if (padAddrs.Contains(addr)) aKind = padKind;
                    else if (maskShadows.Contains(addr)) aKind = AKindMask;
                    else if (ctrlShadows.Contains(addr)) aKind = AKindCtrl;
                }
                continue;
            }

            if (line.StartsWith("a = ", StringComparison.Ordinal) && !IsImmediateLogicOnA(line))
            {
                aKind = AKindNone;
            }
        }
    }

    static bool TryRewriteTypedImmediateLine(string line, byte kind, out string rewritten)
    {
        rewritten = null!;

        if (kind == AKindJoySerial)
            return TryRewriteJoySerialLine(line, out rewritten);

        bool msb = kind == AKindPadMsb;
        Func<byte, string?> formatOp = kind switch
        {
            AKindPad or AKindPadMsb => b =>
                NesLibEnumFormatter.TryFormatPadByteOperand(b, out string e, msb) ? e : null,
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

            string compound = $"a {op}= ";
            if (line.StartsWith(compound, StringComparison.Ordinal) && line.EndsWith(';'))
            {
                string imm = line[compound.Length..^1].Trim();
                if (!TryParseByteLiteral(imm, out byte b))
                    return false;
                string? expr = formatOp(b);
                if (expr == null)
                    return false;
                rewritten = $"{compound}{expr};";
                return true;
            }
        }

        if (kind is not (AKindPad or AKindPadMsb))
            return false;

        const string zeroPrefix = "zero = a == ";
        if (!line.StartsWith(zeroPrefix, StringComparison.Ordinal))
            return false;
        int semi = line.IndexOf(';');
        if (semi < 0)
            return false;
        string immCmp = line[zeroPrefix.Length..semi].Trim();
        if (!TryParseByteLiteral(immCmp, out byte cb) || !NesLibEnumFormatter.IsExactPadFlags(cb, msb))
            return false;
        if (!NesLibEnumFormatter.TryFormatPadByteOperand(cb, out string padExpr, msb))
            return false;

        rewritten = line.Replace($"a == {immCmp}", $"a == {padExpr}")
            .Replace($"a >= {immCmp}", $"a >= {padExpr}")
            .Replace($"(a - {immCmp})", $"(a - {padExpr})");
        return true;
    }

    static bool TryRewriteJoySerialLine(string line, out string rewritten)
    {
        rewritten = null!;
        // carry = (a & 1) != 0; a >>= 1;
        if (line.Contains("(a & 1)", StringComparison.Ordinal)
            || line.Contains("(a & 0x01)", StringComparison.OrdinalIgnoreCase))
        {
            rewritten = line
                .Replace("(a & 1)", "(a & (byte)JOYPADBIT.DATA)", StringComparison.Ordinal)
                .Replace("(a & 0x01)", "(a & (byte)JOYPADBIT.DATA)", StringComparison.OrdinalIgnoreCase)
                .Replace("(a & 0x1)", "(a & (byte)JOYPADBIT.DATA)", StringComparison.OrdinalIgnoreCase);
            return !ReferenceEquals(rewritten, line) && rewritten != line;
        }

        const string andPrefix = "a = (byte)(a & ";
        if (line.StartsWith(andPrefix, StringComparison.Ordinal) && line.EndsWith(");", StringComparison.Ordinal))
        {
            string imm = line[andPrefix.Length..^2].Trim();
            if (TryParseByteLiteral(imm, out byte b) && b == 1)
            {
                rewritten = $"{andPrefix}(byte)JOYPADBIT.DATA);";
                return true;
            }
        }
        if (line.StartsWith("a &= ", StringComparison.Ordinal) && line.EndsWith(';'))
        {
            string imm = line["a &= ".Length..^1].Trim();
            if (TryParseByteLiteral(imm, out byte b) && b == 1)
            {
                rewritten = "a &= (byte)JOYPADBIT.DATA;";
                return true;
            }
        }
        return false;
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
        if (!TryParseAssignPeekRam(line, out dest, out ushort addr) || addr > 0xFF)
            return false;
        zp = (byte)addr;
        return true;
    }

    static readonly Regex AssignPeekAbs = new(
        @"^(?<dest>a|x|y)\s*=\s*peek\(\(ushort\)0x(?<addr>[0-9A-Fa-f]{1,4})\);$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex AssignPeekIndexed = new(
        @"^(?<dest>a|x|y)\s*=\s*peek\(\(ushort\)\(\(ushort\)0x(?<addr>[0-9A-Fa-f]{1,4})\s*\+\s*[xy]\)\);$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex PokeAbs = new(
        @"^poke\(\(ushort\)0x(?<addr>[0-9A-Fa-f]{1,4}),\s*(?<val>\w+)\);$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex PokeIndexed = new(
        @"^poke\(\(ushort\)\(\(ushort\)0x(?<addr>[0-9A-Fa-f]{1,4})\s*\+\s*[xy]\),\s*(?<val>\w+)\);$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Absolute or indexed RAM peek into a/x/y — base address for pad typing.</summary>
    static bool TryParseAssignPeekRam(string line, out string dest, out ushort addr)
    {
        dest = null!;
        addr = 0;
        var m = AssignPeekAbs.Match(line);
        if (!m.Success)
            m = AssignPeekIndexed.Match(line);
        if (!m.Success)
            return false;
        dest = m.Groups["dest"].Value;
        return ushort.TryParse(m.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
    }

    static bool TryParsePokeZp(string line, out byte zp, out string value)
    {
        zp = 0;
        value = null!;
        if (!TryParsePokeRam(line, out ushort addr, out value) || addr > 0xFF)
            return false;
        zp = (byte)addr;
        return true;
    }

    static bool TryParsePokeRam(string line, out ushort addr, out string value)
    {
        addr = 0;
        value = null!;
        var m = PokeAbs.Match(line);
        if (!m.Success)
            m = PokeIndexed.Match(line);
        if (!m.Success)
            return false;
        value = m.Groups["val"].Value;
        return ushort.TryParse(m.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
    }

    static IEnumerable<byte> ExtractZpAddresses(string line)
    {
        foreach (ushort addr in ExtractRamBases(line))
        {
            if (addr <= 0xFF)
                yield return (byte)addr;
        }
    }

    /// <summary>
    /// Absolute RAM bases in <c>(ushort)0x….</c> forms, including indexed <c>(ushort)(0x06FC + x)</c>.
    /// </summary>
    static IEnumerable<ushort> ExtractRamBases(string line)
    {
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
            if (hex.Length is >= 1 and <= 4
                && ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort addr))
                yield return addr;
            i = hexEnd;
        }
    }

    /// <summary>
    /// Track whether subsequent <c>vram_put</c> literals target the PPU palette ($3Fxx).
    /// Recognizes constant <c>vram_adr(0x3F..)</c>, <c>(0x3F &lt;&lt; 8) | …</c> forms, and
    /// <c>poke(PPU_ADDR, 0x3F)</c> + following lo write.
    /// </summary>
    static void TryUpdatePaletteWriteContext(string line, ref bool writingPalette, ref bool expectingPaletteLo)
    {
        if (TryParseVramAdrConstant(line, out ushort vaddr))
        {
            writingPalette = (vaddr & 0xFF00) == 0x3F00;
            expectingPaletteLo = false;
            return;
        }

        if (line.StartsWith("vram_adr(", StringComparison.Ordinal))
        {
            // Soft-CPU lift often emits vram_adr((ushort)((0x3F << 8) | lo)) when lo is a register.
            writingPalette = line.Contains("0x3F << 8", StringComparison.OrdinalIgnoreCase);
            expectingPaletteLo = false;
            return;
        }

        if (TryParsePoke(line, out string pokeAddr, out string pokeVal) && pokeAddr == "PPU_ADDR")
        {
            // PPU_ADDR is written hi then lo. After hi=0x3F, the next write is lo of $3Fxx.
            if (expectingPaletteLo)
            {
                writingPalette = true;
                expectingPaletteLo = false;
                return;
            }

            if (TryParseByteLiteral(pokeVal, out byte hi) && hi == 0x3F)
            {
                expectingPaletteLo = true;
                writingPalette = true;
            }
            else
            {
                writingPalette = false;
            }
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
    /// <c>vram_write(new byte[] { 0x0F, 0x16 });</c> → NESColor names while writing palette RAM.
    /// </summary>
    static bool TryRewriteVramWriteColors(string line, out string rewritten)
    {
        rewritten = null!;
        const string prefix = "vram_write(new byte[] { ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(" });", StringComparison.Ordinal))
            return false;
        string inner = line[prefix.Length..^4]; // strip trailing " });"
        string[] parts = inner.Split(',');
        if (parts.Length == 0)
            return false;
        var colored = new string[parts.Length];
        bool any = false;
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (TryParseByteLiteral(p, out byte b))
            {
                colored[i] = NesLibEnumFormatter.FormatNesColor(b);
                any = true;
            }
            else
                colored[i] = p;
        }
        if (!any)
            return false;
        rewritten = $"vram_write(new byte[] {{ {string.Join(", ", colored)} }});";
        return true;
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

    static bool TryFindNextPpuScroll(List<string> statements, int start, out int index, out string value) =>
        TryFindNextPoke(statements, start, "PPU_SCROLL", out index, out value, stopOnPpuTraffic: true);

    /// <summary>
    /// Find the next <c>poke(target, …)</c> within a short window.
    /// </summary>
    static bool TryFindNextPoke(
        List<string> statements,
        int start,
        string targetAddr,
        out int index,
        out string value,
        bool stopOnPpuTraffic = false,
        bool allowYBump = false)
    {
        value = null!;
        index = -1;
        for (int j = start; j < statements.Count && j < start + 8; j++)
        {
            string t = statements[j].TrimStart();
            if (TryParsePoke(t, out string addr, out value) && addr == targetAddr)
            {
                index = j;
                return true;
            }

            if (allowYBump && t is "y++;" or "y--;" or "x++;" or "x--;")
                continue;
            if (allowYBump && (t.StartsWith("a = PeekVramBufY(", StringComparison.Ordinal)
                || t.StartsWith("a = peek(", StringComparison.Ordinal)))
                continue;

            if (!stopOnPpuTraffic)
                continue;

            // Latch-resetting or other PPU traffic between halves — don't fuse.
            if (t.Contains("PPU_", StringComparison.Ordinal)
                || t.Contains("ppu_status", StringComparison.Ordinal)
                || t.Contains("ppu_ctrl", StringComparison.Ordinal)
                || t.Contains("ppu_mask", StringComparison.Ordinal)
                || t.Contains("ppu_scroll", StringComparison.Ordinal)
                || t.Contains("waitvsync", StringComparison.Ordinal)
                || t.Contains("sprite0_", StringComparison.Ordinal)
                || t.Contains("vram_", StringComparison.Ordinal)
                || t.Contains("oam_dma", StringComparison.Ordinal))
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
            case 0xDD when o1.HasValue && o2.HasValue: // CMP abs,X
                statements.Add($"{{ byte _v = peek({IndexAddr(FormatAddr(Abs(o1, o2)), "x")}); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }}");
                return;
            case 0xD9 when o1.HasValue && o2.HasValue: // CMP abs,Y
                statements.Add($"{{ byte _v = peek({IndexAddr(FormatAddr(Abs(o1, o2)), "y")}); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }}");
                return;
            case 0xD5 when o1.HasValue: // CMP zp,X
                statements.Add($"{{ byte _v = peek({IndexAddr(FormatZp(o1.Value), "x")}); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }}");
                return;
            case 0xE0 when o1.HasValue: // CPX #
                statements.Add($"zero = x == {FormatByte(o1.Value)}; carry = x >= {FormatByte(o1.Value)};");
                return;
            case 0xC0 when o1.HasValue: // CPY #
                statements.Add($"zero = y == {FormatByte(o1.Value)}; carry = y >= {FormatByte(o1.Value)};");
                return;
            case 0x29 when o1.HasValue: // AND #
                statements.Add($"a &= {FormatByte(o1.Value)};");
                lastImmA = null;
                return;
            case 0x25 when o1.HasValue: // AND zp
                statements.Add($"a = (byte)(a & peek({FormatZp(o1.Value)}));");
                lastImmA = null;
                return;
            case 0x2D when o1.HasValue && o2.HasValue: // AND abs
                statements.Add($"a = (byte)(a & peek({FormatAddr(Abs(o1, o2))}));");
                lastImmA = null;
                return;
            case 0x3D when o1.HasValue && o2.HasValue: // AND abs,X
                statements.Add($"a = (byte)(a & peek({IndexAddr(FormatAddr(Abs(o1, o2)), "x")}));");
                lastImmA = null;
                return;
            case 0x39 when o1.HasValue && o2.HasValue: // AND abs,Y
                statements.Add($"a = (byte)(a & peek({IndexAddr(FormatAddr(Abs(o1, o2)), "y")}));");
                lastImmA = null;
                return;
            case 0x09 when o1.HasValue: // ORA #
                statements.Add($"a |= {FormatByte(o1.Value)};");
                lastImmA = null;
                return;
            case 0x05 when o1.HasValue: // ORA zp
                statements.Add($"a = (byte)(a | peek({FormatZp(o1.Value)}));");
                lastImmA = null;
                return;
            case 0x0D when o1.HasValue && o2.HasValue: // ORA abs
                statements.Add($"a = (byte)(a | peek({FormatAddr(Abs(o1, o2))}));");
                lastImmA = null;
                return;
            case 0x1D when o1.HasValue && o2.HasValue: // ORA abs,X
                statements.Add($"a = (byte)(a | peek({IndexAddr(FormatAddr(Abs(o1, o2)), "x")}));");
                lastImmA = null;
                return;
            case 0x49 when o1.HasValue: // EOR #
                statements.Add($"a ^= {FormatByte(o1.Value)};");
                lastImmA = null;
                return;
            case 0x4D when o1.HasValue && o2.HasValue: // EOR abs
                statements.Add($"a = (byte)(a ^ peek({FormatAddr(Abs(o1, o2))}));");
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
            case 0x2E when o1.HasValue && o2.HasValue: // ROL abs
            {
                string a = FormatAddr(Abs(o1, o2));
                statements.Add($"{{ bool _c = carry; byte _m = peek({a}); carry = (_m & 0x80) != 0; poke({a}, (byte)((_m << 1) | (_c ? 1 : 0))); }}");
                return;
            }
            case 0x6A: // ROR A
                statements.Add("{ bool _c = carry; carry = (a & 1) != 0; a = (byte)((a >> 1) | (_c ? 0x80 : 0)); }");
                lastImmA = null;
                return;
            case 0x66 when o1.HasValue: // ROR zp
                statements.Add($"{{ bool _c = carry; byte _m = peek({FormatZp(o1.Value)}); carry = (_m & 1) != 0; poke({FormatZp(o1.Value)}, (byte)((_m >> 1) | (_c ? 0x80 : 0))); }}");
                return;
            case 0x6E when o1.HasValue && o2.HasValue: // ROR abs
            {
                string a = FormatAddr(Abs(o1, o2));
                statements.Add($"{{ bool _c = carry; byte _m = peek({a}); carry = (_m & 1) != 0; poke({a}, (byte)((_m >> 1) | (_c ? 0x80 : 0))); }}");
                return;
            }
            case 0x0E when o1.HasValue && o2.HasValue: // ASL abs
            {
                string a = FormatAddr(Abs(o1, o2));
                statements.Add($"{{ byte _m = peek({a}); carry = (_m & 0x80) != 0; poke({a}, (byte)(_m << 1)); }}");
                return;
            }
            case 0x4E when o1.HasValue && o2.HasValue: // LSR abs
            {
                string a = FormatAddr(Abs(o1, o2));
                statements.Add($"{{ byte _m = peek({a}); carry = (_m & 1) != 0; poke({a}, (byte)(_m >> 1)); }}");
                return;
            }
            case 0x48: // PHA
                statements.Add("pha();");
                return;
            case 0x68: // PLA
                statements.Add("a = pla();");
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
            // SoftCpu recognizes these comment forms; avoid depending on NESLib.cld/sed for compile.
            case 0xD8: statements.Add("// CLD"); return;
            case 0xF8: statements.Add("// SED"); return;
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
                else if (TryFindRoutine(target, routines, out brName))
                    statements.Add($"if ({cond}) {{ {brName}(); return; }}");
                else
                    // Unmapped / below-$8000 / data-as-code: never emit a goto to a missing label.
                    statements.Add($"// if ({cond}) goto {LabelName(target)}; // target ${target:X4} outside recovered body");
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

    /// <summary>
    /// Absolute addresses touched often enough to deserve a named <c>const ushort</c>.
    /// Immediate byte values are intentionally not promoted (too ambiguous).
    /// </summary>
    const int HotAddressMinAccesses = 2;

    static Dictionary<ushort, string> PromoteHotAddresses(
        List<List<string>> bodies,
        HashSet<ushort> padAddrs,
        HashSet<ushort> maskShadows,
        HashSet<ushort> ctrlShadows,
        HashSet<ushort> scrollBoth,
        HashSet<ushort> scrollX,
        HashSet<ushort> scrollY,
        HashSet<byte> ptrLos,
        Dictionary<ushort, string> singleScope)
    {
        var counts = new Dictionary<ushort, int>();
        foreach (var body in bodies)
        {
            foreach (string raw in body)
            {
                foreach (ushort addr in ExtractUshortLiterals(raw))
                    counts[addr] = counts.GetValueOrDefault(addr) + 1;
            }
        }

        var map = new Dictionary<ushort, string>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (addr, count) in counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
        {
            // Always name known role RAM; other addresses need enough traffic.
            bool role = padAddrs.Contains(addr)
                || maskShadows.Contains(addr)
                || ctrlShadows.Contains(addr)
                || scrollBoth.Contains(addr)
                || scrollX.Contains(addr)
                || scrollY.Contains(addr)
                || (addr <= 0xFF && (ptrLos.Contains((byte)addr) || ptrLos.Contains((byte)(addr - 1))));
            if (count < HotAddressMinAccesses && !role)
                continue;
            // Skip MMIO already emitted as NESLib names (never appear as (ushort)0x…).
            if (addr >= 0x2000 && addr < 0x4020)
                continue;

            string name = NameHotAddress(
                addr, padAddrs, maskShadows, ctrlShadows, scrollBoth, scrollX, scrollY, ptrLos, singleScope);
            if (!usedNames.Add(name))
                name = FallbackHotName(addr);
            usedNames.Add(name);
            map[addr] = name;
        }

        if (map.Count == 0)
            return map;

        // Rewrite whole (ushort)0x… tokens only — never prefix-match 0x00 inside 0x00FE.
        foreach (var body in bodies)
        {
            for (int i = 0; i < body.Count; i++)
                body[i] = RewriteUshortLiterals(body[i], map);
        }

        return map;
    }

    /// <summary>
    /// Promote CPU RAM names to <c>byte</c> variables; keep ROM / indexed bases as <c>const ushort</c>.
    /// </summary>
    static (Dictionary<ushort, string> RamVars, Dictionary<ushort, string> AddrConsts) SplitHotRamIntoVariables(
        List<List<string>> bodies,
        Dictionary<ushort, string> hot)
    {
        var indexedBases = new HashSet<string>(StringComparer.Ordinal);
        var indexedBasePattern = new Regex(
            @"\(ushort\)\((\w+)\s*\+\s*(?:x|y)\)|(\w+)\s*\+\s*(?:x|y)",
            RegexOptions.Compiled);
        foreach (var body in bodies)
        {
            foreach (string line in body)
            {
                foreach (Match m in indexedBasePattern.Matches(line))
                {
                    string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (name.Length > 0)
                        indexedBases.Add(name);
                }
            }
        }

        var ramVars = new Dictionary<ushort, string>();
        var addrConsts = new Dictionary<ushort, string>();
        foreach (var (addr, name) in hot)
        {
            // CPU RAM only (ZP + $0100–$07FF and mirrors below $2000). ROM/mapper stay address consts.
            if (addr < 0x2000 && !indexedBases.Contains(name))
                ramVars[addr] = name;
            else
                addrConsts[addr] = name;
        }
        return (ramVars, addrConsts);
    }

    /// <summary>
    /// <c>peek(zp_FE)</c>/<c>poke(zp_FE, v)</c> → <c>zp_FE</c>/<c>zp_FE = v</c> for promoted RAM vars.
    /// Indexed and ROM forms keep peek/poke.
    /// </summary>
    static void EnsureRamVar(
        Dictionary<ushort, string> ramVars,
        Dictionary<ushort, string> addrConsts,
        ushort addr,
        string preferredName)
    {
        if (ramVars.ContainsKey(addr))
            return;
        if (addrConsts.Remove(addr, out _))
        {
            ramVars[addr] = preferredName;
            return;
        }
        ramVars[addr] = preferredName;
    }

    /// <summary>Rewrite an already-promoted name in bodies + maps (e.g. zp_00 → vram_ptr_lo).</summary>
    static void RenameRamVar(
        List<List<string>> bodies,
        Dictionary<ushort, string> ramVars,
        ushort addr,
        string newName)
    {
        string old = ramVars.TryGetValue(addr, out string? existing) ? existing : FallbackHotName(addr);
        ramVars[addr] = newName;
        if (old == newName)
            return;
        var ident = new Regex(@"\b" + Regex.Escape(old) + @"\b", RegexOptions.Compiled);
        foreach (var body in bodies)
        {
            for (int i = 0; i < body.Count; i++)
                body[i] = ident.Replace(body[i], newName);
        }
    }

    static void RewritePeekPokeToVariables(
        List<List<string>> bodies,
        IReadOnlyDictionary<ushort, string> ramVars)
    {
        if (ramVars.Count == 0)
            return;
        var names = new HashSet<string>(ramVars.Values, StringComparer.Ordinal);
        foreach (var body in bodies)
        {
            for (int i = 0; i < body.Count; i++)
                body[i] = RewritePeekPokeLine(body[i], names);
        }
    }

    static readonly Regex PeekNamed = new(
        @"peek\((\w+)\)",
        RegexOptions.Compiled);

    static string RewritePeekPokeLine(string line, HashSet<string> varNames)
    {
        // poke first so poke(v,(byte)(peek(v)+1)) → v = (byte)(peek(v)+1) → v = (byte)(v+1)
        // Must balance parentheses — nested (byte)((…)) breaks non-greedy regex.
        string s = RewritePokesBalanced(line, varNames);
        s = PeekNamed.Replace(s, m =>
        {
            string name = m.Groups[1].Value;
            return varNames.Contains(name) ? name : m.Value;
        });
        return s;
    }

    static string RewritePokesBalanced(string line, HashSet<string> varNames)
    {
        var sb = new StringBuilder(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            if (!StartsWithAt(line, i, "poke("))
            {
                sb.Append(line[i]);
                i++;
                continue;
            }
            int nameStart = i + "poke(".Length;
            int nameEnd = nameStart;
            while (nameEnd < line.Length && (char.IsLetterOrDigit(line[nameEnd]) || line[nameEnd] == '_'))
                nameEnd++;
            if (nameEnd >= line.Length || line[nameEnd] != ',')
            {
                sb.Append(line[i]);
                i++;
                continue;
            }
            string name = line[nameStart..nameEnd];
            int valStart = nameEnd + 1;
            while (valStart < line.Length && char.IsWhiteSpace(line[valStart]))
                valStart++;
            if (!TryReadBalancedArg(line, valStart, out string val, out int afterVal)
                || afterVal >= line.Length || line[afterVal] != ')')
            {
                sb.Append(line[i]);
                i++;
                continue;
            }
            if (varNames.Contains(name))
                sb.Append(name).Append(" = ").Append(val);
            else
                sb.Append(line, i, afterVal + 1 - i);
            i = afterVal + 1;
        }
        return sb.ToString();
    }

    /// <summary>Read one C#-ish argument that may contain nested <c>()</c>.</summary>
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

    static readonly Regex UshortLiteralPattern = new(
        @"\(ushort\)0x([0-9A-Fa-f]{1,4})(?![0-9A-Fa-f])",
        RegexOptions.Compiled);

    static string RewriteUshortLiterals(string line, IReadOnlyDictionary<ushort, string> map) =>
        UshortLiteralPattern.Replace(line, m =>
        {
            if (!ushort.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort addr))
                return m.Value;
            return map.TryGetValue(addr, out string? name) ? name : m.Value;
        });

    static string FallbackHotName(ushort addr) =>
        addr <= 0xFF ? $"zp_{addr:X2}" : addr < 0x8000 ? $"ram_{addr:X4}" : $"rom_{addr:X4}";

    static string NameHotAddress(
        ushort addr,
        HashSet<ushort> padAddrs,
        HashSet<ushort> maskShadows,
        HashSet<ushort> ctrlShadows,
        HashSet<ushort> scrollBoth,
        HashSet<ushort> scrollX,
        HashSet<ushort> scrollY,
        HashSet<byte> ptrLos,
        Dictionary<ushort, string> singleScope)
    {
        if (padAddrs.Contains(addr))
            return addr <= 0xFF ? $"pad_{addr:X2}" : $"pad_{addr:X4}";
        if (maskShadows.Contains(addr))
            return "ppu_mask_shadow";
        if (ctrlShadows.Contains(addr))
            return "ppu_ctrl_shadow";
        if (scrollBoth.Contains(addr))
            return "scroll";
        if (scrollX.Contains(addr))
            return "scroll_x";
        if (scrollY.Contains(addr))
            return "scroll_y";

        if (addr <= 0xFF)
        {
            byte zp = (byte)addr;
            if (ptrLos.Contains(zp))
                return zp == 0 ? "vram_ptr_lo" : $"ptr_{zp:X2}_lo";
            if (zp > 0 && ptrLos.Contains((byte)(zp - 1)))
                return zp == 1 ? "vram_ptr_hi" : $"ptr_{(byte)(zp - 1):X2}_hi";
        }

        if (singleScope.TryGetValue(addr, out string? scope))
        {
            string hex = addr <= 0xFF ? $"{addr:X2}" : $"{addr:X4}";
            return $"{scope}_{hex}";
        }

        return FallbackHotName(addr);
    }

    static IEnumerable<ushort> ExtractUshortLiterals(string line)
    {
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
            if (hex.Length is >= 1 and <= 4
                && ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort addr))
                yield return addr;
            i = hexEnd;
        }
    }

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
                    if (!TryParseGoto(result[j].TrimStart(), out string t, out string? c) || t != label)
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
                    if (TryParseGoto(result[j].TrimStart(), out string t, out _) && t == label)
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
                    if (TryParseGoto(result[j].TrimStart(), out string t, out _) && t == label)
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
            else if (body.Count == 1 && IsSafeOneLinerBody(body[0])
                && !body[0].StartsWith("if ", StringComparison.Ordinal)
                && !body[0].StartsWith("while ", StringComparison.Ordinal)
                && !body[0].StartsWith("do ", StringComparison.Ordinal))
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
            if (TryGetLabelName(stmts[j].TrimStart(), out _))
                return false;
            if (TryParseGoto(stmts[j].TrimStart(), out _, out _))
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
                if (TryParseGoto(stmts[j].TrimStart(), out string t, out _) && t == name)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Countdown loops: trailing <c>x--/y--</c> + BNE → <c>do { … } while (--reg != 0)</c>.
    /// Handles both raw goto form and already-structured <c>do/while (!zero)</c>.
    /// </summary>
    static void StructureCountdownLoops(List<string> statements)
    {
        RewriteStructuredCountdownDos(statements);

        bool changed;
        do
        {
            changed = false;
            var labels = IndexLabels(statements);
            foreach (var (label, li) in labels.OrderByDescending(kv => kv.Value))
            {
                // Find back-edge: x--|y--; if (!zero|/!negative) goto L;
                for (int j = li + 1; j < statements.Count - 1; j++)
                {
                    string dec = statements[j].TrimStart();
                    if (dec is not ("x--;" or "y--;"))
                        continue;
                    if (!TryParseGoto(statements[j + 1].TrimStart(), out string t, out string? cond)
                        || t != label
                        || cond is not ("!zero" or "!negative"))
                        continue;

                    // No other gotos/labels between label and dec
                    bool ok = true;
                    for (int k = li + 1; k < j; k++)
                    {
                        string s = statements[k].TrimStart();
                        if (TryGetLabelName(s, out _) || TryParseGoto(s, out _, out _))
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (!ok)
                        continue;

                    string reg = dec.StartsWith('x') ? "x" : "y";
                    var body = SliceArm(statements, li + 1, j); // excludes dec
                    string indent = LeadingIndent(statements[li]);

                    var replacement = new List<string>();
                    if (cond == "!zero")
                    {
                        // do { body; } while (--reg != 0);
                        replacement.Add(indent + "do");
                        replacement.Add(indent + "{");
                        foreach (string line in body)
                            replacement.Add(indent + "    " + line.TrimStart());
                        replacement.Add(indent + "}");
                        replacement.Add(indent + $"while (--{reg} != 0);");
                    }
                    else
                    {
                        // BPL form: do { body; reg--; } while (!negative);
                        replacement.Add(indent + "do");
                        replacement.Add(indent + "{");
                        foreach (string line in body)
                            replacement.Add(indent + "    " + line.TrimStart());
                        replacement.Add(indent + "    " + dec);
                        replacement.Add(indent + "}");
                        replacement.Add(indent + "while (!negative);");
                    }

                    statements.RemoveRange(li, j + 2 - li);
                    statements.InsertRange(li, replacement);
                    changed = true;
                    break;
                }
                if (changed)
                    break;
            }
        } while (changed);
    }

    /// <summary>
    /// <c>do { …; x--; } while (!zero);</c> → <c>do { … } while (--x != 0);</c>
    /// (StructureLoops already folded the goto form).
    /// </summary>
    static void RewriteStructuredCountdownDos(List<string> statements)
    {
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < statements.Count; i++)
            {
                if (statements[i].TrimStart() != "do")
                    continue;
                if (i + 1 >= statements.Count || statements[i + 1].TrimStart() != "{")
                    continue;
                if (!TryFindMatchingBrace(statements, i + 1, out int closeIdx))
                    continue;
                if (closeIdx + 1 >= statements.Count)
                    continue;
                string whileLine = statements[closeIdx + 1].TrimStart();
                if (whileLine is not ("while (!zero);" or "while(!zero);"))
                    continue;

                // Last non-empty body stmt must be x-- or y--
                int lastBody = -1;
                for (int k = closeIdx - 1; k > i + 1; k--)
                {
                    if (!string.IsNullOrWhiteSpace(statements[k]))
                    {
                        lastBody = k;
                        break;
                    }
                }
                if (lastBody < 0)
                    continue;
                string dec = statements[lastBody].TrimStart();
                if (dec is not ("x--;" or "y--;"))
                    continue;

                // Don't rewrite if body has nested do/while/if that would confuse SoftCpu nesting
                // beyond one level of already-structured content — nested braces already matched.
                string reg = dec.StartsWith('x') ? "x" : "y";
                string indent = LeadingIndent(statements[i]);
                var body = new List<string>();
                for (int k = i + 2; k < lastBody; k++)
                    body.Add(statements[k]);

                var replacement = new List<string>
                {
                    indent + "do",
                    indent + "{",
                };
                foreach (string line in body)
                    replacement.Add(indent + "    " + line.TrimStart());
                replacement.Add(indent + "}");
                replacement.Add(indent + $"while (--{reg} != 0);");

                statements.RemoveRange(i, closeIdx + 2 - i);
                statements.InsertRange(i, replacement);
                changed = true;
                break;
            }

            // One-liner: do { …; x--; } while (!zero);  or  do { x--; } while (!zero);
            for (int i = 0; i < statements.Count; i++)
            {
                string line = statements[i].TrimStart();
                var m = Regex.Match(line,
                    @"^do\s*\{\s*(.*?)\s*(x|y)--;\s*\}\s*while\s*\(\s*!zero\s*\)\s*;\s*$");
                if (!m.Success)
                    continue;
                string prefix = m.Groups[1].Value.Trim();
                string reg = m.Groups[2].Value;
                string indent = LeadingIndent(statements[i]);
                if (string.IsNullOrEmpty(prefix))
                    statements[i] = indent + $"do {{ }} while (--{reg} != 0);";
                else
                {
                    if (!prefix.EndsWith(';'))
                        prefix += ";";
                    statements[i] = indent + $"do {{ {prefix} }} while (--{reg} != 0);";
                }
                changed = true;
                break;
            }
        } while (changed);
    }

    static bool TryFindMatchingBrace(List<string> statements, int openIdx, out int closeIdx)
    {
        closeIdx = -1;
        int depth = 0;
        for (int i = openIdx; i < statements.Count; i++)
        {
            string t = statements[i].TrimStart();
            if (t == "{")
                depth++;
            else if (t == "}")
            {
                depth--;
                if (depth == 0)
                {
                    closeIdx = i;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Prefer <c>for</c> when a do/while is a clear counted loop:
    /// <c>x = 0; do { …; x++; } while (x != N);</c> → <c>for (x = 0; x != N; x++)</c>,
    /// <c>y = N; do { … } while (--y != 0);</c> → <c>for (y = N; y != 0; y--)</c>.
    /// </summary>
    static void StructureForLoops(List<string> statements)
    {
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < statements.Count; i++)
            {
                if (TryRewriteCountUpDoWhileToFor(statements, i))
                {
                    changed = true;
                    break;
                }
                if (TryRewriteCountdownDoWhileToFor(statements, i))
                {
                    changed = true;
                    break;
                }
                if (TryRewriteMemCountdownDoWhileToFor(statements, i))
                {
                    changed = true;
                    break;
                }
            }
        } while (changed);
    }

    static readonly Regex RegImmAssign = new(
        @"^(a|x|y)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary>Loop index init: imm or simple register transfer (<c>y = a</c>).</summary>
    static readonly Regex RegLoopInitAssign = new(
        @"^(x|y)\s*=\s*(0x[0-9A-Fa-f]+|\d+|a|x|y)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex MemImmAssign = new(
        @"^([A-Za-z_]\w*)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex WhileRegCompare = new(
        @"^while\s*\(\s*(a|x|y)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex WhileDecReg = new(
        @"^while\s*\(\s*--(x|y)\s*!=\s*0\s*\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex WhileDecMem = new(
        @"^while\s*\(\s*--([A-Za-z_]\w*)\s*!=\s*0\s*\)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Find <c>reg = …</c> above <paramref name="doAt"/>, allowing intervening lines that
    /// do not assign <paramref name="reg"/> (other inits, peeks, calls).
    /// </summary>
    static bool TryFindLoopInitAbove(
        List<string> statements, int doAt, string reg, bool immOnly,
        out int initAt, out string initExpr)
    {
        initAt = -1;
        initExpr = "";
        const int lookback = 16;
        for (int j = doAt - 1; j >= 0 && j >= doAt - lookback; j--)
        {
            string t = statements[j].TrimStart();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal) || t is "{" or "}")
                continue;
            if (TryGetLabelName(t, out _))
                break; // don't cross labels into another basic block

            var m = immOnly ? RegImmAssign.Match(t) : RegLoopInitAssign.Match(t);
            if (m.Success && m.Groups[1].Value == reg)
            {
                // Ensure nothing between init and do assigns reg.
                if (BodyAssignsReg(statements, j + 1, doAt, reg))
                    return false;
                initAt = j;
                initExpr = $"{reg} = {m.Groups[2].Value}";
                return true;
            }

            // Stop at hard control-flow; one-liner ifs that don't touch reg are OK.
            if (LineStopsLoopInitLookback(t, reg))
                break;
        }
        return false;
    }

    static bool TryFindMemInitAbove(
        List<string> statements, int doAt, string name,
        out int initAt, out string immText)
    {
        initAt = -1;
        immText = "";
        const int lookback = 16;
        for (int j = doAt - 1; j >= 0 && j >= doAt - lookback; j--)
        {
            string t = statements[j].TrimStart();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal) || t is "{" or "}")
                continue;
            if (TryGetLabelName(t, out _))
                break;

            var m = MemImmAssign.Match(t);
            if (m.Success && m.Groups[1].Value == name && name is not ("a" or "x" or "y"))
            {
                if (BodyAssignsReg(statements, j + 1, doAt, name))
                    return false;
                initAt = j;
                immText = m.Groups[2].Value;
                return true;
            }

            if (LineStopsLoopInitLookback(t, name))
                break;
        }
        return false;
    }

    static bool LineStopsLoopInitLookback(string t, string reg)
    {
        if (t.StartsWith("for ", StringComparison.Ordinal)
            || t.StartsWith("do", StringComparison.Ordinal)
            || t.StartsWith("while ", StringComparison.Ordinal)
            || t.StartsWith("return", StringComparison.Ordinal)
            || t.StartsWith("goto ", StringComparison.Ordinal))
            return true;

        if (!t.StartsWith("if ", StringComparison.Ordinal))
            return false;

        // One-liner if { … } that doesn't assign reg — keep scanning.
        // Use token checks (not Contains "y =") so "carry =" doesn't look like "y =".
        if (t.Contains('{', StringComparison.Ordinal) && t.Contains('}', StringComparison.Ordinal))
            return LineAssignsRegToken(t, reg);

        // Multi-line if header — don't look through the block.
        return true;
    }

    static bool LineAssignsRegToken(string t, string reg)
    {
        // Match "reg =", "reg++", "reg--" as whole identifiers (not the "y" in "carry").
        for (int i = 0; i <= t.Length - reg.Length; i++)
        {
            if (!t.AsSpan(i, reg.Length).Equals(reg, StringComparison.Ordinal))
                continue;
            if (i > 0 && (char.IsLetterOrDigit(t[i - 1]) || t[i - 1] == '_'))
                continue;
            int after = i + reg.Length;
            if (after >= t.Length)
                continue;
            if (after + 1 < t.Length && t[after] == '+' && t[after + 1] == '+')
                return true;
            if (after + 1 < t.Length && t[after] == '-' && t[after + 1] == '-')
                return true;
            int k = after;
            while (k < t.Length && char.IsWhiteSpace(t[k]))
                k++;
            if (k < t.Length && t[k] == '=')
                return true;
        }
        return false;
    }

    /// <summary>
    /// Replace a do/while range with a for, deleting a prior init line when present.
    /// </summary>
    static void ReplaceDoWhileWithFor(
        List<string> statements, int doAt, int closeIdx, int initAt, List<string> forBlock)
    {
        statements.RemoveRange(doAt, closeIdx + 2 - doAt);
        statements.InsertRange(doAt, forBlock);
        if (initAt >= 0 && initAt < doAt)
            statements.RemoveAt(initAt);
    }

    /// <summary>
    /// <c>[reg = imm;] do { …; reg++|--|; } while (reg op bound);</c> → for.
    /// </summary>
    static bool TryRewriteCountUpDoWhileToFor(List<string> statements, int doAt)
    {
        if (statements[doAt].TrimStart() != "do")
            return false;
        if (doAt + 1 >= statements.Count || statements[doAt + 1].TrimStart() != "{")
            return false;
        if (!TryFindMatchingBrace(statements, doAt + 1, out int closeIdx))
            return false;
        if (closeIdx + 1 >= statements.Count)
            return false;

        var whileMatch = WhileRegCompare.Match(statements[closeIdx + 1].TrimStart());
        if (!whileMatch.Success)
            return false;
        string reg = whileMatch.Groups[1].Value;
        string op = whileMatch.Groups[2].Value;
        string bound = whileMatch.Groups[3].Value;
        if (reg is not ("x" or "y"))
            return false;

        int lastBody = LastNonEmptyIndex(statements, doAt + 2, closeIdx);
        if (lastBody < 0)
            return false;
        string stepLine = statements[lastBody].TrimStart();
        string? step = stepLine switch
        {
            _ when stepLine == $"{reg}++;" => $"{reg}++",
            _ when stepLine == $"{reg}--;" => $"{reg}--",
            _ => null,
        };
        if (step is null)
            return false;

        // Body (excluding step) must not assign the loop register.
        if (BodyAssignsReg(statements, doAt + 2, lastBody, reg))
            return false;

        if (!TryFindLoopInitAbove(statements, doAt, reg, immOnly: false, out int initAt, out string init))
            return false;

        string indent = LeadingIndent(statements[initAt]);
        var body = new List<string>();
        for (int k = doAt + 2; k < lastBody; k++)
            body.Add(statements[k]);

        var replacement = new List<string>();
        if (body.Count == 0)
        {
            replacement.Add(indent + $"for ({init}; {reg} {op} {bound}; {step}) ;");
        }
        else
        {
            replacement.Add(indent + $"for ({init}; {reg} {op} {bound}; {step})");
            replacement.Add(indent + "{");
            foreach (string line in body)
                replacement.Add(indent + "    " + line.TrimStart());
            replacement.Add(indent + "}");
        }

        ReplaceDoWhileWithFor(statements, doAt, closeIdx, initAt, replacement);
        return true;
    }

    /// <summary>
    /// <c>reg = N; do { … } while (--reg != 0);</c> → <c>for (reg = N; reg != 0; reg--)</c>.
    /// </summary>
    static bool TryRewriteCountdownDoWhileToFor(List<string> statements, int doAt)
    {
        // Multi-line do { } while (--reg != 0);
        if (statements[doAt].TrimStart() == "do"
            && doAt + 1 < statements.Count
            && statements[doAt + 1].TrimStart() == "{"
            && TryFindMatchingBrace(statements, doAt + 1, out int closeIdx)
            && closeIdx + 1 < statements.Count)
        {
            var whileMatch = WhileDecReg.Match(statements[closeIdx + 1].TrimStart());
            if (!whileMatch.Success)
                return false;
            string reg = whileMatch.Groups[1].Value;
            if (!TryFindLoopInitAbove(statements, doAt, reg, immOnly: true, out int initAt, out string init))
                return false;
            string imm = init[(reg.Length + 3)..]; // after "reg = "
            if (!TryParseByteLiteral(imm, out byte n) || n == 0)
                return false;
            if (BodyAssignsReg(statements, doAt + 2, closeIdx, reg))
                return false;

            string indent = LeadingIndent(statements[initAt]);
            var body = new List<string>();
            for (int k = doAt + 2; k < closeIdx; k++)
                body.Add(statements[k]);

            var replacement = new List<string>();
            if (body.Count == 0 || body.TrueForAll(string.IsNullOrWhiteSpace))
                replacement.Add(indent + $"for ({init}; {reg} != 0; {reg}--) ;");
            else
            {
                replacement.Add(indent + $"for ({init}; {reg} != 0; {reg}--)");
                replacement.Add(indent + "{");
                foreach (string line in body)
                    replacement.Add(indent + "    " + line.TrimStart());
                replacement.Add(indent + "}");
            }

            ReplaceDoWhileWithFor(statements, doAt, closeIdx, initAt, replacement);
            return true;
        }

        // One-liner: do { … } while (--reg != 0);  or  do { } while (--reg != 0);
        string oneLine = statements[doAt].TrimStart();
        var one = Regex.Match(oneLine,
            @"^do\s*\{\s*(.*?)\s*\}\s*while\s*\(\s*--(x|y)\s*!=\s*0\s*\)\s*;\s*$");
        if (!one.Success)
            return false;
        string regOne = one.Groups[2].Value;
        if (!TryFindLoopInitAbove(statements, doAt, regOne, immOnly: true, out int initOneAt, out string initOne))
            return false;
        string immOne = initOne[(regOne.Length + 3)..];
        if (!TryParseByteLiteral(immOne, out byte nOne) || nOne == 0)
            return false;

        string indentOne = LeadingIndent(statements[initOneAt]);
        string bodyStmt = one.Groups[1].Value.Trim();
        var replacementOne = new List<string>();
        if (string.IsNullOrEmpty(bodyStmt))
            replacementOne.Add(indentOne + $"for ({initOne}; {regOne} != 0; {regOne}--) ;");
        else
        {
            if (!bodyStmt.EndsWith(';'))
                bodyStmt += ";";
            replacementOne.Add(indentOne + $"for ({initOne}; {regOne} != 0; {regOne}--)");
            replacementOne.Add(indentOne + "{");
            replacementOne.Add(indentOne + "    " + bodyStmt);
            replacementOne.Add(indentOne + "}");
        }
        // One-liner: doAt is the only do/while line (closeIdx := doAt for removal span of 1).
        statements.RemoveAt(doAt);
        statements.InsertRange(doAt, replacementOne);
        if (initOneAt >= 0 && initOneAt < doAt)
            statements.RemoveAt(initOneAt);
        return true;
    }

    /// <summary>
    /// <c>do { …; name--; } while (!zero);</c> → <c>do { … } while (--name != 0);</c>
    /// </summary>
    static void RewriteStructuredMemCountdownDos(List<string> statements)
    {
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < statements.Count; i++)
            {
                if (statements[i].TrimStart() != "do")
                    continue;
                if (i + 1 >= statements.Count || statements[i + 1].TrimStart() != "{")
                    continue;
                if (!TryFindMatchingBrace(statements, i + 1, out int closeIdx))
                    continue;
                if (closeIdx + 1 >= statements.Count)
                    continue;
                if (statements[closeIdx + 1].TrimStart() is not ("while (!zero);" or "while(!zero);"))
                    continue;

                int lastBody = LastNonEmptyIndex(statements, i + 2, closeIdx);
                if (lastBody < 0)
                    continue;
                string last = statements[lastBody].TrimStart();
                string name;
                var decAssign = MemIncDecAssign.Match(last);
                if (decAssign.Success && decAssign.Groups[2].Value == "-")
                    name = decAssign.Groups[1].Value;
                else if (last.Length > 3 && last.EndsWith("--;", StringComparison.Ordinal))
                    name = last[..^3];
                else
                    continue;
                if (name.Length == 0 || name is "a" or "x" or "y"
                    || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
                    continue;
                if (BodyAssignsReg(statements, i + 2, lastBody, name))
                    continue;

                string indent = LeadingIndent(statements[i]);
                var body = new List<string>();
                for (int k = i + 2; k < lastBody; k++)
                    body.Add(statements[k]);

                var replacement = new List<string>
                {
                    indent + "do",
                    indent + "{",
                };
                foreach (string line in body)
                    replacement.Add(indent + "    " + line.TrimStart());
                replacement.Add(indent + "}");
                replacement.Add(indent + $"while (--{name} != 0);");

                statements.RemoveRange(i, closeIdx + 2 - i);
                statements.InsertRange(i, replacement);
                changed = true;
                break;
            }
        } while (changed);
    }

    /// <summary>
    /// <c>name = N; do { … } while (--name != 0);</c> → <c>for (name = N; name != 0; name--)</c>.
    /// </summary>
    static bool TryRewriteMemCountdownDoWhileToFor(List<string> statements, int doAt)
    {
        if (statements[doAt].TrimStart() != "do")
            return false;
        if (doAt + 1 >= statements.Count || statements[doAt + 1].TrimStart() != "{")
            return false;
        if (!TryFindMatchingBrace(statements, doAt + 1, out int closeIdx))
            return false;
        if (closeIdx + 1 >= statements.Count)
            return false;
        var whileMatch = WhileDecMem.Match(statements[closeIdx + 1].TrimStart());
        if (!whileMatch.Success)
            return false;
        string name = whileMatch.Groups[1].Value;
        if (name is "a" or "x" or "y")
            return false;
        if (!TryFindMemInitAbove(statements, doAt, name, out int initAt, out string imm))
            return false;
        if (!TryParseByteLiteral(imm, out byte n) || n == 0)
            return false;
        if (BodyAssignsReg(statements, doAt + 2, closeIdx, name))
            return false;

        string indent = LeadingIndent(statements[initAt]);
        var body = new List<string>();
        for (int k = doAt + 2; k < closeIdx; k++)
            body.Add(statements[k]);

        var replacement = new List<string>();
        if (body.Count == 0 || body.TrueForAll(string.IsNullOrWhiteSpace))
            replacement.Add(indent + $"for ({name} = {imm}; {name} != 0; {name}--) ;");
        else
        {
            replacement.Add(indent + $"for ({name} = {imm}; {name} != 0; {name}--)");
            replacement.Add(indent + "{");
            foreach (string line in body)
                replacement.Add(indent + "    " + line.TrimStart());
            replacement.Add(indent + "}");
        }

        ReplaceDoWhileWithFor(statements, doAt, closeIdx, initAt, replacement);
        return true;
    }

    static int LastNonEmptyIndex(List<string> statements, int startInclusive, int endExclusive)
    {
        for (int k = endExclusive - 1; k >= startInclusive; k--)
        {
            if (!string.IsNullOrWhiteSpace(statements[k]))
                return k;
        }
        return -1;
    }

    static bool BodyAssignsReg(List<string> statements, int startInclusive, int endExclusive, string reg)
    {
        for (int k = startInclusive; k < endExclusive; k++)
        {
            string t = statements[k].TrimStart();
            if (LineAssignsRegToken(t, reg)
                || t.Contains($"({reg} + 1)", StringComparison.Ordinal)
                || t.Contains($"({reg} - 1)", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>do { …; zero = y == N; carry = y &gt;= N; } while (!carry);</c>
    /// → <c>do { … } while (y &lt; N);</c> (SoftCpu: CPy/CPx + BCC).
    /// </summary>
    static void StructureCompareBoundLoops(List<string> statements)
    {
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < statements.Count; i++)
            {
                if (statements[i].TrimStart() != "do")
                    continue;
                if (i + 1 >= statements.Count || statements[i + 1].TrimStart() != "{")
                    continue;
                if (!TryFindMatchingBrace(statements, i + 1, out int closeIdx))
                    continue;
                if (closeIdx + 1 >= statements.Count)
                    continue;
                var whileMatch = Regex.Match(statements[closeIdx + 1].TrimStart(),
                    @"^while\s*\(\s*(!)?(zero|carry)\s*\)\s*;\s*$");
                if (!whileMatch.Success)
                    continue;

                int lastBody = -1;
                for (int k = closeIdx - 1; k > i + 1; k--)
                {
                    if (!string.IsNullOrWhiteSpace(statements[k]))
                    {
                        lastBody = k;
                        break;
                    }
                }
                if (lastBody < 0)
                    continue;
                var cmp = CmpFlagTriple.Match(statements[lastBody].TrimStart());
                if (!cmp.Success)
                    continue;

                string reg = cmp.Groups[1].Value;
                string imm = cmp.Groups[2].Value;
                bool neg = whileMatch.Groups[1].Success;
                string flag = whileMatch.Groups[2].Value;
                string? op = (flag, neg) switch
                {
                    ("carry", true) => "<",   // while (!carry) after CMP ≥ → keep looping while reg < imm
                    ("carry", false) => ">=",
                    ("zero", false) => "==",
                    ("zero", true) => "!=",
                    _ => null,
                };
                if (op is null)
                    continue;

                string indent = LeadingIndent(statements[i]);
                statements.RemoveAt(lastBody);
                // lastBody < closeIdx ⇒ while line slides from closeIdx+1 → closeIdx.
                statements[closeIdx] = indent + $"while ({reg} {op} {imm});";
                changed = true;
                break;
            }
        } while (changed);
    }

    /// <summary>
    /// <c>carry = (a &amp; bit) != 0; a &lt;&lt;=|/&gt;&gt;= 1; if (carry) …</c> when both arms
    /// overwrite <c>a</c> before reading it → pure bit test (SoftCpu: AND #imm / Bxx).
    /// </summary>
    static void SugarDiscardedShiftBitTests(List<string> statements)
    {
        const string lsr = "carry = (a & 1) != 0; a >>= 1;";
        const string asl = "carry = (a & 0x80) != 0; a <<= 1;";
        for (int i = 0; i < statements.Count - 1; i++)
        {
            string t = statements[i].TrimStart();
            string mask;
            if (t == lsr) mask = "1";
            else if (t == asl) mask = "0x80";
            else continue;

            string branch = statements[i + 1].TrimStart();
            var gotoBr = IfFlagGoto.Match(branch);
            var one = Regex.Match(branch,
                @"^if\s*\(\s*(!)?\s*carry\s*\)\s*\{\s*(.+)\s*\}\s*$");
            var hdr = Regex.Match(branch, @"^if\s*\(\s*(!)?\s*carry\s*\)\s*$");
            var ret = Regex.Match(branch, @"^if\s*\(\s*(!)?\s*carry\s*\)\s*return\s*;\s*$");

            bool neg;
            string rewritten;
            string indent = LeadingIndent(statements[i]);

            if (gotoBr.Success && gotoBr.Groups[2].Value == "carry")
            {
                neg = gotoBr.Groups[1].Success;
                if (!ShiftResultDiscardedAfterGoto(statements, i + 2, gotoBr.Groups[3].Value))
                    continue;
                string op = neg ? "==" : "!=";
                rewritten = indent + $"if ((a & {mask}) {op} 0) goto {gotoBr.Groups[3].Value};";
            }
            else if (ret.Success)
            {
                neg = ret.Groups[1].Success;
                // return ends the routine — shifted a is discarded on both paths.
                string op = neg ? "==" : "!=";
                rewritten = indent + $"if ((a & {mask}) {op} 0) return;";
            }
            else if (one.Success)
            {
                neg = one.Groups[1].Success;
                string body = one.Groups[2].Value.Trim();
                if (BodyReadsA(body) || !NextOverwritesA(statements, i + 2))
                    continue;
                string op = neg ? "==" : "!=";
                rewritten = indent + $"if ((a & {mask}) {op} 0) {{ {body} }}";
            }
            else if (hdr.Success)
            {
                neg = hdr.Groups[1].Success;
                if (!TryFindMatchingBrace(statements, i + 2, out int close)
                    || BlockReadsA(statements, i + 3, close)
                    || !NextOverwritesA(statements, close + 1))
                    continue;
                string op = neg ? "==" : "!=";
                statements[i] = indent + $"if ((a & {mask}) {op} 0)";
                statements.RemoveAt(i + 1);
                continue;
            }
            else
                continue;

            statements[i] = rewritten;
            statements.RemoveAt(i + 1);
        }
    }

    static bool ShiftResultDiscardedAfterGoto(List<string> statements, int afterBranch, string label)
    {
        var labels = IndexLabels(statements);
        if (!labels.TryGetValue(label, out int labIdx))
            return false;
        // Fall-through: from afterBranch to labIdx must overwrite a before reading.
        if (!RangeOverwritesABeforeRead(statements, afterBranch, labIdx))
            return false;
        // Taken path: from labIdx+1 onward until overwrite.
        return RangeOverwritesABeforeRead(statements, labIdx + 1, statements.Count);
    }

    static bool RangeOverwritesABeforeRead(List<string> statements, int start, int end)
    {
        for (int i = start; i < end && i < statements.Count; i++)
        {
            string t = statements[i].TrimStart();
            if (string.IsNullOrEmpty(t) || t.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (TryGetLabelName(t, out _))
                continue;
            if (IsAOverwrite(t))
                return true;
            if (BodyReadsA(t))
                return false;
            // Control flow we can't see through
            if (t.StartsWith("goto ", StringComparison.Ordinal)
                || t.StartsWith("if ", StringComparison.Ordinal)
                || t.StartsWith("return", StringComparison.Ordinal)
                || t.StartsWith("do", StringComparison.Ordinal)
                || t.StartsWith("while ", StringComparison.Ordinal)
                || t is "{" or "}")
                return false;
        }
        return false;
    }

    static bool NextOverwritesA(List<string> statements, int start)
    {
        for (int i = start; i < statements.Count; i++)
        {
            string t = statements[i].TrimStart();
            if (string.IsNullOrEmpty(t) || t.StartsWith("//", StringComparison.Ordinal) || t is "{" or "}")
                continue;
            if (TryGetLabelName(t, out _))
                continue;
            return IsAOverwrite(t);
        }
        return false;
    }

    static bool BlockReadsA(List<string> statements, int start, int endExclusive)
    {
        for (int i = start; i < endExclusive; i++)
        {
            if (BodyReadsA(statements[i].TrimStart()))
                return true;
        }
        return false;
    }

    static bool IsAOverwrite(string t) =>
        t.StartsWith("a = ", StringComparison.Ordinal)
        || t.StartsWith("a &= ", StringComparison.Ordinal)
        || t.StartsWith("a |= ", StringComparison.Ordinal)
        || t.StartsWith("a ^= ", StringComparison.Ordinal)
        || t is "a++;" or "a--;"
        || t.StartsWith("a <<= ", StringComparison.Ordinal)
        || t.StartsWith("a >>= ", StringComparison.Ordinal);

    static bool BodyReadsA(string t)
    {
        if (IsAOverwrite(t) && t.StartsWith("a = ", StringComparison.Ordinal))
            return false; // pure assign from non-a still overwrites; check RHS
        if (t.StartsWith("a = ", StringComparison.Ordinal))
        {
            string rhs = t["a = ".Length..];
            return UsesRegA(rhs);
        }
        return UsesRegA(t);
    }

    static bool UsesRegA(string t)
    {
        // Word-boundary-ish: avoid matching ram_ names containing 'a'.
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] != 'a' && t[i] != 'A')
                continue;
            char prev = i > 0 ? t[i - 1] : '\0';
            char next = i + 1 < t.Length ? t[i + 1] : '\0';
            if (char.IsLetterOrDigit(prev) || prev == '_')
                continue;
            if (char.IsLetterOrDigit(next) || next == '_')
                continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Collapse consecutive ASL/LSR lines whose intermediate carry is unused into
    /// <c>a &lt;&lt;= n</c> / <c>a &gt;&gt;= n</c> (SoftCpu emits n shifts).
    /// </summary>
    static void SugarMultiShifts(List<string> statements)
    {
        const string asl = "carry = (a & 0x80) != 0; a <<= 1;";
        const string lsr = "carry = (a & 1) != 0; a >>= 1;";
        for (int i = 0; i < statements.Count; )
        {
            string t = statements[i].TrimStart();
            bool isAsl = t == asl;
            bool isLsr = t == lsr;
            if (!isAsl && !isLsr)
            {
                i++;
                continue;
            }

            int n = 1;
            while (i + n < statements.Count && statements[i + n].TrimStart() == t)
                n++;
            if (n < 2)
            {
                i++;
                continue;
            }

            // Only collapse when the next stmt does not branch on carry (carry = last shift's bit).
            // Intermediate carries are definitely unused; final carry is preserved by emitting n shifts.
            string indent = LeadingIndent(statements[i]);
            string replacement = isAsl
                ? indent + $"a <<= {n};"
                : indent + $"a >>= {n};";
            statements.RemoveRange(i, n);
            statements.Insert(i, replacement);
            i++;
        }
    }

    /// <summary>
    /// Collapse double casts only: <c>peek((ushort)((ushort)0xXXXX + x))</c> →
    /// <c>peek((ushort)(0xXXXX + x))</c>. Keep a single <c>(ushort)</c> so the
    /// emitted Program.cs still type-checks (<c>peek</c>/<c>poke</c> take ushort;
    /// <c>base + x</c> and hex literals are <c>int</c> in C#).
    /// </summary>
    static void SugarIndexedPeeks(List<string> statements)
    {
        var litDouble = new Regex(
            @"(peek|poke)\(\(ushort\)\(\(ushort\)(0x[0-9A-Fa-f]+|\d+)\s*\+\s*(x|y)\)\)",
            RegexOptions.Compiled);
        for (int i = 0; i < statements.Count; i++)
            statements[i] = litDouble.Replace(statements[i], "$1((ushort)($2 + $3))");
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

    /// <summary>
    /// Rewrite simple goto diamonds / skip-arms into <c>if/else</c> blocks SoftCpu can lower.
    /// Innermost (shortest) spans first so nested labels unlock outer arms.
    /// </summary>
    static void StructureIfElseDiamonds(List<string> statements)
    {
        bool changed;
        do
        {
            changed = false;
            var labels = IndexLabels(statements);
            var candidates = new List<(int IfIdx, int ThenIdx, string Cond)>();
            int depth = 0;
            for (int i = 0; i < statements.Count; i++)
            {
                string raw = statements[i].TrimStart();
                if (raw == "{") { depth++; continue; }
                if (raw == "}") { depth = Math.Max(0, depth - 1); continue; }
                if (depth != 0)
                    continue;
                if (!TryParseGoto(raw, out string thenOrSkip, out string? cond)
                    || cond is null
                    || !IsFlagCond(cond) // reg compares (a == N) handled by StructureRegCompareSkipArms
                    || !labels.TryGetValue(thenOrSkip, out int thenIdx)
                    || thenIdx <= i)
                    continue;
                candidates.Add((i, thenIdx, cond));
            }
            candidates.Sort((a, b) => (a.ThenIdx - a.IfIdx).CompareTo(b.ThenIdx - b.IfIdx));

            foreach (var (i, thenIdx, cond) in candidates)
            {
                // Indices may be stale after a prior rewrite in this pass — re-validate.
                if (i >= statements.Count
                    || !TryParseGoto(statements[i].TrimStart(), out string t, out string? c)
                    || c != cond
                    || t != GetLabelAt(statements, thenIdx)
                    || !labels.TryGetValue(t, out int liveThen)
                    || liveThen != thenIdx)
                    continue;

                string indent = LeadingIndent(statements[i]);

                // Full diamond:
                //   if (cond) goto L_then;
                //     <else> ; goto L_join;
                //   L_then: ; <then> ; L_join: ;
                if (TryMatchFullDiamond(statements, i, thenIdx, labels, out int joinGotoIdx, out int joinLabelIdx))
                {
                    var elseArm = SliceArm(statements, i + 1, joinGotoIdx);
                    var thenArm = SliceArm(statements, thenIdx + 1, joinLabelIdx);
                    if (ArmHasNestedLabels(elseArm) || ArmHasNestedLabels(thenArm)
                        || ContainsGoto(elseArm) || ContainsGoto(thenArm))
                        continue;

                    var replacement = BuildIfElse(indent, cond, thenArm, elseArm);
                    statements.RemoveRange(i, joinLabelIdx - i + 1);
                    statements.InsertRange(i, replacement);
                    changed = true;
                    break;
                }

                // JMP-abs join became `callee(); return;` (join is a sibling routine entry):
                //   if (cond) goto L_then;
                //     <else> ; callee(); return;
                //   L_then: ; <then> ;
                if (TryMatchCallReturnDiamond(statements, i, thenIdx, out int callRetIdx, out int thenEndIdx))
                {
                    var elseArm = SliceArm(statements, i + 1, callRetIdx + 1); // include call;return
                    var thenArm = SliceArm(statements, thenIdx + 1, thenEndIdx);
                    if (ArmHasNestedLabels(elseArm) || ArmHasNestedLabels(thenArm)
                        || ContainsGoto(elseArm) || ContainsGoto(thenArm))
                        continue;

                    var replacement = BuildIfElse(indent, cond, thenArm, elseArm);
                    statements.RemoveRange(i, thenEndIdx - i);
                    statements.InsertRange(i, replacement);
                    changed = true;
                    break;
                }

                // One-arm skip: if (cond) goto L_skip; <body>; L_skip: ;
                if (TryMatchSkipArm(statements, i, thenIdx, labels, out int skipLabelIdx))
                {
                    var body = SliceArm(statements, i + 1, skipLabelIdx);
                    // Exit gotos (to skip label or beyond) are OK; only mid-arm gotos/labels/braces block.
                    if (ArmHasNestedLabels(body) || ArmContainsBraces(body)
                        || HasInternalGoto(statements, i + 1, skipLabelIdx, labels))
                        continue;

                    string negated = NegateFlagCond(cond);
                    var replacement = new List<string>();
                    if (body.Count == 0)
                    {
                        // empty skip — drop the branch
                    }
                    else if (body.Count == 1
                        && IsSafeOneLinerBody(body[0])
                        && !body[0].StartsWith("if ", StringComparison.Ordinal))
                    {
                        replacement.Add(indent + $"if ({negated}) {{ {body[0].TrimStart()} }}");
                    }
                    else
                    {
                        replacement.Add(indent + $"if ({negated})");
                        replacement.Add(indent + "{");
                        foreach (string line in body)
                            replacement.Add(indent + "    " + line.TrimStart());
                        replacement.Add(indent + "}");
                    }

                    statements.RemoveRange(i, skipLabelIdx - i + 1);
                    statements.InsertRange(i, replacement);
                    changed = true;
                    break;
                }
            }
        } while (changed);
    }

    static Dictionary<string, int> IndexLabels(List<string> statements)
    {
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < statements.Count; i++)
        {
            if (TryGetLabelName(statements[i].TrimStart(), out string name))
                labels[name] = i;
        }
        return labels;
    }

    static bool TryMatchFullDiamond(
        List<string> stmts, int ifGotoIdx, int thenLabelIdx,
        Dictionary<string, int> labels,
        out int joinGotoIdx, out int joinLabelIdx)
    {
        joinGotoIdx = joinLabelIdx = -1;
        // Find unconditional goto to L_join in (ifGoto, thenLabel)
        for (int j = ifGotoIdx + 1; j < thenLabelIdx; j++)
        {
            if (!TryParseGoto(stmts[j].TrimStart(), out string join, out string? c) || c is not null)
                continue;
            if (!labels.TryGetValue(join, out int jl) || jl <= thenLabelIdx)
                continue;
            // L_join must be the first label after then-arm with no other labels between then and join
            // except we allow only thenLabel between if and join-goto's target.
            bool ok = true;
            for (int k = thenLabelIdx + 1; k < jl; k++)
            {
                if (TryGetLabelName(stmts[k].TrimStart(), out _))
                {
                    ok = false;
                    break;
                }
            }
            if (!ok)
                continue;
            // Nothing after join-goto until then-label except the goto itself span
            joinGotoIdx = j;
            joinLabelIdx = jl;
            // Ensure join-goto is the last stmt before then-label
            if (joinGotoIdx != thenLabelIdx - 1)
                return false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Absolute JMP join lowered as <c>callee(); return;</c> when the join address is a routine entry.
    /// </summary>
    static bool TryMatchCallReturnDiamond(
        List<string> stmts, int ifGotoIdx, int thenLabelIdx,
        out int callReturnIdx, out int thenEndIdx)
    {
        callReturnIdx = thenEndIdx = -1;
        if (thenLabelIdx != ifGotoIdx + 1 && thenLabelIdx > ifGotoIdx + 1)
        {
            // last stmt before then-label must be `name(); return;`
            int j = thenLabelIdx - 1;
            if (!IsCallReturn(stmts[j].TrimStart()))
                return false;
            for (int k = ifGotoIdx + 1; k < j; k++)
            {
                if (TryGetLabelName(stmts[k].TrimStart(), out _))
                    return false;
                if (TryParseGoto(stmts[k].TrimStart(), out _, out _))
                    return false;
            }
            callReturnIdx = j;
        }
        else
            return false;

        // Then-arm runs from after then-label to next label or end of body (no further labels).
        thenEndIdx = stmts.Count;
        for (int k = thenLabelIdx + 1; k < stmts.Count; k++)
        {
            if (TryGetLabelName(stmts[k].TrimStart(), out _))
            {
                thenEndIdx = k;
                break;
            }
        }
        return true;
    }

    static bool IsCallReturn(string line)
    {
        // `func_XXXX(); return;` or `name(); return;`
        int callEnd = line.IndexOf("(); return;", StringComparison.Ordinal);
        if (callEnd <= 0 || !line.EndsWith("(); return;", StringComparison.Ordinal))
            return false;
        string name = line[..callEnd];
        return name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    static List<string> BuildIfElse(string indent, string cond, List<string> thenArm, List<string> elseArm)
    {
        var replacement = new List<string>
        {
            indent + $"if ({cond})",
            indent + "{",
        };
        foreach (string line in thenArm)
            replacement.Add(indent + "    " + line.TrimStart());
        replacement.Add(indent + "}");
        replacement.Add(indent + "else");
        replacement.Add(indent + "{");
        foreach (string line in elseArm)
            replacement.Add(indent + "    " + line.TrimStart());
        replacement.Add(indent + "}");
        return replacement;
    }

    static bool TryMatchSkipArm(
        List<string> stmts, int ifGotoIdx, int skipLabelIdx,
        Dictionary<string, int> labels,
        out int labelIdx)
    {
        labelIdx = skipLabelIdx;
        // No other labels between if and skip. Exit gotos (to skip or beyond) are OK.
        for (int j = ifGotoIdx + 1; j < skipLabelIdx; j++)
        {
            if (TryGetLabelName(stmts[j].TrimStart(), out _))
                return false;
        }
        if (HasInternalGoto(stmts, ifGotoIdx + 1, skipLabelIdx, labels))
            return false;
        // Don't steal a diamond's then-label: if previous stmt before skip is `goto L_join`, it's a diamond.
        if (skipLabelIdx > 0
            && TryParseGoto(stmts[skipLabelIdx - 1].TrimStart(), out _, out string? c)
            && c is null)
            return false;
        // Label must not be targeted from elsewhere
        string name = GetLabelAt(stmts, skipLabelIdx);
        for (int j = 0; j < stmts.Count; j++)
        {
            if (j == ifGotoIdx)
                continue;
            if (TryParseGoto(stmts[j].TrimStart(), out string t, out _) && t == name)
                return false;
        }
        return true;
    }

    static List<string> SliceArm(List<string> stmts, int startInclusive, int endExclusive)
    {
        var arm = new List<string>();
        for (int i = startInclusive; i < endExclusive; i++)
        {
            string t = stmts[i].TrimStart();
            if (t.Length == 0)
                continue;
            arm.Add(t);
        }
        return arm;
    }

    static bool ArmHasNestedLabels(List<string> arm)
    {
        foreach (string line in arm)
        {
            if (TryGetLabelName(line, out _))
                return true;
        }
        return false;
    }

    static bool ArmContainsBraces(List<string> arm)
    {
        foreach (string line in arm)
        {
            string t = line.TrimStart();
            if (t is "{" or "}")
                return true;
        }
        return false;
    }

    static bool ContainsGoto(List<string> arm)
    {
        foreach (string line in arm)
        {
            if (TryParseGoto(line, out _, out _))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Goto whose target lies strictly inside the arm <c>(start, end)</c>.
    /// Gotos to <paramref name="end"/> (the skip label) or outside are exit edges — allowed.
    /// </summary>
    static bool HasInternalGoto(
        List<string> statements, int start, int end, Dictionary<string, int> labels)
    {
        for (int j = start; j < end; j++)
        {
            if (!TryParseGoto(statements[j].TrimStart(), out string t, out _))
                continue;
            if (!labels.TryGetValue(t, out int ti))
                return true;
            if (ti > start && ti < end)
                return true;
        }
        return false;
    }

    static string GetLabelAt(List<string> stmts, int idx)
    {
        TryGetLabelName(stmts[idx].TrimStart(), out string name);
        return name;
    }

    static bool IsFlagCond(string cond) =>
        cond is "zero" or "!zero" or "carry" or "!carry" or "negative" or "!negative";

    static string NegateFlagCond(string cond) => cond switch
    {
        "zero" => "!zero",
        "!zero" => "zero",
        "carry" => "!carry",
        "!carry" => "carry",
        "negative" => "!negative",
        "!negative" => "negative",
        _ => cond, // never prefix-bang non-flags (would turn `a == 2` into `!a == 2`)
    };

    /// <summary>
    /// One-liner <c>if (c) { body }</c> puts <c>}</c> on the same line; comments would swallow it.
    /// </summary>
    static bool IsSafeOneLinerBody(string line)
    {
        string t = line.TrimStart();
        if (t.Length == 0 || t is "{" or "}")
            return false;
        if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal))
            return false;
        return true;
    }

    /// <summary>
    /// <c>a = (byte)(a &amp; expr);</c> → <c>a &amp;= expr;</c> (and | / ^).
    /// </summary>
    static void SugarAluAssigns(List<string> statements)
    {
        for (int i = 0; i < statements.Count; i++)
        {
            string indent = LeadingIndent(statements[i]);
            string line = statements[i].TrimStart();
            if (TrySugarAluLine(line, out string sugared))
                statements[i] = indent + sugared;
        }
    }

    // CPX/CPY emit without negative; CMP # includes it. Negative clause optional.
    static readonly Regex CmpFlagTriple = new(
        @"^zero\s*=\s*(a|x|y)\s*==\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*carry\s*=\s*\1\s*>=\s*\2\s*;(?:\s*negative\s*=\s*\(byte\)\(\1\s*-\s*\2\)\s*>=\s*0x80\s*;)?\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfFlagGoto = new(
        @"^if\s*\(\s*(!)?\s*(zero|carry|negative)\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfRegCompareGoto = new(
        @"^if\s*\(\s*(a|x|y)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfRegMemCompareGoto = new(
        @"^if\s*\(\s*(a|x|y)\s*(==|!=|>=|<)\s*([A-Za-z_]\w*)\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex MemCmpBlock = new(
        @"^\{\s*byte\s+_v\s*=\s*(.+?);\s*zero\s*=\s*a\s*==\s*_v;\s*carry\s*=\s*a\s*>=\s*_v;\s*negative\s*=\s*\(byte\)\(a\s*-\s*_v\)\s*>=\s*0x80;\s*\}\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfZeroTest = new(
        @"^if\s*\(\s*(!)?\s*zero\s*\)\s*(goto\s+(L_[0-9A-Fa-f]+)\s*;|return\s*;|\{.*\}\s*)$",
        RegexOptions.Compiled);

    static readonly Regex IfAnyGoto = new(
        @"^if\s*\((.+)\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex MemIncDecAssign = new(
        @"^([A-Za-z_]\w*)\s*=\s*\(byte\)\(\1\s*([+-])\s*1\)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// <c>zero = a/x/y == N; carry = …; if (zero|/carry) …</c>
    /// → <c>if (reg op N) …</c> (SoftCpu lowers to CMP/CPX/CPY + Bxx).
    /// </summary>
    static void SugarCompareBranches(List<string> statements)
    {
        for (int i = 0; i < statements.Count - 1; i++)
        {
            string indent = LeadingIndent(statements[i]);
            string cmpLine = statements[i].TrimStart();
            var cmp = CmpFlagTriple.Match(cmpLine);
            if (!cmp.Success)
                continue;

            string reg = cmp.Groups[1].Value;
            string imm = cmp.Groups[2].Value;
            string branchLine = statements[i + 1].TrimStart();

            // if (flag) goto L;
            var br = IfFlagGoto.Match(branchLine);
            if (br.Success)
            {
                string? op = FlagCompareOp(br.Groups[2].Value, br.Groups[1].Success);
                if (op is null)
                    continue;
                statements[i] = indent + $"if ({reg} {op} {imm}) goto {br.Groups[3].Value};";
                statements.RemoveAt(i + 1);
                continue;
            }

            // if (flag) return;
            var ret = Regex.Match(branchLine,
                @"^if\s*\(\s*(!)?\s*(zero|carry)\s*\)\s*return\s*;\s*$");
            if (ret.Success)
            {
                string? op = FlagCompareOp(ret.Groups[2].Value, ret.Groups[1].Success);
                if (op is null)
                    continue;
                statements[i] = indent + $"if ({reg} {op} {imm}) return;";
                statements.RemoveAt(i + 1);
                continue;
            }

            // if (flag) { stmt; }
            var one = Regex.Match(branchLine,
                @"^if\s*\(\s*(!)?\s*(zero|carry)\s*\)\s*\{\s*(.+)\s*\}\s*$");
            if (one.Success)
            {
                string? op = FlagCompareOp(one.Groups[2].Value, one.Groups[1].Success);
                if (op is null)
                    continue;
                statements[i] = indent + $"if ({reg} {op} {imm}) {{ {one.Groups[3].Value.Trim()} }}";
                statements.RemoveAt(i + 1);
                continue;
            }

            // if (flag)  — multi-line block header
            var hdr = Regex.Match(branchLine, @"^if\s*\(\s*(!)?\s*(zero|carry)\s*\)\s*$");
            if (hdr.Success)
            {
                string? op = FlagCompareOp(hdr.Groups[2].Value, hdr.Groups[1].Success);
                if (op is null)
                    continue;
                statements[i] = indent + $"if ({reg} {op} {imm})";
                statements.RemoveAt(i + 1);
            }
        }
    }

    static string? FlagCompareOp(string flag, bool negated) => (flag, negated) switch
    {
        ("zero", false) => "==",
        ("zero", true) => "!=",
        ("carry", false) => ">=",
        ("carry", true) => "<",
        _ => null,
    };

    /// <summary>
    /// <c>{ byte _v = addr; zero = a == _v; … }</c> + flag branch → <c>if (a == addr) …</c>.
    /// </summary>
    static void SugarMemCompareBranches(List<string> statements)
    {
        for (int i = 0; i < statements.Count - 1; i++)
        {
            string indent = LeadingIndent(statements[i]);
            var cmp = MemCmpBlock.Match(statements[i].TrimStart());
            if (!cmp.Success)
                continue;
            string addr = cmp.Groups[1].Value.Trim();
            // Only simple names / peeks SoftCpu can CMP against.
            if (addr.Contains('(', StringComparison.Ordinal) && !addr.StartsWith("peek(", StringComparison.Ordinal))
                continue;

            string branchLine = statements[i + 1].TrimStart();
            var br = IfFlagGoto.Match(branchLine);
            string? op = null;
            string suffix;
            if (br.Success)
            {
                op = (br.Groups[2].Value, br.Groups[1].Success) switch
                {
                    ("zero", false) => "==",
                    ("zero", true) => "!=",
                    ("carry", false) => ">=",
                    ("carry", true) => "<",
                    _ => null,
                };
                if (op is null)
                    continue;
                suffix = $"goto {br.Groups[3].Value};";
            }
            else if (branchLine is "if (zero) return;" or "if (!zero) return;")
            {
                op = branchLine.StartsWith("if (!", StringComparison.Ordinal) ? "!=" : "==";
                suffix = "return;";
            }
            else if (branchLine.StartsWith("if (zero)", StringComparison.Ordinal)
                || branchLine.StartsWith("if (!zero)", StringComparison.Ordinal))
            {
                // if (zero) { … } one-liner / block — leave for StructureIfElse; still collapse cmp.
                continue;
            }
            else
                continue;

            string rhs = addr.StartsWith("peek(", StringComparison.Ordinal) ? addr : addr;
            statements[i] = indent + $"if (a {op} {rhs}) {suffix}";
            statements.RemoveAt(i + 1);
        }
    }

    /// <summary>
    /// After a/x/y update, <c>if (zero)</c>/<c>if (!zero)</c> → <c>if (reg == 0)</c>/<c>if (reg != 0)</c>.
    /// </summary>
    static void SugarZeroTestsOnA(List<string> statements)
    {
        for (int i = 1; i < statements.Count; i++)
        {
            string prev = statements[i - 1].TrimStart();
            if (!TryRegUpdatedBy(prev, out string reg))
                continue;
            string indent = LeadingIndent(statements[i]);
            string line = statements[i].TrimStart();

            // if (zero) goto L; / if (!zero) return; / if (zero) { stmt; }
            var gotoBr = IfFlagGoto.Match(line);
            if (gotoBr.Success && gotoBr.Groups[2].Value == "zero")
            {
                string op = gotoBr.Groups[1].Success ? "!=" : "==";
                statements[i] = indent + $"if ({reg} {op} 0) goto {gotoBr.Groups[3].Value};";
                continue;
            }
            if (line is "if (zero) return;")
            {
                statements[i] = indent + $"if ({reg} == 0) return;";
                continue;
            }
            if (line is "if (!zero) return;")
            {
                statements[i] = indent + $"if ({reg} != 0) return;";
                continue;
            }
            // if (zero) { … }  or if (!zero) { … }
            var one = Regex.Match(line, @"^if\s*\(\s*(!)?\s*zero\s*\)\s*\{\s*(.+)\s*\}\s*$");
            if (one.Success)
            {
                string op = one.Groups[1].Success ? "!=" : "==";
                statements[i] = indent + $"if ({reg} {op} 0) {{ {one.Groups[2].Value.Trim()} }}";
                continue;
            }
            if (line is "if (zero)" or "if (!zero)")
            {
                string op = line.Contains('!', StringComparison.Ordinal) ? "!=" : "==";
                statements[i] = indent + $"if ({reg} {op} 0)";
            }
        }
    }

    static bool TryRegUpdatedBy(string line, out string reg)
    {
        reg = "a";
        if (line.StartsWith("a = ", StringComparison.Ordinal)
            || line.StartsWith("a &= ", StringComparison.Ordinal)
            || line.StartsWith("a |= ", StringComparison.Ordinal)
            || line.StartsWith("a ^= ", StringComparison.Ordinal)
            || line is "a++;" or "a--;"
            || line.Contains("a <<= ", StringComparison.Ordinal)
            || line.Contains("a >>= ", StringComparison.Ordinal))
            return true;

        if (line.StartsWith("x = ", StringComparison.Ordinal) || line is "x++;" or "x--;")
        {
            reg = "x";
            return true;
        }
        if (line.StartsWith("y = ", StringComparison.Ordinal) || line is "y++;" or "y--;")
        {
            reg = "y";
            return true;
        }

        return false;
    }

    /// <summary><c>y = a; y++;</c> → <c>y = (byte)(a + 1);</c> (preserves A via TAY/INY when lowered).</summary>
    static void SugarTransferInc(List<string> statements)
    {
        for (int i = 0; i < statements.Count - 1; i++)
        {
            string indent = LeadingIndent(statements[i]);
            string a = statements[i].TrimStart();
            string b = statements[i + 1].TrimStart();
            if (a == "y = a;" && b == "y++;")
            {
                statements[i] = indent + "y = (byte)(a + 1);";
                statements.RemoveAt(i + 1);
            }
            else if (a == "x = a;" && b == "x++;")
            {
                statements[i] = indent + "x = (byte)(a + 1);";
                statements.RemoveAt(i + 1);
            }
            else if (a == "y = a;" && b == "y--;")
            {
                statements[i] = indent + "y = (byte)(a - 1);";
                statements.RemoveAt(i + 1);
            }
            else if (a == "x = a;" && b == "x--;")
            {
                statements[i] = indent + "x = (byte)(a - 1);";
                statements.RemoveAt(i + 1);
            }
        }
    }

    /// <summary>
    /// <c>ram--; if (!zero) goto/return</c> → <c>if (ram != 0) goto/return</c>
    /// (SoftCpu peephole: DEC + BNE without re-load).
    /// </summary>
    static void SugarZeroAfterMemDec(List<string> statements)
    {
        for (int i = 0; i < statements.Count - 1; i++)
        {
            var dec = Regex.Match(statements[i].TrimStart(), @"^([A-Za-z_]\w*)--;\s*$");
            if (!dec.Success || dec.Groups[1].Value is "a" or "x" or "y")
                continue;
            string name = dec.Groups[1].Value;
            string indent = LeadingIndent(statements[i + 1]);
            string next = statements[i + 1].TrimStart();

            var g = IfFlagGoto.Match(next);
            if (g.Success && g.Groups[2].Value == "zero")
            {
                string op = g.Groups[1].Success ? "!=" : "==";
                statements[i + 1] = indent + $"if ({name} {op} 0) goto {g.Groups[3].Value};";
                continue;
            }
            if (next is "if (zero) return;")
            {
                statements[i + 1] = indent + $"if ({name} == 0) return;";
                continue;
            }
            if (next is "if (!zero) return;")
            {
                statements[i + 1] = indent + $"if ({name} != 0) return;";
                continue;
            }
            var one = Regex.Match(next, @"^if\s*\(\s*(!)?\s*zero\s*\)\s*\{\s*(.+)\s*\}\s*$");
            if (one.Success)
            {
                string op = one.Groups[1].Success ? "!=" : "==";
                statements[i + 1] = indent + $"if ({name} {op} 0) {{ {one.Groups[2].Value.Trim()} }}";
                continue;
            }
            if (next is "if (zero)" or "if (!zero)")
            {
                string op = next.Contains('!', StringComparison.Ordinal) ? "!=" : "==";
                statements[i + 1] = indent + $"if ({name} {op} 0)";
            }
        }
    }

    /// <summary>
    /// <c>if (ram != 0) goto L_skip; body; L_skip:</c> → <c>if (ram == 0) { body }</c>.
    /// </summary>
    static void StructureMemVarZeroSkipArms(List<string> statements)
    {
        var memGoto = new Regex(
            @"^if\s*\(\s*([A-Za-z_]\w*)\s*(==|!=)\s*0\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
            RegexOptions.Compiled);
        bool changed;
        do
        {
            changed = false;
            var labels = IndexLabels(statements);
            var candidates = new List<(int IfIdx, int SkipIdx, Match Br)>();
            int depth = 0;
            for (int i = 0; i < statements.Count; i++)
            {
                string raw = statements[i].TrimStart();
                if (raw == "{") { depth++; continue; }
                if (raw == "}") { depth = Math.Max(0, depth - 1); continue; }
                if (depth != 0)
                    continue;
                var br = memGoto.Match(raw);
                if (!br.Success || br.Groups[1].Value is "a" or "x" or "y")
                    continue;
                if (!labels.TryGetValue(br.Groups[3].Value, out int skipIdx) || skipIdx <= i)
                    continue;
                candidates.Add((i, skipIdx, br));
            }
            candidates.Sort((a, b) => (a.SkipIdx - a.IfIdx).CompareTo(b.SkipIdx - b.IfIdx));

            foreach (var (i, skipIdx, br) in candidates)
            {
                if (i >= statements.Count || !memGoto.IsMatch(statements[i].TrimStart()))
                    continue;
                if (skipIdx >= statements.Count
                    || !TryGetLabelName(statements[skipIdx].TrimStart(), out string lab)
                    || lab != br.Groups[3].Value)
                    continue;

                var body = SliceArm(statements, i + 1, skipIdx);
                if (ArmHasNestedLabels(body) || ArmContainsBraces(body)
                    || HasInternalGoto(statements, i + 1, skipIdx, labels))
                    continue;

                string indent = LeadingIndent(statements[i]);
                string name = br.Groups[1].Value;
                string negOp = br.Groups[2].Value == "==" ? "!=" : "==";
                if (body.Count == 0)
                {
                    statements.RemoveRange(i, skipIdx - i + 1);
                    changed = true;
                    break;
                }

                var replacement = new List<string>();
                if (body.Count == 1 && IsSafeOneLinerBody(body[0])
                    && !body[0].TrimStart().StartsWith("if ", StringComparison.Ordinal))
                {
                    replacement.Add(indent + $"if ({name} {negOp} 0) {{ {body[0].TrimStart()} }}");
                }
                else
                {
                    replacement.Add(indent + $"if ({name} {negOp} 0)");
                    replacement.Add(indent + "{");
                    foreach (string line in body)
                        replacement.Add(indent + "    " + line.TrimStart());
                    replacement.Add(indent + "}");
                }
                statements.RemoveRange(i, skipIdx - i + 1);
                statements.InsertRange(i, replacement);
                changed = true;
                break;
            }
        } while (changed);
    }

    /// <summary>
    /// <c>if ((a &amp; M) != 0) goto L_skip; body; L_skip:</c> → <c>if ((a &amp; M) == 0) { body }</c>.
    /// </summary>
    static void StructureBitTestSkipArms(List<string> statements)
    {
        var bitGoto = new Regex(
            @"^if\s*\(\s*\(a\s*&\s*(0x[0-9A-Fa-f]+|\d+)\)\s*(==|!=)\s*0\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
            RegexOptions.Compiled);
        bool changed;
        do
        {
            changed = false;
            var labels = IndexLabels(statements);
            var candidates = new List<(int IfIdx, int SkipIdx, Match Br)>();
            int depth = 0;
            for (int i = 0; i < statements.Count; i++)
            {
                string raw = statements[i].TrimStart();
                if (raw == "{") { depth++; continue; }
                if (raw == "}") { depth = Math.Max(0, depth - 1); continue; }
                if (depth != 0)
                    continue;
                var br = bitGoto.Match(raw);
                if (!br.Success)
                    continue;
                if (!labels.TryGetValue(br.Groups[3].Value, out int skipIdx) || skipIdx <= i)
                    continue;
                candidates.Add((i, skipIdx, br));
            }
            candidates.Sort((a, b) => (a.SkipIdx - a.IfIdx).CompareTo(b.SkipIdx - b.IfIdx));

            foreach (var (i, skipIdx, br) in candidates)
            {
                if (i >= statements.Count || !bitGoto.IsMatch(statements[i].TrimStart()))
                    continue;
                if (skipIdx >= statements.Count
                    || !TryGetLabelName(statements[skipIdx].TrimStart(), out string lab)
                    || lab != br.Groups[3].Value)
                    continue;

                var body = SliceArm(statements, i + 1, skipIdx);
                if (ArmHasNestedLabels(body) || ArmContainsBraces(body)
                    || HasInternalGoto(statements, i + 1, skipIdx, labels))
                    continue;

                string indent = LeadingIndent(statements[i]);
                string mask = br.Groups[1].Value;
                string op = br.Groups[2].Value;
                string negOp = op == "==" ? "!=" : "==";
                if (body.Count == 0)
                {
                    statements.RemoveRange(i, skipIdx - i + 1);
                    changed = true;
                    break;
                }

                var replacement = new List<string>();
                if (body.Count == 1 && IsSafeOneLinerBody(body[0])
                    && !body[0].TrimStart().StartsWith("if ", StringComparison.Ordinal))
                {
                    replacement.Add(indent + $"if ((a & {mask}) {negOp} 0) {{ {body[0].TrimStart()} }}");
                }
                else
                {
                    replacement.Add(indent + $"if ((a & {mask}) {negOp} 0)");
                    replacement.Add(indent + "{");
                    foreach (string line in body)
                        replacement.Add(indent + "    " + line.TrimStart());
                    replacement.Add(indent + "}");
                }
                statements.RemoveRange(i, skipIdx - i + 1);
                statements.InsertRange(i, replacement);
                changed = true;
                break;
            }
        } while (changed);
    }

    /// <summary>
    /// Same as <see cref="StructureRegCompareSkipArms"/> but for <c>if (a == zp_xx) goto</c>.
    /// </summary>
    static void StructureMemCompareSkipArms(List<string> statements)
    {
        bool changed;
        do
        {
            changed = false;
            var labels = IndexLabels(statements);
            for (int i = 0; i < statements.Count; i++)
            {
                string indent = LeadingIndent(statements[i]);
                var br = IfRegMemCompareGoto.Match(statements[i].TrimStart());
                if (!br.Success)
                    continue;
                // Exclude immediates mistaken as names (handled elsewhere) and a/x/y.
                string rhs = br.Groups[3].Value;
                if (rhs is "a" or "x" or "y" or "zero" or "carry" or "negative")
                    continue;
                if (rhs.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || char.IsDigit(rhs[0]))
                    continue;
                string label = br.Groups[4].Value;
                if (!labels.TryGetValue(label, out int skipIdx) || skipIdx <= i)
                    continue;

                var body = SliceArm(statements, i + 1, skipIdx);
                if (ArmHasNestedLabels(body) || HasInternalGoto(statements, i + 1, skipIdx, labels))
                    continue;
                if (body.Count == 0)
                {
                    statements.RemoveRange(i, skipIdx - i + 1);
                    changed = true;
                    break;
                }

                string reg = br.Groups[1].Value;
                string op = br.Groups[2].Value;
                string negOp = op switch
                {
                    "==" => "!=",
                    "!=" => "==",
                    ">=" => "<",
                    "<" => ">=",
                    _ => op,
                };

                var replacement = new List<string>
                {
                    indent + $"if ({reg} {negOp} {rhs})",
                    indent + "{",
                };
                foreach (string line in body)
                    replacement.Add(indent + "    " + line.TrimStart());
                replacement.Add(indent + "}");

                statements.RemoveRange(i, skipIdx - i + 1);
                statements.InsertRange(i, replacement);
                changed = true;
                break;
            }
        } while (changed);
    }

    /// <summary>
    /// After <c>a |= nonzero</c>, <c>if (!zero) goto L</c> is unconditional (Z is clear).
    /// </summary>
    static void SugarForcedGotos(List<string> statements)
    {
        var orNonZero = new Regex(
            @"^a\s*\|\=\s*(?:\(byte\))?(0x[0-9A-Fa-f]+|[1-9]\d*)\s*;\s*$",
            RegexOptions.Compiled);
        for (int i = 0; i < statements.Count - 1; i++)
        {
            var om = orNonZero.Match(statements[i].TrimStart());
            if (!om.Success)
                continue;
            if (!TryParseByteLiteral(om.Groups[1].Value, out byte v) || v == 0)
                continue;
            string indent = LeadingIndent(statements[i + 1]);
            var br = IfFlagGoto.Match(statements[i + 1].TrimStart());
            // if (!zero) goto L  →  goto L
            if (br.Success && br.Groups[1].Success && br.Groups[2].Value == "zero")
                statements[i + 1] = indent + $"goto {br.Groups[3].Value};";
        }
    }

    /// <summary>
    /// <c>if (cond) goto L_exit;</c> where <c>L_exit</c> is only <c>return;</c> → <c>if (cond) return;</c>.
    /// </summary>
    static void SugarExitGotos(List<string> statements)
    {
        var labels = IndexLabels(statements);
        var exitLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, idx) in labels)
        {
            if (IsReturnOnlyLabel(statements, idx))
                exitLabels.Add(name);
        }
        if (exitLabels.Count == 0)
            return;

        for (int i = 0; i < statements.Count; i++)
        {
            string indent = LeadingIndent(statements[i]);
            string line = statements[i].TrimStart();
            var m = IfAnyGoto.Match(line);
            if (!m.Success || !exitLabels.Contains(m.Groups[2].Value))
                continue;
            statements[i] = indent + $"if ({m.Groups[1].Value.Trim()}) return;";
        }

        // Drop now-unreferenced exit labels + their return.
        labels = IndexLabels(statements);
        for (int i = statements.Count - 1; i >= 0; i--)
        {
            if (!TryGetLabelName(statements[i].TrimStart(), out string name) || !exitLabels.Contains(name))
                continue;
            // Still referenced?
            bool used = false;
            for (int j = 0; j < statements.Count; j++)
            {
                if (j == i) continue;
                if (statements[j].Contains(name, StringComparison.Ordinal)
                    && statements[j].Contains("goto", StringComparison.Ordinal))
                {
                    used = true;
                    break;
                }
            }
            if (used)
                continue;
            // Remove label and following return;
            int remove = 1;
            if (i + 1 < statements.Count && statements[i + 1].TrimStart() == "return;")
                remove = 2;
            statements.RemoveRange(i, remove);
        }
    }

    static bool IsReturnOnlyLabel(List<string> statements, int labelIdx)
    {
        int j = labelIdx + 1;
        while (j < statements.Count && string.IsNullOrWhiteSpace(statements[j]))
            j++;
        if (j >= statements.Count)
            return false;
        string t = statements[j].TrimStart();
        if (t != "return;")
            return false;
        // Optional trailing labels only
        for (int k = j + 1; k < statements.Count; k++)
        {
            string s = statements[k].TrimStart();
            if (s.Length == 0)
                continue;
            if (TryGetLabelName(s, out _))
                continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// <c>if (a == N) goto L_skip; body; L_skip:</c> → <c>if (a != N) { body }</c>.
    /// Innermost spans first.
    /// </summary>
    static void StructureRegCompareSkipArms(List<string> statements)
    {
        bool changed;
        do
        {
            changed = false;
            var labels = IndexLabels(statements);
            var candidates = new List<(int IfIdx, int SkipIdx, Match Br)>();
            int depth = 0;
            for (int i = 0; i < statements.Count; i++)
            {
                string raw = statements[i].TrimStart();
                if (raw == "{") { depth++; continue; }
                if (raw == "}") { depth = Math.Max(0, depth - 1); continue; }
                if (depth != 0)
                    continue;
                var br = IfRegCompareGoto.Match(raw);
                if (!br.Success)
                    continue;
                string label = br.Groups[4].Value;
                if (!labels.TryGetValue(label, out int skipIdx) || skipIdx <= i)
                    continue;
                candidates.Add((i, skipIdx, br));
            }
            candidates.Sort((a, b) => (a.SkipIdx - a.IfIdx).CompareTo(b.SkipIdx - b.IfIdx));

            foreach (var (i, skipIdx, br) in candidates)
            {
                if (i >= statements.Count || !IfRegCompareGoto.IsMatch(statements[i].TrimStart()))
                    continue;
                // Skip label must still sit at skipIdx
                if (skipIdx >= statements.Count
                    || !TryGetLabelName(statements[skipIdx].TrimStart(), out string lab)
                    || lab != br.Groups[4].Value)
                    continue;

                var body = SliceArm(statements, i + 1, skipIdx);
                if (ArmHasNestedLabels(body) || ArmContainsBraces(body)
                    || HasInternalGoto(statements, i + 1, skipIdx, labels))
                    continue;

                string indent = LeadingIndent(statements[i]);
                if (body.Count == 0)
                {
                    // if (cond) goto L; L: ; → drop both
                    statements.RemoveRange(i, skipIdx - i + 1);
                    changed = true;
                    break;
                }

                string reg = br.Groups[1].Value;
                string op = br.Groups[2].Value;
                string imm = br.Groups[3].Value;
                string negOp = op switch
                {
                    "==" => "!=",
                    "!=" => "==",
                    ">=" => "<",
                    "<" => ">=",
                    _ => op,
                };

                var replacement = new List<string>();
                if (body.Count == 1 && IsSafeOneLinerBody(body[0])
                    && !body[0].TrimStart().StartsWith("if ", StringComparison.Ordinal))
                {
                    replacement.Add(indent + $"if ({reg} {negOp} {imm}) {{ {body[0].TrimStart()} }}");
                }
                else
                {
                    replacement.Add(indent + $"if ({reg} {negOp} {imm})");
                    replacement.Add(indent + "{");
                    foreach (string line in body)
                        replacement.Add(indent + "    " + line.TrimStart());
                    replacement.Add(indent + "}");
                }

                statements.RemoveRange(i, skipIdx - i + 1);
                statements.InsertRange(i, replacement);
                changed = true;
                break;
            }
        } while (changed);
    }

    /// <summary><c>ram_X = (byte)(ram_X - 1);</c> → <c>ram_X--;</c></summary>
    static void SugarMemIncDec(List<string> statements)
    {
        for (int i = 0; i < statements.Count; i++)
        {
            string indent = LeadingIndent(statements[i]);
            var m = MemIncDecAssign.Match(statements[i].TrimStart());
            if (!m.Success)
                continue;
            string name = m.Groups[1].Value;
            if (name is "a" or "x" or "y")
                continue;
            statements[i] = indent + (m.Groups[2].Value == "+" ? $"{name}++;" : $"{name}--;");
        }
    }

    /// <summary>Repair <c>(byte(expr)</c> left by a truncated poke rewrite.</summary>
    static void RepairBrokenByteCasts(List<string> statements)
    {
        for (int i = 0; i < statements.Count; i++)
            statements[i] = statements[i].Replace("(byte(", "(byte)(", StringComparison.Ordinal);
    }

    static bool TrySugarAluLine(string line, out string sugared)
    {
        sugared = null!;
        // a = (byte)(a OP rhs);
        foreach (string op in new[] { "&", "|", "^" })
        {
            string prefix = $"a = (byte)(a {op} ";
            if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(");", StringComparison.Ordinal))
                continue;
            string rhs = line[prefix.Length..^2].Trim();
            // Don't sugar memory forms here — SoftCpu has dedicated peek/poke paths.
            if (rhs.StartsWith("peek(", StringComparison.Ordinal))
                return false;
            string assignOp = op switch { "&" => "&=", "|" => "|=", _ => "^=" };
            sugared = $"a {assignOp} {rhs};";
            return true;
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
        NESLib.APU_PULSE1_SWEEP => "APU_PULSE1_SWEEP",
        NESLib.APU_PULSE1_TIMER_LO => "APU_PULSE1_TIMER_LO",
        NESLib.APU_PULSE1_TIMER_HI => "APU_PULSE1_TIMER_HI",
        NESLib.APU_PULSE2_CTRL => "APU_PULSE2_CTRL",
        NESLib.APU_PULSE2_SWEEP => "APU_PULSE2_SWEEP",
        NESLib.APU_PULSE2_TIMER_LO => "APU_PULSE2_TIMER_LO",
        NESLib.APU_PULSE2_TIMER_HI => "APU_PULSE2_TIMER_HI",
        NESLib.APU_TRIANGLE_CTRL => "APU_TRIANGLE_CTRL",
        NESLib.APU_TRIANGLE_TIMER_LO => "APU_TRIANGLE_TIMER_LO",
        NESLib.APU_TRIANGLE_TIMER_HI => "APU_TRIANGLE_TIMER_HI",
        NESLib.APU_NOISE_CTRL => "APU_NOISE_CTRL",
        NESLib.APU_NOISE_PERIOD => "APU_NOISE_PERIOD",
        NESLib.APU_NOISE_LENGTH => "APU_NOISE_LENGTH",
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
