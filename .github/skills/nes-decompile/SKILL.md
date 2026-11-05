---
name: nes-decompile
description: >-
  Decompile NES ROM files (.nes) into C# projects that can be rebuilt with dotnes.
  Use this skill whenever the user wants to decompile a ROM, reverse-engineer a .nes file,
  convert a ROM to C#, extract code from a NES ROM, create a project from an existing ROM,
  or do a round-trip test (transpile → decompile → retranspile). Also use when the user says
  things like "decompile this ROM", "turn this .nes into C#", "extract the code", "what does
  this ROM do", "reverse engineer", or "round-trip". This is the high-level decompilation tool
  — for low-level 6502 disassembly, use nes-rom-debug instead.
---

# NES ROM Decompiler

Decompile `.nes` ROM files into complete C# projects that can be rebuilt through dotnes.

## Decompiler Tool

The decompiler lives at `src/dotnes.decompiler/` and is a .NET CLI tool.

### Running the Decompiler

```bash
dotnet run --project src/dotnes.decompiler -- <input.nes> [output-directory] [--mode auto|transpile|raw] [--exact]
# or
dotnes decompile <input.nes> [output-directory] [--mode auto|transpile|raw] [--exact]
```

- **`<input.nes>`** — Path to the NES ROM file (required)
- **`[output-directory]`** — Where to write the C# project (optional; defaults to the ROM filename without extension)
- **`--mode`** — `auto` (default): Transpile for detected dotnes ROMs, RawPrg otherwise; `transpile` forces NESLib C# recovery; `raw` forces byte-identical `prg.bin` packing
- **`--exact`** / **`--no-optimize`** — Opt out of soft-CPU structuring (more literal goto/labels). Default is optimized readability. Never removes discovered routines (no dead-code elimination).

### Output Files

| File | Description |
|------|-------------|
| `Program.cs` | Recovered C# — NESLib for dotnes ROMs; soft-CPU game logic for arbitrary ROMs |
| `{name}.csproj` | MSBuild project with NES header props (`NESMirroring`, `NESBattery`, mapper/banks, optional `NESPackMode`) |
| `chr_generic.s` / `chr_generic_N.s` | CHR ROM as ca65 assembly |
| `prg.bin` / `header.bin` | Exact PRG + iNES header (RawPrg mode only; rebuild authority) |
| `RomImage.cs` | C# mirror of the exact header (RawPrg mode only) |
| `disasm.s` | Best-effort listing (RawPrg mode only) |

### What Gets Recovered (dotnes / NESLib)

The recoverer formats **most NESLib APIs** when their labels/signatures are found, including:

| Category | Functions |
|----------|-----------|
| **Palette** | `pal_col`, `pal_bg`, `pal_spr`, `pal_all`, `pal_clear`, `pal_*_bright` |
| **VRAM** | `vram_adr(NTADR_*)`, `vram_write`, `vram_fill`, `vram_put`, `vram_inc`, `vram_unrle`, `vram_read`, `set_vram_update`, `flush_vram_update`, vrambuf_* |
| **PPU** | `ppu_on_*`, `ppu_off`, `ppu_mask`, `ppu_wait_*`, `waitvsync`, `scroll`/`split`, `fade_in`/`fade_out` |
| **Sprites/OAM** | `oam_clear`, `oam_size`, `oam_spr`, `oam_spr_2x2`, `oam_meta_spr*`, `oam_hide_rest` |
| **Input** | `pad_poll`, `pad_trigger`, `pad_state` |
| **RNG / misc** | `rand8`, `rand16`, `set_rand`, `srand`, `delay`, `nesclock`, `bcd_add`, `rect_overlap`, `sprite_overlap` |
| **Audio** | `apu_init`, `start_music`, `music_tick`, `music_play`/`stop`/`pause`, `sfx_play` |
| **Mapper** | `bank_spr`/`bank_bg`, `cnrom_set_chr_bank` |
| **Memory** | `poke`/`peek`, `memfill` |

String literals in `vram_write()` are recovered when bytes are printable ASCII.
Final-built-in detection uses byte signatures extracted from `BuiltInSubroutines` plus hand-tuned patterns.

### Arbitrary (non-dotnes) ROMs

Auto mode emits `NESPackMode=RawPrg` with `prg.bin`/`header.bin` so rebuilds are **byte-identical**. `Program.cs` gets soft-CPU recovered game logic (optimized for reading by default — structured loops, folded imm+poke; pass `--exact` for literal recovery). All discovered routines are kept (no DCE). Packing still uses the raw PRG until transpile can match commercial banks.

## Common Workflows

```bash
cd samples/hello && dotnet build && cd ../..
dotnet run --project src/dotnes.decompiler -- samples/hello/bin/Debug/net10.0/hello.nes hello-decompiled
```

Round-trip (dotnes ROM):

```bash
dotnet run --project src/dotnes.decompiler -- samples/hello/bin/Debug/net10.0/hello.nes hello-roundtrip
cd hello-roundtrip && dotnet build && cd ..
python scripts/compare_rom.py samples/hello/bin/Debug/net10.0/hello.nes hello-roundtrip/bin/Debug/net10.0/hello.nes
```

## Architecture

1. **Disassemble** — recursive descent from RESET/NMI/IRQ
2. **Detect** — stock CRT prefix → Transpile vs RawPrg
3. **Recover** — symbol table from `CreateWithBuiltIns` + signature DB; lift NESLib calls / control flow
4. **Emit** — project tree via `ProjectEmitter`

## MSBuild Properties in Generated .csproj

| Property | Condition | Example |
|----------|-----------|---------|
| `NESMapper` | Mapper ≠ 0 | `<NESMapper>1</NESMapper>` |
| `NESPrgBanks` | PRG banks ≠ 2 | `<NESPrgBanks>4</NESPrgBanks>` |
| `NESChrBanks` | CHR banks ≠ 1 | `<NESChrBanks>2</NESChrBanks>` |
| `NESMirroring` | Vertical mirroring | `<NESMirroring>Vertical</NESMirroring>` |
| `NESBattery` | Battery-backed SRAM | `<NESBattery>true</NESBattery>` |
| `NESPackMode` / `NESRawPrg` | Non-dotnes / `--mode raw` | `RawPrg` + `prg.bin` |

## Related Skills

- **nes-rom-debug** — Low-level 6502 disassembly and byte-level ROM inspection
- **nes-emu-debug** — Run ROMs in MesenCE emulator to verify runtime behavior
