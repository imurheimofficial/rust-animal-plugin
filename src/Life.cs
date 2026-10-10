using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oxide.Game.Rust.Cui;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- the life of a played animal
        // The prompt of E on the screen of an animal form (open the door, loot, eat, drink, lead), the dung a played horse, cow or sheep leaves
        // now and then, the egg a played chicken very rarely lays, the rope of the game that an animal form must not show, and the wild animals
        // that do not jump over things (only an animal a player controls jumps).

        // An item by its short name, or - when this game build calls it differently - the first item whose name has that word in it.
        internal static ItemDefinition ItemLike(string preferred, string word, string exclude)
        {
            ItemDefinition def = string.IsNullOrEmpty(preferred) ? null : ItemManager.FindItemDefinition(preferred);
            if (def != null)
            {
                return def;
            }
            foreach (ItemDefinition d in ItemManager.itemList)
            {
                string n = d.shortname;
                if (n == word || ((n.StartsWith(word + ".") || n.EndsWith("." + word)) && (exclude == null || !n.Contains(exclude))))
                {
                    return d;
                }
            }
            return null;
        }

        internal sealed class LifeModule : Module
        {
            public override int Order => 60;

            private const string PromptUi = "BeastmasterPrompt";

            private readonly Dictionary<ulong, string> prompts = new Dictionary<ulong, string>();
            private readonly Dictionary<ulong, float> nextDung = new Dictionary<ulong, float>();
            private float nextPrompt, nextMinute, nextRope;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Subscribe("OnEntitySpawned");
            }

            public override void OnServerInitialized()
            {
                if (!Bm.Cfg.Life.NoWildJumps)
                {
                    return;
                }
                int n = 0;
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                {
                    BaseNPC2 npc = net as BaseNPC2;
                    if (npc != null && NoJumps(npc))
                    {
                        n++;
                    }
                }
                Bm.Log(3, "Wild jumps switched off on " + n + " animals (" + JumpReport + ")");
            }

            public override void OnEntitySpawned(BaseNetworkable entity)
            {
                BaseNPC2 npc = entity as BaseNPC2;
                if (npc == null || !Bm.Cfg.Life.NoWildJumps)
                {
                    return;
                }
                Bm.NextTick(() =>
                {
                    if (Valid(npc))
                    {
                        NoJumps(npc);
                    }
                });
            }

            public override void Tick(float now)
            {
                if (now >= nextPrompt)
                {
                    nextPrompt = now + 0.3f;
                    Prompts();
                }
                if (now >= nextRope)
                {
                    nextRope = now + 1f;
                    HideRopes();
                }
                if (now >= nextMinute)
                {
                    float step = nextMinute <= 0f ? 0f : 5f;
                    nextMinute = now + 5f;
                    if (step > 0f)
                    {
                        Droppings(now, step);
                    }
                }
            }

            public override void OnPlayerDisconnected(BasePlayer player, string reason)
            {
                prompts.Remove(player.userID);
            }

            public override void OnDisable()
            {
                Unload();
            }

            public override void Unload()
            {
                foreach (ulong id in prompts.Keys.ToList())
                {
                    BasePlayer p = BasePlayer.FindByID(id);
                    if (p != null && p.IsConnected)
                    {
                        CuiHelper.DestroyUi(p, PromptUi);
                    }
                }
                prompts.Clear();
                RestoreJumps();
            }

            // ------------------------------------------------------------ the prompt of E

            private void Prompts()
            {
                var shown = new HashSet<ulong>();
                if (Bm.Cfg.Life.Prompts)
                {
                    foreach (Driver d in Bm.Drivers.Values.ToList())
                    {
                        if (d == null || !d.Alive || d.Kind != ControlKind.Form || !d.Spectating || d.Player == null || !d.Player.IsConnected)
                        {
                            continue;
                        }
                        string text = null;
                        try
                        {
                            text = Bm.UsePrompt(d);
                        }
                        catch (Exception ex)
                        {
                            Bm.Log(1, "Prompt: " + ex.Message);
                        }
                        Draw(d.Player, text);
                        shown.Add(d.Player.userID);
                    }
                }
                foreach (ulong id in prompts.Keys.Where(k => !shown.Contains(k)).ToList())
                {
                    BasePlayer p = BasePlayer.FindByID(id);
                    if (p != null && p.IsConnected)
                    {
                        CuiHelper.DestroyUi(p, PromptUi);
                    }
                    prompts.Remove(id);
                }
            }

            private void Draw(BasePlayer p, string text)
            {
                string old;
                if (prompts.TryGetValue(p.userID, out old) && old == text)
                {
                    return;
                }
                prompts[p.userID] = text;
                CuiHelper.DestroyUi(p, PromptUi);
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }
                var c = new CuiElementContainer();
                c.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.07 0.05 0.55" },
                    RectTransform = { AnchorMin = "0.5 0.38", AnchorMax = "0.5 0.38", OffsetMin = "-150 -15", OffsetMax = "150 15" }
                }, "Overlay", PromptUi);
                c.Add(new CuiLabel
                {
                    Text = { Text = "<color=#e8c35a><b>E</b></color>   " + text, FontSize = 15, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.95" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, PromptUi);
                CuiHelper.AddUi(p, c);
            }

            // ------------------------------------------------------------ dung and eggs

            private void Droppings(float now, float seconds)
            {
                LifeCfg cfg = Bm.Cfg.Life;
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || d.Kind != ControlKind.Form || d.Species == null || !Valid(d.Body) || d.Body.IsDead())
                    {
                        continue;
                    }
                    if (WaterLevel.GetWaterDepth(d.Body.transform.position, true, true, null) > 0.3f)
                    {
                        continue;                         // not while swimming
                    }
                    ulong id = d.Player.userID;
                    if (cfg.Dung && cfg.DungSpecies != null && cfg.DungSpecies.Contains(d.Species.Id))
                    {
                        float due;
                        if (!nextDung.TryGetValue(id, out due))
                        {
                            nextDung[id] = now + NextDungDelay();
                        }
                        else if (now >= due)
                        {
                            nextDung[id] = now + NextDungDelay();
                            Drop(d, ItemLike(cfg.DungItem, "dung", null), 1);
                        }
                    }
                    if (cfg.Eggs && d.Species.Id == "chicken" && UnityEngine.Random.value * 100f < Mathf.Max(0f, cfg.EggChancePerMinute) * seconds / 60f)
                    {
                        float roll = UnityEngine.Random.value;
                        Drop(d, ItemLike(cfg.EggItem, "egg", "easter"), roll < 0.8f ? 1 : roll < 0.95f ? 2 : 3);
                    }
                }
                foreach (ulong id in nextDung.Keys.ToList())
                {
                    if (!Bm.Drivers.ContainsKey(id))
                    {
                        nextDung.Remove(id);
                    }
                }
            }

            private float NextDungDelay()
            {
                float min = Mathf.Max(0.5f, Bm.Cfg.Life.DungMinMinutes);
                float max = Mathf.Max(min, Bm.Cfg.Life.DungMaxMinutes);
                return UnityEngine.Random.Range(min, max) * 60f;
            }

            // Behind the animal, on the ground.
            private void Drop(Driver d, ItemDefinition def, int amount)
            {
                if (def == null)
                {
                    return;
                }
                Bounds b = d.Body.bounds;
                Vector3 back = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.back;
                Vector3 center = d.Body.transform.TransformPoint(b.center);
                Vector3 at = center + back * (Mathf.Max(b.extents.x, b.extents.z) * ModelScale(d.Body) + 0.1f);
                RaycastHit hit;
                if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out hit, 4f, LayerMask.GetMask("Terrain", "World", "Construction", "Default"), QueryTriggerInteraction.Ignore))
                {
                    at = hit.point + Vector3.up * 0.15f;
                }
                Item item = ItemManager.Create(def, amount, 0UL);
                if (item != null)
                {
                    item.Drop(at, back * 0.3f);
                }
            }

            // ------------------------------------------------------------ the rope of the game

            // An animal form leads the way a player in animal form does: invisibly. A rope of the game that is still tied to the hidden human
            // (taken before the transformation, or tied again by the horse code) is taken off; the animal follows the form on the plugin's own
            // invisible lead instead.
            private void HideRopes()
            {
                if (Bm.Drivers.Count == 0)
                {
                    return;
                }
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || d.Kind != ControlKind.Form || d.Player == null || !Valid(d.Body))
                    {
                        continue;
                    }
                    var near = new List<BaseEntity>();
                    Vis.Entities(d.Body.transform.position, 20f, near);
                    foreach (BaseEntity e in near)
                    {
                        RidableHorse horse = e as RidableHorse;
                        if (horse != null && horse != d.Body && horse.IsLeading && horse.leadingPlayer == d.Player)
                        {
                            horse.SetLeading(null);
                            HorseState s = Bm.Stable != null ? Bm.Stable.StateOf(horse) : null;
                            if (s != null)
                            {
                                s.WasLeading = false;
                                s.Tether = d.Player;          // follows on the invisible tether of the horse code
                            }
                            continue;
                        }
                        LivestockAnimal ls = e as LivestockAnimal;
                        if (ls == null || ls == d.Body || !ls.IsLeading() || ls.LeadingPlayer.Get(true) != d.Player)
                        {
                            continue;
                        }
                        if (StopVanillaLead(ls) && Bm.Leads != null && Bm.Cfg.Features.Lead && !Bm.Leads.ChainOf(d).Contains(ls) && Bm.Leads.ChainOf(d).Count < Bm.Cfg.Lead.MaxChain)
                        {
                            Bm.Leads.Lead(d, ls);
                        }
                    }
                }
            }

            private static MethodInfo stopLead;
            private static bool stopLeadWithPlayer;
            private static bool stopLeadLooked;

            private static bool StopVanillaLead(LivestockAnimal ls)
            {
                if (!stopLeadLooked)
                {
                    stopLeadLooked = true;
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (string name in new[] { "StopLeading", "StopLead", "EndLeading", "ClearLeading", "ReleaseLead" })
                    {
                        stopLead = typeof(LivestockAnimal).GetMethod(name, flags, null, Type.EmptyTypes, null);
                        if (stopLead != null)
                        {
                            break;
                        }
                    }
                    if (stopLead == null)
                    {
                        foreach (string name in new[] { "SetLeading", "SetLeadingPlayer", "StartLeading" })
                        {
                            stopLead = typeof(LivestockAnimal).GetMethod(name, flags, null, new[] { typeof(BasePlayer) }, null);
                            if (stopLead != null)
                            {
                                stopLeadWithPlayer = true;
                                break;
                            }
                        }
                    }
                }
                if (stopLead == null)
                {
                    return false;
                }
                try
                {
                    stopLead.Invoke(ls, stopLeadWithPlayer ? new object[] { null } : new object[0]);
                    return !ls.IsLeading();
                }
                catch
                {
                    return false;
                }
            }

            // ------------------------------------------------------------ wild animals do not jump

            private static readonly List<FieldInfo> jumpSwitches = new List<FieldInfo>();
            private static readonly List<PropertyInfo> jumpSwitchProps = new List<PropertyInfo>();
            private static FieldInfo unityAgentField;
            private static PropertyInfo areaMaskProperty;
            private static int jumpAreaBit;
            private static bool jumpLooked;
            internal static string JumpReport = "not looked at yet";
            private readonly HashSet<BaseNPC2> jumpless = new HashSet<BaseNPC2>();

            private static void LookUpJumpSwitches()
            {
                if (jumpLooked)
                {
                    return;
                }
                jumpLooked = true;
                var found = new List<string>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                try
                {
                    for (Type t = typeof(RustNavMeshAgent); t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
                    {
                        foreach (FieldInfo f in t.GetFields(flags))
                        {
                            string n = f.Name.ToLowerInvariant();
                            if (f.FieldType == typeof(bool) && n.Contains("jump") && !n.Contains("isjumping") && n != "jumping" && (n.Contains("can") || n.Contains("allow") || n.Contains("enable") || n.Contains("use")))
                            {
                                jumpSwitches.Add(f);
                                found.Add(f.Name);
                            }
                            if (unityAgentField == null && f.FieldType.FullName == "UnityEngine.AI.NavMeshAgent")
                            {
                                unityAgentField = f;
                            }
                        }
                        foreach (PropertyInfo p in t.GetProperties(flags))
                        {
                            string n = p.Name.ToLowerInvariant();
                            if (p.PropertyType == typeof(bool) && p.CanWrite && p.GetIndexParameters().Length == 0 && n.Contains("jump") && !n.Contains("isjumping") && (n.Contains("can") || n.Contains("allow") || n.Contains("enable")))
                            {
                                jumpSwitchProps.Add(p);
                                found.Add(p.Name);
                            }
                        }
                    }
                    if (unityAgentField != null)
                    {
                        Type agentType = unityAgentField.FieldType;
                        areaMaskProperty = agentType.GetProperty("areaMask", BindingFlags.Instance | BindingFlags.Public);
                        Type navMesh = agentType.Assembly.GetType("UnityEngine.AI.NavMesh");
                        MethodInfo area = navMesh != null ? navMesh.GetMethod("GetAreaFromName", new[] { typeof(string) }) : null;
                        int index = area != null ? (int)area.Invoke(null, new object[] { "Jump" }) : -1;
                        jumpAreaBit = index >= 0 && index < 32 ? 1 << index : 0;
                        if (areaMaskProperty != null && jumpAreaBit != 0)
                        {
                            found.Add("navmesh area Jump");
                        }
                    }
                }
                catch (Exception ex)
                {
                    found.Add("lookup failed: " + ex.Message);
                }
                JumpReport = found.Count > 0 ? string.Join(", ", found.ToArray()) : "this game build has no jump switch on the animals";
            }

            private bool NoJumps(BaseNPC2 npc)
            {
                LookUpJumpSwitches();
                RustNavMeshAgent agent = npc.GetComponent<RustNavMeshAgent>();
                if (agent == null)
                {
                    return false;
                }
                bool changed = false;
                try
                {
                    foreach (FieldInfo f in jumpSwitches)
                    {
                        f.SetValue(agent, false);
                        changed = true;
                    }
                    foreach (PropertyInfo p in jumpSwitchProps)
                    {
                        p.SetValue(agent, false, null);
                        changed = true;
                    }
                    if (unityAgentField != null && areaMaskProperty != null && jumpAreaBit != 0)
                    {
                        object unityAgent = unityAgentField.GetValue(agent);
                        if (unityAgent != null)
                        {
                            int mask = (int)areaMaskProperty.GetValue(unityAgent, null);
                            areaMaskProperty.SetValue(unityAgent, mask & ~jumpAreaBit, null);
                            changed = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Bm.Log(1, "Switching off the jumps of " + npc.ShortPrefabName + " failed: " + ex.Message);
                }
                if (changed)
                {
                    jumpless.Add(npc);
                }
                return changed;
            }

            private void RestoreJumps()
            {
                foreach (BaseNPC2 npc in jumpless)
                {
                    if (!Valid(npc))
                    {
                        continue;
                    }
                    RustNavMeshAgent agent = npc.GetComponent<RustNavMeshAgent>();
                    if (agent == null)
                    {
                        continue;
                    }
                    try
                    {
                        foreach (FieldInfo f in jumpSwitches)
                        {
                            f.SetValue(agent, true);
                        }
                        foreach (PropertyInfo p in jumpSwitchProps)
                        {
                            p.SetValue(agent, true, null);
                        }
                        if (unityAgentField != null && areaMaskProperty != null && jumpAreaBit != 0)
                        {
                            object unityAgent = unityAgentField.GetValue(agent);
                            if (unityAgent != null)
                            {
                                areaMaskProperty.SetValue(unityAgent, (int)areaMaskProperty.GetValue(unityAgent, null) | jumpAreaBit, null);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                jumpless.Clear();
            }

            public override void SelfTest(List<string> report)
            {
                LookUpJumpSwitches();
                ItemDefinition dung = ItemLike(Bm.Cfg.Life.DungItem, "dung", null);
                ItemDefinition egg = ItemLike(Bm.Cfg.Life.EggItem, "egg", "easter");
                report.Add("Life: dung item " + (dung != null ? dung.shortname : "MISSING") + ", egg item " + (egg != null ? egg.shortname : "MISSING")
                    + "; wild jumps off on " + jumpless.Count + " animals (" + JumpReport + "); vanilla rope release " + (stopLead != null ? stopLead.Name : stopLeadLooked ? "MISSING" : "not needed yet"));
            }
        }
    }
}
