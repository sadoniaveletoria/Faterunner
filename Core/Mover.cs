using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using FateRunner.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FateRunner.Core;

/// <summary>
/// Getting from A to B: mount when it's far, take off where flying is unlocked, let vnavmesh fly/walk the route,
/// and come down on walkable ground near the destination before getting off.
/// </summary>
public sealed class Mover
{
    private readonly Configuration cfg;
    private readonly VNavmeshIpc nav;
    private readonly Dictionary<uint, DateTime> lastAction = new();
    private Vector3 lastDest = new(float.MaxValue), lastTarget = new(float.MaxValue);
    // No-progress guard: where we were when we last moved, and when.
    private Vector3 progressPos = new(float.MaxValue);
    private DateTime progressAt = DateTime.MinValue, lastStepAt = DateTime.MinValue;
    private Vector3 progressDest = new(float.MaxValue);
    private DateTime lastIssue = DateTime.MinValue, mountTryStart = DateTime.MinValue, mountRetryAfter = DateTime.MinValue;
    private int takeoffTries;
    // Landing: when it started, the spot being flown down to, which spot (0 = at the destination), and timers.
    private DateTime landStart = DateTime.MinValue, landStepAt = DateTime.MinValue, lowSince = DateTime.MinValue;
    private int landAttempt;
    private uint lastTerritory;
    private DateTime territoryChangedAt = DateTime.MinValue;

    public string Status { get; private set; } = "";

    public Mover(Configuration cfg, VNavmeshIpc nav) { this.cfg = cfg; this.nav = nav; }

    public static bool Mounted => Plugin.Condition[ConditionFlag.Mounted];
    public static bool Airborne => Plugin.Condition[ConditionFlag.InFlight] || Plugin.Condition[ConditionFlag.Diving];

    public void Reset()
    {
        lastDest = lastTarget = progressPos = new Vector3(float.MaxValue);
        progressAt = DateTime.MinValue;
        takeoffTries = 0;
        mountTryStart = DateTime.MinValue;
        landStart = landStepAt = lowSince = DateTime.MinValue;
        landAttempt = 0;
    }

    public void Stop() { nav.Stop(); Reset(); }

    /// <summary>
    /// One step towards <paramref name="dest"/>. True once within <paramref name="range"/> (on the ground, off the mount
    /// if <paramref name="dismount"/>). <paramref name="mountAbove"/>: only mount when further than this.
    /// </summary>
    public bool Step(Vector3 dest, float range, bool dismount = true, float mountAbove = 30f)
    {
        TrackTerritory();
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null || !CanActNow()) return false;
        if (!nav.IsReady) { Status = $"Waiting for vnavmesh ({nav.BuildProgress:P0})"; return false; }

        var dist = Vector3.Distance(me.Position, dest);
        var flat = Vector2.Distance(new Vector2(me.Position.X, me.Position.Z), new Vector2(dest.X, dest.Z));

        // Arrived in the air: come down onto walkable ground before getting off (the game won't dismount from high
        // up or over ground you can't land on). Sticky once started, so trying a spot a little further out doesn't
        // flip back to normal travel.
        // vnavmesh is aimed at a ground point near dest, and stops once that point is within range — so count that
        // as arrived too, or we'd keep asking it to go somewhere it says we already are.
        var nearTarget = Vector3.Distance(lastDest, dest) < 4f && Vector3.Distance(me.Position, lastTarget) <= range;

        // Not moving for a while although not there (vnavmesh keeps finishing at once): close enough counts.
        // Restart the clock when we moved, when it's a new destination, or when Step wasn't being called (fighting,
        // talking…), so standing still on purpose doesn't count.
        var now = DateTime.UtcNow;
        if (Vector3.Distance(me.Position, progressPos) > 1.5f || nav.IsPathfinding || Vector3.Distance(progressDest, dest) > 4f ||
            now - lastStepAt > TimeSpan.FromSeconds(1))
        {
            progressPos = me.Position;
            progressAt = now;
            progressDest = dest;
        }
        lastStepAt = now;
        var noProgress = progressAt != DateTime.MinValue && DateTime.UtcNow - progressAt > TimeSpan.FromSeconds(6)
                         && !Plugin.Condition[ConditionFlag.InCombat] && flat <= Math.Max(range, 8f) * 2f;

