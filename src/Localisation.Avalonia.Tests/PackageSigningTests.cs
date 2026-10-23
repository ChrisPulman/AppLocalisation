// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers.Binary;
using System.Formats.Asn1;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using SafeSign.Formats.NuGet;
using SafeSign.Signing;

namespace Localisation.Avalonia.Tests;

/// <summary>Exercises package signing using an ephemeral local key without modifying certificate stores.</summary>
internal sealed class PackageSigningTests
{
    private const string SignaturePath = ".signature.p7s";

    private const string OrdinaryPath = "lib/net10.0/Ordinary.dll";

    /// <summary>Verifies payload signatures, unchanged auxiliary entries, and reuse of one provider across packages.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task SignAsyncSignsPayloadsBeforeContainersWithOneSession()
    {
        using var fixture = new SigningFixture();
        var first = fixture.CreatePackage("First");
        var second = fixture.CreatePackage("Second");
        const int expectedTotalSignatures = 6;
        var signer = new NuGetFileSigner();
        var session = fixture.Session;
        foreach (var package in new[] { first, second })
        {
            await signer.SignAsync(new(package, package, session), CancellationToken.None);
        }

        await Assert.That(fixture.SignCount).IsEqualTo(expectedTotalSignatures);
        await Assert.That(fixture.CertificateRequests).IsEqualTo(0);
        foreach (var package in new[] { first, second })
        {
            await using var archive = await ZipFile.OpenReadAsync(package);
            await VerifyCmsAsync(await ReadEntryAsync(archive, SignaturePath), fixture.Certificate);
            await VerifyPeAsync(await ReadEntryAsync(archive, OrdinaryPath), fixture.Certificate);
            await VerifyPeAsync(await ReadEntryAsync(archive, "analyzers/dotnet/cs/Analyzer.dll"), fixture.Certificate);
            await Assert.That(await ReadEntryAsync(archive, "lib/net10.0/Ordinary.pdb")).IsEquivalentTo(SigningFixture.AuxiliaryBytes);
            await Assert.That(await ReadEntryAsync(archive, "data/readme.txt")).IsEquivalentTo(SigningFixture.AuxiliaryBytes);
        }
    }

    /// <summary>Verifies a provider failure never publishes a partially signed package.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task SignAsyncProviderFailurePreservesOriginalPackage()
    {
        using var fixture = new SigningFixture();
        var package = fixture.CreatePackage("Rejected");
        var original = await File.ReadAllBytesAsync(package);
        const int rejectedSignature = 2;
        fixture.FailOnSignature = rejectedSignature;
        var signer = new NuGetFileSigner();
        await Assert.That(async () => await signer.SignAsync(new(package, package, fixture.Session), CancellationToken.None)).Throws<CryptographicException>();
        await Assert.That(await File.ReadAllBytesAsync(package)).IsEquivalentTo(original);
        await Assert.That(fixture.SignCount).IsEqualTo(rejectedSignature);
        await using var archive = await ZipFile.OpenReadAsync(package);
        await Assert.That(archive.GetEntry(SignaturePath)).IsNull();
    }

    /// <summary>Verifies invalid archive inputs fail closed without publishing changes.</summary>
    /// <param name="scenario">The invalid input shape.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("malformed")]
    internal async Task SignAsyncInvalidPackagePreservesOriginal(string scenario)
    {
        using var fixture = new SigningFixture();
        var package = fixture.CreatePackage("Invalid", scenario);
        var original = await File.ReadAllBytesAsync(package);
        var signer = new NuGetFileSigner();
        await Assert.That(async () => await signer.SignAsync(new(package, package, fixture.Session), CancellationToken.None)).Throws<InvalidDataException>();
        await Assert.That(await File.ReadAllBytesAsync(package)).IsEquivalentTo(original);
        await Assert.That(fixture.SignCount).IsEqualTo(0);
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchive archive, string name)
    {
        await using var stream = await archive.GetEntry(name)!.OpenAsync();
        await using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        return output.ToArray();
    }

