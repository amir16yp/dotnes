namespace dotnes.Disasm;

/// <summary>
/// Recursive-descent 6502 disassembler with mapper-aware PRG banking.
/// Work items carry (cpu address, switchable bank). Bus-conflict and MMC2/MMC4
/// PRG-register writes update the bank when the stored immediate is known.
/// </summary>
sealed class Disassembler
{
    readonly NESRomReader _rom;
    readonly PrgBankModel _banks;
    readonly Dictionary<int, DecodedInstruction> _byOffset = new();
    readonly Dictionary<(int Bank, ushort Address), DecodedInstruction> _bySite = new();
    readonly HashSet<ushort> _codeAddresses = new();
    readonly HashSet<(int Bank, ushort Address)> _entryPoints = new();

    public Disassembler(NESRomReader rom)
    {
        _rom = rom;
        _banks = new PrgBankModel(rom);
    }

    public PrgBankModel Banks => _banks;
    public IReadOnlyDictionary<int, DecodedInstruction> InstructionsByOffset => _byOffset;
    public IReadOnlyDictionary<(int Bank, ushort Address), DecodedInstruction> Instructions => _bySite;

    /// <summary>CPU addresses that contain code in at least one bank.</summary>
    public IReadOnlyCollection<ushort> CodeAddresses => _codeAddresses;

    public IReadOnlyCollection<(int Bank, ushort Address)> EntryPoints => _entryPoints;

    /// <summary>Flat view keyed by CPU address in the fixed/last window (compat helpers).</summary>
    public bool TryGetInstruction(int bank, ushort address, out DecodedInstruction insn) =>
        _bySite.TryGetValue((bank, address), out insn);

    public bool TryGetAny(ushort address, out DecodedInstruction insn)
    {
        if (_banks.IsFixedCpuAddress(address))
        {
            int site = _banks.SiteBank(address, _banks.FixedBank);
            if (_bySite.TryGetValue((site, address), out insn))
                return true;
        }

        foreach (var kv in _bySite)
        {
            if (kv.Key.Address == address)
            {
                insn = kv.Value;
                return true;
            }
        }

        insn = default;
        return false;
    }

    public void Analyze()
    {
        var work = new Queue<(ushort Cpu, int Bank)>();
        // Vectors live in the fixed bank; start with bank 0 as the switchable window default.
        Enqueue(work, _rom.ResetVector, 0);
        Enqueue(work, _rom.NmiVector, 0);
        Enqueue(work, _rom.IrqVector, 0);
        Trace(work);
    }

    /// <summary>
    /// Continue recursive descent from extra entry points (e.g. absolute loads of controller RAM
    /// discovered after joypad poll recovery). Already-traced PRG bytes are skipped.
    /// </summary>
    public void ContinueFrom(IEnumerable<(ushort Cpu, int Bank)> entries)
    {
        var work = new Queue<(ushort Cpu, int Bank)>();
        foreach (var (cpu, bank) in entries)
            Enqueue(work, cpu, bank);
        Trace(work);
    }

    void Trace(Queue<(ushort Cpu, int Bank)> work)
    {
        while (work.Count > 0)
        {
            var (pc, bank) = work.Dequeue();
            byte? lastImmA = null, lastImmX = null, lastImmY = null;

            while (_banks.TryMap(pc, bank, out int offset))
            {
                if (!_byOffset.TryAdd(offset, default))
                    break; // already traced this PRG byte as code

                byte opcode = _rom.PrgRom[offset];
                int size = OpcodeTable.GetSize(opcode);
                byte? op1 = size >= 2 && offset + 1 < _rom.PrgRom.Length ? _rom.PrgRom[offset + 1] : null;
                byte? op2 = size >= 3 && offset + 2 < _rom.PrgRom.Length ? _rom.PrgRom[offset + 2] : null;

                int siteBank = _banks.SiteBank(pc, bank);
                var insn = new DecodedInstruction(pc, opcode, op1, op2, siteBank, offset, bank);
                _byOffset[offset] = insn;
                _bySite[(siteBank, pc)] = insn;
                _codeAddresses.Add(pc);

                if (!OpcodeTable.IsOfficial(opcode))
                {
                    pc = (ushort)(pc + 1);
                    lastImmA = lastImmX = lastImmY = null;
                    continue;
                }

                UpdateImmediates(opcode, op1, ref lastImmA, ref lastImmX, ref lastImmY);

                if (IsAbsStore(opcode) && op1.HasValue && op2.HasValue)
                {
                    ushort storeAddr = (ushort)(op1.Value | (op2.Value << 8));
                    byte? imm = opcode switch
                    {
                        0x8D => lastImmA,
                        0x8E => lastImmX,
                        0x8C => lastImmY,
                        _ => null,
                    };
                    if (imm.HasValue)
                    {
                        // Bus-conflict mappers: any write to $8000-$FFFF selects PRG bank
                        if (_banks.BusConflictBankSwitch && storeAddr >= 0x8000)
                            bank = _banks.ExtractPrgBankFromBusWrite(imm.Value);
                        // MMC2/MMC4: PRG bank register at $A000-$AFFF
                        else if (_banks.IsMmc2StylePrgRegisterWrite(storeAddr))
                            bank = _banks.MaskBank(imm.Value);
                        // NINA-003/006 / 113: packed bank select at $4100-$5FFF
                        else if (_banks.IsNinaBankRegisterWrite(storeAddr))
                            bank = _banks.ExtractPrgBankFromNinaWrite(imm.Value);
                        // VRC1/2/4/7: PRG0 register near $8000
                        else if (_banks.IsVrcStylePrg0RegisterWrite(storeAddr))
                            bank = _banks.MaskBank(imm.Value);
                    }
                }

                foreach (ushort target in GetControlFlowTargets(insn))
                    EnqueueControlFlow(work, target, bank);

                if (IsTerminator(opcode))
                    break;

                pc = (ushort)(pc + size);
            }
        }
    }

