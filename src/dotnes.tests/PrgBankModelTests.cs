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

    [Fact]
    public void Batch12_AliasesAndNes2()
    {
        Assert.True(new PrgBankModel(CreateRom(109, 8)).IsNinaBankRegisterWrite(0x4100));
        Assert.True(new PrgBankModel(CreateRom(110, 8)).IsNinaBankRegisterWrite(0x4100));
        Assert.True(new PrgBankModel(CreateRom(129, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(126, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(131, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(260, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(264, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(269, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(331, 8)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(534, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch13_Nes2More()
    {
        Assert.True(new PrgBankModel(CreateRom(365, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(371, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(417, 16)).HasSwitchable16KWindow);
        // 347 banks $6000/$7000 windows; 446 is FPGA supervisor — no fixed $8000 model
        Assert.False(new PrgBankModel(CreateRom(347, 16)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(446, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch14_Nes2More()
    {
        Assert.True(new PrgBankModel(CreateRom(266, 16)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(292, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(296, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(351, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(359, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(375, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(375, 8)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(383, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(452, 8)).HasSwitchable32KWindow);
    }

    [Fact]
    public void Batch15_Nes2More()
    {
        Assert.True(new PrgBankModel(CreateRom(285, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(286, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(302, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(354, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(357, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(368, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(380, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(444, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(451, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(454, 8)).HasSwitchable32KWindow);
        Assert.False(new PrgBankModel(CreateRom(305, 16)).HasSwitchable16KWindow); // banks $6000 window
    }

    [Fact]
    public void Batch16_Nes2More()
    {
        Assert.True(new PrgBankModel(CreateRom(270, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(274, 8)).HasSwitchable32KWindow);
        Assert.False(new PrgBankModel(CreateRom(304, 16)).HasSwitchable16KWindow); // $6000 only
        Assert.True(new PrgBankModel(CreateRom(315, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(391, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(398, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(429, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(518, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(528, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(542, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(544, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(558, 8)).HasSwitchable32KWindow);
    }

    [Fact]
    public void Batch17_Nes2More()
    {
        Assert.True(new PrgBankModel(CreateRom(268, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(281, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(282, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(288, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(289, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(307, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(319, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(342, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(344, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(348, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(353, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(449, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(513, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(526, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch18_Nes2More()
    {
        Assert.True(new PrgBankModel(CreateRom(295, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(309, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(310, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(312, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(314, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(322, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(337, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(350, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(460, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(512, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(514, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(538, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(548, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(550, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(551, 8)).HasSwitchable32KWindow);
    }

    [Fact]
    public void Batch19_Nes2Mmc3AndLatch()
    {
        Assert.True(new PrgBankModel(CreateRom(267, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(287, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(291, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(290, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(294, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(294, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(299, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(300, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(321, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(333, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(335, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(338, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(345, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(364, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(379, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(434, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(434, 16)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(437, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch20_Nes2LatchAndMmc3()
    {
        Assert.True(new PrgBankModel(CreateRom(261, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(265, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(301, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(324, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(324, 16)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(329, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(332, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(336, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(340, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(341, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(349, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(430, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(441, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(443, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(450, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(453, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(455, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(455, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(456, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(457, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(458, 8)).HasSwitchable32KWindow);
    }

    [Fact]
    public void Batch21_Nes2More()
    {
        Assert.True(new PrgBankModel(CreateRom(283, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(320, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(323, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(346, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(361, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(370, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(376, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(381, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(381, 16)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(382, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(385, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(393, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(395, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(396, 16)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(396, 16)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(403, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(403, 8)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(414, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(448, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(515, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(516, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(541, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch22_Nes2More()
    {
        Assert.True(new PrgBankModel(CreateRom(262, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(262, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(271, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(293, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(297, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(327, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(334, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(339, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(367, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(369, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(378, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(386, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(388, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(402, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(409, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(435, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(439, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(445, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(447, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(519, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(525, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch23_Nes2More()
    {
        Assert.False(new PrgBankModel(CreateRom(306, 16)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(306, 16)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(356, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(358, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(366, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(387, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(389, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(392, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(394, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(397, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(404, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(410, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(412, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(416, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(432, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(461, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(467, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(470, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(471, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(517, 16)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(517, 16)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(521, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch24_Nes2MopUp()
    {
        Assert.True(new PrgBankModel(CreateRom(263, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(372, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(373, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(377, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(384, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(401, 16)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(415, 16)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(415, 16)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(438, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(522, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(523, 16)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(533, 8)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(535, 16)).HasSwitchable16KWindow);
        Assert.False(new PrgBankModel(CreateRom(539, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(540, 16)).HasSwitchable16KWindow);
    }

    [Fact]
    public void Batch25_Nes2VrcAndOuter()
    {
        Assert.True(new PrgBankModel(CreateRom(298, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(298, 16)).HasVrcStylePrg0Register);
        Assert.True(new PrgBankModel(CreateRom(303, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(308, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(313, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(325, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(362, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(406, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(411, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(423, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(424, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(425, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(436, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(468, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(527, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(529, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(530, 16)).HasVrcStylePrg0Register);
    }

    [Fact]
    public void Batch26_AliasesAndOneBusLean()
    {
        Assert.True(new PrgBankModel(CreateRom(256, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(256, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(408, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(408, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(419, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(419, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(422, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(426, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(427, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(433, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(433, 8)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(469, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(472, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(536, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(537, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(547, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(552, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(561, 8)).HasSwitchable32KWindow);
        Assert.False(new PrgBankModel(CreateRom(561, 8)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(561, 8)).HasNinaBankRegister);
    }

    [Fact]
    public void Batch27_AliasesAndTurboDoctor()
    {
        Assert.True(new PrgBankModel(CreateRom(363, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(390, 8)).HasSwitchable32KWindow);
        Assert.True(new PrgBankModel(CreateRom(390, 8)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(405, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(405, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(407, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(407, 16)).HasNinaBankRegister);
        Assert.True(new PrgBankModel(CreateRom(431, 16)).HasSwitchable16KWindow);
        Assert.True(new PrgBankModel(CreateRom(562, 8)).HasSwitchable32KWindow);
        Assert.False(new PrgBankModel(CreateRom(562, 8)).BusConflictBankSwitch);
        Assert.True(new PrgBankModel(CreateRom(562, 8)).HasNinaBankRegister);
    }

    /// <summary>Minimal iNES/NES 2.0 ROM with distinctive PRG bytes (offset = low byte of address).</summary>
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
        if (mapper > 255)
        {
            // NES 2.0 identifier in flags7 bits 2–3; mapper bits 8–11 in byte 8
            rom[7] = (byte)((rom[7] & 0xF0) | 0x08);
            rom[8] = (byte)((mapper >> 8) & 0x0F);
        }

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
