using dotnes.ObjectModel;

namespace dotnes.Analysis;

/// <summary>
/// Detects whether a ROM was built with the stock dotnes CRT layout.
/// </summary>
static class DotnesLayoutDetector
{
    public sealed record Result(bool IsDotnesLayout, ushort? MainAddress, ushort BuiltInsEnd);

    public static Result Detect(NESRomReader rom, ILogger? logger = null)
    {
        logger ??= new NullLogger();

        var program = Program6502.CreateWithBuiltIns();
        program.ResolveAddresses();
        var labels = program.GetLabels();
        ushort builtInsEnd = (ushort)(0x8000 + program.TotalSize);

        // Stock CRT must begin at $8000 matching CreateWithBuiltIns bytes for a prefix.
        byte[] expected = program.ToBytes();
        int compareLen = Math.Min(64, Math.Min(expected.Length, rom.PrgRom.Length));
        bool prefixMatch = compareLen > 0;
        for (int i = 0; i < compareLen && prefixMatch; i++)
        {
            if (rom.PrgRom[i] != expected[i])
                prefixMatch = false;
        }

        if (!prefixMatch)
        {
            logger.WriteLine($"Dotnes layout: PRG prefix does not match stock CRT.");
            return new Result(false, null, builtInsEnd);
        }

        ushort? main = null;
        if (labels.TryGetValue("detectNTSC", out ushort detectAddr))
        {
            int detectOffset = detectAddr - 0x8000;
            int blockSize = program.Blocks.FirstOrDefault(b => b.Label == "detectNTSC")?.Size ?? 0;
            if (blockSize > 0)
            {
                int blockEnd = detectOffset + blockSize;
                for (int i = detectOffset; i < blockEnd && i < rom.PrgRom.Length - 2; i++)
                {
                    if (rom.PrgRom[i] != 0x4C)
                        continue;
                    ushort target = (ushort)(rom.PrgRom[i + 1] | (rom.PrgRom[i + 2] << 8));
                    if (target >= builtInsEnd && target < 0xFFFA)
                    {
                        main = target;
                        break;
                    }
                }
            }
        }

        if (main == null)
        {
            // Prefix matched but no main JMP — still treat as dotnes-like with fallback main.
            main = builtInsEnd;
        }

        logger.WriteLine($"Dotnes layout detected; main=${main.Value:X4}, builtInsEnd=${builtInsEnd:X4}");
        return new Result(true, main, builtInsEnd);
    }
}
