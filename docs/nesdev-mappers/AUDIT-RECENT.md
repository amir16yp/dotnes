# Mapper audit (recent additions vs NESDev mirror)
Spec source: `docs/nesdev-mappers/` (nes.science dump, ~2024-11-23).
Audited **52** recently added / packing-fixed mappers.

## Mapper 11
- Spec: `INES_Mapper_011.txt`
- Helpers: `colordreams_set`, `colordreams_set_banks`, `cprom_set_chr_bank`, `jy211_set_prg_bank`, `mapper111_set`
- Notes:
  - ExtractPrgBank 11 OK

## Mapper 29
- Spec: `INES_Mapper_029.txt`
- Helpers: `mapper151_set_prg_bank`, `mapper229_set`, `retcufrom29_set`, `retcufrom29_set_banks`
- Notes:
  - retcufrom packing OK
- Spec register hints:
  ```
  The board includes 4x banks worth of full 8KB CHR ram, and 8x 16KB PRG banks.
  Registers:
  Range,Mask:   $8000-FFFF, $8000
  $8000:  [...P PPCC]
  C = CHR RAM bank
  P = PRG ROM bank
  PRG Setup:
  $8000   $A000   $C000   $E000
  |     $8000     |     { -1}     |
  CHR Setup:
  ```

## Mapper 48
- Spec: `INES_Mapper_048.txt`
- Helpers: `mapper248_set_bank`, `rambo64_set_bank`, `sachen148_set`, `taito48_set_prg_bank`
- Spec register hints:
  ```
  Registers:
  Range,Mask:   $8000-FFFF, $E003
  $8000:    PRG Reg 0 (8k @ $8000)
  $8001:    PRG Reg 1 (8k @ $A000)
  $8002:    CHR Reg 0 (2k @ $0000)
  $8003:    CHR Reg 1 (2k @ $0800)
  $A000:    CHR Reg 2 (1k @ $1000)
  $A001:    CHR Reg 3 (1k @ $1400)
  $A002:    CHR Reg 4 (1k @ $1800)
  $A003:    CHR Reg 5 (1k @ $1C00)
  ```

## Mapper 64
- Spec: `INES_Mapper_064.txt`
- Helpers: `mapper164_set_prg_bank`, `rambo64_set_bank`, `un1rom_set_prg_bank`

## Mapper 78
- Spec: `INES_Mapper_078.txt`
- Helpers: `holydiver_set`, `holydiver_set_banks`, `mapper178_set_prg_bank`
- Notes:
  - ExtractPrgBank 78 OK
  - holydiver_set_banks packing OK
- Spec register hints:
  ```
  3 Registers
  3.1 Bank Select ($8000-$FFFF)
  PRG ROM size: 128 KiB
  PRG ROM bank size: 16 KiB
  PRG RAM: No
  CHR capacity: 128KiB ROM
  CHR bank size: 8 KiB
  CPU $8000-$BFFF: 16KiB switchable PRG ROM bank
  CPU $C000-$FFFF: Fixed to last 16KiB of PRG ROM
  PPU $0000-$1FFF: 8KiB switchable CHR ROM bank
  ```

## Mapper 94
- Spec: `INES_Mapper_094.txt`
- Helpers: `mapper105_write`, `mapper194_set_bank`, `un1rom_set_prg_bank`
- Notes:
  - ExtractPrgBank 94 OK
- Spec register hints:
  ```
  3 Registers
  3.1 Bank select ($8000-$FFFF)
  PRG ROM size: 128 KB
  PRG ROM bank size: 16 KB
  PRG RAM: None
  CHR capacity: 8 KB RAM
  CHR bank size: Not bankswitched
  CPU $8000-$BFFF: 16 KB switchable PRG ROM bank
  CPU $C000-$FFFF: 16 KB PRG ROM bank, fixed to the last bank
  Registers
  ```

## Mapper 103
- Spec: `INES_Mapper_103.txt`
- Helpers: `mapper103_set_prg_bank`, `mapper106_write`, `mapper134_set`
- Notes:
  - 103 banks $6000 via $8000 write; lean STA $8000 OK, CPU map not modeled
