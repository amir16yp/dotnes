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

        Console.WriteLine($"Running {project}...");
        string extra = args.Length > 1
            ? " -- " + string.Join(" ", args.Skip(1).Select(a => $"\"{a}\""))
            : "";
        return Dotnet.Run($"run --project \"{project}\"{RestoreSourceArgs()}{extra}");
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
              dotnes run [project]              Build and run in the emulator
              dotnes transpile <dll> <chr.s>    Low-level transpile (advanced)

            Examples:
              dotnes new mygame
              cd mygame
              dotnes build
              dotnes run
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
