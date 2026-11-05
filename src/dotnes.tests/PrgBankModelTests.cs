using dotnes.Disasm;

namespace dotnes.tests;

public class PrgBankModelTests
{
    [Fact]
    public void Axrom_Maps32KWindow()
    {
        // 128KB PRG = 4×32KB banks, mapper 7
        var rom = CreateRom(mapper: 7, prgBanks16k: 8);
        var model = new PrgBankModel(rom);

        Assert.True(model.HasSwitchable32KWindow);
        Assert.True(model.BusConflictBankSwitch);
        Assert.Equal(0x8000, model.BankSize);
        Assert.Equal(4, model.SwitchableBankCount);

        Assert.True(model.TryMap(0x8000, 1, out int off));
        Assert.Equal(0x8000, off);
        Assert.True(model.TryMap(0xC000, 1, out off));
        Assert.Equal(0xC000, off);
        Assert.True(model.TryMap(0xFFFF, 3, out off));
        Assert.Equal(0x1FFFF, off);

        Assert.False(model.IsFixedCpuAddress(0xC000));
        Assert.Equal(1, model.ExtractPrgBankFromBusWrite(0x11)); // low bits; bit4 is mirroring
    }

    [Fact]
    public void Gxrom_ExtractsPrgFromHighNibble()
    {
        var rom = CreateRom(mapper: 66, prgBanks16k: 8);
        var model = new PrgBankModel(rom);

        Assert.True(model.HasSwitchable32KWindow);
        Assert.Equal(2, model.ExtractPrgBankFromBusWrite(0x21)); // bits 5-4 = 2
        Assert.Equal(1, model.ExtractPrgBankFromBusWrite(0x13));
    }

    [Fact]
    public void Bnrom_MapsLikeAxrom()
    {
        var rom = CreateRom(mapper: 34, prgBanks16k: 4);
        var model = new PrgBankModel(rom);
        Assert.True(model.HasSwitchable32KWindow);
        Assert.Equal(2, model.SwitchableBankCount);
        Assert.True(model.TryMap(0x9000, 0, out int off));
        Assert.Equal(0x1000, off);
    }

    [Fact]
    public void Mmc2_Maps8KSwitchableAndFixed24K()
    {
        // 128KB PRG = 16×8KB banks, mapper 9
        var rom = CreateRom(mapper: 9, prgBanks16k: 8);
        var model = new PrgBankModel(rom);

        Assert.True(model.HasSwitchable8KWindow);
        Assert.False(model.HasSwitchable16KWindow);
        Assert.Equal(0x2000, model.BankSize);
        Assert.Equal(16, model.SwitchableBankCount);
        Assert.True(model.IsFixedCpuAddress(0xA000));
        Assert.False(model.IsFixedCpuAddress(0x9FFF));

        Assert.True(model.TryMap(0x8000, 2, out int off));
        Assert.Equal(0x4000, off);

        // Fixed last 24KB at $A000
        Assert.True(model.TryMap(0xA000, 0, out off));
        Assert.Equal(rom.PrgRom.Length - 0x6000, off);
        Assert.True(model.TryMap(0xFFFF, 0, out off));
        Assert.Equal(rom.PrgRom.Length - 1, off);

        Assert.True(model.IsMmc2StylePrgRegisterWrite(0xA000));
        Assert.False(model.IsMmc2StylePrgRegisterWrite(0xB000));
    }

    [Fact]
    public void Mmc4_Uses16KSwitchableWindow()
    {
        var rom = CreateRom(mapper: 10, prgBanks16k: 8);
        var model = new PrgBankModel(rom);

        Assert.True(model.HasSwitchable16KWindow);
        Assert.False(model.HasSwitchable8KWindow);
        Assert.Equal(0x4000, model.BankSize);
        Assert.True(model.IsFixedCpuAddress(0xC000));
        Assert.True(model.TryMap(0x8000, 3, out int off));
        Assert.Equal(0xC000, off);
        Assert.True(model.TryMap(0xC000, 0, out off));
        Assert.Equal(7 * 0x4000, off); // fixed last 16K
        Assert.True(model.IsMmc2StylePrgRegisterWrite(0xA080));
    }

    [Fact]
    public void Mmc5_Approximate16KWindow()
    {
        var rom = CreateRom(mapper: 5, prgBanks16k: 16);
        var model = new PrgBankModel(rom);
        Assert.True(model.HasSwitchable16KWindow);
        Assert.False(model.BusConflictBankSwitch);
    }

