# AGENTS.md: IRONSTRIKE Mod API

These are notes for an agent working on this library. **Read the game-side `AGENTS.md` first**; it sits
next to `Ironstrike.exe` in the Linux analysis copy of the game. Its rules (§0) apply here unchanged:

- no AI attribution in commits
- no `sudo`
- never let a modded client reach public multiplayer
- no currency
- don't defeat `GM.CreateDevMenu`
- don't kill the user's game session
- no private data in logs
- Windows-usable tooling

On top of those: **never push, tag, create a repo or publish a release without the user's explicit
say-so** for that action.

Facts marked **verified** were observed in the running game or in disassembly.

## 1. What this is

A BepInEx 6 IL2CPP plugin (`eu.euroswarms.ironstrike.api`) that other mods depend on through
`[BepInDependency(ModApi.Guid)]`. It provides:

- events
- gameplay helpers
- a window toolkit and the Mods window
- mod-to-mod networking
- the public-play lockout

Ironstrike Trainer and Ironstrike Servers don't use it yet. Migrating them is planned; until then,
both must keep working alongside it unchanged.

## 2. Layout

| Path | What |
| --- | --- |
| `IronstrikeApi/Plugin.cs` | `ModApi` constants, `Plugin` (installs each patch class separately), `Cfg` |
| `IronstrikeApi/Core/` | `Driver` (GM.Update postfix), `Mods`, `Safety`, `Game`, `Log`/`Safe`, `SelfTest` |
| `IronstrikeApi/Events/` | `GameEvents` (public), `Hooks` (polling plus hooks), `SessionHooks` (also sets `Safety.Context`) |
| `IronstrikeApi/Gameplay/` | `Stats`, `DamageEvent`/`ProjectileEvent` and their hooks, `Players`, `Bots`, `Status`, `Loadout` |
| `IronstrikeApi/Net/ModNet.cs` | channels over Fusion reliable data |
| `IronstrikeApi/Ui/` | `Kit` (public drawing kit, from Servers), `Panel` (internal window host), `Window`, `Page`, `TextInput`, `MainMenu`, `ModSettings`, `ModsWindow` |
| `examples/HelloMod/` | the example the tutorial walks through; CI builds it |
| `docs/` | Sphinx site (python-docs-theme); `docs/_ext/csdomain.py` is a small C# domain; `docs/tools/RefGen` generates `docs/reference/api/` from the DLL and its XML docs |

## 3. Contracts that must not break

- **Every public type and member has a `///` summary.** RefGen exits 1 otherwise, which fails CI's
  docs job. `GenerateDocumentationFile` is on.
- **Semver.** Within 0.x, only add. HelloMod is the compile-time check that existing calls still
  work.
- **`Mods.Hash` equals Ironstrike Servers' `Protocol.ModHash`:** SHA-256 over the sorted
  `GUID@version` lines joined by `\n`, first 8 bytes, lower-case hex.
- **The lockout has no off switch.** `Safety` blocks `PressPlay`, `PressHost`, `StartMatchmaking`
  and `FindOrHostMatchmaking`. Button greying is skipped when the trainer or Servers is loaded,
  because they handle those buttons themselves (Servers rebinds them every second).
- **Gameplay helpers check `Safety.GameplayAllowed`.** Every new helper that changes the game must
  call `Safety.Check` first.
- **ModNet never sends data to a peer that has not greeted back,** and never in `Public`. The game's
  `NetworkLogic.OnReliableDataReceived` is empty, so a vanilla peer ignores the greeting.

## 4. How the play context is decided

`SessionHooks` uses a prefix on `NetworkRunner.StartGame` with `Priority.Last`. It only records what
it sees: the runner pointer, the `GameMode`, and whether `CustomLobbyName` starts with `ism-`.

The session is classified on the next frame:

1. `Single` → Solo.
2. The modded lobby, or `IronstrikeServers.Api.InModdedSession` (read by reflection) → ModdedServer.
3. Entered through `PressPrivateMatch` → PrivateMatch.
4. Anything else → Public.

The one-frame delay matters. Servers re-issues `StartGame` with its own args from inside its prefix,
so the first args seen can be the unmodified ones.

The session ends in a `NetworkLogic.OnShutdown` postfix, matched by runner pointer. Servers' browse
runner (GameObject name contains `BrowseRunner`) is ignored.

## 5. Where events come from

