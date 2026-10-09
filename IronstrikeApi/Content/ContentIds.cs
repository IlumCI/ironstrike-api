using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IronstrikeApi.Content;

// Byte ids for custom content. The game sends skill, spell and weapon types over the network as a
// single byte, so every kind of content has 255 slots, some taken by the game.
//
// Allocation is deterministic: the content keys are sorted (ordinal) and handed ids from 255
// downward, skipping every id the running game already uses. Two players with the same mods and the
// same game version therefore agree on every id without talking. Counting down from the top keeps
// clear of the developer, who adds new content from the bottom (the October 2026 update inserted a
// weapon at 123 and moved the placeholders up).
internal static class ContentIds
{
    internal const int Highest = 255, Lowest = 1;

    internal static Dictionary<string, int> Allocate(IEnumerable<string> keys, ISet<int> taken)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        int next = Highest;
        foreach (var key in keys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            while (next >= Lowest && taken.Contains(next)) next--;
            if (next < Lowest) throw new InvalidOperationException($"no free ids left for '{key}' (255 slots, {taken.Count} used by the game)");
            map[key] = next--;
        }
        return map;
    }

    // What every peer must agree on: each kind, key and id. Exchanged in the mod-message greeting.
    internal static byte[] Manifest(IEnumerable<(string Kind, string Key, int Id)> entries)
    {
        var lines = entries.Select(e => $"{e.Kind}:{e.Key}={e.Id}").OrderBy(x => x, StringComparer.Ordinal);
        using var sha = SHA256.Create();
        var h = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        return h.Take(8).ToArray();
    }

    // Keys look like "mymod.berserk": lower-case, a mod prefix, no spaces. Enforced so that two mods
    // cannot collide by accident and ids do not depend on capitalisation.
    internal static string Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "the key is empty";
        if (key.Length > 64) return "the key is longer than 64 characters";
        if (!key.Contains('.')) return "the key needs a mod prefix, like \"mymod.berserk\"";
        foreach (char c in key)
            if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-'))
                return $"'{c}' is not allowed (use a-z, 0-9, '.', '_', '-')";
        return null;
    }
}
