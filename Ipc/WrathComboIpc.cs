using System;
using System.Collections.Generic;
using Dalamud.Plugin.Ipc;

namespace FateRunner.Ipc;

/// <summary>
/// Wrath Combo's lease-based IPC. While FateRunner holds a lease it controls Wrath's
/// Auto-Rotation; releasing the lease restores the user's own settings.
/// Names/signatures checked against WrathCombo Services/IPC/Provider.cs and docs/IPCExample.cs (Oct 2026).
/// </summary>
public sealed class WrathComboIpc : IpcBase
{
    protected override string InternalName => "WrathCombo";

    // Mirrors of Wrath's enums (WrathCombo.API). Dalamud IPC converts by value, so numbers must match.
    public enum AutoRotationConfigOption
    {
        InCombatOnly = 0,
        DPSRotationMode = 1,
        OnlyAttackInCombat = 13, // Wrath ignores targets that aren't already fighting when this is on
    }

    public enum DPSRotationMode
    {
        Manual = 0, // attack the current (hard) target
    }

    private readonly ICallGateSubscriber<string, string, Guid?> registerForLease;
    private readonly ICallGateSubscriber<Guid, object> releaseControl;
    private readonly ICallGateSubscriber<Guid, bool, object> setAutoRotationState;
    private readonly ICallGateSubscriber<Guid, object> setCurrentJobReady;
    private readonly ICallGateSubscriber<Guid, AutoRotationConfigOption, object, object> setConfigState;
    private readonly ICallGateSubscriber<bool> getAutoRotationState;
    private readonly ICallGateSubscriber<bool> isCurrentJobReady;
    private readonly ICallGateSubscriber<object, object?> getConfigState;
    private readonly ICallGateSubscriber<Guid, string, bool, object> setComboOptionState;
    private readonly ICallGateSubscriber<string, bool> getComboOptionState;

    private Guid? lease;
    private bool? lastAutoRotation;

    public WrathComboIpc()
    {
        var pi = Plugin.PluginInterface;
        registerForLease = pi.GetIpcSubscriber<string, string, Guid?>("WrathCombo.RegisterForLease");
        releaseControl = pi.GetIpcSubscriber<Guid, object>("WrathCombo.ReleaseControl");
        setAutoRotationState = pi.GetIpcSubscriber<Guid, bool, object>("WrathCombo.SetAutoRotationState");
        setCurrentJobReady = pi.GetIpcSubscriber<Guid, object>("WrathCombo.SetCurrentJobAutoRotationReady");
        setConfigState = pi.GetIpcSubscriber<Guid, AutoRotationConfigOption, object, object>("WrathCombo.SetAutoRotationConfigState");
        getAutoRotationState = pi.GetIpcSubscriber<bool>("WrathCombo.GetAutoRotationState");
        isCurrentJobReady = pi.GetIpcSubscriber<bool>("WrathCombo.IsCurrentJobAutoRotationReady");
        getConfigState = pi.GetIpcSubscriber<object, object?>("WrathCombo.GetAutoRotationConfigState");
        setComboOptionState = pi.GetIpcSubscriber<Guid, string, bool, object>("WrathCombo.SetComboOptionState");
        getComboOptionState = pi.GetIpcSubscriber<string, bool>("WrathCombo.GetComboOptionState");
    }

    public bool HasLease => lease != null;

    /// <summary>
    /// Wrath answered InvalidLease/BlacklistedLease: our lease was cancelled (e.g. a Wrath setting was changed
    /// while FateRunner held it, or Wrath reloaded). None of our overrides apply until we take a new one.
    /// </summary>
    public bool LeaseLost { get; private set; }

    /// <summary>Drops the dead lease and takes a fresh one with the same overrides.</summary>
    public bool Reacquire(bool configureForFarming, uint classJobId)
    {
        lease = null;
        lastAutoRotation = null;
        suppressingPullOptions = false;
        userOptionValues.Clear();
        LeaseLost = false;
        Plugin.Log.Warning("Wrath lease was cancelled (a Wrath setting changed or Wrath reloaded) — taking a new one.");
        if (!Acquire(configureForFarming)) return false;
        ReadyCurrentJob(classJobId, configureForFarming);
        return true;
    }

