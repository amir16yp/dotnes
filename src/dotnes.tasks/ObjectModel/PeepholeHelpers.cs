namespace dotnes.ObjectModel;

/// <summary>
/// Shared helpers for label-aware, fail-closed 6502 peephole rewrites.
/// </summary>
static class PeepholeHelpers
{
    public static bool SameLoadStoreTarget(Instruction a, Instruction b)
    {
        if (a.Mode != b.Mode)
            return false;
        return Equals(a.Operand, b.Operand);
    }

    public static bool IsLoad(Opcode opcode) =>
        opcode is Opcode.LDA or Opcode.LDX or Opcode.LDY;

    public static bool IsStore(Opcode opcode) =>
        opcode is Opcode.STA or Opcode.STX or Opcode.STY;

    public static Opcode? MatchingLoad(Opcode store) => store switch
    {
        Opcode.STA => Opcode.LDA,
        Opcode.STX => Opcode.LDX,
        Opcode.STY => Opcode.LDY,
        _ => null,
    };

    /// <summary>
    /// Removes instruction <paramref name="index"/> when it has no label.
    /// Labeled instructions are never removed (basic-block barriers).
    /// </summary>
    public static bool TryRemoveUnlabeled(Block block, int index)
    {
        if (index < 0 || index >= block.Count)
            return false;
        if (block.GetLabelAt(index) is not null)
            return false;
        block.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// True when a JMP at <paramref name="index"/> targets the immediately following instruction.
    /// </summary>
    public static bool IsJumpToNext(Block block, int index)
    {
        if (index < 0 || index + 1 >= block.Count)
            return false;
        var jmp = block[index];
        if (jmp.Opcode != Opcode.JMP || jmp.Operand is not LabelOperand label)
            return false;
        return block.GetLabelAt(index + 1) == label.Label;
    }
}
