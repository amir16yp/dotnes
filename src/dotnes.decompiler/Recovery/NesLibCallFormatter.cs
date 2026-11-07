namespace dotnes;

/// <summary>
/// Formats recognized NESLib subroutine calls into C# source.
/// Covers the common calling conventions used by the transpiler:
/// A-only, pusha+A, X:A word, and multi-push + A.
/// </summary>
static class NesLibCallFormatter
{
    static readonly HashSet<string> InternalRuntime = new(StringComparer.Ordinal)
    {
        "pusha", "pushax", "popa", "popax",
        "incsp1", "incsp2", "addysp", "decsp4",
        "zerobss", "copydata", "initlib", "donelib",
        "ppu_onoff", "pal_copy",
        "nmi_set_callback", "irq_set_callback", "irq_with_callback",
        "_nmi", "_irq", "_exit", "_initPPU", "_clearPalette", "_clearVRAM",
        "_clearRAM", "_waitSync3", "detectNTSC", "doUpdate", "updPal", "updVRAM",
        "skipUpd", "skipAll", "skipNtsc",
    };

    public static bool IsInternalRuntime(string name) => InternalRuntime.Contains(name);

    /// <summary>
    /// A holds last arg; optional prior pusha value is first arg.
    /// </summary>
    public static string? FormatCall(string name, byte? aValue, byte? pushedArg = null)
    {
        if (IsInternalRuntime(name))
            return null;

        string a = aValue.HasValue ? FormatByte(aValue.Value) : "0";
        string? p = pushedArg.HasValue ? FormatByte(pushedArg.Value) : null;

        return name switch
        {
            // zero-arg
            "ppu_on_all" or "ppu_on_bg" or "ppu_on_spr" or "ppu_off"
                or "ppu_wait_nmi" or "ppu_wait_frame" or "waitvsync"
                or "oam_clear" or "oam_clear_fast" or "pal_clear"
                or "nesclock" or "get_frame_count" or "apu_init" or "apu_stop_all"
                or "apu_stop_triangle" or "apu_stop_noise"
                or "music_stop" or "music_tick" or "vrambuf_clear" or "vrambuf_end"
                or "vrambuf_flush" or "clear_vram_buffer" or "sprite0_wait"
                or "cli" or "sei" or "nop"
                => $"{CanonicalName(name)}();",

            // return-in-A zero-arg
            "rand8" or "rand" => "rand8();",
            "rand16" => "rand16();",
            "ppu_system" => "ppu_system();",
            "get_ppu_ctrl_var" => "get_ppu_ctrl_var();",
            "ppu_status" => "ppu_status();",

            // single byte in A
            "pal_bright" or "pal_spr_bright" or "pal_bg_bright"
                or "oam_hide_rest" or "vram_put" or "bank_spr" or "bank_bg"
                or "delay" or "set_rand" or "pad_poll" or "pad_trigger" or "pad_state"
                or "get_pad_new" or "pad1_poll" or "fade_in" or "fade_out"
                or "set_ppu_ctrl_var" or "music_play" or "sample_play"
                or "cnrom_set_chr_bank" or "uxrom_set_prg_bank" or "axrom_set_prg_bank"
                or "bnrom_set_prg_bank" or "gxrom_set" or "colordreams_set"
                or "cprom_set_chr_bank" or "unrom512_set_prg_bank" or "camerica_set_prg_bank"
                or "holydiver_set" or "nina_set" or "nina113_set" or "jaleco_set_chr_bank"
                or "mapper15_set" or "bandai_set_prg_bank" or "cnrom185_set_chr_bank"
                or "action53_set" or "vrc3_set_prg_bank" or "vrc6_set_prg16" or "vrc6_set_prg8"
                or "vrc1_set_mirroring" or "vrc24_set_mirroring"
                or "mmc2_set_prg_bank" or "mmc4_set_prg_bank"
                or "mmc2_set_mirroring" or "mmc4_set_mirroring"
                or "mmc5_set_prg_mode" or "mmc5_set_chr_mode"
                or "oam_dma" or "set_music_speed"
                => $"{CanonicalName(name)}({a});",

            "oam_size" => $"oam_size({(aValue.HasValue ? NesLibEnumFormatter.FormatSpriteSize(aValue.Value) : "SpriteSize.Size8x8")});",
            "vram_inc" => $"vram_inc({(aValue.HasValue ? NesLibEnumFormatter.FormatVramIncrement(aValue.Value) : "VramIncrement.By1")});",
            "ppu_mask" => $"ppu_mask({(aValue.HasValue ? NesLibEnumFormatter.FormatMask(aValue.Value) : "0")});",
            "ppu_mask_write" => $"ppu_mask_write({(aValue.HasValue ? NesLibEnumFormatter.FormatMask(aValue.Value) : "0")});",
            "ppu_ctrl" => $"ppu_ctrl({(aValue.HasValue ? NesLibEnumFormatter.FormatPpuCtrl(aValue.Value) : "0")});",
            "apu_frame" => $"apu_frame({(aValue.HasValue ? NesLibEnumFormatter.FormatApuFrame(aValue.Value) : "0")});",
            "apu_status_write" => $"apu_status_write({(aValue.HasValue ? NesLibEnumFormatter.FormatApuStatus(aValue.Value) : "0")});",
            "joypad1_write" => $"joypad1_write({(aValue.HasValue ? NesLibEnumFormatter.FormatJoyStrobe(aValue.Value) : "JOYSTROBE.Shift")});",
            "music_pause" => $"music_pause({((aValue ?? 0) != 0).ToString().ToLowerInvariant()});",
            "apu_stop" => $"apu_stop({(aValue.HasValue ? NesLibEnumFormatter.FormatPulseChannel(aValue.Value) : "PulseChannel.Pulse1")});",
            "mmc1_set_mirroring" => $"mmc1_set_mirroring({(aValue.HasValue ? NesLibEnumFormatter.FormatMmc1Mirror(aValue.Value) : "0")});",

            // two-arg: pusha then A
            "pal_col" => $"pal_col({p ?? "0"}, {(aValue.HasValue ? NesLibEnumFormatter.FormatNesColor(aValue.Value) : "0")});",
            "pal_bg_col" => $"pal_bg_col({p ?? "0"}, {(aValue.HasValue ? NesLibEnumFormatter.FormatNesColor(aValue.Value) : "0")});",
            "pal_spr_col" => $"pal_spr_col({p ?? "0"}, {(aValue.HasValue ? NesLibEnumFormatter.FormatNesColor(aValue.Value) : "0")});",
            "sfx_play" => $"sfx_play({p ?? "0"}, {a});",
            "pad_pressed" when pushedArg.HasValue && aValue.HasValue =>
                $"pad_pressed(/* joy */, {NesLibEnumFormatter.FormatPad(aValue.Value)});",

            "vram_adr" when aValue.HasValue && pushedArg is null =>
                $"vram_adr(0x{aValue.Value:X4});",

            _ => null, // let caller try other conventions / fallback
        };
    }

