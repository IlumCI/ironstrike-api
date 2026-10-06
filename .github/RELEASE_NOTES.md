The shared modding API for IRONSTRIKE.

**Install:** BepInEx 6 IL2CPP **be.788**, then drop `BepInEx/plugins/IronstrikeApi.dll` (from the zip) into your game's `BepInEx/plugins/`. Mods built on the API need it; everyone on a modded server needs the same set.

**For players:** a **MODS** button on the main menu's Credits card (or F8) opens the Mods window: every installed mod, its version and its settings, editable in VR.

**For modders:** reference `IronstrikeApi.dll` (the zip includes `IronstrikeApi.xml`, so your IDE shows the docs) and add `[BepInDependency("eu.euroswarms.ironstrike.api")]`. The docs site is built by CI (the `docs-html` artifact); start with the tutorial.

Public matchmaking is locked while the API is loaded, as the game's developer asked. Gameplay changes made through the API apply only in solo games, private matches and modded servers.

**Known issues:** verified against the game build before the October 7, 2026 update; re-checking against the new build is pending. Played flat, a mod window whose page is only text or a table may not appear when opened in front of the player (menu-opened windows and settings pages are fine).

**Docs:** https://ilumci.github.io/ironstrike-api/
