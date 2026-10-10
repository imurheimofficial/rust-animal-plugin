using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oxide.Core;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BeastmasterDump", "Beastmaster dev", "0.1.0")]
    [Description("DEV ONLY - never shipped. Dumps the animal, mount, gesture, item and horse data of the running build to oxide/data/BeastmasterDump so the real plugin is built on facts, not guesses.")]
    public class BeastmasterDump : RustPlugin
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const int MaxListItems = 48;

        private static readonly string[] AnimalWords =
        {
            "npc", "animal", "livestock", "horse", "bear", "boar", "wolf", "stag", "deer", "chicken", "cow", "sheep",
            "lamb", "calf", "tiger", "panther", "croc", "snake", "rabbit", "squirrel", "frog", "crab", "turtle",
            "jelly", "shark", "zombie", "pig", "corpse", "saddle", "seat", "mount", "flatbed", "module", "trough", "hitch",
            "dart", "blowpipe", "beartrap", "syringe", "spray", "torch", "rope", "lead"
        };

        private string dumpDir;

        private void OnServerInitialized()
        {
            timer.Once(5f, RunDump);
        }

        [ConsoleCommand("bmdump.run")]
        private void CmdRun(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                return;
            }
            RunDump();
            arg.ReplyWith("BeastmasterDump written to " + dumpDir);
        }

        private void RunDump()
        {
            dumpDir = Path.Combine(Interface.Oxide.DataDirectory, "BeastmasterDump");
            Directory.CreateDirectory(dumpDir);
            Section("build", DumpBuild);
            Section("prefabs", DumpPrefabs);
            Section("animals", DumpAnimals);
            Section("mountables", DumpMountables);
            Section("gestures", DumpGestures);
            Section("items", DumpItems);
            Section("horse", DumpHorse);
            Section("convars_livestock", () => DumpStatics(typeof(ConVar.Livestock)));
            Section("convars_ai", () => DumpStatics(typeof(ConVar.AI)));
            Puts("BeastmasterDump complete: " + dumpDir);
        }

        private void Section(string name, Func<object> producer)
        {
            try
            {
                object result = producer();
                File.WriteAllText(Path.Combine(dumpDir, name + ".json"), JsonConvert.SerializeObject(result, Formatting.Indented));
                Puts($"dump section '{name}': ok");
            }
            catch (Exception ex)
            {
                PrintError($"dump section '{name}' FAILED: {ex}");
            }
        }

        private object DumpBuild()
        {
            return new Dictionary<string, object>
            {
                ["protocol"] = Rust.Protocol.printable,
                ["network"] = Rust.Protocol.network,
                ["unity"] = Application.unityVersion,
                ["oxide"] = OxideMod.Version.ToString(),
                ["entityPrefabs"] = GameManifest.Current.entities.Length,
                ["pooledStrings"] = GameManifest.Current.pooledStrings.Length
            };
        }

        private object DumpPrefabs()
        {
            List<string> entities = GameManifest.Current.entities.Select(e => e.ToLowerInvariant()).OrderBy(e => e).ToList();
            List<string> relevant = entities.Where(IsRelevantPath).ToList();
            List<string> relevantStrings = GameManifest.Current.pooledStrings
                .Select(p => p.str == null ? string.Empty : p.str.ToLowerInvariant())
                .Where(s => s.EndsWith(".prefab") && IsRelevantPath(s) && !entities.Contains(s))
                .OrderBy(s => s)
                .ToList();
            return new Dictionary<string, object>
            {
                ["relevantEntityPrefabs"] = relevant,
                ["relevantNonEntityPrefabs"] = relevantStrings,
                ["allEntityPrefabCount"] = entities.Count
            };
        }

        private static bool IsRelevantPath(string path)
        {
            foreach (string word in AnimalWords)
            {
                if (path.Contains(word))
                {
                    return true;
                }
            }
            return false;
        }

        private object DumpAnimals()
        {
            var result = new List<object>();
            foreach (string path in GameManifest.Current.entities)
            {
                GameObject prefab = GameManager.server.FindPrefab(path);
                if (prefab == null)
                {
                    continue;
                }
                BaseEntity entity = prefab.GetComponent<BaseEntity>();
                if (entity == null)
                {
                    continue;
                }
                bool isAnimal = entity is BaseNPC2 || entity is BaseNpc || entity is RidableHorse || entity is SnakeHazard || entity is SimpleShark || entity is BaseCorpse;
                if (!isAnimal)
                {
                    continue;
                }
                var row = new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["class"] = entity.GetType().FullName,
                    ["baseChain"] = BaseChain(entity.GetType()),
                    ["prefabID"] = entity.prefabID,
                    ["bounds"] = entity.bounds.ToString(),
                    ["components"] = prefab.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToList()
                };
                BaseCombatEntity combat = entity as BaseCombatEntity;
                if (combat != null)
                {
                    row["startHealth"] = combat.startHealth;
                }
                if (entity is BaseNPC2 npc2)
                {
                    row["displayName"] = SafeString(() => npc2.displayName);
                    row["categorize"] = SafeString(() => npc2.Categorize());
                    row["mass"] = npc2.RealisticMass;
                }
                row["entityFields"] = DescribeFields(entity, 2);
                var parts = new Dictionary<string, object>();
                foreach (Component component in prefab.GetComponents<Component>())
                {
                    if (component == null || component == entity || component is Transform)
                    {
                        continue;
                    }
                    string typeName = component.GetType().Name;
                    if (typeName.Contains("FSM") || typeName.Contains("Fsm") || typeName.Contains("Agent") || typeName.Contains("RootMotion")
                        || typeName.Contains("Sense") || typeName.Contains("Mountable") || typeName.Contains("Networking") || typeName.Contains("Encounter"))
                    {
                        parts[typeName] = DescribeFields(component, 3);
                    }
                }
                row["parts"] = parts;
                row["model"] = DescribeModel(prefab);
                result.Add(row);
            }
            return result;
        }

        private static object DescribeModel(GameObject prefab)
        {
            Model model = prefab.GetComponentInChildren<Model>(true);
            if (model == null)
            {
                return null;
            }
            var bones = new List<string>();
            if (model.rootBone != null)
            {
                foreach (Transform t in model.rootBone.GetComponentsInChildren<Transform>(true))
                {
                    bones.Add(t.name);
                    if (bones.Count >= 200)
                    {
                        break;
                    }
                }
            }
            return new Dictionary<string, object>
            {
                ["rootBone"] = model.rootBone != null ? model.rootBone.name : null,
                ["headBone"] = model.headBone != null ? model.headBone.name : null,
                ["eyeBone"] = model.eyeBone != null ? model.eyeBone.name : null,
                ["bones"] = bones
            };
        }

        private object DumpMountables()
        {
            var result = new List<object>();
            foreach (string path in GameManifest.Current.entities)
            {
                GameObject prefab = GameManager.server.FindPrefab(path);
                if (prefab == null)
                {
                    continue;
                }
                foreach (BaseMountable mountable in prefab.GetComponentsInChildren<BaseMountable>(true))
                {
                    result.Add(new Dictionary<string, object>
                    {
                        ["prefab"] = path,
                        ["class"] = mountable.GetType().Name,
                        ["isRoot"] = mountable.gameObject == prefab,
                        ["mountPose"] = mountable.mountPose.ToString(),
                        ["cameraMode"] = mountable.MountedCameraMode.ToString(),
                        ["canWieldItems"] = mountable.canWieldItems,
                        ["allowedGestures"] = mountable.allowedGestures.ToString(),
                        ["maxMountDistance"] = mountable.maxMountDistance,
                        ["hasEyeOverride"] = mountable.eyePositionOverride != null,
                        ["fields"] = DescribeFields(mountable, 1)
                    });
                }
            }
            return result;
        }

        private object DumpGestures()
        {
            var result = new List<object>();
            foreach (GestureConfig g in GestureCollection.Instance.AllGestures)
            {
                if (g == null)
                {
                    continue;
                }
                result.Add(new Dictionary<string, object>
                {
                    ["id"] = g.gestureId,
                    ["convar"] = g.convarName,
                    ["command"] = g.gestureCommand,
                    ["name"] = g.gestureName != null ? g.gestureName.english : null,
                    ["type"] = g.gestureType.ToString(),
                    ["viewMode"] = g.viewMode.ToString(),
                    ["movement"] = g.movementMode.ToString(),
                    ["layer"] = g.playerModelLayer.ToString(),
                    ["animation"] = g.animationType.ToString(),
                    ["duration"] = g.duration,
                    ["hideInWheel"] = g.hideInWheel,
                    ["forceUnlock"] = g.forceUnlock,
                    ["dlc"] = g.dlcItem != null,
                    ["steamItem"] = g.inventoryItem != null,
                    ["action"] = g.actionType.ToString()
                });
            }
            return result;
        }

        private object DumpItems()
        {
            var relevant = new List<object>();
            var all = new List<string>();
            foreach (ItemDefinition def in ItemManager.itemList)
            {
                all.Add(def.shortname);
                ItemModConsumable consumable = def.GetComponent<ItemModConsumable>();
                ItemModAnimalEquipment equipment = def.GetComponent<ItemModAnimalEquipment>();
                ItemModEntity heldEntity = def.GetComponent<ItemModEntity>();
                ItemModDeployable deployable = def.GetComponent<ItemModDeployable>();
                bool keyword = IsRelevantPath(def.shortname.ToLowerInvariant());
                if (consumable == null && equipment == null && !keyword)
                {
                    continue;
                }
                var row = new Dictionary<string, object>
                {
                    ["shortname"] = def.shortname,
                    ["name"] = def.displayName != null ? def.displayName.english : null,
                    ["itemid"] = def.itemid,
                    ["category"] = def.category.ToString(),
                    ["heldEntity"] = heldEntity != null && heldEntity.entityPrefab != null ? heldEntity.entityPrefab.resourcePath : null,
                    ["deployable"] = deployable != null && deployable.entityPrefab != null ? deployable.entityPrefab.resourcePath : null
                };
                if (consumable != null)
                {
                    row["effects"] = consumable.effects.Select(e => e.type + "=" + e.amount).ToList();
                }
                if (equipment != null)
                {
                    row["equipment"] = DescribeFields(equipment, 1);
                }
                relevant.Add(row);
            }
            return new Dictionary<string, object> { ["relevant"] = relevant, ["allShortnames"] = all.OrderBy(s => s).ToList() };
        }

        private object DumpHorse()
        {
            var result = new List<object>();
            foreach (string path in GameManifest.Current.entities)
            {
                GameObject prefab = GameManager.server.FindPrefab(path);
                RidableHorse horse = prefab != null ? prefab.GetComponent<RidableHorse>() : null;
                if (horse == null)
                {
                    continue;
                }
                var mounts = new List<object>();
                if (horse.mountPoints != null)
                {
                    foreach (BaseVehicle.MountPointInfo mp in horse.mountPoints)
                    {
                        mounts.Add(new Dictionary<string, object>
                        {
                            ["isDriver"] = mp.isDriver,
                            ["bone"] = mp.bone,
                            ["pos"] = mp.pos.ToString(),
                            ["rot"] = mp.rot.ToString(),
                            ["prefab"] = mp.prefab != null ? mp.prefab.resourcePath : null
                        });
                    }
                }
                result.Add(new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["mountPoints"] = mounts,
                    ["fields"] = DescribeFields(horse, 2)
                });
            }
            return result;
        }

        private static object DumpStatics(Type type)
        {
            var result = new Dictionary<string, object>();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                try
                {
                    object value = field.GetValue(null);
                    result[field.Name] = Simple(value) ?? (value != null ? value.GetType().Name : null);
                }
                catch (Exception)
                {
                    result[field.Name] = "?";
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ generic, cycle-safe reflection

        private static List<string> BaseChain(Type type)
        {
            var chain = new List<string>();
            while (type != null && type != typeof(object))
            {
                chain.Add(type.Name);
                type = type.BaseType;
            }
            return chain;
        }

        private static string SafeString(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception ex)
            {
                return "!" + ex.GetType().Name;
            }
        }

        private static object Simple(object value)
        {
            if (value == null)
            {
                return null;
            }
            Type type = value.GetType();
            if (type.IsPrimitive || value is string || value is decimal)
            {
                return value;
            }
            if (type.IsEnum)
            {
                return value.ToString();
            }
            if (value is Vector2 || value is Vector3 || value is Vector4 || value is Quaternion || value is Color || value is Bounds || value is LayerMask)
            {
                return value.ToString();
            }
            if (value is GameObjectRef goRef)
            {
                return "ref:" + goRef.resourcePath;
            }
            if (value is Translate.Phrase phrase)
            {
                return "phrase:" + phrase.english;
            }
            if (value is AnimationCurve curve)
            {
                Keyframe[] keys = curve.keys;
                return keys.Length == 0 ? "curve:empty" : $"curve:{keys.Length} keys, t {keys[0].time:0.##}..{keys[keys.Length - 1].time:0.##}, v {keys[0].value:0.##}..{keys[keys.Length - 1].value:0.##}";
            }
            if (value is IDictionary dictionary)
            {
                return "dictionary:" + dictionary.Count;
            }
            if (value is UnityEngine.Object unityObject)
            {
                if (unityObject == null)
                {
                    return "null-object";
                }
                string text = unityObject.GetType().Name + ":" + unityObject.name;
                PropertyInfo length = unityObject.GetType().GetProperty("length", BindingFlags.Instance | BindingFlags.Public);
                if (length != null && length.PropertyType == typeof(float))
                {
                    text += " len=" + ((float)length.GetValue(unityObject, null)).ToString("0.###");
                }
                return text;
            }
            return null;
        }

        private static object Describe(object value, int depth)
        {
            object simple = Simple(value);
            if (simple != null || value == null)
            {
                return simple;
            }
            Type type = value.GetType();
            if (typeof(Delegate).IsAssignableFrom(type) || type.Namespace != null && type.Namespace.StartsWith("System.Reflection"))
            {
                return type.Name;
            }
            if (value is IEnumerable enumerable)
            {
                var list = new List<object>();
                foreach (object item in enumerable)
                {
                    if (list.Count >= MaxListItems)
                    {
                        list.Add("...");
                        break;
                    }
                    list.Add(depth > 0 ? Describe(item, depth - 1) : (Simple(item) ?? item?.GetType().Name));
                }
                return list;
            }
            if (depth <= 0)
            {
                return type.Name;
            }
            return DescribeFields(value, depth);
        }

        // Reflects the serialized fields of an object; used directly for components and assets
        // (Describe() would only print their name because they are Unity objects).
        private static Dictionary<string, object> DescribeFields(object value, int depth)
        {
            Type type = value.GetType();
            var fields = new Dictionary<string, object>();
            for (Type t = type; t != null && t != typeof(object) && t != typeof(MonoBehaviour) && t != typeof(Component); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                {
                    if (fields.ContainsKey(field.Name) || field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                    {
                        continue;
                    }
                    bool serialized = field.IsPublic || field.IsDefined(typeof(SerializeField), false);
                    if (!serialized)
                    {
                        continue;
                    }
                    object fieldValue;
                    try
                    {
                        fieldValue = field.GetValue(value);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (fieldValue is ScriptableObject so && so != null)
                    {
                        fields[field.Name] = depth > 1 ? new Dictionary<string, object> { ["asset"] = Simple(so), ["fields"] = DescribeFields(so, depth - 2) } : Simple(so);
                        continue;
                    }
                    if (fieldValue is UnityEngine.Object)
                    {
                        fields[field.Name] = Simple(fieldValue);
                        continue;
                    }
                    fields[field.Name] = Describe(fieldValue, depth - 1);
                }
            }
            return fields;
        }
    }
}
