The shared modding API for IRONSTRIKE. **0.2.1 is a prerelease for testing.**

**Fixed in 0.2.1:** custom spells now get the preview icons beside their corner of the rune grid. In 0.2.0 a corner holding only custom schools showed no icons, so the new spells looked missing from the casting menu (they were reachable by drawing into the corner anyway). Both players should update, so your content matches.

**New in 0.2:** mods can add content to the game itself: new skills on the upgrade screen, new magic schools for casters (three stages, enhanced versions) and the spells they teach, cast from the wand's rune grid, with an effects engine built on the game's own visuals.

**Arcana** (`Arcana-1.0.0.zip`) is the example spell pack: Lightning Magic (Chain Lightning, Thunderstorm), Sky Magic (Air Strike, Orbital Laser), Death Magic (Raise Dead, Life Drain), Mind Magic (Telekinesis, Kinetic Ward) and Plague Magic (Plague Cloud, Pestilence). Casters find them on the upgrade screen; each is drawn on the rune branch of a game school you don't own, labelled with its own names and icons.

**Install:** BepInEx 6 IL2CPP **be.788**, then unzip `IronstrikeApi-0.2.1.zip` (and `Arcana-1.0.0.zip` for the spells) into the game folder so the DLLs land in `BepInEx/plugins/`.

**Playing together:** custom skills and spells work solo, on modded servers, and in Private Matches where **every** player has the same mods and versions. If anyone in the match lacks them, they switch off for everybody (the log says so).

**For players:** a **MODS** button on the main menu's Credits card (or F8) opens the Mods window: every installed mod, its version and its settings.

**For modders:** reference `IronstrikeApi.dll` (the zip includes `IronstrikeApi.xml`) and add `[BepInDependency("eu.euroswarms.ironstrike.api")]`. Start with the docs' "New skills, magic schools and spells" guide.

Public matchmaking is locked while the API is loaded, as the game's developer asked.

**Not yet verified:** the 0.2.1 corner icons in a running game (built from the game's own code and photos of the grid), drawing a custom school's runes in VR, and two players with custom content in one match. Please report how they go.

**Docs:** https://ilumci.github.io/ironstrike-api/
