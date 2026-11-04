using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

public class OptimizationPassTests
{
    [Fact]
    public void SizePass_RemovesRedundantConsecutiveLoads()
    {
        var block = new Block("main");
        block.Emit(LDA_abs(0x0325));
        block.Emit(LDA_abs(0x0325));
        block.Emit(STA_abs(0x0326));

        Assert.True(SizeOptimizationPass.OptimizeBlock(block));
        Assert.Equal(2, block.Count);
        Assert.Equal(Opcode.LDA, block[0].Opcode);
        Assert.Equal(Opcode.STA, block[1].Opcode);
    }

    [Fact]
    public void SizePass_PreservesStoreReloadForFlags()
    {
        // STA does not set NZ; a following LDA may exist solely to feed a branch.
        var block = new Block("main");
        block.Emit(STA_abs(0x0325));
        block.Emit(LDA_abs(0x0325));
        block.Emit(BEQ("done"));
        block.Emit(NOP(), "done");

        Assert.False(SizeOptimizationPass.OptimizeBlock(block));
        Assert.Equal(4, block.Count);
    }

    [Fact]
    public void SizePass_PreservesLabeledInstructions()
    {
        var block = new Block("main");
        block.Emit(LDA_abs(0x0325), "keep");
        block.Emit(LDA_abs(0x0325));

        Assert.True(SizeOptimizationPass.OptimizeBlock(block));
        Assert.Equal(1, block.Count);
        Assert.Equal("keep", block.GetLabelAt(0));
    }

    [Fact]
    public void SizePass_DoesNotCrossBasicBlockEdge()
    {
        var block = new Block("main");
        block.Emit(LDA_abs(0x0325));
        block.Emit(LDA_abs(0x0325), "edge");

        Assert.False(SizeOptimizationPass.OptimizeBlock(block));
        Assert.Equal(2, block.Count);
    }

    [Fact]
    public void SizePass_RemovesJumpToNext()
    {
        var block = new Block("main");
        block.Emit(JMP("next"));
        block.Emit(LDA(1), "next");

        Assert.True(SizeOptimizationPass.OptimizeBlock(block));
        Assert.Equal(1, block.Count);
        Assert.Equal(Opcode.LDA, block[0].Opcode);
        Assert.Equal("next", block.GetLabelAt(0));
    }

    [Fact]
    public void SizePass_RemovesNop()
    {
        var block = new Block("main");
        block.Emit(NOP());
        block.Emit(LDA(1));

        Assert.True(SizeOptimizationPass.OptimizeBlock(block));
        Assert.Equal(1, block.Count);
        Assert.Equal(Opcode.LDA, block[0].Opcode);
    }

    [Fact]
    public void EfficiencyPass_RemovesRedundantTaxTxa()
    {
        var block = new Block("main");
        block.Emit(TAX());
        block.Emit(TXA());
        block.Emit(STA_abs(0x0325));

        Assert.True(EfficiencyOptimizationPass.OptimizeBlock(block));
        Assert.Equal(2, block.Count);
        Assert.Equal(Opcode.TAX, block[0].Opcode);
        Assert.Equal(Opcode.STA, block[1].Opcode);
    }

    [Fact]
    public void EfficiencyPass_RemovesDuplicateClc()
    {
        var block = new Block("main");
        block.Emit(CLC());
        block.Emit(CLC());
        block.Emit(ADC(1));

        Assert.True(EfficiencyOptimizationPass.OptimizeBlock(block));
        Assert.Equal(2, block.Count);
        Assert.Equal(Opcode.CLC, block[0].Opcode);
        Assert.Equal(Opcode.ADC, block[1].Opcode);
    }

    [Fact]
    public void Pipeline_InvalidatesAddressesWhenChanged()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };
        var block = new Block("main");
        block.Emit(LDA(0x10));
        block.Emit(LDA(0x10));
        program.AddMainProgram(block);
        program.ResolveAddresses();

        Assert.True(OptimizationPipeline.Run(program, new SizeOptimizationPass()));
        Assert.Equal(1, block.Count);
    }
}