| Event | Source |
| --- | --- |
| Update | `GM.Update` postfix |
| SceneChanged, RunStarted/Ended, LevelStarted | polled from `SceneManager.GetActiveScene().buildIndex` (`LevelSceneNum` = build index; a run is time in non-haven levels) |
| Run outcome | `NetworkGameMaster.WinRun_Synced` / `LoseRun_Synced` postfixes |
| EncounterStarted/Completed | `NetworkGameMaster.TriggerEncounter_Synced(int)` / `CompleteEncounter_Synced(int×4)`, deduplicated |
| FighterDied | `SkillManager.OnFigherDeath_Local(Fighter)` (static; the spelling is the game's) |
| PlayerJoined/Left | `NetworkLogic.OnPlayerJoined/OnPlayerLeft`, main runner only |
| Damage | `Fighter.CalculateDamage` postfix (verified in the trainer) |
| ProjectileSpawned | both `Projectile.SetTypes` overloads, full signatures (§7.4 of the game-side file) |
| Stats | `Fighter.CalcSkillAndStatusEffectValue` postfix (verified in the trainer) |
| ModNet receive | prefix on `NetworkRunner.Fusion_Simulation_ICallbacks_OnReliableData(PlayerRef, byte[])` |

Each hook logs `PATCH LIVE: <name>` the first time it fires. `Hooks.LiveHooks` is shown in the Mods
window.

## 6. UI notes

`Panel` is Servers' window host, generalised:

- It copies the Options canvas, 2600 x 1650, and is rebuilt per scene.
- Views are named `V_*`.
- Click delegates are kept for two redraws, then dropped; chrome and pill delegates are kept for
  good.

`Window`/`Page` work like an immediate-mode UI: every change re-runs the render callback.

Main-menu pills are copies of the HOST pill:

- The API's MODS pill goes on the Credits card.
- The trainer's pill sits on Options and Servers' on Private Match, so pills added to those cards
  start one slot to the left.

## 7. Debug aids (`[09 Debug]`)

- **`SelfTest`:**
  1. Waits for the haven.
  2. Presses OK on the first-start safety notice (`Modal.Confirm`).
  3. Reads `ReliableDataTransferModes`, opens the Mods window and spawns a dummy.
  4. Starts a solo run with `PressSolo` and hurts the bots twice.
  5. Leaves and logs event counts (`self-test [...]` lines).
- **`LogEvents`:** logs every event, with counts and enums only.
- **`AutoOpenAfterSeconds`:** opens the Mods window for screenshots.

Set them all back afterwards.

## 8. Docs build

```
pip install -r docs/requirements.txt
./build-docs.sh        # dotnet build -> RefGen -> sphinx-build -W
```

- `docs/reference/api/` is generated and gitignored.
- The landing page is `_templates/indexcontent.html`, served as `index`. The root document is
  `contents.rst`, as on docs.python.org.
- `_templates/layout.html` replaces the theme's footer, which carries the PSF's license and
  donation lines.
- The Pages deploy job in `docs.yml` is `if: false` until the user enables Pages.

## 9. Verified in game (2026-10-07, Linux flat, alongside the trainer and Servers)

- **Hooks that fired:**
  - `GM.Update`
  - `Fighter.CalcSkillAndStatusEffectValue`
  - `NetworkRunner.StartGame` (session tracking)
  - `NetworkLogic.OnPlayerJoined`
  - `NetworkLogic.OnShutdown`
  - `NetworkGameMaster.TriggerEncounter_Synced`
  - `Projectile.SetTypes` (both overloads installed, no crash)
  - `Fighter.CalculateDamage`
- **Events seen:**
  - SceneChanged
  - SessionStarted (Solo) and SessionEnded
  - PlayerJoined
  - RunStarted, LevelStarted and RunEnded (Ended)
  - EncounterStarted
  - Damage
  - ProjectileSpawned
- **Helpers:**
  - `Bots.SpawnDummy`, `HurtAll` and `DespawnAll` work on the solo host.
  - The Mods window renders in the game (Installed and Session tabs, auto-generated config rows).
  - The MODS pill is placed on the Credits card.
- **`ReliableDataTransferModes` is `ClientToServer, ClientToClientWithServerProxy`.** Client→host
  and host→client reliable data are both enabled.
- **The haven has no network session until a mode is chosen.** Before that, the context is
  Offline. `PressSolo` starts a `Single` haven session, and the run starts at the haven portal.
  `NetworkGameMaster.ProgressToNextLevelInSequence()` does the same.
- **The first start shows a "Safety Warning" notice in the boot scene, and the haven waits for its
  OK.**

## 9b. Stress testing

Two suites. Run both after any change to the areas they cover.

**`tests/IronstrikeApi.Tests`** (xunit, in CI, no game needed): `dotnet test tests/IronstrikeApi.Tests`.
- `NetTests` run `NetCore` against a simulated session:
  - drop, duplication and reordering
  - vanilla peers
  - spoofed origins
  - 5000 fuzzed packets
  - floods (the host relays at most `MaxInPerSecond` per sender)
  - payload and rate limits
  - id reuse, including when the leave callback was missed
  - a throwing transport
- `CoreTests` cover:
  - the stat engine: order, mid-pass add/remove, throwing modifiers, NaN, the recursion guard,
    10k modifiers with no allocation
  - dispatch under failure, and the global error-report cap
  - the session-classification table
  - the mod-hash vectors against Servers' formula
  - redaction
  - the settings classifier on odd types: byte/uint/decimal, exact long, `[Flags]`, undefined enum
    values, int lists, markup

The game-independent cores exist for these tests: `NetCore`, `StatEngine`, `SessionHooks.Classify`
and `ModSettings.Classify`/`TrySet`. Keep logic there. Do not touch Il2Cpp objects in them, and use
`is null`, not `== null`, on game objects: Unity's operator calls into the engine.

**`[09 Debug] StressTest`** (in the game; logs `stress: PASS/FAIL`, ends with `stress: DONE n passed, m failed`):
- **Windows:**
  - re-entrancy
  - a throwing render
  - huge pages, and table size against rendering
  - an open/close storm
  - fuzz-clicking every control, stale and throwing ones included
- **Settings:** a config full of awkward entries, fuzz-clicked, then re-validated.
- **Keyboard:** concurrency, and the keyboard being closed behind our back.
- **Load:** 2000 Update handlers (500 throwing), and 2000 stat modifiers gated by context.
- **Hooks and menu:** fuzzed packets through the real receive hook, menu pills.
- **Session:** a scene change under an open window, damage handlers in a real fight, and leaving the
  run.

Bugs these found and fixed:
- **Windows:**
  - A render that refreshed itself overflowed the stack.
  - Windows survived level loads and floated where the player used to be: the base scene persists,
    and they now close on scene change.
- **Keyboard:** `TextInput` lost track of the keyboard. `menuOpen` only turns on after the opening
  animation, and `Hide()` keeps the GameObject active.
- **Settings:**
  - `[Flags]` and undefined enum values were silently rewritten.
  - Exact `long` values were lost; their bounds now use decimal, because `long.MaxValue` as a double
    overflows.
  - Config text was parsed as markup.
- **Networking:**
  - The host had no receive-side limits.
  - A lost greeting reply stranded a client.
  - Stale peer ids were kept.
- **Logging:**
  - 500 throwing handlers could write 2500 error lines.
  - Errors before the plugin loaded could throw.

Facts learned:
- `MoveSpeed` reaches the hook as 0, a bonus value; use `Stats.Scale`.
- Flat under Proton, `Main Camera` is the only camera.

## 10. Open work

- **A game update (security and networking, public lobbies) was released on 2026-10-07, mid-work.**
  Everything in this file was verified against the build before it. After updating:
  - Regenerate `refs/` from the game's new `BepInEx/interop`.
  - Re-run the unit tests and the `[09 Debug] StressTest` suite.
  - Re-check every `PATCH LIVE` line: signatures, inlining, `NetworkRunner.StartGame`, the
    reliable-data receive method, the PressPlay/PressHost/matchmaking lockout.
  - Re-check the Servers mod's lobby and token handling.
- **Unsolved: some windows opened in front of the camera do not render, flat under Proton.**
  - **What fails:** text-only pages and tables, opened with `Window.Open()` and no anchor, at any
    time in the haven.
  - **What renders,** at the identical position, with the same camera and the same canvas state
    (active, nothing culled, alpha 1): settings pages and the Mods window, at the menu anchor and in
    a level.
  - **Ruled out:**
    - the vertex count (50 rows fail too)
    - timing (0-200 s)
    - a Unity error
    - the camera choice (only `Main Camera` exists)
  - **Next step:** the A/B probes at the top of `StressTest.Build()` (settings vs header vs info vs
    text) were about to answer which content matters; they are still in the suite. Check VR too:
    there the camera is the headset and this may not occur.

- Not yet seen firing:
  - `EncounterCompleted`
  - `WinRun_Synced` / `LoseRun_Synced` (RunOutcome Won/Lost)
  - `FighterDied` (`SkillManager.OnFigherDeath_Local`): `HurtAll` wounds without killing
  - `PlayerLeft`
  - `PlayerJoined` on a client
- Verify ModNet between two machines. The self-test cannot loop a packet back to the host.
- Move the trainer and Servers onto the API (see `docs/howto/porting.rst`).
- The window sidebar has no scrolling.
