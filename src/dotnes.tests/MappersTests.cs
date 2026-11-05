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

    [Fact]
    public void Batch17_Nes2More_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper268_write(0x6000, 0x11);
            mapper268_set_bank(6, 2);
            mapper281_set_outer(0x03);
            mapper288_write(0x8000, 0x05);
            mapper289_set_mode(0x02);
            mapper289_set_outer(0x10);
            mapper289_set(0x07);
            mapper307_set_bank(6, 1);
            mapper319_set_chr(0x04);
            mapper319_set_prg(0x28);
            mapper342_write(0x5000, 0x0A);
            mapper344_set_outer(0x01);
            mapper348_set_outer(0x0C);
            mapper353_write(0x8080, 0x00);
            mapper449_write(0x8000, 0x21);
            mapper513_set_bank(7, 3);
            mapper526_write(0x8008, 0x05);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9118D0060", hex); // 268
        Assert.Contains("A9068D0080A9028D0180", hex); // 268 bank
        Assert.Contains("A9038D03D0", hex); // 281 @$D003
        Assert.Contains("A9058D0080", hex); // 288
        Assert.Contains("A9028D0060", hex); // 289 mode
        Assert.Contains("A9108D0160", hex); // 289 outer @$6001
        Assert.Contains("A9078D0080", hex); // 289 latch
        Assert.Contains("A9068D0080A9018D0180", hex); // 307
        Assert.Contains("A9048D0060", hex); // 319 chr
        Assert.Contains("A9288D0460", hex); // 319 prg @$6004
        Assert.Contains("A90A8D0050", hex); // 342
        Assert.Contains("A9018D0060", hex); // 344
        Assert.Contains("A90C8D0068", hex); // 348
        Assert.Contains("A9008D8080", hex); // 353 @$8080
        Assert.Contains("A9218D0080", hex); // 449
        Assert.Contains("A9078D0080A9038D0180", hex); // 513
        Assert.Contains("A9058D0880", hex); // 526 @$8008
    }

    [Fact]
    public void Batch18_Nes2More_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper295_set_outer(0x05);
            mapper309_set_prg_bank(3);
            mapper310_write(0xC000, 0x01);
            mapper312_set_prg_bank(2);
            mapper314_set_mode(0x80);
            mapper314_set_prg(0x43);
            mapper314_set(0x07);
            mapper322_set_outer(0x10);
            mapper322_set_bank(6, 2);
            mapper337_write(0x8000, 0x20);
            mapper350_write(0xC000, 0x03);
            mapper460_set_outer(0x04);
            mapper512_set_mode(0x02);
            mapper512_set_bank(7, 1);
            mapper514_set(0x11);
            mapper538_set_prg(0x0A);
            mapper548_set_latch(0x1C);
            mapper548_apply(0x00);
            mapper550_set_outer(0x03);
            mapper550_set(0x10);
            mapper551_set_mode(0x02);
            mapper551_set_prg_low(0x05);
            mapper551_set_prg_high(0x01);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9058D03D0", hex); // 295 @$D003
        Assert.Contains("A9038D0080", hex); // 309
        Assert.Contains("A9018D00C0", hex); // 310 @$C000
        Assert.Contains("A9028D0060", hex); // 312
        Assert.Contains("A9808D0050", hex); // 314 mode
        Assert.Contains("A9438D0150", hex); // 314 prg
        Assert.Contains("A9078D0080", hex); // 314 latch
        Assert.Contains("A9108D0060", hex); // 322
        Assert.Contains("A9068D0080A9028D0180", hex); // 322 bank
        Assert.Contains("A9208D0080", hex); // 337
        Assert.Contains("A9038D00C0", hex); // 350
        Assert.Contains("A9048D0060", hex); // 460
        Assert.Contains("A9028D0041", hex); // 512 mode @$4100
        Assert.Contains("A9078D0080A9018D0180", hex); // 512 bank
        Assert.Contains("A9118D0080", hex); // 514
        Assert.Contains("A90A8D00C0", hex); // 538 @$C000
        Assert.Contains("A91C8D0048", hex); // 548 latch @$4800
        Assert.Contains("A9008D0050", hex); // 548 apply
        Assert.Contains("A9038D0070", hex); // 550 @$7000
        Assert.Contains("A9108D0080", hex); // 550 latch
        Assert.Contains("A9028D0048", hex); // 551 mode
        Assert.Contains("A9058D0148", hex); // 551 low
        Assert.Contains("A9018D0248", hex); // 551 high
    }

    [Fact]
    public void Batch19_Nes2Mmc3AndLatch_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper267_set_outer(0x08);
            mapper267_set_bank(6, 1);
            mapper287_set_outer(0x14);
            mapper287_set_bank(7, 2);
            mapper291_set_outer(0x20);
            mapper291_set_bank(6, 3);
            mapper290_write(0x8000, 0x40);
            mapper294_set_outer(0x05);
            mapper299_set(0x12);
            mapper300_write(0x8000, 0x06);
            mapper321_set_outer(0x0C);
            mapper321_set_bank(6, 4);
            mapper333_write(0x9000, 0x11);
            mapper333_set_bank(7, 5);
            mapper335_set_prg(0x07);
            mapper335_set_chr(0x0A);
            mapper338_write(0x8000, 0x03);
            mapper345_set_outer(0x30);
            mapper345_set_bank(6, 2);
            mapper364_set_outer(0x40);
            mapper364_set_bank(7, 1);
            mapper379_set(0x09);
            mapper434_set_outer(0x02);
            mapper434_set(0x05);
            mapper437_write(0x5000, 0x04);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9088D0060", hex); // 267
        Assert.Contains("A9068D0080A9018D0180", hex); // 267 bank
        Assert.Contains("A9148D0060", hex); // 287
        Assert.Contains("A9078D0080A9028D0180", hex); // 287 bank
        Assert.Contains("A9208D0060", hex); // 291
        Assert.Contains("A9068D0080A9038D0180", hex); // 291 bank
        Assert.Contains("A9408D0080", hex); // 290
        Assert.Contains("A9058D0041", hex); // 294 @$4100
        Assert.Contains("A9128D0080", hex); // 299
        Assert.Contains("A9068D0080", hex); // 300
        Assert.Contains("A90C8D0060", hex); // 321
        Assert.Contains("A9068D0080A9048D0180", hex); // 321 bank
        Assert.Contains("A9118D0090", hex); // 333 @$9000
        Assert.Contains("A9078D0080A9058D0180", hex); // 333 bank
        Assert.Contains("A9078D00C0", hex); // 335 prg
        Assert.Contains("A90A8D0080", hex); // 335 chr
        Assert.Contains("A9038D0080", hex); // 338
        Assert.Contains("A9308D0060", hex); // 345
        Assert.Contains("A9068D0080A9028D0180", hex); // 345 bank (also 287)
        Assert.Contains("A9408D0060", hex); // 364
        Assert.Contains("A9078D0080A9018D0180", hex); // 364 bank
        Assert.Contains("A9098D0080", hex); // 379
        Assert.Contains("A9028D0060", hex); // 434 outer
        Assert.Contains("A9058D0080", hex); // 434 latch
        Assert.Contains("A9048D0050", hex); // 437 @$5000
    }

    [Fact]
    public void Batch20_Nes2LatchAndMmc3_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper261_write(0x8000, 0x10);
            mapper265_write(0x8000, 0x07);
            mapper301_write(0xC000, 0x21);
            mapper324_set(0x13);
            mapper329_set(0x05);
            mapper332_set_prg(0x0A);
            mapper332_set_chr(0x0C);
            mapper332_set(0x02);
            mapper336_set(0x17);
            mapper340_write(0x8000, 0x08);
            mapper341_write(0x8000, 0x04);
            mapper349_write(0x8000, 0x30);
            mapper430_set_outer(0x06);
            mapper430_set_bank(6, 1);
            mapper441_set_outer(0x11);
            mapper441_set_bank(7, 2);
            mapper443_set_outer(0x22);
            mapper443_set_bank(6, 3);
            mapper450_set_outer(0x03);
            mapper453_set(0x15);
            mapper455_set_outer(0x09);
            mapper455_set_bank(6, 4);
            mapper456_set_outer(0x02);
            mapper456_set_bank(7, 5);
            mapper457_set_outer(0x08);
            mapper457_set_bank(6, 2);
            mapper458_set_outer(0x14);
            mapper458_set_bank(7, 1);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9108D0080", hex); // 261
        Assert.Contains("A9078D0080", hex); // 265
        Assert.Contains("A9218D00C0", hex); // 301 @$C000
        Assert.Contains("A9138D0080", hex); // 324
        Assert.Contains("A9058D0080", hex); // 329
        Assert.Contains("A90A8D0060", hex); // 332 prg
        Assert.Contains("A90C8D0160", hex); // 332 chr @$6001
        Assert.Contains("A9028D0080", hex); // 332 CNROM
        Assert.Contains("A9178D00C0", hex); // 336 @$C000
        Assert.Contains("A9088D0080", hex); // 340
        Assert.Contains("A9048D0080", hex); // 341
        Assert.Contains("A9308D0080", hex); // 349
        Assert.Contains("A9068D0060", hex); // 430
        Assert.Contains("A9068D0080A9018D0180", hex); // 430 bank
        Assert.Contains("A9118D0060", hex); // 441
        Assert.Contains("A9078D0080A9028D0180", hex); // 441 bank
        Assert.Contains("A9228D0060", hex); // 443
        Assert.Contains("A9068D0080A9038D0180", hex); // 443 bank
        Assert.Contains("A9038D0060", hex); // 450
        Assert.Contains("A9158D0080", hex); // 453
        Assert.Contains("A9098D0041", hex); // 455 @$4100
        Assert.Contains("A9068D0080A9048D0180", hex); // 455 bank
        Assert.Contains("A9028D0041", hex); // 456
        Assert.Contains("A9078D0080A9058D0180", hex); // 456 bank
        Assert.Contains("A9088D0060", hex); // 457
        Assert.Contains("A9068D0080A9028D0180", hex); // 457 bank
        Assert.Contains("A9148D0060", hex); // 458
        Assert.Contains("A9078D0080A9018D0180", hex); // 458 bank
    }

    [Fact]
    public void Batch21_Nes2More_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper283_set(0x03);
            mapper320_write(0xF0E0, 0x05);
            mapper320_set(0x02);
            mapper323_set_outer(0x10);
            mapper346_write(0xE0A0, 0x00);
            mapper361_set_outer(0x20);
            mapper361_set_bank(6, 1);
            mapper370_set_outer(0x0C);
            mapper370_set_bank(7, 2);
            mapper376_set_outer(0x80);
            mapper376_set_outer_hi(0x01);
            mapper376_set_bank(6, 3);
            mapper381_set(0x07);
            mapper382_write(0x8000, 0x08);
            mapper382_set(0x04);
            mapper385_write(0x8000, 0x06);
            mapper393_set_outer(0x14);
            mapper393_set_bank(6, 2);
            mapper395_set_outer(0x30);
            mapper395_set_outer2(0x41);
            mapper395_set_bank(7, 1);
            mapper396_set_outer(0x05);
            mapper396_set(0x03);
            mapper403_set_prg(0x0A);
            mapper403_set_chr(0x02);
            mapper403_set_mode(0x10);
            mapper414_write(0x8000, 0x21);
            mapper448_set_outer(0x06);
            mapper515_set(0x11);
            mapper516_write(0x8010, 0x03);
            mapper516_set_bank(6, 4);
            mapper541_write(0xC000, 0x09);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9038D0080", hex); // 283
        Assert.Contains("A9058DE0F0", hex); // 320 outer
        Assert.Contains("A9028D0080", hex); // 320 inner
        Assert.Contains("A9108D0060", hex); // 323
        Assert.Contains("A9008DA0E0", hex); // 346 @$E0A0
        Assert.Contains("A9208D0070", hex); // 361
        Assert.Contains("A9068D0080A9018D0180", hex); // 361 bank
        Assert.Contains("A90C8D0050", hex); // 370
        Assert.Contains("A9078D0080A9028D0180", hex); // 370 bank
        Assert.Contains("A9808D0070", hex); // 376
        Assert.Contains("A9018D0170", hex); // 376 hi
        Assert.Contains("A9068D0080A9038D0180", hex); // 376 bank
        Assert.Contains("A9078D0080", hex); // 381
        Assert.Contains("A9088D0080", hex); // 382 write
        Assert.Contains("A9048D0080", hex); // 382 set
        Assert.Contains("A9068D0080", hex); // 385
        Assert.Contains("A9148D0060", hex); // 393
        Assert.Contains("A9068D0080A9028D0180", hex); // 393 bank
        Assert.Contains("A9308D0060", hex); // 395
        Assert.Contains("A9418D1060", hex); // 395 @$6010
        Assert.Contains("A9078D0080A9018D0180", hex); // 395 bank
        Assert.Contains("A9058D00A0", hex); // 396 outer
        Assert.Contains("A9038D0080", hex); // 396 (also 283)
        Assert.Contains("A90A8D0041", hex); // 403 prg
        Assert.Contains("A9028D0141", hex); // 403 chr
        Assert.Contains("A9108D0241", hex); // 403 mode
        Assert.Contains("A9218D0080", hex); // 414
        Assert.Contains("A9068D0060", hex); // 448
        Assert.Contains("A9118D0080", hex); // 515
        Assert.Contains("A9038D1080", hex); // 516 @$8010
        Assert.Contains("A9068D0080A9048D0180", hex); // 516 bank
        Assert.Contains("A9098D00C0", hex); // 541
    }

    [Fact]
    public void Batch22_Nes2More_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper262_set_mode(0x10);
            mapper262_set_bank(6, 1);
            mapper271_set(0x23);
            mapper293_write(0x8000, 0x05);
            mapper297_set_mode(0x01);
            mapper297_set(0x40);
            mapper327_set_outer(0x08);
            mapper327_set_bank(7, 2);
            mapper334_set_prg(0x0C);
            mapper334_set_bank(6, 3);
            mapper339_set_outer(0x14);
            mapper339_set_bank(6, 4);
            mapper367_write(0x6000, 0x02);
            mapper367_set_bank(7, 1);
            mapper369_set_outer(0x13);
            mapper369_set_bank(6, 2);
            mapper378_write(0x8000, 0x11);
            mapper386_set_outer(0x05);
            mapper388_set_outer(0x06);
            mapper402_write(0x8000, 0x07);
            mapper409_write(0xC000, 0x0A);
            mapper435_write(0x8000, 0x20);
            mapper439_set(0x03);
            mapper439_set_outer(0x10);
            mapper439_set_mask(0x70);
            mapper445_set_prg(0x20);
            mapper445_set_chr(0x30);
            mapper445_set_mode(0x01);
            mapper445_set_bank(6, 5);
            mapper447_set_outer(0x04);
            mapper519_write(0x8000, 0x15);
            mapper525_set_prg(0x08);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9108D0041", hex); // 262
        Assert.Contains("A9068D0080A9018D0180", hex); // 262 bank
        Assert.Contains("A9238D0080", hex); // 271
        Assert.Contains("A9058D0080", hex); // 293
        Assert.Contains("A9018D2041", hex); // 297 mode @$4120
        Assert.Contains("A9408D0080", hex); // 297 latch
        Assert.Contains("A9088D0060", hex); // 327
        Assert.Contains("A9078D0080A9028D0180", hex); // 327 bank
        Assert.Contains("A90C8D0060", hex); // 334
        Assert.Contains("A9068D0080A9038D0180", hex); // 334 bank
        Assert.Contains("A9148D0060", hex); // 339
        Assert.Contains("A9068D0080A9048D0180", hex); // 339 bank
        Assert.Contains("A9028D0060", hex); // 367
        Assert.Contains("A9078D0080A9018D0180", hex); // 367 bank
        Assert.Contains("A9138D0041", hex); // 369
        Assert.Contains("A9068D0080A9028D0180", hex); // 369 bank
        Assert.Contains("A9118D0080", hex); // 378
        Assert.Contains("A9058D03D0", hex); // 386
        Assert.Contains("A9068D03D0", hex); // 388
        Assert.Contains("A9078D0080", hex); // 402
        Assert.Contains("A90A8D00C0", hex); // 409
        Assert.Contains("A9208D0080", hex); // 435
        Assert.Contains("A9038D0080", hex); // 439 latch
        Assert.Contains("A9108D0060", hex); // 439 outer
        Assert.Contains("A9708D0160", hex); // 439 mask
        Assert.Contains("A9208D0050", hex); // 445 prg
        Assert.Contains("A9308D0150", hex); // 445 chr
        Assert.Contains("A9018D0250", hex); // 445 mode
        Assert.Contains("A9068D0080A9058D0180", hex); // 445 bank
        Assert.Contains("A9048D0060", hex); // 447
        Assert.Contains("A9158D0080", hex); // 519
        Assert.Contains("A9088D0080", hex); // 525
    }

    [Fact]
    public void Batch23_Nes2More_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper306_write(0xD903, 0x0C);
            mapper356_set_outer(0x0F);
            mapper356_set_bank(6, 1);
            mapper358_set_outer(0x04);
            mapper366_set_outer(0x20);
            mapper366_set_bank(7, 2);
            mapper387_set_outer(0x05);
            mapper389_write(0x8000, 0x10);
            mapper392_set_outer(0x08);
            mapper392_set_bank(6, 3);
            mapper394_set_mode(0x01);
            mapper394_set_outer(0x90);
            mapper394_set_bank(7, 1);
            mapper397_set_outer(0x06);
            mapper404_set_outer(0x18);
            mapper410_set_outer(0x07);
            mapper410_set_bank(6, 2);
            mapper412_set_chr(0x40);
            mapper412_set_mode(0x02);
            mapper412_set_prg(0x11);
            mapper412_set_bank(6, 4);
            mapper416_set(0x50);
            mapper432_set_solder(0x00);
            mapper432_set_outer(0x0C);
            mapper432_set_bank(7, 5);
            mapper461_set_outer(0x30);
            mapper467_set_outer(0x21);
            mapper467_set_bank(6, 1);
            mapper470_set_outer(0x01);
            mapper470_set(0x03);
            mapper471_write(0x8000, 0x01);
            mapper517_set(0x09);
            mapper521_set_prg(0x0A);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A90C8D03D9", hex); // 306 @$D903
        Assert.Contains("A90F8D0060", hex); // 356
        Assert.Contains("A9068D0080A9018D0180", hex); // 356 bank
        Assert.Contains("A9048D03D0", hex); // 358
        Assert.Contains("A9208D0068", hex); // 366 @$6800
        Assert.Contains("A9078D0080A9028D0180", hex); // 366 bank
        Assert.Contains("A9058D03D0", hex); // 387
        Assert.Contains("A9108D0080", hex); // 389
        Assert.Contains("A9088D0060", hex); // 392
        Assert.Contains("A9068D0080A9038D0180", hex); // 392 bank
        Assert.Contains("A9018D0950", hex); // 394 mode
        Assert.Contains("A9908D0B50", hex); // 394 outer
        Assert.Contains("A9078D0080A9018D0180", hex); // 394 bank
        Assert.Contains("A9068D03D0", hex); // 397
        Assert.Contains("A9188D0060", hex); // 404
        Assert.Contains("A9078D0060", hex); // 410
        Assert.Contains("A9068D0080A9028D0180", hex); // 410 bank
        Assert.Contains("A9408D0060", hex); // 412 chr
        Assert.Contains("A9028D0160", hex); // 412 mode
        Assert.Contains("A9118D0260", hex); // 412 prg
        Assert.Contains("A9068D0080A9048D0180", hex); // 412 bank
        Assert.Contains("A9508D0080", hex); // 416
        Assert.Contains("A9008D0060", hex); // 432 solder
        Assert.Contains("A90C8D0160", hex); // 432 outer
        Assert.Contains("A9078D0080A9058D0180", hex); // 432 bank
        Assert.Contains("A9308D0060", hex); // 461
        Assert.Contains("A9218D0090", hex); // 467 @$9000
        Assert.Contains("A9068D0080A9018D0180", hex); // 467 bank
        Assert.Contains("A9018D0050", hex); // 470 outer
        Assert.Contains("A9038D0080", hex); // 470 latch
        Assert.Contains("A9018D0080", hex); // 471
        Assert.Contains("A9098D0080", hex); // 517
        Assert.Contains("A90A8D2050", hex); // 521 @$5020
    }

    [Fact]
    public void Batch24_Nes2MopUp_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper263_write(0x8000, 0x06);
            mapper372_set_outer(0x0F);
            mapper372_set_bank(6, 1);
            mapper373_set_outer(0x11);
            mapper373_set_bank(7, 2);
            mapper377_set_outer(0x08);
            mapper377_set_bank(6, 3);
            mapper384_set_outer(0x04);
            mapper401_set_outer(0x12);
            mapper401_set_bank(6, 4);
            mapper415_set_prg(0x05);
            mapper438_write(0x8000, 0x20);
            mapper522_set_bank(6, 2);
            mapper523_set_bank(7, 1);
            mapper533_set_chr(0x10);
            mapper535_set_prg(0x03);
            mapper539_set_prg(0x0A);
            mapper540_write(0xA000, 0x07);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9068D0080", hex); // 263
        Assert.Contains("A90F8D0060", hex); // 372
        Assert.Contains("A9068D0080A9018D0180", hex); // 372 bank
        Assert.Contains("A9118D0060", hex); // 373
        Assert.Contains("A9078D0080A9028D0180", hex); // 373 bank
        Assert.Contains("A9088D0060", hex); // 377
        Assert.Contains("A9068D0080A9038D0180", hex); // 377 bank
        Assert.Contains("A9048D0068", hex); // 384
        Assert.Contains("A9128D0060", hex); // 401
        Assert.Contains("A9068D0080A9048D0180", hex); // 401 bank
        Assert.Contains("A9058D0080", hex); // 415
        Assert.Contains("A9208D0080", hex); // 438
        Assert.Contains("A9068D0080A9028D0180", hex); // 522
        Assert.Contains("A9078D0080A9018D0180", hex); // 523
        Assert.Contains("A9108D0080", hex); // 533
        Assert.Contains("A9038D00F0", hex); // 535
        Assert.Contains("A90A8D00A0", hex); // 539
        Assert.Contains("A9078D00A0", hex); // 540 @$A000
    }

    [Fact]
    public void Batch25_Nes2VrcAndOuter_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper298_set_prg_bank(0, 2);
            mapper303_write(0x4A00, 0x14);
            mapper303_apply(0x00);
            mapper308_set_prg_bank(1, 3);
            mapper313_set_bank(6, 1);
            mapper325_set_bank(7, 2);
            mapper362_write(0xB001, 0x40);
            mapper406_set_bank(6, 4);
            mapper411_set_outer(0x80);
            mapper411_set_outer2(0x82);
            mapper411_set_bank(6, 3);
            mapper423_set_outer(0x01);
            mapper424_set_outer(0x02);
            mapper425_set_outer(0x03);
            mapper436_set_outer(0x40);
            mapper436_set_io(0x20);
            mapper468_set_mode(0x10);
            mapper520_write(0xB000, 0x08);
            mapper527_set_prg_bank(0, 5);
            mapper529_set_prg(0x07);
            mapper530_set_prg_bank(1, 1);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9028D0080", hex); // 298 PRG0
        Assert.Contains("A9148D004A", hex); // 303 latch
        Assert.Contains("A9008D0051", hex); // 303 apply
        Assert.Contains("A9038D00A0", hex); // 308 PRG1
        Assert.Contains("A9068D0080A9018D0180", hex); // 313
        Assert.Contains("A9078D0080A9028D0180", hex); // 325
        Assert.Contains("A9408D01B0", hex); // 362
        Assert.Contains("A9068D0080A9048D0280", hex); // 406 @$8000/$8002
        Assert.Contains("A9808DF05F", hex); // 411 @$5FF0
        Assert.Contains("A9828DF15F", hex); // 411 @$5FF1
        Assert.Contains("A9068D0080A9038D0180", hex); // 411 bank
        Assert.Contains("A9018D5041", hex); // 423
        Assert.Contains("A9028D1E41", hex); // 424
        Assert.Contains("A9038DE641", hex); // 425
        Assert.Contains("A9408D0041", hex); // 436
        Assert.Contains("A9208D0F41", hex); // 436 io
        Assert.Contains("A9108D0057", hex); // 468
        Assert.Contains("A9088D00B0", hex); // 520
        Assert.Contains("A9058D0080", hex); // 527 PRG0
        Assert.Contains("A9078D00A0", hex); // 529
        Assert.Contains("A9018D00A0", hex); // 530 PRG1
    }

    [Fact]
    public void Batch26_AliasesAndOneBusLean_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper256_set_prg(0x11);
            mapper256_set_chr(0x22);
            mapper256_set_scramble(0x80);
            mapper256_set_scramble2(0x01);
            mapper256_set_bank(6, 3);
            mapper408_set_prg(0x12);
            mapper408_set_chr(0x23);
            mapper408_set_scramble(0x00);
            mapper419_set_prg(0x13);
            mapper419_set_chr(0x24);
            mapper419_set_scramble(0x40);
            mapper419_set_io(0x20);
            mapper422_set_outer(0x81);
            mapper422_set_bank(6, 2);
            mapper426_set_gpio(0x55);
            mapper427_set_gpio(0x66);
            mapper427_write(0x4150, 0x77);
            mapper433_set(0x10);
            mapper469_set_mode(0x20);
            mapper472_set_outer(0x03);
            mapper472_set_bank(7, 1);
            mapper536_set_bank(6, 4);
            mapper537_set_bank(6, 5);
            mapper547_write(0xD300, 0x08);
            mapper547_set_prg0(0x09);
            mapper552_set_prg_bank(0x0A);
            mapper561_write(0x42FC, 0x00);
            mapper561_set_irq_lo(0x10);
            mapper561_set_irq_hi(0x20);
            mapper561_set(0x30);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9118D0741", hex); // 256 prg
        Assert.Contains("A9228D0841", hex); // 256 chr
        Assert.Contains("A9808D1C41", hex); // 256 scramble
        Assert.Contains("A9018D6941", hex); // 256 scramble2
        Assert.Contains("A9068D0080A9038D0180", hex); // 256 bank
        Assert.Contains("A9128D0741", hex); // 408 prg
        Assert.Contains("A9238D0841", hex); // 408 chr
        Assert.Contains("A9008D1C41", hex); // 408 scramble
        Assert.Contains("A9138D0741", hex); // 419 prg
        Assert.Contains("A9248D0841", hex); // 419 chr
        Assert.Contains("A9408D1C41", hex); // 419 scramble
        Assert.Contains("A9208D0F41", hex); // 419 io
        Assert.Contains("A9818D0060", hex); // 422 outer
        Assert.Contains("A9068D0080A9028D0180", hex); // 422 bank
        Assert.Contains("A9558D5041", hex); // 426
        Assert.Contains("A9668D4841", hex); // 427 gpio
        Assert.Contains("A9778D5041", hex); // 427 write
        Assert.Contains("A9108D0080", hex); // 433
        Assert.Contains("A9208D0057", hex); // 469
        Assert.Contains("A9038D0060", hex); // 472 outer
        Assert.Contains("A9078D0080A9018D0180", hex); // 472 bank
        Assert.Contains("A9068D0080A9048D0180", hex); // 536
        Assert.Contains("A9068D0080A9058D0180", hex); // 537
        Assert.Contains("A9088D00D3", hex); // 547 write
        Assert.Contains("A9098D00D2", hex); // 547 prg0
        Assert.Contains("A90A8DF07E", hex); // 552
        Assert.Contains("A9008DFC42", hex); // 561 mode
        Assert.Contains("A9108D0041", hex); // 561 irq lo
        Assert.Contains("A9208D0141", hex); // 561 irq hi
        Assert.Contains("A9308D0080", hex); // 561 latch
    }

    [Fact]
    public void Batch27_AliasesAndTurboDoctor_Emit()
    {
        var hex = Convert.ToHexString(GetProgramBytes(
            """
            mapper363_set_outer(0x04);
            mapper390_set(0x11);
            mapper405_set_prg(0x21);
            mapper405_set_chr(0x22);
            mapper405_set_bank(6, 1);
            mapper407_set_prg(0x31);
            mapper407_set_chr(0x32);
            mapper407_set_scramble2(0x01);
            mapper407_set_bank(6, 2);
            mapper431_set_outer(0x05);
            mapper431_set(0x03);
            mapper562_write(0x42FC, 0x80);
            mapper562_set_tgd(0x10);
            mapper562_set_irq_lo(0x20);
            mapper562_set_irq_hi(0x00);
            mapper562_set(0x07);
            ppu_on_all();
            while (true) ;
            """)!);
        Assert.Contains("A9048D03D0", hex); // 363
        Assert.Contains("A9118D0080", hex); // 390
        Assert.Contains("A9218D0741", hex); // 405 prg
        Assert.Contains("A9228D0841", hex); // 405 chr
        Assert.Contains("A9068D0080A9018D0180", hex); // 405 bank
        Assert.Contains("A9318D0741", hex); // 407 prg
        Assert.Contains("A9328D0841", hex); // 407 chr
        Assert.Contains("A9018D6941", hex); // 407 scramble2
        Assert.Contains("A9068D0080A9028D0180", hex); // 407 bank
        Assert.Contains("A9058D00A0", hex); // 431 outer
        Assert.Contains("A9038D0080", hex); // 431 set
        Assert.Contains("A9808DFC42", hex); // 562 mode
        Assert.Contains("A9108D1144", hex); // 562 tgd
        Assert.Contains("A9208D0D44", hex); // 562 irq lo
        Assert.Contains("A9008D0C44", hex); // 562 irq hi
        Assert.Contains("A9078D0080", hex); // 562 latch
    }
}
