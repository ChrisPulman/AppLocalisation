# Release signing

`BuildDeploy.yml` builds, tests and packs unsigned release artifacts before a separate Ubuntu signing job. It installs the pinned SafeSign 1.1.0 CLI. Signing credentials are available only to the signing step, which passes all packages to one command so the batch reuses one Certum provider/session.

SafeSign 1.1.0 recursively Authenticode-signs unsigned DLLs and EXEs inside each NuGet package before signing the enclosing container. This includes source-generator analyzer DLLs. SHA-256 and RFC 3161 timestamps are requested. Already-signed payloads are retained by SafeSign; the final verification gate still requires every DLL to be trusted, timestamped and signed by the expected release certificate. No custom signing implementation or strong-name identity change is needed.

The runner split follows controlled authentication probes: the unchanged SafeSign CLI/.NET 11 succeeded on Ubuntu and failed at the first Certum CAS login on Windows using the same credentials. Signing therefore runs in the previously working Ubuntu environment, while Windows remains the authoritative trust-policy gate.

An independent Windows job runs `Verify-SignedPackages.ps1`, verifies each final NuGet signature with `dotnet nuget verify --all --certificate-fingerprint`, extracts every packaged DLL into an isolated temporary directory, and checks Windows Authenticode trust, timestamp and certificate pin. Failed, missing, untrusted, untimestamped or unexpected-signer signatures stop publication. Archive entry paths are never used as filesystem paths.

Signing artifacts are uploaded as `pending-signature-verification`; only successful Windows verification produces the `signed-nuget` artifact consumed by publication. A signing failure prevents artifact upload. Portable PDBs, metadata and prepared `.snupkg` files are preserved.

The public SHA-256 certificate pin is in the workflow's `CERTUM_CERT_FINGERPRINT`. Certificate rotation requires deliberately updating that pin and registering the replacement certificate with NuGet.org. Existing `CERTUM_USER_ID` and `CERTUM_OTP_URI` secrets are reused; private keys remain with Certum.

To independently verify downloaded release artifacts on Windows:

```powershell
./build/Verify-SignedPackages.ps1 -PackagesDirectory ./packages -CertificateFingerprint <SHA256-of-release-certificate>
```

The TUnit signing regressions exercise the upstream SafeSign API with ephemeral RSA keys, checking nested DLL signatures, PE content digests, preserved PDB/data entries and atomic failure behavior without installing test certificates or accessing Certum.

Select `verifyOnly: true` when manually running `BuildDeploy` to exercise the complete build, signing and Windows verification path with protected signing credentials. NuGet publication and GitHub release creation are skipped; signature verification remains mandatory.
