namespace dotnes.ObjectModel;

/// <summary>
/// Post-emission rewrite over a fully built <see cref="Program6502"/>.
/// Passes must preserve observable 6502 semantics and fail closed on uncertainty.
/// </summary>
public interface IOptimizationPass
{
    /// <summary>
    /// Mutates blocks in place. Returns true if any instruction was changed or removed.
    /// </summary>
    bool Optimize(Program6502 program);
}
