namespace dotnes;

/// <summary>
/// Smarter recovery of RAM arrays and ROM constant tables from 6502 indexed access.
/// </summary>
sealed class ArrayRecovery
{
    public sealed record RamArray(ushort Base, int Size, string Name, int? LoopBound);
    public sealed record RomTable(ushort CpuAddress, int Length, string Name, bool PreferUshort, bool PreferNesColor);

    readonly Dictionary<ushort, int> _maxImmIndex = new();
    readonly Dictionary<ushort, int> _loopBoundByBase = new();
    readonly Dictionary<ushort, string> _usageHint = new(); // base → semantic name hint
    readonly HashSet<ushort> _romBases = new();
    readonly HashSet<ushort> _ramBases = new();

    public IReadOnlyList<RamArray> RamArrays { get; private set; } = Array.Empty<RamArray>();
    public IReadOnlyList<RomTable> RomTables { get; private set; } = Array.Empty<RomTable>();

    /// <summary>
    /// Analyze indexed accesses and for-loops to recover array shapes.
    /// </summary>
    public void Analyze(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions,
        IReadOnlyList<(ushort LocalAddress, byte InitValue, byte Limit, int InitIndex, int BodyStartIndex, int FooterStartIndex, int AfterLoopIndex)> forLoops,
        ushort localStackBase)
    {
        const ushort MaxLocal = 0x0800;
        var nonIndexedLocals = new HashSet<ushort>();
        byte? pendingImmX = null;
        byte? pendingImmY = null;
        ushort? pendingIndexLocal = null;

        for (int i = 0; i < instructions.Count; i++)
        {
            var (_, opcode, op1, op2) = instructions[i];

            // LDX #imm / LDY #imm
            if (opcode == 0xA2 && op1.HasValue) { pendingImmX = op1; pendingIndexLocal = null; continue; }
            if (opcode == 0xA0 && op1.HasValue) { pendingImmY = op1; pendingIndexLocal = null; continue; }

            // LDX/LDY absolute → index from local
            if (opcode is 0xAE or 0xAC && op1.HasValue && op2.HasValue)
            {
                pendingIndexLocal = (ushort)(op1.Value | (op2.Value << 8));
                pendingImmX = pendingImmY = null;
                continue;
            }

            if (!op1.HasValue || !op2.HasValue)
                continue;

            int size = opcode switch
            {
                // 3-byte abs / abs,x / abs,y family
                _ when IsAbsFamily(opcode) => 3,
                _ => 0,
            };
            if (size != 3)
                continue;

            ushort addr = (ushort)(op1.Value | (op2.Value << 8));

            if (IsIndexedAbs(opcode))
            {
                if (addr >= 0x8000)
                {
                    _romBases.Add(addr);
                    NoteImmIndex(addr, pendingImmX ?? pendingImmY);
                }
                else if (addr >= localStackBase && addr < MaxLocal)
                {
                    _ramBases.Add(addr);
                    NoteImmIndex(addr, pendingImmX ?? pendingImmY);

                    // Match for-loop whose counter is the index register source
                    if (pendingIndexLocal.HasValue)
                    {
                        foreach (var loop in forLoops)
                        {
                            if (loop.LocalAddress == pendingIndexLocal.Value && i >= loop.BodyStartIndex && i < loop.AfterLoopIndex)
                            {
                                int bound = loop.Limit;
                                if (!_loopBoundByBase.TryGetValue(addr, out int existing) || bound > existing)
                                    _loopBoundByBase[addr] = bound;
                            }
                        }
                    }
                }
            }
            else if (addr >= localStackBase && addr < MaxLocal)
            {
                nonIndexedLocals.Add(addr);
            }
        }

        // Build RAM arrays
        var ram = new List<RamArray>();
        var sortedRam = _ramBases.OrderBy(a => a).ToList();
        for (int i = 0; i < sortedRam.Count; i++)
        {
            ushort bas = sortedRam[i];
            int gapSize = i + 1 < sortedRam.Count
                ? sortedRam[i + 1] - bas
                : EstimateTailSize(bas, nonIndexedLocals);

            int size = gapSize;
            if (_loopBoundByBase.TryGetValue(bas, out int loopSize) && loopSize > 0)
                size = Math.Min(gapSize, loopSize);
            if (_maxImmIndex.TryGetValue(bas, out int maxImm))
                size = Math.Max(size, maxImm + 1);

            // Clamp: never exceed gap (avoid overlapping next array)
            size = Math.Clamp(size, 1, gapSize);

            string name = _usageHint.TryGetValue(bas, out var hint) ? hint : $"array_{bas:X4}";
            int? loopBound = _loopBoundByBase.TryGetValue(bas, out var lb) ? lb : null;
            ram.Add(new RamArray(bas, size, name, loopBound));
        }

        RamArrays = ram;

        // ROM tables: size from max immediate index + 1, default 16 if unknown
        var rom = new List<RomTable>();
        foreach (ushort bas in _romBases.OrderBy(a => a))
        {
            int len = _maxImmIndex.TryGetValue(bas, out int m) ? m + 1 : 16;
            string name = _usageHint.TryGetValue(bas, out var hint) ? hint : $"rom_{bas:X4}";
            bool preferUshort = hint is "music_pulse" or "music_triangle" or "ushorts";
            bool preferColor = hint is "palette" or "pal_bg" or "pal_spr" or "pal_all";
            rom.Add(new RomTable(bas, len, name, preferUshort, preferColor));
        }

        RomTables = rom;
    }

