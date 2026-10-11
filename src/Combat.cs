using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- damage of an animal attack

        // Damage the way the game's own animal attack states do it (BaseCombatEntity.OnAttacked) to everything in reach of the attack.
        // Trees, ore and corpses are gathered for the player the way a hit without a tool gathers (ResourceDispenser.DoGather).
        // Returns a short summary for the player ("cow 450>415, tree gathered").
        internal string ResolveHits(BaseNPC2 animal, BasePlayer owner, float heading, AttackParams p, bool rear)
        {
            var summary = new List<string>();
            float k = ModelScale(animal);          // a bigger animal (a boss) reaches further
            Vector3 facing = Quaternion.Euler(0f, heading, 0f) * (rear ? Vector3.back : Vector3.forward);
            Vector3 center;
            // the hit lands right at the nose (or, for a kick, right behind the tail) - never a metre in front of the animal
            Vector3 origin = AttackTip(animal, heading, rear, out center);
            float reach = (p.Range > 0f && p.Range < Cfg.Combat.HitReach ? p.Range : Cfg.Combat.HitReach) * k;
            float tall = Mathf.Max(0.8f, animal.bounds.size.y * k);       // a lower or taller target in front is still in reach
            var found = new List<BaseEntity>();
            Vis.Entities(origin, reach + 2f * k, found);
            var done = new HashSet<BaseEntity>();
            float scale = Cfg.Combat.DamageScale * ClimateDamageFactor(animal) * BossDamageFactor(animal, p.Damage);
            float halfAngle = rear ? 70f : Mathf.Clamp(p.Angle, 30f, 70f);
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == animal || (owner != null && e == owner) || e.GetParentEntity() == animal || !done.Add(e))
                {
                    continue;
                }
                Vector3 point = e.ClosestPoint(origin);
                Vector3 v = point - center;
                v.y = 0f;
                Vector3 gap = point - origin;
                float flat = new Vector2(gap.x, gap.z).magnitude;
                if (flat > reach || Mathf.Abs(gap.y) > tall || (v.sqrMagnitude > 0.01f && Vector3.Angle(facing, v) > halfAngle))
                {
                    continue;
                }
                ResourceDispenser dispenser = e.GetComponent<ResourceDispenser>();
                if (dispenser != null && (e is TreeEntity || e is OreResourceEntity || e is BaseCorpse))
                {
                    if (Cfg.Combat.Gather && owner != null)
                    {
                        HitInfo gather = new HitInfo();
                        gather.Init(owner, e, p.Type, p.Damage * scale, point);
                        gather.CanGather = true;
                        dispenser.DoGather(gather, e as BaseCorpse);
                        HitSound(e, point);
                        summary.Add(ShortName(e) + " gathered");
                    }
                    continue;
                }
                BaseCombatEntity target = e as BaseCombatEntity;
                if (target == null || target.IsDead() || !MayHit(owner, animal, target))
                {
                    continue;
                }
                float before = target.health;
                target.OnAttacked(p.Damage * scale, p.Type, animal, false);
                HitSound(target, point);
                BossHit(animal, target);
                summary.Add(ShortName(target) + " " + before.ToString("0") + ">" + Mathf.Max(0f, target.health).ToString("0"));
                BasePlayer victim = target as BasePlayer;
                if (victim != null && !victim.IsNpc && p.Push != Vector2.zero)
                {
                    Vector3 away = v.sqrMagnitude > 0.01f ? v.normalized : facing;
                    victim.DoPush(away * p.Push.y + Vector3.up * p.Push.x);
                }
                if (summary.Count >= 6)
                {
                    break;
                }
            }
            return string.Join(", ", summary.ToArray());
        }

        // The tip of the attack: the nose of the animal, or the end of its body behind it for a kick, at the height of the middle of the body.
        internal static Vector3 AttackTip(BaseEntity animal, float heading, bool rear, out Vector3 center)
        {
            Bounds b = animal.bounds;
            float k = ModelScale(animal);
            Quaternion rot = Quaternion.Euler(0f, heading, 0f);
            center = animal.transform.position + Vector3.up * b.center.y * k;
            float along = rear ? (b.center.z - b.extents.z) : (b.center.z + b.extents.z);
            return center + rot * Vector3.forward * (along * k);
        }

        // The sound of what was hit: the thud of flesh, or of the wood, rock or metal of an object (the game's own impact effects).
        internal void HitSound(BaseEntity e, Vector3 point)
        {
            string material;
            if (e is BasePlayer || e is BaseNPC2 || e is RidableHorse || e is BaseAnimalNPC || e is BaseCorpse)
            {
                material = "flesh";
            }
            else if (e is TreeEntity)
            {
                material = "wood";
            }
            else if (e is OreResourceEntity)
            {
                material = "rock";
            }
            else
            {
                material = ColliderSurface(e.GetComponentInChildren<Collider>());
            }
            if (material == "stones" || material == "gravel")
            {
                material = "rock";
            }
            string path = FindPrefabPath("impacts/blunt/" + material + "/") ?? FindPrefabPath("impacts/blunt/generic/");
            RunEffect(path, point);
            if (material == "flesh" && !(e is BaseCorpse))
            {
                // a living body bleeds where it was hit: the game's own blood impact of a bite or a cut in flesh
                RunEffect(FindPrefabPath("impacts/slash/flesh/") ?? FindPrefabPath("impacts/stab/flesh/") ?? FindPrefabPath("fleshbloodimpact"), point);
            }
        }

        private static string ShortName(BaseEntity e)
        {
            return e.ShortPrefabName.Replace(".entity", string.Empty).Replace("_deployed", string.Empty);
        }

        private bool MayHit(BasePlayer owner, BaseNPC2 animal, BaseCombatEntity target)
        {
            if (target is BasePlayer)
            {
                if (!Cfg.Combat.HitPlayers)
                {
                    return false;
                }
                BasePlayer tp = (BasePlayer)target;
                if (tp.IsDead() || (tp.IsSleeping() && false))
                {
                    return false;
                }
                if (IsFormPlayer(tp))
                {
                    return false;   // the hidden human is not a target; its animal is
                }
                if (owner != null && SameParty(owner, tp) && Cfg.Combat.SparePartyMembers)
                {
                    return false;
                }
            }
            else if (target is BaseNPC2 || target is RidableHorse || target is BaseAnimalNPC)
            {
                if (!Cfg.Combat.HitAnimals)
                {
                    return false;
                }
                Driver other = DriverOfAnimal(target);
                if (other != null && owner != null && SameParty(owner, other.Player) && Cfg.Combat.SparePartyMembers)
                {
                    return false;
                }
                TamedRecord rec = RecordOf(target);
                if (rec != null && owner != null && Cfg.Combat.SparePartyMembers && MayUse(owner, rec.Owner, "team"))
                {
                    return false;   // never hit the animals of your own team
                }
                if (IsBossPair(animal, target))
                {
                    return false;   // bosses may fight each other but deal no damage to each other
                }
            }
            else if (!Cfg.Combat.HitStructures)
            {
                return false;
            }
            if (Cfg.Combat.SparesSafeZone && target.InSafeZone())
            {
                return false;
            }
            object veto = Interface.CallHook("CanBeastmasterAttack", owner, animal, target);
            return veto == null;
        }

        internal bool SameParty(BasePlayer a, BasePlayer b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            if (a == b)
            {
                return true;
            }
            RelationshipManager.PlayerTeam team = a.Team;
            return team != null && team.members.Contains(b.userID);
        }

        // Rabbit, squirrel, frog and other critters have no attack clip: a call and a very small nip (assumption A12).
        internal void CritterNip(Driver d)
        {
            if (!Valid(d.Animal))
            {
                return;
            }
            AttackParams p = new AttackParams { Damage = 3f, Delay = 0f, Type = Rust.DamageType.Bite, Range = 0.35f, Angle = 60f, Push = Vector2.zero };
            string summary = ResolveHits(d.Animal, d.Player, d.Heading, p, false);
            if (!string.IsNullOrEmpty(summary))
            {
                Tell(d.Player, "HitSummary", summary);
            }
        }

        // The game's attack states turn the animal toward its target as the attack starts (State_Attack.AlignToTarget).
        // A player cannot aim an animal that precisely, so the same happens here: the nearest valid target in front is faced.
        internal float AimAtTarget(Driver d, AttackParams p, bool rear)
        {
            if (rear || !Cfg.Combat.AutoAim)
            {
                return d.Heading;
            }
            float heading = d.Heading;
            Vector3 facing = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Vector3 origin;
            Vector3 nose = AttackTip(d.Animal, heading, false, out origin);
            float reach = Vector3.Distance(new Vector3(nose.x, origin.y, nose.z), origin) + Cfg.Combat.HitReach * ModelScale(d.Animal) + 0.4f;
            var found = new List<BaseEntity>();
            Vis.Entities(origin, reach + 1f, found);
            BaseEntity best = null;
            float bestScore = float.MaxValue;
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == d.Animal || e == d.Player || e.GetParentEntity() == d.Animal)
                {
                    continue;
                }
                bool gatherable = Cfg.Combat.Gather && (e is TreeEntity || e is OreResourceEntity || e is BaseCorpse);
                BaseCombatEntity target = e as BaseCombatEntity;
                if (!gatherable && (target == null || target.IsDead() || !MayHit(d.Player, d.Animal, target)))
                {
                    continue;
                }
                Vector3 to = e.ClosestPoint(origin) - origin;
                to.y = 0f;
                if (to.magnitude > reach)
                {
                    continue;
                }
                float angle = Vector3.Angle(facing, to);
                if (angle > Cfg.Combat.AutoAimAngle)
                {
                    continue;
                }
                float score = angle * 0.05f + to.magnitude;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }
            if (best == null)
            {
                return heading;
            }
            Vector3 dir = best.ClosestPoint(origin) - origin;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.01f ? Yaw(dir) : heading;
        }

        // Headless test of the attack pipeline (server console only): a bear bites a cow two metres in front of it.
        internal void TestCombat(Action<string> reply, Vector3 basePos)
        {
            SpeciesDef bearDef = SpeciesById("bear"), cowDef = SpeciesById("cow");
            if (bearDef == null || cowDef == null || !bearDef.Available || !cowDef.Available)
            {
                reply("combat test: species missing");
                return;
            }
            basePos.y = TerrainMeta.HeightMap.GetHeight(basePos) + 0.2f;
            BaseNPC2 attacker = GameManager.server.CreateEntity(bearDef.Prefab, basePos, Quaternion.identity) as BaseNPC2;
            BaseNPC2 victim = GameManager.server.CreateEntity(cowDef.Prefab, basePos + new Vector3(0f, 0f, 2.2f), Quaternion.identity) as BaseNPC2;
            if (attacker == null || victim == null)
            {
                reply("combat test: could not spawn");
                return;
            }
            attacker.Spawn();
            victim.Spawn();
            timer.Once(1.2f, () =>
            {
                Park(attacker, "test");
                Park(victim, "test");
                attacker.transform.rotation = Quaternion.identity;
                victim.transform.position = attacker.transform.position + Vector3.forward * 2.2f;
                Physics.SyncTransforms();
                // the colliders follow the transforms at the next physics step
                timer.Once(0.4f, () =>
                {
                    try
                    {
                        ClipRef clip = Clips(attacker).FirstOrDefault(c => Has(c, "attack"));
                        if (clip == null)
                        {
                            Puts("[test] combat: bear has no attack clip");
                            return;
                        }
                        AttackParams p = ReadAttack(attacker, clip, false);
                        float before = victim.health;
                        string summary = ResolveHits(attacker, null, 0f, p, false);
                        Puts("[test] combat: " + clip.Name + " " + p.Damage + " " + p.Type + " reach " + p.Range + " angle " + p.Angle + " -> [" + summary + "] cow health " + before.ToString("0") + " > " + victim.health.ToString("0"));
                    }
                    finally
                    {
                        Unpark(attacker, "test");
                        Unpark(victim, "test");
                        if (Valid(attacker)) attacker.Kill();
                        if (Valid(victim)) victim.Kill();
                    }
                });
            });
        }

        // Bull and ram sprinting through a wooden barricade break it (R4.6).
        private float nextRamCheck;
    }
}
