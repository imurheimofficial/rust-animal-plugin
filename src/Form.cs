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
            timer.Once(0.1f, () => FormAppear(at, hearer, 0.35f));     // and the animal appears with the same effect, heard by the player too
            Data.Forms.Remove(d.Player.userID);
            FormSleepers.Remove(d.Player.userID);
        }

        internal void OnFormEnded(Driver d, string reason)
        {
            BasePlayer player = d.Player;
            Vector3 spot = Valid(d.Body) ? d.Body.transform.position : player.transform.position;
            FormSmoke(spot, player, 0.3f);
            if (player != null && player.IsConnected && !player.IsDead() && reason != "animal died")
            {
                Vector3 back = GroundNear(spot, d.Body != null && !d.Body.IsDestroyed ? d.Body.bounds.extents.x * ModelScale(d.Body) + 1.2f : 1.5f);
                player.Teleport(back);
                FormAppear(back, player, 0.3f);
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
                    GearRecord carried;
                    if (RecordOf(d.Body) == null && FormGears.TryGetValue(player.userID, out carried))
                    {
                        DropGearRecord(carried, spot + Vector3.up * 0.6f);   // what the animal carried stays on the ground
                        FormGears.Remove(player.userID);
                    }
                    RemoveSaddle(d.Body);
                    Data.Bosses.Remove(d.Body.skinID);
                    d.Body.Kill();
                }
            }
        }

        // The animal of a player died: what it carried falls next to its body.
        internal void FormAnimalDied(Driver d)
        {
            if (d == null || d.Player == null || RecordOf(d.Body) != null)
            {
                return;                                 // a tamed animal drops its gear by its record (TameModule)
            }
            GearRecord carried;
            if (FormGears.TryGetValue(d.Player.userID, out carried))
            {
                Vector3 at = d.Body != null && !d.Body.IsDestroyed ? d.Body.transform.position : d.Player.transform.position;
                DropGearRecord(carried, at + Vector3.up * 0.6f);
                FormGears.Remove(d.Player.userID);
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
                OwnsAnimal = d.OwnsAnimal,
                Gear = RecordOf(animal) == null && FormGears.ContainsKey(player.userID) ? FormGears[player.userID] : null
            };
            FormGears.Remove(player.userID);              // the inventory stays with the sleeping animal (the record), not with the player
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
                    DropGearRecord(rec.Gear, spot + Vector3.up * 0.6f);     // what it carried stays on the ground
                    RemoveSaddle(animal);
                    Data.Bosses.Remove(animal.skinID);
                    animal.Kill();
                }
            }
            else
            {
                DropGearRecord(rec.Gear, spot + Vector3.up * 0.6f);
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
                DropGearRecord(rec.Gear, (Valid(animal) ? animal.transform.position : rec.Position.Value) + Vector3.up * 0.6f);
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
                if (rec.Gear != null)
                {
                    FormGears[player.userID] = rec.Gear;       // the inventory it carried
                }
                Driver d;
                if (StartControl(player, animal, ControlKind.Form, true, true))
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
                            EnforceLook(animal, species, rec.Genes != 0UL && animal is LivestockAnimal ? (int?)unchecked((int)(uint)rec.Genes) : null);
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
                    DropGearRecord(rec.Gear, rec.Position.Value + Vector3.up * 0.6f);
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
                    FormRecord dead;
                    if (Data.Forms.TryGetValue(pair.Key, out dead) && dead != null)
                    {
                        Vector3 at = Valid(pair.Value) ? pair.Value.transform.position : dead.Position.Value;
                        DropGearRecord(dead.Gear, at + Vector3.up * 0.6f);      // what the sleeping animal carried
                    }
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
            public GearRecord Gear = new GearRecord();       // its inventory (saddle, shoes, bag, armour, storage)
            public bool Kept;                                // another player has ridden it: it never vanishes
        }

        internal readonly Dictionary<ulong, LeftForm> LeftForms = new Dictionary<ulong, LeftForm>();

        internal bool LeftOwnedBy(BaseNetworkable animal, BasePlayer player)
        {
            LeftForm lf;
            return animal != null && animal.net != null && player != null && LeftForms.TryGetValue(animal.net.ID.Value, out lf) && lf.Owner == player.userID;
        }

        // The animal a player has left is ridden by him and by whom the riding rule lets ride his animals (with a saddle).
        internal bool LeftUsableBy(BaseNetworkable animal, BasePlayer player)
        {
            LeftForm lf;
            return animal != null && animal.net != null && player != null && LeftForms.TryGetValue(animal.net.ID.Value, out lf)
                && (lf.Owner == player.userID || MayUse(player, lf.Owner, Cfg.Riding.Ownership));
        }

        private static bool RiddenByOther(LeftForm lf, Driver d)
        {
            if (d != null && d.Player != null && d.Player.userID != lf.Owner)
            {
                return true;
            }
            BaseVehicle vehicle = lf.Animal as BaseVehicle;
            if (vehicle == null || vehicle.mountPoints == null)
            {
                return false;
            }
            foreach (BaseVehicle.MountPointInfo mp in vehicle.mountPoints)
            {
                BasePlayer rider = mp.mountable != null ? mp.mountable.GetMounted() : null;
                if (rider != null && rider.userID != lf.Owner)
                {
                    return true;
                }
            }
            return false;
        }

        internal void LeaveAnimalBehind(Driver d)
        {
            BaseCombatEntity body = d.Body;
            var left = new LeftForm { Animal = body, Owner = d.Player.userID };
            GearRecord carried;
            if (RecordOf(body) == null && FormGears.TryGetValue(d.Player.userID, out carried) && carried != null)
            {
                left.Gear = carried;                 // the inventory stays with the animal
                FormGears.Remove(d.Player.userID);
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
                SetSeats(horse, HorseModule.SaddleSeats(horse));      // its owner rides it the game's way, when it has a saddle
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
            DropGearRecord(lf.Gear, lf.Animal.transform.position + Vector3.up * 0.6f);      // what the animal carried stays on the ground
            RemoveSaddle(lf.Animal);
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
                        if (Valid(lf.Animal))
                        {
                            Bm.DropGearRecord(lf.Gear, lf.Animal.transform.position + Vector3.up * 0.6f);
                        }
                        Bm.LeftForms.Remove(lf.Animal != null && lf.Animal.net != null ? lf.Animal.net.ID.Value : 0UL);
                        continue;
                    }
                    Driver rider = Bm.DriverOfAnimal(lf.Animal);
                    if (!lf.Kept && RiddenByOther(lf, rider))
                    {
                        lf.Kept = true;                                // somebody else has ridden it: it stays for good
                    }
                    if (rider != null || lf.Kept)
                    {
                        continue;                                      // somebody is in it or on it, or it is kept
                    }
                    BasePlayer owner = BasePlayer.FindByID(lf.Owner);
                    if (owner == null || !owner.IsConnected || owner.IsDead() || Vector3.Distance(owner.transform.position, lf.Animal.transform.position) > Mathf.Max(5f, Bm.Cfg.Form.LeftDespawnDistance))
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
                if (!Bm.SwapAllowed(player))
                {
                    return true;
                }
                BaseNPC2 npc = best.Animal as BaseNPC2;
                if (npc != null)
                {
                    Bm.Unpark(npc, "left");
                }
                Bm.CloseInventoriesOf(best.Gear);
                Bm.LeftForms.Remove(best.Animal.net.ID.Value);
                GearRecord gearBack = best.Gear;
                BasePlayer p = player;
                BaseCombatEntity animal = best.Animal;
                Bm.FormGears[p.userID] = gearBack;            // it carries its inventory again
                Bm.NextTick(() =>
                {
                    if (Bm.StartControl(p, animal, ControlKind.Form, true, true))
                    {
                        Driver d = Bm.DriverOf(p);
                        if (d != null)
                        {
                            d.OwnsAnimal = true;
                        }
                    }
                    else if (Valid(animal) && !animal.IsDead())
                    {
                        // it could not be taken again: it stays where it is, with its inventory
                        Bm.FormGears.Remove(p.userID);
                        Bm.LeftForms[animal.net.ID.Value] = new LeftForm { Animal = animal, Owner = p.userID, Gear = gearBack };
                        BaseNPC2 parked = animal as BaseNPC2;
                        if (parked != null)
                        {
                            Bm.Park(parked, "left");
                        }
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

            // An animal that was left behind dies (or is removed some other way): what it carried falls to the ground. It may be ridden by its
            // owner at that moment.
            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                DropLeft(entity);
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                DropLeft(entity);
            }

            private void DropLeft(BaseNetworkable entity)
            {
                LeftForm lf;
                if (Bm.LeftForms.Count == 0 || entity == null || entity.net == null || !Bm.LeftForms.TryGetValue(entity.net.ID.Value, out lf))
                {
                    return;
                }
                Bm.LeftForms.Remove(entity.net.ID.Value);
                if (entity.transform != null)
                {
                    Bm.DropGearRecord(lf.Gear, entity.transform.position + Vector3.up * 0.6f);
                }
                Bm.RemoveSaddle(entity);
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