- Spec register hints:
  ```
  2 Registers
  2.1 PRG bank ($8000-$8FFF)
  2.2 Mirroring control ($E000-$EFFF)
  2.3 RAM disable ($F000-$FFFF)
  CPU $6000-$7FFF: 8 KiB RAM, or 8 KiB controllable ROM bank.
  CPU $8000-$B7FF: 14 KiB ROM, starting at ROM offset 0x18000
  CPU $B800-$D7FF: 8 KiB RAM, or 8 KiB fixed ROM bank, starting at ROM offset 0x1B800
  CPU $D800-$FFFF: 10 KiB ROM, starting at ROM offset 0x1D800
  Registers
  Registers are not cleared on reset, and state is unknown on power-up. No bus conflicts.
  ```

## Mapper 105
- Spec: `INES_Mapper_105.txt`
- Helpers: `mapper105_write`, `tqrom119_set_bank`, `txsrom118_set_bank`

## Mapper 106
- Spec: `INES_Mapper_106.txt`
- Helpers: `mapper106_write`, `mapper134_set`, `mapper135_set`
- Notes:
  - 106 uses absolute write for $8008-$800B (lean OK)
- Spec register hints:
  ```
  2 Registers
  2.1 CHR banking ($8000-$8007)
  2.2 PRG banking ($8008-$800b)
  2.3 Mirroring ($800c)
  2.4 Counter and interrupts ($800d-$800f)
  PRG-ROM: 2x128 KiB
  CHR-ROM: 128 KiB
  PRG-RAM: 8 KiB
  CPU $6000-$7FFF: 8 KB PRG RAM bank
  CPU $8000-$9FFF: 8 KB switchable PRG ROM bank0
  ```

## Mapper 113
- Spec: `INES_Mapper_113.txt`
- Helpers: `action53_set`, `nina113_set`, `nina113_set_banks`
- Notes:
  - nina113 CHR bit6 packing OK
- Spec register hints:
  ```
  PRG ROM capacity
  PRG ROM window
  PRG RAM capacity
  CHR capacity
  CHR window
  2 Registers
  2.1 Control Register
  CPU $8000-$FFFF: 32 KB switchable PRG ROM bank (max 256 KB)
  PPU $0000-$1FFF: 8 KB switchable CHR ROM bank (max 128 KB)
  Registers
  ```

## Mapper 118
- Spec: `INES_Mapper_118.txt`
- Helpers: `tqrom119_set_bank`, `txsrom118_set_bank`
- Spec register hints:
  ```
  1 Registers
  1.1 Bank data ($8001-$9FFF, odd)
  1.2 Mirroring ($A000-$BFFE, even)
  Registers
  The behavior of these boards differs from that of a typical MMC3 board in the use of the upper CHR address line.
  However, on these particular boards, the CHR bankswitching directly affects the mirroring mapping the nametable RAM.
  Bank data ($8001-$9FFF, odd)
  |+++-++++- New bank value, based on last value written to Bank select register
  |          0: Select 2 KB CHR bank at PPU $0000-$07FF (or $1000-$17FF);
  |          1: Select 2 KB CHR bank at PPU $0800-$0FFF (or $1800-$1FFF);
  ```

## Mapper 119
- Spec: `INES_Mapper_119.txt`
- Helpers: `mapper155_write`, `tqrom119_set_bank`
- Spec register hints:
  ```
  1 Registers
  1.1 Bank data ($8001-$9FFF, odd)
  Registers
  Other than the CHR bank register described below, see MMC3 for a full description.
  Bank data ($8001-$9FFF, odd)
  Bit 6 of the CHR bank number is used to switch between the CHR ROM chip and the CHR RAM chip.
  |++-++++- New bank value, based on last value written to Bank select register
  |         0: Select 2 KB CHR bank at PPU $0000-$07FF (or $1000-$17FF);
  |         1: Select 2 KB CHR bank at PPU $0800-$0FFF (or $1800-$1FFF);
  |         2: Select 1 KB CHR bank at PPU $1000-$13FF (or $0000-$03FF);
  ```

