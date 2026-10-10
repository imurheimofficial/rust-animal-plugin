using System;
using System.Collections.Generic;
using System.Linq;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- how wild animals meet the plugin
        // 1. Defence (R19.18): a wild cow, sheep, horse or doe that is attacked defends itself with a chance (30 % by default), otherwise it flees as in
        //    the game; a stag, bull or ram always defends. 2. Relations (R7): who follows, tolerates, flees from or attacks a rider or a transformed player.

        internal WildModule Wildlife;

        internal sealed class WildModule : Module
        {
            public override int Order => 50;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Wildlife = this;
            }

            // The one who is reacted to: a ridden or transformed animal (or, in the test, any animal).
            internal sealed class Subject
            {
                public BaseNPC2 Animal;
                public string Species;
                public Func<bool> Live;
                public Func<float> Speed;
            }

            private sealed class Reaction
            {
                public BaseNPC2 Animal;
                public Subject Who;
                public RelationRule Rule;
                public float Seen;
            }

            private readonly Dictionary<ulong, Reaction> reactions = new Dictionary<ulong, Reaction>();
            private float nextRelations;

            public override void OnDisable()
            {
                Release(true);
            }

            public override void Unload()
            {
                Release(true);
            }

            // ---------------------------------------------------------------- defence

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                if (info != null && entity != null && (entity is BasePlayer || Bm.DriverOfAnimal(entity) != null))
                {
                    BaseNPC2 biter = info.Initiator as BaseNPC2;
                    if (biter != null && Bm.DriverOfAnimal(biter) == null)
                    {
                        NoteAggression(biter, entity);          // a wild animal attacked a player (or the animal of one)
                    }
                    if (!(entity is BaseNPC2))
                    {
                        return null;
                    }
                }
                BaseNPC2 npc = entity as BaseNPC2;
                if (npc == null || info == null || !Bm.Cfg.Features.WildDefence)
                {
                    return null;
                }
                BaseCombatEntity by = AttackerOf(info);
                if (by == null || by == npc)
                {
                    return null;
                }
                SpeciesDef species = SpeciesOf(npc);
                if (species == null || npc.IsDead() || Bm.IsTamed(npc) || Bm.DriverOfAnimal(npc) != null)
                {
                    return null;
                }
                Follower busy = Bm.FollowerOf(npc);
                if (busy != null && busy.Reason != "scared" && busy.Reason != "defend")
                {
                    return null;                    // busy with something else of the plugin's (a boss hunting, a chain, a command)
                }
                bool always = Bm.Cfg.Wild.AlwaysDefend.Contains(species.Id);
                bool may = always || Bm.Cfg.Wild.MayDefend.Count == 0 || Bm.Cfg.Wild.MayDefend.Contains(species.Id);
                float now = Time.time;
                ulong key = npc.net.ID.Value;
                // the decision of an animal sticks: one that fights keeps fighting, one that runs keeps running - no attack and run off at once
                Decision decided;
                if (decisions.TryGetValue(key, out decided) && now < decided.Until)
                {
                    decided.Until = now + (decided.Defend ? Bm.Cfg.Wild.DefendSeconds : Bm.Cfg.Wild.FleeSeconds);
                    Follower current = Bm.FollowerOf(npc);
                    if (decided.Defend && (current == null || current.Reason != "defend"))
                    {
                        Defend(npc, by);
                    }
                    else if (!decided.Defend && (current == null || current.Reason != "scared"))
                    {
                        Scare(npc, by);
                    }
                    return null;
                }
                if (decisions.Count > 400)
                {
                    foreach (ulong old in decisions.Where(kv => now > kv.Value.Until + 30f).Select(kv => kv.Key).ToList())
                    {
                        decisions.Remove(old);
                    }
                }
                // the animals on the list always fight back, an animal that has just attacked this player goes on attacking; the others run,
                // and now and then they fight as well
                bool defend = always || AttackedRecently(npc, by, now) || (may && UnityEngine.Random.Range(0f, 100f) < Bm.Cfg.Wild.DefendChance);
                decisions[key] = new Decision { Defend = defend, Until = now + (defend ? Bm.Cfg.Wild.DefendSeconds : Bm.Cfg.Wild.FleeSeconds) };
                if (defend)
                {
                    Defend(npc, by);
                }
                else
                {
                    Scare(npc, by);
                }
                return null;
            }

            private sealed class Decision
            {
                public bool Defend;
                public float Until;
            }

            private readonly Dictionary<ulong, Decision> decisions = new Dictionary<ulong, Decision>();
            private readonly Dictionary<string, float> aggressors = new Dictionary<string, float>();

            private static string PairKey(BaseEntity animal, BaseEntity victim)
            {
                return animal.net.ID.Value + ">" + (victim.net != null ? victim.net.ID.Value : 0UL);
            }

            private bool AttackedRecently(BaseNPC2 npc, BaseCombatEntity victim, float now)
            {
                float at;
                return victim != null && victim.net != null && aggressors.TryGetValue(PairKey(npc, victim), out at) && now - at < 30f;
            }

            // A wild animal hurts somebody: remembered, so that it keeps attacking when it is hit back.
            internal void NoteAggression(BaseNPC2 npc, BaseCombatEntity victim)
            {
                if (npc == null || victim == null || npc.net == null || victim.net == null)
                {
                    return;
                }
                if (aggressors.Count > 500)
                {
                    aggressors.Clear();
                }
                aggressors[PairKey(npc, victim)] = Time.time;
            }

            // Who hurt the animal: a player, or the animal body a player rides or has become.
            internal static BaseCombatEntity AttackerOf(HitInfo info)
            {
                BasePlayer p = info.InitiatorPlayer;
                if (p != null)
                {
                    return p.IsNpc ? null : p;
                }
                BaseEntity initiator = info.Initiator;
                Driver d = initiator != null ? Instance.DriverOfAnimal(initiator) : null;
                return d != null ? d.Body : null;
            }

            internal void Scare(BaseNPC2 npc, BaseCombatEntity attacker)
            {
                Follower f = Bm.StartFollower(npc, "scared", null);
                if (f == null)
                {
                    return;
                }
                f.TeleportDistance = 0f;
                f.Flee = true;
                f.FleeDistance = 40f;
                f.Where = () => Alive(attacker) ? (Vector3?)attacker.transform.position : null;
                Bm.timer.Once(Bm.Cfg.Wild.FleeSeconds, () =>
                {
                    Follower cur = Valid(npc) ? Bm.FollowerOf(npc) : null;
                    if (cur != null && cur.Reason == "scared")
                    {
                        Bm.StopFollower(npc);
                    }
                });
            }

            internal void Defend(BaseNPC2 npc, BaseCombatEntity attacker)
            {
                Follower f = Bm.StartFollower(npc, "defend", null);
                if (f == null)
                {
                    return;
                }
                float until = Time.time + Bm.Cfg.Wild.DefendSeconds;
                f.TeleportDistance = 0f;
                f.Target = () => Alive(attacker) && Time.time < until && Valid(npc) && Vector3.Distance(attacker.transform.position, npc.transform.position) < 45f ? attacker : null;
                Bm.Play(npc, "roar", "growl", "snort");
                Bm.timer.Once(Bm.Cfg.Wild.DefendSeconds + 1f, () =>
                {
                    Follower cur = Valid(npc) ? Bm.FollowerOf(npc) : null;
                    if (cur != null && cur.Reason == "defend")
                    {
                        Bm.StopFollower(npc);
                    }
                });
            }

            // ---------------------------------------------------------------- relations

            public override void Tick(float now)
            {
                if (now < nextRelations)
                {
                    return;
                }
                nextRelations = now + 1f;
                if (!Bm.Cfg.Features.Relations || Bm.Drivers.Count == 0)
                {
                    if (reactions.Count > 0)
                    {
                        Release(true);
                    }
                    return;
                }
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || !Valid(d.Animal) || d.Species == null)
                    {
                        continue;
                    }
                    Driver driver = d;
                    React(new Subject { Animal = d.Animal, Species = d.Species.Id, Live = () => driver.Alive, Speed = () => driver.Speed }, now);
                }
                Release(false, now);
            }

            private static RelationRule RuleFor(string observer, string subject)
            {
                return Instance.Cfg.Relations.Rules.FirstOrDefault(r => string.Equals(r.Observer, observer, StringComparison.OrdinalIgnoreCase) && string.Equals(r.Subject, subject, StringComparison.OrdinalIgnoreCase));
            }

            internal void React(Subject who, float now)
            {
                var near = new List<BaseNPC2>();
                Vis.Entities(who.Animal.transform.position, Bm.Cfg.Relations.Range, near);
                int handled = 0;
                foreach (BaseNPC2 npc in near)
                {
                    if (npc == null || npc == who.Animal || npc.IsDestroyed || npc.IsDead() || handled >= 12)
                    {
                        continue;
                    }
                    SpeciesDef species = SpeciesOf(npc);
                    if (species == null || Bm.IsTamed(npc) || Bm.DriverOfAnimal(npc) != null)
                    {
                        continue;
                    }
                    RelationRule rule = RuleFor(species.Id, who.Species);
                    if (rule == null || rule.Relation == "tolerate")
                    {
                        continue;
                    }
                    ulong id = npc.net.ID.Value;
                    Reaction r;
                    Follower f = Bm.FollowerOf(npc);
                    if (reactions.TryGetValue(id, out r))
                    {
                        r.Seen = now;
                        r.Who = who;
                        if (f == null || f.Reason != "relation")
                        {
                            reactions.Remove(id);
                        }
                        else
                        {
                            Maintain(r, f, who);
                            handled++;
                            continue;
                        }
                    }
                    if (f != null)
                    {
                        continue;       // busy with something else of the plugin's (defending, commanded, scared ...)
                    }
                    if (rule.Relation == "tether")
                    {
                        float dist = Vector3.Distance(npc.transform.position, who.Animal.transform.position);
                        if (dist <= rule.Keep)
                        {
                            continue;       // inside the tether it roams as it likes
                        }
                    }
                    f = Bm.StartFollower(npc, "relation", null);
                    if (f == null)
                    {
                        continue;
                    }
                    r = new Reaction { Animal = npc, Who = who, Rule = rule, Seen = now };
                    reactions[id] = r;
                    f.TeleportDistance = 0f;
                    Maintain(r, f, who);
                    handled++;
                }
            }

            // Points a relation follower at its subject according to the rule.
            private void Maintain(Reaction r, Follower f, Subject subject)
            {
                RelationRule rule = r.Rule;
                f.Target = null;
                f.Flee = false;
                switch (rule.Relation)
                {
                    case "hostile":
                        f.Target = () => subject.Live() && Alive(subject.Animal) ? subject.Animal : null;
                        break;
                    case "follow":
                    case "tether":
                        f.Where = () => subject.Live() && Valid(subject.Animal) ? (Vector3?)subject.Animal.transform.position : null;
                        f.StopDistance = rule.Relation == "tether" ? Mathf.Max(8f, rule.Keep * 0.4f) : Mathf.Max(3f, rule.Keep);
                        f.MatchLeader = rule.Relation == "follow";
                        f.LeaderSpeed = () => subject.Speed();
                        break;
                    case "flee":
                        f.Where = () => subject.Live() && Valid(subject.Animal) ? (Vector3?)subject.Animal.transform.position : null;
                        f.Flee = true;
                        f.FleeDistance = Mathf.Max(12f, rule.Keep);
                        break;
                }
            }

            // Ends the reactions whose subject is gone or far (all of them with `all`).
            private void Release(bool all, float now = 0f)
            {
                foreach (KeyValuePair<ulong, Reaction> kv in reactions.ToList())
                {
                    Reaction r = kv.Value;
                    bool end = all || !Valid(r.Animal) || now - r.Seen > 2.5f;
                    if (!end && r.Rule.Relation == "tether" && r.Who != null && Valid(r.Who.Animal))
                    {
                        // back inside the tether: free again
                        end = Vector3.Distance(r.Animal.transform.position, r.Who.Animal.transform.position) < r.Rule.Keep * 0.55f;
                    }
                    if (!end)
                    {
                        continue;
                    }
                    reactions.Remove(kv.Key);
                    if (Valid(r.Animal))
                    {
                        Follower f = Bm.FollowerOf(r.Animal);
                        if (f != null && f.Reason == "relation")
                        {
                            Bm.StopFollower(r.Animal);
                        }
                    }
                }
            }

            internal int ReactionCount { get { return reactions.Count; } }

            public override void SelfTest(List<string> report)
            {
                report.Add("Wild: " + reactions.Count + " animals reacting to riders or transformed players; " + Bm.Cfg.Relations.Rules.Count + " relation rules; defence " + Bm.Cfg.Wild.DefendChance + " %");
            }
        }

        private BaseNPC2 SpawnTestAnimal(string id, Vector3 pos, bool park)
        {
            SpeciesDef sp = SpeciesById(id);
            if (sp == null || !SpeciesEnabled(sp))
            {
                return null;
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.2f;
            BaseNPC2 npc = GameManager.server.CreateEntity(sp.Prefab, pos, Quaternion.identity) as BaseNPC2;
            if (npc == null)
            {
                return null;
            }
            npc.Spawn();
            return npc;
        }

        // Headless test: a stag defends itself against an attacker; cows follow a bull, a bear goes for a stag (R7.1, R7.2, R19.18).
        internal void TestWild(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            BaseNPC2 stag = SpawnTestAnimal("stag", pos, false);
            BaseNPC2 victim = SpawnTestAnimal("cow", pos + new Vector3(5f, 0f, 0f), false);
            BaseNPC2 bull = SpawnTestAnimal("bull", pos + new Vector3(0f, 0f, 60f), false);
            BaseNPC2 cow1 = SpawnTestAnimal("cow", pos + new Vector3(22f, 0f, 60f), false);
            BaseNPC2 cow2 = SpawnTestAnimal("cow", pos + new Vector3(-22f, 0f, 60f), false);
            BaseNPC2 deer = SpawnTestAnimal("stag", pos + new Vector3(0f, 0f, 120f), false);
            BaseNPC2 bear = SpawnTestAnimal("bear", pos + new Vector3(18f, 0f, 120f), false);
            var all = new[] { stag, victim, bull, cow1, cow2, deer, bear };
            if (all.Any(a => a == null))
            {
                foreach (BaseNPC2 a in all) { if (a != null) a.Kill(); }
                reply("A test animal is not available in this build.");
                return;
            }
            timer.Once(2.0f, () =>
            {
                Park(victim, "test");
                Park(bull, "test");
                Park(deer, "test");
                float victimHp = victim.health, deerHp = deer.health;
                float cowBefore = Vector3.Distance(cow1.transform.position, bull.transform.position);
                Wildlife.Defend(stag, victim);
                check("the stag defends and parks (follower " + (FollowerOf(stag) != null ? FollowerOf(stag).Reason : "none") + ")", FollowerOf(stag) != null && FollowerOf(stag).Reason == "defend");
                int ticks = 0;
                var subjectBull = new WildModule.Subject { Animal = bull, Species = "bull", Live = () => true, Speed = () => 0f };
                var subjectDeer = new WildModule.Subject { Animal = deer, Species = "stag", Live = () => true, Speed = () => 0f };
                Action tick = null;
                tick = () =>
                {
                    Wildlife.React(subjectBull, Time.time);
                    Wildlife.React(subjectDeer, Time.time);
                    if (++ticks < 14)
                    {
                        timer.Once(1f, tick);
                        return;
                    }
                    float cowAfter = Vector3.Distance(cow1.transform.position, bull.transform.position);
                    check("the stag hurt its attacker (" + Mathf.RoundToInt(victimHp) + " -> " + Mathf.RoundToInt(victim.health) + ")", victim.health < victimHp);
                    check("cows follow a bull (" + Mathf.RoundToInt(cowBefore) + " m -> " + Mathf.RoundToInt(cowAfter) + " m)", cowAfter < cowBefore - 8f);
                    check("a bear goes for a stag (" + Mathf.RoundToInt(deerHp) + " -> " + Mathf.RoundToInt(deer.health) + ")", deer.health < deerHp);
                    check("reactions are tracked (" + Wildlife.ReactionCount + ")", Wildlife.ReactionCount >= 2);
                    Wildlife.OnDisable();
                    check("they end when the relations are switched off", FollowerOf(bear) == null && FollowerOf(cow1) == null);
                    StopFollower(stag);
                    foreach (BaseNPC2 a in all) { if (Valid(a)) { a.Kill(); } }
                    Puts("[test] wild:\n" + string.Join("\n", lines.ToArray()));
                };
                tick();
            });
            reply("Wild test started: the result comes to the log in ~20 s.");
        }
    }
}
