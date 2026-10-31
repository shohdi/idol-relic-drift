param([string]$IdolStudioBuildDir, [string[]]$Names = @('all'))
$ErrorActionPreference = 'Stop'
$gameRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $IdolStudioBuildDir) {
    foreach ($sibling in @('idol','idol-csharp')) {
        $candidate = [IO.Path]::GetFullPath((Join-Path $gameRoot "..\$sibling\src\Idol.Inference\bin\Release\net9.0-windows"))
        if (Test-Path -LiteralPath (Join-Path $candidate 'Idol.Inference.dll')) { $IdolStudioBuildDir = $candidate; break }
    }
}
if (-not $IdolStudioBuildDir -or -not (Test-Path -LiteralPath (Join-Path $IdolStudioBuildDir 'Idol.Inference.dll'))) {
    throw 'Provide -IdolStudioBuildDir pointing to an already-built Idol Studio Release/net9.0-windows folder. This script will not build or edit that repository.'
}
$previousRoot = $env:RELIC_DRIFT_ROOT
Push-Location $gameRoot
try {
    $env:RELIC_DRIFT_ROOT = $gameRoot
    dotnet run --project Tools/AssetGenerator -c Release "-p:IdolStudioBuildDir=$IdolStudioBuildDir" -- @Names
    if ($LASTEXITCODE -ne 0) { throw "Asset generation failed with code $LASTEXITCODE" }
} finally {
    $env:RELIC_DRIFT_ROOT = $previousRoot
    Pop-Location
}
