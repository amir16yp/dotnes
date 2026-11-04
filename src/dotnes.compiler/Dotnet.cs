using System.Diagnostics;

namespace dotnes;

static class Dotnet
{
    public static int Run(string arguments, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'dotnet'. Is the .NET SDK installed?");
        process.WaitForExit();
        return process.ExitCode;
    }

    public static string FindProject(string? path)
    {
        path ??= Directory.GetCurrentDirectory();
        path = Path.GetFullPath(path);

        if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return path;

        if (Directory.Exists(path))
        {
            var projects = Directory.GetFiles(path, "*.csproj");
            if (projects.Length == 1)
                return projects[0];
            if (projects.Length == 0)
                throw new InvalidOperationException($"No .csproj found in '{path}'.");
            throw new InvalidOperationException(
                $"Multiple .csproj files in '{path}'. Pass the project path explicitly.");
        }

        throw new InvalidOperationException($"Path not found: '{path}'.");
    }
}
