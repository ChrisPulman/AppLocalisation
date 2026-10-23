// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;

namespace Localisation.ReleaseSigning;

/// <summary>Checks signed PE integrity before staging; Windows remains responsible for trust policy.</summary>
internal static class PortableSignature
{
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    private const int CertificateHeaderSize = 8;

    private const int CertificateAlignmentMask = 7;

    private const int RevisionOffset = 4;

    private const int TypeOffset = 6;

    private const int Pkcs7Type = 2;

    private const int ChecksumOffset = 64;

    private const int Pe32PlusDirectories = 112;

    private const int Pe32Directories = 96;

    private const int SecurityDirectoryOffset = 32;

    internal static async Task VerifyAsync(string path, string fingerprint)
    {
        var image = await File.ReadAllBytesAsync(path);
        await using var stream = new MemoryStream(image, writable: false);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        var optional = headers.PEHeader ?? throw new InvalidDataException("Expected a PE image.");
        var cms = DecodeSignature(image, optional);
        VerifySigner(cms, fingerprint);
        var expected = ReadDigest(cms);
        VerifyDigest(image, headers, expected);
        VerifyTimestamp(cms.SignerInfos[0]);
    }

    private static SignedCms DecodeSignature(byte[] image, PEHeader optional)
    {
        var certificate = optional.CertificateTableDirectory;
        var offset = certificate.RelativeVirtualAddress;
        if (offset <= 0 || certificate.Size < CertificateHeaderSize || offset > image.Length - certificate.Size || offset + certificate.Size != image.Length)
        {
            throw new InvalidDataException("Expected a final Authenticode certificate table.");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(offset));
        if (length < CertificateHeaderSize || length > certificate.Size ||
            ((length + (long)CertificateAlignmentMask) & ~(long)CertificateAlignmentMask) != certificate.Size ||
            BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(offset + RevisionOffset)) != 0x200 ||
            BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(offset + TypeOffset)) != Pkcs7Type)
        {
            throw new InvalidDataException("Expected one PKCS7 Authenticode signature.");
        }

        var cms = new SignedCms();
        cms.Decode(image.AsSpan(offset + CertificateHeaderSize, length - CertificateHeaderSize));
        return cms;
    }

    private static void VerifySigner(SignedCms cms, string fingerprint)
    {
        cms.CheckSignature(verifySignatureOnly: true);
        if (cms.ContentInfo.ContentType.Value != "1.3.6.1.4.1.311.2.1.4" || cms.SignerInfos.Count != 1)
        {
            throw new CryptographicException("Expected one Authenticode signer.");
        }

        var signer = cms.SignerInfos[0];
        var actual = signer.Certificate?.GetCertHashString(HashAlgorithmName.SHA256);
        if (string.Equals(actual, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new CryptographicException("The Authenticode signer does not match the expected SHA256 fingerprint.");
    }

    private static byte[] ReadDigest(SignedCms cms)
    {
        var content = new AsnReader(cms.ContentInfo.Content, AsnEncodingRules.BER).ReadSequence();
        var descriptor = content.ReadSequence();
        if (descriptor.ReadObjectIdentifier() != "1.3.6.1.4.1.311.2.1.15")
        {
            throw new CryptographicException("Expected a PE content descriptor.");
        }

        var digestInfo = content.ReadSequence();
        var algorithm = digestInfo.ReadSequence();
        if (algorithm.ReadObjectIdentifier() != Sha256Oid || cms.SignerInfos[0].DigestAlgorithm.Value != Sha256Oid)
        {
            throw new CryptographicException("Expected SHA256 Authenticode digests.");
        }

        var expected = digestInfo.ReadOctetString();
        digestInfo.ThrowIfNotEmpty();
        content.ThrowIfNotEmpty();
        return expected;
    }

    private static void VerifyDigest(byte[] image, PEHeaders headers, byte[] expected)
    {
        var optional = headers.PEHeader!;
        var offset = optional.CertificateTableDirectory.RelativeVirtualAddress;
        var optionalOffset = headers.PEHeaderStartOffset;
        var checksum = optionalOffset + ChecksumOffset;
        var security = optionalOffset + (optional.Magic == PEMagic.PE32Plus ? Pe32PlusDirectories : Pe32Directories) + SecurityDirectoryOffset;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(image.AsSpan(0, checksum));
        hash.AppendData(image.AsSpan(checksum + sizeof(uint), security - checksum - sizeof(uint)));
        hash.AppendData(image.AsSpan(security + CertificateHeaderSize, optional.SizeOfHeaders - security - CertificateHeaderSize));
        var hashed = optional.SizeOfHeaders;
        foreach (var section in headers.SectionHeaders.OrderBy(section => section.PointerToRawData))
        {
            if (section.SizeOfRawData == 0)
            {
                continue;
            }

            if (section.PointerToRawData != hashed || section.SizeOfRawData < 0 || section.SizeOfRawData > offset - hashed)
            {
                throw new InvalidDataException("Expected contiguous, non-overlapping PE sections.");
            }

            hash.AppendData(image.AsSpan(section.PointerToRawData, section.SizeOfRawData));
            hashed += section.SizeOfRawData;
        }

        hash.AppendData(image.AsSpan(hashed, offset - hashed));
        if (CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expected))
        {
            return;
        }

        throw new CryptographicException("The PE content does not match its signed SHA256 digest.");
    }

    private static void VerifyTimestamp(SignerInfo signer)
    {
        foreach (var attribute in signer.UnsignedAttributes)
        {
            if (attribute.Oid.Value != "1.3.6.1.4.1.311.3.3.1")
            {
                continue;
            }

            foreach (var value in attribute.Values)
            {
                if (Rfc3161TimestampToken.TryDecode(value.RawData, out var token, out var consumed) && consumed == value.RawData.Length &&
                    token.VerifySignatureForSignerInfo(signer, out _))
                {
                    return;
                }
            }
        }

        throw new CryptographicException("A valid RFC3161 timestamp bound to the Authenticode signature is required.");
    }
}