    private static async Task VerifyPeAsync(byte[] image, X509Certificate2 certificate)
    {
        const int coffHeaderSize = 24;
        const int pe32PlusDirectories = 112;
        const int pe32Directories = 96;
        const int securityDirectoryOffset = 32;
        const int certificateHeaderSize = 8;
        const int checksumOffset = 64;
        var pe = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3c));
        var optional = pe + coffHeaderSize;
        var directories = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(optional)) == 0x20b ? pe32PlusDirectories : pe32Directories;
        var directory = optional + directories + securityDirectoryOffset;
        var offset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(directory));
        var length = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(offset));
        await Assert.That(offset).IsGreaterThan(0);
        var encoded = image.AsSpan(offset + certificateHeaderSize, length - certificateHeaderSize).ToArray();
        await VerifyCmsAsync(encoded, certificate);
        var cms = new SignedCms();
        cms.Decode(encoded);
        var content = new AsnReader(cms.ContentInfo.Content, AsnEncodingRules.BER).ReadSequence();
        _ = content.ReadEncodedValue();
        var digestInfo = content.ReadSequence();
        _ = digestInfo.ReadSequence();
        var expectedDigest = digestInfo.ReadOctetString();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(image.AsSpan(0, optional + checksumOffset));
        hash.AppendData(image.AsSpan(optional + checksumOffset + sizeof(uint), directory - optional - checksumOffset - sizeof(uint)));
        hash.AppendData(image.AsSpan(directory + certificateHeaderSize, offset - directory - certificateHeaderSize));
        await Assert.That(hash.GetHashAndReset()).IsEquivalentTo(expectedDigest);
    }

    private static async Task VerifyCmsAsync(byte[] encoded, X509Certificate2 certificate)
    {
        var cms = new SignedCms();
        cms.Decode(encoded);
        cms.CheckSignature(verifySignatureOnly: true);
        await Assert.That(cms.SignerInfos.Count).IsEqualTo(1);
        await Assert.That(cms.SignerInfos[0].Certificate!.RawData).IsEquivalentTo(certificate.RawData);
    }

    /// <summary>Owns a temporary package directory and an RSA key held exclusively in memory.</summary>
    private sealed class SigningFixture : ISigningProvider, IDisposable
    {
        internal static readonly byte[] AuxiliaryBytes = [1, 4, 9, 16, 25];

        private readonly RSA _key = RSA.Create(2048);

        internal SigningFixture()
        {
            DirectoryPath = Directory.CreateTempSubdirectory("localisation-signing-test-").FullName;
            var request = new CertificateRequest("CN=Localisation Signing Test", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var privateCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            Certificate = X509CertificateLoader.LoadCertificate(privateCertificate.Export(X509ContentType.Cert));
        }

        public string Name => "ephemeral-test";

        internal string DirectoryPath { get; }

        internal X509Certificate2 Certificate { get; }

        internal SigningSession Session => new(this, new(Certificate, []), HashAlgorithmName.SHA256, null, "Tests", null, TimeProvider.System);

        internal int SignCount { get; private set; }

        internal int? FailOnSignature { get; set; }

        internal int CertificateRequests { get; private set; }

        public ValueTask<SigningCertificate> GetCertificateAsync(CancellationToken cancellationToken)
        {
            CertificateRequests++;
            return ValueTask.FromResult(new SigningCertificate(Certificate, []));
        }

        public ValueTask<byte[]> SignDigestAsync(SignDigestRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SignCount++;
            if (SignCount == FailOnSignature)
            {
                throw new CryptographicException("Injected signing provider failure.");
            }

            return ValueTask.FromResult(_key.SignHash(request.Digest.Span, request.HashAlgorithm, RSASignaturePadding.Pkcs1));
        }

        public void Dispose()
        {
            Certificate.Dispose();
            _key.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
        }

        internal string CreatePackage(string name, string? scenario = null)
        {
            var path = Path.Combine(DirectoryPath, $"Localisation.{name}.1.0.0.nupkg");
            if (scenario == "malformed")
            {
                File.WriteAllBytes(path, AuxiliaryBytes);
                return path;
            }

            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var writer = new StreamWriter(archive.CreateEntry($"Localisation.{name}.nuspec").Open()))
            {
                writer.Write($"<package><metadata><id>Localisation.{name}</id><version>1.0.0</version>" +
                    "<authors>Tests</authors><description>Signing fixture</description></metadata></package>");
            }

            AddEntry(archive, "lib/net10.0/Ordinary.pdb", AuxiliaryBytes);
            AddEntry(archive, "data/readme.txt", AuxiliaryBytes);
            var image = scenario == "empty-dll" ? [] : File.ReadAllBytes(typeof(PackageSigningTests).Assembly.Location);
            AddEntry(archive, OrdinaryPath, image);
            AddEntry(archive, "analyzers/dotnet/cs/Analyzer.dll", image);

            return path;
        }

        private static void AddEntry(ZipArchive archive, string name, byte[] bytes)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }
}
