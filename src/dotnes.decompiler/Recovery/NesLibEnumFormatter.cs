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

        return FormatFlags(value, new (byte, string)[]
        {
            (0x01, "PAD.A"),
            (0x02, "PAD.B"),
            (0x04, "PAD.SELECT"),
            (0x08, "PAD.START"),
            (0x10, "PAD.UP"),
            (0x20, "PAD.DOWN"),
            (0x40, "PAD.LEFT"),
            (0x80, "PAD.RIGHT"),
        }, castPrefix: null);
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
