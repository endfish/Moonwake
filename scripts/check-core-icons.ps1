[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing
foreach ($name in @('Dalamud.Injector.exe','Dalamud.Boot.dll','DalamudCrashHandler.exe')) {
    $actualIcon = [Drawing.Icon]::ExtractAssociatedIcon((Resolve-Path -LiteralPath (Join-Path $PayloadDirectory $name)).Path)
    if ($null -eq $actualIcon) { throw "Missing application icon: $name" }
    $actual = $actualIcon.ToBitmap()
    $expectedIcon = [Drawing.Icon]::new((Join-Path $repoRoot 'src/Moonwake/Resources/app-icon.ico'), $actual.Width, $actual.Height)
    $expected = $expectedIcon.ToBitmap()
    try {
        for ($y = 0; $y -lt $actual.Height; $y++) {
            for ($x = 0; $x -lt $actual.Width; $x++) {
                if ($actual.GetPixel($x, $y).ToArgb() -ne $expected.GetPixel($x, $y).ToArgb()) {
                    throw "Stale application icon in $name. Rebuild the resource before staging."
                }
            }
        }
    } finally {
        $actual.Dispose()
        $expected.Dispose()
        $actualIcon.Dispose()
        $expectedIcon.Dispose()
    }
}
Write-Output 'PASS: injector, boot and crash-handler binaries contain the Moonwake icon.'
