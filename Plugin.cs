using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FateRunner.Core;
using FateRunner.Ipc;
using FateRunner.Windows;

namespace FateRunner;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IFateTable Fates { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;

    private const string Command = "/faterunner";
    private const string ShortCommand = "/fr";

    public Configuration Config { get; }
    public VNavmeshIpc Nav { get; }
    public WrathComboIpc Wrath { get; }
    public LifestreamIpc Lifestream { get; }
    public FateEngine Engine { get; }

    private readonly WindowSystem windows = new("FateRunner");
    private readonly MainWindow mainWindow;

    // IPC for other plugins (CraftBuddy asks for gemstones this way).
    private readonly ICallGateProvider<bool> ipcIsRunning;
    private readonly ICallGateProvider<string> ipcStatus;
    private readonly ICallGateProvider<int> ipcGemstones;
    private readonly ICallGateProvider<int, bool> ipcStartForGemstones;
    private readonly ICallGateProvider<bool> ipcStart;
    private readonly ICallGateProvider<object> ipcStop;
    private readonly ICallGateProvider<string> ipcLastStartError;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Nav = new VNavmeshIpc();
        Wrath = new WrathComboIpc();
        Lifestream = new LifestreamIpc();
        Engine = new FateEngine(Config, Nav, Wrath, Lifestream);

        mainWindow = new MainWindow(this);
        windows.AddWindow(mainWindow);
        windows.AddWindow(new OverlayWindow(this));

        Commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open FateRunner. Subcommands: start, stop." });
        Commands.AddHandler(ShortCommand, new CommandInfo(OnCommand) { HelpMessage = "Same as /faterunner." });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += mainWindow.Toggle;

        ipcIsRunning = PluginInterface.GetIpcProvider<bool>("FateRunner.IsRunning");
        ipcIsRunning.RegisterFunc(() => Engine.Running);
        ipcStatus = PluginInterface.GetIpcProvider<string>("FateRunner.Status");
        ipcStatus.RegisterFunc(() => Engine.Status);
        ipcGemstones = PluginInterface.GetIpcProvider<int>("FateRunner.Gemstones");
        ipcGemstones.RegisterFunc(() => Game.Gemstones);
        ipcStartForGemstones = PluginInterface.GetIpcProvider<int, bool>("FateRunner.StartForGemstones");
        ipcStartForGemstones.RegisterFunc(target => Engine.Start(RunGoal.Gemstones, target, "another plugin") == null);
        ipcStart = PluginInterface.GetIpcProvider<bool>("FateRunner.Start");
        ipcStart.RegisterFunc(() => Engine.Start(Config.Goal, Config.Goal == RunGoal.Leveling ? Config.LevelTarget : Config.GemstoneTarget, null) == null);
        ipcStop = PluginInterface.GetIpcProvider<object>("FateRunner.Stop");
        ipcStop.RegisterAction(() => Engine.Stop("Stopped by another plugin"));
        ipcLastStartError = PluginInterface.GetIpcProvider<string>("FateRunner.LastStartError");
        ipcLastStartError.RegisterFunc(() => Engine.LastStartError);
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "start":
                if (Engine.Start(Config.Goal, Config.Goal == RunGoal.Leveling ? Config.LevelTarget : Config.GemstoneTarget, null) is { } why) Chat.PrintError($"[FateRunner] {why}");
                break;
            case "stop":
                Engine.Stop("Stopped");
                break;
            default:
                mainWindow.Toggle();
                break;
        }
    }

    public void Dispose()
    {
        ipcIsRunning.UnregisterFunc();
        ipcStatus.UnregisterFunc();
        ipcGemstones.UnregisterFunc();
        ipcStartForGemstones.UnregisterFunc();
        ipcStart.UnregisterFunc();
        ipcStop.UnregisterAction();
        ipcLastStartError.UnregisterFunc();
        Engine.Dispose();
        Commands.RemoveHandler(Command);
        Commands.RemoveHandler(ShortCommand);
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= mainWindow.Toggle;
        windows.RemoveAllWindows();
    }
}