    /// <summary>
    /// Record that a recovered API used a pointer at <paramref name="cpuAddress"/> with known length/role.
    /// </summary>
    public void NotePointerUsage(ushort cpuAddress, int length, string role)
    {
        if (cpuAddress >= 0x8000)
        {
            _romBases.Add(cpuAddress);
            if (!_maxImmIndex.TryGetValue(cpuAddress, out int existing) || length - 1 > existing)
                _maxImmIndex[cpuAddress] = Math.Max(0, length - 1);
        }
        else
        {
            _ramBases.Add(cpuAddress);
        }

        _usageHint[cpuAddress] = role switch
        {
            "pal_bg" or "pal_spr" or "pal_all" => role,
            "vram_write" => $"data_{cpuAddress:X4}",
            "vram_unrle" => $"rle_{cpuAddress:X4}",
            "start_music" => $"music_{cpuAddress:X4}",
            "set_music_pulse_table" => "music_pulse",
            "set_music_triangle_table" => "music_triangle",
            "oam_meta_spr" or "oam_meta_spr_pal" => $"metasprite_{cpuAddress:X4}",
            _ => role,
        };
    }

    public string ArrayName(ushort baseAddress)
    {
        foreach (var a in RamArrays)
        {
            if (a.Base == baseAddress)
                return a.Name;
        }
        return _usageHint.TryGetValue(baseAddress, out var hint) ? hint : $"array_{baseAddress:X4}";
    }

    void NoteImmIndex(ushort bas, byte? imm)
    {
        if (!imm.HasValue)
            return;
        if (!_maxImmIndex.TryGetValue(bas, out int existing) || imm.Value > existing)
            _maxImmIndex[bas] = imm.Value;
    }

    static int EstimateTailSize(ushort bas, HashSet<ushort> nonIndexedLocals)
    {
        ushort upper = 0;
        foreach (var addr in nonIndexedLocals)
        {
            if (addr > bas && (upper == 0 || addr < upper))
                upper = addr;
        }
        return upper > 0 ? upper - bas : 1;
    }

    static bool IsIndexedAbs(byte opcode) => opcode is
        0x1D or 0x1E or 0x3D or 0x3E or 0x5D or 0x5E
        or 0x7D or 0x7E or 0x9D or 0xBC or 0xBD
        or 0xDD or 0xDE or 0xFD or 0xFE
        or 0x19 or 0x39 or 0x59 or 0x79 or 0x99
        or 0xB9 or 0xBE or 0xD9 or 0xF9;

    static bool IsAbsFamily(byte opcode) =>
        IsIndexedAbs(opcode)
        || opcode is 0x0D or 0x0E or 0x2C or 0x2D or 0x2E or 0x4D or 0x4E
            or 0x6D or 0x6E or 0x8C or 0x8D or 0x8E or 0xAC or 0xAD or 0xAE
            or 0xCC or 0xCD or 0xCE or 0xEC or 0xED or 0xEE
            or 0x20 or 0x4C; // JSR/JMP also 3-byte but ignored by callers via indexed check
}
