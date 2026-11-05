using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FateRunner.Data;
using FateRunner.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using CsGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace FateRunner.Core;

public enum RunPhase { Idle, ToZone, Choosing, Travel, StartNpc, InFate, AfterFate, Dead, Done, Failed }

/// <summary>
/// The FATE loop: be in a gemstone zone, pick the best FATE, fly there, level sync, and finish it — killing its enemies
/// (Wrath Combo's Auto-Rotation does the fighting), handing in items, following an escort, or talking to the NPC that
/// starts it. When the zone runs dry it hops to the next one; it stops at the gemstone goal (or near the cap).
/// </summary>
public sealed unsafe class FateEngine : IDisposable
{
    private readonly Configuration cfg;
    private readonly VNavmeshIpc nav;
    private readonly WrathComboIpc wrath;
    private readonly LifestreamIpc lifestream;
    private readonly Mover mover;
    private readonly BossModAi bossMod = new();
    private readonly Chocobo chocobo;
    private DateTime syncStartedAt = DateTime.MinValue;
    private bool syncWarned;

    public RunPhase Phase { get; private set; } = RunPhase.Idle;
    public string Status { get; private set; } = "Idle";
    public bool Running => Phase is not (RunPhase.Idle or RunPhase.Done or RunPhase.Failed);
    public bool BossModLoaded => bossMod.IsAvailable;
    public string BossModName => bossMod.Name;
    public FateInfo? Current { get; private set; }
    public RunGoal Goal { get; private set; }
    public int Target { get; private set; }
    public string? Requester { get; private set; }
    public int FatesDone { get; private set; }
    public int GemsAtStart { get; private set; }
    public int LevelAtStart { get; private set; }
    public DateTime StartedAt { get; private set; }
    public uint TargetZone { get; private set; }
    /// <summary>Why the last Start was refused ("" when it started).</summary>
    public string LastStartError { get; private set; } = "";
    /// <summary>This run picks its own gemstone zones (always for other plugins; otherwise the "pick for me" setting).</summary>
    public bool AutoZones { get; private set; }

    private DateTime phaseSince, nextTick, emptySince = DateTime.MinValue, lastSync, lastInteract, diedAt;
    private readonly Dictionary<ushort, DateTime> blacklist = new();      // FATEs to leave alone for a while
    private readonly HashSet<uint> badZones = new();                      // zones we couldn't get to this run
    private int teleportTries, npcTries, handInStage;
    private ushort currentId;
    private uint escortNpc;
    private bool fighting;

    public FateEngine(Configuration cfg, VNavmeshIpc nav, WrathComboIpc wrath, LifestreamIpc lifestream)
    {
        this.cfg = cfg; this.nav = nav; this.wrath = wrath; this.lifestream = lifestream;
        mover = new Mover(cfg, nav);
        chocobo = new Chocobo(cfg);
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        if (Running) Stop("Unloaded");
    }

    // ---------------- Start / stop ----------------

    /// <summary>Starts a run. Returns why it can't, or null.</summary>
    public string? Start(RunGoal goal, int target, string? requester)
    {
        var why = TryStart(goal, target, requester);
        LastStartError = why ?? "";
        return why;
    }

