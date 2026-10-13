[CmdletBinding()]
param([string]$Executable, [string]$ManagedAssembly)
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
    $stream.Position = 0
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream, [Reflection.PortableExecutable.PEStreamOptions]::LeaveOpen)
    try {
        $resources = $pe.PEHeaders.SectionHeaders | Where-Object Name -eq '.rsrc'
        if (!$resources) { throw 'The desktop EXE must contain a Windows application manifest.' }
        $stream.Position = $resources.PointerToRawData
        $resourceText = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($resources.SizeOfRawData))
        if ($resourceText -notmatch 'requestedExecutionLevel\s+level="requireAdministrator"\s+uiAccess="false"') {
            throw 'The desktop EXE must request administrator privileges.'
        }
    } finally { $pe.Dispose() }
} finally { $reader.Dispose() }
if (!$ManagedAssembly) { $ManagedAssembly = Join-Path $repoRoot 'src/Moonwake/bin/Release/net10.0-windows/win-x64/Moonwake.dll' }
$ManagedAssembly = (Resolve-Path -LiteralPath $ManagedAssembly).Path
# Test the CLI implementation without triggering an interactive UAC prompt on build agents.
# The apphost's GUI subsystem and administrator manifest are checked separately above.
$start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
$start.ArgumentList.Add($ManagedAssembly)
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
Write-Output 'PASS: GUI executable requests administrator privileges; managed CLI retains redirected output.'
