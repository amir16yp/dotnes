using System.Reflection;
using System.Text;
using NES;

namespace dotnes;

/// <summary>
/// Recovers NESLib enum / flags / named-constant expressions from raw byte values.
/// </summary>
static class NesLibEnumFormatter
{
    static readonly Dictionary<byte, string> NesColors = BuildNesColorMap();

    public static string FormatSpriteSize(byte value) => value switch
    {
        0 => "SpriteSize.Size8x8",
        1 => "SpriteSize.Size8x16",
        _ => $"(SpriteSize)0x{value:X2}",
    };

    public static string FormatVramIncrement(byte value) => value switch
    {
        0 => "VramIncrement.By1",
        1 => "VramIncrement.By32",
        _ => $"(VramIncrement)0x{value:X2}",
    };

    public static string FormatPulseChannel(byte value) => value switch
    {
        0 => "PulseChannel.Pulse1",
        1 => "PulseChannel.Pulse2",
        _ => $"(PulseChannel)0x{value:X2}",
    };

    public static string FormatApuDuty(byte value) => value switch
    {
        0 => "APUDuty.Duty12",
        1 => "APUDuty.Duty25",
        2 => "APUDuty.Duty50",
        3 => "APUDuty.Duty75",
        _ => $"(APUDuty)0x{value:X2}",
    };

    public static string FormatVideoSystem(byte value) => value switch
    {
        0x00 => "VideoSystem.PAL",
        0x80 => "VideoSystem.NTSC",
        _ => $"(VideoSystem)0x{value:X2}",
    };

    public static string FormatMmc1Mirror(byte value)
    {
        // Low 2 bits are the mirror mode; high bits may be OR'd mode flags.
        byte mirror = (byte)(value & 0x03);
        string mirrorName = mirror switch
        {
            0 => "MMC1Mirror.OneLower",
            1 => "MMC1Mirror.OneUpper",
            2 => "MMC1Mirror.Vertical",
            3 => "MMC1Mirror.Horizontal",
            _ => $"(MMC1Mirror)0x{mirror:X2}",
        };
        byte rest = (byte)(value & ~0x03);
        if (rest == 0)
            return $"(byte){mirrorName}";
        return $"(byte){mirrorName} | 0x{rest:X2}";
    }

    static readonly (byte Bit, string Name)[] PadFlags =
    [
        (0x01, "PAD.A"),
        (0x02, "PAD.B"),
        (0x04, "PAD.SELECT"),
        (0x08, "PAD.START"),
        (0x10, "PAD.UP"),
        (0x20, "PAD.DOWN"),
        (0x40, "PAD.LEFT"),
        (0x80, "PAD.RIGHT"),
    ];

    public static string FormatPad(byte value)
    {
        if (value == 0)
            return "0";

        // Prefer a single named button when possible
        string? single = value switch
        {
            0x01 => "PAD.A",
            0x02 => "PAD.B",
            0x04 => "PAD.SELECT",
            0x08 => "PAD.START",
            0x10 => "PAD.UP",
            0x20 => "PAD.DOWN",
            0x40 => "PAD.LEFT",
            0x80 => "PAD.RIGHT",
            _ => null,
        };
        if (single != null)
            return single;

        return FormatFlags(value, PadFlags, castPrefix: null);
    }

    /// <summary>
    /// True when every set bit is a <see cref="PAD"/> flag (no leftover bits).
    /// </summary>
    public static bool IsExactPadFlags(byte value)
    {
        if (value == 0)
            return false;
        byte remaining = value;
        foreach (var (bit, _) in PadFlags)
        {
            if ((value & bit) != 0)
                remaining = (byte)(remaining & ~bit);
        }
        return remaining == 0;
    }

    /// <summary>
    /// Formats a pad mask/compare/AND operand as a byte expression:
    /// <c>(byte)PAD.UP</c>, <c>(byte)(PAD.A | PAD.B)</c>, or <c>(byte)~PAD.DOWN</c> for clear-masks.
    /// </summary>
    public static bool TryFormatPadByteOperand(byte value, out string expression)
    {
        expression = null!;
        if (value == 0)
            return false;

        // Clear-mask: AND #$DF clears DOWN → (byte)~PAD.DOWN
        byte cleared = (byte)~value;
        if (cleared != 0 && IsExactPadFlags(cleared) && BitCount(cleared) <= 2)
        {
            string pad = FormatPad(cleared);
            expression = pad.Contains('|', StringComparison.Ordinal)
                ? $"(byte)~({pad})"
                : $"(byte)~{pad}";
            return true;
        }

        if (!IsExactPadFlags(value))
            return false;

        // Avoid turning common non-input masks into buttons without caller context:
        // caller must gate on pad-typed / pad-ZP. Multi-bit face/nibble masks are fine there.
        string flags = FormatPad(value);
        expression = flags.Contains('|', StringComparison.Ordinal)
            ? $"(byte)({flags})"
            : $"(byte){flags}";
        return true;
    }

    static int BitCount(byte value)
    {
        int n = 0;
        while (value != 0)
        {
            n += value & 1;
            value >>= 1;
        }
        return n;
    }

