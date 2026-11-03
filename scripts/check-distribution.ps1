[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$root = Join-Path $repoRoot ('artifacts/distribution-check/' + [Guid]::NewGuid().ToString('N'))
function Invoke-Verification {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['__COMPAT_LAYER'] = 'RunAsInvoker'
    $start.ArgumentList.Add('verify-bundle')
    $start.ArgumentList.Add($root)
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(60000)) { throw 'Bundled initialization timed out.' }
        $text = $output.GetAwaiter().GetResult()
        $errorText = $errors.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Bundle verification failed: $text $errorText" }
        $result = $text | ConvertFrom-Json
        if (!$result.IsValid -or !$result.bundled) { throw 'Bundle did not report a complete installation.' }
    } finally { $process.Dispose() }
}
Invoke-Verification
$files = [ordered]@{}
(@{
    'moonwake/config.json'='{"LanguageOverride":"zh","ChosenStyle":"Personal Blue","PluginSafeMode":false,"ThirdRepoList":[{"Url":"https://example.test/repo.json","IsEnabled":true}],"DevPluginLoadLocations":[{"Path":"example.dll","IsEnabled":true}]}'
    'dalamudConfig.json'='{"ChosenStyle":"CN personal style"}'
    'installedPlugins/Personal/1.0/Personal.dll'='personal plugin fixture'
    'pluginConfigs/Personal.json'='{"keep":true}'
}).GetEnumerator() | ForEach-Object {
    $path = Join-Path $root $_.Key
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, $_.Value)
    $files[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
Invoke-Verification
foreach ($entry in $files.GetEnumerator()) {
    if ((Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash -ne $entry.Value) { throw 'Existing player data changed during repeat initialization.' }
}
@{Root=$root;PreservedFiles=$files.Count;Passed=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'verification-result.json')
Write-Output 'PASS: actual full EXE initializes an empty home, validates runtime/resources, and preserves existing core/CN/plugin data on repeat startup.'
