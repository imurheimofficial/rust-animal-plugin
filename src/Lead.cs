using System;
using System.Collections.Generic;
using System.Linq;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- leading (R6B, R9.27 - R9.29)
        // 1. The game's own rope of the livestock breaks at 7 m; while the plugin runs it breaks at Lead.BreakDistance (50 m).
        // 2. The horse's rope breaks at 7 m in the horse code: the horse then keeps following its leader on an invisible tether (HorseModule)
        //    and takes the rope again when it is back within reach.
        // 3. A player in animal form leads tamed animals of their own or their team in a line behind them (E at the animal; E again lets it go).

        private float savedLeadBreak = -1f;

        internal void LeadInit()
        {
            if (!Cfg.Features.Lead)
            {
                return;
            }
            if (savedLeadBreak < 0f)
            {
                savedLeadBreak = ConVar.Livestock.leadBreakDistance;
            }
            ConVar.Livestock.leadBreakDistance = Cfg.Lead.BreakDistance;
        }

        internal void LeadRestore()
        {
            if (savedLeadBreak >= 0f)
            {
                ConVar.Livestock.leadBreakDistance = savedLeadBreak;
                savedLeadBreak = -1f;
            }
        }

        internal LeadModule Leads;

        // A tamed animal of the player's own or team within reach of the animal's head.
        private BaseNPC2 LeadCandidate(Driver d)
        {
            Vector3 head = d.HeadPosition();
            var near = new List<BaseNPC2>();
            Vis.Entities(head, 4f, near);
            BaseNPC2 best = null;
            float bestDist = float.MaxValue;
            foreach (BaseNPC2 npc in near)
            {
                if (npc == null || npc == d.Body || npc.IsDestroyed || npc.IsDead() || DriverOfAnimal(npc) != null)
                {
                    continue;
                }
                TamedRecord rec = RecordOf(npc);
                if (rec == null || !MayUse(d.Player, rec.Owner, "team"))
                {
                    continue;
                }
                float dist = Vector3.Distance(npc.WorldSpaceBounds().ToBounds().ClosestPoint(head), head);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = npc;
                }
            }
            return best;
        }

        // E from the animal's head: lead the tamed animal in front of it, or let it go if it is led already.
        internal bool TryLeadToggle(Driver d)
        {
            if (!Cfg.Features.Lead || d.Kind != ControlKind.Form || Leads == null)
            {
                return false;
            }
            BaseNPC2 target = LeadCandidate(d);
            if (target == null)
            {
                return false;
            }
            SpeciesDef species = SpeciesOf(target);
            List<BaseNPC2> led = Leads.ChainOf(d);
            if (led.Contains(target))
            {
                Leads.Unlead(d, target, "released");
                Tell(d.Player, "LeadOff", species != null ? species.Display : "animal");
                return true;
            }
            if (led.Count >= Cfg.Lead.MaxChain)
            {
                Tell(d.Player, "LeadFull", Cfg.Lead.MaxChain);
                return true;
            }
            Leads.Lead(d, target);
            Tell(d.Player, "LeadOn", species != null ? species.Display : "animal", led.Count, Cfg.Lead.MaxChain);
            return true;
        }

        internal void EndChain(Driver d, string reason)
        {
            if (Leads != null)
            {
                Leads.EndChain(d, reason);
            }
        }

        internal sealed class LeadModule : Module
        {
            public override int Order => 55;

            private readonly Dictionary<ulong, List<BaseNPC2>> chains = new Dictionary<ulong, List<BaseNPC2>>();

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Leads = this;
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["LeadOn"] = "You lead the {0} behind you ({1}/{2}). Press E at it again to let it go.",
                    ["LeadOff"] = "The {0} is free again.",
                    ["LeadFull"] = "You can lead {0} animals at most.",
                    ["LeadBroke"] = "The {0} fell too far behind and is free again.",
                };
            }

            public override void OnServerInitialized()
            {
                Bm.LeadInit();
            }

            public override void OnEnable()
            {
                Bm.LeadInit();
            }

            public override void OnDisable()
            {
                EndAll("off");
                Bm.LeadRestore();
            }

            public override void Unload()
            {
                EndAll("unload");
                Bm.LeadRestore();
            }

            public List<BaseNPC2> ChainOf(Driver d)
            {
                List<BaseNPC2> list;
                if (!chains.TryGetValue(d.Player.userID, out list))
                {
                    list = new List<BaseNPC2>();
                    chains[d.Player.userID] = list;
                }
                return list;
            }

            public void Lead(Driver d, BaseNPC2 animal)
            {
                List<BaseNPC2> list = ChainOf(d);
                list.Add(animal);
                Follower f = Bm.StartFollower(animal, "led", null);
                if (f == null)
                {
                    list.Remove(animal);
                    return;
                }
                float spacing = Mathf.Max(3.5f, animal.bounds.extents.z * 2f + 1.5f);
                Driver driver = d;
                f.TeleportDistance = 0f;
                f.StopDistance = spacing;
                f.MatchLeader = true;
                f.LeaderSpeed = () => driver.Alive ? driver.Speed : 0f;
                f.Where = () =>
                {
                    List<BaseNPC2> chain;
                    if (!driver.Alive || !chains.TryGetValue(driver.Player.userID, out chain))
                    {
                        return null;
                    }
                    int i = chain.IndexOf(animal);
                    BaseCombatEntity ahead = i <= 0 ? (BaseCombatEntity)driver.Body : chain[i - 1];
                    return Valid(ahead) ? (Vector3?)ahead.transform.position : null;
                };
            }

            public void Unlead(Driver d, BaseNPC2 animal, string reason)
            {
                List<BaseNPC2> list;
                if (chains.TryGetValue(d.Player.userID, out list))
                {
                    list.Remove(animal);
                }
                Release(animal);
            }

            // The animal returns to the command it was under before (follow, wait, guard).
            private void Release(BaseNPC2 animal)
            {
                if (!Valid(animal))
                {
                    return;
                }
                Follower f = Bm.FollowerOf(animal);
                if (f != null && f.Reason == "led")
                {
                    Bm.StopFollower(animal);
                }
                TamedRecord rec = Bm.RecordOf(animal);
                if (rec != null)
                {
                    Bm.ApplyState(animal, rec);
                }
            }

            public void EndChain(Driver d, string reason)
            {
                List<BaseNPC2> list;
                if (d == null || d.Player == null || !chains.TryGetValue(d.Player.userID, out list))
                {
                    return;
                }
                chains.Remove(d.Player.userID);
                foreach (BaseNPC2 a in list.ToList())
                {
                    Release(a);
                }
            }

            private void EndAll(string reason)
            {
                foreach (List<BaseNPC2> list in chains.Values.ToList())
                {
                    foreach (BaseNPC2 a in list.ToList())
                    {
                        Release(a);
                    }
                }
                chains.Clear();
            }

            // Keeps the chains tidy and lets an animal go that fell too far behind (R9.27).
            public override void Tick(float now)
            {
                if (chains.Count == 0)
                {
                    return;
                }
                foreach (ulong id in chains.Keys.ToList())
                {
                    Driver d = Bm.Drivers.Values.FirstOrDefault(x => x.Player != null && x.Player.userID == id);
                    List<BaseNPC2> list = chains[id];
                    if (d == null || !d.Alive || !Valid(d.Body) || !Bm.Cfg.Features.Lead)
                    {
                        chains.Remove(id);
                        foreach (BaseNPC2 a in list.ToList()) Release(a);
                        continue;
                    }
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        BaseNPC2 a = list[i];
                        if (!Valid(a) || a.IsDead())
                        {
                            list.RemoveAt(i);
                            continue;
                        }
                        Follower f = Bm.FollowerOf(a);
                        if (f == null || f.Reason != "led")
                        {
                            list.RemoveAt(i);               // commanded or replaced by something else
                            continue;
                        }
                        BaseCombatEntity ahead = i == 0 ? (BaseCombatEntity)d.Body : list[i - 1];
                        if (Valid(ahead) && Vector3.Distance(ahead.transform.position, a.transform.position) > Bm.Cfg.Lead.BreakDistance)
                        {
                            SpeciesDef sp = SpeciesOf(a);
                            list.RemoveAt(i);
                            Release(a);
                            Bm.Tell(d.Player, "LeadBroke", sp != null ? sp.Display : "animal");
                        }
                    }
                }
            }

            // A led animal that is attacked is free: it reacts as a normal animal (R9.29).
            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                BaseNPC2 a = entity as BaseNPC2;
                if (a == null || chains.Count == 0 || info == null)
                {
                    return null;
                }
                foreach (KeyValuePair<ulong, List<BaseNPC2>> kv in chains.ToList())
                {
                    if (kv.Value.Contains(a) && info.InitiatorPlayer != null && info.InitiatorPlayer.userID != kv.Key)
                    {
                        kv.Value.Remove(a);
                        Release(a);
                    }
                }
                return null;
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Lead: " + chains.Values.Sum(l => l.Count) + " animals led; livestock rope breaks at " + (savedLeadBreakInfo()) + " m");
            }

            private static string savedLeadBreakInfo()
            {
                return ConVar.Livestock.leadBreakDistance.ToString("0");
            }
        }
    }
}