## Mapper 134
- Spec: `INES_Mapper_134.txt`
- Helpers: `mapper134_set`, `mapper134_set_bank`
- **Fixed:** was wrongly modeled as NINA `$4100`; now `mapper134_set` → STA `$6000`, `mapper134_set_bank` → MMC3 `$8000/$8001`; removed from `HasNinaBankRegister`.
- Spec register hints:
  ```
  1 Mode Register ($6000, write)
  2 Outer Bank Register ($6001, write)
  3 CNROM register ($6002, write)
  4 Unknown Register ($6003, write)
  5 MMC3-compatible registers ($8000-$FFFF)
  Mode Register ($6000, write)
  Mask: $E003
  |||| +---- CHR-ROM banking mode
  ||||        0: MMC3 (inner banks from $8000.0-$8000.5)
  ||||        1: CNROM (8 KiB inner bank from $6002)
  ```

## Mapper 135
- Spec: `INES_Mapper_135.txt` → Sachen 8259 (see mappers 137–141)
- Helpers: `mapper135_set` → STA `$4100` (8259 register select/data lean surface)
- Notes: `$4100` is correct for 8259; kept in `HasNinaBankRegister`.

## Mapper 151
- Spec: `INES_Mapper_151.txt`
- Helpers: `mapper151_set_prg_bank`, `mapper183_set_prg_bank`, `racermate168_set`

## Mapper 155
- Spec: `INES_Mapper_155.txt`
- Helpers: `mapper155_write`, `mapper180_set_prg_bank`, `mapper192_set_bank`

## Mapper 160
- Spec: `INES_Mapper_160.txt` (duplicate of mapper 90 / JY ASIC)
- Helpers: `mapper160_set_prg_bank(reg, bank)` — same MMC3-like emit as `jy90_set_prg_bank`
- **Fixed:** signature was single-bank STA `$8000`; now select/data; window moved from 32K → 16K to match mapper 90.

## Mapper 161
- Spec: `INES_Mapper_161.txt` (Hanjuku Hero → use MMC1 / mapper 1)
- Helpers: `mapper161_write(addr, value)` — same serial emit as `mmc1_write`
- **Fixed:** was STA `$8000` / 32K bus-conflict; now MMC1 serial + 16K window.

## Mapper 162
- Spec: `INES_Mapper_162.txt`
- Helpers: `mapper162_set_mode`, `mapper162_set_prg_high`, `mapper162_set_prg_low`, `mapper171_set`, `mapper172_set`
- Notes:
  - 162 32K window OK
- Spec register hints:
  ```
  2 Registers
  2.1 PRG-ROM Bank Low/CHR-RAM Switch ($5000, write)
  2.2 Alternative PRG-ROM Bank low ($5100, write)
  2.3 PRG-ROM Bank High ($5200, write)
  2.4 Mode ($5300, write)
  CPU $6000-$7FFF: 8 KiB unbanked PRG-RAM, battery-backed
  CPU $8000-$FFFF: 32 KiB switchable PRG-ROM bank
  PPU $0000-$1FFF: 8 KiB unbanked CHR-RAM bank, 4 KiB can be automatically switched
  Registers
  PRG-ROM Bank Low/CHR-RAM Switch ($5000, write)
  ```

## Mapper 168
- Spec: `INES_Mapper_168.txt`
- Helpers: `racermate168_set` → STA `$8000`
- **Fixed:** removed from `BusConflictBankSwitch` (NESDev: Subject to bus conflicts: no). `ExtractPrgBank` `>>6` OK.
- Spec register hints:
  ```
  4 Registers
  4.1 Bank Select ($8000-$BFFF)
  4.2 RAM Protection and IRQ acknowledge ($C000-$FFFF)
  PRG ROM size: 64 KiB
  PRG ROM bank size: 16 KiB
  PRG RAM: No
  CHR capacity: 64 KiB RAM, half (or all) battery-backed.
  CHR bank size: 4 KiB
  CPU $8000-$BFFF: 16 KiB switchable PRG ROM bank
  CPU $C000-$FFFF: 16 KiB PRG ROM bank, fixed to the last bank
  ```

