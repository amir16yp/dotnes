using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("dotnes")]
[assembly: InternalsVisibleTo("dotnes-decompiler")]
[assembly: InternalsVisibleTo("dotnes.tasks")]
[assembly: InternalsVisibleTo("dotnes.tests")]

namespace NES;

/// <summary>
/// Recommended use via `using static NES.NESLib;`
/// Based on: https://github.com/clbr/neslib/blob/master/neslib.h
/// Alternate: https://github.com/mhughson/attributes/blob/master/neslib.h
/// Tables in: https://github.com/clbr/neslib/blob/master/neslib.sinc
/// </summary>
public static partial class NESLib
{
    /// <summary>
    /// set bg and spr palettes, data is 32 bytes array
    /// </summary>
    public static void pal_all(byte[] data) => throw null!;

    /// <summary>
    /// set bg palette only, data is 16 bytes array
    /// </summary>
    public static void pal_bg(byte[] data) => throw null!;

    /// <summary>
    /// set spr palette only, data is 16 bytes array
    /// </summary>
    public static void pal_spr(byte[] data) => throw null!;

    /// <summary>
    /// set a palette entry, index is 0..31
    /// </summary>
    public static void pal_col(byte index, byte color) => throw null!;

    /// <summary>
    /// set a background palette entry, index is 0..15
    /// </summary>
    public static void pal_bg_col(byte index, byte color) => pal_col(index, color);

    /// <summary>
    /// set a sprite palette entry, index is 0..15 (maps to global indices 16..31)
    /// </summary>
    public static void pal_spr_col(byte index, byte color) => pal_col((byte)(index + 16), color);

    /// <summary>
    /// reset palette to $0f
    /// </summary>
    public static void pal_clear() => throw null!;

    /// <summary>
    /// set virtual bright both for sprites and background, 0 is black, 4 is normal, 8 is white
    /// </summary>
    public static void pal_bright(byte bright) => throw null!;

    /// <summary>
    /// set virtual bright for sprites only
    /// </summary>
    public static void pal_spr_bright(byte bright) => throw null!;

    /// <summary>
    /// set virtual bright for sprites background only
    /// </summary>
    public static void pal_bg_bright(byte bright) => throw null!;

    /// <summary>
    /// play music (FamiTone2 library).
    /// Starts or resumes playback of a song by index from FamiTone2 music data.
    /// <para><b>⚠️ Not to be confused with <see cref="music_tick"/>.</b>
    /// This method is for the FamiTone2 external music library.
    /// <see cref="music_tick"/> is for the dotnes built-in music engine.</para>
    /// </summary>
    public static void music_play(byte song) => throw null!;

    /// <summary>
    /// stop music (FamiTone)
    /// </summary>
    public static void music_stop() => throw null!;

    /// <summary>
    /// pause music (FamiTone)
    /// </summary>
    public static void music_pause(bool pause) => throw null!;

    /// <summary>
    /// initialize the APU (Audio Processing Unit)
    /// enables all sound channels and sets initial state
    /// </summary>
    public static void apu_init() => throw null!;

    /// <summary>
    /// Play a tone on a pulse channel.
    /// Packs duty/volume into the control register and splits the period
    /// into timer lo/hi writes, so callers don't need register-level knowledge.
    /// </summary>
    /// <remarks>
    /// All arguments must be compile-time constants. Using local variables or
    /// expressions will cause a <c>TranspileException</c>.
    /// </remarks>
    /// <param name="channel">Pulse channel to use.</param>
    /// <param name="period">11-bit timer period (0x000–0x7FF). Lower values = higher pitch.</param>
    /// <param name="duty">Duty cycle (waveform shape).</param>
    /// <param name="volume">Volume 0–15</param>
    public static void apu_play_tone(PulseChannel channel, ushort period, APUDuty duty, byte volume) => throw null!;

    /// <summary>
    /// Stop (silence) a pulse channel.
    /// Writes 0x30 to the channel's control register (constant volume = 0).
    /// </summary>
    /// <remarks>
    /// The channel argument must be a compile-time constant. Using a local variable or
    /// expression will cause a <c>TranspileException</c>.
    /// </remarks>
    /// <param name="channel">Pulse channel to silence.</param>
    public static void apu_stop(PulseChannel channel) => throw null!;

    /// <summary>
    /// Silence both pulse channels.
    /// </summary>
    public static void apu_stop_all()
    {
        apu_stop(PulseChannel.Pulse1);
        apu_stop(PulseChannel.Pulse2);
    }

    /// <summary>
    /// Play a tone on the triangle channel. Arguments must be compile-time constants.
    /// </summary>
    public static void apu_play_triangle(ushort period, byte linear) => throw null!;

    /// <summary>
    /// Silence the triangle channel.
    /// </summary>
    public static void apu_stop_triangle() => throw null!;

    /// <summary>
    /// Play noise. Arguments must be compile-time constants.
    /// </summary>
    /// <param name="period">Noise period index 0–15 (low nibble of $400E).</param>
    /// <param name="volume">Volume 0–15 with constant volume + length halt.</param>
    /// <param name="toneMode">If true, sets bit 7 of $400E (tone mode).</param>
    public static void apu_play_noise(byte period, byte volume, bool toneMode) => throw null!;

    /// <summary>
    /// Silence the noise channel.
    /// </summary>
    public static void apu_stop_noise() => throw null!;

    /// <summary>
    /// initialize FamiTone2 music library with music data
    /// Usage: famitone_init("danger_streets_music_data")
    /// The string names a data label in a linked .s file
    /// </summary>
    public static void famitone_init(string musicDataLabel) => throw null!;

    /// <summary>
    /// initialize FamiTone2 sound effects
    /// Usage: sfx_init("demo_sounds")
    /// The string names a data label in a linked .s file
    /// </summary>
    public static void sfx_init(string soundDataLabel) => throw null!;

    /// <summary>
    /// set up music data pointer for playback
    /// data is a byte array containing encoded music commands
    /// </summary>
    public static void start_music(byte[] data) => throw null!;

