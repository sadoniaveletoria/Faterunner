using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FateRunner.Core;
using FateRunner.Data;

namespace FateRunner.Windows;

public sealed class MainWindow : Window
{
    private readonly Plugin p;
    private readonly string version;

    public MainWindow(Plugin plugin) : base("FateRunner###FateRunnerMain")
    {
        p = plugin;
        // Four parts when the last one is used (0.0.0.1), otherwise three (1.2.0).
        var v = typeof(Plugin).Assembly.GetName().Version;
        version = v == null ? "v?" : "v" + v.ToString(v.Revision > 0 ? 4 : 3);
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(480, 520), MaximumSize = new Vector2(900, 1400) };
        Size = new Vector2(540, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Ui.PushWindowStyle();
    public override void PostDraw() => Ui.PopWindowStyle();

    public override void Draw()
    {
        var c = p.Config;
        var e = p.Engine;
        var changed = false;

        // ---- Header ----
        var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        Ui.Logo(ImGui.GetTextLineHeight() + 6);
        ImGui.SameLine(0, 8);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("FateRunner");
        ImGui.SameLine(0, 6);
        ImGui.TextColored(Ui.Muted, version);
        var chips = new[]
        {
            ("vnavmesh", p.Nav.IsAvailable, "Required — moving and flying."),
            ("Wrath", p.Wrath.IsAvailable, "Wrath Combo — does the fighting (Auto-Rotation)."),
            ("Lifestream", p.Lifestream.IsAvailable, "Optional — teleports."),
            ("BossMod", e.BossModLoaded, "Optional — its AI dodges mechanics while running (BossMod or BossMod Reborn)."),
        };
        var width = chips.Sum(ch => Ui.ChipWidth(ch.Item1)) + 6 * (chips.Length - 1);
        ImGui.SameLine();
        if (ImGui.GetCursorPosX() + width > right) ImGui.NewLine(); else ImGui.SetCursorPosX(right - width);
        for (var i = 0; i < chips.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0, 6);
            Ui.Chip(chips[i].Item1, chips[i].Item2, $"{chips[i].Item1}: {(chips[i].Item2 ? "detected" : "not detected")}\n{chips[i].Item3}");
        }
        ImGui.Dummy(new Vector2(0, 2));

        DrawHero(c, e);
        DrawStats(c, e);
        changed |= DrawControls(c, e);

        // ---- Tabs ----
        var overview = e.Running || Plugin.ObjectTable.LocalPlayer != null ? e.Overview() : new List<FateEngine.FateRow>();
        var leveling = c.Goal == RunGoal.Leveling;
        var zoneCount = leveling ? FateZones.ForLevel(Game.JobLevel).Count : c.Zones.Count;
        Ui.TabStrip("##tabs", new[]
        {
            ("FATEs here", overview.Count(r => !r.Skipped).ToString()),
            ("Zones", zoneCount.ToString()),
            ("Which FATEs", ""),
            ("Travel & combat", ""),
        }, ref tab);

        ImGui.BeginChild("tabbody", new Vector2(-1, -1), false);
        switch (tab)
        {
            case 0: DrawFatesHere(overview); break;
            case 1: changed |= DrawZones(c, e); break;
            case 2: changed |= DrawWhichFates(c); break;
            default: changed |= DrawTravel(c, e); break;
        }
        ImGui.EndChild();

        if (changed) c.Save();
    }

    private int tab;

    // ---------------- Hero: ring + phase + current FATE ----------------

