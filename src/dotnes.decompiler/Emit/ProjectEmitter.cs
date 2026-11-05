using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using dotnes.Disasm;

namespace dotnes;

/// <summary>
/// Writes a complete, buildable dotnes project from decompilation results.
/// </summary>
static class ProjectEmitter
{
    public static void AppendRomProperties(StringBuilder sb, NESRomReader rom, PackMode packMode)
    {
        if (rom.Mapper != 0)
            sb.AppendLine($"    <NESMapper>{rom.Mapper}</NESMapper>");
        if (rom.PrgBanks != 2)
            sb.AppendLine($"    <NESPrgBanks>{rom.PrgBanks}</NESPrgBanks>");
        if (rom.ChrBanks != 1)
            sb.AppendLine($"    <NESChrBanks>{rom.ChrBanks}</NESChrBanks>");
        if (rom.VerticalMirroring)
            sb.AppendLine("    <NESMirroring>Vertical</NESMirroring>");
        if (rom.HasBattery)
            sb.AppendLine("    <NESBattery>true</NESBattery>");
        if (packMode == PackMode.RawPrg)
        {
            sb.AppendLine("    <NESPackMode>RawPrg</NESPackMode>");
            sb.AppendLine("    <NESRawPrg>prg.bin</NESRawPrg>");
            sb.AppendLine("    <NESRawHeader>header.bin</NESRawHeader>");
            if (rom.HasTrainer)
                sb.AppendLine("    <NESRawTrainer>trainer.bin</NESRawTrainer>");
        }
    }

    public static string GenerateCsproj(string projectName, NESRomReader rom, PackMode packMode)
    {
        string version = ResolveDotnesPackageVersion();
        string mesenVersion = ResolveMesenPackageVersion();
        string? localSource = FindLocalPackageSource();

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <OutputType>Exe</OutputType>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        // Allow prerelease packages (dotnes ships as *-beta).
        sb.AppendLine("    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>");
        if (localSource != null)
            sb.AppendLine($"    <RestoreAdditionalProjectSources>{localSource}</RestoreAdditionalProjectSources>");
        AppendRomProperties(sb, rom, packMode);
        if (packMode == PackMode.RawPrg)
        {
            // Soft-CPU Program.cs is readable game logic, not NESLib transpile input.
            // Analyzers target Transpile patterns; packing uses prg.bin regardless of IL.
            // Keep unused local functions from static discovery (no dead-code elimination).
            sb.AppendLine("    <RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>");
            sb.AppendLine("    <RunAnalyzersDuringLiveAnalysis>false</RunAnalyzersDuringLiveAnalysis>");
            sb.AppendLine("    <NoWarn>$(NoWarn);CS8321;CS0162;CS0164</NoWarn>");
        }
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <Using Include=\"NES\" />");
        // Explicit in case an older package props file is resolved; matches samples/dotnes.defaults.props.
        sb.AppendLine("    <Using Include=\"NES.NESLib\" Static=\"true\" />");
        // Decompiler emits NESColor.Name (via using NES;). Strip the package's
        // using static NES.NESColor so bare color names are not in scope.
        sb.AppendLine("    <Using Remove=\"NES.NESColor\" />");
        sb.AppendLine($"    <PackageReference Include=\"dotnes\" Version=\"{version}\" />");
        // Wires RunCommand → Mesen so `dotnes run` / `dotnet run` launch the .nes, not a .NET apphost.
        sb.AppendLine($"    <PackageReference Include=\"dotnes.mesen\" Version=\"{mesenVersion}\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    /// <summary>
    /// Emits C# that owns the exact iNES header (and optional trainer) as byte data.
    /// PRG stays in prg.bin for size; packer writes header.bin verbatim for identity.
    /// </summary>
    public static string GenerateRomImageCs(NESRomReader rom)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated ROM image metadata for RawPrg rebuilds.");
        sb.AppendLine("// Header (and trainer) match header.bin / trainer.bin exactly.");
        sb.AppendLine("// PRG authority remains prg.bin until banked verbatim spans replace it.");
        sb.AppendLine();
        sb.AppendLine("static class RomImage");
        sb.AppendLine("{");
        // Fields — NES analyzers reject properties on game types.
        sb.Append("    public static readonly byte[] Header = { ");
        AppendBytes(sb, rom.Header);
        sb.AppendLine(" };");
        if (rom.HasTrainer)
        {
            sb.AppendLine();
            sb.Append("    public static readonly byte[] Trainer = { ");
            AppendBytes(sb, rom.Trainer);
            sb.AppendLine(" };");
        }
        sb.AppendLine();
        sb.AppendLine($"    public const int PrgBanks = {rom.PrgBanks};");
        sb.AppendLine($"    public const int ChrBanks = {rom.ChrBanks};");
        sb.AppendLine($"    public const int Mapper = {rom.Mapper};");
        sb.AppendLine($"    public const int PrgSize = {rom.PrgRom.Length};");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static void AppendBytes(StringBuilder sb, byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append($"0x{data[i]:X2}");
        }
    }

