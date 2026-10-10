using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- climate (R19.19)
        // A polar bear outside the snow needs water, an animal that does not belong in the snow needs cooked meat in the inventory there;
        // without it the animal is slower and weaker. The supplies are used up slowly. Applies to ridden animals and to animal forms.

        internal ClimateModule Weather;

        internal float ClimateSpeedFactor(BaseEntity animal)
        {
            return Weather != null ? Weather.Factor(animal, true) : 1f;
        }

        internal float ClimateDamageFactor(BaseEntity animal)
        {
            return Weather != null ? Weather.Factor(animal, false) : 1f;
        }

        internal static bool IsSnow(Vector3 pos)
        {
            return TerrainMeta.BiomeMap != null && TerrainMeta.BiomeMap.GetBiome(pos, TerrainBiome.ARCTIC) > 0.5f;
        }

        internal sealed class ClimateModule : Module
        {
            public override int Order => 60;

            private sealed class State
            {
                public string Need;             // water, warmth or null
                public bool Lacking;
                public float NextUse;
                public float NextWarn;
            }

            private readonly Dictionary<ulong, State> states = new Dictionary<ulong, State>();
            private float nextTick;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Weather = this;
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["ClimateWater"] = "You are too far from the snow and dry out: carry water (a bottle or jug) or you get slower and weaker.",
                    ["ClimateCold"] = "You freeze in the snow: carry cooked meat to keep warm or you get slower and weaker.",
                    ["ClimateOk"] = "You feel fine again.",
                };
            }

            public float Factor(BaseEntity animal, bool speed)
            {
                if (states.Count == 0 || !Bm.Cfg.Features.Climate)
                {
                    return 1f;
                }
                Driver d = Bm.DriverOfAnimal(animal);
                State s;
                if (d == null || d.Player == null || !states.TryGetValue(d.Player.userID, out s) || !s.Lacking)
                {
                    return 1f;
                }
                return speed ? Bm.Cfg.Climate.PenaltySpeed : Bm.Cfg.Climate.PenaltyDamage;
            }

            public override void OnDisable()
            {
                states.Clear();
            }

            public override void Tick(float now)
            {
                if (now < nextTick)
                {
                    return;
                }
                nextTick = now + 2f;
                if (!Bm.Cfg.Features.Climate || Bm.Drivers.Count == 0)
                {
                    states.Clear();
                    return;
                }
                var present = new HashSet<ulong>();
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || d.Player == null || !Valid(d.Body) || d.Species == null)
                    {
                        continue;
                    }
                    present.Add(d.Player.userID);
                    Check(d, now);
                }
                foreach (ulong id in states.Keys.Where(k => !present.Contains(k)).ToList())
                {
                    states.Remove(id);
                }
            }

            private void Check(Driver d, float now)
            {
                BasePlayer p = d.Player;
                bool snow = IsSnow(d.Body.transform.position);
                bool native = Bm.Cfg.Climate.SnowNative.Contains(d.Species.Id);
                string need = native && !snow && Bm.Cfg.Climate.PolarNeedsWater ? "water" : !native && snow && Bm.Cfg.Climate.ColdNeedsMeat ? "warmth" : null;
                State s;
                if (!states.TryGetValue(p.userID, out s))
                {
                    s = new State();
                    states[p.userID] = s;
                }
                if (need == null)
                {
                    if (s.Lacking)
                    {
                        Bm.Tell(p, "ClimateOk");
                    }
                    s.Need = null;
                    s.Lacking = false;
                    return;
                }
                bool wasLacking = s.Lacking;
                Item supply = need == "water" ? FindWater(p) : FindWarmMeat(p);
                if (s.Need != need)
                {
                    s.Need = need;
                    s.NextUse = now + 5f;
                }
                if (supply == null)
                {
                    s.Lacking = true;
                    if (!wasLacking || now >= s.NextWarn)
                    {
                        s.NextWarn = now + 45f;
                        Bm.Tell(p, need == "water" ? "ClimateWater" : "ClimateCold");
                    }
                    return;
                }
                s.Lacking = false;
                if (wasLacking)
                {
                    Bm.Tell(p, "ClimateOk");
                }
                if (now >= s.NextUse)
                {
                    // the supply is used up slowly: 50 ml of water per 5 s, one cooked meat per 90 s
                    if (need == "water")
                    {
                        s.NextUse = now + 5f;
                        supply.UseItem(Mathf.Min(50, supply.amount));
                    }
                    else
                    {
                        s.NextUse = now + Bm.Cfg.Climate.MeatSeconds;
                        supply.UseItem(1);
                    }
                }
            }

            // Water as an item or inside a bottle, jug or bucket.
            private static Item FindWater(BasePlayer p)
            {
                foreach (Item it in PlayerItems(p))
                {
                    if (it.info.shortname == "water" && it.amount > 0)
                    {
                        return it;
                    }
                    if (it.contents != null)
                    {
                        foreach (Item inner in it.contents.itemList)
                        {
                            if (inner.info.shortname == "water" && inner.amount > 0)
                            {
                                return inner;
                            }
                        }
                    }
                }
                return null;
            }

            private static Item FindWarmMeat(BasePlayer p)
            {
                foreach (Item it in PlayerItems(p))
                {
                    if (it.info.shortname.EndsWith(".cooked") && it.amount > 0)
                    {
                        return it;
                    }
                }
                return null;
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Climate: " + (Bm.Cfg.Features.Climate ? "on" : "off") + ", " + states.Count(kv => kv.Value.Lacking) + " animals lacking supplies; snow detection " + (TerrainMeta.BiomeMap != null ? "OK" : "MISSING"));
            }
        }
    }
}