    private string? TryStart(RunGoal goal, int target, string? requester)
    {
        if (!Running) AutoZones = goal != RunGoal.Leveling && (requester != null || cfg.AutoPickGemZones);
        if (Running) return "already running.";
        if (Plugin.ObjectTable.LocalPlayer == null) return "not logged in.";
        if (!nav.IsAvailable) return "vnavmesh isn't loaded — it's needed to move around.";
        if (cfg.UseWrath && !wrath.IsAvailable) return "Wrath Combo isn't loaded — it does the fighting (or untick \"Use Wrath Combo\").";
        if (goal == RunGoal.Leveling ? FateZones.ForLevel(Game.JobLevel).Count == 0 : false)
            return $"no zone with FATEs for Lv{Game.JobLevel} that you've attuned to an aetheryte in.";
        if (goal != RunGoal.Leveling && AutoZones && GemZones().Count == 0)
            return Game.JobLevel < 70
                ? $"your job is Lv{Game.JobLevel} — gemstone FATEs start at Lv70 (Shadowbringers). Switch to a Lv70+ combat job."
                : "you haven't attuned to an aetheryte in any Shadowbringers, Endwalker or Dawntrail zone your level allows.";
        if (goal != RunGoal.Leveling && GemZones().Count == 0)
        {
            var level = Game.JobLevel;
            var ticked = FateZones.All.Where(z => cfg.Zones.Contains(z)).ToList();
            if (ticked.Count == 0) return "no zones ticked — tick some under Zones.";
            var why = ticked.Select(z => !FateZones.Attuned(z) ? $"{FateZones.Name(z)}: no aetheryte attuned"
                                       : $"{FateZones.Name(z)}: needs Lv{FateZones.MinLevel(z)}").Take(4);
            return $"no zone you can farm (your job reads as Lv{level}) — {string.Join("; ", why)}.";
        }
        var gems = Game.Gemstones;
        if (goal == RunGoal.Gemstones && gems >= target) return $"you already have {gems:N0} Bicolor Gemstones (goal {target:N0}).";
        if (goal == RunGoal.Leveling && Game.JobLevel >= target) return $"your job is already Lv{Game.JobLevel} (target Lv{target}).";
        if (goal != RunGoal.Leveling && cfg.StopNearCap && gems >= Game.GemstoneCap - cfg.CapMargin) return $"you're at {gems:N0}/1,500 Bicolor Gemstones — spend some first.";

        Goal = goal;
        Target = goal == RunGoal.Leveling ? Math.Clamp(target, 1, 100) : Math.Clamp(target, 1, Game.GemstoneCap);
        LevelAtStart = Game.JobLevel;
        Requester = requester;
        FatesDone = 0;
        GemsAtStart = gems;
        StartedAt = DateTime.UtcNow;
        blacklist.Clear();
        badZones.Clear();
        chocobo.Reset();
        Current = null;
        currentId = 0;
        if (cfg.UseWrath)
        {
            if (!wrath.Acquire(true)) return "Wrath Combo refused control (check /xllog; reloading Wrath usually fixes it).";
            wrath.ReadyCurrentJob(Plugin.ObjectTable.LocalPlayer!.ClassJob.RowId, true);
        }
        if (cfg.UseBossModAi) bossMod.On();
        Plugin.Chat.Print($"[FateRunner] Started{(requester != null ? $" for {requester}" : "")}: " + goal switch
        {
            RunGoal.Gemstones => $"farming FATEs until {Target:N0} Bicolor Gemstones (have {gems:N0}).",
            RunGoal.Leveling => $"leveling with FATEs from Lv{Game.JobLevel} to Lv{Target}.",
            _ => "farming FATEs until stopped.",
        });
        Enter(RunPhase.ToZone, "Heading to a FATE zone");
        return null;
    }

    public void Stop(string reason) => Finish(RunPhase.Idle, reason);

    private void Finish(RunPhase end, string text)
    {
        mover.Stop();
        if (cfg.UseWrath) { wrath.SetAutoRotation(false); wrath.Release(); }
        bossMod.Off();
        fighting = false;
        Phase = end;
        Status = text;
        Current = null;
        if (end != RunPhase.Idle || text != "Unloaded")
            Plugin.Chat.Print($"[FateRunner] {text} — {FatesDone} FATE{(FatesDone == 1 ? "" : "s")}, +{Game.Gemstones - GemsAtStart:N0} gemstones.");
    }

    private void Enter(RunPhase p, string text)
    {
        Plugin.Log.Debug($"FateRunner: {Phase} -> {p} ({text})");
        Phase = p;
        Status = text;
        phaseSince = DateTime.UtcNow;
        mover.Reset();
    }

    private TimeSpan InPhase => DateTime.UtcNow - phaseSince;

    // ---------------- Tick ----------------

