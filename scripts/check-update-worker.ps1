[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$root = Join-Path $repoRoot ('artifacts/update-worker-check/' + [Guid]::NewGuid().ToString('N'))
$tool = Join-Path $root 'tool'
$job = Join-Path $root 'job'
$stage = Join-Path $job 'ready'
New-Item -ItemType Directory -Path $tool,$stage,(Join-Path $stage 'licenses') -Force | Out-Null
$target = Join-Path $tool 'Moonwake.exe'
Copy-Item -LiteralPath $Executable -Destination $target
$append = [IO.File]::Open($target, [IO.FileMode]::Append, [IO.FileAccess]::Write)
try { $append.Write([Text.Encoding]::ASCII.GetBytes('old-version-test-marker')) } finally { $append.Dispose() }
$oldHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
Copy-Item -LiteralPath $Executable -Destination (Join-Path $stage 'Moonwake.exe')
'test license' | Set-Content -LiteralPath (Join-Path $stage 'licenses/NOTICE.txt')
'test instructions' | Set-Content -LiteralPath (Join-Path $stage '使用说明.txt')
$hashes = [ordered]@{}
Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\','/')
    $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'ready.json')
$helper = Join-Path $job 'Moonwake.Update.exe'
Copy-Item -LiteralPath $Executable -Destination $helper
$ticket = Join-Path $job 'update.json'
@{ParentId=[int]::MaxValue;ParentStart=0;Target=$target;Stage=$stage;OriginalHash=$oldHash;Relaunch=$false} | ConvertTo-Json | Set-Content -LiteralPath $ticket
$start = [Diagnostics.ProcessStartInfo]::new($helper)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.Environment['__COMPAT_LAYER'] = 'RunAsInvoker'
$start.ArgumentList.Add('--apply-update')
$start.ArgumentList.Add($ticket)
$process = [Diagnostics.Process]::Start($start)
try {
    if (!$process.WaitForExit(45000)) { throw 'Update worker did not exit within 45 seconds.' }
    if ($process.ExitCode -ne 0) { throw "Update worker failed: $($process.ExitCode)" }
} finally { $process.Dispose() }
if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash) { throw 'Updated executable mismatch.' }
$backups = @(Get-ChildItem -LiteralPath (Join-Path $tool '.moonwake-previous') -Recurse -Filter Moonwake.exe -File)
if ($backups.Count -ne 1 -or (Get-FileHash -LiteralPath $backups[0].FullName -Algorithm SHA256).Hash -ne $oldHash) { throw 'Original executable was not preserved.' }
Write-Output 'PASS: actual GUI EXE update worker replaces a real executable and preserves its prior version in an isolated directory.'
