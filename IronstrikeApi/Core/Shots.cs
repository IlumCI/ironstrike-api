using System;
using System.IO;
using UnityEngine;

namespace IronstrikeApi.Core;

// Debug screenshots taken by the game itself, at the frame they are asked for. Screenshots taken from
// outside, timed off the log, are unreliable: BepInEx writes its log file in batches, so a "now"
// line can reach the disk seconds after the moment has passed.
internal static class Shots
{
    internal static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "debug-shots");

    internal static void Take(string name)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);   // written at the end of this frame
            Plugin.Log.LogInfo($"screenshot queued: debug-shots/{name}.png");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"screenshot {name} failed: {e.Message}"); }
    }
}
