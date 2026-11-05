namespace dotnes.Disasm;

/// <summary>
/// One decoded 6502 instruction at a CPU address within a specific PRG bank view.
/// <see cref="Bank"/> is the PRG bank the bytes were read from.
/// <see cref="SwitchableBank"/> is the UxROM (etc.) window bank in effect when this
/// instruction was reached — used to resolve subsequent JMP/JSR into $8000-$BFFF.
/// </summary>
readonly record struct DecodedInstruction(
    ushort Address,
    byte Opcode,
    byte? Op1,
    byte? Op2,
    int Bank,
    int PrgOffset,
    int SwitchableBank)
{
    public int Size => OpcodeTable.GetSize(Opcode);
    public bool IsOfficial => OpcodeTable.IsOfficial(Opcode);
    public string Mnemonic => OpcodeTable.GetMnemonic(Opcode);

    public ushort AbsoluteTarget =>
        Op1.HasValue && Op2.HasValue
            ? (ushort)(Op1.Value | (Op2.Value << 8))
            : (ushort)0;
}
