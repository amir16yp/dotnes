namespace NES;

/// <summary>
/// Cartridge mapper bank-switch helpers and register constants for <see cref="NESLib"/>.
/// </summary>
public static partial class NESLib
{
    // CNROM (Mapper 3) bank switching

    /// <summary>
    /// select a CHR ROM bank (CNROM mapper 3)
    /// writes the bank number to $8000 to switch the 8 KB CHR bank
    /// </summary>
    public static void cnrom_set_chr_bank(byte bank) => throw null!;

    // MMC3 mapper register addresses for bank switching via poke()

    /// <summary>
    /// Address of the MMC3 bank select register used for bank switching via poke().
    /// </summary>
    /// <remarks>Write to this address to select which internal MMC3 bank register will be modified; a
    /// subsequent write to the bank data register (0x8001) updates the selected bank mapping.</remarks>
    public const ushort MMC3_BANK_SELECT = 0x8000;

    /// <summary>
    /// Address of the MMC3 mapper bank data register ($8001) used to update PRG and CHR bank mappings.
    /// </summary>
    /// <remarks>Used by MMC3 (mapper 4). Writes to this register set the bank data selected by the bank
    /// select register at $8000 and affect either PRG or CHR mapping depending on the current bank select
    /// value.</remarks>
    public const ushort MMC3_BANK_DATA = 0x8001;

    /// <summary>
    /// Address of the MMC3 mapper's mirroring control register ($A000).
    /// </summary>
    /// <remarks>Writes to this address select the PPU nametable mirroring mode; bit 0 = 0 for horizontal
    /// mirroring and 1 for vertical mirroring. Used by the MMC3 mapper to control nametable mapping.</remarks>
    public const ushort MMC3_MIRRORING = 0xA000;

    /// <summary>
    /// CPU address for the MMC3 mapper register that controls PRG RAM (WRAM) enable and protection.
    /// </summary>
    /// <remarks>Writes to this address set PRG RAM write-enable/protect bits on MMC3-compatible mappers.
    /// Behavior is mapper-specific and these registers are typically mirrored across the $8000–$FFFF CPU address
    /// range.</remarks>
    public const ushort MMC3_WRAM_ENABLE = 0xA001;

    // MMC3 IRQ register addresses for scanline counting via poke()

    /// <summary>
    /// Memory-mapped address of the MMC3 IRQ latch register used for scanline counting.
    /// </summary>
    /// <remarks>Write a byte to this address (for example via poke()) to set the MMC3 IRQ latch. The latched
    /// value is transferred to the IRQ counter on reload; use the MMC3 IRQ enable/disable registers to control IRQ
    /// behavior.</remarks>
    public const ushort MMC3_IRQ_LATCH = 0xC000;
    /// <summary>
    /// Memory-mapped CPU address of the MMC3 IRQ reload register.
    /// </summary>
    /// <remarks>Writing to this address reloads the MMC3 mapper's IRQ/scanline counter. Typically used by NES
    /// cartridges and emulators to control scanline-timed IRQs for split-screen rendering and raster effects.</remarks>
    public const ushort MMC3_IRQ_RELOAD = 0xC001;
    /// <summary>
    /// CPU memory address that disables MMC3 mapper IRQs when written.
    /// </summary>
    /// <remarks>Writing any value to this address clears the MMC3 IRQ enable flag and stops the IRQ counter.
    /// Use the corresponding enable address (0xE001) to re-enable IRQs. Represents the CPU address $E000.</remarks>
    public const ushort MMC3_IRQ_DISABLE = 0xE000;
    /// <summary>
    /// Address of the MMC3 IRQ enable register in the mapper's CPU memory-mapped I/O space.
    /// </summary>
    /// <remarks>Writing to this address enables the MMC3 scanline IRQ generator. Typically used together with
    /// the IRQ disable register (0xE000) and the IRQ latch/counter registers.</remarks>
    public const ushort MMC3_IRQ_ENABLE = 0xE001;

    /// <summary>
    /// Set an MMC3 CHR bank register to the specified bank number.
    /// Writes reg to $8000 (MMC3_BANK_SELECT) and bank to $8001 (MMC3_BANK_DATA).
    /// </summary>
    /// <param name="reg">MMC3 bank-select value. Bits 0-2 select the register (R0-R7),
    /// bits 6-7 control PRG/CHR banking mode. Must be a compile-time constant.
    /// R0/R1 (reg 0-1): 2KB CHR banks at PPU $0000/$0800.
    /// R2-R5 (reg 2-5): 1KB CHR banks at PPU $1000/$1400/$1800/$1C00.
    /// R6-R7 (reg 6-7): PRG banks (not CHR).</param>
    /// <param name="bank">Bank number to select. Can be a constant or local variable.</param>
    public static void mmc3_set_chr_bank(byte reg, byte bank) => throw null!;

    /// <summary>
    /// Set an MMC3 PRG bank register (R6 or R7). <paramref name="reg"/> must be a compile-time constant
    /// with bits 0–2 equal to 6 or 7 (optionally OR'd with mode bits 6–7).
    /// </summary>
    public static void mmc3_set_prg_bank(byte reg, byte bank) => throw null!;

    /// <summary>
    /// Enable or protect MMC3 PRG-RAM via $A001. Common values: 0x80 enable, 0xC0 enable+write-protect.
    /// </summary>
    public static void mmc3_wram_enable(byte value) => poke(MMC3_WRAM_ENABLE, value);

    /// <summary>
    /// UxROM / UNROM (mapper 2): switch the 16KB PRG bank at $8000 by writing to $8000.
    /// </summary>
    public static void uxrom_set_prg_bank(byte bank) => throw null!;

    /// <summary>
    /// AxROM (mapper 7): switch the 32KB PRG bank at $8000–$FFFF by writing to $8000.
    /// Bits 0–2 select the PRG bank; bit 4 selects single-screen nametable (0=lower, 1=upper).
    /// </summary>
    public static void axrom_set_prg_bank(byte bank) => throw null!;

    /// <summary>
    /// BNROM (mapper 34): switch the 32KB PRG bank at $8000–$FFFF by writing to $8000.
    /// </summary>
    public static void bnrom_set_prg_bank(byte bank) => throw null!;

    /// <summary>
    /// GxROM (mapper 66): write a packed bank-select value to $8000.
    /// Bits 5–4 select the 32KB PRG bank; bits 1–0 select the 8KB CHR bank.
    /// </summary>
    public static void gxrom_set(byte value) => throw null!;

    /// <summary>
    /// GxROM (mapper 66): pack PRG and CHR bank numbers and write to $8000.
    /// Bits 5–4 = <paramref name="prgBank"/>, bits 1–0 = <paramref name="chrBank"/>.
    /// </summary>
    public static void gxrom_set_banks(byte prgBank, byte chrBank) => throw null!;

    // MMC2 / MMC4 register addresses (same layout; PRG bank size differs)

    /// <summary>MMC2/MMC4 PRG bank select ($A000).</summary>
    public const ushort MMC2_PRG_BANK = 0xA000;
    /// <summary>MMC2/MMC4 CHR bank 0 FD latch ($B000).</summary>
    public const ushort MMC2_CHR_BANK0_FD = 0xB000;
    /// <summary>MMC2/MMC4 CHR bank 0 FE latch ($C000).</summary>
    public const ushort MMC2_CHR_BANK0_FE = 0xC000;
    /// <summary>MMC2/MMC4 CHR bank 1 FD latch ($D000).</summary>
    public const ushort MMC2_CHR_BANK1_FD = 0xD000;
    /// <summary>MMC2/MMC4 CHR bank 1 FE latch ($E000).</summary>
    public const ushort MMC2_CHR_BANK1_FE = 0xE000;
    /// <summary>MMC2/MMC4 mirroring ($F000); 0=vertical, 1=horizontal.</summary>
    public const ushort MMC2_MIRRORING = 0xF000;

    /// <summary>MMC4 PRG bank select ($A000) — same address as <see cref="MMC2_PRG_BANK"/>.</summary>
    public const ushort MMC4_PRG_BANK = MMC2_PRG_BANK;
    /// <summary>MMC4 CHR bank 0 FD latch.</summary>
    public const ushort MMC4_CHR_BANK0_FD = MMC2_CHR_BANK0_FD;
    /// <summary>MMC4 CHR bank 0 FE latch.</summary>
    public const ushort MMC4_CHR_BANK0_FE = MMC2_CHR_BANK0_FE;
    /// <summary>MMC4 CHR bank 1 FD latch.</summary>
    public const ushort MMC4_CHR_BANK1_FD = MMC2_CHR_BANK1_FD;
    /// <summary>MMC4 CHR bank 1 FE latch.</summary>
    public const ushort MMC4_CHR_BANK1_FE = MMC2_CHR_BANK1_FE;
    /// <summary>MMC4 mirroring ($F000).</summary>
    public const ushort MMC4_MIRRORING = MMC2_MIRRORING;

    /// <summary>MMC2 (mapper 9): switch the 8KB PRG bank at $8000–$9FFF.</summary>
    public static void mmc2_set_prg_bank(byte bank) => throw null!;

    /// <summary>MMC2: set CHR bank 0 FD/FE latch pair ($B000 / $C000).</summary>
    public static void mmc2_set_chr_bank0(byte fdBank, byte feBank) => throw null!;

    /// <summary>MMC2: set CHR bank 1 FD/FE latch pair ($D000 / $E000).</summary>
    public static void mmc2_set_chr_bank1(byte fdBank, byte feBank) => throw null!;

    /// <summary>MMC2: set nametable mirroring via $F000 (0=vertical, 1=horizontal).</summary>
    public static void mmc2_set_mirroring(byte mirror) => throw null!;

    /// <summary>MMC4 (mapper 10): switch the 16KB PRG bank at $8000–$BFFF.</summary>
    public static void mmc4_set_prg_bank(byte bank) => throw null!;

    /// <summary>MMC4: set CHR bank 0 FD/FE latch pair ($B000 / $C000).</summary>
    public static void mmc4_set_chr_bank0(byte fdBank, byte feBank) => throw null!;

    /// <summary>MMC4: set CHR bank 1 FD/FE latch pair ($D000 / $E000).</summary>
    public static void mmc4_set_chr_bank1(byte fdBank, byte feBank) => throw null!;

    /// <summary>MMC4: set nametable mirroring via $F000 (0=vertical, 1=horizontal).</summary>
    public static void mmc4_set_mirroring(byte mirror) => throw null!;

    // MMC5 (mapper 5) — lean register surface; no ExRAM / IRQ helpers

    /// <summary>MMC5 PRG mode register ($5100).</summary>
    public const ushort MMC5_PRG_MODE = 0x5100;
    /// <summary>MMC5 CHR mode register ($5101).</summary>
    public const ushort MMC5_CHR_MODE = 0x5101;
    /// <summary>MMC5 PRG bank register base ($5114); reg 0–3 map to $5114–$5117.</summary>
    public const ushort MMC5_PRG_BANK0 = 0x5114;
    /// <summary>MMC5 CHR bank register base ($5120); reg 0–7 map to $5120–$5127.</summary>
    public const ushort MMC5_CHR_BANK0 = 0x5120;

    /// <summary>MMC5: write PRG banking mode to $5100.</summary>
    public static void mmc5_set_prg_mode(byte mode) => throw null!;

    /// <summary>MMC5: write CHR banking mode to $5101.</summary>
    public static void mmc5_set_chr_mode(byte mode) => throw null!;

    /// <summary>
    /// MMC5: set PRG bank register <paramref name="reg"/> (0–3 → $5114–$5117).
    /// <paramref name="reg"/> must be a compile-time constant.
    /// </summary>
    public static void mmc5_set_prg_bank(byte reg, byte bank) => throw null!;

    /// <summary>
    /// MMC5: set CHR bank register <paramref name="reg"/> (0–7 → $5120–$5127).
    /// <paramref name="reg"/> must be a compile-time constant.
    /// </summary>
    public static void mmc5_set_chr_bank(byte reg, byte bank) => throw null!;

    // Color Dreams (mapper 11) — packed PRG/CHR write to $8000

    /// <summary>
    /// Color Dreams (mapper 11): write a packed bank-select value to $8000.
    /// Layout <c>CCCC LLPP</c>: bits 1–0 = 32KB PRG, bits 7–4 = 8KB CHR (bits 3–2 lockout).
    /// </summary>
    public static void colordreams_set(byte value) => throw null!;

    /// <summary>
    /// Color Dreams (mapper 11): pack PRG (low) and CHR (high nibble) and write to $8000.
    /// </summary>
    public static void colordreams_set_banks(byte prgBank, byte chrBank) => throw null!;

    /// <summary>
    /// CPROM (mapper 13): switch a 4KB CHR-RAM bank by writing to $8000.
    /// </summary>
    public static void cprom_set_chr_bank(byte bank) => throw null!;

    /// <summary>
    /// UNROM 512 (mapper 30): switch the 16KB PRG bank at $8000 by writing to $8000.
    /// </summary>
    public static void unrom512_set_prg_bank(byte bank) => throw null!;

    /// <summary>
    /// Camerica / Codemasters (mapper 71): switch the 16KB PRG bank at $8000 by writing to $8000/$C000.
    /// </summary>
    public static void camerica_set_prg_bank(byte bank) => throw null!;

    /// <summary>
    /// Holy Diver / Irem (mapper 78): write a packed bank/mirroring value to $8000.
    /// Layout <c>CCCC MPPP</c>: bits 2–0 = 16KB PRG, bit 3 = mirroring, bits 7–4 = 8KB CHR.
    /// </summary>
    public static void holydiver_set(byte value) => throw null!;

    /// <summary>
    /// Holy Diver (mapper 78): pack PRG, CHR, and mirroring bit and write to $8000.
    /// </summary>
    public static void holydiver_set_banks(byte prgBank, byte chrBank, byte mirror) => throw null!;

    /// <summary>NINA-003/006 (mapper 79) bank-select register ($4100).</summary>
    public const ushort NINA_BANK_SELECT = 0x4100;

    /// <summary>
    /// NINA-003/006 (mapper 79): write a packed bank-select value to $4100.
    /// Layout <c>.... .PCC</c>: bit 3 = 32KB PRG, bits 2–0 = 8KB CHR.
    /// </summary>
    public static void nina_set(byte value) => throw null!;