    [Fact]
    public void Uxrom_StillBusConflict16K()
    {
        var rom = CreateRom(mapper: 2, prgBanks16k: 8);
        var model = new PrgBankModel(rom);
        Assert.True(model.HasSwitchable16KWindow);
        Assert.True(model.BusConflictBankSwitch);
        Assert.Equal(3, model.ExtractPrgBankFromBusWrite(0x03));
    }

    [Fact]
    public void Colordreams_Maps32KAndExtractsLowPrgBits()
    {
        // NESDev: CCCC LLPP — PRG in bits 0–1
        var rom = CreateRom(mapper: 11, prgBanks16k: 8);
        var model = new PrgBankModel(rom);
        Assert.True(model.HasSwitchable32KWindow);
        Assert.Equal(1, model.ExtractPrgBankFromBusWrite(0x21)); // PP=01
        Assert.Equal(2, model.ExtractPrgBankFromBusWrite(0x12)); // PP=10
        Assert.True(model.TryMap(0xC000, 1, out int off));
        Assert.Equal(0xC000, off);
    }

    [Fact]
    public void Unrom512AndHolyDiver_Are16KBusConflict()
    {
        foreach (int mapper in new[] { 30, 78 })
        {
            var model = new PrgBankModel(CreateRom(mapper, prgBanks16k: 8));
            Assert.True(model.HasSwitchable16KWindow);
            Assert.True(model.BusConflictBankSwitch);
        }
        // NESDev: CCCC MPPP — PRG in bits 0–2; $41 → PPP=001
        Assert.Equal(1, new PrgBankModel(CreateRom(78, 8)).ExtractPrgBankFromBusWrite(0x41));
    }

    [Fact]
    public void Nina_Maps32KAndExtractsFrom4100Write()
    {
        var model = new PrgBankModel(CreateRom(mapper: 79, prgBanks16k: 8));
        Assert.True(model.HasSwitchable32KWindow);
        Assert.True(model.IsNinaBankRegisterWrite(0x4100));
        Assert.Equal(2, model.ExtractPrgBankFromNinaWrite(0x13));
    }

