// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using SafeSign.Formats.NuGet;
using SafeSign.Signing;
using SafeSign.Signing.Pe;

namespace Localisation.ReleaseSigning;

/// <summary>Signs package payloads and then their containers using one authenticated session.</summary>
internal static class PackageSigning
{
    internal static async Task SignAsync(string directory, SigningSession session, Func<string, Task> verifyFile)
    {
        var packages = Directory.GetFiles(directory, "*.nupkg").Order(StringComparer.Ordinal).ToArray();
        if (packages.Length == 0)
        {
            throw new InvalidDataException("No NuGet packages were found.");
        }

        var staging = Directory.CreateTempSubdirectory("localisation-signing-");
        try
        {
            foreach (var package in packages)
            {
                var staged = Path.Combine(staging.FullName, Path.GetFileName(package));
                File.Copy(package, staged);
                await SignPayloadAsync(staged, session, verifyFile);
            }

            foreach (var package in packages)
            {
                var staged = Path.Combine(staging.FullName, Path.GetFileName(package));
                await new NuGetFileSigner().SignAsync(new(staged, staged, session), CancellationToken.None);
            }

            // Publish staged files only after every payload and container was signed successfully.
            foreach (var package in packages)
            {
                File.Copy(Path.Combine(staging.FullName, Path.GetFileName(package)), package, overwrite: true);
            }
        }
        finally
        {
            staging.Delete(recursive: true);
        }
    }

    internal static async Task VerifyFileAsync(string path, string script, string fingerprint)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-File", script, "-Path", path, "-CertificateFingerprint", fingerprint })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Authenticode verification.");
        await process.WaitForExitAsync();
        if (process.ExitCode == 0)
        {
            return;
        }

        throw new InvalidDataException($"Authenticode verification failed for '{path}'.");
    }

    private static async Task SignPayloadAsync(string package, SigningSession session, Func<string, Task> verifyFile)
    {
        await using var archive = await ZipFile.OpenAsync(package, ZipArchiveMode.Update);
        await AssertIdentityAsync(archive);
        if (archive.GetEntry(NuGetFileSigner.SignatureEntryPath) is not null)
        {
            throw new InvalidDataException("Expected unsigned input packages; refusing to replace an existing package signature.");
        }

        var entries = archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length == 0 || entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
        {
            throw new InvalidDataException("Expected at least one DLL and no duplicate DLL entry paths.");
        }

        foreach (var entry in entries)
        {
            await SignEntryAsync(archive, entry, session, verifyFile);
        }
    }

    private static async Task SignEntryAsync(ZipArchive archive, ZipArchiveEntry entry, SigningSession session, Func<string, Task> verifyFile)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");
        try
        {
            await entry.ExtractToFileAsync(temporary);
            await new PeFileSigner().SignAsync(new(temporary, temporary, session), CancellationToken.None);
            await verifyFile(temporary);
            var name = entry.FullName;
            var timestamp = entry.LastWriteTime;
            var attributes = entry.ExternalAttributes;
            entry.Delete();
            var replacement = await archive.CreateEntryFromFileAsync(temporary, name);
            replacement.LastWriteTime = timestamp;
            replacement.ExternalAttributes = attributes;
            await Console.Out.WriteLineAsync($"Verified signed DLL '{name}'.");
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task AssertIdentityAsync(ZipArchive archive)
    {
        var specifications = archive.Entries.Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (specifications.Length != 1)
        {
            throw new InvalidDataException("Expected exactly one NuGet specification.");
        }

        await using var stream = await specifications[0].OpenAsync();
        var specification = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
        var id = specification.Root?.Elements().SingleOrDefault(element => element.Name.LocalName == "metadata")?
            .Elements().SingleOrDefault(element => element.Name.LocalName == "id")?.Value;
        if (id?.StartsWith("Localisation.", StringComparison.Ordinal) is true)
        {
            return;
        }

        throw new InvalidDataException("Only Localisation.* packages may be signed by this release tool.");
    }
}
