<div align="center">

<img src="assets/moonwake-mark.png" width="160" alt="Moonwake emblem" />

# Moonwake

**A quiet companion for your next adventure.**

国服 Dalamud 注入工具

</div>

Moonwake 附加到已经启动的 FFXIV 客户端，为它加载 Dalamud 核心。无需通过登录器启动游戏；不处理账号、密码、登录或游戏更新。

**当前适配国服客户端 `2026.10.06.0000.0000`。项目尚未提供公开二进制发行版。**

## 使用方式

正常启动游戏，打开 Moonwake，选择目标游戏，再点击注入。成功后窗口隐藏，不驻留系统托盘；目标游戏实例退出后 Moonwake 自动结束。手动关闭 Moonwake 不会结束游戏。

<img src="assets/window-preview.png" width="400" alt="Moonwake compact desktop window; example process" />

Moonwake 默认使用自己的 `%APPDATA%/Moonwake` 数据目录，也支持通过 EXE 旁的 `moonwake.profile.json` 固定便携数据目录。旧环境可以保留用于过渡，两个核心不能同时注入同一个游戏进程。

注入窗口最底部的“导入插件”可复制已有环境中 API 15 的插件及可选配置，保留源目录和 Moonwake 中已有的内容。导入的插件默认禁用，请在游戏内逐项启用并测试；仓库地址可单独选择，默认不导入。请在注入前完成导入。

EXE 启动时会请求管理员权限，以便读取同样以管理员身份运行的游戏进程。点击“注入”后正常加载已启用的插件；未适配的后续游戏版本会提示更新 Moonwake。

开发插件可一并导入：保留原 DLL 构建路径和自动重载偏好，复制插件配置到 Moonwake，关闭自动启动。之后重新构建本地插件时仍可使用原开发流程；不迁移历史失效路径或其他全局偏好。

**Moonwake 不内置官方插件仓库，所有插件均来自您自行添加的自定义仓库。** 在游戏内打开“Moonwake 设置”→“测试版与高级设置”→“自定义插件仓库”，添加仓库地址并保存；也可以通过“导入插件”选择复制已有的仓库地址。插件问题请联系对应插件或仓库的维护者。

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

Moonwake 桌面壳由 Astra 编写，采用 AI 开发，不喜勿用。桌面壳使用 MIT 许可。Dalamud 补丁按 AGPL-3.0-or-later 提供，FFXIVClientStructs 补丁按 MIT 提供；上游及参考实现的版权声明保留在 `licenses/` 和 `NOTICE.md`。

Moonwake 是独立项目，与 Square Enix、goatcorp 或 OtterCorp 无隶属关系。