    /// <summary>
    /// Multi-arg via software stack pushes + optional A.
    /// </summary>
    public static string? FormatCallN(string name, string? aArg, List<string> pushedArgs)
    {
        if (IsInternalRuntime(name))
            return null;

        string[] args = pushedArgs.Concat(aArg != null ? new[] { aArg } : Array.Empty<string>()).ToArray();
        string Join() => string.Join(", ", args.Select(FormatOamArg));

        return name switch
        {
            "oam_spr" when pushedArgs.Count == 4 && aArg != null =>
                FormatOamSpr(pushedArgs[0], pushedArgs[1], pushedArgs[2], pushedArgs[3], aArg),

            "oam_spr_2x2" when args.Length == 8 =>
                $"oam_spr_2x2({FormatOamArg(args[0])}, {FormatOamArg(args[1])}, {FormatOamArg(args[2])}, {FormatOamArg(args[3])}, {FormatOamArg(args[4])}, {FormatOamArg(args[5])}, {FormatOamAttrArg(args[6])}, {FormatOamArg(args[7])});",

            "oam_meta_spr" when args.Length >= 3 =>
                $"oam_meta_spr({Join()});",

            "oam_meta_spr_pal" when args.Length >= 3 =>
                $"oam_meta_spr_pal({Join()});",

            "oam_meta_spr_clip" when args.Length >= 3 =>
                $"oam_meta_spr_clip({Join()});",

            "rect_overlap" when args.Length == 8 =>
                $"rect_overlap({Join()});",

            "sprite_overlap" when args.Length == 5 =>
                $"sprite_overlap({Join()});",

            "one_vram_buffer" when args.Length == 2 =>
                $"one_vram_buffer({FormatOamArg(args[0])}, {FormatWordish(args[1])});",

            "multi_vram_buffer_horz" or "multi_vram_buffer_vert" when args.Length >= 3 =>
                $"{name}({Join()});",

            "vrambuf_put" when args.Length >= 2 =>
                $"vrambuf_put({Join()});",

            "vrambuf_put_vert" when args.Length >= 2 =>
                $"vrambuf_put_vert({Join()});",

            "apu_play_tone" when args.Length >= 4 =>
                $"apu_play_tone({FormatPulseChannelArg(args[0])}, {FormatWordish(args[1])}, {FormatApuDutyArg(args[2])}, {FormatOamArg(args[3])});",

            "apu_play_triangle" when args.Length >= 2 =>
                $"apu_play_triangle({FormatWordish(args[0])}, {FormatOamArg(args[1])});",

            "apu_play_noise" when args.Length >= 3 =>
                $"apu_play_noise({FormatOamArg(args[0])}, {FormatOamArg(args[1])}, {FormatBoolish(args[2])});",

            "apu_stop" when args.Length >= 1 =>
                $"apu_stop({FormatPulseChannelArg(args[0])});",

            "pad_pressed" when args.Length >= 2 =>
                $"pad_pressed({args[0]}, {FormatPadArg(args[1])});",

            "oam_size" when args.Length >= 1 =>
                $"oam_size({FormatSpriteSizeArg(args[0])});",

            "vram_inc" when args.Length >= 1 =>
                $"vram_inc({FormatVramIncrementArg(args[0])});",

            "ppu_mask" when args.Length >= 1 =>
                $"ppu_mask({FormatMaskArg(args[0])});",

            "ppu_mask_write" when args.Length >= 1 =>
                $"ppu_mask_write({FormatMaskArg(args[0])});",

            "ppu_ctrl" when args.Length >= 1 =>
                $"ppu_ctrl({FormatPpuCtrlArg(args[0])});",

            "apu_frame" when args.Length >= 1 =>
                $"apu_frame({FormatApuFrameArg(args[0])});",

            "apu_status_write" when args.Length >= 1 =>
                $"apu_status_write({FormatApuStatusArg(args[0])});",

            "joypad1_write" when args.Length >= 1 =>
                $"joypad1_write({FormatJoyStrobeArg(args[0])});",

            "mmc1_set_mirroring" when args.Length >= 1 =>
                $"mmc1_set_mirroring({FormatMmc1MirrorArg(args[0])});",

            "pal_col" or "pal_bg_col" or "pal_spr_col" when args.Length == 2 =>
                $"{name}({FormatOamArg(args[0])}, {FormatNesColorArg(args[1])});",

            "pad_poll" or "pad_trigger" or "pad_state" or "oam_hide_rest"
                or "delay" or "set_rand" or "fade_in" or "fade_out"
                or "bank_spr" or "bank_bg" or "cnrom_set_chr_bank"
                or "uxrom_set_prg_bank" or "axrom_set_prg_bank" or "bnrom_set_prg_bank"
                or "gxrom_set" or "colordreams_set" or "cprom_set_chr_bank"
                or "unrom512_set_prg_bank" or "camerica_set_prg_bank" or "holydiver_set"
                or "nina_set" or "nina113_set" or "jaleco_set_chr_bank"
                or "mapper15_set" or "bandai_set_prg_bank" or "cnrom185_set_chr_bank"
                or "action53_set" or "vrc3_set_prg_bank" or "vrc6_set_prg16" or "vrc6_set_prg8"
                or "vrc1_set_mirroring" or "vrc24_set_mirroring"
                or "mmc2_set_prg_bank" or "mmc4_set_prg_bank"
                or "mmc2_set_mirroring" or "mmc4_set_mirroring"
                or "mmc5_set_prg_mode" or "mmc5_set_chr_mode"
                => $"{name}({aArg ?? (pushedArgs.Count > 0 ? pushedArgs[^1] : "0")});",

            "gxrom_set_banks" or "colordreams_set_banks" or "nina_set_banks"
                or "nina113_set_banks"
                or "mmc2_set_chr_bank0" or "mmc2_set_chr_bank1"
                or "mmc4_set_chr_bank0" or "mmc4_set_chr_bank1"
                or "mmc5_set_prg_bank" or "mmc5_set_chr_bank"
                or "fme7_write" or "fme7_set_prg_bank" or "fme7_set_chr_bank"
                or "vrc1_set_prg_bank" or "vrc1_set_chr_bank" or "vrc24_set_prg_bank"
                or "vrc7_set_prg_bank" or "namco163_set_prg_bank" or "action52_write"
                when args.Length >= 2 =>
                $"{name}({FormatOamArg(args[0])}, {FormatOamArg(args[1])});",

            "sfx_play" when args.Length == 2 =>
                $"sfx_play({FormatOamArg(args[0])}, {FormatOamArg(args[1])});",

            _ when args.Length == 0 =>
                FormatCall(name, null, null),

            _ when args.Length == 1 && byte.TryParse(args[0], out var b) =>
                FormatCall(name, b, null),

            _ => null,
        };
    }

