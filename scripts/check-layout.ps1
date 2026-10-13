[CmdletBinding()]
param([string]$SourceRoot)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$SourceRoot) { $SourceRoot = Join-Path $repoRoot '.build/source/Dalamud' }
$structs = Join-Path $SourceRoot 'lib/FFXIVClientStructs'
Push-Location $structs
try {
    $output = & dotnet run --project CExporter -c Release -- --quiet --no-write 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    $actual = @($output | ForEach-Object { "$_" } | Where-Object { $_ -match '^Field (overlap|offset|size)' } | Sort-Object)
    $expected = @(Get-Content -LiteralPath (Join-Path $repoRoot 'validation/cexporter-baseline.txt') | Where-Object { $_ } | Sort-Object)
    if (Compare-Object $expected $actual) { throw 'CExporter layout diagnostics changed; review the full output.' }
    Write-Output "CExporter: $($actual.Count) inherited diagnostics match the baseline; no game compatibility is implied."
} finally { Pop-Location }