    /// <summary>
    /// NINA-003/006 (mapper 79): pack PRG (bit 3) and CHR (bits 2–0) and write to $4100.
    /// </summary>
    public static void nina_set_banks(byte prgBank, byte chrBank) => throw null!;

    /// <summary>
    /// Jaleco JF-13 style (mapper 87): switch the 8KB CHR bank by writing to $6000.
    /// </summary>
    public static void jaleco_set_chr_bank(byte bank) => throw null!;

    // Sunsoft FME-7 (mapper 69) — command/parameter pair

    /// <summary>FME-7 command register ($8000).</summary>
    public const ushort FME7_COMMAND = 0x8000;
    /// <summary>FME-7 parameter register ($A000).</summary>
    public const ushort FME7_PARAMETER = 0xA000;

    /// <summary>
    /// FME-7 (mapper 69): write <paramref name="command"/> to $8000 then <paramref name="parameter"/> to $A000.
    /// Both arguments must be compile-time constants.
    /// </summary>
    public static void fme7_write(byte command, byte parameter) => throw null!;

    /// <summary>
    /// FME-7: set a PRG bank register. <paramref name="reg"/> must be 8–11 (commands $08–$0B).
    /// </summary>
    public static void fme7_set_prg_bank(byte reg, byte bank) => throw null!;

    /// <summary>
    /// FME-7: set a CHR bank register. <paramref name="reg"/> must be 0–7 (commands $00–$07).
    /// </summary>
    public static void fme7_set_chr_bank(byte reg, byte bank) => throw null!;

    /// <summary>
    /// Mapper 15 (100-in-1 Contra): write a packed mode/bank value to $8000.
    /// </summary>
    public static void mapper15_set(byte value) => throw null!;

    /// <summary>
    /// Bandai (mapper 70): switch the 16KB PRG bank at $8000 by writing to $8000.
    /// </summary>
    public static void bandai_set_prg_bank(byte bank) => throw null!;

    /// <summary>
    /// CNROM variant (mapper 185): write CHR bank / disable value to $8000.
    /// </summary>
    public static void cnrom185_set_chr_bank(byte bank) => throw null!;

    /// <summary>
    /// HES NTD-8 (mapper 113): write a packed bank-select value to $4100.
    /// Layout <c>MCPP PCCC</c>: bits 5–3 = PRG, bits 2–0 + bit 6 = CHR, bit 7 = mirroring.
    /// </summary>
    public static void nina113_set(byte value) => throw null!;

    /// <summary>
    /// HES NTD-8 (mapper 113): pack PRG and CHR (CHR bit 3 → bit 6) and write to $4100.
    /// </summary>
    public static void nina113_set_banks(byte prgBank, byte chrBank) => throw null!;

    /// <summary>
    /// Action 53 (mapper 28): write a bank/mode value to $8000.
    /// </summary>
    public static void action53_set(byte value) => throw null!;

    /// <summary>
    /// Action 52 (mapper 228): write <paramref name="value"/> to <paramref name="register"/>.
    /// Bank bits are encoded in the register address on real hardware; pass the full CPU address.
    /// </summary>
    public static void action52_write(ushort register, byte value) => throw null!;

    // Konami VRC1 (mapper 75)

    /// <summary>VRC1 PRG bank at $8000–$9FFF.</summary>
    public const ushort VRC1_PRG_BANK0 = 0x8000;
    /// <summary>VRC1 PRG bank at $A000–$BFFF.</summary>
    public const ushort VRC1_PRG_BANK1 = 0xA000;
    /// <summary>VRC1 mirroring ($9000).</summary>
    public const ushort VRC1_MIRRORING = 0x9000;
    /// <summary>VRC1 CHR bank 0 ($E000).</summary>
    public const ushort VRC1_CHR_BANK0 = 0xE000;
    /// <summary>VRC1 CHR bank 1 ($F000).</summary>
    public const ushort VRC1_CHR_BANK1 = 0xF000;

    /// <summary>VRC1: set PRG bank 0 ($8000) or 1 ($A000). <paramref name="which"/> must be 0 or 1.</summary>
    public static void vrc1_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>VRC1: set CHR bank 0 ($E000) or 1 ($F000). <paramref name="which"/> must be 0 or 1.</summary>
    public static void vrc1_set_chr_bank(byte which, byte bank) => throw null!;

    /// <summary>VRC1: set mirroring via $9000 (0=vertical, 1=horizontal).</summary>
    public static void vrc1_set_mirroring(byte mirror) => throw null!;

    // Konami VRC2/VRC4 family (mappers 21–23, 25) — lean $8000/$A000 PRG surface

    /// <summary>VRC2/4 PRG bank at $8000–$9FFF.</summary>
    public const ushort VRC24_PRG_BANK0 = 0x8000;
    /// <summary>VRC2/4 PRG bank at $A000–$BFFF.</summary>
    public const ushort VRC24_PRG_BANK1 = 0xA000;
    /// <summary>VRC2/4 mirroring ($9000).</summary>
    public const ushort VRC24_MIRRORING = 0x9000;

    /// <summary>VRC2/4: set PRG bank 0 or 1. <paramref name="which"/> must be 0 or 1.</summary>
    public static void vrc24_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>VRC2/4: set mirroring via $9000.</summary>
    public static void vrc24_set_mirroring(byte mirror) => throw null!;

    // Konami VRC3 (mapper 73)

    /// <summary>VRC3 16KB PRG bank register ($F000).</summary>
    public const ushort VRC3_PRG_BANK = 0xF000;

    /// <summary>VRC3: switch the 16KB PRG bank at $8000 via $F000.</summary>
    public static void vrc3_set_prg_bank(byte bank) => throw null!;

    // Konami VRC6 (mappers 24, 26)

    /// <summary>VRC6 16KB PRG bank at $8000 ($8000).</summary>
    public const ushort VRC6_PRG_BANK16 = 0x8000;
    /// <summary>VRC6 8KB PRG bank at $C000 ($C000).</summary>
    public const ushort VRC6_PRG_BANK8 = 0xC000;

    /// <summary>VRC6: set the 16KB PRG bank at $8000–$BFFF.</summary>
    public static void vrc6_set_prg16(byte bank) => throw null!;

    /// <summary>VRC6: set the 8KB PRG bank at $C000–$DFFF.</summary>
    public static void vrc6_set_prg8(byte bank) => throw null!;

    // Konami VRC7 (mapper 85)

    /// <summary>VRC7 PRG bank at $8000–$9FFF.</summary>
    public const ushort VRC7_PRG_BANK0 = 0x8000;
    /// <summary>VRC7 PRG bank at $A000–$BFFF.</summary>
    public const ushort VRC7_PRG_BANK1 = 0xA000;

    /// <summary>VRC7: set PRG bank 0 or 1. <paramref name="which"/> must be 0 or 1.</summary>
    public static void vrc7_set_prg_bank(byte which, byte bank) => throw null!;

    // Namco 163 (mapper 19)

    /// <summary>Namco 163 PRG bank at $8000 ($E000).</summary>
    public const ushort NAMCO163_PRG_BANK0 = 0xE000;
    /// <summary>Namco 163 PRG bank at $A000 ($E800).</summary>
    public const ushort NAMCO163_PRG_BANK1 = 0xE800;
    /// <summary>Namco 163 PRG bank at $C000 ($F000).</summary>
    public const ushort NAMCO163_PRG_BANK2 = 0xF000;

    /// <summary>
    /// Namco 163: set PRG bank window. <paramref name="which"/> must be 0–2 ($E000/$E800/$F000).
    /// </summary>
    public static void namco163_set_prg_bank(byte which, byte bank) => throw null!;

    // Bandai FCG / LZ93D50 (mapper 16) — registers at $6000–$600F / $8000–$800F

    /// <summary>Bandai FCG PRG bank register ($8008 / $6008 depending on board).</summary>
    public const ushort BANDAI_PRG_BANK = 0x8008;
    /// <summary>Bandai FCG mirroring ($8009).</summary>
    public const ushort BANDAI_MIRRORING = 0x8009;

    /// <summary>Bandai FCG (mapper 16): set 16KB PRG bank via $8008.</summary>
    public static void bandai16_set_prg_bank(byte bank) => throw null!;

    /// <summary>Bandai FCG: set mirroring via $8009.</summary>
    public static void bandai16_set_mirroring(byte mirror) => throw null!;

    /// <summary>Bandai (mapper 152): write packed PRG/CHR/mirroring value to $8000.</summary>
    public static void bandai152_set(byte value) => throw null!;

    // Jaleco SS88006 (mapper 18)

    /// <summary>Jaleco SS88006 PRG bank low nibble at $8000.</summary>
    public const ushort JALECO18_PRG0_LO = 0x8000;
    /// <summary>Jaleco SS88006 PRG bank high nibble at $8001.</summary>
    public const ushort JALECO18_PRG0_HI = 0x8001;

    /// <summary>
    /// Jaleco SS88006 (mapper 18): write low then high nibble for PRG bank 0 ($8000/$8001).
    /// </summary>
    public static void jaleco18_set_prg0(byte bank) => throw null!;

    // Irem / Taito / Sunsoft register-style

    /// <summary>Irem G-101 (mapper 32) PRG bank at $8000.</summary>
    public const ushort IREM32_PRG_BANK0 = 0x8000;
    /// <summary>Irem G-101 PRG bank at $A000.</summary>
    public const ushort IREM32_PRG_BANK1 = 0xA000;

    /// <summary>Irem G-101: set PRG bank 0 or 1. <paramref name="which"/> must be 0 or 1.</summary>
    public static void irem32_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Taito TC0190 (mapper 33) PRG bank at $8000.</summary>
    public const ushort TAITO33_PRG_BANK0 = 0x8000;
    /// <summary>Taito TC0190 PRG bank at $8001.</summary>
    public const ushort TAITO33_PRG_BANK1 = 0x8001;

    /// <summary>Taito TC0190/TC069 (33/48): set PRG bank 0 or 1.</summary>
    public static void taito33_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Irem H3001 (mapper 65) PRG bank at $8000.</summary>
    public const ushort IREM65_PRG_BANK0 = 0x8000;
    /// <summary>Irem H3001 PRG bank at $A000.</summary>
    public const ushort IREM65_PRG_BANK1 = 0xA000;

    /// <summary>Irem H3001: set PRG bank 0 or 1.</summary>
    public static void irem65_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Sunsoft-3 (mapper 67) PRG bank ($8000).</summary>
    public const ushort SUNSOFT3_PRG_BANK = 0x8000;

    /// <summary>Sunsoft-3: switch 16KB PRG bank at $8000.</summary>
    public static void sunsoft3_set_prg_bank(byte bank) => throw null!;

    /// <summary>Sunsoft-4 (mapper 68) PRG bank ($8000).</summary>
    public const ushort SUNSOFT4_PRG_BANK = 0x8000;

    /// <summary>Sunsoft-4: switch 16KB PRG bank at $8000.</summary>
    public static void sunsoft4_set_prg_bank(byte bank) => throw null!;

    /// <summary>Jaleco JF-17 (mapper 72): write packed PRG/CHR value to $8000.</summary>
    public static void jaleco72_set(byte value) => throw null!;

    /// <summary>Namcot 3446 (mapper 76) PRG bank ($8000) — MMC3-like select/data via poke also works.</summary>
    public static void namcot76_set_prg_bank(byte bank) => throw null!;

    /// <summary>Taito X1-005 (mapper 80) PRG bank at $7EF8.</summary>
    public const ushort TAITO80_PRG_BANK0 = 0x7EF8;
    /// <summary>Taito X1-005 PRG bank at $7EF9.</summary>
    public const ushort TAITO80_PRG_BANK1 = 0x7EF9;

    /// <summary>Taito X1-005: set PRG bank 0 or 1.</summary>
    public static void taito80_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Jaleco JF-13 (mapper 86): write packed PRG/CHR value to $6000.</summary>
    public static void jaleco86_set(byte value) => throw null!;

    /// <summary>Namco 118 / DxROM cousin (mapper 88): MMC3-style select at $8000 then data at $8001.</summary>
    public static void namco88_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Sunsoft (mapper 89): write packed bank/mirroring value to $8000.</summary>
    public static void sunsoft89_set(byte value) => throw null!;

    /// <summary>Sunsoft (mapper 93): switch 16KB PRG bank via $8000.</summary>
    public static void sunsoft93_set_prg_bank(byte bank) => throw null!;

    /// <summary>Irem TAM-S1 (mapper 97): write packed PRG/mirroring value to $8000.</summary>
    public static void irem97_set(byte value) => throw null!;

    /// <summary>Jaleco JF-11/14 (mapper 140): write packed PRG/CHR value to $6000.</summary>
    public static void jaleco140_set(byte value) => throw null!;

    /// <summary>Sunsoft-1 (mapper 184): write CHR bank value to $6000.</summary>
    public static void sunsoft184_set_chr_bank(byte bank) => throw null!;

    /// <summary>TXC (mapper 189): write PRG bank to $4100–$4103 / $8000 (uses $8000).</summary>
    public static void txc189_set_prg_bank(byte bank) => throw null!;

    /// <summary>Namco 175/340 (mapper 210): set PRG bank window like Namco 163. <paramref name="which"/> 0–2.</summary>
    public static void namco210_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Multicart (mapper 225): write packed bank bits via address — use <see cref="action52_write"/>-style absolute store.</summary>
    public static void mapper225_write(ushort register, byte value) => throw null!;

    /// <summary>Multicart (mapper 226): write packed value to $8000.</summary>
    public static void mapper226_set(byte value) => throw null!;

    /// <summary>Camerica BF9096 (mapper 232): write outer/inner bank value to $8000.</summary>
    public static void bf9096_set(byte value) => throw null!;

    /// <summary>Multicart (mapper 241): switch 32KB PRG bank via $8000.</summary>
    public static void mapper241_set_prg_bank(byte bank) => throw null!;

    // MMC1 mapper register addresses for serial shift register writes via mmc1_write()

    /// <summary>
    /// Base address of the MMC1 mapper control register used for serial shift-register writes via mmc1_write().
    /// </summary>
    /// <remarks>Writes to this address follow the MMC1 serial write protocol: consecutive writes shift bits
    /// into an internal 5-bit register; a write with bit 7 set resets the shift register. After five valid writes the
    /// 5-bit value is latched and controls mirroring, PRG banking mode, and CHR banking. This constant represents the
    /// control register region base (typically mapped to 0x8000–0x9FFF).</remarks>
    public const ushort MMC1_CONTROL = 0x8000;

