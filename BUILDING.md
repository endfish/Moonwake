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
./scripts/stage-core.ps1 -Destination artifacts/test-home/payloads/current -VCRuntimeDirectory <VS-Redist-x64-Microsoft.VC143.CRT-directory>
```

The source recipe uses exact international commits in `upstream.lock.json`, then applies the numbered patches in order and copies the Moonwake application icon into the injector, boot and crash-handler resources. `0001` carries regional adaptation; the remaining patches provide the independent host, services and Moonwake policy. Submodules retain their pinned upstream revisions. Cloned source trees disable pushing and exclude local collaboration instructions.

Optional `-DalamudCache` and `-ClientStructsCache` inputs reuse local Git objects without copying working-tree modifications. They are only build caches. A modified recipe requires a fresh `-OutputRoot`; the script will never reset an existing source directory. Pass its `Dalamud` subdirectory as `-SourceRoot` to the build and staging scripts.

Staging checks the compiled application icons, includes the three native CRT dependencies from the Visual Studio x64 redistributable directory, and writes SHA-256 hashes for all payload files to `moonwake-build.json`. The injector loads missing native CRT modules from this payload before loading the core. Injection checks the payload hashes before starting the injector. This detects damaged local payloads; it is not a signed release or update trust system.

## Isolated test resources

Install the same stable .NET 10 patch release for both the x64 .NET Runtime and Windows Desktop Runtime. Then choose that installed version explicitly:

```powershell
./scripts/prepare-resources.ps1 -HomeDirectory artifacts/test-home -RuntimeVersion <installed-10.0-version>
```

The maintenance script fetches assets at the international commit pinned in `resources.lock.json` and the Simplified Chinese font directly from Noto. It omits the upstream blacklist and game debug symbols, preserves resource licenses, and copies only the selected runtime components from the local Microsoft installation. It does not copy any launcher settings, plugin repositories or plugins. The desktop app itself has no automatic download or login flow.

Resource preparation also applies the Moonwake logo and the terminology overlay in `resources/zh-cn-overrides.json`. To update an existing development asset directory after editing that overlay, run `scripts/apply-brand-assets.ps1 -AssetDirectory <asset-directory>`. The new core defaults to Moonwake Blue; existing user themes are preserved.

Native menu entries use an ice-blue M prefix. Plugins using the former default red D prefix also receive the new default, while custom prefixes remain intact. Plugin lists and notifications use the Moonwake crescent when a plugin has no icon of its own.

Run `scripts/check-localization.ps1 -SourceRoot <prepared-Dalamud-directory> -AssetDirectory <asset-directory>` to check translated UI keys, chat-channel labels and style-color labels. Chat labels follow the CN client's terminology; Debug retains its original label. Technical identifiers and font names retain their original spelling.

The host is a Windows GUI executable with a `requireAdministrator` manifest. Double-clicking requests elevation without creating a console window; the Windows prompt must be handled by the user. Run EXE CLI calls from an elevated shell and pipe them to `Out-Host`, or redirect output from an elevated parent process. `scripts/check-shell.ps1` verifies the compiled GUI subsystem and administrator manifest, then tests CLI output through the managed assembly without requesting interactive elevation on a build agent.

The data root is always `%APPDATA%/XIVLauncherCN`; a first launch creates it if missing. Installed plugins and plugin configuration use the shared `installedPlugins` and `pluginConfigs` directories. Core preferences use `moonwake/config.json`; UI layout and the configuration backup database live beside that file. Plugin configuration paths are resolved from the installed-plugin directory, independently of the core configuration path. Logs and temporary files use the Moonwake subdirectory. Core/runtime/assets stay in versioned component folders within the shared root, without replacing the CN launcher's addon core.

On the first initialization, `CoreProfileInitializer` copies only the repository list, development registrations and plugin loading/profile preferences into a new Moonwake configuration. Development DLL paths and identities remain unchanged; binaries are not copied. Later starts preserve Moonwake settings and do not automatically resync repositories. The repository settings page offers an explicit additive CN sync, preserving existing addresses and enabled states. The CN configuration is never used as Moonwake's save target. There is no profile selector or MOONWAKE_HOME override.

Opening the plugin manager uses its current in-memory repository snapshot. Startup, configured automatic updates, repository changes and the explicit refresh button can refresh metadata. Background refresh retains the previous list until a successful replacement and does not show the initial loading overlay again. A failed refresh retains the last successful list and exposes the repository error.

## Compatibility and testing

New configuration admits the currently adapted CN client `2026.10.06.0000.0000` and enables ordinary plugin loading. The desktop always uses ordinary injection and does not enable safe mode. Other client versions require a matching adaptation. An explicit research mode remains available only as a diagnostic CLI command:

```powershell
./artifacts/shell/Moonwake.exe inject --pid <game-pid> --research | Out-Host
```

Research mode always disables plugins. It still rejects an existing or unreadable Dalamud module and verifies payload integrity. It does not establish compatibility or make a client update safe.

The interface hides after detecting the loaded core module and holds the selected process instance until it exits. It creates no tray icon. Holding the process handle prevents PID reuse from switching the tracked game. Module presence confirms injection progress, not successful loading of every core service.

Offline checks include the shell lifecycle/gating suite, upstream core tests, FFXIVClientStructs signature resolution and CExporter layout diagnostics. Signature resolution must be run against the intended game executable; matches alone do not prove field layout correctness. `validation/cexporter-baseline.txt` records known inherited layout diagnostics for comparison, not a compatibility exemption. The upstream concurrent file test may intermittently fail on Windows when its unsynchronised delete races other operations; retain failures and investigate rather than hiding them. The language-pack integration test additionally requires its external fixture.

For a client update, run `scripts/check-client.ps1 -GameExecutable <ffxiv_dx11.exe> -SourceRoot <prepared-Dalamud-directory>`. It records the client version, executable hash, source recipe and resolved/total signature counts under `artifacts/`. Missing signatures fail the check even when the upstream resolver process exits successfully. The check does not add a supported game version. Use `scripts/check-layout.ps1` separately to compare inherited layout diagnostics.

Before a public binary release, verify startup, Chinese fonts and text input, UI interaction, login/logout, scene transitions, core configuration, clean game exit, and selected plugins. Record the scope of completed checks separately from local-use configuration. No game files or local test logs belong in this repository.

## Distribution status

The current adaptation targets CN client `2026.10.06.0000.0000` for local beta use. There is no published binary release yet. Build a complete installer-free package from clean, prepared components:

```powershell
./scripts/build-distribution.ps1 -PayloadDirectory <staged-payload> -AssetDirectory <prepared-assets> -RuntimeDirectory <prepared-runtime> -OutputDirectory artifacts/releases/Moonwake-win-x64
```

The script embeds only these runtime components and their SHA-256 manifest in a self-contained EXE, then creates a ZIP containing the EXE and license notices. Never use a personal profile as one of these component directories. The package excludes plugin repositories, installed/development plugins and personal configuration. The data home is `%APPDATA%/XIVLauncherCN`; a fresh home is created automatically. `Moonwake.exe prepare` performs the same initialization without injecting a game.

Initialization verifies bundled resources, stages missing components, and only switches component paths after verification succeeds. Damaged components receive a new directory; old components and all personal data are preserved. Existing user preferences and normal plugin loading remain intact. There is no automatic updater yet: replace the tool with a newer complete package while retaining the user data directory. Publish the corresponding AGPL source and notices alongside any future public core distribution.
