using System;
using System.Collections.Generic;
using System.Linq;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- animal form: start, end, logout sleep

        // Players who logged out in animal form: their animal sleeps where it stands until they return (R9.31-R9.34).
        internal readonly Dictionary<ulong, BaseCombatEntity> FormSleepers = new Dictionary<ulong, BaseCombatEntity>();

        internal void OnFormStarted(Driver d)
        {
            Vector3 at = d.Body.transform.position;
            BasePlayer hearer = d.Player;
            timer.Once(0.1f, () => FormAppear(at, hearer));            // and the animal appears with the same effect, heard by the player too
            Data.Forms.Remove(d.Player.userID);
            FormSleepers.Remove(d.Player.userID);
        }

        internal void OnFormEnded(Driver d, string reason)
        {
            BasePlayer player = d.Player;
            Vector3 spot = Valid(d.Body) ? d.Body.transform.position : player.transform.position;
            FormSmoke(spot, player);
            if (player != null && player.IsConnected && !player.IsDead() && reason != "animal died")
            {
                Vector3 back = GroundNear(spot, d.Body != null && !d.Body.IsDestroyed ? d.Body.bounds.extents.x * ModelScale(d.Body) + 1.2f : 1.5f);
                player.Teleport(back);
                FormAppear(back, player);
            }
            HudSwitch(player, true);
            Data.RitualCooldown[player.userID] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Cfg.Form.Cooldown;
            if ((d.OwnsAnimal || RecordOf(d.Body) == null) && Valid(d.Body) && !d.Body.IsDead())
            {
                bool shutdown = reason != null && (reason.StartsWith("plugin") || reason == "despawned");
                if (!shutdown)
                {
                    LeaveAnimalBehind(d);               // it stays where it is: X at it becomes it again, E rides it, 50 m away it vanishes
                }
                else
                {
                    if (d.Horse != null && Stable != null)
                    {
                        Stable.Forget(d.Horse);
                        Data.Horses.Remove(d.Horse.skinID);
                    }
                    Data.Bosses.Remove(d.Body.skinID);
                    d.Body.Kill();
                }
            }
        }

        // Returns true when the animal stays behind asleep and the player's form is kept.
        internal bool OnFormLogout(Driver d)
        {
            if (!Cfg.Form.SleepOnLogout || !Valid(d.Body) || d.Body.IsDead())
            {
                return false;
            }
            BasePlayer player = d.Player;
            BaseCombatEntity animal = d.Body;
            ulong tag = animal.skinID >= TagBase ? animal.skinID : NewTag();
            animal.skinID = tag;
            d.DetachForSleep();
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (animal is BaseNPC2)
            {
                Park((BaseNPC2)animal, "sleep");
                if (livestock != null)
                {
                    livestock.SetSleeping(true);
                }
                else
                {
                    Play((BaseNPC2)animal, "sleep", "lie", "rest");
                }
            }
            Data.Forms[player.userID] = new FormRecord
            {
                Player = player.userID,
                Species = d.Species.Id,
                Prefab = animal.PrefabName,
                Position = new V3(animal.transform.position),
                Health = animal.health,
                Genes = livestock != null ? unchecked((ulong)(uint)livestock.Genes) : d.Horse != null ? (ulong)Mathf.Max(0, Array.IndexOf(d.Horse.breeds, d.Horse.GetBreed())) : 0UL,
                LoggedOut = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Asleep = true,
                Tag = tag,
                OwnsAnimal = d.OwnsAnimal
            };
            FormSleepers[player.userID] = animal;
            Log(2, player.displayName + " left in " + d.Species.Display + " form - the animal sleeps.");
            return true;
        }

        // A player who left in animal form comes back as himself: the animal that slept for him goes away (a tamed one only wakes up).
        internal void EndFormOnLogin(BasePlayer player)
        {
            FormRecord rec;
            if (!Data.Forms.TryGetValue(player.userID, out rec))
            {
                return;
            }
            BaseCombatEntity animal;
            FormSleepers.TryGetValue(player.userID, out animal);
            Vector3 spot = Valid(animal) ? animal.transform.position : rec.Position.Value;
            Data.Forms.Remove(player.userID);
            FormSleepers.Remove(player.userID);
            if (Valid(animal) && !animal.IsDead())
            {
                BaseNPC2 npc = animal as BaseNPC2;
                if (npc != null)
                {
                    Unpark(npc, "sleep");
                }
                else if (animal is RidableHorse && Stable != null)
                {
                    Stable.WakeForm((RidableHorse)animal);
                }
                LivestockAnimal livestock = animal as LivestockAnimal;
                if (livestock != null)
                {
                    livestock.SetSleeping(false);
                }
                if (rec.OwnsAnimal || RecordOf(animal) == null)
                {
                    FormSmoke(spot);
                    Data.Bosses.Remove(animal.skinID);
                    animal.Kill();
                }
            }
            player.limitNetworking = false;
            BasePlayer p = player;
            timer.Once(1.5f, () =>
            {
                if (p != null && p.IsConnected && !p.IsDead())
                {
                    p.Teleport(GroundNear(spot, 1.5f));
                    FormAppear(p.transform.position, p);
                    HudSwitch(p, true);
                }
            });
        }

        // A returning player continues in animal form; an animal that died while the player was away has already killed them.
        internal void ResumeForm(BasePlayer player)
        {
            FormRecord rec;
            if (!Data.Forms.TryGetValue(player.userID, out rec))
            {
                return;
            }
            BaseCombatEntity animal;
            if (!FormSleepers.TryGetValue(player.userID, out animal) || !Valid(animal) || animal.IsDead())
            {
                Data.Forms.Remove(player.userID);
                FormSleepers.Remove(player.userID);
                return;
            }
            timer.Once(1.5f, () =>
            {
                if (player == null || !player.IsConnected || player.IsDead() || !Valid(animal))
                {
                    return;
                }
                player.limitNetworking = false;
                Driver d;
                if (StartControl(player, animal, ControlKind.Form, true))
                {
                    d = DriverOf(player);
                    if (d != null)
                    {
                        d.OwnsAnimal = rec.OwnsAnimal;
                    }
                    BaseNPC2 npc = animal as BaseNPC2;
                    if (npc != null)
                    {
                        Unpark(npc, "sleep");
                    }
                    else if (animal is RidableHorse && Stable != null)
                    {
                        Stable.WakeForm((RidableHorse)animal);
                    }
                    LivestockAnimal livestock = animal as LivestockAnimal;
                    if (livestock != null)
                    {
                        livestock.SetSleeping(false);
                    }
                    Data.Forms.Remove(player.userID);
                    FormSleepers.Remove(player.userID);
                }
            });
        }

        // After a reload or restart: find the sleeping animals of logged-out players again (they carry their tag in skinID).
        internal void RestoreSleepers()
        {
            foreach (FormRecord rec in Data.Forms.Values.ToList())
            {
                BaseCombatEntity animal = null;
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                {
                    BaseCombatEntity npc = net as BaseNPC2 ?? (BaseCombatEntity)(net as RidableHorse);
                    if (npc != null && rec.Tag != 0UL && npc.skinID == rec.Tag)
                    {
                        animal = npc;
                        break;
                    }
                }
                if (animal == null)
                {
                    SpeciesDef species = SpeciesById(rec.Species);
                    if (species != null && species.Available)
                    {
                        animal = GameManager.server.CreateEntity(species.Prefab, rec.Position.Value, Quaternion.identity) as BaseCombatEntity;
                        if (animal != null)
                        {
                            animal.skinID = rec.Tag;
                            animal.Spawn();
                            animal.health = Mathf.Max(1f, rec.Health);
                            RidableHorse reborn = animal as RidableHorse;
                            if (reborn != null)
                            {
                                reborn.SetBreed((int)rec.Genes);
                                SetSeats(reborn, 0);
                            }
                        }
                    }
                }
                if (animal == null)
                {
                    Data.Forms.Remove(rec.Player);
                    continue;
                }
                FormSleepers[rec.Player] = animal;
                BaseNPC2 sleeperNpc = animal as BaseNPC2;
                if (sleeperNpc != null)
                {
                    Park(sleeperNpc, "sleep");
                }
                else if (animal is RidableHorse && Stable != null)
                {
                    Stable.SleepForm((RidableHorse)animal);
                }
                LivestockAnimal livestock = animal as LivestockAnimal;
                if (livestock != null)
                {
                    livestock.SetSleeping(true);
                }
            }
        }

        // The animal of an absent player died: the player dies with it - it is processed at once, the player meets the death on the next login.
        internal void SleeperAnimalDied(BaseNetworkable entity)
        {
            if (FormSleepers.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<ulong, BaseCombatEntity> pair in FormSleepers.ToList())
            {
                if (pair.Value == entity)
                {
                    FormSleepers.Remove(pair.Key);
                    Data.Forms.Remove(pair.Key);
                    BasePlayer sleeper = BasePlayer.FindSleeping(pair.Key);
                    if (sleeper != null && !sleeper.IsDead())
                    {
                        sleeper.limitNetworking = false;
                        sleeper.Die();
                    }
                }
            }
        }

        // ---------------------------------------------------------------- the animal a player has left

        internal sealed class LeftForm
        {
            public BaseCombatEntity Animal;
            public ulong Owner;
            public List<ItemRecord> Bag = new List<ItemRecord>();
        }

        internal readonly Dictionary<ulong, LeftForm> LeftForms = new Dictionary<ulong, LeftForm>();

        internal bool LeftOwnedBy(BaseNetworkable animal, BasePlayer player)
        {
            LeftForm lf;
            return animal != null && animal.net != null && player != null && LeftForms.TryGetValue(animal.net.ID.Value, out lf) && lf.Owner == player.userID;
        }

        internal void LeaveAnimalBehind(Driver d)
        {
            BaseCombatEntity body = d.Body;
            var left = new LeftForm { Animal = body, Owner = d.Player.userID };
            List<ItemRecord> carried;
            if (FormBags.TryGetValue(d.Player.userID, out carried))
            {
                left.Bag = carried;                  // the bag stays with the animal
                FormBags.Remove(d.Player.userID);
            }
            LeftForms[body.net.ID.Value] = left;
            BaseNPC2 npc = body as BaseNPC2;
            if (npc != null)
            {
                Park(npc, "left");
            }
            RidableHorse horse = body as RidableHorse;
            if (horse != null)
            {
                SetSeats(horse, Mathf.Max(1, SeatsOf(horse)));       // the horse can be ridden by its owner
            }
        }

        internal void DespawnLeft(LeftForm lf, bool effect)
        {
            LeftForms.Remove(lf.Animal != null && lf.Animal.net != null ? lf.Animal.net.ID.Value : 0UL);
            if (!Valid(lf.Animal))
            {
                return;
            }
            if (effect)
            {
                FormSmoke(lf.Animal.transform.position);
            }
            foreach (ItemRecord rec in lf.Bag)
            {
                Item item = ItemFromRecord(rec);
                if (item != null)
                {
                    item.Drop(lf.Animal.transform.position + Vector3.up, Vector3.up);       // what the animal carried stays on the ground
                }
            }
            lf.Bag.Clear();
            Despawn(lf.Animal);
        }

        internal sealed class LeftModule : Module
        {
            public override int Order => 16;
            private float nextCheck;

            public override void Tick(float now)
            {
                if (Bm.LeftForms.Count == 0 || now < nextCheck)
                {
                    return;
                }
                nextCheck = now + 1f;
                foreach (LeftForm lf in Bm.LeftForms.Values.ToList())
                {
                    if (!Valid(lf.Animal) || lf.Animal.IsDead())
                    {
                        Bm.LeftForms.Remove(lf.Animal != null && lf.Animal.net != null ? lf.Animal.net.ID.Value : 0UL);
                        continue;
                    }
                    if (Bm.DriverOfAnimal(lf.Animal) != null)
                    {
                        continue;                                      // the owner is in it or on it
                    }
                    BasePlayer owner = BasePlayer.FindByID(lf.Owner);
                    if (owner == null || !owner.IsConnected || owner.IsDead() || Vector3.Distance(owner.transform.position, lf.Animal.transform.position) > 50f)
                    {
                        Bm.DespawnLeft(lf, true);
                    }
                }
            }

            // X at the animal that was left: the player becomes it again.
            public override object OnServerCommand(BasePlayer player, ConsoleSystem.Arg arg)
            {
                if (Bm.LeftForms.Count == 0 || arg.cmd.FullName != "vehicle.swapseats" || Bm.DriverOf(player) != null)
                {
                    return null;
                }
                LeftForm best = null;
                float bestDist = 5f;
                foreach (LeftForm lf in Bm.LeftForms.Values)
                {
                    if (lf.Owner != player.userID || !Valid(lf.Animal))
                    {
                        continue;
                    }
                    float dist = Vector3.Distance(player.transform.position, lf.Animal.transform.position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = lf;
                    }
                }
                if (best == null)
                {
                    return null;
                }
                BaseNPC2 npc = best.Animal as BaseNPC2;
                if (npc != null)
                {
                    Bm.Unpark(npc, "left");
                }
                Bm.LeftForms.Remove(best.Animal.net.ID.Value);
                List<ItemRecord> bagBack = best.Bag;
                BasePlayer p = player;
                BaseCombatEntity animal = best.Animal;
                Bm.NextTick(() =>
                {
                    if (Bm.StartControl(p, animal, ControlKind.Form, true))
                    {
                        Driver d = Bm.DriverOf(p);
                        if (d != null)
                        {
                            d.OwnsAnimal = true;
                        }
                        Bm.FormBags[p.userID] = bagBack;      // it carries its bag again
                    }
                });
                return true;
            }

            public override void OnPlayerDisconnected(BasePlayer player, string reason)
            {
                foreach (LeftForm lf in Bm.LeftForms.Values.Where(x => x.Owner == player.userID).ToList())
                {
                    if (Bm.DriverOfAnimal(lf.Animal) == null)
                    {
                        Bm.DespawnLeft(lf, false);
                    }
                }
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                if (Bm.LeftForms.Count > 0 && entity != null && entity.net != null)
                {
                    Bm.LeftForms.Remove(entity.net.ID.Value);
                }
            }

            public override void OnDisable()
            {
                Clear();
            }

            public override void Unload()
            {
                Clear();
            }

            private void Clear()
            {
                foreach (LeftForm lf in Bm.LeftForms.Values.ToList())
                {
                    Bm.DespawnLeft(lf, false);
                }
                Bm.LeftForms.Clear();
            }
        }

        internal sealed class FormModule : Module
        {
            public override int Order => 20;

            public override void OnServerInitialized()
            {
                Bm.RestoreSleepers();
            }

            public override void OnPlayerConnected(BasePlayer player)
            {
                if (Bm.Data.Forms.ContainsKey(player.userID))
                {
                    Bm.ResumeForm(player);              // the animal lay down when the player left: he is that animal again
                }
            }

            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                Bm.SleeperAnimalDied(entity);
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                Bm.SleeperAnimalDied(entity);
            }

            public override void Unload()
            {
                Bm.ReleaseAllParked();
            }

            public override void OnDisable()
            {
                Bm.ReleaseAllParked();
            }
        }
    }
}
