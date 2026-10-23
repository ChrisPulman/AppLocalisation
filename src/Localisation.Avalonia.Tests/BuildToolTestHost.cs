// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Localisation.Avalonia.Tests;

internal static class BuildToolTestHost
{
    internal static string FindTool()
    {
#if DEBUG
        const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var tool = Path.Combine(directory.FullName, "build", "Tools", "bin", Configuration, "net10.0", "Localisation.BuildTools.dll");
            if (File.Exists(tool))
            {
                return tool;
            }
        }

        throw new FileNotFoundException("Localisation.BuildTools.dll could not be located.");
    }

    internal static async Task<(int ExitCode, string Diagnostics)> RunAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(FindTool());
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start build tools.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, $"{await output}\n{await error}");
    }
}
