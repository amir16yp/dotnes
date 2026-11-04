using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class MappersTests : RoslynTests
{
    public MappersTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Poke_To_MMC3_Registers()
    {
        // poke() to MMC3 mapper registers should emit STA to correct addresses
        var bytes = GetProgramBytes(
            """
            poke(0x8000, 0x06);
            poke(0x8001, 0x00);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // STA $8000 = 8D0080
        Assert.Contains("8D0080", hex);
        // STA $8001 = 8D0180
        Assert.Contains("8D0180", hex);
    }

    [Fact]
    public void CnromSetChrBank_EmitsStaToMapper()
    {
        // cnrom_set_chr_bank(byte) should emit STA $8000 to switch CHR bank
        var bytes = GetProgramBytes(
            """
            cnrom_set_chr_bank(0);
            cnrom_set_chr_bank(1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CNROM hex: {hex}");

        // LDA #$00 = A900, STA $8000 = 8D0080
        Assert.Contains("A9008D0080", hex);
        // LDA #$01 = A901, STA $8000 = 8D0080
        Assert.Contains("A9018D0080", hex);
    }

    [Fact]
    public void Mmc3SetChrBank_EmitsRegAndBankWrites()
    {
        // mmc3_set_chr_bank(byte reg, byte bank) should emit:
        // LDA #reg, STA $8000 (bank select), LDA #bank, STA $8001 (bank data)
        var bytes = GetProgramBytes(
            """
            mmc3_set_chr_bank(0x00, 0x00);
            mmc3_set_chr_bank(0x02, 0x09);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MMC3 CHR hex: {hex}");

        // First call: LDA #$00, STA $8000, LDA #$00, STA $8001
        Assert.Contains("A9008D0080A9008D0180", hex);
        // Second call: LDA #$02, STA $8000, LDA #$09, STA $8001
        Assert.Contains("A9028D0080A9098D0180", hex);
    }

    [Fact]
    public void Mmc3SetChrBank_SupportsLocalBankArg()
    {
        // mmc3_set_chr_bank with bank from a local variable should emit
        // LDA #reg, STA $8000, LDA $bank_addr, STA $8001.
        var bytes = GetProgramBytes(
            """
            byte bank = 9;
            mmc3_set_chr_bank(0x02, bank);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Mmc3SetChrBank local bank hex: {hex}");

        // LDA #$02, STA $8000 (register select)
        Assert.Contains("A9028D0080", hex);
        // STA $8001 (bank data write)
        Assert.Contains("8D0180", hex);
    }

    [Fact]
    public void Mmc1Write_EmitsShiftRegisterProtocol()
    {
        // mmc1_write(0x8000, 0x0C) should emit:
        // LDA #$80, STA $8000 (reset)
        // LDA #$0C, STA $8000, LSR A, STA $8000, LSR A, STA $8000, LSR A, STA $8000, LSR A, STA $8000
        var bytes = GetProgramBytes(
            """
            mmc1_write(0x8000, 0x0C);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Reset: LDA #$80 (A980), STA $8000 (8D0080)
        Assert.Contains("A980" + "8D0080", hex);
        // Value load + 5-bit serial write: LDA #$0C, STA $8000, LSR A, STA $8000 (×4), LSR A, STA $8000
        // Contiguous pattern: A90C 8D0080 4A 8D0080 4A 8D0080 4A 8D0080 4A 8D0080
        Assert.Contains("A90C" + "8D0080" + "4A" + "8D0080" + "4A" + "8D0080" + "4A" + "8D0080" + "4A" + "8D0080", hex);
    }

    [Fact]
    public void Mmc1SetPrgBank_EmitsCSharpWrapper()
    {
        using var transpiler = BuildProgram(
            """
            mmc1_set_prg_bank(2);
            ppu_on_all();
            while (true) ;
            """, out var program);

        Assert.Contains("mmc1_set_prg_bank", transpiler.NesLibImplementedMethods);
        Assert.Single(program.Blocks, b => b.Label == "mmc1_set_prg_bank");
        Assert.NotNull(program.GetBlock("mmc1_set_prg_bank"));
        // Serial writes to $E000 live in the transpiled wrapper body
        Assert.Contains("8D00E0", Convert.ToHexString(program.ToBytes()));
    }

    [Fact]
    public void Mmc1SetMirroring_EmitsCSharpWrapper()
    {
        using var transpiler = BuildProgram(
            """
            mmc1_set_mirroring((byte)MMC1Mirror.Vertical | MMC1_PRG_FIX_LAST);
            ppu_on_all();
            while (true) ;
            """, out var program);

        Assert.Contains("mmc1_set_mirroring", transpiler.NesLibImplementedMethods);
        Assert.NotNull(program.GetBlock("mmc1_set_mirroring"));
        Assert.Contains("8D0080", Convert.ToHexString(program.ToBytes()));
    }

    [Fact]
    public void Mmc1SetChrBank_EmitsCSharpWrapper()
    {
        using var transpiler = BuildProgram(
            """
            mmc1_set_chr_bank(0, 1);
            ppu_on_all();
            while (true) ;
            """, out var program);

        Assert.Contains("mmc1_set_chr_bank", transpiler.NesLibImplementedMethods);
        Assert.NotNull(program.GetBlock("mmc1_set_chr_bank"));
        var hex = Convert.ToHexString(program.ToBytes());
        Assert.Contains("8D00A0", hex);
        Assert.Contains("8D00C0", hex);
    }
}
