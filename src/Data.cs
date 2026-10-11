using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class Beastmaster
    {
#pragma warning disable 0649
        // Saved to oxide/data/Beastmaster.json. Tamed animals carry a tag in their skinID so a record can find its entity again.
        internal const ulong TagBase = 7770000000000UL;

        internal partial class StoredData
        {
            public bool Enabled = true;
            public ulong NextTag = 1;
            public Dictionary<ulong, PlayerSettings> Players = new Dictionary<ulong, PlayerSettings>();
            public Dictionary<ulong, TamedRecord> Tamed = new Dictionary<ulong, TamedRecord>();
            public Dictionary<ulong, FormRecord> Forms = new Dictionary<ulong, FormRecord>();
            public Dictionary<ulong, long> RitualCooldown = new Dictionary<ulong, long>();
            public HashSet<ulong> Spawned = new HashSet<ulong>();          // tags of animals made by the spawn tool (bm.clear removes them)
            public Dictionary<ulong, HorseInfo> Horses = new Dictionary<ulong, HorseInfo>();     // horses the plugin manages, by tag
            public Dictionary<ulong, BossInfo> Bosses = new Dictionary<ulong, BossInfo>();      // boss animals, by tag
            public Dictionary<ulong, GearRecord> WildGears = new Dictionary<ulong, GearRecord>(); // inventories of wild animals, by tag
        }

        internal partial class BossInfo
        {
            public string Kind = "bull";                // horse, bull, ram, stag
            public bool Mini;
            public bool Tamed;
        }

        // A horse under the plugin's care: its temperament is assigned at spawn and kept (R8.2).
        internal partial class HorseInfo
        {
            public string Temperament = "calm";          // calm, shy, aggressive
            public bool Calmed;                          // woke from a dart sleep: calm for good
        }

        internal partial class PlayerSettings
        {
            public string View = string.Empty;           // "third" or "first"; empty = config default
            public string Preset = string.Empty;         // camera preset of the third person
            public bool RefuseSaddle;                    // refuse saddles from others while in animal form
            public bool ShowHud = true;
            public bool HintShown;                       // the one-time "how to open the menu" line was sent
            public Dictionary<string, float> CamLift = new Dictionary<string, float>();      // camera height added per species (m)
        }

        internal partial class V3
        {
            public float X, Y, Z;
            public V3() { }
            public V3(Vector3 v) { X = v.x; Y = v.y; Z = v.z; }
            [JsonIgnore] public Vector3 Value => new Vector3(X, Y, Z);
        }

        internal partial class ItemRecord
        {
            public string Shortname = string.Empty;
            public int Amount = 1;
            public float Condition = -1f;
            public ulong Skin;
            public int Slot = -1;
        }

        // The inventory of an animal, the same as the horse's: four equipment slots and the storage the saddle bag (and the wooden armour) give.
        internal partial class GearRecord
        {
            public int Saddle;                           // 0 none, 1 single, 2 double
            public int Shoes;                            // 0 none, 1 basic, 2 high quality
            public List<ItemRecord> Bag = new List<ItemRecord>();      // the storage (Slot = index in the storage)
            public bool HasBag;                          // a saddle bag is fitted
            public int Armor;                            // 0 none, 1 wooden, 2 roadsign, 3 lunar new year
            public List<ItemRecord> Worn = new List<ItemRecord>();     // the items of the four equipment slots as they were (skin, condition)
            public V3 Seen;                              // a wild animal: where it was last seen (its things fall there if it is gone after a restart)
        }

        // One owned animal. State: follow, wait, guard.
        internal partial class TamedRecord
        {
            public ulong Tag;
            public ulong Owner;
            public string Species = string.Empty;
            public string State = "follow";
            public string Prefab = string.Empty;
            public V3 Position = new V3();
            public float Health;
            public float Bond;
            public string Temperament = string.Empty;
            public ulong Genes;
            public bool Boss;
            public string BossKind = string.Empty;
            public bool MiniBoss;
            public GearRecord Gear = new GearRecord();
            public string Name = string.Empty;
        }

        // A player who logged out (or reloaded) in animal form.
        internal partial class FormRecord
        {
            public ulong Player;
            public string Species = string.Empty;
            public string Prefab = string.Empty;
            public V3 Position = new V3();
            public float Health;
            public ulong Genes;
            public long LoggedOut;
            public bool Asleep;
            public ulong Tag;
            public bool OwnsAnimal;
            public GearRecord Gear;                      // the inventory of the animal (when it is not a tamed one, whose record keeps it)
        }

        internal PlayerSettings SettingsOf(ulong id)
        {
            PlayerSettings s;
            if (!Data.Players.TryGetValue(id, out s))
            {
                s = new PlayerSettings();
                Data.Players[id] = s;
            }
            return s;
        }

        private void LoadData()
        {
            try
            {
                Data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch (Exception ex)
            {
                Log(0, "Data file could not be read, starting empty: " + ex.Message);
            }
            if (Data == null)
            {
                Data = new StoredData { Enabled = Cfg.General.EnabledByDefault };
            }
            if (Data.Players == null) Data.Players = new Dictionary<ulong, PlayerSettings>();
            if (Data.Tamed == null) Data.Tamed = new Dictionary<ulong, TamedRecord>();
            if (Data.Forms == null) Data.Forms = new Dictionary<ulong, FormRecord>();
            if (Data.RitualCooldown == null) Data.RitualCooldown = new Dictionary<ulong, long>();
            if (Data.Spawned == null) Data.Spawned = new HashSet<ulong>();
            if (Data.Horses == null) Data.Horses = new Dictionary<ulong, HorseInfo>();
            if (Data.Bosses == null) Data.Bosses = new Dictionary<ulong, BossInfo>();
        }

        internal void SaveData()
        {
            if (Data == null)
            {
                return;
            }
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(Name, Data);
            }
            catch (Exception ex)
            {
                Log(0, "Data file could not be saved: " + ex.Message);
            }
        }

        internal ulong NewTag()
        {
            return TagBase + Data.NextTag++;
        }

        internal TamedRecord RecordOf(BaseEntity entity)
        {
            TamedRecord rec;
            return entity != null && entity.skinID >= TagBase && Data.Tamed.TryGetValue(entity.skinID, out rec) ? rec : null;
        }
    }
}
