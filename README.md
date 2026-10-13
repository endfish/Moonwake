<div align="center">

<img src="assets/moonwake-mark.png" width="160" alt="Moonwake emblem" />

# Moonwake

**A quiet companion for your next adventure.**

独立的国服 Dalamud 注入工具 · by Endfish

</div>

Moonwake 附加到已经启动的 FFXIV 客户端，为它加载独立维护的 Dalamud。无需通过登录器启动游戏；不处理账号、密码、登录或游戏更新。

**开发预览：尚未完成新核心的游戏内验证，当前没有受支持的正式游戏版本，也没有公开二进制发行版。**

## 使用方式

正常启动游戏，打开 Moonwake，选择目标游戏，再点击注入。成功后窗口隐藏，不驻留系统托盘；目标游戏实例退出后 Moonwake 自动结束。手动关闭 Moonwake 不会结束游戏。

<img src="assets/window-preview.png" width="400" alt="Moonwake compact desktop window; example process" />

Moonwake 使用自己的 `%APPDATA%/Moonwake` 数据目录。旧环境可以保留用于过渡，两个核心不能同时注入同一个游戏进程。

## 独立维护

- 核心源码直接固定到 [goatcorp/Dalamud](https://github.com/goatcorp/Dalamud)。
- 客户端结构直接固定到 [aers/FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs)。
- `upstream.lock.json` 记录基线；`patches/` 分开记录国服适配、独立运行支持和 Moonwake 策略。
- 不内置官方插件仓库，不读取外部插件黑名单。技术兼容性检查继续保留。
- 不提供官方分支、反馈或核心更新服务。市场板上传默认关闭。
- 插件仓库由使用者明确添加；第三方插件本身的网络行为不由此保证。

## 从源码构建

需要 Windows x64、.NET 10 SDK、Git。核心还需要 Visual Studio 2022 C++ Build Tools / MSVC v143、Windows SDK 和 CMake 4.0 以上。

```powershell
./scripts/build-shell.ps1
./scripts/prepare-core.ps1
./scripts/build-core.ps1 -CMakePath cmake
```

桌面壳输出到 `artifacts/shell/`。核心源代码在 `.build/` 中从锁定的国际服提交物化并应用补丁；不会使用已安装的国服核心二进制。

运行还需要与核心匹配的 .NET Runtime 和 Assets。它们不进入本仓库，客户端不自动下载。开发阶段可用维护脚本准备隔离资源，具体方法见 [BUILDING.md](BUILDING.md)。

## 来源与许可

Moonwake 桌面壳由 Endfish 编写，源自 Endfish 的 DalamudStandaloneCN，使用 MIT 许可。Dalamud 补丁按 AGPL-3.0-or-later 提供，FFXIVClientStructs 补丁按 MIT 提供；上游及参考实现的版权声明保留在 `licenses/` 和 `NOTICE.md`。

Moonwake 是独立项目，与 Square Enix、goatcorp 或 OtterCorp 无隶属关系。
