using Xunit.Abstractions;

namespace dotnes.tests;

public class OptimizeCodeTests : RoslynTests
{
    public OptimizeCodeTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void CompilationOptions_OptimizeCodeDefaultsTrue()
    {
        Assert.True(new CompilationOptions().OptimizeCode);
    }

    [Fact]
    public void OptimizeCode_ArrayLoopExecutesCorrectly()
    {
        using var t = BuildProgram(
            """
            byte[] first = new byte[8];
            byte[] second = new byte[8];
            first[3] = 17;
            second[5] = 23;
            byte index = 3;
            byte frame = 0;
            while (frame < 2)
            {
                first[index] = (byte)(first[index] + second[index + 2]);
                frame++;
            }
            poke(0x6000, first[3]);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """,
            out var program, optimizeCode: true);
        const ushort stop = 0x7FF0;
        program.DefineExternalLabel("_test_stop", stop);
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress,
            program.Labels.TryResolve("main", out ushort entry) ? entry : (ushort)0);
        cpu.RunUntil(stop);
        // iter0: 17+23=40; iter1: 40+23=63 (second[5] unchanged)
        Assert.Equal(63, cpu.Memory[0x6000]);
        t.Dispose();
    }

    [Fact]
    public void OptimizeCode_PacksNonOverlappingLocalsWhenAcyclic()
    {
        // No backward branches — lifetime reuse is allowed. (Omit the usual
        // while(true) so HasBackwardBranch stays false for this measurement.)
        const string source = """
            byte a = 1;
            poke(0x2000, a);
            byte b = 2;
            poke(0x2000, b);
            byte c = 3;
            poke(0x2000, c);
            """;

        ushort localsOff, localsOn;
        using (var t = ReadProgram(source, optimizeCode: false))
            _ = t.BuildProgram6502(out _, out localsOff);
        using (var t = ReadProgram(source, optimizeCode: true))
            _ = t.BuildProgram6502(out _, out localsOn);

        Assert.True(localsOn < localsOff,
            $"Expected packing to reduce locals ({localsOn} vs unoptimized {localsOff}).");
    }

    [Fact]
    public void OptimizeCode_PreassignSharesSlotForNonOverlappingLocals()
    {
        var manager = new LocalVariableManager();
        manager.PreassignScalarLocal(0, NESConstants.LocalStackBase, isWord: false);
        manager.PreassignScalarLocal(1, NESConstants.LocalStackBase, isWord: false);

        Assert.Equal(NESConstants.LocalStackBase, manager.Locals[0].Address);
        Assert.Equal(NESConstants.LocalStackBase, manager.Locals[1].Address);
        Assert.Equal(1, manager.LocalCount);
    }

    [Fact]
    public void NesCompiler_OptimizeCodeFalse_MatchesTranspilerOptOut()
    {
        using var assembly = CompileAssembly("""
            byte a = 1;
            poke(0x2000, a);
            while (true) ;
            """);

        var viaOptions = NesCompiler.Compile(assembly, new CompilationOptions { OptimizeCode = false });
        assembly.Position = 0;
        using var transpiler = new Transpiler(assembly, Array.Empty<AssemblyReader>(), _logger)
        {
            OptimizeCode = false,
        };
        var viaTranspiler = transpiler.BuildProgram6502(out _, out _);
        Assert.Equal(viaTranspiler.ToBytes(), viaOptions.ToBytes());
    }

    [Fact]
    public void ComputeScalarPacking_SharesNonOverlappingBytes()
    {
        var il = new[]
        {
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Ldc_i4_1, 0),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Stloc_0, 1),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Ldloc_0, 2),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Pop, 3),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Ldc_i4_2, 4),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Stloc_1, 5),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Ldloc_1, 6),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Pop, 7),
        };

        using var dll = Utilities.GetResource("hello.release.dll");
        using var transpiler = new Transpiler(dll, Array.Empty<AssemblyReader>(), _logger)
        {
            OptimizeCode = true,
        };
        var packing = transpiler.ComputeScalarPacking(il, new ReflectionCache(), "main", optimize: true);
        Assert.Equal(1, packing.PackedScalarBytes);
        Assert.Equal(0, packing.RelativeOffsets[0]);
        Assert.Equal(0, packing.RelativeOffsets[1]);
    }

    [Fact]
    public void ComputeScalarPacking_DoesNotReuseAcrossBackwardBranches()
    {
        // Br_s target = Offset + 2 + (sbyte)Integer. From offset 4, Integer=-5 → target 1.
        var il = new[]
        {
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Ldc_i4_1, 0),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Stloc_0, 1),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Ldloc_0, 2),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Pop, 3),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Br_s, 4, Integer: unchecked((int)(sbyte)-5)),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Ldc_i4_2, 6),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Stloc_1, 7),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Ldloc_1, 8),
            new ILInstruction(System.Reflection.Metadata.ILOpCode.Pop, 9),
        };

        using var dll = Utilities.GetResource("hello.release.dll");
        using var transpiler = new Transpiler(dll, Array.Empty<AssemblyReader>(), _logger);
        var packing = transpiler.ComputeScalarPacking(il, new ReflectionCache(), "main", optimize: true);
        Assert.Equal(2, packing.PackedScalarBytes);
        Assert.NotEqual(packing.RelativeOffsets[0], packing.RelativeOffsets[1]);
    }
}