    /// <summary>
    /// advance one frame of music playback (dotnes built-in music engine).
    /// Call once per vblank/NMI to process music data and write to APU registers.
    /// <para><b>⚠️ Not to be confused with <see cref="music_play"/>.</b>
    /// <see cref="music_play"/> is for the FamiTone2 external library.
    /// This method is for the dotnes built-in music engine.</para>
    /// </summary>
    public static void music_tick() => throw null!;

    /// <summary>
    /// register a ushort[] note table for pulse channel playback
    /// </summary>
    public static void set_music_pulse_table(ushort[] table) => throw null!;

    /// <summary>
    /// register a ushort[] note table for triangle channel playback
    /// </summary>
    public static void set_music_triangle_table(ushort[] table) => throw null!;

    /// <summary>
    /// play sound effect
    /// </summary>
    public static void sfx_play(byte sound, byte channel) => throw null!;

    /// <summary>
    /// play sample
    /// </summary>
    public static void sample_play(byte sample) => throw null!;

    /// <summary>
    /// Set NMI callback to a function (called every NMI frame).
    /// Usage: nmi_set_callback(&amp;my_nmi_handler)
    /// </summary>
    /// <remarks>
    /// Accepts a direct address of a static managed or extern void method with no arguments.
    /// Disable PPU NMI generation before changing the callback; the address update is not atomic.
    /// Native callbacks return with RTS, not RTI, and must preserve compiler scratch memory.
    /// </remarks>
    public static unsafe void nmi_set_callback(delegate*<void> callback) => throw null!;

    /// <summary>
    /// Set IRQ callback to a function (called when a hardware IRQ fires).
    /// Usage: irq_set_callback(&amp;my_irq_handler)
    /// </summary>
    /// <remarks>
    /// Accepts a direct address of a static managed or extern void method with no arguments.
    /// This call retains the IRQ dispatcher even when the callback is entirely native.
    /// Disable IRQs before changing the callback. Native callbacks must acknowledge their
    /// IRQ source, preserve compiler scratch memory, and return with RTS, not RTI.
    /// </remarks>
    public static unsafe void irq_set_callback(delegate*<void> callback) => throw null!;

    /// <summary>
    /// Compile-time directive selecting native ownership of PPU/OAM for the entire program.
    /// The NMI dispatcher retains frame counters and the callback but performs no graphics I/O.
    /// </summary>
    /// <remarks>
    /// Put this declaration in the program's startup code. It emits no instructions and
    /// is not a runtime switch. Stock buffered rendering APIs cannot be used in this mode.
    /// Reset-time PPU initialization is unchanged; the native runtime owns graphics after startup.
    /// </remarks>
    public static void ppu_use_native_renderer() => throw null!;

    /// <summary>
    /// enable CPU interrupts (6502 CLI instruction)
    /// Must be called after setting up the IRQ source (e.g., MMC3 scanline counter).
    /// </summary>
    public static void cli() => throw null!;

    /// <summary>
    /// disable CPU interrupts (6502 SEI instruction)
    /// </summary>
    public static void sei() => throw null!;

    /// <summary>6502 NOP</summary>
    public static void nop() => throw null!;
    /// <summary>6502 PHA</summary>
    public static void pha() => throw null!;
    /// <summary>6502 PLA</summary>
    public static void pla() => throw null!;
    /// <summary>6502 PHP</summary>
    public static void php() => throw null!;
    /// <summary>6502 PLP</summary>
    public static void plp() => throw null!;
    /// <summary>6502 TAX</summary>
    public static void tax() => throw null!;
    /// <summary>6502 TXA</summary>
    public static void txa() => throw null!;
    /// <summary>6502 TAY</summary>
    public static void tay() => throw null!;
    /// <summary>6502 TYA</summary>
    public static void tya() => throw null!;
    /// <summary>6502 TXS</summary>
    public static void txs() => throw null!;
    /// <summary>6502 TSX</summary>
    public static void tsx() => throw null!;
    /// <summary>6502 INX</summary>
    public static void inx() => throw null!;
    /// <summary>6502 DEX</summary>
    public static void dex() => throw null!;
    /// <summary>6502 INY</summary>
    public static void iny() => throw null!;
    /// <summary>6502 DEY</summary>
    public static void dey() => throw null!;
    /// <summary>6502 BRK (software interrupt / debug stop)</summary>
    public static void brk() => throw null!;

    /// <summary>
    /// get pad trigger
    /// </summary>
    public static PAD pad_trigger(byte pad) => throw null!;

    /// <summary>
    /// get pad state
    /// </summary>
    public static PAD pad_state(byte pad) => throw null!;

    /// <summary>
    /// Returns -1 if LEFT is pressed, +1 if RIGHT is pressed, 0 otherwise.
    /// </summary>
    public static sbyte pad_dpad_x(PAD joy) => throw null!;

    /// <summary>
    /// Returns -1 if UP is pressed, +1 if DOWN is pressed, 0 otherwise.
    /// </summary>
    public static sbyte pad_dpad_y(PAD joy) => throw null!;

    /// <summary>
    /// Returns true if the specified button is pressed in the pad state.
    /// Compile-time intrinsic: emits inline AND + branch, identical to (joy &amp; button) != 0.
    /// <paramref name="button"/> must be a compile-time constant (e.g. <c>PAD.LEFT</c>).
    /// </summary>
    public static bool pad_pressed(PAD joy, PAD button) => throw null!;

    /// <summary>
    /// read from vram
    /// </summary>
    public static void vram_read(byte[] dst, uint size) => throw null!;

    /// <summary>
    /// write to vram
    /// </summary>
    public static void vram_write(byte[] src, uint size) => throw null!;

    /// <summary>
    /// unpack LZ4 data to vram
    /// </summary>
    public static void vram_unlz4(byte[] input, byte[] output, uint uncompressedSize) => throw null!;

    /// <summary>
    /// fill memory at an absolute address
    /// </summary>
    public static void memfill(ushort addr, byte value, uint len) => throw null!;

    /// <summary>
    /// clear OAM buffer fast (hides all 64 sprites)
    /// </summary>
    public static void oam_clear_fast() => throw null!;

