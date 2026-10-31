[CmdletBinding()]
param(
    [string]$EditorPath,
    [switch]$Development,
    [switch]$SetupOnly,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionLine = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') |
    Where-Object { $_ -match '^m_EditorVersion: ' } | Select-Object -First 1
$requiredVersion = ($versionLine -replace '^m_EditorVersion: ', '').Trim()

if ([string]::IsNullOrWhiteSpace($EditorPath)) { $EditorPath = $env:UNITY_EDITOR }
if ([string]::IsNullOrWhiteSpace($EditorPath)) {
    $EditorPath = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$requiredVersion\Editor\Unity.exe"
}
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) {
    throw "Unity $requiredVersion not found. Install it with Windows Build Support (Mono), set UNITY_EDITOR, or pass -EditorPath 'C:\...\Unity.exe'."
}
$runningEditors = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($projectRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0 }
if ($runningEditors) {
    throw 'This Unity project is already open. Close that Editor before using the command-line build.'
}
foreach ($name in @('courier', 'sun-crystal', 'void-rock')) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "Assets\Game\Art\Splats\$name.idolply") -PathType Leaf)) {
        throw "Missing Idol-generated model '$name.idolply'. See Docs/CREATION.md for asset generation."
    }
}

$checksRoot = Join-Path $projectRoot 'Builds\Checks'
New-Item -ItemType Directory -Path $checksRoot -Force | Out-Null
$logPath = Join-Path $checksRoot $(if ($SetupOnly) { 'unity-setup.log' } else { 'unity-build.log' })
$method = if ($SetupOnly) { 'RelicDrift.Editor.RelicDriftBuild.ValidateProject' } else { 'RelicDrift.Editor.RelicDriftBuild.Build' }
$arguments = @('-batchmode', '-quit', '-force-d3d11', '-projectPath', ('"' + $projectRoot + '"'),
    '-executeMethod', $method, '-logFile', ('"' + $logPath + '"'))
$previousDevelopment = $env:RELIC_DRIFT_DEVELOPMENT
$previousOutput = $env:RELIC_DRIFT_BUILD_PATH
try {
    $env:RELIC_DRIFT_DEVELOPMENT = if ($Development) { '1' } else { '0' }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $env:RELIC_DRIFT_BUILD_PATH = [IO.Path]::GetFullPath($OutputPath)
    } else { $env:RELIC_DRIFT_BUILD_PATH = '' }
    Write-Host "Unity $requiredVersion - $method"
    Write-Host "Log: $logPath"
    $process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(1800000)) {
        $process.Kill()
        throw "Unity exceeded the 30 minute build timeout. Inspect $logPath"
    }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 60 | Write-Host }
        throw "Unity exited with code $($process.ExitCode). Inspect $logPath"
    }
    $successMarker = if ($SetupOnly) { 'RELIC_DRIFT_VALIDATION_OK' } else { 'RELIC_DRIFT_BUILD_Succeeded' }
    if (-not (Select-String -LiteralPath $logPath -Pattern $successMarker -Quiet)) {
        throw "Unity did not report '$successMarker'. Inspect $logPath"
    }
    if ($SetupOnly) { Write-Host 'Project import, scene setup and Idol-only asset audit passed.' }
    else {
        $exe = if ([string]::IsNullOrWhiteSpace($OutputPath)) { Join-Path $projectRoot 'Builds\Windows\RelicDrift.exe' } else { [IO.Path]::GetFullPath($OutputPath) }
        Write-Host "Game built: $exe"
    }
} finally {
    $env:RELIC_DRIFT_DEVELOPMENT = $previousDevelopment
    $env:RELIC_DRIFT_BUILD_PATH = $previousOutput
}
