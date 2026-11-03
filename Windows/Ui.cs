using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace FateRunner.Windows;

/// <summary>Small shared look for FateRunner's windows: colours, status dots, section headers, pills, help markers.</summary>
public static partial class Ui
{
    public static readonly Vector4 Accent = new(0.55f, 0.78f, 1f, 1f);
    public static readonly Vector4 Good = new(0.36f, 0.85f, 0.42f, 1f);
    public static readonly Vector4 Bad = new(0.95f, 0.36f, 0.36f, 1f);
    public static readonly Vector4 Warn = new(1f, 0.78f, 0.3f, 1f);
    public static readonly Vector4 Muted = new(0.62f, 0.62f, 0.66f, 1f);

    /// <summary>A filled dot, vertically centred on the current text line.</summary>
    public static void Dot(Vector4 color, float radius = 4.5f)
    {
        var h = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        var center = new Vector2(pos.X + radius + 1, pos.Y + h / 2f + 1);
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircleFilled(center, radius, ImGui.GetColorU32(color));
        dl.AddCircle(center, radius, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)));
        ImGui.Dummy(new Vector2(radius * 2 + 2, h));
    }

    /// <summary>
    /// Plugin status: green dot when it's loaded, red when it isn't. Hover for what FateRunner uses it for.
    /// </summary>
    public static void PluginStatus(string name, bool loaded, bool required, string usedFor)
    {
        ImGui.BeginGroup();
        Dot(loaded ? Good : Bad);
        ImGui.SameLine(0, 4);
        if (loaded) ImGui.TextUnformatted(name);
        else ImGui.TextColored(required ? Bad : Muted, name);
        ImGui.EndGroup();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{name}: {(loaded ? "detected" : "not detected")}{(required ? " (required)" : " (optional)")}\n{usedFor}");
    }

    /// <summary>Coloured section title with a thin rule under it.</summary>
    public static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(Accent, title);
        var pos = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddLine(pos, pos + new Vector2(w, 0), ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.35f)));
        ImGui.Dummy(new Vector2(0, 3));
    }

    /// <summary>A rounded coloured label, e.g. a state ("Farming", "Failed").</summary>
    public static void Pill(string text, Vector4 color)
    {
        var size = ImGui.CalcTextSize(text);
        var pad = new Vector2(7, 1);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size + pad * 2, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.22f)), 8f);
        dl.AddRect(pos, pos + size + pad * 2, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.7f)), 8f);
        ImGui.SetCursorScreenPos(pos + pad);
        ImGui.TextColored(color, text);
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y));
        ImGui.Dummy(size + pad * 2);
    }

    /// <summary>"(?)" that shows a tooltip — keeps long explanations out of the way.</summary>
    public static void Help(string text)
    {
        ImGui.SameLine();
        ImGui.TextColored(Muted, "(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    /// <summary>A button tinted with a colour (green Start, red Stop…).</summary>
    public static bool ColorButton(string label, Vector4 color, Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(color.X * 0.55f, color.Y * 0.55f, color.Z * 0.55f, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(color.X * 0.7f, color.Y * 0.7f, color.Z * 0.7f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(color.X * 0.85f, color.Y * 0.85f, color.Z * 0.85f, 1f));
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    /// <summary>A bordered, rounded panel. Use with <c>using (Ui.Card("id", height)) { … }</c>.</summary>
    public static CardScope Card(string id, float height) => new(id, height);

    public readonly struct CardScope : IDisposable
    {
        public CardScope(string id, float height)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.035f));
            ImGui.BeginChild(id, new Vector2(-1, height), true);
        }

        public void Dispose()
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        }
    }

    /// <summary>Two-colour "label value" text, e.g. "Data  254 items".</summary>
    public static void LabelValue(string label, string value, Vector4? valueColor = null)
    {
        ImGui.TextColored(Muted, label);
        ImGui.SameLine();
        if (valueColor is { } c) ImGui.TextColored(c, value);
        else ImGui.TextUnformatted(value);
    }

    public static Vector4 StateColor(bool failed, bool done, bool running)
        => failed ? Bad : done ? Good : running ? Warn : Muted;

    public static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);

    public static readonly Vector4 Track = new(0.17f, 0.19f, 0.23f, 1f);
    public static readonly Vector4 CardBg = new(0.11f, 0.13f, 0.17f, 1f);
    public static readonly Vector4 Border = new(0.17f, 0.20f, 0.25f, 1f);
    public static readonly Vector4 Blue = new(0.44f, 0.71f, 1f, 1f);

    private static uint U32(Vector4 c) => ImGui.GetColorU32(c);
    private static Vector4 Alpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    /// <summary>A rounded chip with a status dot, e.g. a plugin. Hover text explains it.</summary>
    public static void Chip(string text, bool ok, string tooltip)
    {
        var color = ok ? Good : Bad;
        var size = ImGui.CalcTextSize(text);
        var pad = new Vector2(8, 2);
        var dotSpace = 13f;
        var full = new Vector2(size.X + pad.X * 2 + dotSpace, size.Y + pad.Y * 2);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + full, U32(ok ? new Vector4(0.13f, 0.15f, 0.18f, 1f) : new Vector4(0.17f, 0.11f, 0.13f, 1f)), full.Y / 2);
        dl.AddRect(pos, pos + full, U32(ok ? new Vector4(0.2f, 0.23f, 0.28f, 1f) : Alpha(Bad, 0.45f)), full.Y / 2);
        var c = pos + new Vector2(pad.X + 4, full.Y / 2);
        dl.AddCircleFilled(c, 6f, U32(Alpha(color, 0.25f)));
        dl.AddCircleFilled(c, 3.6f, U32(color));
        dl.AddText(pos + new Vector2(pad.X + dotSpace, pad.Y), U32(ok ? new Vector4(0.9f, 0.91f, 0.93f, 1f) : new Vector4(0.75f, 0.68f, 0.7f, 1f)), text);
        ImGui.Dummy(full);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
    }

    /// <summary>Width a <see cref="Chip"/> will take (to right-align a row of them).</summary>
    public static float ChipWidth(string text) => ImGui.CalcTextSize(text).X + 16 + 13;

    /// <summary>An on/off switch. Returns true when clicked (value already flipped).</summary>
    public static bool Toggle(string label, ref bool value, string? help = null)
    {
        var h = ImGui.GetTextLineHeight();
        var size = new Vector2(h * 1.9f, h * 1.05f);
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0, (ImGui.GetTextLineHeightWithSpacing() - size.Y) / 2 - 1);
        ImGui.PushID(label);
        var clicked = ImGui.InvisibleButton("##tg", new Vector2(size.X, ImGui.GetTextLineHeightWithSpacing()));
        ImGui.PopID();
        if (clicked) value = !value;
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var r = size.Y / 2;
        var bg = value ? new Vector4(0.18f, 0.49f, 0.27f, 1f) : new Vector4(0.23f, 0.25f, 0.29f, 1f);
        if (hovered) bg = new Vector4(bg.X * 1.15f, bg.Y * 1.15f, bg.Z * 1.15f, 1f);
        dl.AddRectFilled(pos, pos + size, U32(bg), r);
        var knob = new Vector2(value ? pos.X + size.X - r : pos.X + r, pos.Y + r);
        dl.AddCircleFilled(knob, r - 2.5f, U32(value ? new Vector4(1, 1, 1, 1) : new Vector4(0.8f, 0.82f, 0.86f, 1f)));
        ImGui.SameLine(0, 8);
        ImGui.TextUnformatted(label);
        if (ImGui.IsItemClicked()) { value = !value; clicked = true; }
        if (help != null) Help(help);
        return clicked;
    }

    /// <summary>Thin rounded progress bar; green when full.</summary>
    public static void Bar(float fraction, float width = -1, float height = 5f, Vector4? color = null)
    {
        fraction = Clamp01(fraction);
        if (width < 0) width = AvailX();
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(width, height), U32(Track), height / 2);
        if (fraction > 0)
            dl.AddRectFilled(pos, pos + new Vector2(Math.Max(height, width * fraction), height),
                             U32(color ?? (fraction >= 1f ? Good : Blue)), height / 2);
        ImGui.Dummy(new Vector2(width, height + 2));
    }

    public enum StepMark { Pending, Active, Done, Failed }

    /// <summary>Horizontal step track (Farm — Reduce — Buy …) with dots and labels.</summary>
    public static void StepTrack(IReadOnlyList<(string Label, StepMark Mark)> steps)
    {
        if (steps.Count == 0) return;
        var width = ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0, 7);
        var dl = ImGui.GetWindowDrawList();
        var seg = width / steps.Count;
        dl.AddLine(pos, pos + new Vector2(width, 0), U32(Track), 3f);
        for (var i = 0; i < steps.Count; i++)
        {
            var (label, mark) = steps[i];
            var cx = pos.X + seg * i + seg / 2;
            var col = mark switch { StepMark.Done => Good, StepMark.Active => Warn, StepMark.Failed => Bad, _ => Track };
            if (mark is StepMark.Done or StepMark.Active or StepMark.Failed)
                dl.AddLine(new Vector2(pos.X + seg * i, pos.Y), new Vector2(mark == StepMark.Done ? pos.X + seg * (i + 1) : cx, pos.Y), U32(mark == StepMark.Done ? Good : col), 3f);
            if (mark == StepMark.Active) dl.AddCircleFilled(new Vector2(cx, pos.Y), 9f, U32(Alpha(Warn, 0.25f)));
            dl.AddCircleFilled(new Vector2(cx, pos.Y), 5.5f, U32(col));
            dl.AddCircle(new Vector2(cx, pos.Y), 6.5f, U32(new Vector4(0.08f, 0.09f, 0.11f, 1f)), 0, 2f);
            var ts = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(cx - ts.X / 2, pos.Y + 11), U32(mark == StepMark.Pending ? Muted : new Vector4(0.9f, 0.91f, 0.93f, 1f)), label);
        }
        ImGui.Dummy(new Vector2(width, 14 + ImGui.GetTextLineHeight() + 4));
    }

    /// <summary>Circular progress ring with the percentage inside.</summary>
    public static void Ring(float fraction, float radius = 19f)
    {
        fraction = Clamp01(fraction);
        var pos = ImGui.GetCursorScreenPos();
        var c = pos + new Vector2(radius, radius);
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircle(c, radius - 3, U32(Track), 48, 5f);
        if (fraction > 0)
        {
            var start = -MathF.PI / 2;
            dl.PathArcTo(c, radius - 3, start, start + MathF.PI * 2 * fraction, 48);
            dl.PathStroke(U32(fraction >= 1 ? Good : Good), ImDrawFlags.None, 5f);
        }
        var text = $"{(int)(fraction * 100)}%";
        var ts = ImGui.CalcTextSize(text);
        dl.AddText(c - ts / 2, U32(new Vector4(0.92f, 0.93f, 0.95f, 1f)), text);
        ImGui.Dummy(new Vector2(radius * 2, radius * 2));
    }

    /// <summary>Step marker for lists: ✓ done, glowing ring for current, empty ring for pending, red for failed.</summary>
    public static void CheckCircle(StepMark mark)
    {
        var h = ImGui.GetTextLineHeight();
        var r = h * 0.42f;
        var pos = ImGui.GetCursorScreenPos();
        var c = pos + new Vector2(r + 1, h / 2 + 1);
        var dl = ImGui.GetWindowDrawList();
        switch (mark)
        {
            case StepMark.Done:
                dl.AddCircleFilled(c, r, U32(new Vector4(0.18f, 0.49f, 0.27f, 1f)));
                dl.AddLine(c + new Vector2(-r * 0.45f, 0), c + new Vector2(-r * 0.1f, r * 0.38f), U32(new Vector4(1, 1, 1, 1)), 1.8f);
                dl.AddLine(c + new Vector2(-r * 0.1f, r * 0.38f), c + new Vector2(r * 0.5f, -r * 0.35f), U32(new Vector4(1, 1, 1, 1)), 1.8f);
                break;
            case StepMark.Active:
                dl.AddCircleFilled(c, r + 3, U32(Alpha(Warn, 0.2f)));
                dl.AddCircle(c, r - 1, U32(Warn), 0, 2.4f);
                break;
            case StepMark.Failed:
                dl.AddCircleFilled(c, r, U32(Bad));
                dl.AddLine(c - new Vector2(r * 0.4f, r * 0.4f), c + new Vector2(r * 0.4f, r * 0.4f), U32(new Vector4(1, 1, 1, 1)), 1.8f);
                dl.AddLine(c + new Vector2(-r * 0.4f, r * 0.4f), c + new Vector2(r * 0.4f, -r * 0.4f), U32(new Vector4(1, 1, 1, 1)), 1.8f);
                break;
            default:
                dl.AddCircle(c, r - 1, U32(new Vector4(0.25f, 0.27f, 0.31f, 1f)), 0, 2f);
                break;
        }
        ImGui.Dummy(new Vector2(r * 2 + 4, h));
    }

    /// <summary>The game's icon for an item (a grey square while it loads).</summary>
    public static void ItemIcon(uint iconId, float size)
    {
        if (iconId != 0)
        {
            try
            {
                var wrap = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                ImGui.Image(wrap.Handle, new Vector2(size, size));
                return;
            }
            catch { /* fall through to the placeholder */ }
        }
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(pos, pos + new Vector2(size, size), U32(Track), 4f);
        ImGui.Dummy(new Vector2(size, size));
    }

    /// <summary>The FateRunner "F" logo tile (FATE orange).</summary>
    public static void Logo(float size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilledMultiColor(pos, pos + new Vector2(size, size),
            U32(new Vector4(1f, 0.72f, 0.3f, 1f)), U32(new Vector4(1f, 0.45f, 0.35f, 1f)),
            U32(new Vector4(1f, 0.45f, 0.35f, 1f)), U32(new Vector4(1f, 0.72f, 0.3f, 1f)));
        var ts = ImGui.CalcTextSize("F");
        dl.AddText(pos + new Vector2(size, size) / 2 - ts / 2, U32(new Vector4(0.13f, 0.07f, 0.05f, 1f)), "F");
        ImGui.Dummy(new Vector2(size, size));
    }

    /// <summary>A rounded card with a coloured title, for settings groups.</summary>
    public static void CardTitle(string title)
    {
        ImGui.TextColored(new Vector4(0.55f, 0.77f, 1f, 1f), title);
        ImGui.Dummy(new Vector2(0, 2));
    }

    private static readonly Stack<(Vector2 Start, float Width, Vector4 Bg, Vector4 Edge)> panels = new();
    private static readonly Vector2 PanelPad = new(11, 9);

    /// <summary>
    /// Start of a rounded panel that grows to fit what's drawn inside (drawn behind the content afterwards, so no
    /// fixed height). <paramref name="width"/> 0 = full width. Always pair with <see cref="EndPanel"/>.
    /// </summary>
    public static void BeginPanel(float width = 0, Vector4? bg = null, Vector4? edge = null)
    {
        if (width <= 0) width = ImGui.GetContentRegionAvail().X;
        var start = ImGui.GetCursorScreenPos();
        panels.Push((start, width, bg ?? CardBg, edge ?? Border));
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(start + PanelPad);
        ImGui.BeginGroup();
    }

    /// <summary>
    /// Width from the cursor to the right edge of where content may go: the inside of the current panel, or the
    /// window's content area outside panels. Use this (not GetContentRegionAvail) to right-align inside panels.
    /// </summary>
    public static float AvailX()
    {
        var x = ImGui.GetCursorScreenPos().X;
        if (panels.Count > 0)
        {
            var (start, width, _, _) = panels.Peek();
            return Math.Max(0, start.X + width - PanelPad.X - x);
        }
        return ImGui.GetContentRegionAvail().X;
    }

    public static void EndPanel()
    {
        var (start, width, bg, edge) = panels.Pop();
        ImGui.EndGroup();
        var max = ImGui.GetItemRectMax();
        var end = new Vector2(start.X + width, max.Y + PanelPad.Y);
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(start, end, U32(bg), 10f);
        dl.AddRect(start, end, U32(edge), 10f);
        dl.ChannelsMerge();
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(end - start);
    }

    /// <summary>Panel inner width (for laying out inside a panel).</summary>
    public static float PanelInner(float width) => width - PanelPad.X * 2;

    /// <summary>Section title inside a panel: a numbered badge, the title, and a muted note on the right.</summary>
    public static void StepHeader(int number, string title, string right = "")
    {
        var h = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var r = h * 0.55f;
        var c = pos + new Vector2(r, h / 2);
        dl.AddCircleFilled(c, r, U32(new Vector4(0.15f, 0.21f, 0.31f, 1f)));
        var n = number.ToString();
        var ns = ImGui.CalcTextSize(n);
        dl.AddText(c - ns / 2, U32(Blue), n);
        ImGui.Dummy(new Vector2(r * 2, h));
        ImGui.SameLine(0, 8);
        ImGui.TextColored(new Vector4(0.9f, 0.91f, 0.93f, 1f), title);
        if (right.Length > 0)
        {
            // Measure on the same line as the title, then push the note to the right edge.
            ImGui.SameLine(0, 8);
            var w = ImGui.CalcTextSize(right).X;
            var gap = AvailX() - w;
            if (gap > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + gap);
            ImGui.TextColored(Muted, right);
        }
        ImGui.Dummy(new Vector2(0, 1));
    }

    /// <summary>
    /// An item line: small game icon, name, a right-aligned value (e.g. "4 / 10"), an optional thin progress bar
    /// under the name, and an optional muted note under that.
    /// </summary>
    public static void ItemRow(uint iconId, string name, string right, Vector4? rightColor = null, float? fraction = null, string? note = null)
    {
        var h = ImGui.GetTextLineHeight();
        var width = AvailX();
        var start = ImGui.GetCursorPosX();
        ItemIcon(iconId, h + 4);
        ImGui.SameLine(0, 8);
        ImGui.BeginGroup();
        var nameX = ImGui.GetCursorPosX();
        var rightW = ImGui.CalcTextSize(right).X;
        var nameMax = width - (nameX - start) - rightW - 12;
        var shown = name;
        while (shown.Length > 4 && ImGui.CalcTextSize(shown).X > nameMax) shown = shown[..^2] + "…";
        ImGui.TextUnformatted(shown);
        if (right.Length > 0)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(start + width - rightW);
            ImGui.TextColored(rightColor ?? Muted, right);
        }
        if (fraction is { } f) Bar(f, width - (nameX - start), 3f);
        if (!string.IsNullOrEmpty(note))
        {
            ImGui.PushTextWrapPos(start + width);
            ImGui.TextColored(Muted, note);
            ImGui.PopTextWrapPos();
        }
        ImGui.EndGroup();
    }

    /// <summary>A muted line with a coloured lead dot, for notes and warnings.</summary>
    public static void Note(string text, Vector4 color)
    {
        Dot(color, 3f);
        ImGui.SameLine(0, 6);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + AvailX());
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>The FateRunner window look (rounded frames, dark background). Pair with <see cref="PopWindowStyle"/>.</summary>
    public static void PushWindowStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.086f, 0.094f, 0.114f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.21f, 0.31f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.29f, 0.42f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.14f, 0.16f, 0.2f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.12f, 0.16f, 0.24f, 1f));
    }

    public static void PopWindowStyle()
    {
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(4);
    }
}
