# FateRunner

Automated FATE farming for Bicolor Gemstones (Dalamud API 15, .NET 10).

`/fr` (or `/faterunner`) opens the window · `/fr start` · `/fr stop`

## Install

1. In game: `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, add
   `https://raw.githubusercontent.com/sadoniaveletoria/Faterunner/main/repo.json`, tick it, and save.
2. `/xlplugins` → search **FateRunner** → Install.

You also need **vnavmesh** and **Wrath Combo** (and optionally Lifestream and BossMod Reborn / BossMod).

## What it does

1. **Gets to a FATE zone.** For gemstones it picks the zones itself by default: every Shadowbringers, Endwalker and
   Dawntrail zone (their FATEs pay Bicolor Gemstones) your job's level allows and you've attuned to, starting where you
   are, highest level first. Turn off "Pick gemstone zones for me" under Zones to choose them yourself. Runs CraftBuddy
   starts always pick their own. It teleports to the zone's aetheryte, through
   Lifestream if loaded, otherwise the game's own teleport.
2. **Picks the best FATE.** The order is:
   - the one you're standing in;
   - then bonus FATEs (if "prefer bonus" is on);
   - then running ones over ones waiting to start;
   - then near and well-progressed ones.

   It skips FATEs with too little time left, ones too far along, levels well above you, and types you've turned off.
3. **Travels there.** It mounts and flies where flying is unlocked, lands inside the ring, then dismounts and level-syncs.
4. **Does the FATE:**
   - **Kill, Boss and Defend:** attacks the FATE's enemies, ones hitting you first. Wrath Combo's Auto-Rotation does
     the fighting; FateRunner targets and pulls with auto-attack.
   - **Hand-in:** kills enemies and picks up the FATE's items lying around. It hands in batches of 10, or whatever you
     hold when time is short or nothing is left to kill: walks to the FATE NPC, talks to them, puts the item in the
     hand-in slot and presses Hand Over.
   - **Escort:** stays with the escorted NPC and fights whatever comes for it.
   - **FATEs waiting to start:** talks to the NPC that begins them and says yes.
5. **Repeats.** If the zone has no FATEs for a while, it **hops to the next zone**.
6. **Stops** at your gemstone goal, or just before the 1,500 cap so rewards aren't wasted.

**Chocobo companion:** while running it keeps your chocobo out. It summons it with Gysahl Greens when it's away
(out of combat, on the ground), feeds it again when under 5 minutes are left, and sets the stance you picked (Healer by
default). Toggle under Travel & combat.

If you're knocked out, it waits for a raise (and accepts one). Otherwise it returns to your home point and travels
back.

## Goals

- **Gemstones: stop at a goal** — farm the zones you picked until you hold N Bicolor Gemstones.
- **Gemstones: run until stopped** — same, without a goal (still stops before the 1,500 cap if you want).
- **Leveling: up to a level** — FateRunner picks the zones with the most FATEs you can do at your job's real level
  (read from the game's own FATE list; any expansion, only zones you've attuned to), and moves up as you level, until
  the level you set. The zone tiles show which zones it will use.

**Group bosses** (Which FATEs → Group bosses, on by default): once synced in a boss FATE's ring, a boss with far more
HP than you (default 60×) counts as a group boss. FateRunner leaves and remembers the FATE by name, so it's skipped from
then on without flying there; click a name in that list to do it again. Optionally it joins anyway when enough other
players (default 8) are already fighting it.

**Level sync** is always on: inside the ring it syncs before attacking, retrying until the game confirms (an
over-level, un-synced player gets nothing from a FATE).

**BossMod AI** (BossMod Reborn's `/bmrai`, or BossMod's `/vbm ai`) is turned on for the run so mechanics get dodged,
and off again when FateRunner stops. Optional; toggle under Travel & combat.

## For other plugins (IPC)

| Name | Signature | |
|---|---|---|
| `FateRunner.IsRunning` | `() → bool` | |
| `FateRunner.Status` | `() → string` | what it's doing |
| `FateRunner.Gemstones` | `() → int` | Bicolor Gemstones held |
| `FateRunner.StartForGemstones` | `(int target) → bool` | run until you hold `target` gemstones, in zones it picks itself; false if it can't start (see `LastStartError`) |
| `FateRunner.Start` | `() → bool` | with the goal set in its window |
| `FateRunner.Stop` | `() → void` | |
| `FateRunner.LastStartError` | `() → string` | why the last start was refused ("" if it started) |

CraftBuddy uses `StartForGemstones` when a Make needs more Bicolor Gemstones than you have.

## Requirements

- **vnavmesh** (required)
- **Wrath Combo** (required for fighting; your job needs its combos enabled in Wrath's Auto-Mode)
- **Lifestream** (optional)
- **BossMod Reborn** or **BossMod** (optional, AI dodging)

## Things to verify on first run (written without a local build)

| Where | What to check |
|---|---|
| `Core/Fates.cs` | FATE kinds come from the FATE's event item (hand-in), then its map icon (boss 60722/3, escort 60725/6, defend 60727/8, collect 60729/30), then `Rule` (4 boss, 5 defend, 6 escort). Log `Rule`/icons for FATEs that get the wrong kind. |
| `Core/FateEngine.cs` (hand-in) | Item goes in with `AgentInventoryContext.OpenForItemSlot(KeyItems, slot, 0, Request.Id)`, then `Request` callback 0 = Hand Over. |
| `Core/Game.cs` | `Talk` is advanced with `ReceiveEvent(MouseClick)`. `SelectYesno`/`SelectString` with callback 0. |
| `Data/FateZones.cs` | Territory ids per expansion; aetheryte = lowest-id aetheryte crystal in the zone. |

## Releasing a new version

1. Set `<Version>` in `FateRunner.csproj` and `AssemblyVersion` in `repo.json` to the new number.
2. Commit and push to `main`.
3. GitHub Actions builds the plugin; since that version has no release yet, it tags the commit `v<version>` and
   publishes the release with `latest.zip`. Dalamud picks the update up from `repo.json`.

FateRunner's code is its own. Auto FATE Grind and Autofate were only read for reference on how the game behaves; no
code from them is used.
