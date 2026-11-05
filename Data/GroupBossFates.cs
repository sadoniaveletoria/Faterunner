using System;
using System.Collections.Generic;

namespace FateRunner.Data;

/// <summary>
/// FATEs built for a crowd: each expansion's "special FATEs" (world bosses), plus the big notorious monsters of
/// A Realm Reborn. Skipped by name without flying there, as long as "Skip bosses that need a group" is on. Names are
/// the English FATE titles (from the FFXIV wiki's special FATE list). Anything else too big is caught by the HP check
/// and remembered separately.
/// </summary>
public static class GroupBossFates
{
    public static readonly (string Expansion, string Name, string Boss)[] Known =
    {
        ("A Realm Reborn", "He Taketh It with His Eyes", "Odin"),
        ("A Realm Reborn", "Steel Reign", "special FATE"),
        ("A Realm Reborn", "Behold Now Behemoth", "Behemoth"),
        ("A Realm Reborn", "Long Live the Coeurl", "Coeurl Regina"),
        ("Heavensward", "Coeurls Chase Boys Chase Coeurls", "Coeurlregina"),
        ("Heavensward", "Prey Online", "Noctilucale"),
        ("Stormblood", "Foxy Lady", "Tamamo Gozen"),
        ("Stormblood", "A Horse Outside", "Ixion"),
        ("Shadowbringers", "The Head, the Tail, the Whole Damned Thing", "Archaeotania"),
        ("Shadowbringers", "A Finale Most Formidable", "Formidable"),
        ("Endwalker", "Devout Pilgrims vs. Daivadipa", "Daivadipa"),
        ("Endwalker", "Omicron Recall: Killing Order", "Chi"),
        ("Dawntrail", "The Serpentlord Seethes", "Ttokrrone"),
        ("Dawntrail", "Mascot Murder", "Sally the Sweeper"),
    };

    private static readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string fateName)
    {
        if (names.Count == 0) foreach (var k in Known) names.Add(k.Name);
        return names.Contains(fateName.Trim());
    }
}
