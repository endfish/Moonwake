[CmdletBinding()]
param([string]$SourceRoot, [string]$CMakePath = 'cmake', [switch]$SkipNative, [string]$ReleaseId = '0.2.0-preview.2')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$SourceRoot) { $SourceRoot = Join-Path $repoRoot '.build/source/Dalamud' }
if (!(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $SourceRoot) 'moonwake-source.json'))) { throw 'Run prepare-core.ps1 first.' }
Push-Location $SourceRoot
try {
    if (!$SkipNative) {
        & $CMakePath --preset vs2022
        if ($LASTEXITCODE -ne 0) { throw 'Native configuration failed.' }
        & $CMakePath --build build --config Release --parallel
        if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
    }
    $sourceMap = [IO.Path]::GetFullPath((Get-Location).Path) + '=/_/Dalamud'
    dotnet build Dalamud/Dalamud.csproj -c Release "-p:PathMap=$sourceMap" -p:UseSharedCompilation=false "-p:SCMVersion=Moonwake-$ReleaseId" -p:Branch=moonwake
    if ($LASTEXITCODE -ne 0) { throw 'Core build failed.' }
    dotnet build Dalamud.Injector/Dalamud.Injector.csproj -c Release "-p:PathMap=$sourceMap" -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Injector build failed.' }
    dotnet test Dalamud.Test/Dalamud.Test.csproj -c Release "-p:PathMap=$sourceMap" -p:UseSharedCompilation=false "-p:SCMVersion=Moonwake-$ReleaseId" -p:Branch=moonwake
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
} finally { Pop-Location }
