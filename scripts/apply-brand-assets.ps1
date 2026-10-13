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
Add-Type -AssemblyName System.Drawing
$mark = [Drawing.Image]::FromFile((Join-Path $repoRoot 'assets/moonwake-mark.png'))
$small = [Drawing.Bitmap]::new(64, 64, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($small)
try {
    # ITitleScreenMenuEntry accepts only a 64x64 texture.
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $scale = [Math]::Min(64.0 / $mark.Width, 64.0 / $mark.Height)
    $width = [int][Math]::Round($mark.Width * $scale)
    $height = [int][Math]::Round($mark.Height * $scale)
    $graphics.DrawImage($mark, [int]((64 - $width) / 2), [int]((64 - $height) / 2), $width, $height)
    $small.Save((Join-Path $ui 'tsmLogo.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $small.Dispose(); $mark.Dispose() }
Write-Output "Moonwake artwork and $($overrides.Count) terminology overrides applied."
