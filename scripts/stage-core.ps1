[CmdletBinding()]
param([string]$SourceRoot, [Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$SourceRoot) { $SourceRoot = Join-Path $repoRoot '.build/source/Dalamud' }
$source = Join-Path $SourceRoot 'bin/Release'
$markerPath = Join-Path (Split-Path -Parent $SourceRoot) 'moonwake-source.json'
$sourceManifest = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
$inputFiles = @((Join-Path $repoRoot 'upstream.lock.json')) + @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'patches') -Recurse -Filter '*.patch' | Sort-Object FullName | ForEach-Object FullName)
$expectedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes((($inputFiles | ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}) -join ''))))
if ($sourceManifest.inputHash -ne $expectedHash) { throw 'Source inputs differ from current patches. Materialize and build a new source directory.' }
foreach ($required in @('Dalamud.dll','Dalamud.Boot.dll','Dalamud.Injector.exe','DalamudCrashHandler.exe','cimgui.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $source $required))) { throw "Missing build output: $required" }
}
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Destination already exists. Choose a new empty destination.' }
New-Item -ItemType Directory -Path $Destination | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in '.dll','.exe','.json' -or $_.Name -eq 'licenses.txt' } | Copy-Item -Destination $Destination
$native = Join-Path $source 'runtimes/win-x64/native'
if (Test-Path -LiteralPath $native) {
    $nativeDestination = Join-Path $Destination 'runtimes/win-x64/native'
    New-Item -ItemType Directory -Path $nativeDestination -Force | Out-Null
    Get-ChildItem -LiteralPath $native -File | Copy-Item -Destination $nativeDestination
}
$hashes = [ordered]@{}
Get-ChildItem -LiteralPath $Destination -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($Destination, $_.FullName).Replace('\','/')
    $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
@{ product='Moonwake'; schemaVersion=1; source=$sourceManifest; files=$hashes } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Destination 'moonwake-build.json') -Encoding utf8
Write-Output "Staged $($hashes.Count) verified payload files: $Destination"
