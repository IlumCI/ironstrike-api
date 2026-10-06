using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using IronstrikeApi;
using IronstrikeApi.Gameplay;
using IronstrikeApi.Ui;

namespace HelloMod;

// [BepInDependency] makes BepInEx load the API first, and refuse to load this mod without it.
// [ModKind] tells players what the mod changes; [RequiresApi] guards against an old API.
[BepInPlugin("com.example.hellomod", "Hello Mod", "1.0.0")]
[BepInDependency(ModApi.Guid)]
[ModKind(ModKind.Gameplay)]
[RequiresApi("0.1.0")]
public class Plugin : BasePlugin
{
    ConfigEntry<float> speed;
    ConfigEntry<bool> greet;
    NetChannel chat;
    int kills;

    public override void Load()
    {
        // 1. Settings. These show up in the Mods window by themselves.
        speed = Config.Bind("General", "MoveSpeed", 1.25f,
            new ConfigDescription("Your move speed multiplier.", new AcceptableValueRange<float>(0.5f, 3f)));
        greet = Config.Bind("General", "Greet", true, "Say hello to other players who have this mod.");

        // 2. A stat modifier: the API only applies it in solo and private games.
        Stats.ModifyLocal(SkillCalcType.MoveSpeed, v => Stats.Scale(v, speed.Value));

        // 3. Events.
        GameEvents.RunStarted += () => { kills = 0; Log.LogInfo("Run started. Good luck!"); };
        GameEvents.FighterDied += f => { if (f.faction == Faction.EnemyBots) kills++; };
        GameEvents.RunEnded += outcome => Log.LogInfo($"Run over ({outcome}) with {kills} kill(s).");

        // 4. Talking to the same mod on other players' machines.
        chat = ModNet.Channel("hellomod.chat");
        chat.Received += m => Log.LogInfo($"hello from player #{m.Sender.PlayerId}: {m.Text}");
        ModNet.PeerReady += p => { if (greet.Value) chat.SendTo(p, "hello!"); };

        // 5. A row of our own on our page in the Mods window.
        ModSettings.AddSection("com.example.hellomod", page =>
        {
            page.Header("This run");
            page.Info("Kills", kills.ToString());
            page.Button("Reset counter", () => kills = 0);
        });
    }
}