    static string FormatOamSpr(string x, string y, string tile, string attr, string sprid)
    {
        string call = $"oam_spr({FormatOamArg(x)}, {FormatOamArg(y)}, {FormatOamArg(tile)}, {FormatOamAttrArg(attr)}, {FormatOamArg(sprid)})";
        return sprid.StartsWith("var_", StringComparison.Ordinal) ? $"{sprid} = {call};" : $"{call};";
    }

    static string FormatPulseChannelArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatPulseChannel(b) : arg;

    static string FormatApuDutyArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatApuDuty(b) : arg;

    static string FormatSpriteSizeArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatSpriteSize(b) : arg;

    static string FormatVramIncrementArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatVramIncrement(b) : arg;

    static string FormatMaskArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatMask(b) : arg;

    static string FormatPpuCtrlArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatPpuCtrl(b) : arg;

    static string FormatApuStatusArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatApuStatus(b) : arg;

    static string FormatApuFrameArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatApuFrame(b) : arg;

    static string FormatJoyStrobeArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatJoyStrobe(b) : arg;

    static string FormatPadArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatPad(b) : arg;

    static string FormatMmc1MirrorArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatMmc1Mirror(b) : arg;

    static string FormatNesColorArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatNesColor(b) : arg;

