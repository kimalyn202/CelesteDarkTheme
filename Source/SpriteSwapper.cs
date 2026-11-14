using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Monocle;

namespace Celeste.Mod.DarkTheme;

static class SpriteSwapper
{
    private struct ThemedSprite
    {
        public string Key;
        public string Dir;
        public string BaseLeaf;
        public string Prefix;
    }

    static readonly string[] AllPrefixes = ["lb_", "inv_", "af_"];
    static string[] PriorityFor(SpriteTheme theme)
    {
        return theme switch
        {
            SpriteTheme.LowBrightness => ["lb_"],
            SpriteTheme.Inverted => ["inv_", "lb_"],
            SpriteTheme.Aurafarm => ["af_", "inv_", "lb_"],
            _ => Array.Empty<string>()
        };
    }
    public static int PairCount { get; private set; } = 0;

    static readonly Dictionary<MTexture, MTexture> Pristine = new();
    static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    static readonly List<Atlas> Atlases = [MTN.FileSelect, MTN.Journal, GFX.Gui];

    static IEnumerable<FieldInfo>? MTexFields = null;

    static void CopyState(MTexture src, MTexture dst)
    {
        MTexFields ??= typeof(MTexture).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (FieldInfo field in MTexFields) field.SetValue(dst, field.GetValue(src));        
    }

    public static void Reset()
    {
        Pristine.Clear();
        PairCount = 0;
    }


    public static void Apply(SpriteTheme theme)
    {
        int pairs = 0;
        int swapped = 0;
        string[] priority = PriorityFor(theme);

        foreach (Atlas atlas in Atlases)
        {
            if (atlas == null) continue;
            Dictionary<string, MTexture> dict = atlas.Textures;

            Dictionary<string, List<string>> byLeaf = new(StringComparer.OrdinalIgnoreCase);
            List<ThemedSprite> themed = new();

            foreach (string key in dict.Keys)
            {
                int slash = key.LastIndexOf('/');

                string leaf = key.Substring(slash + 1);
                string? prefix = PrefixOf(leaf);
                
                if (prefix != null)
                {
                    themed.Add(new() { 
                        Key = key, 
                        Dir = slash >= 0 ? key.Substring(0, slash + 1) : "", 
                        BaseLeaf = leaf.Substring(prefix.Length), 
                        Prefix = prefix });
                }
                else
                {
                    if (!byLeaf.TryGetValue(leaf, out List<string>? list)) byLeaf[leaf] = list = new();
                    list.Add(key);
                }
            }

            Dictionary<string, Dictionary<string, MTexture>> perTarget = new();

            foreach (ThemedSprite sprite in themed)
            {
                bool found = false;
                string targetKey = "";

                if (dict.ContainsKey(sprite.Dir + sprite.BaseLeaf))
                {
                    found = true;
                    targetKey = sprite.Dir + sprite.BaseLeaf;
                }
                else if (byLeaf.TryGetValue(sprite.BaseLeaf, out List<string>? cands))
                {
                    if (cands.Count == 1)
                    {
                        found = true;
                        targetKey = cands[0];
                        Logging.Warn($"'{sprite.Key}' is not next to its original; using '{targetKey}'.");
                    }
                    else Logging.Warn($"'{sprite.Key}' matches several originals ({string.Join(", ", cands)}). Move it into the folder of the one you want.");
                }
                else Logging.Warn($"'{sprite.Key}' has no original named '{sprite.BaseLeaf}' anywhere in this atlas.");
                

                if (!found) continue;

                if (!perTarget.TryGetValue(targetKey, out var variants))
                    perTarget[targetKey] = variants = new Dictionary<string, MTexture>();

                variants[sprite.Prefix] = dict[sprite.Key];
            }

            foreach ((string key, Dictionary<string, MTexture> textures) in perTarget)
            {
                MTexture target = dict[key];
                pairs++;

                if (!Pristine.TryGetValue(target, out MTexture? original))
                {
                    original = (MTexture)MemberwiseCloneMethod.Invoke(target, null)!;
                    Pristine[target] = original;
                }

                MTexture source = original;
                foreach (string prefix in priority)
                {
                    if (textures.TryGetValue(prefix, out MTexture? variant) && variant != null)
                    {
                        source = variant;
                        swapped++;
                        break;
                    }
                }
                CopyState(source, target);
            }
        }

        PairCount = pairs;
        Logging.Log($"Theme {theme}: {pairs} sprite(s) have themed variants, {swapped} swapped in.");


        static string? PrefixOf(string leaf)
        {
            foreach (string prefix in AllPrefixes)
                if (leaf.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return prefix;

            return null;
        }
    }
}