    /// <summary>
    /// Address of the MMC1 CHR bank 0 register in the NES CPU memory map.
    /// </summary>
    /// <remarks>Used to select the first CHR bank when the MMC1 mapper's CHR banking mode is active. Writing
    /// to this address sets the lower 4 KB CHR bank; behavior depends on the MMC1 control register's CHR
    /// mode.</remarks>
    public const ushort MMC1_CHR_BANK0 = 0xA000;

    /// <summary>
    /// Address label for the MMC1 mapper's CHR bank 1 in the assembled NES ROM.
    /// </summary>
    /// <remarks>Used as a 16-bit ROM address when emitting or referencing the first CHR bank for MMC1-based
    /// cartridges.</remarks>
    public const ushort MMC1_CHR_BANK1 = 0xC000;

    /// <summary>
    /// Address of the MMC1 PRG bank register used to select the active PRG ROM bank.
    /// </summary>
    /// <remarks>Memory-mapped CPU address $E000 for the MMC1 mapper's PRG bank register; writes to this
    /// address update the mapper's active program bank selection.</remarks>
    public const ushort MMC1_PRG_BANK = 0xE000;

    // MMC1 Control register PRG/CHR mode bits: use mirror | (MMC1Mirror)prg_chr_bits
    // when combining mirroring with PRG/CHR modes in mmc1_set_mirroring().

    // MMC1 Control register PRG/CHR mode bits (OR with mirroring constants)
    /// <summary>PRG mode: fix last bank at $C000, switch 16KB bank at $8000 (bits 2-3 = 11).</summary>
    public const byte MMC1_PRG_FIX_LAST = 0x0C;
    /// <summary>CHR mode: two separate 4KB banks (bit 4 = 1).</summary>
    public const byte MMC1_CHR_4K = 0x10;

    /// <summary>
    /// Write a 5-bit value to an MMC1 register using the serial shift register protocol.
    /// MMC1 requires writing one bit at a time (5 writes total) to latch a value.
    /// The addr must be one of the MMC1_* register constants.
    /// </summary>
    public static void mmc1_write(ushort addr, byte value) => throw null!;

    /// <summary>
    /// Switch the MMC1 PRG bank by writing to the PRG bank register ($E000).
    /// </summary>
    public static void mmc1_set_prg_bank(byte bank) => mmc1_write(MMC1_PRG_BANK, bank);

    /// <summary>
    /// Switch both MMC1 CHR banks by writing to CHR bank 0 ($A000) and CHR bank 1 ($C000).
    /// </summary>
    public static void mmc1_set_chr_bank(byte bank0, byte bank1)
    {
        mmc1_write(MMC1_CHR_BANK0, bank0);
        mmc1_write(MMC1_CHR_BANK1, bank1);
    }

    /// <summary>
    /// Write the full MMC1 Control register ($8000) via the serial shift register.
    /// The value contains: mirroring mode (bits 0-1), PRG bank mode (bits 2-3),
    /// and CHR bank mode (bit 4). Use <see cref="MMC1Mirror"/> values OR'd with PRG/CHR
    /// mode bits. Writing only a mirror constant (e.g., <see cref="MMC1Mirror.Vertical"/>)
    /// resets PRG/CHR modes to zero — combine with your desired mode bits.
    /// </summary>
    public static void mmc1_set_mirroring(byte mode) => mmc1_write(MMC1_CONTROL, mode);

    // ---- Additional commercial / multicart mappers (lean STA helpers) ----

    /// <summary>TXC / HWE (mapper 36): write PRG bank to $8000.</summary>
    public static void txc36_set_prg_bank(byte bank) => throw null!;

    /// <summary>Rumblestation 15-in-1 (mapper 46): write packed bank value to $6000.</summary>
    public static void mapper46_set(byte value) => throw null!;

    /// <summary>MMC3 multicart (mapper 47): write outer bank select to $6000.</summary>
    public static void mapper47_set_outer(byte bank) => throw null!;

    /// <summary>MMC3 multicart (mapper 49): write packed outer/PRG mode value to $6000.</summary>
    public static void mapper49_set(byte value) => throw null!;

    /// <summary>Irem (mapper 77): write packed PRG/CHR value to $8000.</summary>
    public static void irem77_set(byte value) => throw null!;

    /// <summary>Cony (mapper 83): write PRG bank to $8000.</summary>
    public static void cony83_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 91: write PRG bank to $8000 / $8001 style — STA $8000.</summary>
    public static void mapper91_set_prg_bank(byte bank) => throw null!;

    /// <summary>Jaleco (mapper 92): write packed PRG/CHR value to $8000.</summary>
    public static void jaleco92_set(byte value) => throw null!;

    /// <summary>Namcot 3425 (mapper 95): MMC3-like bank select/data via <see cref="namco88_set_bank"/>.</summary>
    public static void namcot95_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Oeka Kids (mapper 96): write CHR/PRG mode value to $8000.</summary>
    public static void oeka96_set(byte value) => throw null!;

    /// <summary>ASDER / NTDEC (mapper 112): write PRG bank to $8000.</summary>
    public static void asder112_set_prg_bank(byte bank) => throw null!;

    /// <summary>Sachen (mapper 133): write packed bank value to $4100.</summary>
    public static void sachen133_set(byte value) => throw null!;

    /// <summary>Mapper 156: write PRG bank to $C000.</summary>
    public static void mapper156_set_prg_bank(byte bank) => throw null!;

    /// <summary>Waixing / Chinese multicart (mapper 176): write PRG bank to $8000.</summary>
    public static void mapper176_set_prg_bank(byte bank) => throw null!;

    /// <summary>Hengeek / multicart (mapper 177): write PRG bank to $8000.</summary>
    public static void mapper177_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 178: write PRG bank to $8000.</summary>
    public static void mapper178_set_prg_bank(byte bank) => throw null!;

    /// <summary>Multicart (mapper 227): write packed value via absolute register address.</summary>
    public static void mapper227_write(ushort register, byte value) => throw null!;

    /// <summary>Multicart (mapper 229): write packed bank value to $8000.</summary>
    public static void mapper229_set(byte value) => throw null!;

    /// <summary>Multicart (mapper 230): write packed bank value to $8000.</summary>
    public static void mapper230_set(byte value) => throw null!;

    /// <summary>Multicart (mapper 231): write packed bank value to $8000.</summary>
    public static void mapper231_set(byte value) => throw null!;

    /// <summary>Maxi 15 (mapper 234): write bank select to $FF80–$FF9F — use absolute <paramref name="register"/>.</summary>
    public static void mapper234_write(ushort register, byte value) => throw null!;

    /// <summary>Multicart (mapper 235): write packed bank value to $8000.</summary>
    public static void mapper235_set(byte value) => throw null!;

    /// <summary>Mapper 240: write packed PRG/CHR value to $5FFF / $4020 — STA $5FFF.</summary>
    public static void mapper240_set(byte value) => throw null!;

    /// <summary>Mapper 242: write packed bank value to $8000.</summary>
    public static void mapper242_set(byte value) => throw null!;

    /// <summary>Mapper 246: write PRG bank to $6000–$6003 — STA $6000.</summary>
    public static void mapper246_set_prg_bank(byte bank) => throw null!;

    // ---- Remaining common iNES numbers (lean STA / MMC3-style) ----

    /// <summary>MMC3 pirate (mapper 37): outer bank via $6000; use <see cref="mmc3_set_prg_bank"/> for inner.</summary>
    public static void mapper37_set_outer(byte bank) => throw null!;

    /// <summary>NTDEC 2722 (mapper 40): write PRG bank to $8000.</summary>
    public static void mapper40_set_prg_bank(byte bank) => throw null!;

    /// <summary>Caltron 6-in-1 (mapper 41): write packed bank value to $6000.</summary>
    public static void mapper41_set(byte value) => throw null!;

    /// <summary>Mapper 42: write PRG bank to $8000.</summary>
    public static void mapper42_set_prg_bank(byte bank) => throw null!;

    /// <summary>MMC3 multicart (mapper 52): write outer bank to $6000.</summary>
    public static void mapper52_set_outer(byte bank) => throw null!;

    /// <summary>Mapper 57: write packed bank value to $8000.</summary>
    public static void mapper57_set(byte value) => throw null!;

    /// <summary>Mapper 58 / Study and Game: write packed bank value to $8000.</summary>
    public static void mapper58_set(byte value) => throw null!;

    /// <summary>Mapper 60 (NROM-128 reset-based carts): write to $8000 (often unused at runtime).</summary>
    public static void mapper60_set(byte value) => throw null!;

    /// <summary>Mapper 62: write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper62_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 74 (MMC3 + CHR RAM protect): same as MMC3 — <see cref="mmc3_set_prg_bank"/> / <see cref="mmc3_set_chr_bank"/>.</summary>
    public static void mapper74_set_prg_bank(byte reg, byte bank) => throw null!;

    /// <summary>Taito X1-017 (mapper 82): write PRG bank to $7EF0–$7EFF — STA $7EF0.</summary>
    public static void taito82_set_prg_bank(byte bank) => throw null!;

    /// <summary>JY Company (mapper 90): write PRG bank register index then data via $8000/$8001 style.</summary>
    public static void jy90_set_prg_bank(byte reg, byte bank) => throw null!;

    /// <summary>VS System (mapper 99): write CHR bank to $4016 (bit 2) — STA $4016.</summary>
    public static void vs99_set_chr_bank(byte bank) => throw null!;

    /// <summary>Bandai Datach / LZ93D50 (mapper 157): set PRG bank via $8008.</summary>
    public static void bandai157_set_prg_bank(byte bank) => throw null!;

    /// <summary>Tengen 800037 (mapper 158): MMC3-like bank select/data.</summary>
    public static void tengen158_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Nanjing / Waixing (mapper 163): write PRG bank to $5000–$5003 — STA $5000.</summary>
    public static void mapper163_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 164: write PRG bank to $5000.</summary>
    public static void mapper164_set_prg_bank(byte bank) => throw null!;

    /// <summary>MMC3 variant (mapper 182): MMC3-like bank select/data.</summary>
    public static void mapper182_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>JY Company (mapper 209): same register surface as mapper 90.</summary>
    public static void jy209_set_prg_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 243 (Sachen 74LS374N): write packed bank value to $4100.</summary>
    public static void sachen243_set(byte value) => throw null!;

    /// <summary>Mapper 245 (Waixing MMC3 variant): MMC3-like bank select/data.</summary>
    public static void mapper245_set_bank(byte reg, byte bank) => throw null!;

    // ---- Broad coverage: FFE / BMC / Sachen / Waixing / MMC3 pirates ----

    /// <summary>Generic bus-conflict bank write to $8000 (many simple/pirate mappers).</summary>
    public static void mapper_bus_set(byte value) => throw null!;

    /// <summary>Generic outer-bank write to $6000 (many multicarts).</summary>
    public static void mapper_outer_set(byte value) => throw null!;

    /// <summary>FFE (mapper 6): write PRG bank to $8000.</summary>
    public static void ffe6_set_prg_bank(byte bank) => throw null!;

    /// <summary>FFE (mapper 8): write packed PRG/CHR value to $8000.</summary>
    public static void ffe8_set(byte value) => throw null!;

    /// <summary>MMC3 variant (mapper 12): MMC3-like bank select/data.</summary>
    public static void mapper12_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 14: MMC3-like bank select/data.</summary>
    public static void mapper14_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>FFE (mapper 17): write PRG bank to $4504–$4507 — STA $4504.</summary>
    public static void ffe17_set_prg_bank(byte bank) => throw null!;

    /// <summary>VRC4 variant (mapper 27): same as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper27_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>NSF / homebrew (mapper 31): write 4KB PRG bank via $5000–$5FFF — STA $5000.</summary>
    public static void mapper31_set_prg_bank(byte bank) => throw null!;

    /// <summary>BMC (mapper 35): write PRG bank to $8000.</summary>
    public static void mapper35_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 38: write packed bank value to $7000.</summary>
    public static void mapper38_set(byte value) => throw null!;

    /// <summary>Mapper 39: write PRG bank to $8000.</summary>
    public static void mapper39_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 43: write PRG bank to $8000.</summary>
    public static void mapper43_set_prg_bank(byte bank) => throw null!;

    /// <summary>MMC3 multicart (mapper 44): outer bank via $6000.</summary>
    public static void mapper44_set_outer(byte bank) => throw null!;

    /// <summary>MMC3 multicart (mapper 45): outer bank via $6000.</summary>
    public static void mapper45_set_outer(byte bank) => throw null!;

    /// <summary>Mapper 50: write PRG bank to $4020–$403F — STA $4020.</summary>
    public static void mapper50_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 51: write packed bank value to $6000.</summary>
    public static void mapper51_set(byte value) => throw null!;

    /// <summary>Mapper 53: write packed bank value to $6000.</summary>
    public static void mapper53_set(byte value) => throw null!;

    /// <summary>Mapper 54 / Novel Diamond: write packed bank value to $8000.</summary>
    public static void mapper54_set(byte value) => throw null!;

    /// <summary>Mapper 56: write PRG bank to $8000.</summary>
    public static void mapper56_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 61: write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper61_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 63: write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper63_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 81: write PRG bank to $8000.</summary>
    public static void mapper81_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 84: write PRG bank to $8000.</summary>
    public static void mapper84_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 98: write PRG bank to $8000.</summary>
    public static void mapper98_set_prg_bank(byte bank) => throw null!;

    /// <summary>JF-13 / GNROM cousin (mapper 101): write CHR bank to $8000.</summary>
    public static void mapper101_set_chr_bank(byte bank) => throw null!;

    /// <summary>Mapper 107: write packed bank value to $8000.</summary>
    public static void mapper107_set(byte value) => throw null!;

    /// <summary>Mapper 111 (GTROM / Cheapocabra): write bank/flash value to $5000–$5FFF — STA $5000.</summary>
    public static void mapper111_set(byte value) => throw null!;

    /// <summary>MMC3 pirate (mapper 114): MMC3-like bank select/data.</summary>
    public static void mapper114_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>MMC3 pirate (mapper 115): outer via $6000; inner via MMC3 helpers.</summary>
    public static void mapper115_set_outer(byte bank) => throw null!;

