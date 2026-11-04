namespace dotnes.ObjectModel;

/// <summary>
/// Cycle-focused peepholes that preserve semantics. Adjacent-only patterns;
/// labeled instructions are barriers.
/// </summary>
public sealed class EfficiencyOptimizationPass : IOptimizationPass
{
    static readonly HashSet<Opcode> DuplicateFlagOps = new()
    {
        Opcode.CLC, Opcode.SEC, Opcode.CLD, Opcode.SED, Opcode.CLI, Opcode.SEI, Opcode.CLV,
    };

    public bool Optimize(Program6502 program)
    {
        bool changed = false;
        foreach (var block in program.Blocks)
        {
            if (block.IsDataBlock)
                continue;
            changed |= OptimizeBlock(block);
        }
        return changed;
    }

    internal static bool OptimizeBlock(Block block)
    {
        bool changed = false;
        bool progress;
        do
        {
            progress = false;
            for (int i = 0; i < block.Count; i++)
            {
                if (TryRemoveRedundantTransfer(block, i)
                    || TryRemoveDuplicateFlag(block, i))
                {
                    progress = true;
                    changed = true;
                    break;
                }
            }
        } while (progress);
        return changed;
    }

    static bool TryRemoveRedundantTransfer(Block block, int i)
    {
        if (i + 1 >= block.Count)
            return false;
        if (block.GetLabelAt(i + 1) is not null)
            return false;

        var first = block[i];
        var second = block[i + 1];
        // TAX; TXA — A already holds the value TAX copied to X.
        if (first.Opcode == Opcode.TAX && second.Opcode == Opcode.TXA)
            return PeepholeHelpers.TryRemoveUnlabeled(block, i + 1);
        // TAY; TYA
        if (first.Opcode == Opcode.TAY && second.Opcode == Opcode.TYA)
            return PeepholeHelpers.TryRemoveUnlabeled(block, i + 1);
        // TXA; TAX — X already holds the value TXA copied to A.
        if (first.Opcode == Opcode.TXA && second.Opcode == Opcode.TAX)
            return PeepholeHelpers.TryRemoveUnlabeled(block, i + 1);
        // TYA; TAY
        if (first.Opcode == Opcode.TYA && second.Opcode == Opcode.TAY)
            return PeepholeHelpers.TryRemoveUnlabeled(block, i + 1);
        return false;
    }

    static bool TryRemoveDuplicateFlag(Block block, int i)
    {
        if (i + 1 >= block.Count)
            return false;
        if (block.GetLabelAt(i + 1) is not null)
            return false;

        var first = block[i];
        var second = block[i + 1];
        if (first.Opcode != second.Opcode || !DuplicateFlagOps.Contains(first.Opcode))
            return false;
        return PeepholeHelpers.TryRemoveUnlabeled(block, i + 1);
    }
}