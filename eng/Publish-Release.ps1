param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repositoryRoot 'src\DeepSeekHarnessDesktop\DeepSeekHarnessDesktop.csproj'
$buildProperties = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw)
$version = [string]$buildProperties.Project.PropertyGroup.AppVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Directory.Build.props contains an invalid AppVersion: '$version'."
}
$versionHistory = Get-Content -LiteralPath (Join-Path $repositoryRoot 'VERSION_HISTORY.md') -Raw
if ($versionHistory -notmatch "(?m)^## \[$([regex]::Escape($version))\] - \d{4}-\d{2}-\d{2}$") {
    throw "VERSION_HISTORY.md does not contain a dated entry for version $version."
}
$outputDirectory = Join-Path $repositoryRoot 'output'
$publishDirectory = Join-Path $outputDirectory "publish\$version\$Runtime"
$zipPath = Join-Path $outputDirectory "DeepSeekHarnessDesktop-$version-$Runtime.zip"

$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($outputDirectory).TrimEnd('\') + '\'
$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublishDirectory.StartsWith($resolvedOutputDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside output: $resolvedPublishDirectory"
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($publishDirectory) | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$documentationFiles = Get-ChildItem -LiteralPath $publishDirectory -Filter 'Microsoft.Web.WebView2.*.xml' -File
foreach ($documentationFile in $documentationFiles) {
    Remove-Item -LiteralPath $documentationFile.FullName -Force
}

$releaseNotes = Join-Path $repositoryRoot 'docs\installation.md'
Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $publishDirectory 'README.md') -Force
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
Write-Output $zipPath