## Mapper 171
- Spec: `INES_Mapper_171.txt`
- Helpers: `mapper171_set`, `mapper172_set`, `mapper173_write`

## Mapper 172
- Spec: `INES_Mapper_172.txt`
- Helpers: `mapper172_set`, `mapper173_write`, `mapper174_set`
- Spec register hints:
  ```
  2 Registers
  PPU $0000-$1FFF: 8 KB switchable CHR ROM bank
  Registers
  Mask: $E103
  Read $4100-$4103: [..RR RRRR]: Read Register. Bits 4-5 are inverted if Invert==1.
  Write $4100: When Mode==0: Bits 0-5 of Register := Input, bits 0-3 being inverted if Invert==1.
  When Mode==1: Bits 0-3 of Register incremented by one, bits 4-5 unaffected.
  Write $4101: Invert := Written value bit 5.
  Write $4102: Input := Written value bits 0-5. Note that the bit order D0-D5 is reversed.
  Write $4103: Mode := Written value bit 5.
  ```

## Mapper 173
- Spec: `INES_Mapper_173.txt`
- Helpers: `mapper173_write`, `mapper174_set`, `mapper175_set`
- Spec register hints:
  ```
  2 Registers
  PPU $0000-$1FFF: 8 KB switchable CHR ROM bank
  Registers
  Mask: $E100
  read $4100: [xxxx SRRR]
  Mask: $E103
  write $4100: If Increment is set, internal register 'RRR' <- 'RRR'+1
  write $4101: [.... ...V] - Invert Mode. This value is immediately inverted and used as CHR A14.
  write $4102: [.... SPPP] - Copy data bus to internal registers 'S' and 'PPP'.
  'S' can be read back immediately; 'PPP' must be copied using $4100 first.
  ```

## Mapper 174
- Spec: `INES_Mapper_174.txt` (NTDec 5-in-1; ≡ mapper 58 bit shuffle)
- Helpers: `mapper174_set` → STA `$8000` — OK. Packing `OPPP CCCM` left to caller.
- Notes: dual 16K/32K mode via bit O; lean model keeps 32K window (same as mapper 58 lean).

## Mapper 175
- Spec: `INES_Mapper_175.txt` (Kaiser 15-in-1 — stub page only)
- Helpers: `mapper175_set` → STA `$8000` (lean placeholder; no register layout on wiki)

## Mapper 179
- Spec: `INES_Mapper_179.txt` (Nestopia duplicate of 176)
- Helpers: `mapper179_set` → STA `$8000`; prefer `mapper176_set_prg_bank`

## Mapper 180
- Spec: `INES_Mapper_180.txt`
- Helpers: `mapper180_set_prg_bank` → STA `$8000` (bus conflicts: yes)
- **Fixed:** `HasFixedFirst16KWindow` — `$8000–$BFFF` fixed bank 0, `$C000–$FFFF` switchable; `TryMap` / `SiteBank` / `IsFixedCpuAddress` updated.
- Spec register hints:
  ```
  3.1 Bank select ($8000-$FFFF)
  CPU $8000-$BFFF: 16 KB PRG ROM bank, fixed to the first bank
  CPU $C000-$FFFF: 16 KB switchable PRG ROM bank
  ```

## Mapper 181
- Spec: `INES_Mapper_181.txt` (FCEUX Seicross v2 ≈ mapper 185 CHR-disable; no distinct PRG bank)
- Helpers: `mapper181_set` lean STA `$8000` stub only
- **Fixed:** removed from 32K / bus-conflict PRG model (not a PRG bank mapper).

## Mapper 183
- Spec: `INES_Mapper_183.txt`
- Helpers: `mapper183_set_prg_bank`, `mapper190_write`
- Spec register hints:
  ```
  Mask: $F80C
  $8800: PRG at $8000-$9FFF
  $A800: PRG at $A000-$BFFF
  $A000: PRG at $C000-$DFFF
  $9800: standard VRC4 mirroring control
  CHR and IRQ are the same as VRC4e.
  ```

