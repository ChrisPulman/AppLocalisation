# Release signing

`BuildDeploy.yml` builds, tests and packs unsigned release artifacts before a separate Ubuntu signing job. Signing credentials are available only to the signing step. The signing tool uses the pinned SafeSign 1.0.0 libraries and one Certum provider/session for the entire batch. Controlled authentication probes succeeded on Ubuntu and failed at the first Certum CAS login on Windows, including with the unchanged SafeSign CLI on .NET 11.

The tool stages every `Localisation.*` NuGet package, Authenticode-signs every DLL entry (including analyzer and satellite paths) with SHA-256 and an RFC 3161 timestamp, and checks the CMS signature, PE content digest, expected signing certificate and timestamp binding. Only after all DLLs pass does it sign the package containers. Original artifacts are replaced only after the entire signing batch succeeds.

An independent Windows job runs `Verify-SignedPackages.ps1`, verifies each final NuGet signature with `dotnet nuget verify --all --certificate-fingerprint`, extracts each packaged DLL into an isolated temporary directory, and checks Windows Authenticode trust, timestamp and certificate pin. Failed, missing, untrusted, untimestamped or unexpected-signer signatures stop publication. DLLs are extracted to generated paths rather than trusting ZIP entry paths. Signing artifacts are uploaded as `pending-signature-verification`; only the successful Windows verification job produces the `signed-nuget` artifact used by publication.

The public SHA-256 certificate pin is in the workflow's `CERTUM_CERT_FINGERPRINT`. Certificate rotation requires deliberately updating that pin and registering the replacement certificate with NuGet.org. Existing `CERTUM_USER_ID` and `CERTUM_OTP_URI` secrets are reused. Private keys remain with Certum. No strong-name identity change is introduced.

Portable PDBs, package metadata and other payloads are retained. Prepared `.snupkg` files remain unchanged; their existing symbol publication path is preserved.

To independently verify downloaded release artifacts on Windows:

```powershell
./build/Verify-SignedPackages.ps1 -PackagesDirectory ./packages -CertificateFingerprint <SHA256-of-release-certificate>
```

The TUnit suite tests real in-memory RSA signing of fixture DLLs and NuGet containers, preserved payloads, rejected input and verification failures without accessing Certum or installing test certificates. Actual Certum credentials are used only by the signing job; running tests or opening a PR does not publish a release.

Select `verifyOnly: true` when manually running `BuildDeploy` to exercise the complete build, signing and Windows verification path with protected signing credentials. Both package publication and GitHub release creation are skipped. This mode does not bypass any signature verification.
