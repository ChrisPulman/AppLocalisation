namespace Localisation.BuildTools;

internal static class AotVerification
{
    public static async Task RunAsync(Options options)
    {
        var runtime = options.Get("RuntimeIdentifier", "win-x64");
        var configuration = options.Get("Configuration", "Release");
        if (options.Flag("UseEnvironmentalTools"))
        {
            if (!OperatingSystem.IsWindows() || runtime != "win-x64")
            {
                throw new ArgumentException("Environmental tool initialization currently supports win-x64 on Windows.");
            }

            await InitializeToolsAsync();
        }

        var root = FindRoot();
        var project = Path.Combine(root, "src", "Localisation.AotSmoke", "Localisation.AotSmoke.csproj");
        foreach (var mode in new[] { "trimmed", "native" })
        {
            var output = Path.Combine(root, "artifacts", "aot-verification", mode);
            await ToolProcess.RunAsync("dotnet", "publish", project, "-c", configuration, "-r", runtime,
                "--self-contained", "true", $"-p:LocalisationPublishAot={mode == "native"}",
                "-p:ILLinkTreatWarningsAsErrors=true", "-o", output);
            await ToolProcess.RunAsync(Path.Combine(output, "Localisation.AotSmoke" + (runtime.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : "")));
            Console.WriteLine($"Verified {mode} resources ({runtime}).");
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Localisation.AotSmoke", "Localisation.AotSmoke.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository's AOT smoke project.");
    }

    private static async Task InitializeToolsAsync()
    {
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        var installations = await ToolProcess.CaptureAsync(vswhere, "-all", "-prerelease", "-products", "*", "-property", "installationPath");
        foreach (var installation in installations.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var toolRoot = Path.Combine(installation, "VC", "Tools", "MSVC");
            var developerCommand = Path.Combine(installation, "Common7", "Tools", "VsDevCmd.bat");
            if (!Directory.Exists(toolRoot) || !File.Exists(developerCommand))
            {
                continue;
            }

            var tools = Directory.EnumerateDirectories(toolRoot)
                .Where(path => File.Exists(Path.Combine(path, "lib", "x64", "libcmt.lib")))
                .Select(path => (Path: path, Version: Version.TryParse(Path.GetFileName(path), out var version) ? version : null))
                .Where(tool => tool.Version is not null)
                .OrderByDescending(tool => tool.Version).FirstOrDefault();
            if (tools.Path is null)
            {
                continue;
            }

            // cmd is required to import the environment produced by Microsoft's batch file.
            var environment = await ToolProcess.CaptureAsync(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                "/d", "/s", "/c", $"\"\"{developerCommand}\" -arch=x64 -host_arch=x64 >nul && set\"");
            foreach (var line in environment.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOf('=');
                if (separator > 0)
                {
                    Environment.SetEnvironmentVariable(line[..separator], line[(separator + 1)..]);
                }
            }

            Environment.SetEnvironmentVariable("LIB", Path.Combine(tools.Path, "lib", "x64") + ";" + Environment.GetEnvironmentVariable("LIB"));
            Environment.SetEnvironmentVariable("PATH", Path.Combine(tools.Path, "bin", "Hostx64", "x64") + ";" + Environment.GetEnvironmentVariable("PATH"));
            Environment.SetEnvironmentVariable("IlcUseEnvironmentalTools", "true");
            return;
        }

        throw new InvalidOperationException("Visual Studio x64 C++ build tools were not found.");
    }
}