    public static void WriteProject(
        string outputDir,
        string projectName,
        NESRomReader rom,
        PackMode packMode,
        string programCs,
        IReadOnlyList<(ushort PpuAddress, byte[] Data)>? chrRamUploads = null,
        Disassembler? disasm = null)
    {
        Directory.CreateDirectory(outputDir);

        File.WriteAllText(Path.Combine(outputDir, "Program.cs"), programCs);
        File.WriteAllText(Path.Combine(outputDir, $"{projectName}.csproj"), GenerateCsproj(projectName, rom, packMode));

        string? localSource = FindLocalPackageSource();
        if (localSource != null)
        {
            // nuget.config so `dotnet build` in the project dir picks up local bin/Debug packs
            var nuget = new StringBuilder();
            nuget.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            nuget.AppendLine("<configuration>");
            nuget.AppendLine("  <packageSources>");
            nuget.AppendLine("    <clear />");
            nuget.AppendLine($"    <add key=\"dotnes-local\" value=\"{localSource}\" />");
            nuget.AppendLine("    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />");
            nuget.AppendLine("  </packageSources>");
            nuget.AppendLine("</configuration>");
            File.WriteAllText(Path.Combine(outputDir, "nuget.config"), nuget.ToString());
        }

        if (packMode == PackMode.RawPrg)
        {
            File.WriteAllBytes(Path.Combine(outputDir, "prg.bin"), rom.PrgRom);
            File.WriteAllBytes(Path.Combine(outputDir, "header.bin"), rom.Header);
            if (rom.HasTrainer)
                File.WriteAllBytes(Path.Combine(outputDir, "trainer.bin"), rom.Trainer);
            File.WriteAllText(Path.Combine(outputDir, "RomImage.cs"), GenerateRomImageCs(rom));
            if (disasm != null)
                File.WriteAllText(Path.Combine(outputDir, "disasm.s"), disasm.GenerateListing());
        }

        WriteChrFiles(outputDir, rom, chrRamUploads);
    }

    static void WriteChrFiles(
        string outputDir,
        NESRomReader rom,
        IReadOnlyList<(ushort PpuAddress, byte[] Data)>? chrRamUploads)
    {
        if (rom.ChrBanks > 1)
        {
            for (int bank = 0; bank < rom.ChrBanks; bank++)
            {
                string path = Path.Combine(outputDir, $"chr_generic_{bank}.s");
                File.WriteAllText(path, rom.GenerateChrAssembly(bank));
            }
            return;
        }

        if (rom.ChrBanks == 1)
        {
            File.WriteAllText(Path.Combine(outputDir, "chr_generic.s"), rom.GenerateChrAssembly());
            return;
        }

        // CHR RAM
        if (chrRamUploads is { Count: > 0 })
        {
            const int chrSize = 8192;
            var chr = new byte[chrSize];
            foreach (var (ppuAddr, data) in chrRamUploads)
            {
                if (ppuAddr >= chrSize)
                    continue;
                int copyLen = Math.Min(data.Length, chrSize - ppuAddr);
                if (copyLen > 0)
                    Array.Copy(data, 0, chr, ppuAddr, copyLen);
            }
            File.WriteAllText(
                Path.Combine(outputDir, "chr_generic.s"),
                NESRomReader.GenerateChrAssemblyFromData(chr, 0, chrSize));
        }
    }

    const string DefaultMesenPackageVersion = "2.2.1";

    internal static string ResolveDotnesPackageVersion()
    {
        string? dir = FindLocalPackageSource();
        if (dir != null)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "dotnes.*.nupkg"))
            {
                string name = Path.GetFileName(file);
                var match = Regex.Match(name, @"^dotnes\.(\d.*)\.nupkg$", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value;
            }
        }

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.3.3-beta";
        int plus = version.IndexOf('+');
        if (plus >= 0)
            version = version[..plus];
        if (!version.Contains('-', StringComparison.Ordinal))
            version += "-beta";
        return version;
    }

    internal static string ResolveMesenPackageVersion()
    {
        string? dir = FindLocalPackageSource();
        if (dir != null)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "dotnes.mesen.*.nupkg"))
            {
                string name = Path.GetFileName(file);
                var match = Regex.Match(name, @"^dotnes\.mesen\.(\d.*)\.nupkg$", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value;
            }
        }
        return DefaultMesenPackageVersion;
    }

    static string? FindLocalPackageSource()
    {
        // Prefer the directory next to the running decompiler (bin/Debug when building from source)
        string toolDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (HasDotnesNupkg(toolDir))
            return toolDir;

        // Walk up a few levels looking for bin/Debug packs
        string? dir = toolDir;
        for (int i = 0; i < 5 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "bin", "Debug");
            if (HasDotnesNupkg(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    static bool HasDotnesNupkg(string dir) =>
        Directory.Exists(dir)
        && Directory.EnumerateFiles(dir, "dotnes.*.nupkg")
            .Select(Path.GetFileName)
            .Any(f => f is not null && Regex.IsMatch(f, @"^dotnes\.\d.*\.nupkg$", RegexOptions.IgnoreCase));
}