    private void DrawHero(Configuration c, FateEngine e)
    {
        Ui.BeginPanel(0, Ui.HeroBg, Ui.HeroEdge);
        var inner = Ui.AvailX();
        const float radius = 56f;

        var goal = e.Running ? e.Goal : c.Goal;
        if (goal == RunGoal.Leveling)
        {
            var lv = Game.JobLevel;
            var target = e.Running ? e.Target : c.LevelTarget;
            var from = e.Running ? e.LevelAtStart : lv;
            var frac = target <= from ? (lv >= target ? 1f : 0f) : Ui.Clamp01((lv - from) / (float)(target - from));
            Ui.ProgressRing(frac, radius, $"Lv{lv}", $"to Lv{target}", 0, new Vector4(0.26f, 0.76f, 0.35f, 1f), Ui.Good);
        }
        else
        {
            var gems = Game.Gemstones;
            var target = goal == RunGoal.Gemstones ? (e.Running ? e.Target : c.GemstoneTarget) : Game.GemstoneCap;
            Ui.ProgressRing(gems / (float)Math.Max(1, target), radius, $"{gems:N0}", $"of {target:N0}", Ui.IconOfItem(Game.BicolorGemstone));
        }

        ImGui.SameLine(0, 14);
        ImGui.BeginGroup();
        var right = inner - radius * 2 - 14;
        var (phase, color) = Ui.PhaseLabel(e.Phase, e.Running);
        Ui.PhasePill(phase, color);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + right);
        ImGui.TextColored(Ui.Muted, StatusLine(c, e));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0, 1));
        if (e.Running && e.Current is { } f)
            Ui.FateCard(f, Fates.SyncedTo(f.Id), right, false);
        else
        {
            var zone = (uint)Plugin.ClientState.TerritoryType;
            var hint = e.Running ? "Looking for a FATE…"
                     : FateZones.Get(zone) != null ? $"In {FateZones.Name(zone)} — press Start to run FATEs."
                     : "Press Start: it travels to a FATE zone for you.";
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + right);
            ImGui.TextColored(Ui.Dim, hint);
            ImGui.PopTextWrapPos();
        }
        ImGui.EndGroup();
        Ui.EndPanel();
    }

    private string StatusLine(Configuration c, FateEngine e)
    {
        if (!e.Running) return e.Phase is RunPhase.Done or RunPhase.Failed ? e.Status : "Not running.";
        var extras = new List<string>();
        if (c.UseWrath && p.Wrath.IsAvailable) extras.Add("Wrath on");
        if (c.UseBossModAi && e.BossModLoaded) extras.Add($"{e.BossModName} AI on");
        if (e.Requester != null) extras.Add($"for {e.Requester}");
        return extras.Count == 0 ? e.Status : $"{e.Status} · {string.Join(" · ", extras)}";
    }

    // ---------------- Stat strip ----------------

    private void DrawStats(Configuration c, FateEngine e)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var w = (avail - 8 * 3) / 4f;
        var active = e.Running || e.FatesDone > 0;
        var mins = active ? Math.Max(1, (DateTime.UtcNow - e.StartedAt).TotalMinutes) : 0;
        var leveling = (e.Running ? e.Goal : c.Goal) == RunGoal.Leveling;
        var gained = Game.Gemstones - e.GemsAtStart;

        Ui.StatBox(active ? e.FatesDone.ToString() : "—", "FATEs done", w);
        ImGui.SameLine(0, 8);
        if (leveling)
            Ui.StatBox(active ? $"+{Math.Max(0, Game.JobLevel - e.LevelAtStart)}" : "—", "levels", w, active ? Ui.Good : null);
        else
            Ui.StatBox(active ? $"{(gained >= 0 ? "+" : "")}{gained:N0}" : "—", "gemstones", w, active && gained > 0 ? Ui.Good : null);
        ImGui.SameLine(0, 8);
        if (leveling)
            Ui.StatBox(active ? $"{e.FatesDone / mins * 60:0.#}/h" : "—", "FATE rate", w);
        else
            Ui.StatBox(active ? $"{gained / mins * 60:N0}/h" : "—", "gem rate", w);
        ImGui.SameLine(0, 8);
        Ui.StatBox(active ? RunTime(DateTime.UtcNow - e.StartedAt) : "—", "run time", w);
    }

    private static string RunTime(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:D2}m" : $"{(int)t.TotalMinutes}m";

    // ---------------- Start / mode / target ----------------

    private static readonly RunGoal[] Modes = { RunGoal.Gemstones, RunGoal.Endless, RunGoal.Leveling };
    private static readonly string[] ModeNames = { "Gem goal", "Endless", "Leveling" };

    private bool DrawControls(Configuration c, FateEngine e)
    {
        var changed = false;
        var h = ImGui.GetFrameHeight() + 10;
        ImGui.Dummy(new Vector2(0, 1));
        if (e.Running)
        {
            if (Ui.BigButton("##stop", "Stop", new Vector4(0.886f, 0.314f, 0.29f, 1f), new Vector2(128, h))) e.Stop("Stopped");
        }
        else if (Ui.BigButton("##start", "Start", new Vector4(0.263f, 0.765f, 0.353f, 1f), new Vector2(128, h)))
        {
            if (e.Start(c.Goal, c.Goal == RunGoal.Leveling ? c.LevelTarget : c.GemstoneTarget, null) is { } why) Plugin.Chat.PrintError($"[FateRunner] {why}");
        }
        ImGui.SameLine(0, 10);
        var mode = Array.IndexOf(Modes, c.Goal);
        if (mode < 0) mode = 0;
        if (e.Running) ImGui.BeginDisabled();
        if (Ui.Segmented("##mode", ModeNames, ref mode, ImGui.GetContentRegionAvail().X, h)) { c.Goal = Modes[mode]; changed = true; }
        if (e.Running) ImGui.EndDisabled();
        if (e.Running && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Stop to change the mode.");

        // Target + cap on one line.
        ImGui.Dummy(new Vector2(0, 1));
        var rowStart = ImGui.GetCursorPosX();
        var full = ImGui.GetContentRegionAvail().X;
        ImGui.AlignTextToFramePadding();
        if (c.Goal == RunGoal.Gemstones)
        {
            ImGui.TextColored(Ui.Muted, "Stop at");
            ImGui.SameLine();
            var t = c.GemstoneTarget;
            ImGui.SetNextItemWidth(110);
            if (ImGui.InputInt("##target", ref t, 50, 250)) { c.GemstoneTarget = Math.Clamp(t, 1, Game.GemstoneCap); changed = true; }
            ImGui.SameLine();
            ImGui.TextColored(Ui.Muted, "gemstones");
        }
        else if (c.Goal == RunGoal.Leveling)
        {
            ImGui.TextColored(Ui.Muted, $"Your job is Lv{Game.JobLevel}. Level up to");
            ImGui.SameLine();
            var t = c.LevelTarget;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("##lvtarget", ref t, 1, 5)) { c.LevelTarget = Math.Clamp(t, 2, 100); changed = true; }
        }
        else
            ImGui.TextColored(Ui.Muted, "Runs until you press Stop.");

        if (c.Goal != RunGoal.Leveling)
        {
            var label = $"Stop before cap ({Game.GemstoneCap - c.CapMargin:N0})";
            var w = ImGui.GetTextLineHeight() * 1.9f + 8 + ImGui.CalcTextSize(label).X;
            ImGui.SameLine();
            var x = rowStart + full - w;
            if (x > ImGui.GetCursorPosX()) ImGui.SetCursorPosX(x); else ImGui.NewLine();
            var cap = c.StopNearCap;
            if (Ui.Toggle(label, ref cap)) { c.StopNearCap = cap; changed = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("FATE rewards past the 1,500 cap are lost — it stops so you can spend them.");
        }
        ImGui.Dummy(new Vector2(0, 2));
        return changed;
    }

    // ---------------- Tab: FATEs here ----------------

    private void DrawFatesHere(List<FateEngine.FateRow> rows)
    {
        ImGui.Dummy(new Vector2(0, 2));
        var zone = (uint)Plugin.ClientState.TerritoryType;
        if (rows.Count == 0)
        {
            ImGui.TextColored(Ui.Muted, FateZones.Get(zone) != null
                ? $"No FATEs up in {FateZones.Name(zone)} right now."
                : "You're not in a FATE zone. Press Start and it goes to one, or teleport to a zone to see its FATEs here.");
            return;
        }
        var w = ImGui.GetContentRegionAvail().X;
        foreach (var r in rows)
        {
            Ui.FateListRow(r, w);
            ImGui.Dummy(new Vector2(0, 0));
        }
    }

    // ---------------- Tab: Zones ----------------

    private bool DrawZones(Configuration c, FateEngine e)
    {
        var changed = false;
        var level = Game.JobLevel;
        var here = (uint)Plugin.ClientState.TerritoryType;
        var leveling = c.Goal == RunGoal.Leveling;
        var picks = leveling ? FateZones.ForLevel(level) : new List<uint>();
        ImGui.Dummy(new Vector2(0, 2));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        if (leveling)
            ImGui.TextColored(Ui.Muted, picks.Count > 0
                ? $"Leveling at Lv{level}: {FateZones.Name(picks[0])} first" + (picks.Count > 1 ? $", then {string.Join(", ", picks.Skip(1).Take(3).Select(FateZones.Name))}" : "") + ". It moves up as you level."
                : $"No attuned zone with FATEs for Lv{level}. Attune to that zone's aetheryte first.");
        else
            ImGui.TextColored(Ui.Muted, "Click zones to farm. Only Shadowbringers onward pays Bicolor Gemstones.");
        ImGui.PopTextWrapPos();

        var avail = ImGui.GetContentRegionAvail().X;
        var tileW = (avail - 8) / 2f;
        foreach (var expansion in FateZones.Expansions)
        {
            var zones = FateZones.Zones.Where(z => z.Expansion == expansion && (leveling || z.Gemstones)).ToArray();
            if (zones.Length == 0) continue;
            if (leveling && !zones.Any(z => z.MinLevel <= level + 10 && z.MaxLevel >= level - 12)) continue;

            ImGui.Dummy(new Vector2(0, 4));
            var rowStart = ImGui.GetCursorPosX();
            Ui.ExpansionHeader(expansion, leveling
                ? $"Lv{zones.Min(z => z.MinLevel)}–{zones.Max(z => z.MaxLevel)}"
                : $"{zones.Count(z => c.Zones.Contains(z.Territory))} / {zones.Length}");
            if (!leveling)
            {
                var bw = Ui.MiniButtonWidth("All") + Ui.MiniButtonWidth("None") + 5;
                ImGui.SameLine();
                ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), rowStart + avail - bw));
                if (Ui.MiniButton($"All##{expansion}")) { foreach (var z in zones) c.Zones.Add(z.Territory); changed = true; }
                ImGui.SameLine(0, 5);
                if (Ui.MiniButton($"None##{expansion}")) { foreach (var z in zones) c.Zones.Remove(z.Territory); changed = true; }
            }
            ImGui.Dummy(new Vector2(0, 1));

            var color = Ui.ExpansionColor(expansion);
            for (var i = 0; i < zones.Length; i++)
            {
                var z = zones[i];
                if (i % 2 == 1) ImGui.SameLine(0, 8);
                var attuned = FateZones.Attuned(z.Territory);
                var tooLow = level < z.MinLevel;
                var note = !attuned ? "not attuned" : tooLow ? $"needs Lv{z.MinLevel}" : null;
                var selected = leveling ? picks.Contains(z.Territory) : c.Zones.Contains(z.Territory);
                var active = e.Running && e.TargetZone == z.Territory;
                if (Ui.ZoneTile($"##zone{z.Territory}", FateZones.Name(z.Territory), $"Lv{z.MinLevel}–{z.MaxLevel}", note, color,
                                selected, attuned && !tooLow, here == z.Territory, active, tileW) && !leveling)
                {
                    if (!c.Zones.Remove(z.Territory)) c.Zones.Add(z.Territory);
                    changed = true;
                }
                if (i % 2 == 1 || i == zones.Length - 1) ImGui.Dummy(new Vector2(0, 0));
            }
        }

        ImGui.Dummy(new Vector2(0, 6));
        Ui.BeginPanel();
        changed |= Flag("Hop to the next zone when this one runs dry", c.HopZones, v => c.HopZones = v);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Ui.Muted, "Wait before hopping");
        ImGui.SameLine();
        var wait = c.EmptyZoneWaitSeconds;
        ImGui.SetNextItemWidth(110);
        if (ImGui.InputInt("s##wait", ref wait, 15, 60)) { c.EmptyZoneWaitSeconds = Math.Clamp(wait, 5, 900); changed = true; }
        Ui.EndPanel();
        return changed;
    }

    // ---------------- Tab: Which FATEs ----------------

    private bool DrawWhichFates(Configuration c)
    {
        var changed = false;
        ImGui.Dummy(new Vector2(0, 2));
        Ui.BeginPanel();
        Ui.CardTitle("FATE types");
        var col = ImGui.GetCursorPosX() + Ui.AvailX() / 2;
        changed |= KindFlag(FateKind.Kill, "Kill enemies", c.DoKill, v => c.DoKill = v, "Slay the listed enemies.");
        ImGui.SameLine(col); changed |= KindFlag(FateKind.Boss, "Bosses", c.DoBoss, v => c.DoBoss = v, "Notorious monsters.");
        changed |= KindFlag(FateKind.HandIn, "Hand-ins", c.DoHandIn, v => c.DoHandIn = v, "Collect the items enemies drop (or that lie around) and hand them to the FATE's NPC.");
        ImGui.SameLine(col); changed |= KindFlag(FateKind.Escort, "Escorts", c.DoEscort, v => c.DoEscort = v, "Follow and protect the NPC.");
        changed |= KindFlag(FateKind.Defend, "Defend", c.DoDefend, v => c.DoDefend = v, "Fend off waves attacking a spot or NPC.");
        Ui.EndPanel();

        ImGui.Dummy(new Vector2(0, 4));
        Ui.BeginPanel();
        Ui.CardTitle("Choosing");
        changed |= Flag("Start FATEs that wait for an NPC", c.StartNpcFates, v => c.StartNpcFates = v, "Talk to the NPC that begins FATEs waiting to start.");
        changed |= Flag("Prefer bonus FATEs", c.PreferBonus, v => c.PreferBonus = v, "FATEs with the bonus icon go first.");
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Ui.Muted, "Skip with less time left than");
        ImGui.SameLine();
        var secs = c.MinSecondsLeft;
        ImGui.SetNextItemWidth(110);
        if (ImGui.InputInt("s##secs", ref secs, 15, 60)) { c.MinSecondsLeft = Math.Clamp(secs, 0, 900); changed = true; }
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Ui.Muted, "Skip when already past");
        ImGui.SameLine();
        var prog = c.SkipAboveProgress;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("##prog", ref prog, 50, 100, "%d%%")) { c.SkipAboveProgress = prog; changed = true; }
        Ui.EndPanel();

        ImGui.Dummy(new Vector2(0, 4));
        Ui.Note("Level sync is always on: it syncs inside the ring before attacking (un-synced, a FATE pays nothing).", Ui.Good);
        return changed;
    }

    private static bool KindFlag(FateKind kind, string label, bool value, Action<bool> set, string help)
    {
        var size = ImGui.GetTextLineHeight() + 4;
        var pos = ImGui.GetCursorScreenPos();
        Ui.KindBadgeAt(ImGui.GetWindowDrawList(), pos + new Vector2(0, (ImGui.GetTextLineHeightWithSpacing() - size) / 2 - 1), size, kind);
        ImGui.Dummy(new Vector2(size, ImGui.GetTextLineHeightWithSpacing()));
        ImGui.SameLine(0, 8);
        return Flag(label, value, set, help);
    }

    // ---------------- Tab: Travel & combat ----------------

    private bool DrawTravel(Configuration c, FateEngine e)
    {
        var changed = false;
        ImGui.Dummy(new Vector2(0, 2));
        Ui.BeginPanel();
        Ui.CardTitle("Getting there");
        var col = ImGui.GetCursorPosX() + Ui.AvailX() / 2;
        changed |= Flag("Use mount", c.UseMount, v => c.UseMount = v);
        ImGui.SameLine(col); changed |= Flag("Fly where unlocked", c.Fly, v => c.Fly = v);
        changed |= Flag("Teleport with Lifestream", c.PreferLifestream, v => c.PreferLifestream = v, "Otherwise the game's own teleport.");
        Ui.EndPanel();

        ImGui.Dummy(new Vector2(0, 4));
        Ui.BeginPanel();
        Ui.CardTitle("Fighting");
        changed |= Flag("Use Wrath Combo", c.UseWrath, v => c.UseWrath = v, "Takes Wrath's Auto-Rotation over while running and puts it back afterwards. Your job needs its combos enabled in Wrath's Auto-Mode.");
        changed |= Flag($"Turn on {e.BossModName} AI", c.UseBossModAi, v => c.UseBossModAi = v,
                        e.BossModLoaded ? "Dodges AoEs and mechanics while running; turned off again when FateRunner stops." : "Neither BossMod nor BossMod Reborn is loaded.");
        var melee = c.MeleeRange;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Melee range", ref melee, 1f, 5f, "%.1f y")) { c.MeleeRange = melee; changed = true; }
        var ranged = c.RangedRange;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Ranged range", ref ranged, 5f, 25f, "%.0f y")) { c.RangedRange = ranged; changed = true; }
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Ui.Muted, "If knocked out, wait for a raise");
        ImGui.SameLine();
        var death = c.ReturnAfterDeathSeconds;
        ImGui.SetNextItemWidth(110);
        if (ImGui.InputInt("s##death", ref death, 5, 30)) { c.ReturnAfterDeathSeconds = Math.Clamp(death, 0, 300); changed = true; }
        Ui.EndPanel();

        ImGui.Dummy(new Vector2(0, 4));
        Ui.BeginPanel();
        Ui.CardTitle("Chocobo companion");
        changed |= Flag("Keep my chocobo out", c.AutoChocobo, v => c.AutoChocobo = v,
                        "Summons it with Gysahl Greens when it's away (out of combat, on the ground) and feeds it again before it leaves.");
        if (c.AutoChocobo)
        {
            var stances = new[] { ChocoboStance.Healer, ChocoboStance.Attacker, ChocoboStance.Defender, ChocoboStance.FreeStance };
            var names = new[] { "Healer", "Attacker", "Defender", "Free" };
            var si = Math.Max(0, Array.IndexOf(stances, c.ChocoboStance));
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Ui.Muted, "Stance");
            ImGui.SameLine();
            if (Ui.Segmented("##stance", names, ref si, Ui.AvailX(), ImGui.GetFrameHeight() + 4)) { c.ChocoboStance = stances[si]; changed = true; }
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Ui.Muted, "Feed again when less than");
            ImGui.SameLine();
            var mins = c.ChocoboRefreshSeconds / 60;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("min left##choco", ref mins, 1, 5)) { c.ChocoboRefreshSeconds = Math.Clamp(mins, 0, 25) * 60; changed = true; }
            var greens = Chocobo.Greens;
            var left = Chocobo.TimeLeft;
            Ui.Note(Chocobo.IsOut ? $"Out · {(int)left / 60}:{(int)left % 60:D2} left · {greens} Gysahl Greens" : $"Not out · {greens} Gysahl Greens",
                    greens == 0 ? Ui.Bad : Chocobo.IsOut ? Ui.Good : Ui.Muted);
        }
        Ui.EndPanel();

        ImGui.Dummy(new Vector2(0, 4));
        Ui.BeginPanel();
        Ui.CardTitle("Display");
        changed |= Flag("Show the overlay while running", c.ShowOverlay, v => c.ShowOverlay = v);
        Ui.EndPanel();
        return changed;
    }

    private static bool Flag(string label, bool value, Action<bool> set, string? help = null)
    {
        var v = value;
        if (!Ui.Toggle(label, ref v, help)) return false;
        set(v);
        return true;
    }
}
