using System;
using System.Collections.Generic;
using System.Linq;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // Animals the plugin walks around (tamed followers, the chain behind an animal-form leader, a scared wolf, a wolf pack that keeps
        // close to a wolf player ...). The animal's own AI stays parked; its navigation agent does the path finding with the species'
        // authored gaits, so the animal looks and sounds exactly like a free animal on the move.
        internal readonly Dictionary<ulong, Follower> Followers = new Dictionary<ulong, Follower>();

        internal Follower FollowerOf(BaseNetworkable animal)
        {
            Follower f;
            return animal != null && animal.net != null && Followers.TryGetValue(animal.net.ID.Value, out f) && f != null ? f : null;
        }

        // The entries of followers whose animal is gone (the component dies with it) are dropped from time to time.
        private int followerPrune;

        private void PruneFollowers()
        {
            foreach (KeyValuePair<ulong, Follower> kv in Followers.ToList())
            {
                if (kv.Value == null)
                {
                    Followers.Remove(kv.Key);
                }
            }
        }

        internal Follower StartFollower(BaseNPC2 animal, string reason, Func<Vector3?> where)
        {
            if (!Valid(animal))
            {
                return null;
            }
            if (Followers.Count > 40 && ++followerPrune % 25 == 0)
            {
                PruneFollowers();
            }
            StopFollower(animal);
            Park(animal, reason);
            Follower f = animal.gameObject.AddComponent<Follower>();
            f.Setup(this, animal, reason, where);
            Followers[animal.net.ID.Value] = f;
            return f;
        }

        internal void StopFollower(BaseNetworkable animal)
        {
            Follower f = FollowerOf(animal);
            if (f != null)
            {
                Followers.Remove(animal.net.ID.Value);
                f.End();
            }
        }

        internal void StopAllFollowers()
        {
            foreach (Follower f in Followers.Values.ToList())
            {
                if (f != null)
                {
                    f.End();
                }
            }
            Followers.Clear();
        }

        // Authored gait speed of an animal (the agent's own table), scaled by the config.
        internal float AnimalGaitSpeed(BaseNPC2 animal, SpeciesDef species, int gait)
        {
            RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
            if (agent == null)
            {
                return 1f;
            }
            float speed;
            switch (gait)
            {
                case 0: speed = agent.sneakSpeed; break;
                case 1: speed = agent.walkSpeed; break;
                case 2: speed = agent.jogSpeed; break;
                case 3: speed = agent.runSpeed; break;
                case 4: speed = agent.sprintSpeed; break;
                default: speed = agent.fullSprintSpeed; break;
            }
            return speed * (species != null ? SpeedScaleOf(species) : 1f);
        }

        internal sealed class Follower : FacepunchBehaviour
        {
            public BaseNPC2 Animal;
            public string Reason;
            public Func<Vector3?> Where;                  // where to be (null = stand still)
            public float StopDistance = 4f;               // close enough
            public float RunDistance = 22f;               // beyond this the animal runs
            public float TeleportDistance = 0f;           // beyond this it is moved next to the destination (0 = never)
            public bool Flee;                             // run away from Where instead of toward it
            public float FleeDistance = 18f;
            public Func<BaseCombatEntity> Target;         // attack this (overrides Where while it lives)
            public BasePlayer AttackOwner;                // the owner that sent the animal (gets no gather credit)
            public bool MatchLeader;                      // adopt the leader's gait (chains)
            public Func<float> LeaderSpeed;

            private Beastmaster bm;
            private SpeciesDef species;
            private RustNavMeshAgent agent;
            private float nextAttack;
            private float nextIdle;
            private bool ended;
            private int attackIndex;

            public void Setup(Beastmaster plugin, BaseNPC2 animal, string reason, Func<Vector3?> where)
            {
                bm = plugin;
                Animal = animal;
                Reason = reason;
                Where = where;
                species = SpeciesOf(animal);
                agent = animal.GetComponent<RustNavMeshAgent>();
                nextIdle = Time.time + UnityEngine.Random.Range(4f, 9f);
                InvokeRepeating(Think, UnityEngine.Random.Range(0.1f, 0.5f), 0.4f);
            }

            private void Think()
            {
                if (ended)
                {
                    return;
                }
                if (!Valid(Animal) || Animal.IsDead() || agent == null)
                {
                    End();
                    return;
                }
                if (bm.DriverOfAnimal(Animal) != null)
                {
                    return;      // a player controls it right now
                }
                BaseCombatEntity target = Target != null ? Target() : null;
                if (target != null && Alive(target))
                {
                    Chase(target);
                    return;
                }
                Vector3? where = Where != null ? Where() : null;
                if (!where.HasValue)
                {
                    agent.ResetPath();
                    Idle();
                    return;
                }
                Vector3 pos = Animal.transform.position;
                Vector3 dest = where.Value;
                float dist = Vector3.Distance(pos, dest);
                if (Flee)
                {
                    Vector3 away = pos - dest;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.01f)
                    {
                        away = Animal.transform.forward;
                    }
                    if (dist > FleeDistance)
                    {
                        agent.ResetPath();
                        Idle();
                        return;
                    }
                    Vector3 spot = pos + away.normalized * FleeDistance;
                    spot.y = TerrainMeta.HeightMap.GetHeight(spot);
                    Go(spot, RustNavMeshAgent.Speeds.Sprint, 1f);
                    return;
                }
                if (TeleportDistance > 0f && dist > TeleportDistance)
                {
                    Vector3 spot = GroundNear(dest, 5f);
                    if (agent.WarpToWorldPosition(spot))
                    {
                        agent.ResetPath();
                    }
                    return;
                }
                if (dist <= StopDistance)
                {
                    agent.ResetPath();
                    Idle();
                    return;
                }
                RustNavMeshAgent.Speeds gait = dist > RunDistance ? RustNavMeshAgent.Speeds.Run : dist > StopDistance * 3f ? RustNavMeshAgent.Speeds.Jog : RustNavMeshAgent.Speeds.Walk;
                if (MatchLeader && LeaderSpeed != null)
                {
                    float speed = LeaderSpeed();
                    gait = speed > agent.runSpeed * 0.9f ? RustNavMeshAgent.Speeds.Run : speed > agent.jogSpeed * 0.8f ? RustNavMeshAgent.Speeds.Jog : speed > 0.2f ? RustNavMeshAgent.Speeds.Walk : gait;
                    if (dist > StopDistance * 2.5f && gait < RustNavMeshAgent.Speeds.Jog)
                    {
                        gait = RustNavMeshAgent.Speeds.Jog;
                    }
                }
                Go(dest, gait, StopDistance * 0.7f);
            }

            private void Go(Vector3 destination, RustNavMeshAgent.Speeds gait, float stopDistance)
            {
                agent.SetDestinationWithParams(destination, true, gait, null, null, null, null, stopDistance);
            }

            private void Idle()
            {
                if (Time.time < nextIdle)
                {
                    return;
                }
                nextIdle = Time.time + UnityEngine.Random.Range(8f, 16f);
                List<ClipRef> clips = bm.Clips(Animal).Where(c => (Has(c, "idle") || Has(c, "look") || Has(c, "breathe")) && !Has(c, "death") && !Has(c, "turn") && !Has(c, "sleep") && !Has(c, "attack")).ToList();
                if (clips.Count > 0)
                {
                    PlayClip(Animal, clips[UnityEngine.Random.Range(0, clips.Count)]);
                }
            }

            // Goes for a target and bites it with the animal's own attack (the same damage pipeline as a controlled animal).
            private void Chase(BaseCombatEntity target)
            {
                Vector3 pos = Animal.transform.position;
                Vector3 to = target.transform.position - pos;
                to.y = 0f;
                // close enough for the hit, which lands right at the nose: the nose has to come up to the target's body
                float k = ModelScale(Animal);
                float reach = (Animal.bounds.center.z + Animal.bounds.extents.z) * k + bm.Cfg.Combat.HitReach * 0.7f * k;
                Vector3 gap = target.ClosestPoint(pos) - pos;
                gap.y = 0f;
                if (gap.magnitude > reach)
                {
                    Go(target.transform.position, RustNavMeshAgent.Speeds.Sprint, Mathf.Max(0.3f, reach * 0.5f));
                    return;
                }
                agent.ResetPath();
                if (Time.time < nextAttack)
                {
                    return;
                }
                List<ClipRef> clips = bm.Clips(Animal).Where(c => (Has(c, "attack") || Has(c, "headbutt") || Has(c, "bite")) && !Has(c, "death")).ToList();
                if (clips.Count == 0)
                {
                    return;
                }
                ClipRef clip = clips[attackIndex++ % clips.Count];
                AttackParams p = ReadAttack(Animal, clip, false);
                float heading = Yaw(to);
                Animal.transform.rotation = Quaternion.Euler(0f, heading, 0f);
                PlayClip(Animal, clip);
                nextAttack = Time.time + Mathf.Max(bm.Cfg.Combat.Cooldown, p.Delay + 0.5f);
                BaseNPC2 animal = Animal;
                Invoke(() =>
                {
                    if (!ended && Valid(animal) && !animal.IsDead())
                    {
                        bm.ResolveHits(animal, null, heading, p, false);
                    }
                }, p.Delay);
            }

            public void End()
            {
                if (ended)
                {
                    return;
                }
                ended = true;
                CancelInvoke(Think);
                if (Valid(Animal))
                {
                    if (agent != null)
                    {
                        agent.ResetPath();
                    }
                    bm.Followers.Remove(Animal.net.ID.Value);
                    bm.Unpark(Animal, Reason);
                }
                Destroy(this);
            }
        }
    }
}
