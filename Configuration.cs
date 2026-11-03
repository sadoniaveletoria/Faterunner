using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace FateRunner;

public enum RunGoal
{
    /// <summary>Keep going until stopped (or the gemstone cap is near).</summary>
    Endless = 0,
    /// <summary>Stop once you hold this many Bicolor Gemstones.</summary>
    Gemstones = 1,
    /// <summary>Level your job: zones that fit your level, moving up as you level, until the target level.</summary>
    Leveling = 2,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ---- Goal ----
    public RunGoal Goal { get; set; } = RunGoal.Gemstones;
    public int GemstoneTarget { get; set; } = 1000;
    /// <summary>Leveling: stop when your job reaches this level.</summary>
    public int LevelTarget { get; set; } = 100;
    /// <summary>Stop before rewards would be wasted at the 1,500 cap (at cap minus this margin).</summary>
    public bool StopNearCap { get; set; } = true;
    public int CapMargin { get; set; } = 60;

    // ---- Which FATEs ----
    public bool DoKill { get; set; } = true;
    public bool DoBoss { get; set; } = true;
    public bool DoHandIn { get; set; } = true;
    public bool DoEscort { get; set; } = true;
    public bool DoDefend { get; set; } = true;
    /// <summary>Talk to an NPC to start FATEs that wait for someone to begin them.</summary>
    public bool StartNpcFates { get; set; } = true;
    /// <summary>Skip FATEs with less than this many seconds left (you'd arrive as it ends).</summary>
    public int MinSecondsLeft { get; set; } = 90;
    /// <summary>Skip FATEs already this far along (%), unless it's the only one.</summary>
    public int SkipAboveProgress { get; set; } = 90;
    public bool PreferBonus { get; set; } = true;

    // ---- Zones ----
    /// <summary>Territory ids FateRunner may farm (and hop between).</summary>
    public HashSet<uint> Zones { get; set; } = new(Data.FateZones.All);
    /// <summary>Gemstone runs: pick the zones automatically (every gemstone zone your level and aetherytes allow).</summary>
    public bool AutoPickGemZones { get; set; } = true;
    public bool HopZones { get; set; } = true;
    /// <summary>Seconds to wait in a zone with no FATEs before hopping to the next one.</summary>
    public int EmptyZoneWaitSeconds { get; set; } = 45;
    public bool PreferLifestream { get; set; } = true;

    // ---- Travel / combat ----
    public bool UseMount { get; set; } = true;
    public bool Fly { get; set; } = true;
    public float MeleeRange { get; set; } = 2.5f;
    public float RangedRange { get; set; } = 20f;
    /// <summary>Take Wrath Combo's Auto-Rotation over while running (put back afterwards).</summary>
    public bool UseWrath { get; set; } = true;
    /// <summary>Turn BossMod's AI on while running (dodges mechanics), off afterwards.</summary>
    public bool UseBossModAi { get; set; } = true;
    /// <summary>When you die and nobody raises you within this time, return to the home point and carry on.</summary>
    public int ReturnAfterDeathSeconds { get; set; } = 20;

    // ---- Chocobo companion ----
    /// <summary>Keep your chocobo out with Gysahl Greens while running.</summary>
    public bool AutoChocobo { get; set; } = true;
    public Core.ChocoboStance ChocoboStance { get; set; } = Core.ChocoboStance.Healer;
    /// <summary>Feed greens when it has less than this many seconds left (or isn't out).</summary>
    public int ChocoboRefreshSeconds { get; set; } = 300;

    // ---- UI ----
    public bool ShowOverlay { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
