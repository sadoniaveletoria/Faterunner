using System.Linq;

namespace FateRunner.Ipc;

/// <summary>
/// BossMod's AI (dodges AoEs and mechanics while Wrath does the rotation). Two plugins exist:
///   BossMod Reborn (InternalName "BossModReborn"): /bmrai on|off
///   BossMod (InternalName "BossMod"):               /vbm ai on|off
/// Turned on for a run and off again afterwards (only if FateRunner turned it on).
/// </summary>
public sealed class BossModAi
{
    private bool turnedOn;

    private static string? Loaded =>
        new[] { "BossModReborn", "BossMod" }.FirstOrDefault(n => Plugin.PluginInterface.InstalledPlugins.Any(p => p.InternalName == n && p.IsLoaded));

    public bool IsAvailable => Loaded != null;

    public string Name => Loaded == "BossModReborn" ? "BossMod Reborn" : Loaded == "BossMod" ? "BossMod" : "BossMod";

    public void On()
    {
        var cmd = Loaded switch { "BossModReborn" => "/bmrai on", "BossMod" => "/vbm ai on", _ => null };
        if (cmd == null) return;
        Plugin.Commands.ProcessCommand(cmd);
        turnedOn = true;
        Plugin.Log.Debug($"BossMod AI on ({cmd})");
    }

    public void Off()
    {
        if (!turnedOn) return;
        turnedOn = false;
        var cmd = Loaded switch { "BossModReborn" => "/bmrai off", "BossMod" => "/vbm ai off", _ => null };
        if (cmd == null) return;
        Plugin.Commands.ProcessCommand(cmd);
        Plugin.Log.Debug($"BossMod AI off ({cmd})");
    }
}
