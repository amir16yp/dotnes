# NES Program Review Rules

Rules for reviewing NES program samples (code under `samples/`) and changes to
the NESLib API (`src/neslib/NESLib.cs`).

---

## NES Program Constraints

NES programs run on a 6502 processor with 2KB RAM, no operating system, and no
garbage collector. The transpiler supports a strict subset of C#.

| Check | What to look for |
|-------|-----------------|
| **Must end with `while (true) ;`** | Every NES program must end with an infinite loop. The NES has no concept of "exit" — without the loop, the CPU executes garbage bytes past the end of your code. |
| **Top-level statements only** | Programs use top-level statements (no `class Program { static void Main() }`). User-defined static methods with parameters and return values are supported. |
| **No BCL types** | `string`, `List<T>`, `Dictionary<K,V>`, LINQ, `Console.WriteLine`, and all Base Class Library types are unavailable. Programs can only use `byte`, `ushort`, `int`, `bool`, byte arrays (`byte[]`), ushort arrays (`ushort[]`), and NESLib API calls. |
| **Sealed layout classes only** | `sealed class` with blittable fields (`byte`/`ushort`/`bool`/fixed buffers) is allowed. `new T()` bump-allocates in BSS (no GC/free). No inheritance, virtuals, interfaces, or reference-typed fields. Structs and static methods remain fully supported. |
| **No string manipulation** | The NES has no string support. Text display uses tile indices via `vram_put`/`vram_write` or the `set_vram_update` mechanism. |
| **Fixed-size byte arrays** | `byte[]` initializers become ROM data tables. They are read-only and baked into the ROM at compile time. Don't try to modify them at runtime. |

---

## NESLib API Usage

| Check | What to look for |
|-------|-----------------|
| **Never redefine neslib constants** | The `PAD` enum (`PAD.A`, `PAD.UP`, `PAD.LEFT`, etc.), `MASK` constants, PPU register constants, and APU constants are all built-in. Do NOT create local `const byte PAD_A = 0x01` or similar redefinitions. Use the built-in definitions directly. |
| **NESLib primitives stay stubs** | Low-level primitives (`pal_col`, `ppu_on_all`, …) stay `=> throw null!` and map to `BuiltInSubroutines` / ILDispatch. Composed wrappers may have real C# bodies that call other NESLib APIs; the transpiler loads those bodies from the implementation DLL and emits them only when used. |
| **Adding new NESLib methods** | Primitives: (1) stub in `NESLib.cs`, (2) 6502 in `BuiltInSubroutines.cs` or ILDispatch, (3) `UsedMethods` wiring. Wrappers: implement in C# calling existing APIs — no duplicate builtin block. Never ship both a C# body and a builtin for the same name. |
| **Palette data sizes** | `pal_all` expects exactly 32 bytes. `pal_bg` and `pal_spr` expect exactly 16 bytes. Wrong sizes produce garbled colors. |
| **Sprite limits** | The NES supports 64 sprites (256 bytes of OAM). Exceeding this silently overwrites previous sprites. The `oam_spr` return value tracks the OAM index — verify it's checked. |
| **PPU timing** | VRAM writes (`vram_put`, `vram_adr`, `vram_write`) must happen during vblank (between `ppu_wait_nmi` and `ppu_on_all`/`ppu_on_bg`/`ppu_on_spr`). Writing to VRAM while rendering is active produces graphical glitches. |

---

## Sample Project Conventions

| Check | What to look for |
|-------|-----------------|
| **Every sample needs a `.csproj`** | Samples must have a project file that references `dotnes`. Check that `<PackageReference Include="dotnes" />` is present. |
| **CHR ROM files** | Most samples need a `chr_*.s` file for graphics. The test infrastructure looks for `chr_{samplename}.s` first, then falls back to `chr_generic.s`. If a sample uses custom tiles, it needs its own CHR file. |
| **Test coverage** | Every sample should have a corresponding `[InlineData]` entry in `TranspilerTests.Write` with a `.verified.bin` snapshot. Missing test data means the sample can silently break. |
| **`README.md` or comments** | Non-trivial samples should explain what they demonstrate (scrolling, bank switching, music, etc.) so future contributors understand the purpose. |

---

## Music Architecture

| Check | What to look for |
|-------|-----------------|
| **Music subroutines go before `main()`** | In the ROM layout, `music_tick` and `start_music` are emitted before the `main` entry point to match cc65's layout. Don't reorder these. |
| **Note table format** | `set_music_pulse_table(ushort[])` and `set_music_triangle_table(ushort[])` store note frequencies as interleaved lo/hi byte pairs. The transpiler handles the byte interleaving — the C# code provides a plain `ushort[]`. |
| **`apu_init()` before `start_music()`** | The APU must be initialized before starting music playback. Calling `start_music` without `apu_init` produces undefined APU state. |
| **`music_tick()` in the main loop** | `music_tick()` (the built-in music engine) must be called every frame (inside the `while (true)` loop, typically after `ppu_wait_nmi`). Missing it causes music to stall. Note: `music_play(byte)` is the separate FamiTone2 extern — don't confuse the two. |