    [Fact]
    public void Fme7AndRambo_Approximate16K()
    {
        Assert.True(new PrgBankModel(CreateRom(69, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(64, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(206, 8)).HasSwitchable16KWindow);
    }

    [Fact]
    public void VrcAndNamco_Approximate16K()
    {
        foreach (int mapper in new[] { 19, 21, 23, 24, 28, 73, 75, 85 })
            Assert.True(new PrgBankModel(CreateRom(mapper, 16)).HasSwitchable16KWindow);

        var vrc = new PrgBankModel(CreateRom(21, 16));
        Assert.True(vrc.IsVrcStylePrg0RegisterWrite(0x8000));
        Assert.False(vrc.IsVrcStylePrg0RegisterWrite(0xA000));
    }

    [Fact]
    public void Mapper15AndAction52_Are32KBusConflict()
    {
        foreach (int mapper in new[] { 15, 228 })
        {
            var model = new PrgBankModel(CreateRom(mapper, 8));
            Assert.True(model.HasSwitchable32KWindow);
            Assert.True(model.BusConflictBankSwitch);
        }
    }

    [Fact]
    public void Nina113_UsesNinaRegisterRange()
    {
        var model = new PrgBankModel(CreateRom(113, 8));
        Assert.True(model.HasSwitchable32KWindow);
        Assert.True(model.IsNinaBankRegisterWrite(0x4100));
        Assert.True(model.IsNinaBankRegisterWrite(0x5000));
    }

    [Fact]
    public void Batch4_MappersHaveExpectedWindows()
    {
        Assert.True(new PrgBankModel(CreateRom(16, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(32, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(67, 8)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(88, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(232, 8)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(241, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(226, 8)).BusConflictBankSwitch);
    }

    [Fact]
    public void Batch5_MappersHaveExpectedWindows()
    {
        Assert.True(new PrgBankModel(CreateRom(36, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(47, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(176, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(229, 8)).BusConflictBankSwitch);
    }

    [Fact]
    public void Batch6_MappersHaveExpectedWindows()
    {
        Assert.True(new PrgBankModel(CreateRom(37, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(40, 8)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(41, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(90, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(163, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(209, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch7_BroadCoverageWindows()
    {
        Assert.True(new PrgBankModel(CreateRom(6, 8)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(8, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(27, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(147, 8)).IsNinaBankRegisterWrite(0x4100));
        Assert.True(new PrgBankModel(CreateRom(191, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(233, 8)).BusConflictBankSwitch);
    }

    [Fact]
    public void Batch8_NesDevCommercialGaps()
    {
        Assert.True(new PrgBankModel(CreateRom(21, 16)).HasVrcStylePrg0Register);
        Assert.True(new PrgBankModel(CreateRom(24, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(48, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(64, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(94, 8)).BusConflictBankSwitch);
        Assert.Equal(3, new PrgBankModel(CreateRom(94, 8)).ExtractPrgBankFromBusWrite(0x0C));
        Assert.True(new PrgBankModel(CreateRom(105, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(118, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(119, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(155, 16)).HasSwitchable16KWindow);
        var m180 = new PrgBankModel(CreateRom(180, 8));
        Assert.True(m180.BusConflictBankSwitch);
        Assert.True(m180.HasFixedFirst16KWindow);
        Assert.True(m180.IsFixedCpuAddress(0x8000));
        Assert.True(m180.IsSwitchableCpuAddress(0xC000));
        Assert.True(m180.TryMap(0x8000, 3, out int off180low));
        Assert.Equal(0, off180low);
        Assert.True(m180.TryMap(0xC000, 3, out int off180hi));
        Assert.Equal(3 * 0x4000, off180hi);
        Assert.True(new PrgBankModel(CreateRom(192, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(206, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch9_SpecFixesAndMore()
    {
        Assert.Equal(3, new PrgBankModel(CreateRom(29, 8)).ExtractPrgBankFromBusWrite(0x0E)); // >>2
        var m168 = new PrgBankModel(CreateRom(168, 4));
        Assert.Equal(2, m168.ExtractPrgBankFromBusWrite(0x80)); // >>6
        Assert.False(m168.BusConflictBankSwitch); // NESDev: no bus conflicts
        Assert.True(new PrgBankModel(CreateRom(183, 16)).HasVrcStylePrg0Register);
        Assert.True(new PrgBankModel(CreateRom(190, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(208, 16)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(219, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch10_RemainingBoards()
    {
        Assert.True(new PrgBankModel(CreateRom(106, 16)).HasSwitchable16KWindow);
        var m134 = new PrgBankModel(CreateRom(134, 8));
        Assert.False(m134.IsNinaBankRegisterWrite(0x4100)); // $6000 outer + MMC3, not NINA
        Assert.True(m134.HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(135, 8)).IsNinaBankRegisterWrite(0x4100)); // Sachen 8259
        Assert.True(new PrgBankModel(CreateRom(160, 16)).HasSwitchable16KWindow); // ≡ mapper 90
        Assert.True(new PrgBankModel(CreateRom(162, 16)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(174, 8)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(187, 16)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(211, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch11_NesDevContinuation()
    {
        Assert.True(new PrgBankModel(CreateRom(59, 8)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(59, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(100, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(121, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(123, 16)).HasSwitchable16KWindow);
        // 108/125 only bank $6000 — not modeled as CPU $8000 window switchers
        Assert.False(new PrgBankModel(CreateRom(108, 16)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(108, 16)).HasSwitchable32KWindow);
        Assert.False(new PrgBankModel(CreateRom(125, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(169, 8)).HasSwitchable32KWindow); // ≡ mapper 15
        Assert.True(new PrgBankModel(CreateRom(169, 8)).BusConflictBankSwitch);
        Assert.False(new PrgBankModel(CreateRom(120, 16)).HasSwitchable16KWindow); // only banks $6000
    }

    /// <summary>Minimal iNES ROM with distinctive PRG bytes (offset = low byte of address).</summary>
    static NESRomReader CreateRom(int mapper, int prgBanks16k)
    {
        int prgSize = prgBanks16k * 16384;
        var rom = new byte[16 + prgSize];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = (byte)prgBanks16k;
        rom[5] = 0; // CHR RAM
        rom[6] = (byte)((mapper & 0x0F) << 4);
        rom[7] = (byte)(mapper & 0xF0);

        for (int i = 0; i < prgSize; i++)
            rom[16 + i] = (byte)(i & 0xFF);

        // Vectors → $8000
        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        return new NESRomReader(rom);
    }
}