    private void OnUpdate(IFramework _)
    {
        if (!Running || DateTime.UtcNow < nextTick) return;
        nextTick = DateTime.UtcNow.AddMilliseconds(150);
        try { Tick(); }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "FateRunner tick");
            Finish(RunPhase.Failed, $"Error: {ex.Message}");
        }
    }

    private void Tick()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return;
        var c = Plugin.Condition;
        if (c[ConditionFlag.BetweenAreas] || c[ConditionFlag.BetweenAreas51]) return;

        // Dead: accept a raise, or return home after a while and come back.
        if (me.IsDead || c[ConditionFlag.Unconscious])
        {
            if (Phase != RunPhase.Dead) { diedAt = DateTime.UtcNow; StopFighting(); Enter(RunPhase.Dead, "Knocked out"); }
            TickDead();
            return;
        }
        if (Phase == RunPhase.Dead) { Enter(RunPhase.ToZone, "Back on your feet"); return; }

        // Goal / cap: checked between FATEs (rewards land when a FATE ends).
        if (Phase is RunPhase.Choosing or RunPhase.AfterFate or RunPhase.ToZone && GoalReached() is { } done)
        {
            Finish(RunPhase.Done, done);
            return;
        }

        chocobo.Tick();

        switch (Phase)
        {
            case RunPhase.ToZone: TickToZone(); break;
            case RunPhase.Choosing: TickChoosing(me); break;
            case RunPhase.Travel: TickTravel(me); break;
            case RunPhase.StartNpc: TickStartNpc(me); break;
            case RunPhase.InFate: TickInFate(me); break;
            case RunPhase.AfterFate:
                Status = "FATE done — collecting the reward";
                if (InPhase < TimeSpan.FromSeconds(4)) break;
                // Leveling: levelled into a better zone? Move up.
                if (Goal == RunGoal.Leveling && FateZones.ForLevel(Game.JobLevel) is { Count: > 0 } best &&
                    best[0] != TargetZone && FateZones.MinLevel(best[0]) > FateZones.MinLevel(TargetZone))
                {
                    TargetZone = best[0];
                    Plugin.Chat.Print($"[FateRunner] Lv{Game.JobLevel} — moving up to {FateZones.Name(TargetZone)} (Lv{FateZones.MinLevel(TargetZone)}+ FATEs).");
                    Enter(RunPhase.ToZone, $"Travelling to {FateZones.Name(TargetZone)}");
                    break;
                }
                Enter(RunPhase.Choosing, "Looking for the next FATE");
                break;
        }
    }

    private string? GoalReached()
    {
        if (Goal == RunGoal.Leveling) return Game.JobLevel >= Target ? $"Reached Lv{Game.JobLevel}" : null;
        var gems = Game.Gemstones;
        if (Goal == RunGoal.Gemstones && gems >= Target) return $"Reached {gems:N0} Bicolor Gemstones";
        if (cfg.StopNearCap && gems >= Game.GemstoneCap - cfg.CapMargin) return $"Bicolor Gemstones at {gems:N0}/1,500 — stopping before the cap";
        return null;
    }

    // ---------------- Zones ----------------

    /// <summary>Where to farm: leveling picks zones by your level; the gemstone goals use the zones you ticked.</summary>
    private List<uint> UsableZones()
        => (Goal == RunGoal.Leveling ? FateZones.ForLevel(Game.JobLevel) : GemZones()).Where(z => !badZones.Contains(z)).ToList();

    private List<uint> GemZones()
    {
        var level = Game.JobLevel;
        if (AutoZones) return FateZones.ForGemstones(level);
        return FateZones.All.Where(z => cfg.Zones.Contains(z) && FateZones.Attuned(z) && level >= FateZones.MinLevel(z)).ToList();
    }

    private void TickToZone()
    {
        var here = (uint)Plugin.ClientState.TerritoryType;
        var zones = UsableZones();
        if (zones.Count == 0) { Finish(RunPhase.Failed, "No zone left to farm"); return; }
        if (TargetZone == 0 || !zones.Contains(TargetZone)) TargetZone = zones.Contains(here) ? here : zones[0];
        if (here == TargetZone)
        {
            teleportTries = 0;
            emptySince = DateTime.MinValue;
            Enter(RunPhase.Choosing, $"In {FateZones.Name(here)} — looking for FATEs");
            return;
        }
        Status = $"Travelling to {FateZones.Name(TargetZone)}";
        if (Plugin.Condition[ConditionFlag.InCombat] && FightAttackers(Plugin.ObjectTable.LocalPlayer!)) return;
        if (Plugin.Condition[ConditionFlag.Casting] || (lifestream.IsAvailable && lifestream.IsBusy)) { phaseSince = DateTime.UtcNow; return; }
        if (InPhase < TimeSpan.FromSeconds(teleportTries == 0 ? 0 : 12)) return;
        if (teleportTries >= 3)
        {
            Plugin.Chat.PrintError($"[FateRunner] Couldn't get to {FateZones.Name(TargetZone)} — skipping it this run.");
            badZones.Add(TargetZone);
            TargetZone = 0;
            teleportTries = 0;
            return;
        }
        if (Mover.Mounted) { mover.GeneralAction(23, 1.5); return; }
        teleportTries++;
        phaseSince = DateTime.UtcNow;
        if (!Game.Teleport(FateZones.Aetheryte(TargetZone), lifestream, cfg.PreferLifestream))
            Plugin.Log.Debug($"Teleport to {FateZones.Name(TargetZone)} refused (try {teleportTries})");
    }

    private void HopZone(string why)
    {
        var zones = UsableZones();
        if (!cfg.HopZones || zones.Count < 2) { Status = why; return; }
        var i = zones.IndexOf((uint)Plugin.ClientState.TerritoryType);
        TargetZone = zones[(i + 1) % zones.Count];
        Plugin.Chat.Print($"[FateRunner] {why} — moving on to {FateZones.Name(TargetZone)}.");
        Enter(RunPhase.ToZone, $"Travelling to {FateZones.Name(TargetZone)}");
    }

    // ---------------- Choosing ----------------

    private bool KindAllowed(FateKind k) => k switch
    {
        FateKind.Kill => cfg.DoKill, FateKind.Boss => cfg.DoBoss, FateKind.HandIn => cfg.DoHandIn,
        FateKind.Escort => cfg.DoEscort, FateKind.Defend => cfg.DoDefend, _ => true,
    };

    /// <summary>FATEs worth going to, best first: the one you're standing in, then bonus, further along, nearer.</summary>
    public List<FateInfo> Candidates(IBattleChara me)
    {
        foreach (var k in blacklist.Where(b => b.Value < DateTime.UtcNow).Select(b => b.Key).ToList()) blacklist.Remove(k);
        var level = Game.JobLevel;
        var all = Fates.All().Where(f => (f.Running || (f.NeedsNpcStart && cfg.StartNpcFates)) && KindAllowed(f.Kind) &&
                                         !blacklist.ContainsKey(f.Id) && f.Level <= level + 3 &&
                                         (f.Preparing || f.SecondsLeft >= cfg.MinSecondsLeft)).ToList();
        var fresh = all.Where(f => f.Progress < cfg.SkipAboveProgress).ToList();
        if (fresh.Count > 0) all = fresh;
        var inRing = Fates.CurrentId;
        return all.OrderByDescending(f => f.Id == inRing)
                  .ThenByDescending(f => cfg.PreferBonus && f.Bonus)
                  .ThenByDescending(f => f.Running)
                  .ThenBy(f => Vector3.Distance(me.Position, f.Position) - f.Progress * 3f)
                  .ToList();
    }

    /// <summary>One FATE in the zone for the UI list: why it's next / skipped, and how far away.</summary>
    public sealed record FateRow(FateInfo Fate, string Why, bool Skipped, bool IsCurrent, bool IsNext, float Distance, bool Synced);

    /// <summary>
    /// Every FATE in the zone for the "FATEs here" list: the current one, then the order FateRunner would do them in,
    /// then the ones it skips with the reason. Read-only (doesn't touch the blacklist).
    /// </summary>
    public List<FateRow> Overview()
    {
        var rows = new List<FateRow>();
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return rows;
        var level = Game.JobLevel;
        var inRing = Fates.CurrentId;
        var all = Fates.All().Where(f => f.Running || f.Preparing).ToList();
        var anyFresh = all.Any(f => f.Progress < cfg.SkipAboveProgress && Eligible(f, level));
        var order = all.Where(f => Eligible(f, level) && (!anyFresh || f.Progress < cfg.SkipAboveProgress))
                       .OrderByDescending(f => f.Id == inRing)
                       .ThenByDescending(f => cfg.PreferBonus && f.Bonus)
                       .ThenByDescending(f => f.Running)
                       .ThenBy(f => Vector3.Distance(me.Position, f.Position) - f.Progress * 3f)
                       .ToList();
        var cur = Running ? currentId : (ushort)0;
        var nextId = order.Select(f => f.Id).FirstOrDefault(id => id != cur);

        foreach (var f in order.OrderByDescending(f => f.Id == cur))
        {
            var dist = Vector3.Distance(me.Position, f.Position);
            var isCur = f.Id == cur;
            var bits = new List<string>();
            if (isCur) bits.Add("current"); else if (f.Id == nextId) bits.Add("next");
            if (f.Bonus) bits.Add("bonus");
            bits.Add(KindName(f.Kind));
            if (f.NeedsNpcStart) bits.Add("waiting for NPC");
            else if (!isCur) bits.Add($"{dist:0}y");
            rows.Add(new FateRow(f, string.Join(" · ", bits), false, isCur, f.Id == nextId && !isCur, dist, Fates.SyncedTo(f.Id)));
        }
        foreach (var f in all.Where(f => order.All(o => o.Id != f.Id)))
            rows.Add(new FateRow(f, "skipped · " + SkipReason(f, level), true, false, false, Vector3.Distance(me.Position, f.Position), false));
        return rows;
    }

    private bool Eligible(FateInfo f, int level) =>
        (f.Running || (f.NeedsNpcStart && cfg.StartNpcFates)) && KindAllowed(f.Kind) && !blacklist.ContainsKey(f.Id) &&
        f.Level <= level + 3 && (f.Preparing || f.SecondsLeft >= cfg.MinSecondsLeft);

    private string SkipReason(FateInfo f, int level)
    {
        if (!KindAllowed(f.Kind)) return $"{KindName(f.Kind)} turned off";
        if (blacklist.ContainsKey(f.Id)) return "couldn't reach it";
        if (f.Level > level + 3) return $"Lv{f.Level}, too high";
        if (f.Preparing && !f.NeedsNpcStart) return "not started yet";
        if (f.Preparing && !cfg.StartNpcFates) return "needs an NPC to start";
        if (f.SecondsLeft < cfg.MinSecondsLeft) return "almost over";
        if (f.Progress >= cfg.SkipAboveProgress) return $"{f.Progress}% done";
        return "not worth it";
    }

    public static string KindName(FateKind k) => k switch
    {
        FateKind.Kill => "kill", FateKind.Boss => "boss", FateKind.HandIn => "hand-in",
        FateKind.Escort => "escort", FateKind.Defend => "defend", _ => "FATE",
    };

    private void TickChoosing(IBattleChara me)
    {
        if ((uint)Plugin.ClientState.TerritoryType != TargetZone || !UsableZones().Contains(TargetZone)) { Enter(RunPhase.ToZone, "Back to a FATE zone"); return; }
        if (Plugin.Condition[ConditionFlag.InCombat] && FightAttackers(me)) return;
        StopFighting();
        var pick = Candidates(me).FirstOrDefault();
        if (pick == null)
        {
            if (emptySince == DateTime.MinValue) emptySince = DateTime.UtcNow;
            var waited = DateTime.UtcNow - emptySince;
            Status = $"No FATEs to do in {FateZones.Name(TargetZone)} — waiting ({(int)waited.TotalSeconds}s)";
            if (waited > TimeSpan.FromSeconds(cfg.EmptyZoneWaitSeconds)) { emptySince = DateTime.MinValue; HopZone($"No FATEs in {FateZones.Name(TargetZone)}"); }
            return;
        }
        emptySince = DateTime.MinValue;
        currentId = pick.Id;
        Current = pick;
        npcTries = 0;
        escortNpc = 0;
        handInStage = 0;
        handInMob = handInItem = 0;
        syncStartedAt = DateTime.MinValue;
        syncWarned = false;
        Enter(RunPhase.Travel, $"Going to {pick.Name}");
    }

    // ---------------- Travel ----------------

    private void TickTravel(IBattleChara me)
    {
        if (Fates.Get(currentId) is not { } f || f.State is not (FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Running or FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Preparing))
        {
            Enter(RunPhase.Choosing, "That FATE ended — picking another");
            return;
        }
        Current = f;
        if (Plugin.Condition[ConditionFlag.InCombat] && !Mover.Airborne && FightAttackers(me)) return;
        StopFighting();

        if (f.NeedsNpcStart && cfg.StartNpcFates && Find(f.MotivationNpc) is { } npc)
        {
            if (mover.Step(npc.Position, 4f)) Enter(RunPhase.StartNpc, $"Starting {f.Name}");
            else Status = $"To {f.Name} (start NPC): {mover.Status}";
        }
        else
        {
            // Inside the ring, a little in from the edge.
            var reach = Math.Clamp(f.Radius * 0.5f, 4f, 20f);
            var inRing = Fates.CurrentId == f.Id && Vector3.Distance(me.Position, f.Position) < f.Radius * 0.85f;
            if ((inRing && !Mover.Mounted) || mover.Step(f.Position, reach))
            {
                Enter(RunPhase.InFate, f.Name);
                return;
            }
            Status = $"To {f.Name}: {mover.Status}";
        }
        // Stuck getting there: leave it alone for a while.
        if (InPhase > TimeSpan.FromMinutes(4)) { Blacklist(f.Id, "took too long to reach"); Enter(RunPhase.Choosing, "Picking another FATE"); }
    }

    // ---------------- Starting an NPC FATE ----------------

    private void TickStartNpc(IBattleChara me)
    {
        if (Fates.Get(currentId) is not { } f) { Enter(RunPhase.Choosing, "That FATE is gone"); return; }
        if (f.Running) { Enter(RunPhase.InFate, f.Name); return; }
        if (HandleDialogs()) return;
        if (Find(f.MotivationNpc) is not { } npc) { Enter(RunPhase.Travel, $"Going to {f.Name}"); return; }
        if (!mover.Step(npc.Position, 4f)) { Status = $"To {f.Name} (start NPC): {mover.Status}"; return; }
        if (DateTime.UtcNow - lastInteract < TimeSpan.FromSeconds(2.5)) return;
        if (npcTries >= 4) { Blacklist(f.Id, "the NPC wouldn't start it"); Enter(RunPhase.Choosing, "Picking another FATE"); return; }
        npcTries++;
        Interact(npc);
        Status = $"Talking to start {f.Name}";
    }

    /// <summary>Clicks through the NPC dialog: speech bubbles, the first menu line, and "Yes".</summary>
    private bool HandleDialogs()
    {
        if (Game.Visible("SelectYesno")) { Game.Callback("SelectYesno", 0); return true; }
        if (Game.Visible("SelectString")) { Game.Callback("SelectString", 0); return true; }
        if (Game.ClickTalk()) return true;
        return false;
    }

    // ---------------- In the FATE ----------------

    private void TickInFate(IBattleChara me)
    {
        var f = Fates.Get(currentId);
        if (f == null || f.State is not FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Running)
        {
            // Hand-in FATE ended: hand in anything left first? No — the game takes nothing after it ends.
            StopFighting();
            FatesDone++;
            Enter(RunPhase.AfterFate, "FATE done");
            return;
        }
        Current = f;

        // Off the mount, and level-synced, once inside.
        if (Mover.Mounted || Mover.Airborne)
        {
            if (!mover.Step(f.Position, Math.Clamp(f.Radius * 0.5f, 4f, 20f))) { Status = $"{f.Name}: {mover.Status}"; return; }
        }
        // Level sync before fighting: over-level and un-synced, a FATE gives no reward. Step into the ring (the game
        // only lets you sync from inside it) and sync, retrying until the game confirms.
        // Synced = the level the game shows you is the FATE's level (more reliable than the FATE manager's id).
        if (Game.JobLevel > f.MaxLevel && me.Level > f.MaxLevel)
        {
            if (syncStartedAt == DateTime.MinValue) syncStartedAt = DateTime.UtcNow;
            if (Fates.CurrentId != f.Id)
            {
                mover.Step(f.Position, Math.Max(3f, f.Radius * 0.4f), true, 999f);
                Status = $"{f.Name} — stepping into the ring to level sync";
                return;
            }
            // Level Sync is a toggle: press it, then give the game a few seconds to apply it before pressing again.
            if (DateTime.UtcNow - lastSync > TimeSpan.FromSeconds(3))
            {
                lastSync = DateTime.UtcNow;
                Fates.SyncTo(f.Id);
            }
            if (DateTime.UtcNow - syncStartedAt < TimeSpan.FromSeconds(10))
            {
                Status = $"{f.Name} — level syncing to Lv{f.MaxLevel}";
                return;
            }
            if (!syncWarned)
            {
                syncWarned = true;
                Plugin.Chat.PrintError($"[FateRunner] Couldn't level sync to {f.Name} — fighting anyway (no reward un-synced).");
            }
        }
        else { syncStartedAt = DateTime.MinValue; syncWarned = false; }

        switch (f.Kind)
        {
            case FateKind.HandIn: TickHandIn(me, f); return;
            case FateKind.Escort: TickEscort(me, f); return;
            default: TickFight(me, f, f.Position, f.Radius + 8f); return;
        }
    }

    /// <summary>Fight this FATE's enemies (attackers first, then nearest to <paramref name="around"/>). False if none.</summary>
    private bool TickFight(IBattleChara me, FateInfo f, Vector3 around, float within)
    {
        var mob = PickEnemy(me, f.Id, around, within);
        if (mob == null)
        {
            StopFighting();
            // Nothing to hit right now: wait near the middle for the next wave.
            if (Vector3.Distance(me.Position, f.Position) > Math.Max(6f, f.Radius * 0.5f)) mover.Step(f.Position, Math.Max(5f, f.Radius * 0.4f), true, 999f);
            Status = $"{f.Name} {f.Progress}% — waiting for enemies";
            return false;
        }
        Engage(me, mob);
        Status = $"{f.Name} {f.Progress}% — fighting {mob.Name.TextValue}";
        return true;
    }

    private void TickEscort(IBattleChara me, FateInfo f)
    {
        // The escorted NPC: the one who started it, else a friendly that belongs to this FATE.
        var npc = escortNpc != 0 ? FindById(escortNpc) : null;
        if (npc == null)
        {
            npc = Find(f.MotivationNpc) ?? FateFriendly(f.Id);
            escortNpc = npc?.EntityId ?? 0;
        }
        var anchor = npc?.Position ?? f.Position;
        if (PickEnemy(me, f.Id, anchor, 25f) is { } mob)
        {
            Engage(me, mob);
            Status = $"{f.Name} {f.Progress}% — protecting the escort from {mob.Name.TextValue}";
            return;
        }
        StopFighting();
        if (Vector3.Distance(me.Position, anchor) > 5f) mover.Step(anchor, 3f, true, 999f);
        Status = $"{f.Name} {f.Progress}% — following {npc?.Name.TextValue ?? "the escort"}";
    }

    private void TickHandIn(IBattleChara me, FateInfo f)
    {
        var have = Game.ItemCount(f.EventItem);
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var enemies = PickEnemy(me, f.Id, f.Position, f.Radius + 8f) != null;
        // Hand in a batch, or whatever you hold when it's nearly over / there's nothing left to kill.
        var turnIn = have > 0 && (have >= 10 || f.SecondsLeft < 60 || (!enemies && FatePickup(me, f.Id) == null)) && !inCombat;
        if (turnIn || handInStage > 0)
        {
            TickTurnIn(me, f, have);
            return;
        }
        // Stick with what was started — finish the mob being fought, or the item being fetched — instead of choosing
        // again every tick (which flipped between pulling a mob and walking to an item, finishing neither).
        var now = DateTime.UtcNow;

        // 1) Fighting a mob: until it's dead or gone (a minute at most).
        if (handInMob != 0)
        {
            var mob = ByObjectId(handInMob) as IBattleChara;
            if (mob != null && !mob.IsDead && mob.CurrentHp > 0 && mob.IsTargetable && now - handInSince < TimeSpan.FromSeconds(60))
            {
                Engage(me, mob);
                Status = $"{f.Name} {f.Progress}% — fighting {mob.Name.TextValue} ({have} held)";
                return;
            }
            handInMob = 0;
        }

        // 2) Fetching an item: until it's picked up (it disappears) or it's clearly not working.
        if (handInItem != 0)
        {
            var item = ByObjectId(handInItem);
            var attacked = AttackerOf(me, f.Id);
            if (item == null || !item.IsTargetable) handInItem = 0; // picked up (or someone else got it)
            else if (attacked != null) { handInItem = 0; StartHandInFight(attacked); return; } // deal with it first
            else if (now - handInSince > TimeSpan.FromSeconds(15) || handInTries >= 5)
            {
                skippedItems[handInItem] = now.AddMinutes(1); // can't reach / pick it up — leave it for a minute
                handInItem = 0;
            }
            else
            {
                StopFighting();
                if (mover.Step(item.Position, 2.5f, true, 999f) && !nav.IsBusy && now - lastInteract > TimeSpan.FromSeconds(1.5))
                {
                    Interact(item);
                    handInTries++;
                }
                Status = $"{f.Name} {f.Progress}% — picking up ({have} held)";
                return;
            }
        }

        // 3) Choose the next thing: whatever is attacking you, else an item on the ground, else a FATE mob.
        if (AttackerOf(me, f.Id) is { } attacker) { StartHandInFight(attacker); return; }
        foreach (var k in skippedItems.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList()) skippedItems.Remove(k);
        if (FatePickup(me, f.Id) is { } next)
        {
            handInItem = next.GameObjectId;
            handInSince = now;
            handInTries = 0;
            Status = $"{f.Name} {f.Progress}% — going for an item ({have} held)";
            return;
        }
        if (PickEnemy(me, f.Id, f.Position, f.Radius + 8f) is { } target) { StartHandInFight(target); return; }
        TickFight(me, f, f.Position, f.Radius + 8f); // nothing to do: wait near the middle
        Status = $"{f.Name} {f.Progress}% — {have} held, waiting for enemies";
    }

    private ulong handInMob, handInItem;
    private DateTime handInSince;
    private int handInTries;
    private readonly Dictionary<ulong, DateTime> skippedItems = new();

    private void StartHandInFight(IBattleChara mob)
    {
        handInMob = mob.GameObjectId;
        handInSince = DateTime.UtcNow;
    }

    /// <summary>A FATE enemy (or anything hostile) that's attacking you right now.</summary>
    private static IBattleChara? AttackerOf(IBattleChara me, ushort fateId)
        => Plugin.ObjectTable.OfType<IBattleChara>()
                 .Where(o => o.ObjectKind == ObjectKind.BattleNpc && o.IsTargetable && !o.IsDead && o.CurrentHp > 0 &&
                             o.TargetObjectId == me.GameObjectId && IsEnemy(o))
                 .OrderByDescending(o => FateIdOf(o) == fateId)
                 .ThenBy(o => Vector3.Distance(o.Position, me.Position)).FirstOrDefault();

    private void TickTurnIn(IBattleChara me, FateInfo f, int have)
    {
        StopFighting();
        if (Game.Visible("Request"))
        {
            // Put the item in the hand-in slot, then "Hand Over".
            if (handInStage < 2)
            {
                var slot = KeyItemSlot(f.EventItem);
                var request = Game.Addon("Request");
                if (slot >= 0 && request != null)
                    AgentInventoryContext.Instance()->OpenForItemSlot(InventoryType.KeyItems, slot, 0, request->Id);
                handInStage = 2;
                lastInteract = DateTime.UtcNow;
                return;
            }
            if (Game.Visible("ContextIconMenu")) { Game.Callback("ContextIconMenu", 0, 0, 1021003, 0, 0); return; }
            if (DateTime.UtcNow - lastInteract > TimeSpan.FromSeconds(0.8))
            {
                Game.Callback("Request", 0);
                lastInteract = DateTime.UtcNow;
                handInStage = 3;
            }
            return;
        }
        if (HandleDialogs()) return;
        if (handInStage == 3 && have == 0) { handInStage = 0; return; } // handed over
        if (handInStage >= 2 && DateTime.UtcNow - lastInteract > TimeSpan.FromSeconds(6)) handInStage = 0; // try again

        if (Find(f.ObjectiveNpc) is not { } npc) { handInStage = 0; Status = $"{f.Name} — can't find who takes the items"; return; }
        if (!mover.Step(npc.Position, 4f)) { Status = $"{f.Name} — taking {have} to {npc.Name.TextValue}: {mover.Status}"; return; }
        if (DateTime.UtcNow - lastInteract < TimeSpan.FromSeconds(2.5)) return;
        handInStage = 1;
        Interact(npc);
        Status = $"{f.Name} — handing in {have}";
    }

    private static int KeyItemSlot(uint itemId)
    {
        var im = InventoryManager.Instance();
        var keys = im == null ? null : im->GetInventoryContainer(InventoryType.KeyItems);
        if (keys == null) return -1;
        for (var i = 0; i < keys->Size; i++)
        {
            var s = keys->GetInventorySlot(i);
            if (s != null && s->ItemId == itemId && s->Quantity > 0) return i;
        }
        return -1;
    }

    // ---------------- Combat ----------------

    private void Engage(IBattleChara me, IBattleChara mob)
    {
        var range = (IsMelee(me) ? cfg.MeleeRange : cfg.RangedRange) + mob.HitboxRadius;
        var dist = Vector3.Distance(me.Position, mob.Position);
        if (Plugin.Targets.Target?.GameObjectId != mob.GameObjectId) Plugin.Targets.Target = mob;
        if (dist > range)
        {
            if (cfg.UseWrath && Plugin.Condition[ConditionFlag.InCombat]) wrath.SetAutoRotation(true);
            mover.Step(mob.Position, Math.Max(1f, range - 1f), true, 999f);
            return;
        }
        if (nav.IsBusy) nav.Stop();
        if (cfg.UseWrath) wrath.SetAutoRotation(true);
        fighting = true;
        // The rotation won't open a fight on its own: open it with the job's basic attack (or auto-attack).
        if (!Plugin.Condition[ConditionFlag.InCombat]) Pull.Open(me, mob, () => { if (nav.IsBusy) nav.Stop(); });
    }

    private void StopFighting()
    {
        if (!fighting) return;
        fighting = false;
        if (cfg.UseWrath) wrath.SetAutoRotation(false);
    }

    /// <summary>Something's hitting you outside a FATE (on the way): deal with it. True while fighting.</summary>
    private bool FightAttackers(IBattleChara me)
    {
        var attacker = Plugin.ObjectTable.OfType<IBattleChara>()
            .Where(o => o.IsTargetable && !o.IsDead && o.CurrentHp > 0 && o.TargetObjectId == me.GameObjectId && IsEnemy(o))
            .OrderBy(o => Vector3.Distance(o.Position, me.Position)).FirstOrDefault();
        if (attacker == null) return false;
        if (Mover.Mounted) { mover.Stop(); mover.GeneralAction(23, 1.5); }
        Engage(me, attacker);
        Status = $"Fighting off {attacker.Name.TextValue}";
        return true;
    }

    /// <summary>This FATE's enemies: ones attacking you first, then the nearest to <paramref name="around"/>.</summary>
    private IBattleChara? PickEnemy(IBattleChara me, ushort fateId, Vector3 around, float within)
    {
        var mobs = Plugin.ObjectTable.OfType<IBattleChara>()
            .Where(o => o.ObjectKind == ObjectKind.BattleNpc && o.IsTargetable && !o.IsDead && o.CurrentHp > 0 &&
                        FateIdOf(o) == fateId && IsEnemy(o) &&
                        Vector3.Distance(o.Position, around) <= within && Math.Abs(o.Position.Y - me.Position.Y) < 15f)
            .ToList();
        if (mobs.Count == 0) return null;
        var current = Plugin.Targets.Target is IBattleChara t ? mobs.FirstOrDefault(m => m.GameObjectId == t.GameObjectId) : null;
        if (current != null) return current; // finish what you started
        return mobs.OrderByDescending(m => m.TargetObjectId == me.GameObjectId)
                   .ThenBy(m => Vector3.Distance(m.Position, me.Position)).First();
    }

    private IGameObject? FatePickup(IBattleChara me, ushort fateId)
        => Plugin.ObjectTable.Where(o => o.ObjectKind == ObjectKind.EventObj && o.IsTargetable && FateIdOf(o) == fateId &&
                                         !skippedItems.ContainsKey(o.GameObjectId) &&
                                         Vector3.Distance(o.Position, me.Position) < 40f)
                             .OrderBy(o => Vector3.Distance(o.Position, me.Position)).FirstOrDefault();

    private static IBattleChara? FateFriendly(ushort fateId)
        => Plugin.ObjectTable.OfType<IBattleChara>()
                 .FirstOrDefault(o => o.ObjectKind == ObjectKind.BattleNpc && FateIdOf(o) == fateId && o.MaxHp > 0 && !IsEnemy(o));

    private static ushort FateIdOf(IGameObject o) => ((CsGameObject*)o.Address)->FateId;

    private static bool IsEnemy(IGameObject o)
        => o is IBattleChara b && ActionManager.ClassifyTarget((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)b.Address) == ActionManager.TargetCategory.Enemy;

    private static bool IsMelee(IBattleChara me)
        => Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().TryGetRow(me.ClassJob.RowId, out var j) && j.Role is 1 or 2;

    // ---------------- Death ----------------

    private void TickDead()
    {
        var since = DateTime.UtcNow - diedAt;
        // A raise was offered: take it.
        if (Game.Visible("SelectYesno") && since > TimeSpan.FromSeconds(1)) { Game.Callback("SelectYesno", 0); return; }
        Status = $"Knocked out — {(since.TotalSeconds < cfg.ReturnAfterDeathSeconds ? $"waiting for a raise ({cfg.ReturnAfterDeathSeconds - (int)since.TotalSeconds}s)" : "returning to the home point")}";
        if (since.TotalSeconds < cfg.ReturnAfterDeathSeconds) return;
        if (DateTime.UtcNow - lastInteract < TimeSpan.FromSeconds(8)) return;
        lastInteract = DateTime.UtcNow;
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 8); // Return
    }

    // ---------------- Helpers ----------------

    private void Blacklist(ushort id, string why)
    {
        blacklist[id] = DateTime.UtcNow.AddMinutes(10);
        Plugin.Log.Debug($"Skipping FATE {id} for 10 min: {why}");
    }

    private static IGameObject? Find(uint entityId)
        => entityId == 0 || entityId == 0xE0000000 ? null : FindById(entityId);

    private static IGameObject? ByObjectId(ulong id)
        => id == 0 ? null : Plugin.ObjectTable.FirstOrDefault(o => o.GameObjectId == id);

    private static IGameObject? FindById(uint entityId)
        => Plugin.ObjectTable.FirstOrDefault(o => o.EntityId == entityId && o.IsTargetable);

    private void Interact(IGameObject o)
    {
        lastInteract = DateTime.UtcNow;
        Plugin.Targets.Target = o;
        TargetSystem.Instance()->InteractWithObject((CsGameObject*)o.Address, false);
    }

}
