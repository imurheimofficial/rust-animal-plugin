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
        // ---------------------------------------------------------------- owned animals

        // Entities of the tamed records by tag (a record finds its animal again through the tag stored in skinID).
        internal readonly Dictionary<ulong, BaseEntity> TagIndex = new Dictionary<ulong, BaseEntity>();

        internal static void MakeMale(LivestockAnimal animal)
        {
            if (animal != null)
            {
                animal.IsMale = true;
            }
        }

        internal bool IsTamed(BaseEntity e)
        {
            return RecordOf(e) != null;
        }

        internal int OwnedCount(ulong owner)
        {
            return Data.Tamed.Values.Count(r => r.Owner == owner);
        }

        internal BaseEntity EntityOfRecord(TamedRecord rec)
        {
            BaseEntity e;
            if (TagIndex.TryGetValue(rec.Tag, out e) && Valid(e))
            {
                return e;
            }
            TagIndex.Remove(rec.Tag);
            return null;
        }

        internal List<BaseEntity> OwnedEntities(ulong owner)
        {
            var list = new List<BaseEntity>();
            foreach (TamedRecord rec in Data.Tamed.Values)
            {
                if (rec.Owner == owner)
                {
                    BaseEntity e = EntityOfRecord(rec);
                    if (e != null)
                    {
                        list.Add(e);
                    }
                }
            }
            return list;
        }

        // A wild animal becomes the property of a player (called by every taming flow).
        internal TamedRecord TameAnimal(BaseEntity entity, BasePlayer owner)
        {
            SpeciesDef species = SpeciesOf(entity);
            if (species == null || owner == null || !Valid(entity))
            {
                return null;
            }
            TamedRecord rec = RecordOf(entity);
            if (rec != null && rec.Owner == owner.userID)
            {
                return rec;
            }
            if (OwnedCount(owner.userID) >= Cfg.Taming.MaxOwned)
            {
                Tell(owner, "TameLimit", Cfg.Taming.MaxOwned);
                return null;
            }
            rec = MakeTamed(entity, species, owner.userID, false);
            TagIndex[rec.Tag] = entity;
            Tell(owner, "Tamed", species.Display);
            Log(2, owner.displayName + " tamed a " + species.Id);
            ApplyState(entity, rec);
            return rec;
        }

        internal void ReleaseAnimal(BaseEntity entity)
        {
            TamedRecord rec = RecordOf(entity);
            if (rec == null)
            {
                return;
            }
            BaseNPC2 npc = entity as BaseNPC2;
            if (npc != null)
            {
                StopFollower(npc);
                Unpark(npc, "tame");
            }
            Data.Tamed.Remove(rec.Tag);
            TagIndex.Remove(rec.Tag);
            RidableHorse horse = entity as RidableHorse;
            if (horse != null)
            {
                HorseState hs = Stable != null ? Stable.StateOf(horse) : null;
                if (hs != null)
                {
                    hs.Drive.Stop();
                    hs.Info.Calmed = true;          // a horse that was tamed once stays gentle; it stays under the plugin's care (tag kept)
                }
                return;
            }
            if (!Data.Spawned.Contains(entity.skinID))
            {
                entity.skinID = 0UL;
            }
        }

        // Puts a tamed animal into its command state: follow, wait or guard.
        internal void ApplyState(BaseEntity entity, TamedRecord rec)
        {
            RidableHorse horse = entity as RidableHorse;
            if (horse != null && Stable != null)
            {
                Stable.Command(horse, rec);
                return;
            }
            BaseNPC2 npc = entity as BaseNPC2;
            if (npc == null || !Cfg.Features.Taming)
            {
                return;
            }
            StopFollower(npc);
            ulong owner = rec.Owner;
            Func<Vector3?> ownerPos = () =>
            {
                BasePlayer o = BasePlayer.FindByID(owner);
                return o != null && o.IsConnected && !o.IsDead() ? (Vector3?)o.transform.position : null;
            };
            Follower f;
            switch (rec.State)
            {
                case "wait":
                    StartFollower(npc, "tame", () => null);
                    break;
                case "guard":
                    f = StartFollower(npc, "tame", ownerPos);
                    if (f != null)
                    {
                        f.StopDistance = Cfg.Taming.GuardRadius * 0.45f;
                        f.TeleportDistance = Cfg.Taming.TeleportDistance;
                        f.Target = () => FindThreat(npc, owner, 20f);
                    }
                    break;
                default:
                    f = StartFollower(npc, "tame", ownerPos);
                    if (f != null)
                    {
                        f.StopDistance = Cfg.Taming.FollowDistance;
                        f.TeleportDistance = Cfg.Taming.TeleportDistance;
                    }
                    break;
            }
        }

        // Hostile things near a guarding animal: players who are not in the owner's party and wild predators.
        private BaseCombatEntity FindThreat(BaseNPC2 animal, ulong ownerId, float radius)
        {
            BasePlayer owner = BasePlayer.FindByID(ownerId);
            BaseCombatEntity best = null;
            float bestDist = radius;
            var near = new List<BaseCombatEntity>();
            Vis.Entities(animal.transform.position, radius, near);
            foreach (BaseCombatEntity e in near)
            {
                if (!Alive(e) || e == animal || e == owner)
                {
                    continue;
                }
                BasePlayer p = e as BasePlayer;
                if (p != null)
                {
                    if (p.IsNpc || p.IsSleeping() || IsFormPlayer(p) || (owner != null && SameParty(owner, p)) || p.userID == ownerId || p.InSafeZone())
                    {
                        continue;
                    }
                }
                else
                {
                    BaseNPC2 other = e as BaseNPC2;
                    SpeciesDef sp = other != null ? SpeciesOf(other) : null;
                    if (other == null || sp == null || !sp.Predator || IsTamed(other) || DriverOfAnimal(other) != null)
                    {
                        continue;
                    }
                }
                float d = Vector3.Distance(animal.transform.position, e.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- commands (R6)

        private static readonly string[] CommandNames = { "follow", "wait", "guard", "attack", "release" };

        private void CommandAnimals(BasePlayer player, string[] args, Action<string> reply)
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
            if (!CommandNames.Contains(cmd))
            {
                reply("Usage: bm.cmd follow | wait | guard | attack | release  (aim at an animal's head for just that one)");
                return;
            }
            int n = ApplyCommand(player, cmd);
            reply(n > 0 ? L("CommandSet", player.UserIDString, n + " animal(s)", cmd) : L("NoAnimalsNearby"));
        }

        // Animals a command goes to: the one aimed at, else every animal of the player within reach.
        private List<BaseEntity> CommandTargets(BasePlayer player)
        {
            BaseNPC2 aimed = AnimalInSight(player, Cfg.Taming.CommandRange, 8f);
            if (aimed != null)
            {
                TamedRecord rec = RecordOf(aimed);
                if (rec != null && rec.Owner == player.userID)
                {
                    return new List<BaseEntity> { aimed };
                }
            }
            Vector3 here = player.transform.position;
            return OwnedEntities(player.userID).Where(e => Vector3.Distance(e.transform.position, here) <= Cfg.Taming.CommandRange).ToList();
        }

        internal int ApplyCommand(BasePlayer player, string cmd, bool everyAnimal = false)
        {
            if (!Cfg.Features.Commands || !Active)
            {
                return 0;
            }
            int count = 0;
            BaseCombatEntity victim = cmd == "attack" ? PickAttackTarget(player) : null;
            foreach (BaseEntity e in everyAnimal ? OwnedEntities(player.userID) : CommandTargets(player))
            {
                TamedRecord rec = RecordOf(e);
                if (rec == null)
                {
                    continue;
                }
                BaseNPC2 npc = e as BaseNPC2;
                RidableHorse mount = e as RidableHorse;
                if (mount != null && cmd == "attack")
                {
                    if (victim != null && Stable != null)
                    {
                        Stable.Attack(mount, victim, 60f);
                        count++;
                    }
                    continue;
                }
                switch (cmd)
                {
                    case "release":
                        ReleaseAnimal(e);
                        break;
                    case "attack":
                        if (npc == null)
                        {
                            continue;
                        }
                        BaseCombatEntity foe = victim ?? FindThreat(npc, player.userID, 30f);
                        if (foe == null)
                        {
                            continue;
                        }
                        ApplyState(e, rec);
                        Follower f = FollowerOf(npc);
                        if (f != null)
                        {
                            float until = Time.time + 60f;
                            BaseCombatEntity target = foe;
                            f.Target = () => Time.time < until && Alive(target) ? target : null;
                        }
                        break;
                    default:
                        rec.State = cmd;
                        ApplyState(e, rec);
                        break;
                }
                count++;
            }
            return count;
        }

        // What the player points at (a hostile player or animal), if anything.
        private BaseCombatEntity PickAttackTarget(BasePlayer player)
        {
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.position, player.eyes.HeadForward(), out hit, Cfg.Taming.CommandRange, LayerMask.GetMask("Player (Server)", "AI", "Deployed", "Construction", "World", "Terrain"), QueryTriggerInteraction.Ignore))
            {
                BaseCombatEntity e = hit.GetEntity() as BaseCombatEntity;
                if (e != null && Alive(e) && !(e is BasePlayer && SameParty(player, (BasePlayer)e)) && !(e is BaseNPC2 && IsTamed(e)))
                {
                    return e;
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- the use key (E) on animals

        // E in front of an animal: syringe on a trapped predator, the deer's inventory, gear and mounting of owned animals, claiming a bonded one.
        internal void UseKey(BasePlayer player)
        {
            if (!Cfg.Features.Riding && !Cfg.Features.Taming)
            {
                return;
            }
            if (!Has(player, PermUse) || DriverOf(player) != null)
            {
                return;
            }
            BaseNPC2 animal = AnimalInSight(player, Cfg.Riding.MountDistance, 20f);
            if (animal == null)
            {
                RidableHorse horse = HorseInSight(player, Cfg.Riding.MountDistance + 1f, 20f);
                if (horse != null)
                {
                    Driver horseHost = DriverOfAnimal(horse);
                    if (horseHost != null)
                    {
                        if (horseHost.Kind == ControlKind.Form)
                        {
                            TryFormHandle(player, horseHost);   // crouch + E leads it; riding is the game's own mount
                        }
                        return;
                    }
                    if (LeftOwnedBy(horse, player))
                    {
                        if (player.serverInput.IsDown(BUTTON.DUCK))
                        {
                            OpenFormBag(player, LeftForms[horse.net.ID.Value]);     // Ctrl + E: what the horse carried for its player
                        }
                        return;                                 // E: the game's own mount of the horse the player has just left
                    }
                    HorseUse(player, horse);
                }
                return;
            }
            Driver hostDriver = DriverOfAnimal(animal);
            if (hostDriver != null)
            {
                if (hostDriver.Kind == ControlKind.Form)
                {
                    TryFormHandle(player, hostDriver);        // the animal is a player: a rider, a leash
                }
                else
                {
                    TryRideBehind(player, hostDriver);        // a ridden animal with a double saddle carries a second rider
                }
                return;
            }
            SpeciesDef species = SpeciesOf(animal);
            if (species == null || !SpeciesEnabled(species))
            {
                return;
            }
            if (LeftOwnedBy(animal, player))
            {
                if (player.serverInput.IsDown(BUTTON.DUCK))
                {
                    OpenFormBag(player, LeftForms[animal.net.ID.Value]);      // Ctrl + E: what the animal carries
                    return;
                }
                if (species.Ride != RideTier.None && !species.Critter)
                {
                    StartControl(player, animal, ControlKind.Ride);        // E at the animal one has left: riding it, like the horse
                }
                return;
            }
            if (TryBossSaddle(player, animal) || TrySyringe(player, animal) || TryDeerBox(player, animal))
            {
                return;
            }
            TamedRecord rec = RecordOf(animal);
            if (rec != null)
            {
                if (!MayUse(player, rec.Owner, "team"))
                {
                    Tell(player, "NotYours");
                    return;
                }
                bool crouch = player.serverInput.IsDown(BUTTON.DUCK);
                if (species.Gear && Cfg.Features.Gear && (crouch || rec.Gear.Saddle <= 0))
                {
                    OpenGear(player, animal, rec);
                    return;
                }
                TryMountFromUse(player);
                return;
            }
            if (species.Ride == RideTier.Restricted)
            {
                TryMountFromUse(player);
                return;
            }
            TryClaim(player, animal, species);
        }

        // E on an animal somebody rides: with a double saddle the player climbs on behind the rider (owner and team, like the riding rule).
        private void TryRideBehind(BasePlayer player, Driver host)
        {
            if (host == null || host.Kind != ControlKind.Ride || host.Animal == null || host.Player == player)
            {
                return;
            }
            TamedRecord rec = RecordOf(host.Animal);
            if (rec == null || rec.Gear.Saddle < 2)
            {
                TellRaw(player, "Only an animal with a double saddle carries a second rider.");
                return;
            }
            if (!MayUse(player, rec.Owner, Cfg.Riding.Ownership) && !SameParty(player, host.Player))
            {
                Tell(player, "NotYours");
                return;
            }
            host.PruneGuests();
            if (host.Guests.Count > 0)
            {
                TellRaw(player, "There is no room left on the " + host.Species.Display.ToLowerInvariant() + ".");
                return;
            }
            if (host.AddGuest(player, true))
            {
                TellRaw(player, "You ride behind " + host.Player.displayName + ". Press Space to get off.");
            }
        }

        // ---------------------------------------------------------------- module (hooks, persistence)

        internal sealed class TameModule : Module
        {
            public override int Order => 30;

            private readonly Dictionary<ulong, string> boneMode = new Dictionary<ulong, string>();
            private readonly Dictionary<ulong, float> boneCooldown = new Dictionary<ulong, float>();

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["Tamed"] = "The {0} is tamed now and follows you. Commands: /bm cmd follow|wait|guard|attack|release, a Bone in hand, or the gestures ok (wait), wave (follow), thumbs up (guard), point (attack).",
                    ["TameLimit"] = "You already own {0} animals.",
                    ["FeatureOff"] = "That feature is switched off on this server.",
                    ["BondProgress"] = "It is getting used to you: {0}/{1} s. Stay near it and be gentle, then press E again.",
                    ["BondTooEarly"] = "It trusts someone else more than you.",
                    ["BoneMode"] = "Bone command: {0} (primary use = give it, secondary use = next command).",
                };
            }

            public override void OnServerInitialized()
            {
                Bm.timer.Once(3f, Bm.RestoreTamed);
            }

            public override void OnSave()
            {
                Bm.StoreTamed();
            }

            public override void Unload()
            {
                Bm.StoreTamed();
                Bm.StopAllFollowers();
            }

            public override void OnDisable()
            {
                Bm.StopAllFollowers();
            }

            public override void OnEnable()
            {
                Bm.RestoreTamed();
            }

            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (input.WasJustPressed(BUTTON.USE))
                {
                    Bm.UseKey(player);
                }
                if (!Bm.Cfg.Features.Commands)
                {
                    return null;
                }
                if (input.WasJustPressed(BUTTON.FIRE_PRIMARY) || input.WasJustPressed(BUTTON.FIRE_SECONDARY))
                {
                    Item active = player.GetActiveItem();
                    if (active != null && active.info.shortname == "bone.fragments")
                    {
                        BoneUse(player, input.WasJustPressed(BUTTON.FIRE_SECONDARY));
                    }
                }
                return null;
            }

            private void BoneUse(BasePlayer player, bool cycle)
            {
                float last;
                if (boneCooldown.TryGetValue(player.userID, out last) && Time.time - last < 0.5f)
                {
                    return;
                }
                boneCooldown[player.userID] = Time.time;
                string mode;
                if (!boneMode.TryGetValue(player.userID, out mode))
                {
                    mode = "follow";
                }
                if (cycle)
                {
                    mode = CommandNames[(Array.IndexOf(CommandNames, mode) + 1) % CommandNames.Length];
                    boneMode[player.userID] = mode;
                    Bm.Tell(player, "BoneMode", mode);
                    return;
                }
                Bm.ApplyCommand(player, mode);
            }

            // The gestures every player owns command owned animals nearby (R6A.4); the human gesture still plays.
            public override object CanUseGesture(BasePlayer player, GestureConfig gesture)
            {
                if (!Bm.Cfg.Taming.GestureCommands || !Bm.Cfg.Features.Commands || Bm.DriverOf(player) != null)
                {
                    return null;
                }
                string cmd = null;
                switch (gesture.convarName)
                {
                    case "ok": cmd = "wait"; break;
                    case "wave": cmd = "follow"; break;
                    case "thumbsup": cmd = "guard"; break;
                    case "point": cmd = "attack"; break;
                }
                if (cmd != null)
                {
                    Bm.ApplyCommand(player, cmd);
                }
                return null;
            }

            // A tamed animal that dies drops its gear and bag and its record ends.
            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                TamedRecord rec = Bm.RecordOf(entity);
                if (rec == null)
                {
                    return;
                }
                Bm.DropGear(entity, rec);
                Bm.StopFollower(entity);
                Bm.Data.Tamed.Remove(rec.Tag);
                Bm.TagIndex.Remove(rec.Tag);
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                BaseEntity e = entity as BaseEntity;
                if (e != null && e.skinID >= TagBase)
                {
                    Bm.TagIndex.Remove(e.skinID);
                }
                Bm.StopFollower(entity);
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Tame: " + Bm.Data.Tamed.Count + " owned animals, " + Bm.Followers.Count + " followers");
            }
        }

        // Headless test: a cow is tamed, follows, is released; the record survives a store and restore.
        internal void TestTame(Vector3 pos)
        {
            SpeciesDef cow = SpeciesById("cow");
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.2f;
            BaseEntity entity = GameManager.server.CreateEntity(cow.Prefab, pos, Quaternion.identity);
            entity.Spawn();
            timer.Once(1.0f, () =>
            {
                TamedRecord rec = MakeTamed(entity, cow, 76561198727638665UL, true);
                TagIndex[rec.Tag] = entity;
                rec.State = "follow";
                ApplyState(entity, rec);
                timer.Once(1.0f, () =>
                {
                    BaseNPC2 npc = (BaseNPC2)entity;
                    Puts("[test] tame: record " + (RecordOf(entity) != null) + ", follower " + (FollowerOf(npc) != null) + ", parked " + IsParked(npc) + ", saddle " + rec.Gear.Saddle + ", speed bonus " + GearSpeedBonus(npc));
                    StoreTamed();
                    ReleaseAnimal(entity);
                    Puts("[test] tame: after release record " + (RecordOf(entity) != null) + ", follower " + (FollowerOf(npc) != null) + ", parked " + IsParked(npc));
                    entity.Kill();
                });
            });
        }

        // ---------------------------------------------------------------- persistence

        internal void StoreTamed()
        {
            foreach (TamedRecord rec in Data.Tamed.Values)
            {
                BaseEntity e = EntityOfRecord(rec);
                if (e != null)
                {
                    rec.Position = new V3(e.transform.position);
                    BaseCombatEntity c = e as BaseCombatEntity;
                    if (c != null)
                    {
                        rec.Health = c.health;
                    }
                }
            }
        }

        // After a start: find the animals of the records (skinID tag), recreate the ones the game did not save.
        internal void RestoreTamed()
        {
            if (!Active)
            {
                return;
            }
            var byTag = new Dictionary<ulong, BaseEntity>();
            foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
            {
                BaseEntity e = net as BaseEntity;
                if (e != null && e.skinID >= TagBase && Data.Tamed.ContainsKey(e.skinID) && (e is BaseNPC2 || e is RidableHorse))
                {
                    byTag[e.skinID] = e;
                }
            }
            foreach (TamedRecord rec in Data.Tamed.Values.ToList())
            {
                BaseEntity e;
                if (!byTag.TryGetValue(rec.Tag, out e))
                {
                    SpeciesDef species = SpeciesById(rec.Species);
                    if (species == null || !species.Available)
                    {
                        continue;
                    }
                    e = GameManager.server.CreateEntity(species.Prefab, rec.Position.Value, Quaternion.identity);
                    if (e == null)
                    {
                        continue;
                    }
                    e.skinID = rec.Tag;
                    e.Spawn();
                    BaseCombatEntity c = e as BaseCombatEntity;
                    if (c != null && rec.Health > 1f)
                    {
                        c.health = Mathf.Min(rec.Health, c.MaxHealth());
                    }
                    if (species.Id == "ram")
                    {
                        MakeMale(e as LivestockAnimal);
                    }
                }
                TagIndex[rec.Tag] = e;
                ApplyState(e, rec);
            }
        }
    }
}
