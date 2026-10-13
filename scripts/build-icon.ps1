$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$image = [Drawing.Image]::FromFile((Join-Path $repoRoot 'assets/moonwake-mark.png'))
$frames = @()
try {
    foreach ($size in @(16,24,32,48,64,128,256)) {
        $bitmap = [Drawing.Bitmap]::new($size,$size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($image,0,0,$size,$size)
            $stream = [IO.MemoryStream]::new()
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            $frames += @{ Size=$size; Data=$stream.ToArray() }
            $stream.Dispose()
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    $iconPath = Join-Path $repoRoot 'src/Moonwake/Resources/app-icon.ico'
    $output = [IO.File]::Create($iconPath)
    $writer = [IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Data.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
    } finally { $writer.Dispose(); $output.Dispose() }
} finally { $image.Dispose() }