    /// <summary>
    /// Trigger OAM DMA from a 256-byte-aligned page in CPU RAM (writes page to $4014).
    /// Stock NMI already DMAs page $02; use this for custom OAM buffers / native renderer.
    /// </summary>
    public static void oam_dma(byte page) => throw null!;

    /// <summary>
    /// Read PPUSTATUS ($2002). Clears the vblank flag and resets the $2005/$2006 write toggle.
    /// </summary>
    public static byte ppu_status() => peek(PPU_STATUS);

    /// <summary>
    /// Wait until sprite 0 hit flag is clear, then until it is set (same wait as <see cref="split"/>).
    /// </summary>
    public static void sprite0_wait() => throw null!;

    /// <summary>
    /// True if sprite 0 hit flag is set (does not wait). Reads $2002 (clears vblank / latch).
    /// </summary>
    public static bool sprite0_hit() => throw null!;

    /// <summary>
    /// Write fine/coarse scroll registers ($2005 twice). Call during vblank.
    /// </summary>
    public static void ppu_scroll(byte x, byte y)
    {
        poke(PPU_SCROLL, x);
        poke(PPU_SCROLL, y);
    }

    /// <summary>
    /// set metasprite in OAM buffer with palette override
    /// palette bits (0-3) are OR'd into each sprite's attribute byte
    /// uses oam_off global for OAM buffer offset, updates it after writing
    /// </summary>
    public static void oam_meta_spr_pal(byte x, byte y, byte pal, byte[] data) => throw null!;

    /// <summary>
    /// set metasprite in OAM buffer with clipping
    /// </summary>
    public static void oam_meta_spr_clip(int x, byte y, byte[] metasprite) => throw null!;

    /// <summary>
    /// wait actual TV frame, 50hz for PAL, 60hz for NTSC
    /// </summary>
    public static void ppu_wait_nmi() => throw null!;

    /// <summary>
    /// write a byte value to an absolute memory address
    /// </summary>
    public static void poke(ushort addr, byte value) => throw null!;

    /// <summary>
    /// read a byte value from an absolute memory address
    /// </summary>
    public static byte peek(ushort addr) => throw null!;

    /// <summary>
    /// wait for vertical sync (vblank), busy-loops on PPU status bit 7
    /// </summary>
    public static void waitvsync() => throw null!;

    /// <summary>
    /// wait virtual frame, it is always 50hz, frame-to-frame in PAL, frameskip in NTSC
    /// </summary>
    public static void ppu_wait_frame() => throw null!;

    /// <summary>
    /// turn off rendering, nmi still enabled when rendering is disabled
    /// </summary>
    public static void ppu_off() => throw null!;

    /// <summary>
    /// turn on bg, spr
    /// </summary>
    public static void ppu_on_all() => throw null!;

    /// <summary>
    /// turn on bg only
    /// </summary>
    public static void ppu_on_bg() => throw null!;

    /// <summary>
    /// turn on spr only
    /// </summary>
    public static void ppu_on_spr() => throw null!;

    /// <summary>
    /// set PPU_MASK directly
    /// </summary>
    public static void ppu_mask(MASK mask) => throw null!;

    /// <summary>
    /// Get the current video system (PAL or NTSC).
    /// </summary>
    public static VideoSystem ppu_system() => throw null!;

    /// <summary>
    /// Return an 8-bit counter incremented at each vblank
    /// </summary>
    public static byte nesclock() => throw null!;

    /// <summary>
    /// get the internal ppu ctrl cache var for manual writing
    /// </summary>
    public static byte get_ppu_ctrl_var() => throw null!;

    /// <summary>
    /// set the internal ppu ctrl cache var for manual writing
    /// </summary>
    public static void set_ppu_ctrl_var(byte var) => throw null!;

    /// <summary>
    /// clear OAM buffer, all the sprites are hidden
    /// </summary>
    public static void oam_clear() => throw null!;

    /// <summary>
    /// OAM buffer offset, used by oam_meta_spr_pal
    /// </summary>
    public static byte oam_off { get; set; }

    /// <summary>
    /// set sprite display mode, 8x8 or 8x16 sprites
    /// </summary>
    public static void oam_size(SpriteSize size) => throw null!;

    /// <summary>
    /// set sprite in OAM buffer, chrnum is tile, attr is attribute, sprid is offset in OAM in bytes
    /// </summary>
    /// <returns>returns sprid+4, which is offset for a next sprite</returns>
    public static byte oam_spr(byte x, byte y, byte chrnum, byte attr, byte sprid) => throw null!;

    /// <summary>
    /// Draw a 2×2 (16×16 pixel) sprite from four 8×8 tiles.
    /// Writes 4 entries into the OAM buffer with standard 8-pixel offsets.
    /// Parameters: topLeft, bottomLeft, topRight, bottomRight to match NES tile layout convention.
    /// </summary>
    /// <returns>returns sprid+16, which is offset for the next sprite</returns>
    public static byte oam_spr_2x2(byte x, byte y, byte topLeft, byte bottomLeft, byte topRight, byte bottomRight, byte attr, byte sprid) => throw null!;

    /// <summary>
    /// poll controller and return enum like PAD.LEFT, etc.
    /// </summary>
    /// <param name="pad">pad number (0 or 1)</param>
    /// <returns>Enum like PAD.LEFT, etc.</returns>
    public static PAD pad_poll(byte pad) => throw null!;

    /// <summary>
    /// poll controller 1
    /// </summary>
    public static PAD pad1_poll() => pad_poll(0);

    /// <summary>
    /// buttons newly pressed this frame on controller 1
    /// </summary>
    public static PAD pad1_trigger() => pad_trigger(0);

    /// <summary>
    /// current held buttons on controller 1
    /// </summary>
    public static PAD pad1_state() => pad_state(0);

    /// <summary>
    /// set metasprite in OAM buffer
    /// meta sprite is a const unsigned char array, it contains four bytes per sprite
    /// in order x offset, y offset, tile, attribute
    /// x=128 is end of a meta sprite
    /// </summary>
    /// <returns>returns sprid+4, which is offset for a next sprite</returns>
    public static byte oam_meta_spr(byte x, byte y, byte sprid, byte[] data) => throw null!;

