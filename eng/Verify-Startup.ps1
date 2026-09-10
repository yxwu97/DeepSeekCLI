param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$smokeDirectory = Join-Path $repositoryRoot ('output\validation\startup\' + [Guid]::NewGuid().ToString('N'))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($ArchivePath), $smokeDirectory)
$executable = Join-Path $smokeDirectory 'DeepSeekHarnessDesktop.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'The release archive does not contain the application executable at its root.'
}

$process = Start-Process -FilePath $executable -ArgumentList '--startup-smoke' `
    -WorkingDirectory $smokeDirectory -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(30000)) {
        throw 'Published application did not render its main window and exit within 30 seconds.'
    }
    if ($process.ExitCode -eq 2) {
        throw 'Close the existing Desktop instance before running the published application startup gate.'
    }
    if ($process.ExitCode -ne 0) {
        throw "Published application startup failed with exit code $($process.ExitCode). Check the Desktop application log."
    }
    Write-Host 'PASS: ZIP executable resolved production services, rendered the main window and exited normally.'
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
    $process.Dispose()
}