        var landing = landStart != DateTime.MinValue && flat <= Math.Max(range, 8f) + 18f;
        if (Airborne && (dist <= range || flat <= Math.Max(range, 8f) || landing || nearTarget || noProgress))
        {
            Land(me.Position, dest, range);
            return false;
        }
        if (!Airborne) { landStart = landStepAt = lowSince = DateTime.MinValue; landAttempt = 0; }

        // Arrived.
        if (dist <= range || nearTarget || noProgress)
        {
            if (dismount && Mounted)
            {
                if (nav.IsBusy) nav.Stop();
                GeneralAction(23, 1.5); // Dismount
                Status = "Dismounting";
                return false;
            }
            if (nav.IsBusy) nav.Stop();
            Status = "";
            return true;
        }

        // Mount up for longer trips.
        if (!Mounted && cfg.UseMount && dist > mountAbove && !Plugin.Condition[ConditionFlag.InCombat] && DateTime.UtcNow >= mountRetryAfter)
        {
            if (mountTryStart == DateTime.MinValue) mountTryStart = DateTime.UtcNow;
            if (DateTime.UtcNow - mountTryStart > TimeSpan.FromSeconds(8))
            {
                mountTryStart = DateTime.MinValue;
                mountRetryAfter = DateTime.UtcNow.AddSeconds(30);
            }
            else
            {
                if (nav.IsBusy) nav.Stop();
                GeneralAction(9, 3.0); // Mount Roulette
                Status = "Mounting up";
                return false;
            }
        }
        if (Mounted) mountTryStart = DateTime.MinValue;

        // Take off where flying is unlocked.
        var fly = cfg.Fly && Mounted && CanFlyHere();
        if (fly && !Airborne && dist > 40f && takeoffTries < 4)
        {
            if (nav.IsBusy) nav.Stop();
            if (GeneralAction(2, 1.5)) takeoffTries++; // Jump = take off while mounted
            Status = "Taking off";
            return false;
        }