## Mapper 187
- Spec: `INES_Mapper_187.txt`
- Helpers: `jy211_set_prg_bank`, `mapper187_set_bank`, `mapper187_set_outer`, `mapper214_set_bank`
- Notes:
  - 187 outer @$5000 OK
- Spec register hints:
  ```
  1 Registers
  1.1 NROM Override Register ($5000)
  1.2 Protection Read ($5000-$5FFF)
  1.3 CHR A18 Control ($8000-$9FFF, even)
  1.4 MMC3-compatible registers ($8000-$FFFF)
  Registers
  NROM Override Register ($5000)
  Mask: $F001
  | |+-+++-- Select 16 KiB PRG-ROM bank at CPU
  | |        $8000-$BFFF and $C000-$FFFF
  ```

## Mapper 190
- Spec: `INES_Mapper_190.txt`
- Helpers: `mapper190_write`, `mapper204_write`, `mapper208_set_prg`
- Spec register hints:
  ```
  PRG switching:
  There's 16 16K PRG ROM banks, but a combination of address and data lines are used
  CHR Switching:
  CHR is composed of 4 2K banks at 0000-07FF, 0800-0FFF, 1000-17FF, 1800-1FFF.
  Only A0, A1, A13, 14, and 15 are used to select the CHR bank and match the following mask:
  b = CHR bank # to change.
  This means that A000, A004, A008, A00C, ... BFF8, BFFC all set the first 2K CHR bank.
  +----------------------------+++--- Select 16 KiB PRG bank at $8000-$BFFF
  PRG bank at $C000-$FFFF fixed to bank 0
  ||     ++++-++++--- Select 2 KiB CHR bank ...
  ```

## Mapper 192
- Spec: `INES_Mapper_192.txt`
- Helpers: `mapper192_set_bank`, `mapper193_set_bank`, `mapper194_set_bank`
- Spec register hints:
  ```
  Registers
  Registers resemble MMC3, except CHR ROM banks 8, 9, 10, and 11 are replaced with CHR RAM.
  This is effectively the same as INES Mapper 074 but with twice as much CHR RAM.
  In addition to any CHR-ROM present, there is also an additional 4k of CHR-RAM which is selectable.
  CHR Pages $08-$0B are CHR-RAM, other pages are CHR-ROM.
  Categories: Mappers with scanline IRQs, INES Mappers, MMC3 with CHR ROM and CHR RAM
  ```

## Mapper 204
- Spec: `INES_Mapper_204.txt`
- Helpers: `mapper204_write`, `mapper208_set_bank`, `mapper208_set_prg`

## Mapper 206
- Spec: `INES_Mapper_206.txt`
- Helpers: `dxrom206_set_bank`, `retcufrom29_set`
- Spec register hints:
  ```
  PRG ROM capacity
  PRG ROM window
  PRG RAM capacity
  CHR capacity
  CHR window
  PRG always has the last two 8KiB banks fixed to the end.
  CHR size limit is 64KiB, PRG size limit is 128KiB.
  There are no control registers in the $A000-$FFFF range.
  Writing to RAM while executing from $8000-9FFF can cause erratic mapper writes. See Errata below.
  1 Registers
  ```

## Mapper 208
- Spec: `INES_Mapper_208.txt`
- Helpers: `mapper208_set_bank`, `mapper208_set_prg`, `mapper213_write`, `mapper215_set_bank`
- Notes:
  - 208 PRG @$4800 OK
- Spec register hints:
  ```
  2 Registers
  2.1 PRG Bank and Mirroring ($4800-$4FFF, $6800-$6FFF, write-only)
  2.2 Protection Table Index ($5000-$57FF, write-only)
  2.3 Protection Data Access ($5800-$5FFF, read)
  2.4 Protection Data Access ($5800-$5FFF, write)
  2.5 IRQ latch ($C000-$DFFE, even)
  2.6 IRQ reload ($C001-$DFFF, odd)
  2.7 IRQ disable ($E000-$FFFE, even)
  2.8 IRQ enable ($E001-$FFFF, odd)
  CPU $8000-$FFFF: 32 KB switchable PRG ROM bank
  ```

