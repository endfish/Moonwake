[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HomeDirectory,
    [string]$DotnetRoot = (Join-Path $env:ProgramFiles 'dotnet'),
    [Parameter(Mandatory)][ValidatePattern('^10\.0\.\d+$')][string]$RuntimeVersion
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $repoRoot 'resources.lock.json') -Raw | ConvertFrom-Json
$HomeDirectory = [IO.Path]::GetFullPath($HomeDirectory)
$assets = Join-Path $HomeDirectory 'assets/current'
$runtime = Join-Path $HomeDirectory 'runtime'
if ((Test-Path -LiteralPath $assets) -or (Test-Path -LiteralPath $runtime)) { throw 'Resource destinations already exist. Choose a fresh HomeDirectory.' }
$runtimePaths = @("host/fxr/$RuntimeVersion", "shared/Microsoft.NETCore.App/$RuntimeVersion", "shared/Microsoft.WindowsDesktop.App/$RuntimeVersion")
foreach ($relative in $runtimePaths) {
    if (!(Test-Path -LiteralPath (Join-Path $DotnetRoot $relative))) { throw "Missing installed .NET runtime component: $relative" }
}
$base = "https://raw.githubusercontent.com/$($lock.assets.repository)/$($lock.assets.commit)"
$manifest = Invoke-RestMethod "$base/asset.json"
New-Item -ItemType Directory -Path $assets -Force | Out-Null
foreach ($asset in $manifest.Assets) {
    # Maintenance-time provisioning only. The app never reads an upstream ban list.
    if ($asset.FileName -eq 'UIRes/bannedplugin.json' -or $asset.FileName -like 'UIRes/pdb/*') { continue }
    $destination = [IO.Path]::GetFullPath((Join-Path $assets $asset.FileName))
    if (!$destination.StartsWith($assets + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid asset path.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Invoke-WebRequest "$base/$($asset.FileName)" -OutFile $destination
    if ($asset.Hash -and (Get-FileHash -LiteralPath $destination -Algorithm SHA1).Hash -ne $asset.Hash) { throw "Asset hash mismatch: $($asset.FileName)" }
}
Invoke-WebRequest "$base/LICENSE" -OutFile (Join-Path $assets 'LICENSE-DalamudAssets.txt')
$fontBase = "https://raw.githubusercontent.com/$($lock.chineseFont.repository)/$($lock.chineseFont.commit)"
Invoke-WebRequest "$fontBase/$($lock.chineseFont.path)" -OutFile (Join-Path $assets 'UIRes/NotoSansCJKsc-Medium.otf')
Invoke-WebRequest "$fontBase/Sans/LICENSE" -OutFile (Join-Path $assets 'LICENSE-NotoCJK.txt')
foreach ($relative in $runtimePaths) {
    $destination = Join-Path $runtime $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $DotnetRoot $relative) -Destination $destination -Recurse
}
foreach ($name in @('LICENSE.txt','ThirdPartyNotices.txt')) {
    if (Test-Path -LiteralPath (Join-Path $DotnetRoot $name)) { Copy-Item -LiteralPath (Join-Path $DotnetRoot $name) -Destination $runtime }
}
@{ assets=$lock.assets; chineseFont=$lock.chineseFont; runtimeVersion=$RuntimeVersion } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $HomeDirectory 'moonwake-resources.json') -Encoding utf8
Write-Output "Resources prepared in $HomeDirectory. No game version has been admitted and no injection has been performed."