    /// <summary>MMC3 pirate (mapper 116): MMC3-like bank select/data.</summary>
    public static void mapper116_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 117: write PRG bank to $8000.</summary>
    public static void mapper117_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 132: write packed bank value to $4100.</summary>
    public static void mapper132_set(byte value) => throw null!;

    /// <summary>Sachen (mapper 136): write packed bank value to $4100.</summary>
    public static void sachen136_set(byte value) => throw null!;

    /// <summary>Sachen (mapper 137): write packed bank value to $4100.</summary>
    public static void sachen137_set(byte value) => throw null!;

    /// <summary>Sachen (mapper 138): write packed bank value to $4100.</summary>
    public static void sachen138_set(byte value) => throw null!;

    /// <summary>Sachen (mapper 139): write packed bank value to $4100.</summary>
    public static void sachen139_set(byte value) => throw null!;

    /// <summary>Sachen (mapper 141): write packed bank value to $4100.</summary>
    public static void sachen141_set(byte value) => throw null!;

    /// <summary>Mapper 142: write PRG bank to $8000.</summary>
    public static void mapper142_set_prg_bank(byte bank) => throw null!;

    /// <summary>Sachen (mapper 143): read-only protect style — write to $4100.</summary>
    public static void sachen143_set(byte value) => throw null!;

    /// <summary>Mapper 144: write packed bank value to $8000.</summary>
    public static void mapper144_set(byte value) => throw null!;

    /// <summary>Sachen (mapper 145): write CHR bank to $4100.</summary>
    public static void sachen145_set_chr_bank(byte bank) => throw null!;

    /// <summary>Sachen (mapper 146): write packed bank value to $4100.</summary>
    public static void sachen146_set(byte value) => throw null!;

    /// <summary>Sachen (mapper 147): write packed bank value to $4100.</summary>
    public static void sachen147_set(byte value) => throw null!;

    /// <summary>Sachen (mapper 148): write packed bank value to $4100.</summary>
    public static void sachen148_set(byte value) => throw null!;

    /// <summary>Sachen (mapper 149): write CHR bank to $8000.</summary>
    public static void sachen149_set_chr_bank(byte bank) => throw null!;

    /// <summary>Sachen (mapper 150): write packed bank value to $4100.</summary>
    public static void sachen150_set(byte value) => throw null!;

    /// <summary>Bandai (mapper 153): set PRG bank via $8008.</summary>
    public static void bandai153_set_prg_bank(byte bank) => throw null!;

    /// <summary>Bandai (mapper 154): write packed bank/mirroring value to $8000.</summary>
    public static void bandai154_set(byte value) => throw null!;

    /// <summary>Bandai (mapper 159): set PRG bank via $8008.</summary>
    public static void bandai159_set_prg_bank(byte bank) => throw null!;

    /// <summary>Waixing (mapper 165): MMC3-like bank select/data.</summary>
    public static void mapper165_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 166: write PRG bank to $8000.</summary>
    public static void mapper166_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 167: write PRG bank to $8000.</summary>
    public static void mapper167_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 180 already covered via UxROM cousins; mapper 188: write PRG bank to $7FF0.</summary>
    public static void mapper188_set_prg_bank(byte bank) => throw null!;

    /// <summary>Waixing MMC3 (mapper 191–199): MMC3-like bank select/data.</summary>
    public static void mapper191_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 200: write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper200_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 201: write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper201_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 202: write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper202_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 203: write packed bank value to $8000.</summary>
    public static void mapper203_set(byte value) => throw null!;

    /// <summary>Mapper 205: MMC3 multicart outer via $6000.</summary>
    public static void mapper205_set_outer(byte bank) => throw null!;

    /// <summary>Taito X1-005 variant (mapper 207): PRG via $7EF8.</summary>
    public static void mapper207_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 212: write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper212_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 221: write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper221_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 222: write PRG bank to $8000.</summary>
    public static void mapper222_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 224: write PRG bank to $8000.</summary>
    public static void mapper224_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 233: write packed bank value to $8000.</summary>
    public static void mapper233_set(byte value) => throw null!;

    /// <summary>Mapper 236: write packed bank value to $8000.</summary>
    public static void mapper236_set(byte value) => throw null!;

    /// <summary>Mapper 237: write packed bank value to $8000.</summary>
    public static void mapper237_set(byte value) => throw null!;

    /// <summary>Mapper 238: MMC3-like bank select/data.</summary>
    public static void mapper238_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 244: write packed bank value to $8000.</summary>
    public static void mapper244_set(byte value) => throw null!;

    /// <summary>Mapper 248: MMC3-like bank select/data.</summary>
    public static void mapper248_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 250: MMC3-like bank select/data (address-encoded).</summary>
    public static void mapper250_write(ushort register, byte value) => throw null!;

    // ---- NESDev commercial gaps (lean aliases / register surface) ----

    /// <summary>VRC4a/c (mapper 21): same lean PRG surface as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper21_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>VRC2a (mapper 22): same lean PRG surface as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper22_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>VRC2b/VRC4e (mapper 23): same lean PRG surface as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper23_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>VRC6a (mapper 24): set 16KB PRG bank at $8000 — same as <see cref="vrc6_set_prg16"/>.</summary>
    public static void mapper24_set_prg16(byte bank) => throw null!;

    /// <summary>VRC4b/d (mapper 25): same lean PRG surface as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper25_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>VRC6b (mapper 26): set 16KB PRG bank at $8000 — same as <see cref="vrc6_set_prg16"/>.</summary>
    public static void mapper26_set_prg16(byte bank) => throw null!;

    /// <summary>Taito TC0690 (mapper 48): PRG regs at $8000/$8001 — same as <see cref="taito33_set_prg_bank"/>.</summary>
    public static void taito48_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Tengen RAMBO-1 (mapper 64): MMC3-like select/data at $8000/$8001 (also R8/R9/RF).</summary>
    public static void rambo64_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>UN1ROM / HVC-UN1ROM (mapper 94): pack PRG bank into bits 2–4 and write to $8000 (bus conflicts).</summary>
    public static void un1rom_set_prg_bank(byte bank) => throw null!;

    /// <summary>NES-EVENT / NWC (mapper 105): MMC1 serial write — same as <see cref="mmc1_write"/>.</summary>
    public static void mapper105_write(ushort addr, byte value) => throw null!;

    /// <summary>TxSROM (mapper 118): MMC3-like bank select/data (CHR A17 → CIRAM A10).</summary>
    public static void txsrom118_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>TQROM (mapper 119): MMC3-like bank select/data (CHR ROM+RAM).</summary>
    public static void tqrom119_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>MMC1A (mapper 155): MMC1 serial write — same as <see cref="mmc1_write"/>.</summary>
    public static void mapper155_write(ushort addr, byte value) => throw null!;

    /// <summary>Crazy Climber / inverted UxROM (mapper 180): write PRG bank for $C000–$FFFF to $8000 (bus conflicts).</summary>
    public static void mapper180_set_prg_bank(byte bank) => throw null!;

    /// <summary>Waixing FS308 (mapper 192): MMC3-like bank select/data.</summary>
    public static void mapper192_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Waixing MMC3 (mapper 193): MMC3-like bank select/data.</summary>
    public static void mapper193_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Waixing MMC3 (mapper 194): MMC3-like bank select/data.</summary>
    public static void mapper194_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Waixing MMC3 (mapper 195): MMC3-like bank select/data.</summary>
    public static void mapper195_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Waixing MMC3 (mapper 196): MMC3-like bank select/data.</summary>
    public static void mapper196_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Waixing MMC3 (mapper 197): MMC3-like bank select/data.</summary>
    public static void mapper197_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Waixing MMC3 (mapper 198): MMC3-like bank select/data.</summary>
    public static void mapper198_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Waixing MMC3 (mapper 199): MMC3-like bank select/data.</summary>
    public static void mapper199_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>DxROM / Namco 118 / MIMIC-1 (mapper 206): MMC3-predecessor select/data at $8000/$8001.</summary>
    public static void dxrom206_set_bank(byte reg, byte bank) => throw null!;

    // ---- Spec-checked additions (NESDev) ----

    /// <summary>
    /// RET-CUFROM / Sealie (mapper 29): pack PRG (bits 2–4) and CHR-RAM (bits 0–1) to $8000.
    /// </summary>
    public static void retcufrom29_set(byte value) => throw null!;

    /// <summary>RET-CUFROM (mapper 29): pack 16KB PRG and 8KB CHR-RAM banks to $8000.</summary>
    public static void retcufrom29_set_banks(byte prgBank, byte chrBank) => throw null!;

    /// <summary>Vs. System VRC1 (mapper 151): same as <see cref="vrc1_set_prg_bank"/> (prefer mapper 75).</summary>
    public static void mapper151_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Racermate Challenge 2 (mapper 168): write packed PRG/CHR value to $8000 ($8000–$BFFF).</summary>
    public static void racermate168_set(byte value) => throw null!;

    /// <summary>Gimmick! VRC4 pirate (mapper 183): same lean PRG surface as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper183_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Magic Kid GooGoo (mapper 190): write PRG bank bits to <paramref name="register"/> ($8000 or $C000).</summary>
    public static void mapper190_write(ushort register, byte value) => throw null!;

    /// <summary>Pirate multicart (mapper 204): write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper204_write(ushort register, byte value) => throw null!;

    /// <summary>Street Fighter IV pirate (mapper 208): PRG/mirroring via $4800; CHR via MMC3 helpers.</summary>
    public static void mapper208_set_prg(byte value) => throw null!;

    /// <summary>Mapper 208: MMC3-like CHR/IRQ bank select/data.</summary>
    public static void mapper208_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Pirate multicart (mapper 213): write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper213_write(ushort register, byte value) => throw null!;

    /// <summary>Pirate MMC3 (mapper 215): MMC3-like bank select/data.</summary>
    public static void mapper215_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Kasheng A9461 (mapper 219): MMC3-like bank select/data.</summary>
    public static void mapper219_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 219: outer bank bit via $5002/$5003 — STA absolute <paramref name="register"/>.</summary>
    public static void mapper219_set_outer(ushort register, byte value) => throw null!;

    /// <summary>Mapper 220: write packed bank value to $8000.</summary>
    public static void mapper220_set(byte value) => throw null!;

    /// <summary>Mapper 223: write PRG bank to $8000.</summary>
    public static void mapper223_set_prg_bank(byte bank) => throw null!;

    /// <summary>Mapper 247: MMC3-like bank select/data.</summary>
    public static void mapper247_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 249: MMC3-like bank select/data.</summary>
    public static void mapper249_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 252: VRC-like / Waixing — write PRG bank via $8000/$A000 style.</summary>
    public static void mapper252_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Mapper 253: VRC-like — write PRG bank via $8000/$A000 style.</summary>
    public static void mapper253_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Mapper 254: MMC3 pirate outer via $6000.</summary>
    public static void mapper254_set_outer(byte bank) => throw null!;

    // ---- Remaining documented boards (lean) ----

    /// <summary>Doki Doki Panic pirate (mapper 103): write 8KB PRG bank for $6000 to $8000.</summary>
    public static void mapper103_set_prg_bank(byte bank) => throw null!;

    /// <summary>SMB3 pirate discrete (mapper 106): write PRG bank to $8008–$800B — absolute <paramref name="register"/>.</summary>
    public static void mapper106_write(ushort register, byte value) => throw null!;

    /// <summary>
    /// T4A54A / WX-KB4K / BS-5652 (mapper 134): write mode/outer/CNROM register value to $6000–$6003
    /// (lean: STA $6000). Use <see cref="mapper134_set_bank"/> for MMC3-compatible $8000/$8001.
    /// </summary>
    public static void mapper134_set(byte value) => throw null!;

    /// <summary>Mapper 134: MMC3-compatible bank select/data at $8000/$8001.</summary>
    public static void mapper134_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Sachen 8259 (mapper 135): write register select/data via $4100 (same lean surface as other 8259 boards).</summary>
    public static void mapper135_set(byte value) => throw null!;

    /// <summary>
    /// Former duplicate of JY Company ASIC (mapper 160 ≡ 90): MMC3-like bank select/data — prefer <see cref="jy90_set_prg_bank"/>.
    /// </summary>
    public static void mapper160_set_prg_bank(byte reg, byte bank) => throw null!;

    /// <summary>
    /// Hanjuku Hero mis-assignment (mapper 161 ≡ MMC1 / mapper 1): serial shift-register write — same as <see cref="mmc1_write"/>.
    /// Prefer mapper 1 helpers for new code.
    /// </summary>
    public static void mapper161_write(ushort addr, byte value) => throw null!;

    /// <summary>Waixing FS304 (mapper 162): write PRG bank low/CHR mode to $5000.</summary>
    public static void mapper162_set_prg_low(byte value) => throw null!;

    /// <summary>Waixing FS304 (mapper 162): write PRG bank high to $5200.</summary>
    public static void mapper162_set_prg_high(byte value) => throw null!;

    /// <summary>Waixing FS304 (mapper 162): write mode to $5300.</summary>
    public static void mapper162_set_mode(byte value) => throw null!;

    /// <summary>Idea-Tek / TXC (mapper 171): write packed bank value to $8000.</summary>
    public static void mapper171_set(byte value) => throw null!;

    /// <summary>Idea-Tek (mapper 172): write packed bank value to $4100.</summary>
    public static void mapper172_set(byte value) => throw null!;

    /// <summary>Idea-Tek (mapper 173): write CHR control to $4101 / $8000 — absolute <paramref name="register"/>.</summary>
    public static void mapper173_write(ushort register, byte value) => throw null!;

    /// <summary>NTDec 5-in-1 (mapper 174): write packed mode/bank/mirroring value to $8000 (like mapper 58, bits moved).</summary>
    public static void mapper174_set(byte value) => throw null!;

    /// <summary>Mapper 175: write packed bank value to $8000.</summary>
    public static void mapper175_set(byte value) => throw null!;

    /// <summary>Nestopia duplicate of mapper 176 (mapper 179): write PRG bank to $8000 — prefer <see cref="mapper176_set_prg_bank"/>.</summary>
    public static void mapper179_set(byte value) => throw null!;

