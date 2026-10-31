[CmdletBinding()]
param(
    [string]$EditorPath,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$checksRoot = Join-Path $projectRoot 'Builds\Checks'
$playerPath = Join-Path $projectRoot 'Builds\Windows-Checks\RelicDrift.exe'
New-Item -ItemType Directory -Path $checksRoot -Force | Out-Null
if (-not $SkipBuild) {
    $buildArguments = @{ Development = $true; OutputPath = $playerPath }
    if (-not [string]::IsNullOrWhiteSpace($EditorPath)) { $buildArguments.EditorPath = $EditorPath }
    & (Join-Path $PSScriptRoot 'Build.ps1') @buildArguments
}
if (-not (Test-Path -LiteralPath $playerPath -PathType Leaf)) {
    throw "Development game not found: $playerPath. Run without -SkipBuild first."
}

$outputRoot = Join-Path $checksRoot ('Smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$logPath = Join-Path $outputRoot 'player.log'
$arguments = @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    '-logFile', ('"' + $logPath + '"'), '--smoke-test', '--smoke-output', ('"' + $outputRoot + '"'))
Write-Host 'Running the real development player: start, collect, collision, pause, finish, loss and restart.'
$process = Start-Process -FilePath $playerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) {
    $process.Kill()
    throw "Smoke test exceeded 60 seconds. Inspect $logPath"
}
$process.WaitForExit()
$resultPath = Join-Path $outputRoot 'smoke-result.json'
if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
    if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 50 | Write-Host }
    throw "No smoke result was written; use a Development build. Inspect $logPath"
}
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if ($process.ExitCode -ne 0 -or -not $result.success) {
    $result | ConvertTo-Json -Depth 8 | Write-Host
    throw "Smoke test failed (exit $($process.ExitCode)). Inspect $outputRoot"
}
Write-Host "$($result.passed.Count) checks passed on $($result.graphicsDevice) / $($result.graphicsApi)."
Write-Host "Report: $resultPath"
Write-Host "Screenshots: $(Join-Path $outputRoot 'title.png') and $(Join-Path $outputRoot 'playing.png')"
