using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Localisation.BuildTools;

internal static class SymbolPackages
{
    internal static async Task RunAsync(Options options)
    {
        var packages = Directory.GetFiles(options.Required("PackagesDirectory"), "*.nupkg");
        if (packages.Length == 0)
        {
            throw new InvalidDataException("No NuGet packages were found.");
        }
        using var client = new HttpClient();
        foreach (var package in packages)
        {
            var symbols = Path.ChangeExtension(package, ".snupkg");
            if (!File.Exists(symbols))
            {
                throw new InvalidDataException($"Missing symbol package for '{package}'.");
            }
            (string Id, string Version) identity;
            await using (var archive = await ZipFile.OpenReadAsync(package))
            {
                identity = Identity(archive);
            }
            var download = Path.GetTempFileName();
            var staged = symbols + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var stagedMain = package + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string? published = null;
                var directory = options.Get("PublishedPackagesDirectory", "");
                if (directory.Length > 0)
                {
                    var candidate = Path.Combine(directory, Path.GetFileName(package));
                    if (File.Exists(candidate))
                    {
                        published = candidate;
                    }
                }
                else if (options.Flag("ResolvePublishedPackages"))
                {
                    var id = Uri.EscapeDataString(identity.Id.ToLowerInvariant());
                    var version = Uri.EscapeDataString(identity.Version.ToLowerInvariant());
                    var uri = new Uri($"https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg");
                    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                    if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        response.EnsureSuccessStatusCode();
                        await using (var output = File.Create(download))
                        {
                            await response.Content.CopyToAsync(output);
                        }
                        published = download;
                    }
                }
                File.Copy(symbols, staged);
                if (published is not null)
                {
                    File.Copy(published, stagedMain);
                }
                await using (var main = await ZipFile.OpenReadAsync(published is null ? package : stagedMain))
                await using (var target = await ZipFile.OpenAsync(staged, ZipArchiveMode.Update))
                {
                    if (!Same(identity, Identity(main)) || !Same(identity, Identity(target)))
                    {
                        throw new InvalidDataException("Package identity mismatch.");
                    }
                    if (published is not null)
                    {
                        foreach (var entry in target.Entries.Where(e => e.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)).ToArray())
                        {
                            entry.Delete();
                        }
                    }

                    var dlls = main.Entries.Where(IsLibrary).ToArray();
                    if (dlls.Length == 0)
                    {
                        throw new InvalidDataException("No library or analyzer assemblies.");
                    }
                    foreach (var dll in dlls)
                    {
                        using var stream = new MemoryStream(Bytes(dll));
                        using var reader = new PEReader(stream);
                        var path = Path.ChangeExtension(dll.FullName, ".pdb");
                        var pdb = published is null
                            ? Bytes(target.GetEntry(path) ?? throw new InvalidDataException($"Missing '{path}'."))
                            : ReadPdb(main, path, reader);
                        Validate(pdb, reader);
                        if (published is not null)
                        {
                            await using var output = await target.CreateEntry(path).OpenAsync();
                            await output.WriteAsync(pdb);
                        }
                    }
                    Console.WriteLine($"Validated {dlls.Length} matching portable PDBs for {Path.GetFileName(package)}; reused published binaries: {published is not null}.");
                }
                if (published is not null)
                {
                    File.Move(stagedMain, package, true);
                }
                File.Move(staged, symbols, true);
            }
            finally
            {
                File.Delete(download);
                File.Delete(staged);
                File.Delete(stagedMain);
            }
        }
    }

    private static bool IsLibrary(ZipArchiveEntry entry) =>
        (entry.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.StartsWith("analyzers/dotnet/cs/", StringComparison.OrdinalIgnoreCase))
        && entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        && !entry.FullName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);

    private static bool Same((string Id, string Version) a, (string Id, string Version) b) =>
        string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Version, b.Version, StringComparison.OrdinalIgnoreCase);

    private static (string Id, string Version) Identity(ZipArchive archive)
    {
        var specs = archive.Entries.Where(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (specs.Length != 1) throw new InvalidDataException("A package must contain exactly one nuspec.");
        using var stream = specs[0].Open();
        var metadata = XDocument.Load(stream).Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "metadata") ?? throw new InvalidDataException("Missing package metadata.");
        string Value(string name) => metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name)?.Value is { Length: > 0 } value ? value : throw new InvalidDataException($"Missing package {name}.");
        return (Value("id"), Value("version"));
    }
    private static byte[] Bytes(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
    private static byte[] ReadPdb(ZipArchive archive, string path, PEReader reader)
    {
        if (archive.GetEntry(path) is { } entry) return Bytes(entry);
        var embedded = reader.ReadDebugDirectory().Where(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb).ToArray();
        if (embedded.Length != 1) throw new InvalidDataException($"No portable PDB for '{path}'.");
        var data = reader.GetSectionData(embedded[0].DataRelativeVirtualAddress).GetContent(0, embedded[0].DataSize).ToArray();
        if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual("MPDB"u8)) throw new InvalidDataException("Invalid embedded PDB header.");
        using var compressed = new MemoryStream(data, 8, data.Length - 8);
        using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        if (output.Length != System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4, 4))) throw new InvalidDataException("Invalid embedded PDB size.");
        return output.ToArray();
    }
    private static void Validate(byte[] bytes, PEReader reader)
    {
        using var stream = new MemoryStream(bytes);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var header = provider.GetMetadataReader().DebugMetadataHeader ?? throw new InvalidDataException("Missing portable PDB header.");
        var id = new BlobContentId(header.Id);
        var entries = reader.ReadDebugDirectory();
        var code = entries.Where(e => e.Type == DebugDirectoryEntryType.CodeView).ToArray();
        if (code.Length != 1 || reader.ReadCodeViewDebugDirectoryData(code[0]).Guid != id.Guid || code[0].Stamp != id.Stamp) throw new InvalidDataException("Portable PDB does not match assembly.");
        var checksums = entries.Where(e => e.Type == DebugDirectoryEntryType.PdbChecksum).ToArray();
        if (checksums.Length != 1) throw new InvalidDataException("Missing PDB checksum.");
        var checksum = reader.ReadPdbChecksumDebugDirectoryData(checksums[0]);
        if (checksum.AlgorithmName != "SHA256") throw new InvalidDataException("Unsupported PDB checksum.");
        var hashBytes = (byte[])bytes.Clone();
        Array.Clear(hashBytes, header.IdStartOffset, header.Id.Length);
        if (!SHA256.HashData(hashBytes).AsSpan().SequenceEqual(checksum.Checksum.AsSpan())) throw new InvalidDataException("PDB checksum mismatch.");
    }
}
