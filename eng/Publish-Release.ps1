param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
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
$resolvedOutputDirectory = [IO.Path]::GetFullPath($outputDirectory).TrimEnd('\') + '\'
$resolvedPublishDirectory = [IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublishDirectory.StartsWith($resolvedOutputDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside output: $resolvedPublishDirectory"
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
[IO.Directory]::CreateDirectory($publishDirectory) | Out-Null

dotnet publish $project `
    -c $Configuration `
    --no-restore `
    -p:PlatformTarget=x64 `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $publishDirectory -Filter '*.xml' -File |
    Where-Object Name -Like 'Microsoft.Web.WebView2.*' |
    Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\installation.md') `
    -Destination (Join-Path $publishDirectory 'README.md') -Force

Compress-Archive -Path (Join-Path $publishDirectory '*') `
    -DestinationPath $zipPath -CompressionLevel Optimal -Force
Write-Output $zipPath