    /// <summary>
    /// Build a 2×2 (16×16 pixel) metasprite from four tile indices.
    /// Parameters: topLeft, bottomLeft, topRight, bottomRight to match NES OAM convention.
    /// Returns 17-byte metasprite array suitable for oam_meta_spr / oam_meta_spr_pal.
    /// </summary>
    public static byte[] meta_spr_2x2(byte topLeft, byte bottomLeft, byte topRight, byte bottomRight, byte attr = 0) => throw null!;

    /// <summary>
    /// Build a horizontally-flipped 2×2 metasprite (sets 0x40 flip bit, swaps L/R columns).
    /// Returns 17-byte metasprite array suitable for oam_meta_spr / oam_meta_spr_pal.
    /// </summary>
    public static byte[] meta_spr_2x2_flip(byte topLeft, byte bottomLeft, byte topRight, byte bottomRight, byte attr = 0) => throw null!;

    /// <summary>
    /// hide all remaining sprites from given offset
    /// </summary>
    public static void oam_hide_rest(byte sprid) => throw null!;

    /// <summary>
    /// set vram pointer to write operations if you need to write some data to vram
    /// </summary>
    public static void vram_adr(ushort adr) => throw null!;

    /// <summary>
    /// put a byte at current vram address, works only when rendering is turned off
    /// </summary>
    public static void vram_put(byte n) => throw null!;

    /// <summary>
    /// fill a block with a byte at current vram address, works only when rendering is turned off
    /// </summary>
    public static void vram_fill(byte n, uint len) => throw null!;

    /// <summary>
    /// Set VRAM auto-increment mode: <see cref="VramIncrement.By1"/> for +1 or <see cref="VramIncrement.By32"/> for +32
    /// </summary>
    public static void vram_inc(VramIncrement mode) => throw null!;

    /// <summary>
    /// write a block to current address of vram, works only when rendering is turned off
    /// </summary>
    public static void vram_write(string src) => throw null!;

    /// <summary>
    /// write a block to current address of vram, works only when rendering is turned off
    /// </summary>
    public static void vram_write(byte[] src) => throw null!;

    /// <summary>
    /// unpack RLE data to current address of vram, mostly used for nametables
    /// </summary>
    public static void vram_unrle(byte[] data) => throw null!;

    /// <summary>
    /// delay for N frames
    /// </summary>
    public static void delay(byte frames) => throw null!;

    /// <summary>
    /// fade in from black to normal brightness over N frames per step
    /// </summary>
    public static void fade_in(byte delay) => throw null!;

    /// <summary>
    /// fade out from normal brightness to black over N frames per step
    /// </summary>
    public static void fade_out(byte delay) => throw null!;

    #region nesdoug helpers

    // Based on Doug Fraker's nesdoug library:
    // https://github.com/mhughson/mbh-firstnes/blob/master/game/LIB/nesdoug.h
    // https://github.com/mhughson/mbh-firstnes/blob/master/game/LIB/nesdoug.s

    /// <summary>
    /// nesdoug: Append a single byte write to the VRAM update buffer.
    /// Schedules <paramref name="data"/> to be written to <paramref name="ppuAddress"/>
    /// during the next NMI.
    /// </summary>
    public static void one_vram_buffer(byte data, ushort ppuAddress) => throw null!;

    /// <summary>
    /// nesdoug: Append a horizontal sequence of <paramref name="len"/> bytes from
    /// <paramref name="data"/> to the VRAM update buffer, starting at <paramref name="ppuAddress"/>.
    /// </summary>
    public static void multi_vram_buffer_horz(byte[] data, byte len, ushort ppuAddress) => throw null!;

    /// <summary>
    /// nesdoug: Append a vertical sequence of <paramref name="len"/> bytes from
    /// <paramref name="data"/> to the VRAM update buffer, starting at <paramref name="ppuAddress"/>.
    /// </summary>
    public static void multi_vram_buffer_vert(byte[] data, byte len, ushort ppuAddress) => throw null!;

    /// <summary>
    /// nesdoug: Reset the VRAM update buffer to empty and write the EOF marker.
    /// </summary>
    public static void clear_vram_buffer() => vrambuf_clear();

    /// <summary>
    /// nesdoug: Returns buttons newly pressed this frame (same as <see cref="pad_trigger"/>).
    /// </summary>
    public static PAD get_pad_new(byte pad) => pad_trigger(pad);

    /// <summary>
    /// nesdoug: Returns the 8-bit frame counter (same as <see cref="nesclock"/>).
    /// </summary>
    public static byte get_frame_count() => nesclock();

    /// <summary>
    /// nesdoug: Set music playback speed/tempo.
    /// Note: dotnes uses its own music engine; this is currently a stub.
    /// </summary>
    public static void set_music_speed(byte tempo) => throw null!;

    /// <summary>
    /// nesdoug: Set horizontal scroll, including the high nametable bit.
    /// </summary>
    public static void set_scroll_x(ushort x) => throw null!;

    /// <summary>
    /// nesdoug: Set vertical scroll, including the high nametable bit.
    /// </summary>
    public static void set_scroll_y(ushort y) => throw null!;

    /// <summary>
    /// nesdoug: Convert pixel coordinates to a PPU nametable address.
    /// <paramref name="nt"/> is the nametable (0..3), <paramref name="x"/>/<paramref name="y"/>
    /// are pixel coordinates (0..255).
    /// </summary>
    public static ushort get_ppu_addr(byte nt, byte x, byte y) => throw null!;

    #endregion

    /// <summary>
    /// set scroll, including rhe top bits
    /// it is always applied at beginning of a TV frame, not at the function call
    /// </summary>
    public static void scroll(int x, int y) => throw null!;

    /// <summary>
    /// set scroll after screen split invoked by the sprite 0 hit
    /// warning: all CPU time between the function call and the actual split point will be wasted!
    /// warning: the program loop has to fit into the frame time, ppu_wait_frame should not be used
    ///          otherwise empty frames without split will be inserted, resulting in jumpy screen
    /// warning: only X scroll could be changed in this version
    /// </summary>
    public static void split(int x, int y) => throw null!;

    /// <summary>
    /// select current chr bank for sprites, 0..1
    /// </summary>
    public static void bank_spr(byte n) => throw null!;

