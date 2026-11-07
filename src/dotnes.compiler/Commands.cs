using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace dotnes;

static class Commands
{
    const string DefaultMesenPackageVersion = "2.2.1";

    static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Directory containing the running <c>dotnes</c> tool. When building from source this is
    /// <c>bin/Debug</c>, which also holds freshly packed <c>dotnes*.nupkg</c> files.
    /// </summary>
    static string ToolDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    static string? LocalPackageSource
    {
        get
        {
            string dir = ToolDirectory;
            // Main package is packed as dotnes.<version>.nupkg (version starts with a digit).
            if (Directory.Exists(dir)
                && Directory.EnumerateFiles(dir, "dotnes.*.nupkg")
                    .Select(Path.GetFileName)
                    .Any(f => f is not null && Regex.IsMatch(f, @"^dotnes\.\d.*\.nupkg$", RegexOptions.IgnoreCase)))
                return dir;
            return null;
        }
    }

    static string DotnesPackageVersion
    {
        get
        {
            if (TryReadMainDotnesNupkgVersion(out string? fromNupkg))
                return fromNupkg!;

            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "0.3.3-beta";
            int plus = version.IndexOf('+');
            if (plus >= 0)
                version = version.Substring(0, plus);
            if (!version.Contains('-'))
                version += "-beta";
            return version;
        }
    }

    static string MesenPackageVersion
    {
        get
        {
            if (TryReadNupkgVersion("dotnes.mesen.", out string? fromNupkg))
                return fromNupkg!;
            return DefaultMesenPackageVersion;
        }
    }

    static bool TryReadMainDotnesNupkgVersion(out string? version)
    {
        version = null;
        string dir = ToolDirectory;
        if (!Directory.Exists(dir))
            return false;
        foreach (var file in Directory.EnumerateFiles(dir, "dotnes.*.nupkg"))
        {
            string fileName = Path.GetFileName(file);
            var match = Regex.Match(fileName, @"^dotnes\.(\d.*)\.nupkg$", RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;
            version = match.Groups[1].Value;
            return true;
        }
        return false;
    }

    static bool TryReadNupkgVersion(string prefix, out string? version)
    {
        version = null;
        string dir = ToolDirectory;
        if (!Directory.Exists(dir))
            return false;

        foreach (var file in Directory.EnumerateFiles(dir, prefix + "*.nupkg"))
        {
            string fileName = Path.GetFileName(file);
            string withoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (withoutExt.Length <= prefix.Length)
                continue;
            version = withoutExt.Substring(prefix.Length);
            return true;
        }
        return false;
    }

    static string RestoreSourceArgs()
    {
        string? source = LocalPackageSource;
        if (source is null)
            return "";
        // Prefer local bin/Debug packages over nuget.org for unpublished versions.
        return $" -p:RestoreAdditionalProjectSources=\"{source}\"";
    }

    public static int New(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: dotnes new <project-name>");
            return 1;
        }

        string input = args[0].Trim().TrimEnd('/', '\\');
        string name = Path.GetFileName(input);
        if (!IsValidProjectName(name))
        {
            Console.Error.WriteLine(
                $"Invalid project name '{name}'. Use letters, digits, underscores, or hyphens; must start with a letter.");
            return 1;
        }

        string dir = Path.GetFullPath(input);
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Console.Error.WriteLine($"Directory '{dir}' already exists and is not empty.");
            return 1;
        }

        Directory.CreateDirectory(dir);

