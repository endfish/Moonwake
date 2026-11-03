[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$AssetDirectory,
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ReleaseId = '0.2.0-preview.3',
    [string[]]$SupportedGameVersions = @('2026.10.06.0000.0000')
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory.' }
foreach ($name in @('PayloadDirectory','AssetDirectory','RuntimeDirectory')) {
    Set-Variable -Name $name -Value ((Resolve-Path -LiteralPath (Get-Variable -Name $name -ValueOnly)).Path)
}
$payloadManifest = Get-Content -LiteralPath (Join-Path $PayloadDirectory 'moonwake-build.json') -Raw | ConvertFrom-Json
foreach ($name in @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')) {
    if (!$payloadManifest.files.PSObject.Properties[$name]) { throw "Payload is missing bundled native runtime: $name" }
}
foreach ($item in $payloadManifest.files.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path $PayloadDirectory $item.Name) -Algorithm SHA256).Hash -ne $item.Value) { throw "Invalid payload file: $($item.Name)" }
}
$runtimeVersions = @(Get-ChildItem -LiteralPath (Join-Path $RuntimeDirectory 'host\fxr') -Directory)
if ($runtimeVersions.Count -ne 1) { throw 'Bundle exactly one runtime version.' }
$runtimeVersion = $runtimeVersions[0].Name
$componentPaths = @(
    ('payloads/' + (Split-Path $PayloadDirectory -Leaf)),
    ('assets/' + (Split-Path $AssetDirectory -Leaf)),
    ('runtime/' + $runtimeVersion)
)
$work = Join-Path $repoRoot ('artifacts/distribution-build/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
$archivePath = Join-Path $work 'distribution.zip'
$manifestPath = Join-Path $work 'distribution.json'
$files = [ordered]@{}
$sources = @($PayloadDirectory,$AssetDirectory,$RuntimeDirectory)
$stream = [IO.File]::Create($archivePath)
$zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    for ($index = 0; $index -lt $sources.Count; $index++) {
        if (@(Get-ChildItem -LiteralPath $sources[$index] -Recurse -Force -Attributes ReparsePoint).Count) { throw 'Distribution components cannot contain links.' }
        foreach ($file in Get-ChildItem -LiteralPath $sources[$index] -Recurse -File | Sort-Object FullName) {
            $relative = [IO.Path]::GetRelativePath($sources[$index], $file.FullName).Replace('\','/')
            if ($index -eq 0 -and $relative -ne 'moonwake-build.json' -and !$payloadManifest.files.PSObject.Properties[$relative]) { continue }
            $name = $componentPaths[$index] + '/' + $relative
            $files[$name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally { $zip.Dispose(); $stream.Dispose() }
@{schemaVersion=1;releaseId=$ReleaseId;payloadDirectory=$componentPaths[0];assetDirectory=$componentPaths[1];runtimeDirectory=$componentPaths[2];supportedGameVersions=$SupportedGameVersions;files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
$publish = Join-Path $work 'publish'
dotnet publish (Join-Path $repoRoot 'src/Moonwake/Moonwake.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "-p:DistributionArchive=$archivePath" "-p:DistributionManifest=$manifestPath" "-p:Version=$ReleaseId" -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Distribution build failed.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $publish 'Moonwake.exe') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'licenses') -Destination $OutputDirectory -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $OutputDirectory 'licenses/Moonwake-MIT.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'NOTICE.md') -Destination (Join-Path $OutputDirectory 'licenses/NOTICE.md')
@'
Moonwake 内测预览版

解压整个工具目录，双击 Moonwake.exe，确认 Windows 管理员权限提示。
首次启动会自动创建 %APPDATA%\XIVLauncherCN，并从程序内置资源准备 Moonwake 核心、字体和运行时。
无需安装 .NET、访问开发工作区或下载首次运行资源。
启动游戏后选择进程，点击“注入”。成功后隐藏窗口，游戏退出时自动结束。

直接共用 XIVLauncherCN 的 installedPlugins 和 pluginConfigs。
Moonwake 核心设置、窗口布局、日志与配置备份独立保存在 XIVLauncherCN/moonwake。
首次使用沿用国服自定义仓库、开发插件入口和插件启用状态；之后独立保存加载偏好，开发插件仍使用原 DLL 路径。
在游戏内设置的“自定义插件仓库”处，可点击“同步国服自定义仓库”。默认合并新地址，保留已有地址及其启用状态，不修改国服配置。
Moonwake 自己编译的核心、资源和运行时也保存在 XIVLauncherCN 内，不替换国服启动器的核心。
不再创建 %APPDATA%\Moonwake。切换工具后继续使用同一套插件与插件自身配置。

本包不包含任何人的插件、仓库地址、个人配置、账号或开发路径。
程序每天在后台检查一次 GitHub 更新。窗口底部可手动检查，确认后下载、校验并更新；游戏已加载核心时请先退出游戏。
更新只替换工具文件，不覆盖插件和个人设置；上一版工具保留在工具目录的 .moonwake-previous 中。
无法连接 GitHub 时，请检查网络，必要时使用网络代理；也可从发布页手动下载完整包并覆盖工具文件。

目前为不稳定测试版。如遇问题，请在 https://github.com/endfish/Moonwake/issues 提交反馈。
'@ | Set-Content -LiteralPath (Join-Path $OutputDirectory '使用说明.txt') -Encoding utf8
$releaseArchive = Join-Path (Split-Path -Parent $OutputDirectory) "Moonwake-$ReleaseId-win-x64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($OutputDirectory, $releaseArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
$archiveHash = (Get-FileHash -LiteralPath $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $([IO.Path]::GetFileName($releaseArchive))" | Set-Content -LiteralPath ($releaseArchive + '.sha256') -Encoding ascii
[pscustomobject]@{Output=$OutputDirectory;Archive=$releaseArchive;Components=$files.Count;Manifest=$manifestPath;BuildDirectory=$work;ExeSha256=(Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'Moonwake.exe') -Algorithm SHA256).Hash;ArchiveSha256=$archiveHash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'build-result.json') -Encoding utf8
Write-Output "Distribution created: $releaseArchive"
Write-Output "Build evidence: $work"
