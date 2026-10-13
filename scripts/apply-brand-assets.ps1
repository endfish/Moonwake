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
try {
    # Title entries require 64x64; plugin fallback icons use the plugin manager's 512x512 limit.
    foreach ($spec in @(@{Name='tsmLogo.png'; Size=64}, @{Name='defaultIcon.png'; Size=512})) {
        $size = $spec.Size
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale = [Math]::Min($size / [double]$mark.Width, $size / [double]$mark.Height)
            $width = [int][Math]::Round($mark.Width * $scale)
            $height = [int][Math]::Round($mark.Height * $scale)
            $graphics.DrawImage($mark, [int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height)
            $bitmap.Save((Join-Path $ui $spec.Name), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $mark.Dispose() }
Write-Output "Moonwake artwork and $($overrides.Count) terminology overrides applied."
