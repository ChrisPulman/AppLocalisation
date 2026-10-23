// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using Localisation.ReleaseSigning;
using SafeSign.Providers;
using SafeSign.Signing;

if (args.Length != 2)
{
    throw new ArgumentException("Usage: ReleaseSigning <packages-directory> <certificate-sha256>");
}

var services = new ProviderServices();

var settings = new ProviderSettings([with(comparer: StringComparer.OrdinalIgnoreCase)], Environment.GetEnvironmentVariable);

var provider = ProviderCatalog.Find("certum")!.Create!(settings, services);

try
{
    var certificate = await provider.GetCertificateAsync(CancellationToken.None);
    if (!string.Equals(certificate.Certificate.GetCertHashString(HashAlgorithmName.SHA256), args[1], StringComparison.OrdinalIgnoreCase))
    {
        throw new CryptographicException("The Certum certificate does not match the expected SHA256 fingerprint.");
    }

    var session = new SigningSession(
        provider,
        certificate,
        HashAlgorithmName.SHA256,
        new TimestampSettings(new Uri("http://time.certum.pl"), HashAlgorithmName.SHA256),
        "AppLocalisation",
        new Uri("https://github.com/ChrisPulman/AppLocalisation"),
        services.Time);
    await PackageSigning.SignAsync(args[0], session, path => PortableSignature.VerifyAsync(path, args[1]));
}
finally
{
    (provider as IDisposable)?.Dispose();
}