    static string FormatOamAttrArg(string arg) =>
        TryParseByte(arg, out byte b) ? NesLibEnumFormatter.FormatOamAttr(b) : arg;

    static string FormatBoolish(string arg) =>
        TryParseByte(arg, out byte b) ? (b != 0).ToString().ToLowerInvariant() : arg;

    static bool TryParseByte(string arg, out byte b)
    {
        if (byte.TryParse(arg, out b))
            return true;
        if (arg.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && byte.TryParse(arg.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out b))
            return true;
        b = 0;
        return false;
    }

    /// <summary>
    /// X:A hold a 16-bit value; optional pushed pointer/byte for length-style APIs.
    /// </summary>
    public static List<string>? FormatCallXA(
        string name,
        byte xValue,
        byte aValue,
        ushort? pushedPtr,
        byte? pushedByte,
        Func<string, ushort, ushort?, List<string>>? formatPointerPayload)
    {
        if (IsInternalRuntime(name))
            return new List<string>();

        ushort value16 = (ushort)((xValue << 8) | aValue);

        return name switch
        {
            "vram_adr" => new List<string> { FormatVramAdr(aValue, xValue) },
            "vram_write" => formatPointerPayload?.Invoke("vram_write", value16, pushedPtr)
                ?? new List<string> { $"vram_write(/* {value16} bytes */);" },
            "vram_read" => formatPointerPayload?.Invoke("vram_read", value16, pushedPtr)
                ?? new List<string> { $"vram_read(/* {value16} bytes */);" },
            "vram_unrle" => formatPointerPayload?.Invoke("vram_unrle", value16, pushedPtr)
                ?? new List<string> { $"vram_unrle(/* ptr 0x{value16:X4} */);" },
            "pal_bg" or "pal_spr" or "pal_all" =>
                formatPointerPayload?.Invoke(name, value16, value16)
                ?? new List<string> { $"{name}(/* 0x{value16:X4} */);" },
            "vram_fill" => new List<string>
            {
                pushedByte.HasValue
                    ? $"vram_fill(0x{pushedByte.Value:X2}, {value16});"
                    : $"// vram_fill(?, {value16}); // fill value not recovered",
            },
            "scroll" or "ppu_scroll" or "split" =>
                new List<string> { $"{(name == "ppu_scroll" ? "scroll" : name)}({aValue}, {xValue});" },
            "set_scroll_x" => new List<string> { $"set_scroll_x(0x{value16:X4});" },
            "set_scroll_y" => new List<string> { $"set_scroll_y(0x{value16:X4});" },
            "srand" => new List<string> { $"srand(0x{value16:X4});" },
            "set_vram_update" => new List<string>
            {
                value16 == 0x0100
                    ? "set_vram_update(updbuf);"
                    : $"set_vram_update(0x{value16:X4});",
            },
            "flush_vram_update" => new List<string> { $"flush_vram_update(0x{value16:X4});" },
            "memfill" when pushedByte.HasValue =>
                new List<string> { $"memfill(0x{value16:X4}, 0x{pushedByte.Value:X2}, /* len */);" },
            "start_music" => formatPointerPayload?.Invoke("start_music", value16, pushedPtr)
                ?? new List<string> { $"start_music(/* 0x{value16:X4} */);" },
            "set_music_pulse_table" or "set_music_triangle_table" =>
                formatPointerPayload?.Invoke(name, value16, pushedPtr)
                ?? new List<string> { $"{name}(/* 0x{value16:X4} */);" },
            "bcd_add" when pushedPtr.HasValue =>
                new List<string> { $"bcd_add(0x{pushedPtr.Value:X4}, 0x{value16:X4});" },
            "bcd_add" => new List<string> { $"bcd_add(/* ?, 0x{value16:X4} */);" },
            _ => null,
        };
    }

