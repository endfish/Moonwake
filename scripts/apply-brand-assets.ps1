[CmdletBinding()]
param([Parameter(Mandatory)][string]$AssetDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$ui = Join-Path $AssetDirectory 'UIRes'
$languageFile = Join-Path $ui 'loc/dalamud/dalamud_zh.json'
$language = Get-Content -LiteralPath $languageFile -Raw | ConvertFrom-Json -AsHashtable
$overrides = Get-Content -LiteralPath (Join-Path $repoRoot 'resources/zh-cn-overrides.json') -Raw | ConvertFrom-Json -AsHashtable
foreach ($entry in $overrides.GetEnumerator()) {
    $language[$entry.Key] = @{ message=$entry.Value; description='Moonwake interface terminology' }
}
$language | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $languageFile -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repoRoot 'assets/moonwake-mark.png') -Destination (Join-Path $ui 'logo.png')
Copy-Item -LiteralPath (Join-Path $repoRoot 'assets/moonwake-mark.png') -Destination (Join-Path $ui 'tsmLogo.png')
Write-Output "Moonwake artwork and $($overrides.Count) terminology overrides applied."
