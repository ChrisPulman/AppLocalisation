using System.IO.Compression;

namespace Localisation.BuildTools;

internal static class SignedPackages
{
    internal static async Task RunAsync(Options options)
    {
        var packagesDirectory = options.Required("PackagesDirectory");
        var fingerprint = options.Required("CertificateFingerprint");
        var packages = Directory.EnumerateFiles(packagesDirectory, "*.nupkg", SearchOption.TopDirectoryOnly).ToArray();
        if (packages.Length == 0)
            throw new InvalidOperationException("No signed NuGet packages were found.");

        var temporary = Directory.CreateTempSubdirectory("localisation-verify-");
        try
        {
            foreach (var package in packages)
            {
                await ToolProcess.RunAsync("dotnet", "nuget", "verify", package, "--all", "--certificate-fingerprint", fingerprint, "--verbosity", "detailed");
                await using var archive = await ZipFile.OpenReadAsync(package);
                var dlls = archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (dlls.Length == 0)
                    throw new InvalidOperationException($"Package has no DLLs: {Path.GetFileName(package)}");

                foreach (var entry in dlls)
                {
                    var path = Path.Combine(temporary.FullName, $"{Guid.NewGuid():N}.dll");
                    await using (var output = File.Create(path))
                    await using (var input = await entry.OpenAsync())
                        await input.CopyToAsync(output);
                    Authenticode.Verify(path, fingerprint);
                    Console.WriteLine($"Verified packaged DLL: {Path.GetFileName(package)}/{entry.FullName}");
                }
            }
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }
}