## Mapper 211
- Spec: `INES_Mapper_211.txt`
- Helpers: `jy211_set_prg_bank`, `mapper214_set_bank`

## Mapper 213
- Spec: `INES_Mapper_213.txt`
- Helpers: `mapper213_write`, `mapper215_set_bank`, `mapper219_set_bank`

## Mapper 214
- Spec: `INES_Mapper_214.txt`
- Helpers: `mapper214_set_bank`, `mapper216_write`, `mapper217_set_bank`

## Mapper 215
- Spec: `INES_Mapper_215.txt`
- Helpers: `mapper215_set_bank`, `mapper219_set_bank`, `mapper219_set_outer`
- Spec register hints:
  ```
  1 Registers
  1.1 NROM Override/Mode Register ($5000)
  1.2 Outer Bank Register ($5001) - Submapper 0 (UNL-8237)
  1.3 Outer Bank Register ($5001) - Submapper 1 (UNL-8237A)
  1.4 Scrambling Pattern Register ($5007)
  1.5 MMC3-compatible registers ($8000-$FFFF, write-only)
  Registers
  NROM Override/Mode Register ($5000)
  Mask: $F007
  |||  ++++- Select 16 KiB PRG-ROM bank at CPU
  ```

## Mapper 216
- Spec: `INES_Mapper_216.txt`
- Helpers: `mapper216_write`, `mapper217_set_bank`, `mapper239_set`

## Mapper 217
- Spec: `INES_Mapper_217.txt`
- Helpers: `mapper217_set_bank`, `mapper239_set`, `mapper251_set_outer`

## Mapper 219
- Spec: `INES_Mapper_219.txt`
- Helpers: `mapper219_set_bank`, `mapper219_set_outer`, `mapper220_set`, `mapper223_set_prg_bank`
- Spec register hints:
  ```
  1 Mode Register ($8002)
  2 MMC3 mode registers ($8000-$FFFF)
  3 MMC3 Bank Register Index ($8000)/Data ($8001)
  4 Extended PRG-ROM Bank Index ($8000, $23-$26)/Data ($8001)
  5 CHR-ROM Bank Number Latch Index ($8000, $08-$1E, even)/Data ($8001)
  6 CHR-ROM Pseudo 2 KiB switch Index ($8000, $09-$0F, odd)/Data ($8001)
  7 CHR-ROM 1 KiB switch Index ($8000, $11-$1F, odd)/Data ($8001)
  8 Outer Bank Bit 0 ($5002)
  9 Outer Bank Bit 1 ($5003)
  Mode Register ($8002)
  ```

## Mapper 220
- Spec: `INES_Mapper_220.txt`
- Helpers: `mapper220_set`, `mapper223_set_prg_bank`, `mapper247_set_bank`, `mapper249_set_bank`

## Mapper 223
- Spec: `INES_Mapper_223.txt`
- Helpers: `mapper223_set_prg_bank`, `mapper247_set_bank`, `mapper249_set_bank`

## Mapper 239
- Spec: `None` (absent from nes.science dump / no INES_Mapper_239 page)
- Helpers: `mapper239_set` → STA `$8000` lean stub; keep until a NESDev page appears

## Mapper 247
- Spec: `None` (absent from dump / no INES_Mapper_247 page)
- Helpers: `mapper247_set_bank` MMC3-like lean stub; keep until a NESDev page appears

## Mapper 249
- Spec: `INES_Mapper_249.txt`
- Helpers: `mapper249_set_bank`, `mapper252_set_prg_bank`, `mapper253_set_prg_bank`

## Mapper 251
- Spec: `INES_Mapper_251.txt`
- Helpers: `mapper251_set_outer`, `mapper255_write`

