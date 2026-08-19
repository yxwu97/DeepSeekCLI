param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version,
    [switch]$SkipInteractiveWebView2,
    [switch]$SkipPrivateDshSmoke
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildProperties = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw)
$configuredVersion = [string]$buildProperties.Project.PropertyGroup.AppVersion
if ($configuredVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Directory.Build.props contains an invalid AppVersion: '$configuredVersion'."
}
if ($Version -and $Version -ne $configuredVersion) {
    throw "Requested version '$Version' does not match configured AppVersion '$configuredVersion'."
}
$Version = $configuredVersion

$solution = Join-Path $repositoryRoot 'DeepSeekHarnessDesktop.sln'
$unitProject = Join-Path $repositoryRoot 'tests\DeepSeekHarnessDesktop.UnitTests\DeepSeekHarnessDesktop.UnitTests.csproj'
$integrationProject = Join-Path $repositoryRoot 'tests\DeepSeekHarnessDesktop.IntegrationTests\DeepSeekHarnessDesktop.IntegrationTests.csproj'
$validationAssembly = Join-Path $repositoryRoot 'eng\Phase0Validation\bin\Release\net48\DeepSeekHarnessDesktop.Phase0Validation.exe'
$publishScript = Join-Path $PSScriptRoot 'Publish-Release.ps1'
$runtimeResourceDirectory = Join-Path $repositoryRoot 'eng\dsh-runtime'
$outputDirectory = Join-Path $repositoryRoot 'output'
$publishDirectory = Join-Path $outputDirectory "publish\$Version\$Runtime"
$executablePath = Join-Path $publishDirectory 'DeepSeekHarnessDesktop.exe'
$zipPath = Join-Path $outputDirectory "DeepSeekHarnessDesktop-$Version-$Runtime.zip"
$resultsDirectory = Join-Path $outputDirectory 'validation'
$reportPath = Join-Path $resultsDirectory "release-gate-$Version-$Runtime.json"
[IO.Directory]::CreateDirectory($resultsDirectory) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-LastExitCode([string]$Operation) {
    if ($LASTEXITCODE -ne 0) { throw "$Operation failed with exit code $LASTEXITCODE." }
}

function Read-TestSummary([string]$Path) {
    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $results = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
    $failed = @($results | Where-Object { $_.outcome -ne 'Passed' })
    if ($results.Count -eq 0 -or $failed.Count -ne 0) {
        throw "Invalid or failing test results in $Path."
    }
    return [ordered]@{ total = $results.Count; passed = $results.Count; failed = 0 }
}

function ConvertFrom-JsonDictionary([string]$Text) {
    $command = Get-Command ConvertFrom-Json
    if ($command.Parameters.ContainsKey('AsHashtable')) {
        return ConvertFrom-Json -InputObject $Text -AsHashtable
    }

    Add-Type -AssemblyName System.Web.Extensions
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength = 4MB
    return $serializer.DeserializeObject($Text)
}

Write-Host 'Building Debug solution...'
dotnet build $solution -c Debug --no-restore '-m:1'
Assert-LastExitCode 'Debug build'

Write-Host 'Building Release solution...'
dotnet build $solution -c $Configuration --no-restore '-m:1'
Assert-LastExitCode "$Configuration build"

$unitResults = Join-Path $resultsDirectory 'unit.trx'
$integrationResults = Join-Path $resultsDirectory 'integration.trx'
Write-Host 'Running unit tests...'
dotnet test $unitProject -c $Configuration --no-build --no-restore `
    --logger 'trx;LogFileName=unit.trx' --results-directory $resultsDirectory
Assert-LastExitCode 'Unit tests'
$unitSummary = Read-TestSummary $unitResults

Write-Host 'Running integration tests...'
dotnet test $integrationProject -c $Configuration --no-build --no-restore `
    --logger 'trx;LogFileName=integration.trx' --results-directory $resultsDirectory
Assert-LastExitCode 'Integration tests'
$integrationSummary = Read-TestSummary $integrationResults

$webViewResult = 'passed'
if ($SkipInteractiveWebView2) {
    $webViewResult = 'skipped'
    Write-Warning 'Skipping interactive WebView2 validation by explicit request.'
}
else {
    Write-Host 'Running interactive WebView2 validation...'
    & $validationAssembly --chat-webview-smoke
    Assert-LastExitCode 'Interactive WebView2 validation'
}

$privateDshResult = 'passed'
if ($SkipPrivateDshSmoke) {
    $privateDshResult = 'skipped'
    Write-Warning 'Skipping real private DSH installation smoke by explicit request.'
}
else {
    Write-Host 'Running real private DSH installation and reuse smoke...'
    & $validationAssembly --private-dsh-smoke
    Assert-LastExitCode 'Private DSH installation smoke'
}

Write-Host 'Publishing .NET Framework 4.8 release archive...'
& $publishScript -Configuration $Configuration -Runtime $Runtime
Assert-LastExitCode 'Release publish'

