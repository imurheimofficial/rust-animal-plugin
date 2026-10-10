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
        // Health, Stamina, Hunger and Thirst of the animal are shown on a small panel. Eating and drinking restore them: the Use key at food on the
        // ground, at a corpse (carnivores), at water or at grass (herbivores), and the items of the inventory that are on the animal's diet.
        // The needs are gentle: nothing dies of hunger unless the server owner switches that on. The human body's own needs stay frozen.

        internal StatsModule Needs;

        internal sealed class FormStats
        {
            public float Hunger = 100f;
            public float Thirst = 100f;
            public float Stamina = 100f;
            public float NextBite;
            public float NextHud;
            public float HealBudget;            // health the animal may still gain this second from food (heal-per-second cap)
            public float HealWindow;
            public float LastSpend;
            public bool Saved;                  // the human's own calories and hydration, put back when the form ends
            public float HumanCalories, HumanHydration;
            public bool HudDrawn;
            public int HudValue;
            public float HudAt;
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

        // The Use key with nothing else to use: eat or drink what is in front of the animal's head.
        internal bool TryEat(Driver d)
        {
            return Needs != null && Needs.Eat(d);
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
                    ["StatTired"] = "You are out of breath.",
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

            public int GaitCap(Driver d)
            {
                FormStats s;
                if (d.Kind != ControlKind.Form || !Bm.Cfg.Form.Needs || !stats.TryGetValue(d.Player.userID, out s))
                {
                    return 5;
                }
                return s.Stamina < 5f ? 2 : 5;
            }

            public void Spend(Driver d, float amount)
            {
                if (d.Kind != ControlKind.Form || !Bm.Cfg.Form.Needs)
                {
                    return;
                }
                FormStats s = Of(d.Player);
                s.Stamina = Mathf.Max(0f, s.Stamina - amount);
                s.LastSpend = Time.time;
            }

            // ---------------------------------------------------------------- ticking: drains, recovery, the panel

            public override void Tick(float now)
            {
                float dt = lastTick > 0f ? Mathf.Min(now - lastTick, 1f) : 0.25f;
                lastTick = now;
                if (Bm.Drivers.Count == 0)
                {
                    if (stats.Count > 0)
                    {
                        OnDisable();
                    }
                    return;
                }
                var present = new HashSet<ulong>();
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || d.Kind != ControlKind.Form || d.Player == null || !Valid(d.Body))
                    {
                        continue;
                    }
                    present.Add(d.Player.userID);
                    Update(d, now, dt);
                }
                foreach (ulong id in stats.Keys.Where(k => !present.Contains(k)).ToList())
                {
                    BasePlayer gone = BasePlayer.FindByID(id);
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
                    if (now >= s.NextHud)
                    {
                        s.NextHud = now + 1f;
                        CuiHelper.DestroyUi(p, Hud);
                    }
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
                bool wasHungry = s.Hunger <= 0.5f, wasThirsty = s.Thirst <= 0.5f, wasTired = s.Stamina < 5f;
                s.Hunger = Mathf.Max(0f, s.Hunger - 0.045f * rate * dt);
                s.Thirst = Mathf.Max(0f, s.Thirst - 0.07f * rate * dt);
                int gait = d.Gait;
                float drain = d.Standing ? 0f : gait >= 5 ? 3.5f : gait == 4 ? 2.2f : gait == 3 ? 0.8f : 0f;
                if (drain > 0f)
                {
                    s.Stamina = Mathf.Max(0f, s.Stamina - drain * dt);
                    s.LastSpend = now;
                }
                s.Stamina = 100f;                        // there is no stamina: an animal runs as long as it likes
                if (Bm.Cfg.Form.StarveDamage && (s.Hunger <= 0f || s.Thirst <= 0f) && Valid(d.Body))
                {
                    d.Body.Hurt(0.6f * dt, Rust.DamageType.Hunger, null, false);
                }
                if (s.Hunger <= 0.5f && !wasHungry) Bm.Tell(p, "StatHungry");
                if (s.Thirst <= 0.5f && !wasThirsty) Bm.Tell(p, "StatThirsty");
                if (s.Stamina < 5f && !wasTired) Bm.Tell(p, "StatTired");
                if (now >= s.NextHud)
                {
                    s.NextHud = now + 0.4f;
                    CuiHelper.DestroyUi(p, Hud);                // the game's own bars show the animal: health, food and water
                }
            }

            // Health, food and water of the animal are in the game's own bars; the one thing the game has no bar for, the stamina, gets a bar of the same
            // size above them. It is drawn once and then only updated, so that it does not flicker.
            private void DrawHud(Driver d, FormStats s)
            {
                BasePlayer p = d.Player;
                int value = Mathf.RoundToInt(Mathf.Clamp(s.Stamina, 0f, 100f));
                float now = Time.time;
                if (!d.Spectating)
                {
                    if (s.HudDrawn)
                    {
                        CuiHelper.DestroyUi(p, Hud);
                        s.HudDrawn = false;
                    }
                    return;
                }
                if (s.HudDrawn && value == s.HudValue && now - s.HudAt < 30f)
                {
                    return;
                }
                float frac = value / 100f;
                var c = new CuiElementContainer();
                if (!s.HudDrawn || now - s.HudAt >= 30f)
                {
                    c.Add(new CuiPanel
                    {
                        Image = { Color = "0.09 0.10 0.08 0.45" },
                        RectTransform = { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-311 150", OffsetMax = "-29 184" }
                    }, "Overlay", Hud);
                    c.Add(new CuiElement
                    {
                        Name = Hud + ".fill",
                        Parent = Hud,
                        Components = { new CuiImageComponent { Color = "0.86 0.76 0.24 0.85" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = Pt(frac, 1f) } }
                    });
                    c.Add(new CuiElement
                    {
                        Name = Hud + ".num",
                        Parent = Hud,
                        Components = { new CuiTextComponent { Text = value.ToString(), FontSize = 18, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0.03 0", AnchorMax = "1 1" } }
                    });
                    CuiHelper.DestroyUi(p, Hud);
                    CuiHelper.AddUi(p, c);
                    s.HudDrawn = true;
                    s.HudAt = now;
                }
                else
                {
                    c.Add(new CuiElement
                    {
                        Name = Hud + ".fill",
                        Update = true,
                        Components = { new CuiImageComponent { Color = "0.86 0.76 0.24 0.85" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = Pt(frac, 1f) } }
                    });
                    c.Add(new CuiElement
                    {
                        Name = Hud + ".num",
                        Update = true,
                        Components = { new CuiTextComponent { Text = value.ToString(), FontSize = 18, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0.03 0", AnchorMax = "1 1" } }
                    });
                    CuiHelper.AddUi(p, c);
                }
                s.HudValue = value;
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
                            Bm.FormFed(d);
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
                if (d.Animal == null)
                {
                    return;                  // the horse has no eating clip to play
                }
                List<ClipRef> clips = Bm.Clips(d.Animal).Where(c => Has(c, word) && !Has(c, "death") && !Has(c, "turn")).ToList();
                if (clips.Count == 0)
                {
                    clips = Bm.Clips(d.Animal).Where(c => (Has(c, "eat") || Has(c, "graze") || Has(c, "drink") || Has(c, "head")) && !Has(c, "death") && !Has(c, "turn")).ToList();
                }
                if (clips.Count > 0)
                {
                    PlayClip(d.Animal, clips[UnityEngine.Random.Range(0, clips.Count)]);
                }
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