    public static string FormatOamArg(string arg) =>
        byte.TryParse(arg, out var b) ? FormatByte(b) : arg;

    public static string FormatByte(byte b) => b <= 9 ? b.ToString() : $"0x{b:X2}";

    public static string FormatVramAdr(byte lo, byte hi)
    {
        ushort addr = (ushort)((hi << 8) | lo);
        if (addr >= 0x2000 && addr < 0x2400)
        {
            int offset = addr - 0x2000;
            return $"vram_adr(NTADR_A({offset % 32}, {offset / 32}));";
        }
        if (addr >= 0x2400 && addr < 0x2800)
        {
            int offset = addr - 0x2400;
            return $"vram_adr(NTADR_B({offset % 32}, {offset / 32}));";
        }
        if (addr >= 0x2800 && addr < 0x2C00)
        {
            int offset = addr - 0x2800;
            return $"vram_adr(NTADR_C({offset % 32}, {offset / 32}));";
        }
        if (addr >= 0x2C00 && addr < 0x3000)
        {
            int offset = addr - 0x2C00;
            return $"vram_adr(NTADR_D({offset % 32}, {offset / 32}));";
        }
        return $"vram_adr(0x{addr:X4});";
    }

    /// <summary>
    /// SoftCpu-friendly text/tile run: printable ASCII → string; else <c>new byte[] { … }</c>
    /// with a comment naming the ASCII string form when every byte is a printable character.
    /// </summary>
    public static string FormatVramWriteFromBytes(IReadOnlyList<byte> bytes)
    {
        if (bytes.Count == 0)
            return "vram_write(\"\");";

        bool allPrintable = true;
        for (int i = 0; i < bytes.Count; i++)
        {
            byte b = bytes[i];
            if (b < 0x20 || b >= 0x7F)
            {
                allPrintable = false;
                break;
            }
        }

        string asciiLiteral = EscapeAsciiString(bytes);

        if (allPrintable)
            return $"vram_write(\"{asciiLiteral}\");";

        var parts = new string[bytes.Count];
        for (int i = 0; i < bytes.Count; i++)
            parts[i] = FormatByte(bytes[i]);

        string bytesExpr = $"new byte[] {{ {string.Join(", ", parts)} }}";

        // Only annotate when a tile font decodes to confident English; otherwise leave as byte[].
        if (GameTextTableRecovery.TryDecodeTiles(bytes, out string tiled, out _))
            return $"vram_write({bytesExpr}); // can be replaced with \"{tiled}\"";

        return $"vram_write({bytesExpr});";
    }

    /// <summary>Build a C# string literal body: printable as-is, else <c>\xHH</c>.</summary>
    public static string EscapeAsciiString(IReadOnlyList<byte> bytes)
    {
        var sb = new System.Text.StringBuilder(bytes.Count * 4);
        for (int i = 0; i < bytes.Count; i++)
        {
            byte b = bytes[i];
            switch (b)
            {
                case (byte)'\\': sb.Append("\\\\"); break;
                case (byte)'"': sb.Append("\\\""); break;
                case (byte)'\n': sb.Append("\\n"); break;
                case (byte)'\r': sb.Append("\\r"); break;
                case (byte)'\t': sb.Append("\\t"); break;
                default:
                    if (b >= 0x20 && b < 0x7F)
                        sb.Append((char)b);
                    else
                        sb.Append("\\x").Append(b.ToString("X2"));
                    break;
            }
        }
        return sb.ToString();
    }

    static string CanonicalName(string name) => name switch
    {
        "get_frame_count" => "nesclock",
        "clear_vram_buffer" => "vrambuf_clear",
        "get_pad_new" => "pad_trigger",
        "pad1_poll" => "pad_poll",
        "rand" => "rand8",
        _ => name,
    };

    static string FormatWordish(string arg) =>
        ushort.TryParse(arg, out var w) ? (w <= 9 ? w.ToString() : $"0x{w:X4}") : arg;
}