    /// <summary>
    /// FCEUX Seicross v2 stub (mapper 181 ≈ 185 CHR-disable heuristic): lean write to $8000.
    /// Prefer mapper 185 / CNROM disable heuristics; no distinct bank hardware per NESDev.
    /// </summary>
    public static void mapper181_set(byte value) => throw null!;

    /// <summary>Kasheng A98402 (mapper 187): NROM override / outer at $5000; use MMC3 helpers for inner.</summary>
    public static void mapper187_set_outer(byte value) => throw null!;

    /// <summary>Mapper 187: MMC3-like bank select/data.</summary>
    public static void mapper187_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>JY Company (mapper 211): same register surface as mapper 90 / 209.</summary>
    public static void jy211_set_prg_bank(byte reg, byte bank) => throw null!;

    /// <summary>Pirate MMC3 (mapper 214): MMC3-like bank select/data.</summary>
    public static void mapper214_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Pirate multicart (mapper 216): write packed bank value via absolute <paramref name="register"/>.</summary>
    public static void mapper216_write(ushort register, byte value) => throw null!;

    /// <summary>Pirate MMC3 (mapper 217): MMC3-like bank select/data.</summary>
    public static void mapper217_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 239: write packed bank value to $8000.</summary>
    public static void mapper239_set(byte value) => throw null!;

    /// <summary>Mapper 251: MMC3 multicart outer via $6000.</summary>
    public static void mapper251_set_outer(byte bank) => throw null!;

    /// <summary>Mapper 255: write packed bank value via absolute <paramref name="register"/> (Action 52 cousin).</summary>
    public static void mapper255_write(ushort register, byte value) => throw null!;

    // ---- Batch 11 (NESDev audit continuation) ----

    /// <summary>
    /// BMC-T3H53 / D1038 (mapper 59): write packed latch <c>LD SPPp MCCC</c> to $8000–$FFFF.
    /// </summary>
    public static void mapper59_set(byte value) => throw null!;

    /// <summary>Nesticle MMC3 hack (mapper 100): MMC3-like bank select/data at $8000/$8001 (extended indices).</summary>
    public static void mapper100_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>
    /// FDS conversion (mapper 108): write 8KB PRG bank for $6000–$7FFF to $8000 (submapper 3 full range; ignore stray writes per game).
    /// </summary>
    public static void mapper108_set_prg_bank(byte bank) => throw null!;

    /// <summary>Kasheng A9711/A9713 (mapper 121): MMC3-compatible bank select/data.</summary>
    public static void mapper121_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 121 A9713: outer 256 KiB bank via $5180.</summary>
    public static void mapper121_set_outer(byte value) => throw null!;

    /// <summary>Kasheng H2288 (mapper 123): MMC3-compatible bank select/data (scrambled indices left to caller).</summary>
    public static void mapper123_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 123: NROM override register at $5800.</summary>
    public static void mapper123_set_outer(byte value) => throw null!;

    /// <summary>LH32 Monty on the Run FDS conversion (mapper 125): write 8KB PRG bank for $6000 to $6000.</summary>
    public static void mapper125_set_prg_bank(byte bank) => throw null!;

    /// <summary>LH15 Tobidase Daisakusen FDS conversion (mapper 120): write 8KB PRG bank for $6000 to $41FF.</summary>
    public static void mapper120_set_prg_bank(byte bank) => throw null!;

    /// <summary>
    /// Yuxing / GoodNES Contra 168-in-1 mis-tag (mapper 169 ≡ 15): write packed latch to $8000 — prefer <see cref="mapper15_set"/>.
    /// </summary>
    public static void mapper169_set(byte value) => throw null!;

    // ---- Batch 12: classic aliases + NES 2.0 lean ----

    /// <summary>Sachen Great Wall mis-tag (mapper 109 ≡ 137 / 8259D): write via $4100 — prefer <see cref="sachen137_set"/>.</summary>
    public static void mapper109_set(byte value) => throw null!;

    /// <summary>Sachen Honey Peach mis-tag (mapper 110 ≡ 243): write via $4100 — prefer <see cref="sachen243_set"/>.</summary>
    public static void mapper110_set(byte value) => throw null!;

    /// <summary>fwNES Sunsoft-1 mis-tag (mapper 122 ≡ 184): write CHR banks to $6000 — prefer <see cref="sunsoft184_set_chr_bank"/>.</summary>
    public static void mapper122_set_chr_bank(byte bank) => throw null!;

    /// <summary>Duplicate of mapper 58 (mapper 129): write packed value to $8000 — prefer <see cref="mapper58_set"/>.</summary>
    public static void mapper129_set(byte value) => throw null!;

    /// <summary>Alt assignment of mapper 205 (mapper 131): outer via $6000 — prefer <see cref="mapper205_set_outer"/>.</summary>
    public static void mapper131_set_outer(byte bank) => throw null!;

    /// <summary>TEC9719 / ING family (mapper 126 ≡ 422/534): outer/mode via $6000–$6003 — lean STA $6000.</summary>
    public static void mapper126_set_outer(byte value) => throw null!;

    /// <summary>Mapper 126: MMC3-compatible bank select/data.</summary>
    public static void mapper126_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 534 (ING-022; same outer as 126/422): outer via $6000.</summary>
    public static void mapper534_set_outer(byte value) => throw null!;

    /// <summary>Mapper 534: MMC3-compatible bank select/data.</summary>
    public static void mapper534_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>HP10xx/HP20xx multicart (mapper 260): mode register at $5000.</summary>
    public static void mapper260_set_mode(byte value) => throw null!;

    /// <summary>Mapper 260: PRG base at $5001.</summary>
    public static void mapper260_set_prg_base(byte value) => throw null!;

    /// <summary>Mapper 260: CHR base at $5002.</summary>
    public static void mapper260_set_chr_base(byte value) => throw null!;

    /// <summary>Mapper 260: MMC3-compatible bank select/data.</summary>
    public static void mapper260_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>MMC3 multicart (mapper 269): sequential outer writes at $5000.</summary>
    public static void mapper269_set_outer(byte value) => throw null!;

    /// <summary>Mapper 269: MMC3-compatible bank select/data.</summary>
    public static void mapper269_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Yoko Soft Cony (mapper 264): write outer PRG bank to $8000.</summary>
    public static void mapper264_set_outer(byte value) => throw null!;

    /// <summary>Mapper 264: write mode to $8400.</summary>
    public static void mapper264_set_mode(byte value) => throw null!;

    /// <summary>BMC-12-IN-1 / NS03 (mapper 331): write packed bank/mode via absolute <paramref name="register"/> ($A000/$C000/$E000).</summary>
    public static void mapper331_write(ushort register, byte value) => throw null!;

    // ---- Batch 13 ----

    /// <summary>Kaiser KS7030 Doki Doki Panic (mapper 347): write bank/mirroring via absolute <paramref name="register"/> ($8000/$9000).</summary>
    public static void mapper347_write(ushort register, byte value) => throw null!;

    /// <summary>Asder PC-95 (mapper 365): select 8KB PRG bank; <paramref name="which"/> 0–3 → $8000–$8003.</summary>
    public static void mapper365_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Spanish PEC-586 cartridge (mapper 371): main register at $5000.</summary>
    public static void mapper371_set(byte value) => throw null!;

    /// <summary>Mapper 371: auxiliary register at $5100.</summary>
    public static void mapper371_set_aux(byte value) => throw null!;

    /// <summary>Fine Studio Batman/RoboCop pirate (mapper 417): write via absolute <paramref name="register"/> ($8000–$8023).</summary>
    public static void mapper417_write(ushort register, byte value) => throw null!;

    /// <summary>Mindkids SMD172B FPGA (mapper 446): supervisor mapper-select / lock at $5000.</summary>
    public static void mapper446_set_mode(byte value) => throw null!;

    /// <summary>Mapper 446: supervisor write via absolute <paramref name="register"/> ($5001–$5006).</summary>
    public static void mapper446_write(ushort register, byte value) => throw null!;

    // ---- Batch 14 ----

    /// <summary>City Fighter IV (mapper 266): 32KB PRG select at $900C (VRC4 clone overlay).</summary>
    public static void mapper266_set_prg(byte value) => throw null!;

    /// <summary>Dragon Fighter MMC3 clone (mapper 292): PRG bank for $8000 at $6000.</summary>
    public static void mapper292_set_prg(byte bank) => throw null!;

    /// <summary>Mapper 292: MMC3-compatible bank select/data.</summary>
    public static void mapper292_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>FC Pocket / My Arcade VT3x (mapper 296): mapper config at $411D.</summary>
    public static void mapper296_set_config(byte value) => throw null!;

    /// <summary>Mapper 296: outer bank at $412C.</summary>
    public static void mapper296_set_outer(byte value) => throw null!;

    /// <summary>Techline XB multicart (mapper 351): CHR base / ASIC mode at $5000.</summary>
    public static void mapper351_set_chr_base(byte value) => throw null!;

    /// <summary>Mapper 351: PRG base at $5001.</summary>
    public static void mapper351_set_prg_base(byte value) => throw null!;

    /// <summary>Mapper 351: banking mode at $5002.</summary>
    public static void mapper351_set_mode(byte value) => throw null!;

    /// <summary>Mapper 351: MMC3-compatible bank select/data (when in MMC3 ASIC mode).</summary>
    public static void mapper351_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>SB-5013 / GCL8050 multicart (mapper 359): 8KB PRG; <paramref name="which"/> 0–3 → $8000–$8003.</summary>
    public static void mapper359_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>Mapper 359: PRG base at $9000.</summary>
    public static void mapper359_set_prg_base(byte value) => throw null!;

    /// <summary>2 MiB address-latch multicart (mapper 375): write via absolute <paramref name="register"/> (enhancement of 227).</summary>
    public static void mapper375_write(ushort register, byte value) => throw null!;

    /// <summary>JY YY840708C Soccer 6-in-1 (mapper 383): MMC3-compatible bank select/data.</summary>
    public static void mapper383_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 383: PAL outer bits via absolute <paramref name="register"/> ($8100 mask).</summary>
    public static void mapper383_write(ushort register, byte value) => throw null!;

    /// <summary>DS-9-27 multicart (mapper 452): address/data latch via absolute <paramref name="register"/> ($8000–$DFFF).</summary>
    public static void mapper452_write(ushort register, byte value) => throw null!;

    // ---- Batch 15 ----

    /// <summary>A65AS / JY-066 multicart (mapper 285): packed data latch to $8000.</summary>
    public static void mapper285_set(byte value) => throw null!;

    /// <summary>Benshieng BS-5 (mapper 286): address-latch bank select via absolute <paramref name="register"/>.</summary>
    public static void mapper286_write(ushort register, byte value) => throw null!;

    /// <summary>Kaiser KS7057 Gyruss FDS conversion (mapper 302): write via absolute <paramref name="register"/> ($8000/$B000–$E003).</summary>
    public static void mapper302_write(ushort register, byte value) => throw null!;

    /// <summary>Kaiser KS7031 Dracula II FDS conversion (mapper 305): address-select PRG bank via absolute <paramref name="register"/>.</summary>
    public static void mapper305_write(ushort register, byte value) => throw null!;

    /// <summary>FAM250 / 810139C multicart (mapper 354): address/data latch via absolute <paramref name="register"/> ($E000–$FFFF; like mapper 15+).</summary>
    public static void mapper354_write(ushort register, byte value) => throw null!;

    /// <summary>Bit Corp 4602 (mapper 357): PRG bank select at $4022 (SMB2J mode).</summary>
    public static void mapper357_set_prg(byte value) => throw null!;

    /// <summary>Mapper 357: secondary bank/IRQ latch at $4120.</summary>
    public static void mapper357_set_aux(byte value) => throw null!;

    /// <summary>Yung-08 SMB2J conversion (mapper 368): PRG bank at $4022.</summary>
    public static void mapper368_set_prg(byte value) => throw null!;

    /// <summary>970630C / KN-35A multicart (mapper 380): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper380_write(ushort register, byte value) => throw null!;

    /// <summary>NC7000M multicart (mapper 444): outer/mode at $6000 (WRAM enable required on hardware).</summary>
    public static void mapper444_set_outer(byte value) => throw null!;

    /// <summary>Mapper 444: MMC3-compatible bank select/data.</summary>
    public static void mapper444_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Haratyler (mapper 451): bank/IRQ/mirroring via absolute <paramref name="register"/> ($A000/$C000/$E000).</summary>
    public static void mapper451_write(ushort register, byte value) => throw null!;

    /// <summary>110-in-1 custom 227-like (mapper 454): address/data latch via absolute <paramref name="register"/>.</summary>
    public static void mapper454_write(ushort register, byte value) => throw null!;

    // ---- Batch 16 ----

    /// <summary>OneBus UIO multicart (mapper 270): outer PRG via $412C.</summary>
    public static void mapper270_set_outer(byte value) => throw null!;

    /// <summary>RCM Cartridge Story (mapper 274): inner bank/mirroring at $8000.</summary>
    public static void mapper274_set_inner(byte value) => throw null!;

    /// <summary>Mapper 274: outer bank/chip select via absolute <paramref name="register"/> ($A000–$FFFF).</summary>
    public static void mapper274_write(ushort register, byte value) => throw null!;

    /// <summary>Whirlwind SMB2J-family FDS conversion (mapper 304): PRG bank for $6000 at $4027.</summary>
    public static void mapper304_set_prg(byte value) => throw null!;

    /// <summary>BMC-830134C multicart (mapper 315): outer at $6800; MMC3 when WRAM enabled.</summary>
    public static void mapper315_set_outer(byte value) => throw null!;

    /// <summary>Mapper 315: MMC3-compatible bank select/data.</summary>
    public static void mapper315_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BS-110 MMC3 variant (mapper 391): outer/mode at $6000.</summary>
    public static void mapper391_set_outer(byte value) => throw null!;

    /// <summary>Mapper 391: MMC3-compatible bank select/data.</summary>
    public static void mapper391_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>JY YY840820C (mapper 398): outer/mode via absolute <paramref name="register"/> (overlaps VRC4).</summary>
    public static void mapper398_write(ushort register, byte value) => throw null!;

    /// <summary>Milowork FCFC1 A-mode (mapper 429): packed PRG/CHR latch to $8000 (no bus conflicts).</summary>
    public static void mapper429_set(byte value) => throw null!;

    /// <summary>Subor Dance 2000 (mapper 518): PRG bank at $5000.</summary>
    public static void mapper518_set_prg(byte value) => throw null!;

