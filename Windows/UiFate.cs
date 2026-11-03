using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using FateRunner.Core;

namespace FateRunner.Windows;

/// <summary>The FateRunner-specific pieces of the look: ring, mode buttons, tabs, stat boxes, FATE cards, zone tiles.</summary>
public static partial class Ui
{
    public static readonly Vector4 Fate = new(1f, 0.624f, 0.29f, 1f);
    public static readonly Vector4 Fate2 = new(1f, 0.42f, 0.34f, 1f);
    public static readonly Vector4 Card2 = new(0.125f, 0.141f, 0.172f, 1f);
    public static readonly Vector4 TextBright = new(0.925f, 0.933f, 0.95f, 1f);
    public static readonly Vector4 Dim = new(0.42f, 0.44f, 0.48f, 1f);
    public static readonly Vector4 HeroBg = new(0.135f, 0.11f, 0.095f, 1f);
    public static readonly Vector4 HeroEdge = new(0.23f, 0.165f, 0.115f, 1f);

    private static uint C(Vector4 c) => ImGui.GetColorU32(c);
    private static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

    /// <summary>Each expansion's colour (zone tiles and headers).</summary>
    public static Vector4 ExpansionColor(string expansion) => expansion switch
    {
        "A Realm Reborn" => new Vector4(0.45f, 0.72f, 1f, 1f),
        "Heavensward" => new Vector4(0.55f, 0.66f, 0.9f, 1f),
        "Stormblood" => new Vector4(0.95f, 0.42f, 0.45f, 1f),
        "Shadowbringers" => new Vector4(0.61f, 0.48f, 1f, 1f),
        "Endwalker" => new Vector4(0.235f, 0.81f, 0.77f, 1f),
        "Dawntrail" => new Vector4(1f, 0.81f, 0.35f, 1f),
        _ => Fate,
    };

    public static Vector4 KindColor(FateKind k) => k switch
    {
        FateKind.Kill => Bad,
        FateKind.Boss => Fate,
        FateKind.HandIn => Blue,
        FateKind.Escort => new Vector4(0.235f, 0.81f, 0.77f, 1f),
        FateKind.Defend => new Vector4(0.61f, 0.48f, 1f, 1f),
        _ => Muted,
    };

    public static FontAwesomeIcon KindIcon(FateKind k) => k switch
    {
        FateKind.Kill => FontAwesomeIcon.Crosshairs,
        FateKind.Boss => FontAwesomeIcon.Skull,
        FateKind.HandIn => FontAwesomeIcon.HandHolding,
        FateKind.Escort => FontAwesomeIcon.Walking,
        FateKind.Defend => FontAwesomeIcon.ShieldAlt,
        _ => FontAwesomeIcon.Star,
    };

    /// <summary>Friendly name and colour for the engine phase.</summary>
    public static (string Text, Vector4 Color) PhaseLabel(RunPhase phase, bool running) => phase switch
    {
        RunPhase.ToZone => ("Heading to zone", Blue),
        RunPhase.Choosing => ("Choosing a FATE", Warn),
        RunPhase.Travel => ("Travelling", Blue),
        RunPhase.StartNpc => ("Starting FATE", Warn),
        RunPhase.InFate => ("In a FATE", Good),
        RunPhase.AfterFate => ("Wrapping up", Good),
        RunPhase.Dead => ("Knocked out", Bad),
        RunPhase.Done => ("Done", Good),
        RunPhase.Failed => ("Stopped", Bad),
        _ => ("Idle", Muted),
    };

    /// <summary>Shortens text with "…" to fit a width.</summary>
    public static string Fit(string text, float maxWidth)
    {
        if (maxWidth <= 0) return "";
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;
        var s = text;
        while (s.Length > 1 && ImGui.CalcTextSize(s + "…").X > maxWidth) s = s[..^1];
        return s.TrimEnd() + "…";
    }

    public static string Clock(long seconds) => seconds >= 3600 ? $"{seconds / 3600}:{seconds % 3600 / 60:D2}:{seconds % 60:D2}" : $"{seconds / 60}:{seconds % 60:D2}";

    /// <summary>Pill that's drawn (not laid out) at a spot; returns its width. Used for tags inside cards.</summary>
    public static float TagAt(ImDrawListPtr dl, Vector2 pos, string text, Vector4 color, bool rightAligned = false)
    {
        var ts = ImGui.CalcTextSize(text);
        var w = ts.X + 12;
        if (rightAligned) pos.X -= w;
        dl.AddRectFilled(pos, pos + new Vector2(w, ts.Y + 2), C(new Vector4(color.X, color.Y, color.Z, 0.16f)), (ts.Y + 2) / 2);
        dl.AddText(pos + new Vector2(6, 1), C(color), text);
        return w;
    }