    /// <summary>
    /// select current chr bank for background, 0..1
    /// </summary>
    public static void bank_bg(byte n) => throw null!;

    /// <summary>
    /// get random number 0..255, same as rand8()
    /// </summary>
    public static byte rand() => rand8();

    /// <summary>
    /// get random number 0..255
    /// </summary>
    public static byte rand8() => throw null!;

    /// <summary>
    /// get random number 0..32767, cc65-compatible 16-bit PRNG (32-bit LCG state)
    /// </summary>
    public static ushort rand16() => throw null!;

    /// <summary>
    /// set random seed for cc65-compatible PRNG, seed in A(lo)/X(hi)
    /// </summary>
    public static void srand(ushort seed) => throw null!;

    /// <summary>
    /// set random seed for 8-bit LFSR PRNG
    /// </summary>
    public static void set_rand(byte seed) => throw null!;

    /// <summary>
    /// Add two packed-BCD 16-bit numbers (software BCD since NES CPU lacks hardware BCD).
    /// Each nibble represents a decimal digit 0-9. Result is a 4-digit BCD value.
    /// Example: bcd_add(0x0100, 0x0001) returns 0x0101 (100 + 1 = 101).
    /// </summary>
    public static ushort bcd_add(ushort a, ushort b) => throw null!;

    /// <summary>
    /// Returns true if two axis-aligned rectangles overlap.
    /// Uses unsigned byte arithmetic and assumes rectangle extents do not wrap:
    /// x1+w1, x2+w2, y1+h1, and y2+h2 must all be less than or equal to 255.
    /// Under that precondition, AABB test: overlap iff
    /// (x1 &lt; x2+w2) &amp;&amp; (x2 &lt; x1+w1) &amp;&amp; (y1 &lt; y2+h2) &amp;&amp; (y2 &lt; y1+h1).
    /// </summary>
    public static bool rect_overlap(byte x1, byte y1, byte w1, byte h1, byte x2, byte y2, byte w2, byte h2) => throw null!;

    /// <summary>
    /// Returns true if two sprites overlap within a given threshold distance on both axes.
    /// Uses unsigned absolute-difference: overlap iff |x1-x2| &lt; threshold &amp;&amp; |y1-y2| &lt; threshold.
    /// Suitable for equal-size sprite collision checks (e.g., 8x8 sprites with threshold=8).
    /// </summary>
    public static bool sprite_overlap(byte x1, byte y1, byte x2, byte y2, byte threshold) => throw null!;

    /// <summary>
    /// when display is enabled, vram access could only be done with this vram update system
    /// the function sets a pointer to the update buffer that contains data and addresses
    /// in a special format. It allows to write non-sequental bytes, as well as horizontal or
    /// vertical nametable sequences.
    /// buffer pointer could be changed during rendering, but it only takes effect on a new frame
    /// number of transferred bytes is limited by vblank time
    /// to disable updates, call this function with NULL pointer
    ///
    /// the update data format:
    ///  MSB, LSB, byte for a non-sequental write
    ///  MSB|NT_UPD_HORZ, LSB, LEN, [bytes] for a horizontal sequence
    ///  MSB|NT_UPD_VERT, LSB, LEN, [bytes] for a vertical sequence
    ///  NT_UPD_EOF to mark end of the buffer
    ///
    /// length of this data should be under 256 bytes
    /// </summary>
    public static void set_vram_update(byte[] buf) => throw null!;

    /// <summary>
    /// set_vram_update with a raw address (e.g., 0x0100 for the stack-page update buffer)
    /// </summary>
    public static void set_vram_update(ushort addr) => throw null!;

    // all following vram functions only work when display is disabled

    /// <summary>
    /// do a series of VRAM writes, the same format as for set_vram_update, but writes done right away
    /// </summary>
    public static void flush_vram_update(byte[] buf) => throw null!;

    // VRAM update buffer constants

    /// <summary>
    /// Flag bit used in the VRAM update buffer to indicate a horizontal nametable update.
    /// </summary>
    /// <remarks>Represents a bit flag in the ROM's VRAM update buffer format. When set on an update entry,
    /// the entry is interpreted as a horizontal write operation and should be combined with the update address and data
    /// bytes to perform a horizontal name table update.</remarks>
    public const byte NT_UPD_HORZ = 0x40;

    /// <summary>
    /// Flag indicating a vertical nametable update.
    /// </summary>
    /// <remarks>Used as a bit mask in nametable update operations; combine with other NT_* flags as
    /// needed.</remarks>
    public const byte NT_UPD_VERT = 0x80;

    /// <summary>
    /// Marker value that signals the end of a nametable update sequence.
    /// </summary>
    /// <remarks>Used as a sentinel in nametable update streams; not a valid tile index and should not be used
    /// as data.</remarks>
    public const byte NT_UPD_EOF = 0xFF;

    /// <summary>
    /// VRAM update buffer address (stack page $0100)
    /// Pass to set_vram_update() to use the vrambuf module
    /// </summary>
    public const ushort updbuf = 0x0100;

    /// <summary>
    /// OR with address to select vertical sequential mode in vrambuf_put
    /// </summary>
    public const ushort VRAMBUF_VERT = 0x8000;

    /// <summary>
    /// clear the VRAM update buffer and write EOF marker
    /// </summary>
    public static void vrambuf_clear() => throw null!;

    /// <summary>
    /// write a horizontal string sequence to the VRAM update buffer
    /// the NMI handler will flush it to VRAM on the next frame
    /// </summary>
    public static void vrambuf_put(ushort addr, string str) => throw null!;

    /// <summary>
    /// write a horizontal byte array sequence to the VRAM update buffer
    /// the NMI handler will flush it to VRAM on the next frame
    /// </summary>
    public static void vrambuf_put(ushort addr, byte[] buf, byte len) => throw null!;

    /// <summary>
    /// write a vertical byte array sequence to the VRAM update buffer
    /// same as vrambuf_put but sets the vertical increment flag
    /// </summary>
    public static void vrambuf_put_vert(ushort addr, byte[] buf, byte len) => throw null!;

    /// <summary>
    /// write EOF marker at current buffer position (without advancing pointer)
    /// </summary>
    public static void vrambuf_end() => throw null!;