    /// <summary>Mapper 518: mode at $5200.</summary>
    public static void mapper518_set_mode(byte value) => throw null!;

    /// <summary>UNL-831128C Rugby 2-in-1 (mapper 528): FME-7-like absolute <paramref name="register"/> ($A000/$C000).</summary>
    public static void mapper528_write(ushort register, byte value) => throw null!;

    /// <summary>Mao Zedong VRC4 pirate (mapper 542): same lean PRG surface as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper542_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>CH-501 MMC1 multicart (mapper 543): serial outer bit stream at $5000 (lean STA $5000).</summary>
    public static void mapper543_set_outer(byte value) => throw null!;

    /// <summary>Waixing FS306 (mapper 544): VRC-like PRG0/PRG1 via <see cref="vrc24_set_prg_bank"/>; extra PRG2 at $9C00.</summary>
    public static void mapper544_set_prg2(byte bank) => throw null!;

    /// <summary>Yancheng YC-03-09 (mapper 558): PRG low at $5000 (like Waixing 162).</summary>
    public static void mapper558_set_prg_low(byte value) => throw null!;

    /// <summary>Mapper 558: PRG high at $5100.</summary>
    public static void mapper558_set_prg_high(byte value) => throw null!;

    /// <summary>Mapper 558: mode at $5300.</summary>
    public static void mapper558_set_mode(byte value) => throw null!;

    // ---- Batch 17 ----

    /// <summary>AA6023 / COOLBOY (mapper 268): outer register via absolute <paramref name="register"/> ($5000/$6000 family).</summary>
    public static void mapper268_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 268: MMC3-compatible bank select/data.</summary>
    public static void mapper268_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>JY YY841144C family (mapper 281): outer bank at $D003; inner via <see cref="jy90_set_prg_bank"/>.</summary>
    public static void mapper281_set_outer(byte value) => throw null!;

    /// <summary>JY multicart (mapper 282): outer bank at $D003.</summary>
    public static void mapper282_set_outer(byte value) => throw null!;

    /// <summary>GKCX1 21-in-1 (mapper 288): address-latch bank select via absolute <paramref name="register"/>.</summary>
    public static void mapper288_write(ushort register, byte value) => throw null!;

    /// <summary>BMC-60311C (mapper 289): mode at $6000.</summary>
    public static void mapper289_set_mode(byte value) => throw null!;

    /// <summary>Mapper 289: outer bank at $6001.</summary>
    public static void mapper289_set_outer(byte value) => throw null!;

    /// <summary>Mapper 289: data latch to $8000.</summary>
    public static void mapper289_set(byte value) => throw null!;

    /// <summary>Kaiser KS7037 Metroid FDS (mapper 307): MMC3-like select/data at $8000/$8001.</summary>
    public static void mapper307_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>HP-898F multicart (mapper 319): CHR bank at $6000.</summary>
    public static void mapper319_set_chr(byte value) => throw null!;

    /// <summary>Mapper 319: PRG/mirroring at $6004.</summary>
    public static void mapper319_set_prg(byte value) => throw null!;

    /// <summary>COOLGIRL (mapper 342): supervisor register via absolute <paramref name="register"/> ($5000–$5007).</summary>
    public static void mapper342_write(ushort register, byte value) => throw null!;

    /// <summary>GN-26 multicart (mapper 344): outer/mode at $6000.</summary>
    public static void mapper344_set_outer(byte value) => throw null!;

    /// <summary>Mapper 344: MMC3-compatible bank select/data.</summary>
    public static void mapper344_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BMC-830118C (mapper 348): outer at $6800 — same lean surface as <see cref="mapper315_set_outer"/>.</summary>
    public static void mapper348_set_outer(byte value) => throw null!;

    /// <summary>Mapper 348: MMC3-compatible bank select/data.</summary>
    public static void mapper348_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>92 Super Mario Family (mapper 353): outer via absolute <paramref name="register"/> ($8080–$E080).</summary>
    public static void mapper353_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 353: MMC3-compatible bank select/data.</summary>
    public static void mapper353_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Super Games King (mapper 449): address/data latch via absolute <paramref name="register"/>.</summary>
    public static void mapper449_write(ushort register, byte value) => throw null!;

    /// <summary>Sachen SA-9602B Princess Maker (mapper 513): MMC3-like bank select/data (CHR bits carry outer PRG).</summary>
    public static void mapper513_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>UNL-BJ-56 Sangokushi bootleg (mapper 526): PRG via absolute <paramref name="register"/> ($8008–$800B).</summary>
    public static void mapper526_write(ushort register, byte value) => throw null!;

    // ---- Batch 18 ----

    /// <summary>JY Soccer/HIK multicart (mapper 295): outer bank at $D003 (128 KiB; like 281).</summary>
    public static void mapper295_set_outer(byte value) => throw null!;

    /// <summary>LH51 Ai Senshi Nicol FDS conversion (mapper 309): 8KB PRG at $8000.</summary>
    public static void mapper309_set_prg_bank(byte bank) => throw null!;

    /// <summary>K-1053 New World Multi (mapper 310): address/data latch via absolute <paramref name="register"/> (mapper 15+).</summary>
    public static void mapper310_write(ushort register, byte value) => throw null!;

    /// <summary>Kaiser KS7013B Highway Star (mapper 312): 16KB PRG at $6000.</summary>
    public static void mapper312_set_prg_bank(byte bank) => throw null!;

    /// <summary>BMC-64in1NoRepeat (mapper 314): misc/mode at $5000.</summary>
    public static void mapper314_set_mode(byte value) => throw null!;

    /// <summary>Mapper 314: PRG at $5001.</summary>
    public static void mapper314_set_prg(byte value) => throw null!;

    /// <summary>Mapper 314: UNROM data latch to $8000.</summary>
    public static void mapper314_set(byte value) => throw null!;

    /// <summary>BMC-K-3033 (mapper 322): outer/mode at $6000.</summary>
    public static void mapper322_set_outer(byte value) => throw null!;

    /// <summary>Mapper 322: MMC3-compatible bank select/data.</summary>
    public static void mapper322_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BMC-CTC-12IN1 (mapper 337): outer/inner via absolute <paramref name="register"/> ($8000/$C000).</summary>
    public static void mapper337_write(ushort register, byte value) => throw null!;

    /// <summary>BMC-891227 Super 15-in-1 (mapper 350): outer/inner via absolute <paramref name="register"/>.</summary>
    public static void mapper350_write(ushort register, byte value) => throw null!;

    /// <summary>FC-29-40 / K-3101 (mapper 460): outer at $6000.</summary>
    public static void mapper460_set_outer(byte value) => throw null!;

    /// <summary>Mapper 460: MMC3-compatible bank select/data.</summary>
    public static void mapper460_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Sachen Zhongguo Daheng (mapper 512): CHR/VRAM mode at $4100.</summary>
    public static void mapper512_set_mode(byte value) => throw null!;

    /// <summary>Mapper 512: MMC3-compatible bank select/data.</summary>
    public static void mapper512_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Subor Karaoke (mapper 514): packed mode/PRG latch to $8000.</summary>
    public static void mapper514_set(byte value) => throw null!;

    /// <summary>60-1064-16L Super Soccer Champion (mapper 538): PRG banks via write to $C000.</summary>
    public static void mapper538_set_prg(byte value) => throw null!;

    /// <summary>CTC-15 Almana no Kiseki (mapper 548): latch/IRQ at $4800.</summary>
    public static void mapper548_set_latch(byte value) => throw null!;

    /// <summary>Mapper 548: apply latch (and PRG bank) at $5000.</summary>
    public static void mapper548_apply(byte value) => throw null!;

    /// <summary>JY820845C Chess Series (mapper 550): outer at $7000.</summary>
    public static void mapper550_set_outer(byte value) => throw null!;

    /// <summary>Mapper 550: MHROM latch to $8000.</summary>
    public static void mapper550_set(byte value) => throw null!;

    /// <summary>Jncota mapper 178 variant (mapper 551): mode at $4800.</summary>
    public static void mapper551_set_mode(byte value) => throw null!;

    /// <summary>Mapper 551: low PRG at $4801.</summary>
    public static void mapper551_set_prg_low(byte value) => throw null!;

    /// <summary>Mapper 551: high PRG at $4802.</summary>
    public static void mapper551_set_prg_high(byte value) => throw null!;

    // ---- Batch 19 ----

    /// <summary>JY-119 8-in-1 (mapper 267): outer bank at $6000.</summary>
    public static void mapper267_set_outer(byte value) => throw null!;

    /// <summary>Mapper 267: MMC3-compatible bank select/data.</summary>
    public static void mapper267_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BMC-411120-C / K-3088 (mapper 287): outer/mode at $6000.</summary>
    public static void mapper287_set_outer(byte value) => throw null!;

    /// <summary>Mapper 287: MMC3-compatible bank select/data.</summary>
    public static void mapper287_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Kasheng Mortal Kombat 6 2-in-1 (mapper 291): outer/mode at $6000.</summary>
    public static void mapper291_set_outer(byte value) => throw null!;

    /// <summary>Mapper 291: MMC3-compatible bank select/data.</summary>
    public static void mapper291_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BMC-NTD-03 Asder 20-in-1 (mapper 290): address/data latch via absolute <paramref name="register"/>.</summary>
    public static void mapper290_write(ushort register, byte value) => throw null!;

    /// <summary>63-1601 UNROM multicart (mapper 294): outer at $4100.</summary>
    public static void mapper294_set_outer(byte value) => throw null!;

    /// <summary>BMC-11160 TXC 6-in-1 (mapper 299): packed PRG/CHR latch to $8000.</summary>
    public static void mapper299_set(byte value) => throw null!;

    /// <summary>BMC-190in1 (mapper 300): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper300_write(ushort register, byte value) => throw null!;

    /// <summary>BMC-820310-C (mapper 321): outer/mode at $6000.</summary>
    public static void mapper321_set_outer(byte value) => throw null!;

    /// <summary>Mapper 321: MMC3-compatible bank select/data.</summary>
    public static void mapper321_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BMC-NEWSTAR-GRM070-8IN1 (mapper 333): outer/mode via absolute <paramref name="register"/> ($9000/$B000/$D000/$F000).</summary>
    public static void mapper333_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 333: MMC3-compatible bank select/data.</summary>
    public static void mapper333_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BMC-CTC-09 10-in-1 (mapper 335): PRG/mirroring at $C000.</summary>
    public static void mapper335_set_prg(byte value) => throw null!;

    /// <summary>Mapper 335: CHR at $8000.</summary>
    public static void mapper335_set_chr(byte value) => throw null!;

    /// <summary>BMC-SA005-A (mapper 338): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper338_write(ushort register, byte value) => throw null!;

    /// <summary>BMC-L6IN1 New Star 6-in-1 (mapper 345): outer/mode at $6000.</summary>
    public static void mapper345_set_outer(byte value) => throw null!;

    /// <summary>Mapper 345: MMC3-compatible bank select/data.</summary>
    public static void mapper345_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>JY830832C / 9053 (mapper 364): outer at $6000.</summary>
    public static void mapper364_set_outer(byte value) => throw null!;

    /// <summary>Mapper 364: MMC3-compatible bank select/data.</summary>
    public static void mapper364_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>NROM 35-in-1 (mapper 379): packed PRG/CHR latch to $8000.</summary>
    public static void mapper379_set(byte value) => throw null!;

    /// <summary>S-009 UNROM multicart (mapper 434): outer at $6000.</summary>
    public static void mapper434_set_outer(byte value) => throw null!;

    /// <summary>Mapper 434: UNROM inner latch to $8000 (bus conflicts).</summary>
    public static void mapper434_set(byte value) => throw null!;

    /// <summary>NTDEC TH2348 (mapper 437): outer via absolute <paramref name="register"/> ($5000–$5FFF).</summary>
    public static void mapper437_write(ushort register, byte value) => throw null!;

    // ---- Batch 20 ----

    /// <summary>BMC-810544-C-A1 / NTDEC 2746 (mapper 261): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper261_write(ushort register, byte value) => throw null!;

    /// <summary>BMC-T-262 (mapper 265): address/data latch via absolute <paramref name="register"/>.</summary>
    public static void mapper265_write(ushort register, byte value) => throw null!;

    /// <summary>BMC-8157 GG1 (mapper 301): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper301_write(ushort register, byte value) => throw null!;

    /// <summary>FARID_UNROM_8-IN-1 (mapper 324): packed outer/inner latch to $8000 (bus conflicts).</summary>
    public static void mapper324_set(byte value) => throw null!;

    /// <summary>UNL-EDU2000 (mapper 329): BNROM-like latch to $8000.</summary>
    public static void mapper329_set(byte value) => throw null!;

    /// <summary>BMC-WS Super 40/500-in-1 (mapper 332): PRG/mirroring at $6000.</summary>
    public static void mapper332_set_prg(byte value) => throw null!;

    /// <summary>Mapper 332: CHR/solder at $6001.</summary>
    public static void mapper332_set_chr(byte value) => throw null!;

    /// <summary>Mapper 332: CNROM latch to $8000.</summary>
    public static void mapper332_set(byte value) => throw null!;

    /// <summary>BMC-K-3046 (mapper 336): outer/inner latch to $C000.</summary>
    public static void mapper336_set(byte value) => throw null!;

    /// <summary>BMC-K-3036 (mapper 340): address/data latch via absolute <paramref name="register"/>.</summary>
    public static void mapper340_write(ushort register, byte value) => throw null!;

    /// <summary>BMC-TJ-03 (mapper 341): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper341_write(ushort register, byte value) => throw null!;

    /// <summary>BMC-G-146 Super HIK 14-in-1 (mapper 349): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper349_write(ushort register, byte value) => throw null!;

    /// <summary>831031C/T-308 (mapper 430): outer at $6000.</summary>
    public static void mapper430_set_outer(byte value) => throw null!;

    /// <summary>Mapper 430: MMC3-compatible bank select/data.</summary>
    public static void mapper430_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>841026C/850335C (mapper 441): outer/mode at $6000.</summary>
    public static void mapper441_set_outer(byte value) => throw null!;

    /// <summary>Mapper 441: MMC3-compatible bank select/data.</summary>
    public static void mapper441_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>NC3000M (mapper 443): outer/mode at $6000.</summary>
    public static void mapper443_set_outer(byte value) => throw null!;

