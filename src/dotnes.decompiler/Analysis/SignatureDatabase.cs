using dotnes.ObjectModel;

namespace dotnes.Analysis;

/// <summary>
/// Byte signatures for neslib/cc65 helpers found after main.
/// Includes hand-written patterns plus prefixes extracted from <see cref="BuiltInSubroutines"/>.
/// </summary>
static class SignatureDatabase
{
    public static readonly (string Name, byte[] Signature)[] FinalBuiltIns = Build();

    static (string Name, byte[] Signature)[] Build()
    {
        var byName = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        void Add(string name, byte[] sig)
        {
            if (sig.Length < 3)
                return;
            byName.TryAdd(name, sig);
        }

        // Hand-tuned short signatures (stable entry prefixes)
        Add("pusha", [0xA4, 0x22, 0xF0]);
        Add("pushax", [0x48, 0xA5, 0x22, 0x38, 0xE9, 0x02]);
        Add("popa", [0xA0, 0x00, 0xB1, 0x22, 0xE6, 0x22]);
        Add("oam_spr", [0xAA, 0xA0, 0x00, 0xB1, 0x22, 0xC8]);
        Add("popax", [0xAA, 0x88, 0xB1, 0x22, 0xE6, 0x22]);
        Add("decsp4", [0xA5, 0x22, 0x38, 0xE9, 0x04, 0x85, 0x22]);
        Add("incsp2", [0xA0, 0x01, 0xB1, 0x22]);
        Add("incsp1", [0xA0, 0x00, 0xF0]);
        Add("vram_write", [0x85, 0x2A, 0x86, 0x2B]);
        Add("set_rand", [0x85, 0x3C, 0x60]);
        Add("pad_poll", [0xA8, 0xA2, 0x00, 0xA9, 0x01, 0x8D, 0x16, 0x40]);
        Add("pad_state", [0xAA, 0xB5, 0x3C, 0xA2, 0x00, 0x60]);

        // Extract fixed prefixes from optional / final built-in bodies
        foreach (var block in EnumerateSignatureBlocks())
        {
            if (block.Label is null)
                continue;
            byte[]? prefix = ExtractFixedPrefix(block, maxLen: 8);
            if (prefix != null)
                Add(block.Label, prefix);
        }

        return byName.Select(kvp => (kvp.Key, kvp.Value)).ToArray();
    }

    static IEnumerable<Block> EnumerateSignatureBlocks()
    {
        yield return BuiltInSubroutines.PadPoll();
        yield return BuiltInSubroutines.PadTrigger();
        yield return BuiltInSubroutines.PadState();
        yield return BuiltInSubroutines.OamSpr();
        yield return BuiltInSubroutines.Rand8();
        yield return BuiltInSubroutines.SetRand();
        yield return BuiltInSubroutines.SRand();
        yield return BuiltInSubroutines.Rand();
        yield return BuiltInSubroutines.OamMetaSpr();
        yield return BuiltInSubroutines.OamMetaSprPal();
        yield return BuiltInSubroutines.ApuInit();
        yield return BuiltInSubroutines.VramUnrle();
        yield return BuiltInSubroutines.VramRead();
        yield return BuiltInSubroutines.Split();
        yield return BuiltInSubroutines.VrambufClear();
        yield return BuiltInSubroutines.VrambufPut();
        yield return BuiltInSubroutines.VrambufEnd();
        yield return BuiltInSubroutines.VrambufFlush();
        yield return BuiltInSubroutines.Waitvsync();
        yield return BuiltInSubroutines.FadeIn();
        yield return BuiltInSubroutines.FadeOut();
        yield return BuiltInSubroutines.BcdAdd();
        yield return BuiltInSubroutines.RectOverlap();
        yield return BuiltInSubroutines.SpriteOverlap();
        yield return BuiltInSubroutines.StartMusic();
        yield return BuiltInSubroutines.MusicTick();
        yield return BuiltInSubroutines.Memfill();
        yield return BuiltInSubroutines.OamClearFast();
        yield return BuiltInSubroutines.Sprite0Wait();
        yield return BuiltInSubroutines.Addysp();
        yield return BuiltInSubroutines.Incsp1();
        yield return BuiltInSubroutines.Pusha();
        yield return BuiltInSubroutines.Pushax();
        yield return BuiltInSubroutines.Popa();
        yield return BuiltInSubroutines.Popax();
        yield return BuiltInSubroutines.Decsp4();
    }

    /// <summary>
    /// Walk instructions until a relocatable operand appears; return leading fixed bytes.
    /// </summary>
    internal static byte[]? ExtractFixedPrefix(Block block, int maxLen = 8)
    {
        if (block.IsDataBlock || block.RawData != null)
            return null;

        var bytes = new List<byte>(maxLen);
        ushort address = 0x8000;
        var labels = new LabelTable();

        foreach (var (instruction, _) in block.InstructionsWithLabels)
        {
            if (instruction.Operand is LabelOperand or RelativeOperand)
                break;

            byte[] part;
            try
            {
                part = instruction.ToBytes(address, labels);
            }
            catch
            {
                break;
            }

            if (part.Length == 0)
                break;
            if (bytes.Count + part.Length > maxLen)
                break;

            bytes.AddRange(part);
            address += (ushort)part.Length;
            if (bytes.Count >= 3 && bytes.Count >= maxLen / 2)
            {
                // Prefer at least 3 bytes; keep going until max or reloc
            }
        }

        return bytes.Count >= 3 ? bytes.ToArray() : null;
    }

    public static bool Matches(byte[] prg, int offset, byte[] pattern)
    {
        if (offset < 0 || offset + pattern.Length > prg.Length)
            return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (prg[offset + i] != pattern[i])
                return false;
        }
        return true;
    }
}
