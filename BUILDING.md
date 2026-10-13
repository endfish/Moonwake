# Building Moonwake

## Desktop application

Use Windows x64, PowerShell 7, Git and a stable .NET 10 SDK.

```powershell
./scripts/build-shell.ps1
dotnet run --project tests/Moonwake.Tests -c Release
```

`artifacts/shell/Moonwake.exe` is a self-contained desktop host. It is not a complete Dalamud distribution: the independently built core and its runtime resources are prepared separately. Run with no arguments for the compact illustrated window. The process selector refreshes automatically; the first discovered game is selected by default.

## Core source and build

Install Visual Studio 2022 C++ Build Tools with MSVC v143 and a Windows SDK, plus CMake 4 or later.

```powershell
./scripts/prepare-core.ps1
./scripts/build-core.ps1 -CMakePath cmake
./scripts/stage-core.ps1 -Destination artifacts/test-home/payloads/current
```

The source recipe uses exact international commits in `upstream.lock.json`, then applies the numbered patches in order. `0001` carries regional adaptation; the remaining patches provide the independent host, services and Moonwake policy. Submodules retain their pinned upstream revisions. Cloned source trees disable pushing and exclude local collaboration instructions.

Optional `-DalamudCache` and `-ClientStructsCache` inputs reuse local Git objects without copying working-tree modifications. They are only build caches. A modified recipe requires a fresh `-OutputRoot`; the script will never reset an existing source directory. Pass its `Dalamud` subdirectory as `-SourceRoot` to the build and staging scripts.

Staging writes SHA-256 hashes for all payload files to `moonwake-build.json`. Injection checks these hashes before starting the injector. This detects damaged local payloads; it is not a signed release or update trust system.

## Isolated test resources

Install the same stable .NET 10 patch release for both the x64 .NET Runtime and Windows Desktop Runtime. Then choose that installed version explicitly:

```powershell
./scripts/prepare-resources.ps1 -HomeDirectory artifacts/test-home -RuntimeVersion <installed-10.0-version>
$env:MOONWAKE_HOME = Join-Path $PWD 'artifacts/test-home'
./artifacts/shell/Moonwake.exe init
./artifacts/shell/Moonwake.exe status
```

The maintenance script fetches assets at the international commit pinned in `resources.lock.json` and the Simplified Chinese font directly from Noto. It omits the upstream blacklist and game debug symbols, preserves resource licenses, and copies only the selected runtime components from the local Microsoft installation. It does not copy any launcher settings, plugin repositories or plugins. The desktop app itself has no automatic download or login flow.

Resource preparation also applies the Moonwake logo and the terminology overlay in `resources/zh-cn-overrides.json`. To update an existing development asset directory after editing that overlay, run `scripts/apply-brand-assets.ps1 -AssetDirectory <asset-directory>`. The new core defaults to Moonwake Blue; existing user themes are preserved.

The default data location is `%APPDATA%/Moonwake`. `MOONWAKE_HOME` overrides it for experiments. Test resources and payloads stay out of Git. Resource preparation refuses existing destination directories; use a new test home for another bundle.

The desktop import dialog copies API 15 plugin files and optional per-plugin configuration from an existing profile. It never shares live directories or imports the source core/global settings. Existing target plugins and configuration are preserved, copied plugins receive new disabled identities, and repository URLs require individual selection. API matching is only an initial filter, not proof of game compatibility. File staging and rollback protect against partial copies; staging files and configuration backups remain under `imports/`.

Opening the plugin manager uses its current in-memory repository snapshot. Startup, configured automatic updates, repository changes and the explicit refresh button can refresh metadata. Background refresh retains the previous list until a successful replacement and does not show the initial loading overlay again. A failed refresh retains the last successful list and exposes the repository error.

## Compatibility and testing

New configuration has an empty `supportedGameVersions` list and disables plugin loading. Ordinary injection remains unavailable until a precise game version has passed in-game checks. An explicit diagnostic command is available for controlled tests:

```powershell
./artifacts/shell/Moonwake.exe inject --pid <game-pid> --research
```

Research mode always disables plugins. It still rejects an existing or unreadable Dalamud module and verifies payload integrity. It does not establish compatibility or make a client update safe.

The interface hides after detecting the loaded core module and holds the selected process instance until it exits. It creates no tray icon. Holding the process handle prevents PID reuse from switching the tracked game. Module presence confirms injection progress, not successful loading of every core service.

Offline checks include the shell lifecycle/gating suite, upstream core tests, FFXIVClientStructs signature resolution and CExporter layout diagnostics. Signature resolution must be run against the intended game executable; matches alone do not prove field layout correctness. `validation/cexporter-baseline.txt` records known inherited layout diagnostics for comparison, not a compatibility exemption. The upstream concurrent file test may intermittently fail on Windows when its unsynchronised delete races other operations; retain failures and investigate rather than hiding them. The language-pack integration test additionally requires its external fixture.

For a client update, run `scripts/check-client.ps1 -GameExecutable <ffxiv_dx11.exe> -SourceRoot <prepared-Dalamud-directory>`. It records the client version, executable hash, source recipe and resolved/total signature counts under `artifacts/`. Missing signatures fail the check even when the upstream resolver process exits successfully. The check does not add a supported game version. Use `scripts/check-layout.ps1` separately to compare inherited layout diagnostics.

Before admitting a client version, verify startup, Chinese fonts and text input, UI interaction, login/logout, scene transitions, core configuration, clean game exit, and selected plugins in an isolated test profile. No game files or local test logs belong in this repository.

## Distribution status

This is a development preview. There is no supported client version or published binary release yet. A future release can package the EXE, independently built core, runtime, assets and license notices after game validation and a separate packaging review. The AGPL-covered core must retain its corresponding source and notices.