    /// <summary>Mapper 443: MMC3-compatible bank select/data.</summary>
    public static void mapper443_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Jingtai YY841157C (mapper 450): outer at $6000.</summary>
    public static void mapper450_set_outer(byte value) => throw null!;

    /// <summary>Realtec 8042 (mapper 453): AOROM/UNROM latch to $8000.</summary>
    public static void mapper453_set(byte value) => throw null!;

    /// <summary>NTDEC N625836 (mapper 455): outer/mode at $4100.</summary>
    public static void mapper455_set_outer(byte value) => throw null!;

    /// <summary>Mapper 455: MMC3-compatible bank select/data.</summary>
    public static void mapper455_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Realtec K6C3001A (mapper 456): outer at $4100.</summary>
    public static void mapper456_set_outer(byte value) => throw null!;

    /// <summary>Mapper 456: MMC3-compatible bank select/data.</summary>
    public static void mapper456_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>810431C (mapper 457): outer at $6000.</summary>
    public static void mapper457_set_outer(byte value) => throw null!;

    /// <summary>Mapper 457: MMC3-compatible bank select/data.</summary>
    public static void mapper457_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>K-3102 / GN-23 (mapper 458): outer/PRG at $6000.</summary>
    public static void mapper458_set_outer(byte value) => throw null!;

    /// <summary>Mapper 458: MMC3-compatible bank select/data (CHR/mirroring).</summary>
    public static void mapper458_set_bank(byte reg, byte bank) => throw null!;

    // ---- Batch 21 ----

    /// <summary>BMC-GS-2004/2013 (mapper 283): 32KB PRG latch to $8000.</summary>
    public static void mapper283_set(byte value) => throw null!;

    /// <summary>BMC-830425C-4391T (mapper 320): outer via absolute <paramref name="register"/> ($F0E0–$F0FF).</summary>
    public static void mapper320_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 320: UNROM inner latch to $8000.</summary>
    public static void mapper320_set(byte value) => throw null!;

    /// <summary>FARID_SLROM_8-IN-1 (mapper 323): outer at $6000 (MMC1 via mmc1 helpers).</summary>
    public static void mapper323_set_outer(byte value) => throw null!;

    /// <summary>UNL-KS7012 Zanac FDS (mapper 346): bank via absolute <paramref name="register"/> ($E0A0/$EE36).</summary>
    public static void mapper346_write(ushort register, byte value) => throw null!;

    /// <summary>YY841101C (mapper 361): outer at $7000.</summary>
    public static void mapper361_set_outer(byte value) => throw null!;

    /// <summary>Mapper 361: MMC3-compatible bank select/data.</summary>
    public static void mapper361_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>F600 Golden Mario Party (mapper 370): outer at $5000.</summary>
    public static void mapper370_set_outer(byte value) => throw null!;

    /// <summary>Mapper 370: MMC3-compatible bank select/data.</summary>
    public static void mapper370_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>YY841155C (mapper 376): outer #0 at $7000.</summary>
    public static void mapper376_set_outer(byte value) => throw null!;

    /// <summary>Mapper 376: outer #1 (A18) at $7001.</summary>
    public static void mapper376_set_outer_hi(byte value) => throw null!;

    /// <summary>Mapper 376: MMC3-compatible bank select/data.</summary>
    public static void mapper376_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>KN-42 (mapper 381): UOROM latch to $8000 (bus conflicts).</summary>
    public static void mapper381_set(byte value) => throw null!;

    /// <summary>830928C (mapper 382): outer/mode via absolute <paramref name="register"/>.</summary>
    public static void mapper382_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 382: inner latch to $8000 (bus conflicts).</summary>
    public static void mapper382_set(byte value) => throw null!;

    /// <summary>NTDEC 2779 (mapper 385): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper385_write(ushort register, byte value) => throw null!;

    /// <summary>820720C (mapper 393): outer/mode at $6000.</summary>
    public static void mapper393_set_outer(byte value) => throw null!;

    /// <summary>Mapper 393: MMC3-compatible bank select/data.</summary>
    public static void mapper393_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Realtec 8210 (mapper 395): outer #1 at $6000.</summary>
    public static void mapper395_set_outer(byte value) => throw null!;

    /// <summary>Mapper 395: outer #2 at $6010.</summary>
    public static void mapper395_set_outer2(byte value) => throw null!;

    /// <summary>Mapper 395: MMC3-compatible bank select/data.</summary>
    public static void mapper395_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>YY850437C / GN-51 (mapper 396): outer at $A000.</summary>
    public static void mapper396_set_outer(byte value) => throw null!;

    /// <summary>Mapper 396: UNROM inner latch to $8000 (no bus conflicts).</summary>
    public static void mapper396_set(byte value) => throw null!;

    /// <summary>89433 (mapper 403): PRG at $4100.</summary>
    public static void mapper403_set_prg(byte value) => throw null!;

    /// <summary>Mapper 403: CHR at $4101.</summary>
    public static void mapper403_set_chr(byte value) => throw null!;

    /// <summary>Mapper 403: mode at $4102.</summary>
    public static void mapper403_set_mode(byte value) => throw null!;

    /// <summary>9999999-in-1 (mapper 414): address/data latch via absolute <paramref name="register"/>.</summary>
    public static void mapper414_write(ushort register, byte value) => throw null!;

    /// <summary>830768C (mapper 448): outer/mode at $6000.</summary>
    public static void mapper448_set_outer(byte value) => throw null!;

    /// <summary>Family Noraebang (mapper 515): UNROM-style latch to $8000 (no bus conflicts).</summary>
    public static void mapper515_set(byte value) => throw null!;

    /// <summary>Cocoma Pack (mapper 516): outer via absolute <paramref name="register"/> ($8010).</summary>
    public static void mapper516_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 516: MMC3-compatible bank select/data.</summary>
    public static void mapper516_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>LittleCom 160-in-1 (mapper 541): address latch via absolute <paramref name="register"/> ($C000).</summary>
    public static void mapper541_write(ushort register, byte value) => throw null!;

    // ---- Batch 22 ----

    /// <summary>UNL-SHERO Street Heroes (mapper 262): CHR mode at $4100.</summary>
    public static void mapper262_set_mode(byte value) => throw null!;

    /// <summary>Mapper 262: MMC3-compatible bank select/data.</summary>
    public static void mapper262_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BMC-22026 TXC 4-in-1 (mapper 271): GNROM latch to $8000.</summary>
    public static void mapper271_set(byte value) => throw null!;

    /// <summary>NewStar 12/76-in-1 (mapper 293): banking via absolute <paramref name="register"/>.</summary>
    public static void mapper293_write(ushort register, byte value) => throw null!;

    /// <summary>TXC 01-22110-000 (mapper 297): mode at $4120.</summary>
    public static void mapper297_set_mode(byte value) => throw null!;

    /// <summary>Mapper 297: mapper-70 latch to $8000.</summary>
    public static void mapper297_set(byte value) => throw null!;

    /// <summary>BMC-10-24-C-A1 (mapper 327): outer at $6000.</summary>
    public static void mapper327_set_outer(byte value) => throw null!;

    /// <summary>Mapper 327: MMC3-compatible bank select/data.</summary>
    public static void mapper327_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>5/20-in-1 1993 (mapper 334): 32KB PRG at $6000.</summary>
    public static void mapper334_set_prg(byte value) => throw null!;

    /// <summary>Mapper 334: MMC3-compatible bank select/data.</summary>
    public static void mapper334_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BMC-K-3006 (mapper 339): outer/mode at $6000.</summary>
    public static void mapper339_set_outer(byte value) => throw null!;

    /// <summary>Mapper 339: MMC3-compatible bank select/data.</summary>
    public static void mapper339_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Mapper 205 address-latch variant (mapper 367): outer via absolute <paramref name="register"/> ($6000).</summary>
    public static void mapper367_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 367: MMC3-compatible bank select/data.</summary>
    public static void mapper367_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>BMC-N49C-300 SMB Party (mapper 369): outer at $4100.</summary>
    public static void mapper369_set_outer(byte value) => throw null!;

    /// <summary>Mapper 369: MMC3-compatible bank select/data.</summary>
    public static void mapper369_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>AxROM/UNROM 8-in-1 (mapper 378): outer via absolute <paramref name="register"/>.</summary>
    public static void mapper378_write(ushort register, byte value) => throw null!;

    /// <summary>YY860729C (mapper 386): outer at $D003.</summary>
    public static void mapper386_set_outer(byte value) => throw null!;

    /// <summary>YY841145C (mapper 388): outer at $D003.</summary>
    public static void mapper388_set_outer(byte value) => throw null!;

    /// <summary>J-2282 Olympic (mapper 402): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper402_write(ushort register, byte value) => throw null!;

    /// <summary>retroUSB DPCMcart (mapper 409): address latch via absolute <paramref name="register"/> ($C000).</summary>
    public static void mapper409_write(ushort register, byte value) => throw null!;

    /// <summary>F-1002 (mapper 435): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper435_write(ushort register, byte value) => throw null!;

    /// <summary>YS2309 / DS-07 (mapper 439): UxROM latch to $8000.</summary>
    public static void mapper439_set(byte value) => throw null!;

    /// <summary>Mapper 439: outer at $6000.</summary>
    public static void mapper439_set_outer(byte value) => throw null!;

    /// <summary>Mapper 439: mask at $6001.</summary>
    public static void mapper439_set_mask(byte value) => throw null!;

    /// <summary>DG574B (mapper 445): outer PRG at $5000.</summary>
    public static void mapper445_set_prg(byte value) => throw null!;

    /// <summary>Mapper 445: outer CHR at $5001.</summary>
    public static void mapper445_set_chr(byte value) => throw null!;

    /// <summary>Mapper 445: mode at $5002.</summary>
    public static void mapper445_set_mode(byte value) => throw null!;

    /// <summary>Mapper 445: MMC3-compatible bank select/data.</summary>
    public static void mapper445_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>KL-06 (mapper 447): outer at $6000.</summary>
    public static void mapper447_set_outer(byte value) => throw null!;

    /// <summary>UNL-EH8813A (mapper 519): address/data latch via absolute <paramref name="register"/>.</summary>
    public static void mapper519_write(ushort register, byte value) => throw null!;

    /// <summary>UNL-KS7021A (mapper 525): 16KB PRG at $8000.</summary>
    public static void mapper525_set_prg(byte value) => throw null!;

    // ---- Batch 23 ----

    /// <summary>UNL-KS7016 Exciting Basket (mapper 306): PRG via absolute <paramref name="register"/> ($D903).</summary>
    public static void mapper306_write(ushort register, byte value) => throw null!;

    /// <summary>JY-208 Rockman 7-in-1 (mapper 356): outer via $6000 (mapper-45 sequential lean: one STA).</summary>
    public static void mapper356_set_outer(byte value) => throw null!;

    /// <summary>Mapper 356: MMC3-compatible bank select/data.</summary>
    public static void mapper356_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>YY860606C (mapper 358): outer at $D003.</summary>
    public static void mapper358_set_outer(byte value) => throw null!;

    /// <summary>GN-45 (mapper 366): outer at $6800.</summary>
    public static void mapper366_set_outer(byte value) => throw null!;

    /// <summary>Mapper 366: MMC3-compatible bank select/data.</summary>
    public static void mapper366_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>YY850735C (mapper 387): outer at $D003.</summary>
    public static void mapper387_set_outer(byte value) => throw null!;

    /// <summary>Caltron 9-in-1 (mapper 389): banking via absolute <paramref name="register"/>.</summary>
    public static void mapper389_write(ushort register, byte value) => throw null!;

    /// <summary>00202650 8-in-1 (mapper 392): outer at $6000.</summary>
    public static void mapper392_set_outer(byte value) => throw null!;

    /// <summary>Mapper 392: MMC3-compatible bank select/data.</summary>
    public static void mapper392_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Realtec HSK007 (mapper 394): mode at $5009.</summary>
    public static void mapper394_set_mode(byte value) => throw null!;

    /// <summary>Mapper 394: outer at $500B.</summary>
    public static void mapper394_set_outer(byte value) => throw null!;

    /// <summary>Mapper 394: MMC3-compatible bank select/data.</summary>
    public static void mapper394_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>JY Soccer 6-in-1 variant (mapper 397): outer at $D003.</summary>
    public static void mapper397_set_outer(byte value) => throw null!;

    /// <summary>JY012005 (mapper 404): outer at $6000 (MMC1 via mmc1 helpers).</summary>
    public static void mapper404_set_outer(byte value) => throw null!;

    /// <summary>JY-302 mapper-45 CHR-RAM variant (mapper 410): outer via $6000.</summary>
    public static void mapper410_set_outer(byte value) => throw null!;

    /// <summary>Mapper 410: MMC3-compatible bank select/data.</summary>
    public static void mapper410_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Henggedianzi MMC3+NROM (mapper 412): CHR at $6000.</summary>
    public static void mapper412_set_chr(byte value) => throw null!;

    /// <summary>Mapper 412: mode at $6001.</summary>
    public static void mapper412_set_mode(byte value) => throw null!;

    /// <summary>Mapper 412: PRG at $6002.</summary>
    public static void mapper412_set_prg(byte value) => throw null!;

    /// <summary>Mapper 412: MMC3-compatible bank select/data.</summary>
    public static void mapper412_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>4-in-1 with SMB2J (mapper 416): packed latch to $8000.</summary>
    public static void mapper416_set(byte value) => throw null!;

    /// <summary>Realtec 8090 (mapper 432): solder/pad at $6000.</summary>
    public static void mapper432_set_solder(byte value) => throw null!;

    /// <summary>Mapper 432: outer at $6001.</summary>
    public static void mapper432_set_outer(byte value) => throw null!;

    /// <summary>Mapper 432: MMC3-compatible bank select/data.</summary>
    public static void mapper432_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>CM-9309 Wuzi Gun (mapper 461): outer at $6000.</summary>
    public static void mapper461_set_outer(byte value) => throw null!;

    /// <summary>47-2 72-in-1 (mapper 467): outer at $9000.</summary>
    public static void mapper467_set_outer(byte value) => throw null!;

    /// <summary>Mapper 467: MMC3-compatible bank select/data.</summary>
    public static void mapper467_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>INX_007T_V01 (mapper 470): outer at $5000.</summary>
    public static void mapper470_set_outer(byte value) => throw null!;

