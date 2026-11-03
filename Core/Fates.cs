using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;

namespace FateRunner.Core;

public enum FateKind { Kill, Boss, HandIn, Escort, Defend }

/// <summary>A snapshot of one FATE in the zone (read from the game's FateManager each tick).</summary>
public sealed record FateInfo(
    ushort Id, string Name, FateState State, int Progress, Vector3 Position, float Radius, int Level, int MaxLevel,
    bool Bonus, long SecondsLeft, uint ObjectiveNpc, uint MotivationNpc, uint EventItem, int HandInCount, FateKind Kind)
{
    public bool Running => State == FateState.Running;
    public bool Preparing => State == FateState.Preparing;
    /// <summary>Waiting for someone to talk to an NPC to begin it.</summary>
    public bool NeedsNpcStart => Preparing && MotivationNpc != 0 && MotivationNpc != 0xE0000000;
}

public static unsafe class Fates
{
    /// <summary>Every FATE the game knows about in this zone right now.</summary>
    public static List<FateInfo> All()
    {
        var list = new List<FateInfo>();
        var fm = FateManager.Instance();
        if (fm == null) return list;
        var now = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.GetServerTime();
        for (var i = 0; i < fm->Fates.Count; i++)
        {
            var f = fm->Fates[i].Value;
            if (f == null || f->FateId == 0) continue;
            list.Add(Read(f, now));
        }
        return list;
    }

    public static FateInfo? Get(ushort id)
    {
        var fm = FateManager.Instance();
        if (fm == null) return null;
        var f = fm->GetFateById(id);
        return f == null ? null : Read(f, FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.GetServerTime());
    }

    private static FateInfo Read(FateContext* f, long now)
    {
        // Server clock, not the PC's (which can drift).
        var end = (long)f->StartTimeEpoch + f->Duration;
        var left = f->State == FateState.Preparing ? f->Duration : Math.Max(0, end - now);
        return new FateInfo(f->FateId, f->Name.ToString(), f->State, f->Progress, f->Location, f->Radius, f->Level, f->MaxLevel,
                            f->IsBonus, left, f->ObjectiveNpc, f->MotivationNpc, f->EventItem, f->HandInCount, KindOf(f));
    }

    /// <summary>
    /// What kind of FATE: one with an event item is a hand-in; otherwise by its map icon (boss / escort / defend), with
    /// the FATE rule as a fallback; everything else is "kill enemies".
    /// </summary>
    private static FateKind KindOf(FateContext* f)
    {
        if (f->EventItem != 0) return FateKind.HandIn;
        foreach (var icon in new[] { f->MapIconId, f->IconId })
            switch (icon)
            {
                case 60722 or 60723: return FateKind.Boss;
                case 60725 or 60726: return FateKind.Escort;
                case 60727 or 60728: return FateKind.Defend;
                case 60729 or 60730: return FateKind.HandIn;
            }
        return f->Rule switch { 4 => FateKind.Boss, 5 => FateKind.Defend, 6 => FateKind.Escort, _ => FateKind.Kill };
    }

    /// <summary>The FATE you're currently in (inside its ring), or 0.</summary>
    public static ushort CurrentId
    {
        get
        {
            var fm = FateManager.Instance();
            return fm == null || fm->CurrentFate == null ? (ushort)0 : fm->CurrentFate->FateId;
        }
    }

    /// <summary>Level-syncs to the FATE you're in, if not already (LevelSync toggles, so check first).</summary>
    public static bool SyncTo(ushort id)
    {
        var fm = FateManager.Instance();
        if (fm == null || fm->CurrentFate == null || fm->CurrentFate->FateId != id) return false;
        if (fm->SyncedFateId == id) return false;
        fm->LevelSync();
        return true;
    }

    public static bool SyncedTo(ushort id)
    {
        var fm = FateManager.Instance();
        return fm != null && fm->SyncedFateId == id;
    }
}