## Mapper 252
- Spec: `INES_Mapper_252.txt`
- Helpers: `mapper252_set_prg_bank`, `mapper253_set_prg_bank`, `mapper254_set_outer`
- Spec register hints:
  ```
  INES Mapper 253 is the same, except that it's 1 KiB CHR pages 4 and 5 that are CHR-RAM.
  ```

## Mapper 253
- Spec: `INES_Mapper_253.txt`
- Helpers: `mapper103_set_prg_bank`, `mapper253_set_prg_bank`, `mapper254_set_outer`
- Spec register hints:
  ```
  INES Mapper 252 is the same, except that it's 1 KiB CHR pages 6 and 7 that are CHR-RAM.
  ```

## Mapper 254
- Spec: `INES_Mapper_254.txt`
- Helpers: `mapper103_set_prg_bank`, `mapper254_set_outer`

## Mapper 255
- Spec: `INES_Mapper_255.txt`
- Helpers: `mapper255_write`
- Spec register hints:
  ```
  +-----------++-++++ - Select 8 KiB CHR at PPU $0000
  +---++++--++------- - Select 16 KiB PRG at CPU $8000 and $C000 if Z=1
  +---++++--+-------- - Select 32 KiB PRG at CPU $8000 if Z=0
  |+---------------- - Select PRG bank size: 0-32K 1-16K
  four 512 KiB ROMs for PRG
  two 512 KiB ROMs for CHR
  ```

---

# Batch 11 (added + audited against local specs)

## Mapper 59
- Spec: `INES_Mapper_059.txt` (BMC-T3H53 / D1038)
- Helpers: `mapper59_set` → STA `$8000` packed latch `LD SPPp MCCC`
- Model: 32K window + bus-conflict (NROM-128/256 via bit P)
- Audit: OK vs Address Latch ($8000-$FFFF)

## Mapper 100
- Spec: `INES_Mapper_100.txt` (Nesticle MMC3 hack)
- Helpers: `mapper100_set_bank` → MMC3 `$8000/$8001` (extended indices `$46/$47/$80+` left to caller)
- Model: 16K MMC3-like window
- Audit: OK

## Mapper 108
- Spec: `INES_Mapper_108.txt` (FDS conversions; submappers 1–4)
- Helpers: `mapper108_set_prg_bank` → STA `$8000` (submapper 3 full-range lean)
- Model: no `$8000` switchable window (fixed last 32K; banks `$6000`)
- Audit: OK lean; `$F000`-only submapper 1 not specialized

## Mapper 121
- Spec: `INES_Mapper_121.txt` (Kasheng A9711/A9713)
- Helpers: `mapper121_set_bank` MMC3; `mapper121_set_outer` → STA `$5180`
- Model: 16K
- Audit: OK (protection array / bit-reverse left to caller)

## Mapper 123
- Spec: `INES_Mapper_123.txt` (Kasheng H2288)
- Helpers: `mapper123_set_bank` MMC3; `mapper123_set_outer` → STA `$5800`
- Model: 16K
- Audit: OK (index scramble left to caller)

## Mapper 125
- Spec: `INES_Mapper_125.txt` (LH32 Monty FDS conversion)
- Helpers: `mapper125_set_prg_bank` → STA `$6000`
- Model: no `$8000` window (fixed banks + `$6000` switch)
- Audit: OK

