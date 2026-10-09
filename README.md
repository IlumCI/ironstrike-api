# IRONSTRIKE Mod API

The shared library IRONSTRIKE mods are built on. It installs once, every mod depends on it, and it
turns the game's internals into documented C# you can build on:

- **Game events.** Runs, levels, encounters, scenes, sessions, players joining, hits, deaths and
  projectiles are all plain C# events. A handler that throws is blamed on its own mod and never takes
  the others down.
- **Gameplay hooks.** Stat modifiers that stack between mods, damage you can change or cancel,
  status effects, skills, weapon sets and bots.
- **New content.** Skills on the upgrade screen, magic schools for casters and the spells they
  teach, drawn on the wand's rune grid and cast like the game's own, with a library of icons drawn
  in the game's style. The [Arcana](examples/Arcana) example adds five schools and ten spells.
- **A window toolkit.** Windows in the classic Steam style, built on a copy of the game's own
  Options window, so they work the same in VR and flat. Pages are built from check boxes, steppers,
  pickers, tables and buttons, and text entry uses the game's VR keyboard.
- **The Mods window.** It lists every loaded mod with its version, what it needs and a page of its
  settings. That page is generated from the mod's BepInEx config, so a mod needs no code to get it.
  Open it with the **MODS** button on the main menu's Credits card, or **F8**.
- **Mod-to-mod networking.** Named channels between the same mod on different players' machines,
  over the game's own connection and relayed through the host. Players without the API never get
  mod traffic.
- **The developer's rule, enforced once.** Public matchmaking (Play, HOST) is locked while the API
  is loaded. Gameplay changes made through the API apply only in solo games, private matches and
  [modded servers](https://github.com/IlumCI/ironstrike-servers).

## Documentation

`docs/` is a documentation site in the style of docs.python.org. It has a tutorial, how-to guides,
notes on the game's internals, and an API reference generated from the code. To build it:

```
pip install -r docs/requirements.txt
./build-docs.sh            # or .\build-docs.ps1 on Windows
```

Then open `docs/_build/html/index.html`. CI builds it on every push and attaches it as an artifact.

## Install (players)

1. Install BepInEx 6 IL2CPP, build **be.788**:
   [BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip).
   Extract **all six** items (`winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`,
   `changelog.txt`, `dotnet/`, `BepInEx/`) directly next to `Ironstrike.exe`.
2. Start the game once and wait for the main menu.
3. Put `IronstrikeApi.dll` in `BepInEx/plugins/`, along with the mods that use it.

If nothing happens on launch, run `bepinex-doctor.ps1` from this repo in PowerShell. On Linux under
Proton, add `WINEDLLOVERRIDES="winhttp=n,b" %command%` to the Steam launch options.

## A mod in thirty lines

```csharp
[BepInPlugin("com.example.hellomod", "Hello Mod", "1.0.0")]
[BepInDependency(ModApi.Guid)]
[ModKind(ModKind.Gameplay)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        var speed = Config.Bind("General", "MoveSpeed", 1.25f,
            new ConfigDescription("Move speed multiplier.", new AcceptableValueRange<float>(0.5f, 3f)));

        Stats.ModifyLocal(SkillCalcType.MoveSpeed, v => Stats.Scale(v, speed.Value));
        GameEvents.RunEnded += outcome => Log.LogInfo($"Run over: {outcome}");

        var chat = ModNet.Channel("hellomod.chat");
        chat.Received += m => Log.LogInfo($"player #{m.Sender.PlayerId}: {m.Text}");
        ModNet.PeerReady += p => chat.SendTo(p, "hello!");
    }
}
```

The `MoveSpeed` setting appears in the Mods window by itself. The full example is in
[`examples/HelloMod`](examples/HelloMod), and it builds in CI.

## Building

```
dotnet build IronstrikeApi/IronstrikeApi.csproj -c Release
```

The reference assemblies are committed in `refs/`, so this works with no game files present.
`./build.sh <gameDir>` or `.\build.ps1 -GameDir <gameDir>` builds against the game's own interop and
deploys the DLL. `./package.sh` makes the release zip. CI builds every push, and tags `v*` publish a
release.

## Works with

[Ironstrike Trainer](https://github.com/IlumCI/ironstrike-trainer) 1.0.3 and
[Ironstrike Servers](https://github.com/IlumCI/ironstrike-servers) 0.1.0 run alongside the API
without changes. They don't use it yet.

Installing the API changes your mod set, so on a modded server either everyone has it or nobody
does.

## License

AGPL-3.0. IRONSTRIKE is a game by E McNeill. This project is not affiliated with or endorsed by its
developer.
