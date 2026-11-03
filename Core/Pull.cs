using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FateRunner.Core;

/// <summary>
/// Opening a fight. Wrath's Auto-Rotation won't start one by itself, so the first hit comes from here: the job's
/// basic damage action (ranged where the job has one), and only if that's refused, switching auto-attack on. Every
/// press is checked with the game's GetActionStatus first, so nothing is pressed that the game would refuse
/// ("Cannot execute at this time").
/// </summary>
public static unsafe class Pull
{
    private static ulong lastTarget;
    private static DateTime lastPress = DateTime.MinValue;
    private static bool lastAccepted;
    private static int tries;

    /// <summary>Call while next to an enemy and not yet in combat. Presses at most once every couple of seconds.</summary>
    public static void Open(IBattleChara me, IBattleChara mob, Action stopMoving)
    {
        if (mob.GameObjectId != lastTarget) { lastTarget = mob.GameObjectId; tries = 0; lastAccepted = false; }
        if (tries >= 5) return;
        // A press that went through needs time to land (auto-attack is a toggle — pressing again turns it off).
        var wait = lastAccepted ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(1.5);
        if (DateTime.UtcNow - lastPress < wait) return;
        var am = ActionManager.Instance();
        if (am == null || am->AnimationLock > 0) return;

        lastPress = DateTime.UtcNow;
        tries++;

        if (Openers.TryGetValue(me.ClassJob.RowId, out var opener))
        {
            var id = am->GetAdjustedActionId(opener); // Jolt → Jolt III, Stone → Glare…
            var status = am->GetActionStatus(ActionType.Action, id, mob.GameObjectId);
            if (status == 0)
            {
                stopMoving(); // a spell with a cast time can't be cast while moving
                lastAccepted = am->UseAction(ActionType.Action, id, mob.GameObjectId);
                Plugin.Log.Debug($"Pull: opener {id} on {mob.Name.TextValue} -> {(lastAccepted ? "ok" : "refused")}");
                if (lastAccepted) return;
            }
            else Plugin.Log.Debug($"Pull: opener {id} on {mob.Name.TextValue} not usable now (status {status})");
        }

        const uint AutoAttack = 1; // general action, same as the hotbar button
        if (am->GetActionStatus(ActionType.GeneralAction, AutoAttack, mob.GameObjectId) != 0) { lastAccepted = false; return; }
        lastAccepted = am->UseAction(ActionType.GeneralAction, AutoAttack, mob.GameObjectId);
        Plugin.Log.Debug($"Pull: auto-attack on {mob.Name.TextValue} -> {(lastAccepted ? "ok" : "refused")}");
    }

    /// <summary>Each job's basic damage action (ClassJob row id → action id); base classes share their job's.</summary>
    private static readonly Dictionary<uint, uint> Openers = new()
    {
        [1] = 24, [19] = 24,          // GLA / PLD  Shield Lob
        [3] = 46, [21] = 46,          // MRD / WAR  Tomahawk
        [32] = 3624,                  // DRK        Unmend
        [37] = 16143,                 // GNB        Lightning Shot
        [2] = 53, [20] = 53,          // PGL / MNK  Bootshine
        [4] = 90, [22] = 90,          // LNC / DRG  Piercing Talon
        [29] = 2274, [30] = 2274,     // ROG / NIN  Throwing Dagger
        [34] = 7486,                  // SAM        Enpi
        [39] = 24386,                 // RPR        Harpe
        [41] = 34632,                 // VPR        Writhing Snap
        [5] = 97, [23] = 97,          // ARC / BRD  Heavy Shot
        [31] = 2866,                  // MCH        Split Shot
        [38] = 15989,                 // DNC        Cascade
        [7] = 142, [25] = 142,        // THM / BLM  Blizzard
        [26] = 163, [27] = 163,       // ACN / SMN  Ruin
        [28] = 17869,                 // SCH        Ruin
        [35] = 7503,                  // RDM        Jolt
        [42] = 34650,                 // PCT        Fire in Red
        [6] = 119, [24] = 119,        // CNJ / WHM  Stone
        [33] = 3596,                  // AST        Malefic
        [40] = 24283,                 // SGE        Dosis
        [36] = 11385,                 // BLU        Water Cannon
    };
}
