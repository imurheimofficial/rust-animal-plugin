using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Game.Rust.Cui;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- orders to a tamed animal
        // Control (hold E at the animal, the button turns yellow) puts the animal under the player's control: whenever he looks at it from up to
        // 50 m, the prompt "E Control" shows, and E opens the wheel of orders: Follow, Wait, Defend, Loot. The button toggles it off again.
        //   Follow  - it follows the player.
        //   Wait    - it stops where it is.
        //   Defend  - it runs at whoever hits the player, and at whoever the player hits.
        //   Loot    - within 25 m it finds things to take (things lying about, crates, barrels, dropped bags, corpses), takes the items, gives
        //             them to the player's inventory and, when it is full, brings them and drops them in front of the player.
        // A player who hits his own tamed animal lets it go: it is wild again and may fight back or run.

        private const string WheelUi = "BeastmasterWheel";
        private const float OrderRange = 50f;
        private const float LootRange = 25f;

        private sealed class WheelState
        {
            public BaseNPC2 Target;
            public float Until;
        }

        private readonly Dictionary<ulong, WheelState> wheels = new Dictionary<ulong, WheelState>();
        private readonly Dictionary<ulong, HashSet<ulong>> controlled = new Dictionary<ulong, HashSet<ulong>>();      // player -> net ids of the animals he controls

        internal bool IsControlled(BasePlayer player, BaseEntity animal)
        {
            HashSet<ulong> set;
            return player != null && animal != null && animal.net != null && controlled.TryGetValue(player.userID, out set) && set.Contains(animal.net.ID.Value);
        }

        internal void ToggleControl(BasePlayer player, BaseNPC2 animal)
        {
            HashSet<ulong> set;
            if (!controlled.TryGetValue(player.userID, out set))
            {
                set = new HashSet<ulong>();
                controlled[player.userID] = set;
            }
            SpeciesDef sp = SpeciesOf(animal);
            string who = sp != null ? sp.Display.ToLowerInvariant() : "animal";
            ulong id = animal.net.ID.Value;
            if (!set.Add(id))
            {
                set.Remove(id);
                TellRaw(player, "You no longer control the " + who + ".");
                return;
            }
            TellRaw(player, "You control the " + who + ": look at it from up to " + (int)OrderRange + " m and press E for its orders.");
        }

        private static bool LookingAt(BasePlayer player, BaseEntity animal, float maxAngle)
        {
            Vector3 eye = player.eyes.position;
            Vector3 dir = player.eyes.HeadForward();
            Bounds b = animal.WorldSpaceBounds().ToBounds();
            Vector3 to = b.center - eye;
            Vector3 edge = b.ClosestPoint(eye + dir * Mathf.Max(0.5f, to.magnitude)) - eye;
            return Mathf.Min(Vector3.Angle(dir, to), Vector3.Angle(dir, edge)) <= maxAngle;
        }

        // The animal this player controls, when it is within 50 m and he looks at it.
        internal BaseNPC2 ControlledAnimalInSight(BasePlayer player)
        {
            HashSet<ulong> set;
            if (controlled.Count == 0 || !controlled.TryGetValue(player.userID, out set) || set.Count == 0)
            {
                return null;
            }
            BaseNPC2 best = null;
            float bestDist = float.MaxValue;
            foreach (ulong id in set.ToList())
            {
                BaseNPC2 animal = BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseNPC2;
                TamedRecord rec = Valid(animal) && !animal.IsDead() ? RecordOf(animal) : null;
                if (rec == null || !MayUse(player, rec.Owner, "team"))
                {
                    set.Remove(id);                          // gone, dead or not tame any more
                    continue;
                }
                float dist = Vector3.Distance(player.transform.position, animal.transform.position);
                if (DriverOfAnimal(animal) == null && dist <= OrderRange && dist < bestDist && LookingAt(player, animal, 8f))
                {
                    bestDist = dist;
                    best = animal;
                }
            }
            return best;
        }

        // The prompt "E Control" for a player who looks at an animal he controls from afar (near it, E rides it and hold E has the menu).
        internal bool ControlPromptFor(BasePlayer player)
        {
            return ControlledAnimalInSight(player) != null;
        }

        internal void ShowWheel(BasePlayer player, BaseNPC2 animal)
        {
            CloseWheel(player);
            TamedRecord rec = RecordOf(animal);
            if (rec == null || animal.net == null)
            {
                return;
            }
            SpeciesDef species = SpeciesOf(animal);
            string id = animal.net.ID.Value.ToString(CultureInfo.InvariantCulture);
            const float size = 210f, bw = 76f, bh = 30f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.6" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = Pt(-size / 2f, -size / 2f), OffsetMax = Pt(size / 2f, size / 2f) },
                CursorEnabled = true
            }, "Overlay", WheelUi);
            string name = !string.IsNullOrEmpty(rec.Name) ? rec.Name : species != null ? species.Display : "Animal";
            c.Add(new CuiLabel
            {
                Text = { Text = "<b>" + name + "</b>", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "0.92 0.93 0.88 1" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = Pt(-size / 2f + 8f, size / 2f - 24f), OffsetMax = Pt(size / 2f - 30f, size / 2f - 4f) }
            }, WheelUi);
            string[][] orders =
            {
                new[] { "follow", "Follow", "0", "62" },
                new[] { "wait", "Wait", "58", "0" },
                new[] { "guard", "Defend", "0", "-62" },
                new[] { "loot", "Loot", "-58", "0" },
            };
            foreach (string[] o in orders)
            {
                float cx = float.Parse(o[2], CultureInfo.InvariantCulture), cy = float.Parse(o[3], CultureInfo.InvariantCulture);
                c.Add(new CuiButton
                {
                    Button = { Command = "bm.ui wheel " + id + " " + o[0], Color = rec.State == o[0] ? ColOn : ColBtn },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = Pt(cx - bw / 2f, cy - bh / 2f), OffsetMax = Pt(cx + bw / 2f, cy + bh / 2f) },
                    Text = { Text = o[1], FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, WheelUi);
            }
            c.Add(new CuiButton
            {
                Button = { Command = "bm.ui wheel 0 close", Color = ColOff },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = Pt(size / 2f - 26f, size / 2f - 26f), OffsetMax = Pt(size / 2f - 6f, size / 2f - 6f) },
                Text = { Text = "x", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, WheelUi);
            CuiHelper.AddUi(player, c);
            wheels[player.userID] = new WheelState { Target = animal, Until = Time.time + 15f };
        }

        internal void CloseWheel(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }
            if (wheels.Remove(player.userID) && player.IsConnected)
            {
                CuiHelper.DestroyUi(player, WheelUi);
            }
        }

        // A button of the wheel: everything is checked again, the wheel may be old by now.
        internal void WheelButton(BasePlayer player, string[] args)
        {
            WheelState wheel;
            wheels.TryGetValue(player.userID, out wheel);
            CloseWheel(player);
            CuiHelper.DestroyUi(player, WheelUi);
            ulong id;
            if (!Active || args.Length < 3 || !ulong.TryParse(args[1], out id) || id == 0UL || wheel == null || !Valid(wheel.Target) || wheel.Target.net.ID.Value != id)
            {
                return;
            }
            string act = args[2].ToLowerInvariant();
            if (act == "follow" || act == "wait" || act == "guard" || act == "loot")
            {
                GiveOrder(player, wheel.Target, act);
            }
        }

        internal void GiveOrder(BasePlayer player, BaseNPC2 animal, string order)
        {
            TamedRecord rec = RecordOf(animal);
            if (rec == null || !MayUse(player, rec.Owner, "team") || DriverOfAnimal(animal) != null || !UseAllowed(player)
                || Vector3.Distance(player.transform.position, animal.transform.position) > OrderRange + 3f)
            {
                return;
            }
            rec.State = order;
            ApplyState(animal, rec);
            SpeciesDef sp = SpeciesOf(animal);
            string who = "The " + (sp != null ? sp.Display.ToLowerInvariant() : "animal");
            TellRaw(player, who + (order == "follow" ? " follows you." : order == "wait" ? " waits here." : order == "guard" ? " defends you." : " looks for things to take."));
        }

        // ---------------------------------------------------------------- defending

        private sealed class DefendTarget
        {
            public BaseCombatEntity Target;
            public float Until;
        }

        private readonly Dictionary<ulong, DefendTarget> defendTargets = new Dictionary<ulong, DefendTarget>();     // owner -> whom his defenders go for

        internal BaseCombatEntity DefendTargetOf(ulong owner)
        {
            DefendTarget t;
            if (defendTargets.Count == 0 || !defendTargets.TryGetValue(owner, out t))
            {
                return null;
            }
            if (t.Target == null || t.Target.IsDestroyed || t.Target.IsDead() || Time.time > t.Until)
            {
                defendTargets.Remove(owner);
                return null;
            }
            return t.Target;
        }

        private bool HasDefenders(ulong owner)
        {
            foreach (TamedRecord rec in Data.Tamed.Values)
            {
                if (rec.Owner == owner && rec.State == "guard")
                {
                    return true;
                }
            }
            return false;
        }

        // Does the player's side count this one as his (himself, his team, his animals)? Those are never attacked by the defenders.
        private bool OnSideOf(ulong owner, BaseCombatEntity e)
        {
            BasePlayer p = e as BasePlayer;
            if (p != null)
            {
                BasePlayer o = BasePlayer.FindByID(owner);
                return p.userID == owner || (o != null && SameParty(o, p));
            }
            TamedRecord rec = RecordOf(e);
            return rec != null && rec.Owner == owner;
        }

        private void NoteDefence(ulong owner, BaseCombatEntity target)
        {
            if (target == null || !(target is BasePlayer || target is BaseNPC2 || target is RidableHorse || target is BaseAnimalNPC) || !Alive(target)
                || OnSideOf(owner, target) || !HasDefenders(owner))
            {
                return;
            }
            defendTargets[owner] = new DefendTarget { Target = target, Until = Time.time + 25f };
        }

        // ---------------------------------------------------------------- looting

        private sealed class LootTrip
        {
            public BaseEntity Target;
            public float Until;
            public float NextScan;
            public float ClearTried;
            public readonly HashSet<ulong> Tried = new HashSet<ulong>();
            public readonly List<Item> Carried = new List<Item>();
        }

        private readonly Dictionary<ulong, LootTrip> lootTrips = new Dictionary<ulong, LootTrip>();      // tag of the tamed animal -> its trip

        // What the animal drops when the order changes (it carried something that did not fit into the player's inventory).
        internal void EndLootTrip(ulong tag, BaseNPC2 npc)
        {
            LootTrip trip;
            if (!lootTrips.TryGetValue(tag, out trip))
            {
                return;
            }
            lootTrips.Remove(tag);
            Vector3 at = Valid(npc) ? npc.transform.position : Vector3.zero;
            foreach (Item it in trip.Carried)
            {
                if (it != null && Valid(npc))
                {
                    it.Drop(at + Vector3.up * 0.5f, Vector3.up);
                }
            }
        }

        private static readonly string[] LootBoxWords = { "crate", "barrel", "foodbox", "trash", "loot" };

        private static bool LootWorthy(BaseEntity e)
        {
            if (!Valid(e) || e.IsDestroyed)
            {
                return false;
            }
            WorldItem lying = e as WorldItem;
            if (lying != null)
            {
                return lying.item != null && lying.allowPickup;
            }
            if (e is CollectibleEntity)
            {
                return true;
            }
            DroppedItemContainer dropped = e as DroppedItemContainer;
            if (dropped != null)
            {
                return dropped.inventory != null && dropped.inventory.itemList.Count > 0;
            }
            LootableCorpse corpse = e as LootableCorpse;
            if (corpse != null)
            {
                return corpse.containers != null && corpse.containers.Any(x => x != null && x.itemList.Count > 0);
            }
            StorageContainer box = e as StorageContainer;
            if (box != null && box.OwnerID == 0UL && box.inventory != null && box.inventory.itemList.Count > 0 && !box.IsLocked())
            {
                string name = box.ShortPrefabName ?? string.Empty;
                return LootBoxWords.Any(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return false;
        }

        private BaseEntity FindLootTarget(BaseNPC2 npc, BasePlayer owner, LootTrip trip)
        {
            var found = new List<BaseEntity>();
            Vis.Entities(npc.transform.position, LootRange, found);
            BaseEntity best = null;
            float bestDist = float.MaxValue;
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e.net == null || trip.Tried.Contains(e.net.ID.Value) || !LootWorthy(e))
                {
                    continue;
                }
                if (Vector3.Distance(e.transform.position, owner.transform.position) > OrderRange - 5f)
                {
                    continue;
                }
                float d = Vector3.Distance(e.transform.position, npc.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }
            return best;
        }

        // Where the looting animal goes now (null = it stands). Called by its follower every few tenths of a second; takes the things as well.
        internal Vector3? LootDestination(BaseNPC2 npc, ulong ownerId, ulong tag)
        {
            BasePlayer owner = BasePlayer.FindByID(ownerId);
            if (!Valid(npc) || npc.IsDead() || owner == null || !owner.IsConnected || owner.IsDead())
            {
                return null;
            }
            LootTrip trip;
            if (!lootTrips.TryGetValue(tag, out trip))
            {
                trip = new LootTrip { ClearTried = Time.time + 90f };
                lootTrips[tag] = trip;
            }
            if (Time.time > trip.ClearTried)
            {
                trip.Tried.Clear();
                trip.ClearTried = Time.time + 90f;
            }
            Vector3 pos = npc.transform.position;
            Vector3 ownerPos = owner.transform.position;
            if (trip.Carried.Count > 0)
            {
                // something did not fit into the inventory: it is brought to the player and dropped in front of him
                if (Vector3.Distance(pos, ownerPos) <= 3.5f)
                {
                    Vector3 front = owner.eyes.HeadForward();
                    front.y = 0f;
                    Vector3 spot = ownerPos + (front.sqrMagnitude > 0.01f ? front.normalized : Vector3.forward) * 1.4f + Vector3.up * 0.8f;
                    int dropped = 0;
                    foreach (Item it in trip.Carried.ToList())
                    {
                        if (it != null && (it.MoveToContainer(owner.inventory.containerMain) || it.MoveToContainer(owner.inventory.containerBelt)))
                        {
                            dropped++;
                            continue;
                        }
                        if (it != null)
                        {
                            it.Drop(spot, Vector3.up * 1.5f);
                            dropped++;
                        }
                    }
                    trip.Carried.Clear();
                    TellRaw(owner, "Your animal brought you " + dropped + " item(s).");
                }
                return ownerPos;
            }
            if (trip.Target != null && (!LootWorthy(trip.Target) || Time.time > trip.Until))
            {
                if (trip.Target != null && trip.Target.net != null)
                {
                    trip.Tried.Add(trip.Target.net.ID.Value);
                }
                trip.Target = null;
            }
            if (trip.Target == null && Time.time >= trip.NextScan)
            {
                trip.NextScan = Time.time + 1.5f;
                trip.Target = FindLootTarget(npc, owner, trip);
                trip.Until = Time.time + 25f;
            }
            if (trip.Target != null)
            {
                Vector3 at = trip.Target.transform.position;
                if (Vector3.Distance(pos, at) <= 2.8f || Vector3.Distance(trip.Target.ClosestPoint(pos), pos) <= 2.2f)
                {
                    TakeLoot(owner, npc, trip);
                    return ownerPos;
                }
                return at;
            }
            return Vector3.Distance(pos, ownerPos) > 6f ? (Vector3?)ownerPos : null;      // nothing to take: it stays close to the player
        }

        private void TakeLoot(BasePlayer owner, BaseNPC2 npc, LootTrip trip)
        {
            BaseEntity target = trip.Target;
            trip.Target = null;
            if (target == null || target.net == null)
            {
                return;
            }
            trip.Tried.Add(target.net.ID.Value);
            int taken = 0;
            CollectibleEntity collectible = target as CollectibleEntity;
            if (collectible != null)
            {
                collectible.DoPickup(owner);
                TellRaw(owner, "Your animal picked something up for you.");
                return;
            }
            var items = new List<Item>();
            WorldItem lying = target as WorldItem;
            if (lying != null)
            {
                if (lying.item != null)
                {
                    Item picked = lying.item;
                    lying.RemoveItem();
                    if (!lying.IsDestroyed)
                    {
                        lying.Kill();
                    }
                    items.Add(picked);
                }
            }
            else
            {
                var boxes = new List<ItemContainer>();
                DroppedItemContainer dropped = target as DroppedItemContainer;
                LootableCorpse corpse = target as LootableCorpse;
                StorageContainer box = target as StorageContainer;
                if (dropped != null && dropped.inventory != null)
                {
                    boxes.Add(dropped.inventory);
                }
                else if (corpse != null && corpse.containers != null)
                {
                    boxes.AddRange(corpse.containers.Where(x => x != null));
                }
                else if (box != null && box.inventory != null)
                {
                    boxes.Add(box.inventory);
                }
                foreach (ItemContainer container in boxes)
                {
                    foreach (Item it in container.itemList.ToList())
                    {
                        it.RemoveFromContainer();
                        items.Add(it);
                    }
                }
            }
            foreach (Item it in items)
            {
                taken++;
                if (!it.MoveToContainer(owner.inventory.containerMain) && !it.MoveToContainer(owner.inventory.containerBelt))
                {
                    trip.Carried.Add(it);                  // no room: the animal brings it
                }
            }
            if (taken > 0)
            {
                SpeciesDef sp = SpeciesOf(npc);
                TellRaw(owner, "Your " + (sp != null ? sp.Display.ToLowerInvariant() : "animal") + " took " + taken + " item(s)" + (trip.Carried.Count > 0 ? " - " + trip.Carried.Count + " did not fit, it brings them to you." : " and gave them to you."));
            }
        }

        // ---------------------------------------------------------------- hooks

        internal sealed class OrdersModule : Module
        {
            public override int Order => 63;
            private float nextCheck;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Subscribe("OnPlayerAttack");
            }

            // Whoever sits on an animal never hits it (no blood, no sparks, no damage on his own seat): a weapon in his hands hit the mount.
            public override object OnPlayerAttack(BasePlayer attacker, HitInfo info)
            {
                if (attacker == null || info == null || !attacker.isMounted)
                {
                    return null;
                }
                BaseMountable seat = attacker.GetMounted();
                BaseEntity hit = info.HitEntity;
                if (seat == null || hit == null)
                {
                    return null;
                }
                BaseEntity mount = seat.GetParentEntity();
                return hit == seat || (mount != null && (hit == mount || hit.GetParentEntity() == mount)) ? (object)true : null;
            }

            public override void Tick(float now)
            {
                if (Bm.wheels.Count == 0 || now < nextCheck)
                {
                    return;
                }
                nextCheck = now + 0.5f;
                foreach (KeyValuePair<ulong, WheelState> kv in Bm.wheels.ToList())
                {
                    BasePlayer p = BasePlayer.FindByID(kv.Key);
                    if (p == null || !p.IsConnected || now > kv.Value.Until || !Valid(kv.Value.Target))
                    {
                        if (p != null)
                        {
                            Bm.CloseWheel(p);
                        }
                        else
                        {
                            Bm.wheels.Remove(kv.Key);
                        }
                    }
                }
            }

            public override void OnDisable()
            {
                foreach (ulong id in Bm.wheels.Keys.ToList())
                {
                    BasePlayer p = BasePlayer.FindByID(id);
                    if (p != null)
                    {
                        Bm.CloseWheel(p);
                    }
                }
                Bm.wheels.Clear();
                Bm.defendTargets.Clear();
                Bm.controlled.Clear();
            }

            public override void Unload()
            {
                OnDisable();
            }

            // Hurting one's own tamed animal lets it go; the defenders learn who hit the owner and whom the owner hit.
            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                if (entity == null || info == null || !Bm.Active || Bm.Data.Tamed.Count == 0)
                {
                    return null;
                }
                BasePlayer attacker = info.InitiatorPlayer;
                if (attacker != null && attacker.isMounted && entity is BaseNPC2)
                {
                    BaseMountable sitting = attacker.GetMounted();
                    if (sitting != null && sitting.GetParentEntity() == entity)
                    {
                        return true;                       // a rider cannot hurt the animal he sits on
                    }
                }
                TamedRecord rec = Bm.RecordOf(entity);
                if (rec != null && attacker != null && attacker.userID == rec.Owner && Bm.DriverOfAnimal(entity) == null)
                {
                    Item held = attacker.GetActiveItem();
                    if (held == null || held.info.shortname != "bone.fragments")
                    {
                        BaseCombatEntity animal = entity;
                        BasePlayer owner = attacker;
                        Bm.NextTick(() =>
                        {
                            if (Valid(animal) && Bm.RecordOf(animal) != null)
                            {
                                Bm.ReleaseAnimal(animal);
                                Bm.TellRaw(owner, "You hit your animal: it is not tame any more.");
                            }
                        });
                    }
                    return null;
                }
                BasePlayer victim = entity as BasePlayer;
                if (victim != null && !victim.IsNpc)
                {
                    BaseCombatEntity hitter = info.Initiator as BaseCombatEntity;
                    if (hitter != null && hitter != victim)
                    {
                        Bm.NoteDefence(victim.userID, hitter);
                    }
                }
                if (attacker != null && !attacker.IsNpc && entity != attacker)
                {
                    Bm.NoteDefence(attacker.userID, entity);
                }
                return null;
            }
        }
    }
}
