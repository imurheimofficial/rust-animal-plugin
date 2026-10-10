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
        internal void InteractFrom(Driver d)
        {
            BasePlayer player = d.Player;
            if (TryLeadToggle(d))
            {
                return;
            }
            if (!Cfg.Form.Doors && !Cfg.Form.Looting)
            {
                return;
            }
            Vector3 head = d.HeadPosition();
            Vector3 dir = Quaternion.Euler(10f, d.Heading, 0f) * Vector3.forward;
            var found = new List<BaseEntity>();
            Vis.Entities(head + dir * 1.1f, 2.4f, found);
            BaseEntity best = null;
            float bestScore = float.MaxValue;
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == d.Body || e == player)
                {
                    continue;
                }
                bool wanted = (Cfg.Form.Doors && e is Door) || (Cfg.Form.Looting && (e is StorageContainer || e is LootableCorpse || e is DroppedItemContainer || e is CollectibleEntity || e is WorldItem));
                if (!wanted)
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
                lying.RemoveItem();
                if (!AddToBag(player, picked))
                {
                    picked.Drop(player.transform.position + Vector3.up, Vector3.up);       // the bag is full
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
