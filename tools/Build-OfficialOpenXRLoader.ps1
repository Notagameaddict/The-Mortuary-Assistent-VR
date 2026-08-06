param(
    [string]$Destination = "",
    [string]$Tag = "release-1.1.62"
)

$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

Require-Command "git"
Require-Command "cmake"
Require-Command "cl"
Require-Command "nmake"

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $PSScriptRoot "..\OpenXRLoaderOutput"
}

$Destination = [System.IO.Path]::GetFullPath($Destination)
$WorkRoot = Join-Path $env:TEMP "MortuaryAssistantVR-OpenXR"
$SourceDir = Join-Path $WorkRoot "OpenXR-SDK-Source"
$BuildDir = Join-Path $WorkRoot "build"

Remove-Item $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item $WorkRoot -ItemType Directory | Out-Null
New-Item $Destination -ItemType Directory -Force | Out-Null

Write-Host "Compiler:"
cl 2>&1 | Select-Object -First 1

Write-Host "Cloning official Khronos OpenXR SDK Source tag $Tag..."
git clone `
    --depth 1 `
    --branch $Tag `
    https://github.com/KhronosGroup/OpenXR-SDK-Source.git `
    $SourceDir

Write-Host "Configuring official dynamic x64 Windows loader with NMake..."
cmake `
    -S $SourceDir `
    -B $BuildDir `
    -G "NMake Makefiles" `
    -DCMAKE_BUILD_TYPE=Release `
    -DDYNAMIC_LOADER=ON `
    -DBUILD_TESTS=OFF `
    -DBUILD_API_LAYERS=OFF `
    -DBUILD_CONFORMANCE_TESTS=OFF `
    -DBUILD_LOADER=ON

Write-Host "Building Release loader..."
cmake `
    --build $BuildDir `
    --target openxr_loader `
    --parallel

$Loader = Get-ChildItem `
    -Path $BuildDir `
    -Filter "openxr_loader.dll" `
    -Recurse |
    Select-Object -First 1

if ($null -eq $Loader) {
    throw "The build completed, but openxr_loader.dll was not found."
}

$OutputPath = Join-Path $Destination "openxr_loader.dll"
Copy-Item $Loader.FullName $OutputPath -Force

$Hash = Get-FileHash $OutputPath -Algorithm SHA256
$HashPath = Join-Path $Destination "openxr_loader.dll.sha256.txt"
"$($Hash.Hash)  openxr_loader.dll" |
    Set-Content $HashPath -Encoding ASCII

Write-Host ""
Write-Host "Official loader built successfully:"
Write-Host $OutputPath
Write-Host "SHA-256: $($Hash.Hash)"
Write-Host ""
Write-Host "Copy openxr_loader.dll to:"
Write-Host "BepInEx\plugins\MortuaryAssistantVR\"