    public static string FormatMask(byte value)
    {
        if (value == 0)
            return "0";

        return FormatFlags(value, new (byte, string)[]
        {
            (0x01, "MASK.MONO"),
            (0x02, "MASK.EDGE_BG"),
            (0x04, "MASK.EDGE_SPR"),
            (0x08, "MASK.BG"),
            (0x10, "MASK.SPR"),
            (0x20, "MASK.TINT_RED"),
            (0x40, "MASK.TINT_GREEN"),
            (0x80, "MASK.TINT_BLUE"),
        }, castPrefix: null);
    }

    public static string FormatPpuCtrl(byte value)
    {
        if (value == 0)
            return "0";

        return FormatFlags(value, new (byte, string)[]
        {
            (0x01, "PPUCTRL.NAMETABLE_X"),
            (0x02, "PPUCTRL.NAMETABLE_Y"),
            (0x04, "PPUCTRL.VRAM_INC_32"),
            (0x08, "PPUCTRL.SPR_TABLE_1000"),
            (0x10, "PPUCTRL.BG_TABLE_1000"),
            (0x20, "PPUCTRL.SPR_SIZE_8X16"),
            (0x40, "PPUCTRL.MASTER_SLAVE"),
            (0x80, "PPUCTRL.NMI"),
        }, castPrefix: null);
    }

    public static string FormatApuStatus(byte value)
    {
        if (value == 0)
            return "0";

        return FormatFlags(value, new (byte, string)[]
        {
            (0x01, "APUSTATUS.PULSE1"),
            (0x02, "APUSTATUS.PULSE2"),
            (0x04, "APUSTATUS.TRIANGLE"),
            (0x08, "APUSTATUS.NOISE"),
            (0x10, "APUSTATUS.DMC"),
        }, castPrefix: null);
    }

    public static string FormatApuFrame(byte value)
    {
        if (value == 0)
            return "0";

        return FormatFlags(value, new (byte, string)[]
        {
            (0x40, "APUFRAME.IRQ_INHIBIT"),
            (0x80, "APUFRAME.MODE_5STEP"),
        }, castPrefix: null);
    }

    /// <summary>
    /// Sprite attribute byte: palette 0-3 plus optional OAM.FLIP_*/BEHIND flags.
    /// </summary>
    public static string FormatOamAttr(byte value)
    {
        byte palette = (byte)(value & 0x03);
        byte flags = (byte)(value & 0xE0);
        byte unknown = (byte)(value & 0x1C);

        if (flags == 0 && unknown == 0)
            return palette.ToString();

        var parts = new List<string>();
        if (palette != 0)
            parts.Add(palette.ToString());
        if ((flags & OAM.FLIP_V) != 0) parts.Add("OAM.FLIP_V");
        if ((flags & OAM.FLIP_H) != 0) parts.Add("OAM.FLIP_H");
        if ((flags & OAM.BEHIND) != 0) parts.Add("OAM.BEHIND");
        if (unknown != 0)
            parts.Add($"0x{unknown:X2}");

        return parts.Count == 0 ? "0" : string.Join(" | ", parts);
    }

    public static string FormatNesColor(byte value)
    {
        if (NesColors.TryGetValue(value, out string? name))
            return name;
        return value <= 9 ? value.ToString() : $"0x{value:X2}";
    }

    public static string FormatByteOrEnum(string? enumKind, byte value) => enumKind switch
    {
        "SpriteSize" => FormatSpriteSize(value),
        "VramIncrement" => FormatVramIncrement(value),
        "PulseChannel" => FormatPulseChannel(value),
        "APUDuty" => FormatApuDuty(value),
        "MASK" => FormatMask(value),
        "PPUCTRL" => FormatPpuCtrl(value),
        "APUSTATUS" => FormatApuStatus(value),
        "APUFRAME" => FormatApuFrame(value),
        "PAD" => FormatPad(value),
        "VideoSystem" => FormatVideoSystem(value),
        "MMC1Mirror" => FormatMmc1Mirror(value),
        "NESColor" => FormatNesColor(value),
        "OamAttr" => FormatOamAttr(value),
        _ => value <= 9 ? value.ToString() : $"0x{value:X2}",
    };

    static string FormatFlags(byte value, (byte Bit, string Name)[] flags, string? castPrefix)
    {
        var parts = new List<string>();
        byte remaining = value;
        foreach (var (bit, name) in flags)
        {
            if ((value & bit) != 0)
            {
                parts.Add(name);
                remaining = (byte)(remaining & ~bit);
            }
        }

        if (remaining != 0)
            parts.Add($"0x{remaining:X2}");

        if (parts.Count == 0)
            return "0";

        string joined = string.Join(" | ", parts);
        return castPrefix != null ? $"({castPrefix})({joined})" : joined;
    }

    static Dictionary<byte, string> BuildNesColorMap()
    {
        var map = new Dictionary<byte, string>();
        foreach (FieldInfo field in typeof(NESColor).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(byte))
                continue;
            byte value = (byte)field.GetValue(null)!;
            // Prefer first name if duplicates (shouldn't happen for usable colors)
            map.TryAdd(value, $"NESColor.{field.Name}");
        }
        return map;
    }
}
