[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,

    [switch]$Deploy,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\MortuaryAssistantVR\MortuaryAssistantVR.csproj"

if (-not (Test-Path -LiteralPath $GameDir -PathType Container)) {
    throw "Game directory not found: $GameDir"
}

$deployValue = if ($Deploy) { "true" } else { "false" }

dotnet build $project `
    --configuration $Configuration `
    -p:MortuaryAssistantDir="$GameDir" `
    -p:DeployToGame="$deployValue"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Host "Build completed." -ForegroundColor Green

if ($Deploy) {
    Write-Host "Plugin deployed to BepInEx\plugins\MortuaryAssistantVR." -ForegroundColor Green
}
