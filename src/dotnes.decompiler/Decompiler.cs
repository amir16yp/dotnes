using dotnes.Analysis;
using dotnes.Disasm;

namespace dotnes;

/// <summary>
/// Decompiles a .nes ROM into a C# project that can be rebuilt with dotnes.
/// Dotnes-layout ROMs recover NESLib C# (Transpile). Arbitrary ROMs emit soft-CPU
/// game logic in Program.cs; rebuild uses RawPrg (prg.bin) for byte-identical .nes.
/// Soft-CPU recovery defaults to <see cref="RecoveryStyle.Optimized"/>; use Exact to opt out.
/// </summary>
sealed class Decompiler
{
    readonly NESRomReader _rom;
    readonly ILogger _logger;
    readonly PackModePreference _preference;
    readonly RecoveryStyle _recoveryStyle;

    SemanticRecoverer? _semantic;
    Disassembler? _disassembler;
    PackMode _resolvedMode = PackMode.RawPrg;
    string? _programCs;
    bool _ran;

    public Decompiler(
        NESRomReader rom,
        ILogger? logger = null,
        PackModePreference preference = PackModePreference.Auto,
        RecoveryStyle recoveryStyle = RecoveryStyle.Optimized)
    {
        _rom = rom;
        _logger = logger ?? new NullLogger();
        _preference = preference;
        _recoveryStyle = recoveryStyle;
    }

    public PackMode ResolvedPackMode
    {
        get
        {
            EnsureDecompiled();
            return _resolvedMode;
        }
    }

    public RecoveryStyle RecoveryStyle => _recoveryStyle;

    /// <summary>
    /// Decompile the ROM and return Program.cs contents (semantic or soft-CPU recovery).
    /// </summary>
    public string Decompile()
    {
        EnsureDecompiled();
        return _programCs!;
    }

    /// <summary>
    /// Generate a .csproj for the decompiled project.
    /// </summary>
    public string GenerateCsproj(string projectName)
    {
        EnsureDecompiled();
        return ProjectEmitter.GenerateCsproj(projectName, _rom, _resolvedMode);
    }

    public IReadOnlyList<(ushort PpuAddress, byte[] Data)> GetChrRamTileData()
    {
        EnsureDecompiled();
        return _semantic?.GetChrRamTileData() ?? Array.Empty<(ushort, byte[])>();
    }

    /// <summary>
    /// Write a complete project directory (Program.cs, csproj, CHR, and prg.bin when RawPrg).
    /// </summary>
    public void WriteProject(string outputDir, string projectName)
    {
        EnsureDecompiled();
        ProjectEmitter.WriteProject(
            outputDir,
            projectName,
            _rom,
            _resolvedMode,
            _programCs!,
            GetChrRamTileData(),
            _disassembler);
    }

    void EnsureDecompiled()
    {
        if (_ran)
            return;
        _ran = true;

        _logger.WriteLine($"Decompiling ROM: PRG={_rom.PrgBanks} banks, CHR={_rom.ChrBanks} banks, Mapper={_rom.Mapper}");
        _logger.WriteLine($"Vectors: NMI=${_rom.NmiVector:X4}, RESET=${_rom.ResetVector:X4}, IRQ=${_rom.IrqVector:X4}");

        _disassembler = new Disassembler(_rom);
        _disassembler.Analyze();
        _ = ControlFlowGraph.Build(_disassembler.Instructions);

        var detection = DotnesLayoutDetector.Detect(_rom, _logger);
        _resolvedMode = ResolveMode(detection.IsDotnesLayout);

        if (_resolvedMode == PackMode.Transpile)
        {
            _semantic = new SemanticRecoverer(_rom, _logger);
            _programCs = _semantic.Decompile();
            _logger.WriteLine($"Emit mode: Transpile (semantic NESLib recovery).");
        }
        else
        {
            var general = new GeneralRecoverer(_rom, _disassembler, _logger, _recoveryStyle);
            _programCs = general.Decompile();
            if (_recoveryStyle == RecoveryStyle.Optimized)
                _logger.WriteLine($"Emit mode: RawPrg + optimized soft-CPU Program.cs (use --exact to opt out).");
            else
                _logger.WriteLine($"Emit mode: RawPrg + exact soft-CPU Program.cs.");
        }
    }

    PackMode ResolveMode(bool isDotnesLayout) => _preference switch
    {
        PackModePreference.Transpile => PackMode.Transpile,
        PackModePreference.RawPrg => PackMode.RawPrg,
        _ => isDotnesLayout ? PackMode.Transpile : PackMode.RawPrg,
    };
}
