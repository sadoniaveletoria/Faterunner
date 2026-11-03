using System;
using FateRunner.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FateRunner.Core;

/// <summary>Small game helpers: gemstone wallet, teleporting, and clicking through simple dialogs.</summary>
public static unsafe class Game
{
    public const uint BicolorGemstone = 26807;
    public const int GemstoneCap = 1500;

    /// <summary>
    /// Your current job's real level — not the level-synced one (a FATE sync or duty would otherwise make a Lv100 job
    /// look like Lv70).
    /// </summary>
    public static int JobLevel
    {
        get
        {
            var ps = PlayerState.Instance();
            var me = Plugin.ObjectTable.LocalPlayer;
            if (ps == null || me == null) return 0;
            if (Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().TryGetRow(me.ClassJob.RowId, out var cj) && cj.ExpArrayIndex >= 0)
            {
                var lvl = ps->ClassJobLevels[cj.ExpArrayIndex];
                if (lvl > 0) return lvl;
            }
            return Math.Max((int)ps->CurrentLevel, (int)me.Level);
        }
    }

    /// <summary>Bicolor Gemstones held (currency tab / CurrencyManager — whichever the game uses).</summary>
    public static int Gemstones => CurrencyCount(BicolorGemstone);

    public static int CurrencyCount(uint itemId)
    {
        var im = InventoryManager.Instance();
        if (im == null) return 0;
        var inBags = im->GetInventoryItemCount(itemId, false, false, false);
        var inCurrency = 0;
        var c = im->GetInventoryContainer(InventoryType.Currency);
        if (c != null)
            for (var i = 0; i < c->Size; i++)
            {
                var slot = c->GetInventorySlot(i);
                if (slot != null && slot->ItemId == itemId) inCurrency += (int)slot->Quantity;
            }
        var managed = 0;
        var cm = CurrencyManager.Instance();
        if (cm != null) managed = (int)cm->GetItemCount(itemId);
        return Math.Max(managed, Math.Max(inBags, inCurrency));
    }

    /// <summary>How many of an item (incl. key items, e.g. a FATE's hand-in item) you hold.</summary>
    public static int ItemCount(uint itemId)
    {
        var im = InventoryManager.Instance();
        if (im == null) return 0;
        var n = im->GetInventoryItemCount(itemId, false, false, false);
        var keys = im->GetInventoryContainer(InventoryType.KeyItems);
        if (keys != null)
            for (var i = 0; i < keys->Size; i++)
            {
                var slot = keys->GetInventorySlot(i);
                if (slot != null && slot->ItemId == itemId) n += slot->Quantity;
            }
        return n;
    }

    /// <summary>Teleports to an aetheryte (Lifestream if preferred and loaded, else the game's own teleport).</summary>
    public static bool Teleport(uint aetheryte, LifestreamIpc lifestream, bool preferLifestream)
    {
        if (aetheryte == 0) return false;
        if (preferLifestream && lifestream.IsAvailable && lifestream.Teleport(aetheryte)) return true;
        var t = Telepo.Instance();
        return t != null && t->Teleport(aetheryte, 0);
    }

    public static AtkUnitBase* Addon(string name)
    {
        var mgr = RaptureAtkUnitManager.Instance();
        if (mgr == null) return null;
        var a = mgr->GetAddonByName(name);
        return a != null && a->IsVisible ? a : null;
    }

    public static bool Visible(string name) => Addon(name) != null;

    /// <summary>Fires an addon callback with int values (e.g. SelectYesno "yes" = 0).</summary>
    public static bool Callback(string addon, params int[] values)
    {
        var a = Addon(addon);
        if (a == null || !a->IsReady) return false;
        var v = stackalloc AtkValue[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            v[i].Type = AtkValueType.Int;
            v[i].Int = values[i];
        }
        a->FireCallback((uint)values.Length, v, true);
        return true;
    }

    /// <summary>Advances an NPC speech bubble (Talk addon), if one is up.</summary>
    public static bool ClickTalk()
    {
        var a = Addon("Talk");
        if (a == null || !a->IsReady) return false;
        var evt = stackalloc AtkEvent[1];
        var data = stackalloc AtkEventData[1];
        a->ReceiveEvent(AtkEventType.MouseClick, 0, evt, data);
        return true;
    }
}