foreach ($required in @(
    $executablePath,
    (Join-Path $publishDirectory 'DeepSeekHarnessDesktop.exe.config'),
    (Join-Path $publishDirectory 'README.md'),
    (Join-Path $publishDirectory 'dsh-runtime\package.json'),
    (Join-Path $publishDirectory 'dsh-runtime\package-lock.json'),
    $zipPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required release file was not found: $required"
    }
}

$sourcePackagePath = Join-Path $runtimeResourceDirectory 'package.json'
$sourceLockPath = Join-Path $runtimeResourceDirectory 'package-lock.json'
$publishedPackagePath = Join-Path $publishDirectory 'dsh-runtime\package.json'
$publishedLockPath = Join-Path $publishDirectory 'dsh-runtime\package-lock.json'
$metadataPath = Join-Path $repositoryRoot 'src\DeepSeekHarnessDesktop\Utilities\DshPackageMetadata.cs'
$metadataText = Get-Content -LiteralPath $metadataPath -Raw
if ($metadataText -notmatch 'ValidatedVersion\s*=\s*"([^"]+)"') {
    throw 'Unable to read the validated DSH version from DshPackageMetadata.cs.'
}
$validatedDshVersion = $Matches[1]
$sourcePackage = ConvertFrom-JsonDictionary (Get-Content -LiteralPath $sourcePackagePath -Raw)
$sourceLock = ConvertFrom-JsonDictionary (Get-Content -LiteralPath $sourceLockPath -Raw)
$packageVersion = $sourcePackage['dependencies']['@deepseek-ai/dsh']
$lockVersion = $sourceLock['packages']['']['dependencies']['@deepseek-ai/dsh']
if ($packageVersion -ne $validatedDshVersion -or $lockVersion -ne $validatedDshVersion) {
    throw "DSH runtime resources do not pin validated version $validatedDshVersion."
}
foreach ($pair in @(
    @($sourcePackagePath, $publishedPackagePath),
    @($sourceLockPath, $publishedLockPath))) {
    $sourceHash = (Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash
    $publishedHash = (Get-FileHash -LiteralPath $pair[1] -Algorithm SHA256).Hash
    if ($sourceHash -ne $publishedHash) {
        throw "Published DSH runtime resource hash differs from source: $($pair[1])"
    }
}

$versionInfo = (Get-Item -LiteralPath $executablePath).VersionInfo
if ($versionInfo.FileVersion -ne "$Version.0" -or $versionInfo.ProductVersion -ne $Version) {
    throw "Unexpected executable version '$($versionInfo.FileVersion)' / '$($versionInfo.ProductVersion)'."
}

$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try { $entries = @($archive.Entries | ForEach-Object FullName | Sort-Object) }
finally { $archive.Dispose() }
$forbidden = @(
    'managed-runtime/', 'node_modules/', 'node.exe', 'npm.cmd', 'npx.cmd', '.npmrc', 'settings.json',
    '.git/', 'WebView2/', '/logs/', '.pdb', '.runtimeconfig.json', '.deps.json',
    'coreclr.dll', 'clrjit.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.NETCore.App'
)
foreach ($entry in $entries) {
    foreach ($needle in $forbidden) {
        if ($entry.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Forbidden release entry detected: $entry"
        }
    }
}

$zip = Get-Item -LiteralPath $zipPath
$executable = Get-Item -LiteralPath $executablePath
$maximumArchiveBytes = 30MB
$maximumExecutableBytes = 5MB
if ($zip.Length -gt $maximumArchiveBytes -or $executable.Length -gt $maximumExecutableBytes) {
    throw "Lightweight release size limit exceeded: ZIP=$($zip.Length), EXE=$($executable.Length)."
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$externalValidationRequired = @()
if ($SkipInteractiveWebView2) {
    $externalValidationRequired += 'Interactive Code/Chat WebView2 smoke in a normal desktop session'
}
if ($SkipPrivateDshSmoke) {
    $externalValidationRequired += 'Real private DSH installation and second-start no-download smoke'
}
$externalValidationRequired += @(
    'Windows 10/11 x64 with .NET Framework 4.8 and WebView2',
    'Missing WebView2 and missing Node.js step-by-step installation flow',
    'Pinned DSH download through npm registry on a clean user profile',
    'Windows 10/11 x64 at 100%, 125%, and 150% DPI'
)
$report = [ordered]@{
    schemaVersion = 5
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    version = $Version
    runtime = $Runtime
    deployment = 'net-framework-4.8'
    tests = [ordered]@{
        unit = $unitSummary
        integration = $integrationSummary
        webViewInteractive = $webViewResult
        privateDshInstallAndReuse = $privateDshResult
    }
    artifact = [ordered]@{
        path = $zip.FullName
        bytes = $zip.Length
        maximumBytes = $maximumArchiveBytes
        sha256 = $hash
        entries = $entries
        executableBytes = $executable.Length
        fileVersion = $versionInfo.FileVersion
        productVersion = $versionInfo.ProductVersion
    }
    externalValidationRequired = $externalValidationRequired
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "Release gate passed: $reportPath"
Write-Host "Archive bytes: $($zip.Length)"
Write-Host "SHA-256: $hash"