## Mapper 120
- Spec: `INES_Mapper_120.txt` (LH15 Tobidase)
- Helpers: `mapper120_set_prg_bank` → STA `$41FF`
- Model: no `$8000` window (fixed bank #2; switch `$6000`)
- Audit: OK

## Mapper 169
- Spec: `INES_Mapper_169.txt` (GoodNES Contra 168-in-1 ≡ mapper 15)
- Helpers: `mapper169_set` → STA `$8000` (alias of `mapper15_set`)
- Model: 32K + bus-conflict like 15
- Audit: OK

## Skipped (no lean PRG bank surface)
- 55: NROM + $6000 ROM/RAM, no bankswitch
- 186: Fukutake Study Box BIOS — assignment unused
- 218: Magic Floor CIRAM-as-CHR — no bankswitch

---

# Batch 12 (classic aliases + NES 2.0 lean)

## Aliases (audited vs redirect pages)
- **109** → Sachen 8259D / 137 — `mapper109_set` @$4100
- **110** → Sachen 243 — `mapper110_set` @$4100
- **122** → Sunsoft-1 / 184 — `mapper122_set_chr_bank` @$6000
- **126** → TEC9719 family / 534 — outer @$6000 + MMC3 bank
- **129** → mapper 58 — `mapper129_set` @$8000
- **131** → mapper 205 — `mapper131_set_outer` @$6000
- **534** — same outer/MMC3 lean surface as 126

## NES 2.0 added
- **260** HP10xx: mode @$5000, PRG @$5001, CHR @$5002, MMC3 bank — OK vs spec
- **269** Games Xplosion: outer @$5000 (sequential), MMC3 — OK
- **264** Yoko: outer @$8000, mode @$8400 — OK lean
- **331** BMC-12-IN-1: absolute write @$A000/$C000/$E000 — OK

## Still skipped (no useful lean PRG surface / missing page)
- 0 NROM, 20 FDS, 55 no bankswitch, 102→284 stub, 104 PEGASUS page missing from dump,
  124 kevtris archive only, 127/128/170 stub pages, 186 Study Box, 218 Magic Floor

# Batch 13
- **347** KS7030: `mapper347_write` @$8000/$9000 — OK; no $8000 window model
- **365** Asder PC-95: `mapper365_set_prg_bank(which,bank)` @$8000+which — OK; 16K model
- **371** PEC-586 ES: `$5000`/`$5100` — OK; 16K model
- **417** Fine Studio: absolute write — OK; 16K model
- **446** SMD172B: mode @$5000 + supervisor write — OK; no fixed window (FPGA)

Also: `NESRomReader` now reads NES 2.0 mapper bits 8–11.

# Batch 14
- **266** City Fighter: `$900C` 32K PRG — OK
- **292** Dragon Fighter: `$6000` PRG + MMC3 — OK
- **296** VT3x: `$411D`/`$412C` — OK
- **351** Techline XB: `$5000`–`$5002` + MMC3 — OK
- **359** SB-5013: `$8000+which` + `$9000` base — OK
- **375** 2MiB latch (≡227+): absolute write — OK; 32K+bus model
- **383** JY soccer: MMC3 + PAL write — OK
- **452** DS-9-27: absolute latch — OK; 32K model

# Batch 15
- **285** A65AS: data latch @$8000 — OK
- **286** BS-5: address latch write — OK; 16K
- **302** KS7057: absolute write — OK; 16K
- **305** KS7031: address-select write — OK; no $8000 window
- **354** FAM250: @$E000/$F000 latch — OK; 32K
- **357** Bit Corp 4602: @$4022/@$4120 — OK
- **368** Yung-08: @$4022 — OK
- **380** 970630C: address latch write — OK; 32K
- **444** NC7000M: @$6000 + MMC3 — OK
- **451** Haratyler: absolute write — OK; 16K
- **454** 110-in-1: latch write — OK; 32K

# Batch 16
- **270** OneBus UIO: `$412C` — OK
- **274** Cartridge Story: inner `$8000` + outer write — OK; 32K
- **304** SMB2J FDS: `$4027` — OK; no $8000 window
- **315** 830134C: `$6800` + MMC3 — OK
- **391** BS-110: `$6000` + MMC3 — OK
- **398** JY YY840820C: absolute write — OK; 16K
- **429** FCFC1: `$8000` latch — OK; 32K (no bus conflicts)
- **518** Subor Dance: `$5000`/`$5200` — OK; 32K
- **528** 831128C: FME-7-like write — OK; 16K
- **542** Mao VRC4: vrc24 PRG — OK
- **543** CH-501: serial `$5000` lean — OK
- **544** FS306: `$9C00` PRG2 — OK
- **558** Yancheng: `$5000`/`$5100`/`$5300` — OK; 32K