    /// <summary>
    /// write EOF, wait for NMI frame, then clear buffer
    /// </summary>
    public static void vrambuf_flush() => throw null!;

    // These are from: https://github.com/mhughson/attributes/blob/master/neslib.h

    /// <summary>
    /// Base PPU nametable address for nametable A (0x2000).
    /// </summary>
    /// <remarks>Represents the start address of nametable A in the NES PPU address space. Nametables occupy
    /// the $2000–$23FF range and the $2000–$2FFF region contains four nametable slots with mirroring behavior
    /// determined by cartridge wiring.</remarks>
    public const ushort NAMETABLE_A = 0x2000;
    /// <summary>
    /// Base PPU nametable address for nametable B (0x2400).
    /// </summary>
    /// <remarks>Represents the start address of nametable B in the NES PPU address space. Nametables occupy
    /// the $2400–$27FF range and the $2000–$2FFF region contains four nametable slots with mirroring behavior
    /// determined by cartridge wiring.</remarks>
    public const ushort NAMETABLE_B = 0x2400;
    /// <summary>
    /// Base PPU nametable address for nametable C (0x2800).
    /// </summary>
    /// <remarks>Represents the start address of nametable C in the NES PPU address space. Nametables occupy
    /// the $2800–$2BFF range and the $2000–$2FFF region contains four nametable slots with mirroring behavior
    /// determined by cartridge wiring.</remarks>
    public const ushort NAMETABLE_C = 0x2800;
    /// <summary>
    /// Base PPU nametable address for nametable D (0x2c00).
    /// </summary>
    /// <remarks>Represents the start address of nametable D in the NES PPU address space. Nametables occupy
    /// the $2c00–$2FFF range and the $2000–$2FFF region contains four nametable slots with mirroring behavior
    /// determined by cartridge wiring.</remarks>
    public const ushort NAMETABLE_D = 0x2c00;

    // PPU register addresses for direct hardware access via poke()/peek()

    /// <summary>
    /// Address of the NES PPU control register (PPUCTRL), used to configure core PPU options.
    /// </summary>
    /// <remarks>Access this register with poke() for direct hardware writes. Writes set bits
    /// such as NMI enable on VBlank, sprite/background pattern table selection, VRAM address increment mode, and base
    /// nametable. This register is write-only; reads are open-bus on real hardware.</remarks>
    public const ushort PPU_CTRL = 0x2000;

    /// <summary>
    /// The CPU memory-mapped address of the NES PPU mask register (PPUMASK).
    /// </summary>
    /// <remarks>Controls PPU rendering options such as grayscale, background and sprite visibility, and color
    /// emphasis. Accessed via the CPU memory map at $2001; reads are typically open-bus on real hardware.</remarks>
    public const ushort PPU_MASK = 0x2001;

    /// <summary>
    /// CPU address for the NES PPU status register (PPUSTATUS) at $2002.
    /// </summary>
    /// <remarks>Memory-mapped, read-only register that returns PPU status flags: VBlank (bit 7), Sprite 0 Hit
    /// (bit 6), Sprite Overflow (bit 5), with lower bits typically open-bus or unused. Reading this register clears the
    /// VBlank flag and resets the PPU address latch used by subsequent $2005/$2006 writes. PPU registers $2000–$2007
    /// are mirrored every $8 bytes through $3FFF.</remarks>
    public const ushort PPU_STATUS = 0x2002;

    /// <summary>PPU OAM address register ($2003).</summary>
    public const ushort PPU_OAM_ADDR = 0x2003;
    /// <summary>PPU OAM data register ($2004).</summary>
    public const ushort PPU_OAM_DATA = 0x2004;
    /// <summary>OAM DMA register ($4014). Write the high byte of a 256-byte page in CPU RAM.</summary>
    public const ushort PPU_OAM_DMA = 0x4014;

    /// <summary>
    /// NES PPU scroll register address ($2005). Writing to this register sets the background scroll position; the first
    /// write updates horizontal scroll (coarse X and fine X) and the second write updates vertical scroll (coarse Y and
    /// fine Y).
    /// </summary>
    /// <remarks>This register is write-only. The PPU maintains an internal write toggle that determines
    /// whether a write affects horizontal or vertical scroll; reading PPUSTATUS ($2002) resets that toggle. Update this
    /// register during VBlank or via NMI to avoid visible rendering artifacts.</remarks>
    public const ushort PPU_SCROLL = 0x2005;

    /// <summary>
    /// CPU address port for the NES Picture Processing Unit (PPU), corresponding to the PPUADDR register.
    /// </summary>
    /// <remarks>Set the PPU's VRAM address by writing to this register before accessing the PPU data port
    /// ($2007). Writes occur in two steps (high byte then low byte) using the internal address latch; writing updates
    /// the PPU's VRAM address and affects subsequent VRAM reads/writes. This register is generally used as a write-only
    /// memory-mapped I/O port on the NES.</remarks>
    public const ushort PPU_ADDR = 0x2006;

    /// <summary>
    /// Address of the NES PPU data register ($2007) used to read from and write to PPU VRAM via the CPU bus.
    /// </summary>
    /// <remarks>Reads from this register are buffered: the first read returns the internal buffer and then
    /// updates the buffer with the VRAM byte at the current VRAM address; subsequent reads return the buffered value.
    /// Writes store a byte to VRAM at the current VRAM address and then increment the VRAM address by 1 or 32 depending
    /// on the PPUCTRL address-increment flag (bit 2). The register is mirrored every eight bytes in the CPU $2000–$3FFF
    /// range.</remarks>
    public const ushort PPU_DATA = 0x2007;

    // APU register addresses for direct hardware access via poke()/peek()

    /// <summary>
    /// Address of the APU Pulse 1 control register (0x4000) for direct hardware access via poke()/peek().
    /// </summary>
    /// <remarks>Writing to this register configures the channel's duty, envelope/constant volume, and length
    /// counter halt/loop behavior. Bits 7-6 select duty, bit 5 is length counter halt/envelope loop, bit 4 is constant
    /// volume, and bits 3-0 specify envelope period or volume. This register is write-only.</remarks>
    public const ushort APU_PULSE1_CTRL = 0x4000;

