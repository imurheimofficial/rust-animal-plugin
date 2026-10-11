using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
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
                animal.SendNetworkUpdate();
            }
        }

        // The sex of the kind: a ram is male and a sheep is female (both use the sheep's body), a bull is male and a cow is female. The game rolls
        // the sex (and the coat and the name) when a livestock animal spawns, over anything set before: so it is set after the spawn, and once
        // more a moment later. genes / name / male: the exact look to keep (the ritual copies the look of the killed animal).
        internal void EnforceLook(BaseEntity entity, SpeciesDef species, int? genes = null, string name = null, bool? male = null)
        {
            Action apply = () =>
            {
                LivestockAnimal ls = entity as LivestockAnimal;
                if (ls == null || !Valid(ls) || species == null)
                {
                    return;
                }
                bool? sex = male;
                if (species.Id == "ram" || species.Id == "bull") sex = true;
                else if (species.Id == "sheep" || species.Id == "cow") sex = false;
                bool changed = false;
                if (sex.HasValue && ls.IsMale != sex.Value)
                {
                    ls.IsMale = sex.Value;
                    changed = true;
                }
                if (genes.HasValue && ls.Genes != genes.Value)
                {
                    ls.Genes = genes.Value;
                    changed = true;
                }
                if (!string.IsNullOrEmpty(name) && ls.AnimalName != name)
                {
                    ls.AnimalName = name;
                    changed = true;
                }
                if (changed)
                {
                    ls.SendNetworkUpdate();
                }
            };
            apply();
            timer.Once(0.5f, apply);
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
                if (rec.Gear != null && !GearEmpty(rec.Gear))
                {
                    CloseInventoriesOf(rec.Gear);
                    Data.WildGears[rec.Tag] = rec.Gear;        // a released animal keeps what it carries, as a wild animal
                    WildIndex[rec.Tag] = npc;
                }
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
            if (!Data.Spawned.Contains(entity.skinID) && !Data.WildGears.ContainsKey(entity.skinID))
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
        // A tap of E does the obvious thing with the animal in front of the player: rides it when it has a saddle, opens its inventory when it has
        // none, tames, claims. Ctrl + E opens the inventory (on a player's animal: takes or drops the leash). Holding E a moment shows everything
        // that can be done with that animal right now. The horse keeps the game's own E (mount, and its own menu when E is held).

        private sealed class UsePress
        {
            public BaseNPC2 Target;
            public float Start;
            public bool Crouch;
            public bool Done;
        }

        private readonly Dictionary<ulong, UsePress> usePresses = new Dictionary<ulong, UsePress>();
        private const float UseHoldTime = 0.45f;

        private bool UseAllowed(BasePlayer player)
        {
            return (Cfg.Features.Riding || Cfg.Features.Taming || Cfg.Features.Gear || Cfg.Features.AnimalForm)
                && Has(player, PermUse) && DriverOf(player) == null && !player.isMounted && !player.IsDead() && !player.IsSleeping();
        }

        // The game's own handling of livestock works with a tool in the hand (the knife shears, the bucket milks, the rope leads): with one of
        // those the plugin leaves E alone.
        private static bool VanillaTool(Item item)
        {
            if (item == null)
            {
                return false;
            }
            string n = item.info.shortname;
            return n.Contains("knife") || n.Contains("rope") || n.Contains("bucket") || n.Contains("jug") || n == "water.jar" || n.Contains("bota") || n.Contains("canteen");
        }

        internal void UsePressed(BasePlayer player)
        {
            usePresses.Remove(player.userID);
            CloseBeastMenu(player);
            if (!UseAllowed(player))
            {
                return;
            }
            bool crouch = player.serverInput.IsDown(BUTTON.DUCK);
            BaseNPC2 animal = AnimalInSight(player, Cfg.Riding.MountDistance, 20f);
            if (animal == null)
            {
                RidableHorse horse = HorseInSight(player, Cfg.Riding.MountDistance + 1f, 20f);
                if (horse != null)
                {
                    HorseKey(player, horse, crouch);
                }
                return;
            }
            if (animal is LivestockAnimal && VanillaTool(player.GetActiveItem()))
            {
                return;
            }
            usePresses[player.userID] = new UsePress { Target = animal, Start = Time.time, Crouch = crouch };
        }

        internal void UseHeld(BasePlayer player)
        {
            UsePress press;
            if (!usePresses.TryGetValue(player.userID, out press) || press.Done || Time.time - press.Start < UseHoldTime)
            {
                return;
            }
            press.Done = true;
            if (!Valid(press.Target) || press.Target.IsDead() || !UseAllowed(player))
            {
                return;
            }
            List<string[]> choices = BeastChoices(player, press.Target);
            if (choices.Count == 0)
            {
                UseTap(player, press.Target, press.Crouch);           // nothing to choose from: the hold counts as a tap
                return;
            }
            ShowBeastMenu(player, press.Target, choices);
        }

        internal void UseReleased(BasePlayer player)
        {
            UsePress press;
            if (!usePresses.TryGetValue(player.userID, out press))
            {
                return;
            }
            usePresses.Remove(player.userID);
            if (press.Done || !Valid(press.Target) || press.Target.IsDead())
            {
                return;
            }
            UseTap(player, press.Target, press.Crouch);
        }

        // E on a horse: the game mounts it and shows its menu; the plugin adds the leash of a horse that is a player (Ctrl + E), the inventory
        // of the horse a player has left (Ctrl + E) and taming of the wild ones.
        private void HorseKey(BasePlayer player, RidableHorse horse, bool crouch)
        {
            Driver horseHost = DriverOfAnimal(horse);
            if (horseHost != null)
            {
                if (horseHost.Kind == ControlKind.Form && crouch)
                {
                    TryFormHandle(player, horseHost, true);
                }
                return;
            }
            if (LeftOwnedBy(horse, player))
            {
                // E: the game mounts it when it has a saddle; Ctrl + E on a horse without one: its inventory (hold E: the game's own menu)
                if (crouch && SeatsOf(horse) == 0 && !OpenEntityLoot(player, horse))
                {
                    TellRaw(player, "Hold E at the horse and choose its inventory.");
                }
                return;
            }
            HorseUse(player, horse);
        }

        private void UseTap(BasePlayer player, BaseNPC2 animal, bool crouch)
        {
            if (!UseAllowed(player))
            {
                return;
            }
            Driver host = DriverOfAnimal(animal);
            if (host != null)
            {
                if (host.Kind == ControlKind.Form)
                {
                    FormKey(player, host, crouch);
                }
                else
                {
                    TryRideBehind(player, host);                    // a ridden animal with a double saddle carries a second rider
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
                if (crouch || (species.Gear && Cfg.Features.Gear && SaddleOf(animal) <= 0 && !RideBypass(player)))
                {
                    if (!crouch)
                    {
                        TellRaw(player, "It has no saddle: fit one in slot 1 of its inventory to ride it.");
                    }
                    OpenInventory(player, animal);
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
                if (species.Gear && Cfg.Features.Gear && (crouch || (SaddleOf(animal) <= 0 && !RideBypass(player))))
                {
                    OpenInventory(player, animal);
                    return;
                }
                if (species.Ride != RideTier.None && !species.Critter)
                {
                    StartControl(player, animal, ControlKind.Ride);
                }
                return;
            }
            if (crouch && species.Gear && Cfg.Features.Gear)
            {
                OpenInventory(player, animal);                            // Ctrl + E: the inventory of a wild animal too
                return;
            }
            if (species.Ride == RideTier.Restricted)
            {
                if (!crouch)
                {
                    StartControl(player, animal, ControlKind.Ride);
                }
                return;
            }
            if (!TryClaim(player, animal, species) && species.Gear && Cfg.Features.Gear && !crouch)
            {
                OpenInventory(player, animal);                            // nothing to tame with E: its inventory
            }
        }

        private bool RideBypass(BasePlayer player)
        {
            return Cfg.Riding.AdminBypass && IsAdmin(player);
        }

        // E on another player's animal: rides it when it has a saddle, opens its inventory when it has none (so that one can be fitted).
        // Ctrl + E: the leash.
        private void FormKey(BasePlayer player, Driver host, bool crouch)
        {
            if (crouch)
            {
                TryFormHandle(player, host, true);
                return;
            }
            if (host.Horse == null && host.Species != null && host.Species.Gear && Cfg.Features.Gear && !host.Species.Critter
                && host.Species.Ride != RideTier.None && FormHandledBy(host, player) && MaxRiders(host) <= 0)
            {
                TellRaw(player, "It has no saddle yet: fit one in slot 1 of its inventory to ride it.");
                OpenInventory(player, host.Body);
                return;
            }
            TryFormHandle(player, host, false);
        }

        // E on an animal somebody rides: with a double saddle the player climbs on behind the rider (owner and team, like the riding rule).
        private void TryRideBehind(BasePlayer player, Driver host)
        {
            if (host == null || host.Kind != ControlKind.Ride || host.Animal == null || host.Player == player)
            {
                return;
            }
            if (SaddleOf(host.Animal) < 2)
            {
                TellRaw(player, "Only an animal with a double saddle carries a second rider.");
                return;
            }
            TamedRecord rec = RecordOf(host.Animal);
            if (rec != null && !MayUse(player, rec.Owner, Cfg.Riding.Ownership) && !SameParty(player, host.Player))
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

        // ---------------------------------------------------------------- the menu of E held

        private const string BeastUi = "BeastmasterBeast";

        private sealed class BeastMenu
        {
            public BaseNPC2 Target;
            public float Until;
        }

        private readonly Dictionary<ulong, BeastMenu> beastMenus = new Dictionary<ulong, BeastMenu>();

        // What this player can do with that animal right now: { action, button text }.
        internal List<string[]> BeastChoices(BasePlayer player, BaseNPC2 animal)
        {
            var list = new List<string[]>();
            if (player == null || !Valid(animal) || animal.IsDead())
            {
                return list;
            }
            SpeciesDef species = SpeciesOf(animal);
            if (species == null)
            {
                return list;
            }
            GearRecord ignored;
            bool inventory = Cfg.Features.Gear && species.Gear && InventoryProblem(player, animal, out ignored, false) == null;
            Driver host = DriverOfAnimal(animal);
            if (host != null)
            {
                if (host.Kind == ControlKind.Form && host.Player != player)
                {
                    host.PruneGuests();
                    if (host.Horse == null && FormMountCheck(player, host, false) && host.Guests.Count < MaxRiders(host))
                    {
                        list.Add(new[] { "ride", "Ride" });
                    }
                    if (inventory)
                    {
                        list.Add(new[] { "inventory", "Inventory" });
                    }
                    if (FormHandledBy(host, player))
                    {
                        list.Add(new[] { "lead", host.Leader == player ? "Let go" : "Lead" });
                    }
                }
                else if (host.Kind == ControlKind.Ride && host.Player != player && SaddleOf(animal) >= 2)
                {
                    host.PruneGuests();
                    if (host.Guests.Count == 0)
                    {
                        list.Add(new[] { "behind", "Ride behind" });
                    }
                }
                return list;
            }
            object[] args;
            bool rideable = species.Ride != RideTier.None && !species.Critter && ControlProblem(player, animal, ControlKind.Ride, out args) == null;
            if (LeftOwnedBy(animal, player))
            {
                if (rideable)
                {
                    list.Add(new[] { "ride", "Ride" });
                }
                if (inventory)
                {
                    list.Add(new[] { "inventory", "Inventory" });
                }
                return list;
            }
            TamedRecord rec = RecordOf(animal);
            if (rec != null && MayUse(player, rec.Owner, "team"))
            {
                if (rideable)
                {
                    list.Add(new[] { "ride", "Ride" });
                }
                if (inventory)
                {
                    list.Add(new[] { "inventory", "Inventory" });
                }
                if (Cfg.Features.Taming)
                {
                    list.Add(new[] { "follow", rec.State == "follow" ? "Follow (now)" : "Follow" });
                    list.Add(new[] { "wait", rec.State == "wait" ? "Wait (now)" : "Wait" });
                    list.Add(new[] { "guard", rec.State == "guard" ? "Guard (now)" : "Guard" });
                }
            }
            else if (rec == null && inventory)
            {
                list.Add(new[] { "inventory", "Inventory" });            // a wild animal has the horse's inventory too
            }
            return list;
        }

        private void ShowBeastMenu(BasePlayer player, BaseNPC2 animal, List<string[]> choices)
        {
            CloseBeastMenu(player);
            SpeciesDef species = SpeciesOf(animal);
            TamedRecord rec = RecordOf(animal);
            Driver host = DriverOfAnimal(animal);
            string title = species != null ? species.Display : "Animal";
            if (host != null && host.Kind == ControlKind.Form && host.Player != null)
            {
                title += "  <size=12>(" + host.Player.displayName + ")</size>";
            }
            else if (rec != null && !string.IsNullOrEmpty(rec.Name))
            {
                title += "  <size=12>(" + rec.Name + ")</size>";
            }
            float rows = choices.Count + 1;
            float height = 0.045f * rows + 0.06f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.08 0.09 0.07 0.92" }, RectTransform = { AnchorMin = Pt(0.42f, 0.5f - height / 2f), AnchorMax = Pt(0.58f, 0.5f + height / 2f) }, CursorEnabled = true }, "Overlay", BeastUi);
            float step = 1f / (rows + 1.3f);
            UiLabel(c, BeastUi, "<b>" + title + "</b>", 14, 0.04f, 1f - step * 1.2f, 0.96f, 0.98f, TextAnchor.MiddleCenter);
            string id = animal.net.ID.Value.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < choices.Count; i++)
            {
                float top = 1f - step * (1.3f + i);
                UiButton(c, BeastUi, choices[i][1], "bm.ui beast " + id + " " + choices[i][0], 0.06f, top - step * 0.86f, 0.94f, top, choices[i][0] == "ride" || choices[i][0] == "behind" ? ColOn : ColBtn, 13);
            }
            float last = 1f - step * (1.3f + choices.Count);
            UiButton(c, BeastUi, "Close", "bm.ui beast 0 close", 0.30f, last - step * 0.86f, 0.70f, last, ColOff, 12);
            CuiHelper.AddUi(player, c);
            beastMenus[player.userID] = new BeastMenu { Target = animal, Until = Time.time + 12f };
        }

        internal void CloseBeastMenu(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }
            if (beastMenus.Remove(player.userID) && player.IsConnected)
            {
                CuiHelper.DestroyUi(player, BeastUi);
            }
        }

        // A button of the menu: everything is checked again, the menu may be old by now.
        internal void BeastButton(BasePlayer player, string[] args)
        {
            BeastMenu menu;
            beastMenus.TryGetValue(player.userID, out menu);
            CloseBeastMenu(player);
            CuiHelper.DestroyUi(player, BeastUi);              // also a menu left over from before the plugin was switched off
            if (!Active)
            {
                return;
            }
            ulong id;
            if (args.Length < 3 || !ulong.TryParse(args[1], out id) || id == 0UL || menu == null || !Valid(menu.Target) || menu.Target.net.ID.Value != id)
            {
                return;
            }
            BaseNPC2 animal = menu.Target;
            string act = args[2].ToLowerInvariant();
            if (!UseAllowed(player) || Vector3.Distance(player.transform.position, animal.transform.position) > Cfg.Riding.MountDistance + 3f)
            {
                return;
            }
            if (!BeastChoices(player, animal).Any(c => c[0] == act))
            {
                return;
            }
            Driver host = DriverOfAnimal(animal);
            switch (act)
            {
                case "ride":
                    if (host != null && host.Kind == ControlKind.Form)
                    {
                        TryFormHandle(player, host, false);
                    }
                    else
                    {
                        StartControl(player, animal, ControlKind.Ride);
                    }
                    break;
                case "behind":
                    TryRideBehind(player, host);
                    break;
                case "inventory":
                    OpenInventory(player, animal);
                    break;
                case "lead":
                    TryFormHandle(player, host, true);
                    break;
                case "follow":
                case "wait":
                case "guard":
                    {
                        TamedRecord rec = RecordOf(animal);
                        if (rec != null)
                        {
                            rec.State = act;
                            ApplyState(animal, rec);
                            SpeciesDef sp = SpeciesOf(animal);
                            TellRaw(player, "The " + (sp != null ? sp.Display.ToLowerInvariant() : "animal") + (act == "follow" ? " follows you." : act == "wait" ? " waits here." : " guards you."));
                        }
                        break;
                    }
            }
        }

        // /bm bag: the inventory of the animal the player looks at; as an animal: his own.
        internal void BagCommand(BasePlayer player, Action<string> reply)
        {
            Driver d = DriverOf(player);
            if (d != null)
            {
                if (d.Kind == ControlKind.Form)
                {
                    OpenOwnInventory(d);
                }
                else
                {
                    reply("Get off first; then look at the animal and use /bm bag (or Ctrl + E).");
                }
                return;
            }
            BaseNPC2 animal = AnimalInSight(player, 6f, 30f);
            if (animal != null)
            {
                OpenInventory(player, animal);
                return;
            }
            RidableHorse horse = HorseInSight(player, 6f, 30f);
            if (horse != null)
            {
                if (!OpenEntityLoot(player, horse))
                {
                    reply("Hold E at the horse and choose its inventory.");
                }
                return;
            }
            reply("Look at an animal within 6 m.");
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
                CloseMenus();
            }

            public override void OnDisable()
            {
                Bm.StopAllFollowers();
                CloseMenus();
            }

            private void CloseMenus()
            {
                foreach (ulong id in Bm.beastMenus.Keys.ToList())
                {
                    BasePlayer p = BasePlayer.FindByID(id);
                    if (p != null)
                    {
                        Bm.CloseBeastMenu(p);
                    }
                }
                Bm.beastMenus.Clear();
                Bm.usePresses.Clear();
            }

            public override void OnEnable()
            {
                Bm.RestoreTamed();
            }

            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (input.WasJustPressed(BUTTON.USE))
                {
                    Bm.UsePressed(player);
                }
                else if (input.IsDown(BUTTON.USE))
                {
                    Bm.UseHeld(player);
                }
                if (input.WasJustReleased(BUTTON.USE))
                {
                    Bm.UseReleased(player);
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

            private float nextMenuCheck;

            // The menu of E held closes by itself: after a while, or when the animal is gone or far.
            public override void Tick(float now)
            {
                if (now < nextMenuCheck || Bm.beastMenus.Count == 0)
                {
                    return;
                }
                nextMenuCheck = now + 0.5f;
                foreach (KeyValuePair<ulong, BeastMenu> pair in Bm.beastMenus.ToList())
                {
                    BasePlayer p = BasePlayer.FindByID(pair.Key);
                    BeastMenu m = pair.Value;
                    if (p == null || !p.IsConnected || now > m.Until || !Valid(m.Target) || m.Target.IsDead() || Vector3.Distance(p.transform.position, m.Target.transform.position) > Bm.Cfg.Riding.MountDistance + 3f)
                    {
                        if (p != null)
                        {
                            Bm.CloseBeastMenu(p);
                        }
                        else
                        {
                            Bm.beastMenus.Remove(pair.Key);
                        }
                    }
                }
            }

            public override void OnPlayerDisconnected(BasePlayer player, string reason)
            {
                Bm.usePresses.Remove(player.userID);
                Bm.beastMenus.Remove(player.userID);
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
                    EnforceLook(e, species, rec.Genes != 0UL ? (int?)unchecked((int)(uint)rec.Genes) : null, rec.Name);
                }
                TagIndex[rec.Tag] = e;
                ApplyState(e, rec);
            }
        }
    }
}