    /// <summary>Takes control of Auto-Rotation and readies the current job. Returns false if Wrath refused.</summary>
    public bool Acquire(bool configureForFarming)
    {
        if (lease == null)
            lease = Safe(() => registerForLease.InvokeFunc("FateRunner", "FateRunner"), null, "RegisterForLease");
        if (lease is not { } l)
        {
            Plugin.Log.Warning("Wrath refused the lease (check /xllog for Wrath's reason; a lease may still be held from before — reload Wrath).");
            return false;
        }

        // Must come before any SetAutoRotationConfigState call. Wrath (Leasing.AddRegistrationForAutoRotation)
        // reads the lease's stored on/off state whenever the lease has config overrides, without checking it
        // exists — so configuring first makes every later SetAutoRotationState throw and Wrath never attacks.
        Call("SetAutoRotationState(off, init)", () => setAutoRotationState.InvokeFunc(l, false));
        lastAutoRotation = false;

        Call("SetCurrentJobAutoRotationReady", () => setCurrentJobReady.InvokeFunc(l));
        if (configureForFarming)
        {
            // Wrath may act out of combat, on the mob FateRunner targets, even if that mob isn't fighting yet.
            Call("SetConfig InCombatOnly=false", () => setConfigState.InvokeFunc(l, AutoRotationConfigOption.InCombatOnly, false));
            Call("SetConfig DPSRotationMode=Manual", () => setConfigState.InvokeFunc(l, AutoRotationConfigOption.DPSRotationMode, (int)DPSRotationMode.Manual));
            Call("SetConfig OnlyAttackInCombat=false", () => setConfigState.InvokeFunc(l, AutoRotationConfigOption.OnlyAttackInCombat, false));
        }
        return true;
    }

    /// <summary>
    /// Asks Wrath to enable the current job's single- and multi-target combos in Auto-Mode.
    /// Wrath does this asynchronously (a few seconds); check <see cref="IsCurrentJobReady"/> afterwards.
    /// </summary>
    public void ReadyCurrentJob(uint classJobId, bool disableOpener)
    {
        if (lease is not { } l) return;
        Call("SetCurrentJobAutoRotationReady", () => setCurrentJobReady.InvokeFunc(l));
        if (disableOpener) DisableOpener(l, classJobId);
    }

    /// <summary>
    /// Wrath's job openers (e.g. RDM "Balance Opener", Lv100-109) start with a pre-pull "Cease" step that waits
    /// for a countdown or for combat — so out in the world Wrath never opens a fight. While FateRunner holds the
    /// lease it turns the current job's opener off; releasing the lease restores the user's setting.
    /// Preset names from WrathCombo Combos/CustomComboPreset.cs (Oct 2026).
    /// </summary>
    private void DisableOpener(Guid l, uint classJobId)
    {
        if (!OpenerPresets.TryGetValue(classJobId, out var preset)) return;
        Call($"SetComboOptionState {preset}=off", () => setComboOptionState.InvokeFunc(l, preset, false), alwaysLog: true);
    }

    private bool suppressingPullOptions;
    private readonly System.Collections.Generic.Dictionary<string, bool> userOptionValues = new();

    /// <summary>
    /// Some combo options pick an action the game only allows in combat (RDM Manafication). Out of combat Wrath
    /// keeps choosing it, the game refuses ("Cannot execute at this time"), and Wrath never reaches its real
    /// opener — even a Jolt pressed by hand is turned into Manafication by Wrath's button replacement.
    /// While pulling, those options are held off; once in combat they go back to the user's own setting.
    /// </summary>
    public void SetPullSuppression(uint classJobId, bool suppress)
    {
        if (lease is not { } l || suppress == suppressingPullOptions) return;
        if (!InCombatOnlyOptions.TryGetValue(classJobId, out var options)) return;
        suppressingPullOptions = suppress;

        foreach (var option in options)
        {
            if (suppress)
            {
                // Read the user's own value once, before our override hides it.
                if (!userOptionValues.ContainsKey(option))
                    userOptionValues[option] = Safe(() => getComboOptionState.InvokeFunc(option), true, $"GetComboOptionState {option}");
                Call($"SetComboOptionState {option}=off (pulling)", () => setComboOptionState.InvokeFunc(l, option, false), alwaysLog: true);
            }
            else
            {
                var userValue = userOptionValues.GetValueOrDefault(option, true);
                Call($"SetComboOptionState {option}={(userValue ? "on" : "off")} (in combat)", () => setComboOptionState.InvokeFunc(l, option, userValue), alwaysLog: true);
            }
        }
    }

