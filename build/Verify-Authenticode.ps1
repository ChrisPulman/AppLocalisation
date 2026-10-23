[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,
    [Parameter(Mandatory = $true)]
    [string] $CertificateFingerprint
)

$ErrorActionPreference = 'Stop'
$signature = Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.Status -ne 'Valid') {
    throw "Invalid Authenticode signature for '$Path': $($signature.Status) ($($signature.StatusMessage))"
}
if ($null -eq $signature.TimeStamperCertificate) {
    throw "Missing trusted timestamp for '$Path'."
}
$actual = $signature.SignerCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
if ($actual -ne $CertificateFingerprint) {
    throw "Unexpected Authenticode signer for '$Path': $actual"
}
Write-Host "Verified Authenticode signature and timestamp: $Path ($actual)"
