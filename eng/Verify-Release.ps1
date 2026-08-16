param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
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
$unitTestProject = Join-Path $repositoryRoot 'tests\DeepSeekHarnessDesktop.UnitTests\DeepSeekHarnessDesktop.UnitTests.csproj'
$integrationTestProject = Join-Path $repositoryRoot 'tests\DeepSeekHarnessDesktop.IntegrationTests\DeepSeekHarnessDesktop.IntegrationTests.csproj'
$publishScript = Join-Path $PSScriptRoot 'Publish-Release.ps1'
$outputDirectory = Join-Path $repositoryRoot 'output'
$publishDirectory = Join-Path $outputDirectory "publish\$Version\$Runtime"
$executablePath = Join-Path $publishDirectory 'DeepSeekHarnessDesktop.exe'
$zipPath = Join-Path $outputDirectory "DeepSeekHarnessDesktop-$Version-$Runtime.zip"
$resultsDirectory = Join-Path $outputDirectory 'validation'
$reportPath = Join-Path $resultsDirectory "release-gate-$Version-$Runtime.json"

[System.IO.Directory]::CreateDirectory($resultsDirectory) | Out-Null

function Assert-LastExitCode {
    param([string]$Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Read-TestSummary {
    param([string]$Path)

    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $results = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
    $failed = @($results | Where-Object { $_.outcome -ne 'Passed' })
    if ($results.Count -eq 0) {
        throw "No test results were found in $Path."
    }
    if ($failed.Count -ne 0) {
        throw "$($failed.Count) non-passing test results were found in $Path."
    }

    return [ordered]@{
        total = $results.Count
        passed = $results.Count
        failed = 0
    }
}

Write-Host 'Building Debug solution...'
& dotnet build $solution -c Debug --no-restore '-m:1'
Assert-LastExitCode 'Debug build'

Write-Host 'Building Release solution...'
& dotnet build $solution -c $Configuration --no-restore '-m:1'
Assert-LastExitCode "$Configuration build"

$unitResultsPath = Join-Path $resultsDirectory 'unit.trx'
$integrationResultsPath = Join-Path $resultsDirectory 'integration.trx'

Write-Host 'Running unit tests...'
& dotnet test $unitTestProject -c $Configuration --no-build --no-restore --logger 'trx;LogFileName=unit.trx' --results-directory $resultsDirectory
Assert-LastExitCode 'Unit tests'
$unitSummary = Read-TestSummary $unitResultsPath

Write-Host 'Running integration tests...'
& dotnet test $integrationTestProject -c $Configuration --no-build --no-restore --logger 'trx;LogFileName=integration.trx' --results-directory $resultsDirectory
Assert-LastExitCode 'Integration tests'
$integrationSummary = Read-TestSummary $integrationResultsPath

Write-Host 'Publishing release archive...'
& $publishScript -Configuration $Configuration -Runtime $Runtime
Assert-LastExitCode 'Release publish'

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable was not found: $executablePath"
}
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "Release archive was not found: $zipPath"
}

$versionInfo = (Get-Item -LiteralPath $executablePath).VersionInfo
if ($versionInfo.FileVersion -ne "$Version.0") {
    throw "Unexpected file version '$($versionInfo.FileVersion)'; expected '$Version.0'."
}
if ($versionInfo.ProductVersion -ne $Version) {
    throw "Unexpected product version '$($versionInfo.ProductVersion)'; expected '$Version'."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
}
finally {
    $archive.Dispose()
}

$expectedEntries = @('DeepSeekHarnessDesktop.exe', 'README.md') | Sort-Object
if ([string]::Join('|', $entries) -ne [string]::Join('|', $expectedEntries)) {
    throw "Unexpected archive entries: $([string]::Join(', ', $entries))"
}

$zip = Get-Item -LiteralPath $zipPath
$executable = Get-Item -LiteralPath $executablePath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$report = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    version = $Version
    runtime = $Runtime
    builds = [ordered]@{
        debug = 'passed'
        release = 'passed'
    }
    tests = [ordered]@{
        unit = $unitSummary
        integration = $integrationSummary
    }
    artifact = [ordered]@{
        path = $zip.FullName
        bytes = $zip.Length
        sha256 = $hash
        entries = $entries
        executableBytes = $executable.Length
        fileVersion = $versionInfo.FileVersion
        productVersion = $versionInfo.ProductVersion
    }
    externalValidationRequired = @(
        'Windows 10 x64 clean-user environment',
        'Windows 11 x64 at 125% DPI',
        'Windows 11 x64 at 150% DPI',
        'Clean environments without Node.js, npm cache, or WebView2 Runtime'
    )
}

$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "Release gate passed: $reportPath"
Write-Host "SHA-256: $hash"
