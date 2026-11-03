[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameExecutable,
    [string]$SourceRoot,
    [string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$SourceRoot) { $SourceRoot = Join-Path $repoRoot '.build/source/Dalamud' }
if (!$ReportDirectory) { $ReportDirectory = Join-Path $repoRoot ('artifacts/client-check-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$GameExecutable = (Resolve-Path -LiteralPath $GameExecutable).Path
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$versionFile = Join-Path (Split-Path -Parent $GameExecutable) 'ffxivgame.ver'
$version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
$structs = Join-Path $SourceRoot 'lib/FFXIVClientStructs'
$sourceManifest = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $SourceRoot) 'moonwake-source.json') -Raw | ConvertFrom-Json
$inputFiles = @((Join-Path $repoRoot 'upstream.lock.json'), (Join-Path $repoRoot 'src/Moonwake/Resources/app-icon.ico')) + @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'patches') -Recurse -Filter '*.patch' | Sort-Object FullName | ForEach-Object FullName)
$inputHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes((($inputFiles | ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}) -join ''))))
if ($sourceManifest.inputHash -ne $inputHash) { throw 'Source recipe changed; materialize and build the current recipe first.' }
$hashBefore = (Get-FileHash -LiteralPath $GameExecutable -Algorithm SHA256).Hash
Push-Location $structs
try {
    $output = @(& dotnet run --project FFXIVClientStructs.ResolverTester -c Release -- $GameExecutable 2>&1 | ForEach-Object { "$_" })
    $resolverExit = $LASTEXITCODE
} finally { Pop-Location }
$output | Set-Content -LiteralPath (Join-Path $ReportDirectory 'resolver.log') -Encoding utf8
$failed = @($output | Where-Object { $_ -match '^\[FAIL\]' })
$addresses = @($output | Where-Object { $_ -match '^FFXIVClientStructs\.\S+ [0-9A-F]+$' })
$resolved = @($addresses | Where-Object { $_ -notmatch ' 0$' })
$hashAfter = (Get-FileHash -LiteralPath $GameExecutable -Algorithm SHA256).Hash
$passed = $resolverExit -eq 0 -and $failed.Count -eq 0 -and $addresses.Count -gt 0 -and $resolved.Count -eq $addresses.Count -and $hashBefore -eq $hashAfter
[ordered]@{
    gameVersion=$version; executableSha256=$hashBefore; source=$sourceManifest
    resolved=$resolved.Count; total=$addresses.Count; unresolved=$failed
    offlineSignaturesPassed=$passed; gameValidated=$false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'client-check.json') -Encoding utf8
Write-Output "$version : $($resolved.Count)/$($addresses.Count) signatures resolved."
if (!$passed) { throw "Client signature check failed. See $ReportDirectory/resolver.log" }
Write-Output 'Offline checks do not admit this version for ordinary injection; in-game validation is still required.'
