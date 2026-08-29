[CmdletBinding(DefaultParameterSetName = 'CertificateStore')]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$CatalogSequence,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Za-z._-]{1,64}$')]
    [string]$SigningKeyId,

    [Parameter(Mandatory = $true)]
    [Uri]$AssetBaseUri,

    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$LockPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$MinimumDesktopVersion = '0.11.0',
    [string]$NodeVersionRange = '>=20 <25',
    [int]$RuntimeProtocol = 1,
    [DateTimeOffset]$PublishedAt = [DateTimeOffset]::UtcNow,
    [string]$PreviousCatalogPath,
    [string]$PreviousSignaturePath,

    [Parameter(Mandatory = $true, ParameterSetName = 'CertificateStore')]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory = $true, ParameterSetName = 'ExternalPfx')]
    [string]$SigningCertificatePath,

    [Parameter(ParameterSetName = 'ExternalPfx')]
    [Security.SecureString]$SigningCertificatePassword
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$utf8NoBom = New-Object Text.UTF8Encoding($false)

function Assert-RegularFile([string]$Path, [long]$MaximumBytes) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "A regular non-reparse file is required: $Path"
    }
    if ($item.Length -le 0 -or $item.Length -gt $MaximumBytes) {
        throw "File size is outside the allowed range: $Path ($($item.Length) bytes)."
    }
    return $item.FullName
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-DetachedSignature([byte[]]$CatalogBytes, [byte[]]$SignatureBytes, $Certificate) {
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($Certificate)
    if ($null -eq $rsa) { throw 'The signing certificate does not contain an RSA public key.' }
    try {
        return $rsa.VerifyData(
            $CatalogBytes,
            $SignatureBytes,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    }
    finally { $rsa.Dispose() }
}

if (-not $AssetBaseUri.IsAbsoluteUri -or
    $AssetBaseUri.Scheme -ne 'https' -or
    $AssetBaseUri.UserInfo -or
    $AssetBaseUri.Fragment) {
    throw 'AssetBaseUri must be an absolute HTTPS URI without user information or a fragment.'
}
if ($NodeVersionRange -notmatch '^>=[0-9]+(?:\.[0-9]+(?:\.[0-9]+)?)? <[0-9]+(?:\.[0-9]+(?:\.[0-9]+)?)?$') {
    throw "Unsupported Node version range: $NodeVersionRange"
}
if ($RuntimeProtocol -lt 1) { throw 'RuntimeProtocol must be positive.' }

$PackagePath = Assert-RegularFile $PackagePath 65536
$LockPath = Assert-RegularFile $LockPath 4194304
$package = Get-Content -LiteralPath $PackagePath -Raw | ConvertFrom-Json
$lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
$packageVersion = $package.dependencies.'@deepseek-ai/dsh'
$lockVersion = $lock.packages.''.dependencies.'@deepseek-ai/dsh'
if ($packageVersion -ne $Version -or $lock.lockfileVersion -ne 3 -or $lockVersion -ne $Version) {
    throw "package.json and package-lock.json must pin @deepseek-ai/dsh@$Version with lockfileVersion 3."
}

if ($PSCmdlet.ParameterSetName -eq 'CertificateStore') {
    $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    $certificate = Get-ChildItem -Path Cert:\CurrentUser\My |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
        Select-Object -First 1
    if ($null -eq $certificate) {
        throw "Signing certificate was not found in Cert:\CurrentUser\My: $normalizedThumbprint"
    }
}
else {
    $SigningCertificatePath = [IO.Path]::GetFullPath($SigningCertificatePath)
    $repositoryPrefix = $repositoryRoot.TrimEnd('\') + '\'
    if ($SigningCertificatePath.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The signing certificate/private key path must be outside the repository.'
    }
    $SigningCertificatePath = Assert-RegularFile $SigningCertificatePath 1048576
    $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    if ($null -eq $SigningCertificatePassword) {
        $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2($SigningCertificatePath)
    }
    else {
        $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2(
            $SigningCertificatePath,
            $SigningCertificatePassword,
            $flags)
    }
}

try {
    $privateRsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    if ($null -eq $privateRsa -or $privateRsa.KeySize -lt 3072) {
        throw 'The signing certificate must contain an RSA private key of at least 3072 bits.'
    }

    $previousEntries = @()
    if ($PreviousCatalogPath -or $PreviousSignaturePath) {
        if (-not $PreviousCatalogPath -or -not $PreviousSignaturePath) {
            throw 'PreviousCatalogPath and PreviousSignaturePath must be supplied together.'
        }
        $PreviousCatalogPath = Assert-RegularFile $PreviousCatalogPath 262144
        $PreviousSignaturePath = Assert-RegularFile $PreviousSignaturePath 1024
        $previousBytes = [IO.File]::ReadAllBytes($PreviousCatalogPath)
        $previousSignature = [IO.File]::ReadAllBytes($PreviousSignaturePath)
        if (-not (Test-DetachedSignature $previousBytes $previousSignature $certificate)) {
            throw 'The previous catalog signature is invalid for the selected certificate.'
        }
        $previous = $utf8NoBom.GetString($previousBytes) | ConvertFrom-Json
        if ($previous.schemaVersion -ne 1 -or
            [long]$previous.catalogSequence -ge $CatalogSequence -or
            $previous.signingKeyId -ne $SigningKeyId) {
            throw 'The previous catalog schema, sequence, or signingKeyId is incompatible.'
        }
        $previousEntries = @($previous.entries | Where-Object { $_.version -ne $Version })
    }

    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $assetDirectory = Join-Path $OutputDirectory (Join-Path 'assets' $Version)
    [IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
    $outputPackage = Join-Path $assetDirectory 'package.json'
    $outputLock = Join-Path $assetDirectory 'package-lock.json'
    Copy-Item -LiteralPath $PackagePath -Destination $outputPackage -Force
    Copy-Item -LiteralPath $LockPath -Destination $outputLock -Force

    $base = $AssetBaseUri.AbsoluteUri.TrimEnd('/')
    $entry = [ordered]@{
        version = $Version
        publishedAt = $PublishedAt.ToUniversalTime().ToString('o')
        runtimeProtocol = $RuntimeProtocol
        minimumDesktopVersion = $MinimumDesktopVersion
        nodeVersionRange = $NodeVersionRange
        revoked = $false
        package = [ordered]@{
            url = "$base/$Version/package.json"
            bytes = (Get-Item -LiteralPath $outputPackage).Length
            sha256 = Get-Sha256 $outputPackage
        }
        lock = [ordered]@{
            url = "$base/$Version/package-lock.json"
            bytes = (Get-Item -LiteralPath $outputLock).Length
            sha256 = Get-Sha256 $outputLock
        }
    }
    $entries = @($entry) + $previousEntries
    if ($entries.Count -gt 64) { throw 'Catalog entry count exceeds 64.' }
    $catalog = [ordered]@{
        schemaVersion = 1
        catalogSequence = $CatalogSequence
        generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
        signingKeyId = $SigningKeyId
        entries = $entries
    }
    $catalogBytes = $utf8NoBom.GetBytes(($catalog | ConvertTo-Json -Depth 10 -Compress))
    if ($catalogBytes.Length -gt 262144) { throw 'Catalog exceeds 256 KiB.' }
    $signatureBytes = $privateRsa.SignData(
        $catalogBytes,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    if (-not (Test-DetachedSignature $catalogBytes $signatureBytes $certificate)) {
        throw 'Generated detached signature failed immediate verification.'
    }
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'catalog-v1.json'), $catalogBytes)
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'catalog-v1.sig'), $signatureBytes)
    Write-Host "Created signed DSH catalog sequence $CatalogSequence for $Version in $OutputDirectory"
}
finally {
    if ($null -ne $privateRsa) { $privateRsa.Dispose() }
    if ($PSCmdlet.ParameterSetName -eq 'ExternalPfx' -and $null -ne $certificate) {
        $certificate.Dispose()
    }
}
