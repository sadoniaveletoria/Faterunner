using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FateRunner.Core;
using FateRunner.Data;

namespace FateRunner.Windows;

/// <summary>A small see-through box while running: what it's doing, the current FATE, and gemstones.</summary>
public sealed class OverlayWindow : Window
{
    private readonly Plugin p;

    public OverlayWindow(Plugin plugin) : base("FateRunner overlay###FateRunnerOverlay",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar)
    {
        p = plugin;
        RespectCloseHotkey = false;
        Position = new Vector2(40, 260);
        PositionCondition = ImGuiCond.FirstUseEver;
        IsOpen = true;
    }

    public override bool DrawConditions() => p.Config.ShowOverlay && p.Engine.Running;

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.078f, 0.098f, 0.8f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1f, 1f, 1f, 0.07f));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    public override void Draw()
    {
        var e = p.Engine;
        const float w = 290f;
        var start = ImGui.GetCursorPosX();

        // Title row: logo, name, phase pill, Stop on the right.
        Ui.Logo(ImGui.GetTextLineHeight() + 2);
        ImGui.SameLine(0, 7);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("FateRunner");
        ImGui.SameLine(0, 8);
        var (phase, color) = Ui.PhaseLabel(e.Phase, e.Running);
        Ui.PhasePill(phase, color);
        ImGui.SameLine();
        var stopW = Ui.MiniButtonWidth("Stop");
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), start + w - stopW));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.95f, 0.36f, 0.36f, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.95f, 0.36f, 0.36f, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.7f, 0.7f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        if (ImGui.Button("Stop##ov")) e.Stop("Stopped");
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);

        ImGui.PushTextWrapPos(start + w);
        ImGui.TextColored(Ui.Muted, e.Status);
        ImGui.PopTextWrapPos();

        if (e.Current is { } f && e.Phase is RunPhase.InFate or RunPhase.Travel or RunPhase.StartNpc)
            Ui.FateCard(f, Fates.SyncedTo(f.Id), w, true);

        // Goal progress on the left, zone on the right.
        ImGui.Dummy(new Vector2(0, 1));
        var half = (w - 10) / 2;
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var h = ImGui.GetTextLineHeight();
        string left;
        float frac;
        if (e.Goal == RunGoal.Leveling)
        {
            var lv = Game.JobLevel;
            left = $"Lv{lv} → {e.Target}";
            frac = e.Target <= e.LevelAtStart ? 1f : Ui.Clamp01((lv - e.LevelAtStart) / (float)(e.Target - e.LevelAtStart));
        }
        else
        {
            var gems = Game.Gemstones;
            var goal = e.Goal == RunGoal.Gemstones ? e.Target : Game.GemstoneCap;
            left = $"{gems:N0} / {goal:N0} gems";
            frac = Ui.Clamp01(gems / (float)Math.Max(1, goal));
        }
        dl.AddText(pos, ImGui.GetColorU32(Ui.Muted), Ui.Fit(left, half));
        Ui.GradientBarAt(dl, pos + new Vector2(0, h + 4), half, 5f, frac, new Vector4(0.26f, 0.76f, 0.35f, 1f), Ui.Good);

        var zone = e.TargetZone != 0 ? FateZones.Name(e.TargetZone) : "—";
        var rightText = Ui.Fit($"{zone} · {e.FatesDone} FATEs", half);
        dl.AddText(pos + new Vector2(half + 10, 0), ImGui.GetColorU32(Ui.Muted), rightText);
        var mins = Math.Max(1, (DateTime.UtcNow - e.StartedAt).TotalMinutes);
        dl.AddText(pos + new Vector2(half + 10, h + 1), ImGui.GetColorU32(Ui.Dim),
                   Ui.Fit($"{(int)mins}m · {e.FatesDone / mins * 60:0.#} FATEs/h", half));
        ImGui.Dummy(new Vector2(w, h * 2 + 6));
    }
}
