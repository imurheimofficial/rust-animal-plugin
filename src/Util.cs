using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- client RPCs without naming RpcTarget
        // RpcTarget pulls ReadOnlySpan into the signature, which plugin compilers do not always resolve, so it is called by reflection.

        private static readonly MethodInfo RpcTargetPlayerMethod = typeof(BaseEntity).Assembly.GetType("RpcTarget")?.GetMethod("Player", new[] { typeof(string), typeof(BasePlayer) });
        private static readonly MethodInfo ClientRpcNoArg = typeof(BaseEntity).GetMethods().FirstOrDefault(m => m.Name == "ClientRPC" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "RpcTarget");

        internal static void RpcTo(BaseEntity entity, BasePlayer target, string name)
        {
            if (RpcTargetPlayerMethod == null || ClientRpcNoArg == null || entity == null || target == null || !target.IsConnected)
            {
                return;
            }
            ClientRpcNoArg.Invoke(entity, new object[] { RpcTargetPlayerMethod.Invoke(null, new object[] { name, target }) });
        }

        // ---------------------------------------------------------------- effects

        private readonly Dictionary<string, string> effectCache = new Dictionary<string, string>();

        // Finds a prefab path in the game manifest by words (every word has to be in the path), caches it.
        internal string FindPrefabPath(params string[] words)
        {
            string key = string.Join("|", words);
            string path;
            if (effectCache.TryGetValue(key, out path))
            {
                return path;
            }
            path = null;
            foreach (string candidate in GameManifest.Current.pooledStrings.Select(p => p.str))
            {
                string lower = candidate.ToLowerInvariant();
                if (lower.EndsWith(".prefab") && words.All(w => lower.Contains(w)))
                {
                    path = candidate;
                    break;
                }
            }
            effectCache[key] = path;
            return path;
        }

        // Makes an entity look bigger (or normal again) on every client: the game sends the transform's scale of an entity whose
        // networkEntityScale flag is set (the admin command "entity scale" does exactly this).
        internal void ScaleModel(BaseEntity e, float scale)
        {
            if (!Valid(e))
            {
                return;
            }
            if (Mathf.Approximately(scale, 1f))
            {
                if (e.networkEntityScale)
                {
                    e.networkEntityScale = false;
                    e.transform.localScale = Vector3.one;
                    e.SendNetworkUpdate();
                }
                return;
            }
            e.networkEntityScale = true;
            e.transform.localScale = new Vector3(scale, scale, scale);
            e.SendNetworkUpdate();
        }

        // The size of a model relative to its prefab (reach of attacks grows with it).
        internal static float ModelScale(BaseEntity e)
        {
            return e != null ? Mathf.Max(1f, e.transform.localScale.x) : 1f;
        }

        // Everything the player carries: backpack, belt and worn clothes.
        internal static List<Item> PlayerItems(BasePlayer p)
        {
            var list = new List<Item>();
            if (p == null || p.inventory == null)
            {
                return list;
            }
            if (p.inventory.containerMain != null) list.AddRange(p.inventory.containerMain.itemList);
            if (p.inventory.containerBelt != null) list.AddRange(p.inventory.containerBelt.itemList);
            if (p.inventory.containerWear != null) list.AddRange(p.inventory.containerWear.itemList);
            return list;
        }

        // Admin helper: the ENTITY prefabs (things the server can spawn and network) that contain all the words, with their game class.
        internal string ListEntities(string[] words)
        {
            if (words == null || words.Length == 0)
            {
                return "Usage: bm.entities <word> [word ...]   (lists up to 60 spawnable entity prefabs with their class)";
            }
            var found = new List<string>();
            foreach (string path in GameManifest.Current.entities)
            {
                string lower = path.ToLowerInvariant();
                if (!words.All(w => lower.Contains(w.ToLowerInvariant())))
                {
                    continue;
                }
                GameObject go = GameManager.server.FindPrefab(path);
                BaseEntity be = go != null ? go.GetComponent<BaseEntity>() : null;
                found.Add(path + "  [" + (be != null ? be.GetType().Name : "?") + "]");
                if (found.Count >= 60)
                {
                    break;
                }
            }
            return found.Count == 0 ? "No entity matches." : string.Join("\n", found.ToArray());
        }

        // Admin helper: the items of this game build whose shortname or name contains all the words.
        internal string ListItems(string[] words)
        {
            if (words == null || words.Length == 0)
            {
                return "Usage: bm.items <word> [word ...]   (lists up to 60 items: shortname = display name)";
            }
            var found = new List<string>();
            foreach (ItemDefinition def in ItemManager.itemList)
            {
                string key = (def.shortname + " " + def.displayName.english).ToLowerInvariant();
                if (words.All(w => key.Contains(w.ToLowerInvariant())))
                {
                    found.Add(def.shortname + " = " + def.displayName.english);
                    if (found.Count >= 60)
                    {
                        break;
                    }
                }
            }
            return found.Count == 0 ? "No item matches." : string.Join("\n", found.ToArray());
        }

        // Admin helper: the prefab paths of this game build that contain all the words (for choosing effects and bodies).
        internal string ListPrefabs(string[] words)
        {
            if (words == null || words.Length == 0)
            {
                return "Usage: bm.prefabs <word> [word ...]   (lists up to 60 prefab paths that contain every word)";
            }
            var found = new List<string>();
            foreach (string candidate in GameManifest.Current.pooledStrings.Select(p => p.str))
            {
                string lower = candidate.ToLowerInvariant();
                if (lower.EndsWith(".prefab") && words.All(w => lower.Contains(w.ToLowerInvariant())))
                {
                    found.Add(candidate);
                    if (found.Count >= 60)
                    {
                        break;
                    }
                }
            }
            return found.Count == 0 ? "No prefab matches." : string.Join("\n", found.ToArray());
        }

        // Admin helper: any asset path of this game build (sprites, textures, sounds ...) that contains all the words.
        internal string ListAssets(string[] words)
        {
            if (words == null || words.Length == 0)
            {
                return "Usage: bm.assets <word> [word ...]   (lists up to 80 asset paths of any kind)";
            }
            var found = new List<string>();
            foreach (string candidate in GameManifest.Current.pooledStrings.Select(p => p.str))
            {
                string lower = candidate.ToLowerInvariant();
                if (words.All(w => lower.Contains(w.ToLowerInvariant())))
                {
                    found.Add(candidate);
                    if (found.Count >= 80)
                    {
                        break;
                    }
                }
            }
            return found.Count == 0 ? "No asset matches." : string.Join(Environment.NewLine, found.ToArray());
        }

        internal void RunEffect(string prefab, Vector3 position)
        {
            if (string.IsNullOrEmpty(prefab))
            {
                return;
            }
            Effect.server.Run(prefab, position, Vector3.up);
        }

        // The smoke of a transformation (config path, else the nearest looking effect of the game).
        internal void FormSmoke(Vector3 position, BasePlayer hearer = null)
        {
            if (!Cfg.Form.Smoke)
            {
                return;
            }
            string prefab = Cfg.Form.SmokePrefab;
            if (string.IsNullOrEmpty(prefab) || GameManager.server.FindPrefab(prefab) == null)
            {
                prefab = FindPrefabPath("fx", "smoke") ?? FindPrefabPath("fx", "poof") ?? FindPrefabPath("fx", "explosion", "small");
            }
            RunEffectFor(prefab, position + Vector3.up * 0.8f, hearer);
        }

        // The human shows up again: the game's appear effect with its sound.
        internal void FormAppear(Vector3 position, BasePlayer hearer = null)
        {
            if (!Cfg.Form.Smoke)
            {
                return;
            }
            string prefab = Cfg.Form.AppearPrefab;
            if (!string.IsNullOrEmpty(prefab) && GameManager.server.FindPrefab(prefab) != null)
            {
                RunEffectFor(prefab, position + Vector3.up * 0.8f, hearer);
            }
        }

        // The effect for everybody near, and sent straight to the player's own connection: he hears and sees it whatever the networking of his
        // body is at that moment (it is hidden right after the transformation).
        internal void RunEffectFor(string prefab, Vector3 position, BasePlayer hearer)
        {
            if (string.IsNullOrEmpty(prefab))
            {
                return;
            }
            if (hearer == null || !hearer.IsConnected || hearer.net == null || hearer.net.connection == null)
            {
                Effect.server.Run(prefab, position, Vector3.up);
                return;
            }
            Effect.server.Run(prefab, position, Vector3.up, hearer.net.connection);
            EffectNetwork.Send(new Effect(prefab, position, Vector3.up), hearer.net.connection);
        }

        // The game has a setting that switches the player's HUD off; the plugin asks the player's game to use it while the player is an animal
        // (and to switch it on for the inventory view and when the form ends).
        internal void HudSwitch(BasePlayer player, bool show)
        {
            if (player != null && player.IsConnected && Cfg.Form.HideHud)
            {
                player.SendConsoleCommand("graphics.hud " + (show ? "1" : "0"));
            }
        }

        // ---------------------------------------------------------------- positions

        // A standing spot on the ground next to a position (for returning to human form).
        internal static Vector3 GroundNear(Vector3 position, float radius)
        {
            Vector3 best = position;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                Vector3 p = position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                p.y = TerrainMeta.HeightMap.GetHeight(p) + 0.3f;
                if (!Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.4f, LayerMask.GetMask("World", "Construction", "Deployed", "Tree"), QueryTriggerInteraction.Ignore))
                {
                    return p;
                }
                best = p;
            }
            return best;
        }

        internal static string Pretty(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        internal string PresetOf(BasePlayer player)
        {
            string preset = player != null ? SettingsOf(player.userID).Preset : null;
            return string.IsNullOrEmpty(preset) ? Cfg.Camera.ThirdPreset : preset;
        }

        internal BasePlayer FindPlayer(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            ulong id;
            if (ulong.TryParse(text, out id))
            {
                BasePlayer byId = BasePlayer.FindByID(id) ?? BasePlayer.FindSleeping(id);
                if (byId != null)
                {
                    return byId;
                }
            }
            BasePlayer partial = null;
            foreach (BasePlayer p in BasePlayer.activePlayerList)
            {
                if (p.displayName.Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
                if (partial == null && p.displayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    partial = p;
                }
            }
            return partial;
        }
    }
}
