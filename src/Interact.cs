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

        // The door, container, corpse or thing right in front of the animal's head that E would use. It has to be close to the head (not a
        // door further in behind a window), in front of it, and nothing solid may stand between the head and it (no doors through walls).
        internal BaseEntity InteractTarget(Driver d)
        {
            if (!Cfg.Form.Doors && !Cfg.Form.Looting)
            {
                return null;
            }
            Vector3 head = d.HeadPosition();
            Vector3 flatDir = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.forward;
            float headHeight = Mathf.Max(0.3f, head.y - d.Body.transform.position.y);
            int blockers = LayerMask.GetMask("Construction", "Deployed", "World", "Terrain", "Default", "Tree");
            BaseEntity aimed = AimedTarget(d, head, headHeight, blockers);
            if (aimed != null)
            {
                return aimed;                                  // what the crosshair points at next to the animal, whichever way the animal faces
            }
            const float reach = 1.5f;
            var found = new List<BaseEntity>();
            Vis.Entities(head + flatDir * (reach * 0.5f), reach + 0.5f, found);
            BaseEntity best = null;
            float bestScore = float.MaxValue;
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == d.Body || e == d.Player || !InteractWanted(e))
                {
                    continue;
                }
                Vector3 point = e.ClosestPoint(head);
                Vector3 to = point - head;
                float flat = new Vector2(to.x, to.z).magnitude;
                if (flat > reach || to.y > 0.8f || to.y < -(headHeight + 0.3f))
                {
                    continue;                                  // too far from the head (or high above it, or below the ground)
                }
                Vector3 flatTo = new Vector3(to.x, 0f, to.z);
                float angle = flatTo.sqrMagnitude < 0.0025f ? 0f : Vector3.Angle(flatDir, flatTo);
                if (angle > 50f)
                {
                    continue;                                  // beside or behind the head
                }
                // nothing solid between the head and the thing: a wall, a window frame, a floor
                if (InteractBlocked(d, head - flatDir * 0.25f, point, e, blockers))
                {
                    continue;
                }
                float score = angle * 0.03f + flat;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }
            return best;
        }

        private bool InteractWanted(BaseEntity e)
        {
            bool wanted = (Cfg.Form.Doors && e is Door) || (Cfg.Form.Looting && (e is StorageContainer || e is LootableCorpse || e is DroppedItemContainer || e is CollectibleEntity || e is WorldItem));
            return wanted && !IsSaddleModel(e);
        }

        private static bool InteractBlocked(Driver d, Vector3 from, Vector3 point, BaseEntity e, int blockers)
        {
            RaycastHit hit;
            if (!Physics.Linecast(from, point, out hit, blockers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            BaseEntity blocker = hit.GetEntity();
            return blocker != e && (blocker == null || (blocker.GetParentEntity() != e && blocker != d.Body)) && Vector3.Distance(hit.point, point) > 0.15f;
        }

        // The thing the camera points at, close to the animal on any side (a door behind it, a box beside it), in sight of its head.
        private BaseEntity AimedTarget(Driver d, Vector3 head, float headHeight, int blockers)
        {
            Vector3 dir = d.CameraForward();
            if (dir.sqrMagnitude < 0.01f)
            {
                return null;
            }
            dir.Normalize();
            Vector3 center = d.Body.transform.position + Vector3.up * Mathf.Max(0.3f, headHeight * 0.7f);
            Vector3 cam = center - dir * 2.5f + Vector3.up * 0.4f;            // about where the camera hangs behind and above the animal
            var found = new List<BaseEntity>();
            Vis.Entities(center, 3f, found);
            BaseEntity best = null;
            float bestAngle = 12f;
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == d.Body || e == d.Player || !InteractWanted(e))
                {
                    continue;
                }
                float along = Mathf.Max(0.5f, Vector3.Dot(e.transform.position - cam, dir));
                Vector3 point = e.ClosestPoint(cam + dir * along);
                if (Vector3.Distance(d.Body.ClosestPoint(point), point) > 1.4f || point.y > head.y + 1.2f || point.y < d.Body.transform.position.y - 0.6f)
                {
                    continue;                                  // not next to the animal
                }
                float angle = Vector3.Angle(dir, point - cam);
                if (angle >= bestAngle || InteractBlocked(d, head, point, e, blockers))
                {
                    continue;
                }
                bestAngle = angle;
                best = e;
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
