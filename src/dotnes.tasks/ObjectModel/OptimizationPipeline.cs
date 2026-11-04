namespace dotnes.ObjectModel;

/// <summary>
/// Runs one or more <see cref="IOptimizationPass"/> instances and invalidates
/// resolved addresses when any pass mutates the program.
/// </summary>
public static class OptimizationPipeline
{
    public static bool Run(Program6502 program, params IOptimizationPass[] passes)
    {
        if (program is null)
            throw new ArgumentNullException(nameof(program));
        if (passes is null || passes.Length == 0)
            return false;

        bool changed = false;
        foreach (var pass in passes)
        {
            if (pass is null)
                throw new ArgumentException("Optimization passes must not contain null entries.", nameof(passes));
            changed |= pass.Optimize(program);
        }

        if (changed)
            program.InvalidateAddresses();
        return changed;
    }
}
