using System;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FateRunner.Core;

/// <summary>Companion stances (the game's buddy command ids).</summary>
public enum ChocoboStance : byte { FreeStance = 4, Defender = 5, Attacker = 6, Healer = 7 }

/// <summary>
/// Keeps your chocobo companion out while running: feeds Gysahl Greens when it's away or about to leave, and sets
/// the stance you picked. Only acts when the game would accept it (on the ground, not mounted, greens in the bags,
/// a zone that allows companions).
/// </summary>
public sealed unsafe class Chocobo
{
    public const uint GysahlGreens = 4868;
    private const uint ItemParam = 0xFFFF;

    private readonly Configuration cfg;
    private DateTime nextSummon = DateTime.MinValue, nextStance = DateTime.MinValue;
    private bool warnedNoGreens;

    public Chocobo(Configuration cfg) => this.cfg = cfg;

    /// <summary>Seconds the companion has left (0 = not out).</summary>
    public static float TimeLeft
    {
        get
        {
            var ui = UIState.Instance();
            return ui == null ? 0 : ui->Buddy.CompanionInfo.TimeLeft;
        }
    }

    public static bool IsOut => TimeLeft > 0;
    public static int Greens => Game.ItemCount(GysahlGreens);

    public static ChocoboStance? Stance
    {
        get
        {
            var ui = UIState.Instance();
            if (ui == null || !IsOut) return null;
            var s = ui->Buddy.CompanionInfo.ActiveCommand;
            return s is >= 4 and <= 7 ? (ChocoboStance)s : null;
        }
    }

    /// <summary>Call at the start of a run.</summary>
    public void Reset()
    {
        warnedNoGreens = false;
        nextSummon = nextStance = DateTime.MinValue;
    }

    /// <summary>One check per engine tick. Never blocks: it either uses greens / a stance command or does nothing.</summary>
    public void Tick()
    {
        if (!cfg.AutoChocobo) return;
        var c = Plugin.Condition;
        if (c[ConditionFlag.Mounted] || c[ConditionFlag.InFlight] || c[ConditionFlag.Diving] || c[ConditionFlag.Casting] ||
            c[ConditionFlag.Occupied] || c[ConditionFlag.OccupiedInEvent] || c[ConditionFlag.BetweenAreas] || c[ConditionFlag.Unconscious])
            return;
        var am = ActionManager.Instance();
        if (am == null || am->AnimationLock > 0) return;
        var now = DateTime.UtcNow;

        // Away, or leaving soon: feed greens (summons it, or tops the timer up).
        if (TimeLeft < cfg.ChocoboRefreshSeconds && now >= nextSummon)
        {
            nextSummon = now.AddSeconds(5);
            if (Greens <= 0)
            {
                if (!warnedNoGreens)
                    Plugin.Chat.PrintError("[FateRunner] No Gysahl Greens — can't summon your chocobo. (Buy them from a general merchant or the Grand Company quartermaster.)");
                warnedNoGreens = true;
                return;
            }
            // Status 0 = usable now (not in combat, companions allowed in this zone…).
            if (!c[ConditionFlag.InCombat] && am->GetActionStatus(ActionType.Item, GysahlGreens) == 0)
            {
                am->UseAction(ActionType.Item, GysahlGreens, 0xE0000000, ItemParam);
                nextStance = now.AddSeconds(4); // give it a moment to arrive before setting the stance
            }
            return;
        }

        // Out with the wrong stance: set it.
        if (IsOut && now >= nextStance && Stance != cfg.ChocoboStance)
        {
            nextStance = now.AddSeconds(4);
            var id = (uint)cfg.ChocoboStance;
            if (am->GetActionStatus(ActionType.BuddyAction, id) == 0) am->UseAction(ActionType.BuddyAction, id);
        }
    }
}