    void EnqueueControlFlow(Queue<(ushort Cpu, int Bank)> work, ushort target, int bank)
    {
        if (target < 0x8000)
            return;

        // Path-sensitive: follow the switchable bank known at this edge.
        // Full static coverage comes from exploring every bank-switch edge the
        // ROM actually performs (LDA #imm / STA mapper), not from spraying all
        // banks at every switchable jump (that decodes data as code).
        Enqueue(work, target, bank);
    }

    void Enqueue(Queue<(ushort Cpu, int Bank)> work, ushort address, int bank)
    {
        if (address < 0x8000)
            return;
        int siteBank = _banks.SiteBank(address, bank);
        _entryPoints.Add((siteBank, address));
        work.Enqueue((address, bank));
    }

    public string GenerateListing()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("; Auto-generated disassembly — not required for SoftCpu rebuild");
        sb.AppendLine($"; Mapper={_rom.Mapper}, PRG banks={_rom.PrgBanks}");

        int listingBanks = _banks.SwitchableBankCount;
        for (int bank = 0; bank < listingBanks; bank++)
        {
            sb.AppendLine();
            sb.AppendLine($"; ---- PRG bank {bank} ($8000 view) ----");
            sb.AppendLine($".segment \"BANK{bank:X2}\"");

            int baseOff = bank * _banks.BankSize;
            int end = Math.Min(baseOff + _banks.BankSize, _rom.PrgRom.Length);
            int offset = baseOff;
            while (offset < end)
            {
                ushort cpu = (ushort)(0x8000 + (offset - baseOff));
                // Fixed last window also appears at $C000 (16K) / $A000 (MMC2 8K units in last banks)
                if (!_banks.HasSwitchable32KWindow
                    && bank == _banks.FixedBank
                    && listingBanks > 1
                    && _banks.BankSize == 0x4000)
                    cpu = (ushort)(0xC000 + (offset - baseOff));

                if (_entryPoints.Contains((bank, cpu)) || _byOffset.ContainsKey(offset))
                    sb.AppendLine($"L{bank:X2}_{cpu:X4}:");

                if (_byOffset.TryGetValue(offset, out var insn) && insn.IsOfficial)
                {
                    sb.AppendLine($"    {FormatInstruction(insn)}");
                    offset += insn.Size;
                    continue;
                }

                sb.AppendLine($"    .byte ${_rom.PrgRom[offset]:X2}");
                offset++;
            }
        }

        return sb.ToString();
    }

    static void UpdateImmediates(
        byte opcode, byte? op1,
        ref byte? lastImmA, ref byte? lastImmX, ref byte? lastImmY)
    {
        switch (opcode)
        {
            case 0xA9: lastImmA = op1; break;
            case 0xA2: lastImmX = op1; break;
            case 0xA0: lastImmY = op1; break;
            case 0xAA: lastImmX = lastImmA; break;
            case 0xA8: lastImmY = lastImmA; break;
            case 0x8A: lastImmA = lastImmX; break;
            case 0x98: lastImmA = lastImmY; break;
            case 0x8D or 0x8E or 0x8C or 0x85 or 0x86 or 0x84:
                break; // stores keep last imm
            case 0x20 or 0x4C or 0x6C:
                lastImmA = lastImmX = lastImmY = null;
                break;
            default:
                if (opcode is not (0x18 or 0x38 or 0x58 or 0x78 or 0xB8 or 0xD8 or 0xF8 or 0xEA
                    or 0xE8 or 0xC8 or 0xCA or 0x88))
                    lastImmA = lastImmX = lastImmY = null;
                break;
        }
    }

    static bool IsAbsStore(byte opcode) => opcode is 0x8D or 0x8E or 0x8C;

    static IEnumerable<ushort> GetControlFlowTargets(DecodedInstruction insn)
    {
        byte op = insn.Opcode;
        if (op == 0x20 || op == 0x4C)
        {
            if (insn.Op1.HasValue && insn.Op2.HasValue)
                yield return insn.AbsoluteTarget;
            yield break;
        }

        if (op is 0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0)
        {
            if (insn.Op1.HasValue)
                yield return (ushort)(insn.Address + 2 + (sbyte)insn.Op1.Value);
        }
    }

    static bool IsTerminator(byte opcode) =>
        opcode is 0x00 or 0x40 or 0x60 or 0x4C or 0x6C;

    static string FormatInstruction(DecodedInstruction insn)
    {
        string mnemonic = insn.Mnemonic;
        int size = insn.Size;
        if (size == 1)
            return mnemonic;
        if (size == 2 && insn.Op1.HasValue)
        {
            return insn.Opcode switch
            {
                0x09 or 0x29 or 0x49 or 0x69 or 0xA0 or 0xA2 or 0xA9
                    or 0xC0 or 0xC9 or 0xE0 or 0xE9
                    => $"{mnemonic} #${insn.Op1.Value:X2}",
                0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0
                    => $"{mnemonic} L{insn.Bank:X2}_{(ushort)(insn.Address + 2 + (sbyte)insn.Op1.Value):X4}",
                _ => $"{mnemonic} ${insn.Op1.Value:X2}",
            };
        }

        if (size == 3 && insn.Op1.HasValue && insn.Op2.HasValue)
        {
            ushort addr = insn.AbsoluteTarget;
            if (insn.Opcode is 0x20 or 0x4C)
                return $"{mnemonic} L{insn.Bank:X2}_{addr:X4}";
            return $"{mnemonic} ${addr:X4}";
        }

        return mnemonic;
    }
}
