using Xunit.Abstractions;

namespace dotnes.tests;

public abstract class ExecutionTests(ITestOutputHelper output) : RoslynTests(output)
{
    // Source calls test_stop() before its terminal loop and declares it static extern.
    // The stop is only a breakpoint: all tested operations execute their emitted bytes.
    private protected Cpu6502 ExecuteProgram(string source, Action<Cpu6502>? initialize = null,
        int instructionLimit = 100000)
    {
        // Opt out: some fixtures plant values at LocalStackBase ($0325) assuming
        // bump layout. Optimized packing moves arrays; cover OptimizeCode=true in
        // OptimizeCodeTests / ArrayExecution instead.
        using var transpiler = BuildProgram(source, out var program, optimizeCode: false);
        const ushort stop = 0x7FF0;
        program.DefineExternalLabel("_test_stop", stop);
        byte[] bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
        initialize?.Invoke(cpu);
        cpu.RunUntil(stop, instructionLimit);
        return cpu;
    }
}
