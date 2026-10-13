[CmdletBinding()]
param(
    [string]$OutputRoot,
    [string]$DalamudCache,
    [string]$ClientStructsCache
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $repoRoot 'upstream.lock.json') -Raw | ConvertFrom-Json
if (!$OutputRoot) { $OutputRoot = Join-Path $repoRoot '.build/source' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$inputFiles = @((Join-Path $repoRoot 'upstream.lock.json')) + @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'patches') -Recurse -Filter '*.patch' | Sort-Object FullName | ForEach-Object FullName)
$inputHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes((($inputFiles | ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}) -join ''))))
$marker = Join-Path $OutputRoot 'moonwake-source.json'
if (Test-Path -LiteralPath $OutputRoot) {
    if ((Test-Path -LiteralPath $marker) -and ((Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json).inputHash -eq $inputHash)) {
        Write-Output (Join-Path $OutputRoot 'Dalamud')
        return
    }
    throw "Output directory already exists with different or incomplete inputs. Choose a new -OutputRoot; existing files are never reset automatically."
}
New-Item -ItemType Directory -Path $OutputRoot | Out-Null

function Invoke-Git([string]$At, [string[]]$Arguments) {
    & git -C $At @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
function Initialize-Checkout([string]$Destination, [string]$Repository, [string]$Commit, [string]$Cache) {
    if ($Cache -and (Test-Path -LiteralPath $Cache)) {
        & git clone --shared --no-checkout $Cache $Destination
    } else {
        & git clone --filter=blob:none --no-checkout $Repository $Destination
    }
    if ($LASTEXITCODE -ne 0) { throw "Clone failed: $Repository" }
    # Local prompts must be excluded before doing anything else in a new checkout.
    $gitDir = (& git -C $Destination rev-parse --absolute-git-dir).Trim()
    Add-Content -LiteralPath (Join-Path $gitDir 'info/exclude') -Value "`nAGENTS.md`n"
    Invoke-Git $Destination @('remote','set-url','origin',$Repository)
    Invoke-Git $Destination @('config','remote.origin.pushurl','DISABLED')
    & git -C $Destination cat-file -e "$Commit^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) { Invoke-Git $Destination @('fetch','--no-tags','origin',$Commit) }
    Invoke-Git $Destination @('checkout','--detach',$Commit)
    if ((& git -C $Destination rev-parse HEAD).Trim() -ne $Commit) { throw 'Pinned commit mismatch.' }
}

$core = Join-Path $OutputRoot 'Dalamud'
Initialize-Checkout $core $lock.dalamud.repository $lock.dalamud.commit $DalamudCache
$structs = Join-Path $core 'lib/FFXIVClientStructs'
Initialize-Checkout $structs $lock.clientStructs.repository $lock.clientStructs.commit $ClientStructsCache

function Initialize-Dependencies([string]$Parent, [string]$ParentCache) {
if (!(Test-Path -LiteralPath (Join-Path $Parent '.gitmodules'))) { return }
$modules = & git -C $Parent config -f .gitmodules --get-regexp '^submodule\..*\.path$'
foreach ($entry in $modules) {
    $parts = $entry -split ' ', 2
    $relative = $parts[1]
    if ($relative -in @('lib/FFXIVClientStructs','lib/Hexa.NET.ImGui')) { continue }
    $key = $parts[0] -replace '\.path$', '.url'
    $url = (& git -C $Parent config -f .gitmodules --get $key).Trim()
    $commit = ((& git -C $Parent ls-tree HEAD -- $relative) -split '\s+')[2]
    $cache = if ($ParentCache) { Join-Path $ParentCache $relative } else { $null }
    $child = Join-Path $Parent $relative
    Initialize-Checkout $child $url $commit $cache
    Initialize-Dependencies $child $cache
}
}
Initialize-Dependencies $core $DalamudCache
foreach ($spec in @(@('clientstructs',$structs),@('dalamud',$core))) {
    foreach ($patch in Get-ChildItem -LiteralPath (Join-Path $repoRoot "patches/$($spec[0])") -Filter '*.patch' | Sort-Object Name) {
        Invoke-Git $spec[1] @('apply','--check','--whitespace=nowarn',$patch.FullName)
        Invoke-Git $spec[1] @('apply','--whitespace=nowarn',$patch.FullName)
    }
}
@{ inputHash=$inputHash; dalamud=$lock.dalamud.commit; clientStructs=$lock.clientStructs.commit } | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding utf8
Write-Output $core
