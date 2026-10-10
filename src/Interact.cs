using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- interacting from the animal's head (third person)

        // The game skips the whole normal tick for a spectating player and refuses interactions (BasePlayer.CanInteract), so while the
        // plugin runs a vanilla method on behalf of the hidden body it lifts the spectating flag of the SERVER copy for the call only.
        // The client keeps its spectator view because the flag is restored before anything is sent.
        internal struct VanillaScope : IDisposable
        {
            private readonly BasePlayer player;
            private readonly bool wasSpectating;

            public VanillaScope(BasePlayer p)
            {
                player = p;
                wasSpectating = (p.playerFlags & BasePlayer.PlayerFlags.Spectating) != 0;
                if (wasSpectating)
                {
                    p.playerFlags &= ~BasePlayer.PlayerFlags.Spectating;
                }
            }

            public void Dispose()
            {
                if (wasSpectating && player != null)
                {
                    player.playerFlags |= BasePlayer.PlayerFlags.Spectating;
                }
            }
        }

        // Use key in the third-person animal form: the nearest door, container, corpse or pickup in front of the animal's head.
        internal void InteractFrom(Driver d, bool crouch)
        {
            BasePlayer player = d.Player;
            if (crouch)
            {
                OpenOwnInventory(d);                    // Ctrl + E: the animal's own inventory (saddle, shoes, saddle bag, armour)
                return;
            }
            if (TryLeadToggle(d))
            {
                return;
            }
            if (!Cfg.Form.Doors && !Cfg.Form.Looting)
            {
                TryEat(d);
                return;
            }
            BaseEntity best = InteractTarget(d);
            if (best == null)
            {
                TryEat(d);
                return;
            }
            WorldItem lying = best as WorldItem;
            if (lying != null && lying.item != null && StatsModule.OnDiet(d.Species, lying.item.info.shortname))
            {
                TryEat(d);                              // food on the ground is eaten, anything else is picked up
                return;
            }
            try
            {
                using (new VanillaScope(player))
                {
                    InteractWith(player, best);
                }
            }
            catch (Exception ex)
            {
                Log(0, "Interaction with " + best.ShortPrefabName + " failed: " + ex.Message);
            }
        }

        // The door, container, corpse or thing in front of the animal's head that E would use.
        internal BaseEntity InteractTarget(Driver d)
        {
            if (!Cfg.Form.Doors && !Cfg.Form.Looting)
            {
                return null;
            }
            Vector3 head = d.HeadPosition();
            Vector3 dir = Quaternion.Euler(10f, d.Heading, 0f) * Vector3.forward;
            var found = new List<BaseEntity>();
            Vis.Entities(head + dir * 1.1f, 2.4f, found);
            BaseEntity best = null;
            float bestScore = float.MaxValue;
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == d.Body || e == d.Player)
                {
                    continue;
                }
                bool wanted = (Cfg.Form.Doors && e is Door) || (Cfg.Form.Looting && (e is StorageContainer || e is LootableCorpse || e is DroppedItemContainer || e is CollectibleEntity || e is WorldItem));
                if (!wanted || IsSaddleModel(e))
                {
                    continue;
                }
                Vector3 to = e.ClosestPoint(head) - head;
                float dist = to.magnitude;
                if (dist > 2.6f)
                {
                    continue;
                }
                float angle = Vector3.Angle(dir, to);
                if (angle > 65f)
                {
                    continue;
                }
                float score = angle * 0.04f + dist;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }
            return best;
        }

        // What E would do right now, for the prompt on the screen of an animal form (null = nothing).
        internal string UsePrompt(Driver d)
        {
            if (d == null || d.Kind != ControlKind.Form || !d.Alive || d.Body == null)
            {
                return null;
            }
            string lead = LeadPrompt(d);
            if (lead != null)
            {
                return lead;
            }
            BaseEntity target = InteractTarget(d);
            if (target != null)
            {
                Door door = target as Door;
                if (door != null)
                {
                    if (!door.canHandOpen)
                    {
                        return null;
                    }
                    return door.IsLocked() ? "Locked" : door.IsOpen() ? "Close" : "Open";
                }
                if (target is LootableCorpse)
                {
                    return "Loot";
                }
                if (target is StorageContainer || target is DroppedItemContainer)
                {
                    return "Open";
                }
                if (target is CollectibleEntity)
                {
                    return "Pick up";
                }
                WorldItem lying = target as WorldItem;
                if (lying != null && lying.item != null)
                {
                    return StatsModule.OnDiet(d.Species, lying.item.info.shortname) ? "Eat " + lying.item.info.displayName.english : "Pick up " + lying.item.info.displayName.english;
                }
            }
            if (!Cfg.Form.Needs)
            {
                return null;
            }
            Vector3 fwd = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.forward;
            foreach (float reach in new[] { 0.5f, 1.2f, 2f })
            {
                if (WaterLevel.GetWaterDepth(d.Body.transform.position + fwd * reach, true, true, null) > 0.02f)
                {
                    return "Drink";
                }
            }
            return null;
        }

        private void InteractWith(BasePlayer player, BaseEntity entity)
        {
            Door door = entity as Door;
            if (door != null)
            {
                ToggleDoor(player, door);
                return;
            }
            StorageContainer container = entity as StorageContainer;
            if (container != null)
            {
                container.PlayerOpenLoot(player, string.Empty, false);
                return;
            }
            LootableCorpse corpse = entity as LootableCorpse;
            if (corpse != null)
            {
                OpenCorpse(player, corpse);
                return;
            }
            DroppedItemContainer dropped = entity as DroppedItemContainer;
            if (dropped != null)
            {
                dropped.PlayerOpenLoot(player);
                return;
            }
            CollectibleEntity collectible = entity as CollectibleEntity;
            if (collectible != null)
            {
                collectible.DoPickup(player);
                return;
            }
            WorldItem lying = entity as WorldItem;
            if (lying != null && lying.item != null && lying.allowPickup)
            {
                Item picked = lying.item;
                string pickedName = picked.info.displayName.english;
                Vector3 at = lying.transform.position;
                lying.RemoveItem();
                if (!lying.IsDestroyed)
                {
                    lying.Kill();                           // the item lies in the bag now, the empty world item goes away
                }
                string why;
                if (StoreForForm(DriverOf(player), picked, out why))
                {
                    TellRaw(player, "Picked up: " + pickedName + " (it is in your saddle bag; Ctrl + E opens your inventory).");
                }
                else
                {
                    picked.Drop(at + Vector3.up * 0.3f, Vector3.up);       // no saddle bag or no room: it stays where it lay
                    TellRaw(player, why);
                }
            }
        }

        // Opens or closes a door with exactly the rights of the human body: locks the player is authorised on open, others stay shut.
        private void ToggleDoor(BasePlayer player, Door door)
        {
            if (!door.canHandOpen || door.IsBusy() || door.IsLocked())
            {
                return;
            }
            BaseLock baseLock = door.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
            if (door.IsOpen())
            {
                if (baseLock == null || baseLock.OnTryToClose(player))
                {
                    door.SetFlagLocal(BaseEntity.Flags.Open, false);
                    door.SendNetworkUpdateImmediate();
                    Interface.CallHook("OnDoorClosed", door, player);
                }
                return;
            }
            if (baseLock != null && !baseLock.OnTryToOpen(player))
            {
                return;
            }
            door.SetFlagLocal(BaseEntity.Flags.Open, true);
            door.SendNetworkUpdateImmediate();
            Interface.CallHook("OnDoorOpened", door, player);
        }

        private void OpenCorpse(BasePlayer player, LootableCorpse corpse)
        {
            if (Interface.CallHook("CanLootEntity", player, corpse) != null || !player.inventory.loot.StartLootingEntity(corpse, false))
            {
                return;
            }
            corpse.SetFlagLocal(BaseEntity.Flags.Open, true);
            ItemContainer[] containers = corpse.containers;
            if (containers != null)
            {
                for (int i = 0; i < containers.Length; i++)
                {
                    player.inventory.loot.AddContainer(containers[i]);
                }
            }
            player.inventory.loot.SendImmediate();
            RpcTo(corpse, player, "RPC_ClientLootCorpse");
            corpse.SendNetworkUpdate();
        }
    }
}
