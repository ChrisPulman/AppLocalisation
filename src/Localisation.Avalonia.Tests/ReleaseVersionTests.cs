// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies the release workflow against isolated Git tag histories.</summary>
public sealed class ReleaseVersionTests
{
    /// <summary>Verifies that the selected bump applies after the minimum version.</summary>
    /// <param name="tag">The existing stable tag, if any.</param>
    /// <param name="bump">The requested release level.</param>
    /// <param name="expected">The expected release version.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task representing the test.</returns>
    [Test]
    [Arguments("", "major", "2.0.0")]
    [Arguments("", "minor", "1.2.0")]
    [Arguments("", "patch", "1.1.1")]
    [Arguments("1.0.4", "major", "2.0.0")]
    [Arguments("v0.5.0", "major", "2.0.0")]
    [Arguments("v2.4.3", "major", "3.0.0")]
    [Arguments("v2.4.3", "minor", "2.5.0")]
    [Arguments("v2.4.3", "patch", "2.4.4")]
    public async Task ComputeReleaseVersion_StableHistory_AppliesRequestedBump(string tag, string bump, string expected, CancellationToken cancellationToken)
    {
        var tags = string.IsNullOrEmpty(tag) ? Array.Empty<string>() : [tag];
        var result = await RunWorkflowAsync(tags, bump, "none", cancellationToken);
        await Assert.That(result.Output).Contains($"version={expected}\n");
        await Assert.That(result.Output).Contains($"tag=v{expected}\n");
        await Assert.That(result.Output).Contains("prerelease=false\n");
        await Assert.That(result.Environment).Contains($"MINVERVERSIONOVERRIDE={expected}\n");
        await Assert.That(result.Environment).Contains($"MinVerVersionOverride={expected}\n");
    }

    /// <summary>Verifies that only the matching core and channel determine the next prerelease number.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task representing the test.</returns>
    [Test]
    public async Task ComputeReleaseVersion_PrereleaseHistory_UsesNextMatchingNumber(CancellationToken cancellationToken)
    {
        string[] tags = ["v1.1.0", "v2.0.0-alpha.1", "v2.0.0-alpha.9", "v2.0.0-alpha.invalid", "v2.0.0-beta.20", "v3.0.0-alpha.30"];
        var result = await RunWorkflowAsync(tags, "major", "alpha", cancellationToken);
        await Assert.That(result.Output).Contains("version=2.0.0-alpha.10\n");
        await Assert.That(result.Output).Contains("tag=v2.0.0-alpha.10\n");
        await Assert.That(result.Output).Contains("prerelease=true\n");
    }

    private static async Task<(string Output, string Environment)> RunWorkflowAsync(
        string[] tags,
        string bump,
        string channel,
        CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("localisation-release-");
        try
        {
            await RunProcessAsync(
                "git",
                ["init", "--quiet"],
                directory.FullName,
                null,
                cancellationToken);
            await RunProcessAsync(
                "git",
                ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                    "-c", "commit.gpgsign=false", "commit", "--quiet", "--allow-empty", "-m", "Release fixture"],
                directory.FullName,
                null,
                cancellationToken);
            foreach (var tag in tags)
            {
                await RunProcessAsync(
                    "git",
                    ["-c", "tag.gpgsign=false", "tag", tag],
                    directory.FullName,
                    null,
                    cancellationToken);
            }

            var scriptPath = Path.Combine(directory.FullName, "release.sh");
            var outputPath = Path.Combine(directory.FullName, "output.txt");
            var environmentPath = Path.Combine(directory.FullName, "environment.txt");
            await File.WriteAllTextAsync(scriptPath, ReadReleaseScript(), new UTF8Encoding(false), cancellationToken);
            var variables = new Dictionary<string, string>
            {
                ["BUMP"] = bump,
                ["CHANNEL"] = channel,
                ["TAG_PREFIX"] = "v",
                ["MINIMUM_MAJOR_MINOR"] = "1.1",
                ["GITHUB_OUTPUT"] = outputPath.Replace('\\', '/'),
                ["GITHUB_ENV"] = environmentPath.Replace('\\', '/'),
            };
            var bash = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe")
                : "/bin/bash";
            await RunProcessAsync(bash, ["release.sh"], directory.FullName, variables, cancellationToken);
            return (await File.ReadAllTextAsync(outputPath, cancellationToken), await File.ReadAllTextAsync(environmentPath, cancellationToken));
        }
        finally
        {
            DeleteRepository(directory);
        }
    }

    private static void DeleteRepository(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes &= ~FileAttributes.ReadOnly;
        }

        directory.Delete(true);
    }

    private static string ReadReleaseScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var workflowPath = Path.Combine(directory.FullName, ".github", "workflows", "BuildDeploy.yml");
            if (File.Exists(workflowPath))
            {
                var lines = File.ReadAllLines(workflowPath);
                var step = Array.FindIndex(lines, static line => line.Trim() == "- name: Compute release version");
                var start = Array.FindIndex(lines, step + 1, static line => line.Trim() == "run: |");
                if (step < 0 || start < 0)
                {
                    throw new InvalidOperationException("The release version workflow step was not found.");
                }

                const int YamlIndentSize = 2;
                var indentation = lines[start].Length - lines[start].TrimStart().Length + YamlIndentSize;
                return ReadIndentedBlock(lines, start + 1, indentation);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("BuildDeploy.yml could not be located from the test output directory.");
    }

    private static string ReadIndentedBlock(string[] lines, int start, int indentation)
    {
        var script = new StringBuilder();
        for (var index = start; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                _ = script.Append('\n');
            }
            else if (line.Length - line.TrimStart().Length >= indentation)
            {
                _ = script.Append(line.AsSpan(indentation)).Append('\n');
            }
            else
            {
                break;
            }
        }

        return script.ToString();
    }

    private static async Task RunProcessAsync(
        string executable,
        string[] arguments,
        string directory,
        Dictionary<string, string>? variables,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (variables is not null)
        {
            foreach (var variable in variables)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var diagnostics = $"{await output}\n{await error}";
        await Assert.That(process.ExitCode).IsEqualTo(0).Because(diagnostics);
    }
}
