using Moonwake.Configuration;
using Moonwake.Infrastructure;
using Moonwake.Services;
using Moonwake.Windows;

namespace Moonwake;

internal static class StandaloneApplication
{
    public static int RunGui()
    {
        var paths = StandalonePaths.Create();
        paths.EnsureDirectories();
        using var logger = new StandaloneLogger(paths);
        try
        {
            var configuration = new ConfigurationStore(paths).LoadOrCreate();
            var validator = new PayloadValidator(paths);
            Application.Run(new LauncherForm(configuration, new GameProcessService(), new InjectionService(paths, validator, logger), logger, paths));
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error(ex.ToString());
            MessageBox.Show(ex.Message, "Moonwake 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var paths = StandalonePaths.Create();
        paths.EnsureDirectories();
        using var logger = new StandaloneLogger(paths);
        try
        {
            if (args.Length == 2 && args[0] == "--render-preview") return RenderPreview(paths, logger, args[1]);
            var configuration = new ConfigurationStore(paths).LoadOrCreate();
            var validator = new PayloadValidator(paths);
            switch (args[0].ToLowerInvariant())
            {
                case "init":
                    paths.EnsureConfiguredDirectories(configuration);
                    logger.Info($"Moonwake 数据目录：{paths.HomeDirectory}");
                    return 0;
                case "status":
                    var validation = validator.Validate(configuration);
                    logger.Info($"数据目录：{paths.HomeDirectory}");
                    foreach (var error in validation.Errors) logger.Warn(error);
                    foreach (var process in new GameProcessService().Discover())
                        logger.Info($"PID={process.ProcessId} Version={process.GameVersion ?? "未知"} Core={process.DalamudState}");
                    logger.Info($"已验证游戏版本：{string.Join(", ", configuration.SupportedGameVersions)}");
                    return validation.IsValid ? 0 : 2;
                case "inject":
                    return await InjectAsync(args, paths, configuration, validator, logger);
                case "help": case "--help": case "-h":
                    Console.WriteLine("Moonwake\n  init\n  status\n  inject --pid <PID> [--research] [--no-plugins]\n  help");
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
