using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace FateRunner.Data;

/// <summary>One open-world zone with FATEs: its FATE level range, and whether its FATEs pay Bicolor Gemstones.</summary>
public sealed record FateZone(uint Territory, string Expansion, int MinLevel, int MaxLevel)
{
    public bool Gemstones => Expansion is "Shadowbringers" or "Endwalker" or "Dawntrail";
}

/// <summary>
/// Every field zone with FATEs, in level order per expansion. Gemstone zones (Shadowbringers onward) are what the
/// gemstone goals farm; leveling mode can use any of them. Names and aetherytes come from the game's sheets.
/// </summary>
public static unsafe class FateZones
{
    public static readonly FateZone[] Zones =
    {
        // A Realm Reborn
        new(134, "A Realm Reborn", 3, 9),    // Middle La Noscea
        new(140, "A Realm Reborn", 4, 11),   // Western Thanalan
        new(148, "A Realm Reborn", 4, 10),   // Central Shroud
        new(135, "A Realm Reborn", 3, 10),   // Lower La Noscea
        new(141, "A Realm Reborn", 5, 15),   // Central Thanalan
        new(154, "A Realm Reborn", 10, 17),  // North Shroud
        new(138, "A Realm Reborn", 10, 20),  // Western La Noscea
        new(152, "A Realm Reborn", 11, 21),  // East Shroud
        new(145, "A Realm Reborn", 15, 22),  // Eastern Thanalan
        new(139, "A Realm Reborn", 20, 25),  // Upper La Noscea
        new(146, "A Realm Reborn", 21, 34),  // Southern Thanalan
        new(153, "A Realm Reborn", 21, 32),  // South Shroud
        new(137, "A Realm Reborn", 30, 34),  // Eastern La Noscea
        new(180, "A Realm Reborn", 30, 32),  // Outer La Noscea
        new(155, "A Realm Reborn", 35, 44),  // Coerthas Central Highlands
        new(147, "A Realm Reborn", 37, 49),  // Northern Thanalan
        new(156, "A Realm Reborn", 44, 50),  // Mor Dhona
        // Heavensward
        new(397, "Heavensward", 50, 54),     // Coerthas Western Highlands
        new(401, "Heavensward", 50, 54),     // The Sea of Clouds
        new(398, "Heavensward", 52, 55),     // The Dravanian Forelands
        new(400, "Heavensward", 54, 57),     // The Churning Mists
        new(399, "Heavensward", 58, 60),     // The Dravanian Hinterlands
        new(402, "Heavensward", 59, 60),     // Azys Lla
        // Stormblood
        new(612, "Stormblood", 60, 63),      // The Fringes
        new(613, "Stormblood", 62, 64),      // The Ruby Sea
        new(620, "Stormblood", 64, 66),      // The Peaks
        new(614, "Stormblood", 64, 67),      // Yanxia
        new(622, "Stormblood", 66, 69),      // The Azim Steppe
        new(621, "Stormblood", 69, 70),      // The Lochs
        // Shadowbringers
        new(813, "Shadowbringers", 70, 73),  // Lakeland
        new(814, "Shadowbringers", 70, 74),  // Kholusia
        new(816, "Shadowbringers", 72, 75),  // Il Mheg
        new(817, "Shadowbringers", 74, 77),  // The Rak'tika Greatwood
        new(815, "Shadowbringers", 76, 78),  // Amh Araeng
        new(818, "Shadowbringers", 79, 80),  // The Tempest
        // Endwalker
        new(956, "Endwalker", 80, 82),       // Labyrinthos
        new(957, "Endwalker", 80, 83),       // Thavnair
        new(958, "Endwalker", 82, 85),       // Garlemald
        new(959, "Endwalker", 83, 86),       // Mare Lamentorum
        new(961, "Endwalker", 86, 88),       // Elpis
        new(960, "Endwalker", 88, 90),       // Ultima Thule
        // Dawntrail
        new(1187, "Dawntrail", 90, 92),      // Urqopacha
        new(1188, "Dawntrail", 90, 93),      // Kozama'uka
        new(1189, "Dawntrail", 93, 95),      // Yak T'el
        new(1190, "Dawntrail", 95, 97),      // Shaaloani
        new(1191, "Dawntrail", 97, 99),      // Heritage Found
        new(1192, "Dawntrail", 99, 100),     // Living Memory
    };

