using dotnes;

var logger = new ConsoleLogger();

if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]) || args[0] is "-h" or "--help")
{
    PrintUsage();
    return args.Length > 0 && args[0] is "-h" or "--help" ? 0 : 1;
}

string? inputPath = null;
string? outputDir = null;
var preference = PackModePreference.Auto;
var recoveryStyle = RecoveryStyle.Optimized;

for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if (arg is "--mode" && i + 1 < args.Length)
    {
        preference = args[++i].Trim().ToLowerInvariant() switch
        {
            "transpile" => PackModePreference.Transpile,
            "softcpu" or "soft" => PackModePreference.SoftCpu,
            "auto" => PackModePreference.Auto,
            _ => throw new ArgumentException($"Unknown mode '{args[i]}'. Expected auto, transpile, or softcpu."),
        };
        continue;
    }

    if (arg is "--exact" or "--no-optimize")
    {
        recoveryStyle = RecoveryStyle.Exact;
        continue;
    }

    if (arg is "--optimize")
    {
        recoveryStyle = RecoveryStyle.Optimized;
        continue;
    }

    if (inputPath == null)
    {
        inputPath = arg;
        continue;
    }

    if (outputDir == null)
    {
        outputDir = arg;
        continue;
    }

    Console.Error.WriteLine($"Unexpected argument: {arg}");
    PrintUsage();
    return 1;
}

if (inputPath == null)
{
    PrintUsage();
    return 1;
}

outputDir ??= Path.GetFileNameWithoutExtension(inputPath);

if (!File.Exists(inputPath))
{
    logger.WriteError($"The input file '{inputPath}' does not exist.");
    return 1;
}

logger.WriteStatus("Running decompiler.");
logger.WriteStatus($"Input: {inputPath}");
logger.WriteStatus($"Output: {outputDir}");

using var input = File.OpenRead(inputPath);
var rom = new NESRomReader(input);

logger.WriteStatus($"PRG banks: {rom.PrgBanks}, CHR banks: {rom.ChrBanks}, Mapper: {rom.Mapper}");
logger.WriteStatus($"Mirroring: {(rom.VerticalMirroring ? "Vertical" : "Horizontal")}, Battery: {rom.HasBattery}");
logger.WriteStatus($"Vectors: NMI=${rom.NmiVector:X4} RESET=${rom.ResetVector:X4} IRQ=${rom.IrqVector:X4}");

var decompiler = new Decompiler(rom, logger, preference, recoveryStyle);
string projectName = Path.GetFileNameWithoutExtension(inputPath);
decompiler.WriteProject(outputDir, projectName);

logger.WriteStatus($"Pack mode: {decompiler.ResolvedPackMode}");
if (decompiler.ResolvedPackMode == PackMode.SoftCpu)
    logger.WriteStatus($"Recovery: {decompiler.RecoveryStyle}");
logger.WriteStatus("Decompilation complete.");
return 0;

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnes-decompiler <input.nes> [output-directory] [options]");
    Console.WriteLine();
    Console.WriteLine("Decompiles a .nes ROM into a C# project rebuildable with dotnes.");
    Console.WriteLine();
    Console.WriteLine("Pack mode:");
    Console.WriteLine("  --mode auto|transpile|softcpu");
    Console.WriteLine("  auto       Detect dotnes layout → Transpile; otherwise SoftCpu (default)");
    Console.WriteLine("  transpile  Force NESLib C# recovery (best for dotnes-built ROMs)");
    Console.WriteLine("  softcpu    Soft-CPU Program.cs → 6502 (prg.bin fills data gaps)");
    Console.WriteLine();
    Console.WriteLine("Soft-CPU recovery (SoftCpu / arbitrary ROMs):");
    Console.WriteLine("  (default)  Optimized readability — structure loops; keep all routines (no DCE)");
    Console.WriteLine("  --exact    Opt out of structuring (more literal goto/labels)");
    Console.WriteLine("  --no-optimize  Alias for --exact");
}
