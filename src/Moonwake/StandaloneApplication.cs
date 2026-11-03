using Moonwake.Configuration;
using Moonwake.Infrastructure;
using Moonwake.Models;
using Moonwake.Services;
using Moonwake.Windows;

namespace Moonwake;

internal static class StandaloneApplication
{
    public static int RunGui()
    {
        StandaloneLogger? logger = null;
        try
        {
            var paths = StandalonePaths.Create();
            paths.EnsureDirectories();
            logger = new StandaloneLogger(paths);
            logger.Info($"数据目录：{paths.HomeDirectory}");
            StandaloneConfiguration configuration;
            if (DistributionBootstrap.IsBundled)
            {
                using var preparation = new PreparationForm(paths);
                Application.Run(preparation);
                if (preparation.Failure is not null) throw preparation.Failure;
                configuration = preparation.Configuration!;
            }
            else configuration = new ConfigurationStore(paths).LoadOrCreate();
            CoreProfileInitializer.EnsureCreated(paths);
            var validator = new PayloadValidator(paths);
            Application.Run(new LauncherForm(configuration, new GameProcessService(), new InjectionService(paths, validator, logger), logger, paths));
            return 0;
        }
        catch (Exception ex)
        {
            logger?.Error(ex.ToString());
            MessageBox.Show(ex.Message, "Moonwake 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally { logger?.Dispose(); }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 2 && args[0] == "verify-bundle")
        {
            var verificationPaths = StandalonePaths.ForRoot(args[1]);
            var verifiedConfiguration = DistributionBootstrap.Prepare(verificationPaths);
            CoreProfileInitializer.EnsureCreated(verificationPaths);
            var result = new PayloadValidator(verificationPaths).Validate(verifiedConfiguration);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { version = UpdateService.CurrentVersion, bundled = DistributionBootstrap.IsBundled, result.IsValid, result.Errors }));
            return DistributionBootstrap.IsBundled && result.IsValid ? 0 : 2;
        }
        if (args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine("Moonwake\n  prepare\n  init\n  status\n  inject --pid <PID> [--research] [--no-plugins]\n  help");
            return 0;
        }
        var paths = StandalonePaths.Create();
        paths.EnsureDirectories();
        using var logger = new StandaloneLogger(paths);
        try
        {
            if (args.Length == 2 && args[0] == "--render-preview") return RenderPreview(paths, logger, args[1]);
            var configuration = DistributionBootstrap.Prepare(paths);
            CoreProfileInitializer.EnsureCreated(paths);
            var validator = new PayloadValidator(paths);
            switch (args[0].ToLowerInvariant())
            {
                case "init":
                case "prepare":
                    paths.EnsureConfiguredDirectories(configuration);
                    logger.Info($"Moonwake 数据目录：{paths.HomeDirectory}");
                    return 0;
                case "status":
                    var validation = validator.Validate(configuration);
                    logger.Info($"数据目录：{paths.HomeDirectory}");
                    logger.Info($"插件目录：{paths.InstalledPluginsDirectory}");
                    logger.Info($"核心配置：{paths.DalamudConfigurationPath}");
                    foreach (var error in validation.Errors) logger.Warn(error);
                    foreach (var process in new GameProcessService().Discover())
                        logger.Info($"PID={process.ProcessId} Version={process.GameVersion ?? "未知"} Core={process.DalamudState}");
                    logger.Info($"已验证游戏版本：{string.Join(", ", configuration.SupportedGameVersions)}");
                    return validation.IsValid ? 0 : 2;
                case "inject":
                    return await InjectAsync(args, paths, configuration, validator, logger);
                case "help": case "--help": case "-h":
                    Console.WriteLine("Moonwake\n  prepare\n  init\n  status\n  inject --pid <PID> [--research] [--no-plugins]\n  help");
                    return 0;
                default:
                    logger.Error("未知命令。使用 Moonwake help 查看可用命令。");
                    return 2;
            }
        }
        catch (Exception ex) { logger.Error(ex.ToString()); return 1; }
    }

    private static async Task<int> InjectAsync(string[] args, StandalonePaths paths, StandaloneConfiguration configuration,
        PayloadValidator validator, StandaloneLogger logger)
    {
        var index = Array.IndexOf(args, "--pid");
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var pid) || pid <= 0)
            throw new ArgumentException("必须使用 --pid 明确选择游戏进程。");
        var target = new GameProcessService().Find(pid) ?? throw new InvalidOperationException("目标不是正在运行的 ffxiv_dx11 进程。");
        using var session = GameSession.Capture(target);
        var result = await new InjectionService(paths, validator, logger).InjectAsync(target, configuration,
            args.Contains("--research"), args.Contains("--no-plugins"), CancellationToken.None);
        if (!result.Success) { logger.Error(result.Message); return 4; }
        logger.Info(result.Message + " Moonwake 将在该游戏实例退出后结束。");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { await session.WaitForExitAsync(cancellation.Token); }
        catch (OperationCanceledException) { logger.Info("已停止等待；游戏和已加载的核心不受影响。"); }
        finally { Console.CancelKeyPress -= handler; }
        return 0;
    }

    private static int RenderPreview(StandalonePaths paths, StandaloneLogger logger, string destination)
    {
        ApplicationConfiguration.Initialize();
        using var form = new LauncherForm(new StandaloneConfiguration(), new GameProcessService(),
            new InjectionService(paths, new PayloadValidator(paths), logger), logger, paths, preview: true);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
        form.Refresh();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.GetFullPath(destination), System.Drawing.Imaging.ImageFormat.Png);
        form.Close();
        return 0;
    }
}
