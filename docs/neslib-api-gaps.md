# NESLib API Gaps

Remaining low-level items after the hardware-surface pass. Everything else listed historically
as a stub either has a builtin, an ILDispatch intrinsic, a C# NESLib body, or links as `JSR _name`.

## Still incomplete

| Method / area | Status |
|---------------|--------|
| `oam_meta_spr_clip` | Explicit transpile error — signed-X clipping not implemented; use `oam_meta_spr` |
| `vram_unlz4` | Emits `JSR _vram_unlz4` — requires linked ca65 implementation |
| `music_play` / `music_stop` / `music_pause` / `sfx_play` / `sample_play` | Emit `JSR _name` for FamiTone / linked asm (not stock ROM builtins) |
| General pointers / `ref` / `fixed` | Transpiler limit — use `peek`/`poke` or native `.s` |
| `reset_set_callback` | Not added — reset path remains CRT-only |
| Undocumented 6502 opcodes | Not in object model |

## Recently closed (for reference)

- CPU ops: `nop`, `pha`/`pla`, `php`/`plp`, `tax`/`txa`/`tay`/`tya`, `txs`/`tsx`, `inx`/`dex`/`iny`/`dey`, `brk`
- PPU: `PPU_OAM_*`, `oam_dma`, `ppu_status`, `ppu_scroll`, `sprite0_wait`, `sprite0_hit`, `NT_ATTR_*`
- APU: DMC/frame constants, `apu_play_*` / `apu_stop_*`, runtime channel writers
  (`apu_pulse1_ctrl`, `apu_noise_period`, `apu_dmc_raw`, …) as ILDispatch STA,
  `apu_status_write` / `apu_frame` / `apu_status()`
- Input: `JOYPAD1`/`JOYPAD2`, zapper helpers
- Memory: `memfill`, `oam_clear_fast`
- Mappers: see `NESLib.Mappers.cs` — CNROM/UxROM/AxROM/MMC1–5/VRC/Namco/Bandai/
  Jaleco/Irem/Taito/Sunsoft/NINA/Action/multicart helpers (lean STA surface)
- Aliases: `rand()` → `rand8()`, pad1 / palette helpers, MMC1 C# wrappers
