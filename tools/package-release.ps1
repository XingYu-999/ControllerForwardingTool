param(
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]*$')][string]$Version = '20260926-r8',
    [switch]$SelfContained
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
$buildRoot = Join-Path $artifactRoot "build-$Version"
$runtimeRoot = Join-Path $artifactRoot "Publish\$Version\ControllerForwardingTool"
$runtimeZip = Join-Path $artifactRoot "ControllerForwardingTool-win-x64-$Version.zip"
$sourceZip = Join-Path $artifactRoot "ControllerForwardingTool-third-party-sources-$Version.zip"
# A new directory avoids retaining obsolete docs/symbols from older dotnet publish runs.
# Never delete a user's previous release or publish folder to prepare packaging.
foreach ($path in @($buildRoot, $runtimeRoot, $runtimeZip, $sourceZip)) {
    if (Test-Path -LiteralPath $path) { throw "Output already exists; choose a new Version: $path" }
}
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
& dotnet publish (Join-Path $projectRoot 'ControllerForwardingTool\ControllerForwardingTool.csproj') -c Release -r win-x64 `
    --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() --artifacts-path $buildRoot -o $runtimeRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$unexpected = @(Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File | Where-Object {
    $_.FullName -match '[\\/](docs|sources)[\\/]' -or $_.Name -match '\.(pdb|tar\.gz|tar\.bz2)$' -or $_.Name -eq 'README.md'
})
if ($unexpected.Count) { throw "Unexpected development files in runtime: $($unexpected.FullName -join ', ')" }
$sourceFiles = @(
    'LICENSE', 'THIRD_PARTY_NOTICES.md', 'tools\build-sdl.ps1',
    'drivers\sdl3\3.4.16\SDL3-3.4.16.tar.gz', 'drivers\sdl3\3.4.16\LICENSE.txt',
    'drivers\libusb\1.0.30\libusb-1.0.30.tar.bz2', 'drivers\libusb\1.0.30\COPYING',
    'drivers\libusb\1.0.30\libusb.h', 'drivers\libusb\1.0.30\libusb-1.0.lib',
    'drivers\viiper\viiper-haptic-source.tar.gz', 'drivers\viiper\LICENSE.txt', 'drivers\viiper\BRIDGE-LICENSE.txt'
)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $sourceFiles) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $projectRoot $relative),
            $relative.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
[IO.Compression.ZipFile]::CreateFromDirectory($runtimeRoot, $runtimeZip, [IO.Compression.CompressionLevel]::Optimal, $false)
Get-FileHash -Algorithm SHA256 -LiteralPath $runtimeZip, $sourceZip | Select-Object Path, Hash
