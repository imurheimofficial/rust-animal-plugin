using System;
using System.Collections.Generic;
using System.Linq;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- virtual containers
        // A real loot panel for something that has no container of its own (an animal's gear, a sleeping deer): a wooden box is spawned
        // under the player, filled, opened and destroyed again when the panel closes. The plugin decides what may go in and what happens
        // to the contents.

        internal sealed class VirtualBox
        {
            public StorageContainer Box;
            public BasePlayer Player;
            public string Kind;
            public object Data;
            public bool Filling;
            public bool Done;
            public Func<VirtualBox, Item, int, bool> Accept;
            public Action<VirtualBox, Item> Added;
            public Action<VirtualBox> Closed;
        }

        private const string BoxPrefab = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab";
        internal readonly Dictionary<ItemContainer, VirtualBox> Boxes = new Dictionary<ItemContainer, VirtualBox>();

        internal VirtualBox OpenBox(BasePlayer player, string kind, int slots, List<KeyValuePair<int, ItemRecord>> contents, object data,
            Func<VirtualBox, Item, int, bool> accept, Action<VirtualBox, Item> added, Action<VirtualBox> closed)
        {
            foreach (VirtualBox old in Boxes.Values.Where(b => b.Player == player).ToList())
            {
                FinishBox(old);
            }
            StorageContainer box = GameManager.server.CreateEntity(BoxPrefab, player.transform.position + Vector3.down * 2.5f, Quaternion.identity) as StorageContainer;
            if (box == null)
            {
                return null;
            }
            box.enableSaving = false;
            box.Spawn();
            box.inventorySlots = slots;
            box.inventory.capacity = slots;
            var vb = new VirtualBox { Box = box, Player = player, Kind = kind, Data = data, Accept = accept, Added = added, Closed = closed, Filling = true };
            Boxes[box.inventory] = vb;
            if (contents != null)
            {
                foreach (KeyValuePair<int, ItemRecord> pair in contents)
                {
                    Item item = ItemFromRecord(pair.Value);
                    if (item != null && !item.MoveToContainer(box.inventory, pair.Key, true))
                    {
                        item.Remove();
                    }
                }
            }
            vb.Filling = false;
            if (!box.PlayerOpenLoot(player, box.panelName, false))
            {
                FinishBox(vb, false);
                return null;
            }
            return vb;
        }

        // Ends a panel: the owner of the box decides about the contents, then the box disappears.
        internal void FinishBox(VirtualBox vb, bool callClosed = true)
        {
            if (vb == null || vb.Done)
            {
                return;
            }
            vb.Done = true;
            if (callClosed && vb.Closed != null)
            {
                try
                {
                    vb.Closed(vb);
                }
                catch (Exception ex)
                {
                    Log(0, "Closing the " + vb.Kind + " panel failed: " + ex);
                }
            }
            if (vb.Box != null && vb.Box.inventory != null)
            {
                Boxes.Remove(vb.Box.inventory);
            }
            if (Valid(vb.Box))
            {
                vb.Box.Kill();
            }
        }

        internal static ItemRecord RecordFromItem(Item item, int slot)
        {
            return new ItemRecord { Shortname = item.info.shortname, Amount = item.amount, Condition = item.hasCondition ? item.condition : -1f, Skin = item.skin, Slot = slot };
        }

        internal static Item ItemFromRecord(ItemRecord rec)
        {
            Item item = ItemManager.CreateByName(rec.Shortname, Mathf.Max(1, rec.Amount), rec.Skin);
            if (item != null && rec.Condition >= 0f && item.hasCondition)
            {
                item.condition = Mathf.Min(rec.Condition, item.maxCondition);
            }
            return item;
        }

        // ---------------------------------------------------------------- the gear panel of an owned animal (E, or crouch + E)
        // Slot 0 = saddle (single or double), slot 1 = horseshoes, slot 2 = saddle bag, everything after that is the bag's own storage.

        private const int GearFixedSlots = 3;

        internal void OpenGear(BasePlayer player, BaseEntity animal, TamedRecord rec)
        {
            if (!Cfg.Features.Gear)
            {
                Tell(player, "FeatureOff");
                return;
            }
            SpeciesDef species = SpeciesOf(animal);
            if (species == null || !species.Gear)
            {
                return;
            }
            if (Cfg.Gear.BagPrivate && rec.Owner != player.userID && !MayUse(player, rec.Owner, "team"))
            {
                Tell(player, "NotYours");
                return;
            }
            var contents = new List<KeyValuePair<int, ItemRecord>>();
            if (rec.Gear.Saddle > 0)
            {
                contents.Add(new KeyValuePair<int, ItemRecord>(0, new ItemRecord { Shortname = rec.Gear.Saddle == 2 ? "horse.saddle.double" : "horse.saddle.single" }));
            }
            if (rec.Gear.Shoes > 0)
            {
                contents.Add(new KeyValuePair<int, ItemRecord>(1, new ItemRecord { Shortname = rec.Gear.Shoes == 2 ? "horse.shoes.advanced" : "horse.shoes.basic" }));
            }
            if (rec.Gear.HasBag)
            {
                contents.Add(new KeyValuePair<int, ItemRecord>(2, new ItemRecord { Shortname = "horse.saddlebag" }));
                foreach (ItemRecord bag in rec.Gear.Bag)
                {
                    contents.Add(new KeyValuePair<int, ItemRecord>(GearFixedSlots + Mathf.Max(0, bag.Slot), bag));
                }
            }
            int slots = GearFixedSlots + Cfg.Gear.BagSlots;
            ulong tag = rec.Tag;
            VirtualBox vb = OpenBox(player, "gear", slots, contents, tag,
                GearAccept,
                null,
                GearClosed);
            if (vb != null)
            {
                Tell(player, "GearOpen", species.Display);
            }
        }

        private bool GearAccept(VirtualBox vb, Item item, int slot)
        {
            string name = item.info.shortname;
            if (slot < 0)
            {
                return false;
            }
            switch (slot)
            {
                case 0: return name == "horse.saddle.single" || name == "horse.saddle.double";
                case 1: return name == "horse.shoes.basic" || name == "horse.shoes.advanced";
                case 2: return name == "horse.saddlebag";
                default:
                    {
                        Item bag = vb.Box.inventory.GetSlot(2);
                        return bag != null && bag.info.shortname == "horse.saddlebag" && item != bag;
                    }
            }
        }

        private void GearClosed(VirtualBox vb)
        {
            TamedRecord rec;
            if (!Data.Tamed.TryGetValue((ulong)vb.Data, out rec))
            {
                ReturnAll(vb);
                return;
            }
            ItemContainer inv = vb.Box.inventory;
            Item saddle = inv.GetSlot(0), shoes = inv.GetSlot(1), bagItem = inv.GetSlot(2);
            rec.Gear.Saddle = saddle == null ? 0 : saddle.info.shortname == "horse.saddle.double" ? 2 : 1;
            rec.Gear.Shoes = shoes == null ? 0 : shoes.info.shortname == "horse.shoes.advanced" ? 2 : 1;
            rec.Gear.HasBag = bagItem != null;
            rec.Gear.Bag.Clear();
            var leftovers = new List<Item>();
            foreach (Item item in inv.itemList.ToList())
            {
                if (item.position < GearFixedSlots)
                {
                    continue;
                }
                if (rec.Gear.HasBag)
                {
                    rec.Gear.Bag.Add(RecordFromItem(item, item.position - GearFixedSlots));
                }
                else
                {
                    leftovers.Add(item);     // the bag was taken out: its contents go back to the player
                }
            }
            foreach (Item item in leftovers)
            {
                GiveBack(vb.Player, item);
            }
            // the gear items themselves are stored in the record; they are made again when the panel opens
            foreach (Item fixedItem in new[] { saddle, shoes, bagItem })
            {
                if (fixedItem != null)
                {
                    fixedItem.Remove();
                }
            }
            foreach (Item rest in inv.itemList.ToList())
            {
                rest.Remove();
            }
            Tell(vb.Player, "GearSaved");
        }

        private void ReturnAll(VirtualBox vb)
        {
            foreach (Item item in vb.Box.inventory.itemList.ToList())
            {
                GiveBack(vb.Player, item);
            }
        }

        internal static void GiveBack(BasePlayer player, Item item)
        {
            if (player == null || !player.IsConnected || !item.MoveToContainer(player.inventory.containerMain))
            {
                item.Drop(player != null ? player.transform.position + Vector3.up : Vector3.zero, Vector3.up);
            }
        }

        // Dying animals drop what they carried (R3.5).
        internal void DropGear(BaseEntity animal, TamedRecord rec)
        {
            Vector3 pos = animal.transform.position + Vector3.up * 0.6f;
            var names = new List<string>();
            if (rec.Gear.Saddle > 0) names.Add(rec.Gear.Saddle == 2 ? "horse.saddle.double" : "horse.saddle.single");
            if (rec.Gear.Shoes > 0) names.Add(rec.Gear.Shoes == 2 ? "horse.shoes.advanced" : "horse.shoes.basic");
            if (rec.Gear.HasBag) names.Add("horse.saddlebag");
            foreach (string name in names)
            {
                Item item = ItemManager.CreateByName(name, 1);
                if (item != null)
                {
                    item.Drop(pos, Vector3.up * 2f + UnityEngine.Random.insideUnitSphere);
                }
            }
            foreach (ItemRecord bag in rec.Gear.Bag)
            {
                Item item = ItemFromRecord(bag);
                if (item != null)
                {
                    item.Drop(pos, Vector3.up * 2f + UnityEngine.Random.insideUnitSphere);
                }
            }
            rec.Gear = new GearRecord();
        }

        // Speed change from the animal's gear (shoes add speed, the saddle bag costs some), like the horse's equipmentSpeedMod.
        internal float GearSpeedBonus(BaseNPC2 animal)
        {
            TamedRecord rec = RecordOf(animal);
            if (rec == null)
            {
                return 0f;
            }
            float bonus = rec.Gear.Shoes == 2 ? Cfg.Movement.ShoesAdvanced : rec.Gear.Shoes == 1 ? Cfg.Movement.ShoesBasic : 0f;
            if (rec.Gear.HasBag)
            {
                bonus += Cfg.Movement.SaddleBag;
            }
            return bonus;
        }

        // ---------------------------------------------------------------- hooks

        internal sealed class GearModule : Module
        {
            public override int Order => 40;

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["GearOpen"] = "{0} gear: slot 1 = saddle, slot 2 = horseshoes, slot 3 = saddle bag, the other slots are the bag. Close the panel to save.",
                    ["GearSaved"] = "Gear saved.",
                };
            }

            public override object CanAcceptItem(ItemContainer container, Item item, int targetPos, BasePlayer player)
            {
                VirtualBox vb;
                if (container != null && Bm.Boxes.TryGetValue(container, out vb) && !vb.Filling && vb.Accept != null && !vb.Accept(vb, item, targetPos))
                {
                    return ItemContainer.CanAcceptResult.CannotAccept;
                }
                return null;
            }

            public override void OnItemAddedToContainer(ItemContainer container, Item item, BasePlayer sourcePlayer)
            {
                VirtualBox vb;
                if (container != null && Bm.Boxes.TryGetValue(container, out vb) && !vb.Filling && vb.Added != null)
                {
                    vb.Added(vb, item);
                }
            }

            public override void OnLootEntityEnd(BasePlayer player, StorageContainer container)
            {
                if (container == null || container.inventory == null)
                {
                    return;
                }
                VirtualBox vb;
                if (Bm.Boxes.TryGetValue(container.inventory, out vb))
                {
                    Bm.NextTick(() => Bm.FinishBox(vb));
                }
            }

            public override void OnPlayerDisconnected(BasePlayer player, string reason)
            {
                foreach (VirtualBox vb in Bm.Boxes.Values.Where(b => b.Player == player).ToList())
                {
                    Bm.FinishBox(vb);
                }
            }

            public override void Unload()
            {
                foreach (VirtualBox vb in Bm.Boxes.Values.ToList())
                {
                    Bm.FinishBox(vb);
                }
            }

            public override void OnDisable()
            {
                Unload();
            }
        }
    }
}
