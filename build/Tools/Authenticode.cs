using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Localisation.BuildTools;

internal static partial class Authenticode
{
    internal static void Verify(string path, string certificateFingerprint)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Authenticode verification requires Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateFingerprint);
        var expected = Convert.FromHexString(certificateFingerprint);
        if (expected.Length != 32)
            throw new ArgumentException("The certificate fingerprint must be SHA256.", nameof(certificateFingerprint));

        // Keep the file open without write/delete sharing throughout trust validation.
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var name = Marshal.StringToCoTaskMemUni(Path.GetFullPath(path));
        var infoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoData>());
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(),
            UiChoice = 2,
            UnionChoice = 1,
            File = infoPointer,
            StateAction = 1,
        };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            Marshal.StructureToPtr(new FileInfoData
            {
                Size = (uint)Marshal.SizeOf<FileInfoData>(),
                Path = name,
                Handle = file.SafeFileHandle.DangerousGetHandle(),
            }, infoPointer, false);
            var result = NativeMethods.WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (result != 0)
                throw new CryptographicException($"Invalid Authenticode signature for '{path}': 0x{result:X8}.");
            var provider = NativeMethods.WTHelperProvDataFromStateData(data.StateData);
            var signerPointer = NativeMethods.WTHelperGetProvSignerFromChain(provider, 0, 0, 0);
            if (signerPointer == IntPtr.Zero)
                throw new CryptographicException("The trusted signature has no signer.");
            var signer = Marshal.PtrToStructure<ProviderSigner>(signerPointer);
            if (signer.Error != 0 || signer.CertificateCount == 0 || signer.Certificates == IntPtr.Zero)
                throw new CryptographicException("The signature signer is invalid.");
            var cert = Marshal.PtrToStructure<ProviderCertificate>(signer.Certificates);
            using var certificate = new X509Certificate2(cert.Certificate);
            if (!CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(HashAlgorithmName.SHA256), expected))
                throw new CryptographicException($"Unexpected signing certificate for '{path}'.");
            var timestamp = NativeMethods.WTHelperGetProvSignerFromChain(provider, 0, 1, 0);
            if (signer.CounterSignerCount == 0 || timestamp == IntPtr.Zero ||
                Marshal.PtrToStructure<ProviderSigner>(timestamp).Error != 0)
                throw new CryptographicException($"Missing trusted timestamp for '{path}'.");
            Console.WriteLine($"Verified Authenticode signature and timestamp: {path} ({certificateFingerprint})");
        }
        finally
        {
            data.StateAction = 2;
            _ = NativeMethods.WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.FreeHGlobal(infoPointer);
            Marshal.FreeCoTaskMem(name);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfoData
    {
        internal uint Size;
        internal IntPtr Path, Handle, KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        internal uint Size;
        internal IntPtr PolicyCallback, SipClient;
        internal uint UiChoice, RevocationChecks, UnionChoice;
        internal IntPtr File;
        internal uint StateAction;
        internal IntPtr StateData, Url;
        internal uint ProviderFlags, UiContext;
        internal IntPtr SignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderSigner
    {
        internal uint Size;
        internal System.Runtime.InteropServices.ComTypes.FILETIME VerifyAsOf;
        internal uint CertificateCount;
        internal IntPtr Certificates;
        internal uint SignerType;
        internal IntPtr Signer;
        internal uint Error, CounterSignerCount;
        internal IntPtr CounterSigners, ChainContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderCertificate
    {
        internal uint Size;
        internal IntPtr Certificate;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("wintrust.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

        [LibraryImport("wintrust.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial IntPtr WTHelperProvDataFromStateData(IntPtr state);

        [LibraryImport("wintrust.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, int counterSigner, uint counterSignerIndex);
    }
}
