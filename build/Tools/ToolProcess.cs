using System.Diagnostics;

namespace Localisation.BuildTools;

internal static class ToolProcess
{
    public static async Task RunAsync(string executable, params string[] arguments)
    {
        using var process = Start(executable, arguments);
        var output = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        var error = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{executable} failed with exit code {process.ExitCode}.");
        }
    }

    public static async Task<string> CaptureAsync(string executable, params string[] arguments)
    {
        using var process = Start(executable, arguments);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        await process.WaitForExitAsync();
        await error;
        var text = await output;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{executable} failed with exit code {process.ExitCode}.");
        }

        return text;
    }

    private static Process Start(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (OperatingSystem.IsWindows() && Path.GetFileName(executable).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            && arguments is ["/d", "/s", "/c", var command])
        {
            // cmd parses a command string, not the CommandLineToArgvW format used by ArgumentList.
            start.Arguments = "/d /s /c " + command;
        }
        else
        {
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
        }

        return Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{executable}'.");
    }
}