    /// <summary>
    /// Address of the APU Pulse channel 1 sweep register.
    /// </summary>
    /// <remarks>Memory-mapped I/O register used to configure the pulse-1 sweep unit (enable, period, negate,
    /// and shift); writes modify the channel's frequency sweep behavior.</remarks>
    public const ushort APU_PULSE1_SWEEP = 0x4001;

    /// <summary>
    /// Low-byte register address for the NES APU Pulse channel 1 timer (register $4002).
    /// </summary>
    /// <remarks>Paired with the high-byte register at $4003 to form the channel's 11-bit timer value that
    /// controls the pulse waveform frequency. Write the low 8 bits here; combine with the high byte to update the
    /// period.</remarks>
    public const ushort APU_PULSE1_TIMER_LO = 0x4002;

    /// <summary>
    /// CPU address of the APU Pulse 1 high-timer and length-counter register ($4003).
    /// </summary>
    /// <remarks>Writes to this register set the high bits of the 11-bit timer for pulse channel 1 and load
    /// the length counter; typically written after the low timer byte to finalize the channel's frequency and
    /// duration.</remarks>
    public const ushort APU_PULSE1_TIMER_HI = 0x4003;

    /// <summary>
    /// Address of the NES APU Pulse 2 control register.
    /// </summary>
    /// <remarks>Write-only register controlling pulse channel 2. Bits 6-7 select the duty cycle, bit 5 is
    /// length-counter halt / envelope loop, bit 4 enables constant volume, and bits 0-3 specify envelope/volume. Used
    /// to configure duty, envelope, and length-counter behavior for pulse channel 2.</remarks>
    public const ushort APU_PULSE2_CTRL = 0x4004;

    /// <summary>
    /// Address of the APU pulse channel 2 sweep register.
    /// </summary>
    /// <remarks>Write-only hardware register that configures the sweep unit for pulse channel 2; writing to
    /// this address updates sweep parameters that alter the channel's frequency over time.</remarks>
    public const ushort APU_PULSE2_SWEEP = 0x4005;

    /// <summary>
    /// Low-byte address of the Pulse 2 timer register in the NES APU.
    /// </summary>
    /// <remarks>Write-only register; writing stores the low 8 bits of Pulse 2's timer period. The timer's
    /// high bits and the length counter are written to the corresponding high-byte register at 0x4007.</remarks>
    public const ushort APU_PULSE2_TIMER_LO = 0x4006;

    /// <summary>
    /// Memory-mapped APU register address for the Pulse 2 channel timer high and length counter.
    /// </summary>
    /// <remarks>Writing to this register sets the high bits of Pulse 2's 11-bit timer and loads the length
    /// counter (write-only in the NES APU).</remarks>
    public const ushort APU_PULSE2_TIMER_HI = 0x4007;

    /// <summary>
    /// Memory-mapped address of the NES APU triangle channel linear counter / length control register.
    /// </summary>
    /// <remarks>Bit 7 is the control/length-counter halt flag; bits 6–0 contain the 7-bit linear counter
    /// reload value. Writing to this address updates the triangle channel's linear counter reload and the
    /// length-counter halt flag.</remarks>
    public const ushort APU_TRIANGLE_CTRL = 0x4008;

    /// <summary>
    /// Address of the APU triangle channel timer low register (low 8 bits).
    /// </summary>
    /// <remarks>Write the low byte of the triangle channel timer/period. Combined with APU_TRIANGLE_TIMER_HI
    /// (0x400B) which supplies the high bits and length-counter control; updates affect the triangle waveform
    /// frequency.</remarks>
    public const ushort APU_TRIANGLE_TIMER_LO = 0x400A;

    /// <summary>
    /// Memory-mapped address of the APU triangle channel timer high / length-counter register.
    /// </summary>
    /// <remarks>Writes to this register provide the high bits of the triangle channel's frequency timer and,
    /// per NES APU behavior, also load the length counter and affect the linear counter/reload sequence.</remarks>
    public const ushort APU_TRIANGLE_TIMER_HI = 0x400B;

    /// <summary>
    /// Memory-mapped CPU address of the NES APU noise channel control register.
    /// </summary>
    /// <remarks>Writing to this register configures the noise channel's envelope/constant-volume and
    /// loop/length behaviour.</remarks>
    public const ushort APU_NOISE_CTRL = 0x400C;

    /// <summary>
    /// Memory-mapped CPU address of the NES APU noise channel period (timer) register.
    /// </summary>
    /// <remarks>Used to set the noise channel's period/timer via CPU memory-mapped I/O; writing a byte to
    /// this address updates the APU noise channel timing.</remarks>
    public const ushort APU_NOISE_PERIOD = 0x400E;

    /// <summary>
    /// CPU memory-mapped register address for the NES APU noise channel (0x400F).
    /// </summary>
    /// <remarks>Represents the APU noise channel's register used when writing length counter and related
    /// control data via the CPU memory map.</remarks>
    public const ushort APU_NOISE_LENGTH = 0x400F;

    /// <summary>
    /// CPU address of the NES APU status register ($4015).
    /// </summary>
    /// <remarks>Read to obtain audio channel and APU interrupt status; write to enable or disable individual
    /// APU channels and update status flags.</remarks>
    public const ushort APU_STATUS = 0x4015;

    /// <summary>APU DMC frequency / IRQ control ($4010).</summary>
    public const ushort APU_DMC_FREQ = 0x4010;
    /// <summary>APU DMC raw DAC ($4011).</summary>
    public const ushort APU_DMC_RAW = 0x4011;
    /// <summary>APU DMC sample start address ($4012).</summary>
    public const ushort APU_DMC_START = 0x4012;
    /// <summary>APU DMC sample length ($4013).</summary>
    public const ushort APU_DMC_LEN = 0x4013;
    /// <summary>APU frame counter ($4017).</summary>
    public const ushort APU_FRAME = 0x4017;

    /// <summary>Controller port 1 / expansion ($4016).</summary>
    public const ushort JOYPAD1 = 0x4016;
    /// <summary>Controller port 2 / expansion / zapper ($4017).</summary>
    public const ushort JOYPAD2 = 0x4017;

