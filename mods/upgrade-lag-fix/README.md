# Upgrade Lag Fix

A client-side mod for Mycopunk that removes the freezes you get when many upgrades are stacked on one gear (for example, in Ouroboros and Ouroboros: Blitz). It **does not change gameplay**: damage, effect values and the order upgrades are applied in stay exactly the same. It only makes the game find the same answers faster.

## Features

- With dozens of upgrades on one gear, every time the game re-applies upgrades (closing the inventory, picking up an upgrade, and so on), it can freeze for seconds. This mod brings those freezes down to milliseconds.

  | One gear with 120 upgrades, ~1,670 owned upgrades | Time to apply that gear's upgrades |
  |---|---|
  | Unmodded | ~2,600 ms |
  | With this mod | ~10 ms |

- New in 1.1.0: a per-gear **stacking table** answers "how many copies of this upgrade come before this one" without walking the whole list again, and the options can be switched in game from the MODS tab.
- The startup-animation option from earlier test builds now lives in a separate mod, **QuickTimers**.

## How it works

The game looks up upgrades with linear searches and re-checks and re-sorts the equipped list on every step. The mod answers lookups from an index, and skips a check or a sort only when it is **proven** that nothing it depends on has changed: at startup it finds every instruction in the game that can remove upgrades or replace that data, and puts a change counter right before each one. If something can't be instrumented, that feature turns itself off and the game runs its original code. Running the original code next to every cached or skipped step found 0 mismatches over tens of millions of checks; you can run the same check yourself with `Cache.Verify`.

## Requirements

- [BepInExPack_Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/)
- Mycopunk Mod Kit (installed automatically as a dependency)

## Settings

The config file is `BepInEx/config/t4t5u0.UpgradeLagFix.cfg`. The defaults are what you want for normal play. Settings marked \* can also be changed in game under Settings → MODS (the MODS tab is provided by Mod Kit) and take effect at once.

| Setting | Default | What it does |
|---|---|---|
| `Cache.Enabled` \* | true | Answer upgrade lookups from an index instead of searching every owned upgrade |
| `Cache.Verify` \* | false | (debug) Run the original code next to every cached or skipped step and log mismatches. Safe, but as slow as the unmodded game |
| `Cleanup.SkipRedundant` \* | true | Skip the gear cleanup step when it provably can't remove anything |
| `Walk.SkipRedundantPrepare` \* | true | Skip the destroyed-upgrade check at the start of each walk when it provably can't remove anything |
| `Walk.SkipSortedSort` \* | true | Skip sorting the equipped list when it is already strictly in order |
| `Walk.StackingTable` \* | true | Answer stacking counts from a per-gear table. Needs the two skip options above |
| `Profiling.Hotkeys` \* | false | (debug) F6–F9 toggle the features, F10 runs a benchmark, F11 prints stats |
| `Profiling.LogThresholdMs` | 1.0 | Log upgrade applies that take at least this many milliseconds |
| `Profiling.Detailed` | false | (debug) Per-call timing. It slows down the code it measures. Needs a restart |
| `Profiling.SessionLogFile` | true | Also write this mod's log to `BepInEx/UpgradeLagFix/logs/`, one file per launch. Needs a restart |
| `Profiling.StatsDumpSeconds` | 120 | Print and reset the stats every this many seconds (0 = off). Needs a restart |
| `Profiling.StatsDumpOnEvents` | true | Also print the stats on scene changes, Ouroboros room clears and quit. Needs a restart |

## Multiplayer

Client-side only. It only changes how your own game looks up data, so other players don't need it. As with any BepInEx mod, your game counts as modded: lobbies show `[MODDED]`, and hosts can block modded players.

## Notes

- After a game update, check `BepInEx/LogOutput.log`. If a step can't find what it needs, it logs a warning and turns itself off, and the game keeps working.
- Mods that remove upgrades from the player's data in their own code (not by calling the game's methods) are not covered by the change tracking. If you see problems together with such a mod, turn off the `Cleanup` and `Walk` options.

---

Source: https://github.com/doku-macaron/mycopunk-mods-public
Author: t4t5u0
Not affiliated with Pigeons at Play or Devolver Digital.
