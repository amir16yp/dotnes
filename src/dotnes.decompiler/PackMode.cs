namespace dotnes;

/// <summary>
/// How the emitted project should rebuild the ROM.
/// </summary>
enum PackMode
{
    /// <summary>IL→6502 transpile of recovered C#.</summary>
    Transpile,
    /// <summary>
    /// Soft-CPU Program.cs is code authority; lower to 6502 at commercial addresses.
    /// <c>prg.bin</c> remains the data/gap bridge for unrecovered spans.
    /// </summary>
    SoftCpu,
}

/// <summary>
/// Forced pack mode override from the CLI, or Auto to detect.
/// </summary>
enum PackModePreference
{
    Auto,
    Transpile,
    SoftCpu,
}

/// <summary>
/// Soft-CPU recovery presentation. Optimizer is on by default; pass <c>--exact</c> to opt out.
/// Does not delete discovered routines (no dead-code elimination).
/// </summary>
enum RecoveryStyle
{
    /// <summary>
    /// Cleaner structured C# (loops, folded imm+poke) with the same soft-CPU semantics.
    /// SoftCpu rebuild lowers this; <c>prg.bin</c> fills data gaps.
    /// </summary>
    Optimized,

    /// <summary>
    /// More literal recovery (less structuring). Prefer with SoftCpu size fidelity.
    /// </summary>
    Exact,
}
