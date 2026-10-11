using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Game.Rust.Cui;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- the needs of an animal form (R9.22 - R9.26)
        // Health, hunger and thirst of the animal are shown in the game's own bars, the stamina in a bar above them. Eating and drinking restore them: the Use key at food on the
        // ground, at a corpse (carnivores), at water or at grass (herbivores), and the items of the inventory that are on the animal's diet.
        // The needs are gentle: nothing dies of hunger unless the server owner switches that on. The human body's own needs stay frozen.

        internal StatsModule Needs;

        internal sealed class FormStats
        {
            public float Hunger = 100f;
            public float Thirst = 100f;
            public float NextBite;
            public float HealBudget;            // health the animal may still gain this second from food (heal-per-second cap)
            public float HealWindow;
            public bool Saved;                  // the human's own calories and hydration, put back when the form ends
            public float HumanCalories, HumanHydration;
        }

        // The climate and the needs together slow an animal that is hungry, thirsty or worn out.
        internal float StatsSpeedFactor(BaseEntity animal)
        {
            return Needs != null ? Needs.SpeedFactor(animal) : 1f;
        }

        // The highest gait a worn-out animal can keep up (stamina below 5 % = a jog at most).
        internal int StatsGaitCap(Driver d)
        {
            return Needs != null ? Needs.GaitCap(d) : 5;
        }

        internal void StatsSpend(Driver d, float amount)
        {
            if (Needs != null)
            {
                Needs.Spend(d, amount);
            }
        }

        // Out of breath (stamina): no jumps, a trot at most.
        internal bool OutOfBreath(Driver d)
        {
            return Needs != null && Needs.Exhausted(d);
        }

        // The Use key with nothing else to use: eat or drink what is in front of the animal's head.
        internal bool TryEat(Driver d)
        {
            return Needs != null && Needs.Eat(d);
        }

        // E at food, water or grass: the animal lowers its head (the game's own grazing and drinking pose of livestock, the eating clip of the
        // others) until it walks off or a few seconds have passed.
        internal void StartFeedPose(Driver d, string word)
        {
            if (d == null || d.Animal == null)
            {
                return;                  // the horse has no eating pose to show
            }
            d.InterruptClip();
            LivestockAnimal ls = d.Animal as LivestockAnimal;
            if (ls != null)
            {
                ls.SetGrazing(word != "drink");
                ls.SetDrinking(word == "drink");
            }
            List<ClipRef> clips = Clips(d.Animal).Where(c => Has(c, word) && !Has(c, "death") && !Has(c, "turn") && !Has(c, "walk") && !Has(c, "run")).ToList();
            if (clips.Count == 0)
            {
                clips = Clips(d.Animal).Where(c => (Has(c, "eat") || Has(c, "graze") || Has(c, "drink") || Has(c, "food")) && !Has(c, "death") && !Has(c, "turn") && !Has(c, "walk") && !Has(c, "run")).ToList();
            }
            if (clips.Count > 0)
            {
                d.PlayIdleClip(clips[UnityEngine.Random.Range(0, clips.Count)]);
            }
            d.FeedPoseUntil = Time.time + 3f;
        }

        internal void EndFeedPose(Driver d)
        {
            if (d == null || d.FeedPoseUntil <= 0f)
            {
                return;
            }
            d.FeedPoseUntil = 0f;
            LivestockAnimal ls = d.Animal as LivestockAnimal;
            if (ls != null && Valid(ls))
            {
                ls.SetGrazing(false);
                ls.SetDrinking(false);
            }
        }

        // Headless test of the diets: who eats what, with the food values read from the item definitions.
        internal string TestStats()
        {
            var lines = new List<string>();
            bool ok = true;
            Func<string, string, bool, bool> expect = (species, item, eats) =>
            {
                SpeciesDef sp = SpeciesById(species);
                bool got = sp != null && StatsModule.OnDiet(sp, item);
                if (got != eats) { ok = false; lines.Add("FAIL " + species + " " + (eats ? "should eat " : "should not eat ") + item); }
                return got == eats;
            };
            expect("cow", "corn", true); expect("cow", "bearmeat", false); expect("wolf", "bearmeat", true); expect("wolf", "corn", false);
            expect("bear", "corn", true); expect("bear", "fish.cooked", true); expect("rabbit", "black.berry", true); expect("rabbit", "wolfmeat.raw", false);
            expect("stag", "potato", true); expect("tiger", "chicken.cooked", true); expect("tiger", "pumpkin", false); expect("horse", "apple", true);
            string[] foods = { "corn", "potato", "pumpkin", "mushroom", "black.berry", "apple", "chicken.cooked", "bearmeat", "fish.cooked", "granolabar", "smallwaterbottle", "bandage", "syringe.medical" };
            var parsed = new List<string>();
            foreach (string f in foods)
            {
                Item item = ItemManager.CreateByName(f, 1);
                if (item == null) { parsed.Add(f + "=?"); continue; }
                ItemModConsumable c = item.info.GetComponent<ItemModConsumable>();
                parsed.Add(f + "=" + (c == null ? "no" : c.GetIfType(MetabolismAttribute.Type.Calories) + "/" + c.GetIfType(MetabolismAttribute.Type.Hydration) + "/" + c.GetIfType(MetabolismAttribute.Type.Health)));
                item.Remove();
            }
            lines.Insert(0, (ok ? "PASS" : "FAIL") + " diets of 12 pairs");
            lines.Add("INFO calories/hydration/health: " + string.Join(", ", parsed.ToArray()));
            lines.Add("INFO snow here: " + IsSnow(Vector3.zero) + ", biome map " + (TerrainMeta.BiomeMap != null) + ", topology map " + (TerrainMeta.TopologyMap != null));
            return string.Join("\n", lines.ToArray());
        }

        internal sealed class StatsModule : Module
        {
            public override int Order => 65;

            private readonly Dictionary<ulong, FormStats> stats = new Dictionary<ulong, FormStats>();
            private float lastTick;

            private const string Hud = "BeastmasterHud";

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Needs = this;
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["EatNothing"] = "There is nothing here that a {0} eats or drinks.",
                    ["EatFull"] = "You are not hungry now.",
                    ["Ate"] = "You eat. Hunger {0} %.",
                    ["Drank"] = "You drink. Thirst {0} %.",
                    ["Grazed"] = "You graze. Hunger {0} %.",
                    ["EatRefused"] = "A {0} does not eat {1}.",
                    ["DrinkSalt"] = "Salt water cannot be drunk. Find a river, a lake or a pond.",
                    ["StatHungry"] = "You are starving - find food.",
                    ["StatThirsty"] = "You are parched - find water.",
                    ["StatTired"] = "Out of breath: walk or stand a moment to get the stamina back.",
                };
            }

            public override void OnDisable()
            {
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p != null && p.IsConnected)
                    {
                        CuiHelper.DestroyUi(p, Hud);
                        FormStats old;
                        if (stats.TryGetValue(p.userID, out old))
                        {
                            Restore(p, old);
                        }
                    }
                }
                stats.Clear();
                bars.Clear();
            }

            public override void Unload()
            {
                OnDisable();
            }

            private FormStats Of(BasePlayer p)
            {
                FormStats s;
                if (!stats.TryGetValue(p.userID, out s))
                {
                    s = new FormStats();
                    stats[p.userID] = s;
                }
                return s;
            }

            public float SpeedFactor(BaseEntity animal)
            {
                if (stats.Count == 0)
                {
                    return 1f;
                }
                Driver d = Bm.DriverOfAnimal(animal);
                FormStats s;
                if (d == null || d.Kind != ControlKind.Form || !stats.TryGetValue(d.Player.userID, out s))
                {
                    return 1f;
                }
                float f = 1f;
                if (s.Hunger <= 0.5f) f *= 0.75f;
                if (s.Thirst <= 0.5f) f *= 0.75f;
                return f;
            }

            // ---------------------------------------------------------------- stamina (ridden animals and animal forms, like the horse)

            internal sealed class Breath
            {
                public float Value = 100f;
                public bool Exhausted;
                public float LastSpend;
                public float Seen;
            }

            // Kept per animal, so that a tired animal stays tired when its rider gets off and on again.
            private readonly Dictionary<ulong, Breath> breaths = new Dictionary<ulong, Breath>();

            private Breath BreathOf(Driver d)
            {
                if (d == null || d.Body == null || d.Body.net == null)
                {
                    return null;
                }
                ulong id = d.Body.net.ID.Value;
                Breath b;
                float now = Time.time;
                if (!breaths.TryGetValue(id, out b))
                {
                    b = new Breath { Seen = now };
                    breaths[id] = b;
                }
                else if (now - b.Seen > 2f)
                {
                    // the animal rested while nobody drove it
                    b.Value = Mathf.Min(100f, b.Value + Mathf.Max(0f, Bm.Cfg.Stamina.Recover) * (now - b.Seen));
                    if (b.Value >= Bm.Cfg.Stamina.RecoverTo)
                    {
                        b.Exhausted = false;
                    }
                }
                b.Seen = now;
                return b;
            }

            public float StaminaOf(Driver d)
            {
                Breath b = Bm.Cfg.Stamina.Enabled ? BreathOf(d) : null;
                return b != null ? b.Value : 100f;
            }

            public bool Exhausted(Driver d)
            {
                Breath b = Bm.Cfg.Stamina.Enabled ? BreathOf(d) : null;
                return b != null && b.Exhausted;
            }

            // The highest gait the animal can keep up: out of breath, a trot.
            public int GaitCap(Driver d)
            {
                return Exhausted(d) ? 2 : 5;
            }

            public void Spend(Driver d, float amount)
            {
                if (!Bm.Cfg.Stamina.Enabled || amount <= 0f)
                {
                    return;
                }
                Breath b = BreathOf(d);
                if (b == null)
                {
                    return;
                }
                b.Value = Mathf.Max(0f, b.Value - amount);
                b.LastSpend = Time.time;
                if (!b.Exhausted && b.Value <= Bm.Cfg.Stamina.ExhaustedBelow)
                {
                    b.Exhausted = true;
                    Bm.Tell(d.Player, "StatTired");
                }
            }

            // Running uses the breath, walking and standing give it back.
            private void Breathe(Driver d, float now, float dt)
            {
                StaminaCfg c = Bm.Cfg.Stamina;
                Breath b = BreathOf(d);
                if (b == null)
                {
                    return;
                }
                // what the animal really does counts (a gait chosen while standing or backing up costs nothing)
                float speed = Mathf.Abs(d.Speed);
                float use = d.Standing || d.Speed < 0f ? 0f
                    : speed >= d.GaitSpeed(5) * 0.9f ? c.FullSprint
                    : speed >= d.GaitSpeed(4) * 0.9f ? c.Sprint
                    : speed >= d.GaitSpeed(3) * 0.9f ? c.Run : 0f;
                if (use > 0f)
                {
                    Spend(d, use * dt);
                    return;
                }
                if (now - b.LastSpend < c.RecoverDelay)
                {
                    return;
                }
                b.Value = Mathf.Min(100f, b.Value + Mathf.Max(0f, c.Recover) * dt * (d.Standing ? 1.3f : 1f));
                if (b.Exhausted && b.Value >= c.RecoverTo)
                {
                    b.Exhausted = false;
                }
            }

            // ---------------------------------------------------------------- ticking: drains, recovery, the panel

            public override void Tick(float now)
            {
                float dt = lastTick > 0f ? Mathf.Min(now - lastTick, 1f) : 0.25f;
                lastTick = now;
                if (Bm.Drivers.Count == 0 && stats.Count == 0 && bars.Count == 0)
                {
                    return;
                }
                var present = new HashSet<ulong>();
                var shown = new HashSet<ulong>();
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || d.Player == null || !Valid(d.Body))
                    {
                        continue;
                    }
                    if (d.FeedPoseUntil > 0f && now > d.FeedPoseUntil)
                    {
                        Bm.EndFeedPose(d);
                    }
                    if (Bm.Cfg.Stamina.Enabled)
                    {
                        Breathe(d, now, dt);
                    }
                    if (DrawBars(d, now))
                    {
                        shown.Add(d.Player.userID);
                    }
                    if (d.Kind != ControlKind.Form)
                    {
                        continue;
                    }
                    present.Add(d.Player.userID);
                    Update(d, now, dt);
                }
                foreach (ulong id in bars.Keys.Where(k => !shown.Contains(k)).ToList())
                {
                    BasePlayer gone = BasePlayer.FindByID(id);
                    if (gone != null && gone.IsConnected)
                    {
                        CuiHelper.DestroyUi(gone, Hud);
                    }
                    bars.Remove(id);
                }
                foreach (ulong id in breaths.Where(kv => now - kv.Value.Seen > 900f).Select(kv => kv.Key).ToList())
                {
                    breaths.Remove(id);
                }
                foreach (ulong id in stats.Keys.Where(k => !present.Contains(k)).ToList())
                {
                    BasePlayer gone = BasePlayer.FindByID(id);
                    if ((gone == null || !gone.IsConnected) && Bm.Data.Forms.ContainsKey(id))
                    {
                        continue;           // logged out as an animal: the needs and the human's own food and water wait for the return
                    }
                    if (gone != null && gone.IsConnected)
                    {
                        CuiHelper.DestroyUi(gone, Hud);
                        Restore(gone, stats[id]);
                    }
                    stats.Remove(id);
                }
            }

            private void Update(Driver d, float now, float dt)
            {
                BasePlayer p = d.Player;
                FormStats s = Of(p);
                if (!Bm.Cfg.Form.Needs)
                {
                    return;
                }
                // the game's own bars (health, food, water) show the animal's: the human's values are kept aside and put back later
                if (p.metabolism != null)
                {
                    if (!s.Saved)
                    {
                        s.HumanCalories = p.metabolism.calories.value;
                        s.HumanHydration = p.metabolism.hydration.value;
                        s.Saved = true;
                    }
                    p.metabolism.calories.value = Mathf.Clamp(s.Hunger * p.metabolism.calories.max / 100f, 0f, p.metabolism.calories.max);
                    p.metabolism.hydration.value = Mathf.Clamp(s.Thirst * p.metabolism.hydration.max / 100f, 0f, p.metabolism.hydration.max);
                    p.metabolism.isDirty = true;
                }
                float rate = Bm.Cfg.Form.NeedsRate;
                bool wasHungry = s.Hunger <= 0.5f, wasThirsty = s.Thirst <= 0.5f;
                s.Hunger = Mathf.Max(0f, s.Hunger - 0.045f * rate * dt);
                s.Thirst = Mathf.Max(0f, s.Thirst - 0.07f * rate * dt);
                if (Bm.Cfg.Form.StarveDamage && (s.Hunger <= 0f || s.Thirst <= 0f) && Valid(d.Body))
                {
                    d.Body.Hurt(0.6f * dt, Rust.DamageType.Hunger, null, false);
                }
                if (s.Hunger <= 0.5f && !wasHungry) Bm.Tell(p, "StatHungry");
                if (s.Thirst <= 0.5f && !wasThirsty) Bm.Tell(p, "StatThirsty");
            }

            // ---------------------------------------------------------------- the bars
            // The stamina gets a bar above the game's own bars, the way the horse shows its own; a rider also sees the health of the animal (an
            // animal form sees its health in the game's own health bar). Drawn once, then only updated, so that nothing flickers.

            private sealed class BarState
            {
                public int Stamina = -1;
                public int Health = -1;
                public bool Rider;
                public float At;
                public bool InBase;
                public float BaseCheckAt;
            }

            private readonly Dictionary<ulong, BarState> bars = new Dictionary<ulong, BarState>();

            private const string StaminaColor = "0.86 0.76 0.24 0.85";
            private const string HealthColor = "0.47 0.69 0.24 0.85";

            // True = the bars of this player are up.
            private bool DrawBars(Driver d, float now)
            {
                BasePlayer p = d.Player;
                if (!Bm.Cfg.Stamina.Enabled || !Bm.Cfg.Stamina.ShowBar || !p.IsConnected || (d.Kind == ControlKind.Form && !d.Spectating))
                {
                    return false;
                }
                bool rider = d.Kind != ControlKind.Form;
                int stamina = Mathf.RoundToInt(Mathf.Clamp(StaminaOf(d), 0f, 100f));
                int health = rider ? Mathf.RoundToInt(Mathf.Clamp01(d.Body.health / Mathf.Max(1f, d.Body.MaxHealth())) * 100f) : 0;
                BarState st;
                bool had = bars.TryGetValue(p.userID, out st);
                bool inBase = had && st.InBase;
                if (!had || now >= st.BaseCheckAt)
                {
                    // inside building privilege the game shows two more rows (privilege, upkeep) above its bars: the bar moves up past them
                    inBase = p.GetBuildingPrivilege() != null;
                }
                bool fresh = !had || st.Rider != rider || st.InBase != inBase || now - st.At >= 30f;
                if (had && now >= st.BaseCheckAt)
                {
                    st.BaseCheckAt = now + 1.5f;
                }
                if (!fresh && st.Stamina == stamina && st.Health == health)
                {
                    return true;
                }
                var c = new CuiElementContainer();
                if (fresh)
                {
                    st = new BarState { Rider = rider, At = now, InBase = inBase, BaseCheckAt = now + 1.5f };
                    bars[p.userID] = st;
                    // the size and the row spacing of the game's own bars, right above them; a rider gets the animal's health as a second bar above it
                    StaminaCfg sc = Bm.Cfg.Stamina;
                    float w = Mathf.Max(60f, sc.BarWidth), h = Mathf.Max(10f, sc.BarHeight), gap = 8f;
                    float right = -Mathf.Max(0f, sc.BarRight), bottom = Mathf.Max(0f, sc.BarBottom) + (inBase ? Mathf.Max(0f, sc.BarRaiseInBase) : 0f);
                    float top = bottom + (rider ? h * 2f + gap : h);
                    c.Add(new CuiPanel
                    {
                        Image = { Color = "0 0 0 0" },
                        RectTransform = { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = Pt(right - w, bottom), OffsetMax = Pt(right, top) }
                    }, "Overlay", Hud);
                    float split = rider ? h / (h * 2f + gap) : 1f;
                    AddBar(c, "stamina", StaminaColor, stamina, 0f, split);
                    if (rider)
                    {
                        AddBar(c, "health", HealthColor, health, 1f - split, 1f);
                    }
                    CuiHelper.DestroyUi(p, Hud);
                    CuiHelper.AddUi(p, c);
                }
                else
                {
                    UpdateBar(c, "stamina", StaminaColor, stamina);
                    if (rider)
                    {
                        UpdateBar(c, "health", HealthColor, health);
                    }
                    CuiHelper.AddUi(p, c);
                }
                st.Stamina = stamina;
                st.Health = health;
                return true;
            }

            private static void AddBar(CuiElementContainer c, string id, string color, int value, float y0, float y1)
            {
                string name = Hud + "." + id;
                c.Add(new CuiElement
                {
                    Name = name,
                    Parent = Hud,
                    Components = { new CuiImageComponent { Color = "0.09 0.10 0.08 0.45" }, new CuiRectTransformComponent { AnchorMin = Pt(0f, y0), AnchorMax = Pt(1f, y1) } }
                });
                c.Add(new CuiElement
                {
                    Name = name + ".fill",
                    Parent = name,
                    Components = { new CuiImageComponent { Color = color }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = Pt(value / 100f, 1f) } }
                });
                c.Add(new CuiElement
                {
                    Name = name + ".num",
                    Parent = name,
                    Components = { new CuiTextComponent { Text = value.ToString(), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0.04 0", AnchorMax = "0.6 1" } }
                });
            }

            private static void UpdateBar(CuiElementContainer c, string id, string color, int value)
            {
                string name = Hud + "." + id;
                c.Add(new CuiElement
                {
                    Name = name + ".fill",
                    Update = true,
                    Components = { new CuiImageComponent { Color = color }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = Pt(value / 100f, 1f) } }
                });
                c.Add(new CuiElement
                {
                    Name = name + ".num",
                    Update = true,
                    Components = { new CuiTextComponent { Text = value.ToString(), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0.04 0", AnchorMax = "0.6 1" } }
                });
            }

            // The human's own food and water come back when the animal form ends.
            private static void Restore(BasePlayer p, FormStats s)
            {
                if (p != null && p.metabolism != null && s != null && s.Saved)
                {
                    p.metabolism.calories.value = s.HumanCalories;
                    p.metabolism.hydration.value = s.HumanHydration;
                    p.metabolism.isDirty = true;
                }
            }

            // ---------------------------------------------------------------- eating and drinking

            public bool Eat(Driver d)
            {
                if (d.Kind != ControlKind.Form || !Bm.Cfg.Form.Needs)
                {
                    return false;
                }
                BasePlayer p = d.Player;
                FormStats s = Of(p);
                float now = Time.time;
                if (now < s.NextBite)
                {
                    return true;
                }
                Vector3 head = d.HeadPosition();
                Vector3 fwd = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.forward;
                Vector3 mouth = head + fwd * 1.0f;
                SpeciesDef sp = d.Species;
                // 1. food lying on the ground
                var near = new List<BaseEntity>();
                Vis.Entities(mouth, 1.6f, near);
                foreach (BaseEntity e in near)
                {
                    DroppedItem dropped = e as DroppedItem;
                    if (dropped != null && dropped.item != null)
                    {
                        float calories, hydration, health;
                        if (Consumable(dropped.item, out calories, out hydration, out health) && OnDiet(sp, dropped.item.info.shortname))
                        {
                            Feed(d, s, calories, hydration, health, "Ate");
                            if (dropped.item.amount > 1)
                            {
                                dropped.item.amount -= 1;
                                dropped.item.MarkDirty();
                                dropped.SendNetworkUpdate();
                            }
                            else
                            {
                                dropped.DestroyItem();
                                dropped.Kill();
                            }
                            Bite(d, s, "eat");
                            return true;
                        }
                    }
                }
                // 2. a fresh animal corpse (carnivores and omnivores)
                if (sp.Food == Diet.Carnivore || sp.Food == Diet.Omnivore)
                {
                    foreach (BaseEntity e in near)
                    {
                        BaseCorpse corpse = e as BaseCorpse;
                        if (corpse != null && !(corpse is PlayerCorpse) && !(corpse is LootableCorpse && !(corpse is HorseCorpse)) && !Bm.IsRitualCorpse(corpse))
                        {
                            Feed(d, s, 40f, 0f, 8f, "Ate");
                            corpse.Hurt(corpse.MaxHealth() * 0.34f + 1f, Rust.DamageType.Bite, d.Body, false);
                            Bite(d, s, "eat");
                            return true;
                        }
                    }
                }
                // 3. water: under the animal, at its mouth or in the few metres ahead of it (the shallows count)
                Vector3 drinkSpot = d.Body.transform.position;
                bool water = false;
                foreach (float reach in new[] { 0f, 0.5f, 1.0f, 1.6f, 2.3f })
                {
                    Vector3 q = d.Body.transform.position + fwd * reach;
                    if (WaterLevel.GetWaterDepth(q, true, true, null) > 0.02f)
                    {
                        water = true;
                        drinkSpot = q;
                        break;
                    }
                }
                if (!water && WaterLevel.GetWaterDepth(mouth, true, true, null) > 0.02f)
                {
                    water = true;
                    drinkSpot = mouth;
                }
                if (water)
                {
                    bool sea = IsSeaWater(drinkSpot);
                    if (sea && Bm.Cfg.Form.SeaWaterShare <= 0f)
                    {
                        Bm.Tell(p, "DrinkSalt");
                        s.NextBite = now + 1.5f;
                        return true;
                    }
                    float salt = sea ? Bm.Cfg.Form.SeaWaterShare : 1f;
                    Feed(d, s, 0f, 30f * salt, 1.5f, "Drank");
                    Bite(d, s, "drink");
                    return true;
                }
                // 4. grass (herbivores)
                if (sp.Food == Diet.Herbivore || sp.Food == Diet.Small || sp.Food == Diet.Omnivore)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(mouth + Vector3.up * 0.6f, Vector3.down, out hit, 2.2f, LayerMask.GetMask("Terrain"), QueryTriggerInteraction.Ignore) && !IsSnow(hit.point))
                    {
                        if (s.Hunger > 97f)
                        {
                            Bm.Tell(p, "EatFull");
                            s.NextBite = now + 1.5f;
                            return true;
                        }
                        Feed(d, s, 18f, 0f, 1f, "Grazed");
                        Bite(d, s, "graze");
                        return true;
                    }
                }
                Bm.Tell(p, "EatNothing", sp.Display.ToLowerInvariant());
                s.NextBite = now + 1.5f;
                return true;
            }

            private static bool IsSeaWater(Vector3 pos)
            {
                return TerrainMeta.TopologyMap != null && TerrainMeta.TopologyMap.GetTopology(pos, (int)TerrainTopology.OCEAN);
            }

            private void Bite(Driver d, FormStats s, string word)
            {
                s.NextBite = Time.time + Bm.Cfg.Form.BiteCooldown;
                Bm.StartFeedPose(d, word);
            }

            // Food values come from the item definitions, so changed or modded items work (R9.25).
            private static bool Consumable(Item item, out float calories, out float hydration, out float health)
            {
                calories = hydration = health = 0f;
                ItemModConsumable c = item.info.GetComponent<ItemModConsumable>();
                if (c == null)
                {
                    return false;
                }
                calories = c.GetIfType(MetabolismAttribute.Type.Calories);
                hydration = c.GetIfType(MetabolismAttribute.Type.Hydration);
                health = c.GetIfType(MetabolismAttribute.Type.Health) + c.GetIfType(MetabolismAttribute.Type.HealthOverTime);
                return calories > 0f || hydration > 0f || health > 0f;
            }

            private static readonly string[] MeatWords = { "meat", "fish", "chicken", "pork", "mutton", "bacon", "steak", "crocodile", "snake" };

            // What a species eats: herbivores plants and produce, carnivores meat and fish, omnivores both, small animals seeds and small food.
            public static bool OnDiet(SpeciesDef sp, string shortname)
            {
                bool meat = MeatWords.Any(w => shortname.Contains(w));
                bool drink = shortname.Contains("water") || shortname.Contains("juice") || shortname.Contains("milk");
                if (drink)
                {
                    return true;
                }
                switch (sp.Food)
                {
                    case Diet.Carnivore: return meat;
                    case Diet.Omnivore: return true;
                    case Diet.Small: return !meat && (shortname.Contains("seed") || shortname.Contains("berry") || shortname.Contains("corn") || shortname.Contains("bread") || shortname.Contains("mushroom") || shortname.Contains("apple") || shortname.Contains("pickle") || shortname.Contains("granola") || shortname.Contains("grub") || shortname.Contains("worm"));
                    default: return !meat;
                }
            }

            private void Feed(Driver d, FormStats s, float calories, float hydration, float health, string message)
            {
                s.Hunger = Mathf.Min(100f, s.Hunger + calories * 0.25f);
                s.Thirst = Mathf.Min(100f, s.Thirst + hydration * 0.5f);
                if (health > 0f)
                {
                    Heal(d, s, health);
                }
                Bm.Tell(d.Player, message, Mathf.RoundToInt(message == "Drank" ? s.Thirst : s.Hunger));
            }

            // Healing from food is capped per second (R9.26).
            private void Heal(Driver d, FormStats s, float amount)
            {
                float now = Time.time;
                if (now - s.HealWindow > 1f)
                {
                    s.HealWindow = now;
                    s.HealBudget = Bm.Cfg.Form.HealPerSecond;
                }
                float gain = Mathf.Min(amount, s.HealBudget);
                s.HealBudget -= gain;
                if (gain > 0f && Valid(d.Body))
                {
                    d.Body.health = Mathf.Min(d.Body.MaxHealth(), d.Body.health + gain);
                    d.Body.SendNetworkUpdate();
                }
            }

            // ---------------------------------------------------------------- the inventory: food, drink and medicine act on the animal

            public override object OnItemAction(Item item, string action, BasePlayer player)
            {
                if (player == null || action != "consume" || !Bm.Cfg.Form.Needs)
                {
                    return null;
                }
                Driver d = Bm.DriverOf(player);
                if (d == null || d.Kind != ControlKind.Form)
                {
                    return null;
                }
                float calories, hydration, health;
                if (!Consumable(item, out calories, out hydration, out health))
                {
                    return null;
                }
                if (!OnDiet(d.Species, item.info.shortname) && calories + hydration > 0f && health <= 0f)
                {
                    Bm.Tell(player, "EatRefused", d.Species.Display.ToLowerInvariant(), item.info.displayName.english.ToLowerInvariant());
                    return true;
                }
                FormStats s = Of(player);
                if (Time.time < s.NextBite)
                {
                    return true;
                }
                s.NextBite = Time.time + Bm.Cfg.Form.BiteCooldown;
                Feed(d, s, calories, hydration, health, hydration > calories ? "Drank" : "Ate");
                item.UseItem(1);
                return true;
            }

            // Bandage and syringe used on oneself heal the animal, which is the player's health (R9.45).
            public override object OnHealingItemUse(MedicalTool tool, BasePlayer fromPlayer, IMedicalToolTarget toTarget)
            {
                Driver d = fromPlayer != null ? Bm.DriverOf(fromPlayer) : null;
                if (d == null || d.Kind != ControlKind.Form || (object)toTarget != (object)fromPlayer)
                {
                    return null;
                }
                ItemModConsumable c = tool.GetItem() != null ? tool.GetItem().info.GetComponent<ItemModConsumable>() : null;
                float health = c != null ? c.GetIfType(MetabolismAttribute.Type.Health) + c.GetIfType(MetabolismAttribute.Type.HealthOverTime) : 15f;
                Heal(d, Of(fromPlayer), Mathf.Max(health, 10f));
                tool.GetItem().UseItem(1);
                return true;
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Needs: " + stats.Count + " animal forms tracked, the needs are " + (Bm.Cfg.Form.Needs ? "on" : "off") + (Bm.Cfg.Form.StarveDamage ? ", starvation hurts" : ", nothing starves"));
            }
        }
    }
}
