// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers.Binary;
using System.Formats.Asn1;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Localisation.ReleaseSigning;

namespace Localisation.Avalonia.Tests;

/// <summary>Exercises portable PE verification with ephemeral signing and timestamp keys.</summary>
internal sealed class PortableSignatureTests
{
    /// <summary>Verifies a real local signature and bound RFC3161 timestamp without trusting a local root.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task VerifyAsyncAcceptsTimestampedSignature()
    {
        using var fixture = new SignatureFixture("valid");
        await PortableSignature.VerifyAsync(fixture.Path, fixture.Fingerprint);
        await Assert.That(File.Exists(fixture.Path)).IsTrue();
    }

    /// <summary>Rejects alterations, a different signer, and absent or unbound timestamps.</summary>
    /// <param name="scenario">The invalid signature scenario.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("tampered")]
    [Arguments("wrong-pin")]
    [Arguments("missing-timestamp")]
    [Arguments("unbound-timestamp")]
    [Arguments("invalid-timestamp")]
    internal async Task VerifyAsyncRejectsInvalidSignature(string scenario)
    {
        using var fixture = new SignatureFixture(scenario);
        await Assert.That(() => PortableSignature.VerifyAsync(fixture.Path, fixture.Fingerprint)).Throws<CryptographicException>();
    }

    /// <summary>Rejects unsigned PE files.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    internal async Task VerifyAsyncRejectsUnsignedImage()
    {
        await Assert.That(() => PortableSignature.VerifyAsync(typeof(PortableSignature).Assembly.Location, string.Empty)).Throws<InvalidDataException>();
    }

    /// <summary>Owns one temporary image and certificates created without accessing certificate stores.</summary>
    private sealed class SignatureFixture : IDisposable
    {
        private const string TimestampOid = "1.3.6.1.4.1.311.3.3.1";

        private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

        private const int KeySize = 2048;

        private const int FingerprintLength = 64;

        private const int Pkcs7Type = 2;

        private const int CertificateHeaderSize = 8;

        private const int AlignmentMask = 7;

        private const int ChecksumOffset = 64;

        private const int SecurityOffset = 128;

        private const int CoffHeaderSize = 24;

        internal SignatureFixture(string scenario)
        {
            using var key = RSA.Create(KeySize);
            using var certificate = CreateCertificate(key, false);
            Fingerprint = scenario == "wrong-pin" ? new string('0', FingerprintLength) : certificate.GetCertHashString(HashAlgorithmName.SHA256);
            var image = File.ReadAllBytes(typeof(PortableSignature).Assembly.Location);
            var optional = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3c)) + CoffHeaderSize;
            var security = optional + SecurityOffset;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(image.AsSpan(0, optional + ChecksumOffset));
            hash.AppendData(image.AsSpan(optional + ChecksumOffset + sizeof(uint), security - optional - ChecksumOffset - sizeof(uint)));
            hash.AppendData(image.AsSpan(security + CertificateHeaderSize));
            var cms = CreateSignature(hash.GetHashAndReset(), certificate);
            if (scenario != "missing-timestamp")
            {
                AddTimestamp(cms, scenario);
            }

            var encoded = cms.Encode();
            var length = encoded.Length + CertificateHeaderSize;
            var padded = (length + AlignmentMask) & ~AlignmentMask;
            var signed = new byte[image.Length + padded];
            image.CopyTo(signed, 0);
            BinaryPrimitives.WriteInt32LittleEndian(signed.AsSpan(security), image.Length);
            BinaryPrimitives.WriteInt32LittleEndian(signed.AsSpan(security + sizeof(uint)), padded);
            BinaryPrimitives.WriteInt32LittleEndian(signed.AsSpan(image.Length), length);
            BinaryPrimitives.WriteUInt16LittleEndian(signed.AsSpan(image.Length + sizeof(uint)), 0x200);
            BinaryPrimitives.WriteUInt16LittleEndian(signed.AsSpan(image.Length + sizeof(uint) + sizeof(ushort)), Pkcs7Type);
            encoded.CopyTo(signed, image.Length + CertificateHeaderSize);
            if (scenario == "tampered")
            {
                signed[image.Length - 1] ^= 1;
            }

            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(Path, signed);
        }

        internal string Path { get; }

        internal string Fingerprint { get; }

        public void Dispose() => File.Delete(Path);

        private static X509Certificate2 CreateCertificate(RSA key, bool timestamp)
        {
            var request = new CertificateRequest("CN=Ephemeral Signature Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            if (timestamp)
            {
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.8") }, true));
            }

            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        }

        private static SignedCms CreateSignature(byte[] digest, X509Certificate2 certificate)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier("1.3.6.1.4.1.311.2.1.15");
                }

                using (writer.PushSequence())
                {
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier(Sha256Oid);
                        writer.WriteNull();
                    }

                    writer.WriteOctetString(digest);
                }
            }

            var cms = new SignedCms(new ContentInfo(new Oid("1.3.6.1.4.1.311.2.1.4"), writer.Encode()));
            cms.ComputeSignature(new(certificate) { DigestAlgorithm = new(Sha256Oid) });
            return cms;
        }

        private static AsnEncodedData CreateCertificateAttribute(X509Certificate2 certificate)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            using (writer.PushSequence())
            using (writer.PushSequence())
            {
                writer.WriteOctetString(certificate.GetCertHash(HashAlgorithmName.SHA256));
            }

            return new(new Oid("1.2.840.113549.1.9.16.2.47"), writer.Encode());
        }

        private static void AddTimestamp(SignedCms cms, string scenario)
        {
            using var key = RSA.Create(KeySize);
            using var certificate = CreateCertificate(key, true);
            var signature = cms.SignerInfos[0].GetSignature();
            var digest = SHA256.HashData(scenario == "unbound-timestamp" ? [byte.MaxValue] : signature);
            var info = new Rfc3161TimestampTokenInfo(new Oid("1.2.3.4"), new Oid(Sha256Oid), digest, new byte[] { 1 }, DateTimeOffset.UtcNow);
            var token = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), info.Encode()));
            var signer = new CmsSigner(certificate) { DigestAlgorithm = new(Sha256Oid) };
            _ = signer.SignedAttributes.Add(CreateCertificateAttribute(certificate));
            token.ComputeSignature(signer);
            var encoded = token.Encode();
            if (scenario == "invalid-timestamp")
            {
                encoded[^1] ^= 1;
            }

            cms.SignerInfos[0].AddUnsignedAttribute(new(new Oid(TimestampOid), encoded));
        }
    }
}
