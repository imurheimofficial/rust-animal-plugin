using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- the transformation ritual (R9.1 - R9.12)
        // 1. kill a wild animal, the body stays   2. circle it with a lit torch (10 times, 50 for a boss)   3. a low ring of flames burns round the body
        // for 10 seconds - press E at it: half of the current health and bleeding are the price   4. you become that animal, with the same look, at the body.
        // If nobody presses E in time, the fire and the body vanish. The ritual is silent: nothing about it is announced in the chat.

        internal sealed class RitualCorpse
        {
            public SpeciesDef Species;
            public ulong Killer;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Died;
            public BaseEntity Animal;           // the dying animal: its death animation runs before the game drops the body
            public BaseCorpse Corpse;
            public bool Linked;                 // a body belongs to the record (it is never "unlinked" again: a vanished body ends the ritual)
            public bool StandIn;
            public string Name = string.Empty;
            public int Genes;
            public bool Male;
            public bool Burning;
            public float BurnUntil;
            public ulong Performer;
            public BossInfo Boss;               // the body of a boss: 50 circles instead of 10, and the player is reborn as a boss
            public readonly List<BaseEntity> Flames = new List<BaseEntity>();
            public readonly Dictionary<ulong, Circle> Circles = new Dictionary<ulong, Circle>();
        }

        internal sealed class Circle
        {
            public float Last;          // last angle around the body (rad)
            public float Total;         // swept angle (rad), the sign is the direction
            public float Seen;          // time of the last counted step
            public bool Started;
            public int Announced;
        }

        internal readonly List<RitualCorpse> RitualCorpses = new List<RitualCorpse>();

        internal bool IsRitualCorpse(BaseEntity entity)
        {
            return entity != null && RitualCorpses.Any(r => r.Corpse == entity && (r.Burning || r.StandIn));
        }

        internal sealed class RitualModule : Module
        {
            public override int Order => 45;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Subscribe("OnEntitySpawned");
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["RitualTooWeak"] = "You are too weak for the blood sacrifice.",
                    ["RitualCooldown"] = "You can transform again in {0} s.",
                    ["RitualSafeZone"] = "No rituals in a safe zone.",
                    ["RitualNotYours"] = "This body is not yours: it has to be an animal you (or your team) killed.",
                    ["RitualOff"] = "The transformation ritual is switched off on this server.",
                };
            }

            public override void OnDisable()
            {
                Cleanup(true);
            }

            public override void Unload()
            {
                Cleanup(true);
            }

            private void Cleanup(bool all)
            {
                foreach (RitualCorpse rc in Bm.RitualCorpses.ToList())
                {
                    if (all || Time.time - rc.Died > Bm.Cfg.Ritual.CorpseKeepSeconds)
                    {
                        Remove(rc);
                    }
                }
            }

            internal void KillFlames(RitualCorpse rc)
            {
                foreach (BaseEntity flame in rc.Flames)
                {
                    if (flame != null && !flame.IsDestroyed)
                    {
                        flame.Kill();
                    }
                }
                rc.Flames.Clear();
            }

            // vanish = the fire went out without a transformation: the body is gone with it.
            private void Remove(RitualCorpse rc, bool vanish = false)
            {
                Bm.RitualCorpses.Remove(rc);
                KillFlames(rc);
                if (!Valid(rc.Corpse))
                {
                    return;
                }
                if (rc.StandIn || vanish)
                {
                    Bm.FormSmoke(rc.Corpse.transform.position);
                    rc.Corpse.Kill();
                    return;
                }
                // an ordinary body again: it can be harvested as usual
                rc.Corpse.resourceDispenser = rc.Corpse.GetComponent<ResourceDispenser>();
            }

            // Harvesting the body would cancel the ritual (R9.35): with the dispenser switched off a tool hit lands as plain damage,
            // and that damage is refused below.
            private static void Protect(BaseCorpse corpse)
            {
                if (corpse != null && corpse.resourceDispenser != null)
                {
                    corpse.resourceDispenser = null;
                }
            }

            // Only the burning body (and a stand-in, which is no real body) is protected: before the fire every body is an ordinary body that
            // gives its loot and its meat - harvesting it simply ends the ritual on it.
            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                if (Bm.RitualCorpses.Count == 0 || !(entity is BaseCorpse))
                {
                    return null;
                }
                foreach (RitualCorpse rc in Bm.RitualCorpses)
                {
                    if (rc.Corpse == entity && (rc.Burning || rc.StandIn))
                    {
                        return true;
                    }
                }
                return null;
            }

            // The killer's animal leaves a body that stays; the look of the animal is remembered for the transformation.
            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                if (!Bm.Cfg.Features.Transformation)
                {
                    return;
                }
                BaseNPC2 animal = entity as BaseNPC2;
                RidableHorse horse = entity as RidableHorse;
                if (animal == null && horse == null)
                {
                    return;
                }
                SpeciesDef species = SpeciesOf(entity);
                if (species == null || !species.Transform || !Bm.SpeciesEnabled(species) || Bm.IsTamed(entity) || Bm.DriverOfAnimal(entity) != null)
                {
                    return;
                }
                BossInfo boss = Bm.BossOf(entity);
                BasePlayer killer = info != null ? (info.InitiatorPlayer ?? info.Initiator as BasePlayer) : null;
                if (killer == null && info != null && info.Initiator != null)
                {
                    Driver byForm = Bm.DriverOfAnimal(info.Initiator);
                    killer = byForm != null ? byForm.Player : null;          // killed by the animal body of a player
                }
                if (killer == null || killer.IsNpc)
                {
                    return;
                }
                bool allowed = species.TransformRestricted ? Bm.Has(killer, PermTransformRestricted) : Bm.Has(killer, PermTransform);
                if (!allowed)
                {
                    Bm.Log(2, "Ritual: " + killer.displayName + " killed a " + species.Id + " but has no permission to transform (beastmaster.transform).");
                    return;
                }
                Bm.Log(2, "Ritual: " + killer.displayName + " killed a " + species.Id + ": its body is tracked.");
                var rc = new RitualCorpse { Species = species, Killer = killer.userID, Animal = entity, Position = entity.transform.position, Rotation = entity.transform.rotation, Died = Time.time };
                if (boss != null)
                {
                    rc.Boss = new BossInfo { Kind = boss.Kind, Mini = boss.Mini };
                }
                LivestockAnimal livestock = animal as LivestockAnimal;
                if (livestock != null)
                {
                    rc.Name = livestock.AnimalName;
                    rc.Genes = livestock.Genes;
                    rc.Male = livestock.IsMale;
                }
                else if (horse != null)
                {
                    rc.Genes = Mathf.Max(0, Array.IndexOf(horse.breeds, horse.GetBreed()));
                }
                Bm.RitualCorpses.Add(rc);
            }

            // The game drops the body after the death animation, with the dead animal as its parent: that body belongs to the record
            // and is kept for the ritual.
            public override void OnEntitySpawned(BaseNetworkable entity)
            {
                BaseCorpse corpse = entity as BaseCorpse;
                if (corpse == null || Bm.RitualCorpses.Count == 0)
                {
                    return;
                }
                foreach (RitualCorpse rc in Bm.RitualCorpses)
                {
                    if (rc.Linked)
                    {
                        continue;
                    }
                    bool mine = rc.Animal != null && corpse.parentEnt == rc.Animal;
                    if (!mine && Time.time - rc.Died < 20f && Vector3.Distance(rc.Position, corpse.transform.position) < 8f
                        && (!Valid(rc.Animal) || CorpseOfKind(corpse, rc.Species)))
                    {
                        mine = true;      // the body of that kind that appeared there right after the death (the horse's body has no parent)
                    }
                    if (mine)
                    {
                        HorseCorpse horseBody = corpse as HorseCorpse;
                        if (horseBody != null)
                        {
                            rc.Genes = horseBody.breedIndex;
                        }
                        rc.Corpse = corpse;
                        rc.Linked = true;
                        rc.Position = corpse.transform.position;
                        corpse.ResetRemovalTime(Bm.Cfg.Ritual.CorpseKeepSeconds);
                        Bm.Log(2, "Ritual: the body of the " + rc.Species.Id + " is ready for the ritual (" + Bm.Cfg.Ritual.Circles + " circles with a lit torch).");
                        return;
                    }
                }
            }

            // A creature that leaves no body in the game (the small ones) gets a stand-in where it lay down.
            public override void OnEntityKill(BaseNetworkable entity)
            {
                if (Bm.RitualCorpses.Count == 0 || !Bm.Cfg.Features.Transformation)
                {
                    return;
                }
                foreach (RitualCorpse rc in Bm.RitualCorpses)
                {
                    if (rc.Animal == entity && !rc.Linked)
                    {
                        rc.Position = entity.transform.position;
                        rc.Rotation = entity.transform.rotation;
                        rc.Animal = null;
                        MakeStandIn(rc);
                        return;
                    }
                }
            }

            public override void Tick(float now)
            {
                if (!Bm.Cfg.Features.Transformation || Bm.RitualCorpses.Count == 0)
                {
                    return;
                }
                foreach (RitualCorpse rc in Bm.RitualCorpses.ToList())
                {
                    if (now - rc.Died > Bm.Cfg.Ritual.CorpseKeepSeconds)
                    {
                        Remove(rc);
                        continue;
                    }
                    if (!rc.Linked)
                    {
                        if (Valid(rc.Animal))
                        {
                            rc.Position = rc.Animal.transform.position;     // the death animation may carry it a few steps
                            rc.Rotation = rc.Animal.transform.rotation;
                            if (now - rc.Died > 30f)
                            {
                                Remove(rc);
                            }
                        }
                        else if (now - rc.Died > 4f)
                        {
                            MakeStandIn(rc);      // the animal vanished without leaving a body
                            if (!rc.Linked)
                            {
                                Remove(rc);
                            }
                        }
                        continue;
                    }
                    if (!Valid(rc.Corpse))
                    {
                        Remove(rc);               // harvested some other way, despawned or destroyed: the ritual is off (R9.10)
                        continue;
                    }
                    if (rc.Burning || rc.StandIn)
                    {
                        Protect(rc.Corpse);       // the game may fill the dispenser in after the spawn hook
                    }
                    if (rc.Burning)
                    {
                        if (now > rc.BurnUntil)
                        {
                            Remove(rc, true);              // nobody pressed E: the fire and the body vanish
                        }
                        continue;
                    }
                    Circling(rc, now);
                }
            }

            // The species' own body prefab if the game has one, else the chicken's: it is only the marker of the ritual spot.
            private static bool CorpseOfKind(BaseCorpse corpse, SpeciesDef species)
            {
                if (corpse is HorseCorpse)
                {
                    return species.Id == "horse";
                }
                string name = corpse.ShortPrefabName.ToLowerInvariant();
                string kind = species.Id == "ram" ? "sheep" : species.Id == "doe" ? "stag" : species.Id;
                return name.StartsWith(kind) || (species.Short != null && name.StartsWith(species.Short));
            }

            private void MakeStandIn(RitualCorpse rc)
            {
                string path = rc.Species.Id == "horse"
                    ? Bm.FindPrefabPath("vehicles/horse/", ".corpse.prefab")
                    : Bm.FindPrefabPath("rust.ai/agents/" + rc.Species.Id + "/", ".corpse.prefab");
                path = path ?? Bm.FindPrefabPath("rust.ai/agents/chicken/", ".corpse.prefab");
                if (path == null)
                {
                    Bm.Log(1, "Ritual: no body prefab for the stand-in of the " + rc.Species.Id);
                    return;
                }
                BaseCorpse corpse = GameManager.server.CreateEntity(path, rc.Position, rc.Rotation) as BaseCorpse;
                if (corpse == null)
                {
                    Bm.Log(1, "Ritual: the stand-in body " + path + " could not be created");
                    return;
                }
                Bm.Log(3, "Ritual: stand-in body " + path + " for the " + rc.Species.Id);
                corpse.Spawn();
                corpse.ResetRemovalTime(Bm.Cfg.Ritual.CorpseKeepSeconds);
                Protect(corpse);
                rc.Corpse = corpse;
                rc.Linked = true;
                rc.StandIn = true;
            }

            // Walking around the body with a lit torch is counted by the angle the player sweeps around it. Returns the full circles so far.
            // The walk has to be done in one go (30 s without progress start it over); a jump in the angle (lag, teleport) is not walking.
            internal static int AdvanceCircle(Circle c, Vector3 center, Vector3 at, float radius, bool lit, float now)
            {
                Vector3 delta = at - center;
                delta.y = 0f;
                float dist = delta.magnitude;
                if (c.Total != 0f && now - c.Seen > 30f)
                {
                    c.Total = 0f;
                    c.Announced = 0;
                    c.Started = false;
                }
                if (!lit || dist < 1.2f || dist > radius + 2.5f)
                {
                    c.Started = false;
                    if (!lit || dist > radius + 5f)
                    {
                        c.Total = 0f;
                        c.Announced = 0;
                    }
                }
                else
                {
                    float angle = Mathf.Atan2(delta.z, delta.x);
                    if (!c.Started)
                    {
                        c.Started = true;
                        c.Last = angle;
                    }
                    else
                    {
                        float step = Mathf.DeltaAngle(c.Last * Mathf.Rad2Deg, angle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                        c.Last = angle;
                        if (Mathf.Abs(step) <= 1.2f)
                        {
                            c.Total += step;
                            c.Seen = now;
                        }
                    }
                }
                return Mathf.FloorToInt(Mathf.Abs(c.Total) / (2f * Mathf.PI));
            }

            private void Circling(RitualCorpse rc, float now)
            {
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || Bm.DriverOf(p) != null || !Eligible(rc, p))
                    {
                        continue;
                    }
                    TorchWeapon torch = p.GetHeldEntity() as TorchWeapon;
                    bool lit = torch != null && torch.HasFlag(BaseEntity.Flags.On);
                    Circle c;
                    if (!rc.Circles.TryGetValue(p.userID, out c))
                    {
                        c = new Circle();
                        rc.Circles[p.userID] = c;
                    }
                    int done = AdvanceCircle(c, rc.Position, p.transform.position, Bm.Cfg.Ritual.Radius, lit, now);
                    if (done >= (rc.Boss != null ? Bm.Cfg.Ritual.BossCircles : Bm.Cfg.Ritual.Circles))
                    {
                        Ignite(rc, p, now);
                        return;
                    }
                }
            }

            private bool Eligible(RitualCorpse rc, BasePlayer p)
            {
                if (!Bm.Cfg.Ritual.RequireOwnKill || rc.Killer == p.userID)
                {
                    return true;
                }
                BasePlayer killer = BasePlayer.FindByID(rc.Killer);
                return killer != null && Bm.SameParty(killer, p);
            }

            private void Ignite(RitualCorpse rc, BasePlayer p, float now)
            {
                Protect(rc.Corpse);               // the burning body cannot be harvested any more
                rc.Burning = true;
                rc.BurnUntil = now + Mathf.Max(3f, Bm.Cfg.Ritual.BurnSeconds);
                rc.Performer = p.userID;
                rc.Circles.Clear();
                SpawnFlames(rc);
                Bm.RunEffect("assets/bundled/prefabs/fx/fire/fire_v3.prefab", rc.Position + Vector3.up * 0.3f);      // the body itself burns
                Bm.TellRaw(p, "The body catches fire. Press E next to it before the fire dies.");
                Bm.Log(2, "Ritual: " + p.displayName + " lit the body of the " + rc.Species.Id);
            }

            // A low, thin ring of small flames round the body (real fire: it burns whoever stands in it).
            internal void SpawnFlames(RitualCorpse rc)
            {
                RitualCfg cfg = Bm.Cfg.Ritual;
                string path = cfg.FirePrefab;
                if (string.IsNullOrEmpty(path) || GameManager.server.FindPrefab(path) == null)
                {
                    path = Bm.FindPrefabPath("oilfireballsmall") ?? Bm.FindPrefabPath("fireball_small");
                }
                if (path == null)
                {
                    Bm.RunEffect("assets/bundled/prefabs/fx/fire/fire_v3.prefab", rc.Position + Vector3.up * 0.2f);
                    return;
                }
                int count = Mathf.Clamp(cfg.FireCount, 3, 24);
                for (int i = 0; i < count; i++)
                {
                    float a = i * Mathf.PI * 2f / count;
                    Vector3 pos = rc.Position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * cfg.FireRadius;
                    pos.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(pos), rc.Position.y - 0.4f) + 0.04f;
                    if (i == 0)
                    {
                        SpawnBodyFlame(rc, path);          // one flame on the body itself
                    }
                    FireBall flame = GameManager.server.CreateEntity(path, pos, Quaternion.identity) as FireBall;
                    if (flame == null)
                    {
                        continue;
                    }
                    flame.enableSaving = false;
                    flame.lifeTimeMin = flame.lifeTimeMax = cfg.BurnSeconds + 0.5f;
                    flame.tickRate = 0.5f;
                    flame.damagePerSecond = cfg.FireDamage;
                    flame.radius = 0.55f;
                    flame.Spawn();
                    flame.CancelInvoke(flame.TryToSpread);            // the flames stay where they are: no sparks fly off
                    Rigidbody body = flame.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        body.isKinematic = true;
                        body.useGravity = false;
                    }
                    Bm.ScaleModel(flame, cfg.FireScale);
                    rc.Flames.Add(flame);
                }
            }

            // A bigger flame right on the body, so that the body itself is seen burning (it does no harm: nobody stands in it).
            private void SpawnBodyFlame(RitualCorpse rc, string path)
            {
                RitualCfg cfg = Bm.Cfg.Ritual;
                FireBall flame = GameManager.server.CreateEntity(path, rc.Position + Vector3.up * 0.25f, Quaternion.identity) as FireBall;
                if (flame == null)
                {
                    return;
                }
                flame.enableSaving = false;
                flame.lifeTimeMin = flame.lifeTimeMax = cfg.BurnSeconds + 0.5f;
                flame.tickRate = 0.5f;
                flame.damagePerSecond = 0f;
                flame.radius = 0.3f;
                flame.Spawn();
                flame.CancelInvoke(flame.TryToSpread);
                Rigidbody body = flame.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.isKinematic = true;
                    body.useGravity = false;
                }
                Bm.ScaleModel(flame, cfg.FireScale * 2.4f);
                rc.Flames.Add(flame);
            }

            // E next to the burning body: the blood sacrifice and the transformation.
            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (!input.WasJustPressed(BUTTON.USE) || Bm.RitualCorpses.Count == 0 || Bm.DriverOf(player) != null)
                {
                    return null;
                }
                foreach (RitualCorpse rc in Bm.RitualCorpses)
                {
                    if (rc.Burning && Vector3.Distance(player.transform.position, rc.Position) < 3.8f && (rc.Performer == player.userID || Eligible(rc, player)))
                    {
                        Bm.PerformTransformation(player, rc);
                        break;
                    }
                }
                return null;
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Ritual: " + Bm.RitualCorpses.Count + " eligible bodies");
            }
        }

        // The blood sacrifice and the rebirth as the animal that was killed.
        internal void PerformTransformation(BasePlayer player, RitualCorpse rc)
        {
            if (!Cfg.Features.Transformation || !Cfg.Features.AnimalForm)
            {
                Tell(player, "RitualOff");
                return;
            }
            bool allowed = rc.Species.TransformRestricted ? Has(player, PermTransformRestricted) : Has(player, PermTransform);
            if (!allowed)
            {
                Tell(player, "NoPermission");
                return;
            }
            long until;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Data.RitualCooldown.TryGetValue(player.userID, out until) && until > now && !IsAdmin(player))
            {
                Tell(player, "RitualCooldown", until - now);
                return;
            }
            if (Cfg.Ritual.BlockInSafeZone && player.InSafeZone())
            {
                Tell(player, "RitualSafeZone");
                return;
            }
            if (player.health * (1f - Cfg.Ritual.HealthCost) < 4f)
            {
                Tell(player, "RitualTooWeak");
                return;
            }
            // the price
            player.health = Mathf.Max(1f, player.health * (1f - Cfg.Ritual.HealthCost));
            player.metabolism.bleeding.value = Mathf.Max(player.metabolism.bleeding.value, Cfg.Ritual.Bleeding);
            player.metabolism.isDirty = true;
            // the animal of the same kind and look, at the body (the human vanishes and the animal appears with the effects of the form itself)
            Vector3 pos = rc.Position;
            pos.y = Mathf.Max(pos.y, TerrainMeta.HeightMap.GetHeight(pos)) + 0.2f;
            BaseCombatEntity animal = GameManager.server.CreateEntity(rc.Species.Prefab, pos, rc.Rotation) as BaseCombatEntity;
            if (animal == null)
            {
                Tell(player, "RitualOff");
                return;
            }
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock != null)
            {
                if (!string.IsNullOrEmpty(rc.Name))
                {
                    livestock.AnimalName = rc.Name;
                }
                livestock.Genes = rc.Genes;
                livestock.IsMale = rc.Male;
            }
            RidableHorse reborn = animal as RidableHorse;
            if (rc.Boss != null)
            {
                ulong tag = NewTag();
                animal.skinID = tag;
                Data.Bosses[tag] = new BossInfo { Kind = rc.Boss.Kind, Mini = rc.Boss.Mini, Tamed = true };
            }
            animal.Spawn();
            if (livestock != null)
            {
                EnforceLook(animal, rc.Species, rc.Genes, rc.Name, rc.Male);      // the game rolls a new look at the spawn: the killed one's is put back
            }
            if (reborn != null)
            {
                reborn.SetBreed(rc.Genes);
            }
            if (rc.Boss != null && Bosses != null)
            {
                Bosses.Setup(animal, Data.Bosses[animal.skinID], true);     // the size, the strength, the glow of a boss
            }
            RitualCorpses.Remove(rc);
            foreach (BaseEntity flame in rc.Flames)
            {
                if (flame != null && !flame.IsDestroyed)
                {
                    flame.Kill();
                }
            }
            rc.Flames.Clear();
            if (Valid(rc.Corpse))
            {
                rc.Corpse.Kill();
            }
            SpeciesDef species = rc.Species;
            // the agent needs a moment to settle on the navigation mesh (a few physics frames - not felt as a delay)
            timer.Once(0.25f, () =>
            {
                if (!Valid(animal) || player == null || !player.IsConnected || player.IsDead())
                {
                    if (Valid(animal)) animal.Kill();
                    return;
                }
                if (StartControl(player, animal, ControlKind.Form, true, true))
                {
                    Driver d = DriverOf(player);
                    if (d != null)
                    {
                        d.OwnsAnimal = true;
                    }
                }
                else
                {
                    animal.Kill();
                }
            });
        }

        // Headless test: the circle counting on synthetic walks, then a real animal body is linked, protected and released again.
        internal void TestRitual(Action<string> reply, Vector3 pos, string only = null)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            Vector3 center = Vector3.zero;
            float r = Cfg.Ritual.Radius;
            int circles = Cfg.Ritual.Circles;
            Func<float, Vector3> onRing = a => center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            // a lit walk of three full circles counts, and not before
            var c = new Circle();
            int done = 0, at = -1, tick = 0;
            for (float a = 0f; a < (circles + 0.3f) * 2f * Mathf.PI; a += 0.3f)
            {
                done = RitualModule.AdvanceCircle(c, center, onRing(a), r, true, ++tick * 0.25f);
                if (done >= circles && at < 0) at = tick;
            }
            check("lit walk counts " + circles + " circles (reached at tick " + at + ")", at > 0 && at > (int)(circles * 2f * Mathf.PI / 0.3f) - 2);
            // opposite direction counts too
            c = new Circle(); done = 0; tick = 0;
            for (float a = 0f; a > -(circles + 0.2f) * 2f * Mathf.PI; a -= 0.3f) done = RitualModule.AdvanceCircle(c, center, onRing(a), r, true, ++tick * 0.25f);
            check("the other direction counts too", done >= circles);
            // an unlit torch, a far ring, standing still and teleport jumps never count
            c = new Circle(); done = 0; tick = 0;
            for (float a = 0f; a < 4f * 2f * Mathf.PI; a += 0.3f) done = RitualModule.AdvanceCircle(c, center, onRing(a), r, false, ++tick * 0.25f);
            check("unlit torch counts nothing", done == 0);
            c = new Circle(); done = 0; tick = 0;
            for (float a = 0f; a < 4f * 2f * Mathf.PI; a += 0.3f) done = RitualModule.AdvanceCircle(c, center, center + new Vector3(Mathf.Cos(a) * (r + 6f), 0f, Mathf.Sin(a) * (r + 6f)), r, true, ++tick * 0.25f);
            check("a far ring counts nothing", done == 0);
            c = new Circle(); done = 0; tick = 0;
            for (int i = 0; i < 200; i++) done = RitualModule.AdvanceCircle(c, center, onRing(0.7f), r, true, ++tick * 0.25f);
            check("standing still counts nothing", done == 0);
            c = new Circle(); done = 0; tick = 0;
            for (int i = 0; i < 200; i++) done = RitualModule.AdvanceCircle(c, center, onRing(i % 2 == 0 ? 0f : 2.2f), r, true, ++tick * 0.25f);
            check("jumping between spots counts nothing", done == 0);
            // half a walk, a long pause, another half: starts over
            c = new Circle(); done = 0; tick = 0;
            float now = 0f;
            for (float a = 0f; a < 1.6f * 2f * Mathf.PI; a += 0.3f) done = RitualModule.AdvanceCircle(c, center, onRing(a), r, true, now += 0.25f);
            now += 40f;
            for (float a = 0f; a < 1.6f * 2f * Mathf.PI; a += 0.3f) done = RitualModule.AdvanceCircle(c, center, onRing(a), r, true, now += 0.25f);
            check("a long pause starts the walk over", done < circles);
            // real bodies of real animals, one after the other: a cow leaves a body, a rabbit leaves none (a stand-in takes its place)
            Action<string, Vector3, bool, Action> bodyTest = null;
            bodyTest = (speciesId, at2, expectStandIn, next) =>
            {
                SpeciesDef sp = SpeciesById(speciesId);
                if (sp == null || !SpeciesEnabled(sp))
                {
                    lines.Add("SKIP " + speciesId + " (not available in this build)");
                    next();
                    return;
                }
                at2.y = TerrainMeta.HeightMap.GetHeight(at2) + 0.2f;
                BaseNPC2 animal = GameManager.server.CreateEntity(sp.Prefab, at2, Quaternion.identity) as BaseNPC2;
                if (animal == null)
                {
                    check(speciesId + ": the test animal spawns", false);
                    next();
                    return;
                }
                animal.Spawn();
                timer.Once(1.0f, () =>
                {
                    var rc = new RitualCorpse { Species = sp, Killer = 0UL, Animal = animal, Position = animal.transform.position, Rotation = animal.transform.rotation, Died = Time.time };
                    RitualCorpses.Add(rc);
                    animal.Hurt(animal.health + 50f, Rust.DamageType.Bullet);
                    timer.Once(9f, () =>
                    {
                        if (expectStandIn && Valid(animal))
                        {
                            animal.Kill();      // no player is near, so the critter's FSM sleeps: the end of its death animation is simulated
                        }
                        timer.Once(0.6f, () =>
                    {
                        bool linked = rc.Linked && rc.Corpse != null && Valid(rc.Corpse);
                        check(speciesId + ": the body is linked to the record" + (expectStandIn ? " (stand-in expected)" : "") + " [linked " + rc.Linked + ", stand-in " + rc.StandIn + ", animal valid " + Valid(rc.Animal) + ", animal dead " + (animal != null && !animal.IsDestroyed && animal.IsDead()) + "]", linked && rc.StandIn == expectStandIn);
                        if (linked)
                        {
                            check(speciesId + ": the body is protected from harvesting (dispenser off)", rc.Corpse.resourceDispenser == null);
                            float before = rc.Corpse.health;
                            rc.Corpse.Hurt(30f, Rust.DamageType.Slash);
                            check(speciesId + ": damage to the body is refused (" + before + " -> " + rc.Corpse.health + ")", Mathf.Approximately(before, rc.Corpse.health));
                        }
                        rc.Died = Time.time - Cfg.Ritual.CorpseKeepSeconds - 5f;       // the ritual window is over
                        timer.Once(1.0f, () =>
                        {
                            check(speciesId + ": the record is dropped when the window ends", !RitualCorpses.Contains(rc));
                            if (rc.Corpse != null && Valid(rc.Corpse))
                            {
                                if (rc.StandIn)
                                {
                                    check(speciesId + ": the stand-in is removed with the record", false);
                                    rc.Corpse.Kill();
                                }
                                else
                                {
                                    check(speciesId + ": the body is an ordinary body again (dispenser back)", rc.Corpse.resourceDispenser != null || rc.Corpse.GetComponent<ResourceDispenser>() == null);
                                    rc.Corpse.Kill();
                                }
                            }
                            else if (rc.StandIn)
                            {
                                check(speciesId + ": the stand-in is removed with the record", true);
                            }
                            next();
                        });
                    });
                    });
                });
            };
            Action report = () => Puts("[test] ritual:\n" + string.Join("\n", lines.ToArray()));
            Func<string, bool> hasBody = id => FindPrefabPath("rust.ai/agents/" + id + "/", ".corpse.prefab") != null;
            if (!string.IsNullOrEmpty(only))
            {
                bodyTest(only, pos, !hasBody(only), report);
            }
            else
            {
                bodyTest("cow", pos, false, () => bodyTest("rabbit", pos + new Vector3(5f, 0f, 0f), true, report));
            }
            reply("Ritual test started: the walk checks ran (" + lines.Count(l => l.StartsWith("PASS")) + " pass, " + lines.Count(l => l.StartsWith("FAIL")) + " fail); the body parts report to the log in ~25 s.");
        }
    }
}
