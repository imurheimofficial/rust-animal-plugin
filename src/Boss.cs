using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- boss animals (R20)
        // Rare versions of the horse, the bull, the ram and the stag: they appear at night in a burst of fire with 4000 health, hit for 99, run
        // 1.5x faster, are immune to fire and set their victims on fire. They drop scrap and a saddle that tames the next boss of that kind.
        // The look is limited by what a server can do: a black horse (the game's breed), effects from the game's own effect prefabs; no
        // bigger model, no glowing eyes.

        internal BossModule Bosses;
        internal const ulong BossSaddleSkin = 71000UL;          // item skin + kind index marks a boss saddle
        internal static readonly string[] BossKinds = { "horse", "bull", "ram", "stag" };

        internal BossInfo BossOf(BaseNetworkable e)
        {
            BaseEntity be = e as BaseEntity;
            BossInfo info;
            return be != null && be.skinID >= TagBase && Data.Bosses.TryGetValue(be.skinID, out info) ? info : null;
        }

        internal float BossDamageFactor(BaseNPC2 animal, float baseDamage)
        {
            BossInfo info = Cfg.Features.BossAnimals ? BossOf(animal) : null;
            if (info == null)
            {
                return 1f;
            }
            return Mathf.Max(1f, Cfg.Boss.Damage) / Mathf.Max(1f, baseDamage) * (info.Mini ? 0.5f : 1f);
        }

        // Bosses may fight each other but deal no damage to each other.
        internal bool IsBossPair(BaseEntity attacker, BaseEntity target)
        {
            return BossOf(attacker) != null && BossOf(target) != null;
        }

        // The hit of a boss sets the player on fire for a few seconds (like stepping into a campfire).
        internal void BossHit(BaseEntity attacker, BaseCombatEntity target)
        {
            BasePlayer p = target as BasePlayer;
            if (p != null && Bosses != null && BossOf(attacker) != null)
            {
                Bosses.Ignite(p);
            }
        }

        internal bool TryBossSaddle(BasePlayer player, BaseEntity entity)
        {
            return Bosses != null && Bosses.UseSaddle(player, entity);
        }

        internal static bool IsNight()
        {
            if (TOD_Sky.Instance != null)
            {
                return TOD_Sky.Instance.IsNight;
            }
            float hour = ConVar.Env.time;
            return hour < 6f || hour >= 20f;
        }

        internal sealed class BossModule : Module
        {
            public override int Order => 70;

            private sealed class Run
            {
                public BaseCombatEntity Entity;
                public BossInfo Info;
                public ulong Tag;
                public float NextEffect;
                public float NextRetarget;
                public BaseCombatEntity Target;
            }

            private readonly Dictionary<ulong, Run> runs = new Dictionary<ulong, Run>();
            private readonly List<float> kills = new List<float>();
            private readonly HashSet<ulong> burning = new HashSet<ulong>();
            private float nextSpawnCheck;
            private float nextVanishCheck;

            public int Alive { get { return runs.Count; } }

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Bosses = this;
                bm.Subscribe("OnEntitySpawned");
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["BossRoar"] = "A terrible {0} call echoes across the land - a boss {0} has risen!",
                    ["BossTamed"] = "The boss {0} bows to the saddle: it is yours now.",
                    ["BossDrop"] = "The boss {0} is dead. It dropped {1} scrap and a boss saddle.",
                    ["BossGone"] = "A boss {0} has vanished with the night.",
                };
            }

            public override void OnServerInitialized()
            {
                int found = 0;
                var seen = new HashSet<ulong>();
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                {
                    BaseCombatEntity c = net as BaseCombatEntity;
                    if (c != null && c.skinID >= TagBase && Bm.Data.Bosses.ContainsKey(c.skinID))
                    {
                        Setup(c, Bm.Data.Bosses[c.skinID], false);
                        seen.Add(c.skinID);
                        found++;
                    }
                }
                foreach (ulong tag in Bm.Data.Bosses.Keys.ToList())
                {
                    if (!seen.Contains(tag))
                    {
                        Bm.Data.Bosses.Remove(tag);
                    }
                }
                Bm.Log(3, "Bosses: " + found + " restored");
            }

            public override void OnDisable()
            {
                foreach (Run r in runs.Values)
                {
                    Calm(r);
                }
            }

            public override void Unload()
            {
                OnDisable();
                runs.Clear();
            }

            // ---------------------------------------------------------------- creating a boss

            public BaseCombatEntity SpawnBoss(string kind, Vector3 pos, bool mini = false)
            {
                SpeciesDef sp = SpeciesById(kind);
                if (sp == null || !Bm.SpeciesEnabled(sp))
                {
                    return null;
                }
                pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.3f;
                BaseCombatEntity e = GameManager.server.CreateEntity(sp.Prefab, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)) as BaseCombatEntity;
                if (e == null)
                {
                    return null;
                }
                ulong tag = Bm.NewTag();
                e.skinID = tag;
                var info = new BossInfo { Kind = kind, Mini = mini };
                Bm.Data.Bosses[tag] = info;
                e.Spawn();
                Bm.EnforceLook(e, sp);
                Setup(e, info, true);
                Bm.RunEffect(Bm.Cfg.Boss.SpawnEffect, pos);
                // no chat line: the appearance of a boss is a surprise for the players (the burst of fire and the roar are heard and seen)
                Bm.Log(2, "Boss " + kind + (mini ? " (mini)" : string.Empty) + " spawned at " + pos);
                return e;
            }

            // Health, speed and look (also after a restart: the game restores the health itself, the speeds and the flags are set again).
            public void Setup(BaseCombatEntity e, BossInfo info, bool fresh)
            {
                float hp = info.Mini ? Bm.Cfg.Boss.MiniHealth : Bm.Cfg.Boss.Health;
                if (fresh || e.MaxHealth() < hp - 1f)
                {
                    e.InitializeHealth(hp, hp);
                }
                Bm.ScaleModel(e, info.Mini ? Bm.Cfg.Boss.MiniScale : Bm.Cfg.Boss.Scale);
                LivestockAnimal coat = e as LivestockAnimal;
                if (coat != null && fresh && Bm.Cfg.Boss.DarkCoat)
                {
                    coat.Genes = DarkGenes(coat.Genes);
                }
                BaseNPC2 npc = e as BaseNPC2;
                if (npc != null)
                {
                    RustNavMeshAgent agent = npc.GetComponent<RustNavMeshAgent>();
                    if (agent != null)
                    {
                        float k = Bm.Cfg.Boss.SpeedScale;
                        agent.sneakSpeed *= k;
                        agent.walkSpeed *= k;
                        agent.jogSpeed *= k;
                        agent.runSpeed *= k;
                        agent.sprintSpeed *= k;
                        agent.fullSprintSpeed *= k;
                    }
                }
                RidableHorse horse = e as RidableHorse;
                if (horse != null)
                {
                    KeepRiderSize(horse, info.Mini ? Bm.Cfg.Boss.MiniScale : Bm.Cfg.Boss.Scale);
                }
                if (horse != null && Bm.Stable != null)
                {
                    HorseState hs = Bm.Stable.Register(horse);
                    hs.SpeedMult = Bm.Cfg.Boss.SpeedScale;
                    hs.Damage = Bm.Cfg.Boss.Damage * (info.Mini ? 0.5f : 1f);
                    if (SeatsOf(horse) > 0 && !info.Tamed)
                    {
                        SetSeats(horse, 0);
                    }
                    int black = BlackBreed(horse);
                    if (black >= 0 && fresh)
                    {
                        horse.SetBreed(black);
                    }
                }
                runs[e.skinID] = new Run { Entity = e, Info = info, Tag = e.skinID, NextEffect = Time.time + 1f };
                AttachEyes(e, info.Kind);
            }

            // The coat of the game's livestock is drawn from the genes: the patch colour darkens with a bad Dung gene and the patches grow with a good
            // Yield and Longevity gene. These two settings give the blackest animal the genes can make.
            private static int DarkGenes(int genes)
            {
                if (!LivestockGenome.IsRolled(genes))
                {
                    genes = LivestockGenome.Roll();
                }
                genes = LivestockGenome.WithPair(genes, LivestockGene.Dung, LivestockAllele.Bad, LivestockAllele.Bad);
                genes = LivestockGenome.WithPair(genes, LivestockGene.Yield, LivestockAllele.Good, LivestockAllele.Good);
                genes = LivestockGenome.WithPair(genes, LivestockGene.Longevity, LivestockAllele.Good, LivestockAllele.Good);
                return genes;
            }

            private const ulong EyeSkin = 71995UL;

            // A narrow coloured spot of the game on each eye of the boss: no model (the cinematic lights are only light), pointing the way the boss looks.
            private void AttachEyes(BaseCombatEntity e, string kind)
            {
                string path;
                if (e == null || !Bm.Cfg.Boss.Eyes.TryGetValue(kind, out path) || string.IsNullOrEmpty(path) || GameManager.server.FindPrefab(path) == null)
                {
                    return;
                }
                foreach (BaseEntity child in e.children)
                {
                    if (child != null && child.skinID == EyeSkin)
                    {
                        return;
                    }
                }
                var eyes = new List<Transform>();
                Transform head = null;
                if (e.model != null && e.model.boneTransforms != null)
                {
                    foreach (Transform bone in e.model.boneTransforms)
                    {
                        if (bone == null)
                        {
                            continue;
                        }
                        string n = bone.name.ToLowerInvariant();
                        if (n.Contains("eye") && !n.Contains("lid") && !n.Contains("brow") && !n.Contains("lash"))
                        {
                            eyes.Add(bone);
                        }
                        else if (n.Contains("head") && (head == null || bone.name.Length < head.name.Length))
                        {
                            head = bone;
                        }
                    }
                }
                var spots = new List<KeyValuePair<Transform, Vector3>>();   // bone, world position of the light
                if (eyes.Count > 0)
                {
                    foreach (Transform eye in eyes.Take(2))
                    {
                        spots.Add(new KeyValuePair<Transform, Vector3>(eye, eye.position));
                    }
                }
                else if (head != null)
                {
                    float side = Mathf.Max(0.04f, e.bounds.size.x * 0.08f) * ModelScale(e);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        spots.Add(new KeyValuePair<Transform, Vector3>(head, head.position + e.transform.right * side * s + e.transform.forward * side * 1.2f + e.transform.up * side * 0.5f));
                    }
                }
                else
                {
                    Bounds b = e.bounds;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        spots.Add(new KeyValuePair<Transform, Vector3>(null, e.transform.TransformPoint(new Vector3(b.center.x + s * b.extents.x * 0.25f, b.max.y * 0.85f, b.center.z + b.extents.z * 0.85f))));
                    }
                }
                foreach (KeyValuePair<Transform, Vector3> spot in spots)
                {
                    BaseEntity lamp = GameManager.server.CreateEntity(path, spot.Value, e.transform.rotation);
                    if (lamp == null)
                    {
                        continue;
                    }
                    lamp.skinID = EyeSkin;
                    lamp.enableSaving = false;
                    if (spot.Key != null)
                    {
                        lamp.SetParent(e, spot.Key.name, true, false);
                    }
                    else
                    {
                        lamp.SetParent(e, true, false);
                    }
                    lamp.Spawn();
                }
            }

            // The lamp is switched on again if the game's power logic turned it off.
            private static void KeepEyesLit(BaseEntity e)
            {
                foreach (BaseEntity child in e.children)
                {
                    if (child != null && child.skinID == EyeSkin && !child.HasFlag(BaseEntity.Flags.On))
                    {
                        child.SetFlagLocal(BaseEntity.Flags.On, true);
                        child.SendNetworkUpdate();
                    }
                }
            }

            // The seats of a bigger horse sit where the saddle is (they are children of the horse), but keep normal size: the rider is not enlarged.
            private static void KeepRiderSize(RidableHorse horse, float scale)
            {
                if (horse.mountPoints == null)
                {
                    return;
                }
                foreach (BaseVehicle.MountPointInfo mp in horse.mountPoints)
                {
                    if (mp != null && mp.mountable != null && !mp.mountable.IsDestroyed)
                    {
                        mp.mountable.networkEntityScale = !Mathf.Approximately(scale, 1f);
                        mp.mountable.transform.localScale = Vector3.one / Mathf.Max(0.1f, scale);
                        mp.mountable.SendNetworkUpdate();
                    }
                }
            }

            private static int BlackBreed(RidableHorse horse)
            {
                for (int i = 0; i < horse.breeds.Length; i++)
                {
                    HorseBreed b = horse.breeds[i];
                    if (b != null && (b.name.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0 || b.breedName.english.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return i;
                    }
                }
                return -1;
            }

            // ---------------------------------------------------------------- the ticking brain: spawning, effects, hunting, vanishing

            public override void Tick(float now)
            {
                if (!Bm.Cfg.Features.BossAnimals)
                {
                    return;
                }
                if (now >= nextSpawnCheck)
                {
                    nextSpawnCheck = now + Mathf.Max(30f, Bm.Cfg.Boss.CheckSeconds);
                    TrySpawn();
                }
                bool day = Bm.Cfg.Boss.NightOnly && !IsNight();
                bool vanishCheck = now >= nextVanishCheck;
                if (vanishCheck)
                {
                    nextVanishCheck = now + 10f;
                }
                foreach (Run r in runs.Values.ToList())
                {
                    if (!Valid(r.Entity) || r.Entity.IsDead())
                    {
                        runs.Remove(r.Tag);
                        continue;
                    }
                    if (vanishCheck && day && !r.Info.Tamed && !r.Info.Mini)
                    {
                        Vanish(r);
                        continue;
                    }
                    if (now >= r.NextEffect)
                    {
                        r.NextEffect = now + 1.5f;
                        PlayEffects(r);
                        KeepEyesLit(r.Entity);
                    }
                    if (!r.Info.Tamed && now >= r.NextRetarget)
                    {
                        r.NextRetarget = now + 1f;
                        Hunt(r, now);
                    }
                }
                if (kills.Count > 0)
                {
                    kills.RemoveAll(t => now - t > Bm.Cfg.Boss.GroupSeconds);
                }
            }

            private void TrySpawn()
            {
                if (runs.Count >= Bm.Cfg.Boss.MaxAlive || (Bm.Cfg.Boss.NightOnly && !IsNight()))
                {
                    return;
                }
                if (UnityEngine.Random.Range(0f, 100f) >= Bm.Cfg.Boss.SpawnChance)
                {
                    return;
                }
                List<BasePlayer> players = BasePlayer.activePlayerList.Where(p => p != null && !p.IsDead() && !p.IsSleeping() && !p.IsNpc && !p.InSafeZone()).ToList();
                if (players.Count == 0)
                {
                    return;
                }
                BasePlayer near = players[UnityEngine.Random.Range(0, players.Count)];
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    Vector3 pos = near.transform.position + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * UnityEngine.Random.Range(90f, 200f);
                    pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                    if (WaterLevel.GetWaterDepth(pos, true, true, null) > 0.2f)
                    {
                        continue;
                    }
                    SpawnBoss(BossKinds[UnityEngine.Random.Range(0, BossKinds.Length)], pos);
                    return;
                }
            }

            private void Vanish(Run r)
            {
                SpeciesDef sp = SpeciesById(r.Info.Kind);
                Bm.RunEffect(Bm.Cfg.Form.SmokePrefab, r.Entity.transform.position + Vector3.up);
                Bm.Data.Bosses.Remove(r.Tag);
                runs.Remove(r.Tag);
                if (Bm.Stable != null && r.Entity is RidableHorse)
                {
                    Bm.Stable.Forget((RidableHorse)r.Entity);
                }
                r.Entity.Kill();
                if (sp != null)
                {
                    Bm.Log(3, "Boss " + sp.Id + " vanished with the night");
                }
            }

            private void PlayEffects(Run r)
            {
                List<string> list;
                if (!Bm.Cfg.Boss.Effects.TryGetValue(r.Info.Kind, out list) || list == null)
                {
                    return;
                }
                Vector3 pos = r.Entity.transform.position + Vector3.up * 0.8f;
                foreach (string fx in list)
                {
                    if (!string.IsNullOrEmpty(fx) && GameManager.server.FindPrefab(fx) != null)
                    {
                        Effect.server.Run(fx, pos, Vector3.up);
                    }
                }
            }

            // ---------------------------------------------------------------- hunting

            private bool Carries(BasePlayer p, string kind)
            {
                return FindSaddle(p, kind) != null;
            }

            private void Hunt(Run r, float now)
            {
                Vector3 pos = r.Entity.transform.position;
                BasePlayer best = null;
                float bestDist = Bm.Cfg.Boss.AggroRadius;
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || p.IsNpc || p.InSafeZone() || Bm.IsFormPlayer(p))
                    {
                        continue;
                    }
                    float d = Vector3.Distance(p.transform.position, pos);
                    if (d < bestDist && !Carries(p, r.Info.Kind))
                    {
                        bestDist = d;
                        best = p;
                    }
                }
                r.Target = best;
                BaseNPC2 npc = r.Entity as BaseNPC2;
                if (npc != null && !Bm.IsTamed(npc) && Bm.DriverOfAnimal(npc) == null)
                {
                    Follower f = Bm.FollowerOf(npc);
                    if (best != null && f == null)
                    {
                        f = Bm.StartFollower(npc, "boss", null);
                        if (f != null)
                        {
                            f.TeleportDistance = 0f;
                            Run run = r;
                            f.Target = () => run.Target != null && Alive(run.Target) ? run.Target : null;
                        }
                    }
                    else if (best == null && f != null && f.Reason == "boss")
                    {
                        Bm.StopFollower(npc);
                    }
                }
                else if (r.Entity is RidableHorse && Bm.Stable != null && best != null)
                {
                    Bm.Stable.Attack((RidableHorse)r.Entity, best, 3f);
                }
            }

            private void Calm(Run r)
            {
                BaseNPC2 npc = r.Entity as BaseNPC2;
                if (npc != null && Valid(npc))
                {
                    Follower f = Bm.FollowerOf(npc);
                    if (f != null && f.Reason == "boss")
                    {
                        Bm.StopFollower(npc);
                    }
                }
            }

            // ---------------------------------------------------------------- fire, immunity

            public void Ignite(BasePlayer p)
            {
                if (p == null || p.IsDead() || !burning.Add(p.userID))
                {
                    return;
                }
                int ticks = Mathf.Max(1, Mathf.RoundToInt(Bm.Cfg.Boss.BurnSeconds));
                string fire = "assets/bundled/prefabs/fx/player/onfire.prefab";
                Bm.timer.Repeat(1f, ticks, () =>
                {
                    if (p == null || p.IsDead() || !p.IsConnected)
                    {
                        return;
                    }
                    p.Hurt(new HitInfo(null, p, Rust.DamageType.Heat, Bm.Cfg.Boss.BurnDamage, p.transform.position));
                    if (GameManager.server.FindPrefab(fire) != null)
                    {
                        Effect.server.Run(fire, p.transform.position + Vector3.up, Vector3.up);
                    }
                });
                Bm.timer.Once(ticks + 1f, () => burning.Remove(p.userID));
            }

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                if (runs.Count == 0 || info == null || entity == null || entity.skinID < TagBase || !runs.ContainsKey(entity.skinID))
                {
                    return null;
                }
                if (info.damageTypes.Get(Rust.DamageType.Heat) > 0f)
                {
                    info.damageTypes.Set(Rust.DamageType.Heat, 0f);          // immune to fire
                }
                if (info.Initiator != null && Bm.BossOf(info.Initiator) != null)
                {
                    return true;                                              // no damage between bosses
                }
                return null;
            }

            // ---------------------------------------------------------------- loot

            private static string DisplayOf(string kind)
            {
                SpeciesDef sp = SpeciesById(kind);
                return sp != null ? sp.Display : kind;
            }

            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                if (entity == null || entity.skinID < TagBase || !runs.ContainsKey(entity.skinID))
                {
                    return;
                }
                Run r = runs[entity.skinID];
                runs.Remove(r.Tag);
                Bm.Data.Bosses.Remove(r.Tag);
                if (r.Info.Mini)
                {
                    return;                                                    // a mini boss gives no scrap
                }
                float now = Time.time;
                bool group = kills.Count > 0 || runs.Count > 0 && runs.Values.Any(o => Valid(o.Entity) && Vector3.Distance(o.Entity.transform.position, entity.transform.position) < 150f);
                kills.Add(now);
                int scrap = ScrapOf(r.Info.Kind);
                if (group)
                {
                    scrap = Mathf.RoundToInt(scrap * Bm.Cfg.Boss.GroupBonus);
                }
                Vector3 at = entity.transform.position + Vector3.up * 0.8f;
                DropScrap(scrap, at);
                Item saddle = ItemManager.CreateByName(r.Info.Kind == "horse" ? "horse.saddle.double" : "horse.saddle.single", 1);
                if (saddle != null)
                {
                    saddle.name = "Boss Saddle: " + DisplayOf(r.Info.Kind);
                    saddle.skin = BossSaddleSkin + (ulong)Array.IndexOf(BossKinds, r.Info.Kind);
                    saddle.MarkDirty();
                    saddle.Drop(at, new Vector3(0f, 2f, 0f));
                }
                BasePlayer killer = info != null ? info.InitiatorPlayer : null;
                if (killer != null)
                {
                    Bm.Tell(killer, "BossDrop", DisplayOf(r.Info.Kind).ToLowerInvariant(), scrap);
                }
            }

            private int ScrapOf(string kind)
            {
                switch (kind)
                {
                    case "horse": return Bm.Cfg.Boss.ScrapHorse;
                    case "stag": return Bm.Cfg.Boss.ScrapStag;
                    case "ram": return Bm.Cfg.Boss.ScrapRam;
                    default: return Bm.Cfg.Boss.ScrapBull;
                }
            }

            private static void DropScrap(int amount, Vector3 at)
            {
                ItemDefinition def = ItemManager.FindItemDefinition("scrap");
                if (def == null)
                {
                    return;
                }
                int stack = Mathf.Max(1, def.stackable);
                while (amount > 0)
                {
                    int n = Mathf.Min(stack, amount);
                    Item item = ItemManager.Create(def, n, 0UL);
                    if (item != null)
                    {
                        item.Drop(at + new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)), new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 2.5f, UnityEngine.Random.Range(-1.5f, 1.5f)));
                    }
                    amount -= n;
                }
            }

            // ---------------------------------------------------------------- the boss saddle and the offspring

            public Item FindSaddle(BasePlayer p, string kind)
            {
                ulong skin = BossSaddleSkin + (ulong)Array.IndexOf(BossKinds, kind);
                foreach (Item it in PlayerItems(p))
                {
                    if (it.skin == skin)
                    {
                        return it;
                    }
                }
                return null;
            }

            // E on a boss while carrying its saddle tames it on the spot (R20.7).
            public bool UseSaddle(BasePlayer player, BaseEntity entity)
            {
                BossInfo info = Bm.BossOf(entity);
                if (info == null || info.Tamed || !Bm.Cfg.Features.Taming)
                {
                    return false;
                }
                Item saddle = FindSaddle(player, info.Kind);
                if (saddle == null)
                {
                    return false;
                }
                TamedRecord rec = Bm.TameAnimal(entity, player);
                if (rec == null)
                {
                    return true;
                }
                saddle.UseItem(1);
                info.Tamed = true;
                rec.Boss = true;
                rec.BossKind = info.Kind;
                rec.MiniBoss = info.Mini;
                RidableHorse horse = entity as RidableHorse;
                if (horse != null && Bm.Stable != null)
                {
                    Bm.Stable.GiveSaddle(horse, 1);
                }
                else if (rec.Gear.Saddle <= 0)
                {
                    rec.Gear.Saddle = 1;                  // a saddle it already carried (its wild inventory) stays
                }
                Run r;
                if (runs.TryGetValue(entity.skinID, out r))
                {
                    Calm(r);
                    r.Target = null;
                }
                Bm.Tell(player, "BossTamed", DisplayOf(info.Kind).ToLowerInvariant());
                return true;
            }

            // A calf or lamb born to a tamed boss is a mini boss with a 10 % chance (R20.8).
            public override void OnEntitySpawned(BaseNetworkable entity)
            {
                LivestockAnimal baby = entity as LivestockAnimal;
                if (baby == null || baby.skinID != 0UL || !Bm.Cfg.Features.BossAnimals || Bm.Data.Tamed.Count == 0)
                {
                    return;
                }
                Vector3 pos = baby.transform.position;
                foreach (TamedRecord rec in Bm.Data.Tamed.Values)
                {
                    if (!rec.Boss || rec.MiniBoss)
                    {
                        continue;
                    }
                    BaseEntity parent = Bm.EntityOfRecord(rec);
                    if (parent == null || Vector3.Distance(parent.transform.position, pos) > 14f)
                    {
                        continue;
                    }
                    if (UnityEngine.Random.Range(0f, 100f) < Bm.Cfg.Boss.MiniChance)
                    {
                        ulong tag = Bm.NewTag();
                        baby.skinID = tag;
                        var info = new BossInfo { Kind = rec.BossKind, Mini = true };
                        Bm.Data.Bosses[tag] = info;
                        Setup(baby, info, true);
                    }
                    return;
                }
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Bosses: " + runs.Count + " alive, " + (Bm.Cfg.Features.BossAnimals ? "spawning on" : "spawning off") + ", it is " + (IsNight() ? "night" : "day"));
            }
        }

        // Headless test: a boss bull and a boss ram are made, resist fire, die together and drop scrap (with the group bonus) and boss saddles.
        internal void TestBoss(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            BaseCombatEntity bull = Bosses.SpawnBoss("bull", pos);
            BaseCombatEntity ram = Bosses.SpawnBoss("ram", pos + new Vector3(8f, 0f, 0f));
            BaseCombatEntity horse = Bosses.SpawnBoss("horse", pos + new Vector3(-8f, 0f, 0f));
            if (bull == null || ram == null || horse == null)
            {
                foreach (BaseCombatEntity e in new[] { bull, ram, horse }) { if (e != null) e.Kill(); }
                reply("A boss kind is not available in this build.");
                return;
            }
            timer.Once(2.0f, () =>
            {
                check("the boss has " + Cfg.Boss.Health + " health (" + Mathf.RoundToInt(bull.health) + ")", Mathf.Approximately(bull.health, Cfg.Boss.Health) && Mathf.Approximately(bull.MaxHealth(), Cfg.Boss.Health));
                check("bosses are registered (" + Bosses.Alive + ")", Bosses.Alive >= 3);
                float before = bull.health;
                bull.Hurt(500f, Rust.DamageType.Heat);
                check("a boss is immune to fire (" + Mathf.RoundToInt(before) + " -> " + Mathf.RoundToInt(bull.health) + ")", Mathf.Approximately(before, bull.health));
                RidableHorse h = horse as RidableHorse;
                check("the boss horse has no saddle until tamed", h != null && SeatsOf(h) == 0);
                float want = Cfg.Boss.Scale;
                check("a boss is bigger (bull x" + bull.transform.localScale.x.ToString("0.00") + ", horse x" + horse.transform.localScale.x.ToString("0.00") + ", ram x" + ram.transform.localScale.x.ToString("0.00") + ")",
                    bull.networkEntityScale && Mathf.Approximately(bull.transform.localScale.x, want) && horse.networkEntityScale && Mathf.Approximately(horse.transform.localScale.x, want) && ram.networkEntityScale);
                bool seatsKeepSize = h != null && h.mountPoints.Count > 0 && h.mountPoints.All(mp => mp.mountable == null || Mathf.Abs(mp.mountable.transform.lossyScale.x - 1f) < 0.02f);
                check("the horse's seats keep normal size, so a rider on the bigger horse is not enlarged", seatsKeepSize);
                Vector3 horseStart = horse.transform.position;
                HorseState horseState = h != null && Stable != null ? Stable.StateOf(h) : null;
                if (horseState != null)
                {
                    horseState.Drive.StopDistance = 1.5f;
                    horseState.Drive.MoveTo(horseStart + horse.transform.forward * 14f, 5f);
                }
                float hb = ram.health;
                ram.Hurt(100f, Rust.DamageType.Bullet);
                check("other damage still hurts (" + Mathf.RoundToInt(hb) + " -> " + Mathf.RoundToInt(ram.health) + ")", ram.health < hb);
                Vector3 at = bull.transform.position;
                bull.Hurt(100000f, Rust.DamageType.Bullet);
                ram.Hurt(100000f, Rust.DamageType.Bullet);
                timer.Once(3.0f, () =>
                {
                    int scrap = 0, saddles = 0;
                    foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                    {
                        WorldItem wi = net as WorldItem;
                        if (wi == null || wi.item == null || Vector3.Distance(wi.transform.position, at) > 20f)
                        {
                            continue;
                        }
                        if (wi.item.info.shortname == "scrap") scrap += wi.item.amount;
                        if (wi.item.skin >= BossSaddleSkin && wi.item.skin < BossSaddleSkin + 4UL) saddles++;
                    }
                    int expected = Mathf.RoundToInt(Cfg.Boss.ScrapBull * Cfg.Boss.GroupBonus) + Mathf.RoundToInt(Cfg.Boss.ScrapRam * Cfg.Boss.GroupBonus);
                    check("the two bosses dropped " + scrap + " scrap (expected " + expected + ": each gets the group bonus)", scrap == expected);
                    check("and two boss saddles (" + saddles + ")", saddles == 2);
                    check("their records are gone", Bosses.Alive <= 1);
                    if (Valid(horse))
                    {
                        float moved = Vector3.Distance(horse.transform.position, horseStart);
                        float clearance = horse.transform.position.y - TerrainMeta.HeightMap.GetHeight(horse.transform.position);
                        check("the bigger horse still walks (" + moved.ToString("0.0") + " m) and stands on the ground (" + clearance.ToString("0.00") + " m above it)", moved > 5f && clearance > -0.4f && clearance < 2.2f);
                    }
                    foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                    {
                        WorldItem wi = net as WorldItem;
                        if (wi != null && wi.item != null && Vector3.Distance(wi.transform.position, at) < 20f && (wi.item.info.shortname == "scrap" || wi.item.skin >= BossSaddleSkin))
                        {
                            wi.Kill();
                        }
                    }
                    if (Valid(horse))
                    {
                        Data.Bosses.Remove(horse.skinID);
                        if (Stable != null && horse is RidableHorse) Stable.Forget((RidableHorse)horse);
                        horse.Kill();
                    }
                    Puts("[test] boss:\n" + string.Join("\n", lines.ToArray()));
                });
            });
            reply("Boss test started: the result comes to the log in ~8 s.");
        }

        // bm.boss spawn <horse|bull|ram|stag> [mini] | list | clear
        private void BossCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            switch (sub)
            {
                case "spawn":
                    {
                        string kind = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
                        if (!BossKinds.Contains(kind)) { reply("Usage: bm.boss spawn horse|bull|ram|stag [mini]"); return; }
                        Vector3 spot = player != null ? SpawnSpot(player) : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(15f, 0f, 15f);
                        BaseCombatEntity e = Bosses.SpawnBoss(kind, spot, args.Length > 2 && args[2] == "mini");
                        reply(e != null ? "A boss " + kind + " stands at " + spot + "." : "It could not be created.");
                        return;
                    }
                case "clear":
                    {
                        int n = 0;
                        foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                        {
                            BaseEntity be = net as BaseEntity;
                            if (be != null && be.skinID >= TagBase && Data.Bosses.ContainsKey(be.skinID) && RecordOf(be) == null)
                            {
                                Data.Bosses.Remove(be.skinID);
                                be.Kill();
                                n++;
                            }
                        }
                        reply(n + " untamed bosses removed.");
                        return;
                    }
                default:
                    reply("Bosses alive: " + Bosses.Alive + ". " + (IsNight() ? "Night." : "Day.") + " Usage: bm.boss spawn horse|bull|ram|stag [mini] | clear");
                    return;
            }
        }
    }
}