    /// <summary>Phase pill with a glowing dot (laid out at the cursor).</summary>
    public static void PhasePill(string text, Vector4 color)
    {
        var ts = ImGui.CalcTextSize(text);
        var size = new Vector2(ts.X + 28, ts.Y + 4);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, C(new Vector4(color.X, color.Y, color.Z, 0.15f)), size.Y / 2);
        var dot = pos + new Vector2(11, size.Y / 2);
        dl.AddCircleFilled(dot, 6f, C(new Vector4(color.X, color.Y, color.Z, 0.25f)));
        dl.AddCircleFilled(dot, 3.5f, C(color));
        dl.AddText(pos + new Vector2(20, 2), C(color), text);
        ImGui.Dummy(size);
    }

    /// <summary>A FATE-kind badge: tinted rounded square with the kind's icon.</summary>
    public static void KindBadgeAt(ImDrawListPtr dl, Vector2 pos, float size, FateKind kind)
    {
        var col = KindColor(kind);
        dl.AddRectFilled(pos, pos + new Vector2(size, size), C(new Vector4(col.X, col.Y, col.Z, 0.2f)), size * 0.27f);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var icon = KindIcon(kind).ToIconString();
            var ts = ImGui.CalcTextSize(icon);
            var scale = Math.Min(1f, size * 0.62f / Math.Max(ts.X, ts.Y));
            if (scale < 1f)
            {
                // Smaller badges: draw the icon at a reduced font size.
                var fs = ImGui.GetFontSize() * scale;
                dl.AddText(ImGui.GetFont(), fs, pos + new Vector2(size, size) / 2 - ts * scale / 2, C(col), icon);
            }
            else dl.AddText(pos + new Vector2(size, size) / 2 - ts / 2, C(col), icon);
        }
    }

    /// <summary>Rounded progress bar with a FATE-orange gradient (or a solid colour), drawn at a spot.</summary>
    public static void GradientBarAt(ImDrawListPtr dl, Vector2 pos, float width, float height, float fraction, Vector4? a = null, Vector4? b = null)
    {
        fraction = Clamp01(fraction);
        dl.AddRectFilled(pos, pos + new Vector2(width, height), C(Track), height / 2);
        if (fraction <= 0) return;
        var w = Math.Max(height, width * fraction);
        var ca = a ?? Fate;
        var cb = b ?? (a ?? Fate2);
        // Rounded ends in the two colours, a gradient body between them.
        dl.AddRectFilled(pos, pos + new Vector2(Math.Min(w, height * 2), height), C(ca), height / 2);
        dl.AddRectFilled(pos + new Vector2(Math.Max(0, w - height * 2), 0), pos + new Vector2(w, height), C(Lerp(ca, cb, 1f)), height / 2);
        if (w > height * 2)
            dl.AddRectFilledMultiColor(pos + new Vector2(height / 2, 0), pos + new Vector2(w - height / 2, height), C(ca), C(cb), C(cb), C(ca));
    }

    /// <summary>Gradient bar laid out at the cursor.</summary>
    public static void GradientBar(float fraction, float width = -1, float height = 7f, Vector4? a = null, Vector4? b = null)
    {
        if (width < 0) width = AvailX();
        GradientBarAt(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), width, height, fraction, a, b);
        ImGui.Dummy(new Vector2(width, height + 1));
    }

    private static readonly Dictionary<uint, uint> itemIcons = new();

    /// <summary>The icon id of an item (cached).</summary>
    public static uint IconOfItem(uint itemId)
    {
        if (itemIcons.TryGetValue(itemId, out var icon)) return icon;
        icon = 0;
        try { icon = (uint)(Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRowOrDefault(itemId)?.Icon ?? 0); } catch { }
        itemIcons[itemId] = icon;
        return icon;
    }

    private static void IconAt(ImDrawListPtr dl, uint iconId, Vector2 pos, float size)
    {
        if (iconId == 0) return;
        try
        {
            var wrap = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            dl.AddImage(wrap.Handle, pos, pos + new Vector2(size, size));
        }
        catch { /* icon not loaded yet */ }
    }

    /// <summary>
    /// Big progress ring (orange gradient) with a large value in the middle and a small line under it, optionally
    /// led by a game icon (e.g. the gemstone).
    /// </summary>
    public static void ProgressRing(float fraction, float radius, string big, string small, uint smallIcon = 0, Vector4? from = null, Vector4? to = null)
    {
        fraction = Clamp01(fraction);
        const float thick = 10f;
        var pos = ImGui.GetCursorScreenPos();
        var c = pos + new Vector2(radius, radius);
        var r = radius - thick / 2;
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircle(c, r, C(Track), 64, thick);
        var a = from ?? Fate;
        var b = to ?? Fate2;
        if (fraction > 0)
        {
            var start = -MathF.PI / 2;
            var segs = Math.Max(2, (int)(60 * fraction));
            for (var i = 0; i < segs; i++)
            {
                var a0 = start + MathF.PI * 2 * fraction * i / segs;
                var a1 = start + MathF.PI * 2 * fraction * (i + 1) / segs + 0.01f;
                dl.PathArcTo(c, r, a0, a1, 3);
                dl.PathStroke(C(Lerp(a, b, i / (float)segs)), ImDrawFlags.None, thick);
            }
            // Round caps.
            dl.AddCircleFilled(c + new Vector2(MathF.Cos(start), MathF.Sin(start)) * r, thick / 2, C(a));
            var end = start + MathF.PI * 2 * fraction;
            dl.AddCircleFilled(c + new Vector2(MathF.Cos(end), MathF.Sin(end)) * r, thick / 2, C(b));
        }

        ImGui.SetWindowFontScale(1.65f);
        var bs = ImGui.CalcTextSize(big);
        dl.AddText(c - new Vector2(bs.X / 2, bs.Y * 0.78f), C(TextBright), big);
        ImGui.SetWindowFontScale(1f);
        var ss = ImGui.CalcTextSize(small);
        var iconSize = smallIcon != 0 ? ss.Y : 0;
        var gap = smallIcon != 0 ? 3f : 0f;
        var sp = new Vector2(c.X - (ss.X + iconSize + gap) / 2, c.Y + bs.Y * 0.22f + 1);
        IconAt(dl, smallIcon, sp, iconSize);
        dl.AddText(sp + new Vector2(iconSize + gap, 0), C(Muted), small);
        ImGui.Dummy(new Vector2(radius * 2, radius * 2));
    }

    /// <summary>Segmented buttons (one of N), e.g. the run mode. Returns true when the choice changed.</summary>
    public static bool Segmented(string id, IReadOnlyList<string> labels, ref int selected, float width, float height)
    {
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(width, height), C(CardBg), 9f);
        dl.AddRect(pos, pos + new Vector2(width, height), C(Border), 9f);
        var n = labels.Count;
        var segW = (width - 6 - 3 * (n - 1)) / n;
        var changed = false;
        for (var i = 0; i < n; i++)
        {
            var p = pos + new Vector2(3 + i * (segW + 3), 3);
            var size = new Vector2(segW, height - 6);
            ImGui.SetCursorScreenPos(p);
            if (ImGui.InvisibleButton($"{id}{i}", size) && selected != i) { selected = i; changed = true; }
            var hovered = ImGui.IsItemHovered();
            var on = selected == i;
            if (on)
            {
                dl.AddRectFilled(p, p + size, C(new Vector4(0.173f, 0.133f, 0.098f, 1f)), 7f);
                dl.AddRect(p, p + size, C(new Vector4(0.29f, 0.204f, 0.125f, 1f)), 7f);
            }
            else if (hovered) dl.AddRectFilled(p, p + size, C(new Vector4(1, 1, 1, 0.04f)), 7f);
            var ts = ImGui.CalcTextSize(labels[i]);
            dl.AddText(p + size / 2 - ts / 2, C(on ? Fate : hovered ? TextBright : Muted), labels[i]);
        }
        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, height));
        return changed;
    }

    /// <summary>A row of tabs with an orange underline on the open one, and an optional small count per tab.</summary>
    public static void TabStrip(string id, IReadOnlyList<(string Label, string Count)> tabs, ref int selected)
    {
        var width = AvailX();
        var pos = ImGui.GetCursorScreenPos();
        var h = ImGui.GetTextLineHeight() + 12;
        var dl = ImGui.GetWindowDrawList();
        dl.AddLine(pos + new Vector2(0, h), pos + new Vector2(width, h), C(Border), 1f);
        var x = pos.X;
        for (var i = 0; i < tabs.Count; i++)
        {
            var (label, count) = tabs[i];
            var ls = ImGui.CalcTextSize(label);
            var cs = count.Length > 0 ? ImGui.CalcTextSize(count) : Vector2.Zero;
            var w = ls.X + (count.Length > 0 ? cs.X * 0.9f + 6 : 0) + 22;
            var p = new Vector2(x, pos.Y);
            ImGui.SetCursorScreenPos(p);
            if (ImGui.InvisibleButton($"{id}{i}", new Vector2(w, h))) selected = i;
            var hovered = ImGui.IsItemHovered();
            var on = selected == i;
            dl.AddText(p + new Vector2(11, 6), C(on ? TextBright : hovered ? new Vector4(0.8f, 0.81f, 0.84f, 1f) : Muted), label);
            if (count.Length > 0)
            {
                ImGui.SetWindowFontScale(0.9f);
                dl.AddText(p + new Vector2(11 + ls.X + 5, 7), C(Dim), count);
                ImGui.SetWindowFontScale(1f);
            }
            if (on) dl.AddRectFilled(p + new Vector2(4, h - 2), p + new Vector2(w - 4, h + 0.5f), C(Fate), 1.5f);
            x += w + 2;
        }
        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, h + 4));
    }

    /// <summary>A small box with a big value and a label under it (the stat strip).</summary>
    public static void StatBox(string value, string label, float width, Vector4? valueColor = null)
    {
        var h = ImGui.GetTextLineHeight();
        var height = h * 2.35f + 12;
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(width, height), C(CardBg), 9f);
        dl.AddRect(pos, pos + new Vector2(width, height), C(Border), 9f);
        ImGui.SetWindowFontScale(1.22f);
        dl.AddText(pos + new Vector2(10, 6), C(valueColor ?? TextBright), Fit(value, width - 16));
        var vh = ImGui.GetTextLineHeight();
        ImGui.SetWindowFontScale(0.9f);
        dl.AddText(pos + new Vector2(10, 6 + vh + 1), C(Muted), Fit(label, width - 16));
        ImGui.SetWindowFontScale(1f);
        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>
    /// The current FATE as a card: kind badge, name, tags (bonus / synced), gradient progress bar and, unless compact,
    /// a line with the kind and time left.
    /// </summary>
    public static void FateCard(FateInfo f, bool synced, float width, bool compact)
    {
        var h = ImGui.GetTextLineHeight();
        var pad = compact ? 7f : 9f;
        var badge = compact ? h + 4 : h + 9;
        var barH = compact ? 5f : 7f;
        var height = pad * 2 + badge + 7 + barH + (compact ? 0 : 5 + h);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(width, height), C(Card2), 9f);
        dl.AddRect(pos, pos + new Vector2(width, height), C(Border), 9f);

        var p = pos + new Vector2(pad, pad);
        KindBadgeAt(dl, p, badge, f.Kind);

        // Tags from the right edge.
        var right = pos.X + width - pad;
        var tagY = p.Y + badge / 2 - (h + 2) / 2;
        if (compact)
        {
            right -= TagAt(dl, new Vector2(right, tagY), Clock(f.SecondsLeft), Muted, true) + 4;
            right -= TagAt(dl, new Vector2(right, tagY), $"{f.Progress}%", Muted, true) + 4;
        }
        else
        {
            if (synced) right -= TagAt(dl, new Vector2(right, tagY), $"synced Lv{f.MaxLevel}", Good, true) + 4;
            if (f.Bonus) right -= TagAt(dl, new Vector2(right, tagY), "bonus", Warn, true) + 4;
        }
        var nameX = p.X + badge + 8;
        dl.AddText(new Vector2(nameX, p.Y + badge / 2 - h / 2), C(TextBright), Fit(f.Name, right - nameX - 4));

        var barY = p.Y + badge + 7;
        GradientBarAt(dl, new Vector2(p.X, barY), width - pad * 2, barH, f.Progress / 100f);
        if (!compact)
        {
            var metaY = barY + barH + 5;
            dl.AddText(new Vector2(p.X, metaY), C(Muted), $"{char.ToUpper(FateEngine.KindName(f.Kind)[0])}{FateEngine.KindName(f.Kind)[1..]} · {f.Progress}%");
            var left = f.Preparing ? "not started" : $"{Clock(f.SecondsLeft)} left";
            var ls = ImGui.CalcTextSize(left);
            dl.AddText(new Vector2(pos.X + width - pad - ls.X, metaY), C(f.SecondsLeft < 120 && !f.Preparing ? Warn : Muted), left);
        }
        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>One line of the "FATEs here" list.</summary>
    public static void FateListRow(FateEngine.FateRow row, float width)
    {
        var f = row.Fate;
        var h = ImGui.GetTextLineHeight();
        var badge = h + 8;
        var height = Math.Max(badge, h * 2 + 2) + 12;
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var a = row.Skipped ? 0.5f : 1f;
        Vector4 A(Vector4 v) => new(v.X, v.Y, v.Z, v.W * a);

        var bg = row.IsCurrent ? new Vector4(0.137f, 0.11f, 0.086f, 1f) : CardBg;
        var edge = row.IsCurrent ? new Vector4(0.353f, 0.24f, 0.133f, 1f) : row.IsNext ? new Vector4(0.25f, 0.27f, 0.31f, 1f) : Border;
        dl.AddRectFilled(pos, pos + new Vector2(width, height), C(A(bg)), 9f);
        dl.AddRect(pos, pos + new Vector2(width, height), C(A(edge)), 9f);

        var p = pos + new Vector2(9, height / 2 - badge / 2);
        if (row.Skipped) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
        KindBadgeAt(dl, p, badge, f.Kind);

        const float timeW = 46f, barW = 66f;
        var timeX = pos.X + width - 10 - timeW;
        var barX = timeX - 10 - barW;
        var nameX = p.X + badge + 9;
        var textTop = pos.Y + height / 2 - (h * 2 + 2) / 2;
        dl.AddText(new Vector2(nameX, textTop), C(TextBright), Fit(f.Name, barX - nameX - 8));
        ImGui.SetWindowFontScale(0.9f);
        var whyColor = row.IsCurrent ? Fate : row.IsNext ? Good : Dim;
        dl.AddText(new Vector2(nameX, textTop + h + 2), C(whyColor), Fit(row.Why, barX - nameX - 8));
        ImGui.SetWindowFontScale(1f);

        GradientBarAt(dl, new Vector2(barX, pos.Y + height / 2 - 2.5f), barW, 5f, f.Progress / 100f, Fate, Fate);
        var t = f.Preparing ? "start" : Clock(f.SecondsLeft);
        var ts = ImGui.CalcTextSize(t);
        dl.AddText(new Vector2(timeX + timeW - ts.X, pos.Y + height / 2 - ts.Y / 2), C(f.SecondsLeft < 120 && !f.Preparing ? Warn : Muted), t);
        if (row.Skipped) ImGui.PopStyleVar();

        ImGui.Dummy(new Vector2(width, height));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{f.Name}\nLv{f.Level} · {FateEngine.KindName(f.Kind)} · {f.Progress}% · {row.Distance:0}y away{(f.Bonus ? " · bonus" : "")}");
    }

    /// <summary>
    /// A selectable zone tile in its expansion's colour: tinted with a ✓ when picked, green outline while farming it,
    /// dimmed (with the reason) when you can't use it. Returns true when clicked (and usable).
    /// </summary>
    public static bool ZoneTile(string id, string name, string levels, string? note, Vector4 color, bool selected, bool available,
                                bool here, bool active, float width)
    {
        var h = ImGui.GetTextLineHeight();
        var height = h * 2 + 18;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var a = available ? 1f : 0.45f;
        Vector4 A(Vector4 v, float mul = 1f) => new(v.X, v.Y, v.Z, v.W * a * mul);

        var end = pos + new Vector2(width, height);
        if (selected)
        {
            var tint = Lerp(CardBg, color, 0.2f);
            dl.AddRectFilledMultiColor(pos + new Vector2(4, 0), end - new Vector2(4, 0), C(A(tint)), C(A(CardBg)), C(A(CardBg)), C(A(tint)));
            // Rounded ends so the gradient sits inside the rounded tile.
            dl.AddRectFilled(pos, new Vector2(pos.X + 9, end.Y), C(A(tint)), 9f, ImDrawFlags.RoundCornersLeft);
            dl.AddRectFilled(new Vector2(end.X - 9, pos.Y), end, C(A(CardBg)), 9f, ImDrawFlags.RoundCornersRight);
        }
        else dl.AddRectFilled(pos, end, C(A(CardBg)), 9f);
        if (hovered && available) dl.AddRectFilled(pos, end, C(new Vector4(1, 1, 1, 0.035f)), 9f);

        var edge = active ? Good : selected ? Lerp(Border, color, 0.55f) : Border;
        dl.AddRect(pos, end, C(A(edge)), 9f, ImDrawFlags.None, active ? 2f : 1f);
        dl.AddRectFilled(pos + new Vector2(0, 0), new Vector2(pos.X + 4, end.Y), C(A(color, selected ? 1f : 0.25f)), 9f, ImDrawFlags.RoundCornersLeft);

        var tx = pos.X + 14;
        var maxW = width - 14 - (selected ? 34 : 12);
        dl.AddText(new Vector2(tx, pos.Y + 7), C(A(TextBright)), Fit(name, maxW));

        // Level chip, then "farming here" / "here" / the reason it can't be used.
        ImGui.SetWindowFontScale(0.9f);
        var ly = pos.Y + 9 + h;
        var lw = TagAt(dl, new Vector2(tx, ly), levels, A(TextBright, 0.85f));
        var nx = tx + lw + 6;
        var (extra, extraColor) = active ? ("● farming here", Good) : here ? ("● you're here", Good) : (note ?? "", Muted);
        if (extra.Length > 0) dl.AddText(new Vector2(nx, ly + 1), C(A(extraColor)), Fit(extra, pos.X + maxW + 14 - nx));
        ImGui.SetWindowFontScale(1f);

        if (selected)
        {
            var c = new Vector2(end.X - 18, pos.Y + height / 2);
            dl.AddCircleFilled(c, 9f, C(A(color)));
            var ink = C(A(new Vector4(0.1f, 0.07f, 0.03f, 1f)));
            dl.AddLine(c + new Vector2(-4.2f, 0.2f), c + new Vector2(-1.2f, 3.2f), ink, 2f);
            dl.AddLine(c + new Vector2(-1.2f, 3.2f), c + new Vector2(4.2f, -3f), ink, 2f);
        }
        if (hovered && note is { Length: > 0 } && !available) ImGui.SetTooltip(note);
        return clicked && available;
    }

    /// <summary>An expansion header: colour bar, name, a muted count, and room on the right for buttons.</summary>
    public static void ExpansionHeader(string expansion, string count)
    {
        var h = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos + new Vector2(0, 1), pos + new Vector2(4, h + 1), C(ExpansionColor(expansion)), 2f);
        ImGui.Dummy(new Vector2(6, h));
        ImGui.SameLine(0, 6);
        ImGui.TextColored(TextBright, expansion);
        ImGui.SameLine(0, 8);
        ImGui.TextColored(Muted, count);
    }

    /// <summary>A small flat button (All / None).</summary>
    public static bool MiniButton(string label)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.133f, 0.149f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.19f, 0.21f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
        return clicked;
    }

    /// <summary>Width of a <see cref="MiniButton"/>.</summary>
    public static float MiniButtonWidth(string label)
    {
        var l = label.Contains("##") ? label[..label.IndexOf("##", StringComparison.Ordinal)] : label;
        return ImGui.CalcTextSize(l).X + 16;
    }

    /// <summary>The big Start / Stop button (gradient fill, soft glow).</summary>
    public static bool BigButton(string id, string label, Vector4 color, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        var dl = ImGui.GetWindowDrawList();
        var top = hovered ? Lerp(color, new Vector4(1, 1, 1, 1), 0.12f) : color;
        var bottom = Lerp(color, new Vector4(0, 0, 0, 1), held ? 0.4f : 0.28f);
        dl.AddRectFilled(pos + new Vector2(0, 3), pos + size + new Vector2(0, 3), C(new Vector4(color.X, color.Y, color.Z, 0.18f)), 10f);
        dl.AddRectFilled(pos, pos + size, C(bottom), 10f);
        dl.AddRectFilledMultiColor(pos + new Vector2(0, 6), pos + size - new Vector2(0, 6), C(top), C(top), C(bottom), C(bottom));
        dl.AddRectFilled(pos, pos + new Vector2(size.X, 10), C(top), 10f, ImDrawFlags.RoundCornersTop);
        ImGui.SetWindowFontScale(1.12f);
        var ts = ImGui.CalcTextSize(label);
        dl.AddText(pos + size / 2 - ts / 2, C(new Vector4(1, 1, 1, 1)), label);
        ImGui.SetWindowFontScale(1f);
        return clicked;
    }
}
