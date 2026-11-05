namespace dotnes;

/// <summary>
/// How the emitted project should rebuild the ROM.
/// </summary>
enum PackMode
{
    /// <summary>IL→6502 transpile of recovered C#.</summary>
    Transpile,
    /// <summary>Pack original PRG bytes via NESPackMode=RawPrg.</summary>
    RawPrg,
}

/// <summary>
/// Forced pack mode override from the CLI, or Auto to detect.
/// </summary>
enum PackModePreference
{
    Auto,
    Transpile,
    RawPrg,
}

/// <summary>
/// Soft-CPU recovery presentation. Optimizer is on by default; pass <c>--exact</c> to opt out.
/// Does not delete discovered routines (no dead-code elimination).
/// </summary>
enum RecoveryStyle
{
    /// <summary>
    /// Cleaner structured C# (loops, folded imm+poke) with the same soft-CPU semantics.
    /// Not a byte-faithful encoding of the PRG — rebuild still uses prg.bin for identity.
    /// </summary>
    Optimized,

    /// <summary>
    /// More literal recovery (less structuring). Prefer with byte-identical RawPrg rebuilds.
    /// </summary>
    Exact,
}
