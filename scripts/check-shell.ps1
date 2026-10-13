[CmdletBinding()]
param([string]$Executable)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$Executable) { $Executable = Join-Path $repoRoot 'artifacts/shell/Moonwake.exe' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$stream = [IO.File]::OpenRead($Executable)
$reader = [IO.BinaryReader]::new($stream)
try {
    $stream.Position = 0x3C
    $peOffset = $reader.ReadInt32()
    $stream.Position = $peOffset + 24 + 68
    if ($reader.ReadUInt16() -ne 2) { throw 'The desktop EXE must use the Windows GUI subsystem.' }
} finally { $reader.Dispose() }
$start = [Diagnostics.ProcessStartInfo]::new($Executable)
$start.ArgumentList.Add('help')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['MOONWAKE_HOME'] = Join-Path $repoRoot ('artifacts/shell-check/' + [Guid]::NewGuid().ToString('N'))
$process = [Diagnostics.Process]::Start($start)
try {
    $outputTask = $process.StandardOutput.ReadToEndAsync()
    $errorTask = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(15000)) { throw 'CLI help did not finish.' }
    $output = $outputTask.GetAwaiter().GetResult()
    $errors = $errorTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0 -or $output -notmatch 'inject --pid' -or $errors) { throw 'CLI help output or exit code is incorrect.' }
} finally { $process.Dispose() }
Write-Output 'PASS: GUI executable has no startup console and retains redirected CLI output.'
