using Xunit.Abstractions;

namespace dotnes.tests;

public class LowLevelHardwareTests : RoslynTests
{
    public LowLevelHardwareTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void CpuOps_EmitImpliedInstructions()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            sei();
            nop();
            pha();
            pla();
            cli();
            while (true) ;
            """));
        Assert.Contains("78", hex); // SEI
        Assert.Contains("EA", hex); // NOP
        Assert.Contains("48", hex); // PHA
        Assert.Contains("68", hex); // PLA
        Assert.Contains("58", hex); // CLI
    }

    [Fact]
    public void OamDma_Writes4014()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            oam_dma(0x02);
            while (true) ;
            """));
        Assert.Contains("8D1440", hex); // STA $4014
    }

    [Fact]
    public void UxromSetPrgBank_Writes8000()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            uxrom_set_prg_bank(3);
            while (true) ;
            """));
        Assert.Contains("8D0080", hex); // STA $8000
    }

    [Fact]
    public void Mmc3SetPrgBank_WritesSelectAndData()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mmc3_set_prg_bank(0x06, 0x01);
            while (true) ;
            """));
        Assert.Contains("8D0080", hex); // STA $8000
        Assert.Contains("8D0180", hex); // STA $8001
    }

    [Fact]
    public void Sprite0Wait_EmitsBuiltin()
    {
        using var transpiler = BuildProgram(
            """
            sprite0_wait();
            while (true) ;
            """, out var program);
        Assert.NotNull(program.GetBlock("sprite0_wait"));
        Assert.Contains("sprite0_wait", transpiler.UsedMethods);
    }

    [Fact]
    public void Memfill_EmitsBuiltin()
    {
        using var transpiler = BuildProgram(
            """
            memfill(0x0300, 0x00, 16);
            while (true) ;
            """, out var program);
        Assert.NotNull(program.GetBlock("memfill"));
        Assert.Contains("memfill", transpiler.UsedMethods);
    }

    [Fact]
    public void ApuTriangleAndNoise_EmitRegisterWrites()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            apu_play_triangle(0x1AB, 0x7F);
            apu_stop_triangle();
            apu_play_noise(0x0C, 0x0F, false);
            apu_stop_noise();
            while (true) ;
            """));
        Assert.Contains("8D0840", hex); // STA $4008 triangle ctrl
        Assert.Contains("8D0C40", hex); // STA $400C noise ctrl
    }

    [Fact]
    public void NtAttrA_IsCompileTimeAddress()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            vram_adr(NT_ATTR_A(4, 4));
            while (true) ;
            """));
        // 0x23C0 | ((4>>2)<<3) | (4>>2) = 0x23C0 | 0x08 | 0x01 = 0x23C9
        Assert.Contains("A9C9", hex); // LDA #$C9
        Assert.Contains("A223", hex); // LDX #$23
    }

    [Fact]
    public void OamClearFast_LinksBuiltin()
    {
        using var transpiler = BuildProgram(
            """
            oam_clear_fast();
            while (true) ;
            """, out var program);
        Assert.NotNull(program.GetBlock("oam_clear_fast"));
    }

    [Fact]
    public void RandAlias_UsesRand8()
    {
        using var transpiler = BuildProgram(
            """
            byte r = rand();
            poke(0x6000, r);
            while (true) ;
            """, out var program);
        Assert.Contains("rand", transpiler.NesLibImplementedMethods);
        Assert.Contains("rand8", transpiler.UsedMethods);
    }
}
