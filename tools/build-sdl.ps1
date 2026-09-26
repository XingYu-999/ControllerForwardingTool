param(
    [Parameter(Mandatory = $true)][string]$ToolchainBin,
    [string]$CMake = 'cmake',
    [string]$Ninja = 'ninja'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceArchive = Join-Path $projectRoot 'drivers\sdl3\3.4.16\SDL3-3.4.16.tar.gz'
if ((Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash -ne '7322236CD12090C3EB40B9728BE4D49C76F66AD17D04369584D4ECAD5CF77C68') {
    throw 'SDL source archive checksum mismatch'
}
$buildRoot = Join-Path $PSScriptRoot 'sdl-rebuild'
New-Item -ItemType Directory -Force $buildRoot | Out-Null
& tar -xzf $sourceArchive -C $buildRoot
if ($LASTEXITCODE -ne 0) { throw 'SDL extraction failed' }
$sourceRoot = Join-Path $buildRoot 'SDL3-3.4.16'
$libusbRoot = Join-Path $projectRoot 'drivers\libusb\1.0.30'
$env:PATH = "$ToolchainBin;$env:PATH"
$ninjaPath = (Get-Command $Ninja -ErrorAction Stop).Source
& $CMake -S $sourceRoot -B (Join-Path $buildRoot 'build') -G Ninja `
    "-DCMAKE_MAKE_PROGRAM=$ninjaPath" `
    "-DCMAKE_C_COMPILER=$ToolchainBin/x86_64-w64-mingw32-clang.exe" `
    "-DCMAKE_CXX_COMPILER=$ToolchainBin/x86_64-w64-mingw32-clang++.exe" `
    -DCMAKE_BUILD_TYPE=Release -DSDL_SHARED=ON -DSDL_STATIC=OFF `
    -DSDL_TESTS=OFF -DSDL_TEST_LIBRARY=OFF -DSDL_AUDIO=OFF -DSDL_VIDEO=OFF `
    -DSDL_RENDER=OFF -DSDL_CAMERA=OFF -DSDL_HIDAPI_LIBUSB=ON -DSDL_HIDAPI_LIBUSB_SHARED=ON `
    "-DLibUSB_INCLUDE_PATH=$libusbRoot" "-DLibUSB_LIBRARY=$libusbRoot/libusb-1.0.lib"
if ($LASTEXITCODE -ne 0) { throw 'SDL configuration failed' }
& $CMake --build (Join-Path $buildRoot 'build') --parallel 8
if ($LASTEXITCODE -ne 0) { throw 'SDL build failed' }
Copy-Item -LiteralPath (Join-Path $buildRoot 'build\SDL3.dll') -Destination (Join-Path $projectRoot 'drivers\sdl3\3.4.16\SDL3.dll')
Write-Output 'SDL3.dll rebuilt with dynamic libusb support. Rebuild the .NET solution to copy it into the application.'