    /// <summary>Mapper 470: AOROM latch to $8000.</summary>
    public static void mapper470_set(byte value) => throw null!;

    /// <summary>Impact Soft IM1 (mapper 471): address latch via absolute <paramref name="register"/>.</summary>
    public static void mapper471_write(ushort register, byte value) => throw null!;

    /// <summary>Kkachi karaoke (mapper 517): UNROM latch to $8000 (no bus conflicts).</summary>
    public static void mapper517_set(byte value) => throw null!;

    /// <summary>Korean Igo / DREAMTECH01 (mapper 521): PRG at $5020.</summary>
    public static void mapper521_set_prg(byte value) => throw null!;

    // ---- Batch 24 ----

    /// <summary>UNL-KOF97 (mapper 263): MMC3 bank via absolute <paramref name="register"/> ($8000/$9000…; caller unscrambles data bits).</summary>
    public static void mapper263_write(ushort register, byte value) => throw null!;

    /// <summary>SFC-12 Rockman I-VI (mapper 372): outer via $6000 (mapper-45 lean).</summary>
    public static void mapper372_set_outer(byte value) => throw null!;

    /// <summary>Mapper 372: MMC3-compatible bank select/data.</summary>
    public static void mapper372_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>SFC-13 Super 4-in-1 (mapper 373): outer via $6000 (mapper-45 lean).</summary>
    public static void mapper373_set_outer(byte value) => throw null!;

    /// <summary>Mapper 373: MMC3-compatible bank select/data.</summary>
    public static void mapper373_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>JY-111 Super Game 8-in-1 (mapper 377): outer at $6000 (like 267).</summary>
    public static void mapper377_set_outer(byte value) => throw null!;

    /// <summary>Mapper 377: MMC3-compatible bank select/data.</summary>
    public static void mapper377_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>L1A16 Crisis Force multicart (mapper 384): outer at $6800.</summary>
    public static void mapper384_set_outer(byte value) => throw null!;

    /// <summary>KC885 VIP19 (mapper 401): outer via $6000 (mapper-45 lean).</summary>
    public static void mapper401_set_outer(byte value) => throw null!;

    /// <summary>Mapper 401: MMC3-compatible bank select/data.</summary>
    public static void mapper401_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Lucky Rabbit FDS (mapper 415): PRG for $6000 via latch to $8000.</summary>
    public static void mapper415_set_prg(byte value) => throw null!;

    /// <summary>K-3071 (mapper 438): address/data latch via absolute <paramref name="register"/>.</summary>
    public static void mapper438_write(ushort register, byte value) => throw null!;

    /// <summary>UNL-LH10 Fūun Shōrinken (mapper 522): MMC3-like bank select/data ($8000/$8001).</summary>
    public static void mapper522_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Jncota Fēngshénbǎng (mapper 523): MMC3-compatible bank select/data.</summary>
    public static void mapper523_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Sachen 3014 (mapper 533): CHR/protection latch to $8000.</summary>
    public static void mapper533_set_chr(byte value) => throw null!;

    /// <summary>UNL-LH53 Murasamejō (mapper 535): 8KB PRG at $F000 (banks $6000).</summary>
    public static void mapper535_set_prg(byte value) => throw null!;

    /// <summary>Kid Icarus FDS bootleg (mapper 539): 8KB PRG at $A000.</summary>
    public static void mapper539_set_prg(byte value) => throw null!;

    /// <summary>UNL-82112C (mapper 540): CHR via absolute <paramref name="register"/> ($A000/$A001/$B002/$B003); PRG like 359.</summary>
    public static void mapper540_write(ushort register, byte value) => throw null!;

    // ---- Batch 25 ----

    /// <summary>UNL-TF1201 Lethal Weapon (mapper 298): VRC4b lean PRG — same as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper298_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>UNL-KS7017 Almana no Kiseki (mapper 303): PRG latch via absolute <paramref name="register"/> ($4A00).</summary>
    public static void mapper303_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 303: commit latch at $5100.</summary>
    public static void mapper303_apply(byte value) => throw null!;

    /// <summary>UNL-TH2131-1 Batman bootleg (mapper 308): VRC2b lean PRG — same as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper308_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>BMC-RESET-TXROM (mapper 313): MMC3-compatible bank select/data (outer via reset).</summary>
    public static void mapper313_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>UNL-MALISB (mapper 325): MMC3-compatible bank select/data (address/data scramble left to caller).</summary>
    public static void mapper325_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>830506C JY-005 (mapper 362): VRC4f CHR/outer via absolute <paramref name="register"/> ($B001…).</summary>
    public static void mapper362_write(ushort register, byte value) => throw null!;

    /// <summary>Haradius Zero (mapper 406): MMC3 bank via $8000/$8002 (submapper 0 wiring).</summary>
    public static void mapper406_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>A88S-1 (mapper 411): outer #1 at $5FF0.</summary>
    public static void mapper411_set_outer(byte value) => throw null!;

    /// <summary>Mapper 411: outer #2 at $5FF1.</summary>
    public static void mapper411_set_outer2(byte value) => throw null!;

    /// <summary>Mapper 411: MMC3-compatible bank select/data.</summary>
    public static void mapper411_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Lexibook Compact Cyber Arcade (mapper 423): VT369 outer at $4150.</summary>
    public static void mapper423_set_outer(byte value) => throw null!;

    /// <summary>Lexibook Retro TV (mapper 424): VT369 outer at $411E.</summary>
    public static void mapper424_set_outer(byte value) => throw null!;

    /// <summary>Cube Tech VT369 (mapper 425): outer at $41E6.</summary>
    public static void mapper425_set_outer(byte value) => throw null!;

    /// <summary>ZLX-08 OneBus (mapper 436): outer/PRG A24 at $4100.</summary>
    public static void mapper436_set_outer(byte value) => throw null!;

    /// <summary>Mapper 436: I/O port at $410F (PRG A23).</summary>
    public static void mapper436_set_io(byte value) => throw null!;

    /// <summary>BlazePro MAX II (mapper 468): mapper-select/mode at $5700.</summary>
    public static void mapper468_set_mode(byte value) => throw null!;

    /// <summary>Datach 2-in-1 (mapper 520): VRC4e CHR/outer via absolute <paramref name="register"/> ($B000…).</summary>
    public static void mapper520_write(ushort register, byte value) => throw null!;

    /// <summary>UNL-AX-40G (mapper 527): VRC2b lean PRG — same as <see cref="vrc24_set_prg_bank"/>.</summary>
    public static void mapper527_set_prg_bank(byte which, byte bank) => throw null!;

    /// <summary>UNL-T-230 (mapper 529): 16KB PRG at $A000 (VRC4e $A00x).</summary>
    public static void mapper529_set_prg(byte value) => throw null!;

    /// <summary>UNL-AX5705 (mapper 530): VRC4f lean PRG — same as <see cref="vrc24_set_prg_bank"/> (scramble left to caller).</summary>
    public static void mapper530_set_prg_bank(byte which, byte bank) => throw null!;

    // ---- Batch 26 ----

    /// <summary>OneBus / VT02+ (mapper 256): native PRG bank at $4107.</summary>
    public static void mapper256_set_prg(byte value) => throw null!;

    /// <summary>Mapper 256: native CHR/high bank at $4108.</summary>
    public static void mapper256_set_chr(byte value) => throw null!;

    /// <summary>Mapper 256: opcode/register scramble control at $411C.</summary>
    public static void mapper256_set_scramble(byte value) => throw null!;

    /// <summary>Mapper 256: alternate scramble control at $4169.</summary>
    public static void mapper256_set_scramble2(byte value) => throw null!;

    /// <summary>Mapper 256: MMC3-compatible bank select/data.</summary>
    public static void mapper256_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Konami Collector's Series PnP (mapper 408): same lean OneBus surface as <see cref="mapper256_set_prg"/>.</summary>
    public static void mapper408_set_prg(byte value) => throw null!;

    /// <summary>Mapper 408: CHR/high at $4108.</summary>
    public static void mapper408_set_chr(byte value) => throw null!;

    /// <summary>Mapper 408: scramble at $411C.</summary>
    public static void mapper408_set_scramble(byte value) => throw null!;

    /// <summary>VT369 + SD (mapper 419): same scramble/bank surface as 256, plus I/O at $410F.</summary>
    public static void mapper419_set_prg(byte value) => throw null!;

    /// <summary>Mapper 419: CHR/high at $4108.</summary>
    public static void mapper419_set_chr(byte value) => throw null!;

    /// <summary>Mapper 419: scramble at $411C.</summary>
    public static void mapper419_set_scramble(byte value) => throw null!;

    /// <summary>Mapper 419: I/O/nibble port at $410F.</summary>
    public static void mapper419_set_io(byte value) => throw null!;

    /// <summary>TEC9719 / ING003C alias of 534 (mapper 422): outer at $6000.</summary>
    public static void mapper422_set_outer(byte value) => throw null!;

    /// <summary>Mapper 422: MMC3-compatible bank select/data.</summary>
    public static void mapper422_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>VT369 serial-ROM GPIO (mapper 426): port at $4150.</summary>
    public static void mapper426_set_gpio(byte value) => throw null!;

    /// <summary>VT369 I²C GPIO (mapper 427): port at $4148 (sub0) / use write for $4150.</summary>
    public static void mapper427_set_gpio(byte value) => throw null!;

    /// <summary>Mapper 427: alternate GPIO via absolute <paramref name="register"/>.</summary>
    public static void mapper427_write(ushort register, byte value) => throw null!;

    /// <summary>NC-20MB (mapper 433): packed latch to $8000 — same lean surface as <see cref="mapper233_set"/>.</summary>
    public static void mapper433_set(byte value) => throw null!;

    /// <summary>BlazePro FDS CPLD (mapper 469): mode at $5700 — same board as <see cref="mapper468_set_mode"/>.</summary>
    public static void mapper469_set_mode(byte value) => throw null!;

    /// <summary>FK-206 JG (mapper 472): outer at $6000 (mapper-52 bits reshuffled; packing left to caller).</summary>
    public static void mapper472_set_outer(byte value) => throw null!;

    /// <summary>Mapper 472: MMC3-compatible bank select/data.</summary>
    public static void mapper472_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Waixing FS303 alias (mapper 536): MMC3 bank — same as <see cref="mapper195_set_bank"/>.</summary>
    public static void mapper536_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Waixing FS303 alias (mapper 537): MMC3 bank — same as <see cref="mapper195_set_bank"/>.</summary>
    public static void mapper537_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Konami QTa / VRC5 (mapper 547): PRG bank via absolute <paramref name="register"/> ($D200/$D300/$D400).</summary>
    public static void mapper547_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 547: 8KB PRG0 at $D200.</summary>
    public static void mapper547_set_prg0(byte bank) => throw null!;

    /// <summary>Taito X1-017 alias (mapper 552): PRG at $7EF0 — same as <see cref="taito82_set_prg_bank"/>.</summary>
    public static void mapper552_set_prg_bank(byte bank) => throw null!;

    /// <summary>Super Game Doctor / Venus (mapper 561): 1M mode via absolute <paramref name="register"/> ($42FC–$42FF).</summary>
    public static void mapper561_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 561: IRQ counter low at $4100.</summary>
    public static void mapper561_set_irq_lo(byte value) => throw null!;

    /// <summary>Mapper 561: IRQ counter high / enable at $4101.</summary>
    public static void mapper561_set_irq_hi(byte value) => throw null!;

    /// <summary>Mapper 561: PRG latch to $8000 (when write-protect/latch enabled).</summary>
    public static void mapper561_set(byte value) => throw null!;

    // ---- Batch 27 ----

    /// <summary>YY860606C alias (mapper 363): outer at $D003 — same as <see cref="mapper358_set_outer"/>.</summary>
    public static void mapper363_set_outer(byte value) => throw null!;

    /// <summary>Mapper 390: packed latch to $8000 — same as <see cref="mapper236_set"/>.</summary>
    public static void mapper390_set(byte value) => throw null!;

    /// <summary>UM6578 OneBus (mapper 405): native PRG bank at $4107.</summary>
    public static void mapper405_set_prg(byte value) => throw null!;

    /// <summary>Mapper 405: native CHR/high bank at $4108.</summary>
    public static void mapper405_set_chr(byte value) => throw null!;

    /// <summary>Mapper 405: MMC3-compatible bank select/data.</summary>
    public static void mapper405_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>Win Lose or Draw PnP (mapper 407): OneBus PRG at $4107 (submapper 15 scramble ≡256).</summary>
    public static void mapper407_set_prg(byte value) => throw null!;

    /// <summary>Mapper 407: CHR/high at $4108.</summary>
    public static void mapper407_set_chr(byte value) => throw null!;

    /// <summary>Mapper 407: scramble disable at $4169 (submapper 15).</summary>
    public static void mapper407_set_scramble2(byte value) => throw null!;

    /// <summary>Mapper 407: MMC3-compatible bank select/data.</summary>
    public static void mapper407_set_bank(byte reg, byte bank) => throw null!;

    /// <summary>GN-51 alias (mapper 431): outer at $A000 — same as <see cref="mapper396_set_outer"/>.</summary>
    public static void mapper431_set_outer(byte value) => throw null!;

    /// <summary>Mapper 431: UNROM inner latch to $8000 — same as <see cref="mapper396_set"/>.</summary>
    public static void mapper431_set(byte value) => throw null!;

    /// <summary>Venus Turbo Game Doctor (mapper 562): 1M/2M mode via absolute <paramref name="register"/> ($42FC–$42FF / $43FE–$43FF).</summary>
    public static void mapper562_write(ushort register, byte value) => throw null!;

    /// <summary>Mapper 562: Turbo Game Doctor mode at $4411.</summary>
    public static void mapper562_set_tgd(byte value) => throw null!;

    /// <summary>Mapper 562: IRQ counter target LSB at $440D.</summary>
    public static void mapper562_set_irq_lo(byte value) => throw null!;

    /// <summary>Mapper 562: IRQ counter target MSB / disable at $440C.</summary>
    public static void mapper562_set_irq_hi(byte value) => throw null!;

    /// <summary>Mapper 562: PRG latch to $8000 (when write-protect/latch enabled).</summary>
    public static void mapper562_set(byte value) => throw null!;
}