    /// <summary>Per job: Wrath options whose action can only be used in combat. Preset names from Wrath 1.0.4.27.</summary>
    private static readonly System.Collections.Generic.Dictionary<uint, string[]> InCombatOnlyOptions = new()
    {
        [35] = ["RDM_ST_Manafication", "RDM_AoE_Manafication"],
    };

    private static readonly System.Collections.Generic.Dictionary<uint, string> OpenerPresets = new()
    {
        [19] = "PLD_ST_AdvancedMode_BalanceOpener",
        [21] = "WAR_ST_BalanceOpener",
        [32] = "DRK_ST_BalanceOpener",
        [37] = "GNB_ST_Opener",
        [20] = "MNK_STUseOpener",
        [22] = "DRG_ST_Opener",
        [30] = "NIN_ST_AdvancedMode_BalanceOpener",
        [34] = "SAM_ST_Adv_Opener",
        [39] = "RPR_ST_Opener",
        [41] = "VPR_ST_Opener",
        [31] = "MCH_ST_Adv_Opener",
        [38] = "DNC_ST_BalanceOpener",
        [25] = "BLM_ST_Opener",
        [27] = "SMN_ST_Advanced_Combo_Balance_Opener",
        [35] = "RDM_Balance_Opener",
        [42] = "PCT_ST_Advanced_Openers",
        [24] = "WHM_ST_MainCombo_Opener",
        [28] = "SCH_ST_ADV_DPS_Balance_Opener",
        [33] = "AST_ST_DPS_Opener",
        [40] = "SGE_ST_Adv_DPS_Opener",
        [36] = "BLU_ST_DPS_Opener",
    };

    /// <summary>
    /// Whether Wrath has a single- and multi-target combo enabled in Auto-Mode for the current job.
    /// If false, Auto-Rotation is "on" but has nothing to run — it will never attack. (Wrath caches this ~30s.)
    /// </summary>
    public bool? IsCurrentJobReady => IsAvailable ? Safe(() => (bool?)isCurrentJobReady.InvokeFunc(), null, "IsCurrentJobAutoRotationReady") : null;

    public void SetAutoRotation(bool on)
    {
        if (lease is not { } l) return;
        if (lastAutoRotation != on)
        {
            lastAutoRotation = on;
            Call($"SetAutoRotationState({(on ? "ON" : "off")})", () => setAutoRotationState.InvokeFunc(l, on), alwaysLog: true);
        }
        else
        {
            Call($"SetAutoRotationState({(on ? "ON" : "off")})", () => setAutoRotationState.InvokeFunc(l, on));
        }
    }

    /// <summary>What Wrath itself reports right now — for the DIAG log line.</summary>
    public string DiagSummary()
    {
        if (!IsAvailable) return "wrath=not loaded";
        string Cfg(AutoRotationConfigOption o)
            => Safe(() => getConfigState.InvokeFunc((int)o)?.ToString() ?? "null", "error", $"GetConfig {o}");
        return $"wrathOn={Safe(() => getAutoRotationState.InvokeFunc().ToString(), "error", "GetAutoRotationState")} " +
               $"wrathJobReady={IsCurrentJobReady?.ToString() ?? "error"} " +
               $"wrathInCombatOnly={Cfg(AutoRotationConfigOption.InCombatOnly)} " +
               $"wrathOnlyAttackInCombat={Cfg(AutoRotationConfigOption.OnlyAttackInCombat)} " +
               $"wrathDpsMode={Cfg(AutoRotationConfigOption.DPSRotationMode)}";
    }

    public void Release()
    {
        if (lease is not { } l) return;
        Safe(() => releaseControl.InvokeAction(l), "ReleaseControl");
        lease = null;
        lastAutoRotation = null;
        suppressingPullOptions = false;
        userOptionValues.Clear();
        LeaseLost = false;
    }

    /// <summary>Calls a Wrath setter and logs Wrath's SetResult (Okay / Duplicate / InvalidLease / ...).</summary>
    private void Call(string what, Func<object> f, bool alwaysLog = false)
    {
        var result = Safe(() => f()?.ToString() ?? "null", "EXCEPTION", what);
        if (result is "InvalidLease" or "11" or "BlacklistedLease" or "12")
            LeaseLost = true;
        if (result is "Duplicate" or "13") return;
        if (alwaysLog || result is not ("Okay" or "OkayWorking" or "0" or "1"))
            Plugin.Log.Debug($"Wrath {what} -> {result}");
    }
}