        string csproj = ReadTemplate("project.csproj.template")
            .Replace("{{DOTNES_VERSION}}", DotnesPackageVersion, StringComparison.Ordinal)
            .Replace("{{MESEN_VERSION}}", MesenPackageVersion, StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), csproj, Utf8NoBom);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), ReadTemplate("Program.cs"), Utf8NoBom);
        // Strip UTF-8 BOM from the embedded CHR assembly if present
        byte[] chr = ReadTemplateBytes("chr_generic.s");
        if (chr.Length >= 3 && chr[0] == 0xEF && chr[1] == 0xBB && chr[2] == 0xBF)
            chr = chr.AsSpan(3).ToArray();
        File.WriteAllBytes(Path.Combine(dir, "chr_generic.s"), chr);

        if (LocalPackageSource is string localSource)
        {
            // So plain `dotnet build` also finds unpublished local packages.
            string nugetConfig = $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="dotnes-local" value="{{localSource}}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """;
            File.WriteAllText(Path.Combine(dir, "nuget.config"), nugetConfig, Utf8NoBom);
        }

        Console.WriteLine($"Created NES project '{name}' in {dir}");
        Console.WriteLine();
        Console.WriteLine($"  cd {name}");
        Console.WriteLine("  dotnes build    # transpile to .nes");
        Console.WriteLine("  dotnes run      # build and launch in Mesen");
        return 0;
    }

    public static int Build(string[] args)
    {
        string project;
        try
        {
            project = Dotnet.FindProject(args.Length > 0 ? args[0] : null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        Console.WriteLine($"Building {project}...");
        return Dotnet.Run($"build \"{project}\" -v minimal{RestoreSourceArgs()}");
    }

    public static int Run(string[] args)
    {
        ParseRunArgs(args, out string? projectArg, out string[] forward);

        string project;
        try
        {
            project = Dotnet.FindProject(projectArg);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        // NES projects set ComputeNETCoreBuildOutputFiles=false — no .NET apphost.
        // Build the ROM, then launch Mesen with the .nes path (do not use `dotnet run`).
        if (!ProjectReferencesMesen(project))
        {
            Console.Error.WriteLine(
                "This project has no PackageReference to dotnes.mesen, so there is no emulator.");
            Console.Error.WriteLine(
                "Add: <PackageReference Include=\"dotnes.mesen\" Version=\"2.2.1\" />");
            Console.Error.WriteLine(
                "Or open the built .nes under bin/Debug/<tfm>/ with any NES emulator.");
            return 1;
        }

        Console.WriteLine($"Building {project}...");
        int buildExit = Dotnet.Run($"build \"{project}\" -v minimal{RestoreSourceArgs()}");
        if (buildExit != 0)
            return buildExit;

        if (!TryGetRunLaunchInfo(project, out string mesenExe, out string romPath, out string? error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        ClearStaleMesenLock();

        var arguments = new StringBuilder();
        arguments.Append(QuoteArg(romPath));
        foreach (string arg in forward)
        {
            arguments.Append(' ');
            arguments.Append(QuoteArg(arg));
        }

        Console.WriteLine($"Running {romPath}...");
        int exit = RunProcess(mesenExe, arguments.ToString());
        if (exit != 0 && MesenLockExists() && Process.GetProcessesByName("Mesen").Length == 0)
        {
            Console.Error.WriteLine(
                "Mesen left a stale lock file. Remove ~/.config/MesenCE/mesen.lock (or Documents/MesenCE on Windows) and retry.");
        }
        return exit;
    }

    static bool MesenLockExists()
    {
        try
        {
            string? configBase = Environment.GetFolderPath(
                OperatingSystem.IsWindows()
                    ? Environment.SpecialFolder.MyDocuments
                    : Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(configBase))
                return false;
            return File.Exists(Path.Combine(configBase, "MesenCE", "mesen.lock"))
                || File.Exists(Path.Combine(configBase, "Mesen2", "mesen.lock"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// <c>dotnes run [project] [-- mesen-args...]</c> or <c>dotnes run -- mesen-args...</c>.
    /// </summary>
    static void ParseRunArgs(string[] args, out string? projectArg, out string[] forward)
    {
        projectArg = null;
        forward = [];
        if (args.Length == 0)
            return;

        int i = 0;
        if (args[0] == "--")
        {
            forward = args.Skip(1).ToArray();
            return;
        }

        if (!args[0].StartsWith('-'))
        {
            projectArg = args[0];
            i = 1;
        }

        if (i < args.Length && args[i] == "--")
            i++;

        if (i < args.Length)
            forward = args.Skip(i).ToArray();
    }

    static bool ProjectReferencesMesen(string projectPath)
    {
        try
        {
            string text = File.ReadAllText(projectPath);
            return text.Contains("dotnes.mesen", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads Mesen exe + ROM path from the project after a successful build.
    /// </summary>
    static bool TryGetRunLaunchInfo(string project, out string mesenExe, out string romPath, out string? error)
    {
        mesenExe = "";
        romPath = "";
        error = null;

        string projectDir = Path.GetDirectoryName(project) ?? Directory.GetCurrentDirectory();
        var props = GetMsBuildProperties(project, "NESTargetPath,_MesenExe,RunCommand,OutputPath,TargetName");
        if (props is null)
        {
            error = "Could not evaluate MSBuild properties for the project.";
            return false;
        }

        mesenExe = FirstNonEmpty(props, "_MesenExe", "RunCommand");
        if (string.IsNullOrWhiteSpace(mesenExe) || !File.Exists(mesenExe))
        {
            error = "Mesen was not installed. Rebuild so dotnes.mesen can download it, or check your platform.";
            return false;
        }

        string nesTarget = NormalizeMsBuildPath(props.GetValueOrDefault("NESTargetPath") ?? "");
        if (!string.IsNullOrWhiteSpace(nesTarget))
        {
            romPath = Path.IsPathRooted(nesTarget)
                ? Path.GetFullPath(nesTarget)
                : Path.GetFullPath(Path.Combine(projectDir, nesTarget));
        }
        else
        {
            string outputPath = NormalizeMsBuildPath(props.GetValueOrDefault("OutputPath") ?? "");
            string targetName = props.GetValueOrDefault("TargetName") ?? Path.GetFileNameWithoutExtension(project);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                error = "Could not resolve NESTargetPath / OutputPath after build.";
                return false;
            }
            string outputDir = Path.IsPathRooted(outputPath)
                ? Path.GetFullPath(outputPath)
                : Path.GetFullPath(Path.Combine(projectDir, outputPath));
            romPath = Path.Combine(outputDir, targetName + ".nes");
        }

        if (!File.Exists(romPath))
        {
            error = $"ROM not found after build: {romPath}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// MSBuild <c>-getProperty</c> may emit Windows backslashes even on Linux
    /// (e.g. <c>bin\Debug/net10.0/</c>). Normalize before path combine/exists checks.
    /// </summary>
    internal static string NormalizeMsBuildPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    static string FirstNonEmpty(Dictionary<string, string> props, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (props.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return "";
    }

    static Dictionary<string, string>? GetMsBuildProperties(string project, string propertyList)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"msbuild \"{project}\" -nologo -getProperty:{propertyList}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi);
        if (process is null)
            return null;
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            return null;

        // -getProperty emits either a bare value (one prop) or JSON { "Properties": { ... } }.
        stdout = stdout.Trim();
        if (stdout.StartsWith('{'))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(stdout);
            if (!doc.RootElement.TryGetProperty("Properties", out var propsElement))
                return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in propsElement.EnumerateObject())
                map[prop.Name] = prop.Value.GetString() ?? "";
            return map;
        }

        // Single property — caller asked for a list, so treat as failure to parse.
        return null;
    }

    /// <summary>
    /// MesenCE leaves mesen.lock if a previous process died. A new launch then tries to
    /// hand off via a pipe that nobody owns and exits without opening the ROM.
    /// </summary>
    static void ClearStaleMesenLock()
    {
        try
        {
            if (Process.GetProcessesByName("Mesen").Length > 0)
                return;

            string? configBase = Environment.GetFolderPath(
                OperatingSystem.IsWindows()
                    ? Environment.SpecialFolder.MyDocuments
                    : Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(configBase))
                return;

            foreach (string name in new[] { "MesenCE", "Mesen2" })
            {
                string lockPath = Path.Combine(configBase, name, "mesen.lock");
                if (File.Exists(lockPath))
                    File.Delete(lockPath);
            }
        }
        catch
        {
            // Best-effort; launching may still work if the lock was already clear.
        }
    }

    public static int Decompile(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: dotnes decompile <input.nes> [output-directory] [--mode auto|transpile|raw]");
            return 1;
        }

        string forward = string.Join(" ", args.Select(QuoteArg));

        // 1) Adjacent binary (repo bin/Debug layout ships both tools side-by-side)
        string adjacent = Path.Combine(ToolDirectory, OperatingSystem.IsWindows() ? "dotnes-decompiler.exe" : "dotnes-decompiler");
        if (File.Exists(adjacent))
            return RunProcess(adjacent, forward);

        // 2) Source project next to the compiler project, or under repo src/
        foreach (string candidate in DecompilerProjectCandidates())
        {
            if (File.Exists(candidate))
                return Dotnet.Run($"run --project \"{candidate}\" -- {forward}");
        }

        // 3) Installed global/local tool
        return Dotnet.Run($"tool run dotnes-decompiler -- {forward}");
    }

    static IEnumerable<string> DecompilerProjectCandidates()
    {
        // src/dotnes.compiler/bin/.../netX.0 → ../../../dotnes.decompiler
        yield return Path.GetFullPath(Path.Combine(
            ToolDirectory, "..", "..", "..", "dotnes.decompiler", "dotnes.decompiler.csproj"));
        // repo/bin/Debug → ../../src/dotnes.decompiler
        yield return Path.GetFullPath(Path.Combine(
            ToolDirectory, "..", "..", "src", "dotnes.decompiler", "dotnes.decompiler.csproj"));
    }

    static string QuoteArg(string arg) =>
        arg.Contains(' ') || arg.Contains('(') || arg.Contains(')')
            ? $"\"{arg.Replace("\"", "\\\"")}\""
            : arg;

    static int RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        process.WaitForExit();
        return process.ExitCode;
    }

    public static int Transpile(string[] args)
    {
        if (args.Length < 2
            || string.IsNullOrWhiteSpace(args[0])
            || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: dotnes transpile <assembly.dll> <chr.s>");
            return 1;
        }

        string targetPath = args[0];
        string assemblyFile = args[1];
        var logger = new ConsoleLogger();

        if (!File.Exists(targetPath))
        {
            logger.WriteError($"The target path '{targetPath}' does not exist.");
            return 1;
        }

        if (!File.Exists(assemblyFile))
        {
            logger.WriteError($"The assembly file '{assemblyFile}' does not exist.");
            return 1;
        }

        string outputPath = Path.ChangeExtension(targetPath, "nes");
        logger.WriteStatus("Running compiler.");
        logger.WriteStatus($"Target Path: {targetPath}");
        logger.WriteStatus($"Assembly File: {assemblyFile}");
        logger.WriteStatus($"Output Path: {outputPath}");

        using var input = File.OpenRead(targetPath);
        using var output = File.Create(outputPath);
        using var transpiler = new Transpiler(input, [new AssemblyReader(assemblyFile)], logger);
        transpiler.Write(output);
        return 0;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            dotnes — .NET for the NES

            Usage:
              dotnes new <name>                 Create a new NES project
              dotnes build [project]            Build the ROM (.nes)
              dotnes run [project] [-- args]    Build ROM and launch in Mesen
              dotnes decompile <rom> [outdir]   Decompile a .nes into a C# project
              dotnes transpile <dll> <chr.s>    Low-level transpile (advanced)

            Examples:
              dotnes new mygame
              cd mygame
              dotnes build
              dotnes run
              dotnes decompile game.nes out/
            """);
    }

    static bool IsValidProjectName(string name)
        => Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9_-]*$");

    static string ReadTemplate(string name)
    {
        using var stream = OpenTemplate(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static byte[] ReadTemplateBytes(string name)
    {
        using var stream = OpenTemplate(name);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    static Stream OpenTemplate(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        string resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Templates." + name.Replace('/', '.'), StringComparison.Ordinal)
                || n.EndsWith("." + name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Missing embedded template '{name}'.");
        return asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Cannot open embedded template '{name}'.");
    }
}
