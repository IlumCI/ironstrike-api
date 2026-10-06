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
    static bool asking, seenOpen;
    static int askedAt;

    /// <summary>True while the keyboard is up for a <see cref="Ask"/>.</summary>
    public static bool Busy
    {
        get
        {
            if (!asking) return false;
            // The game can close the keyboard without calling us back (its own Hide, a scene change).
            // Its menuOpen flag only turns on once the opening animation has run, so: busy until it
            // has been seen open and then closed, or if it never opened within a few seconds.
            var kb = VRKeyboard.instance;
            bool open = kb != null && kb.menuOpen;
            if (open) seenOpen = true;
            if (kb == null || (seenOpen && !open) || (!seenOpen && Environment.TickCount - askedAt > 5000)) asking = false;
            return asking;
        }
    }

    /// <summary>
    /// Shows the keyboard. <paramref name="done"/> gets the trimmed text when the player confirms;
    /// nothing is called if they back out. Returns false if there is no keyboard in this scene, or
    /// if it is already up for another question.
    /// </summary>
    /// <param name="title">The prompt shown above the keys.</param>
    /// <param name="maxLength">The most characters the player can type.</param>
    /// <param name="done">Receives the text.</param>
    public static bool Ask(string title, int maxLength, Action<string> done)
    {
        var kb = VRKeyboard.instance;
        if (kb == null) { Plugin.Log.LogWarning("VR keyboard not available in this scene"); return false; }
        // One question at a time: a second Show would orphan the first one's callbacks and restore
        // the wrong length limit.
        if (Busy) return false;
        maxLength = Math.Clamp(maxLength, 1, 1000);

        int oldMax = kb.maxInputLength;
        bool finished = false;

        Action<string> finish = s =>
        {
            if (finished) return;
            finished = true;
            asking = false;
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
            asking = false;
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
        asking = true;
        seenOpen = false;
        askedAt = Environment.TickCount;
        try { kb.Show(title ?? "", f, b); }
        catch (Exception e)
        {
            Plugin.Log.LogError($"VR keyboard: {e.Message}");
            asking = false;
            kb.maxInputLength = oldMax;
            Panel.Reshow();
            return false;
        }
        return true;
    }
}
