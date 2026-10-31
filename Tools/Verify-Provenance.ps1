$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($name in @('courier','sun-crystal','void-rock')) {
    $recordPath = Join-Path $projectRoot "Docs\Provenance\$name.json"
    if (-not (Test-Path -LiteralPath $recordPath)) { throw "Missing generation record: $recordPath" }
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($record.Model -ne 'speridlabs/idol' -or $record.Tool -ne 'https://github.com/shohdi/idol-csharp') {
        throw "Unexpected model/tool provenance for $name"
    }
    foreach ($item in @(@{Path=$record.SourceImage;Hash=$record.SourceImageSha256}, @{Path=$record.Output;Hash=$record.OutputSha256})) {
        $filePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $item.Path))
        if (-not $filePath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Provenance path escapes the project.'
        }
        if (-not (Test-Path -LiteralPath $filePath)) { throw "Missing generated asset: $filePath" }
        $actual = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
        if ($actual -ne $item.Hash) { throw "Asset changed since Idol generation: $($item.Path)" }
    }
    Write-Host "$name : verified original Idol output ($($record.GaussianCount) Gaussians) and source image"
}
Write-Host 'All three asset provenance records match.'
