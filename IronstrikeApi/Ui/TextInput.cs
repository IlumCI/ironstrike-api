using System;
using Il2CppInterop.Runtime;

namespace IronstrikeApi.Ui;

/// <summary>
/// Text entry through the game's own VR keyboard, the one Private Match uses for codes. It works
/// with controllers in VR and with the mouse when playing flat. The open window steps aside while
/// the keyboard is up and comes back when it closes.
/// </summary>
public static class TextInput
{
    /// <summary>
    /// Shows the keyboard. <paramref name="done"/> gets the trimmed text when the player confirms;
    /// nothing is called if they back out. Returns false if there is no keyboard in this scene.
    /// </summary>
    /// <param name="title">The prompt shown above the keys.</param>
    /// <param name="maxLength">The most characters the player can type.</param>
    /// <param name="done">Receives the text.</param>
    public static bool Ask(string title, int maxLength, Action<string> done)
    {
        var kb = VRKeyboard.instance;
        if (kb == null) { Plugin.Log.LogWarning("VR keyboard not available in this scene"); return false; }

        int oldMax = kb.maxInputLength;
        bool finished = false;

        Action<string> finish = s =>
        {
            if (finished) return;
            finished = true;
            kb.maxInputLength = oldMax;
            Panel.Reshow();
            try { done?.Invoke((s ?? "").Trim()); }
            catch (Exception e) { Safe.Blame(done, "text input", e); }
            Window.Current?.Refresh();
        };
        Action back = () =>
        {
            if (finished) return;
            finished = true;
            kb.maxInputLength = oldMax;
            Panel.Reshow();
        };

        // Il2Cpp delegates must outlive this call, or the GC frees the trampoline under the keyboard.
        var f = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(finish);
        var b = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(back);
        Panel.KeepAlive(f);
        Panel.KeepAlive(b);

        kb.maxInputLength = maxLength;
        Panel.Hide();
        try { kb.Show(title, f, b); }
        catch (Exception e)
        {
            Plugin.Log.LogError($"VR keyboard: {e.Message}");
            kb.maxInputLength = oldMax;
            Panel.Reshow();
            return false;
        }
        return true;
    }
}
