// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Localisation.Avalonia.Tests;

/// <summary>Verifies symbol validation and recovery before publication.</summary>
public sealed class SymbolPackageTests
{
    private const string PublishedScenario = "published";

    private const string PrimaryPackageName = "Fixture.1.0.0.nupkg";

    /// <summary>Verifies valid, missing, corrupt and recovered symbol packages.</summary>
    /// <param name="scenario">The fixture scenario.</param>
    /// <param name="expectedExitCode">The expected validation result.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task representing the test.</returns>
    [Test]
    [Arguments("valid", 0)]
    [Arguments("missing", 1)]
    [Arguments("corrupt", 1)]
    [Arguments("checksum", 1)]
    [Arguments(PublishedScenario, 0)]
    public async Task PrepareSymbols_Fixture_ValidatesOrRecovers(string scenario, int expectedExitCode, CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("localisation-symbols-");
        try
        {
            var packages = Directory.CreateDirectory(Path.Combine(directory.FullName, "packages"));
            var published = Directory.CreateDirectory(Path.Combine(directory.FullName, PublishedScenario));
            var assemblyPath = typeof(SymbolPackageTests).Assembly.Location;
            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            var validSymbols = await File.ReadAllBytesAsync(pdbPath, cancellationToken);
            CreatePackage(Path.Combine(packages.FullName, PrimaryPackageName), assemblyPath, null);
            var symbols = scenario switch
            {
                "valid" => validSymbols,
                "missing" => null,
                "checksum" => ChangeChecksum(validSymbols),
                _ => (byte[])[0],
            };
            CreatePackage(Path.Combine(packages.FullName, "Fixture.1.0.0.snupkg"), null, symbols);
            if (scenario == PublishedScenario)
            {
                CreatePackage(Path.Combine(published.FullName, PrimaryPackageName), assemblyPath, validSymbols);
            }

            var result = await RunPreparationAsync(packages.FullName, published.FullName, cancellationToken);
            await Assert.That(result.ExitCode).IsEqualTo(expectedExitCode).Because(result.Diagnostics);
            if (scenario == PublishedScenario)
            {
                var original = await File.ReadAllBytesAsync(Path.Combine(published.FullName, PrimaryPackageName), cancellationToken);
                var prepared = await File.ReadAllBytesAsync(Path.Combine(packages.FullName, PrimaryPackageName), cancellationToken);
                await Assert.That(Convert.ToHexString(SHA256.HashData(prepared))).IsEqualTo(Convert.ToHexString(SHA256.HashData(original)));
            }

            if (expectedExitCode == 0)
            {
                await using var archive = await ZipFile.OpenReadAsync(Path.Combine(packages.FullName, "Fixture.1.0.0.snupkg"), cancellationToken);
                var pdb = archive.GetEntry("lib/net10.0/Fixture.pdb") ?? throw new FileNotFoundException("Expected portable PDB is missing.");
                await Assert.That(pdb.Length).IsEqualTo(new FileInfo(pdbPath).Length);
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void CreatePackage(string path, string? assemblyPath, byte[]? symbols)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("Fixture.nuspec").Open()))
        {
            writer.Write("<package><metadata><id>Fixture</id><version>1.0.0</version></metadata></package>");
        }

        if (assemblyPath is not null)
        {
            _ = archive.CreateEntryFromFile(assemblyPath, "lib/net10.0/Fixture.dll");
        }

        if (symbols is null)
        {
            return;
        }

        using var output = archive.CreateEntry("lib/net10.0/Fixture.pdb").Open();
        output.Write(symbols);
    }

    private static byte[] ChangeChecksum(byte[] symbols)
    {
        var modified = (byte[])symbols.Clone();
        modified[^1] ^= 1;
        return modified;
    }

    private static async Task<(int ExitCode, string Diagnostics)> RunPreparationAsync(
        string packages,
        string published,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in (string[])["-NoProfile", "-File", FindScript(), "-PackagesDirectory", packages, "-PublishedPackagesDirectory", published])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start symbol validation.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, $"{await output}\n{await error}");
    }

    private static string FindScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var script = Path.Combine(directory.FullName, "build", "Prepare-SymbolPackages.ps1");
            if (File.Exists(script))
            {
                return script;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Prepare-SymbolPackages.ps1 could not be located.");
    }
}
