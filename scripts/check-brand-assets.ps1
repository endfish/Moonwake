$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repoRoot ('artifacts/asset-check/' + [Guid]::NewGuid().ToString('N'))
$loc = Join-Path $fixture 'UIRes/loc/dalamud'
New-Item -ItemType Directory -Path $loc -Force | Out-Null
'{}' | Set-Content -LiteralPath (Join-Path $loc 'dalamud_zh.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'apply-brand-assets.ps1') -AssetDirectory $fixture
foreach ($spec in @(@{Name='tsmLogo.png'; Size=64}, @{Name='defaultIcon.png'; Size=512})) {
    $icon = [Drawing.Image]::FromFile((Join-Path $fixture ('UIRes/' + $spec.Name)))
    try {
        if ($icon.Width -ne $spec.Size -or $icon.Height -ne $spec.Size) { throw "Incorrect icon dimensions: $($spec.Name)" }
        if (($icon.PixelFormat -band [Drawing.Imaging.PixelFormat]::Alpha) -eq 0) { throw "Icon must retain transparency: $($spec.Name)" }
    } finally { $icon.Dispose() }
}
$originalHash = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'assets/moonwake-mark.png')).Hash
$logoHash = (Get-FileHash -LiteralPath (Join-Path $fixture 'UIRes/logo.png')).Hash
if ($originalHash -ne $logoHash) { throw 'Full-size logo changed during packaging.' }
Write-Output 'PASS: title logo is 64x64, plugin fallback is 512x512, both retain alpha; full-size logo is preserved.'