        // Path there (re-path when the target moved or the route ended early).
        var moved = Vector3.Distance(lastDest, dest) > 4f;
        var stale = !nav.IsBusy && DateTime.UtcNow - lastIssue > TimeSpan.FromSeconds(2);
        if (!nav.IsPathfinding && (moved || stale))
        {
            lastDest = dest;
            lastIssue = DateTime.UtcNow;
            nav.PrepareMove();
            var target = dest;
            // Flying in: aim for walkable ground just short of the spot so the landing is clean.
            if (Airborne && nav.GroundPoint(dest.X, dest.Z, dest.Y) is { } ground) target = ground;
            lastTarget = target;
            nav.MoveCloseTo(target, Math.Max(1f, range - 0.5f), Airborne);
        }
        Status = nav.IsPathfinding ? "Finding a path" : $"{(Airborne ? "Flying" : Mounted ? "Riding" : "Running")} ({dist:0}y)";
        return false;
    }

    /// <summary>
    /// Getting down from the air near <paramref name="dest"/>: fly down to walkable ground (right at the destination
    /// first, then spots around it), and once only a few yalms up, dismount — which lands and gets off. A spot that
    /// can't be landed on, or that vnavmesh can't reach, is given up after a few seconds for the next one.
    /// </summary>
    private void Land(Vector3 pos, Vector3 dest, float range)
    {
        var now = DateTime.UtcNow;
        if (landStart == DateTime.MinValue) { landStart = now; landAttempt = 0; landStepAt = lowSince = DateTime.MinValue; }

        var under = nav.GroundPoint(pos.X, pos.Z, pos.Y);
        var height = under is { } g ? pos.Y - g.Y : 99f;

        if (height < 4.5f)
        {
            // Low enough: dismount lands and gets off.
            if (nav.IsBusy) nav.Stop();
            if (lowSince == DateTime.MinValue) lowSince = now;
            GeneralAction(23, 1.5);
            Status = "Landing";
            // Still in the air a while later: this ground can't be landed on (water, a roof, a slope) — try another.
            if (now - lowSince > TimeSpan.FromSeconds(4)) { lowSince = DateTime.MinValue; FlyDownTo(pos, dest, range); }
            return;
        }
        lowSince = DateTime.MinValue;

        // High up: fly down to a landing spot; pick the next spot when the route ended high or took too long.
        var ended = landStepAt != DateTime.MinValue && !nav.IsBusy && now - landStepAt > TimeSpan.FromSeconds(1.5);
        var tooLong = landStepAt != DateTime.MinValue && now - landStepAt > TimeSpan.FromSeconds(10);
        if (landStepAt == DateTime.MinValue || ended || tooLong)
        {
            if (nav.IsBusy) nav.Stop();
            FlyDownTo(pos, dest, range);
        }
        Status = landAttempt <= 1 ? $"Coming down to land ({height:0}y up)" : $"Looking for a spot to land (try {landAttempt})";
    }

    private void FlyDownTo(Vector3 pos, Vector3 dest, float range)
    {
        landStepAt = DateTime.UtcNow;
        // Spot 0: the destination itself. Then a spiral around it, staying close enough to count as arrived.
        for (var tries = 0; tries < 12; tries++)
        {
            var n = landAttempt++ % 13;
            Vector3 probe;
            if (n == 0) probe = dest;
            else
            {
                var angle = n * 2.4f; // ~137°, spreads the spots evenly
                var r = Math.Min(4f + 2.5f * n, Math.Max(range, 8f) + 15f);
                probe = dest + new Vector3(MathF.Cos(angle) * r, 0, MathF.Sin(angle) * r);
            }
            if (nav.GroundPoint(probe.X, probe.Z, Math.Max(pos.Y, dest.Y)) is not { } spot) continue;
            nav.PrepareMove();
            nav.MoveCloseTo(spot, 0.5f, true);
            return;
        }
    }

    public static unsafe bool CanFlyHere()
    {
        if (!Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().TryGetRow(Plugin.ClientState.TerritoryType, out var terr)) return false;
        var currents = terr.AetherCurrentCompFlgSet.RowId;
        if (currents == 0) return false;
        var ps = PlayerState.Instance();
        return ps != null && ps->IsAetherCurrentZoneComplete(currents);
    }

    /// <summary>Uses a general action (mount, jump, dismount…) when the game accepts it, at most once per cooldown.</summary>
    public unsafe bool GeneralAction(uint actionId, double cooldownSeconds)
    {
        if (lastAction.TryGetValue(actionId, out var last) && (DateTime.UtcNow - last).TotalSeconds < cooldownSeconds) return false;
        if (!CanActNow()) return false;
        var am = ActionManager.Instance();
        if (am == null || am->AnimationLock > 0) return false;
        if (am->GetActionStatus(ActionType.GeneralAction, actionId) != 0) return false;
        lastAction[actionId] = DateTime.UtcNow;
        return am->UseAction(ActionType.GeneralAction, actionId);
    }

    /// <summary>Not zoning, casting, in a cutscene/event, dead, or still settling after a zone change.</summary>
    public bool CanActNow()
    {
        var c = Plugin.Condition;
        if (c[ConditionFlag.BetweenAreas] || c[ConditionFlag.BetweenAreas51] || c[ConditionFlag.Casting] ||
            c[ConditionFlag.Jumping] || c[ConditionFlag.Occupied] || c[ConditionFlag.OccupiedInEvent] ||
            c[ConditionFlag.OccupiedInCutSceneEvent] || c[ConditionFlag.Unconscious])
            return false;
        return DateTime.UtcNow - territoryChangedAt > TimeSpan.FromSeconds(2.5);
    }

    private void TrackTerritory()
    {
        var t = (uint)Plugin.ClientState.TerritoryType;
        if (t == lastTerritory) return;
        lastTerritory = t;
        territoryChangedAt = DateTime.UtcNow;
    }
}
