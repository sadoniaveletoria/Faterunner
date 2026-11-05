using System;
using System.Collections.Generic;

namespace FateRunner.Data;

/// <summary>
/// FATEs built for a crowd: each expansion's "special FATEs" (world bosses), plus the big notorious monsters of
/// A Realm Reborn. Skipped by name without flying there, as long as "Skip bosses that need a group" is on. Names are
/// the English FATE titles (from the FFXIV wiki's world boss list). Any other boss too big to solo is caught by the
/// size check in the FATE and remembered with the ones you mark "too big".
/// </summary>
public static class GroupBossFates
{
    public static readonly (string Expansion, string Name, string Boss)[] Known =
    {
        ("A Realm Reborn", "He Taketh It with His Eyes", "Behemoth"),
        ("A Realm Reborn", "Behold Now Behemoth", "Behemoth (lead-in)"),
        ("A Realm Reborn", "Steel Reign", "Odin"),
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
        ("Dawntrail", "Mascot Murder", "Mica the Magical Mu"),
        // Big notorious monsters that aren't world bosses but still need a group (found in play).
        ("Stormblood", "Wham, Bam, Thank You, Mammoth", "Mahishasura"),
    };

    private static readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string fateName)
    {
        if (names.Count == 0) foreach (var k in Known) names.Add(k.Name);
        return names.Contains(fateName.Trim());
    }
}
