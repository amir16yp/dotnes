namespace dotnes.ObjectModel;

/// <summary>
/// ROM-byte reductions that preserve 6502 semantics. Never deletes labeled
/// instructions and never crosses basic-block edges.
/// </summary>
public sealed class SizeOptimizationPass : IOptimizationPass
{
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
                if (TryRemoveRedundantLoad(block, i)
                    || TryRemoveNop(block, i)
                    || TryRemoveJumpToNext(block, i))
                {
                    progress = true;
                    changed = true;
                    break; // restart after structural change
                }
            }
        } while (progress);
        return changed;
    }

    static bool TryRemoveRedundantLoad(Block block, int i)
    {
        if (i + 1 >= block.Count)
            return false;
        // Next instruction starts a new block — cannot fold across the edge.
        if (block.GetLabelAt(i + 1) is not null)
            return false;

        var first = block[i];
        var second = block[i + 1];
        if (!PeepholeHelpers.IsLoad(first.Opcode) || first.Opcode != second.Opcode)
            return false;
        if (!PeepholeHelpers.SameLoadStoreTarget(first, second))
            return false;
        // Drop the earlier load when unlabeled; otherwise drop the later duplicate.
        // Both LDAs set identical NZ flags, so either removal preserves flag users.
        if (PeepholeHelpers.TryRemoveUnlabeled(block, i))
            return true;
        return PeepholeHelpers.TryRemoveUnlabeled(block, i + 1);
    }

    static bool TryRemoveNop(Block block, int i)
    {
        if (block[i].Opcode != Opcode.NOP)
            return false;
        return PeepholeHelpers.TryRemoveUnlabeled(block, i);
    }

    static bool TryRemoveJumpToNext(Block block, int i)
    {
        if (!PeepholeHelpers.IsJumpToNext(block, i))
            return false;
        return PeepholeHelpers.TryRemoveUnlabeled(block, i);
    }
}