    /// <summary>Zapper light-sense bit on <see cref="JOYPAD2"/> (bit 3).</summary>
    public const byte ZAPPER_LIGHT = 0x08;
    /// <summary>Zapper trigger bit on <see cref="JOYPAD2"/> (bit 4).</summary>
    public const byte ZAPPER_TRIGGER = 0x10;

    /// <summary>True if the zapper senses light (reads <see cref="JOYPAD2"/>).</summary>
    public static bool zapper_light() => (peek(JOYPAD2) & ZAPPER_LIGHT) != 0;
    /// <summary>True if the zapper trigger is pulled (reads <see cref="JOYPAD2"/>).</summary>
    public static bool zapper_trigger() => (peek(JOYPAD2) & ZAPPER_TRIGGER) != 0;

    // Battery-backed SRAM address range ($6000-$7FFF) for use with peek()/poke()

    /// <summary>
    /// Starting address of the battery-backed SRAM region used by peek()/poke().
    /// </summary>
    /// <remarks>SRAM occupies the $6000–$7FFF range in the NES memory map; data written here is
    /// battery-backed and persists across power cycles. Use peek()/poke() to access offsets within this
    /// region.</remarks>
    public const ushort SRAM_START = 0x6000;

    /// <summary>
    /// The inclusive end address of the cartridge SRAM region in the NES CPU address space.
    /// </summary>
    /// <remarks>SRAM occupies the $6000–$7FFF range in the NES memory map; data written here is
    /// battery-backed and persists across power cycles. Use peek()/poke() to access offsets within this
    /// region.</remarks>
    public const ushort SRAM_END = 0x7FFF;


    // TODO: Macros below should be computed at compile-time and methods removed

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// <code>#define NTADR_A(x,y)	 	(NAMETABLE_A|(((y)&lt;&lt;5)|(x)))</code>
    /// </summary>
    public static ushort NTADR_A(byte x, byte y) => (ushort)(NAMETABLE_A | ((y << 5) | x));

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// <code>#define NTADR_B(x,y) 		(NAMETABLE_B|(((y)&lt;&lt;5)|(x)))</code>
    /// </summary>
    public static ushort NTADR_B(byte x, byte y) => (ushort)(NAMETABLE_B | ((y << 5) | x));

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// <code>#define NTADR_C(x,y) 		(NAMETABLE_C|(((y)&lt;&lt;5)|(x)))</code>
    /// </summary>
    public static ushort NTADR_C(byte x, byte y) => (ushort)(NAMETABLE_C | ((y << 5) | x));

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// <code>#define NTADR_D(x,y) 		(NAMETABLE_D|(((y)&lt;&lt;5)|(x)))</code>
    /// </summary>
    public static ushort NTADR_D(byte x, byte y) => (ushort)(NAMETABLE_D | ((y << 5) | x));

    /// <summary>Attribute table address for nametable A (tile coords 0..31).</summary>
    public static ushort NT_ATTR_A(byte x, byte y) => (ushort)(0x23C0 | ((y >> 2) << 3) | (x >> 2));
    /// <summary>Attribute table address for nametable B.</summary>
    public static ushort NT_ATTR_B(byte x, byte y) => (ushort)(0x27C0 | ((y >> 2) << 3) | (x >> 2));
    /// <summary>Attribute table address for nametable C.</summary>
    public static ushort NT_ATTR_C(byte x, byte y) => (ushort)(0x2BC0 | ((y >> 2) << 3) | (x >> 2));
    /// <summary>Attribute table address for nametable D.</summary>
    public static ushort NT_ATTR_D(byte x, byte y) => (ushort)(0x2FC0 | ((y >> 2) << 3) | (x >> 2));

    /// <summary>
    /// macro to get most significant byte in compile time
    /// <code>#define MSB(x)			(((x)>>8))</code>
    /// </summary>
    public static byte MSB(ushort x) => (byte)(x >> 8);

    /// <summary>
    /// macro to get least significant byte in compile time
    /// <code>#define LSB(x)			(((x)&amp;0xff))</code>
    /// </summary>
    public static byte LSB(ushort x) => (byte)(x & 0xff);

    /// <summary>
    /// NOTE: this one is internal, not in neslib.h
    /// </summary>
    internal static void pal_copy() => throw null!;

    /// <summary>
    /// NOTE: this one is internal, not in neslib.h
    /// </summary>
    internal static void ppu_onoff() => throw null!;

    // Tables are from: https://github.com/clbr/neslib/blob/master/neslib.sinc

    internal static readonly byte[] palBrightTableL =
    [
        /*
         * palBrightTableL:
         * .byte <palBrightTable0,<palBrightTable1,<palBrightTable2
         * .byte <palBrightTable3,<palBrightTable4,<palBrightTable5
         * .byte <palBrightTable6,<palBrightTable7,<palBrightTable8
         */
        0x34, 0x44, 0x54, 0x64, 0x74, 0x84, 0x94, 0xA4, 0xB4
    ];

    internal static readonly byte[] palBrightTableH =
    [
        0x84, 0x84, 0x84, 0x84, 0x84, 0x84, 0x84, 0x84, 0x84
    ];

    internal static readonly byte[] palBrightTable0 =
    [
        0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, //black
    ];
    internal static readonly byte[] palBrightTable1 =
    [
        0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F,
    ];
    internal static readonly byte[] palBrightTable2 =
    [
        0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F,
    ];
    internal static readonly byte[] palBrightTable3 =
    [
        0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F,
    ];
    internal static readonly byte[] palBrightTable4 =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0F, 0x0F, 0x0F, //normal colors
    ];
    internal static readonly byte[] palBrightTable5 =
    [
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x00, 0x00, 0x00,
    ];
    internal static readonly byte[] palBrightTable6 =
    [
        0x10, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x10, 0x10, 0x10, //$10 because $20 is the same as $30
    ];
    internal static readonly byte[] palBrightTable7 =
    [
        0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x20, 0x20, 0x20,
    ];
    internal static readonly byte[] palBrightTable8 =
    [
        0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, //white
        0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
        0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
        0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
    ];
}
