using System;
using System.Collections.Generic;
using System.Linq;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- headless tests of the newer parts (server console / RCON only)
        // bm.test clips <species> | death | attacks | flames | scare. Results go to the server log ("[test] ...").

        private BaseNPC2 TestSpawn(SpeciesDef sp, Vector3 pos)
        {
            if (sp == null || !sp.Available)
            {
                return null;
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.2f;
            BaseNPC2 npc = GameManager.server.CreateEntity(sp.Prefab, pos, Quaternion.identity) as BaseNPC2;
            if (npc != null)
            {
                npc.Spawn();
            }
            return npc;
        }

        // The clips of a species with the state that holds them: what the sharp turn, the gestures and the attacks have to pick from.
        internal void TestClips(Action<string> reply, string id, Vector3 pos)
        {
            SpeciesDef sp = SpeciesById(id);
            BaseNPC2 a = TestSpawn(sp, pos);
            if (a == null)
            {
                reply("clips: " + id + " is not available");
                return;
            }
            timer.Once(1.5f, () =>
            {
                if (Valid(a))
                {
                    List<ClipRef> clips = Clips(a);
                    Puts("[test] clips " + id + " (" + clips.Count + "): " + string.Join(", ", clips.Select(c => c.State + ":" + c.Name).ToArray()));
                    a.Kill();
                }
            });
            reply("clips " + id + ": the list comes to the log in 2 s");
        }

        // A parked animal, one with a follower and one under control are killed: each has to leave a body and not stand there frozen.
        internal void TestDeath(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            SpeciesDef cow = SpeciesById("cow"), wolf = SpeciesById("wolf");
            BaseNPC2 parked = TestSpawn(cow, pos);
            BaseNPC2 led = TestSpawn(cow, pos + new Vector3(8f, 0f, 0f));
            BaseNPC2 predator = TestSpawn(wolf, pos + new Vector3(-8f, 0f, 0f));
            if (parked == null || led == null || predator == null)
            {
                foreach (BaseNPC2 x in new[] { parked, led, predator }) { if (x != null) x.Kill(); }
                reply("death: a test animal is missing");
                return;
            }
            timer.Once(2f, () =>
            {
                Park(parked, "control");
                StartFollower(led, "tame", () => null);
                Park(predator, "control");
                var before = new Dictionary<BaseNPC2, Vector3> { { parked, parked.transform.position }, { led, led.transform.position }, { predator, predator.transform.position } };
                foreach (BaseNPC2 x in before.Keys) { x.Hurt(x.health + 500f, Rust.DamageType.Bullet); }
                timer.Once(14f, () =>
                {
                    foreach (KeyValuePair<BaseNPC2, Vector3> kv in before)
                    {
                        BaseNPC2 x = kv.Key;
                        bool gone = !Valid(x);
                        bool dead = !gone && x.IsDead();
                        int bodies = 0;
                        foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                        {
                            BaseCorpse corpse = net as BaseCorpse;
                            if (corpse != null && Vector3.Distance(corpse.transform.position, kv.Value) < 6f)
                            {
                                bodies++;
                            }
                        }
                        string name = ReferenceEquals(x, parked) ? "parked cow" : ReferenceEquals(x, led) ? "cow with a follower" : "parked wolf";
                        lines.Add(((gone || dead) && bodies > 0 ? "PASS " : "FAIL ") + name + ": " + (gone ? "removed" : dead ? "dead" : "STILL STANDING") + ", bodies near " + bodies);
                    }
                    foreach (BaseNPC2 x in before.Keys) { if (Valid(x)) x.Kill(); }
                    foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                    {
                        BaseCorpse corpse = net as BaseCorpse;
                        if (corpse != null && before.Values.Any(v => Vector3.Distance(corpse.transform.position, v) < 6f)) corpse.Kill();
                    }
                    Puts("[test] death:\n" + string.Join("\n", lines.ToArray()));
                });
            });
            reply("death test started: the result comes to the log in ~18 s");
        }

        // Every species with an attack hurts a cow in front of it with the damage the game gives that attack.
        internal void TestAttacks(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            List<SpeciesDef> list = AllSpecies.Where(s => s.Available && s.Id != "horse").ToList();
            string[] attackWords = { "attack", "headbutt", "kick", "charge", "flop", "snap", "peck", "bite", "swipe" };
            SpeciesDef cowDef = SpeciesById("cow");
            int index = 0;
            Action next = null;
            next = () =>
            {
                if (index >= list.Count)
                {
                    Puts("[test] attacks:\n" + string.Join("\n", lines.ToArray()));
                    return;
                }
                SpeciesDef sp = list[index];
                Vector3 at = pos + new Vector3((index % 6) * 14f, 0f, (index / 6) * 14f);
                index++;
                BaseNPC2 attacker = TestSpawn(sp, at);
                float gap = attacker != null ? attacker.bounds.extents.z * 1.9f + 0.6f : 1.5f;       // a long body (the crocodile) reaches from further away
                BaseNPC2 victim = TestSpawn(cowDef, at + new Vector3(0f, 0f, gap));
                if (attacker == null || victim == null)
                {
                    lines.Add("SKIP " + sp.Id + ": not spawned");
                    if (attacker != null) attacker.Kill();
                    if (victim != null) victim.Kill();
                    timer.Once(0.2f, next);
                    return;
                }
                timer.Once(1.3f, () =>
                {
                    Park(attacker, "test");
                    Park(victim, "test");
                    attacker.transform.rotation = Quaternion.identity;
                    victim.transform.position = attacker.transform.position + Vector3.forward * gap;
                    Physics.SyncTransforms();
                    timer.Once(0.4f, () =>
                    {
                        try
                        {
                            ClipRef clip = Clips(attacker).FirstOrDefault(c => attackWords.Any(w => Has(c, w)) && !Has(c, "death"));
                            AttackParams p = clip != null ? ReadAttack(attacker, clip, false) : new AttackParams { Damage = 3f, Delay = 0f, Type = Rust.DamageType.Bite, Range = 1.2f, Angle = 60f };
                            float hp = victim.health;
                            string summary = ResolveHits(attacker, null, 0f, p, false);
                            float lost = hp - victim.health;
                            lines.Add((lost > 0f ? "PASS " : "FAIL ") + sp.Id.PadRight(10) + (clip != null ? clip.Name : "(no attack clip: a nip)") + "  damage " + p.Damage.ToString("0") + " " + p.Type + "  range " + p.Range.ToString("0.0") + "  cow lost " + lost.ToString("0.0") + "  [" + summary + "]");
                        }
                        catch (Exception ex)
                        {
                            lines.Add("FAIL " + sp.Id + ": " + ex.Message);
                        }
                        finally
                        {
                            Unpark(attacker, "test");
                            Unpark(victim, "test");
                            if (Valid(attacker)) attacker.Kill();
                            if (Valid(victim)) victim.Kill();
                        }
                        timer.Once(0.3f, next);
                    });
                });
            };
            next();
            reply("attacks test started: " + list.Count + " species, the result comes to the log in ~" + (list.Count * 2 + 4) + " s");
        }

        // The ring of flames of the ritual: made, low, fixed in place, burning, gone again.
        internal void TestFlames(Action<string> reply, Vector3 pos)
        {
            RitualModule ritual = Modules.OfType<RitualModule>().FirstOrDefault();
            if (ritual == null)
            {
                reply("flames: no ritual module");
                return;
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            var rc = new RitualCorpse { Species = SpeciesById("cow"), Position = pos, Rotation = Quaternion.identity, Died = Time.time };
            ritual.SpawnFlames(rc);
            var lines = new List<string>();
            timer.Once(1.5f, () =>
            {
                int alive = rc.Flames.Count(f => f != null && !f.IsDestroyed);
                FireBall first = rc.Flames.FirstOrDefault() as FireBall;
                Rigidbody body = first != null ? first.GetComponent<Rigidbody>() : null;
                lines.Add((alive == Cfg.Ritual.FireCount ? "PASS " : "FAIL ") + "the ring has " + alive + " of " + Cfg.Ritual.FireCount + " flames");
                lines.Add((first != null ? "PASS " : "FAIL ") + "flame prefab " + (first != null ? first.PrefabName : Cfg.Ritual.FirePrefab) + ", scale " + (first != null ? first.transform.localScale.x.ToString("0.00") : "?") + ", damage/s " + (first != null ? first.damagePerSecond.ToString("0.0") : "?") + ", lifetime " + (first != null ? first.lifeTimeMin.ToString("0.0") : "?") + " s");
                lines.Add((body == null || body.isKinematic ? "PASS " : "FAIL ") + "the flames stay where they are (kinematic " + (body != null && body.isKinematic) + ")");
                lines.Add("INFO a flame is " + (first != null ? Vector3.Distance(first.transform.position, pos).ToString("0.00") : "?") + " m from the middle");
                ritual.KillFlames(rc);
                timer.Once(0.5f, () =>
                {
                    lines.Add((rc.Flames.Count == 0 ? "PASS " : "FAIL ") + "the flames are removed with the ritual (" + rc.Flames.Count + " left in the list)");
                    Puts("[test] flames:\n" + string.Join("\n", lines.ToArray()));
                });
            });
            reply("flames test started: the result comes to the log in ~3 s");
        }

        // A hit wild animal that is not on the always-defend list runs from its attacker.
        internal void TestScare(Action<string> reply, Vector3 pos)
        {
            BaseNPC2 cow = TestSpawn(SpeciesById("cow"), pos);
            BaseNPC2 bull = TestSpawn(SpeciesById("bull"), pos + new Vector3(30f, 0f, 0f));
            BaseNPC2 attackerBody = TestSpawn(SpeciesById("wolf"), pos + new Vector3(-3f, 0f, 0f));
            if (cow == null || bull == null || attackerBody == null)
            {
                foreach (BaseNPC2 x in new[] { cow, bull, attackerBody }) { if (x != null) x.Kill(); }
                reply("scare: a test animal is missing");
                return;
            }
            timer.Once(2f, () =>
            {
                Park(attackerBody, "test");
                float cowStart = Vector3.Distance(cow.transform.position, attackerBody.transform.position);
                Wildlife.Scare(cow, attackerBody);
                Wildlife.Defend(bull, attackerBody);
                timer.Once(6f, () =>
                {
                    float cowEnd = Vector3.Distance(cow.transform.position, attackerBody.transform.position);
                    Follower bf = FollowerOf(bull);
                    Puts("[test] scare:\n" + (cowEnd > cowStart + 4f ? "PASS " : "FAIL ") + "the cow runs from its attacker (" + cowStart.ToString("0.0") + " m -> " + cowEnd.ToString("0.0") + " m)\n"
                        + (bf != null && bf.Reason == "defend" ? "PASS " : "FAIL ") + "the bull defends (follower " + (bf != null ? bf.Reason : "none") + ")");
                    StopFollower(cow);
                    StopFollower(bull);
                    foreach (BaseNPC2 x in new[] { cow, bull, attackerBody }) { if (Valid(x)) x.Kill(); }
                });
            });
            reply("scare test started: the result comes to the log in ~8 s");
        }
    }
}
