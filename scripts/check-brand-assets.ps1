$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repoRoot ('artifacts/asset-check/' + [Guid]::NewGuid().ToString('N'))
$loc = Join-Path $fixture 'UIRes/loc/dalamud'
New-Item -ItemType Directory -Path $loc -Force | Out-Null
'{}' | Set-Content -LiteralPath (Join-Path $loc 'dalamud_zh.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'apply-brand-assets.ps1') -AssetDirectory $fixture
$small = [Drawing.Image]::FromFile((Join-Path $fixture 'UIRes/tsmLogo.png'))
try {
    if ($small.Width -ne 64 -or $small.Height -ne 64) { throw 'Title menu logo must be exactly 64x64.' }
    if (($small.PixelFormat -band [Drawing.Imaging.PixelFormat]::Alpha) -eq 0) { throw 'Title menu logo must retain transparency.' }
} finally { $small.Dispose() }
$originalHash = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'assets/moonwake-mark.png')).Hash
$logoHash = (Get-FileHash -LiteralPath (Join-Path $fixture 'UIRes/logo.png')).Hash
if ($originalHash -ne $logoHash) { throw 'Full-size logo changed during packaging.' }
Write-Output 'PASS: packaged title menu logo is 64x64 with alpha; full-size logo is preserved.'
