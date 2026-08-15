Mod developer tool that unlocks buildings, goods, materials, and needs across all factions in a single map for all faction testing.

### Overview
The **OmniFaction Dev Tool** is a strictly developer-focused utility designed to streamline the modding and testing workflow in Timberborn. By bypassing the hardcoded faction restrictions, this mod loads buildings, goods, materials, and needs across *all* factions within a single map instance.  
Whether you need to instantiate models from different factions side-by-side, this tool eliminates the need to constantly restart the game or switch saves.

### WARNINGS:
If you add this mod to an existing save and then later disable the mod, all beavers born between enabling and disabling the mod will be deleted when loading the saved game without the mod.   
If you start a new game with this mod enabled and you later disable this mod, all existing beavers will be deleted when loading the saved game with the mod disabled.   

### Key Features
**Universal Building Unlocks:** Instantly access the entire roster of Folktails and Iron Teeth (and any custom faction) buildings from the bottom toolbar.  
**Cross-Faction Materials & Goods:** Test production chains that require goods typically restricted to a single faction.  
**Faction-Specific Beavers:** New beavers inherit the faction of the building they spawn from (breeding pods, procreation houses, dwelling spawns), or dev-tool spawns use the nearest district center's faction, falling back to round-robin — getting their own faction's needs and fur color while coexisting on the same map.  
**Faction-Specific Bots:** Bots spawn as their own faction type (Folktails, IronTeeth, or any modded faction) — from matching faction buildings, or dev-tool spawns use the nearest district center's faction, falling back to round-robin — and get only their own faction's needs while adapting their outfits to their faction.  
**Faction-Restricted Assignments:** Beavers and bots live in and work at buildings belonging to their own faction (or "Common" buildings), so each faction operates as a self-contained colony sharing one map.  
**Per-Faction UI:** Population counters and character portraits show each character's real faction instead of the currently selected one.  
**Rapid Iteration:** Save time during the mod development cycle by doing all faction testing on a single testing map.

### Usage & Technical Details
This mod uses Harmony to merge every faction's buildings, goods, materials, and needs into a single playable set, so you can test content from any faction without restarting or switching saves.  
**Warning:** Do not use this mod on your standard playthrough saves. Unlocking cross-faction buildings can permanently alter save states or cause unexpected simulation behaviors if the mod is later uninstalled.  
**Target Audience:** This is intended to be used on *test maps* specifically crafted for mod development, asset loading checks, and debugging.

### Known Issues / Limitations
Some faction-specific UI overlays may overlap or display incorrectly when viewing a building belonging to an opposing faction.  
Beavers spawned by the mod get only their faction's needs; pre-existing (save-loaded) beavers keep their original shared blueprint and get all beaver needs (both factions + common survival needs) — this is intentional, since their shared blueprint can't be factioned.

---

[Mods on Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=3682179025)

[Mods on mod-dot-io](https://mod.io/u/calloatti/?_sort=name)

[Mods on Github](https://github.com/search?q=owner%3Acalloatti+sort%3Aname-asc+%22Timberborn+Mod%22&type=repositories)

[Mod zip files](https://github.com/calloatti/ModZips)