    public static readonly string[] Expansions = { "A Realm Reborn", "Heavensward", "Stormblood", "Shadowbringers", "Endwalker", "Dawntrail" };

    /// <summary>The gemstone zones (Shadowbringers onward), territory ids.</summary>
    public static IEnumerable<uint> All => Zones.Where(z => z.Gemstones).Select(z => z.Territory);

    public static FateZone? Get(uint territory) => Zones.FirstOrDefault(z => z.Territory == territory);

    public static int MinLevel(uint territory) => Get(territory)?.MinLevel ?? 1;

    private static readonly Dictionary<uint, string> names = new();

    public static string Name(uint territory)
    {
        if (names.TryGetValue(territory, out var n)) return n;
        n = Plugin.Data.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var t) && t.PlaceName.ValueNullable is { } p
            ? p.Name.ExtractText() : $"Zone {territory}";
        return names[territory] = n;
    }

    /// <summary>The zone's aetheryte crystals (not aethernet shards), lowest id first.</summary>
    private static uint[] AetherytesIn(uint territory)
        => Plugin.Data.GetExcelSheet<Aetheryte>().Where(x => x.IsAetheryte && x.Territory.RowId == territory)
                 .Select(x => x.RowId).OrderBy(x => x).ToArray();

    private static readonly Dictionary<uint, uint[]> aetherytes = new();

    /// <summary>An aetheryte in the zone you've attuned to (else the zone's first one, which will fail to teleport).</summary>
    public static uint Aetheryte(uint territory)
    {
        if (!aetherytes.TryGetValue(territory, out var list)) aetherytes[territory] = list = AetherytesIn(territory);
        return list.FirstOrDefault(IsUnlocked) is var a and not 0 ? a : list.FirstOrDefault();
    }

    /// <summary>You've attuned to at least one aetheryte in the zone (so you can teleport there).</summary>
    public static bool Attuned(uint territory)
    {
        if (!aetherytes.TryGetValue(territory, out var list)) aetherytes[territory] = list = AetherytesIn(territory);
        return list.Any(IsUnlocked);
    }

    private static bool IsUnlocked(uint aetheryte)
    {
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        return ui != null && ui->IsAetheryteUnlocked(aetheryte);
    }

    /// <summary>
    /// Gemstones, picked for you: every Shadowbringers / Endwalker / Dawntrail zone your job's level allows and you can
    /// teleport to, highest-level first. (A run starts in the zone you're already in when it's one of them.) The
    /// order is fixed so zone hopping goes round them instead of back and forth.
    /// </summary>
    public static List<uint> ForGemstones(int level)
        => Zones.Where(z => z.Gemstones && z.MinLevel <= level && Attuned(z.Territory))
                .OrderByDescending(z => z.MinLevel)
                .Select(z => z.Territory).ToList();

    /// <summary>
    /// Leveling: the zones worth farming at this level, best first — ones whose FATE range covers your level, the
    /// higher-level ones first (more experience), then the nearest-below as a fallback. Only zones you can teleport to.
    /// </summary>
    public static List<uint> ForLevel(int level)
    {
        var usable = Zones.Where(z => z.MinLevel <= level && Attuned(z.Territory)).ToList();
        var fitting = usable.Where(z => level <= z.MaxLevel + 2).OrderByDescending(z => z.MinLevel).ToList();
        if (fitting.Count == 0) fitting = usable.OrderByDescending(z => z.MaxLevel).Take(2).ToList();
        return fitting.Select(z => z.Territory).ToList();
    }
}
