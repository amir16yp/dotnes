namespace dotnes.Disasm;

sealed class CfgBlock
{
    public ushort Start { get; init; }
    public int Bank { get; init; }
    public List<DecodedInstruction> Instructions { get; } = new();
    public List<(int Bank, ushort Address)> Successors { get; } = new();
}

/// <summary>
/// Builds basic blocks from a bank-aware disassembled instruction map.
/// </summary>
static class ControlFlowGraph
{
    public static IReadOnlyList<CfgBlock> Build(
        IReadOnlyDictionary<(int Bank, ushort Address), DecodedInstruction> instructions)
    {
        if (instructions.Count == 0)
            return Array.Empty<CfgBlock>();

        var blocks = new List<CfgBlock>();
        foreach (var bankGroup in instructions.GroupBy(kv => kv.Key.Bank).OrderBy(g => g.Key))
        {
            var map = bankGroup.ToDictionary(kv => kv.Key.Address, kv => kv.Value);
            blocks.AddRange(BuildBank(bankGroup.Key, map));
        }

        return blocks;
    }

    static IReadOnlyList<CfgBlock> BuildBank(int bank, Dictionary<ushort, DecodedInstruction> instructions)
    {
        var leaders = new HashSet<ushort>();
        var sorted = instructions.Keys.OrderBy(a => a).ToList();
        leaders.Add(sorted[0]);

        foreach (var addr in sorted)
        {
            var insn = instructions[addr];
            foreach (ushort t in BranchTargets(insn))
                leaders.Add(t);

            if (insn.Opcode is 0x20 or 0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0)
            {
                ushort fallthrough = (ushort)(addr + insn.Size);
                if (instructions.ContainsKey(fallthrough))
                    leaders.Add(fallthrough);
            }
        }

        var blocks = new List<CfgBlock>();
        var leaderList = leaders.OrderBy(a => a).ToList();
        for (int i = 0; i < leaderList.Count; i++)
        {
            ushort start = leaderList[i];
            ushort endExclusive = i + 1 < leaderList.Count
                ? leaderList[i + 1]
                : (ushort)(sorted[^1] + instructions[sorted[^1]].Size);
            var block = new CfgBlock { Start = start, Bank = bank };
            ushort pc = start;
            while (pc < endExclusive && instructions.TryGetValue(pc, out var insn))
            {
                block.Instructions.Add(insn);
                foreach (ushort t in BranchTargets(insn))
                    block.Successors.Add((bank, t));

                bool terminator = insn.Opcode is 0x00 or 0x40 or 0x60 or 0x4C or 0x6C;
                pc = (ushort)(pc + insn.Size);
                if (terminator)
                    break;
                if (insn.Opcode is 0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0)
                {
                    if (instructions.ContainsKey(pc))
                        block.Successors.Add((bank, pc));
                    break;
                }
            }

            blocks.Add(block);
        }

        return blocks;
    }

    static IEnumerable<ushort> BranchTargets(DecodedInstruction insn)
    {
        if (insn.Opcode is 0x20 or 0x4C)
        {
            if (insn.Op1.HasValue && insn.Op2.HasValue)
                yield return insn.AbsoluteTarget;
            yield break;
        }

        if (insn.Opcode is 0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0)
        {
            if (insn.Op1.HasValue)
                yield return (ushort)(insn.Address + 2 + (sbyte)insn.Op1.Value);
        }
    }
}
