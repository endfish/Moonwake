<div align="center">

<img src="assets/moonwake-mark.png" width="160" alt="Moonwake emblem" />

# Moonwake

**A quiet companion for your next adventure.**

国服 Dalamud 注入工具

</div>

Moonwake 附加到已经启动的 FFXIV 客户端，为它加载 Dalamud 核心。无需通过登录器启动游戏；不处理账号、密码、登录或游戏更新。

**目前为不稳定测试版，当前适配国服客户端 `2026.10.06.0000.0000`。** 如遇到启动、注入、更新或插件兼容问题，欢迎随时在 [GitHub Issues](https://github.com/endfish/Moonwake/issues) 提交反馈。请注明 Moonwake 版本、游戏版本、复现步骤及相关错误日志。

从 [GitHub Releases](https://github.com/endfish/Moonwake/releases) 下载 `Moonwake-版本号-win-x64.zip`，解压后运行 `Moonwake.exe`。无需安装器或额外安装 .NET；GitHub 自动生成的 Source code 压缩包用于源码构建。

> **Moonwake 没有内置官方插件仓库或官方插件分发服务器，所有插件仓库都需要作为自定义仓库添加。** 如果电脑上没有已有的国服自定义仓库，首次打开“所有插件”可能为空，这是正常情况。请按下方说明添加仓库。

## 首次添加插件仓库

1. 在游戏内打开 Moonwake 设置（也可输入 `/xlsettings`），进入“测试版与高级设置”→“自定义插件仓库”。
2. 填写插件作者提供的完整仓库地址，启用并保存。仓库地址通常指向 JSON 清单或分发接口，与 GitHub 项目主页不同。
3. 打开插件管理器（`/xlplugins`），点击“刷新仓库”，随后在“所有插件”中选择安装。

已有国服卫月自定义仓库的用户，首次启动时会沿用现有列表；之后可在上述设置中点击“同步国服自定义仓库”，默认合并新地址。Moonwake 不会自动添加国服官方仓库。

一个可选的自定义仓库示例是维护者的插件库：`https://raw.githubusercontent.com/endfish/DalamudPlugins/main/repo.json`。它只包含该仓库维护的插件，并不是 Moonwake 官方插件源或完整插件目录。其他插件请使用对应作者提供的地址。

## 使用方式

正常启动游戏，打开 Moonwake，选择目标游戏，再点击注入。成功后窗口隐藏，不驻留系统托盘；目标游戏实例退出后 Moonwake 自动结束。手动关闭 Moonwake 不会结束游戏。

<img src="assets/window-preview.png" width="400" alt="Moonwake compact desktop window; example process" />

免安装包的工具目录只包含程序和许可说明。Moonwake 使用当前 Windows 用户的 `%APPDATA%/XIVLauncherCN` 作为统一数据根目录：已有目录直接沿用；首次使用时自动创建，并准备包内附带的 Moonwake 核心、字体和运行时。程序不会创建 `%APPDATA%/Moonwake`。

插件文件与插件自身配置直接共用国服的 `installedPlugins` 和 `pluginConfigs`，无需搬迁。Moonwake 的核心设置、窗口布局和配置备份独立保存在 `XIVLauncherCN/moonwake`，国服的 `dalamudConfig.json` 只作为首次初始化和手动同步的数据来源。Moonwake 仍运行自己编译的核心，运行资源放在 XIVLauncherCN 内的版本目录，不替换国服启动器的核心。两个核心不能同时注入同一个游戏进程。

EXE 启动时会请求管理员权限，以便读取同样以管理员身份运行的游戏进程。点击“注入”后正常加载已启用的插件；未适配的后续游戏版本会提示更新 Moonwake。

首次使用自动沿用国服登记的开发插件路径、插件启用状态和配置组，之后由 Moonwake 独立保存加载偏好。开发插件直接加载原 DLL，重新编译后可以自动重载。插件安装、更新、卸载及插件自身设置仍写入共用目录，切换工具后继续使用。

游戏内界面默认使用 Moonwake Blue 和简体中文。主题、字体、窗口布局、通知等核心设置由 Moonwake 独立管理；可在游戏内设置中调整。

**Moonwake 不内置官方插件仓库。** 首次使用读取国服已有自定义仓库，之后独立保存。在“Moonwake 设置”→“测试版与高级设置”→“自定义插件仓库”中，可添加地址，或点击“同步国服自定义仓库”：默认只合并新地址，保留已有仓库及其启用状态，不修改国服列表。插件问题请联系对应插件或仓库的维护者。

## 从源码构建

需要 Windows x64、.NET 10 SDK、Git。核心还需要 Visual Studio 2022 C++ Build Tools / MSVC v143、Windows SDK 和 CMake 4.0 以上。

```powershell
./scripts/build-shell.ps1
./scripts/prepare-core.ps1
./scripts/build-core.ps1 -CMakePath cmake
```

桌面壳输出到 `artifacts/shell/`。核心源代码在 `.build/` 中从锁定的国际服提交物化并应用补丁；不会使用已安装的国服核心二进制。

上述命令生成用于开发的桌面壳。完整免安装包通过 `scripts/build-distribution.ps1` 嵌入经过校验的核心、Assets、.NET 和原生运行库，首次启动自动准备这些资源；包内不包含个人插件、仓库或配置。构建与打包方法见 [BUILDING.md](BUILDING.md)。

## 更新

启动后每天在后台检查一次 GitHub Releases，窗口底部可随时手动检查。发现新版后，可查看说明并选择“下载并更新”或打开发布页手动下载。程序通过 SHA256 校验完整包，保留上一版工具；下载中断、校验失败或替换失败时继续保留原版本。已加载核心的游戏退出后再更新，下次注入才会使用新核心。

更新只处理工具文件与对应的运行组件，不覆盖共用插件、插件配置或开发插件文件。手动更新时，退出 Moonwake，将新包解压到原工具目录并覆盖程序及许可文件即可。无需删除 `%APPDATA%/XIVLauncherCN`。

预览版会接收后续预览版及正式版；正式版只接收正式版。更新检查不需要 GitHub 账号，不上传角色、服务器或插件清单。GitHub 会收到正常网络请求所需的连接信息。

无法连接 GitHub 或下载缓慢时，请检查网络，必要时使用网络代理后重试。程序使用系统代理设置，也可通过浏览器下载完整包。网络失败不会阻止使用已适配的本地版本。

## 来源与许可

Moonwake 桌面壳由 Astra 编写，采用 AI 开发，不喜勿用。桌面壳使用 MIT 许可。Dalamud 补丁按 AGPL-3.0-or-later 提供，FFXIVClientStructs 补丁按 MIT 提供；上游及参考实现的版权声明保留在 `licenses/` 和 `NOTICE.md`。

Moonwake 是独立项目，与 Square Enix、goatcorp 或 OtterCorp 无隶属关系。
