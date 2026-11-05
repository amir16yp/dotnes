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

    [Fact]
    public void AxromSetPrgBank_EmitsStaToMapper()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            axrom_set_prg_bank(1);
            ppu_on_all();
            while (true) ;
            """)!);
        // LDA #$01, STA $8000
        Assert.Contains("A9018D0080", hex);
    }

    [Fact]
    public void BnromSetPrgBank_EmitsStaToMapper()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            bnrom_set_prg_bank(2);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9028D0080", hex);
    }

    [Fact]
    public void GxromSet_EmitsStaToMapper()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            gxrom_set(0x21);
            ppu_on_all();
            while (true) ;
            """)!);
        // LDA #$21, STA $8000
        Assert.Contains("A9218D0080", hex);
    }

    [Fact]
    public void GxromSetBanks_EmitsPackedWrite()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            gxrom_set_banks(2, 1);
            ppu_on_all();
            while (true) ;
            """)!);
        // Packed: (2 << 4) | 1 = 0x21 → LDA #$21, STA $8000
        Assert.Contains("A9218D0080", hex);
    }

    [Fact]
    public void Mmc2SetPrgBank_EmitsStaToA000()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mmc2_set_prg_bank(3);
            ppu_on_all();
            while (true) ;
            """)!);
        // LDA #$03, STA $A000
        Assert.Contains("A9038D00A0", hex);
    }

    [Fact]
    public void Mmc4SetPrgBank_EmitsStaToA000()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mmc4_set_prg_bank(1);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9018D00A0", hex);
    }

    [Fact]
    public void Mmc2SetChrBank0_EmitsFdFeStores()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mmc2_set_chr_bank0(0x04, 0x05);
            ppu_on_all();
            while (true) ;
            """)!);
        // LDA #$04, STA $B000, LDA #$05, STA $C000
        Assert.Contains("A9048D00B0A9058D00C0", hex);
    }

    [Fact]
    public void Mmc4SetChrBank1_EmitsFdFeStores()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mmc4_set_chr_bank1(0x06, 0x07);
            ppu_on_all();
            while (true) ;
            """)!);
        // LDA #$06, STA $D000, LDA #$07, STA $E000
        Assert.Contains("A9068D00D0A9078D00E0", hex);
    }

    [Fact]
    public void Mmc2SetMirroring_EmitsStaToF000()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mmc2_set_mirroring(1);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9018D00F0", hex);
    }

    [Fact]
    public void Mmc5SetPrgMode_EmitsStaTo5100()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mmc5_set_prg_mode(3);
            ppu_on_all();
            while (true) ;
            """)!);
        // LDA #$03, STA $5100
        Assert.Contains("A9038D0051", hex);
    }

    [Fact]
    public void Mmc5SetPrgBank_EmitsStaTo5114PlusReg()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mmc5_set_prg_bank(2, 0x0A);
            ppu_on_all();
            while (true) ;
            """)!);
        // LDA #$0A, STA $5116 ($5114 + 2)
        Assert.Contains("A90A8D1651", hex);
    }

    [Fact]
    public void Mmc5SetChrBank_EmitsStaTo5120PlusReg()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mmc5_set_chr_bank(1, 0x0B);
            ppu_on_all();
            while (true) ;
            """)!);
        // LDA #$0B, STA $5121
        Assert.Contains("A90B8D2151", hex);
    }

    [Fact]
    public void ColordreamsSetBanks_EmitsPackedWrite()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            colordreams_set_banks(2, 1);
            ppu_on_all();
            while (true) ;
            """)!);
        // NESDev CCCC LLPP: (chr<<4)|(prg&3) = (1<<4)|2 = 0x12
        Assert.Contains("A9128D0080", hex);
    }

    [Fact]
    public void CpromSetChrBank_EmitsStaTo8000()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            cprom_set_chr_bank(3);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9038D0080", hex);
    }

    [Fact]
    public void Unrom512AndCamerica_EmitStaTo8000()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            unrom512_set_prg_bank(4);
            camerica_set_prg_bank(5);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9048D0080", hex);
        Assert.Contains("A9058D0080", hex);
    }

    [Fact]
    public void HolydiverSet_EmitsStaTo8000()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            holydiver_set(0x41);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9418D0080", hex);
    }

    [Fact]
    public void NinaSetBanks_EmitsPackedWriteTo4100()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            nina_set_banks(2, 3);
            ppu_on_all();
            while (true) ;
            """)!);
        // (2 << 3) | 3 = 0x13 → LDA #$13, STA $4100
        Assert.Contains("A9138D0041", hex);
    }

    [Fact]
    public void JalecoSetChrBank_EmitsStaTo6000()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            jaleco_set_chr_bank(1);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9018D0060", hex);
    }

    [Fact]
    public void Fme7SetPrgBank_EmitsCommandAndParameter()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            fme7_set_prg_bank(9, 0x05);
            ppu_on_all();
            while (true) ;
            """)!);
        // LDA #$09, STA $8000, LDA #$05, STA $A000
        Assert.Contains("A9098D0080A9058D00A0", hex);
    }

    [Fact]
    public void Mapper15AndBandai_EmitStaTo8000()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper15_set(0x12);
            bandai_set_prg_bank(3);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9128D0080", hex);
        Assert.Contains("A9038D0080", hex);
    }

    [Fact]
    public void Vrc1SetPrgBank_EmitsStaToWindow()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            vrc1_set_prg_bank(0, 2);
            vrc1_set_prg_bank(1, 3);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9028D0080", hex);
        Assert.Contains("A9038D00A0", hex);
    }

    [Fact]
    public void Vrc24AndVrc7_EmitPrgBanks()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            vrc24_set_prg_bank(0, 1);
            vrc7_set_prg_bank(1, 4);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9018D0080", hex);
        Assert.Contains("A9048D00A0", hex);
    }

    [Fact]
    public void Vrc3AndVrc6_EmitPrgRegisters()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            vrc3_set_prg_bank(2);
            vrc6_set_prg16(1);
            vrc6_set_prg8(3);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9028D00F0", hex);
        Assert.Contains("A9018D0080", hex);
        Assert.Contains("A9038D00C0", hex);
    }

    [Fact]
    public void Namco163SetPrgBank_EmitsWindowRegisters()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            namco163_set_prg_bank(0, 1);
            namco163_set_prg_bank(2, 5);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9018D00E0", hex);
        Assert.Contains("A9058D00F0", hex);
    }

    [Fact]
    public void Action52Write_EmitsStaToRegister()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            action52_write(0x8000, 0x0A);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A90A8D0080", hex);
    }

    [Fact]
    public void Nina113SetBanks_EmitsPackedWrite()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            nina113_set_banks(1, 2);
            ppu_on_all();
            while (true) ;
            """)!);
        // (1 << 3) | 2 = 0x0A
        Assert.Contains("A90A8D0041", hex);
    }

    [Fact]
    public void Bandai16AndSunsoft_EmitRegisters()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            bandai16_set_prg_bank(2);
            sunsoft3_set_prg_bank(1);
            sunsoft184_set_chr_bank(3);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9028D0880", hex); // STA $8008
        Assert.Contains("A9018D0080", hex);
        Assert.Contains("A9038D0060", hex);
    }

    [Fact]
    public void IremTaitoJaleco18_EmitBanks()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            irem32_set_prg_bank(0, 1);
            taito33_set_prg_bank(1, 2);
            jaleco18_set_prg0(0x23);
            taito80_set_prg_bank(0, 4);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9018D0080", hex);
        Assert.Contains("A9028D0180", hex);
        Assert.Contains("A9038D0080", hex); // low nibble 3
        Assert.Contains("A9028D0180", hex); // high nibble 2 — also taito33
        Assert.Contains("A9048DF87E", hex); // STA $7EF8
    }

    [Fact]
    public void Namco88AndMulticart_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            namco88_set_bank(6, 3);
            mapper226_set(0x11);
            mapper241_set_prg_bank(2);
            bf9096_set(0x05);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9068D0080A9038D0180", hex);
        Assert.Contains("A9118D0080", hex);
        Assert.Contains("A9028D0080", hex);
        Assert.Contains("A9058D0080", hex);
    }

    [Fact]
    public void Batch5_AdditionalMappers_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            txc36_set_prg_bank(1);
            mapper47_set_outer(2);
            sachen133_set(0x0B);
            mapper156_set_prg_bank(3);
            mapper240_set(0x14);
            namcot95_set_bank(6, 1);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9018D0080", hex);
        Assert.Contains("A9028D0060", hex);
        Assert.Contains("A90B8D0041", hex);
        Assert.Contains("A9038D00C0", hex);
        Assert.Contains("A9148DFF5F", hex);
        Assert.Contains("A9068D0080A9018D0180", hex);
    }

    [Fact]
    public void Batch6_RemainingCommonMappers_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper37_set_outer(1);
            mapper40_set_prg_bank(2);
            taito82_set_prg_bank(3);
            vs99_set_chr_bank(4);
            mapper163_set_prg_bank(5);
            jy90_set_prg_bank(6, 7);
            sachen243_set(0x0C);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9018D0060", hex);
        Assert.Contains("A9028D0080", hex);
        Assert.Contains("A9038DF07E", hex);
        Assert.Contains("A9048D1640", hex);
        Assert.Contains("A9058D0050", hex);
        Assert.Contains("A9068D0080A9078D0180", hex);
        Assert.Contains("A90C8D0041", hex);
    }

    [Fact]
    public void Batch7_BroadCoverageHelpers_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper_bus_set(0x11);
            mapper_outer_set(0x22);
            ffe6_set_prg_bank(3);
            sachen147_set(0x0D);
            mapper31_set_prg_bank(4);
            mapper114_set_bank(6, 2);
            mapper27_set_prg_bank(1, 5);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9118D0080", hex);
        Assert.Contains("A9228D0060", hex);
        Assert.Contains("A9038D0080", hex);
        Assert.Contains("A90D8D0041", hex);
        Assert.Contains("A9048D0050", hex);
        Assert.Contains("A9068D0080A9028D0180", hex);
        Assert.Contains("A9058D00A0", hex);
    }

    [Fact]
    public void Batch8_NesDevCommercialGaps_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper21_set_prg_bank(0, 3);
            mapper24_set_prg16(4);
            taito48_set_prg_bank(1, 5);
            rambo64_set_bank(6, 2);
            un1rom_set_prg_bank(3);
            txsrom118_set_bank(7, 1);
            mapper180_set_prg_bank(2);
            mapper192_set_bank(6, 4);
            dxrom206_set_bank(6, 5);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9038D0080", hex); // VRC21 PRG0
        Assert.Contains("A9048D0080", hex); // VRC6a PRG16
        Assert.Contains("A9058D0180", hex); // Taito48 PRG1
        Assert.Contains("A9068D0080A9028D0180", hex); // RAMBO-1
        Assert.Contains("A90C8D0080", hex); // UN1ROM bank 3 << 2
        Assert.Contains("A9078D0080A9018D0180", hex); // TxSROM
        Assert.Contains("A9028D0080", hex); // Crazy Climber
        Assert.Contains("A9068D0080A9048D0180", hex); // Waixing 192
        Assert.Contains("A9068D0080A9058D0180", hex); // DxROM 206
    }

    [Fact]
    public void Batch9_SpecFixesAndMore_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            holydiver_set_banks(1, 4, 0);
            retcufrom29_set_banks(3, 2);
            mapper208_set_prg(0x11);
            mapper219_set_bank(6, 2);
            mapper183_set_prg_bank(0, 5);
            ppu_on_all();
            while (true) ;
            """)!);
        // Holy Diver: (4<<4)|(0<<3)|1 = 0x41
        Assert.Contains("A9418D0080", hex);
        // RET-CUFROM: (3<<2)|2 = 0x0E
        Assert.Contains("A90E8D0080", hex);
        Assert.Contains("A9118D0048", hex); // $4800
        Assert.Contains("A9068D0080A9028D0180", hex);
        Assert.Contains("A9058D0080", hex); // VRC183 PRG0
    }

    [Fact]
    public void Batch10_RemainingBoards_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper106_write(0x8008, 3);
            mapper134_set(0x0B);
            mapper134_set_bank(6, 2);
            mapper160_set_prg_bank(6, 3);
            mapper162_set_prg_low(0x15);
            mapper162_set_mode(0x04);
            mapper174_set(0x22);
            mapper187_set_outer(0x80);
            jy211_set_prg_bank(6, 1);
            racermate168_set(0x80);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9038D0880", hex); // $8008
        Assert.Contains("A90B8D0060", hex); // mapper 134 mode @$6000
        Assert.Contains("A9068D0080A9028D0180", hex); // mapper134_set_bank
        Assert.Contains("A9068D0080A9038D0180", hex); // mapper160 ≡ jy90
        Assert.Contains("A9158D0050", hex);
        Assert.Contains("A9048D0053", hex);
        Assert.Contains("A9228D0080", hex);
        Assert.Contains("A9808D0050", hex);
        Assert.Contains("A9068D0080A9018D0180", hex);
        Assert.Contains("A9808D0080", hex); // Racermate 168
    }

    [Fact]
    public void Batch11_NesDevContinuation_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper59_set(0x2A);
            mapper100_set_bank(6, 2);
            mapper108_set_prg_bank(4);
            mapper121_set_outer(0x80);
            mapper121_set_bank(7, 1);
            mapper123_set_outer(0x35);
            mapper123_set_bank(6, 3);
            mapper125_set_prg_bank(5);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A92A8D0080", hex); // 59 @$8000
        Assert.Contains("A9068D0080A9028D0180", hex); // 100
        Assert.Contains("A9048D0080", hex); // 108
        Assert.Contains("A9808D8051", hex); // 121 outer @$5180
        Assert.Contains("A9078D0080A9018D0180", hex); // 121 bank
        Assert.Contains("A9358D0058", hex); // 123 outer @$5800
        Assert.Contains("A9068D0080A9038D0180", hex); // 123 bank
        Assert.Contains("A9058D0060", hex); // 125 @$6000
    }

    [Fact]
    public void Batch11b_FdsAndAliases_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper120_set_prg_bank(3);
            mapper169_set(0x0A);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9038DFF41", hex); // $41FF
        Assert.Contains("A90A8D0080", hex); // 169 ≡ 15
    }

    [Fact]
    public void Batch12_AliasesAndNes2_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper109_set(0x05);
            mapper122_set_chr_bank(0x47);
            mapper126_set_outer(0x11);
            mapper126_set_bank(6, 2);
            mapper260_set_mode(0x04);
            mapper260_set_prg_base(0x0A);
            mapper269_set_outer(0x03);
            mapper264_set_outer(0x12);
            mapper264_set_mode(0x01);
            mapper331_write(0xE000, 0x0C);
            mapper534_set_bank(7, 1);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9058D0041", hex); // 109 @$4100
        Assert.Contains("A9478D0060", hex); // 122 ≡ 184 @$6000
        Assert.Contains("A9118D0060", hex); // 126 outer
        Assert.Contains("A9068D0080A9028D0180", hex); // 126 bank
        Assert.Contains("A9048D0050", hex); // 260 mode @$5000
        Assert.Contains("A90A8D0150", hex); // 260 prg @$5001
        Assert.Contains("A9038D0050", hex); // 269 outer
        Assert.Contains("A9128D0080", hex); // 264 outer
        Assert.Contains("A9018D0084", hex); // 264 mode @$8400
        Assert.Contains("A90C8D00E0", hex); // 331 @$E000
        Assert.Contains("A9078D0080A9018D0180", hex); // 534 bank
    }

    [Fact]
    public void Batch13_Nes2More_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper347_write(0x8000, 0x05);
            mapper365_set_prg_bank(2, 7);
            mapper371_set(0x53);
            mapper371_set_aux(0x01);
            mapper417_write(0x8001, 0x0A);
            mapper446_set_mode(0x81);
            mapper446_write(0x5001, 0x10);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9058D0080", hex); // 347 @$8000
        Assert.Contains("A9078D0280", hex); // 365 which=2 @$8002
        Assert.Contains("A9538D0050", hex); // 371 @$5000
        Assert.Contains("A9018D0051", hex); // 371 aux @$5100
        Assert.Contains("A90A8D0180", hex); // 417 @$8001
        Assert.Contains("A9818D0050", hex); // 446 mode
        Assert.Contains("A9108D0150", hex); // 446 @$5001
    }

    [Fact]
    public void Batch14_Nes2More_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper266_set_prg(0x0C);
            mapper292_set_prg(3);
            mapper292_set_bank(6, 2);
            mapper296_set_config(0x02);
            mapper296_set_outer(0x01);
            mapper351_set_chr_base(0x10);
            mapper351_set_prg_base(0x20);
            mapper351_set_mode(0x04);
            mapper359_set_prg_bank(1, 5);
            mapper359_set_prg_base(0x08);
            mapper375_write(0x8000, 0x11);
            mapper383_set_bank(7, 1);
            mapper452_write(0xC000, 0x22);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A90C8D0C90", hex); // 266 @$900C
        Assert.Contains("A9038D0060", hex); // 292 prg @$6000
        Assert.Contains("A9068D0080A9028D0180", hex); // 292 bank
        Assert.Contains("A9028D1D41", hex); // 296 @$411D
        Assert.Contains("A9018D2C41", hex); // 296 @$412C
        Assert.Contains("A9108D0050", hex); // 351 chr
        Assert.Contains("A9208D0150", hex); // 351 prg
        Assert.Contains("A9048D0250", hex); // 351 mode
        Assert.Contains("A9058D0180", hex); // 359 which=1
        Assert.Contains("A9088D0090", hex); // 359 base @$9000
        Assert.Contains("A9118D0080", hex); // 375
        Assert.Contains("A9078D0080A9018D0180", hex); // 383
        Assert.Contains("A9228D00C0", hex); // 452 @$C000
    }

    [Fact]
    public void Batch15_Nes2More_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper285_set(0x41);
            mapper286_write(0xA000, 0x03);
            mapper302_write(0xB000, 0x05);
            mapper305_write(0x8000, 0x00);
            mapper354_write(0xF000, 0x12);
            mapper357_set_prg(0x04);
            mapper357_set_aux(0x01);
            mapper368_set_prg(0x06);
            mapper380_write(0x8000, 0x21);
            mapper444_set_outer(0x0A);
            mapper444_set_bank(6, 2);
            mapper451_write(0xE000, 0x10);
            mapper454_write(0x8000, 0x07);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9418D0080", hex); // 285
        Assert.Contains("A9038D00A0", hex); // 286 @$A000
        Assert.Contains("A9058D00B0", hex); // 302 @$B000
        Assert.Contains("A9008D0080", hex); // 305
        Assert.Contains("A9128D00F0", hex); // 354 @$F000
        Assert.Contains("A9048D2240", hex); // 357 @$4022
        Assert.Contains("A9018D2041", hex); // 357 @$4120
        Assert.Contains("A9068D2240", hex); // 368
        Assert.Contains("A9218D0080", hex); // 380
        Assert.Contains("A90A8D0060", hex); // 444 outer
        Assert.Contains("A9068D0080A9028D0180", hex); // 444 bank
        Assert.Contains("A9108D00E0", hex); // 451 @$E000
        Assert.Contains("A9078D0080", hex); // 454
    }

    [Fact]
    public void Batch16_Nes2More_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper270_set_outer(0x02);
            mapper274_set_inner(0x05);
            mapper274_write(0xA000, 0x10);
            mapper304_set_prg(0x01);
            mapper315_set_outer(0x06);
            mapper315_set_bank(6, 2);
            mapper391_set_outer(0x0C);
            mapper398_write(0x8000, 0xC0);
            mapper429_set(0x14);
            mapper518_set_prg(0x03);
            mapper518_set_mode(0x01);
            mapper528_write(0xA009, 0x07);
            mapper542_set_prg_bank(0, 4);
            mapper543_set_outer(0x01);
            mapper544_set_prg2(0x08);
            mapper558_set_prg_low(0x0A);
            mapper558_set_prg_high(0x02);
            mapper558_set_mode(0x00);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9028D2C41", hex); // 270 @$412C
        Assert.Contains("A9058D0080", hex); // 274 inner
        Assert.Contains("A9108D00A0", hex); // 274 outer
        Assert.Contains("A9018D2740", hex); // 304 @$4027
        Assert.Contains("A9068D0068", hex); // 315 @$6800
        Assert.Contains("A9068D0080A9028D0180", hex); // 315 bank
        Assert.Contains("A90C8D0060", hex); // 391
        Assert.Contains("A9C08D0080", hex); // 398
        Assert.Contains("A9148D0080", hex); // 429
        Assert.Contains("A9038D0050", hex); // 518 prg
        Assert.Contains("A9018D0052", hex); // 518 mode
        Assert.Contains("A9078D09A0", hex); // 528 @$A009
        Assert.Contains("A9048D0080", hex); // 542 PRG0
        Assert.Contains("A9018D0050", hex); // 543
        Assert.Contains("A9088D009C", hex); // 544 @$9C00
        Assert.Contains("A90A8D0050", hex); // 558 low
        Assert.Contains("A9028D0051", hex); // 558 high
        Assert.Contains("A9008D0053", hex); // 558 mode
    }
}
