using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oxide.Core;
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
            public float Opened;
            public Func<VirtualBox, Item, int, bool> Accept;
            public Func<VirtualBox, Item, int, int> Retarget;       // where an item that does not fit the slot it was dropped on goes instead (-1 = nowhere)
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
            // the box hangs under the player's feet: the deployable's own ground checks would destroy it there before the panel shows
            foreach (Component part in box.GetComponents<Component>())
            {
                string kindName = part != null ? part.GetType().Name : string.Empty;
                if (kindName == "GroundWatch" || kindName == "DestroyOnGroundMissing" || kindName == "DeployableDecay")
                {
                    UnityEngine.Object.DestroyImmediate(part);
                }
            }
            box.Spawn();
            box.inventorySlots = slots;
            box.inventory.capacity = slots;
            var vb = new VirtualBox { Box = box, Player = player, Kind = kind, Data = data, Accept = accept, Added = added, Closed = closed, Filling = true, Opened = Time.realtimeSinceStartup };
            Boxes[box.inventory] = vb;
            if (contents != null)
            {
                foreach (KeyValuePair<int, ItemRecord> pair in contents)
                {
                    Item item = ItemFromRecord(pair.Value);
                    if (item != null && !item.MoveToContainer(box.inventory, pair.Key, true))
                    {
                        GiveBack(player, item);             // no slot for it any more (fewer slots in the config): to the player, never lost
                    }
                }
            }
            vb.Filling = false;
            // The panel is opened the way storage plugins open a virtual container: the box is hidden from every game, the player's loot gets
            // the box's container, and the resizable panel is asked for (the wooden box's own panel has 18 slots: a bigger container did not
            // show at all and only the player's own inventory came up).
            try
            {
                box.limitNetworking = true;
            }
            catch
            {
            }
            EndLooting(player);                           // a panel still open (the game's or ours) is closed first
            bool opened;
            using (new VanillaScope(player))             // an animal form (a spectator) may open it too
            {
                opened = player.inventory.loot.StartLootingEntity(box, false);
            }
            if (!opened)
            {
                FinishBox(vb, false);
                return null;
            }
            box.SetFlagLocal(BaseEntity.Flags.Open, true);
            player.inventory.loot.AddContainer(box.inventory);
            player.inventory.loot.SendImmediate();
            Driver.SendPlayerRpc(player, Driver.ClientRpcString, "RPC_OpenLootPanel", "generic_resizable");
            return vb;
        }

        // Closes the loot panel the player has open now (by name: the method is the game's own PlayerLoot.Clear).
        private static MethodInfo lootClear;
        private static bool lootClearLooked;

        private static void EndLooting(BasePlayer player)
        {
            try
            {
                var loot = player.inventory.loot;
                if (loot.entitySource == null)
                {
                    return;
                }
                if (!lootClearLooked)
                {
                    lootClearLooked = true;
                    lootClear = loot.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                }
                if (lootClear != null)
                {
                    lootClear.Invoke(loot, null);
                }
            }
            catch
            {
            }
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


        // ---------------------------------------------------------------- the inventory of an animal (the horse's, for every animal)
        // Slot 1 = saddle (single or double), slot 2 = horseshoes, slot 3 = saddle bag, slot 4 = horse armour; from slot 5 on the storage: the
        // saddle bag gives it (12 slots), the wooden armour adds 6, exactly like the horse. The horseshoes add speed, the bag and the armour cost
        // some, the armour takes damage off. The horse itself keeps the game's own inventory.

        internal const int SlotSaddle = 0, SlotShoes = 1, SlotBag = 2, SlotArmor = 3, GearFixedSlots = 4;
        private static readonly string[] ArmorItems = { string.Empty, "horse.armor.wood", "horse.armor.roadsign", "horse.armor.lny26" };

        internal static int ArmorIndex(string shortname)
        {
            for (int i = 1; i < ArmorItems.Length; i++)
            {
                if (ArmorItems[i] == shortname)
                {
                    return i;
                }
            }
            return 0;
        }

        internal int StorageOf(GearRecord g)
        {
            return g == null ? 0 : (g.HasBag ? Mathf.Max(0, Cfg.Gear.BagSlots) : 0) + (g.Armor == 1 ? Mathf.Max(0, Cfg.Gear.WoodArmorSlots) : 0);
        }

        private int MaxStorage => Mathf.Max(0, Cfg.Gear.BagSlots) + Mathf.Max(0, Cfg.Gear.WoodArmorSlots);

        // The double saddle fits the horse, the bull and the cow; every other animal carries one rider.
        internal static bool DoubleSaddleSpecies(SpeciesDef s)
        {
            if (s == null)
            {
                return false;
            }
            switch (s.Id)
            {
                case "horse": case "bull": case "cow": return true;
                default: return false;
            }
        }

        // The inventory of an animal form that is not a tamed animal (a tamed one keeps its record's): it goes with the animal when the player
        // leaves it, and with the player when he logs out.
        internal readonly Dictionary<ulong, GearRecord> FormGears = new Dictionary<ulong, GearRecord>();

        internal GearRecord FormGearOf(Driver d)
        {
            TamedRecord rec = RecordOf(d.Body);
            if (rec != null)
            {
                return rec.Gear;
            }
            GearRecord g;
            if (!FormGears.TryGetValue(d.Player.userID, out g) || g == null)
            {
                g = WildGearOf(d.Body, false);
                if (g != null)
                {
                    Data.WildGears.Remove(d.Body.skinID);      // the player became a wild animal that carried things: they are the form's now
                    WildIndex.Remove(d.Body.skinID);
                }
                else
                {
                    g = new GearRecord();
                }
                FormGears[d.Player.userID] = g;
            }
            return g;
        }

        // The inventory of a wild animal (not tamed, not a player's), kept by the tag in the animal's skin id so that it survives a restart. It
        // becomes the owner's when the animal is tamed. Created only when somebody opens it.
        internal readonly Dictionary<ulong, BaseNPC2> WildIndex = new Dictionary<ulong, BaseNPC2>();

        internal GearRecord WildGearOf(BaseEntity animal, bool create)
        {
            BaseNPC2 npc = animal as BaseNPC2;
            if (npc == null || npc.IsDead() || IsFormSleeper(npc))
            {
                return null;                         // the sleeping animal of a player who logged out keeps his inventory, it is not wild
            }
            SpeciesDef species = SpeciesOf(npc);
            if (species == null || !species.Gear)
            {
                return null;
            }
            GearRecord g;
            if (npc.skinID >= TagBase && Data.WildGears.TryGetValue(npc.skinID, out g) && g != null)
            {
                WildIndex[npc.skinID] = npc;
                return g;
            }
            if (!create || (npc.skinID >= TagBase && Data.Tamed.ContainsKey(npc.skinID)))
            {
                return null;
            }
            if (npc.skinID < TagBase)
            {
                npc.skinID = NewTag();
            }
            g = new GearRecord();
            Data.WildGears[npc.skinID] = g;
            WildIndex[npc.skinID] = npc;
            return g;
        }

        internal bool IsFormSleeper(BaseEntity animal)
        {
            if (FormSleepers.Count == 0 && Data.Forms.Count == 0)
            {
                return false;
            }
            foreach (BaseCombatEntity sleeper in FormSleepers.Values)
            {
                if (sleeper == animal)
                {
                    return true;
                }
            }
            if (animal.skinID >= TagBase)
            {
                foreach (FormRecord rec in Data.Forms.Values)
                {
                    if (rec.Tag == animal.skinID)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // A wild animal's inventory that is empty again is forgotten (it was only opened, or a give failed).
        internal void ForgetEmptyWild(GearRecord g)
        {
            if (g == null || !GearEmpty(g) || openGears.Contains(g))
            {
                return;
            }
            foreach (KeyValuePair<ulong, GearRecord> pair in Data.WildGears.ToList())
            {
                if (pair.Value == g)
                {
                    Data.WildGears.Remove(pair.Key);
                    WildIndex.Remove(pair.Key);
                }
            }
        }

        internal static bool GearEmpty(GearRecord g)
        {
            return g == null || (g.Saddle <= 0 && g.Shoes <= 0 && !g.HasBag && g.Armor <= 0 && (g.Bag == null || g.Bag.Count == 0));
        }

        // The inventory of an animal: a player's form, an animal a player has left, a tamed animal. Null = it has none (wild animals, horses).
        internal GearRecord GearOf(BaseEntity animal)
        {
            if (animal == null || animal is RidableHorse)
            {
                return null;
            }
            Driver d = DriverOfAnimal(animal);
            if (d != null && d.Kind == ControlKind.Form)
            {
                return FormGearOf(d);
            }
            TamedRecord rec = RecordOf(animal);
            if (rec != null)
            {
                return rec.Gear;                    // a tamed animal keeps its own inventory, also when a player was it and left it
            }
            LeftForm lf;
            if (animal.net != null && LeftForms.TryGetValue(animal.net.ID.Value, out lf))
            {
                return lf.Gear;
            }
            return WildGearOf(animal, false);           // a wild animal (also while somebody rides it)
        }

        internal int SaddleOf(BaseEntity animal)
        {
            RidableHorse horse = animal as RidableHorse;
            if (horse != null)
            {
                return SeatsOf(horse);
            }
            GearRecord g = GearOf(animal);
            return g != null ? g.Saddle : 0;
        }

        // Why a player may not open the inventory of an animal; null = he may.
        // create: a wild animal gets its inventory now (false: only asked whether it could be opened).
        internal string InventoryProblem(BasePlayer player, BaseEntity animal, out GearRecord gear, bool create = true)
        {
            gear = GearOf(animal);
            SpeciesDef species = SpeciesOf(animal);
            if (gear == null && species != null && species.Gear && DriverOfAnimal(animal) == null)
            {
                gear = WildGearOf(animal, create);
                if (gear == null && !create && animal is BaseNPC2 && RecordOf(animal) == null && !IsFormSleeper(animal) && !((BaseNPC2)animal).IsDead())
                {
                    return null;                     // a wild animal: it gets an inventory as soon as somebody opens it
                }
            }
            if (gear == null || species == null || !species.Gear)
            {
                return "This animal has no inventory.";
            }
            Driver d = DriverOfAnimal(animal);
            if (d != null && d.Kind == ControlKind.Form)
            {
                if (d.Player == player)
                {
                    return null;                     // the player's own animal (Ctrl + E as the animal)
                }
                return FormHandledBy(d, player) ? null : "This animal does not let you into its inventory.";
            }
            if (d != null)
            {
                return "Somebody rides it: its inventory opens when nobody is on it.";
            }
            if (LeftOwnedBy(animal, player))
            {
                return null;
            }
            LeftForm lf;
            if (animal.net != null && LeftForms.TryGetValue(animal.net.ID.Value, out lf))
            {
                return L("NotYours", player.UserIDString);
            }
            TamedRecord rec = RecordOf(animal);
            if (rec != null && Cfg.Gear.BagPrivate && !MayUse(player, rec.Owner, "team"))
            {
                return L("NotYours", player.UserIDString);
            }
            return null;
        }

        internal sealed class InventoryContext
        {
            public GearRecord Gear;
            public SpeciesDef Species;
            public BaseEntity Animal;
        }

        private readonly HashSet<GearRecord> openGears = new HashSet<GearRecord>();

        internal void OpenInventory(BasePlayer player, BaseEntity animal)
        {
            if (!Cfg.Features.Gear)
            {
                Tell(player, "FeatureOff");
                return;
            }
            GearRecord gear;
            string problem = InventoryProblem(player, animal, out gear);
            if (problem != null)
            {
                TellRaw(player, problem);
                return;
            }
            foreach (VirtualBox own in Boxes.Values.Where(b => b.Player == player).ToList())
            {
                FinishBox(own);                      // a panel of the same player still counted as open (saved first)
            }
            ReleaseStuckPanels(gear);
            if (openGears.Contains(gear))
            {
                TellRaw(player, "Somebody is in its inventory right now.");
                return;
            }
            SpeciesDef species = SpeciesOf(animal);
            var contents = new List<KeyValuePair<int, ItemRecord>>();
            foreach (KeyValuePair<int, ItemRecord> worn in WornItems(gear))
            {
                contents.Add(worn);
            }
            foreach (ItemRecord stored in gear.Bag)
            {
                contents.Add(new KeyValuePair<int, ItemRecord>(GearFixedSlots + Mathf.Max(0, stored.Slot), stored));
            }
            var ctx = new InventoryContext { Gear = gear, Species = species, Animal = animal };
            VirtualBox vb = OpenBox(player, "inventory", GearFixedSlots + MaxStorage, contents, ctx, InventoryAccept, null, InventoryClosed);
            if (vb != null)
            {
                vb.Retarget = InventoryTarget;
                openGears.Add(gear);
                Tell(player, "GearOpen", species.Display, Cfg.Gear.BagSlots, Cfg.Gear.WoodArmorSlots);
            }
        }

        // The items of the four equipment slots: as they were put in (skin, condition), or plain ones for older records.
        internal static List<KeyValuePair<int, ItemRecord>> WornItems(GearRecord g)
        {
            var list = new List<KeyValuePair<int, ItemRecord>>();
            Action<int, string> add = (slot, shortname) =>
            {
                ItemRecord kept = g.Worn != null ? g.Worn.FirstOrDefault(r => r.Slot == slot && r.Shortname == shortname) : null;
                list.Add(new KeyValuePair<int, ItemRecord>(slot, kept ?? new ItemRecord { Shortname = shortname, Slot = slot }));
            };
            if (g.Saddle > 0) add(SlotSaddle, g.Saddle == 2 ? "horse.saddle.double" : "horse.saddle.single");
            if (g.Shoes > 0) add(SlotShoes, g.Shoes == 2 ? "horse.shoes.advanced" : "horse.shoes.basic");
            if (g.HasBag) add(SlotBag, "horse.saddlebag");
            if (g.Armor > 0 && g.Armor < ArmorItems.Length) add(SlotArmor, ArmorItems[g.Armor]);
            return list;
        }

        // Storage slots the open panel has now (the saddle bag and the wooden armour make them).
        private int StorageCapacity(VirtualBox vb)
        {
            Item bag = vb.Box.inventory.GetSlot(SlotBag);
            Item armor = vb.Box.inventory.GetSlot(SlotArmor);
            return (bag != null && bag.info.shortname == "horse.saddlebag" ? Cfg.Gear.BagSlots : 0)
                + (armor != null && ArmorIndex(armor.info.shortname) == 1 ? Cfg.Gear.WoodArmorSlots : 0);
        }

        // Where an item goes when it was dropped on a slot it does not fit, or with a double click or a shift click (no slot at all): a saddle,
        // horseshoes, a bag or armour to their own slot, everything else to the first free storage slot. -1 = nowhere (no bag, no room).
        private int InventoryTarget(VirtualBox vb, Item item, int requested)
        {
            string name = item.info.shortname;
            int own = name.StartsWith("horse.saddle.") ? SlotSaddle : name.StartsWith("horse.shoes.") ? SlotShoes : name == "horse.saddlebag" ? SlotBag : ArmorIndex(name) > 0 ? SlotArmor : -1;
            if (own >= 0 && vb.Box.inventory.GetSlot(own) == null && InventoryAccept(vb, item, own))
            {
                return own;
            }
            int capacity = Mathf.Min(StorageCapacity(vb), MaxStorage);
            for (int slot = GearFixedSlots; slot < GearFixedSlots + capacity; slot++)
            {
                if (vb.Box.inventory.GetSlot(slot) == null && InventoryAccept(vb, item, slot))
                {
                    return slot;
                }
            }
            return -1;
        }

        private bool InventoryAccept(VirtualBox vb, Item item, int slot)
        {
            InventoryContext ctx = vb.Data as InventoryContext;
            if (ctx == null || slot < 0)
            {
                return false;
            }
            string name = item.info.shortname;
            switch (slot)
            {
                case SlotSaddle: return name == "horse.saddle.single" || (name == "horse.saddle.double" && DoubleSaddleSpecies(ctx.Species));
                case SlotShoes: return name == "horse.shoes.basic" || name == "horse.shoes.advanced";
                case SlotBag: return name == "horse.saddlebag";
                case SlotArmor: return ArmorIndex(name) > 0;
                default:
                    {
                        Item bag = vb.Box.inventory.GetSlot(SlotBag);
                        Item armor = vb.Box.inventory.GetSlot(SlotArmor);
                        return slot - GearFixedSlots < StorageCapacity(vb) && item != bag && item != armor;
                    }
            }
        }

        private void InventoryClosed(VirtualBox vb)
        {
            InventoryContext ctx = vb.Data as InventoryContext;
            if (ctx == null)
            {
                ReturnAll(vb);
                return;
            }
            openGears.Remove(ctx.Gear);
            ItemContainer inv = vb.Box.inventory;
            Item saddle = inv.GetSlot(SlotSaddle), shoes = inv.GetSlot(SlotShoes), bagItem = inv.GetSlot(SlotBag), armor = inv.GetSlot(SlotArmor);
            GearRecord g = ctx.Gear;
            g.Saddle = saddle == null ? 0 : saddle.info.shortname == "horse.saddle.double" ? 2 : saddle.info.shortname == "horse.saddle.single" ? 1 : 0;
            g.Shoes = shoes == null ? 0 : shoes.info.shortname == "horse.shoes.advanced" ? 2 : 1;
            g.HasBag = bagItem != null && bagItem.info.shortname == "horse.saddlebag";
            g.Armor = armor != null ? ArmorIndex(armor.info.shortname) : 0;
            if (g.Worn == null)
            {
                g.Worn = new List<ItemRecord>();
            }
            g.Worn.Clear();
            foreach (Item worn in new[] { saddle, shoes, bagItem, armor })
            {
                if (worn != null)
                {
                    g.Worn.Add(RecordFromItem(worn, worn.position));
                }
            }
            int capacity = StorageOf(g);
            g.Bag.Clear();
            foreach (Item item in inv.itemList.ToList())
            {
                if (item.position < GearFixedSlots)
                {
                    continue;
                }
                int index = item.position - GearFixedSlots;
                if (index < capacity)
                {
                    g.Bag.Add(RecordFromItem(item, index));
                }
                else
                {
                    GiveBack(vb.Player, item);        // no room without the bag (or the armour): back to the player
                }
            }
            foreach (Item rest in inv.itemList.ToList())
            {
                rest.Remove();
            }
            RefreshSaddle(ctx.Animal);
            ForgetEmptyWild(g);
            Tell(vb.Player, "GearSaved");
        }

        private void ReturnAll(VirtualBox vb)
        {
            foreach (Item item in vb.Box.inventory.itemList.ToList())
            {
                GiveBack(vb.Player, item);
            }
        }

        // Ctrl + E as an animal: the animal's own inventory (the horse: the game's own horse inventory).
        internal void OpenOwnInventory(Driver d)
        {
            if (d == null || d.Kind != ControlKind.Form || d.Player == null || !d.Alive)
            {
                return;
            }
            if (d.Horse != null)
            {
                if (!OpenEntityLoot(d.Player, d.Horse))
                {
                    TellRaw(d.Player, "The inventory of the horse cannot be opened right now.");
                }
                return;
            }
            if (d.Species == null || !d.Species.Gear)
            {
                TellRaw(d.Player, "A " + (d.Species != null ? d.Species.Display.ToLowerInvariant() : "creature") + " carries nothing: it has no inventory.");
                return;
            }
            OpenInventory(d.Player, d.Body);
            if (Boxes.Values.Any(b => b.Player == d.Player))
            {
                TellRaw(d.Player, "If no panel shows up, your game does not open panels in the animal view: press X and Ctrl + E at your animal, or let another player open it (hold E at you).");
            }
        }

        // The game's own loot panel of an entity that is no storage box (the horse): its PlayerOpenLoot, whatever its parameters are in this build.
        private static readonly Dictionary<Type, MethodInfo> openLootMethods = new Dictionary<Type, MethodInfo>();

        internal bool OpenEntityLoot(BasePlayer player, BaseEntity entity)
        {
            if (player == null || !Valid(entity))
            {
                return false;
            }
            // the owner rules (this plugin's and other plugins') are asked first: the game's own method may not ask them itself
            if (Interface.CallHook("CanLootEntity", player, entity) != null)
            {
                return true;                         // refused, and the refusal has been told already
            }
            StorageContainer storage = entity as StorageContainer;
            Type type = entity.GetType();
            MethodInfo method;
            if (!openLootMethods.TryGetValue(type, out method))
            {
                method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "PlayerOpenLoot" && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(BasePlayer))
                    .OrderByDescending(m => m.GetParameters().Length)
                    .FirstOrDefault();
                openLootMethods[type] = method;
            }
            try
            {
                using (new VanillaScope(player))
                {
                    if (storage != null)
                    {
                        return storage.PlayerOpenLoot(player, string.Empty, false);
                    }
                    if (method == null)
                    {
                        return false;
                    }
                    ParameterInfo[] ps = method.GetParameters();
                    var args = new object[ps.Length];
                    args[0] = player;
                    for (int i = 1; i < ps.Length; i++)
                    {
                        Type t = ps[i].ParameterType;
                        args[i] = t == typeof(string) ? (object)string.Empty : t == typeof(bool) ? (object)false : ps[i].HasDefaultValue ? ps[i].DefaultValue : (t.IsValueType ? Activator.CreateInstance(t) : null);
                    }
                    object result = method.Invoke(entity, args);
                    return !(result is bool) || (bool)result;
                }
            }
            catch (Exception ex)
            {
                Log(1, "Opening the loot of " + entity.ShortPrefabName + " failed: " + ex.Message);
                return false;
            }
        }

        // The storage of the game's horse (its saddle bag), found by name in this build.
        private static FieldInfo horseStorageField;
        private static PropertyInfo horseStorageProperty;
        private static bool horseStorageLooked;

        internal static ItemContainer HorseStorage(RidableHorse horse)
        {
            if (horse == null)
            {
                return null;
            }
            if (!horseStorageLooked)
            {
                horseStorageLooked = true;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                for (Type t = typeof(RidableHorse); t != null && horseStorageField == null && horseStorageProperty == null; t = t.BaseType)
                {
                    horseStorageField = t.GetField("storageInventory", flags | BindingFlags.DeclaredOnly);
                    if (horseStorageField == null)
                    {
                        horseStorageProperty = t.GetProperty("storageInventory", flags | BindingFlags.DeclaredOnly);
                    }
                }
            }
            try
            {
                if (horseStorageField != null)
                {
                    return horseStorageField.GetValue(horse) as ItemContainer;
                }
                if (horseStorageProperty != null)
                {
                    return horseStorageProperty.GetValue(horse, null) as ItemContainer;
                }
            }
            catch
            {
            }
            return null;
        }

        // An item for a player's animal (picked up, given): into the saddle bag of its inventory - the horse's own one for the horse.
        internal bool StoreForForm(Driver d, Item item, out string why)
        {
            why = null;
            if (d == null || item == null)
            {
                why = "There is nothing to put it in.";
                return false;
            }
            if (d.Horse != null)
            {
                ItemContainer storage = HorseStorage(d.Horse);
                if (storage == null || storage.capacity <= 0)
                {
                    why = "The horse has no saddle bag: things can only be carried in a saddle bag.";
                    return false;
                }
                if (!item.MoveToContainer(storage))
                {
                    why = "The horse's saddle bag is full.";
                    return false;
                }
                return true;
            }
            if (d.Species == null || !d.Species.Gear)
            {
                why = "A " + (d.Species != null ? d.Species.Display.ToLowerInvariant() : "creature") + " cannot carry anything.";
                return false;
            }
            return AddToGear(FormGearOf(d), item, out why);
        }

        // The game of a player in animal form may not show a panel at all (the spectator view), and a player who left cannot close his: such
        // a panel is saved and closed as soon as somebody else needs the inventory, so that it never keeps others (or the pick-ups) out.
        private static bool PanelMayBeUnseen(VirtualBox vb, Beastmaster bm)
        {
            if (vb.Player == null || !vb.Player.IsConnected)
            {
                return true;
            }
            Driver holder = bm.DriverOf(vb.Player);
            return holder != null && holder.Spectating;
        }

        private void ReleaseStuckPanels(GearRecord g)
        {
            if (g == null || !openGears.Contains(g))
            {
                return;
            }
            foreach (VirtualBox held in Boxes.Values.ToList())
            {
                InventoryContext hc = held.Data as InventoryContext;
                if (hc != null && hc.Gear == g && PanelMayBeUnseen(held, this))
                {
                    FinishBox(held);
                }
            }
        }

        // An open panel of this inventory is closed first (its contents are saved), so that nothing is dropped and kept at the same time.
        internal void CloseInventoriesOf(GearRecord g)
        {
            foreach (VirtualBox vb in Boxes.Values.ToList())
            {
                InventoryContext ctx = vb.Data as InventoryContext;
                if (ctx != null && ctx.Gear == g)
                {
                    FinishBox(vb);
                }
            }
        }

        internal static void GiveBack(BasePlayer player, Item item)
        {
            if (player == null || !player.IsConnected || !item.MoveToContainer(player.inventory.containerMain))
            {
                item.Drop(player != null ? player.transform.position + Vector3.up : Vector3.zero, Vector3.up);
            }
        }

        // Puts an item into the storage of an animal (what it picks up, what somebody gives it). False = no saddle bag, no room, or the
        // inventory is open in a panel right now.
        internal bool AddToGear(GearRecord g, Item item, out string why)
        {
            why = null;
            if (g == null)
            {
                why = "This animal has no inventory.";
                return false;
            }
            ReleaseStuckPanels(g);
            if (openGears.Contains(g))
            {
                why = "Its inventory is open right now.";
                return false;
            }
            int capacity = StorageOf(g);
            if (capacity <= 0)
            {
                why = "It has no saddle bag: things can only be carried in a saddle bag (put one into its inventory).";
                return false;
            }
            ItemRecord same = g.Bag.FirstOrDefault(r => r.Shortname == item.info.shortname && r.Skin == item.skin && r.Condition < 0f && !item.hasCondition && r.Amount + item.amount <= item.info.stackable);
            if (same != null)
            {
                same.Amount += item.amount;
                item.Remove();
                return true;
            }
            for (int slot = 0; slot < capacity; slot++)
            {
                if (g.Bag.All(r => r.Slot != slot))
                {
                    g.Bag.Add(RecordFromItem(item, slot));
                    item.Remove();
                    return true;
                }
            }
            why = "Its saddle bag is full.";
            return false;
        }

        // Everything an animal carried falls to the ground (it died, or it vanished).
        internal void DropGearRecord(GearRecord g, Vector3 pos)
        {
            if (g == null)
            {
                return;
            }
            CloseInventoriesOf(g);
            foreach (KeyValuePair<int, ItemRecord> worn in WornItems(g))
            {
                Item item = ItemFromRecord(worn.Value);
                if (item != null)
                {
                    item.Drop(pos, Vector3.up * 2f + UnityEngine.Random.insideUnitSphere);
                }
            }
            foreach (ItemRecord stored in g.Bag)
            {
                Item item = ItemFromRecord(stored);
                if (item != null)
                {
                    item.Drop(pos, Vector3.up * 2f + UnityEngine.Random.insideUnitSphere);
                }
            }
            g.Saddle = 0;
            g.Shoes = 0;
            g.HasBag = false;
            g.Armor = 0;
            g.Bag.Clear();
            if (g.Worn != null)
            {
                g.Worn.Clear();
            }
        }

        // Dying tamed animals drop what they carried (R3.5).
        internal void DropGear(BaseEntity animal, TamedRecord rec)
        {
            DropGearRecord(rec.Gear, animal.transform.position + Vector3.up * 0.6f);
            rec.Gear = new GearRecord();
            RemoveSaddle(animal);
        }

        // Speed change from the inventory, the horse's numbers: horseshoes add speed, the saddle bag and the armour cost some.
        internal float GearSpeedBonus(BaseNPC2 animal)
        {
            GearRecord g = GearOf(animal);
            if (g == null)
            {
                return 0f;
            }
            float bonus = g.Shoes == 2 ? Cfg.Movement.ShoesAdvanced : g.Shoes == 1 ? Cfg.Movement.ShoesBasic : 0f;
            if (g.HasBag)
            {
                bonus += Cfg.Movement.SaddleBag;
            }
            if (g.Armor == 1)
            {
                bonus += Cfg.Movement.ArmorWood;
            }
            else if (g.Armor > 1)
            {
                bonus += Cfg.Movement.ArmorRoadsign;
            }
            return bonus;
        }

        // The horse form drives the horse itself, so the speed of its own equipment is added here (the same numbers as for the other animals).
        internal float HorseGearBonus(RidableHorse horse)
        {
            if (horse == null || horse.equipmentInventory == null)
            {
                return 0f;
            }
            float bonus = 0f;
            foreach (Item it in horse.equipmentInventory.itemList)
            {
                string n = it.info.shortname;
                int armor = ArmorIndex(n);
                if (n == "horse.shoes.advanced") bonus += Cfg.Movement.ShoesAdvanced;
                else if (n == "horse.shoes.basic") bonus += Cfg.Movement.ShoesBasic;
                else if (n == "horse.saddlebag") bonus += Cfg.Movement.SaddleBag;
                else if (armor == 1) bonus += Cfg.Movement.ArmorWood;
                else if (armor > 1) bonus += Cfg.Movement.ArmorRoadsign;
            }
            return bonus;
        }

        private string horseGaitsText;
        private float[] horseGaits;

        internal float[] HorseFormGaits()
        {
            string text = Cfg.Horse.FormGaits ?? string.Empty;
            if (horseGaits != null && horseGaitsText == text)
            {
                return horseGaits;
            }
            float[] fallback = { 1.9f, 4.3f, 7.5f, 11f, 14f };
            float[] values = new float[5];
            string[] parts = text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            float last = 0f;
            for (int i = 0; i < 5; i++)
            {
                float v;
                values[i] = i < parts.Length && float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0f ? v : fallback[i];
                values[i] = Mathf.Max(values[i], last);          // never slower with a higher gait
                last = values[i];
            }
            horseGaitsText = text;
            horseGaits = values;
            return values;
        }

        private float ArmorProtection(GearRecord g)
        {
            if (g == null || g.Armor <= 0)
            {
                return 0f;
            }
            return Mathf.Clamp01(g.Armor == 1 ? Cfg.Gear.ArmorWoodProtection : Cfg.Gear.ArmorRoadsignProtection);
        }

        // ---------------------------------------------------------------- the saddle on the back
        // The horse shows its saddle itself. On the other animals the saddle of the inventory is the game's own saddle model (the item's world
        // model), fixed to the spine so that it moves with the back, scaled to the size of the animal. It can never be picked up.

        internal sealed class SaddleVisual
        {
            public BaseEntity Entity;
            public int Kind;
        }

        private readonly Dictionary<ulong, SaddleVisual> saddles = new Dictionary<ulong, SaddleVisual>();

        internal void RefreshSaddle(BaseEntity animal)
        {
            BaseNPC2 npc = animal as BaseNPC2;
            if (npc == null || !Valid(npc) || npc.IsDead())
            {
                RemoveSaddle(animal);
                return;
            }
            GearRecord g = GearOf(npc);
            int want = Cfg.Gear.ShowSaddle && g != null ? g.Saddle : 0;
            ulong id = npc.net.ID.Value;
            SaddleVisual current;
            if (saddles.TryGetValue(id, out current) && current.Kind == want && Valid(current.Entity))
            {
                return;
            }
            RemoveSaddle(npc);
            if (want <= 0)
            {
                return;
            }
            BaseEntity visual = MakeSaddle(npc, want);
            if (visual != null)
            {
                saddles[id] = new SaddleVisual { Entity = visual, Kind = want };
            }
        }

        // The saddles of one species are put on again (after /bm saddle changed how they sit).
        internal void RefreshSaddlesOf(SpeciesDef species)
        {
            foreach (ulong id in saddles.Keys.ToList())
            {
                BaseNPC2 npc = BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseNPC2;
                if (npc != null && SpeciesOf(npc) == species)
                {
                    RemoveSaddle(npc);
                    RefreshSaddle(npc);
                }
            }
        }

        internal bool HasSaddleModel(BaseEntity animal)
        {
            SaddleVisual v;
            return animal != null && animal.net != null && saddles.TryGetValue(animal.net.ID.Value, out v) && Valid(v.Entity);
        }

        internal bool IsSaddleModel(BaseEntity e)
        {
            if (e == null || saddles.Count == 0)
            {
                return false;
            }
            foreach (SaddleVisual v in saddles.Values)
            {
                if (v.Entity == e)
                {
                    return true;
                }
            }
            return false;
        }

        // A world item vanishes after a while by itself (the game's despawn of dropped items); the saddle on the back must not.
        private static void KeepFromDespawning(BaseEntity visual)
        {
            try
            {
                MethodInfo idle = visual.GetType().GetMethod("IdleDestroy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (idle != null)
                {
                    visual.CancelInvoke((Action)Delegate.CreateDelegate(typeof(Action), visual, idle));
                }
            }
            catch
            {
                // the Tick of the module puts a new saddle on the back if this one goes
            }
        }

        internal void RemoveSaddle(BaseNetworkable animal)
        {
            if (animal == null || animal.net == null)
            {
                return;
            }
            SaddleVisual current;
            if (saddles.TryGetValue(animal.net.ID.Value, out current))
            {
                saddles.Remove(animal.net.ID.Value);
                if (Valid(current.Entity))
                {
                    current.Entity.Kill();
                }
            }
        }

        internal static Vector3 ParseVector(string text)
        {
            Vector3 v = Vector3.zero;
            if (string.IsNullOrEmpty(text))
            {
                return v;
            }
            string[] parts = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            float f;
            if (parts.Length > 0 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) v.x = f;
            if (parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) v.y = f;
            if (parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) v.z = f;
            return v;
        }

        private BaseEntity MakeSaddle(BaseNPC2 animal, int kind)
        {
            SpeciesDef species = SpeciesOf(animal);
            SpeciesOverride o = null;
            if (species != null)
            {
                Cfg.Species.TryGetValue(species.Id, out o);
            }
            Item item = ItemManager.CreateByName(kind == 2 ? "horse.saddle.double" : "horse.saddle.single", 1);
            if (item == null)
            {
                return null;
            }
            float k = ModelScale(animal);
            float lift = Cfg.Gear.SaddleLift + (o != null && o.SaddleLift.HasValue ? o.SaddleLift.Value : 0f);
            Vector3 local = BackPoint(animal) + Vector3.up * lift;
            Vector3 turn = ParseVector(Cfg.Gear.SaddleRotation) + (o != null ? ParseVector(o.SaddleTurn) : Vector3.zero);
            float yawOffset = species != null ? YawOffsetOf(species) : 0f;
            Vector3 world = animal.transform.TransformPoint(local);
            Quaternion rotation = animal.transform.rotation * Quaternion.Euler(0f, -yawOffset, 0f) * Quaternion.Euler(turn);
            BaseEntity visual = item.CreateWorldObject(world, rotation);
            if (visual == null)
            {
                item.Remove();
                return null;
            }
            visual.enableSaving = false;
            KeepFromDespawning(visual);
            WorldItem worldItem = visual as WorldItem;
            if (worldItem != null)
            {
                worldItem.allowPickup = false;
            }
            Rigidbody rb = visual.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
            }
            Transform bone = BackBone(animal);
            if (bone != null)
            {
                visual.SetParent(animal, bone.name, true, false);       // the saddle moves with the spine, like the horse's
            }
            else
            {
                visual.SetParent(animal, true, false);
            }
            // the size: a horse-sized saddle on a horse-sized animal, smaller on a sheep, bigger on a boss
            float length = Mathf.Max(0.3f, animal.bounds.size.z) * k;
            float size = Mathf.Clamp(length / 3.06f, 0.4f, 1.6f) * Mathf.Max(0.1f, Cfg.Gear.SaddleScale) * (o != null && o.SaddleScale.HasValue ? o.SaddleScale.Value : 1f);
            Transform parent = visual.transform.parent;
            float parentScale = parent != null ? Mathf.Max(0.0001f, parent.lossyScale.x) : 1f;
            visual.networkEntityScale = true;
            visual.transform.localScale = Vector3.one * (size / parentScale);
            visual.SendNetworkUpdateImmediate();
            return visual;
        }

        // ---------------------------------------------------------------- hooks

        internal sealed class GearModule : Module
        {
            public override int Order => 40;
            private float nextSaddleCheck;

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["GearOpen"] = "{0} inventory: slot 1 saddle, slot 2 horseshoes, slot 3 saddle bag, slot 4 horse armour; from slot 5 on the storage ({1} slots with a saddle bag, {2} more with the wooden armour). Close the panel to save.",
                    ["GearSaved"] = "Saved.",
                };
            }

            public override object CanAcceptItem(ItemContainer container, Item item, int targetPos, BasePlayer player)
            {
                VirtualBox vb;
                if (container != null && Bm.Boxes.TryGetValue(container, out vb) && !vb.Filling && vb.Accept != null && !vb.Accept(vb, item, targetPos))
                {
                    // dropped on a wrong slot, or a double click / shift click: the item is put on the right slot a moment later
                    int slot = vb.Retarget != null && !vb.Done ? vb.Retarget(vb, item, targetPos) : -1;
                    if (slot >= 0)
                    {
                        Bm.NextTick(() =>
                        {
                            try
                            {
                                if (!vb.Done && item != null && item.info != null && !container.itemList.Contains(item) && container.GetSlot(slot) == null)
                                {
                                    item.MoveToContainer(container, slot, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                Bm.Log(1, "Moving an item to slot " + (slot + 1) + " failed: " + ex.Message);
                            }
                        });
                    }
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

            // The horse armour of an animal takes damage off (the horse's armour works by itself).
            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                BaseNPC2 animal = entity as BaseNPC2;
                if (animal == null || info == null)
                {
                    return null;
                }
                float protection = Bm.ArmorProtection(Bm.GearOf(animal));
                if (protection > 0f)
                {
                    info.damageTypes.ScaleAll(1f - protection);
                }
                return null;
            }

            // The saddles on the backs follow the inventories (a saddle fitted or taken out, an animal that came or went).
            public override void Tick(float now)
            {
                if (now < nextSaddleCheck)
                {
                    return;
                }
                nextSaddleCheck = now + 1f;
                // a panel the player's game never showed, or one that was closed without the end hook, ends here (its contents are saved)
                foreach (VirtualBox vb in Bm.Boxes.Values.ToList())
                {
                    float open = Time.realtimeSinceStartup - vb.Opened;
                    if (open > 2f && (vb.Player == null || !vb.Player.IsConnected || vb.Player.inventory.loot.entitySource != vb.Box))
                    {
                        Bm.FinishBox(vb);
                    }
                    else if (open > 120f && PanelMayBeUnseen(vb, Bm))
                    {
                        Bm.FinishBox(vb);                    // a panel in the animal view that may never have been seen: saved after two minutes
                    }
                }
                var wanted = new HashSet<ulong>();
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d != null && d.Alive && d.Animal != null && Valid(d.Animal))
                    {
                        wanted.Add(d.Animal.net.ID.Value);
                        Bm.RefreshSaddle(d.Animal);
                    }
                }
                foreach (LeftForm lf in Bm.LeftForms.Values.ToList())
                {
                    BaseNPC2 npc = lf.Animal as BaseNPC2;
                    if (npc != null && Valid(npc) && wanted.Add(npc.net.ID.Value))
                    {
                        Bm.RefreshSaddle(npc);
                    }
                }
                foreach (TamedRecord rec in Bm.Data.Tamed.Values.ToList())
                {
                    BaseNPC2 npc = Bm.EntityOfRecord(rec) as BaseNPC2;
                    if (npc != null && wanted.Add(npc.net.ID.Value))
                    {
                        Bm.RefreshSaddle(npc);
                    }
                }
                foreach (KeyValuePair<ulong, BaseNPC2> pair in Bm.WildIndex.ToList())
                {
                    BaseNPC2 npc = pair.Value;
                    if (!Valid(npc) || npc.IsDead() || npc.skinID != pair.Key)
                    {
                        Bm.WildIndex.Remove(pair.Key);
                        continue;
                    }
                    GearRecord seen;
                    if (Bm.Data.WildGears.TryGetValue(pair.Key, out seen) && seen != null)
                    {
                        seen.Seen = new V3(npc.transform.position);
                    }
                    if (wanted.Add(npc.net.ID.Value))
                    {
                        Bm.RefreshSaddle(npc);
                    }
                }
                foreach (ulong id in Bm.saddles.Keys.ToList())
                {
                    if (!wanted.Contains(id))
                    {
                        SaddleVisual v = Bm.saddles[id];
                        Bm.saddles.Remove(id);
                        if (Valid(v.Entity))
                        {
                            v.Entity.Kill();
                        }
                    }
                }
            }

            // A dying animal takes its saddle model with it; a wild animal drops what it carried (the others by their owners' rules).
            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                if (entity is BaseNPC2)
                {
                    DropWild(entity);
                    Bm.RemoveSaddle(entity);
                }
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                if (entity is BaseNPC2)
                {
                    DropWild(entity);
                    Bm.RemoveSaddle(entity);
                }
            }

            private void DropWild(BaseNetworkable entity)
            {
                BaseEntity e = entity as BaseEntity;
                GearRecord g;
                if (e == null || e.skinID < TagBase || !Bm.Data.WildGears.TryGetValue(e.skinID, out g))
                {
                    return;
                }
                Bm.Data.WildGears.Remove(e.skinID);
                Bm.WildIndex.Remove(e.skinID);
                if (e.transform != null)
                {
                    Bm.DropGearRecord(g, e.transform.position + Vector3.up * 0.6f);
                }
            }

            // After a restart: the wild animals that carry things are found again by their tags; inventories of animals that are gone are dropped.
            public override void OnServerInitialized()
            {
                Bm.timer.Once(4f, () =>
                {
                    if (Bm.Data.WildGears.Count == 0)
                    {
                        return;
                    }
                    var found = new HashSet<ulong>();
                    foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                    {
                        BaseNPC2 npc = net as BaseNPC2;
                        if (npc != null && npc.skinID >= TagBase && Bm.Data.WildGears.ContainsKey(npc.skinID))
                        {
                            found.Add(npc.skinID);
                            Bm.WildIndex[npc.skinID] = npc;
                        }
                    }
                    foreach (ulong tag in Bm.Data.WildGears.Keys.ToList())
                    {
                        if (found.Contains(tag))
                        {
                            continue;
                        }
                        GearRecord gone = Bm.Data.WildGears[tag];
                        Bm.Data.WildGears.Remove(tag);
                        if (gone != null && !GearEmpty(gone))
                        {
                            if (gone.Seen != null)
                            {
                                Bm.DropGearRecord(gone, gone.Seen.Value + Vector3.up * 0.8f);       // the animal is gone: its things lie where it was last
                            }
                            else
                            {
                                Bm.Log(1, "A wild animal's inventory (tag " + tag + ") was lost: the animal is gone and no place is known.");
                            }
                        }
                    }
                });
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
                foreach (SaddleVisual v in Bm.saddles.Values.ToList())
                {
                    if (Valid(v.Entity))
                    {
                        v.Entity.Kill();
                    }
                }
                Bm.saddles.Clear();
            }

            public override void OnDisable()
            {
                Unload();
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Gear: " + Bm.saddles.Count + " saddles shown on animals; armour items " + string.Join(", ", ArmorItems.Where(a => a.Length > 0).Select(a => a + (ItemManager.FindItemDefinition(a) != null ? " OK" : " MISSING")).ToArray()));
            }
        }
    }
}
