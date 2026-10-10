// ------------------------------------------------------------------------------------------
// Beastmaster - generated file. DO NOT EDIT: edit src/*.cs and run tools/build.ps1.
// Modules: Beastmaster.cs, Animals.cs, Boss.cs, Climate.cs, Combat.cs, Commands.cs, Config.cs, Control.cs, Data.cs, Follower.cs, Form.cs, Gear.cs, Guests.cs, Horse.cs, Interact.cs, Lang.cs, Lead.cs, Menu.cs, Movement.cs, Play.cs, Ritual.cs, Species.cs, Spray.cs, Stats.cs, Tame.cs, TameFlows.cs, Tests.cs, Util.cs, Wild.cs
// ------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    // ======================================== Beastmaster.cs
    [Info("Beastmaster", "Claude", "1.0.0")]
    [Description("Ride, tame, command and become animals: horses, livestock, predators and small creatures.")]
    public partial class Beastmaster : RustPlugin
    {
        internal const string PermUse = "beastmaster.use";
        internal const string PermRide = "beastmaster.ride";
        internal const string PermRideRestricted = "beastmaster.ride.restricted";
        internal const string PermTame = "beastmaster.tame";
        internal const string PermTransform = "beastmaster.transform";
        internal const string PermTransformRestricted = "beastmaster.transform.restricted";
        internal const string PermSpray = "beastmaster.spray";
        internal const string PermMenu = "beastmaster.menu";
        internal const string PermAdmin = "beastmaster.admin";

        internal static Beastmaster Instance;
        internal ConfigData Cfg;
        internal StoredData Data;
        internal readonly List<Module> Modules = new List<Module>();
        internal bool Ready;

        // ---------------------------------------------------------------- modules

        // A part of the plugin. Modules are found by reflection (every nested non-abstract class derived from Module); a module
        // overrides the hooks it needs, the plugin class dispatches each Oxide hook to every module in order. The first
        // module that returns a non-null value from a hook decides it.
        internal abstract class Module
        {
            protected Beastmaster Bm;
            public virtual string Name => GetType().Name;
            public virtual int Order => 100;
            public virtual void Init(Beastmaster bm) { Bm = bm; }
            public virtual void OnServerInitialized() { }
            public virtual void Unload() { }
            public virtual void OnSave() { }
            public virtual void Tick(float now) { }
            public virtual void OnDisable() { }
            public virtual void OnEnable() { }
            public virtual void SelfTest(List<string> report) { }
            // English texts of the module (key -> text); registered with the lang system after the modules are created.
            public virtual Dictionary<string, string> Messages() { return null; }

            public virtual object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info) { return null; }
            public virtual object OnPlayerInput(BasePlayer player, InputState input) { return null; }
            public virtual object OnServerCommand(BasePlayer player, ConsoleSystem.Arg arg) { return null; }
            public virtual object OnMapMarkerAdd(BasePlayer player, ProtoBuf.MapNote note) { return null; }
            public virtual object CanUseGesture(BasePlayer player, GestureConfig gesture) { return null; }
            public virtual void OnEntityDeath(BaseCombatEntity entity, HitInfo info) { }
            public virtual void OnEntityKill(BaseNetworkable entity) { }
            public virtual void OnEntitySpawned(BaseNetworkable entity) { }
            public virtual void OnPlayerConnected(BasePlayer player) { }
            public virtual void OnPlayerDisconnected(BasePlayer player, string reason) { }
            public virtual void OnPlayerRespawned(BasePlayer player) { }
            public virtual object CanMountEntity(BasePlayer player, BaseMountable mountable) { return null; }
            public virtual void OnEntityMounted(BaseMountable mountable, BasePlayer player) { }
            public virtual void OnEntityDismounted(BaseMountable mountable, BasePlayer player) { }
            public virtual object OnPlayerWantsDismount(BasePlayer player, BaseMountable mountable) { return null; }
            public virtual object CanBeTargeted(BaseCombatEntity target, MonoBehaviour turret) { return null; }
            public virtual object CanNetworkTo(BaseNetworkable entity, BasePlayer target) { return null; }
            public virtual object CanLootEntity(BasePlayer player, BaseEntity entity) { return null; }
            public virtual object OnPlayerAttack(BasePlayer attacker, HitInfo info) { return null; }
            public virtual object OnTrapTrigger(BaseTrap trap, GameObject go) { return null; }
            public virtual void OnPlayerDeath(BasePlayer player, HitInfo info) { }
            public virtual object OnItemUse(Item item, int amount) { return null; }
            public virtual object OnPlayerCommand(BasePlayer player, string command, string[] args) { return null; }
            public virtual object CanAcceptItem(ItemContainer container, Item item, int targetPos, BasePlayer player) { return null; }
            public virtual void OnItemAddedToContainer(ItemContainer container, Item item, BasePlayer sourcePlayer) { }
            public virtual void OnItemRemovedFromContainer(ItemContainer container, Item item, BasePlayer sourcePlayer) { }
            public virtual object OnHorseLead(RidableHorse horse, BasePlayer player) { return null; }
            public virtual object OnSprayCreate(SprayCan spray, Vector3 position, Quaternion rotation) { return null; }
            public virtual object OnItemAction(Item item, string action, BasePlayer player) { return null; }
            public virtual object OnHealingItemUse(MedicalTool tool, BasePlayer fromPlayer, IMedicalToolTarget toTarget) { return null; }
            public virtual void OnLootEntityEnd(BasePlayer player, StorageContainer container) { }
        }

        private void Init()
        {
            Instance = this;
            RegisterPermissions();
            foreach (Type t in typeof(Beastmaster).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!t.IsAbstract && typeof(Module).IsAssignableFrom(t))
                {
                    Modules.Add((Module)Activator.CreateInstance(t));
                }
            }
            Modules.Sort((a, b) => a.Order.CompareTo(b.Order));
            RegisterMessages();
            foreach (Module module in Modules)
            {
                Dictionary<string, string> texts = module.Messages();
                if (texts != null)
                {
                    lang.RegisterMessages(texts, this);
                }
            }
            // Hot hooks nobody needs yet stay switched off; a module that needs one calls Subscribe in its Init.
            foreach (string hook in new[] { "CanNetworkTo", "OnEntitySpawned", "CanLootEntity", "OnPlayerAttack", "OnTrapTrigger", "OnItemUse", "OnPlayerRespawned" })
            {
                Unsubscribe(hook);
            }
            LoadData();
            if (Cfg.General.AutoEnable)
            {
                Data.Enabled = true;               // loaded = active, whatever the last session ended with
            }
            foreach (Module m in Modules)
            {
                Guard(m.Name + ".Init", () => m.Init(this));
            }
        }

        private void OnServerInitialized()
        {
            ResolveSpeciesPrefabs();
            Ready = true;
            foreach (Module m in Modules)
            {
                Guard(m.Name + ".OnServerInitialized", m.OnServerInitialized);
            }
            timer.Every(0.25f, TickModules);
            Log(2, "Beastmaster " + Version + " ready: " + AllSpecies.Count(s => s.Available) + " species, " + Modules.Count + " modules, " + (Data.Enabled ? "ON" : "OFF"));
        }

        private void Unload()
        {
            Ready = false;
            foreach (Module m in Modules.AsEnumerable().Reverse().ToList())
            {
                Module module = m;
                Guard(module.Name + ".Unload", module.Unload);
            }
            SaveData();
            Instance = null;
        }

        private void OnServerSave()
        {
            foreach (Module m in Modules)
            {
                Module module = m;
                Guard(module.Name + ".OnSave", module.OnSave);
            }
            SaveData();
        }

        protected override void LoadDefaultConfig()
        {
            Cfg = new ConfigData();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Cfg = Config.ReadObject<ConfigData>();
            }
            catch (Exception ex)
            {
                PrintWarning("Config file is broken (" + ex.Message + ") - defaults loaded. Fix the file or delete it.");
            }
            if (Cfg == null)
            {
                Cfg = new ConfigData();
            }
            ValidateConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(Cfg, true);
        }

        private void ValidateConfig()
        {
            Cfg.Movement.WaterMinSpeed = Mathf.Clamp(Cfg.Movement.WaterMinSpeed, 0.05f, 1f);
            Cfg.Movement.WaterWalkOnly = Mathf.Clamp(Cfg.Movement.WaterWalkOnly, 0.1f, 1f);
            Cfg.Combat.Cooldown = Mathf.Max(0.2f, Cfg.Combat.Cooldown);
            Cfg.Lead.BreakDistance = Mathf.Max(8f, Cfg.Lead.BreakDistance);
            Cfg.Camera.DefaultView = Cfg.Camera.DefaultView == "first" ? "first" : "third";
            string preset = (Cfg.Camera.ThirdPreset ?? "A").ToUpperInvariant();
            Cfg.Camera.ThirdPreset = preset == "B" || preset == "C" ? preset : "A";
            if (Cfg.Movement.SpeedMode != "table")
            {
                Cfg.Movement.SpeedMode = "authored";
            }
            // a list that was written twice into the file (an older build added to the defaults) is tidied: first entry per key wins
            Cfg.Wild.AlwaysDefend = (Cfg.Wild.AlwaysDefend ?? new List<string>()).Select(s => (s ?? string.Empty).ToLowerInvariant()).Distinct().ToList();
            Cfg.Wild.MayDefend = (Cfg.Wild.MayDefend ?? new List<string>()).Select(s => (s ?? string.Empty).ToLowerInvariant()).Distinct().ToList();
            Cfg.Relations.Rules = (Cfg.Relations.Rules ?? new List<RelationRule>()).Where(r => r != null && !string.IsNullOrEmpty(r.Observer) && !string.IsNullOrEmpty(r.Subject))
                .GroupBy(r => r.Observer.ToLowerInvariant() + ">" + r.Subject.ToLowerInvariant()).Select(g => g.First()).ToList();
            float sum = Cfg.Horse.CalmShare + Cfg.Horse.ShyShare + Cfg.Horse.AggressiveShare;
            if (sum <= 0f)
            {
                Cfg.Horse.CalmShare = 0.4f;
                Cfg.Horse.ShyShare = 0.35f;
                Cfg.Horse.AggressiveShare = 0.25f;
            }
        }

        // ---------------------------------------------------------------- on / off

        internal bool Active => Data != null && Data.Enabled && Ready;

        internal void SetActive(bool on)
        {
            if (Data.Enabled == on)
            {
                return;
            }
            Data.Enabled = on;
            foreach (Module m in Modules)
            {
                Module module = m;
                Guard(module.Name + (on ? ".OnEnable" : ".OnDisable"), on ? new Action(module.OnEnable) : module.OnDisable);
            }
            SaveData();
            Log(2, "Beastmaster switched " + (on ? "ON" : "OFF"));
        }

        // ---------------------------------------------------------------- permissions and access

        private void RegisterPermissions()
        {
            foreach (string perm in new[] { PermUse, PermRide, PermRideRestricted, PermTame, PermTransform, PermTransformRestricted, PermSpray, PermMenu, PermAdmin })
            {
                permission.RegisterPermission(perm, this);
            }
        }

        internal bool Has(BasePlayer player, string perm)
        {
            return player != null && (permission.UserHasPermission(player.UserIDString, perm) || permission.UserHasPermission(player.UserIDString, PermAdmin) || IsOwner(player));
        }

        internal bool IsOwner(BasePlayer player)
        {
            return player != null && player.IsAdmin && Cfg.Access.OwnersAuthorised;
        }

        internal bool IsModerator(BasePlayer player)
        {
            return player != null && Cfg.Access.ModeratorsAuthorised && ServerUsers.Is(player.userID, ServerUsers.UserGroup.Moderator);
        }

        // The control layer of the menu (admin, spawn, players): owners, moderators and everyone granted beastmaster.menu.
        internal bool HasMenuAccess(BasePlayer player)
        {
            return player != null && (IsOwner(player) || IsModerator(player) || permission.UserHasPermission(player.UserIDString, PermMenu) || permission.UserHasPermission(player.UserIDString, PermAdmin));
        }

        internal bool IsAdmin(BasePlayer player)
        {
            return player != null && (IsOwner(player) || permission.UserHasPermission(player.UserIDString, PermAdmin));
        }

        // ---------------------------------------------------------------- logging and messages

        internal void Log(int level, string text)
        {
            if (Cfg != null && level > Cfg.General.LogLevel)
            {
                return;
            }
            if (level <= 0)
            {
                PrintError(text);
            }
            else if (level == 1)
            {
                PrintWarning(text);
            }
            else
            {
                Puts(text);
            }
        }

        private readonly Dictionary<string, float> errorTimes = new Dictionary<string, float>();

        // Runs code so that no exception escapes into the game; an error is logged once a minute per place.
        internal void Guard(string where, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                float last;
                if (!errorTimes.TryGetValue(where, out last) || Time.realtimeSinceStartup - last > 60f)
                {
                    errorTimes[where] = Time.realtimeSinceStartup;
                    PrintError("Error in " + where + ": " + ex);
                }
            }
        }

        internal void Tell(BasePlayer player, string key, params object[] args)
        {
            if (player != null && player.IsConnected)
            {
                player.ChatMessage(Cfg.General.Prefix + L(key, player.UserIDString, args));
            }
        }

        internal void TellRaw(BasePlayer player, string text)
        {
            if (player != null && player.IsConnected)
            {
                player.ChatMessage(Cfg.General.Prefix + text);
            }
        }

        internal string L(string key, string userId = null, params object[] args)
        {
            string text = lang.GetMessage(key, this, userId);
            if (args != null && args.Length > 0)
            {
                try
                {
                    text = string.Format(text, args);
                }
                catch (FormatException)
                {
                }
            }
            return text;
        }

        // ---------------------------------------------------------------- ticking

        private void TickModules()
        {
            if (!Active)
            {
                return;
            }
            float now = Time.time;
            for (int i = 0; i < Modules.Count; i++)
            {
                Module m = Modules[i];
                try
                {
                    m.Tick(now);
                }
                catch (Exception ex)
                {
                    Log(0, "Tick of module " + m.Name + " failed: " + ex.Message);
                }
            }
        }

        // ---------------------------------------------------------------- hook dispatch

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!Active)
            {
                return null;
            }
            for (int i = 0; i < Modules.Count; i++)
            {
                try
                {
                    object r = Modules[i].OnEntityTakeDamage(entity, info);
                    if (r != null)
                    {
                        return r;
                    }
                }
                catch (Exception ex)
                {
                    ReportHookError("OnEntityTakeDamage", Modules[i], ex);
                }
            }
            return null;
        }

        private object OnPlayerInput(BasePlayer player, InputState input)
        {
            if (!Active || player == null || input == null)
            {
                return null;
            }
            for (int i = 0; i < Modules.Count; i++)
            {
                try
                {
                    object r = Modules[i].OnPlayerInput(player, input);
                    if (r != null)
                    {
                        return r;
                    }
                }
                catch (Exception ex)
                {
                    ReportHookError("OnPlayerInput", Modules[i], ex);
                }
            }
            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (!Active || arg == null || arg.cmd == null || arg.Connection == null)
            {
                return null;
            }
            BasePlayer player = arg.Player();
            if (player == null)
            {
                return null;
            }
            for (int i = 0; i < Modules.Count; i++)
            {
                try
                {
                    object r = Modules[i].OnServerCommand(player, arg);
                    if (r != null)
                    {
                        return r;
                    }
                }
                catch (Exception ex)
                {
                    ReportHookError("OnServerCommand", Modules[i], ex);
                }
            }
            return null;
        }

        private object OnMapMarkerAdd(BasePlayer player, ProtoBuf.MapNote note)
        {
            return Dispatch("OnMapMarkerAdd", m => m.OnMapMarkerAdd(player, note));
        }

        private object CanUseGesture(BasePlayer player, GestureConfig gesture)
        {
            return Dispatch("CanUseGesture", m => m.CanUseGesture(player, gesture));
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            // an animal the plugin holds (parked AI) never reaches its death state: its AI is given back right after the game told it about the death
            BaseNPC2 npc = entity as BaseNPC2;
            if (npc != null && npc.net != null && (IsParked(npc) || Followers.ContainsKey(npc.net.ID.Value)))
            {
                NextTick(() => ReviveForDeath(npc, info));
            }
            DispatchVoid("OnEntityDeath", m => m.OnEntityDeath(entity, info));
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            DispatchVoid("OnEntityKill", m => m.OnEntityKill(entity));
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (!Active)
            {
                return;
            }
            for (int i = 0; i < Modules.Count; i++)
            {
                try
                {
                    Modules[i].OnEntitySpawned(entity);
                }
                catch (Exception ex)
                {
                    ReportHookError("OnEntitySpawned", Modules[i], ex);
                }
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            DispatchVoid("OnPlayerConnected", m => m.OnPlayerConnected(player));
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            // runs even while switched off so nobody stays controlled
            foreach (Module m in Modules)
            {
                Module module = m;
                Guard(module.Name + ".OnPlayerDisconnected", () => module.OnPlayerDisconnected(player, reason));
            }
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            DispatchVoid("OnPlayerRespawned", m => m.OnPlayerRespawned(player));
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            DispatchVoid("OnPlayerDeath", m => m.OnPlayerDeath(player, info));
        }

        private object CanMountEntity(BasePlayer player, BaseMountable mountable)
        {
            return Dispatch("CanMountEntity", m => m.CanMountEntity(player, mountable));
        }

        private void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            DispatchVoid("OnEntityMounted", m => m.OnEntityMounted(mountable, player));
        }

        private void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
        {
            DispatchVoid("OnEntityDismounted", m => m.OnEntityDismounted(mountable, player));
        }

        private object CanAcceptItem(ItemContainer container, Item item, int targetPos, BasePlayer player)
        {
            if (Boxes.Count == 0)
            {
                return null;
            }
            return Dispatch("CanAcceptItem", m => m.CanAcceptItem(container, item, targetPos, player));
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item, BasePlayer sourcePlayer)
        {
            if (Boxes.Count == 0 && (Stable == null || Stable.Count == 0))
            {
                return;
            }
            DispatchVoid("OnItemAddedToContainer", m => m.OnItemAddedToContainer(container, item, sourcePlayer));
        }

        private void OnItemRemovedFromContainer(ItemContainer container, Item item, BasePlayer sourcePlayer)
        {
            if (Stable == null || Stable.Count == 0)
            {
                return;
            }
            DispatchVoid("OnItemRemovedFromContainer", m => m.OnItemRemovedFromContainer(container, item, sourcePlayer));
        }

        private object OnHorseLead(RidableHorse horse, BasePlayer player)
        {
            return Dispatch("OnHorseLead", m => m.OnHorseLead(horse, player));
        }

        private object OnSprayCreate(SprayCan spray, Vector3 position, Quaternion rotation)
        {
            return Dispatch("OnSprayCreate", m => m.OnSprayCreate(spray, position, rotation));
        }

        private object OnItemAction(Item item, string action, BasePlayer player)
        {
            if (Drivers.Count == 0)
            {
                return null;
            }
            return Dispatch("OnItemAction", m => m.OnItemAction(item, action, player));
        }

        private object OnHealingItemUse(MedicalTool tool, BasePlayer fromPlayer, IMedicalToolTarget toTarget)
        {
            if (Drivers.Count == 0)
            {
                return null;
            }
            return Dispatch("OnHealingItemUse", m => m.OnHealingItemUse(tool, fromPlayer, toTarget));
        }

        private void OnLootEntityEnd(BasePlayer player, StorageContainer container)
        {
            if (Boxes.Count == 0)
            {
                return;
            }
            DispatchVoid("OnLootEntityEnd", m => m.OnLootEntityEnd(player, container));
        }

        private object OnPlayerWantsDismount(BasePlayer player, BaseMountable mountable)
        {
            return Dispatch("OnPlayerWantsDismount", m => m.OnPlayerWantsDismount(player, mountable));
        }

        private object CanBeTargeted(BaseCombatEntity target, MonoBehaviour turret)
        {
            return Dispatch("CanBeTargeted", m => m.CanBeTargeted(target, turret));
        }

        private object CanNetworkTo(BaseNetworkable entity, BasePlayer target)
        {
            if (!Active || Modules.Count == 0)
            {
                return null;
            }
            for (int i = 0; i < Modules.Count; i++)
            {
                try
                {
                    object r = Modules[i].CanNetworkTo(entity, target);
                    if (r != null)
                    {
                        return r;
                    }
                }
                catch (Exception ex)
                {
                    ReportHookError("CanNetworkTo", Modules[i], ex);
                }
            }
            return null;
        }

        private object CanLootEntity(BasePlayer player, BaseEntity entity)
        {
            return Dispatch("CanLootEntity", m => m.CanLootEntity(player, entity));
        }

        private object OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            return Dispatch("OnPlayerAttack", m => m.OnPlayerAttack(attacker, info));
        }

        private object OnTrapTrigger(BaseTrap trap, GameObject go)
        {
            return Dispatch("OnTrapTrigger", m => m.OnTrapTrigger(trap, go));
        }

        private object OnItemUse(Item item, int amount)
        {
            return Dispatch("OnItemUse", m => m.OnItemUse(item, amount));
        }

        private object Dispatch(string hook, Func<Module, object> call)
        {
            if (!Active)
            {
                return null;
            }
            for (int i = 0; i < Modules.Count; i++)
            {
                try
                {
                    object r = call(Modules[i]);
                    if (r != null)
                    {
                        return r;
                    }
                }
                catch (Exception ex)
                {
                    ReportHookError(hook, Modules[i], ex);
                }
            }
            return null;
        }

        private void DispatchVoid(string hook, Action<Module> call)
        {
            if (!Active)
            {
                return;
            }
            for (int i = 0; i < Modules.Count; i++)
            {
                try
                {
                    call(Modules[i]);
                }
                catch (Exception ex)
                {
                    ReportHookError(hook, Modules[i], ex);
                }
            }
        }

        private void ReportHookError(string hook, Module module, Exception ex)
        {
            string where = hook + "/" + module.Name;
            float last;
            if (!errorTimes.TryGetValue(where, out last) || Time.realtimeSinceStartup - last > 60f)
            {
                errorTimes[where] = Time.realtimeSinceStartup;
                PrintError("Error in hook " + hook + " (module " + module.Name + "): " + ex);
            }
        }

        // ---------------------------------------------------------------- small helpers shared by the modules

        internal static bool Valid(BaseEntity e)
        {
            return e != null && !e.IsDestroyed && e.net != null;
        }

        internal static bool Alive(BaseCombatEntity e)
        {
            return Valid(e) && !e.IsDead();
        }

        internal static float Yaw(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
    }

    // ======================================== Animals.cs
    public partial class Beastmaster
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal const string ParkedStateName = "BmParked";

        // ---------------------------------------------------------------- parking the AI
        // Taking over a Gen2 animal cleanly (verified in the spike): the FSM leaves its current state through the game's own exit
        // code (this stops the state's montage and clears grazing / drinking flags), sits in an empty state and stops ticking.
        // Several owners may hold the same animal; its own AI comes back when the last one lets go.

        private readonly Dictionary<ulong, HashSet<string>> parkReasons = new Dictionary<ulong, HashSet<string>>();
        private readonly Dictionary<ulong, FSMStateBase> parkReturn = new Dictionary<ulong, FSMStateBase>();

        internal bool IsParked(BaseNPC2 animal)
        {
            return animal != null && animal.net != null && parkReasons.ContainsKey(animal.net.ID.Value);
        }

        internal void Park(BaseNPC2 animal, string reason)
        {
            if (!Valid(animal))
            {
                return;
            }
            ulong id = animal.net.ID.Value;
            HashSet<string> reasons;
            if (!parkReasons.TryGetValue(id, out reasons))
            {
                reasons = new HashSet<string>();
                parkReasons[id] = reasons;
                FSMComponent fsm = animal.GetComponent<FSMComponent>();
                FSMStateBase previous = null;
                if (fsm != null)
                {
                    FSMStateBase current = fsm.CurrentState;
                    if (current == null || current.Name != ParkedStateName)
                    {
                        previous = current;
                        fsm.SetState(new State_Nothing { Name = ParkedStateName });
                    }
                    fsm.SetFsmActive(false);
                }
                parkReturn[id] = previous;
                ClearLivestockPose(animal);
                RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
                if (agent != null)
                {
                    agent.ResetPath();
                }
            }
            reasons.Add(reason);
        }

        internal void Unpark(BaseNPC2 animal, string reason)
        {
            if (animal == null || animal.net == null)
            {
                return;
            }
            ulong id = animal.net.ID.Value;
            HashSet<string> reasons;
            if (!parkReasons.TryGetValue(id, out reasons))
            {
                return;
            }
            reasons.Remove(reason);
            if (reasons.Count > 0)
            {
                return;
            }
            parkReasons.Remove(id);
            FSMStateBase back;
            parkReturn.TryGetValue(id, out back);
            parkReturn.Remove(id);
            if (animal.IsDestroyed || animal.IsDead())
            {
                return;
            }
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            if (fsm == null)
            {
                return;
            }
            FSMStateBase target = back ?? DefaultIdleState(fsm);
            fsm.SetFsmActive(true);
            if (target != null)
            {
                fsm.SetState(target);
            }
        }

        // A parked animal that dies never reaches its death state: its AI does not tick, and the one tick that would take the death transition
        // is lost when the plugin puts the AI back (entering a state resets the transition's trigger). So the AI is switched on and put straight
        // into its own dead state, with the hit that killed it: death animation, body, loot, exactly like an animal that was never held.
        internal void ReviveForDeath(BaseNPC2 animal, HitInfo info = null)
        {
            if (animal == null || animal.IsDestroyed || animal.net == null)
            {
                return;
            }
            ulong id = animal.net.ID.Value;
            Follower f = FollowerOf(animal);
            if (f != null)
            {
                Followers.Remove(id);
                f.End();
            }
            parkReasons.Remove(id);
            parkReturn.Remove(id);
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            if (fsm == null)
            {
                return;
            }
            RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
            if (agent != null)
            {
                agent.IsJumping = false;
                agent.ResetPath();
            }
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock != null)
            {
                ClearLivestockPose(animal);
            }
            fsm.SetFsmActive(true);
            FSMStateBase dead = FindDeadState(fsm);
            if (dead != null)
            {
                FSMPayload payload = default(FSMPayload);
                if (info != null)
                {
                    HitInfo copy = new HitInfo();
                    copy.CopyFrom(info);
                    payload.hitInfo = copy;
                }
                fsm.SetState(dead, payload);
                return;
            }
            FSMStateBase idle = DefaultIdleState(fsm);
            if (idle != null)
            {
                fsm.SetState(idle);
            }
        }

        private static FSMStateBase FindDeadState(FSMComponent fsm)
        {
            for (Type t = fsm.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    if (typeof(State_Dead).IsAssignableFrom(f.FieldType))
                    {
                        FSMStateBase state = f.GetValue(fsm) as FSMStateBase;
                        if (state != null)
                        {
                            return state;
                        }
                    }
                }
            }
            return null;
        }

        // Plugin unload or switch-off: every animal the plugin held gets its own AI back.
        internal void ReleaseAllParked()
        {
            foreach (ulong id in parkReasons.Keys.ToList())
            {
                BaseNetworkable net = BaseNetworkable.serverEntities.Find(new NetworkableId(id));
                BaseNPC2 animal = net as BaseNPC2;
                if (animal != null)
                {
                    parkReasons[id].Clear();
                    Unpark(animal, "none");
                }
            }
            parkReasons.Clear();
            parkReturn.Clear();
        }

        internal void ForgetPark(BaseNetworkable entity)
        {
            if (entity != null && entity.net != null)
            {
                parkReasons.Remove(entity.net.ID.Value);
                parkReturn.Remove(entity.net.ID.Value);
            }
        }

        private static void ClearLivestockPose(BaseNPC2 animal)
        {
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock != null)
            {
                livestock.SetGrazing(false);
                livestock.SetDrinking(false);
                livestock.SetMating(false);
                livestock.SetResting(false);
                livestock.SetLyingIn(false);
                livestock.SetSleeping(false);
            }
            if (animal.HasFlag(BaseEntity.Flags.Reserved6))
            {
                animal.SetFlagLocal(BaseEntity.Flags.Reserved6, false);
                animal.SendNetworkUpdate();
            }
        }

        private static FSMStateBase DefaultIdleState(FSMComponent fsm)
        {
            var states = new Dictionary<string, FSMStateBase>();
            for (Type t = fsm.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(FSMStateBase).IsAssignableFrom(f.FieldType))
                    {
                        continue;
                    }
                    FSMStateBase state = f.GetValue(fsm) as FSMStateBase;
                    string key = f.Name.ToLowerInvariant();
                    if (state != null && !states.ContainsKey(key))
                    {
                        states[key] = state;
                    }
                }
            }
            foreach (string name in new[] { "randomidle", "idle", "roam", "wander" })
            {
                FSMStateBase state;
                if (states.TryGetValue(name, out state))
                {
                    return state;
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- clips (animations with their sounds)

        internal sealed class ClipRef
        {
            public string State;
            public string Field;
            public string Name;
            public string Kind;
            public UnityEngine.Object Asset;
        }

        private readonly Dictionary<string, List<ClipRef>> clipCache = new Dictionary<string, List<ClipRef>>();

        internal static bool Has(ClipRef clip, string word)
        {
            return clip.State.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 || clip.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Every AnimationClip / RootMotionData the animal's FSM states hold (one level into each state, plus arrays).
        // The list is discovered from the animal itself, so a game update that renames or adds clips is picked up.
        internal List<ClipRef> Clips(BaseNPC2 animal)
        {
            string key = animal.ShortPrefabName;
            List<ClipRef> cached;
            if (clipCache.TryGetValue(key, out cached) && cached.All(c => c.Asset != null))
            {
                return cached;
            }
            var list = new List<ClipRef>();
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            if (fsm != null)
            {
                for (Type t = fsm.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    foreach (FieldInfo stateField in t.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(FSMStateBase).IsAssignableFrom(stateField.FieldType))
                        {
                            continue;
                        }
                        object state = stateField.GetValue(fsm);
                        if (state == null)
                        {
                            continue;
                        }
                        for (Type st = state.GetType(); st != null && st != typeof(object); st = st.BaseType)
                        {
                            foreach (FieldInfo f in st.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                            {
                                object value = f.GetValue(state);
                                AddClip(list, stateField.Name, f.Name, value);
                                var array = value as System.Collections.IEnumerable;
                                if (array != null && !(value is string) && !(value is UnityEngine.Object))
                                {
                                    int i = 0;
                                    foreach (object element in array)
                                    {
                                        AddClip(list, stateField.Name, f.Name + "[" + i++ + "]", element);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            clipCache[key] = list;
            return list;
        }

        private static void AddClip(List<ClipRef> list, string state, string field, object value)
        {
            var asset = value as UnityEngine.Object;
            if (asset == null)
            {
                return;
            }
            string typeName = asset.GetType().Name;
            if (typeName != "AnimationClip" && typeName != "RootMotionData")
            {
                return;
            }
            if (list.Any(c => c.Asset == asset))
            {
                return;
            }
            list.Add(new ClipRef { State = state, Field = field, Name = asset.name, Kind = typeName, Asset = asset });
        }

        // Plays a clip through the game's own montage channel (RootMotionPlayer -> client CL_PlayMontageDelayed).
        // RootMotionPlayer's overloads mention AnimationClip, which plugins cannot name without an extra reference,
        // so the call goes through reflection.
        internal static bool PlayClip(BaseNPC2 animal, ClipRef clip)
        {
            return PlayClipState(animal, clip) != null;
        }

        // The state of a montage that is playing (null = nothing played); it can be handed to StopClipState.
        internal static object PlayClipState(BaseNPC2 animal, ClipRef clip)
        {
            RootMotionPlayer player = animal.GetComponent<RootMotionPlayer>();
            if (player == null || clip == null || clip.Asset == null)
            {
                return null;
            }
            if (clip.Asset is RootMotionData)
            {
                // root-motion clips move the animal; play the in-place version so the controller keeps steering
                RootMotionData rm = (RootMotionData)clip.Asset;
                object inPlace = typeof(RootMotionData).GetField("inPlaceAnimation", AnyInstance)?.GetValue(rm);
                if (inPlace != null)
                {
                    return InvokeClip(player, inPlace);
                }
                MethodInfo rmMethod = typeof(RootMotionPlayer).GetMethods(AnyInstance)
                    .FirstOrDefault(m => m.Name == "PlayServerAndTakeFromPool" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(RootMotionData));
                if (rmMethod == null)
                {
                    return null;
                }
                return rmMethod.Invoke(player, new object[] { rm }) ?? new object();
            }
            return InvokeClip(player, clip.Asset);
        }

        private static object InvokeClip(RootMotionPlayer player, object animationClip)
        {
            MethodInfo method = typeof(RootMotionPlayer).GetMethods(AnyInstance)
                .FirstOrDefault(m => m.Name == "PlayServerAndTakeFromPool" && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == animationClip.GetType());
            if (method == null)
            {
                return null;
            }
            return method.Invoke(player, new object[] { animationClip, 1f, false }) ?? new object();
        }

        // Stops a montage that is still playing (the animal's own idle animation gives way to what the player does).
        internal static void StopClipState(BaseNPC2 animal, ref object state)
        {
            object held = state;
            state = null;
            if (held == null || animal == null || animal.IsDestroyed)
            {
                return;
            }
            RootMotionPlayer player = animal.GetComponent<RootMotionPlayer>();
            MethodInfo stop = typeof(RootMotionPlayer).GetMethod("StopServerAndReturnToPool", AnyInstance);
            if (player == null || stop == null)
            {
                return;
            }
            try
            {
                stop.Invoke(player, new object[] { held, true });
            }
            catch (Exception)
            {
            }
        }

        internal ClipRef FindClip(BaseNPC2 animal, params string[] words)
        {
            foreach (string word in words)
            {
                ClipRef clip = Clips(animal).FirstOrDefault(c => Has(c, word));
                if (clip != null)
                {
                    return clip;
                }
            }
            return null;
        }

        internal bool Play(BaseNPC2 animal, params string[] words)
        {
            ClipRef clip = FindClip(animal, words);
            return clip != null && PlayClip(animal, clip);
        }

        // ---------------------------------------------------------------- attacks of the game

        internal struct AttackParams
        {
            public float Damage;
            public float Delay;
            public Rust.DamageType Type;
            public float Range;
            public float Angle;
            public Vector2 Push;
        }

        // The game's own numbers for the attack that owns this clip (State_Attack / State_ShovingAttack fields).
        internal static AttackParams ReadAttack(BaseNPC2 animal, ClipRef clip, bool rear)
        {
            AttackParams p = new AttackParams
            {
                Damage = 20f,
                Delay = 0.5f,
                Type = Rust.DamageType.Bite,
                Range = Mathf.Max(2.2f, animal.bounds.extents.z + 1.5f),
                Angle = rear ? 180f : 60f,
                Push = Vector2.zero
            };
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            object state = null;
            for (Type t = fsm != null ? fsm.GetType() : null; t != null && state == null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(clip.State, AnyInstance | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    state = f.GetValue(fsm);
                }
            }
            if (state == null)
            {
                return p;
            }
            for (Type t = state.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    object v = f.GetValue(state);
                    switch (f.Name)
                    {
                        case "Damage": if (v is float) p.Damage = (float)v; break;
                        case "Delay": if (v is float) p.Delay = (float)v; break;
                        case "DamageType": if (v is Rust.DamageType) p.Type = (Rust.DamageType)v; break;
                        case "hitRange": if (v is float) p.Range = (float)v; break;
                        case "hitAngle": if (v is float) p.Angle = rear ? Mathf.Max(90f, (float)v) : (float)v; break;
                        case "pushForce": if (v is Vector2) p.Push = (Vector2)v; break;
                    }
                }
            }
            return p;
        }

        // ---------------------------------------------------------------- shape helpers

        // The middle of the animal's back in its local space: the highest spine bone in the middle part of the body,
        // or the bounds when the model has no spine bones.
        internal static Vector3 BackPoint(BaseNPC2 animal)
        {
            Bounds b = animal.bounds;
            Vector3 best = new Vector3(0f, b.max.y * 0.8f, b.center.z);
            bool found = false;
            if (animal.model != null && animal.model.boneTransforms != null)
            {
                foreach (Transform bone in animal.model.boneTransforms)
                {
                    if (bone == null || bone.name.IndexOf("spine", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    Vector3 local = animal.transform.InverseTransformPoint(bone.position);
                    if (Mathf.Abs(local.z - b.center.z) > b.extents.z * 0.6f)
                    {
                        continue;
                    }
                    if (!found || local.y > best.y)
                    {
                        best = new Vector3(0f, local.y, local.z);
                        found = true;
                    }
                }
            }
            return best;
        }

        // The animal the player looks at (inside a cone), nearest first.
        internal BaseNPC2 AnimalInSight(BasePlayer player, float range, float maxAngle = 25f)
        {
            Vector3 eye = player.eyes.position;
            Vector3 dir = player.eyes.HeadForward();
            BaseNPC2 best = null;
            float bestScore = float.MaxValue;
            var near = new List<BaseNPC2>();
            Vis.Entities(eye, range, near);
            foreach (BaseNPC2 npc in near)
            {
                if (npc == null || npc.IsDestroyed || npc.IsDead())
                {
                    continue;
                }
                Vector3 to = npc.WorldSpaceBounds().ToBounds().center - eye;
                float dist = to.magnitude;
                if (dist > range)
                {
                    continue;
                }
                float angle = Vector3.Angle(dir, to);
                if (angle > maxAngle)
                {
                    continue;
                }
                float score = angle + dist;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = npc;
                }
            }
            return best;
        }
    }

    // ======================================== Boss.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- boss animals (R20)
        // Rare versions of the horse, the bull, the ram and the stag: they appear at night in a burst of fire with 4000 health, hit for 99, run
        // 1.5x faster, are immune to fire and set their victims on fire. They drop scrap and a saddle that tames the next boss of that kind.
        // The look is limited by what a server can do: a black horse (the game's breed), effects from the game's own effect prefabs; no
        // bigger model, no glowing eyes.

        internal BossModule Bosses;
        internal const ulong BossSaddleSkin = 71000UL;          // item skin + kind index marks a boss saddle
        internal static readonly string[] BossKinds = { "horse", "bull", "ram", "stag" };

        internal BossInfo BossOf(BaseNetworkable e)
        {
            BaseEntity be = e as BaseEntity;
            BossInfo info;
            return be != null && be.skinID >= TagBase && Data.Bosses.TryGetValue(be.skinID, out info) ? info : null;
        }

        internal float BossDamageFactor(BaseNPC2 animal, float baseDamage)
        {
            BossInfo info = Cfg.Features.BossAnimals ? BossOf(animal) : null;
            if (info == null)
            {
                return 1f;
            }
            return Mathf.Max(1f, Cfg.Boss.Damage) / Mathf.Max(1f, baseDamage) * (info.Mini ? 0.5f : 1f);
        }

        // Bosses may fight each other but deal no damage to each other.
        internal bool IsBossPair(BaseEntity attacker, BaseEntity target)
        {
            return BossOf(attacker) != null && BossOf(target) != null;
        }

        // The hit of a boss sets the player on fire for a few seconds (like stepping into a campfire).
        internal void BossHit(BaseEntity attacker, BaseCombatEntity target)
        {
            BasePlayer p = target as BasePlayer;
            if (p != null && Bosses != null && BossOf(attacker) != null)
            {
                Bosses.Ignite(p);
            }
        }

        internal bool TryBossSaddle(BasePlayer player, BaseEntity entity)
        {
            return Bosses != null && Bosses.UseSaddle(player, entity);
        }

        internal static bool IsNight()
        {
            if (TOD_Sky.Instance != null)
            {
                return TOD_Sky.Instance.IsNight;
            }
            float hour = ConVar.Env.time;
            return hour < 6f || hour >= 20f;
        }

        internal sealed class BossModule : Module
        {
            public override int Order => 70;

            private sealed class Run
            {
                public BaseCombatEntity Entity;
                public BossInfo Info;
                public ulong Tag;
                public float NextEffect;
                public float NextRetarget;
                public BaseCombatEntity Target;
            }

            private readonly Dictionary<ulong, Run> runs = new Dictionary<ulong, Run>();
            private readonly List<float> kills = new List<float>();
            private readonly HashSet<ulong> burning = new HashSet<ulong>();
            private float nextSpawnCheck;
            private float nextVanishCheck;

            public int Alive { get { return runs.Count; } }

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Bosses = this;
                bm.Subscribe("OnEntitySpawned");
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["BossRoar"] = "A terrible {0} call echoes across the land - a boss {0} has risen!",
                    ["BossTamed"] = "The boss {0} bows to the saddle: it is yours now.",
                    ["BossDrop"] = "The boss {0} is dead. It dropped {1} scrap and a boss saddle.",
                    ["BossGone"] = "A boss {0} has vanished with the night.",
                };
            }

            public override void OnServerInitialized()
            {
                int found = 0;
                var seen = new HashSet<ulong>();
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                {
                    BaseCombatEntity c = net as BaseCombatEntity;
                    if (c != null && c.skinID >= TagBase && Bm.Data.Bosses.ContainsKey(c.skinID))
                    {
                        Setup(c, Bm.Data.Bosses[c.skinID], false);
                        seen.Add(c.skinID);
                        found++;
                    }
                }
                foreach (ulong tag in Bm.Data.Bosses.Keys.ToList())
                {
                    if (!seen.Contains(tag))
                    {
                        Bm.Data.Bosses.Remove(tag);
                    }
                }
                Bm.Log(3, "Bosses: " + found + " restored");
            }

            public override void OnDisable()
            {
                foreach (Run r in runs.Values)
                {
                    Calm(r);
                }
            }

            public override void Unload()
            {
                OnDisable();
                runs.Clear();
            }

            // ---------------------------------------------------------------- creating a boss

            public BaseCombatEntity SpawnBoss(string kind, Vector3 pos, bool mini = false)
            {
                SpeciesDef sp = SpeciesById(kind);
                if (sp == null || !Bm.SpeciesEnabled(sp))
                {
                    return null;
                }
                pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.3f;
                BaseCombatEntity e = GameManager.server.CreateEntity(sp.Prefab, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)) as BaseCombatEntity;
                if (e == null)
                {
                    return null;
                }
                ulong tag = Bm.NewTag();
                e.skinID = tag;
                var info = new BossInfo { Kind = kind, Mini = mini };
                Bm.Data.Bosses[tag] = info;
                e.Spawn();
                if (kind == "ram")
                {
                    MakeMale(e as LivestockAnimal);
                }
                Setup(e, info, true);
                Bm.RunEffect(Bm.Cfg.Boss.SpawnEffect, pos);
                // no chat line: the appearance of a boss is a surprise for the players (the burst of fire and the roar are heard and seen)
                Bm.Log(2, "Boss " + kind + (mini ? " (mini)" : string.Empty) + " spawned at " + pos);
                return e;
            }

            // Health, speed and look (also after a restart: the game restores the health itself, the speeds and the flags are set again).
            public void Setup(BaseCombatEntity e, BossInfo info, bool fresh)
            {
                float hp = info.Mini ? Bm.Cfg.Boss.MiniHealth : Bm.Cfg.Boss.Health;
                if (fresh || e.MaxHealth() < hp - 1f)
                {
                    e.InitializeHealth(hp, hp);
                }
                Bm.ScaleModel(e, info.Mini ? Bm.Cfg.Boss.MiniScale : Bm.Cfg.Boss.Scale);
                LivestockAnimal coat = e as LivestockAnimal;
                if (coat != null && fresh && Bm.Cfg.Boss.DarkCoat)
                {
                    coat.Genes = DarkGenes(coat.Genes);
                }
                BaseNPC2 npc = e as BaseNPC2;
                if (npc != null)
                {
                    RustNavMeshAgent agent = npc.GetComponent<RustNavMeshAgent>();
                    if (agent != null)
                    {
                        float k = Bm.Cfg.Boss.SpeedScale;
                        agent.sneakSpeed *= k;
                        agent.walkSpeed *= k;
                        agent.jogSpeed *= k;
                        agent.runSpeed *= k;
                        agent.sprintSpeed *= k;
                        agent.fullSprintSpeed *= k;
                    }
                }
                RidableHorse horse = e as RidableHorse;
                if (horse != null)
                {
                    KeepRiderSize(horse, info.Mini ? Bm.Cfg.Boss.MiniScale : Bm.Cfg.Boss.Scale);
                }
                if (horse != null && Bm.Stable != null)
                {
                    HorseState hs = Bm.Stable.Register(horse);
                    hs.SpeedMult = Bm.Cfg.Boss.SpeedScale;
                    hs.Damage = Bm.Cfg.Boss.Damage * (info.Mini ? 0.5f : 1f);
                    if (SeatsOf(horse) > 0 && !info.Tamed)
                    {
                        SetSeats(horse, 0);
                    }
                    int black = BlackBreed(horse);
                    if (black >= 0 && fresh)
                    {
                        horse.SetBreed(black);
                    }
                }
                runs[e.skinID] = new Run { Entity = e, Info = info, Tag = e.skinID, NextEffect = Time.time + 1f };
                AttachEyes(e, info.Kind);
            }

            // The coat of the game's livestock is drawn from the genes: the patch colour darkens with a bad Dung gene and the patches grow with a good
            // Yield and Longevity gene. These two settings give the blackest animal the genes can make.
            private static int DarkGenes(int genes)
            {
                if (!LivestockGenome.IsRolled(genes))
                {
                    genes = LivestockGenome.Roll();
                }
                genes = LivestockGenome.WithPair(genes, LivestockGene.Dung, LivestockAllele.Bad, LivestockAllele.Bad);
                genes = LivestockGenome.WithPair(genes, LivestockGene.Yield, LivestockAllele.Good, LivestockAllele.Good);
                genes = LivestockGenome.WithPair(genes, LivestockGene.Longevity, LivestockAllele.Good, LivestockAllele.Good);
                return genes;
            }

            private const ulong EyeSkin = 71995UL;

            // A narrow coloured spot of the game on each eye of the boss: no model (the cinematic lights are only light), pointing the way the boss looks.
            private void AttachEyes(BaseCombatEntity e, string kind)
            {
                string path;
                if (e == null || !Bm.Cfg.Boss.Eyes.TryGetValue(kind, out path) || string.IsNullOrEmpty(path) || GameManager.server.FindPrefab(path) == null)
                {
                    return;
                }
                foreach (BaseEntity child in e.children)
                {
                    if (child != null && child.skinID == EyeSkin)
                    {
                        return;
                    }
                }
                var eyes = new List<Transform>();
                Transform head = null;
                if (e.model != null && e.model.boneTransforms != null)
                {
                    foreach (Transform bone in e.model.boneTransforms)
                    {
                        if (bone == null)
                        {
                            continue;
                        }
                        string n = bone.name.ToLowerInvariant();
                        if (n.Contains("eye") && !n.Contains("lid") && !n.Contains("brow") && !n.Contains("lash"))
                        {
                            eyes.Add(bone);
                        }
                        else if (n.Contains("head") && (head == null || bone.name.Length < head.name.Length))
                        {
                            head = bone;
                        }
                    }
                }
                var spots = new List<KeyValuePair<Transform, Vector3>>();   // bone, world position of the light
                if (eyes.Count > 0)
                {
                    foreach (Transform eye in eyes.Take(2))
                    {
                        spots.Add(new KeyValuePair<Transform, Vector3>(eye, eye.position));
                    }
                }
                else if (head != null)
                {
                    float side = Mathf.Max(0.04f, e.bounds.size.x * 0.08f) * ModelScale(e);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        spots.Add(new KeyValuePair<Transform, Vector3>(head, head.position + e.transform.right * side * s + e.transform.forward * side * 1.2f + e.transform.up * side * 0.5f));
                    }
                }
                else
                {
                    Bounds b = e.bounds;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        spots.Add(new KeyValuePair<Transform, Vector3>(null, e.transform.TransformPoint(new Vector3(b.center.x + s * b.extents.x * 0.25f, b.max.y * 0.85f, b.center.z + b.extents.z * 0.85f))));
                    }
                }
                foreach (KeyValuePair<Transform, Vector3> spot in spots)
                {
                    BaseEntity lamp = GameManager.server.CreateEntity(path, spot.Value, e.transform.rotation);
                    if (lamp == null)
                    {
                        continue;
                    }
                    lamp.skinID = EyeSkin;
                    lamp.enableSaving = false;
                    if (spot.Key != null)
                    {
                        lamp.SetParent(e, spot.Key.name, true, false);
                    }
                    else
                    {
                        lamp.SetParent(e, true, false);
                    }
                    lamp.Spawn();
                }
            }

            // The lamp is switched on again if the game's power logic turned it off.
            private static void KeepEyesLit(BaseEntity e)
            {
                foreach (BaseEntity child in e.children)
                {
                    if (child != null && child.skinID == EyeSkin && !child.HasFlag(BaseEntity.Flags.On))
                    {
                        child.SetFlagLocal(BaseEntity.Flags.On, true);
                        child.SendNetworkUpdate();
                    }
                }
            }

            // The seats of a bigger horse sit where the saddle is (they are children of the horse), but keep normal size: the rider is not enlarged.
            private static void KeepRiderSize(RidableHorse horse, float scale)
            {
                if (horse.mountPoints == null)
                {
                    return;
                }
                foreach (BaseVehicle.MountPointInfo mp in horse.mountPoints)
                {
                    if (mp != null && mp.mountable != null && !mp.mountable.IsDestroyed)
                    {
                        mp.mountable.networkEntityScale = !Mathf.Approximately(scale, 1f);
                        mp.mountable.transform.localScale = Vector3.one / Mathf.Max(0.1f, scale);
                        mp.mountable.SendNetworkUpdate();
                    }
                }
            }

            private static int BlackBreed(RidableHorse horse)
            {
                for (int i = 0; i < horse.breeds.Length; i++)
                {
                    HorseBreed b = horse.breeds[i];
                    if (b != null && (b.name.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0 || b.breedName.english.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return i;
                    }
                }
                return -1;
            }

            // ---------------------------------------------------------------- the ticking brain: spawning, effects, hunting, vanishing

            public override void Tick(float now)
            {
                if (!Bm.Cfg.Features.BossAnimals)
                {
                    return;
                }
                if (now >= nextSpawnCheck)
                {
                    nextSpawnCheck = now + Mathf.Max(30f, Bm.Cfg.Boss.CheckSeconds);
                    TrySpawn();
                }
                bool day = Bm.Cfg.Boss.NightOnly && !IsNight();
                bool vanishCheck = now >= nextVanishCheck;
                if (vanishCheck)
                {
                    nextVanishCheck = now + 10f;
                }
                foreach (Run r in runs.Values.ToList())
                {
                    if (!Valid(r.Entity) || r.Entity.IsDead())
                    {
                        runs.Remove(r.Tag);
                        continue;
                    }
                    if (vanishCheck && day && !r.Info.Tamed && !r.Info.Mini)
                    {
                        Vanish(r);
                        continue;
                    }
                    if (now >= r.NextEffect)
                    {
                        r.NextEffect = now + 1.5f;
                        PlayEffects(r);
                        KeepEyesLit(r.Entity);
                    }
                    if (!r.Info.Tamed && now >= r.NextRetarget)
                    {
                        r.NextRetarget = now + 1f;
                        Hunt(r, now);
                    }
                }
                if (kills.Count > 0)
                {
                    kills.RemoveAll(t => now - t > Bm.Cfg.Boss.GroupSeconds);
                }
            }

            private void TrySpawn()
            {
                if (runs.Count >= Bm.Cfg.Boss.MaxAlive || (Bm.Cfg.Boss.NightOnly && !IsNight()))
                {
                    return;
                }
                if (UnityEngine.Random.Range(0f, 100f) >= Bm.Cfg.Boss.SpawnChance)
                {
                    return;
                }
                List<BasePlayer> players = BasePlayer.activePlayerList.Where(p => p != null && !p.IsDead() && !p.IsSleeping() && !p.IsNpc && !p.InSafeZone()).ToList();
                if (players.Count == 0)
                {
                    return;
                }
                BasePlayer near = players[UnityEngine.Random.Range(0, players.Count)];
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    Vector3 pos = near.transform.position + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * UnityEngine.Random.Range(90f, 200f);
                    pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                    if (WaterLevel.GetWaterDepth(pos, true, true, null) > 0.2f)
                    {
                        continue;
                    }
                    SpawnBoss(BossKinds[UnityEngine.Random.Range(0, BossKinds.Length)], pos);
                    return;
                }
            }

            private void Vanish(Run r)
            {
                SpeciesDef sp = SpeciesById(r.Info.Kind);
                Bm.RunEffect(Bm.Cfg.Form.SmokePrefab, r.Entity.transform.position + Vector3.up);
                Bm.Data.Bosses.Remove(r.Tag);
                runs.Remove(r.Tag);
                if (Bm.Stable != null && r.Entity is RidableHorse)
                {
                    Bm.Stable.Forget((RidableHorse)r.Entity);
                }
                r.Entity.Kill();
                if (sp != null)
                {
                    Bm.Log(3, "Boss " + sp.Id + " vanished with the night");
                }
            }

            private void PlayEffects(Run r)
            {
                List<string> list;
                if (!Bm.Cfg.Boss.Effects.TryGetValue(r.Info.Kind, out list) || list == null)
                {
                    return;
                }
                Vector3 pos = r.Entity.transform.position + Vector3.up * 0.8f;
                foreach (string fx in list)
                {
                    if (!string.IsNullOrEmpty(fx) && GameManager.server.FindPrefab(fx) != null)
                    {
                        Effect.server.Run(fx, pos, Vector3.up);
                    }
                }
            }

            // ---------------------------------------------------------------- hunting

            private bool Carries(BasePlayer p, string kind)
            {
                return FindSaddle(p, kind) != null;
            }

            private void Hunt(Run r, float now)
            {
                Vector3 pos = r.Entity.transform.position;
                BasePlayer best = null;
                float bestDist = Bm.Cfg.Boss.AggroRadius;
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || p.IsNpc || p.InSafeZone() || Bm.IsFormPlayer(p))
                    {
                        continue;
                    }
                    float d = Vector3.Distance(p.transform.position, pos);
                    if (d < bestDist && !Carries(p, r.Info.Kind))
                    {
                        bestDist = d;
                        best = p;
                    }
                }
                r.Target = best;
                BaseNPC2 npc = r.Entity as BaseNPC2;
                if (npc != null && !Bm.IsTamed(npc) && Bm.DriverOfAnimal(npc) == null)
                {
                    Follower f = Bm.FollowerOf(npc);
                    if (best != null && f == null)
                    {
                        f = Bm.StartFollower(npc, "boss", null);
                        if (f != null)
                        {
                            f.TeleportDistance = 0f;
                            Run run = r;
                            f.Target = () => run.Target != null && Alive(run.Target) ? run.Target : null;
                        }
                    }
                    else if (best == null && f != null && f.Reason == "boss")
                    {
                        Bm.StopFollower(npc);
                    }
                }
                else if (r.Entity is RidableHorse && Bm.Stable != null && best != null)
                {
                    Bm.Stable.Attack((RidableHorse)r.Entity, best, 3f);
                }
            }

            private void Calm(Run r)
            {
                BaseNPC2 npc = r.Entity as BaseNPC2;
                if (npc != null && Valid(npc))
                {
                    Follower f = Bm.FollowerOf(npc);
                    if (f != null && f.Reason == "boss")
                    {
                        Bm.StopFollower(npc);
                    }
                }
            }

            // ---------------------------------------------------------------- fire, immunity

            public void Ignite(BasePlayer p)
            {
                if (p == null || p.IsDead() || !burning.Add(p.userID))
                {
                    return;
                }
                int ticks = Mathf.Max(1, Mathf.RoundToInt(Bm.Cfg.Boss.BurnSeconds));
                string fire = "assets/bundled/prefabs/fx/player/onfire.prefab";
                Bm.timer.Repeat(1f, ticks, () =>
                {
                    if (p == null || p.IsDead() || !p.IsConnected)
                    {
                        return;
                    }
                    p.Hurt(new HitInfo(null, p, Rust.DamageType.Heat, Bm.Cfg.Boss.BurnDamage, p.transform.position));
                    if (GameManager.server.FindPrefab(fire) != null)
                    {
                        Effect.server.Run(fire, p.transform.position + Vector3.up, Vector3.up);
                    }
                });
                Bm.timer.Once(ticks + 1f, () => burning.Remove(p.userID));
            }

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                if (runs.Count == 0 || info == null || entity == null || entity.skinID < TagBase || !runs.ContainsKey(entity.skinID))
                {
                    return null;
                }
                if (info.damageTypes.Get(Rust.DamageType.Heat) > 0f)
                {
                    info.damageTypes.Set(Rust.DamageType.Heat, 0f);          // immune to fire
                }
                if (info.Initiator != null && Bm.BossOf(info.Initiator) != null)
                {
                    return true;                                              // no damage between bosses
                }
                return null;
            }

            // ---------------------------------------------------------------- loot

            private static string DisplayOf(string kind)
            {
                SpeciesDef sp = SpeciesById(kind);
                return sp != null ? sp.Display : kind;
            }

            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                if (entity == null || entity.skinID < TagBase || !runs.ContainsKey(entity.skinID))
                {
                    return;
                }
                Run r = runs[entity.skinID];
                runs.Remove(r.Tag);
                Bm.Data.Bosses.Remove(r.Tag);
                if (r.Info.Mini)
                {
                    return;                                                    // a mini boss gives no scrap
                }
                float now = Time.time;
                bool group = kills.Count > 0 || runs.Count > 0 && runs.Values.Any(o => Valid(o.Entity) && Vector3.Distance(o.Entity.transform.position, entity.transform.position) < 150f);
                kills.Add(now);
                int scrap = ScrapOf(r.Info.Kind);
                if (group)
                {
                    scrap = Mathf.RoundToInt(scrap * Bm.Cfg.Boss.GroupBonus);
                }
                Vector3 at = entity.transform.position + Vector3.up * 0.8f;
                DropScrap(scrap, at);
                Item saddle = ItemManager.CreateByName(r.Info.Kind == "horse" ? "horse.saddle.double" : "horse.saddle.single", 1);
                if (saddle != null)
                {
                    saddle.name = "Boss Saddle: " + DisplayOf(r.Info.Kind);
                    saddle.skin = BossSaddleSkin + (ulong)Array.IndexOf(BossKinds, r.Info.Kind);
                    saddle.MarkDirty();
                    saddle.Drop(at, new Vector3(0f, 2f, 0f));
                }
                BasePlayer killer = info != null ? info.InitiatorPlayer : null;
                if (killer != null)
                {
                    Bm.Tell(killer, "BossDrop", DisplayOf(r.Info.Kind).ToLowerInvariant(), scrap);
                }
            }

            private int ScrapOf(string kind)
            {
                switch (kind)
                {
                    case "horse": return Bm.Cfg.Boss.ScrapHorse;
                    case "stag": return Bm.Cfg.Boss.ScrapStag;
                    case "ram": return Bm.Cfg.Boss.ScrapRam;
                    default: return Bm.Cfg.Boss.ScrapBull;
                }
            }

            private static void DropScrap(int amount, Vector3 at)
            {
                ItemDefinition def = ItemManager.FindItemDefinition("scrap");
                if (def == null)
                {
                    return;
                }
                int stack = Mathf.Max(1, def.stackable);
                while (amount > 0)
                {
                    int n = Mathf.Min(stack, amount);
                    Item item = ItemManager.Create(def, n, 0UL);
                    if (item != null)
                    {
                        item.Drop(at + new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)), new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 2.5f, UnityEngine.Random.Range(-1.5f, 1.5f)));
                    }
                    amount -= n;
                }
            }

            // ---------------------------------------------------------------- the boss saddle and the offspring

            public Item FindSaddle(BasePlayer p, string kind)
            {
                ulong skin = BossSaddleSkin + (ulong)Array.IndexOf(BossKinds, kind);
                foreach (Item it in PlayerItems(p))
                {
                    if (it.skin == skin)
                    {
                        return it;
                    }
                }
                return null;
            }

            // E on a boss while carrying its saddle tames it on the spot (R20.7).
            public bool UseSaddle(BasePlayer player, BaseEntity entity)
            {
                BossInfo info = Bm.BossOf(entity);
                if (info == null || info.Tamed || !Bm.Cfg.Features.Taming)
                {
                    return false;
                }
                Item saddle = FindSaddle(player, info.Kind);
                if (saddle == null)
                {
                    return false;
                }
                TamedRecord rec = Bm.TameAnimal(entity, player);
                if (rec == null)
                {
                    return true;
                }
                saddle.UseItem(1);
                info.Tamed = true;
                rec.Boss = true;
                rec.BossKind = info.Kind;
                rec.MiniBoss = info.Mini;
                RidableHorse horse = entity as RidableHorse;
                if (horse != null && Bm.Stable != null)
                {
                    Bm.Stable.GiveSaddle(horse, 1);
                }
                else
                {
                    rec.Gear.Saddle = 1;
                }
                Run r;
                if (runs.TryGetValue(entity.skinID, out r))
                {
                    Calm(r);
                    r.Target = null;
                }
                Bm.Tell(player, "BossTamed", DisplayOf(info.Kind).ToLowerInvariant());
                return true;
            }

            // A calf or lamb born to a tamed boss is a mini boss with a 10 % chance (R20.8).
            public override void OnEntitySpawned(BaseNetworkable entity)
            {
                LivestockAnimal baby = entity as LivestockAnimal;
                if (baby == null || baby.skinID != 0UL || !Bm.Cfg.Features.BossAnimals || Bm.Data.Tamed.Count == 0)
                {
                    return;
                }
                Vector3 pos = baby.transform.position;
                foreach (TamedRecord rec in Bm.Data.Tamed.Values)
                {
                    if (!rec.Boss || rec.MiniBoss)
                    {
                        continue;
                    }
                    BaseEntity parent = Bm.EntityOfRecord(rec);
                    if (parent == null || Vector3.Distance(parent.transform.position, pos) > 14f)
                    {
                        continue;
                    }
                    if (UnityEngine.Random.Range(0f, 100f) < Bm.Cfg.Boss.MiniChance)
                    {
                        ulong tag = Bm.NewTag();
                        baby.skinID = tag;
                        var info = new BossInfo { Kind = rec.BossKind, Mini = true };
                        Bm.Data.Bosses[tag] = info;
                        Setup(baby, info, true);
                    }
                    return;
                }
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Bosses: " + runs.Count + " alive, " + (Bm.Cfg.Features.BossAnimals ? "spawning on" : "spawning off") + ", it is " + (IsNight() ? "night" : "day"));
            }
        }

        // Headless test: a boss bull and a boss ram are made, resist fire, die together and drop scrap (with the group bonus) and boss saddles.
        internal void TestBoss(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            BaseCombatEntity bull = Bosses.SpawnBoss("bull", pos);
            BaseCombatEntity ram = Bosses.SpawnBoss("ram", pos + new Vector3(8f, 0f, 0f));
            BaseCombatEntity horse = Bosses.SpawnBoss("horse", pos + new Vector3(-8f, 0f, 0f));
            if (bull == null || ram == null || horse == null)
            {
                foreach (BaseCombatEntity e in new[] { bull, ram, horse }) { if (e != null) e.Kill(); }
                reply("A boss kind is not available in this build.");
                return;
            }
            timer.Once(2.0f, () =>
            {
                check("the boss has " + Cfg.Boss.Health + " health (" + Mathf.RoundToInt(bull.health) + ")", Mathf.Approximately(bull.health, Cfg.Boss.Health) && Mathf.Approximately(bull.MaxHealth(), Cfg.Boss.Health));
                check("bosses are registered (" + Bosses.Alive + ")", Bosses.Alive >= 3);
                float before = bull.health;
                bull.Hurt(500f, Rust.DamageType.Heat);
                check("a boss is immune to fire (" + Mathf.RoundToInt(before) + " -> " + Mathf.RoundToInt(bull.health) + ")", Mathf.Approximately(before, bull.health));
                RidableHorse h = horse as RidableHorse;
                check("the boss horse has no saddle until tamed", h != null && SeatsOf(h) == 0);
                float want = Cfg.Boss.Scale;
                check("a boss is bigger (bull x" + bull.transform.localScale.x.ToString("0.00") + ", horse x" + horse.transform.localScale.x.ToString("0.00") + ", ram x" + ram.transform.localScale.x.ToString("0.00") + ")",
                    bull.networkEntityScale && Mathf.Approximately(bull.transform.localScale.x, want) && horse.networkEntityScale && Mathf.Approximately(horse.transform.localScale.x, want) && ram.networkEntityScale);
                bool seatsKeepSize = h != null && h.mountPoints.Count > 0 && h.mountPoints.All(mp => mp.mountable == null || Mathf.Abs(mp.mountable.transform.lossyScale.x - 1f) < 0.02f);
                check("the horse's seats keep normal size, so a rider on the bigger horse is not enlarged", seatsKeepSize);
                Vector3 horseStart = horse.transform.position;
                HorseState horseState = h != null && Stable != null ? Stable.StateOf(h) : null;
                if (horseState != null)
                {
                    horseState.Drive.StopDistance = 1.5f;
                    horseState.Drive.MoveTo(horseStart + horse.transform.forward * 14f, 5f);
                }
                float hb = ram.health;
                ram.Hurt(100f, Rust.DamageType.Bullet);
                check("other damage still hurts (" + Mathf.RoundToInt(hb) + " -> " + Mathf.RoundToInt(ram.health) + ")", ram.health < hb);
                Vector3 at = bull.transform.position;
                bull.Hurt(100000f, Rust.DamageType.Bullet);
                ram.Hurt(100000f, Rust.DamageType.Bullet);
                timer.Once(3.0f, () =>
                {
                    int scrap = 0, saddles = 0;
                    foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                    {
                        WorldItem wi = net as WorldItem;
                        if (wi == null || wi.item == null || Vector3.Distance(wi.transform.position, at) > 20f)
                        {
                            continue;
                        }
                        if (wi.item.info.shortname == "scrap") scrap += wi.item.amount;
                        if (wi.item.skin >= BossSaddleSkin && wi.item.skin < BossSaddleSkin + 4UL) saddles++;
                    }
                    int expected = Mathf.RoundToInt(Cfg.Boss.ScrapBull * Cfg.Boss.GroupBonus) + Mathf.RoundToInt(Cfg.Boss.ScrapRam * Cfg.Boss.GroupBonus);
                    check("the two bosses dropped " + scrap + " scrap (expected " + expected + ": each gets the group bonus)", scrap == expected);
                    check("and two boss saddles (" + saddles + ")", saddles == 2);
                    check("their records are gone", Bosses.Alive <= 1);
                    if (Valid(horse))
                    {
                        float moved = Vector3.Distance(horse.transform.position, horseStart);
                        float clearance = horse.transform.position.y - TerrainMeta.HeightMap.GetHeight(horse.transform.position);
                        check("the bigger horse still walks (" + moved.ToString("0.0") + " m) and stands on the ground (" + clearance.ToString("0.00") + " m above it)", moved > 5f && clearance > -0.4f && clearance < 2.2f);
                    }
                    foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                    {
                        WorldItem wi = net as WorldItem;
                        if (wi != null && wi.item != null && Vector3.Distance(wi.transform.position, at) < 20f && (wi.item.info.shortname == "scrap" || wi.item.skin >= BossSaddleSkin))
                        {
                            wi.Kill();
                        }
                    }
                    if (Valid(horse))
                    {
                        Data.Bosses.Remove(horse.skinID);
                        if (Stable != null && horse is RidableHorse) Stable.Forget((RidableHorse)horse);
                        horse.Kill();
                    }
                    Puts("[test] boss:\n" + string.Join("\n", lines.ToArray()));
                });
            });
            reply("Boss test started: the result comes to the log in ~8 s.");
        }

        // bm.boss spawn <horse|bull|ram|stag> [mini] | list | clear
        private void BossCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            switch (sub)
            {
                case "spawn":
                    {
                        string kind = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
                        if (!BossKinds.Contains(kind)) { reply("Usage: bm.boss spawn horse|bull|ram|stag [mini]"); return; }
                        Vector3 spot = player != null ? SpawnSpot(player) : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(15f, 0f, 15f);
                        BaseCombatEntity e = Bosses.SpawnBoss(kind, spot, args.Length > 2 && args[2] == "mini");
                        reply(e != null ? "A boss " + kind + " stands at " + spot + "." : "It could not be created.");
                        return;
                    }
                case "clear":
                    {
                        int n = 0;
                        foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                        {
                            BaseEntity be = net as BaseEntity;
                            if (be != null && be.skinID >= TagBase && Data.Bosses.ContainsKey(be.skinID) && RecordOf(be) == null)
                            {
                                Data.Bosses.Remove(be.skinID);
                                be.Kill();
                                n++;
                            }
                        }
                        reply(n + " untamed bosses removed.");
                        return;
                    }
                default:
                    reply("Bosses alive: " + Bosses.Alive + ". " + (IsNight() ? "Night." : "Day.") + " Usage: bm.boss spawn horse|bull|ram|stag [mini] | clear");
                    return;
            }
        }
    }

    // ======================================== Climate.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- climate (R19.19)
        // A polar bear outside the snow needs water, an animal that does not belong in the snow needs cooked meat in the inventory there;
        // without it the animal is slower and weaker. The supplies are used up slowly. Applies to ridden animals and to animal forms.

        internal ClimateModule Weather;

        internal float ClimateSpeedFactor(BaseEntity animal)
        {
            return Weather != null ? Weather.Factor(animal, true) : 1f;
        }

        internal float ClimateDamageFactor(BaseEntity animal)
        {
            return Weather != null ? Weather.Factor(animal, false) : 1f;
        }

        internal static bool IsSnow(Vector3 pos)
        {
            return TerrainMeta.BiomeMap != null && TerrainMeta.BiomeMap.GetBiome(pos, TerrainBiome.ARCTIC) > 0.5f;
        }

        internal sealed class ClimateModule : Module
        {
            public override int Order => 60;

            private sealed class State
            {
                public string Need;             // water, warmth or null
                public bool Lacking;
                public float NextUse;
                public float NextWarn;
            }

            private readonly Dictionary<ulong, State> states = new Dictionary<ulong, State>();
            private float nextTick;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Weather = this;
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["ClimateWater"] = "You are too far from the snow and dry out: carry water (a bottle or jug) or you get slower and weaker.",
                    ["ClimateCold"] = "You freeze in the snow: carry cooked meat to keep warm or you get slower and weaker.",
                    ["ClimateOk"] = "You feel fine again.",
                };
            }

            public float Factor(BaseEntity animal, bool speed)
            {
                if (states.Count == 0 || !Bm.Cfg.Features.Climate)
                {
                    return 1f;
                }
                Driver d = Bm.DriverOfAnimal(animal);
                State s;
                if (d == null || d.Player == null || !states.TryGetValue(d.Player.userID, out s) || !s.Lacking)
                {
                    return 1f;
                }
                return speed ? Bm.Cfg.Climate.PenaltySpeed : Bm.Cfg.Climate.PenaltyDamage;
            }

            public override void OnDisable()
            {
                states.Clear();
            }

            public override void Tick(float now)
            {
                if (now < nextTick)
                {
                    return;
                }
                nextTick = now + 2f;
                if (!Bm.Cfg.Features.Climate || Bm.Drivers.Count == 0)
                {
                    states.Clear();
                    return;
                }
                var present = new HashSet<ulong>();
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || d.Player == null || !Valid(d.Body) || d.Species == null)
                    {
                        continue;
                    }
                    present.Add(d.Player.userID);
                    Check(d, now);
                }
                foreach (ulong id in states.Keys.Where(k => !present.Contains(k)).ToList())
                {
                    states.Remove(id);
                }
            }

            private void Check(Driver d, float now)
            {
                BasePlayer p = d.Player;
                bool snow = IsSnow(d.Body.transform.position);
                bool native = Bm.Cfg.Climate.SnowNative.Contains(d.Species.Id);
                string need = native && !snow && Bm.Cfg.Climate.PolarNeedsWater ? "water" : !native && snow && Bm.Cfg.Climate.ColdNeedsMeat ? "warmth" : null;
                State s;
                if (!states.TryGetValue(p.userID, out s))
                {
                    s = new State();
                    states[p.userID] = s;
                }
                if (need == null)
                {
                    if (s.Lacking)
                    {
                        Bm.Tell(p, "ClimateOk");
                    }
                    s.Need = null;
                    s.Lacking = false;
                    return;
                }
                bool wasLacking = s.Lacking;
                Item supply = need == "water" ? FindWater(p) : FindWarmMeat(p);
                if (s.Need != need)
                {
                    s.Need = need;
                    s.NextUse = now + 5f;
                }
                if (supply == null)
                {
                    s.Lacking = true;
                    if (!wasLacking || now >= s.NextWarn)
                    {
                        s.NextWarn = now + 45f;
                        Bm.Tell(p, need == "water" ? "ClimateWater" : "ClimateCold");
                    }
                    return;
                }
                s.Lacking = false;
                if (wasLacking)
                {
                    Bm.Tell(p, "ClimateOk");
                }
                if (now >= s.NextUse)
                {
                    // the supply is used up slowly: 50 ml of water per 5 s, one cooked meat per 90 s
                    if (need == "water")
                    {
                        s.NextUse = now + 5f;
                        supply.UseItem(Mathf.Min(50, supply.amount));
                    }
                    else
                    {
                        s.NextUse = now + Bm.Cfg.Climate.MeatSeconds;
                        supply.UseItem(1);
                    }
                }
            }

            // Water as an item or inside a bottle, jug or bucket.
            private static Item FindWater(BasePlayer p)
            {
                foreach (Item it in PlayerItems(p))
                {
                    if (it.info.shortname == "water" && it.amount > 0)
                    {
                        return it;
                    }
                    if (it.contents != null)
                    {
                        foreach (Item inner in it.contents.itemList)
                        {
                            if (inner.info.shortname == "water" && inner.amount > 0)
                            {
                                return inner;
                            }
                        }
                    }
                }
                return null;
            }

            private static Item FindWarmMeat(BasePlayer p)
            {
                foreach (Item it in PlayerItems(p))
                {
                    if (it.info.shortname.EndsWith(".cooked") && it.amount > 0)
                    {
                        return it;
                    }
                }
                return null;
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Climate: " + (Bm.Cfg.Features.Climate ? "on" : "off") + ", " + states.Count(kv => kv.Value.Lacking) + " animals lacking supplies; snow detection " + (TerrainMeta.BiomeMap != null ? "OK" : "MISSING"));
            }
        }
    }

    // ======================================== Combat.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- damage of an animal attack

        // Damage the way the game's own animal attack states do it (BaseCombatEntity.OnAttacked) to everything in reach of the attack.
        // Trees, ore and corpses are gathered for the player the way a hit without a tool gathers (ResourceDispenser.DoGather).
        // Returns a short summary for the player ("cow 450>415, tree gathered").
        internal string ResolveHits(BaseNPC2 animal, BasePlayer owner, float heading, AttackParams p, bool rear)
        {
            var summary = new List<string>();
            Bounds b = animal.bounds;
            float k = ModelScale(animal);          // a bigger animal (a boss) reaches further
            Vector3 facing = Quaternion.Euler(0f, heading, 0f) * (rear ? Vector3.back : Vector3.forward);
            Vector3 origin = animal.transform.position + Vector3.up * b.center.y * k + facing * (rear ? b.extents.z * 0.5f : b.extents.z * 0.8f) * k;
            var found = new List<BaseEntity>();
            Vis.Entities(origin, p.Range * k + 1.5f, found);
            var done = new HashSet<BaseEntity>();
            float scale = Cfg.Combat.DamageScale * ClimateDamageFactor(animal) * BossDamageFactor(animal, p.Damage);
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == animal || (owner != null && e == owner) || e.GetParentEntity() == animal || !done.Add(e))
                {
                    continue;
                }
                Vector3 point = e.ClosestPoint(origin);
                Vector3 v = point - origin;
                v.y = 0f;
                if (v.magnitude > p.Range * k || (p.Angle < 180f && v.sqrMagnitude > 0.01f && Vector3.Angle(facing, v) > p.Angle))
                {
                    continue;
                }
                ResourceDispenser dispenser = e.GetComponent<ResourceDispenser>();
                if (dispenser != null && (e is TreeEntity || e is OreResourceEntity || e is BaseCorpse))
                {
                    if (Cfg.Combat.Gather && owner != null)
                    {
                        HitInfo gather = new HitInfo();
                        gather.Init(owner, e, p.Type, p.Damage * scale, point);
                        gather.CanGather = true;
                        dispenser.DoGather(gather, e as BaseCorpse);
                        HitSound(e, point);
                        summary.Add(ShortName(e) + " gathered");
                    }
                    continue;
                }
                BaseCombatEntity target = e as BaseCombatEntity;
                if (target == null || target.IsDead() || !MayHit(owner, animal, target))
                {
                    continue;
                }
                float before = target.health;
                target.OnAttacked(p.Damage * scale, p.Type, animal, false);
                HitSound(target, point);
                BossHit(animal, target);
                summary.Add(ShortName(target) + " " + before.ToString("0") + ">" + Mathf.Max(0f, target.health).ToString("0"));
                BasePlayer victim = target as BasePlayer;
                if (victim != null && !victim.IsNpc && p.Push != Vector2.zero)
                {
                    Vector3 away = v.sqrMagnitude > 0.01f ? v.normalized : facing;
                    victim.DoPush(away * p.Push.y + Vector3.up * p.Push.x);
                }
                if (summary.Count >= 6)
                {
                    break;
                }
            }
            return string.Join(", ", summary.ToArray());
        }

        // The sound of what was hit: the thud of flesh, or of the wood, rock or metal of an object (the game's own impact effects).
        internal void HitSound(BaseEntity e, Vector3 point)
        {
            string material;
            if (e is BasePlayer || e is BaseNPC2 || e is RidableHorse || e is BaseAnimalNPC || e is BaseCorpse)
            {
                material = "flesh";
            }
            else if (e is TreeEntity)
            {
                material = "wood";
            }
            else if (e is OreResourceEntity)
            {
                material = "rock";
            }
            else
            {
                material = ColliderSurface(e.GetComponentInChildren<Collider>());
            }
            if (material == "stones" || material == "gravel")
            {
                material = "rock";
            }
            string path = FindPrefabPath("impacts/blunt/" + material + "/") ?? FindPrefabPath("impacts/blunt/generic/");
            RunEffect(path, point);
        }

        private static string ShortName(BaseEntity e)
        {
            return e.ShortPrefabName.Replace(".entity", string.Empty).Replace("_deployed", string.Empty);
        }

        private bool MayHit(BasePlayer owner, BaseNPC2 animal, BaseCombatEntity target)
        {
            if (target is BasePlayer)
            {
                if (!Cfg.Combat.HitPlayers)
                {
                    return false;
                }
                BasePlayer tp = (BasePlayer)target;
                if (tp.IsDead() || (tp.IsSleeping() && false))
                {
                    return false;
                }
                if (IsFormPlayer(tp))
                {
                    return false;   // the hidden human is not a target; its animal is
                }
                if (owner != null && SameParty(owner, tp) && Cfg.Combat.SparePartyMembers)
                {
                    return false;
                }
            }
            else if (target is BaseNPC2 || target is RidableHorse || target is BaseAnimalNPC)
            {
                if (!Cfg.Combat.HitAnimals)
                {
                    return false;
                }
                Driver other = DriverOfAnimal(target);
                if (other != null && owner != null && SameParty(owner, other.Player) && Cfg.Combat.SparePartyMembers)
                {
                    return false;
                }
                TamedRecord rec = RecordOf(target);
                if (rec != null && owner != null && Cfg.Combat.SparePartyMembers && MayUse(owner, rec.Owner, "team"))
                {
                    return false;   // never hit the animals of your own team
                }
                if (IsBossPair(animal, target))
                {
                    return false;   // bosses may fight each other but deal no damage to each other
                }
            }
            else if (!Cfg.Combat.HitStructures)
            {
                return false;
            }
            if (Cfg.Combat.SparesSafeZone && target.InSafeZone())
            {
                return false;
            }
            object veto = Interface.CallHook("CanBeastmasterAttack", owner, animal, target);
            return veto == null;
        }

        internal bool SameParty(BasePlayer a, BasePlayer b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            if (a == b)
            {
                return true;
            }
            RelationshipManager.PlayerTeam team = a.Team;
            return team != null && team.members.Contains(b.userID);
        }

        // Rabbit, squirrel, frog and other critters have no attack clip: a call and a very small nip (assumption A12).
        internal void CritterNip(Driver d)
        {
            if (!Valid(d.Animal))
            {
                return;
            }
            AttackParams p = new AttackParams { Damage = 3f, Delay = 0f, Type = Rust.DamageType.Bite, Range = 1.0f, Angle = 60f, Push = Vector2.zero };
            string summary = ResolveHits(d.Animal, d.Player, d.Heading, p, false);
            if (!string.IsNullOrEmpty(summary))
            {
                Tell(d.Player, "HitSummary", summary);
            }
        }

        // The game's attack states turn the animal toward its target as the attack starts (State_Attack.AlignToTarget).
        // A player cannot aim an animal that precisely, so the same happens here: the nearest valid target in front is faced.
        internal float AimAtTarget(Driver d, AttackParams p, bool rear)
        {
            if (rear || !Cfg.Combat.AutoAim)
            {
                return d.Heading;
            }
            Bounds b = d.Animal.bounds;
            float heading = d.Heading;
            Vector3 facing = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Vector3 origin = d.Animal.transform.position + Vector3.up * b.center.y;
            var found = new List<BaseEntity>();
            Vis.Entities(origin, p.Range + b.extents.z + 1.2f, found);
            BaseEntity best = null;
            float bestScore = float.MaxValue;
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == d.Animal || e == d.Player || e.GetParentEntity() == d.Animal)
                {
                    continue;
                }
                bool gatherable = Cfg.Combat.Gather && (e is TreeEntity || e is OreResourceEntity || e is BaseCorpse);
                BaseCombatEntity target = e as BaseCombatEntity;
                if (!gatherable && (target == null || target.IsDead() || !MayHit(d.Player, d.Animal, target)))
                {
                    continue;
                }
                Vector3 to = e.ClosestPoint(origin) - origin;
                to.y = 0f;
                if (to.magnitude > p.Range + b.extents.z)
                {
                    continue;
                }
                float angle = Vector3.Angle(facing, to);
                if (angle > Cfg.Combat.AutoAimAngle)
                {
                    continue;
                }
                float score = angle * 0.05f + to.magnitude;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }
            if (best == null)
            {
                return heading;
            }
            Vector3 dir = best.ClosestPoint(origin) - origin;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.01f ? Yaw(dir) : heading;
        }

        // Headless test of the attack pipeline (server console only): a bear bites a cow two metres in front of it.
        internal void TestCombat(Action<string> reply, Vector3 basePos)
        {
            SpeciesDef bearDef = SpeciesById("bear"), cowDef = SpeciesById("cow");
            if (bearDef == null || cowDef == null || !bearDef.Available || !cowDef.Available)
            {
                reply("combat test: species missing");
                return;
            }
            basePos.y = TerrainMeta.HeightMap.GetHeight(basePos) + 0.2f;
            BaseNPC2 attacker = GameManager.server.CreateEntity(bearDef.Prefab, basePos, Quaternion.identity) as BaseNPC2;
            BaseNPC2 victim = GameManager.server.CreateEntity(cowDef.Prefab, basePos + new Vector3(0f, 0f, 2.2f), Quaternion.identity) as BaseNPC2;
            if (attacker == null || victim == null)
            {
                reply("combat test: could not spawn");
                return;
            }
            attacker.Spawn();
            victim.Spawn();
            timer.Once(1.2f, () =>
            {
                Park(attacker, "test");
                Park(victim, "test");
                attacker.transform.rotation = Quaternion.identity;
                victim.transform.position = attacker.transform.position + Vector3.forward * 2.2f;
                Physics.SyncTransforms();
                // the colliders follow the transforms at the next physics step
                timer.Once(0.4f, () =>
                {
                    try
                    {
                        ClipRef clip = Clips(attacker).FirstOrDefault(c => Has(c, "attack"));
                        if (clip == null)
                        {
                            Puts("[test] combat: bear has no attack clip");
                            return;
                        }
                        AttackParams p = ReadAttack(attacker, clip, false);
                        float before = victim.health;
                        string summary = ResolveHits(attacker, null, 0f, p, false);
                        Puts("[test] combat: " + clip.Name + " " + p.Damage + " " + p.Type + " reach " + p.Range + " angle " + p.Angle + " -> [" + summary + "] cow health " + before.ToString("0") + " > " + victim.health.ToString("0"));
                    }
                    finally
                    {
                        Unpark(attacker, "test");
                        Unpark(victim, "test");
                        if (Valid(attacker)) attacker.Kill();
                        if (Valid(victim)) victim.Kill();
                    }
                });
            });
        }

        // Bull and ram sprinting through a wooden barricade break it (R4.6).
        private float nextRamCheck;
    }

    // ======================================== Commands.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- command entry points
        // Chat: /bm <sub> ...     Console / RCON: bm.<sub> ...     Both end in RunCommand.

        [ChatCommand("bm")]
        private void ChatBm(BasePlayer player, string command, string[] args)
        {
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "menu";
            RunCommand(player, sub, args.Skip(1).ToArray(), text => TellRaw(player, text));
        }

        // /sp is the short way to spawn: /sp cow 2 tamed
        [ChatCommand("sp")]
        private void ChatSp(BasePlayer player, string command, string[] args)
        {
            string[] rest = args.Length > 0 && args[0].ToLowerInvariant() == "spawn" ? args.Skip(1).ToArray() : args;
            RunCommand(player, "spawn", rest, text => TellRaw(player, text));
        }

        [ChatCommand("beast")]
        private void ChatBeast(BasePlayer player, string command, string[] args)
        {
            ChatBm(player, command, args);
        }

        private void Cc(ConsoleSystem.Arg arg, string sub)
        {
            BasePlayer player = arg.Player();
            string full = arg.FullString.ToString();
            string[] args = string.IsNullOrWhiteSpace(full) ? new string[0] : full.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            RunCommand(player, sub, args, text => arg.ReplyWith(text));
        }

        [ConsoleCommand("bm.menu")] private void CcMenu(ConsoleSystem.Arg arg) { Cc(arg, "menu"); }
        [ConsoleCommand("bm.attack")] private void CcAttack(ConsoleSystem.Arg arg) { Cc(arg, "attack"); }
        [ConsoleCommand("bm.view")] private void CcView(ConsoleSystem.Arg arg) { Cc(arg, "view"); }
        [ConsoleCommand("bm.revert")] private void CcRevert(ConsoleSystem.Arg arg) { Cc(arg, "revert"); }
        [ConsoleCommand("bm.cmd")] private void CcCmd(ConsoleSystem.Arg arg) { Cc(arg, "cmd"); }
        [ConsoleCommand("bm.emote")] private void CcEmote(ConsoleSystem.Arg arg) { Cc(arg, "emote"); }
        [ConsoleCommand("bm.keys")] private void CcKeys(ConsoleSystem.Arg arg) { Cc(arg, "keys"); }
        [ConsoleCommand("bm.preset")] private void CcPreset(ConsoleSystem.Arg arg) { Cc(arg, "preset"); }
        [ConsoleCommand("bm.ride")] private void CcRide(ConsoleSystem.Arg arg) { Cc(arg, "ride"); }
        [ConsoleCommand("bm.form")] private void CcForm(ConsoleSystem.Arg arg) { Cc(arg, "form"); }
        [ConsoleCommand("bm.species")] private void CcSpecies(ConsoleSystem.Arg arg) { Cc(arg, "species"); }
        [ConsoleCommand("bm.seat")] private void CcSeat(ConsoleSystem.Arg arg) { Cc(arg, "seat"); }
        [ConsoleCommand("bm.enable")] private void CcEnable(ConsoleSystem.Arg arg) { Cc(arg, "enable"); }
        [ConsoleCommand("bm.disable")] private void CcDisable(ConsoleSystem.Arg arg) { Cc(arg, "disable"); }
        [ConsoleCommand("bm.grant")] private void CcGrant(ConsoleSystem.Arg arg) { Cc(arg, "grant"); }
        [ConsoleCommand("bm.revoke")] private void CcRevoke(ConsoleSystem.Arg arg) { Cc(arg, "revoke"); }
        [ConsoleCommand("bm.access")] private void CcAccess(ConsoleSystem.Arg arg) { Cc(arg, "access"); }
        [ConsoleCommand("bm.grantgroup")] private void CcGrantGroup(ConsoleSystem.Arg arg) { Cc(arg, "grantgroup"); }
        [ConsoleCommand("bm.revokegroup")] private void CcRevokeGroup(ConsoleSystem.Arg arg) { Cc(arg, "revokegroup"); }
        [ConsoleCommand("bm.spawn")] private void CcSpawn(ConsoleSystem.Arg arg) { Cc(arg, "spawn"); }
        [ConsoleCommand("bm.clear")] private void CcClear(ConsoleSystem.Arg arg) { Cc(arg, "clear"); }
        [ConsoleCommand("bm.reload")] private void CcReload(ConsoleSystem.Arg arg) { Cc(arg, "reload"); }
        [ConsoleCommand("bm.selftest")] private void CcSelfTest(ConsoleSystem.Arg arg) { Cc(arg, "selftest"); }
        [ConsoleCommand("bm.debug")] private void CcDebug(ConsoleSystem.Arg arg) { Cc(arg, "debug"); }
        [ConsoleCommand("bm.test")] private void CcTest(ConsoleSystem.Arg arg) { Cc(arg, "test"); }
        [ConsoleCommand("bm.prefabs")] private void CcPrefabs(ConsoleSystem.Arg arg) { Cc(arg, "prefabs"); }
        [ConsoleCommand("bm.ui")] private void CcUi(ConsoleSystem.Arg arg) { Cc(arg, "ui"); }
        [ConsoleCommand("bm.horses")] private void CcHorses(ConsoleSystem.Arg arg) { Cc(arg, "horses"); }
        [ConsoleCommand("bm.items")] private void CcItems(ConsoleSystem.Arg arg) { Cc(arg, "items"); }
        [ConsoleCommand("bm.entities")] private void CcEntities(ConsoleSystem.Arg arg) { Cc(arg, "entities"); }
        [ConsoleCommand("bm.boss")] private void CcBoss(ConsoleSystem.Arg arg) { Cc(arg, "boss"); }
        [ConsoleCommand("bm.feed")] private void CcFeed(ConsoleSystem.Arg arg) { Cc(arg, "feed"); }
        [ConsoleCommand("bm.give")] private void CcGive(ConsoleSystem.Arg arg) { Cc(arg, "give"); }
        [ConsoleCommand("bm.nv")] private void CcNv(ConsoleSystem.Arg arg) { Cc(arg, "nv"); }
        [ConsoleCommand("bm.lie")] private void CcLie(ConsoleSystem.Arg arg) { Cc(arg, "lie"); }
        [ConsoleCommand("bm.sit")] private void CcSit(ConsoleSystem.Arg arg) { Cc(arg, "sit"); }
        [ConsoleCommand("bm.roar")] private void CcRoar(ConsoleSystem.Arg arg) { Cc(arg, "roar"); }
        [ConsoleCommand("bm.look")] private void CcLook(ConsoleSystem.Arg arg) { Cc(arg, "look"); }
        [ConsoleCommand("bm.stand")] private void CcStand(ConsoleSystem.Arg arg) { Cc(arg, "stand"); }
        [ConsoleCommand("bm.cam")] private void CcCam(ConsoleSystem.Arg arg) { Cc(arg, "cam"); }
        [ConsoleCommand("bm.name")] private void CcName(ConsoleSystem.Arg arg) { Cc(arg, "name"); }
        [ConsoleCommand("bm.breed")] private void CcBreed(ConsoleSystem.Arg arg) { Cc(arg, "breed"); }
        [ConsoleCommand("bm.assets")] private void CcAssets(ConsoleSystem.Arg arg) { Cc(arg, "assets"); }
        [ConsoleCommand("bm.run")] private void CcRun(ConsoleSystem.Arg arg) { Cc(arg, "run"); }

        // ---------------------------------------------------------------- the commands

        internal void RunCommand(BasePlayer player, string sub, string[] args, Action<string> reply)
        {
            bool server = player == null;       // server console or RCON: full rights
            bool admin = server || IsAdmin(player);
            try
            {
                // an admin runs any server command from the chat: /bm run env.time 12   or simply   /bm env.time 12
                if (!server && admin && (sub == "run" || (sub.Contains(".") && !sub.StartsWith("bm."))))
                {
                    string line = sub == "run" ? string.Join(" ", args) : sub + (args.Length > 0 ? " " + string.Join(" ", args) : string.Empty);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        reply("Usage: /bm run <any server command>, for example /bm run env.time 12");
                        return;
                    }
                    string result = ConsoleSystem.Run(ConsoleSystem.Option.Server, line);
                    Log(2, "[admin chat command] " + player.displayName + ": " + line);
                    reply(string.IsNullOrWhiteSpace(result) ? "Done: " + line : result);
                    return;
                }
                switch (sub)
                {
                    case "menu":
                    case "help":
                        if (server) { reply(HelpText()); return; }
                        OpenMenu(player, sub == "help" ? "help" : "home");
                        return;
                    case "species": reply(SpeciesList()); return;
                    case "keys": reply(KeysText()); return;
                    case "enable":
                        if (!admin) { reply(L("NoPermission")); return; }
                        SetActive(true);
                        reply(L("Enabled"));
                        return;
                    case "disable":
                        if (!admin) { reply(L("NoPermission")); return; }
                        SetActive(false);
                        reply(L("Disabled"));
                        return;
                    case "reload":
                        if (!admin) { reply(L("NoPermission")); return; }
                        LoadConfig();
                        reply(L("Reloaded"));
                        return;
                    case "debug":
                        if (!admin) { reply(L("NoPermission")); return; }
                        Cfg.General.LogLevel = args.Length > 0 ? Mathf.Clamp(int.Parse(args[0]), 0, 3) : 3;
                        reply("Log level " + Cfg.General.LogLevel);
                        return;
                    case "grant":
                    case "revoke":
                        if (!admin) { reply(L("NoPermission")); return; }
                        GrantCommand(sub == "grant", args, reply);
                        return;
                    case "grantgroup":
                    case "revokegroup":
                        if (!admin) { reply(L("NoPermission")); return; }
                        if (args.Length < 1) { reply("Usage: bm." + sub + " <group>"); return; }
                        if (sub == "grantgroup") permission.GrantGroupPermission(args[0], PermMenu, null); else permission.RevokeGroupPermission(args[0], PermMenu);
                        reply((sub == "grantgroup" ? "Menu access granted to group " : "Menu access removed from group ") + args[0]);
                        return;
                    case "access":
                        if (!admin && !(player != null && HasMenuAccess(player))) { reply(L("NoPermission")); return; }
                        reply("Menu access (spawning included): " + string.Join(", ", permission.GetPermissionUsers(PermMenu)) + " | groups: " + string.Join(", ", permission.GetPermissionGroups(PermMenu)));
                        return;
                    case "selftest":
                        if (!admin) { reply(L("NoPermission")); return; }
                        reply(SelfTestReport());
                        return;
                    case "prefabs":
                        if (!admin) { reply(L("NoPermission")); return; }
                        reply(ListPrefabs(args));
                        return;
                    case "horses":
                        if (!admin) { reply(L("NoPermission")); return; }
                        HorseCommand(player, args, reply);
                        return;
                    case "items":
                        if (!admin) { reply(L("NoPermission")); return; }
                        reply(ListItems(args));
                        return;
                    case "boss":
                        if (!admin) { reply(L("NoPermission")); return; }
                        BossCommand(player, args, reply);
                        return;
                    case "assets":
                        if (!admin) { reply(L("NoPermission")); return; }
                        reply(ListAssets(args));
                        return;
                    case "entities":
                        if (!admin) { reply(L("NoPermission")); return; }
                        reply(ListEntities(args));
                        return;
                    case "test":
                        if (!admin) { reply(L("NoPermission")); return; }
                        if (args.Length > 0 && args[0] == "tame")
                        {
                            TestTame(ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(10f, 0f, 6f));
                        }
                        else if (args.Length > 0 && args[0] == "combat")
                        {
                            Vector3 spot = player != null ? player.transform.position + Vector3.forward * 4f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(12f, 0f, 0f);
                            TestCombat(reply, spot);
                        }
                        else if (args.Length > 0 && args[0] == "menu")
                        {
                            reply(TestMenu());
                        }
                        else if (args.Length > 0 && args[0] == "stats")
                        {
                            reply(TestStats());
                        }
                        else if (args.Length > 0 && args[0] == "spray")
                        {
                            TestSpray(reply, player != null ? player.transform.position + Vector3.forward * 10f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(30f, 0f, 12f));
                        }
                        else if (args.Length > 0 && args[0] == "boss")
                        {
                            TestBoss(reply, player != null ? player.transform.position + Vector3.forward * 12f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(24f, 0f, 12f));
                        }
                        else if (args.Length > 0 && args[0] == "wild")
                        {
                            TestWild(reply, player != null ? player.transform.position + Vector3.forward * 12f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(20f, 0f, 10f));
                        }
                        else if (args.Length > 0 && args[0] == "horse")
                        {
                            TestHorse(reply, player != null ? player.transform.position + Vector3.forward * 8f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(16f, 0f, 8f));
                        }
                        else if (args.Length > 0 && args[0] == "ritual")
                        {
                            TestRitual(reply, player != null ? player.transform.position + Vector3.forward * 6f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(14f, 0f, 4f), args.Length > 1 ? args[1] : null);
                        }
                        else if (args.Length > 0 && args[0] == "clips")
                        {
                            TestClips(reply, args.Length > 1 ? args[1] : "cow", player != null ? player.transform.position + Vector3.forward * 6f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(10f, 0f, 8f));
                        }
                        else if (args.Length > 0 && args[0] == "death")
                        {
                            TestDeath(reply, player != null ? player.transform.position + Vector3.forward * 10f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(10f, 0f, 14f));
                        }
                        else if (args.Length > 0 && args[0] == "attacks")
                        {
                            TestAttacks(reply, player != null ? player.transform.position + Vector3.forward * 12f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(-30f, 0f, 30f));
                        }
                        else if (args.Length > 0 && args[0] == "flames")
                        {
                            TestFlames(reply, player != null ? player.transform.position + Vector3.forward * 6f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(8f, 0f, 20f));
                        }
                        else if (args.Length > 0 && args[0] == "scare")
                        {
                            TestScare(reply, player != null ? player.transform.position + Vector3.forward * 10f : ServerMgr.FindSpawnPoint(null, 0UL).pos + new Vector3(0f, 0f, 40f));
                        }
                        else
                        {
                            reply("Tests: bm.test combat | tame | ritual [species] | menu | horse | wild | boss | spray | stats | clips <species> | death | attacks | flames | scare");
                        }
                        return;
                    case "spawn":
                        if (!server && !HasMenuAccess(player)) { reply(L("NoPermission")); return; }
                        SpawnCommand(player, args, reply);
                        return;
                    case "clear":
                        if (!server && !HasMenuAccess(player)) { reply(L("NoPermission")); return; }
                        reply(L("Cleared", null, ClearSpawned(player, args.Length > 0 ? float.Parse(args[0]) : 0f)));
                        return;
                }
                if (player == null)
                {
                    reply("This command needs a player.");
                    return;
                }
                if (sub == "ui" && (Has(player, PermUse) || HasMenuAccess(player)))
                {
                    MenuAction(player, args, reply);          // the menu's own buttons work while the plugin is off (Enable)
                    return;
                }
                if (!Active)
                {
                    reply(L("PluginOff"));
                    return;
                }
                if (!Has(player, PermUse))
                {
                    reply(L("NoPermission"));
                    return;
                }
                PlayerCommand(player, sub, args, reply);
            }
            catch (Exception ex)
            {
                Log(0, "Command '" + sub + "' failed: " + ex);
                reply("Command failed: " + ex.Message);
            }
        }

        // Commands that belong to a player in the world.
        private void PlayerCommand(BasePlayer player, string sub, string[] args, Action<string> reply)
        {
            switch (sub)
            {
                case "revert":
                    StopControl(player, "command");
                    return;
                case "attack":
                    DriverOf(player)?.Attack();
                    return;
                case "view":
                    {
                        Driver d = DriverOf(player);
                        if (d == null) { reply("Ride an animal or become one first."); return; }
                        if (args.Length == 0) { d.CycleView(); return; }
                        d.SetView(args[0].ToLowerInvariant().StartsWith("f") ? ViewKind.First : ViewKind.Third);
                        return;
                    }
                case "preset":
                    {
                        string preset = args.Length > 0 ? args[0].ToUpperInvariant() : PresetOf(player);
                        if (preset != "A" && preset != "B" && preset != "C" && preset != "D") { reply("Presets: A (spectator camera), B (plus third-person flag), C (camera at the head), D (first person from the head)."); return; }
                        Driver d = DriverOf(player);
                        if (d != null) d.SetPreset(preset); else SettingsOf(player.userID).Preset = preset;
                        reply("Camera preset " + preset);
                        return;
                    }
                case "emote":
                    {
                        Driver d = DriverOf(player);
                        if (d == null) { reply("Ride an animal or become one first."); return; }
                        d.OnGesture(args.Length > 0 ? args[0] : "emote");
                        return;
                    }
                case "nv":
                    ToggleNightVision(player);
                    return;
                case "give":
                    GiveCommand(player, reply);
                    return;
                case "feed":
                    {
                        Driver fd = DriverOf(player);
                        if (fd == null || fd.Kind != ControlKind.Form) { reply("Feeding is for animal forms."); return; }
                        TryEat(fd);
                        return;
                    }
                case "lie":
                case "sit":
                case "roar":
                case "look":
                case "stand":
                    {
                        Driver d = DriverOf(player);
                        if (d == null) { reply("Ride an animal or become one first."); return; }
                        d.OnGesture(sub);
                        return;
                    }
                case "cam":
                    CameraCommand(player, args, reply);
                    return;
                case "name":
                    RenameCommand(player, args, reply);
                    return;
                case "breed":
                    BreedCommand(player, reply);
                    return;
                case "ride":
                case "form":
                    {
                        BaseNPC2 animal = AnimalInSight(player, 12f);
                        if (animal == null) { reply(L("NotLooking", player.UserIDString, 12)); return; }
                        bool ride = sub == "ride";
                        if (!ride && !IsAdmin(player) && !Has(player, PermTransform)) { reply(L("NoPermission")); return; }
                        if (StartControl(player, animal, ride ? ControlKind.Ride : ControlKind.Form))
                        {
                            Driver d = DriverOf(player);
                            if (d != null && !ride) d.OwnsAnimal = false;
                        }
                        return;
                    }
                case "seat":
                    SeatCommand(player, args, reply);
                    return;
                case "cmd":
                    CommandAnimals(player, args, reply);
                    return;
                case "ui":
                    MenuAction(player, args, reply);
                    return;
            }
            reply("Unknown command. Try /bm help, /bm menu or /bm species.");
        }

        private void GrantCommand(bool grant, string[] args, Action<string> reply)
        {
            if (args.Length < 1)
            {
                reply("Usage: bm." + (grant ? "grant" : "revoke") + " <player name or steamid>");
                return;
            }
            BasePlayer target = FindPlayer(args[0]);
            string id = target != null ? target.UserIDString : args[0];
            string name = target != null ? target.displayName : args[0];
            if (grant)
            {
                permission.GrantUserPermission(id, PermMenu, null);
                if (!permission.UserHasPermission(id, PermUse))
                {
                    permission.GrantUserPermission(id, PermUse, null);
                }
                reply(L("GrantOk", null, name));
            }
            else
            {
                permission.RevokeUserPermission(id, PermMenu);
                reply(L("RevokeOk", null, name));
            }
        }

        // ---------------------------------------------------------------- texts

        private string SpeciesList()
        {
            var sb = new StringBuilder();
            foreach (SpeciesDef s in AllSpecies)
            {
                sb.AppendLine(s.Id.PadRight(10) + s.Display.PadRight(12) + (SpeciesEnabled(s) ? s.Ride == RideTier.Restricted ? "restricted" : s.Ride == RideTier.Standard ? "rideable" : "form only" : "n/a: " + s.Note));
            }
            return sb.ToString();
        }

        private string KeysText()
        {
            return "Keys while riding or in animal form: W/A/S/D move, Shift faster, Ctrl brake (hold A/D for the stop-and-turn), S while running = 180 turn, Space jump, "
                + "F attack (look backwards first = rear attack), G or R change view, B gestures (lie down, sit, roar, look around), E use, H night vision at night, X get off / back to human. "
                + "F5 opens the menu, H switches the night vision: the server asks your game to bind them when you join; if yours refuses, type bind f5 bm.menu and bind h bm.nv in the F1 console.";
        }

        private string HelpText()
        {
            return "Beastmaster " + Version + "\n" + KeysText() + "\nCommands: /bm menu, species, view, preset, revert, cmd follow|wait|guard|attack|release, ride, form"
                + (Cfg.Features.Spawning ? ", spawn <species> [count] [tamed|wild]" : string.Empty) + "\nAdmin: bm.enable, bm.disable, bm.grant, bm.revoke, bm.access, bm.grantgroup, bm.revokegroup, bm.clear, bm.reload, bm.selftest, bm.debug.";
        }

        // ---------------------------------------------------------------- spawning

        private readonly Dictionary<ulong, float> spawnCooldowns = new Dictionary<ulong, float>();

        private void SpawnCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            if (!Cfg.Features.Spawning)
            {
                reply("Spawning is switched off.");
                return;
            }
            if (args.Length < 1)
            {
                reply("Usage: bm.spawn <species> [count] [tamed|wild] [saddled] [player]. Species: " + string.Join(", ", AllSpecies.Where(SpeciesEnabled).Select(s => s.Id).ToArray()));
                return;
            }
            SpeciesDef species = SpeciesById(args[0]);
            if (species == null || !SpeciesEnabled(species))
            {
                reply(L("UnknownSpecies", null, args[0], string.Join(", ", AllSpecies.Where(SpeciesEnabled).Select(s => s.Id).ToArray())));
                return;
            }
            int count = 1;
            bool tamed = false, saddled = false, near = false;
            BasePlayer target = player;
            for (int i = 1; i < args.Length; i++)
            {
                int n;
                if (int.TryParse(args[i], out n)) count = n;
                else if (args[i] == "tamed") tamed = true;
                else if (args[i] == "wild") tamed = false;
                else if (args[i] == "saddled") saddled = true;
                else if (args[i] == "near") near = true;
                else target = FindPlayer(args[i]) ?? target;
            }
            if (target == null)
            {
                reply("Name a player (bm.spawn <species> <count> tamed <player>) when using the server console.");
                return;
            }
            count = Mathf.Clamp(count, 1, Cfg.Spawn.MaxPerAction);
            float last;
            if (player != null && spawnCooldowns.TryGetValue(player.userID, out last) && Time.time - last < Cfg.Spawn.Cooldown)
            {
                reply(L("SpawnCooldown"));
                return;
            }
            if (SpawnedAlive() + count > Cfg.Spawn.MaxAlive)
            {
                reply(L("SpawnLimit", null, Cfg.Spawn.MaxAlive));
                return;
            }
            if (player != null)
            {
                spawnCooldowns[player.userID] = Time.time;
            }
            Vector3 spot = SpawnSpot(target, near);
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = spot + new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)) * (1.5f + i * 0.4f);
                pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.2f;
                BaseEntity entity = SpawnAnimal(species, pos, target, tamed, saddled);
                if (entity != null)
                {
                    made++;
                }
            }
            reply(L("Spawned", null, made, species.Display) + (tamed ? " (tamed to " + target.displayName + ")" : string.Empty));
            Log(2, (player != null ? player.displayName : "console") + " spawned " + made + "x " + species.Id + " at " + spot);
        }

        internal Vector3 SpawnSpot(BasePlayer player, bool nextToPlayer = false)
        {
            RaycastHit hit;
            Vector3 eye = player.eyes.position;
            if (!nextToPlayer && Physics.Raycast(eye, player.eyes.HeadForward(), out hit, Cfg.Spawn.MaxDistance, LayerMask.GetMask("Terrain", "World", "Construction"), QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }
            Vector3 p = player.transform.position + Quaternion.Euler(0f, player.eyes.rotation.eulerAngles.y, 0f) * Vector3.forward * 5f;
            p.y = TerrainMeta.HeightMap.GetHeight(p);
            return p;
        }

        // Creates an animal of a species; tamed = owned by the player (record + tag), saddled = with a single saddle fitted.
        internal BaseEntity SpawnAnimal(SpeciesDef species, Vector3 pos, BasePlayer owner, bool tamed, bool saddled)
        {
            BaseEntity entity = GameManager.server.CreateEntity(species.Prefab, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            if (entity == null)
            {
                return null;
            }
            ulong tag = NewTag();
            entity.skinID = tag;
            Data.Spawned.Add(tag);
            entity.Spawn();
            if (species.Id == "ram")
            {
                MakeMale(entity as LivestockAnimal);
            }
            RidableHorse horse = entity as RidableHorse;
            if (horse != null && Stable != null)
            {
                Stable.AfterSpawn(horse, tamed && owner != null, saddled);
            }
            if (tamed && owner != null)
            {
                TamedRecord rec = MakeTamed(entity, species, owner.userID, saddled);
                TagIndex[rec.Tag] = entity;
                ApplyState(entity, rec);
            }
            return entity;
        }

        internal TamedRecord MakeTamed(BaseEntity entity, SpeciesDef species, ulong owner, bool saddled)
        {
            ulong tag = entity.skinID >= TagBase ? entity.skinID : NewTag();
            entity.skinID = tag;
            TamedRecord rec = new TamedRecord
            {
                Tag = tag,
                Owner = owner,
                Species = species.Id,
                Prefab = entity.PrefabName,
                State = "follow",
                Position = new V3(entity.transform.position),
                Health = entity is BaseCombatEntity ? ((BaseCombatEntity)entity).health : 0f
            };
            if (saddled)
            {
                rec.Gear.Saddle = 1;
            }
            Data.Tamed[tag] = rec;
            return rec;
        }

        private int SpawnedAlive()
        {
            int n = 0;
            foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
            {
                BaseEntity e = net as BaseEntity;
                if (e != null && e.skinID >= TagBase && Data.Spawned.Contains(e.skinID))
                {
                    n++;
                }
            }
            return n;
        }

        private int ClearSpawned(BasePlayer player, float radius)
        {
            int n = 0;
            foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
            {
                BaseEntity e = net as BaseEntity;
                if (e == null || e.skinID < TagBase || !Data.Spawned.Contains(e.skinID))
                {
                    continue;
                }
                if (radius > 0f && player != null && Vector3.Distance(e.transform.position, player.transform.position) > radius)
                {
                    continue;
                }
                Data.Tamed.Remove(e.skinID);
                Data.Spawned.Remove(e.skinID);
                e.Kill();
                n++;
            }
            return n;
        }

        // The seat prefabs to try for riders on animals (the right look differs per species; set Riding.SeatPrefab in the config).
        private static readonly string[] SeatCandidates =
        {
            "assets/prefabs/vehicle/seats/snowmobilepassengerseat.prefab",
            "assets/prefabs/vehicle/seats/horsesaddle.prefab",
            "assets/prefabs/vehicle/seats/motorbikepassengerseat.prefab",
            "assets/prefabs/vehicle/seats/bikepassengerseat.prefab",
            "assets/prefabs/vehicle/seats/sledseatfront.prefab",
        };

        private void SeatCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            if (!IsAdmin(player))
            {
                reply(L("NoPermission"));
                return;
            }
            if (args.Length > 0 && (args[0] == "up" || args[0] == "down"))
            {
                Driver rider = DriverOf(player);
                if (rider == null || rider.Seat == null || rider.Kind != ControlKind.Ride)
                {
                    reply("Sit on an animal first, then /bm seat up or down.");
                    return;
                }
                rider.AdjustSeat(args[0] == "up" ? 0.1f : -0.1f);
                reply("Seat of the " + rider.Species.Display.ToLowerInvariant() + ": " + SeatLiftOf(rider.Species).ToString("0.00") + " m (saved).");
                return;
            }
            if (args.Length == 0)
            {
                reply("Seat for riders now: " + Cfg.Riding.SeatPrefab + "\nCandidates (bm.seat <number>): " + string.Join(" | ", SeatCandidates.Select((s, i) => i + " = " + s.Substring(s.LastIndexOf('/') + 1)).ToArray()));
                return;
            }
            int index;
            if (int.TryParse(args[0], out index) && index >= 0 && index < SeatCandidates.Length)
            {
                Cfg.Riding.SeatPrefab = SeatCandidates[index];
                SaveConfig();
                reply("Seat set to " + SeatCandidates[index] + " (saved). New rides use it.");
            }
        }

        // ---------------------------------------------------------------- self test

        internal string SelfTestReport()
        {
            var report = new List<string>();
            report.Add("Beastmaster " + Version + " on Rust " + Rust.Protocol.printable + ", " + (Active ? "ON" : "OFF"));
            int ok = 0;
            foreach (SpeciesDef s in AllSpecies)
            {
                bool found = s.Available;
                if (found) ok++;
                report.Add("  species " + s.Id.PadRight(10) + (found ? "OK  " + s.Prefab : "MISSING " + s.Note));
            }
            report.Add(ok + "/" + AllSpecies.Count + " species available");
            foreach (string item in new[] { "horse.saddle.single", "horse.saddle.double", "horse.saddlebag", "horse.shoes.basic", "horse.shoes.advanced", "dart.incapacitate", "blowpipe", "syringe.medical", "trap.bear", "spraycan", "torch", "bone.fragments", "water.jar", "waterjug", "cooked.wolfmeat", "cooked.bearmeat" })
            {
                report.Add("  item " + item.PadRight(22) + (ItemManager.FindItemDefinition(item) != null ? "OK" : "MISSING"));
            }
            string smoke = FindPrefabPath("fx", "smoke");
            report.Add("  smoke effect: " + (GameManager.server.FindPrefab(Cfg.Form.SmokePrefab) != null ? Cfg.Form.SmokePrefab : smoke ?? "none found"));
            foreach (string seat in new[] { Cfg.Riding.SeatPrefab, Cfg.Riding.RearSeatPrefab, Cfg.Camera.FirstPersonSeat })
            {
                report.Add("  seat " + seat + " " + (GameManager.server.FindPrefab(seat) != null ? "OK" : "MISSING"));
            }
            foreach (Module m in Modules)
            {
                m.SelfTest(report);
            }
            return string.Join("\n", report.ToArray());
        }
    }

    // ======================================== Config.cs
    public partial class Beastmaster
    {
#pragma warning disable 0649
        // Every number of the specification lives here. Oxide writes the file to oxide/config/Beastmaster.json (carbon/configs on Carbon)
        // on first load; keys missing after an update are added with their defaults.
        internal partial class ConfigData
        {
            [JsonProperty("General")] public GeneralCfg General = new GeneralCfg();
            [JsonProperty("Access")] public AccessCfg Access = new AccessCfg();
            [JsonProperty("Features (switch whole parts on or off)")] public FeatureCfg Features = new FeatureCfg();
            [JsonProperty("Riding")] public RidingCfg Riding = new RidingCfg();
            [JsonProperty("Camera")] public CameraCfg Camera = new CameraCfg();
            [JsonProperty("Movement")] public MovementCfg Movement = new MovementCfg();
            [JsonProperty("Combat")] public CombatCfg Combat = new CombatCfg();
            [JsonProperty("Animal form")] public FormCfg Form = new FormCfg();
            [JsonProperty("Taming")] public TamingCfg Taming = new TamingCfg();
            [JsonProperty("Gear")] public GearCfg Gear = new GearCfg();
            [JsonProperty("Transformation ritual")] public RitualCfg Ritual = new RitualCfg();
            [JsonProperty("Lead")] public LeadCfg Lead = new LeadCfg();
            [JsonProperty("Controls (key binds)")] public ControlsCfg Controls = new ControlsCfg();
            [JsonProperty("Inventory of an animal form")] public InventoryCfg Inventory = new InventoryCfg();
            [JsonProperty("Horse")] public HorseCfg Horse = new HorseCfg();
            [JsonProperty("Wild behaviour")] public WildCfg Wild = new WildCfg();
            [JsonProperty("Relations between animals")] public RelationCfg Relations = new RelationCfg();
            [JsonProperty("Spawn tool")] public SpawnCfg Spawn = new SpawnCfg();
            [JsonProperty("Spray can")] public SprayCfg Spray = new SprayCfg();
            [JsonProperty("Boss animals")] public BossCfg Boss = new BossCfg();
            [JsonProperty("Climate")] public ClimateCfg Climate = new ClimateCfg();
            [JsonProperty("Species overrides (key = species id, only what you change)")]
            public Dictionary<string, SpeciesOverride> Species = new Dictionary<string, SpeciesOverride>();
        }

        internal partial class GeneralCfg
        {
            [JsonProperty("Log level (0 errors, 1 warnings, 2 info, 3 debug)")] public int LogLevel = 2;
            [JsonProperty("Chat prefix")] public string Prefix = "<color=#9fd36b>[Beastmaster]</color> ";
            [JsonProperty("Plugin switched on at first start")] public bool EnabledByDefault = true;
            [JsonProperty("Switch Beastmaster on every time the plugin loads (the menu button still switches it off until the next load)")] public bool AutoEnable = true;
            [JsonProperty("Tell a player once, when they first join, how to open the menu")] public bool JoinHint = true;
        }

        internal partial class AccessCfg
        {
            [JsonProperty("Everyone with beastmaster.use may open the menu (player layer)")] public bool OpenForEveryone = true;
            [JsonProperty("Server owners get menu access automatically")] public bool OwnersAuthorised = true;
            [JsonProperty("Moderators get menu access automatically")] public bool ModeratorsAuthorised = true;
        }

        internal partial class FeatureCfg
        {
            public bool Riding = true;
            public bool AnimalForm = true;
            public bool Taming = true;
            public bool Gear = true;
            public bool Commands = true;
            public bool Combat = true;
            public bool Relations = true;
            public bool HorseAi = true;
            public bool Transformation = true;
            public bool Spray = true;
            public bool ThirdPerson = true;
            public bool Lead = true;
            public bool BossAnimals = true;
            public bool Climate = true;
            public bool WildDefence = true;
            public bool Spawning = true;
        }

        internal partial class RidingCfg
        {
            [JsonProperty("Admins may ride any animal without taming or a saddle (for testing)")] public bool AdminBypass = true;
            [JsonProperty("Riding needs a tamed animal (standard species)")] public bool RequireTame = true;
            [JsonProperty("Riding needs a saddle in the animal's gear (standard species)")] public bool RequireSaddle = true;
            [JsonProperty("Who may ride an owned animal (owner, team, friends, clan, anyone)")] public string Ownership = "team";
            [JsonProperty("Seat used for riders on non-horse animals")] public string SeatPrefab = "assets/prefabs/vehicle/seats/snowmobilepassengerseat.prefab";
            [JsonProperty("Seat of the second rider (double saddle)")] public string RearSeatPrefab = "assets/prefabs/vehicle/seats/snowmobilepassengerseat.prefab";
            [JsonProperty("Maximum distance to mount (m)")] public float MountDistance = 3.5f;
            [JsonProperty("Block riding in safe zones")] public bool BlockInSafeZone = false;
            [JsonProperty("Maximum riders on the whole server (0 = unlimited)")] public int MaxRiders = 0;
        }

        internal partial class CameraCfg
        {
            [JsonProperty("Default view of the animal form (third, first)")] public string DefaultView = "third";
            [JsonProperty("Default view when riding (first, third) - G switches")] public string DefaultRideView = "first";
            [JsonProperty("Camera preset of the third person (A = spectator camera around the animal, B = same plus the game's third-person flag, C = camera at the animal's head)")]
            public string ThirdPreset = "A";
            [JsonProperty("Default camera pitch (degrees, positive = looking down)")] public float DefaultPitch = 8f;
            [JsonProperty("Third person: how far the camera pivot is moved back behind the animal (m)")] public float ThirdBack = 0.55f;
            [JsonProperty("First person: height of the eyes as a share of the animal's height")] public float EyeHeight = 0.98f;
            [JsonProperty("First person: how far forward the eyes sit (share of the distance from the middle of the body to the nose)")] public float EyeForward = 0.58f;
            [JsonProperty("Height of the camera pivot as a share of the animal's height (every species also has its own factor)")] public float PivotHeight = 0.95f;
            [JsonProperty("First person of the animal form (spectate = the human is completely invisible, seat = hidden seat, legs may show)")] public string FirstPersonMode = "spectate";
            [JsonProperty("Seat of the hidden body when the first person mode is seat")] public string FirstPersonSeat = "assets/bundled/prefabs/static/chair.invisible.static.prefab";
        }

        internal partial class InventoryCfg
        {
            [JsonProperty("The inventory shows the animal instead of the player (a picture over the player's model and avatar)")] public bool ShowAnimal = true;
            [JsonProperty("Picture of each species (species id -> image address, a PNG on your web space). Without one the animal's name is shown.", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> Images = new Dictionary<string, string>();
            [JsonProperty("The game's own item whose icon stands for each species when there is no picture above (species id -> item shortname)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> Items = new Dictionary<string, string>
            {
                { "horse", "hat.horsemask" }, { "cow", "hat.oxmask" }, { "bull", "hat.oxmask" }, { "calf", "beefmeat" }, { "sheep", "muttonmeat" }, { "ram", "muttonmeat" }, { "lamb", "muttonmeat" },
                { "stag", "deer.skull.mask" }, { "boar", "meat.boar" }, { "bear", "bearmeat" }, { "polarbear", "bearmeat" }, { "wolf", "hat.wolf" }, { "panther", "bigcatmeat" },
                { "tiger", "hat.tigermask" }, { "crocodile", "crocodilemeat" }, { "chicken", "chicken.costume" }, { "rabbit", "hat.rabbitmask" }, { "squirrel", "skull" },
                { "frog", "skull" }, { "crab", "skull" }
            };
            [JsonProperty("Where the model of the player is in the inventory (pixels from the screen's centre: left bottom, right top)")] public string ModelMin = "-690 -230";
            public string ModelMax = "-360 300";
            [JsonProperty("Where the avatar icon is")] public string IconMin = "-690 305";
            public string IconMax = "-630 365";
        }

        internal partial class ControlsCfg
        {
            [JsonProperty("Ask the player's game to bind the keys below when they join (the server cannot rebind keys, but it can ask the game to)")] public bool AutoBind = true;
            [JsonProperty("Key that opens the menu")] public string MenuKey = "f5";
            [JsonProperty("Key that switches the night vision of an animal on and off")] public string NightKey = "h";
        }

        internal partial class MovementCfg
        {
            [JsonProperty("Speed mode: authored = the animals' own gait speeds (no sliding), table = the brief's speed table (may slide)")] public string SpeedMode = "authored";
            [JsonProperty("Speed bonus of basic horseshoes (m/s)")] public float ShoesBasic = 1.5f;
            [JsonProperty("Speed bonus of high quality horseshoes (m/s)")] public float ShoesAdvanced = 2.5f;
            [JsonProperty("Speed change of the saddle bag (m/s)")] public float SaddleBag = -1.0f;
            [JsonProperty("Turn rate in degrees per second at standstill / at full speed")] public float TurnRateSlow = 150f;
            public float TurnRateFast = 105f;
            [JsonProperty("Water: slows animals like the horse")] public bool WaterSlowdown = true;
            [JsonProperty("Water: speed share at the deepest wading (0-1)")] public float WaterMinSpeed = 0.35f;
            [JsonProperty("Water: share of the body under water from which the animal only walks")] public float WaterWalkOnly = 0.5f;
            [JsonProperty("Steep slopes: chance per second to fall at a run when the slope exceeds the limit (0 = off)")] public float SlopeFallChance = 0.35f;
            [JsonProperty("Steep slopes: angle limit in degrees")] public float SlopeLimit = 38f;
            [JsonProperty("Steep slopes: chance that a fall kills the animal")] public float SlopeFallDeathChance = 0.15f;
            [JsonProperty("Jump distance (m) and height (m)")] public float JumpDistance = 3.5f;
            public float JumpHeight = 1.5f;
            [JsonProperty("Jump cooldown (s)")] public float JumpCooldown = 1.2f;
            [JsonProperty("An animal completely under water drowns")] public bool Drowning = true;
            [JsonProperty("Seconds a big animal can stay completely under water")] public float DrownSeconds = 15f;
            [JsonProperty("Seconds a small animal (chicken, rabbit, squirrel) can stay completely under water")] public float DrownSecondsSmall = 6f;
            [JsonProperty("Drowning damage per second (percent of the animal's health)")] public float DrownDamagePercent = 8f;
            [JsonProperty("Damage of a crash into an obstacle or a fall (percent of the animal's health)")] public float CrashDamagePercent = 2f;
        }

        internal partial class CombatCfg
        {
            [JsonProperty("Damage multiplier on top of the game's own animal damage")] public float DamageScale = 1f;
            [JsonProperty("Looking back more than this many degrees before the attack = rear attack")] public float RearAngle = 90f;
            [JsonProperty("Attack cooldown (s)")] public float Cooldown = 0.9f;
            [JsonProperty("Attacks hurt players")] public bool HitPlayers = true;
            [JsonProperty("Attacks hurt animals and NPCs")] public bool HitAnimals = true;
            [JsonProperty("Attacks hurt buildings and deployables")] public bool HitStructures = true;
            [JsonProperty("Attacks gather from trees, ore and corpses")] public bool Gather = true;
            [JsonProperty("An attack turns the animal toward the nearest target in front")] public bool AutoAim = true;
            [JsonProperty("Auto aim: widest angle to a target (degrees)")] public float AutoAimAngle = 100f;
            [JsonProperty("Never hit team members and friends")] public bool SparePartyMembers = true;
            [JsonProperty("Never hit anything in a safe zone")] public bool SparesSafeZone = true;
            [JsonProperty("Damage of running into a player or an animal at a run (bull, ram, cow, sheep, stag, boar, horse)")] public float ChargeDamage = 22f;
        }

        internal partial class FormCfg
        {
            [JsonProperty("Hide the human completely (spectator trick for third person, hidden seat for first person)")] public bool HideHuman = true;
            [JsonProperty("The human cannot be hurt, the animal can")] public bool Invulnerable = true;
            [JsonProperty("The animal's health is the player's health, animal dies = player dies")] public bool LinkHealth = true;
            [JsonProperty("Turrets, NPCs and animals ignore the hidden human")] public bool Untargetable = true;
            [JsonProperty("Animal form may open doors")] public bool Doors = true;
            [JsonProperty("Animal form may loot containers and corpses")] public bool Looting = true;
            [JsonProperty("Animal form may not drive vehicles")] public bool BlockDriving = true;
            [JsonProperty("Animal form may not ride animals")] public bool BlockRiding = true;
            [JsonProperty("Held items in animal form (all, buildOnly, none)")] public string HeldItems = "all";
            [JsonProperty("Smoke effect on transformation and on return")] public bool Smoke = true;
            [JsonProperty("The animal sleeps where the player logs out")] public bool SleepOnLogout = true;
            [JsonProperty("Seconds an offline animal body stays before the player is returned to human form (0 = never)")] public int OfflineKeepSeconds = 1800;
            [JsonProperty("Seconds between leaving the animal form and the next transformation")] public int Cooldown = 120;
            [JsonProperty("Effect (and sound) when the human vanishes into the animal and when the animal vanishes")] public string SmokePrefab = "assets/prefabs/missions/portal/proceduraldungeon/effects/disappear.prefab";
            [JsonProperty("Effect (and sound) when the human appears again")] public string AppearPrefab = "assets/prefabs/missions/portal/proceduraldungeon/effects/appear.prefab";
            [JsonProperty("Switch the player's own HUD off while he is an animal (the game's HUD setting, asked of the player's game)")] public bool HideHud = true;
            [JsonProperty("The animal has health, stamina, hunger and thirst (shown on a small panel)")] public bool Needs = true;
            [JsonProperty("How fast hunger and thirst fall (1 = about 40 and 25 minutes from full to empty)")] public float NeedsRate = 1f;
            [JsonProperty("An animal with empty hunger or thirst slowly loses health")] public bool StarveDamage = false;
            [JsonProperty("Seconds between bites")] public float BiteCooldown = 3f;
            [JsonProperty("Most health food can give per second")] public float HealPerSecond = 6f;
            [JsonProperty("Salt water quenches thirst this much (share of fresh water; 0 = only fresh water can be drunk)")] public float SeaWaterShare = 0f;
            [JsonProperty("Four key hint cells beside the hotbar (F attack, Ctrl brake, Shift sprint, Space jump)")] public bool KeyHints = true;
            [JsonProperty("Who may ride, lead, milk and shear an animal form (anyone, team, nobody)")] public string HandledBy = "anyone";
            [JsonProperty("These animal forms carry riders only after they have eaten food that lay in front of them", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RideFedSpecies = new List<string> { "horse", "bull", "cow", "ram", "sheep", "stag" };
            [JsonProperty("Riders on a fed animal form")] public int RidersFed = 2;
            [JsonProperty("Riders on the other animal forms")] public int RidersOther = 1;
            [JsonProperty("Seconds a fed animal form stays ready to carry riders")] public float FedSeconds = 300f;
            [JsonProperty("Length of the leash of an animal form (m)")] public float LeashLength = 5f;
        }

        internal partial class TamingCfg
        {
            [JsonProperty("Wolf: seconds of chasing with a lit torch")] public float WolfChaseSeconds = 60f;
            [JsonProperty("Wolf: how far the torch scares (m)")] public float TorchRange = 18f;
            [JsonProperty("Predator dart: speed reduction (0-1)")] public float DartSlow = 0.6f;
            [JsonProperty("Predator dart: seconds")] public float DartSlowSeconds = 30f;
            [JsonProperty("Bear trap damage")] public float TrapDamage = 50f;
            [JsonProperty("Syringes needed on a trapped predator")] public int SyringesNeeded = 3;
            [JsonProperty("Seconds the freed predator stays neutral before it follows")] public float NeutralSeconds = 5f;
            [JsonProperty("Deer sleep seconds after the dart")] public float SleepSeconds = 60f;
            [JsonProperty("Deer: food items to feed")] public int FoodNeeded = 3;
            [JsonProperty("Deer: seconds in which the food has to be eaten")] public float FoodSeconds = 5f;
            [JsonProperty("Stag food item")] public string StagFood = "potato";
            [JsonProperty("Doe food item")] public string DoeFood = "corn";
            [JsonProperty("Livestock and horse: seconds of familiarity needed to be tamed")] public float BondSeconds = 300f;
            [JsonProperty("Maximum owned animals per player")] public int MaxOwned = 8;
            [JsonProperty("Commands reach animals this far from the player (m)")] public float CommandRange = 45f;
            [JsonProperty("Followers stay this close to the owner (m)")] public float FollowDistance = 4f;
            [JsonProperty("Followers further away than this are moved next to the owner (m, 0 = never)")] public float TeleportDistance = 0f;
            [JsonProperty("Guard: stay within this distance of the owner (m)")] public float GuardRadius = 50f;
            [JsonProperty("Gestures on foot command nearby owned animals (ok = wait, wave = follow, thumbs up = guard, point = attack)")] public bool GestureCommands = true;
        }

        internal partial class GearCfg
        {
            [JsonProperty("Bag slots")] public int BagSlots = 18;
            [JsonProperty("Only owner and team may open the bag")] public bool BagPrivate = true;
        }

        internal partial class RitualCfg
        {
            [JsonProperty("Circles around the corpse")] public int Circles = 10;
            [JsonProperty("Circles around the corpse of a boss")] public int BossCircles = 50;
            [JsonProperty("Seconds the body burns: press E at the fire in this time or the fire and the body vanish")] public float BurnSeconds = 10f;
            [JsonProperty("The ring of fire: radius around the body (m), number of flames, size of a flame")] public float FireRadius = 1.4f;
            public int FireCount = 10;
            public float FireScale = 0.45f;
            [JsonProperty("The fire burns the player who stands in it (damage per second)")] public float FireDamage = 6f;
            [JsonProperty("Circle radius (m)")] public float Radius = 4f;
            [JsonProperty("Health cost (share of current health)")] public float HealthCost = 0.5f;
            [JsonProperty("Bleeding after the blood sacrifice")] public float Bleeding = 40f;
            [JsonProperty("Seconds a ritual corpse is kept")] public int CorpseKeepSeconds = 600;
            [JsonProperty("The corpse has to be of an animal this player (or team) killed")] public bool RequireOwnKill = true;
            [JsonProperty("Block the ritual in safe zones")] public bool BlockInSafeZone = true;
            [JsonProperty("Fire entity of the ring of flames")] public string FirePrefab = "assets/bundled/prefabs/oilfireballsmall.prefab";
        }

        internal partial class LeadCfg
        {
            [JsonProperty("Leash breaks at this distance (m)")] public float BreakDistance = 50f;
            [JsonProperty("Leash breaks when the animal is hurt")] public bool BreakWhenHurt = false;
            [JsonProperty("Longest chain of led animals behind an animal-form player")] public int MaxChain = 4;
        }

        internal partial class HorseCfg
        {
            [JsonProperty("Share of wild horses that are calm, shy, aggressive (should add up to 1)")] public float CalmShare = 0.40f;
            public float ShyShare = 0.35f;
            public float AggressiveShare = 0.25f;
            [JsonProperty("A shy horse calms down this many seconds after the last scare")] public float ShyCalmSeconds = 15f;
            [JsonProperty("Seconds an incapacitate dart keeps a horse asleep")] public float SleepSeconds = 60f;
            [JsonProperty("Aggression radius (m)")] public float AggressionRadius = 14f;
            [JsonProperty("New wild horses are born without a saddle and must be tamed first")] public bool WildUnsaddled = true;
            [JsonProperty("Wild horses wander a little around where they stand")] public bool Wander = true;
            [JsonProperty("A shy horse flees from players closer than this (m)")] public float ShyRange = 10f;
            [JsonProperty("Speed of a fleeing or charging horse (m/s)")] public float FleeSpeed = 9f;
            [JsonProperty("Horse attack: damage")] public float AttackDamage = 30f;
            [JsonProperty("Horse attack: seconds between attacks")] public float AttackCooldown = 3f;
            [JsonProperty("Horse attack: rear up and strike forward (also with F while riding)")] public bool RearUpAttack = true;
            [JsonProperty("Riding: Space makes the horse jump")] public bool Jump = true;
        }

        internal partial class WildCfg
        {
            [JsonProperty("Chance (%) that an animal that is not on the list below also defends itself when attacked, otherwise it only flees")] public float DefendChance = 10f;
            [JsonProperty("Species that always defend themselves", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> AlwaysDefend = new List<string> { "bear", "polarbear", "boar", "tiger", "panther", "bull", "ram", "crocodile" };
            [JsonProperty("Species that may defend (empty = every other species)", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> MayDefend = new List<string>();
            [JsonProperty("Seconds a defending animal goes for its attacker")] public float DefendSeconds = 25f;
            [JsonProperty("Seconds an animal runs from its attacker")] public float FleeSeconds = 12f;
        }

        // Who reacts to whom when somebody rides an animal or is one (R7). Relation: hostile (attacks), follow (keeps near), tether (never
        // wanders further than Keep metres), flee (runs away), tolerate (leaves alone). Everything not listed is the game's own behaviour.
        internal partial class RelationCfg
        {
            [JsonProperty("Animals notice a rider or a transformed player within (m)")] public float Range = 40f;
            [JsonProperty("Rules (Observer reacts to Subject; Keep = distance in metres)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<RelationRule> Rules = new List<RelationRule>
            {
                new RelationRule("bear", "stag", "hostile"),
                new RelationRule("polarbear", "stag", "hostile"),
                new RelationRule("bear", "doe", "hostile"),
                new RelationRule("polarbear", "doe", "hostile"),
                new RelationRule("bear", "bear", "tolerate"),
                new RelationRule("polarbear", "polarbear", "tolerate"),
                new RelationRule("bear", "polarbear", "hostile"),
                new RelationRule("polarbear", "bear", "hostile"),
                new RelationRule("cow", "bull", "follow", 6f),
                new RelationRule("sheep", "ram", "follow", 6f),
                new RelationRule("wolf", "wolf", "tether", 50f),
                new RelationRule("bull", "bull", "hostile"),
                new RelationRule("ram", "ram", "hostile"),
            };
        }

        internal partial class RelationRule
        {
            public string Observer = string.Empty;
            public string Subject = string.Empty;
            public string Relation = "tolerate";
            public float Keep = 6f;
            public RelationRule() { }
            public RelationRule(string observer, string subject, string relation, float keep = 6f)
            {
                Observer = observer;
                Subject = subject;
                Relation = relation;
                Keep = keep;
            }
        }

        internal partial class SpawnCfg
        {
            [JsonProperty("Largest amount per spawn command")] public int MaxPerAction = 10;
            [JsonProperty("Seconds between spawn commands per player")] public int Cooldown = 3;
            [JsonProperty("Largest number of plugin-spawned animals alive")] public int MaxAlive = 100;
            [JsonProperty("Largest distance to the spawn spot (m)")] public float MaxDistance = 60f;
        }

        internal partial class SprayCfg
        {
            [JsonProperty("Only owner or team may re-roll")] public bool OwnerOnly = true;
        }

        internal partial class BossCfg
        {
            [JsonProperty("Chance (%) per check that a boss appears")] public float SpawnChance = 4f;
            [JsonProperty("Seconds between checks")] public float CheckSeconds = 300f;
            [JsonProperty("Boss health")] public float Health = 4000f;
            [JsonProperty("Boss damage")] public float Damage = 99f;
            [JsonProperty("Boss speed multiplier")] public float SpeedScale = 1.5f;
            [JsonProperty("Mini boss health")] public float MiniHealth = 2000f;
            [JsonProperty("Chance (%) that a tamed boss breeds a mini boss")] public float MiniChance = 10f;
            [JsonProperty("Scrap: bull, ram, horse, stag")] public int ScrapBull = 4000;
            public int ScrapRam = 4000;
            public int ScrapHorse = 6000;
            public int ScrapStag = 7000;
            [JsonProperty("Scrap bonus when several bosses are killed together")] public float GroupBonus = 1.5f;
            [JsonProperty("Bosses only live at night")] public bool NightOnly = true;
            [JsonProperty("Largest number of bosses alive at once")] public int MaxAlive = 2;
            [JsonProperty("Boss size (1 = normal; the game scales the model the way the admin command 'entity scale' does)")] public float Scale = 1.25f;
            [JsonProperty("Mini boss size")] public float MiniScale = 1.1f;
            [JsonProperty("A boss notices players within (m)")] public float AggroRadius = 60f;
            [JsonProperty("Seconds a boss hit keeps the victim burning")] public float BurnSeconds = 4f;
            [JsonProperty("Heat damage per second while burning")] public float BurnDamage = 5f;
            [JsonProperty("Everybody hears a roar when a boss appears")] public bool Announce = true;
            [JsonProperty("Seconds in which bosses killed together count as a group")] public float GroupSeconds = 120f;
            [JsonProperty("Effect when a boss appears")] public string SpawnEffect = "assets/bundled/prefabs/fx/fire_explosion.prefab";
            [JsonProperty("Glowing eyes: the light that is fixed to the head of a boss, by kind (a coloured lamp of the game; empty = none)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> Eyes = new Dictionary<string, string>
            {
                { "horse", "assets/bundled/prefabs/modding/cinematic/cinelights/cinelight_spot_red.prefab" },
                { "bull", "assets/bundled/prefabs/modding/cinematic/cinelights/cinelight_spot_green.prefab" },
                { "ram", "assets/bundled/prefabs/modding/cinematic/cinelights/cinelight_spot_warm.prefab" },
                { "stag", "assets/bundled/prefabs/modding/cinematic/cinelights/cinelight_spot_blue.prefab" },
            };
            [JsonProperty("A boss bull or ram gets the darkest coat the game's genes allow")] public bool DarkCoat = true;
            [JsonProperty("Effects around a boss, by kind (played every 1.5 s)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<string>> Effects = new Dictionary<string, List<string>>
            {
                { "horse", new List<string> { "assets/bundled/prefabs/fx/fire/fire_v3.prefab" } },
                { "bull", new List<string> { "assets/prefabs/weapons/paintball_gun/effects/paintball_impact_burst_green.prefab" } },
                { "ram", new List<string> { "assets/bundled/prefabs/fx/impacts/bullet/metal/metal1.prefab" } },
                { "stag", new List<string> { "assets/bundled/prefabs/fx/player/frosty_breath.prefab", "assets/bundled/prefabs/fx/impacts/paintball/generic/paintball_impact_burst_white.prefab" } },
            };
        }

        internal partial class ClimateCfg
        {
            [JsonProperty("Polar animals outside the snow need water in their inventory")] public bool PolarNeedsWater = true;
            [JsonProperty("Other animals in the snow need warm meat in their inventory")] public bool ColdNeedsMeat = true;
            [JsonProperty("Speed share of an animal that lacks water or warmth")] public float PenaltySpeed = 0.7f;
            [JsonProperty("Damage share of an animal that lacks water or warmth")] public float PenaltyDamage = 0.7f;
            [JsonProperty("Animals that belong in the snow (they need water outside it)", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> SnowNative = new List<string> { "polarbear" };
            [JsonProperty("Seconds one cooked meat keeps an animal warm in the snow")] public float MeatSeconds = 90f;
        }

        internal partial class SpeciesOverride
        {
            [JsonProperty("Enabled")] public bool? Enabled;
            [JsonProperty("Speed multiplier")] public float? SpeedScale;
            [JsonProperty("Walk speed for the table mode (m/s)")] public float? Walk;
            [JsonProperty("Sprint speed for the table mode (m/s)")] public float? Sprint;
            [JsonProperty("Top speed for the table mode (m/s)")] public float? Top;
            [JsonProperty("Body yaw offset in degrees")] public float? YawOffset;
            [JsonProperty("Camera pivot height multiplier")] public float? PivotScale;
            [JsonProperty("Camera height added in metres (what /bm cam save stores)")] public float? PivotLift;
            [JsonProperty("Height of the rider's seat added in metres (what /bm seat up|down stores)")] public float? SeatLift;
        }
    }

    // ======================================== Control.cs
    public partial class Beastmaster
    {
        internal enum ControlKind { Ride, Form }
        internal enum ViewKind { First, Third }

        internal readonly Dictionary<ulong, Driver> Drivers = new Dictionary<ulong, Driver>();
        internal readonly HashSet<BaseEntity> OwnSeats = new HashSet<BaseEntity>();

        internal Driver DriverOf(BasePlayer player)
        {
            Driver d;
            return player != null && Drivers.TryGetValue(player.userID, out d) && d != null && d.Alive ? d : null;
        }

        internal Driver DriverOfAnimal(BaseNetworkable animal)
        {
            if (animal == null)
            {
                return null;
            }
            foreach (Driver d in Drivers.Values)
            {
                if (d != null && d.Alive && d.Body == animal)
                {
                    return d;
                }
            }
            return null;
        }

        internal bool IsFormPlayer(BasePlayer player)
        {
            Driver d = DriverOf(player);
            return d != null && d.Kind == ControlKind.Form;
        }

        // ---------------------------------------------------------------- starting and stopping

        // Why a player may not ride or become an animal right now; null = allowed.
        internal string ControlProblem(BasePlayer player, BaseCombatEntity animal, ControlKind kind, out object[] args)
        {
            args = new object[0];
            SpeciesDef species = SpeciesOf(animal);
            if (!Active)
            {
                return "PluginOff";
            }
            if (species == null || !SpeciesEnabled(species))
            {
                args = new object[] { species != null ? species.Display : "?" };
                return "SpeciesOff";
            }
            if (DriverOf(player) != null)
            {
                return "AlreadyControlling";
            }
            if (!Alive(animal) || DriverOfAnimal(animal) != null)
            {
                return "NotLooking";
            }
            if (kind == ControlKind.Ride)
            {
                if (animal is RidableHorse)
                {
                    return "RideHorseVanilla";
                }
                if (LeftOwnedBy(animal, player))
                {
                    return null;                       // the animal the player has just left carries him
                }
                if (!Cfg.Features.Riding)
                {
                    return "NoPermission";
                }
                if (species.Ride == RideTier.None)
                {
                    args = new object[] { species.Display };
                    return "SpeciesOff";
                }
                if (species.Ride == RideTier.Restricted)
                {
                    return Has(player, PermRideRestricted) ? null : "NoPermission";
                }
                if (!Has(player, PermRide))
                {
                    return "NoPermission";
                }
                if (Cfg.Riding.AdminBypass && IsAdmin(player))
                {
                    return null;
                }
                TamedRecord rec = RecordOf(animal);
                if (Cfg.Riding.RequireTame && (rec == null || rec.Owner == 0UL))
                {
                    return "NeedTame";
                }
                if (rec != null && !MayUse(player, rec.Owner, Cfg.Riding.Ownership))
                {
                    return "NotYours";
                }
                if (Cfg.Riding.RequireSaddle && (rec == null || rec.Gear.Saddle <= 0))
                {
                    return "NeedSaddle";
                }
                return null;
            }
            if (!Cfg.Features.AnimalForm)
            {
                return "NoPermission";
            }
            if (species.TransformRestricted ? !Has(player, PermTransformRestricted) : !Has(player, PermTransform))
            {
                return "NoPermission";
            }
            return null;
        }

        // owner / team / friends / clan / anyone
        internal bool MayUse(BasePlayer player, ulong owner, string rule)
        {
            if (owner == 0UL || player.userID == owner || rule == "anyone")
            {
                return true;
            }
            if (rule == "owner")
            {
                return false;
            }
            RelationshipManager.PlayerTeam team = player.Team;
            if (team != null && team.members.Contains(owner))
            {
                return true;
            }
            return false;
        }

        internal bool StartControl(BasePlayer player, BaseCombatEntity animal, ControlKind kind, bool silent = false)
        {
            object[] args;
            string problem = ControlProblem(player, animal, kind, out args);
            if (problem != null)
            {
                if (!silent)
                {
                    Tell(player, problem, args);
                }
                return false;
            }
            Driver driver = animal.gameObject.AddComponent<Driver>();
            driver.Setup(this, player, animal, kind);
            Drivers[player.userID] = driver;
            if (!driver.Begin())
            {
                Drivers.Remove(player.userID);
                UnityEngine.Object.Destroy(driver);
                return false;
            }
            SpeciesDef species = SpeciesOf(animal);
            if (!silent && kind == ControlKind.Ride)
            {
                Tell(player, "Riding", species != null ? species.Display : animal.ShortPrefabName);
            }
            return true;
        }

        internal void StopControl(BasePlayer player, string reason)
        {
            Driver d;
            if (player != null && Drivers.TryGetValue(player.userID, out d))
            {
                Drivers.Remove(player.userID);
                if (d != null)
                {
                    d.Release(reason);
                }
            }
        }

        internal void StopAllControl(string reason)
        {
            foreach (Driver d in Drivers.Values.ToList())
            {
                if (d != null)
                {
                    d.Release(reason);
                }
            }
            Drivers.Clear();
        }

        // E on a saddled animal mounts it; crouch + E opens the gear panel (see Gear).
        internal bool TryMountFromUse(BasePlayer player)
        {
            if (!Cfg.Features.Riding || !Has(player, PermUse))
            {
                return false;
            }
            BaseNPC2 animal = AnimalInSight(player, Cfg.Riding.MountDistance, 20f);
            if (animal == null || DriverOfAnimal(animal) != null)
            {
                return false;
            }
            SpeciesDef species = SpeciesOf(animal);
            if (species == null || species.Ride == RideTier.None)
            {
                return false;
            }
            TamedRecord rec = RecordOf(animal);
            if (species.Ride == RideTier.Standard && (rec == null || rec.Gear.Saddle <= 0))
            {
                return false;   // not ours to handle: tamed animals without a saddle open their gear panel instead
            }
            if (player.serverInput.IsDown(BUTTON.DUCK))
            {
                return false;   // crouch + E = gear panel
            }
            return StartControl(player, animal, ControlKind.Ride);
        }

        internal static void SetThirdPersonFlag(BasePlayer player, bool on)
        {
            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.ThirdPersonViewmode) == on)
            {
                return;
            }
            player.SetPlayerFlag(BasePlayer.PlayerFlags.ThirdPersonViewmode, on);
            player.SendNetworkUpdateImmediate();
        }

        // ---------------------------------------------------------------- module (hooks)

        internal sealed class ControlModule : Module
        {
            public override int Order => 10;

            public override void OnDisable()
            {
                Bm.StopAllControl("plugin switched off");
            }

            public override void Unload()
            {
                Bm.StopAllControl("plugin unload");
            }

            public override void Tick(float now)
            {
                if (Bm.Drivers.Count == 0)
                {
                    return;
                }
                foreach (ulong id in Bm.Drivers.Where(kv => kv.Value == null || !kv.Value.Alive).Select(kv => kv.Key).ToList())
                {
                    Bm.Drivers.Remove(id);
                }
            }

            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                Driver d;
                if (Bm.Drivers.TryGetValue(player.userID, out d) && d != null && d.Alive)
                {
                    d.ReadInput(input);
                    if (d.Spectating)
                    {
                        // jump and duck would make the game cycle the spectated player
                        input.current.buttons &= ~((int)BUTTON.JUMP | (int)BUTTON.DUCK);
                    }
                    return null;
                }
                return null;
            }

            // F and X already send commands to the server with the vanilla binds (the server cannot rebind keys).
            public override object OnServerCommand(BasePlayer player, ConsoleSystem.Arg arg)
            {
                Driver d = Bm.DriverOf(player);
                if (d == null)
                {
                    return null;
                }
                switch (arg.cmd.FullName)
                {
                    case "inventory.lighttoggle":
                        d.Attack();
                        return true;
                    case "vehicle.swapseats":
                        BasePlayer p = player;
                        Bm.NextTick(() => Bm.StopControl(p, "key X"));
                        return true;
                }
                return null;
            }

            // G places a map marker with the vanilla bind: while controlling, it changes the view instead.
            public override object OnMapMarkerAdd(BasePlayer player, ProtoBuf.MapNote note)
            {
                Driver d = Bm.DriverOf(player);
                if (d == null)
                {
                    return null;
                }
                if (d.Kind == ControlKind.Form || player.serverInput.IsDown(BUTTON.DUCK))
                {
                    Bm.ToggleNightVision(player);          // G is the night vision of an animal form
                    return true;
                }
                d.CycleView();
                return true;
            }

            public override object CanUseGesture(BasePlayer player, GestureConfig gesture)
            {
                Driver d = Bm.DriverOf(player);
                if (d == null)
                {
                    return null;
                }
                d.OnGesture(gesture.convarName);
                return false;
            }

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                BasePlayer player = entity as BasePlayer;
                if (player != null && Bm.Cfg.Form.Invulnerable && Bm.IsFormPlayer(player))
                {
                    return true;
                }
                return null;
            }

            public override object CanBeTargeted(BaseCombatEntity target, MonoBehaviour turret)
            {
                BasePlayer player = target as BasePlayer;
                if (player != null && Bm.Cfg.Form.Untargetable && Bm.IsFormPlayer(player))
                {
                    return false;
                }
                return null;
            }

            // Space is the animal's jump, not the seat's dismount key. A rider may dismount while the animal stands still.
            public override object OnPlayerWantsDismount(BasePlayer player, BaseMountable mountable)
            {
                Driver d = Bm.DriverOf(player);
                if (d == null || d.Seat != mountable)
                {
                    return null;
                }
                if (d.Kind == ControlKind.Ride && d.Standing)
                {
                    return null;
                }
                return true;
            }

            public override void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
            {
                Driver d = Bm.DriverOf(player);
                if (d != null && d.Seat == mountable && !d.SwappingSeats)
                {
                    Bm.StopControl(player, "dismounted");
                }
            }

            public override object CanMountEntity(BasePlayer player, BaseMountable mountable)
            {
                if (player == null || mountable == null || Bm.OwnSeats.Contains(mountable))
                {
                    return null;
                }
                Driver d = Bm.DriverOf(player);
                if (d == null || d.Kind != ControlKind.Form)
                {
                    return null;
                }
                bool animalSeat = mountable is RidableHorse || mountable.GetComponentInParent<BaseNPC2>() != null || mountable.GetParentEntity() is BaseNPC2;
                if (animalSeat ? Bm.Cfg.Form.BlockRiding : Bm.Cfg.Form.BlockDriving)
                {
                    Bm.TellRaw(player, animalSeat ? "An animal cannot ride other animals." : "An animal cannot drive vehicles.");
                    return false;
                }
                return null;
            }

            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                BaseNPC2 animal = entity as BaseNPC2;
                Driver d = animal != null ? Bm.DriverOfAnimal(animal) : null;
                if (d != null)
                {
                    d.AnimalDied();
                }
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                Driver d = Bm.DriverOfAnimal(entity);
                if (d != null)
                {
                    d.AnimalDied();
                }
                Bm.ForgetPark(entity);
            }

            public override void OnPlayerDisconnected(BasePlayer player, string reason)
            {
                Driver d = Bm.DriverOf(player);
                if (d != null)
                {
                    d.PlayerLeft();
                }
            }

            public override void OnPlayerDeath(BasePlayer player, HitInfo info)
            {
                if (Bm.DriverOf(player) != null)
                {
                    Bm.StopControl(player, "player died");
                }
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Control: " + Bm.Drivers.Count + " active drivers");
            }
        }

        // ---------------------------------------------------------------- the controller

        internal sealed class Driver : FacepunchBehaviour
        {
            private const float TurnAboutTime = 1.0f;
            private static readonly FieldInfo LookDirectionField = typeof(NPCNetworking).GetField("<LookDirection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly string[] AttackWords = { "attack", "headbutt", "kick", "charge", "flop", "snap", "peck", "bite", "swipe" };
            private static readonly string[] IdleWords = { "idle", "look", "breathe", "shake", "sniff", "ear", "tail", "chew", "scratch", "stretch", "lick", "moo", "bleat", "howl", "bark", "growl", "roar", "call", "search", "sit" };
            private static readonly string[] NeverWords = { "death", "dead", "die", "lie", "lay", "sleep", "knock", "fall", "mount", "getup", "get_up", "standup", "stand_up", "graze", "drink", "turn", "stop", "run", "walk", "canter", "trot" };

            public BasePlayer Player;
            public BaseCombatEntity Body;     // the controlled entity: a Gen2 animal or a horse
            public BaseNPC2 Animal;           // the Gen2 animal (null for a horse form)
            public RidableHorse Horse;        // the horse (null for the Gen2 animals)
            public SpeciesDef Species;
            public ControlKind Kind;
            public ViewKind View;
            public BaseMountable Seat;
            public bool SwappingSeats;
            public bool Spectating;
            public bool OwnsAnimal;         // the animal was made for this form and vanishes in smoke when the form ends
            public int Gait = 1;
            public bool Alive => !released;
            public bool Standing => Mathf.Abs(currentSpeed) < 0.6f;
            public float Speed => currentSpeed;
            public float Heading => moveYaw;

            private Beastmaster bm;
            private bool released;
            private RustNavMeshAgent agent;
            private NPCNetworking npcNet;
            private float moveYaw, yawVel, yawOffset, currentSpeed;
            private bool fwd, back, left, right, braking;
            private float turnAboutLeft, turnDir;
            private int attackIndex;
            private float nextAttack, nextJump;
            private bool jumping;
            private Vector3 jumpFrom, jumpTo;
            private float jumpTime, jumpDuration, jumpHeight;
            private Vector3 seatOffset;
            private float seatLift;
            private float waterFactor, nextWaterCheck;
            private bool deepWater, swimSet;
            private float nextLook;
            private Vector3 lookDir;
            private float idleSince, nextIdle;
            private float nextHealthSync, lastLog;
            private float fallenUntil, slopeCheckAt;
            // camera
            private float lastMouseTime;
            private bool tpFlagSet;
            // gestures: a held pose (lying, sitting)
            public string PoseName;
            private float poseUntil;
            private int gestureIndex;
            // handled by others
            public float FedUntil;                                       // animal forms: carries riders until then
            public BasePlayer Leader;                                    // the plugin's leash: who leads this form
            public readonly List<BaseMountable> Guests = new List<BaseMountable>();

            public void Setup(Beastmaster plugin, BasePlayer player, BaseCombatEntity body, ControlKind kind)
            {
                bm = plugin;
                Player = player;
                Body = body;
                Animal = body as BaseNPC2;
                Horse = body as RidableHorse;
                Kind = kind;
                Species = SpeciesOf(body);
                agent = Animal != null ? Animal.GetComponent<RustNavMeshAgent>() : null;
                npcNet = Animal != null ? Animal.GetComponent<NPCNetworking>() : null;
                yawOffset = plugin.YawOffsetOf(Species);
                moveYaw = body.transform.eulerAngles.y - yawOffset;
                lookDir = body.transform.forward;
                lastMouseTime = Time.time;
                PlayerSettings settings = plugin.SettingsOf(player.userID);
                string view = kind == ControlKind.Ride
                    ? plugin.Cfg.Camera.DefaultRideView
                    : string.IsNullOrEmpty(settings.View) ? plugin.Cfg.Camera.DefaultView : settings.View;
                View = kind == ControlKind.Form ? ViewKind.Third : (view == "first" || !plugin.Cfg.Features.ThirdPerson ? ViewKind.First : ViewKind.Third);
            }

            public bool Begin()
            {
                if (Kind == ControlKind.Form)
                {
                    bm.FormSmoke(Player.transform.position, Player);          // the human vanishes in the game's effect, at once
                }
                if (agent == null && Horse == null)
                {
                    bm.TellRaw(Player, "This creature cannot be controlled.");
                    return false;
                }
                if (Animal != null)
                {
                    bm.Park(Animal, "control");
                    agent.Pause(this);
                }
                else if (bm.Stable != null)
                {
                    bm.Stable.TakeControl(Horse);
                }
                if (!ApplyView())
                {
                    bm.TellRaw(Player, "Could not seat you on this animal. Admins: try /bm seat to pick another seat model.");
                    Release("view failed");
                    return false;
                }
                if (Kind == ControlKind.Form)
                {
                    if (bm.Cfg.Form.HideHuman)
                    {
                        Player.limitNetworking = true;
                    }
                    if (bm.Cfg.Form.Untargetable)
                    {
                        Rust.Ai.SimpleAIMemory.PlayerIgnoreList.Add(Player);
                    }
                    SyncHealth(true);
                    bm.OnFormStarted(this);
                }
                bm.DrawKeyHints(Player);
                bm.DrawInventoryCard(this);
                if (Kind == ControlKind.Form)
                {
                    bm.HudSwitch(Player, false);
                }
                return true;
            }

            // ------------------------------------------------------------ views

            // The view of the current kind: riding = straddle seat on the animal's back; animal form first person = hidden seat at the
            // animal's head; animal form third person = the game's spectator camera on the animal (the body is hidden by the game).
            private bool ApplyView()
            {
                if (Kind == ControlKind.Form && View == ViewKind.Third)
                {
                    BeginSpectate();
                    return true;
                }
                return MountSeat();
            }

            // A = spectator camera around the animal, B = plus the game's third-person flag, C = camera at the head,
            // D = first person from the animal's eyes (spectator camera at the head with the game's eyes view flag).
            private string CurrentPreset()
            {
                return View == ViewKind.First ? "D" : bm.PresetOf(Player);
            }

            private static void SetViewFlag(BasePlayer player, BasePlayer.PlayerFlags flag, bool on)
            {
                if (player.HasPlayerFlag(flag) == on)
                {
                    return;
                }
                player.SetPlayerFlag(flag, on);
                player.SendNetworkUpdateImmediate();
            }

            private void ClearView()
            {
                if (Spectating)
                {
                    EndSpectate();
                }
                if (Seat != null && !Seat.IsDestroyed)
                {
                    SwappingSeats = true;
                    try
                    {
                        Seat.DismountPlayer(Player, true);
                    }
                    finally
                    {
                        SwappingSeats = false;
                    }
                    bm.OwnSeats.Remove(Seat);
                    Seat.Kill();
                }
                Seat = null;
                if (tpFlagSet || Player.HasPlayerFlag(BasePlayer.PlayerFlags.ThirdPersonViewmode))
                {
                    SetThirdPersonFlag(Player, false);
                    tpFlagSet = false;
                }
                SetViewFlag(Player, BasePlayer.PlayerFlags.EyesViewmode, false);
            }

            public void CycleView()
            {
                if (Kind == ControlKind.Form)
                {
                    return;                                 // an animal form is played in third person only
                }
                if (Kind == ControlKind.Ride)
                {
                    tpFlagSet = !Player.HasPlayerFlag(BasePlayer.PlayerFlags.ThirdPersonViewmode);
                    SetThirdPersonFlag(Player, tpFlagSet);
                    bm.Tell(Player, tpFlagSet ? "ViewThird" : "ViewFirst", bm.PresetOf(Player));
                    return;
                }
                SetView(View == ViewKind.Third ? ViewKind.First : ViewKind.Third);
            }

            public void SetView(ViewKind view)
            {
                if (view == View && (Seat != null || Spectating))
                {
                    return;
                }
                if (view == ViewKind.Third && !bm.Cfg.Features.ThirdPerson)
                {
                    return;
                }
                if (Spectating && view == ViewKind.Third)
                {
                    View = view;
                    ApplyPreset(true);
                }
                else
                {
                    ClearView();
                    View = view;
                    if (!ApplyView())
                    {
                        Release("view change failed");
                        return;
                    }
                }
                if (Kind != ControlKind.Form)
                {
                    bm.SettingsOf(Player.userID).View = view == ViewKind.Third ? "third" : "first";
                }
                else
                {
                    bm.HudSwitch(Player, view != ViewKind.Third);       // the inventory view shows the game's HUD, the animal view hides it
                }
                bm.DrawKeyHints(Player);
                bm.Tell(Player, view == ViewKind.Third ? "ViewThird" : "ViewFirst", bm.PresetOf(Player));
            }

            public void SetPreset(string preset)
            {
                if (preset == "D")
                {
                    View = ViewKind.First;
                }
                else
                {
                    bm.SettingsOf(Player.userID).Preset = preset;
                    View = ViewKind.Third;
                }
                if (Spectating)
                {
                    ApplyPreset(true);
                    bm.Tell(Player, View == ViewKind.First ? "ViewFirst" : "ViewThird", preset);
                }
            }

            private bool MountSeat()
            {
                string prefab = Kind == ControlKind.Ride ? bm.Cfg.Riding.SeatPrefab : bm.Cfg.Camera.FirstPersonSeat;
                BaseEntity seatEntity = GameManager.server.CreateEntity(prefab, Body.transform.position, Body.transform.rotation);
                BaseMountable seat = seatEntity as BaseMountable;
                if (seat == null)
                {
                    if (seatEntity != null)
                    {
                        seatEntity.Kill();
                    }
                    bm.Log(0, "Seat prefab is not a mountable: " + prefab);
                    return false;
                }
                Vector3 eyeLocal = seat.transform.InverseTransformPoint(seat.GetEyeOverride().position);
                Vector3 anchorLocal = seat.mountAnchor != null ? seat.transform.InverseTransformPoint(seat.mountAnchor.position) : Vector3.zero;
                seat.SetParent(Body, false, false);
                float animalScale = ModelScale(Body);
                if (animalScale > 1.001f)
                {
                    // a bigger animal (a boss): the seat is placed by the animal's scale - the height of the back - but keeps normal size
                    // itself, so that the rider is not enlarged with it
                    seat.networkEntityScale = true;
                    seat.transform.localScale = Vector3.one / animalScale;
                }
                Quaternion rotation = Quaternion.Euler(0f, -yawOffset, 0f);
                seat.transform.localRotation = rotation;
                seatOffset = SeatOffset(rotation * eyeLocal, rotation * anchorLocal) + Vector3.up * bm.SeatLiftOf(Species);
                seatLift = RequiredLift();
                seat.transform.localPosition = seatOffset + Vector3.up * seatLift;
                bm.OwnSeats.Add(seat);
                seat.Spawn();
                Rigidbody body = seat.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.isKinematic = true;
                }
                Seat = seat;
                seat.MountPlayer(Player);
                if (Player.GetMounted() != seat)
                {
                    bm.Log(1, "MountPlayer did not mount " + Player.displayName + " on " + prefab);
                    bm.OwnSeats.Remove(seat);
                    seat.Kill();
                    Seat = null;
                    return false;
                }
                if (Kind == ControlKind.Ride)
                {
                    tpFlagSet = View == ViewKind.Third;
                    SetThirdPersonFlag(Player, tpFlagSet);
                }
                ForceView(0f, bm.Cfg.Camera.DefaultPitch);
                return true;
            }

            private Vector3 SeatOffset(Vector3 eye, Vector3 anchor)
            {
                Bounds b = Body.bounds;
                if (Kind == ControlKind.Ride)
                {
                    // the rider's root (the seat's mount anchor) sits on the middle of the back
                    return BackPoint(Animal) - anchor;
                }
                // first person: the eye just in front of the head, in the direction of travel
                Vector3 dir = Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward;
                float reach = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
                Vector3 head = new Vector3(b.center.x, Mathf.Max(b.max.y * 0.9f, 0.4f), b.center.z) + dir * (reach + 0.05f);
                return head - eye;
            }

            // The game's spectator camera on the animal. The spectating state hides the human completely - even for the player himself -
            // and keeps AI and turrets away; the body is parented to the animal at the camera's pivot so the camera follows it.
            private void BeginSpectate()
            {
                Player.EnsureDismounted();
                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, true);
                Player.gameObject.SetLayerRecursive(10);
                BaseEntity.Query.Server.RemovePlayer(Player);
                Player.SetParent(Body, false, true);
                InstallSpectatorStrategy();
                Spectating = true;
                ApplyPreset(true);
                SendSpectateTarget(Player, Body.net.ID);
            }

            // The game only shows other players to a spectator that has a spectate strategy saying whose surroundings to show (BasePlayer.GatherPairs
            // skips spectators without one, so everybody else stayed invisible). The human watches its own surroundings: the animal's.
            private void InstallSpectatorStrategy()
            {
                try
                {
                    var strategy = new BasePlayer.SpectatorSubStrategy();
                    strategy.SpectatedPlayer = Player;
                    Player.net.SubStrategy = strategy;
                    Player.net.InvalidateSubscriptions(2);
                }
                catch (Exception ex)
                {
                    bm.Log(1, "Spectator strategy could not be set: " + ex.Message);
                }
            }

            private void RemoveSpectatorStrategy()
            {
                try
                {
                    if (Player.net != null && Player.net.SubStrategy is BasePlayer.SpectatorSubStrategy)
                    {
                        Player.net.SubStrategy = Network.Server.DefaultSubscriberStrategy;
                        Player.net.InvalidateSubscriptions(2);
                    }
                }
                catch (Exception ex)
                {
                    bm.Log(1, "Spectator strategy could not be removed: " + ex.Message);
                }
            }

            private void ApplyPreset(bool snapView)
            {
                string preset = CurrentPreset();
                bool flag = preset == "B";
                if (flag != tpFlagSet || Player.HasPlayerFlag(BasePlayer.PlayerFlags.ThirdPersonViewmode) != flag)
                {
                    SetThirdPersonFlag(Player, flag);
                    tpFlagSet = flag;
                }
                SetViewFlag(Player, BasePlayer.PlayerFlags.EyesViewmode, preset == "D");
                Bounds b = Body.bounds;
                float scale = bm.PivotScaleOf(Species);
                float lift = bm.CamLiftOf(Player, Species);
                Vector3 pivot;
                if (preset == "C" || preset == "D")
                {
                    // the eyes: at the height of the head, just behind the nose - looking down shows the front legs
                    Vector3 dir = Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward;
                    float reach = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
                    pivot = new Vector3(b.center.x, Mathf.Max(b.max.y * bm.Cfg.Camera.EyeHeight + lift, 0.3f), b.center.z) + dir * (reach * bm.Cfg.Camera.EyeForward);
                }
                else
                {
                    pivot = new Vector3(b.center.x, Mathf.Max(b.max.y * bm.Cfg.Camera.PivotHeight * scale + lift, 0.5f), b.center.z);
                    pivot -= Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward * bm.Cfg.Camera.ThirdBack;
                }
                Player.transform.localPosition = pivot - PlayerEyes.EyeOffset;
                Player.transform.localRotation = Quaternion.Euler(0f, -yawOffset, 0f);
                Player.SendNetworkUpdateImmediate();
                if (snapView)
                {
                    ForceView(0f, bm.Cfg.Camera.DefaultPitch);
                }
            }

            private void EndSpectate()
            {
                SetViewFlag(Player, BasePlayer.PlayerFlags.EyesViewmode, false);
                Vector3 drop = Body != null && !Body.IsDestroyed
                    ? Body.transform.position + Body.transform.right * (Body.bounds.extents.x + 1f)
                    : Player.transform.position;
                drop.y = TerrainMeta.HeightMap.GetHeight(drop) + 0.3f;
                Player.SetParent(null, true, true);
                SendSpectateTarget(Player, default(NetworkableId));
                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, false);
                RemoveSpectatorStrategy();
                Player.gameObject.SetLayerRecursive(17);
                BaseEntity.Query.Server.RemovePlayer(Player);
                BaseEntity.Query.Server.AddPlayer(Player);
                Player.Teleport(drop);
                Spectating = false;
            }

            // RpcTarget pulls in ReadOnlySpan, which plugin compilers do not always resolve - the RPC goes through reflection.
            private static readonly MethodInfo RpcTargetPlayer = typeof(BaseEntity).Assembly.GetType("RpcTarget")?.GetMethod("Player", new[] { typeof(string), typeof(BasePlayer) });
            private static readonly MethodInfo ClientRpcNetId = FindClientRpc(typeof(NetworkableId));
            private static readonly MethodInfo ClientRpcVector3 = FindClientRpc(typeof(Vector3));

            private static MethodInfo FindClientRpc(Type argType)
            {
                return typeof(BaseEntity).GetMethods().FirstOrDefault(m => m.Name == "ClientRPC" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == argType);
            }

            private static void SendPlayerRpc(BasePlayer player, MethodInfo rpc, string name, object arg)
            {
                if (RpcTargetPlayer == null || rpc == null || player == null || !player.IsConnected)
                {
                    return;
                }
                object target = RpcTargetPlayer.Invoke(null, new object[] { name, player });
                rpc.Invoke(player, new object[] { target, arg });
            }

            private static void SendSpectateTarget(BasePlayer player, NetworkableId id)
            {
                SendPlayerRpc(player, ClientRpcNetId, "SpectateTarget", id);
            }

            // Points the player's view along a world direction (yaw relative to the travel direction, pitch positive = down).
            private void ForceView(float relYaw, float pitch)
            {
                Vector3 dir = Quaternion.Euler(pitch, moveYaw + relYaw, 0f) * Vector3.forward;
                SendPlayerRpc(Player, ClientRpcVector3, "ForceViewAnglesTo", dir);
            }

            // the camera follows the animal's own turning, which must not count as mouse movement
            private bool AnimalTurning()
            {
                return Mathf.Abs(yawVel) > 5f;
            }

            // The animal's head: where interactions (doors, containers) start in third person.
            public Vector3 HeadPosition()
            {
                Bounds b = Body.bounds;
                Vector3 dir = Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward;
                float reach = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
                Vector3 local = new Vector3(b.center.x, Mathf.Max(b.max.y * 0.7f, 0.3f), b.center.z) + dir * reach;
                return Body.transform.TransformPoint(local);
            }

            // ------------------------------------------------------------ input

            public void ReadInput(InputState input)
            {
                fwd = input.IsDown(BUTTON.FORWARD);
                back = input.IsDown(BUTTON.BACKWARD);
                left = input.IsDown(BUTTON.LEFT);
                right = input.IsDown(BUTTON.RIGHT);
                jumpDown = input.IsDown(BUTTON.JUMP);
                if (fwd || back || left || right)
                {
                    InterruptClip();                       // walking or running takes over from the idle animation
                }
                if (PoseName != null && (fwd || back || left || right || input.WasJustPressed(BUTTON.JUMP) || input.WasJustPressed(BUTTON.FIRE_THIRD)))
                {
                    ClearPose();
                }
                if (input.WasJustPressed(BUTTON.SPRINT))
                {
                    braking = false;
                    SetGait(Mathf.Max(Gait, 1) + 1);
                }
                if (input.WasJustPressed(BUTTON.DUCK))
                {
                    Brake();
                }
                if (input.WasJustPressed(BUTTON.BACKWARD) && currentSpeed > GaitSpeed(3) * 0.9f)
                {
                    TurnAbout();
                }
                if (input.WasJustPressed(BUTTON.JUMP))
                {
                    Jump();
                }
                if (input.WasJustPressed(BUTTON.FIRE_THIRD))
                {
                    Attack();
                }
                if (input.WasJustPressed(BUTTON.RELOAD) && !input.IsDown(BUTTON.DUCK))
                {
                    if (Kind == ControlKind.Form)
                    {
                        if (Body is LivestockAnimal || Horse != null)
                        {
                            bm.ReskinForm(this);            // R: another skin of the animal (only animals that have skins)
                        }
                    }
                    else
                    {
                        CycleView();
                    }
                }
                if (Kind == ControlKind.Form && Spectating && input.WasJustPressed(BUTTON.USE))
                {
                    bm.InteractFrom(this);
                }
            }

            public void SetGait(int gait)
            {
                Gait = Mathf.Clamp(gait, 0, 5);
            }

            // Ctrl: from a run down to a walk with a hard stop (the stop-and-turn clip when turning), from a walk to a standstill.
            private void Brake()
            {
                float walk = GaitSpeed(1);
                if (currentSpeed > walk * 1.3f)
                {
                    braking = true;
                    Gait = 1;
                    string side = right ? "right" : left ? "left" : null;
                    ClipRef stop = side == null || Animal == null ? null : bm.Clips(Animal).FirstOrDefault(c => Has(c, "stop") && Has(c, side));
                    if (stop != null)
                    {
                        PlayClip(Animal, stop);
                    }
                }
                else
                {
                    Gait = 0;
                }
            }

            // S while running: the 180 degree turn the game's livestock does at a canter (the bull's sliding turn).
            private void TurnAbout()
            {
                if (turnAboutLeft > 0f)
                {
                    return;
                }
                turnDir = right ? 1f : -1f;
                string side = turnDir > 0f ? "right" : "left";
                ClipRef clip = RunningTurnClip(side, true);
                if (clip != null)
                {
                    PlayClip(Animal, clip);
                }
                turnAboutLeft = TurnAboutTime;
                sharpBoostUntil = Time.time + TurnAboutTime;
            }

            // The turn the animal makes while it runs (the bull's slide round, the wolf's skid): the clips of the canter/run turn of that side.
            private ClipRef RunningTurnClip(string side, bool preferHalfTurn)
            {
                if (Animal == null)
                {
                    return null;
                }
                List<ClipRef> clips = bm.Clips(Animal);
                Func<ClipRef, bool> running = c => c.State.IndexOf("turnCantering", StringComparison.OrdinalIgnoreCase) >= 0
                    || (c.Name.IndexOf("turn", StringComparison.OrdinalIgnoreCase) >= 0 && (c.Name.IndexOf("canter", StringComparison.OrdinalIgnoreCase) >= 0 || c.Name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0 || c.Name.IndexOf("sprint", StringComparison.OrdinalIgnoreCase) >= 0 || c.Name.IndexOf("gallop", StringComparison.OrdinalIgnoreCase) >= 0)
                        && c.Name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) < 0 && c.Name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) < 0);
                Func<ClipRef, bool> onSide = c => c.Name.IndexOf(side, StringComparison.OrdinalIgnoreCase) >= 0;
                Func<ClipRef, bool> half = c => c.Name.IndexOf("180", StringComparison.OrdinalIgnoreCase) >= 0;
                return clips.FirstOrDefault(c => running(c) && onSide(c) && half(c) == preferHalfTurn)
                    ?? clips.FirstOrDefault(c => running(c) && onSide(c))
                    ?? (preferHalfTurn ? clips.FirstOrDefault(c => running(c) && half(c)) : null);
            }

            // Holding A or D at a run for a moment: the animal throws itself round with the sharp-turn clip of its species and turns faster for that moment.
            private float sharpTime, nextSharp, sharpBoostUntil;

            private void SharpTurnTick(float dt)
            {
                bool turning = left != right;
                if (Animal == null || !turning || currentSpeed < GaitSpeed(3) * 0.8f || jumping || turnAboutLeft > 0f)
                {
                    sharpTime = Mathf.Max(0f, sharpTime - dt * 2f);
                    return;
                }
                sharpTime += dt;
                if (sharpTime < 0.45f || Time.time < nextSharp)
                {
                    return;
                }
                sharpTime = 0f;
                ClipRef clip = RunningTurnClip(right ? "right" : "left", false);
                if (clip == null)
                {
                    nextSharp = Time.time + 1.5f;
                    return;
                }
                PlayClip(Animal, clip);
                nextSharp = Time.time + 2.4f;
                sharpBoostUntil = Time.time + 0.9f;
            }

            // ------------------------------------------------------------ speeds

            private int EffectiveGait()
            {
                int g = deepWater ? Mathf.Min(Gait, 1) : Gait;
                return Mathf.Min(g, bm.StatsGaitCap(this));
            }

            public float GaitSpeed(int gait)
            {
                float scale = bm.SpeedScaleOf(Species);
                float speed;
                if (bm.Cfg.Movement.SpeedMode == "table" || agent == null)
                {
                    switch (gait)
                    {
                        case 0: speed = 0f; break;
                        case 1: speed = Species.Walk; break;
                        case 2: speed = Mathf.Lerp(Species.Walk, Species.Sprint, 0.33f); break;
                        case 3: speed = Mathf.Lerp(Species.Walk, Species.Sprint, 0.66f); break;
                        case 4: speed = Species.Sprint; break;
                        default: speed = Species.Top; break;
                    }
                }
                else
                {
                    switch (gait)
                    {
                        case 0: speed = 0f; break;
                        case 1: speed = agent.walkSpeed; break;
                        case 2: speed = agent.jogSpeed; break;
                        case 3: speed = agent.runSpeed; break;
                        case 4: speed = agent.sprintSpeed; break;
                        default: speed = agent.fullSprintSpeed; break;
                    }
                    speed += (Animal != null ? bm.GearSpeedBonus(Animal) : 0f) * (gait / 5f);
                }
                return Mathf.Max(0f, speed * scale * bm.ClimateSpeedFactor(Body) * bm.StatsSpeedFactor(Body));
            }

            // Same measure as the horse: the share of the body height under the water surface.
            private bool IgnoresWater => Species != null && (Species.Id == "bear" || Species.Id == "crocodile" || Species.DrownProof);

            private float WaterMultiplier()
            {
                if (!bm.Cfg.Movement.WaterSlowdown || IgnoresWater || waterFactor <= 0.05f)
                {
                    return Species != null && Species.Id == "crocodile" && waterFactor > 0.3f ? 1.8f : 1f;
                }
                return Mathf.Lerp(1f, bm.Cfg.Movement.WaterMinSpeed, Mathf.InverseLerp(0.1f, 0.65f, waterFactor));
            }

            private void UpdateWater()
            {
                if (Time.time < nextWaterCheck)
                {
                    return;
                }
                nextWaterCheck = Time.time + 0.25f;
                waterFactor = WaterLevel.Factor(Body.WorldSpaceBounds().ToBounds(), true, true, Body);
                deepWater = bm.Cfg.Movement.WaterSlowdown && waterFactor > bm.Cfg.Movement.WaterWalkOnly && !IgnoresWater;
                if (agent != null && agent.canSwim && Species.Id != "frog")
                {
                    bool swim = waterFactor > 0.65f;
                    if (swim != agent.IsSwimming)
                    {
                        agent.IsSwimming = swim;
                        swimSet = true;
                    }
                }
            }

            // A body that is completely under water starts to drown after a while (frog, crab and crocodile never do). The big animals
            // swim on the surface with the slow walk; only the whole body under the surface counts (R9.37).
            private float drownTime, nextDrownHit;

            private void DrownTick(float dt)
            {
                if (Species == null || Species.DrownProof || !bm.Cfg.Movement.Drowning)
                {
                    drownTime = 0f;
                    return;
                }
                if (waterFactor >= 0.97f)
                {
                    drownTime += dt;
                    float limit = Species.Critter ? bm.Cfg.Movement.DrownSecondsSmall : bm.Cfg.Movement.DrownSeconds;
                    if (drownTime > limit && Time.time >= nextDrownHit)
                    {
                        nextDrownHit = Time.time + 1f;
                        Body.Hurt(Mathf.Max(1f, Body.MaxHealth() * bm.Cfg.Movement.DrownDamagePercent / 100f), Rust.DamageType.Drowned, null, false);
                    }
                }
                else
                {
                    drownTime = Mathf.Max(0f, drownTime - dt * 2f);
                }
            }

            // ------------------------------------------------------------ health

            // Animal form: the animal's health is the player's health.
            private void SyncHealth(bool force)
            {
                if (Kind != ControlKind.Form || !bm.Cfg.Form.LinkHealth || (!force && Time.time < nextHealthSync))
                {
                    return;
                }
                nextHealthSync = Time.time + 0.25f;
                float hp = Mathf.Clamp(Body.health / Mathf.Max(1f, Body.MaxHealth()) * Player.MaxHealth(), 1f, Player.MaxHealth());
                if (force || Mathf.Abs(Player.health - hp) > 0.5f)
                {
                    Player.health = hp;
                    Player.SendNetworkUpdate();
                }
            }

            public void AnimalDied()
            {
                if (released)
                {
                    return;
                }
                BasePlayer player = Player;
                bool link = Kind == ControlKind.Form && bm.Cfg.Form.LinkHealth;
                bm.Drivers.Remove(player.userID);
                Release("animal died");
                if (link && player != null && !player.IsDead())
                {
                    bm.Tell(player, "Died");
                    player.Die();
                }
            }

            public void PlayerLeft()
            {
                if (released)
                {
                    return;
                }
                if (Kind == ControlKind.Form && bm.OnFormLogout(this))
                {
                    return;
                }
                bm.Drivers.Remove(Player.userID);
                Release("player left");
            }

            // ------------------------------------------------------------ combat and animation

            public void Attack()
            {
                if (!bm.Cfg.Features.Combat || Time.time < nextAttack)
                {
                    return;
                }
                if (Horse != null)
                {
                    HorseAttack();
                    return;
                }
                InterruptClip();
                float camYaw = Yaw(Player.eyes.rotation * Vector3.forward);
                bool rear = Mathf.Abs(Mathf.DeltaAngle(moveYaw, camYaw)) > bm.Cfg.Combat.RearAngle;
                List<ClipRef> all = bm.Clips(Animal);
                // the camera is a circle round the animal: the front half plays the attacks to the front, the rear half the kicks and bites behind
                List<ClipRef> clips = rear ? all.Where(c => (Has(c, "behind") || Has(c, "kick")) && !Has(c, "death")).ToList() : new List<ClipRef>();
                if (!rear)
                {
                    clips = all.Where(c => AttackWords.Any(w => Has(c, w)) && !Has(c, "death") && !Has(c, "behind") && !Has(c, "kick")).ToList();
                }
                if (clips.Count == 0)
                {
                    clips = all.Where(c => AttackWords.Any(w => Has(c, w)) && !Has(c, "death")).ToList();
                }
                if (clips.Count == 0)
                {
                    // critters without an attack clip use a call and a small nip (assumption A12)
                    ClipRef call = all.FirstOrDefault(IsIdleClip);
                    if (call != null)
                    {
                        PlayClip(Animal, call);
                    }
                    bm.CritterNip(this);
                    nextAttack = Time.time + bm.Cfg.Combat.Cooldown;
                    return;
                }
                ClipRef clip = clips[attackIndex++ % clips.Count];
                AttackParams p = ReadAttack(Animal, clip, rear);
                float aimed = bm.AimAtTarget(this, p, rear);
                if (Mathf.Abs(Mathf.DeltaAngle(moveYaw, aimed)) > 1f)
                {
                    moveYaw = aimed;
                    yawVel = 0f;
                    Body.transform.rotation = Quaternion.Euler(0f, moveYaw + yawOffset, 0f);
                }
                PlayClip(Animal, clip);
                bm.StatsSpend(this, 4f);
                nextAttack = Time.time + Mathf.Max(bm.Cfg.Combat.Cooldown, p.Delay + 0.4f);
                float heading = moveYaw;
                Invoke(() =>
                {
                    if (!released && Valid(Body) && !Body.IsDead())
                    {
                        string summary = bm.ResolveHits(Animal, Player, heading, p, rear);
                        if (!string.IsNullOrEmpty(summary))
                        {
                            bm.Tell(Player, "HitSummary", summary);
                        }
                    }
                }, p.Delay);
            }

            public void OnGesture(string gesture)
            {
                bm.PerformGesture(this, gesture);
            }

            public int NextGestureIndex()
            {
                return gestureIndex++;
            }

            // An idle animation or a gesture of the animal: kept so that the player's own action can interrupt it.
            private object idleState;
            private float idleStarted;

            public void PlayIdleClip(ClipRef clip)
            {
                if (Animal == null)
                {
                    return;
                }
                InterruptClip();
                idleState = PlayClipState(Animal, clip);
                idleStarted = Time.time;
            }

            public void InterruptClip()
            {
                if (idleState != null)
                {
                    if (Time.time - idleStarted < 8f)
                    {
                        StopClipState(Animal, ref idleState);
                    }
                    idleState = null;
                }
            }

            // The camera is placed again (after a change of its height).
            public void RefreshCamera()
            {
                if (Spectating && !released)
                {
                    ApplyPreset(false);
                }
            }

            // Lie down or sit: a livestock animal rests on its flag and keeps the pose until the player moves, the others play the clip of their species.
            public void StartPose(string pose)
            {
                if (Animal == null || jumping || released)
                {
                    return;
                }
                ClearPose();
                currentSpeed = 0f;
                Gait = 1;
                LivestockAnimal livestock = Animal as LivestockAnimal;
                if (livestock != null)
                {
                    livestock.SetResting(true);
                    PoseName = pose;
                    poseUntil = float.MaxValue;
                    return;
                }
                string[] words = pose == "sit" ? new[] { "sit", "rest", "lie" } : new[] { "lie", "lay", "rest", "sleep", "sit" };
                ClipRef clip = bm.FindClip(Animal, words);
                if (clip != null && PlayClip(Animal, clip))
                {
                    PoseName = pose;
                    poseUntil = Time.time + 3.2f;
                }
            }

            public void ClearPose()
            {
                if (PoseName == null)
                {
                    return;
                }
                PoseName = null;
                LivestockAnimal livestock = Animal as LivestockAnimal;
                if (livestock != null)
                {
                    livestock.SetResting(false);
                    livestock.SetSleeping(false);
                    fallenUntil = Mathf.Max(fallenUntil, Time.time + 1.4f);        // time to get up
                }
            }

            // ------------------------------------------------------------ riders and the leash

            public void PruneGuests()
            {
                for (int i = Guests.Count - 1; i >= 0; i--)
                {
                    BaseMountable seat = Guests[i];
                    if (seat == null || seat.IsDestroyed || !seat.IsMounted())
                    {
                        DropGuestSeat(seat);
                    }
                }
            }

            public void DropGuestSeat(BaseMountable seat)
            {
                Guests.Remove(seat);
                if (seat == null)
                {
                    return;
                }
                bm.GuestSeats.Remove(seat);
                bm.OwnSeats.Remove(seat);
                if (!seat.IsDestroyed)
                {
                    seat.Kill();
                }
            }

            public bool AddGuest(BasePlayer guest)
            {
                if (Animal == null)
                {
                    return false;
                }
                string prefab = Guests.Count == 0 ? bm.Cfg.Riding.SeatPrefab : bm.Cfg.Riding.RearSeatPrefab;
                BaseEntity entity = GameManager.server.CreateEntity(prefab, Body.transform.position, Body.transform.rotation);
                BaseMountable seat = entity as BaseMountable;
                if (seat == null)
                {
                    if (entity != null)
                    {
                        entity.Kill();
                    }
                    return false;
                }
                Quaternion rotation = Quaternion.Euler(0f, -yawOffset, 0f);
                Vector3 anchor = seat.mountAnchor != null ? rotation * seat.transform.InverseTransformPoint(seat.mountAnchor.position) : Vector3.zero;
                seat.SetParent(Body, false, false);
                float k = ModelScale(Body);
                if (k > 1.001f)
                {
                    seat.networkEntityScale = true;
                    seat.transform.localScale = Vector3.one / k;
                }
                seat.transform.localRotation = rotation;
                Vector3 place = BackPoint(Animal) - anchor + Vector3.up * bm.SeatLiftOf(Species);
                if (Guests.Count > 0)
                {
                    place += rotation * Vector3.back * Mathf.Max(0.55f, Body.bounds.extents.z * 0.5f);
                }
                seat.transform.localPosition = place;
                bm.OwnSeats.Add(seat);
                seat.Spawn();
                Rigidbody rb = seat.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                }
                Guests.Add(seat);
                bm.GuestSeats[seat] = this;
                seat.MountPlayer(guest);
                if (guest.GetMounted() != seat)
                {
                    DropGuestSeat(seat);
                    return false;
                }
                return true;
            }

            // The animal is gone or the player leaves the form: riders are put down, the leash is untied.
            public void EndGuests()
            {
                Leader = null;
                foreach (BaseMountable seat in Guests.ToList())
                {
                    if (seat != null && !seat.IsDestroyed)
                    {
                        seat.DismountAllPlayers();
                    }
                    DropGuestSeat(seat);
                }
                if (Horse != null && !Horse.IsDestroyed)
                {
                    Horse.DismountAllPlayers();
                }
            }

            // The idle clips of the animal (calls, head shakes, looking around) - what a gesture may play.
            public List<ClipRef> IdleClips()
            {
                return Animal == null ? new List<ClipRef>() : bm.Clips(Animal).Where(IsIdleClip).ToList();
            }

            private static bool IsIdleClip(ClipRef c)
            {
                return IdleWords.Any(w => Has(c, w)) && !NeverWords.Any(w => Has(c, w)) && !AttackWords.Any(w => Has(c, w));
            }

            // Standing still under control: the animal keeps its own idle life (breathing, looking, head shake, calls).
            private void UpdateIdle()
            {
                if (Animal == null)
                {
                    return;
                }
                bool still = Mathf.Abs(currentSpeed) < 0.05f && !fwd && !back && !left && !right && turnAboutLeft <= 0f && !jumping;
                if (!still)
                {
                    idleSince = 0f;
                    return;
                }
                if (idleSince <= 0f)
                {
                    idleSince = Time.time;
                    nextIdle = Time.time + UnityEngine.Random.Range(5f, 9f);
                    return;
                }
                if (Time.time < nextIdle)
                {
                    return;
                }
                nextIdle = Time.time + UnityEngine.Random.Range(8f, 14f);
                List<ClipRef> clips = bm.Clips(Animal).Where(IsIdleClip).ToList();
                if (clips.Count > 0)
                {
                    PlayIdleClip(clips[UnityEngine.Random.Range(0, clips.Count)]);
                }
            }

            // ------------------------------------------------------------ jumping and falling

            private float nextWallCrash;
            private bool jumpDown;
            private float jumpHold;
            private bool platformLanding;
            private bool offMesh;
            private float nextMeshCheck;
            private bool jumpCollision;
            private Vector3 jumpHitPoint;
            private string jumpHitSurface = "generic";

            public void Jump()
            {
                if (jumping || Time.time < nextJump || Time.time < fallenUntil)
                {
                    return;
                }
                if (Horse != null)
                {
                    HorseJump();
                    return;
                }
                InterruptClip();
                float k = ModelScale(Body);
                float distance = bm.Cfg.Movement.JumpDistance * Mathf.Clamp(Body.bounds.size.z / 2.4f, 0.45f, 1.6f) * k;
                float landProbeClear = bm.JumpClearance(Body, Species);
                float height = bm.Cfg.Movement.JumpHeight * Mathf.Clamp(Body.bounds.size.y / 1.5f, 0.4f, 1.3f) * k;
                if (Species.Id == "panther")
                {
                    height *= 1.25f;
                }
                else if (Species.Id == "frog")
                {
                    height *= 3f;
                }
                float clear = bm.JumpClearance(Body, Species);
                Vector3 forward = Quaternion.Euler(0f, moveYaw, 0f) * Vector3.forward;
                jumpFrom = Body.transform.position;
                jumpCollision = false;
                platformLanding = false;
                jumpHold = 0f;
                float radius = Mathf.Clamp(Mathf.Min(Body.bounds.extents.x, Body.bounds.extents.z) * 0.6f * k, 0.15f, 0.7f);
                Vector3 origin = jumpFrom + Vector3.up * (radius + 0.25f * k);
                RaycastHit hit;
                Vector3 to = jumpFrom + forward * distance;
                if (Physics.SphereCast(origin, radius, forward, out hit, distance + radius, ObstacleMaskOf(), QueryTriggerInteraction.Ignore) && hit.normal.y < 0.65f)
                {
                    // something solid ahead: how high is it, and what is behind it?
                    float topY = hit.point.y;
                    bool topWalkable = false;
                    RaycastHit top;
                    Vector3 above = hit.point + forward * 0.2f + Vector3.up * (clear + 3f);
                    if (Physics.Raycast(above, Vector3.down, out top, clear + 6f, ObstacleMaskOf() | LayerMask.GetMask("Terrain"), QueryTriggerInteraction.Ignore))
                    {
                        topY = top.point.y;
                        topWalkable = top.normal.y >= 0.35f;
                    }
                    float obstacleHeight = topY - jumpFrom.y;
                    jumpHitSurface = Beastmaster.ColliderSurface(hit.collider);
                    if (obstacleHeight <= clear + 0.05f)
                    {
                        // over it: land beyond the obstacle, on the ground or on its top when the surface is walkable there
                        Vector3 landing;
                        if (topWalkable && FindPlatform(forward, hit.distance, distance, clear, out landing))
                        {
                            to = landing;                      // onto the raised surface (a floor, a triangle, a crate, a rock): it can be stood on
                            height = Mathf.Max(height, obstacleHeight + 0.45f * k);
                            platformLanding = true;
                        }
                        else if (FindLanding(forward, hit.distance, distance, out landing))
                        {
                            to = landing;
                            height = Mathf.Max(height, obstacleHeight + 0.35f * k);
                        }
                        else
                        {
                            jumpCollision = true;
                        }
                    }
                    else
                    {
                        jumpCollision = true;
                    }
                    if (jumpCollision)
                    {
                        jumpHitPoint = hit.point;
                        to = jumpFrom + forward * Mathf.Max(0f, hit.distance - radius * 0.6f);
                        height = Mathf.Min(height, 0.35f * k);
                    }
                }
                if (!jumpCollision && !platformLanding)
                {
                    // something to stand on where the jump ends (a low floor, a crate, a rock the sphere sailed over)?
                    RaycastHit stand;
                    float ground = TerrainMeta.HeightMap.GetHeight(to);
                    if (Physics.Raycast(to + Vector3.up * (landProbeClear + 2f), Vector3.down, out stand, landProbeClear + 5f, ObstacleMaskOf(), QueryTriggerInteraction.Ignore)
                        && stand.normal.y >= 0.35f && stand.point.y > ground + 0.12f && stand.point.y <= jumpFrom.y + landProbeClear + 0.1f
                        && !Physics.CheckCapsule(stand.point + Vector3.up * 0.5f, stand.point + Vector3.up * Mathf.Max(0.9f, Body.bounds.size.y * k * 0.6f), 0.3f, ObstacleMaskOf(), QueryTriggerInteraction.Ignore))
                    {
                        to = stand.point + Vector3.up * 0.05f;
                        height = Mathf.Max(height, stand.point.y - jumpFrom.y + 0.4f * k);
                        platformLanding = true;
                    }
                }
                if (!jumpCollision && !platformLanding)
                {
                    to.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(to), jumpFrom.y - 3f);
                }
                else
                {
                    to.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(to), jumpFrom.y - 0.5f);
                }
                jumpTo = to;
                jumpHeight = height;
                jumpDuration = Mathf.Clamp(0.35f + Vector3.Distance(jumpFrom, to) * 0.06f, 0.4f, 1.2f);
                jumpTime = 0f;
                jumping = true;
                nextJump = Time.time + bm.Cfg.Movement.JumpCooldown;
                agent.IsJumping = true;
                bm.StatsSpend(this, 5f);
                bm.LandSound(jumpFrom);                         // the push-off
            }

            // The first walkable raised surface behind the front edge of an obstacle, with room above it for the animal.
            private bool FindPlatform(Vector3 forward, float obstacleDist, float reach, float clear, out Vector3 landing)
            {
                landing = jumpFrom;
                int mask = ObstacleMaskOf();
                for (float d = obstacleDist + 0.25f; d <= reach + 1.2f; d += 0.35f)
                {
                    Vector3 p = jumpFrom + forward * d;
                    RaycastHit g;
                    if (!Physics.Raycast(p + Vector3.up * (clear + 2f), Vector3.down, out g, clear + 4f, mask, QueryTriggerInteraction.Ignore) || g.normal.y < 0.35f
                        || g.point.y < TerrainMeta.HeightMap.GetHeight(p) + 0.2f)
                    {
                        continue;
                    }
                    if (g.point.y > jumpFrom.y + clear + 0.1f || g.point.y < jumpFrom.y - 0.4f)
                    {
                        continue;
                    }
                    float height = Body.bounds.size.y * ModelScale(Body) * 0.6f;
                    if (!Physics.CheckCapsule(g.point + Vector3.up * 0.5f, g.point + Vector3.up * Mathf.Max(0.9f, height), 0.3f, ObstacleMaskOf(), QueryTriggerInteraction.Ignore))
                    {
                        landing = g.point + Vector3.up * 0.05f;
                        return true;
                    }
                }
                return false;
            }

            private int ObstacleMaskOf()
            {
                return LayerMask.GetMask("Construction", "Deployed", "World", "Tree", "Vehicle World");
            }

            // The first spot behind the obstacle where the animal can stand: on the navigation mesh, free of solid things.
            private bool FindLanding(Vector3 forward, float obstacleDist, float reach, out Vector3 landing)
            {
                landing = jumpFrom;
                for (float d = obstacleDist + 0.9f; d <= reach + 1.4f; d += 0.5f)
                {
                    Vector3 p = jumpFrom + forward * d;
                    p.y = TerrainMeta.HeightMap.GetHeight(p);
                    Vector3 onNav;
                    if (!Animal.TrySampleNavmesh(p, 1.4f, out onNav) || Vector3.Distance(new Vector3(onNav.x, 0f, onNav.z), new Vector3(p.x, 0f, p.z)) > 1.0f)
                    {
                        continue;
                    }
                    float height = Body.bounds.size.y * 0.6f;
                    if (!Physics.CheckCapsule(onNav + Vector3.up * 0.5f, onNav + Vector3.up * Mathf.Max(0.9f, height), 0.3f, ObstacleMaskOf(), QueryTriggerInteraction.Ignore))
                    {
                        landing = onNav;
                        return true;
                    }
                }
                return false;
            }

            private void TickJump(float dt)
            {
                jumpTime += dt;
                float boost = 1f;
                float t = Mathf.Clamp01(jumpTime / jumpDuration);
                Vector3 pos = Vector3.Lerp(jumpFrom, jumpTo, t);
                pos.y += jumpHeight * boost * 4f * t * (1f - t);
                if (TerrainMeta.Collision == null || !TerrainMeta.Collision.GetIgnore(pos))
                {
                    pos.y = Mathf.Max(pos.y, TerrainMeta.HeightMap.GetHeight(pos)); // the arc never cuts through a hill
                }
                Body.transform.position = pos;
                Body.transform.rotation = Quaternion.Euler(0f, moveYaw + yawOffset, 0f);
                if (t >= 1f)
                {
                    jumping = false;
                    if (platformLanding)
                    {
                        // standing on something that has no navigation mesh: the animal is moved by the plugin there until it steps back onto the mesh
                        platformLanding = false;
                        offMesh = true;
                        agent.updatePosition = false;              // the agent must not pull the body back down to the ground under the platform
                        Body.transform.position = jumpTo;
                    }
                    else
                    {
                        agent.IsJumping = false;
                        offMesh = false;
                        agent.updatePosition = true;
                        if (!agent.WarpToWorldPosition(jumpTo))
                        {
                            agent.WarpToWorldPosition(jumpFrom);
                            Body.transform.position = jumpFrom;
                            jumpTo = jumpFrom;
                        }
                    }
                    if (jumpCollision)
                    {
                        Crash();
                    }
                    else
                    {
                        bm.LandSound(jumpTo);                    // the landing
                    }
                }
            }

            // A jump into something too high: the animal hits it, hurts itself a little, whimpers and falls (with the sound of the hit and of the thing it hit).
            private void Crash()
            {
                jumpCollision = false;
                currentSpeed = 0f;
                Gait = 1;
                fallenUntil = Time.time + 2.2f;
                bm.BumpSound(jumpHitPoint, jumpHitSurface);
                if (Animal != null && !bm.Play(Animal, "knockdown", "knock", "stumble", "hurt", "pain"))
                {
                    bm.Play(Animal, "sleep", "lie", "rest");        // an animal without a fall clip (the stag) goes down in its lying pose
                }
                Body.Hurt(Mathf.Max(2f, Body.MaxHealth() * bm.Cfg.Movement.CrashDamagePercent / 100f), Rust.DamageType.Blunt, null, false);
            }

            // The horse: an impulse over what its physics lets it clear, with a push-off and a landing sound.
            private bool horseAir;
            private float horseAirSince;

            private void HorseJump()
            {
                if (bm.Stable == null || !bm.Stable.JumpHorse(Horse, bm.JumpClearance(Body, Species)))
                {
                    return;
                }
                nextJump = Time.time + bm.Cfg.Movement.JumpCooldown;
                horseAir = true;
                horseAirSince = Time.time;
                bm.StatsSpend(this, 5f);
                bm.LandSound(Body.transform.position);
            }

            private void HorseAttack()
            {
                nextAttack = Time.time + bm.Cfg.Horse.AttackCooldown;
                bm.StatsSpend(this, 4f);
                if (bm.Stable != null)
                {
                    bm.Stable.PlayerStrike(Horse, this);
                }
            }

            // Running down a very steep slope: the animal may stumble and fall (and sometimes does not get up), like the horse.
            private void CheckSlope(float dt)
            {
                if (bm.Cfg.Movement.SlopeFallChance <= 0f || Time.time < slopeCheckAt || currentSpeed < GaitSpeed(3) * 0.9f)
                {
                    return;
                }
                slopeCheckAt = Time.time + 0.5f;
                Vector3 pos = Body.transform.position;
                Vector3 ahead = pos + Quaternion.Euler(0f, moveYaw, 0f) * Vector3.forward * 2.5f;
                float drop = TerrainMeta.HeightMap.GetHeight(pos) - TerrainMeta.HeightMap.GetHeight(ahead);
                float slope = Mathf.Atan2(drop, 2.5f) * Mathf.Rad2Deg;
                if (slope < bm.Cfg.Movement.SlopeLimit)
                {
                    return;
                }
                if (UnityEngine.Random.value > bm.Cfg.Movement.SlopeFallChance * 0.5f)
                {
                    return;
                }
                fallenUntil = Time.time + 2.6f;
                currentSpeed = 0f;
                Gait = 1;
                if (Horse != null)
                {
                    Horse.RagdollHorse();                       // the horse's own fall, with its sounds
                }
                else
                {
                    bm.Play(Animal, "knockdown", "knock", "stumble", "death_front", "canter_death");
                    bm.BumpSound(pos + Vector3.up * 0.5f, "dirt");
                }
                Body.Hurt(Mathf.Max(3f, Body.MaxHealth() * bm.Cfg.Movement.CrashDamagePercent / 100f), Rust.DamageType.Fall, null, false);
                if (UnityEngine.Random.value < bm.Cfg.Movement.SlopeFallDeathChance)
                {
                    Body.Hurt(Body.health + 1f, Rust.DamageType.Fall);
                }
            }

            // Is there an object surface ahead that is a little higher than where the animal stands (a roof, a ramp, a floor edge, a crate)?
            private bool StepOntoObject(Vector3 pos, Vector3 next)
            {
                RaycastHit g;
                if (!Physics.Raycast(new Vector3(next.x, pos.y + 1.4f, next.z), Vector3.down, out g, 2.6f, ObstacleMaskOf(), QueryTriggerInteraction.Ignore))
                {
                    return false;
                }
                return g.normal.y >= 0.3f && g.point.y > TerrainMeta.HeightMap.GetHeight(next) + 0.25f && g.point.y - pos.y <= 1.2f && g.point.y > pos.y - 0.3f;
            }

            // /bm seat up|down while riding: the height of the seat of this species, kept for everybody
            public void AdjustSeat(float delta)
            {
                SpeciesOverride o;
                if (!bm.Cfg.Species.TryGetValue(Species.Id, out o))
                {
                    o = new SpeciesOverride();
                    bm.Cfg.Species[Species.Id] = o;
                }
                o.SeatLift = (o.SeatLift ?? 0f) + delta;
                bm.SaveConfig();
                seatOffset += Vector3.up * delta;
                if (Seat != null && !Seat.IsDestroyed)
                {
                    Seat.transform.localPosition = seatOffset + Vector3.up * seatLift;
                    Seat.SendNetworkUpdate();
                }
            }

            public void SuppressCrash(float seconds)
            {
                nextWallCrash = Mathf.Max(nextWallCrash, Time.time + seconds);
            }

            // Walking on a surface without a navigation mesh (the top of a wall, a crate, a floor): the animal follows the ground under it.
            private void OffMeshMove(Vector3 next, float dt)
            {
                Vector3 now = Body.transform.position;
                float ground;
                RaycastHit g;
                int mask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed");
                if (Physics.Raycast(new Vector3(next.x, now.y + 1.2f, next.z), Vector3.down, out g, 4f, mask, QueryTriggerInteraction.Ignore) && g.normal.y > 0.3f)
                {
                    ground = g.point.y;
                }
                else
                {
                    ground = TerrainMeta.HeightMap.GetHeight(next);
                }
                float y = Mathf.MoveTowards(now.y, ground, 10f * dt);
                Body.transform.position = new Vector3(next.x, y, next.z);
            }

            // The frog hops over water and never sinks: it stays on the surface.
            private void KeepFrogAfloat()
            {
                if (Species.Id != "frog" || waterFactor <= 0.02f)
                {
                    return;
                }
                Vector3 p = Body.transform.position;
                float surface = WaterLevel.GetWaterSurface(p, true, true, null);
                if (p.y < surface)
                {
                    Body.transform.position = new Vector3(p.x, surface, p.z);
                }
            }

            // ------------------------------------------------------------ the physics tick

            private void FixedUpdate()
            {
                if (released)
                {
                    return;
                }
                if (Player == null || !Player.IsConnected || Player.IsDead())
                {
                    bm.Drivers.Remove(Player != null ? Player.userID : 0UL);
                    Release("player gone");
                    return;
                }
                if (Body == null || Body.IsDestroyed || Body.IsDead())
                {
                    AnimalDied();
                    return;
                }
                float dt = Time.fixedDeltaTime;
                if (Kind == ControlKind.Form && Player.modelState != null && Player.modelState.ducked)
                {
                    Player.modelState.ducked = false;       // Ctrl is the animal's brake: the hidden body does not crouch (the camera would sink)
                }
                UpdateWater();
                SyncHealth(false);
                DrownTick(dt);
                if (Horse != null)
                {
                    HorseTick(dt);
                    return;
                }
                if (jumping)
                {
                    TickJump(dt);
                    KeepAboveTerrain();
                    return;
                }
                if (Time.time < fallenUntil)
                {
                    currentSpeed = 0f;
                    return;
                }
                if (Animal.IsMounted)
                {
                    currentSpeed = 0f;                  // loaded onto a transport: it can look around but not walk
                    UpdateLook(0f);
                    return;
                }
                if (offMesh && Time.time >= nextMeshCheck)
                {
                    nextMeshCheck = Time.time + 0.3f;
                    Vector3 onNav;
                    Vector3 here = Body.transform.position;
                    if (Animal.TrySampleNavmesh(here, 0.8f, out onNav) && new Vector2(onNav.x - here.x, onNav.z - here.z).magnitude < 0.5f && Mathf.Abs(onNav.y - here.y) < 0.7f)
                    {
                        agent.updatePosition = true;
                        if (agent.WarpToWorldPosition(onNav))
                        {
                            offMesh = false;
                            agent.IsJumping = false;
                        }
                        else
                        {
                            agent.updatePosition = false;
                        }
                    }
                }
                if (PoseName != null)
                {
                    if (Time.time >= poseUntil)
                    {
                        ClearPose();
                    }
                    else
                    {
                        currentSpeed = 0f;
                        UpdateLook(0f);
                        return;
                    }
                }
                float topSpeed = Mathf.Max(0.5f, GaitSpeed(5));
                float speedFrac = Mathf.Clamp01(Mathf.Abs(currentSpeed) / topSpeed);
                float agility = Mathf.Clamp(agent.angularSpeed / 180f, 0.8f, 1.25f);
                float rate = Mathf.Lerp(bm.Cfg.Movement.TurnRateSlow, bm.Cfg.Movement.TurnRateFast, speedFrac) * agility;
                SharpTurnTick(dt);
                if (Time.time < sharpBoostUntil)
                {
                    rate *= 1.8f;                       // thrown round by the sharp-turn animation
                }
                if (turnAboutLeft > 0f)
                {
                    turnAboutLeft -= dt;
                    yawVel = turnDir * 180f / TurnAboutTime;
                }
                else
                {
                    float targetVel = ((right ? 1f : 0f) - (left ? 1f : 0f)) * rate;
                    yawVel = Mathf.MoveTowards(yawVel, targetVel, rate * 7f * dt);
                }
                float newYaw = moveYaw + yawVel * dt;
                if (Mathf.Abs(yawVel) > 0.01f && bm.BodyOverlaps(Body, Body.transform.position, newYaw + yawOffset) && !bm.BodyOverlaps(Body, Body.transform.position, moveYaw + yawOffset))
                {
                    yawVel = 0f;                         // turning would push a part of the body into a wall: it does not turn
                }
                else
                {
                    moveYaw = newYaw;
                }
                float target;
                if (turnAboutLeft > 0f)
                {
                    target = Mathf.Max(GaitSpeed(1), currentSpeed * 0.4f);
                }
                else if (fwd)
                {
                    target = GaitSpeed(Mathf.Max(EffectiveGait(), 1)) * WaterMultiplier();
                }
                else if (back)
                {
                    target = -GaitSpeed(1) * 0.6f * WaterMultiplier();
                }
                else
                {
                    target = 0f;
                }
                if (Gait == 0 && fwd)
                {
                    Gait = 1;
                }
                float accel = braking ? 14f : target > currentSpeed ? 3f : 6f;
                currentSpeed = Mathf.MoveTowards(currentSpeed, target, accel * dt);
                if (braking && currentSpeed <= GaitSpeed(1) + 0.05f)
                {
                    braking = false;
                }
                Quaternion heading = Quaternion.Euler(0f, moveYaw, 0f);
                if (Mathf.Abs(currentSpeed) > 0.01f)
                {
                    Vector3 pos = Body.transform.position;
                    Vector3 step = heading * Vector3.forward * (currentSpeed * dt);
                    float speedBefore = Mathf.Abs(currentSpeed);
                    bool hitWall;
                    Vector3 wallPoint;
                    string wallSurface;
                    step = bm.WallSlide(Body, pos, step, ref currentSpeed, out hitWall, out wallPoint, out wallSurface);       // no walking through walls
                    if (hitWall && speedBefore > GaitSpeed(3) * 0.75f && Time.time >= nextWallCrash)
                    {
                        nextWallCrash = Time.time + 3f;
                        jumpHitPoint = wallPoint;
                        jumpHitSurface = wallSurface;
                        Crash();                                // a wall or an object at a run: the animal hurts itself and falls
                        return;
                    }
                    Vector3 next = pos + step;
                    next = bm.LeashClamp(this, next);
                    if (!offMesh && StepOntoObject(pos, next))
                    {
                        offMesh = true;                           // the way leads up onto something without a navigation mesh: the plugin walks the animal there
                        agent.IsJumping = true;
                        agent.updatePosition = false;
                    }
                    if (offMesh)
                    {
                        OffMeshMove(next, dt);
                    }
                    else
                    {
                        agent.Move(agent.WorldToNavSpace(next) - agent.WorldToNavSpace(pos));
                    }
                }
                else if (!offMesh && (Leader != null || Kind == ControlKind.Form))
                {
                    Vector3 pos = Body.transform.position;
                    Vector3 held = bm.LeashClamp(this, pos);
                    if ((held - pos).sqrMagnitude > 0.0004f)
                    {
                        agent.Move(agent.WorldToNavSpace(held) - agent.WorldToNavSpace(pos));
                    }
                }
                Body.transform.rotation = Quaternion.Euler(0f, moveYaw + yawOffset, 0f);
                KeepAboveTerrain();
                KeepFrogAfloat();
                bm.ChargeTick(this);
                CheckSlope(dt);
                UpdateLook(speedFrac);
                UpdateIdle();
                if (bm.Cfg.General.LogLevel >= 3 && Time.time - lastLog > 5f && Mathf.Abs(currentSpeed) > 0.1f)
                {
                    lastLog = Time.time;
                    bm.Log(3, $"[drive] {Species.Id} {Kind} {(Spectating ? "spec" : "seat")} gait {Gait} speed {currentSpeed:0.00} turn {yawVel:0} water {waterFactor:0.00} pos {Body.transform.position}");
                }
            }

            // The horse form: the plugin sets the speed and heading, the horse's own physics (legs, slopes, water, ragdoll) do the rest.
            private Rigidbody horseBody;
            private HorseDrive horseDrive;
            private float nextHorsePush;

            private void HorseTick(float dt)
            {
                if (horseBody == null)
                {
                    horseBody = Horse.GetComponent<Rigidbody>();
                    horseDrive = Horse.GetComponent<HorseDrive>();
                }
                if (horseBody == null)
                {
                    return;
                }
                if (Horse.HasFlag(BaseEntity.Flags.Reserved12) || horseBody.isKinematic || Time.time < fallenUntil)
                {
                    currentSpeed = 0f;                          // down after a fall
                    return;
                }
                moveYaw = Horse.transform.eulerAngles.y;
                float topSpeed = Mathf.Max(0.5f, GaitSpeed(5));
                float speedFrac = Mathf.Clamp01(Mathf.Abs(currentSpeed) / topSpeed);
                float rate = Mathf.Lerp(bm.Cfg.Movement.TurnRateSlow, bm.Cfg.Movement.TurnRateFast, speedFrac);
                float targetVel = ((right ? 1f : 0f) - (left ? 1f : 0f)) * rate;
                yawVel = Mathf.MoveTowards(yawVel, targetVel, rate * 7f * dt);
                float target = fwd ? GaitSpeed(Mathf.Max(EffectiveGait(), 1)) * WaterMultiplier() : back ? -GaitSpeed(1) * 0.6f * WaterMultiplier() : 0f;
                if (Gait == 0 && fwd)
                {
                    Gait = 1;
                }
                float accel = braking ? 14f : target > currentSpeed ? 3f : 6f;
                currentSpeed = Mathf.MoveTowards(currentSpeed, target, accel * dt);
                if (braking && currentSpeed <= GaitSpeed(1) + 0.05f)
                {
                    braking = false;
                }
                horseBody.MoveRotation(Quaternion.AngleAxis(yawVel * dt, Vector3.up) * horseBody.rotation);
                Vector3 forward = Horse.transform.forward;
                forward.y = 0f;
                forward.Normalize();
                Vector3 v = horseBody.linearVelocity;
                horseBody.linearVelocity = new Vector3(forward.x * currentSpeed, v.y, forward.z * currentSpeed);
                bm.LeashHorse(this, horseBody);
                if (horseBody.IsSleeping())
                {
                    horseBody.WakeUp();
                }
                if (horseAir && Time.time - horseAirSince > 0.25f && Physics.Raycast(Horse.transform.position + Vector3.up * 0.4f, Vector3.down, 0.8f, LayerMask.GetMask("Terrain", "World", "Construction", "Deployed"), QueryTriggerInteraction.Ignore))
                {
                    horseAir = false;
                    bm.LandSound(Horse.transform.position);     // the landing
                }
                if (Time.time >= nextHorsePush)
                {
                    nextHorsePush = Time.time + 0.3f;
                    if (horseDrive != null)
                    {
                        horseDrive.PushAnimation(Mathf.Abs(currentSpeed), yawVel / 60f);
                    }
                }
                bm.ChargeTick(this);
                CheckSlope(dt);
            }

            // The head turns slowly toward where the camera looks, less the faster the animal runs, never past its neck.
            // The FSM normally drives this through NPCNetworking, which does not tick while the AI is parked.
            private void UpdateLook(float speedFrac)
            {
                if (npcNet == null || LookDirectionField == null || Time.time < nextLook)
                {
                    return;
                }
                nextLook = Time.time + 0.1f;
                Quaternion body = Quaternion.Euler(0f, moveYaw + yawOffset, 0f);
                Vector3 local = Quaternion.Inverse(body) * Player.eyes.HeadForward();
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
                float maxYaw = Mathf.Lerp(70f, 12f, speedFrac);
                Vector3 desired = body * Quaternion.Euler(Mathf.Clamp(pitch, -25f, 30f), Mathf.Clamp(yaw, -maxYaw, maxYaw), 0f) * Vector3.forward;
                Vector3 smoothed = Vector3.RotateTowards(lookDir, desired, 70f * Mathf.Deg2Rad * 0.1f, 0f);
                if (Vector3.Angle(smoothed, lookDir) < 0.5f)
                {
                    return;
                }
                lookDir = smoothed;
                LookDirectionField.SetValue(npcNet, lookDir);
                Animal.SendNetworkUpdate();
            }

            // The body sits at the seat's position. Antihack (InsideTerrain) kills and kicks a player whose position is more
            // than 0.3 m under the terrain - the OnPlayerViolation hook can stop the kick but not the kill - so the seat never
            // dips under the surface: on small creatures, slopes and jump arcs it is lifted a little instead.
            private float RequiredLift()
            {
                Vector3 seatBase = Body.transform.TransformPoint(seatOffset);
                if (TerrainMeta.HeightMap == null || (TerrainMeta.Collision != null && TerrainMeta.Collision.GetIgnore(seatBase)))
                {
                    return 0f;
                }
                return Mathf.Max(0f, TerrainMeta.HeightMap.GetHeight(seatBase) + 0.15f - seatBase.y) / ModelScale(Body);
            }

            private void KeepAboveTerrain()
            {
                if (Seat == null || Seat.IsDestroyed)
                {
                    return;
                }
                float lift = RequiredLift();
                if (lift > seatLift || lift < seatLift - 0.1f)
                {
                    seatLift = lift;
                    Seat.transform.localPosition = seatOffset + Vector3.up * seatLift;
                    Seat.SendNetworkUpdate();
                }
            }

            // ------------------------------------------------------------ end

            // The player left in animal form: the view is taken down, the animal stays behind asleep (the plugin parks it).
            public void DetachForSleep()
            {
                if (released)
                {
                    return;
                }
                released = true;
                bm.EndChain(this, "sleep");
                EndGuests();
                ClearPose();
                if (Player != null)
                {
                    Rust.Ai.SimpleAIMemory.PlayerIgnoreList.Remove(Player);
                    bm.ClearKeyHints(Player);
                    bm.ClearInventoryCard(Player);
                    bm.ClearNightVision(Player);
                    ClearView();
                    bm.Drivers.Remove(Player.userID);
                }
                if (Body != null && !Body.IsDestroyed && agent != null)
                {
                    agent.IsJumping = false;
                    agent.Unpause(this);
                }
                if (Body != null && !Body.IsDestroyed)
                {
                    if (Animal != null)
                    {
                        bm.Park(Animal, "sleep");
                        bm.Unpark(Animal, "control");
                    }
                    else if (Horse != null && bm.Stable != null)
                    {
                        bm.Stable.SleepForm(Horse);
                    }
                }
                Destroy(this);
            }

            public void Release(string reason)
            {
                if (released)
                {
                    return;
                }
                released = true;
                bool form = Kind == ControlKind.Form;
                bm.EndChain(this, reason);
                EndGuests();
                ClearPose();
                if (Player != null)
                {
                    bm.ClearKeyHints(Player);
                    bm.ClearInventoryCard(Player);
                    bm.ClearNightVision(Player);
                    if (form)
                    {
                        Player.limitNetworking = false;
                        Rust.Ai.SimpleAIMemory.PlayerIgnoreList.Remove(Player);
                    }
                    ClearView();
                    Player.SendNetworkUpdateImmediate();
                    bm.Drivers.Remove(Player.userID);
                }
                if (Body != null && !Body.IsDestroyed)
                {
                    if (agent != null)
                    {
                        agent.IsJumping = false;
                        if (swimSet)
                        {
                            agent.IsSwimming = false;
                        }
                        agent.Unpause(this);
                    }
                    if (Animal != null)
                    {
                        bm.Unpark(Animal, "control");
                    }
                    else if (Horse != null && bm.Stable != null)
                    {
                        bm.Stable.ReleaseControl(Horse);
                    }
                }
                if (form && Player != null)
                {
                    bm.OnFormEnded(this, reason);
                }
                bm.Log(3, "Control released: " + reason);
                Destroy(this);
            }
        }
    }

    // ======================================== Data.cs
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

        internal partial class GearRecord
        {
            public int Saddle;                           // 0 none, 1 single, 2 double
            public int Shoes;                            // 0 none, 1 basic, 2 high quality
            public List<ItemRecord> Bag = new List<ItemRecord>();
            public bool HasBag;
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

    // ======================================== Follower.cs
    public partial class Beastmaster
    {
        // Animals the plugin walks around (tamed followers, the chain behind an animal-form leader, a scared wolf, a wolf pack that keeps
        // close to a wolf player ...). The animal's own AI stays parked; its navigation agent does the path finding with the species'
        // authored gaits, so the animal looks and sounds exactly like a free animal on the move.
        internal readonly Dictionary<ulong, Follower> Followers = new Dictionary<ulong, Follower>();

        internal Follower FollowerOf(BaseNetworkable animal)
        {
            Follower f;
            return animal != null && animal.net != null && Followers.TryGetValue(animal.net.ID.Value, out f) && f != null ? f : null;
        }

        // The entries of followers whose animal is gone (the component dies with it) are dropped from time to time.
        private int followerPrune;

        private void PruneFollowers()
        {
            foreach (KeyValuePair<ulong, Follower> kv in Followers.ToList())
            {
                if (kv.Value == null)
                {
                    Followers.Remove(kv.Key);
                }
            }
        }

        internal Follower StartFollower(BaseNPC2 animal, string reason, Func<Vector3?> where)
        {
            if (!Valid(animal))
            {
                return null;
            }
            if (Followers.Count > 40 && ++followerPrune % 25 == 0)
            {
                PruneFollowers();
            }
            StopFollower(animal);
            Park(animal, reason);
            Follower f = animal.gameObject.AddComponent<Follower>();
            f.Setup(this, animal, reason, where);
            Followers[animal.net.ID.Value] = f;
            return f;
        }

        internal void StopFollower(BaseNetworkable animal)
        {
            Follower f = FollowerOf(animal);
            if (f != null)
            {
                Followers.Remove(animal.net.ID.Value);
                f.End();
            }
        }

        internal void StopAllFollowers()
        {
            foreach (Follower f in Followers.Values.ToList())
            {
                if (f != null)
                {
                    f.End();
                }
            }
            Followers.Clear();
        }

        // Authored gait speed of an animal (the agent's own table), scaled by the config.
        internal float AnimalGaitSpeed(BaseNPC2 animal, SpeciesDef species, int gait)
        {
            RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
            if (agent == null)
            {
                return 1f;
            }
            float speed;
            switch (gait)
            {
                case 0: speed = agent.sneakSpeed; break;
                case 1: speed = agent.walkSpeed; break;
                case 2: speed = agent.jogSpeed; break;
                case 3: speed = agent.runSpeed; break;
                case 4: speed = agent.sprintSpeed; break;
                default: speed = agent.fullSprintSpeed; break;
            }
            return speed * (species != null ? SpeedScaleOf(species) : 1f);
        }

        internal sealed class Follower : FacepunchBehaviour
        {
            public BaseNPC2 Animal;
            public string Reason;
            public Func<Vector3?> Where;                  // where to be (null = stand still)
            public float StopDistance = 4f;               // close enough
            public float RunDistance = 22f;               // beyond this the animal runs
            public float TeleportDistance = 0f;           // beyond this it is moved next to the destination (0 = never)
            public bool Flee;                             // run away from Where instead of toward it
            public float FleeDistance = 18f;
            public Func<BaseCombatEntity> Target;         // attack this (overrides Where while it lives)
            public BasePlayer AttackOwner;                // the owner that sent the animal (gets no gather credit)
            public bool MatchLeader;                      // adopt the leader's gait (chains)
            public Func<float> LeaderSpeed;

            private Beastmaster bm;
            private SpeciesDef species;
            private RustNavMeshAgent agent;
            private float nextAttack;
            private float nextIdle;
            private bool ended;
            private int attackIndex;

            public void Setup(Beastmaster plugin, BaseNPC2 animal, string reason, Func<Vector3?> where)
            {
                bm = plugin;
                Animal = animal;
                Reason = reason;
                Where = where;
                species = SpeciesOf(animal);
                agent = animal.GetComponent<RustNavMeshAgent>();
                nextIdle = Time.time + UnityEngine.Random.Range(4f, 9f);
                InvokeRepeating(Think, UnityEngine.Random.Range(0.1f, 0.5f), 0.4f);
            }

            private void Think()
            {
                if (ended)
                {
                    return;
                }
                if (!Valid(Animal) || Animal.IsDead() || agent == null)
                {
                    End();
                    return;
                }
                if (bm.DriverOfAnimal(Animal) != null)
                {
                    return;      // a player controls it right now
                }
                BaseCombatEntity target = Target != null ? Target() : null;
                if (target != null && Alive(target))
                {
                    Chase(target);
                    return;
                }
                Vector3? where = Where != null ? Where() : null;
                if (!where.HasValue)
                {
                    agent.ResetPath();
                    Idle();
                    return;
                }
                Vector3 pos = Animal.transform.position;
                Vector3 dest = where.Value;
                float dist = Vector3.Distance(pos, dest);
                if (Flee)
                {
                    Vector3 away = pos - dest;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.01f)
                    {
                        away = Animal.transform.forward;
                    }
                    if (dist > FleeDistance)
                    {
                        agent.ResetPath();
                        Idle();
                        return;
                    }
                    Vector3 spot = pos + away.normalized * FleeDistance;
                    spot.y = TerrainMeta.HeightMap.GetHeight(spot);
                    Go(spot, RustNavMeshAgent.Speeds.Sprint, 1f);
                    return;
                }
                if (TeleportDistance > 0f && dist > TeleportDistance)
                {
                    Vector3 spot = GroundNear(dest, 5f);
                    if (agent.WarpToWorldPosition(spot))
                    {
                        agent.ResetPath();
                    }
                    return;
                }
                if (dist <= StopDistance)
                {
                    agent.ResetPath();
                    Idle();
                    return;
                }
                RustNavMeshAgent.Speeds gait = dist > RunDistance ? RustNavMeshAgent.Speeds.Run : dist > StopDistance * 3f ? RustNavMeshAgent.Speeds.Jog : RustNavMeshAgent.Speeds.Walk;
                if (MatchLeader && LeaderSpeed != null)
                {
                    float speed = LeaderSpeed();
                    gait = speed > agent.runSpeed * 0.9f ? RustNavMeshAgent.Speeds.Run : speed > agent.jogSpeed * 0.8f ? RustNavMeshAgent.Speeds.Jog : speed > 0.2f ? RustNavMeshAgent.Speeds.Walk : gait;
                    if (dist > StopDistance * 2.5f && gait < RustNavMeshAgent.Speeds.Jog)
                    {
                        gait = RustNavMeshAgent.Speeds.Jog;
                    }
                }
                Go(dest, gait, StopDistance * 0.7f);
            }

            private void Go(Vector3 destination, RustNavMeshAgent.Speeds gait, float stopDistance)
            {
                agent.SetDestinationWithParams(destination, true, gait, null, null, null, null, stopDistance);
            }

            private void Idle()
            {
                if (Time.time < nextIdle)
                {
                    return;
                }
                nextIdle = Time.time + UnityEngine.Random.Range(8f, 16f);
                List<ClipRef> clips = bm.Clips(Animal).Where(c => (Has(c, "idle") || Has(c, "look") || Has(c, "breathe")) && !Has(c, "death") && !Has(c, "turn") && !Has(c, "sleep") && !Has(c, "attack")).ToList();
                if (clips.Count > 0)
                {
                    PlayClip(Animal, clips[UnityEngine.Random.Range(0, clips.Count)]);
                }
            }

            // Goes for a target and bites it with the animal's own attack (the same damage pipeline as a controlled animal).
            private void Chase(BaseCombatEntity target)
            {
                Vector3 pos = Animal.transform.position;
                Vector3 to = target.transform.position - pos;
                to.y = 0f;
                float reach = (Animal.bounds.extents.z + 1.8f) * ModelScale(Animal);
                if (to.magnitude > reach)
                {
                    Go(target.transform.position, RustNavMeshAgent.Speeds.Sprint, reach * 0.6f);
                    return;
                }
                agent.ResetPath();
                if (Time.time < nextAttack)
                {
                    return;
                }
                List<ClipRef> clips = bm.Clips(Animal).Where(c => (Has(c, "attack") || Has(c, "headbutt") || Has(c, "bite")) && !Has(c, "death")).ToList();
                if (clips.Count == 0)
                {
                    return;
                }
                ClipRef clip = clips[attackIndex++ % clips.Count];
                AttackParams p = ReadAttack(Animal, clip, false);
                float heading = Yaw(to);
                Animal.transform.rotation = Quaternion.Euler(0f, heading, 0f);
                PlayClip(Animal, clip);
                nextAttack = Time.time + Mathf.Max(bm.Cfg.Combat.Cooldown, p.Delay + 0.5f);
                BaseNPC2 animal = Animal;
                Invoke(() =>
                {
                    if (!ended && Valid(animal) && !animal.IsDead())
                    {
                        bm.ResolveHits(animal, null, heading, p, false);
                    }
                }, p.Delay);
            }

            public void End()
            {
                if (ended)
                {
                    return;
                }
                ended = true;
                CancelInvoke(Think);
                if (Valid(Animal))
                {
                    if (agent != null)
                    {
                        agent.ResetPath();
                    }
                    bm.Followers.Remove(Animal.net.ID.Value);
                    bm.Unpark(Animal, Reason);
                }
                Destroy(this);
            }
        }
    }

    // ======================================== Form.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- animal form: start, end, logout sleep

        // Players who logged out in animal form: their animal sleeps where it stands until they return (R9.31-R9.34).
        internal readonly Dictionary<ulong, BaseCombatEntity> FormSleepers = new Dictionary<ulong, BaseCombatEntity>();

        internal void OnFormStarted(Driver d)
        {
            Vector3 at = d.Body.transform.position;
            timer.Once(0.1f, () => FormAppear(at));                    // and the animal appears with the same effect
            Data.Forms.Remove(d.Player.userID);
            FormSleepers.Remove(d.Player.userID);
        }

        internal void OnFormEnded(Driver d, string reason)
        {
            BasePlayer player = d.Player;
            Vector3 spot = Valid(d.Body) ? d.Body.transform.position : player.transform.position;
            FormSmoke(spot);
            if (player != null && player.IsConnected && !player.IsDead() && reason != "animal died")
            {
                Vector3 back = GroundNear(spot, d.Body != null && !d.Body.IsDestroyed ? d.Body.bounds.extents.x * ModelScale(d.Body) + 1.2f : 1.5f);
                player.Teleport(back);
                FormAppear(back);
            }
            HudSwitch(player, true);
            Data.RitualCooldown[player.userID] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Cfg.Form.Cooldown;
            if ((d.OwnsAnimal || RecordOf(d.Body) == null) && Valid(d.Body) && !d.Body.IsDead())
            {
                bool shutdown = reason != null && (reason.StartsWith("plugin") || reason == "despawned");
                if (!shutdown)
                {
                    LeaveAnimalBehind(d);               // it stays where it is: X at it becomes it again, E rides it, 50 m away it vanishes
                }
                else
                {
                    if (d.Horse != null && Stable != null)
                    {
                        Stable.Forget(d.Horse);
                        Data.Horses.Remove(d.Horse.skinID);
                    }
                    Data.Bosses.Remove(d.Body.skinID);
                    d.Body.Kill();
                }
            }
        }

        // Returns true when the animal stays behind asleep and the player's form is kept.
        internal bool OnFormLogout(Driver d)
        {
            if (!Cfg.Form.SleepOnLogout || !Valid(d.Body) || d.Body.IsDead())
            {
                return false;
            }
            BasePlayer player = d.Player;
            BaseCombatEntity animal = d.Body;
            ulong tag = animal.skinID >= TagBase ? animal.skinID : NewTag();
            animal.skinID = tag;
            d.DetachForSleep();
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (animal is BaseNPC2)
            {
                Park((BaseNPC2)animal, "sleep");
                if (livestock != null)
                {
                    livestock.SetSleeping(true);
                }
                else
                {
                    Play((BaseNPC2)animal, "sleep", "lie", "rest");
                }
            }
            Data.Forms[player.userID] = new FormRecord
            {
                Player = player.userID,
                Species = d.Species.Id,
                Prefab = animal.PrefabName,
                Position = new V3(animal.transform.position),
                Health = animal.health,
                Genes = livestock != null ? unchecked((ulong)(uint)livestock.Genes) : d.Horse != null ? (ulong)Mathf.Max(0, Array.IndexOf(d.Horse.breeds, d.Horse.GetBreed())) : 0UL,
                LoggedOut = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Asleep = true,
                Tag = tag,
                OwnsAnimal = d.OwnsAnimal
            };
            FormSleepers[player.userID] = animal;
            Log(2, player.displayName + " left in " + d.Species.Display + " form - the animal sleeps.");
            return true;
        }

        // A player who left in animal form comes back as himself: the animal that slept for him goes away (a tamed one only wakes up).
        internal void EndFormOnLogin(BasePlayer player)
        {
            FormRecord rec;
            if (!Data.Forms.TryGetValue(player.userID, out rec))
            {
                return;
            }
            BaseCombatEntity animal;
            FormSleepers.TryGetValue(player.userID, out animal);
            Vector3 spot = Valid(animal) ? animal.transform.position : rec.Position.Value;
            Data.Forms.Remove(player.userID);
            FormSleepers.Remove(player.userID);
            if (Valid(animal) && !animal.IsDead())
            {
                BaseNPC2 npc = animal as BaseNPC2;
                if (npc != null)
                {
                    Unpark(npc, "sleep");
                }
                else if (animal is RidableHorse && Stable != null)
                {
                    Stable.WakeForm((RidableHorse)animal);
                }
                LivestockAnimal livestock = animal as LivestockAnimal;
                if (livestock != null)
                {
                    livestock.SetSleeping(false);
                }
                if (rec.OwnsAnimal || RecordOf(animal) == null)
                {
                    FormSmoke(spot);
                    Data.Bosses.Remove(animal.skinID);
                    animal.Kill();
                }
            }
            player.limitNetworking = false;
            BasePlayer p = player;
            timer.Once(1.5f, () =>
            {
                if (p != null && p.IsConnected && !p.IsDead())
                {
                    p.Teleport(GroundNear(spot, 1.5f));
                    FormAppear(p.transform.position);
                    HudSwitch(p, true);
                }
            });
        }

        // A returning player continues in animal form; an animal that died while the player was away has already killed them.
        internal void ResumeForm(BasePlayer player)
        {
            FormRecord rec;
            if (!Data.Forms.TryGetValue(player.userID, out rec))
            {
                return;
            }
            BaseCombatEntity animal;
            if (!FormSleepers.TryGetValue(player.userID, out animal) || !Valid(animal) || animal.IsDead())
            {
                Data.Forms.Remove(player.userID);
                FormSleepers.Remove(player.userID);
                return;
            }
            timer.Once(1.5f, () =>
            {
                if (player == null || !player.IsConnected || player.IsDead() || !Valid(animal))
                {
                    return;
                }
                player.limitNetworking = false;
                Driver d;
                if (StartControl(player, animal, ControlKind.Form, true))
                {
                    d = DriverOf(player);
                    if (d != null)
                    {
                        d.OwnsAnimal = rec.OwnsAnimal;
                    }
                    BaseNPC2 npc = animal as BaseNPC2;
                    if (npc != null)
                    {
                        Unpark(npc, "sleep");
                    }
                    else if (animal is RidableHorse && Stable != null)
                    {
                        Stable.WakeForm((RidableHorse)animal);
                    }
                    LivestockAnimal livestock = animal as LivestockAnimal;
                    if (livestock != null)
                    {
                        livestock.SetSleeping(false);
                    }
                    Data.Forms.Remove(player.userID);
                    FormSleepers.Remove(player.userID);
                }
            });
        }

        // After a reload or restart: find the sleeping animals of logged-out players again (they carry their tag in skinID).
        internal void RestoreSleepers()
        {
            foreach (FormRecord rec in Data.Forms.Values.ToList())
            {
                BaseCombatEntity animal = null;
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                {
                    BaseCombatEntity npc = net as BaseNPC2 ?? (BaseCombatEntity)(net as RidableHorse);
                    if (npc != null && rec.Tag != 0UL && npc.skinID == rec.Tag)
                    {
                        animal = npc;
                        break;
                    }
                }
                if (animal == null)
                {
                    SpeciesDef species = SpeciesById(rec.Species);
                    if (species != null && species.Available)
                    {
                        animal = GameManager.server.CreateEntity(species.Prefab, rec.Position.Value, Quaternion.identity) as BaseCombatEntity;
                        if (animal != null)
                        {
                            animal.skinID = rec.Tag;
                            animal.Spawn();
                            animal.health = Mathf.Max(1f, rec.Health);
                            RidableHorse reborn = animal as RidableHorse;
                            if (reborn != null)
                            {
                                reborn.SetBreed((int)rec.Genes);
                                SetSeats(reborn, 0);
                            }
                        }
                    }
                }
                if (animal == null)
                {
                    Data.Forms.Remove(rec.Player);
                    continue;
                }
                FormSleepers[rec.Player] = animal;
                BaseNPC2 sleeperNpc = animal as BaseNPC2;
                if (sleeperNpc != null)
                {
                    Park(sleeperNpc, "sleep");
                }
                else if (animal is RidableHorse && Stable != null)
                {
                    Stable.SleepForm((RidableHorse)animal);
                }
                LivestockAnimal livestock = animal as LivestockAnimal;
                if (livestock != null)
                {
                    livestock.SetSleeping(true);
                }
            }
        }

        // The animal of an absent player died: the player dies with it - it is processed at once, the player meets the death on the next login.
        internal void SleeperAnimalDied(BaseNetworkable entity)
        {
            if (FormSleepers.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<ulong, BaseCombatEntity> pair in FormSleepers.ToList())
            {
                if (pair.Value == entity)
                {
                    FormSleepers.Remove(pair.Key);
                    Data.Forms.Remove(pair.Key);
                    BasePlayer sleeper = BasePlayer.FindSleeping(pair.Key);
                    if (sleeper != null && !sleeper.IsDead())
                    {
                        sleeper.limitNetworking = false;
                        sleeper.Die();
                    }
                }
            }
        }

        // ---------------------------------------------------------------- the animal a player has left

        internal sealed class LeftForm
        {
            public BaseCombatEntity Animal;
            public ulong Owner;
            public List<ItemRecord> Bag = new List<ItemRecord>();
        }

        internal readonly Dictionary<ulong, LeftForm> LeftForms = new Dictionary<ulong, LeftForm>();

        internal bool LeftOwnedBy(BaseNetworkable animal, BasePlayer player)
        {
            LeftForm lf;
            return animal != null && animal.net != null && player != null && LeftForms.TryGetValue(animal.net.ID.Value, out lf) && lf.Owner == player.userID;
        }

        internal void LeaveAnimalBehind(Driver d)
        {
            BaseCombatEntity body = d.Body;
            var left = new LeftForm { Animal = body, Owner = d.Player.userID };
            List<ItemRecord> carried;
            if (FormBags.TryGetValue(d.Player.userID, out carried))
            {
                left.Bag = carried;                  // the bag stays with the animal
                FormBags.Remove(d.Player.userID);
            }
            LeftForms[body.net.ID.Value] = left;
            BaseNPC2 npc = body as BaseNPC2;
            if (npc != null)
            {
                Park(npc, "left");
            }
            RidableHorse horse = body as RidableHorse;
            if (horse != null)
            {
                SetSeats(horse, Mathf.Max(1, SeatsOf(horse)));       // the horse can be ridden by its owner
            }
        }

        internal void DespawnLeft(LeftForm lf, bool effect)
        {
            LeftForms.Remove(lf.Animal != null && lf.Animal.net != null ? lf.Animal.net.ID.Value : 0UL);
            if (!Valid(lf.Animal))
            {
                return;
            }
            if (effect)
            {
                FormSmoke(lf.Animal.transform.position);
            }
            foreach (ItemRecord rec in lf.Bag)
            {
                Item item = ItemFromRecord(rec);
                if (item != null)
                {
                    item.Drop(lf.Animal.transform.position + Vector3.up, Vector3.up);       // what the animal carried stays on the ground
                }
            }
            lf.Bag.Clear();
            Despawn(lf.Animal);
        }

        internal sealed class LeftModule : Module
        {
            public override int Order => 16;
            private float nextCheck;

            public override void Tick(float now)
            {
                if (Bm.LeftForms.Count == 0 || now < nextCheck)
                {
                    return;
                }
                nextCheck = now + 1f;
                foreach (LeftForm lf in Bm.LeftForms.Values.ToList())
                {
                    if (!Valid(lf.Animal) || lf.Animal.IsDead())
                    {
                        Bm.LeftForms.Remove(lf.Animal != null && lf.Animal.net != null ? lf.Animal.net.ID.Value : 0UL);
                        continue;
                    }
                    if (Bm.DriverOfAnimal(lf.Animal) != null)
                    {
                        continue;                                      // the owner is in it or on it
                    }
                    BasePlayer owner = BasePlayer.FindByID(lf.Owner);
                    if (owner == null || !owner.IsConnected || owner.IsDead() || Vector3.Distance(owner.transform.position, lf.Animal.transform.position) > 50f)
                    {
                        Bm.DespawnLeft(lf, true);
                    }
                }
            }

            // X at the animal that was left: the player becomes it again.
            public override object OnServerCommand(BasePlayer player, ConsoleSystem.Arg arg)
            {
                if (Bm.LeftForms.Count == 0 || arg.cmd.FullName != "vehicle.swapseats" || Bm.DriverOf(player) != null)
                {
                    return null;
                }
                LeftForm best = null;
                float bestDist = 5f;
                foreach (LeftForm lf in Bm.LeftForms.Values)
                {
                    if (lf.Owner != player.userID || !Valid(lf.Animal))
                    {
                        continue;
                    }
                    float dist = Vector3.Distance(player.transform.position, lf.Animal.transform.position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = lf;
                    }
                }
                if (best == null)
                {
                    return null;
                }
                BaseNPC2 npc = best.Animal as BaseNPC2;
                if (npc != null)
                {
                    Bm.Unpark(npc, "left");
                }
                Bm.LeftForms.Remove(best.Animal.net.ID.Value);
                List<ItemRecord> bagBack = best.Bag;
                BasePlayer p = player;
                BaseCombatEntity animal = best.Animal;
                Bm.NextTick(() =>
                {
                    if (Bm.StartControl(p, animal, ControlKind.Form, true))
                    {
                        Driver d = Bm.DriverOf(p);
                        if (d != null)
                        {
                            d.OwnsAnimal = true;
                        }
                        Bm.FormBags[p.userID] = bagBack;      // it carries its bag again
                    }
                });
                return true;
            }

            public override void OnPlayerDisconnected(BasePlayer player, string reason)
            {
                foreach (LeftForm lf in Bm.LeftForms.Values.Where(x => x.Owner == player.userID).ToList())
                {
                    if (Bm.DriverOfAnimal(lf.Animal) == null)
                    {
                        Bm.DespawnLeft(lf, false);
                    }
                }
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                if (Bm.LeftForms.Count > 0 && entity != null && entity.net != null)
                {
                    Bm.LeftForms.Remove(entity.net.ID.Value);
                }
            }

            public override void OnDisable()
            {
                Clear();
            }

            public override void Unload()
            {
                Clear();
            }

            private void Clear()
            {
                foreach (LeftForm lf in Bm.LeftForms.Values.ToList())
                {
                    Bm.DespawnLeft(lf, false);
                }
                Bm.LeftForms.Clear();
            }
        }

        internal sealed class FormModule : Module
        {
            public override int Order => 20;

            public override void OnServerInitialized()
            {
                Bm.RestoreSleepers();
            }

            public override void OnPlayerConnected(BasePlayer player)
            {
                if (Bm.Data.Forms.ContainsKey(player.userID))
                {
                    Bm.ResumeForm(player);              // the animal lay down when the player left: he is that animal again
                }
            }

            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                Bm.SleeperAnimalDied(entity);
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                Bm.SleeperAnimalDied(entity);
            }

            public override void Unload()
            {
                Bm.ReleaseAllParked();
            }

            public override void OnDisable()
            {
                Bm.ReleaseAllParked();
            }
        }
    }

    // ======================================== Gear.cs
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

    // ======================================== Guests.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- others handle a player in animal form (SPEC 9B)
        // Riders (after the animal has eaten food thrown in front of its head), a leash, milking and shearing, loading onto transport and breeding.
        // The animal is steered by the transformed player only: a rider is a passenger, whatever seat it sits in.

        internal readonly Dictionary<BaseMountable, Driver> GuestSeats = new Dictionary<BaseMountable, Driver>();

        // Config, the per-player refusal and the team rule decide who may handle a form.
        internal bool FormHandledBy(Driver host, BasePlayer other)
        {
            if (host == null || host.Kind != ControlKind.Form || other == null || other == host.Player || host.Player == null)
            {
                return false;
            }
            if (SettingsOf(host.Player.userID).RefuseSaddle)
            {
                return false;
            }
            string rule = (Cfg.Form.HandledBy ?? "anyone").ToLowerInvariant();
            if (rule == "nobody")
            {
                return false;
            }
            return rule != "team" || SameParty(host.Player, other);
        }

        internal bool NeedsFood(SpeciesDef s)
        {
            return s != null && Cfg.Form.RideFedSpecies != null && Cfg.Form.RideFedSpecies.Contains(s.Id);
        }

        internal int MaxRiders(SpeciesDef s)
        {
            return Mathf.Max(0, NeedsFood(s) ? Cfg.Form.RidersFed : Cfg.Form.RidersOther);
        }

        // May this player climb on that animal form right now? (the horse form uses this in its vanilla mount hook, the others in TryBoard)
        internal bool FormMountCheck(BasePlayer player, Driver host, bool tell)
        {
            if (!FormHandledBy(host, player) || MaxRiders(host.Species) <= 0)
            {
                if (tell)
                {
                    TellRaw(player, "This animal does not want a rider.");
                }
                return false;
            }
            if (NeedsFood(host.Species) && host.FedUntil <= Time.time)
            {
                if (tell)
                {
                    TellRaw(player, "It will only let you on after it has eaten: drop food in front of its head and wait until it has eaten it.");
                }
                return false;
            }
            return true;
        }

        // The animal has eaten something that was lying on the ground: it is calm enough to be ridden for a while.
        internal void FormFed(Driver d)
        {
            if (d != null && d.Kind == ControlKind.Form)
            {
                d.FedUntil = Time.time + Mathf.Max(30f, Cfg.Form.FedSeconds);
            }
        }

        // E on somebody's animal form (the plugin's own seats for the Gen2 animals; the horse is mounted the game's way).
        internal bool TryFormHandle(BasePlayer player, Driver host)
        {
            if (host == null || host.Kind != ControlKind.Form || !host.Alive)
            {
                return false;
            }
            if (player.serverInput.IsDown(BUTTON.DUCK))
            {
                ToggleLeash(player, host);
                return true;
            }
            if (host.Horse != null)
            {
                return false;                     // the vanilla mount of the horse takes it from here
            }
            host.PruneGuests();
            if (!FormMountCheck(player, host, true))
            {
                return true;
            }
            if (host.Species.Critter || host.Species.Ride == RideTier.None)
            {
                TellRaw(player, "A " + host.Species.Display.ToLowerInvariant() + " is too small to ride.");
                return true;
            }
            if (host.Guests.Count >= MaxRiders(host.Species))
            {
                TellRaw(player, "There is no room left on the " + host.Species.Display.ToLowerInvariant() + ".");
                return true;
            }
            host.AddGuest(player);
            return true;
        }

        // ---------------------------------------------------------------- the leash

        internal BasePlayer LeaderOf(Driver d)
        {
            if (d == null)
            {
                return null;
            }
            if (d.Leader != null && d.Leader.IsConnected && !d.Leader.IsDead())
            {
                return d.Leader;
            }
            LivestockAnimal ls = d.Animal as LivestockAnimal;
            if (ls != null && ls.IsLeading())
            {
                BasePlayer vanilla = ls.LeadingPlayer.Get(true);
                if (vanilla != null && !vanilla.IsDead())
                {
                    return vanilla;
                }
            }
            if (d.Horse != null && d.Horse.IsLeading && d.Horse.leadingPlayer != null)
            {
                return d.Horse.leadingPlayer;
            }
            return null;
        }

        private void ToggleLeash(BasePlayer player, Driver host)
        {
            if (host.Leader == player)
            {
                host.Leader = null;
                TellRaw(player, "You let the " + host.Species.Display.ToLowerInvariant() + " go.");
                TellRaw(host.Player, "You are free of the leash.");
                return;
            }
            if (!FormHandledBy(host, player))
            {
                TellRaw(player, "This animal does not want to be led.");
                return;
            }
            if (host.Leader != null)
            {
                TellRaw(player, "Somebody else leads it already.");
                return;
            }
            host.Leader = player;
            TellRaw(player, "You lead the " + host.Species.Display.ToLowerInvariant() + ". Hold Ctrl and press E at it again to let it go.");
            TellRaw(host.Player, player.displayName + " leads you on a leash.");
        }

        // A taut leash: the animal cannot go further than the rope allows and is drawn along when the leader walks away.
        internal Vector3 LeashClamp(Driver d, Vector3 next)
        {
            BasePlayer leader = LeaderOf(d);
            if (leader == null)
            {
                return next;
            }
            Vector3 at = leader.transform.position;
            Vector3 to = at - next;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > Cfg.Lead.BreakDistance)
            {
                if (d.Leader == leader)
                {
                    d.Leader = null;
                    TellRaw(leader, "The leash broke.");
                    TellRaw(d.Player, "The leash broke.");
                }
                return next;
            }
            float length = Mathf.Max(2f, Cfg.Form.LeashLength);
            if (dist <= length)
            {
                return next;
            }
            Vector3 clamped = next + to.normalized * (dist - length);
            clamped.y = next.y;
            return clamped;
        }

        // The horse: its speed is set toward the leader when the rope is taut.
        internal void LeashHorse(Driver d, Rigidbody body)
        {
            BasePlayer leader = LeaderOf(d);
            if (leader == null || body == null)
            {
                return;
            }
            Vector3 to = leader.transform.position - body.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > Cfg.Lead.BreakDistance)
            {
                if (d.Leader == leader)
                {
                    d.Leader = null;
                }
                return;
            }
            if (dist > Mathf.Max(2f, Cfg.Form.LeashLength))
            {
                Vector3 v = body.linearVelocity;
                Vector3 pull = to.normalized * Mathf.Clamp(dist - Cfg.Form.LeashLength, 1.5f, 7f);
                body.linearVelocity = new Vector3(pull.x, v.y, pull.z);
            }
        }

        // ---------------------------------------------------------------- milking, shearing, leading by the game's own interactions

        // The game's livestock only accepts a hand that it trusts. A player next to a form of cow, bull, sheep or ram is a familiar one, as far as
        // the rules of the server allow it, so that the game's own milking, shearing, rope and loading work on the transformed player.
        internal void CreditTrust(Driver d)
        {
            LivestockAnimal ls = d.Animal as LivestockAnimal;
            if (ls == null || d.Kind != ControlKind.Form)
            {
                return;
            }
            float need = ConVar.Livestock.trustToLead + 1f;
            foreach (BasePlayer p in BasePlayer.activePlayerList)
            {
                if (p == null || p.IsNpc || p.IsDead() || p.IsSleeping() || p == d.Player || Vector3.Distance(p.transform.position, ls.transform.position) > 10f)
                {
                    continue;
                }
                if (!FormHandledBy(d, p))
                {
                    continue;
                }
                float have = ls.TrustOf(p);
                if (have < need)
                {
                    ls.AddFamiliarity(p.userID, need - have);
                }
            }
        }

        // ---------------------------------------------------------------- breeding of forms (R19.7)

        // /bm breed: a cow form and a bull (a ram and a sheep) next to each other: the vanilla pregnancy starts and a calf (a lamb) is born in time.
        internal void BreedCommand(BasePlayer player, Action<string> reply)
        {
            Driver d = DriverOf(player);
            LivestockAnimal me = d != null && d.Kind == ControlKind.Form ? d.Animal as LivestockAnimal : null;
            if (me == null)
            {
                reply("Only a cow, bull, sheep or ram form can breed.");
                return;
            }
            SpeciesDef mine = d.Species;
            if (mine.Id != "cow" && mine.Id != "bull" && mine.Id != "sheep" && mine.Id != "ram")
            {
                reply("A " + mine.Display.ToLowerInvariant() + " cannot breed.");
                return;
            }
            string wanted = mine.Id == "cow" ? "bull" : mine.Id == "bull" ? "cow" : mine.Id == "sheep" ? "ram" : "sheep";
            var near = new List<BaseNPC2>();
            Vis.Entities(me.transform.position, 7f, near);
            LivestockAnimal partner = null;
            float best = float.MaxValue;
            foreach (BaseNPC2 npc in near)
            {
                LivestockAnimal other = npc as LivestockAnimal;
                SpeciesDef sp = other != null ? SpeciesOf(other) : null;
                if (other == null || other == me || sp == null || sp.Id != wanted || other.IsDead())
                {
                    continue;
                }
                float dist = Vector3.Distance(other.transform.position, me.transform.position);
                if (dist < best)
                {
                    best = dist;
                    partner = other;
                }
            }
            if (partner == null)
            {
                reply("There is no " + wanted + " within 7 m.");
                return;
            }
            LivestockAnimal female = me.IsMale ? partner : me;
            LivestockAnimal male = me.IsMale ? me : partner;
            if (!female.CanBePregnant() || !male.CanBreed())
            {
                reply("They are not ready: both have to be adults and not on a breeding cooldown.");
                return;
            }
            if (!female.MakePregnant(male))
            {
                reply("Nothing happened.");
                return;
            }
            RunEffect(FindPrefabPath("fx", "heart") ?? FindPrefabPath("fx", "sparkle"), female.transform.position + Vector3.up * 1.5f);
            reply("The " + (female == me ? "you are" : "she is") + " pregnant now. A young one will be born in time.");
            Driver other2 = DriverOfAnimal(partner);
            if (other2 != null)
            {
                TellRaw(other2.Player, "Your mate " + (female == partner ? "is" : "makes you") + " pregnant.");
            }
        }

        // ---------------------------------------------------------------- the module

        internal sealed class GuestModule : Module
        {
            public override int Order => 14;

            private float nextTrust;

            public override void Tick(float now)
            {
                if (Bm.Drivers.Count == 0)
                {
                    return;
                }
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || d.Kind != ControlKind.Form)
                    {
                        continue;
                    }
                    d.PruneGuests();
                    if (d.Leader != null && (!d.Leader.IsConnected || d.Leader.IsDead()))
                    {
                        d.Leader = null;
                    }
                }
                if (now < nextTrust)
                {
                    return;
                }
                nextTrust = now + 2f;
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d != null && d.Alive)
                    {
                        Bm.CreditTrust(d);
                    }
                }
            }

            // A rider on an animal form is carried; it never steers (the buttons that would drive a horse are dropped from its input).
            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (Bm.GuestSeats.Count == 0 && !player.isMounted)
                {
                    return null;
                }
                BaseMountable seat = player.GetMounted();
                if (seat == null)
                {
                    return null;
                }
                Driver host = HostOfSeat(seat);
                if (host != null && host.Player != player)
                {
                    input.current.buttons &= ~((int)BUTTON.FORWARD | (int)BUTTON.BACKWARD | (int)BUTTON.LEFT | (int)BUTTON.RIGHT | (int)BUTTON.SPRINT);
                }
                return null;
            }

            private Driver HostOfSeat(BaseMountable seat)
            {
                Driver host;
                if (Bm.GuestSeats.TryGetValue(seat, out host))
                {
                    return host;
                }
                RidableHorse horse = seat as RidableHorse ?? seat.GetParentEntity() as RidableHorse;
                Driver d = horse != null ? Bm.DriverOfAnimal(horse) : null;
                return d != null && d.Kind == ControlKind.Form ? d : null;
            }

            public override void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
            {
                Driver host;
                if (mountable != null && Bm.GuestSeats.TryGetValue(mountable, out host))
                {
                    BaseMountable seat = mountable;
                    Bm.NextTick(() => host.DropGuestSeat(seat));
                }
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Guests: " + Bm.GuestSeats.Count + " passenger seats on animal forms");
            }
        }
    }

    // ======================================== Horse.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- horses (R5.1-R5.3, R8)
        // The horse is a physics vehicle with saddle seats, its own inventories and a lead, not a navigation-mesh animal. Wild horses are born
        // without a saddle (so they must be tamed first) and get a temperament; the plugin moves a horse without a rider by setting its
        // velocity (HorseDrive), and uses the game's own seats, inventories and lead for everything a player does with it.

        internal HorseModule Stable;

        private static MethodInfo FindMethod(Type type, string name, params Type[] args)
        {
            try
            {
                return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            try
            {
                return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static readonly MethodInfo HorseSetSeats = FindMethod(typeof(RidableHorse), "SetSeatCount", typeof(int));
        private static readonly MethodInfo HorseUpdateClients = FindMethod(typeof(RidableHorse), "UpdateClients", typeof(bool));
        private static readonly FieldInfo HorseThrottle = FindField(typeof(RidableHorse), "throttleInput");
        private static readonly FieldInfo HorseSteer = FindField(typeof(RidableHorse), "steerInput");

        private static readonly MethodInfo RpcTargetGroup = typeof(BaseEntity).Assembly.GetType("RpcTarget") != null
            ? typeof(BaseEntity).Assembly.GetType("RpcTarget").GetMethod("NetworkGroup", new[] { typeof(string) })
            : null;

        // A client call to everyone who sees the entity (no arguments), by name.
        internal static void RpcGroup(BaseEntity entity, string name)
        {
            if (RpcTargetGroup == null || ClientRpcNoArg == null || entity == null || entity.IsDestroyed)
            {
                return;
            }
            ClientRpcNoArg.Invoke(entity, new object[] { RpcTargetGroup.Invoke(null, new object[] { name }) });
        }

        internal static void SetSeats(RidableHorse horse, int seats)
        {
            if (horse != null && HorseSetSeats != null)
            {
                HorseSetSeats.Invoke(horse, new object[] { seats });
            }
        }

        internal static int SeatsOf(RidableHorse horse)
        {
            return horse.HasDoubleSaddle ? 2 : horse.HasSingleSaddle ? 1 : 0;
        }

        // The horse the player looks at.
        internal RidableHorse HorseInSight(BasePlayer player, float range, float maxAngle = 25f)
        {
            Vector3 eye = player.eyes.position;
            Vector3 dir = player.eyes.HeadForward();
            var near = new List<RidableHorse>();
            Vis.Entities(eye, range, near);
            RidableHorse best = null;
            float bestScore = float.MaxValue;
            foreach (RidableHorse h in near)
            {
                if (!Valid(h) || h.IsDead())
                {
                    continue;
                }
                Vector3 to = h.transform.position + Vector3.up * 1.2f - eye;
                float angle = Vector3.Angle(dir, to);
                if (angle > maxAngle || to.magnitude > range)
                {
                    continue;
                }
                float score = angle + to.magnitude;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = h;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- moving a horse without a rider

        // Sets the velocity of a riderless horse toward a goal; the horse's own physics (suspension, slopes, ragdoll) stay in charge of the rest.
        internal sealed class HorseDrive : FacepunchBehaviour
        {
            public RidableHorse Horse;
            public Vector3? Goal;
            public float Speed;
            public float StopDistance = 1.5f;
            public float TurnRate = 130f;
            public bool Reached;
            private Rigidbody body;
            private float nextPush;

            public bool Busy
            {
                get { return Goal.HasValue && Speed > 0.05f; }
            }

            public void MoveTo(Vector3 goal, float speed)
            {
                Goal = goal;
                Speed = speed;
                Reached = false;
            }

            public void Stop()
            {
                Goal = null;
                Speed = 0f;
            }

            private void FixedUpdate()
            {
                if (!Valid(Horse))
                {
                    Destroy(this);
                    return;
                }
                if (!Goal.HasValue || Speed <= 0.05f)
                {
                    return;
                }
                if (body == null)
                {
                    body = Horse.GetComponent<Rigidbody>();
                    if (body == null)
                    {
                        return;
                    }
                }
                if (Horse.AnyMounted() || Horse.IsLeading || Horse.HasFlag(BaseEntity.Flags.Reserved12) || body.isKinematic || body.constraints == RigidbodyConstraints.FreezeAll)
                {
                    return;
                }
                Vector3 to = Goal.Value - Horse.transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist <= StopDistance)
                {
                    Goal = null;
                    Reached = true;
                    return;
                }
                float dt = Time.fixedDeltaTime;
                float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float diff = Mathf.DeltaAngle(Horse.transform.eulerAngles.y, want);
                float turn = Mathf.Clamp(diff, -TurnRate * dt, TurnRate * dt);
                body.MoveRotation(Quaternion.AngleAxis(turn, Vector3.up) * body.rotation);
                float align = Mathf.Clamp01(1f - Mathf.Abs(diff) / 100f);
                float speed = Mathf.Min(Speed * align, Mathf.Max(1.2f, dist * 1.5f));
                Vector3 fwd = Horse.transform.forward;
                fwd.y = 0f;
                fwd.Normalize();
                Vector3 v = body.linearVelocity;
                body.linearVelocity = new Vector3(fwd.x * speed, v.y, fwd.z * speed);
                if (body.IsSleeping())
                {
                    body.WakeUp();
                }
                if (Time.time >= nextPush)
                {
                    nextPush = Time.time + 0.3f;
                    PushAnimation(speed, diff);
                }
            }

            // The game only tells clients about the gait of a horse that is ridden or led; this makes it do so for a free one too.
            public void PushAnimation(float speed, float steerDiff)
            {
                if (HorseUpdateClients == null || HorseThrottle == null || HorseSteer == null)
                {
                    return;
                }
                Horse.currentGait = speed < 2.3f ? RidableHorse.GaitType.Walk : speed < 5f ? RidableHorse.GaitType.Trot : speed < 8f ? RidableHorse.GaitType.Canter : RidableHorse.GaitType.Gallop;
                HorseThrottle.SetValue(Horse, 1f);
                HorseSteer.SetValue(Horse, Mathf.Clamp(steerDiff / 40f, -1f, 1f));
                HorseUpdateClients.Invoke(Horse, new object[] { true });
            }
        }

        internal sealed class HorseState
        {
            public RidableHorse Horse;
            public HorseInfo Info;
            public ulong Tag;
            public ulong NetId;
            public HorseDrive Drive;
            public Vector3 Home;
            public float ScaredUntil;
            public Vector3 ScaredFrom;
            public float SleepUntil;
            public bool SleepActive;                 // asleep (or just waking): the wake-up still has to happen
            public string Mode = "idle";             // idle, wander, flee, charge: what the brain last set the drive to
            public bool Frozen;
            public RigidbodyConstraints SavedConstraints;
            public float NextWander;
            public float NextAttack;
            public float NextDefence;
            public ulong ProvokedBy;
            public float ProvokedUntil;
            public BaseCombatEntity AttackTarget;
            public float AttackUntil;
            public float SpeedMult = 1f;             // bosses run faster
            public float Damage;                     // bosses hit harder (0 = the configured horse attack)
            public bool WasLeading;                  // the vanilla rope was on last tick
            public BasePlayer LastLeader;
            public float StopRequested;              // the leader asked to let go (so the rope breaking is no accident)
            public BasePlayer Tether;                // keeps following this leader on an invisible tether (the vanilla rope broke at 7 m)
            public readonly Dictionary<ulong, float> Bond = new Dictionary<ulong, float>();
            public bool Asleep(float now) { return SleepUntil > now; }
        }

        internal sealed class HorseModule : Module
        {
            public override int Order => 40;

            public readonly Dictionary<ulong, HorseState> States = new Dictionary<ulong, HorseState>();
            private float lastTick;
            private float nextJump;

            public int Count { get { return States.Count; } }

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Stable = this;
                bm.Subscribe("OnEntitySpawned");
                bm.Subscribe("CanLootEntity");
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["HorseAggressive"] = "This horse is aggressive: it will not let you near. Put it to sleep with an Incapacitate Dart first - it wakes up calm.",
                    ["HorseAsleep"] = "The horse is asleep.",
                    ["HorseWild"] = "This horse is wild. Tame it first (stay near it, then press E).",
                    ["HorseNoRide"] = "You may not ride this horse.",
                    ["HorseNoLoot"] = "You may not open the gear of this horse.",
                    ["HorseNoLead"] = "This horse cannot be led.",
                    ["HorseHurtFlee"] = "The horse bolts!",
                    ["HorseTemper"] = "This horse is {0}.",
                };
            }

            // ---------------------------------------------------------------- registry

            public HorseState StateOf(BaseNetworkable horse)
            {
                HorseState s;
                return horse != null && horse.net != null && States.TryGetValue(horse.net.ID.Value, out s) ? s : null;
            }

            private string RollTemperament()
            {
                HorseCfg h = Bm.Cfg.Horse;
                float total = Mathf.Max(0.0001f, h.CalmShare + h.ShyShare + h.AggressiveShare);
                float roll = UnityEngine.Random.value * total;
                return roll < h.CalmShare ? "calm" : roll < h.CalmShare + h.ShyShare ? "shy" : "aggressive";
            }

            public HorseState Register(RidableHorse horse, string temperament = null)
            {
                HorseState s = StateOf(horse);
                if (s != null)
                {
                    return s;
                }
                ulong tag = horse.skinID >= TagBase ? horse.skinID : Bm.NewTag();
                HorseInfo info;
                if (!Bm.Data.Horses.TryGetValue(tag, out info))
                {
                    info = new HorseInfo { Temperament = temperament ?? RollTemperament() };
                    Bm.Data.Horses[tag] = info;
                }
                horse.skinID = tag;
                s = new HorseState { Horse = horse, Info = info, Tag = tag, NetId = horse.net.ID.Value, Home = horse.transform.position, NextWander = Time.time + UnityEngine.Random.Range(8f, 30f) };
                s.Drive = horse.gameObject.AddComponent<HorseDrive>();
                s.Drive.Horse = horse;
                States[horse.net.ID.Value] = s;
                return s;
            }

            public void Forget(RidableHorse horse)
            {
                HorseState s = StateOf(horse);
                if (s == null)
                {
                    return;
                }
                States.Remove(horse.net.ID.Value);
                Wake(s, false);
                if (s.Drive != null)
                {
                    UnityEngine.Object.Destroy(s.Drive);
                }
            }

            // A wild horse is born without a saddle: it has to be tamed before a saddle can be fitted (R5.1).
            private void MakeWild(RidableHorse horse)
            {
                if (Bm.Cfg.Horse.WildUnsaddled && SeatsOf(horse) > 0 && !horse.AnyMounted())
                {
                    SetSeats(horse, 0);
                }
                Register(horse);
            }

            // A horse made by the spawn tool: wild (unsaddled, with a temperament) or tamed, with or without the saddle item.
            public void AfterSpawn(RidableHorse horse, bool tamed, bool saddled)
            {
                Register(horse);
                bool wantSaddle = saddled;
                if (SeatsOf(horse) > 0 && !wantSaddle)
                {
                    SetSeats(horse, 0);
                }
                if (wantSaddle && SeatsOf(horse) > 0 && horse.equipmentInventory != null && horse.equipmentInventory.itemList.Count == 0)
                {
                    SetSeats(horse, 0);
                    GiveSaddle(horse, 1);
                }
                if (!tamed)
                {
                    Bm.Log(3, "Horse spawned: " + StateOf(horse).Info.Temperament);
                }
            }

            public void GiveSaddle(RidableHorse horse, int seats)
            {
                Item saddle = ItemManager.CreateByName(seats > 1 ? "horse.saddle.double" : "horse.saddle.single", 1);
                if (saddle != null && !saddle.MoveToContainer(horse.equipmentInventory))
                {
                    saddle.Remove();
                }
            }

            // Only fresh, ownerless, unmarked horses of the world are made wild; the horses of vendors, of other plugins and of players stay as they are.
            public override void OnEntitySpawned(BaseNetworkable entity)
            {
                RidableHorse horse = entity as RidableHorse;
                if (horse == null || !Bm.Cfg.Features.Taming)
                {
                    return;
                }
                if (horse.skinID != 0UL || horse.OwnerID != 0UL)
                {
                    return;
                }
                Bm.timer.Once(1.5f, () =>
                {
                    if (Valid(horse) && !horse.IsForSale && horse.skinID == 0UL && horse.OwnerID == 0UL && !horse.AnyMounted())
                    {
                        MakeWild(horse);
                    }
                });
            }

            public override void OnServerInitialized()
            {
                int found = 0;
                var seen = new HashSet<ulong>();
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                {
                    RidableHorse horse = net as RidableHorse;
                    if (horse != null && horse.skinID >= TagBase && (Bm.Data.Horses.ContainsKey(horse.skinID) || Bm.Data.Tamed.ContainsKey(horse.skinID)))
                    {
                        Register(horse);
                        if (Bm.Cfg.Horse.WildUnsaddled && Bm.Cfg.Features.Taming && !Bm.Data.Tamed.ContainsKey(horse.skinID) && Bm.BossOf(horse) == null && SeatsOf(horse) > 0 && !HasSaddleItem(horse) && !horse.AnyMounted())
                        {
                            SetSeats(horse, 0);          // a reload gave it the game's saddle back; it is wild again
                        }
                        seen.Add(horse.skinID);
                        found++;
                    }
                }
                foreach (ulong tag in Bm.Data.Horses.Keys.ToList())
                {
                    if (!seen.Contains(tag))
                    {
                        Bm.Data.Horses.Remove(tag);
                    }
                }
                // the horses that were already in the world (saved by the game) are wild and unsaddled too
                foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                {
                    RidableHorse old = net as RidableHorse;
                    if (old != null && Bm.Cfg.Features.Taming && old.skinID == 0UL && old.OwnerID == 0UL && !old.IsForSale && !old.AnyMounted())
                    {
                        MakeWild(old);
                    }
                }
                Bm.Log(3, "Horses: " + found + " registered");
            }

            public override void OnDisable()
            {
                foreach (HorseState s in States.Values)
                {
                    s.Drive.Stop();
                    Wake(s, false);
                }
            }

            public override void Unload()
            {
                foreach (HorseState s in States.Values.ToList())
                {
                    Wake(s, false);
                    if (s.Drive != null)
                    {
                        UnityEngine.Object.Destroy(s.Drive);
                    }
                    // a wild horse gets the saddle of the game back, so that nothing stays broken without the plugin
                    RidableHorse horse = s.Horse;
                    if (Valid(horse) && Bm.RecordOf(horse) == null && Bm.BossOf(horse) == null && SeatsOf(horse) == 0 && !HasSaddleItem(horse))
                    {
                        SetSeats(horse, 1);
                    }
                }
                States.Clear();
            }

            private static bool HasSaddleItem(RidableHorse horse)
            {
                if (horse.equipmentInventory == null)
                {
                    return false;
                }
                foreach (Item item in horse.equipmentInventory.itemList)
                {
                    if (horse.GetSaddleItemSeatCount(item) > 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            // ---------------------------------------------------------------- the ticking brain

            public override void Tick(float now)
            {
                float dt = lastTick > 0f ? Mathf.Min(now - lastTick, 1f) : 0.25f;
                lastTick = now;
                if (States.Count == 0)
                {
                    return;
                }
                foreach (HorseState s in States.Values.ToList())
                {
                    if (!Valid(s.Horse))
                    {
                        States.Remove(s.NetId);
                        continue;
                    }
                    Guard(s, now, dt);
                }
            }

            private void Guard(HorseState s, float now, float dt)
            {
                try
                {
                    Think(s, now, dt);
                }
                catch (Exception ex)
                {
                    Bm.Log(1, "Horse brain: " + ex.Message);
                }
            }

            private void Think(HorseState s, float now, float dt)
            {
                RidableHorse h = s.Horse;
                if (Bm.DriverOfAnimal(h) != null)
                {
                    s.Drive.Stop();
                    return;                // a player is the horse
                }
                if (s.SleepActive && !s.Asleep(now))
                {
                    Wake(s, true);
                }
                if (Tether(s, now))
                {
                    return;
                }
                if (s.Asleep(now) || h.AnyMounted() || h.IsLeading || h.HasFlag(BaseEntity.Flags.Reserved12))
                {
                    return;
                }
                TamedRecord rec = Bm.RecordOf(h);
                if (rec != null)
                {
                    Duty(s, rec, now);
                    return;
                }
                if (s.AttackTarget != null && now < s.AttackUntil && Alive(s.AttackTarget))
                {
                    Charge(s, s.AttackTarget, now);
                    return;
                }
                if (!Bm.Cfg.Features.HorseAi)
                {
                    return;
                }
                Vector3 pos = h.transform.position;
                BasePlayer near = NearestPlayer(pos, Bm.Cfg.Horse.AggressionRadius + 6f);
                float nearDist = near != null ? Vector3.Distance(near.transform.position, pos) : float.MaxValue;
                string temper = s.Info.Calmed ? "calm" : s.Info.Temperament;
                // provoked by an attack: it defends itself (R19.18)
                if (s.ProvokedUntil > now)
                {
                    BasePlayer foe = BasePlayer.FindByID(s.ProvokedBy);
                    if (foe != null && !foe.IsDead())
                    {
                        Charge(s, foe, now);
                        return;
                    }
                }
                if (s.ScaredUntil > now)
                {
                    Vector3 away = pos - s.ScaredFrom;
                    away.y = 0f;
                    if (away.sqrMagnitude < 1f)
                    {
                        away = h.transform.forward;
                    }
                    Vector3 goal = pos + away.normalized * 30f;
                    s.Mode = "flee";
                    s.Drive.StopDistance = 1.5f;
                    s.Drive.MoveTo(GroundAt(goal), Bm.Cfg.Horse.FleeSpeed * s.SpeedMult);
                    return;
                }
                if (temper == "shy" && near != null && nearDist < Bm.Cfg.Horse.ShyRange)
                {
                    s.ScaredUntil = now + Bm.Cfg.Horse.ShyCalmSeconds;
                    s.ScaredFrom = near.transform.position;
                    s.Bond.Remove(near.userID);
                    RpcGroup(h, "CLIENT_Stand");
                    return;
                }
                if (temper == "aggressive" && near != null && nearDist < Bm.Cfg.Horse.AggressionRadius && !near.InSafeZone())
                {
                    Charge(s, near, now);
                    return;
                }
                if (s.Mode == "flee" || s.Mode == "charge" || (s.Mode == "wander" && !s.Drive.Busy))
                {
                    s.Drive.Stop();
                    s.Mode = "idle";
                }
                Bond(s, dt);
                Wander(s, now);
            }

            // The horse code lets go of the rope at 7 m. The leader keeps the horse on an invisible tether up to Lead.BreakDistance; the rope
            // is taken again when the horse is back within reach (R6B.4).
            private bool Tether(HorseState s, float now)
            {
                RidableHorse h = s.Horse;
                if (!Bm.Cfg.Features.Lead)
                {
                    s.WasLeading = false;
                    s.Tether = null;
                    return false;
                }
                if (h.IsLeading)
                {
                    s.WasLeading = true;
                    s.LastLeader = h.leadingPlayer;
                    s.Tether = null;
                    return false;
                }
                if (s.WasLeading)
                {
                    s.WasLeading = false;
                    BasePlayer l = s.LastLeader;
                    if (l != null && l.IsConnected && !l.IsDead() && !l.isMounted && now - s.StopRequested > 3f
                        && Vector3.Distance(l.transform.position, h.transform.position) > 5f)
                    {
                        s.Tether = l;
                    }
                }
                BasePlayer leader = s.Tether;
                if (leader == null)
                {
                    return false;
                }
                float dist = leader.IsConnected ? Vector3.Distance(leader.transform.position, h.transform.position) : float.MaxValue;
                if (!leader.IsConnected || leader.IsDead() || leader.isMounted || dist > Bm.Cfg.Lead.BreakDistance || h.AnyMounted() || s.Asleep(now))
                {
                    s.Tether = null;
                    s.Drive.Stop();
                    return false;
                }
                if (dist <= 3.4f)
                {
                    s.Tether = null;
                    s.Drive.Stop();
                    h.SetLeading(leader);
                    return true;
                }
                s.Mode = "tether";
                s.Drive.StopDistance = 2.2f;
                s.Drive.MoveTo(leader.transform.position, dist < 10f ? 3.5f : dist < 25f ? 6.5f : 9f);
                return true;
            }

            private static BasePlayer NearestPlayer(Vector3 pos, float range)
            {
                BasePlayer best = null;
                float bestDist = range;
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || p.IsNpc || (Instance != null && Instance.IsFormPlayer(p)) || (p.IsAdmin && p.IsFlying))
                    {
                        continue;
                    }
                    float d = Vector3.Distance(p.transform.position, pos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = p;
                    }
                }
                return best;
            }

            private static Vector3 GroundAt(Vector3 p)
            {
                p.y = TerrainMeta.HeightMap.GetHeight(p);
                return p;
            }

            // Familiarity grows while a player stays near a calm horse and fades when they are away (R5.2).
            private void Bond(HorseState s, float dt)
            {
                if (!Bm.Cfg.Features.Taming || (s.Info.Temperament == "aggressive" && !s.Info.Calmed))
                {
                    return;
                }
                Vector3 pos = s.Horse.transform.position;
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || p.IsNpc)
                    {
                        continue;
                    }
                    float have;
                    s.Bond.TryGetValue(p.userID, out have);
                    float d = Vector3.Distance(p.transform.position, pos);
                    if (d < 6f)
                    {
                        s.Bond[p.userID] = Mathf.Min(have + dt, 900f);
                    }
                    else if (have > 0f && d > 15f)
                    {
                        have -= dt * 0.25f;
                        if (have <= 0f) s.Bond.Remove(p.userID); else s.Bond[p.userID] = have;
                    }
                }
            }

            private void Wander(HorseState s, float now)
            {
                if (!Bm.Cfg.Horse.Wander || now < s.NextWander)
                {
                    return;
                }
                s.NextWander = now + UnityEngine.Random.Range(20f, 50f);
                Vector3 spot = s.Home + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * UnityEngine.Random.Range(3f, 10f);
                spot = GroundAt(spot);
                if (WaterLevel.GetWaterDepth(spot, true, true, null) > 0.3f)
                {
                    return;
                }
                s.Mode = "wander";
                s.Drive.StopDistance = 1.5f;
                s.Drive.MoveTo(spot, 1.8f);
            }

            // ---------------------------------------------------------------- charging and striking

            private void Charge(HorseState s, BaseCombatEntity target, float now)
            {
                Vector3 to = target.transform.position - s.Horse.transform.position;
                to.y = 0f;
                s.Mode = "charge";
                if (to.magnitude > 3.2f)
                {
                    s.Drive.StopDistance = 2.5f;
                    s.Drive.MoveTo(target.transform.position, Bm.Cfg.Horse.FleeSpeed * s.SpeedMult);
                    return;
                }
                s.Drive.Stop();
                Strike(s, target, now);
            }

            public void Strike(HorseState s, BaseCombatEntity target, float now)
            {
                if (now < s.NextAttack)
                {
                    return;
                }
                s.NextAttack = now + Bm.Cfg.Horse.AttackCooldown;
                Vector3 to = target.transform.position - s.Horse.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                {
                    s.Horse.transform.rotation = Quaternion.Euler(0f, Yaw(to), 0f);
                }
                if (Bm.Cfg.Horse.RearUpAttack)
                {
                    RpcGroup(s.Horse, "CLIENT_Stand");
                }
                RidableHorse horse = s.Horse;
                Bm.timer.Once(Bm.Cfg.Horse.RearUpAttack ? 0.8f : 0.1f, () =>
                {
                    if (!Valid(horse) || horse.IsDead())
                    {
                        return;
                    }
                    HitFront(horse, s.Damage > 0f ? s.Damage : Bm.Cfg.Horse.AttackDamage, target);
                });
            }

            // Damage to everything the horse faces within 4 m (the aimed target and others in the cone).
            internal void HitFront(RidableHorse horse, float damage, BaseCombatEntity preferred, bool anyAnimal = false)
            {
                Vector3 pos = horse.transform.position;
                Vector3 fwd = horse.transform.forward;
                fwd.y = 0f;
                var near = new List<BaseCombatEntity>();
                float reach = ModelScale(horse);
                Vis.Entities(pos, 4.2f * reach, near);
                foreach (BaseCombatEntity e in near)
                {
                    if (e == null || e == horse || !Alive(e) || e is BaseCorpse || (e is RidableHorse))
                    {
                        continue;
                    }
                    BasePlayer p = e as BasePlayer;
                    if (p != null && (p.InSafeZone() || (Instance != null && Instance.IsFormPlayer(p) && Instance.Cfg.Form.Untargetable)))
                    {
                        continue;
                    }
                    if ((e is BaseNPC2 && Bm.IsTamed(e)) || Bm.IsBossPair(horse, e))
                    {
                        continue;
                    }
                    if (p == null && e != preferred && !anyAnimal)
                    {
                        continue;       // a charging horse hurts the players around it and its target, not every animal that stands near
                    }
                    Vector3 to = e.transform.position - pos;
                    to.y = 0f;
                    if (e != preferred && Vector3.Angle(fwd, to) > 70f)
                    {
                        continue;
                    }
                    e.Hurt(new HitInfo(horse, e, Rust.DamageType.Blunt, damage * Bm.Cfg.Combat.DamageScale, e.transform.position));
                    Bm.BossHit(horse, e);
                }
            }

            // ---------------------------------------------------------------- tamed horses: follow, wait, guard, attack

            private void Duty(HorseState s, TamedRecord rec, float now)
            {
                if (s.AttackTarget != null && now < s.AttackUntil && Alive(s.AttackTarget))
                {
                    Charge(s, s.AttackTarget, now);
                    return;
                }
                s.AttackTarget = null;
                if (rec.State == "wait" || !Bm.Cfg.Features.Taming)
                {
                    if (s.Drive.Busy) s.Drive.Stop();
                    return;
                }
                BasePlayer owner = BasePlayer.FindByID(rec.Owner);
                if (owner == null || !owner.IsConnected || owner.IsDead() || owner.IsSleeping())
                {
                    if (s.Drive.Busy) s.Drive.Stop();
                    return;
                }
                Vector3 pos = s.Horse.transform.position;
                Vector3 at = owner.transform.position;
                float dist = Vector3.Distance(pos, at);
                bool guard = rec.State == "guard";
                float stop = guard ? Bm.Cfg.Taming.GuardRadius * 0.45f : Bm.Cfg.Taming.FollowDistance;
                float tele = Bm.Cfg.Taming.TeleportDistance;
                if (tele > 0f && dist > tele)
                {
                    Teleport(s.Horse, GroundAt(at + new Vector3(2f, 0f, 2f)));
                    return;
                }
                if (dist <= stop)
                {
                    if (s.Drive.Busy) s.Drive.Stop();
                    return;
                }
                float speed = dist < 10f ? 2.2f : dist < 25f ? 5.5f : dist < 50f ? 8.5f : 11f;
                s.Drive.StopDistance = stop * 0.8f;
                s.Drive.MoveTo(at, speed);
            }

            private static void Teleport(RidableHorse horse, Vector3 spot)
            {
                Rigidbody body = horse.GetComponent<Rigidbody>();
                spot.y += 0.4f;
                if (body != null)
                {
                    body.position = spot;
                    body.linearVelocity = Vector3.zero;
                }
                horse.transform.position = spot;
                horse.SendNetworkUpdate();
            }

            public void Command(RidableHorse horse, TamedRecord rec)
            {
                HorseState s = StateOf(horse) ?? Register(horse);
                s.AttackTarget = null;
                if (rec.State == "wait")
                {
                    s.Drive.Stop();
                }
            }

            public void Attack(RidableHorse horse, BaseCombatEntity target, float seconds)
            {
                HorseState s = StateOf(horse) ?? Register(horse);
                s.AttackTarget = target;
                s.AttackUntil = Time.time + seconds;
            }

            // ---------------------------------------------------------------- taming (E on a wild horse)

            public bool Claim(BasePlayer player, HorseState s)
            {
                if (!Bm.Cfg.Features.Taming || !Bm.Has(player, PermTame))
                {
                    return false;
                }
                float now = Time.time;
                if (s.Asleep(now))
                {
                    Bm.Tell(player, "HorseAsleep");
                    return true;
                }
                if (s.Info.Temperament == "aggressive" && !s.Info.Calmed)
                {
                    Bm.Tell(player, "HorseAggressive");
                    return true;
                }
                if (s.ScaredUntil > now)
                {
                    Bm.Tell(player, "HorseHurtFlee");
                    return true;
                }
                float have;
                s.Bond.TryGetValue(player.userID, out have);
                float best = s.Bond.Count > 0 ? s.Bond.Values.Max() : 0f;
                if (best > have + 1f && best >= Bm.Cfg.Taming.BondSeconds * 0.5f)
                {
                    Bm.Tell(player, "BondTooEarly");
                    return true;
                }
                if (have < Bm.Cfg.Taming.BondSeconds)
                {
                    Bm.Tell(player, "BondProgress", Mathf.RoundToInt(have), Mathf.RoundToInt(Bm.Cfg.Taming.BondSeconds));
                    return true;
                }
                Bm.TameAnimal(s.Horse, player);
                s.Bond.Clear();
                return true;
            }

            // ---------------------------------------------------------------- darts, defence and damage

            // ---------------------------------------------------------------- a player as the horse (horse form)

            public void TakeControl(RidableHorse horse)
            {
                HorseState s = StateOf(horse) ?? Register(horse);
                s.Drive.Stop();
                s.AttackTarget = null;
                s.Mode = "idle";
                s.ProvokedUntil = 0f;
                s.ScaredUntil = 0f;
                Wake(s, false);
                if (horse.IsLeading)
                {
                    horse.SetLeading(null);
                }
                horse.DismountAllPlayers();
                // the player is the horse: riders get the seats once it has eaten (the mount hook checks that), the driver seat is the player's view
                SetSeats(horse, Mathf.Clamp(Bm.Cfg.Form.RidersFed, 0, 2));
            }

            public void ReleaseControl(RidableHorse horse)
            {
                HorseState s = StateOf(horse);
                if (s != null)
                {
                    s.Drive.Stop();
                    s.Info.Calmed = true;
                }
                if (Valid(horse) && !horse.IsDead())
                {
                    // the seats follow the saddle again
                    horse.DismountAllPlayers();
                    int seats = 0;
                    if (horse.equipmentInventory != null)
                    {
                        foreach (Item it in horse.equipmentInventory.itemList)
                        {
                            seats = Mathf.Max(seats, horse.GetSaddleItemSeatCount(it));
                        }
                    }
                    SetSeats(horse, seats);
                }
            }

            // The player logged out as the horse: it stands there asleep until they return.
            public void SleepForm(RidableHorse horse)
            {
                HorseState s = StateOf(horse) ?? Register(horse);
                s.SleepUntil = float.MaxValue;
                s.SleepActive = true;
                Freeze(s);
            }

            public void WakeForm(RidableHorse horse)
            {
                HorseState s = StateOf(horse);
                if (s != null)
                {
                    s.SleepUntil = 0f;
                    Wake(s, false);
                }
            }

            // Space as the horse: the impulse over what its size allows.
            public bool JumpHorse(RidableHorse horse, float height)
            {
                Rigidbody body = horse.GetComponent<Rigidbody>();
                if (body == null || body.isKinematic || horse.HasFlag(BaseEntity.Flags.Reserved12))
                {
                    return false;
                }
                RaycastHit hit;
                if (!Physics.Raycast(horse.transform.position + Vector3.up * 0.3f, Vector3.down, out hit, 0.9f, LayerMask.GetMask("Terrain", "World", "Construction", "Deployed"), QueryTriggerInteraction.Ignore))
                {
                    return false;      // not on the ground
                }
                body.AddForce(Vector3.up * Mathf.Sqrt(2f * 9.81f * Mathf.Clamp(height, 0.3f, 2f)), ForceMode.VelocityChange);
                return true;
            }

            // F as the horse: rear up and strike what is in front.
            public void PlayerStrike(RidableHorse horse, Driver d)
            {
                if (Bm.Cfg.Horse.RearUpAttack)
                {
                    RpcGroup(horse, "CLIENT_Stand");
                }
                Bm.timer.Once(Bm.Cfg.Horse.RearUpAttack ? 0.8f : 0.1f, () =>
                {
                    if (Valid(horse) && !horse.IsDead())
                    {
                        HitFront(horse, Bm.Cfg.Horse.AttackDamage, null, true);
                    }
                });
            }

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                RidableHorse horse = entity as RidableHorse;
                if (horse == null || States.Count == 0)
                {
                    return null;
                }
                if (info != null && info.damageTypes.Get(Rust.DamageType.Decay) > 0f && Bm.DriverOfAnimal(horse) != null)
                {
                    return true;           // the horse of a player does not decay while they are in it
                }
                HorseState s = StateOf(horse);
                if (s == null || info == null)
                {
                    return null;
                }
                BasePlayer by = info.InitiatorPlayer;
                if (by == null && info.Initiator != null)
                {
                    Driver attackingForm = Bm.DriverOfAnimal(info.Initiator);
                    by = attackingForm != null ? attackingForm.Player : null;       // the animal body of a player hit the horse
                }
                float now = Time.time;
                if (by != null && IsIncapacitateDart(info) && Bm.Cfg.Features.Taming)
                {
                    Sleep(s, by);
                    return true;
                }
                if (by != null && Bm.RecordOf(horse) == null && Bm.Cfg.Features.WildDefence && now >= s.NextDefence)
                {
                    // a wild horse that is attacked defends itself with the configured chance, otherwise it flees (R19.18)
                    s.NextDefence = now + 6f;
                    if (Bm.Cfg.Wild.AlwaysDefend.Contains("horse") || UnityEngine.Random.Range(0f, 100f) < Bm.Cfg.Wild.DefendChance)
                    {
                        s.ProvokedBy = by.userID;
                        s.ProvokedUntil = now + 25f;
                    }
                    else
                    {
                        s.ScaredUntil = now + Bm.Cfg.Horse.ShyCalmSeconds;
                        s.ScaredFrom = by.transform.position;
                    }
                }
                return null;
            }

            // A dart puts the horse to sleep for the configured time; it wakes up calm for good (R8.6).
            public void Sleep(HorseState s, BasePlayer by)
            {
                float now = Time.time;
                if (s.Asleep(now) || !Valid(s.Horse) || s.Horse.IsDead())
                {
                    return;
                }
                s.SleepUntil = now + Bm.Cfg.Horse.SleepSeconds;
                s.SleepActive = true;
                s.Mode = "idle";
                s.Drive.Stop();
                s.ProvokedUntil = 0f;
                s.ScaredUntil = 0f;
                s.Horse.RagdollHorse();
                RidableHorse horse = s.Horse;
                Bm.timer.Once(5.6f, () =>
                {
                    if (Valid(horse) && s.Asleep(Time.time))
                    {
                        Freeze(s);
                    }
                });
                if (by != null)
                {
                    Bm.Tell(by, "Sedated", "horse", Mathf.RoundToInt(Bm.Cfg.Horse.SleepSeconds));
                }
            }

            private void Freeze(HorseState s)
            {
                Rigidbody body = s.Horse.GetComponent<Rigidbody>();
                if (body == null || s.Frozen)
                {
                    return;
                }
                s.SavedConstraints = body.constraints;
                body.constraints = RigidbodyConstraints.FreezeAll;
                s.Frozen = true;
            }

            private void Wake(HorseState s, bool calm)
            {
                if (s.Frozen && Valid(s.Horse))
                {
                    Rigidbody body = s.Horse.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        body.constraints = s.SavedConstraints;
                        body.WakeUp();
                    }
                }
                s.Frozen = false;
                s.SleepActive = false;
                if (calm)
                {
                    s.Info.Calmed = true;
                    s.SleepUntil = 0f;
                }
            }

            // ---------------------------------------------------------------- riding rules, gear and the lead

            private static RidableHorse HorseOfSeat(BaseMountable mountable)
            {
                return mountable as RidableHorse ?? mountable.GetParentEntity() as RidableHorse;
            }

            public override object CanMountEntity(BasePlayer player, BaseMountable mountable)
            {
                if (player == null || mountable == null || States.Count == 0)
                {
                    return null;
                }
                RidableHorse horse = HorseOfSeat(mountable);
                HorseState s = horse != null ? StateOf(horse) : null;
                if (s == null)
                {
                    return null;
                }
                if (Bm.LeftOwnedBy(horse, player))
                {
                    return null;                                     // the horse the player has just left can be mounted by him
                }
                Driver formHost = Bm.DriverOfAnimal(horse);
                if (formHost != null && formHost.Kind == ControlKind.Form)
                {
                    // a player is the horse: others ride it after it has eaten, never as its driver
                    return Bm.FormMountCheck(player, formHost, true) ? null : (object)false;
                }
                if (s.Asleep(Time.time) || s.Frozen)
                {
                    Bm.Tell(player, "HorseAsleep");
                    return false;
                }
                if (Bm.Cfg.Riding.AdminBypass && Bm.IsAdmin(player))
                {
                    return null;
                }
                TamedRecord rec = Bm.RecordOf(horse);
                if (!Bm.Cfg.Features.Riding || !Bm.Has(player, PermRide))
                {
                    Bm.Tell(player, "HorseNoRide");
                    return false;
                }
                if (rec == null && Bm.Cfg.Riding.RequireTame)
                {
                    Bm.Tell(player, "HorseWild");
                    return false;
                }
                if (rec != null && !Bm.MayUse(player, rec.Owner, Bm.Cfg.Riding.Ownership))
                {
                    Bm.Tell(player, "NotYours");
                    return false;
                }
                return null;
            }

            // The gear panel of a managed horse: owner and team only, never while it is wild or asleep.
            public override object CanLootEntity(BasePlayer player, BaseEntity entity)
            {
                RidableHorse horse = entity as RidableHorse;
                HorseState s = horse != null ? StateOf(horse) : null;
                if (s == null || (Bm.Cfg.Riding.AdminBypass && Bm.IsAdmin(player)))
                {
                    return null;
                }
                TamedRecord rec = Bm.RecordOf(horse);
                if (rec == null || s.Asleep(Time.time) || !Bm.MayUse(player, rec.Owner, "team"))
                {
                    Bm.Tell(player, rec == null ? "HorseWild" : "HorseNoLoot");
                    return false;
                }
                return null;
            }

            public override object OnHorseLead(RidableHorse horse, BasePlayer player)
            {
                HorseState s = StateOf(horse);
                if (s == null)
                {
                    return null;
                }
                if (horse.IsLeading && horse.leadingPlayer == player)
                {
                    s.StopRequested = Time.time;        // this is the request to let go, not an accident
                    return null;
                }
                Driver formLed = Bm.DriverOfAnimal(horse);
                if (formLed != null && formLed.Kind == ControlKind.Form)
                {
                    return Bm.FormHandledBy(formLed, player) ? null : (object)false;       // the horse form is led by whom the server allows
                }
                TamedRecord rec = Bm.RecordOf(horse);
                if (s.Asleep(Time.time) || (rec == null && Bm.Cfg.Features.Taming) || (rec != null && !Bm.MayUse(player, rec.Owner, "team")))
                {
                    Bm.Tell(player, "HorseNoLead");
                    return false;
                }
                return null;
            }

            // A saddle in the equipment slot gives the horse its seats; taking it out takes them away again.
            public override void OnItemAddedToContainer(ItemContainer container, Item item, BasePlayer sourcePlayer)
            {
                RidableHorse horse = container != null ? container.entityOwner as RidableHorse : null;
                if (horse == null || container != horse.equipmentInventory || StateOf(horse) == null)
                {
                    return;
                }
                int seats = horse.GetSaddleItemSeatCount(item);
                if (seats > 0 && SeatsOf(horse) != seats)
                {
                    SetSeats(horse, seats);
                }
            }

            public override void OnItemRemovedFromContainer(ItemContainer container, Item item, BasePlayer sourcePlayer)
            {
                RidableHorse horse = container != null ? container.entityOwner as RidableHorse : null;
                if (horse == null || container != horse.equipmentInventory || StateOf(horse) == null)
                {
                    return;
                }
                if (horse.GetSaddleItemSeatCount(item) > 0)
                {
                    horse.DismountAllPlayers();
                    SetSeats(horse, 0);
                }
            }

            // ---------------------------------------------------------------- riding extras: jump and the rear-up attack

            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (States.Count == 0 || !player.isMounted)
                {
                    return null;
                }
                RidableHorse horse = player.GetMounted() != null ? HorseOfSeat(player.GetMounted()) : null;
                if (horse == null || horse.GetDriver() != player)
                {
                    return null;
                }
                HorseState s = StateOf(horse);
                if (s == null)
                {
                    return null;
                }
                if (Bm.Cfg.Horse.Jump && input.WasJustPressed(BUTTON.JUMP) && Time.time >= nextJump)
                {
                    nextJump = Time.time + Bm.Cfg.Movement.JumpCooldown;
                    Jump(horse);
                }
                return null;
            }

            private static void Jump(RidableHorse horse)
            {
                Rigidbody body = horse.GetComponent<Rigidbody>();
                if (body == null || body.isKinematic || horse.GetCurrentGaitSpeedFraction() < 0.01f && body.linearVelocity.sqrMagnitude < 1f)
                {
                    return;
                }
                RaycastHit hit;
                if (!Physics.Raycast(horse.transform.position + Vector3.up * 0.3f, Vector3.down, out hit, 0.9f, LayerMask.GetMask("Terrain", "World", "Construction"), QueryTriggerInteraction.Ignore))
                {
                    return;      // not on the ground
                }
                float h = Mathf.Clamp(Instance.Cfg.Movement.JumpHeight, 0.3f, 1.6f);
                float up = Mathf.Sqrt(2f * 9.81f * h);
                body.AddForce(Vector3.up * up, ForceMode.VelocityChange);
            }

            // F while riding a managed horse: rear up and strike forward (A14); the damage is the horse attack of the config.
            public override object OnServerCommand(BasePlayer player, ConsoleSystem.Arg arg)
            {
                if (States.Count == 0 || !player.isMounted || !Bm.Cfg.Features.Combat || arg.cmd.FullName != "inventory.lighttoggle")
                {
                    return null;
                }
                RidableHorse horse = player.GetMounted() != null ? HorseOfSeat(player.GetMounted()) : null;
                HorseState s = horse != null ? StateOf(horse) : null;
                if (s == null || horse.GetDriver() != player || !Bm.Cfg.Horse.RearUpAttack)
                {
                    return null;
                }
                float now = Time.time;
                if (now < s.NextAttack)
                {
                    return true;
                }
                s.NextAttack = now + Bm.Cfg.Horse.AttackCooldown;
                RpcGroup(horse, "CLIENT_Stand");
                Bm.timer.Once(0.8f, () =>
                {
                    if (Valid(horse) && !horse.IsDead())
                    {
                        HitFront(horse, Bm.Cfg.Horse.AttackDamage, null, true);
                    }
                });
                return true;
            }

            public override void OnEntityKill(BaseNetworkable entity)
            {
                RidableHorse horse = entity as RidableHorse;
                if (horse != null && States.Count > 0)
                {
                    HorseState s = StateOf(horse);
                    if (s != null)
                    {
                        States.Remove(horse.net.ID.Value);
                        if (Bm.RecordOf(horse) == null)
                        {
                            Bm.Data.Horses.Remove(s.Tag);
                        }
                    }
                }
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Horses: " + States.Count + " managed (" + States.Values.Count(x => x.Info.Temperament == "calm") + " calm, " + States.Values.Count(x => x.Info.Temperament == "shy") + " shy, " + States.Values.Count(x => x.Info.Temperament == "aggressive") + " aggressive); seat control " + (HorseSetSeats != null ? "OK" : "MISSING") + ", animation push " + (HorseUpdateClients != null && HorseThrottle != null ? "OK" : "MISSING"));
            }
        }

        // ---------------------------------------------------------------- glue for the other modules

        // E on a horse: a wild managed horse can be claimed once it knows the player.
        internal bool HorseUse(BasePlayer player, RidableHorse horse)
        {
            HorseState s = Stable != null ? Stable.StateOf(horse) : null;
            if (s == null || RecordOf(horse) != null)
            {
                return false;
            }
            return TryBossSaddle(player, horse) || Stable.Claim(player, s);
        }

        internal string HorseLabel(RidableHorse horse)
        {
            HorseState s = Stable != null ? Stable.StateOf(horse) : null;
            if (s == null)
            {
                return null;
            }
            TamedRecord rec = RecordOf(horse);
            return (rec != null ? "tamed" : s.Info.Calmed ? "calm" : s.Info.Temperament) + (s.Asleep(Time.time) ? ", asleep" : string.Empty);
        }

        // bm.horses list | wild | set <calm|shy|aggressive> | sleep
        private void HorseCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            switch (sub)
            {
                case "list":
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Managed horses: " + Stable.Count);
                        foreach (HorseState s in Stable.States.Values)
                        {
                            sb.AppendLine("  #" + s.Tag + " " + HorseLabel(s.Horse) + " seats " + SeatsOf(s.Horse) + " at " + s.Horse.transform.position + (RecordOf(s.Horse) != null ? " owner " + RecordOf(s.Horse).Owner : string.Empty));
                        }
                        reply(sb.ToString());
                        return;
                    }
                case "wild":
                    {
                        int n = 0;
                        foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                        {
                            RidableHorse horse = net as RidableHorse;
                            if (horse != null && horse.skinID == 0UL && horse.OwnerID == 0UL && !horse.IsForSale && !horse.AnyMounted() && Stable.StateOf(horse) == null)
                            {
                                if (Cfg.Horse.WildUnsaddled && SeatsOf(horse) > 0)
                                {
                                    SetSeats(horse, 0);
                                }
                                Stable.Register(horse);
                                n++;
                            }
                        }
                        reply(n + " idle horses are wild now (unsaddled, with a temperament).");
                        return;
                    }
                case "set":
                case "sleep":
                    {
                        if (player == null) { reply("Look at a horse in game."); return; }
                        RidableHorse horse = HorseInSight(player, 30f, 20f);
                        HorseState s = horse != null ? Stable.StateOf(horse) ?? Stable.Register(horse) : null;
                        if (s == null) { reply(L("NotLooking", null, 30)); return; }
                        if (sub == "sleep") { Stable.Sleep(s, player); return; }
                        string t = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
                        if (t != "calm" && t != "shy" && t != "aggressive") { reply("Usage: bm.horses set calm|shy|aggressive"); return; }
                        s.Info.Temperament = t;
                        s.Info.Calmed = false;
                        reply("This horse is " + t + " now.");
                        return;
                    }
            }
            reply("Usage: bm.horses list | wild | set calm|shy|aggressive | sleep");
        }

        // Headless test: a horse is born wild, moves, falls asleep, wakes calm, is tamed and saddled.
        internal void TestHorse(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            SpeciesDef species = SpeciesById("horse");
            if (species == null || !species.Available)
            {
                reply("The horse is not available in this build.");
                return;
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.5f;
            RidableHorse horse = GameManager.server.CreateEntity(species.Prefab, pos, Quaternion.identity) as RidableHorse;
            if (horse == null)
            {
                reply("FAIL the horse could not be created");
                return;
            }
            horse.Spawn();
            check("a fresh horse is saddled by the game", SeatsOf(horse) == 1);
            float savedSleep = Cfg.Horse.SleepSeconds;
            Cfg.Horse.SleepSeconds = 4f;
            timer.Once(3.0f, () =>
            {
                HorseState s = Stable.StateOf(horse);
                check("the horse became wild (managed)", s != null);
                check("a wild horse has no saddle", SeatsOf(horse) == 0);
                check("it has a temperament (" + (s != null ? s.Info.Temperament : "?") + ")", s != null && (s.Info.Temperament == "calm" || s.Info.Temperament == "shy" || s.Info.Temperament == "aggressive"));
                if (s == null)
                {
                    Cfg.Horse.SleepSeconds = savedSleep;
                    horse.Kill();
                    Puts("[test] horse:\n" + string.Join("\n", lines.ToArray()));
                    return;
                }
                Vector3 start = horse.transform.position;
                s.Drive.StopDistance = 1.5f;
                s.Drive.MoveTo(start + horse.transform.forward * 14f, 5f);
                timer.Once(6f, () =>
                {
                    float moved = Vector3.Distance(horse.transform.position, start);
                    check("a riderless horse can be driven (moved " + moved.ToString("0.0") + " m in 6 s)", moved > 7f);
                    s.Drive.Stop();
                    Stable.Sleep(s, null);
                    timer.Once(1.0f, () =>
                    {
                        check("a dart makes the horse fall (ragdoll flag)", horse.HasFlag(BaseEntity.Flags.Reserved12) || s.Asleep(Time.time));
                        timer.Once(10f, () =>
                        {
                            check("it wakes up after the sleep (calm for good)", !s.Asleep(Time.time) && s.Info.Calmed && !s.Frozen);
                            // taming and the saddle
                            TamedRecord rec = MakeTamed(horse, species, 76561198727638665UL, false);
                            TagIndex[rec.Tag] = horse;
                            check("a tamed horse is not wild any more", RecordOf(horse) != null);
                            Item saddle = ItemManager.CreateByName("horse.saddle.single", 1);
                            bool moved2 = saddle != null && saddle.MoveToContainer(horse.equipmentInventory);
                            check("the saddle goes into the equipment slot", moved2);
                            check("the saddle gives the horse its seat", SeatsOf(horse) == 1);
                            if (saddle != null && moved2)
                            {
                                saddle.RemoveFromContainer();
                                saddle.Remove();
                            }
                            check("taking the saddle out removes the seat", SeatsOf(horse) == 0);
                            Cfg.Horse.SleepSeconds = savedSleep;
                            Data.Tamed.Remove(rec.Tag);
                            TagIndex.Remove(rec.Tag);
                            Stable.Forget(horse);
                            Data.Horses.Remove(s.Tag);
                            horse.Kill();
                            Puts("[test] horse:\n" + string.Join("\n", lines.ToArray()));
                        });
                    });
                });
            });
            reply("Horse test started: the result comes to the log in ~25 s.");
        }
    }

    // ======================================== Interact.cs
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

    // ======================================== Lang.cs
    public partial class Beastmaster
    {
        // All player-facing text is English and goes through the Oxide lang system (oxide/lang/en/Beastmaster.json),
        // so a server owner can reword it without touching the code.
        private void RegisterMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You do not have permission for that.",
                ["PluginOff"] = "Beastmaster is switched off on this server.",
                ["Enabled"] = "Beastmaster is now ON.",
                ["Disabled"] = "Beastmaster is now OFF. Everyone was released.",
                ["NotLooking"] = "Look at an animal first (within {0} m).",
                ["UnknownSpecies"] = "Unknown species '{0}'. Known: {1}",
                ["SpeciesOff"] = "{0} is not available (disabled or not in this game build).",
                ["NeedTame"] = "This animal is not tamed. Tame it first (see Help - Taming).",
                ["NeedSaddle"] = "This animal has no saddle. Open its gear with E and fit one.",
                ["NotYours"] = "This animal belongs to someone else.",
                ["AlreadyControlling"] = "You are already riding or in animal form. Press X first.",
                ["Riding"] = "Riding the {0}. W/A/S/D move, Shift faster, Ctrl brake, Space jump, F attack, G view, X get off.",
                ["Form"] = "You are now a {0}. W/A/S/D move, Shift faster, Ctrl brake, Space jump, F attack, G view, B animal sounds, X back to human.",
                ["Released"] = "You are free again.",
                ["ViewThird"] = "View: third person (preset {0}). Move the mouse to look around: the camera stays where you leave it and turns with the animal.",
                ["ViewFirst"] = "View: first person. Your inventory opens in this view; G changes the view.",
                ["Brake"] = "Brake.",
                ["Stop"] = "Stop.",
                ["NoAttack"] = "This animal has no attack.",
                ["HitSummary"] = "Hit: {0}",
                ["Died"] = "Your animal died - and you with it.",
                ["Spawned"] = "Spawned {0}x {1}.",
                ["SpawnLimit"] = "Spawn limit reached ({0}).",
                ["SpawnCooldown"] = "Wait a moment before spawning again.",
                ["GrantOk"] = "Menu access granted to {0}.",
                ["RevokeOk"] = "Menu access removed from {0}.",
                ["PlayerNotFound"] = "Player '{0}' not found.",
                ["NoAnimalsNearby"] = "No animal of yours nearby.",
                ["CommandSet"] = "{0}: {1}.",
                ["Cleared"] = "Removed {0} plugin-spawned animals.",
                ["Reloaded"] = "Config reloaded.",
                ["RideHorseVanilla"] = "A horse is ridden with E on its own saddle.",
            }, this);
        }
    }

    // ======================================== Lead.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- leading (R6B, R9.27 - R9.29)
        // 1. The game's own rope of the livestock breaks at 7 m; while the plugin runs it breaks at Lead.BreakDistance (50 m).
        // 2. The horse's rope breaks at 7 m in the horse code: the horse then keeps following its leader on an invisible tether (HorseModule)
        //    and takes the rope again when it is back within reach.
        // 3. A player in animal form leads tamed animals of their own or their team in a line behind them (E at the animal; E again lets it go).

        private float savedLeadBreak = -1f;

        internal void LeadInit()
        {
            if (!Cfg.Features.Lead)
            {
                return;
            }
            if (savedLeadBreak < 0f)
            {
                savedLeadBreak = ConVar.Livestock.leadBreakDistance;
            }
            ConVar.Livestock.leadBreakDistance = Cfg.Lead.BreakDistance;
        }

        internal void LeadRestore()
        {
            if (savedLeadBreak >= 0f)
            {
                ConVar.Livestock.leadBreakDistance = savedLeadBreak;
                savedLeadBreak = -1f;
            }
        }

        internal LeadModule Leads;

        // A tamed animal of the player's own or team within reach of the animal's head.
        private BaseNPC2 LeadCandidate(Driver d)
        {
            Vector3 head = d.HeadPosition();
            var near = new List<BaseNPC2>();
            Vis.Entities(head, 4f, near);
            BaseNPC2 best = null;
            float bestDist = float.MaxValue;
            foreach (BaseNPC2 npc in near)
            {
                if (npc == null || npc == d.Body || npc.IsDestroyed || npc.IsDead() || DriverOfAnimal(npc) != null)
                {
                    continue;
                }
                TamedRecord rec = RecordOf(npc);
                if (rec == null || !MayUse(d.Player, rec.Owner, "team"))
                {
                    continue;
                }
                float dist = Vector3.Distance(npc.WorldSpaceBounds().ToBounds().ClosestPoint(head), head);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = npc;
                }
            }
            return best;
        }

        // E from the animal's head: lead the tamed animal in front of it, or let it go if it is led already.
        internal bool TryLeadToggle(Driver d)
        {
            if (!Cfg.Features.Lead || d.Kind != ControlKind.Form || Leads == null)
            {
                return false;
            }
            BaseNPC2 target = LeadCandidate(d);
            if (target == null)
            {
                return false;
            }
            SpeciesDef species = SpeciesOf(target);
            List<BaseNPC2> led = Leads.ChainOf(d);
            if (led.Contains(target))
            {
                Leads.Unlead(d, target, "released");
                Tell(d.Player, "LeadOff", species != null ? species.Display : "animal");
                return true;
            }
            if (led.Count >= Cfg.Lead.MaxChain)
            {
                Tell(d.Player, "LeadFull", Cfg.Lead.MaxChain);
                return true;
            }
            Leads.Lead(d, target);
            Tell(d.Player, "LeadOn", species != null ? species.Display : "animal", led.Count, Cfg.Lead.MaxChain);
            return true;
        }

        internal void EndChain(Driver d, string reason)
        {
            if (Leads != null)
            {
                Leads.EndChain(d, reason);
            }
        }

        internal sealed class LeadModule : Module
        {
            public override int Order => 55;

            private readonly Dictionary<ulong, List<BaseNPC2>> chains = new Dictionary<ulong, List<BaseNPC2>>();

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Leads = this;
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["LeadOn"] = "You lead the {0} behind you ({1}/{2}). Press E at it again to let it go.",
                    ["LeadOff"] = "The {0} is free again.",
                    ["LeadFull"] = "You can lead {0} animals at most.",
                    ["LeadBroke"] = "The {0} fell too far behind and is free again.",
                };
            }

            public override void OnServerInitialized()
            {
                Bm.LeadInit();
            }

            public override void OnEnable()
            {
                Bm.LeadInit();
            }

            public override void OnDisable()
            {
                EndAll("off");
                Bm.LeadRestore();
            }

            public override void Unload()
            {
                EndAll("unload");
                Bm.LeadRestore();
            }

            public List<BaseNPC2> ChainOf(Driver d)
            {
                List<BaseNPC2> list;
                if (!chains.TryGetValue(d.Player.userID, out list))
                {
                    list = new List<BaseNPC2>();
                    chains[d.Player.userID] = list;
                }
                return list;
            }

            public void Lead(Driver d, BaseNPC2 animal)
            {
                List<BaseNPC2> list = ChainOf(d);
                list.Add(animal);
                Follower f = Bm.StartFollower(animal, "led", null);
                if (f == null)
                {
                    list.Remove(animal);
                    return;
                }
                float spacing = Mathf.Max(3.5f, animal.bounds.extents.z * 2f + 1.5f);
                Driver driver = d;
                f.TeleportDistance = 0f;
                f.StopDistance = spacing;
                f.MatchLeader = true;
                f.LeaderSpeed = () => driver.Alive ? driver.Speed : 0f;
                f.Where = () =>
                {
                    List<BaseNPC2> chain;
                    if (!driver.Alive || !chains.TryGetValue(driver.Player.userID, out chain))
                    {
                        return null;
                    }
                    int i = chain.IndexOf(animal);
                    BaseCombatEntity ahead = i <= 0 ? (BaseCombatEntity)driver.Body : chain[i - 1];
                    return Valid(ahead) ? (Vector3?)ahead.transform.position : null;
                };
            }

            public void Unlead(Driver d, BaseNPC2 animal, string reason)
            {
                List<BaseNPC2> list;
                if (chains.TryGetValue(d.Player.userID, out list))
                {
                    list.Remove(animal);
                }
                Release(animal);
            }

            // The animal returns to the command it was under before (follow, wait, guard).
            private void Release(BaseNPC2 animal)
            {
                if (!Valid(animal))
                {
                    return;
                }
                Follower f = Bm.FollowerOf(animal);
                if (f != null && f.Reason == "led")
                {
                    Bm.StopFollower(animal);
                }
                TamedRecord rec = Bm.RecordOf(animal);
                if (rec != null)
                {
                    Bm.ApplyState(animal, rec);
                }
            }

            public void EndChain(Driver d, string reason)
            {
                List<BaseNPC2> list;
                if (d == null || d.Player == null || !chains.TryGetValue(d.Player.userID, out list))
                {
                    return;
                }
                chains.Remove(d.Player.userID);
                foreach (BaseNPC2 a in list.ToList())
                {
                    Release(a);
                }
            }

            private void EndAll(string reason)
            {
                foreach (List<BaseNPC2> list in chains.Values.ToList())
                {
                    foreach (BaseNPC2 a in list.ToList())
                    {
                        Release(a);
                    }
                }
                chains.Clear();
            }

            // Keeps the chains tidy and lets an animal go that fell too far behind (R9.27).
            public override void Tick(float now)
            {
                if (chains.Count == 0)
                {
                    return;
                }
                foreach (ulong id in chains.Keys.ToList())
                {
                    Driver d = Bm.Drivers.Values.FirstOrDefault(x => x.Player != null && x.Player.userID == id);
                    List<BaseNPC2> list = chains[id];
                    if (d == null || !d.Alive || !Valid(d.Body) || !Bm.Cfg.Features.Lead)
                    {
                        chains.Remove(id);
                        foreach (BaseNPC2 a in list.ToList()) Release(a);
                        continue;
                    }
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        BaseNPC2 a = list[i];
                        if (!Valid(a) || a.IsDead())
                        {
                            list.RemoveAt(i);
                            continue;
                        }
                        Follower f = Bm.FollowerOf(a);
                        if (f == null || f.Reason != "led")
                        {
                            list.RemoveAt(i);               // commanded or replaced by something else
                            continue;
                        }
                        BaseCombatEntity ahead = i == 0 ? (BaseCombatEntity)d.Body : list[i - 1];
                        if (Valid(ahead) && Vector3.Distance(ahead.transform.position, a.transform.position) > Bm.Cfg.Lead.BreakDistance)
                        {
                            SpeciesDef sp = SpeciesOf(a);
                            list.RemoveAt(i);
                            Release(a);
                            Bm.Tell(d.Player, "LeadBroke", sp != null ? sp.Display : "animal");
                        }
                    }
                }
            }

            // A led animal that is attacked is free: it reacts as a normal animal (R9.29).
            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                BaseNPC2 a = entity as BaseNPC2;
                if (a == null || chains.Count == 0 || info == null)
                {
                    return null;
                }
                foreach (KeyValuePair<ulong, List<BaseNPC2>> kv in chains.ToList())
                {
                    if (kv.Value.Contains(a) && info.InitiatorPlayer != null && info.InitiatorPlayer.userID != kv.Key)
                    {
                        kv.Value.Remove(a);
                        Release(a);
                    }
                }
                return null;
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Lead: " + chains.Values.Sum(l => l.Count) + " animals led; livestock rope breaks at " + (savedLeadBreakInfo()) + " m");
            }

            private static string savedLeadBreakInfo()
            {
                return ConVar.Livestock.leadBreakDistance.ToString("0");
            }
        }
    }

    // ======================================== Menu.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- the menu (R12)
        // A window of the game's own UI with two layers: the player layer (Home, Help, My animals, Controls, Transformation) for everybody
        // with beastmaster.use and the control layer (Admin, Spawn, Players) for owners, moderators and everyone granted beastmaster.menu.
        // The server cannot bind F5 on a client, so the menu opens with /bm or the console command bm.menu (bind f5 "bm.menu" in F1 works).

        private const string MenuRoot = "BeastmasterMenu";
        private static readonly Regex TagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        internal sealed class MenuState
        {
            public bool Open;
            public string Page = "home";
            public string Topic = "overview";
            public string Species = "cow";            // the species card of the Help topic and the Spawn page
            public string Note = string.Empty;        // what the player looked at ("a shy horse")
            public string Search = string.Empty;
            public int TextPage;
            public int ListPage;
            public ulong ConfirmRelease;
            public int SpawnCount = 1;
            public bool SpawnTamed;
            public bool SpawnSaddled;
            public bool SpawnMini;
            public ulong RenameTag;
        }

        internal readonly Dictionary<ulong, MenuState> MenuStates = new Dictionary<ulong, MenuState>();

        private MenuState MenuOf(BasePlayer player)
        {
            MenuState st;
            if (!MenuStates.TryGetValue(player.userID, out st))
            {
                st = new MenuState();
                MenuStates[player.userID] = st;
            }
            return st;
        }

        private static readonly string[][] MenuPages =
        {
            new[] { "home", "Home", "player" },
            new[] { "help", "Help / Guide", "player" },
            new[] { "animals", "My animals", "player" },
            new[] { "controls", "Controls", "player" },
            new[] { "ritual", "Transformation", "player" },
            new[] { "admin", "Admin", "control" },
            new[] { "spawn", "Spawn", "control" },
            new[] { "tools", "Aim tools", "control" },
            new[] { "players", "Players", "control" },
        };

        private static bool IsControlPage(string page)
        {
            return MenuPages.Any(p => p[0] == page && p[2] == "control");
        }

        internal void OpenMenu(BasePlayer player, string page)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }
            if (!Has(player, PermUse) || (!Cfg.Access.OpenForEveryone && !HasMenuAccess(player)))
            {
                Tell(player, "NoPermission");
                return;
            }
            MenuState st = MenuOf(player);
            if (st.Open && (string.IsNullOrEmpty(page) || page == "home"))
            {
                CloseMenu(player);                  // the menu command toggles
                return;
            }
            st.Open = true;
            if (!string.IsNullOrEmpty(page) && MenuPages.Any(p => p[0] == page) && (!IsControlPage(page) || HasMenuAccess(player)))
            {
                st.Page = page;
            }
            DrawMenu(player);
        }

        internal void CloseMenu(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }
            MenuState st;
            if (MenuStates.TryGetValue(player.userID, out st))
            {
                st.Open = false;
            }
            if (player.IsConnected)
            {
                CuiHelper.DestroyUi(player, MenuRoot);
            }
        }

        internal void CloseAllMenus()
        {
            foreach (BasePlayer p in BasePlayer.activePlayerList.ToList())
            {
                MenuState st;
                if (p != null && MenuStates.TryGetValue(p.userID, out st) && st.Open)
                {
                    CloseMenu(p);
                }
            }
        }

        // ---------------------------------------------------------------- small UI helpers

        private static string Num(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Pt(float x, float y)
        {
            return Num(x) + " " + Num(y);
        }

        private static string OnOff(bool on)
        {
            return on ? "<color=#8fd36b>on</color>" : "<color=#d36b6b>off</color>";
        }

        private static void UiPanel(CuiElementContainer c, string parent, string name, float x0, float y0, float x1, float y1, string color, bool cursor = false)
        {
            c.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = Pt(x0, y0), AnchorMax = Pt(x1, y1) }, CursorEnabled = cursor }, parent, name);
        }

        private static void UiLabel(CuiElementContainer c, string parent, string text, int size, float x0, float y0, float x1, float y1, TextAnchor align = TextAnchor.UpperLeft, string color = "0.92 0.93 0.88 1")
        {
            c.Add(new CuiLabel { Text = { Text = text, FontSize = size, Align = align, Color = color }, RectTransform = { AnchorMin = Pt(x0, y0), AnchorMax = Pt(x1, y1) } }, parent);
        }

        private static void UiButton(CuiElementContainer c, string parent, string text, string command, float x0, float y0, float x1, float y1, string color, int size = 13)
        {
            c.Add(new CuiButton
            {
                Button = { Command = command, Color = color },
                RectTransform = { AnchorMin = Pt(x0, y0), AnchorMax = Pt(x1, y1) },
                Text = { Text = text, FontSize = size, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, parent);
        }

        private const string ColNav = "0.20 0.24 0.16 1";
        private const string ColNavOn = "0.36 0.50 0.22 1";
        private const string ColBtn = "0.24 0.30 0.18 1";
        private const string ColOn = "0.30 0.48 0.20 1";
        private const string ColOff = "0.50 0.20 0.17 1";
        private const string ColWarn = "0.55 0.38 0.12 1";

        // ---------------------------------------------------------------- the window

        internal void DrawMenu(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }
            MenuState st = MenuOf(player);
            CuiElementContainer c = BuildMenu(player, st, HasMenuAccess(player));
            CuiHelper.DestroyUi(player, MenuRoot);
            CuiHelper.AddUi(player, c);
        }

        // The whole window as a container (kept apart from sending it so that it can be tested without a connected player).
        internal CuiElementContainer BuildMenu(BasePlayer player, MenuState st, bool control)
        {
            if (!control && IsControlPage(st.Page))
            {
                st.Page = "home";
            }
            var c = new CuiElementContainer();
            UiPanel(c, "Overlay", MenuRoot, 0.17f, 0.10f, 0.83f, 0.90f, "0.06 0.07 0.05 0.96", true);
            UiLabel(c, MenuRoot, "<b>Beastmaster</b>  <size=12><color=#9aa58f>" + Version + "</color></size>", 21, 0.02f, 0.925f, 0.7f, 0.995f, TextAnchor.MiddleLeft);
            UiButton(c, MenuRoot, "X", "bm.ui close", 0.95f, 0.935f, 0.99f, 0.99f, "0.55 0.15 0.12 1", 14);
            if (!Active)
            {
                // switched off: only the switch is offered
                UiLabel(c, MenuRoot, "<size=20><b>Beastmaster is OFF</b></size>\n\nNothing of it works while it is off: no riding, no taming, no animal forms, no commands.", 15, 0.05f, 0.45f, 0.95f, 0.85f, TextAnchor.UpperLeft);
                if (control)
                {
                    UiButton(c, MenuRoot, "Enable Beastmaster", "bm.ui plugin", 0.30f, 0.25f, 0.70f, 0.38f, ColOn, 20);
                }
                else
                {
                    UiLabel(c, MenuRoot, "<color=#d9b85a>Ask an admin to enable it (they need the permission beastmaster.menu).</color>", 15, 0.05f, 0.25f, 0.95f, 0.38f, TextAnchor.MiddleCenter);
                }
                return c;
            }
            float y = 0.86f;
            bool controlLabel = false;
            foreach (string[] p in MenuPages)
            {
                if (p[2] == "control")
                {
                    if (!control)
                    {
                        continue;
                    }
                    if (!controlLabel)
                    {
                        controlLabel = true;
                        UiLabel(c, MenuRoot, "<size=11><color=#9aa58f>CONTROL LAYER</color></size>", 11, 0.015f, y - 0.03f, 0.2f, y, TextAnchor.LowerLeft);
                        y -= 0.04f;
                    }
                }
                UiButton(c, MenuRoot, p[1], "bm.ui page " + p[0], 0.015f, y - 0.06f, 0.2f, y, st.Page == p[0] ? ColNavOn : ColNav, 14);
                y -= 0.07f;
            }
            string content = MenuRoot + ".content";
            UiPanel(c, MenuRoot, content, 0.215f, 0.025f, 0.99f, 0.915f, "0.11 0.12 0.09 0.92");
            switch (st.Page)
            {
                case "help": DrawHelp(c, content, player, st); break;
                case "animals": DrawAnimals(c, content, player, st); break;
                case "controls": DrawControls(c, content, player, st); break;
                case "ritual": DrawRitual(c, content, player, st); break;
                case "admin": DrawAdmin(c, content, player, st); break;
                case "spawn": DrawSpawn(c, content, player, st); break;
                case "tools": DrawTools(c, content, player, st); break;
                case "players": DrawPlayers(c, content, player, st); break;
                default: DrawHome(c, content, player, st); break;
            }
            return c;
        }

        private string FeatureLine()
        {
            var sb = new StringBuilder();
            foreach (FieldInfo f in typeof(FeatureCfg).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(bool))
                {
                    sb.Append(Humanise(f.Name)).Append(' ').Append(OnOff((bool)f.GetValue(Cfg.Features))).Append("   ");
                }
            }
            return sb.ToString();
        }

        private static string Humanise(string name)
        {
            return Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        }

        // ---------------------------------------------------------------- Home

        private void DrawHome(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            Driver d = DriverOf(player);
            var sb = new StringBuilder();
            sb.Append("<size=20><b>Welcome, ").Append(player.displayName).Append("</b></size>\n\n");
            sb.Append("Beastmaster is ").Append(OnOff(Active)).Append(".\n");
            sb.Append(d == null ? "You are on foot." : d.Kind == ControlKind.Ride ? "You are riding a " + d.Species.Display + "." : "You are in the form of a " + d.Species.Display + ".").Append('\n');
            sb.Append("Your animals: ").Append(OwnedCount(player.userID)).Append(" of ").Append(Cfg.Taming.MaxOwned).Append(".\n");
            sb.Append("Menu layer: ").Append(HasMenuAccess(player) ? "player + control (Admin, Spawn, Aim tools, Players)" : "player").Append("   |   open it with F5 or /bm\n\n");
            sb.Append("<b>Features on this server</b>\n").Append(FeatureLine()).Append("\n\n");
            sb.Append("<b>Where to start</b>\n");
            sb.Append("Help / Guide explains everything, generated from this server's settings.\n");
            sb.Append("My animals lists your tamed animals and lets you command them.\n");
            sb.Append("Controls holds your own camera and view settings.\n");
            sb.Append("Transformation tells how to become an animal and shows your progress.\n\n");
            sb.Append("<color=#9aa58f>Aim at an animal and press the button below to get its species card.</color>");
            UiLabel(c, parent, sb.ToString(), 14, 0.03f, 0.2f, 0.97f, 0.97f);
            if (HasMenuAccess(player))
            {
                UiButton(c, parent, "Disable Beastmaster", "bm.ui plugin", 0.66f, 0.90f, 0.97f, 0.98f, ColOff, 15);
            }
            UiButton(c, parent, "Help for what I'm looking at", "bm.ui lookhelp", 0.03f, 0.06f, 0.38f, 0.15f, ColBtn);
            UiButton(c, parent, "Open the Help", "bm.ui page help", 0.40f, 0.06f, 0.62f, 0.15f, ColBtn);
            if (d != null)
            {
                UiButton(c, parent, d.Kind == ControlKind.Ride ? "Get off" : "Back to human", "bm.ui revert", 0.64f, 0.06f, 0.88f, 0.15f, ColWarn);
            }
        }

        // ---------------------------------------------------------------- Help

        internal sealed class HelpTopic
        {
            public string Id;
            public string Title;
            public Func<BasePlayer, string> Text;
        }

        private List<HelpTopic> HelpTopics()
        {
            var list = new List<HelpTopic>();
            Action<string, string, Func<BasePlayer, string>> add = (id, title, text) => list.Add(new HelpTopic { Id = id, Title = title, Text = text });
            add("overview", "Overview", HelpOverview);
            add("controls", "Controls (all keys)", HelpControls);
            add("riding", "Riding", HelpRiding);
            add("gear", "Gear and horseshoes", HelpGear);
            add("taming", "Taming", HelpTaming);
            add("commands", "Commanding animals", HelpCommands);
            add("combat", "Combat and rear attacks", HelpCombat);
            add("ritual", "Transformation ritual", HelpRitual);
            add("camera", "Third person", HelpCamera);
            add("nightvision", "Night vision", p => "Night vision goggles worn by your human body keep their vision effect while you are an animal: the effect belongs to the human body, which is only hidden, not removed.\n\nBelt items, clothes and the backpack stay available in animal form - open the inventory as usual. Held items are never visible on the animal.");
            add("spray", "Spray can", HelpSpray);
            add("relations", "Animal relations", HelpRelations);
            add("species", "Species cards", p =>
            {
                MenuState st = MenuOf(p);
                SpeciesDef sp = SpeciesById(st.Species) ?? SpeciesById("cow");
                return (string.IsNullOrEmpty(st.Note) ? string.Empty : "<color=#e6b84a>" + st.Note + "</color>\n\n") + SpeciesCard(sp);
            });
            add("permissions", "Permissions and commands", HelpPermissions);
            add("faq", "FAQ / troubleshooting", HelpFaq);
            return list;
        }

        private string HelpOverview(BasePlayer p)
        {
            return "<b>Beastmaster " + Version + "</b> - animals as mounts, companions and forms.\n\n"
                + "Tame animals, fit saddles and gear, command them and ride them. With the ritual of transformation you can become an animal yourself.\n\n"
                + "<b>Quick start</b>\n"
                + "1. Tame an animal (Help - Taming).\n"
                + "2. Press E on it: the gear panel opens. Put a saddle into slot 1 and close the panel.\n"
                + "3. Press E again to mount. W/A/S/D move, Shift faster, F attack, G changes the view, X gets you off.\n"
                + "4. Command it: /bm cmd follow | wait | guard | attack | release.\n\n"
                + "<b>Switched on here</b>\n" + FeatureLine() + "\n\n"
                + "Open this menu any time with /bm.";
        }

        private string HelpControls(BasePlayer p)
        {
            return "<b>Keys while riding or in animal form</b>\n"
                + "W/A/S/D  move (turning follows the camera when you look around)\n"
                + "Shift  go faster - each press of the gait key climbs one step: walk, jog, run, sprint, full sprint\n"
                + "Ctrl  brake (hold with A or D for the stop-and-turn)\n"
                + "S while running  turn around 180 degrees (a slide for bulls and rams)\n"
                + "Space  jump (" + Cfg.Movement.JumpDistance + " m far, " + Cfg.Movement.JumpHeight + " m high, cooldown " + Cfg.Movement.JumpCooldown + " s)\n"
                + "F  attack - look backwards first for the rear attack\n"
                + "G or R  change view (third / first person)\n"
                + "B  gesture wheel: lie down (thumbs down, surrender), sit (ok, clap), roar (wave, victory, thumbs up), look around (shrug and the rest) - /bm lie, sit, roar, look, stand do the same\n"
                + "E  use: open doors, loot containers, eat what lies in front of you, drink from fresh water, lead tamed animals\n"
                + "Ctrl + G  night vision (put goggles from your inventory on / off) - also the Night button in Controls\n"
                + "Ctrl + R  this menu (empty hands), also F5 and H after the one-time bind\n"
                + "X  get off the animal / return to human form\n\n"
                + "<color=#9aa58f>The server cannot rebind keys by itself; it asks your game to bind F5 and H when you join. If yours refuses, type bind f5 bm.menu and bind h bm.nv in the F1 console.</color>";
        }

        private string HelpRiding(BasePlayer p)
        {
            return "<b>Riding</b> " + OnOff(Cfg.Features.Riding) + "\n\n"
                + "Who: " + (Cfg.Riding.RequireTame ? "tamed animals" : "any animal") + (Cfg.Riding.RequireSaddle ? " with a saddle" : string.Empty) + ". Owner may ride; " + OwnershipText() + ".\n"
                + "How: look at the animal within " + Cfg.Riding.MountDistance + " m and press E. A saddle fitted in the gear panel (E, or crouch + E when it already has one) is needed first.\n"
                + "A double saddle carries a second rider behind you.\n"
                + "Chicken, crocodile, snake, rabbit, squirrel and frog are restricted species (permission beastmaster.ride.restricted).\n\n"
                + "<b>Good to know</b>\n"
                + "Deep water slows animals like the horse (" + Mathf.RoundToInt(Cfg.Movement.WaterMinSpeed * 100f) + " % at the deepest wading); bears and crocodiles are exempt.\n"
                + "Slopes steeper than " + Cfg.Movement.SlopeLimit + " degrees can make a running animal fall.\n"
                + "Camera: see the Third person topic. The animal's own sounds and animations are played for everything it does.";
        }

        private string OwnershipText()
        {
            switch ((Cfg.Riding.Ownership ?? "team").ToLowerInvariant())
            {
                case "owner": return "nobody else";
                case "anyone": return "anyone else";
                case "friends": return "friends too";
                case "clan": return "clan members too";
                default: return "team members too";
            }
        }

        private string HelpGear(BasePlayer p)
        {
            return "<b>Gear</b> " + OnOff(Cfg.Features.Gear) + "\n\n"
                + "Press E on your animal (crouch + E if it already has a saddle) to open its gear panel:\n"
                + "slot 1  saddle (single or double)\nslot 2  horseshoes\nslot 3  saddle bag\nother slots  the bag's contents (" + Cfg.Gear.BagSlots + " slots)\n"
                + "Close the panel to save.\n\n"
                + "<b>Speed</b>\nBasic horseshoes +" + Cfg.Movement.ShoesBasic + " m/s, high quality horseshoes +" + Cfg.Movement.ShoesAdvanced + " m/s, the saddle bag " + Cfg.Movement.SaddleBag + " m/s.\n"
                + (Cfg.Gear.BagPrivate ? "Only the owner and the team can open the bag.\n" : "Anyone can open the bag.\n")
                + "Gear is kept with the animal and survives restarts.";
        }

        private string HelpTaming(BasePlayer p)
        {
            return "<b>Taming</b> " + OnOff(Cfg.Features.Taming) + "   (you can own " + Cfg.Taming.MaxOwned + " animals)\n\n"
                + "<b>Bond - horse, cow, bull, sheep, ram</b>\nStay near it and be gentle: it needs " + Cfg.Taming.BondSeconds + " s of familiarity. Then press E on it to claim it. It trusts someone else more? Wait until the other player leaves.\n\n"
                + "<b>Torch - wolf</b>\nChase the wolf with a lit torch for " + Cfg.Taming.WolfChaseSeconds + " s (within " + Cfg.Taming.TorchRange + " m). It stops fleeing, howls and follows you.\n\n"
                + "<b>Predators - bears, boar, panther, tiger</b>\nPhase 1: hit it with an Incapacitate Dart from a Blowpipe - slowed by " + Mathf.RoundToInt(Cfg.Taming.DartSlow * 100f) + " % for " + Cfg.Taming.DartSlowSeconds + " s.\n"
                + "Phase 2: let it run into a Bear Trap (" + Cfg.Taming.TrapDamage + " damage, it is held).\nPhase 3: use " + Cfg.Taming.SyringesNeeded + " Medical Syringes on it (E). It calms down, steps out and follows you after " + Cfg.Taming.NeutralSeconds + " s.\n\n"
                + "<b>Deer - stag</b>\nIncapacitate Dart: it sleeps " + Cfg.Taming.SleepSeconds + " s. Press E, put " + Cfg.Taming.FoodNeeded + " " + Cfg.Taming.StagFood + " in the first slots (eaten within " + Cfg.Taming.FoodSeconds + " s), then a saddle in the last slot.\n\n"
                + "<b>Others</b>\nCalf, lamb, chicken, crocodile, rabbit, squirrel, frog and crab have no taming flow. Use the transformation ritual or an admin.";
        }

        private string HelpCommands(BasePlayer p)
        {
            return "<b>Commanding tamed animals</b> " + OnOff(Cfg.Features.Commands) + "\n\n"
                + "follow  stays behind you (" + Cfg.Taming.FollowDistance + " m)\nwait  stands still\nguard  stays within " + Cfg.Taming.GuardRadius + " m and fights what threatens you\nattack  goes for what you aim at (or what threatens you)\nrelease  sets it free\n\n"
                + "<b>How</b>\n"
                + "Chat or console: /bm cmd follow  (aim at an animal's head for just that one; otherwise all animals within " + Cfg.Taming.CommandRange + " m)\n"
                + "The My animals page of this menu.\n"
                + "A Bone in hand: primary use gives the command, secondary use picks the next one.\n"
                + (Cfg.Taming.GestureCommands ? "Gestures on foot: ok = wait, wave = follow, thumbs up = guard, point = attack.\n" : "Gesture commands are switched off here.\n")
                + "\nYour team members are never attacked by your animals.";
        }

        private string HelpCombat(BasePlayer p)
        {
            return "<b>Combat</b> " + OnOff(Cfg.Features.Combat) + "\n\n"
                + "F attacks with the animal's own attacks, cycled in order, with the damage the game gives that animal (x" + Cfg.Combat.DamageScale + "). Cooldown " + Cfg.Combat.Cooldown + " s.\n"
                + "Look backwards more than " + Cfg.Combat.RearAngle + " degrees before pressing F: cows, bulls, sheep and rams kick behind them (rear attack).\n"
                + (Cfg.Combat.AutoAim ? "The attack turns the animal toward the nearest target in front of it.\n" : string.Empty)
                + "Attacks hurt: players " + OnOff(Cfg.Combat.HitPlayers) + ", animals " + OnOff(Cfg.Combat.HitAnimals) + ", buildings " + OnOff(Cfg.Combat.HitStructures) + ".\n"
                + (Cfg.Combat.Gather ? "Attacks also gather from trees, ore and corpses.\n" : string.Empty)
                + "Bulls and rams break wooden barricades when sprinting into them.\n"
                + "Wild animals you or your animal hurt run away; " + string.Join(", ", Cfg.Wild.AlwaysDefend.ToArray()) + " always fight back, the others do it with a " + Cfg.Wild.DefendChance + " % chance.\n"
                + "Bulls, rams, cows, sheep, stags, boars and horses that run into a player hurt and knock them down; into an animal they hurt it.";
        }

        private string HelpRitual(BasePlayer p)
        {
            return "<b>Ritual of transformation</b> " + OnOff(Cfg.Features.Transformation && Cfg.Features.AnimalForm) + "\n\n"
                + "1. <b>Kill</b> a wild animal of the kind you want to become (the body stays " + Mathf.RoundToInt(Cfg.Ritual.CorpseKeepSeconds / 60f) + " min and cannot be harvested).\n"
                + "2. <b>Circle</b> the body " + Cfg.Ritual.Circles + " times (" + Cfg.Ritual.BossCircles + " for the body of a boss) with a lit torch, closer than " + (Cfg.Ritual.Radius + 2.5f) + " m. After the last circle a low ring of fire burns round the body.\n"
                + "3. <b>Sacrifice</b>: within " + Cfg.Ritual.BurnSeconds + " s press E at the fire - or it goes out and takes the body. The fire burns whoever stands in it. It costs " + Mathf.RoundToInt(Cfg.Ritual.HealthCost * 100f) + " % of your current health and you bleed.\n"
                + "4. You are reborn as that animal, with its look and full health. Your human body is hidden and cannot be hurt.\n\n"
                + "<b>As the animal</b>\nYour health is the animal's health: if it dies, you die. Press X to return (smoke appears, then a " + Cfg.Form.Cooldown + " s cooldown). "
                + "Animals and turrets ignore you. You can open doors, loot, build, craft and use items (invisibly). Logging out makes the animal sleep where it stands.\n"
                + (Cfg.Ritual.RequireOwnKill ? "The body has to be of an animal you or your team killed.\n" : string.Empty)
                + (Cfg.Ritual.BlockInSafeZone ? "No ritual in safe zones.\n" : string.Empty)
                + "Restricted species (chicken, crocodile, rabbit, squirrel, frog) need the permission beastmaster.transform.restricted.";
        }

        private string HelpCamera(BasePlayer p)
        {
            return "<b>Third person</b> " + OnOff(Cfg.Features.ThirdPerson) + "\n\n"
                + "Default: " + Cfg.Camera.DefaultView + " person as an animal, " + Cfg.Camera.DefaultRideView + " person when riding. G (or R) switches.\n"
                + "Look around with the mouse; the camera stays where you put it and turns with the animal.\n"
                + "Turn the camera behind you to see the animal from the front.\n\n"
                + "<b>Presets</b> (Controls page, or /bm preset a|b|c|d)\n"
                + "A  spectator camera around the animal (default)\nB  A plus the game's own third-person flag\nC  camera at the animal's head\nD  first person from the animal's head\n\n"
                + "In third person, interactions (doors, loot) start from the animal's head; in first person they start from the crosshair.";
        }

        private string HelpSpray(BasePlayer p)
        {
            return "<b>Spray can</b> " + OnOff(Cfg.Features.Spray) + "\n\n"
                + "Spray one of your animals with a Spray Can to roll its look again: the horse's breed, the livestock's coat and wool tint. Only the looks change - the gameplay genes (milk, wool, speed) are never touched.\n"
                + (Cfg.Spray.OwnerOnly ? "Only the owner and the team can re-roll an animal.\n" : "Anyone can re-roll an animal.\n")
                + "A re-rolled look is the look the transformation ritual copies later.";
        }

        private string HelpRelations(BasePlayer p)
        {
            return "<b>Animal relations</b> " + OnOff(Cfg.Features.Relations) + "\n\n"
                + "They apply when you are an animal.\n"
                + "A deer near a bear: the bear attacks. A bear near another bear: they tolerate each other, but a bear and a polar bear are enemies.\n"
                + "You are a bull: cows follow you. You are a ram: sheep follow you.\n"
                + "You are a wolf: other wolves keep within " + Cfg.Lead.BreakDistance + " m around you.\n"
                + "You are a bull or a ram: other bulls and rams fight you.\n"
                + "Everything else is the game's own behaviour. The matrix is in the config (Relations).";
        }

        private string HelpPermissions(BasePlayer p)
        {
            var sb = new StringBuilder();
            sb.Append("<b>Permissions</b>  (green = you have it)\n");
            foreach (string[] row in new[]
            {
                new[] { PermUse, "menu, own animals, commands, gear" }, new[] { PermRide, "ride the standard species" }, new[] { PermRideRestricted, "ride chicken, crocodile, snake, rabbit, squirrel, frog" },
                new[] { PermTame, "tame animals" }, new[] { PermTransform, "transformation ritual" }, new[] { PermTransformRestricted, "ritual for the restricted species" },
                new[] { PermSpray, "re-roll animals with the spray can" }, new[] { PermMenu, "control layer and spawning" }, new[] { PermAdmin, "everything" }
            })
            {
                sb.Append(Has(p, row[0]) ? "<color=#8fd36b>" : "<color=#d36b6b>").Append(row[0]).Append("</color>  ").Append(row[1]).Append('\n');
            }
            sb.Append("\n<b>Chat commands</b>\n/bm  menu    /bm help    /bm species    /bm keys    /bm view    /bm preset a|b|c|d    /bm revert\n/bm cmd follow|wait|guard|attack|release    /bm ride    /bm form (aimed animal)\n");
            sb.Append("\n<b>Server console / RCON</b>\nbm.enable  bm.disable  bm.grant <player>  bm.revoke <player>  bm.access  bm.grantgroup <group>  bm.revokegroup <group>\nbm.spawn <species> [count] [tamed|wild] [saddled] [near] [player]  bm.clear [radius]  bm.reload  bm.selftest  bm.debug <level>  bm.prefabs <words>");
            return sb.ToString();
        }

        private string HelpFaq(BasePlayer p)
        {
            return "<b>FAQ / troubleshooting</b>\n\n"
                + "<b>I cannot mount.</b> The animal must be tamed, fitted with a saddle and within " + Cfg.Riding.MountDistance + " m; you need the permission beastmaster.ride.\n\n"
                + "<b>The animal does not follow.</b> Its state may be wait or guard: /bm cmd follow. Followers further than " + (Cfg.Taming.TeleportDistance > 0f ? Cfg.Taming.TeleportDistance + " m are moved next to you." : "that stay behind.") + "\n\n"
                + "<b>The camera feels wrong.</b> Try another preset: /bm preset b (or c, d). G switches between third and first person.\n\n"
                + "<b>The animal has no attack.</b> Rabbits, squirrels, frogs and crabs have no attack; F plays their call.\n\n"
                + "<b>How do I get back?</b> X returns you to human form. If you logged out as an animal, you wake up as the animal.\n\n"
                + "<b>F5 does nothing.</b> The server asks your game to bind F5 when you join. If it refused, type bind f5 bm.menu once in the F1 console, or use /bm.\n\n"
                + "<b>Something is broken.</b> Tell an admin: they can run bm.selftest and send you the result.";
        }

        // One species in a card: speeds, how to ride and tame, traits.
        private string SpeciesCard(SpeciesDef s)
        {
            if (s == null)
            {
                return "Pick a species.";
            }
            var sb = new StringBuilder();
            sb.Append("<size=17><b>").Append(s.Display).Append("</b></size>  ").Append(SpeciesEnabled(s) ? OnOff(true) : "<color=#d36b6b>not available</color>").Append("\n\n");
            if (!s.Available)
            {
                sb.Append(s.Note).Append('\n');
                return sb.ToString();
            }
            float scale = SpeedScaleOf(s);
            sb.Append("<b>Speed</b>  walk ").Append(Num(s.Walk * scale)).Append(" / sprint ").Append(Num(s.Sprint * scale)).Append(" / top ").Append(Num(s.Top * scale)).Append(" m/s\n");
            sb.Append("<b>Riding</b>  ").Append(s.Ride == RideTier.Standard ? "yes (beastmaster.ride)" : s.Ride == RideTier.Restricted ? "restricted (beastmaster.ride.restricted)" : "not rideable").Append("   <b>Gear</b>  ").Append(s.Gear ? "saddle, shoes, bag" : "none").Append('\n');
            sb.Append("<b>Taming</b>  ").Append(TamingOf(s)).Append('\n');
            sb.Append("<b>Transformation</b>  ").Append(s.Transform ? (s.TransformRestricted ? "yes (beastmaster.transform.restricted)" : "yes (beastmaster.transform)") : s.Id == "horse" ? "no - the horse form is not part of this build" : "no").Append('\n');
            sb.Append("<b>Diet</b>  ").Append(s.Food).Append(s.Predator ? ", predator" : string.Empty).Append(s.Swimmer ? ", swims" : string.Empty).Append(s.DrownProof ? ", cannot drown" : string.Empty).Append('\n');
            if (!string.IsNullOrEmpty(s.Trait))
            {
                sb.Append("<b>Trait</b>  ").Append(s.Trait).Append('\n');
            }
            if (s.Id == "bull" || s.Id == "ram" || s.Id == "stag")
            {
                sb.Append("Always defends itself when attacked.\n");
            }
            return sb.ToString();
        }

        private string TamingOf(SpeciesDef s)
        {
            switch (s.Tame)
            {
                case TameFlow.Bond: return "bond - stay near it for " + Cfg.Taming.BondSeconds + " s, then E";
                case TameFlow.Torch: return "chase it with a lit torch for " + Cfg.Taming.WolfChaseSeconds + " s";
                case TameFlow.Predator: return "dart, bear trap, " + Cfg.Taming.SyringesNeeded + " syringes";
                case TameFlow.SleepFeed: return "dart, then " + Cfg.Taming.FoodNeeded + " " + Cfg.Taming.StagFood + " and a saddle";
                case TameFlow.Admin: return "not by players (admin spawn or the ritual)";
                default: return "cannot be tamed";
            }
        }

        private void DrawHelp(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            List<HelpTopic> all = HelpTopics();
            string needle = (st.Search ?? string.Empty).ToLowerInvariant();
            List<HelpTopic> shown = string.IsNullOrEmpty(needle)
                ? all
                : all.Where(t => t.Title.ToLowerInvariant().Contains(needle) || TagRegex.Replace(t.Text(player), string.Empty).ToLowerInvariant().Contains(needle)).ToList();
            // search box
            UiPanel(c, parent, parent + ".sbox", 0.015f, 0.925f, 0.285f, 0.985f, "0.05 0.06 0.04 1");
            c.Add(new CuiElement
            {
                Parent = parent + ".sbox",
                Components =
                {
                    new CuiInputFieldComponent { Text = st.Search ?? string.Empty, FontSize = 13, Align = TextAnchor.MiddleLeft, Command = "bm.ui search", CharsLimit = 30, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.04 0", AnchorMax = "0.96 1" }
                }
            });
            if (string.IsNullOrEmpty(st.Search))
            {
                UiLabel(c, parent + ".sbox", "<color=#6f7a66>Search the Help ...</color>", 12, 0.04f, 0f, 0.8f, 1f, TextAnchor.MiddleLeft);
            }
            else
            {
                UiButton(c, parent, "x", "bm.ui clearsearch", 0.25f, 0.93f, 0.28f, 0.98f, ColOff, 11);
            }
            float y = 0.91f;
            foreach (HelpTopic t in shown)
            {
                UiButton(c, parent, t.Title, "bm.ui topic " + t.Id, 0.015f, y - 0.05f, 0.285f, y, st.Topic == t.Id ? ColNavOn : ColNav, 12);
                y -= 0.056f;
            }
            if (shown.Count == 0)
            {
                UiLabel(c, parent, "<color=#9aa58f>No topic matches.</color>", 13, 0.02f, 0.80f, 0.28f, 0.88f);
                return;
            }
            HelpTopic topic = shown.FirstOrDefault(t => t.Id == st.Topic) ?? shown[0];
            if (topic.Id != st.Topic)
            {
                st.Topic = topic.Id;
                st.TextPage = 0;
            }
            float top = 0.985f;
            int rows = 22;
            if (topic.Id == "species")
            {
                var species = AllSpecies.Where(s => s.Available).ToList();
                for (int i = 0; i < species.Count; i++)
                {
                    int col = i % 7, row = i / 7;
                    float x0 = 0.31f + col * 0.097f, y1 = 0.985f - row * 0.055f;
                    UiButton(c, parent, species[i].Display, "bm.ui species " + species[i].Id, x0, y1 - 0.05f, x0 + 0.092f, y1, st.Species == species[i].Id ? ColNavOn : ColNav, 11);
                }
                top = 0.985f - ((species.Count + 6) / 7) * 0.055f - 0.01f;
                rows = 15;
            }
            List<string> pages = PageText(topic.Text(player), rows, 78);
            st.TextPage = Mathf.Clamp(st.TextPage, 0, pages.Count - 1);
            UiLabel(c, parent, pages[st.TextPage], 13, 0.31f, 0.09f, 0.985f, top);
            if (pages.Count > 1)
            {
                UiButton(c, parent, "<", "bm.ui tpage -1", 0.31f, 0.02f, 0.37f, 0.08f, ColBtn, 14);
                UiLabel(c, parent, "<color=#9aa58f>page " + (st.TextPage + 1) + " / " + pages.Count + "</color>", 12, 0.38f, 0.02f, 0.62f, 0.08f, TextAnchor.MiddleCenter);
                UiButton(c, parent, ">", "bm.ui tpage 1", 0.63f, 0.02f, 0.69f, 0.08f, ColBtn, 14);
            }
            UiButton(c, parent, "What I'm looking at", "bm.ui lookhelp", 0.76f, 0.02f, 0.985f, 0.08f, ColBtn, 12);
        }

        // Splits a text into pages of about the given number of rows (a long line counts as several).
        private static List<string> PageText(string text, int rowsPerPage, int charsPerRow)
        {
            var pages = new List<string>();
            var cur = new StringBuilder();
            int rows = 0;
            foreach (string line in text.Replace("\r", string.Empty).Split('\n'))
            {
                int visible = TagRegex.Replace(line, string.Empty).Length;
                int n = Mathf.Max(1, Mathf.CeilToInt(visible / (float)charsPerRow));
                if (rows + n > rowsPerPage && rows > 0)
                {
                    pages.Add(cur.ToString().TrimEnd('\n'));
                    cur.Length = 0;
                    rows = 0;
                }
                cur.Append(line).Append('\n');
                rows += n;
            }
            if (cur.Length > 0)
            {
                pages.Add(cur.ToString().TrimEnd('\n'));
            }
            if (pages.Count == 0)
            {
                pages.Add(string.Empty);
            }
            return pages;
        }

        // The species of the animal the player looks at (a Gen2 animal or a horse).
        private SpeciesDef SpeciesInSight(BasePlayer player, out string note)
        {
            note = string.Empty;
            BaseNPC2 npc = AnimalInSight(player, 40f, 20f);
            if (npc != null)
            {
                SpeciesDef sp = SpeciesOf(npc);
                TamedRecord rec = RecordOf(npc);
                note = rec != null ? "This animal is tamed" + (rec.Owner == player.userID ? " - it is yours." : ".") : "This animal is wild.";
                return sp;
            }
            RidableHorse horse = HorseInSight(player, 40f, 20f);
            if (horse != null)
            {
                string label = HorseLabel(horse);
                note = label == null ? "This is an ordinary horse." : "You look at a " + label + " horse.";
                return SpeciesOf(horse);
            }
            return null;
        }

        // ---------------------------------------------------------------- My animals

        private void DrawAnimals(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            List<TamedRecord> mine = Data.Tamed.Values.Where(r => r.Owner == player.userID).OrderBy(r => r.Species).ThenBy(r => r.Tag).ToList();
            UiLabel(c, parent, "<size=18><b>My animals</b></size>   " + mine.Count + " of " + Cfg.Taming.MaxOwned, 14, 0.02f, 0.91f, 0.7f, 0.99f, TextAnchor.MiddleLeft);
            UiLabel(c, parent, "All:", 12, 0.62f, 0.93f, 0.66f, 0.985f, TextAnchor.MiddleRight);
            string[] states = { "follow", "wait", "guard", "attack" };
            for (int i = 0; i < states.Length; i++)
            {
                UiButton(c, parent, states[i], "bm.ui all " + states[i], 0.67f + i * 0.08f, 0.93f, 0.745f + i * 0.08f, 0.985f, ColBtn, 11);
            }
            int perPage = 8;
            int pages = Mathf.Max(1, (mine.Count + perPage - 1) / perPage);
            st.ListPage = Mathf.Clamp(st.ListPage, 0, pages - 1);
            if (mine.Count == 0)
            {
                UiLabel(c, parent, "You do not own any animal yet.\n\nTame one - see Help - Taming. Tamed animals follow you, can wear a saddle and can be commanded here.", 14, 0.03f, 0.55f, 0.97f, 0.88f);
            }
            Vector3 here = player.transform.position;
            for (int i = 0; i < perPage; i++)
            {
                int index = st.ListPage * perPage + i;
                if (index >= mine.Count)
                {
                    break;
                }
                TamedRecord rec = mine[index];
                SpeciesDef sp = SpeciesById(rec.Species);
                BaseEntity e = EntityOfRecord(rec);
                float y1 = 0.89f - i * 0.105f;
                UiPanel(c, parent, parent + ".row" + i, 0.015f, y1 - 0.095f, 0.985f, y1, "0.17 0.19 0.14 0.9");
                string name = (sp != null ? sp.Display : rec.Species) + (string.IsNullOrEmpty(rec.Name) ? string.Empty : " '" + rec.Name + "'") + (rec.Boss ? " <color=#e6b84a>[boss]</color>" : string.Empty);
                BaseCombatEntity living = e as BaseCombatEntity;
                string where = e != null ? Mathf.RoundToInt(Vector3.Distance(here, e.transform.position)) + " m away" : "not loaded";
                string hp = living != null ? Mathf.RoundToInt(living.health) + " HP" : Mathf.RoundToInt(rec.Health) + " HP";
                string gear = rec.Gear.Saddle > 0 ? (rec.Gear.Saddle > 1 ? "double saddle" : "saddle") : "no saddle";
                UiLabel(c, parent + ".row" + i, "<b>" + name + "</b>\n<size=11><color=#b4bfa8>" + rec.State + "  |  " + where + "  |  " + hp + "  |  " + gear + "</color></size>", 13, 0.015f, 0f, 0.5f, 1f, TextAnchor.MiddleLeft);
                string[] cmds = { "follow", "wait", "guard", "name", "release" };
                for (int k = 0; k < cmds.Length; k++)
                {
                    bool release = cmds[k] == "release";
                    string label = release && st.ConfirmRelease == rec.Tag ? "Sure?" : cmds[k] == "name" ? "Name" : cmds[k];
                    string color = release ? ColOff : cmds[k] == "name" ? (st.RenameTag == rec.Tag ? ColNavOn : ColWarn) : rec.State == cmds[k] ? ColNavOn : ColBtn;
                    UiButton(c, parent + ".row" + i, label, "bm.ui animal " + rec.Tag + " " + cmds[k], 0.50f + k * 0.10f, 0.12f, 0.59f + k * 0.10f, 0.88f, color, 12);
                }
            }
            if (pages > 1)
            {
                UiButton(c, parent, "<", "bm.ui list -1", 0.40f, 0.015f, 0.46f, 0.06f, ColBtn, 14);
                UiLabel(c, parent, "<color=#9aa58f>page " + (st.ListPage + 1) + " / " + pages + "</color>", 12, 0.47f, 0.015f, 0.62f, 0.06f, TextAnchor.MiddleCenter);
                UiButton(c, parent, ">", "bm.ui list 1", 0.63f, 0.015f, 0.69f, 0.06f, ColBtn, 14);
            }
            TamedRecord renaming;
            if (st.RenameTag != 0UL && Data.Tamed.TryGetValue(st.RenameTag, out renaming) && renaming.Owner == player.userID)
            {
                // a name field over the list: type the name and press Enter
                UiPanel(c, parent, parent + ".rename", 0.10f, 0.40f, 0.90f, 0.62f, "0.03 0.04 0.02 0.97", true);
                UiLabel(c, parent + ".rename", "Name for your " + renaming.Species + " (Enter saves):", 14, 0.04f, 0.58f, 0.96f, 0.95f, TextAnchor.MiddleLeft);
                UiPanel(c, parent + ".rename", parent + ".renamebox", 0.04f, 0.22f, 0.76f, 0.56f, "0.10 0.11 0.08 1");
                c.Add(new CuiElement
                {
                    Parent = parent + ".renamebox",
                    Components =
                    {
                        new CuiInputFieldComponent { Text = renaming.Name ?? string.Empty, FontSize = 15, Align = TextAnchor.MiddleLeft, Command = "bm.ui renameset " + renaming.Tag, CharsLimit = 24, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.03 0", AnchorMax = "0.97 1" }
                    }
                });
                UiButton(c, parent + ".rename", "Cancel", "bm.ui rename 0", 0.80f, 0.22f, 0.96f, 0.56f, ColOff, 13);
            }
        }

        // ---------------------------------------------------------------- Controls

        private void DrawControls(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            PlayerSettings s = SettingsOf(player.userID);
            Driver d = DriverOf(player);
            UiLabel(c, parent, "<size=18><b>Controls and camera</b></size>", 14, 0.02f, 0.91f, 0.7f, 0.99f, TextAnchor.MiddleLeft);
            UiLabel(c, parent, HelpControls(player).Split(new[] { "\n\n" }, StringSplitOptions.None)[0], 13, 0.03f, 0.43f, 0.97f, 0.90f);
            string view = string.IsNullOrEmpty(s.View) ? Cfg.Camera.DefaultView : s.View;
            string preset = PresetOf(player);
            UiLabel(c, parent, "View as an animal:", 13, 0.03f, 0.34f, 0.28f, 0.41f, TextAnchor.MiddleLeft);
            UiButton(c, parent, "third person", "bm.ui view third", 0.29f, 0.35f, 0.47f, 0.41f, view == "third" ? ColNavOn : ColBtn);
            UiButton(c, parent, "first person", "bm.ui view first", 0.48f, 0.35f, 0.66f, 0.41f, view == "first" ? ColNavOn : ColBtn);
            UiLabel(c, parent, "Third person camera:", 13, 0.03f, 0.26f, 0.28f, 0.33f, TextAnchor.MiddleLeft);
            string[] presets = { "A", "B", "C", "D" };
            for (int i = 0; i < presets.Length; i++)
            {
                UiButton(c, parent, "preset " + presets[i], "bm.ui preset " + presets[i], 0.29f + i * 0.13f, 0.27f, 0.405f + i * 0.13f, 0.33f, preset == presets[i] ? ColNavOn : ColBtn);
            }
            UiButton(c, parent, "Cam up", "bm.ui cam up", 0.82f, 0.27f, 0.895f, 0.33f, ColBtn, 12);
            UiButton(c, parent, "Cam down", "bm.ui cam down", 0.90f, 0.27f, 0.985f, 0.33f, ColBtn, 12);
            UiLabel(c, parent, "<size=11><color=#9aa58f>A = spectator camera around the animal | B = A + third-person flag | C = camera at the head | D = first person from the head</color></size>", 11, 0.03f, 0.20f, 0.97f, 0.26f);
            UiButton(c, parent, (s.RefuseSaddle ? "[x] " : "[  ] ") + "Refuse saddles from others while I am an animal", "bm.ui setting refuse", 0.03f, 0.12f, 0.55f, 0.19f, s.RefuseSaddle ? ColOn : ColBtn, 12);
            UiButton(c, parent, (s.ShowHud ? "[x] " : "[  ] ") + "Show the animal HUD", "bm.ui setting hud", 0.57f, 0.12f, 0.97f, 0.19f, s.ShowHud ? ColOn : ColBtn, 12);
            if (d != null)
            {
                UiButton(c, parent, d.Kind == ControlKind.Ride ? "Get off" : "Back to human", "bm.ui revert", 0.03f, 0.03f, 0.27f, 0.10f, ColWarn);
                string[][] actions = { new[] { "Lie", "lie" }, new[] { "Sit", "sit" }, new[] { "Roar", "roar" }, new[] { "Look", "look" }, new[] { "Stand", "stand" }, new[] { "Night (H)", "nv" }, new[] { "Breed", "breed" } };
                for (int a = 0; a < actions.Length; a++)
                {
                    string command = actions[a][1] == "nv" || actions[a][1] == "breed" ? "bm.ui " + actions[a][1] : "bm.ui gesture " + actions[a][1];
                    UiButton(c, parent, actions[a][0], command, 0.29f + a * 0.098f, 0.03f, 0.375f + a * 0.098f, 0.10f, ColBtn, 12);
                }
            }
        }

        // ---------------------------------------------------------------- Transformation

        private void DrawRitual(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            Driver d = DriverOf(player);
            var sb = new StringBuilder();
            sb.Append("<size=18><b>Transformation</b></size>  ").Append(OnOff(Cfg.Features.Transformation && Cfg.Features.AnimalForm)).Append("\n\n");
            sb.Append(HelpRitual(player).Split(new[] { "\n\n" }, 2, StringSplitOptions.None)[1]).Append("\n\n");
            sb.Append("<b>Your status</b>\n");
            long until;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (d != null)
            {
                sb.Append(d.Kind == ControlKind.Form ? "You are a " + d.Species.Display + ". Press X to return.\n" : "You are riding a " + d.Species.Display + ".\n");
            }
            else if (Data.RitualCooldown.TryGetValue(player.userID, out until) && until > now)
            {
                sb.Append("Cooldown: ").Append(until - now).Append(" s until you can transform again.\n");
            }
            else
            {
                sb.Append("Ready.\n");
            }
            foreach (RitualCorpse rc in RitualCorpses.Where(r => r.Killer == player.userID))
            {
                sb.Append("Body of a ").Append(rc.Species.Display).Append(": ").Append(Mathf.Max(0, Mathf.RoundToInt(Cfg.Ritual.CorpseKeepSeconds - (Time.time - rc.Died)))).Append(" s left").Append(rc.Burning ? ", <color=#e6b84a>burning - press E next to it</color>" : string.Empty).Append('\n');
            }
            UiLabel(c, parent, sb.ToString(), 13, 0.03f, 0.12f, 0.97f, 0.97f);
            if (d != null)
            {
                UiButton(c, parent, d.Kind == ControlKind.Ride ? "Get off" : "Back to human", "bm.ui revert", 0.03f, 0.03f, 0.30f, 0.10f, ColWarn);
            }
        }

        // ---------------------------------------------------------------- Admin (control layer)

        private void DrawAdmin(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            UiLabel(c, parent, "<size=18><b>Admin</b></size>   switches are saved at once", 14, 0.02f, 0.91f, 0.8f, 0.99f, TextAnchor.MiddleLeft);
            UiButton(c, parent, "Plugin " + (Active ? "ON" : "OFF") + " (click to switch)", "bm.ui plugin", 0.03f, 0.83f, 0.40f, 0.90f, Active ? ColOn : ColOff, 13);
            UiLabel(c, parent, "<b>Features</b>", 13, 0.03f, 0.76f, 0.5f, 0.82f, TextAnchor.MiddleLeft);
            int i = 0;
            foreach (FieldInfo f in typeof(FeatureCfg).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(bool))
                {
                    continue;
                }
                bool on = (bool)f.GetValue(Cfg.Features);
                int col = i % 4, row = i / 4;
                float x0 = 0.03f + col * 0.24f, y1 = 0.75f - row * 0.065f;
                UiButton(c, parent, Humanise(f.Name) + (on ? "  [on]" : "  [off]"), "bm.ui feature " + f.Name, x0, y1 - 0.057f, x0 + 0.23f, y1, on ? ColOn : ColOff, 11);
                i++;
            }
            float ys = 0.75f - ((i + 3) / 4) * 0.065f - 0.04f;
            UiLabel(c, parent, "<b>Species</b>", 13, 0.03f, ys - 0.06f, 0.5f, ys, TextAnchor.MiddleLeft);
            ys -= 0.06f;
            int k = 0;
            foreach (SpeciesDef s in AllSpecies)
            {
                int col = k % 5, row = k / 5;
                float x0 = 0.03f + col * 0.19f, y1 = ys - row * 0.065f;
                bool on = SpeciesEnabled(s);
                UiButton(c, parent, s.Display + (s.Available ? (on ? "  [on]" : "  [off]") : "  n/a"), s.Available ? "bm.ui species_toggle " + s.Id : "bm.ui noop", x0, y1 - 0.057f, x0 + 0.18f, y1, !s.Available ? "0.25 0.25 0.25 1" : on ? ColOn : ColOff, 11);
                k++;
            }
        }

        // ---------------------------------------------------------------- Spawn (control layer)

        private void DrawSpawn(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            UiLabel(c, parent, "<size=18><b>Spawn animals</b></size>   " + OnOff(Cfg.Features.Spawning) + "   alive: " + SpawnedAlive() + " / " + Cfg.Spawn.MaxAlive, 14, 0.02f, 0.91f, 0.9f, 0.99f, TextAnchor.MiddleLeft);
            int k = 0;
            foreach (SpeciesDef s in AllSpecies.Where(SpeciesEnabled))
            {
                int col = k % 5, row = k / 5;
                float x0 = 0.03f + col * 0.19f, y1 = 0.89f - row * 0.075f;
                UiButton(c, parent, s.Display, "bm.ui spawnspecies " + s.Id, x0, y1 - 0.066f, x0 + 0.18f, y1, st.Species == s.Id ? ColNavOn : ColNav, 12);
                k++;
            }
            float y = 0.89f - ((k + 4) / 5) * 0.075f - 0.03f;
            SpeciesDef pick = SpeciesById(st.Species);
            UiLabel(c, parent, "Amount:", 13, 0.03f, y - 0.07f, 0.14f, y, TextAnchor.MiddleLeft);
            UiButton(c, parent, "-", "bm.ui spawncount -1", 0.15f, y - 0.065f, 0.20f, y - 0.005f, ColBtn, 15);
            UiLabel(c, parent, "<b>" + st.SpawnCount + "</b>", 15, 0.21f, y - 0.07f, 0.27f, y, TextAnchor.MiddleCenter);
            UiButton(c, parent, "+", "bm.ui spawncount 1", 0.28f, y - 0.065f, 0.33f, y - 0.005f, ColBtn, 15);
            UiButton(c, parent, st.SpawnTamed ? "tamed to me" : "wild", "bm.ui spawnmode", 0.36f, y - 0.065f, 0.54f, y - 0.005f, st.SpawnTamed ? ColOn : ColBtn, 12);
            UiButton(c, parent, (st.SpawnSaddled ? "[x] " : "[  ] ") + "saddled", "bm.ui spawnsaddle", 0.56f, y - 0.065f, 0.72f, y - 0.005f, st.SpawnSaddled ? ColOn : ColBtn, 12);
            y -= 0.10f;
            UiButton(c, parent, "Spawn " + (pick != null ? pick.Display : "?") + " where I aim", "bm.ui spawn aim", 0.03f, y - 0.07f, 0.34f, y, ColOn, 13);
            UiButton(c, parent, "Spawn next to me", "bm.ui spawn near", 0.36f, y - 0.07f, 0.60f, y, ColOn, 13);
            y -= 0.10f;
            UiButton(c, parent, "Remove spawned within 100 m", "bm.ui spawnclear near", 0.03f, y - 0.07f, 0.34f, y, ColWarn, 12);
            UiButton(c, parent, "Remove all spawned", "bm.ui spawnclear all", 0.36f, y - 0.07f, 0.60f, y, ColOff, 12);
            y -= 0.10f;
            UiLabel(c, parent, "<b>Bosses</b> (where I aim):", 13, 0.03f, y - 0.07f, 0.24f, y, TextAnchor.MiddleLeft);
            for (int b = 0; b < BossKinds.Length; b++)
            {
                SpeciesDef bs = SpeciesById(BossKinds[b]);
                UiButton(c, parent, bs != null ? bs.Display : BossKinds[b], "bm.ui spawnboss " + BossKinds[b], 0.25f + b * 0.13f, y - 0.07f, 0.37f + b * 0.13f, y, ColWarn, 12);
            }
            UiButton(c, parent, (st.SpawnMini ? "[x] " : "[  ] ") + "mini", "bm.ui bossmini", 0.78f, y - 0.07f, 0.92f, y, st.SpawnMini ? ColOn : ColBtn, 12);
            UiLabel(c, parent, "<size=11><color=#9aa58f>Limits: " + Cfg.Spawn.MaxPerAction + " per action, cooldown " + Cfg.Spawn.Cooldown + " s, at most " + Cfg.Spawn.MaxAlive + " alive. Every spawn is logged.</color></size>", 11, 0.03f, 0.01f, 0.97f, y - 0.08f);
        }

        // ---------------------------------------------------------------- Aim tools (control layer)

        private void DrawTools(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            string note;
            SpeciesDef looked = SpeciesInSight(player, out note);
            UiLabel(c, parent, "<size=18><b>Aim tools</b></size>   act on the animal you look at (up to 60 m)", 14, 0.02f, 0.91f, 0.95f, 0.99f, TextAnchor.MiddleLeft);
            UiLabel(c, parent, looked != null ? "You look at: <b>" + looked.Display + "</b>  " + note : "<color=#9aa58f>Aim at an animal first, then press a button. The menu stays open.</color>", 13, 0.03f, 0.80f, 0.97f, 0.89f, TextAnchor.MiddleLeft);
            string[][] tools =
            {
                new[] { "Ride it", "ride", "Mount it as a rider (admins ride any animal)" },
                new[] { "Become it", "form", "Take its body as an animal form (X to return)" },
                new[] { "Tame it to me", "tame", "Make it yours" },
                new[] { "Release it", "release", "Free an animal of yours" },
                new[] { "Kill it", "kill", "Kills it at once" },
                new[] { "Despawn it", "despawn", "Removes it without a body" },
            };
            for (int i = 0; i < tools.Length; i++)
            {
                int col = i % 2, row = i / 2;
                float x0 = 0.03f + col * 0.49f, y1 = 0.74f - row * 0.15f;
                UiButton(c, parent, tools[i][0], "bm.ui aim " + tools[i][1], x0, y1 - 0.09f, x0 + 0.20f, y1, tools[i][1] == "kill" || tools[i][1] == "despawn" ? ColOff : ColBtn, 14);
                UiLabel(c, parent, "<size=12><color=#b4bfa8>" + tools[i][2] + "</color></size>", 12, x0 + 0.215f, y1 - 0.09f, x0 + 0.47f, y1, TextAnchor.MiddleLeft);
            }
            UiLabel(c, parent, "<b>Admin commands in the chat</b>\nAn admin can run any server command from the chat, on any server this plugin is on: <b>/bm run env.time 12</b> or simply <b>/bm env.time 12</b>.\n"
                + "Menu access for other players: Players page, or <b>/bm grant <name></b> and <b>/bm revoke <name></b>.", 13, 0.03f, 0.04f, 0.97f, 0.30f);
        }

        // ---------------------------------------------------------------- Players (control layer)

        private void DrawPlayers(CuiElementContainer c, string parent, BasePlayer player, MenuState st)
        {
            UiLabel(c, parent, "<size=18><b>Players</b></size>   menu access also allows spawning", 14, 0.02f, 0.91f, 0.9f, 0.99f, TextAnchor.MiddleLeft);
            var granted = new HashSet<string>(permission.GetPermissionUsers(PermMenu).Select(s => s.Split('(')[0]));
            List<BasePlayer> online = BasePlayer.activePlayerList.Where(p => p != null && !p.IsNpc).OrderBy(p => p.displayName).ToList();
            int perPage = 10;
            int pages = Mathf.Max(1, (online.Count + perPage - 1) / perPage);
            st.ListPage = Mathf.Clamp(st.ListPage, 0, pages - 1);
            for (int i = 0; i < perPage; i++)
            {
                int index = st.ListPage * perPage + i;
                if (index >= online.Count)
                {
                    break;
                }
                BasePlayer p = online[index];
                bool auto = IsOwner(p) || IsModerator(p) || permission.UserHasPermission(p.UserIDString, PermAdmin);
                bool has = granted.Contains(p.UserIDString);
                float y1 = 0.89f - i * 0.078f;
                UiPanel(c, parent, parent + ".p" + i, 0.015f, y1 - 0.07f, 0.985f, y1, "0.17 0.19 0.14 0.9");
                string note = auto ? "  <color=#e6b84a>(owner / moderator / admin: always)</color>" : has ? "  <color=#8fd36b>(granted)</color>" : string.Empty;
                UiLabel(c, parent + ".p" + i, p.displayName + "  <size=11><color=#9aa58f>" + p.UserIDString + "</color></size>" + note, 13, 0.015f, 0f, 0.7f, 1f, TextAnchor.MiddleLeft);
                if (!auto)
                {
                    UiButton(c, parent + ".p" + i, has ? "Revoke" : "Grant", "bm.ui " + (has ? "revoke " : "grant ") + p.UserIDString, 0.80f, 0.12f, 0.98f, 0.88f, has ? ColOff : ColOn, 12);
                }
            }
            if (pages > 1)
            {
                UiButton(c, parent, "<", "bm.ui list -1", 0.40f, 0.015f, 0.46f, 0.06f, ColBtn, 14);
                UiLabel(c, parent, "<color=#9aa58f>page " + (st.ListPage + 1) + " / " + pages + "</color>", 12, 0.47f, 0.015f, 0.62f, 0.06f, TextAnchor.MiddleCenter);
                UiButton(c, parent, ">", "bm.ui list 1", 0.63f, 0.015f, 0.69f, 0.06f, ColBtn, 14);
            }
            UiLabel(c, parent, "<size=11><color=#9aa58f>Offline players: bm.grant <steamid> in the console. Groups: bm.grantgroup <group>.</color></size>", 11, 0.02f, 0.015f, 0.38f, 0.075f);
        }

        // ---------------------------------------------------------------- button actions (console command bm.ui)

        internal void MenuAction(BasePlayer player, string[] args, Action<string> reply)
        {
            if (player == null || args.Length == 0)
            {
                return;
            }
            MenuState st = MenuOf(player);
            bool control = HasMenuAccess(player);
            string act = args[0].ToLowerInvariant();
            if (!Active && act != "close" && act != "plugin" && act != "noop")
            {
                return;
            }
            string arg1 = args.Length > 1 ? args[1] : string.Empty;
            int number;
            switch (act)
            {
                case "close":
                    CloseMenu(player);
                    return;
                case "noop":
                    return;
                case "page":
                    if (MenuPages.Any(p => p[0] == arg1) && (!IsControlPage(arg1) || control))
                    {
                        st.Page = arg1;
                        st.TextPage = 0;
                        st.ListPage = 0;
                        st.ConfirmRelease = 0;
                    }
                    break;
                case "topic":
                    st.Topic = arg1;
                    st.TextPage = 0;
                    break;
                case "species":
                    if (SpeciesById(arg1) != null)
                    {
                        st.Species = arg1;
                        st.Note = string.Empty;
                        st.Topic = "species";
                        st.TextPage = 0;
                    }
                    break;
                case "tpage":
                    if (int.TryParse(arg1, out number))
                    {
                        st.TextPage = Mathf.Max(0, st.TextPage + number);
                    }
                    break;
                case "list":
                    if (int.TryParse(arg1, out number))
                    {
                        st.ListPage = Mathf.Max(0, st.ListPage + number);
                    }
                    break;
                case "search":
                    st.Search = string.Join(" ", args.Skip(1).ToArray()).Trim();
                    st.TextPage = 0;
                    break;
                case "clearsearch":
                    st.Search = string.Empty;
                    break;
                case "lookhelp":
                    {
                        string note;
                        SpeciesDef sp = SpeciesInSight(player, out note);
                        if (sp == null)
                        {
                            Tell(player, "NotLooking", 40);
                        }
                        else
                        {
                            st.Page = "help";
                            st.Topic = "species";
                            st.Species = sp.Id;
                            st.Note = note;
                            st.Search = string.Empty;
                            st.TextPage = 0;
                        }
                        break;
                    }
                case "animal":
                    AnimalButton(player, st, args);
                    break;
                case "all":
                    if (CommandNames.Contains(arg1))
                    {
                        int n = ApplyCommand(player, arg1, true);
                        Tell(player, n > 0 ? "CommandSet" : "NoAnimalsNearby", n + " animal(s)", arg1);
                    }
                    break;
                case "view":
                    {
                        SettingsOf(player.userID).View = arg1 == "first" ? "first" : "third";
                        Driver d = DriverOf(player);
                        if (d != null)
                        {
                            d.SetView(arg1 == "first" ? ViewKind.First : ViewKind.Third);
                        }
                        break;
                    }
                case "preset":
                    {
                        string preset = arg1.ToUpperInvariant();
                        if (preset == "A" || preset == "B" || preset == "C" || preset == "D")
                        {
                            Driver d = DriverOf(player);
                            if (d != null) d.SetPreset(preset); else SettingsOf(player.userID).Preset = preset;
                            SettingsOf(player.userID).Preset = preset;
                        }
                        break;
                    }
                case "setting":
                    {
                        PlayerSettings s = SettingsOf(player.userID);
                        if (arg1 == "refuse") s.RefuseSaddle = !s.RefuseSaddle;
                        else if (arg1 == "hud") s.ShowHud = !s.ShowHud;
                        break;
                    }
                case "revert":
                    StopControl(player, "menu");
                    break;
                case "plugin":
                    if (control) SetActive(!Active);
                    break;
                case "feature":
                    if (control) ToggleFeature(arg1);
                    break;
                case "species_toggle":
                    if (control) ToggleSpecies(arg1);
                    break;
                case "spawnspecies":
                    if (SpeciesById(arg1) != null) st.Species = arg1;
                    break;
                case "spawncount":
                    if (int.TryParse(arg1, out number)) st.SpawnCount = Mathf.Clamp(st.SpawnCount + number, 1, Cfg.Spawn.MaxPerAction);
                    break;
                case "spawnmode":
                    st.SpawnTamed = !st.SpawnTamed;
                    break;
                case "bossmini":
                    st.SpawnMini = !st.SpawnMini;
                    break;
                case "spawnboss":
                    if (control && BossKinds.Contains(arg1) && Bosses != null)
                    {
                        BaseCombatEntity boss = Bosses.SpawnBoss(arg1, SpawnSpot(player), st.SpawnMini);
                        TellRaw(player, boss != null ? "A boss " + arg1 + (st.SpawnMini ? " (mini)" : string.Empty) + " stands where you aimed." : "The boss could not be created.");
                    }
                    break;
                case "aim":
                    if (control)
                    {
                        AimAction(player, arg1);
                    }
                    break;
                case "gesture":
                    {
                        Driver gd = DriverOf(player);
                        if (gd != null)
                        {
                            gd.OnGesture(arg1);
                        }
                        break;
                    }
                case "cam":
                    CameraCommand(player, new[] { arg1 }, text => TellRaw(player, text));
                    break;
                case "nv":
                    ToggleNightVision(player);
                    break;
                case "breed":
                    BreedCommand(player, text => TellRaw(player, text));
                    break;
                case "rename":
                    {
                        ulong tagToName;
                        st.RenameTag = ulong.TryParse(arg1, out tagToName) ? tagToName : 0UL;
                        break;
                    }
                case "renameset":
                    {
                        ulong tagToSet;
                        TamedRecord named;
                        if (ulong.TryParse(arg1, out tagToSet) && Data.Tamed.TryGetValue(tagToSet, out named) && named.Owner == player.userID && args.Length > 2)
                        {
                            SetAnimalName(EntityOfRecord(named), named, string.Join(" ", args.Skip(2).ToArray()));
                        }
                        st.RenameTag = 0UL;
                        break;
                    }
                case "spawnsaddle":
                    st.SpawnSaddled = !st.SpawnSaddled;
                    break;
                case "spawn":
                    if (control)
                    {
                        var spawnArgs = new List<string> { st.Species, st.SpawnCount.ToString(CultureInfo.InvariantCulture), st.SpawnTamed ? "tamed" : "wild" };
                        if (st.SpawnSaddled) spawnArgs.Add("saddled");
                        if (arg1 == "near") spawnArgs.Add("near");
                        SpawnCommand(player, spawnArgs.ToArray(), text => TellRaw(player, text));
                    }
                    break;
                case "spawnclear":
                    if (control)
                    {
                        Tell(player, "Cleared", ClearSpawned(player, arg1 == "near" ? 100f : 0f));
                    }
                    break;
                case "grant":
                case "revoke":
                    if (control && !string.IsNullOrEmpty(arg1))
                    {
                        GrantCommand(act == "grant", new[] { arg1 }, text => TellRaw(player, text));
                    }
                    break;
                default:
                    Log(3, "Menu: unknown action '" + act + "'");
                    break;
            }
            if (st.Open)
            {
                DrawMenu(player);
            }
        }

        private void AnimalButton(BasePlayer player, MenuState st, string[] args)
        {
            ulong tag;
            if (args.Length < 3 || !ulong.TryParse(args[1], out tag))
            {
                return;
            }
            TamedRecord rec;
            if (!Data.Tamed.TryGetValue(tag, out rec) || rec.Owner != player.userID)
            {
                return;
            }
            string cmd = args[2].ToLowerInvariant();
            BaseEntity e = EntityOfRecord(rec);
            if (cmd == "name")
            {
                st.RenameTag = st.RenameTag == tag ? 0UL : tag;
                return;
            }
            if (cmd == "release")
            {
                if (st.ConfirmRelease != tag)
                {
                    st.ConfirmRelease = tag;
                    return;
                }
                st.ConfirmRelease = 0;
                if (e != null)
                {
                    ReleaseAnimal(e);
                }
                else
                {
                    Data.Tamed.Remove(tag);
                }
                return;
            }
            st.ConfirmRelease = 0;
            if (cmd == "follow" || cmd == "wait" || cmd == "guard")
            {
                rec.State = cmd;
                if (e != null)
                {
                    ApplyState(e, rec);
                }
            }
        }

        private void ToggleFeature(string name)
        {
            FieldInfo f = typeof(FeatureCfg).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null || f.FieldType != typeof(bool))
            {
                return;
            }
            f.SetValue(Cfg.Features, !(bool)f.GetValue(Cfg.Features));
            SaveConfig();
            Log(2, "Feature " + name + " is now " + f.GetValue(Cfg.Features));
        }

        private void ToggleSpecies(string id)
        {
            SpeciesDef s = SpeciesById(id);
            if (s == null)
            {
                return;
            }
            SpeciesOverride o;
            if (!Cfg.Species.TryGetValue(s.Id, out o))
            {
                o = new SpeciesOverride();
                Cfg.Species[s.Id] = o;
            }
            o.Enabled = !(o.Enabled ?? true);
            SaveConfig();
            Log(2, "Species " + s.Id + " is now " + (o.Enabled.Value ? "enabled" : "disabled"));
        }

        // ---------------------------------------------------------------- headless test

        private static readonly HashSet<string> MenuActionNames = new HashSet<string>
        {
            "close", "noop", "page", "topic", "species", "tpage", "list", "search", "clearsearch", "lookhelp", "animal", "all", "view", "preset", "setting",
            "revert", "plugin", "feature", "species_toggle", "spawnspecies", "spawncount", "spawnmode", "spawnsaddle", "spawn", "spawnclear", "grant", "revoke",
            "bossmini", "spawnboss", "aim", "gesture", "cam", "nv", "breed", "rename", "renameset"
        };

        // Builds every page and every Help topic for a stand-in player, checks the JSON and that every button names a known action.
        internal string TestMenu()
        {
            var lines = new List<string>();
            var go = new GameObject("bm-menu-test");
            try
            {
                BasePlayer dummy = go.AddComponent<BasePlayer>();
                dummy.userID = 76561198000000001UL;
                dummy.UserIDString = "76561198000000001";
                dummy.displayName = "Tester";
                int built = 0, bytes = 0, buttons = 0, biggest = 0;
                var unknown = new HashSet<string>();
                foreach (bool control in new[] { false, true })
                {
                    foreach (string[] page in MenuPages)
                    {
                        List<string> topics = page[0] == "help" ? HelpTopics().Select(t => t.Id).ToList() : new List<string> { "overview" };
                        foreach (string topic in topics)
                        {
                            List<string> cards = topic == "species" ? AllSpecies.Where(s => s.Available).Select(s => s.Id).ToList() : new List<string> { "cow" };
                            foreach (string card in cards)
                            {
                                var st = new MenuState { Page = page[0], Topic = topic, Species = card };
                                CuiElementContainer c = BuildMenu(dummy, st, control);
                                string json = c.ToJson();
                                JToken parsed = JToken.Parse(json);
                                built++;
                                bytes += json.Length;
                                biggest = Mathf.Max(biggest, json.Length);
                                foreach (JToken cmd in parsed.SelectTokens("..command"))
                                {
                                    string text = (string)cmd;
                                    if (string.IsNullOrEmpty(text))
                                    {
                                        continue;
                                    }
                                    buttons++;
                                    string[] parts = text.Split(' ');
                                    if (parts[0] != "bm.ui" || parts.Length < 2 || !MenuActionNames.Contains(parts[1]))
                                    {
                                        unknown.Add(text);
                                    }
                                }
                            }
                        }
                    }
                }
                lines.Add((unknown.Count == 0 ? "PASS" : "FAIL") + " " + built + " windows built (" + bytes / 1024 + " kB in all, largest " + biggest / 1024 + " kB), " + buttons + " buttons" + (unknown.Count > 0 ? ", unknown actions: " + string.Join(" | ", unknown.ToArray()) : string.Empty));
                var sizes = HelpTopics().Select(t => t.Id + " " + PageText(t.Text(dummy), 22, 78).Count).ToArray();
                lines.Add("PASS help pages per topic: " + string.Join(", ", sizes));
            }
            catch (Exception ex)
            {
                lines.Add("FAIL " + ex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            return string.Join("\n", lines.ToArray());
        }

        // ---------------------------------------------------------------- module

        internal sealed class MenuModule : Module
        {
            public override int Order => 90;

            public override void OnDisable()
            {
                Bm.CloseAllMenus();
            }

            public override void Unload()
            {
                Bm.CloseAllMenus();
            }

            public override void OnPlayerDisconnected(BasePlayer player, string reason)
            {
                Bm.MenuStates.Remove(player.userID);
            }

            public override void OnPlayerDeath(BasePlayer player, HitInfo info)
            {
                MenuState st;
                if (Bm.MenuStates.TryGetValue(player.userID, out st) && st.Open)
                {
                    Bm.CloseMenu(player);
                }
            }
        }
    }

    // ======================================== Movement.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- walls, surfaces and the sounds of moving animals
        // A controlled animal is moved by the plugin, so the plugin also has to stop it at walls, tell the surface it stands on (for the sound
        // of a landing) and play the sounds of the world: the impact of a jump into a fence, a landing on grass or on a roof.

        private static readonly int ObstacleMask = LayerMask.GetMask("Construction", "Deployed", "World", "Tree", "Vehicle World");
        private static readonly int GroundProbeMask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed", "Default");

        // Keeps an animal from walking through walls: a box the size of the whole body (head and tail too) is swept along the step; the step is
        // shortened to the wall and the rest slides along it.
        internal Vector3 WallSlide(BaseCombatEntity body, Vector3 pos, Vector3 step, ref float speed, out bool hitWall, out Vector3 wallPoint, out string wallSurface)
        {
            hitWall = false;
            wallPoint = pos;
            wallSurface = "generic";
            Vector3 flat = new Vector3(step.x, 0f, step.z);
            float dist = flat.magnitude;
            if (dist < 0.0001f)
            {
                return step;
            }
            Bounds b = body.bounds;
            float k = ModelScale(body);
            float low = Mathf.Max(0.3f, 0.35f * k * Mathf.Min(1f, b.size.y));      // curbs and small stones are stepped over
            float top = Mathf.Max(low + 0.3f, b.max.y * k);
            Quaternion rot = Quaternion.Euler(0f, body.transform.eulerAngles.y, 0f);
            Vector3 half = new Vector3(Mathf.Max(0.12f, b.extents.x * k * 1.0f), Mathf.Max(0.12f, (top - low) * 0.5f), Mathf.Max(0.12f, b.extents.z * k * 1.0f + 0.08f));
            Vector3 center = pos + rot * new Vector3(b.center.x * k, 0f, b.center.z * k) + Vector3.up * ((low + top) * 0.5f);
            Vector3 dir = flat / dist;
            RaycastHit hit;
            if (!Physics.BoxCast(center, half, dir, out hit, rot, dist + 0.05f, ObstacleMask, QueryTriggerInteraction.Ignore) || hit.normal.y > 0.35f)
            {
                return step;
            }
            BaseEntity thing = hit.GetEntity();
            if (thing is BasePlayer || thing is BaseNPC2 || thing is RidableHorse || thing is BaseAnimalNPC)
            {
                return step;                            // a living thing is no wall: running into it is an attack (see ChargeTick)
            }
            hitWall = true;
            wallPoint = hit.point;
            wallSurface = ColliderSurface(hit.collider);
            float allowed = Mathf.Max(0f, hit.distance - 0.03f);
            Vector3 first = allowed < 0.04f ? Vector3.zero : dir * Mathf.Min(dist, allowed);        // no creeping along the wall: the body (and the camera) stays still
            Vector3 slide = Vector3.ProjectOnPlane(flat, hit.normal);
            slide.y = 0f;
            slide *= 0.7f;
            if (slide.sqrMagnitude > 0.000001f && Physics.BoxCast(center + first, half, slide.normalized, out hit, rot, slide.magnitude + 0.05f, ObstacleMask, QueryTriggerInteraction.Ignore) && hit.normal.y <= 0.35f)
            {
                slide = Vector3.zero;
            }
            speed = Mathf.MoveTowards(speed, 0f, Mathf.Abs(speed) * 0.5f);
            return first + slide;
        }

        // Is a part of the body inside a wall or an object when the animal stands here, turned this way? (turning checks it first)
        internal bool BodyOverlaps(BaseCombatEntity body, Vector3 pos, float yaw)
        {
            Bounds b = body.bounds;
            float k = ModelScale(body);
            float low = Mathf.Max(0.3f, 0.35f * k * Mathf.Min(1f, b.size.y));
            float top = Mathf.Max(low + 0.3f, b.max.y * k);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 half = new Vector3(Mathf.Max(0.12f, b.extents.x * k * 0.97f), Mathf.Max(0.12f, (top - low) * 0.5f), Mathf.Max(0.12f, b.extents.z * k * 0.97f));
            Vector3 center = pos + rot * new Vector3(b.center.x * k, 0f, b.center.z * k) + Vector3.up * ((low + top) * 0.5f);
            return Physics.CheckBox(center, half, rot, ObstacleMask, QueryTriggerInteraction.Ignore);
        }

        // ---------------------------------------------------------------- surfaces

        internal static string SurfaceAt(Vector3 pos)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out hit, 2.5f, GroundProbeMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider is TerrainCollider)
                {
                    return TerrainSurface(hit.point);
                }
                return ColliderSurface(hit.collider);
            }
            return TerrainSurface(pos);
        }

        private static string TerrainSurface(Vector3 pos)
        {
            if (WaterLevel.GetWaterDepth(pos, true, true, null) > 0.15f)
            {
                return "water";
            }
            if (TerrainMeta.SplatMap != null)
            {
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.SNOW) > 0.5f) return "snow";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.SAND) > 0.5f) return "sand";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.ROCK) > 0.5f) return "rock";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.STONES) > 0.5f) return "stones";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.GRAVEL) > 0.5f) return "gravel";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.FOREST) > 0.5f) return "forest";
                if (TerrainMeta.SplatMap.GetSplat(pos, TerrainSplat.DIRT) > 0.5f) return "dirt";
            }
            return "grass";
        }

        internal static string ColliderSurface(Collider c)
        {
            if (c == null)
            {
                return "generic";
            }
            BuildingBlock block = c.GetComponentInParent<BuildingBlock>();
            if (block != null)
            {
                switch (block.grade)
                {
                    case BuildingGrade.Enum.Twigs:
                    case BuildingGrade.Enum.Wood: return "wood";
                    case BuildingGrade.Enum.Stone: return "rock";
                    case BuildingGrade.Enum.Metal:
                    case BuildingGrade.Enum.TopTier: return "metal";
                }
            }
            string name = (c.sharedMaterial != null ? c.sharedMaterial.name : string.Empty).ToLowerInvariant();
            if (name.Contains("wood")) return "wood";
            if (name.Contains("metal") || name.Contains("steel")) return "metal";
            if (name.Contains("rock") || name.Contains("stone")) return "rock";
            if (name.Contains("concrete") || name.Contains("brick")) return "concrete";
            if (name.Contains("glass")) return "glass";
            if (name.Contains("sand")) return "sand";
            if (name.Contains("snow")) return "snow";
            if (name.Contains("grass")) return "grass";
            if (name.Contains("dirt")) return "dirt";
            string obj = c.gameObject.name.ToLowerInvariant();
            if (obj.Contains("wood") || obj.Contains("fence") || obj.Contains("barricade")) return "wood";
            if (obj.Contains("rock") || obj.Contains("cliff") || obj.Contains("stone")) return "rock";
            if (obj.Contains("metal")) return "metal";
            return "generic";
        }

        // ---------------------------------------------------------------- sounds

        // The sound (and dust) of a landing or a push-off on a surface: the game's own jump-land effects of the barefoot player, per ground.
        internal void LandSound(Vector3 pos, string surface = null)
        {
            string s = surface ?? SurfaceAt(pos);
            string path = FindPrefabPath("jump-land/barefoot/" + s + "/") ?? FindPrefabPath("jump-land/barefoot/dirt/") ?? FindPrefabPath("jump-land/barefoot/generic/");
            RunEffect(path, pos);
        }

        // The sound of hitting a solid thing: a blunt impact of the material (wood, rock, metal, concrete ...) plus the flesh thud.
        internal void BumpSound(Vector3 pos, string surface)
        {
            string material = surface == "stones" || surface == "gravel" ? "rock" : surface;
            string path = FindPrefabPath("impacts/blunt/" + material + "/") ?? FindPrefabPath("impacts/blunt/generic/");
            RunEffect(path, pos);
            RunEffect(FindPrefabPath("impacts/blunt/flesh/"), pos);
        }

        // How high an animal can jump over or onto things: its own size times the species' jumping strength (config: Movement.JumpHeight).
        internal float JumpClearance(BaseCombatEntity body, SpeciesDef species)
        {
            float k = ModelScale(body);
            float size = Mathf.Max(0.1f, body.bounds.size.y) * k;
            float factor = 1f;
            switch (species != null ? species.Id : string.Empty)
            {
                case "stag": factor = 1.35f; break;
                case "panther": factor = 1.45f; break;
                case "tiger": factor = 1.25f; break;
                case "wolf": factor = 1.15f; break;
                case "horse": factor = 1.1f; break;
                case "bear": case "polarbear": case "boar": factor = 0.9f; break;
                case "rabbit": factor = 2f; break;
                case "squirrel": factor = 3f; break;
                case "frog": factor = 4f; break;
                case "crab": case "crocodile": factor = 0.5f; break;
            }
            return Mathf.Max(0.22f, size * 0.7f * factor * Cfg.Movement.JumpHeight);
        }

        // ---------------------------------------------------------------- running into things (R19: charges)

        private readonly Dictionary<ulong, float> chargeHits = new Dictionary<ulong, float>();

        private static bool Charges(SpeciesDef s)
        {
            if (s == null)
            {
                return false;
            }
            switch (s.Id)
            {
                case "bull": case "ram": case "cow": case "sheep": case "stag": case "boar": case "horse": return true;
                default: return false;
            }
        }

        // Bull, ram, cow, sheep, stag, boar and horse that run into a player hurt and knock them down; running into another animal hurts it
        // and counts as an attack; bulls and rams also break wooden barricades (R2.6).
        internal void ChargeTick(Driver d)
        {
            if (Time.time < nextRamCheck || !Charges(d.Species) || d.Gait < 3 || d.Speed < 2.5f)
            {
                return;
            }
            nextRamCheck = Time.time + 0.2f;
            float k = ModelScale(d.Body);
            Vector3 heading = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.forward;
            Vector3 ahead = d.Body.transform.position + heading * (d.Body.bounds.extents.z * k + 0.5f) + Vector3.up * 0.9f * k;
            var found = new List<BaseEntity>();
            Vis.Entities(ahead, 1.0f * k, found);
            float now = Time.time;
            foreach (BaseEntity e in found)
            {
                if (!Valid(e) || e == d.Body || e.GetParentEntity() == d.Body)
                {
                    continue;
                }
                Barricade barricade = e as Barricade;
                if (barricade != null && (d.Species.Id == "bull" || d.Species.Id == "ram") && d.Gait >= 4 && !barricade.IsDead() && barricade.ShortPrefabName.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    barricade.OnAttacked(60f, Rust.DamageType.Blunt, d.Body, false);
                    continue;
                }
                BaseCombatEntity target = e as BaseCombatEntity;
                if (target == null || target is BaseCorpse || !Alive(target))
                {
                    continue;
                }
                BasePlayer victim = target as BasePlayer;
                if (victim == null && !(target is BaseNPC2 || target is RidableHorse))
                {
                    continue;
                }
                if (victim != null && (victim == d.Player || victim.IsNpc && false))
                {
                    continue;
                }
                float last;
                if (chargeHits.TryGetValue(target.net.ID.Value, out last) && now - last < 2.5f)
                {
                    continue;
                }
                if (!MayHitCharge(d, target))
                {
                    continue;
                }
                chargeHits[target.net.ID.Value] = now;
                d.SuppressCrash(2f);                   // running into a living thing is an attack: the animal itself does not fall
                float damage = Cfg.Combat.ChargeDamage * ChargePower(d.Species) * Mathf.Clamp01(d.Speed / Mathf.Max(1f, d.GaitSpeed(5))) * 1.6f * k;
                if (victim != null)
                {
                    target.Hurt(new HitInfo(d.Body, target, Rust.DamageType.Blunt, damage, target.transform.position));
                    Vector3 away = (victim.transform.position - d.Body.transform.position);
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.01f ? away.normalized : heading;
                    if (!victim.IsDead())
                    {
                        victim.Ragdoll(away * 7f + Vector3.up * 3.5f, true, false, false, d.Body);
                    }
                }
                else
                {
                    target.Hurt(new HitInfo(d.Body, target, Rust.DamageType.Blunt, damage * 0.8f, target.transform.position));
                }
                BumpSound(target.transform.position + Vector3.up, "generic");
                if (d.Animal != null)
                {
                    Play(d.Animal, "hurt");
                }
            }
            if (chargeHits.Count > 200)
            {
                foreach (ulong old in chargeHits.Where(kv => now - kv.Value > 10f).Select(kv => kv.Key).ToList())
                {
                    chargeHits.Remove(old);
                }
            }
        }

        private static float ChargePower(SpeciesDef s)
        {
            switch (s.Id)
            {
                case "bull": case "ram": return 1.5f;
                case "horse": case "stag": case "boar": return 1.2f;
                default: return 0.8f;
            }
        }

        private bool MayHitCharge(Driver d, BaseCombatEntity target)
        {
            BasePlayer p = target as BasePlayer;
            if (p != null)
            {
                if (!Cfg.Combat.HitPlayers || IsFormPlayer(p) || (Cfg.Combat.SparePartyMembers && SameParty(d.Player, p)))
                {
                    return false;
                }
            }
            else
            {
                if (!Cfg.Combat.HitAnimals)
                {
                    return false;
                }
                Driver other = DriverOfAnimal(target);
                if (other != null && Cfg.Combat.SparePartyMembers && SameParty(d.Player, other.Player))
                {
                    return false;
                }
                TamedRecord rec = RecordOf(target);
                if (rec != null && Cfg.Combat.SparePartyMembers && MayUse(d.Player, rec.Owner, "team"))
                {
                    return false;
                }
                if (IsBossPair(d.Body, target))
                {
                    return false;
                }
            }
            return !(Cfg.Combat.SparesSafeZone && target.InSafeZone());
        }
    }

    // ======================================== Play.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- what a player does as an animal besides moving
        // Gestures (lie down, sit, roar, look around), the key hints beside the hotbar, night vision, the key binds and the camera height.

        // ---------------------------------------------------------------- gestures

        private static readonly string[] CallWords = { "roar", "growl", "bellow", "moo", "bleat", "howl", "bark", "neigh", "whinny", "snort", "squawk", "cluck", "hiss", "chirp", "scream", "snarl", "intimidate", "threat", "call" };
        private static readonly string[] LookWords = { "look", "sniff", "shake", "ear", "tail", "scratch", "stretch", "lick", "chew", "search", "paw", "stomp", "idle" };
        private static readonly string[] SitWords = { "sit" };
        private static readonly string[] LieWords = { "lie", "lay", "rest", "sleep" };
        internal static readonly HashSet<string> DirectGestures = new HashSet<string> { "roar", "call", "look", "sit", "lie", "stand" };

        private static string GestureCategory(string gesture)
        {
            switch (gesture)
            {
                case "wave": case "victory": case "thumbsup": case "beatchest": case "shush_vocal": case "chicken":
                    return "roar";
                case "ok": case "clap": case "petting": case "hat_tip":
                    return "sit";
                case "surrender": case "friendly": case "thumbsdown": case "cabbagepatch": case "sillydance": case "twist": case "sillydance2": case "raiseroof": case "hiphopdance":
                    return "lie";
                default:
                    return "look";
            }
        }

        // The gesture wheel and the commands /bm lie, sit, roar, look and stand: the animal plays what its species has, with the sounds that belong to the clip.
        internal void PerformGesture(Driver d, string gesture)
        {
            string g = (gesture ?? string.Empty).ToLowerInvariant();
            string category = DirectGestures.Contains(g) ? (g == "call" ? "roar" : g) : GestureCategory(g);
            if (category == "stand")
            {
                d.ClearPose();
                return;
            }
            if (d.Horse != null)
            {
                if (category == "roar")
                {
                    RpcGroup(d.Horse, "CLIENT_Stand");           // the horse rears up and neighs
                }
                return;
            }
            BaseNPC2 animal = d.Animal;
            if (animal == null)
            {
                return;
            }
            switch (category)
            {
                case "lie":
                case "sit":
                    d.StartPose(category);
                    return;
                case "roar":
                    d.ClearPose();
                    PlayOneOf(d, CallWords, "call");
                    return;
                default:
                    d.ClearPose();
                    PlayOneOf(d, LookWords, "look");
                    return;
            }
        }

        private void PlayOneOf(Driver d, string[] words, string kind)
        {
            BaseNPC2 animal = d.Animal;
            List<ClipRef> all = Clips(animal).Where(c => !Has(c, "death") && !Has(c, "attack") && !Has(c, "turn") && !Has(c, "knock")).ToList();
            List<ClipRef> matches = all.Where(c => words.Any(w => Has(c, w)) && !Has(c, "lie") && !Has(c, "sleep") && !Has(c, "graze") && !Has(c, "drink")).ToList();
            if (matches.Count == 0)
            {
                matches = d.IdleClips();
            }
            if (matches.Count == 0)
            {
                return;
            }
            d.PlayIdleClip(matches[d.NextGestureIndex() % matches.Count]);
        }

        // ---------------------------------------------------------------- the height of the camera

        // What the player added to or took from the camera height of this species (m), plus what the admin saved for everyone.
        internal float CamLiftOf(BasePlayer player, SpeciesDef species)
        {
            if (species == null)
            {
                return 0f;
            }
            float lift = 0f;
            SpeciesOverride o;
            if (Cfg.Species.TryGetValue(species.Id, out o) && o.PivotLift.HasValue)
            {
                lift += o.PivotLift.Value;
            }
            float mine;
            PlayerSettings s = player != null ? SettingsOf(player.userID) : null;
            if (s != null && s.CamLift != null && s.CamLift.TryGetValue(species.Id, out mine))
            {
                lift += mine;
            }
            return lift;
        }

        // /bm cam up | down | reset | save
        internal void CameraCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            Driver d = DriverOf(player);
            if (d == null || d.Species == null)
            {
                reply("Ride an animal or become one first.");
                return;
            }
            string what = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
            PlayerSettings s = SettingsOf(player.userID);
            if (s.CamLift == null)
            {
                s.CamLift = new Dictionary<string, float>();
            }
            float now;
            s.CamLift.TryGetValue(d.Species.Id, out now);
            switch (what)
            {
                case "up": now += 0.25f; break;
                case "down": now -= 0.25f; break;
                case "reset": now = 0f; break;
                case "save":
                    if (!IsAdmin(player))
                    {
                        reply(L("NoPermission"));
                        return;
                    }
                    SpeciesOverride o;
                    if (!Cfg.Species.TryGetValue(d.Species.Id, out o))
                    {
                        o = new SpeciesOverride();
                        Cfg.Species[d.Species.Id] = o;
                    }
                    o.PivotLift = (o.PivotLift ?? 0f) + now;
                    s.CamLift[d.Species.Id] = 0f;
                    SaveConfig();
                    reply("Camera height of the " + d.Species.Display + " saved for everyone (" + o.PivotLift.Value.ToString("0.00") + " m).");
                    d.RefreshCamera();
                    return;
                default:
                    reply("Camera height: /bm cam up | down | reset" + (IsAdmin(player) ? " | save (keep it for everyone)" : string.Empty) + ". Now " + now.ToString("0.00") + " m for the " + d.Species.Display + ".");
                    return;
            }
            s.CamLift[d.Species.Id] = Mathf.Clamp(now, -1.5f, 4f);
            d.RefreshCamera();
            reply("Camera height of the " + d.Species.Display + ": " + s.CamLift[d.Species.Id].ToString("0.00") + " m.");
        }

        // ---------------------------------------------------------------- the key hints beside the hotbar

        private const string KeysUi = "BeastmasterKeys";

        // An animal form has no item belt: the panel of its keys takes the belt's place (an opaque panel, the belt is below it). A rider keeps the belt
        // and gets the keys beside it.
        internal void DrawKeyHints(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !Cfg.Form.KeyHints || !SettingsOf(player.userID).ShowHud)
            {
                return;
            }
            Driver d = DriverOf(player);
            bool form = d != null && d.Kind == ControlKind.Form;
            if (form && !d.Spectating)
            {
                ClearKeyHints(player);                 // the inventory view shows the game's own HUD
                return;
            }
            bool hasSkin = form && (d.Body is LivestockAnimal || d.Horse != null);
            string[][] cells = form
                ? (hasSkin
                    ? new[] { new[] { "F", "Attack" }, new[] { "Ctrl", "Brake" }, new[] { "E", "Feed" }, new[] { "R", "Skin" }, new[] { "Shift", "Sprint" }, new[] { "Space", "Jump" }, new[] { "G", "Night" }, new[] { "X", "Human" } }
                    : new[] { new[] { "F", "Attack" }, new[] { "Ctrl", "Brake" }, new[] { "E", "Feed" }, new[] { "Shift", "Sprint" }, new[] { "Space", "Jump" }, new[] { "G", "Night" }, new[] { "X", "Human" } })
                : new[] { new[] { "F", "Attack" }, new[] { "Ctrl", "Brake" }, new[] { "G", "View" }, new[] { "Shift", "Sprint" }, new[] { "Space", "Jump" }, new[] { "X", "Get off" } };
            // one small row just above the item belt
            float cellW = 78f, cellH = 20f, gap = 3f;
            float panelW = cells.Length * (cellW + gap) + gap, panelH = cellH + 2f * gap;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.06 0.04 0.7" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = Pt(-panelW * 0.5f, 100f), OffsetMax = Pt(panelW * 0.5f, 100f + panelH) }
            }, "Overlay", KeysUi);
            for (int i = 0; i < cells.Length; i++)
            {
                float cx = gap + i * (cellW + gap);
                string key = cells[i][0];
                float cap = 8f + 6f * key.Length;
                string cell = KeysUi + ".c" + i;
                c.Add(new CuiPanel
                {
                    Image = { Color = "0.16 0.19 0.12 0.9" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = Pt(cx, gap), OffsetMax = Pt(cx + cellW, gap + cellH) }
                }, KeysUi, cell);
                c.Add(new CuiPanel
                {
                    Image = { Color = "0.89 0.91 0.83 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = Pt(2, 2), OffsetMax = Pt(2 + cap, cellH - 2) }
                }, cell, cell + ".k");
                c.Add(new CuiLabel
                {
                    Text = { Text = "<b>" + key + "</b>", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.09 0.11 0.07 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, cell + ".k");
                c.Add(new CuiLabel
                {
                    Text = { Text = cells[i][1], FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.94 0.95 0.9 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = Pt(cap + 5, 0), OffsetMax = Pt(cellW - 2, cellH) }
                }, cell);
            }
            CuiHelper.DestroyUi(player, KeysUi);
            CuiHelper.AddUi(player, c);
        }

        internal void ClearKeyHints(BasePlayer player)
        {
            if (player != null && player.IsConnected)
            {
                CuiHelper.DestroyUi(player, KeysUi);
            }
        }

        // ---------------------------------------------------------------- the inventory shows the animal

        private const string InvUi = "BeastmasterInventory";

        // Elements on the game's menu layer exist only while the inventory is open: a panel over the player's model and avatar shows the animal.
        internal void DrawInventoryCard(Driver d)
        {
            ClearInventoryCard(d.Player);          // the picture over the inventory is gone for good; the inventory opens as usual
        }

        internal void ClearInventoryCard(BasePlayer p)
        {
            if (p != null && p.IsConnected)
            {
                CuiHelper.DestroyUi(p, InvUi);
                CuiHelper.DestroyUi(p, InvUi + ".icon");
            }
        }

        // ---------------------------------------------------------------- night vision (H)

        private const ulong NightVisionSkin = 71990UL;

        private static Item FindNightVision(BasePlayer p)
        {
            foreach (Item it in PlayerItems(p))
            {
                if (it.skin == NightVisionSkin)
                {
                    return it;
                }
            }
            return null;
        }

        internal void ClearNightVision(BasePlayer p)
        {
            if (p == null || p.inventory == null)
            {
                return;
            }
            Item it;
            int guard = 0;
            while ((it = FindNightVision(p)) != null && guard++ < 4)
            {
                it.Remove();
            }
        }

        // The goggles of the game give the vision, so the player needs a pair in the inventory: Ctrl + G (or H after a bind, or the menu button)
        // puts them on the head and takes them off again.
        internal void ToggleNightVision(BasePlayer player)
        {
            Driver d = DriverOf(player);
            if (d == null)
            {
                TellRaw(player, "Night vision is for animal forms and riders.");
                return;
            }
            ItemContainer wear = player.inventory.containerWear;
            Item on = wear != null ? wear.itemList.FirstOrDefault(i => i.info.shortname == "nightvisiongoggles") : null;
            if (on != null)
            {
                if (!on.MoveToContainer(player.inventory.containerMain) && !on.MoveToContainer(player.inventory.containerBelt))
                {
                    TellRaw(player, "No room in the inventory to take the goggles off.");
                    return;
                }
                TellRaw(player, "Night vision off.");
                return;
            }
            Item goggles = PlayerItems(player).FirstOrDefault(i => i.info.shortname == "nightvisiongoggles");
            if (goggles == null)
            {
                TellRaw(player, "You need night vision goggles in your inventory.");
                return;
            }
            if (!goggles.MoveToContainer(wear))
            {
                TellRaw(player, "Take off your headgear first: the goggles need the head slot.");
                return;
            }
            TellRaw(player, "Night vision on.");
        }

        // ---------------------------------------------------------------- key binds

        // A game only accepts a few commands from a server, and a key bind is not one of them: the key is bound once by the player (it is kept).
        internal void JoinHint(BasePlayer player)
        {
            if (!Cfg.General.JoinHint || player == null || !player.IsConnected || !Has(player, PermUse))
            {
                return;
            }
            PlayerSettings s = SettingsOf(player.userID);
            if (s.HintShown)
            {
                return;
            }
            s.HintShown = true;
            TellRaw(player, "Beastmaster menu: /bm, or hold Ctrl and press R with empty hands. Night vision as an animal: Ctrl + G.");
        }

        // The server cannot rebind keys by itself, but it can ask the player's game to: the menu on F5 and the night vision on H.
        internal void SendBinds(BasePlayer player)
        {
            if (!Cfg.Controls.AutoBind || player == null || !player.IsConnected || !Has(player, PermUse))
            {
                return;
            }
            if (!string.IsNullOrEmpty(Cfg.Controls.MenuKey))
            {
                player.SendConsoleCommand("bind " + Cfg.Controls.MenuKey + " bm.menu");
            }
            if (!string.IsNullOrEmpty(Cfg.Controls.NightKey))
            {
                player.SendConsoleCommand("bind " + Cfg.Controls.NightKey + " bm.nv");
            }
        }

        // ---------------------------------------------------------------- R: another skin (colours and patterns) at once

        private readonly Dictionary<ulong, float> nextSkin = new Dictionary<ulong, float>();

        internal void ReskinForm(Driver d)
        {
            float last;
            if (nextSkin.TryGetValue(d.Player.userID, out last) && Time.time < last)
            {
                return;
            }
            nextSkin[d.Player.userID] = Time.time + 0.3f;
            BaseCombatEntity body = d.Body;
            if (!RerollLook(body))
            {
                return;
            }
            LivestockAnimal livestock = body as LivestockAnimal;
            if (livestock != null)
            {
                // the pattern comes from the name (rolled above), the colours and the size of the patches from these genes
                int genes = livestock.Genes;
                foreach (LivestockGene gene in new[] { LivestockGene.Dung, LivestockGene.Yield, LivestockGene.Longevity, LivestockGene.Fertility, LivestockGene.Hardiness })
                {
                    genes = LivestockGenome.WithPair(genes, gene, (LivestockAllele)UnityEngine.Random.Range(0, 3), (LivestockAllele)UnityEngine.Random.Range(0, 3));
                }
                livestock.Genes = genes;
            }
            body.SendNetworkUpdateImmediate();
        }

        // ---------------------------------------------------------------- the menu's tools on the animal in sight

        internal BaseEntity AimedAnimal(BasePlayer player)
        {
            return (BaseEntity)AnimalInSight(player, 60f, 12f) ?? HorseInSight(player, 60f, 12f);
        }

        internal void Despawn(BaseEntity e)
        {
            if (!Valid(e))
            {
                return;
            }
            BasePlayer rider = null;
            Driver d = DriverOfAnimal(e);
            if (d != null)
            {
                rider = d.Player;
                StopControl(rider, "despawned");
            }
            StopFollower(e);
            Data.Tamed.Remove(e.skinID);
            Data.Spawned.Remove(e.skinID);
            Data.Bosses.Remove(e.skinID);
            Data.Horses.Remove(e.skinID);
            RidableHorse horse = e as RidableHorse;
            if (horse != null && Stable != null)
            {
                Stable.Forget(horse);
            }
            e.Kill();
        }

        internal void AimAction(BasePlayer player, string what)
        {
            BaseEntity target = AimedAnimal(player);
            BaseCombatEntity living = target as BaseCombatEntity;
            if (living == null)
            {
                Tell(player, "NotLooking", 60);
                return;
            }
            switch (what)
            {
                case "ride":
                    StartControl(player, living, ControlKind.Ride);
                    break;
                case "form":
                    if (StartControl(player, living, ControlKind.Form))
                    {
                        Driver d = DriverOf(player);
                        if (d != null)
                        {
                            d.OwnsAnimal = false;
                        }
                    }
                    break;
                case "tame":
                    TameAnimal(target, player);
                    break;
                case "release":
                    if (RecordOf(target) != null)
                    {
                        ReleaseAnimal(target);
                    }
                    break;
                case "kill":
                    living.Hurt(living.health + 100000f, Rust.DamageType.Generic, player, false);
                    break;
                case "despawn":
                    Despawn(target);
                    break;
            }
            Log(2, player.displayName + " used the aim tool '" + what + "' on " + target.ShortPrefabName);
        }

        // ---------------------------------------------------------------- the bag of an animal form
        // What an animal picks up (or is given) goes into its own bag, not into the human inventory. The bag stays with the animal when the player
        // leaves it (X); as a human the owner opens it with Ctrl + E at the animal.

        internal readonly Dictionary<ulong, List<ItemRecord>> FormBags = new Dictionary<ulong, List<ItemRecord>>();

        internal bool AddToBag(BasePlayer player, Item item)
        {
            List<ItemRecord> bag;
            if (!FormBags.TryGetValue(player.userID, out bag))
            {
                bag = new List<ItemRecord>();
                FormBags[player.userID] = bag;
            }
            ItemRecord same = bag.FirstOrDefault(r => r.Shortname == item.info.shortname && r.Skin == item.skin && item.info.stackable > r.Amount + item.amount - 1 && r.Condition < 0f);
            if (same != null)
            {
                same.Amount += item.amount;
                item.Remove();
                return true;
            }
            for (int slot = 0; slot < Cfg.Gear.BagSlots; slot++)
            {
                if (bag.All(r => r.Slot != slot))
                {
                    bag.Add(RecordFromItem(item, slot));
                    item.Remove();
                    return true;
                }
            }
            return false;
        }

        internal void OpenFormBag(BasePlayer player, LeftForm lf)
        {
            var contents = lf.Bag.Select(r => new KeyValuePair<int, ItemRecord>(r.Slot, r)).ToList();
            VirtualBox vb = OpenBox(player, "formbag", Cfg.Gear.BagSlots, contents, lf, (box, item, slot) => slot >= 0, null, box => FormBagClosed(box, lf));
            if (vb != null)
            {
                TellRaw(player, "The bag of your " + (lf.Animal != null ? SpeciesOf(lf.Animal) != null ? SpeciesOf(lf.Animal).Display.ToLowerInvariant() : "animal" : "animal") + ".");
            }
        }

        private void FormBagClosed(VirtualBox vb, LeftForm lf)
        {
            lf.Bag.Clear();
            foreach (Item item in vb.Box.inventory.itemList.ToList())
            {
                lf.Bag.Add(RecordFromItem(item, item.position));
                item.Remove();
            }
        }

        // ---------------------------------------------------------------- giving an item to a player who is an animal (it goes into his bag)

        internal void GiveCommand(BasePlayer player, Action<string> reply)
        {
            BaseEntity target = (BaseEntity)AnimalInSight(player, 6f, 30f) ?? HorseInSight(player, 6f, 30f);
            Driver host = target != null ? DriverOfAnimal(target) : null;
            Item held = player.GetActiveItem();
            if (host == null || host.Kind != ControlKind.Form || host.Player == player)
            {
                reply("Look at an animal that is a player (within 6 m) and hold the item you want to give.");
                return;
            }
            if (held == null)
            {
                reply("Hold the item you want to give.");
                return;
            }
            string name = held.info.displayName.english;
            if (!AddToBag(host.Player, held))
            {
                reply("Its bag is full.");
                return;
            }
            reply("You gave " + name + " to the " + host.Species.Display.ToLowerInvariant() + ".");
            TellRaw(host.Player, player.displayName + " put " + name + " in your bag.");
        }

        // ---------------------------------------------------------------- naming a tamed animal

        internal void RenameCommand(BasePlayer player, string[] args, Action<string> reply)
        {
            if (args.Length == 0)
            {
                reply("Usage: /bm name <new name> while you look at an animal of yours.");
                return;
            }
            BaseEntity target = (BaseEntity)AnimalInSight(player, 14f, 25f) ?? HorseInSight(player, 14f, 25f);
            TamedRecord rec = target != null ? RecordOf(target) : null;
            if (rec == null || !MayUse(player, rec.Owner, "owner"))
            {
                reply("Look at a tamed animal of yours first.");
                return;
            }
            string name = string.Join(" ", args).Trim();
            SetAnimalName(target, rec, name);
            reply("It is called " + rec.Name + " now.");
        }

        internal void SetAnimalName(BaseEntity e, TamedRecord rec, string name)
        {
            name = TagRegex.Replace(name ?? string.Empty, string.Empty).Trim();
            if (name.Length > 24)
            {
                name = name.Substring(0, 24);
            }
            rec.Name = name;
            LivestockAnimal ls = e as LivestockAnimal;
            if (ls != null)
            {
                ls.AnimalName = name;
                ls.SendNetworkUpdate();
            }
        }

        internal sealed class PlayModule : Module
        {
            public override int Order => 12;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Subscribe("OnPlayerRespawned");
            }

            public override void OnPlayerConnected(BasePlayer player)
            {
                BasePlayer p = player;
                Bm.timer.Once(6f, () => Bm.SendBinds(p));
                Bm.timer.Once(9f, () => Bm.JoinHint(p));
                Bm.timer.Once(5f, () => { if (p != null && Bm.DriverOf(p) == null) Bm.HudSwitch(p, true); });       // a HUD left off by an earlier session comes back
            }

            public override void OnPlayerRespawned(BasePlayer player)
            {
                BasePlayer p = player;
                Bm.timer.Once(3f, () => Bm.SendBinds(p));
            }

            private readonly Dictionary<ulong, float> nextCombo = new Dictionary<ulong, float>();

            // Ctrl + R opens the menu without any key bind (empty hands, or while riding or in animal form).
            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (!input.WasJustPressed(BUTTON.RELOAD) || !input.IsDown(BUTTON.DUCK))
                {
                    return null;
                }
                if (Bm.DriverOf(player) == null && player.GetActiveItem() != null)
                {
                    return null;
                }
                float last;
                if (nextCombo.TryGetValue(player.userID, out last) && Time.time < last)
                {
                    return null;
                }
                nextCombo[player.userID] = Time.time + 0.6f;
                Bm.OpenMenu(player, "home");
                return null;
            }

            public override void OnServerInitialized()
            {
                foreach (BasePlayer p in BasePlayer.activePlayerList.ToList())
                {
                    BasePlayer target = p;
                    Bm.timer.Once(2f, () => Bm.SendBinds(target));
                }
            }

            public override void OnDisable()
            {
                Cleanup();
            }

            public override void Unload()
            {
                Cleanup();
            }

            private void Cleanup()
            {
                foreach (BasePlayer p in BasePlayer.activePlayerList.ToList())
                {
                    if (p != null && p.IsConnected)
                    {
                        Bm.ClearKeyHints(p);
                        Bm.ClearInventoryCard(p);
                        Bm.ClearNightVision(p);
                    }
                }
            }

            private float nextRedraw;

            // The hints are drawn when the control starts; they are sent again now and then in case the player's game dropped its UI.
            public override void Tick(float now)
            {
                if (now < nextRedraw || Bm.Drivers.Count == 0)
                {
                    return;
                }
                nextRedraw = now + 20f;
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d != null && d.Alive && d.Player != null)
                    {
                        Bm.DrawKeyHints(d.Player);
                        Bm.DrawInventoryCard(d);
                    }
                }
            }
        }
    }

    // ======================================== Ritual.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- the transformation ritual (R9.1 - R9.12)
        // 1. kill a wild animal, the body stays   2. circle it with a lit torch (10 times, 50 for a boss)   3. a low ring of flames burns round the body
        // for 10 seconds - press E at it: half of the current health and bleeding are the price   4. you become that animal, with the same look, at the body.
        // If nobody presses E in time, the fire and the body vanish. The ritual is silent: nothing about it is announced in the chat.

        internal sealed class RitualCorpse
        {
            public SpeciesDef Species;
            public ulong Killer;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Died;
            public BaseEntity Animal;           // the dying animal: its death animation runs before the game drops the body
            public BaseCorpse Corpse;
            public bool Linked;                 // a body belongs to the record (it is never "unlinked" again: a vanished body ends the ritual)
            public bool StandIn;
            public string Name = string.Empty;
            public int Genes;
            public bool Male;
            public bool Burning;
            public float BurnUntil;
            public ulong Performer;
            public BossInfo Boss;               // the body of a boss: 50 circles instead of 10, and the player is reborn as a boss
            public readonly List<BaseEntity> Flames = new List<BaseEntity>();
            public readonly Dictionary<ulong, Circle> Circles = new Dictionary<ulong, Circle>();
        }

        internal sealed class Circle
        {
            public float Last;          // last angle around the body (rad)
            public float Total;         // swept angle (rad), the sign is the direction
            public float Seen;          // time of the last counted step
            public bool Started;
            public int Announced;
        }

        internal readonly List<RitualCorpse> RitualCorpses = new List<RitualCorpse>();

        internal bool IsRitualCorpse(BaseEntity entity)
        {
            return entity != null && RitualCorpses.Any(r => r.Corpse == entity);
        }

        internal sealed class RitualModule : Module
        {
            public override int Order => 45;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Subscribe("OnEntitySpawned");
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["RitualTooWeak"] = "You are too weak for the blood sacrifice.",
                    ["RitualCooldown"] = "You can transform again in {0} s.",
                    ["RitualSafeZone"] = "No rituals in a safe zone.",
                    ["RitualNotYours"] = "This body is not yours: it has to be an animal you (or your team) killed.",
                    ["RitualOff"] = "The transformation ritual is switched off on this server.",
                };
            }

            public override void OnDisable()
            {
                Cleanup(true);
            }

            public override void Unload()
            {
                Cleanup(true);
            }

            private void Cleanup(bool all)
            {
                foreach (RitualCorpse rc in Bm.RitualCorpses.ToList())
                {
                    if (all || Time.time - rc.Died > Bm.Cfg.Ritual.CorpseKeepSeconds)
                    {
                        Remove(rc);
                    }
                }
            }

            internal void KillFlames(RitualCorpse rc)
            {
                foreach (BaseEntity flame in rc.Flames)
                {
                    if (flame != null && !flame.IsDestroyed)
                    {
                        flame.Kill();
                    }
                }
                rc.Flames.Clear();
            }

            // vanish = the fire went out without a transformation: the body is gone with it.
            private void Remove(RitualCorpse rc, bool vanish = false)
            {
                Bm.RitualCorpses.Remove(rc);
                KillFlames(rc);
                if (!Valid(rc.Corpse))
                {
                    return;
                }
                if (rc.StandIn || vanish)
                {
                    Bm.FormSmoke(rc.Corpse.transform.position);
                    rc.Corpse.Kill();
                    return;
                }
                // an ordinary body again: it can be harvested as usual
                rc.Corpse.resourceDispenser = rc.Corpse.GetComponent<ResourceDispenser>();
            }

            // Harvesting the body would cancel the ritual (R9.35): with the dispenser switched off a tool hit lands as plain damage,
            // and that damage is refused below.
            private static void Protect(BaseCorpse corpse)
            {
                if (corpse != null && corpse.resourceDispenser != null)
                {
                    corpse.resourceDispenser = null;
                }
            }

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                if (Bm.RitualCorpses.Count == 0 || !(entity is BaseCorpse) || !Bm.IsRitualCorpse(entity))
                {
                    return null;
                }
                return true;
            }

            // The killer's animal leaves a body that stays; the look of the animal is remembered for the transformation.
            public override void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            {
                if (!Bm.Cfg.Features.Transformation)
                {
                    return;
                }
                BaseNPC2 animal = entity as BaseNPC2;
                RidableHorse horse = entity as RidableHorse;
                if (animal == null && horse == null)
                {
                    return;
                }
                SpeciesDef species = SpeciesOf(entity);
                if (species == null || !species.Transform || !Bm.SpeciesEnabled(species) || Bm.IsTamed(entity) || Bm.DriverOfAnimal(entity) != null)
                {
                    return;
                }
                BossInfo boss = Bm.BossOf(entity);
                BasePlayer killer = info != null ? (info.InitiatorPlayer ?? info.Initiator as BasePlayer) : null;
                if (killer == null && info != null && info.Initiator != null)
                {
                    Driver byForm = Bm.DriverOfAnimal(info.Initiator);
                    killer = byForm != null ? byForm.Player : null;          // killed by the animal body of a player
                }
                if (killer == null || killer.IsNpc)
                {
                    return;
                }
                bool allowed = species.TransformRestricted ? Bm.Has(killer, PermTransformRestricted) : Bm.Has(killer, PermTransform);
                if (!allowed)
                {
                    return;
                }
                var rc = new RitualCorpse { Species = species, Killer = killer.userID, Animal = entity, Position = entity.transform.position, Rotation = entity.transform.rotation, Died = Time.time };
                if (boss != null)
                {
                    rc.Boss = new BossInfo { Kind = boss.Kind, Mini = boss.Mini };
                }
                LivestockAnimal livestock = animal as LivestockAnimal;
                if (livestock != null)
                {
                    rc.Name = livestock.AnimalName;
                    rc.Genes = livestock.Genes;
                    rc.Male = livestock.IsMale;
                }
                else if (horse != null)
                {
                    rc.Genes = Mathf.Max(0, Array.IndexOf(horse.breeds, horse.GetBreed()));
                }
                Bm.RitualCorpses.Add(rc);
            }

            // The game drops the body after the death animation, with the dead animal as its parent: that body belongs to the record
            // and is kept for the ritual.
            public override void OnEntitySpawned(BaseNetworkable entity)
            {
                BaseCorpse corpse = entity as BaseCorpse;
                if (corpse == null || Bm.RitualCorpses.Count == 0)
                {
                    return;
                }
                foreach (RitualCorpse rc in Bm.RitualCorpses)
                {
                    if (rc.Linked)
                    {
                        continue;
                    }
                    bool mine = rc.Animal != null && corpse.parentEnt == rc.Animal;
                    if (!mine && !Valid(rc.Animal) && Time.time - rc.Died < 20f && Vector3.Distance(rc.Position, corpse.transform.position) < 8f)
                    {
                        mine = true;      // the dead animal is already gone: the nearest body in time and space
                    }
                    if (mine)
                    {
                        HorseCorpse horseBody = corpse as HorseCorpse;
                        if (horseBody != null)
                        {
                            rc.Genes = horseBody.breedIndex;
                        }
                        rc.Corpse = corpse;
                        rc.Linked = true;
                        rc.Position = corpse.transform.position;
                        corpse.ResetRemovalTime(Bm.Cfg.Ritual.CorpseKeepSeconds);
                        Protect(corpse);
                        return;
                    }
                }
            }

            // A creature that leaves no body in the game (the small ones) gets a stand-in where it lay down.
            public override void OnEntityKill(BaseNetworkable entity)
            {
                if (Bm.RitualCorpses.Count == 0 || !Bm.Cfg.Features.Transformation)
                {
                    return;
                }
                foreach (RitualCorpse rc in Bm.RitualCorpses)
                {
                    if (rc.Animal == entity && !rc.Linked)
                    {
                        rc.Position = entity.transform.position;
                        rc.Rotation = entity.transform.rotation;
                        rc.Animal = null;
                        MakeStandIn(rc);
                        return;
                    }
                }
            }

            public override void Tick(float now)
            {
                if (!Bm.Cfg.Features.Transformation || Bm.RitualCorpses.Count == 0)
                {
                    return;
                }
                foreach (RitualCorpse rc in Bm.RitualCorpses.ToList())
                {
                    if (now - rc.Died > Bm.Cfg.Ritual.CorpseKeepSeconds)
                    {
                        Remove(rc);
                        continue;
                    }
                    if (!rc.Linked)
                    {
                        if (Valid(rc.Animal))
                        {
                            rc.Position = rc.Animal.transform.position;     // the death animation may carry it a few steps
                            rc.Rotation = rc.Animal.transform.rotation;
                            if (now - rc.Died > 30f)
                            {
                                Remove(rc);
                            }
                        }
                        else if (now - rc.Died > 4f)
                        {
                            MakeStandIn(rc);      // the animal vanished without leaving a body
                            if (!rc.Linked)
                            {
                                Remove(rc);
                            }
                        }
                        continue;
                    }
                    if (!Valid(rc.Corpse))
                    {
                        Remove(rc);               // harvested some other way, despawned or destroyed: the ritual is off (R9.10)
                        continue;
                    }
                    Protect(rc.Corpse);           // the game may fill the dispenser in after the spawn hook
                    if (rc.Burning)
                    {
                        if (now > rc.BurnUntil)
                        {
                            Remove(rc, true);              // nobody pressed E: the fire and the body vanish
                        }
                        continue;
                    }
                    Circling(rc, now);
                }
            }

            // The species' own body prefab if the game has one, else the chicken's: it is only the marker of the ritual spot.
            private void MakeStandIn(RitualCorpse rc)
            {
                string path = Bm.FindPrefabPath("rust.ai/agents/" + rc.Species.Id + "/", ".corpse.prefab") ?? Bm.FindPrefabPath("rust.ai/agents/chicken/", ".corpse.prefab");
                if (path == null)
                {
                    Bm.Log(1, "Ritual: no body prefab for the stand-in of the " + rc.Species.Id);
                    return;
                }
                BaseCorpse corpse = GameManager.server.CreateEntity(path, rc.Position, rc.Rotation) as BaseCorpse;
                if (corpse == null)
                {
                    Bm.Log(1, "Ritual: the stand-in body " + path + " could not be created");
                    return;
                }
                Bm.Log(3, "Ritual: stand-in body " + path + " for the " + rc.Species.Id);
                corpse.Spawn();
                corpse.ResetRemovalTime(Bm.Cfg.Ritual.CorpseKeepSeconds);
                Protect(corpse);
                rc.Corpse = corpse;
                rc.Linked = true;
                rc.StandIn = true;
            }

            // Walking around the body with a lit torch is counted by the angle the player sweeps around it. Returns the full circles so far.
            // The walk has to be done in one go (30 s without progress start it over); a jump in the angle (lag, teleport) is not walking.
            internal static int AdvanceCircle(Circle c, Vector3 center, Vector3 at, float radius, bool lit, float now)
            {
                Vector3 delta = at - center;
                delta.y = 0f;
                float dist = delta.magnitude;
                if (c.Total != 0f && now - c.Seen > 30f)
                {
                    c.Total = 0f;
                    c.Announced = 0;
                    c.Started = false;
                }
                if (!lit || dist < 1.2f || dist > radius + 2.5f)
                {
                    c.Started = false;
                    if (!lit || dist > radius + 5f)
                    {
                        c.Total = 0f;
                        c.Announced = 0;
                    }
                }
                else
                {
                    float angle = Mathf.Atan2(delta.z, delta.x);
                    if (!c.Started)
                    {
                        c.Started = true;
                        c.Last = angle;
                    }
                    else
                    {
                        float step = Mathf.DeltaAngle(c.Last * Mathf.Rad2Deg, angle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                        c.Last = angle;
                        if (Mathf.Abs(step) <= 1.2f)
                        {
                            c.Total += step;
                            c.Seen = now;
                        }
                    }
                }
                return Mathf.FloorToInt(Mathf.Abs(c.Total) / (2f * Mathf.PI));
            }

            private void Circling(RitualCorpse rc, float now)
            {
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || Bm.DriverOf(p) != null || !Eligible(rc, p))
                    {
                        continue;
                    }
                    TorchWeapon torch = p.GetHeldEntity() as TorchWeapon;
                    bool lit = torch != null && torch.HasFlag(BaseEntity.Flags.On);
                    Circle c;
                    if (!rc.Circles.TryGetValue(p.userID, out c))
                    {
                        c = new Circle();
                        rc.Circles[p.userID] = c;
                    }
                    int done = AdvanceCircle(c, rc.Position, p.transform.position, Bm.Cfg.Ritual.Radius, lit, now);
                    if (done >= (rc.Boss != null ? Bm.Cfg.Ritual.BossCircles : Bm.Cfg.Ritual.Circles))
                    {
                        Ignite(rc, p, now);
                        return;
                    }
                }
            }

            private bool Eligible(RitualCorpse rc, BasePlayer p)
            {
                if (!Bm.Cfg.Ritual.RequireOwnKill || rc.Killer == p.userID)
                {
                    return true;
                }
                BasePlayer killer = BasePlayer.FindByID(rc.Killer);
                return killer != null && Bm.SameParty(killer, p);
            }

            private void Ignite(RitualCorpse rc, BasePlayer p, float now)
            {
                rc.Burning = true;
                rc.BurnUntil = now + Mathf.Max(3f, Bm.Cfg.Ritual.BurnSeconds);
                rc.Performer = p.userID;
                rc.Circles.Clear();
                SpawnFlames(rc);
            }

            // A low, thin ring of small flames round the body (real fire: it burns whoever stands in it).
            internal void SpawnFlames(RitualCorpse rc)
            {
                RitualCfg cfg = Bm.Cfg.Ritual;
                string path = cfg.FirePrefab;
                if (string.IsNullOrEmpty(path) || GameManager.server.FindPrefab(path) == null)
                {
                    path = Bm.FindPrefabPath("oilfireballsmall") ?? Bm.FindPrefabPath("fireball_small");
                }
                if (path == null)
                {
                    Bm.RunEffect("assets/bundled/prefabs/fx/fire/fire_v3.prefab", rc.Position + Vector3.up * 0.2f);
                    return;
                }
                int count = Mathf.Clamp(cfg.FireCount, 3, 24);
                for (int i = 0; i < count; i++)
                {
                    float a = i * Mathf.PI * 2f / count;
                    Vector3 pos = rc.Position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * cfg.FireRadius;
                    pos.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(pos), rc.Position.y - 0.4f) + 0.04f;
                    FireBall flame = GameManager.server.CreateEntity(path, pos, Quaternion.identity) as FireBall;
                    if (flame == null)
                    {
                        continue;
                    }
                    flame.enableSaving = false;
                    flame.lifeTimeMin = flame.lifeTimeMax = cfg.BurnSeconds + 0.5f;
                    flame.tickRate = 0.5f;
                    flame.damagePerSecond = cfg.FireDamage;
                    flame.radius = 0.55f;
                    flame.Spawn();
                    flame.CancelInvoke(flame.TryToSpread);            // the flames stay where they are: no sparks fly off
                    Rigidbody body = flame.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        body.isKinematic = true;
                        body.useGravity = false;
                    }
                    Bm.ScaleModel(flame, cfg.FireScale);
                    rc.Flames.Add(flame);
                }
            }

            // E next to the burning body: the blood sacrifice and the transformation.
            public override object OnPlayerInput(BasePlayer player, InputState input)
            {
                if (!input.WasJustPressed(BUTTON.USE) || Bm.RitualCorpses.Count == 0 || Bm.DriverOf(player) != null)
                {
                    return null;
                }
                foreach (RitualCorpse rc in Bm.RitualCorpses)
                {
                    if (rc.Burning && Vector3.Distance(player.transform.position, rc.Position) < 3.8f && (rc.Performer == player.userID || Eligible(rc, player)))
                    {
                        Bm.PerformTransformation(player, rc);
                        break;
                    }
                }
                return null;
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Ritual: " + Bm.RitualCorpses.Count + " eligible bodies");
            }
        }

        // The blood sacrifice and the rebirth as the animal that was killed.
        internal void PerformTransformation(BasePlayer player, RitualCorpse rc)
        {
            if (!Cfg.Features.Transformation || !Cfg.Features.AnimalForm)
            {
                Tell(player, "RitualOff");
                return;
            }
            bool allowed = rc.Species.TransformRestricted ? Has(player, PermTransformRestricted) : Has(player, PermTransform);
            if (!allowed)
            {
                Tell(player, "NoPermission");
                return;
            }
            long until;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Data.RitualCooldown.TryGetValue(player.userID, out until) && until > now && !IsAdmin(player))
            {
                Tell(player, "RitualCooldown", until - now);
                return;
            }
            if (Cfg.Ritual.BlockInSafeZone && player.InSafeZone())
            {
                Tell(player, "RitualSafeZone");
                return;
            }
            if (player.health * (1f - Cfg.Ritual.HealthCost) < 4f)
            {
                Tell(player, "RitualTooWeak");
                return;
            }
            // the price
            player.health = Mathf.Max(1f, player.health * (1f - Cfg.Ritual.HealthCost));
            player.metabolism.bleeding.value = Mathf.Max(player.metabolism.bleeding.value, Cfg.Ritual.Bleeding);
            player.metabolism.isDirty = true;
            FormSmoke(rc.Position);
            // the animal of the same kind and look, at the body
            Vector3 pos = rc.Position;
            pos.y = Mathf.Max(pos.y, TerrainMeta.HeightMap.GetHeight(pos)) + 0.2f;
            BaseCombatEntity animal = GameManager.server.CreateEntity(rc.Species.Prefab, pos, rc.Rotation) as BaseCombatEntity;
            if (animal == null)
            {
                Tell(player, "RitualOff");
                return;
            }
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock != null)
            {
                if (!string.IsNullOrEmpty(rc.Name))
                {
                    livestock.AnimalName = rc.Name;
                }
                livestock.Genes = rc.Genes;
                livestock.IsMale = rc.Male;
            }
            RidableHorse reborn = animal as RidableHorse;
            if (rc.Boss != null)
            {
                ulong tag = NewTag();
                animal.skinID = tag;
                Data.Bosses[tag] = new BossInfo { Kind = rc.Boss.Kind, Mini = rc.Boss.Mini, Tamed = true };
            }
            animal.Spawn();
            if (reborn != null)
            {
                reborn.SetBreed(rc.Genes);
            }
            if (rc.Boss != null && Bosses != null)
            {
                Bosses.Setup(animal, Data.Bosses[animal.skinID], true);     // the size, the strength, the glow of a boss
            }
            RitualCorpses.Remove(rc);
            foreach (BaseEntity flame in rc.Flames)
            {
                if (flame != null && !flame.IsDestroyed)
                {
                    flame.Kill();
                }
            }
            rc.Flames.Clear();
            if (Valid(rc.Corpse))
            {
                rc.Corpse.Kill();
            }
            SpeciesDef species = rc.Species;
            // the agent needs a moment to settle on the navigation mesh
            timer.Once(0.25f, () =>
            {
                if (!Valid(animal) || player == null || !player.IsConnected || player.IsDead())
                {
                    if (Valid(animal)) animal.Kill();
                    return;
                }
                if (StartControl(player, animal, ControlKind.Form, true))
                {
                    Driver d = DriverOf(player);
                    if (d != null)
                    {
                        d.OwnsAnimal = true;
                    }
                }
                else
                {
                    animal.Kill();
                }
            });
        }

        // Headless test: the circle counting on synthetic walks, then a real animal body is linked, protected and released again.
        internal void TestRitual(Action<string> reply, Vector3 pos, string only = null)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            Vector3 center = Vector3.zero;
            float r = Cfg.Ritual.Radius;
            int circles = Cfg.Ritual.Circles;
            Func<float, Vector3> onRing = a => center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            // a lit walk of three full circles counts, and not before
            var c = new Circle();
            int done = 0, at = -1, tick = 0;
            for (float a = 0f; a < (circles + 0.3f) * 2f * Mathf.PI; a += 0.3f)
            {
                done = RitualModule.AdvanceCircle(c, center, onRing(a), r, true, ++tick * 0.25f);
                if (done >= circles && at < 0) at = tick;
            }
            check("lit walk counts " + circles + " circles (reached at tick " + at + ")", at > 0 && at > (int)(circles * 2f * Mathf.PI / 0.3f) - 2);
            // opposite direction counts too
            c = new Circle(); done = 0; tick = 0;
            for (float a = 0f; a > -(circles + 0.2f) * 2f * Mathf.PI; a -= 0.3f) done = RitualModule.AdvanceCircle(c, center, onRing(a), r, true, ++tick * 0.25f);
            check("the other direction counts too", done >= circles);
            // an unlit torch, a far ring, standing still and teleport jumps never count
            c = new Circle(); done = 0; tick = 0;
            for (float a = 0f; a < 4f * 2f * Mathf.PI; a += 0.3f) done = RitualModule.AdvanceCircle(c, center, onRing(a), r, false, ++tick * 0.25f);
            check("unlit torch counts nothing", done == 0);
            c = new Circle(); done = 0; tick = 0;
            for (float a = 0f; a < 4f * 2f * Mathf.PI; a += 0.3f) done = RitualModule.AdvanceCircle(c, center, center + new Vector3(Mathf.Cos(a) * (r + 6f), 0f, Mathf.Sin(a) * (r + 6f)), r, true, ++tick * 0.25f);
            check("a far ring counts nothing", done == 0);
            c = new Circle(); done = 0; tick = 0;
            for (int i = 0; i < 200; i++) done = RitualModule.AdvanceCircle(c, center, onRing(0.7f), r, true, ++tick * 0.25f);
            check("standing still counts nothing", done == 0);
            c = new Circle(); done = 0; tick = 0;
            for (int i = 0; i < 200; i++) done = RitualModule.AdvanceCircle(c, center, onRing(i % 2 == 0 ? 0f : 2.2f), r, true, ++tick * 0.25f);
            check("jumping between spots counts nothing", done == 0);
            // half a walk, a long pause, another half: starts over
            c = new Circle(); done = 0; tick = 0;
            float now = 0f;
            for (float a = 0f; a < 1.6f * 2f * Mathf.PI; a += 0.3f) done = RitualModule.AdvanceCircle(c, center, onRing(a), r, true, now += 0.25f);
            now += 40f;
            for (float a = 0f; a < 1.6f * 2f * Mathf.PI; a += 0.3f) done = RitualModule.AdvanceCircle(c, center, onRing(a), r, true, now += 0.25f);
            check("a long pause starts the walk over", done < circles);
            // real bodies of real animals, one after the other: a cow leaves a body, a rabbit leaves none (a stand-in takes its place)
            Action<string, Vector3, bool, Action> bodyTest = null;
            bodyTest = (speciesId, at2, expectStandIn, next) =>
            {
                SpeciesDef sp = SpeciesById(speciesId);
                if (sp == null || !SpeciesEnabled(sp))
                {
                    lines.Add("SKIP " + speciesId + " (not available in this build)");
                    next();
                    return;
                }
                at2.y = TerrainMeta.HeightMap.GetHeight(at2) + 0.2f;
                BaseNPC2 animal = GameManager.server.CreateEntity(sp.Prefab, at2, Quaternion.identity) as BaseNPC2;
                if (animal == null)
                {
                    check(speciesId + ": the test animal spawns", false);
                    next();
                    return;
                }
                animal.Spawn();
                timer.Once(1.0f, () =>
                {
                    var rc = new RitualCorpse { Species = sp, Killer = 0UL, Animal = animal, Position = animal.transform.position, Rotation = animal.transform.rotation, Died = Time.time };
                    RitualCorpses.Add(rc);
                    animal.Hurt(animal.health + 50f, Rust.DamageType.Bullet);
                    timer.Once(9f, () =>
                    {
                        if (expectStandIn && Valid(animal))
                        {
                            animal.Kill();      // no player is near, so the critter's FSM sleeps: the end of its death animation is simulated
                        }
                        timer.Once(0.6f, () =>
                    {
                        bool linked = rc.Linked && rc.Corpse != null && Valid(rc.Corpse);
                        check(speciesId + ": the body is linked to the record" + (expectStandIn ? " (stand-in expected)" : "") + " [linked " + rc.Linked + ", stand-in " + rc.StandIn + ", animal valid " + Valid(rc.Animal) + ", animal dead " + (animal != null && !animal.IsDestroyed && animal.IsDead()) + "]", linked && rc.StandIn == expectStandIn);
                        if (linked)
                        {
                            check(speciesId + ": the body is protected from harvesting (dispenser off)", rc.Corpse.resourceDispenser == null);
                            float before = rc.Corpse.health;
                            rc.Corpse.Hurt(30f, Rust.DamageType.Slash);
                            check(speciesId + ": damage to the body is refused (" + before + " -> " + rc.Corpse.health + ")", Mathf.Approximately(before, rc.Corpse.health));
                        }
                        rc.Died = Time.time - Cfg.Ritual.CorpseKeepSeconds - 5f;       // the ritual window is over
                        timer.Once(1.0f, () =>
                        {
                            check(speciesId + ": the record is dropped when the window ends", !RitualCorpses.Contains(rc));
                            if (rc.Corpse != null && Valid(rc.Corpse))
                            {
                                if (rc.StandIn)
                                {
                                    check(speciesId + ": the stand-in is removed with the record", false);
                                    rc.Corpse.Kill();
                                }
                                else
                                {
                                    check(speciesId + ": the body is an ordinary body again (dispenser back)", rc.Corpse.resourceDispenser != null || rc.Corpse.GetComponent<ResourceDispenser>() == null);
                                    rc.Corpse.Kill();
                                }
                            }
                            else if (rc.StandIn)
                            {
                                check(speciesId + ": the stand-in is removed with the record", true);
                            }
                            next();
                        });
                    });
                    });
                });
            };
            Action report = () => Puts("[test] ritual:\n" + string.Join("\n", lines.ToArray()));
            Func<string, bool> hasBody = id => FindPrefabPath("rust.ai/agents/" + id + "/", ".corpse.prefab") != null;
            if (!string.IsNullOrEmpty(only))
            {
                bodyTest(only, pos, !hasBody(only), report);
            }
            else
            {
                bodyTest("cow", pos, false, () => bodyTest("rabbit", pos + new Vector3(5f, 0f, 0f), true, report));
            }
            reply("Ritual test started: the walk checks ran (" + lines.Count(l => l.StartsWith("PASS")) + " pass, " + lines.Count(l => l.StartsWith("FAIL")) + " fail); the body parts report to the log in ~25 s.");
        }
    }

    // ======================================== Species.cs
    public partial class Beastmaster
    {
        internal enum RideTier { None, Standard, Restricted }
        internal enum TameFlow { None, Bond, Torch, Predator, SleepFeed, Admin }
        internal enum Diet { None, Herbivore, Omnivore, Carnivore, Small }

        internal sealed class SpeciesDef
        {
            public string Id;
            public string Display;
            public string Short;                    // prefab file name without extension (lower case)
            public string Prefab;                   // resolved from the game manifest at start
            public RideTier Ride;
            public bool Gear;
            public TameFlow Tame;
            public bool Transform;
            public bool TransformRestricted;
            public float Walk, Sprint, Top;         // the brief's speed table (m/s)
            public string Trait = string.Empty;
            public Diet Food;
            public bool Predator;
            public bool Livestock;
            public bool Critter;
            public bool Male;                       // ram / bull
            public bool Polar;
            public float YawOffset;                 // body yaw relative to the direction of travel
            public bool Swimmer;                    // swims with a penalty instead of wading
            public bool DrownProof;
            public bool Unavailable;                // not in this game build
            public string Note = string.Empty;

            public bool Available => !Unavailable && !string.IsNullOrEmpty(Prefab);
        }

        internal static readonly List<SpeciesDef> AllSpecies = BuildSpecies();

        private static List<SpeciesDef> BuildSpecies()
        {
            var list = new List<SpeciesDef>();
            Func<string, string, string, RideTier, bool, TameFlow, bool, float, float, float, SpeciesDef> add =
                (id, display, shortName, ride, gear, tame, transform, walk, sprint, top) =>
                {
                    var s = new SpeciesDef { Id = id, Display = display, Short = shortName, Ride = ride, Gear = gear, Tame = tame, Transform = transform, Walk = walk, Sprint = sprint, Top = top };
                    list.Add(s);
                    return s;
                };

            SpeciesDef d;
            d = add("horse", "Horse", "ridablehorse", RideTier.Standard, true, TameFlow.Bond, true, 5.5f, 14.5f, 19f);   // the horse form drives the physics vehicle
            d.Trait = "Best handling, smooth obstacle jumps."; d.Food = Diet.Herbivore; d.Livestock = true; d.Swimmer = false;
            d = add("cow", "Cow", "cow", RideTier.Standard, true, TameFlow.Bond, true, 3f, 8f, 10.5f);
            d.Trait = "Slow, heavy loot hauler."; d.Food = Diet.Herbivore; d.Livestock = true;
            d = add("bull", "Bull", "bull", RideTier.Standard, true, TameFlow.Bond, true, 4.2f, 11.5f, 13.5f);
            d.Trait = "Sprinting breaks wooden barricades."; d.Food = Diet.Herbivore; d.Livestock = true; d.Male = true;
            d = add("calf", "Calf", "calf", RideTier.None, false, TameFlow.None, true, 3f, 8f, 10.5f);
            d.Food = Diet.Herbivore; d.Livestock = true;
            d = add("sheep", "Sheep", "sheep", RideTier.Standard, true, TameFlow.Bond, true, 3f, 8f, 10.5f);
            d.Trait = "Slow, heavy loot hauler."; d.Food = Diet.Herbivore; d.Livestock = true;
            d = add("ram", "Ram", "sheep", RideTier.Standard, true, TameFlow.Bond, true, 4.2f, 11.5f, 13.5f);
            d.Trait = "Sprinting breaks wooden barricades."; d.Food = Diet.Herbivore; d.Livestock = true; d.Male = true;
            d = add("lamb", "Lamb", "lamb", RideTier.None, false, TameFlow.None, true, 3f, 8f, 10.5f);
            d.Food = Diet.Herbivore; d.Livestock = true;
            d = add("stag", "Stag", "stag", RideTier.Standard, true, TameFlow.SleepFeed, true, 5f, 14f, 17.5f);
            d.Trait = "Silent run: hoof sounds reduced by 90 %."; d.Food = Diet.Herbivore; d.Swimmer = true;
            d = add("doe", "Doe", "doe", RideTier.Standard, true, TameFlow.SleepFeed, true, 5f, 14f, 17.5f);
            d.Unavailable = true; d.Note = "No doe prefab exists in this game build; the stag covers deer."; d.Food = Diet.Herbivore;
            d = add("boar", "Boar", "boar", RideTier.Standard, true, TameFlow.Predator, true, 4f, 11f, 14.5f);
            d.Trait = "Low centre of mass: harder to hit with projectiles from the side."; d.Food = Diet.Omnivore; d.Predator = true; d.Swimmer = true;
            d = add("bear", "Brown bear", "bear", RideTier.Standard, true, TameFlow.Predator, true, 3.5f, 10.5f, 13f);
            d.Trait = "Ignores slowdown in deep water and swamp."; d.Food = Diet.Omnivore; d.Predator = true; d.Swimmer = true;
            d = add("polarbear", "Polar bear", "polarbear", RideTier.Standard, true, TameFlow.Predator, true, 3.2f, 9.5f, 12.5f);
            d.Trait = "+15 % speed on snow and ice."; d.Food = Diet.Omnivore; d.Predator = true; d.Swimmer = true; d.Polar = true;
            d = add("wolf", "Wolf", "wolf2", RideTier.Standard, true, TameFlow.Torch, true, 4.8f, 12f, 14f);
            d.Trait = "Full speed in dense forest."; d.Food = Diet.Carnivore; d.Predator = true; d.Swimmer = true;
            d = add("panther", "Panther", "panther", RideTier.Standard, true, TameFlow.Predator, true, 5.8f, 15f, 18.5f);
            d.Trait = "Jumps onto rocks 25 % higher."; d.Food = Diet.Carnivore; d.Predator = true; d.Swimmer = true;
            d = add("tiger", "Tiger", "tiger", RideTier.Standard, true, TameFlow.Predator, true, 6f, 16.5f, 21.5f);
            d.Trait = "Fastest mount, slight drift in turns."; d.Food = Diet.Carnivore; d.Predator = true; d.Swimmer = true;
            d = add("crocodile", "Crocodile", "crocodile", RideTier.Restricted, false, TameFlow.Admin, true, 3f, 7f, 9f);
            d.Trait = "Water rocket: sprint in water 18 m/s."; d.Food = Diet.Carnivore; d.Predator = true; d.Swimmer = true; d.DrownProof = true; d.TransformRestricted = true;
            d = add("chicken", "Chicken", "chicken", RideTier.Restricted, false, TameFlow.Admin, true, 1.5f, 4.5f, 6f);
            d.Trait = "Tiny hitbox."; d.Food = Diet.Small; d.Critter = true; d.TransformRestricted = true;
            d = add("rabbit", "Rabbit", "rabbit", RideTier.Restricted, false, TameFlow.Admin, true, 1.5f, 4.5f, 6f);
            d.Trait = "Tiny hitbox."; d.Food = Diet.Small; d.Critter = true; d.TransformRestricted = true;
            d = add("squirrel", "Squirrel", "squirrel", RideTier.Restricted, false, TameFlow.Admin, true, 1.5f, 4.5f, 6f);
            d.Trait = "Tiny hitbox."; d.Food = Diet.Small; d.Critter = true; d.TransformRestricted = true;
            d = add("frog", "Frog", "frog", RideTier.Restricted, false, TameFlow.Admin, true, 2.5f, 5f, 7.5f);
            d.Trait = "Super jump (jump force x3), cannot drown."; d.Food = Diet.Small; d.Critter = true; d.Swimmer = true; d.DrownProof = true; d.TransformRestricted = true;
            d = add("crab", "Crab", "crab_single", RideTier.None, false, TameFlow.None, true, 1.5f, 4.5f, 6f);
            d.Trait = "Scuttles sideways, cannot drown."; d.Food = Diet.Small; d.Critter = true; d.YawOffset = 90f; d.Swimmer = true; d.DrownProof = true;
            d = add("snake", "Snake", "snake.entity", RideTier.Restricted, false, TameFlow.Admin, false, 2f, 6f, 8f);
            d.Unavailable = true; d.TransformRestricted = true; d.Note = "In this game build the snake is a hazard entity, not a creature the plugin can control.";
            return list;
        }

        internal static SpeciesDef SpeciesById(string id)
        {
            return AllSpecies.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        // The species of a live entity (ram = male sheep).
        internal static SpeciesDef SpeciesOf(BaseNetworkable entity)
        {
            if (entity == null)
            {
                return null;
            }
            if (entity is RidableHorse)
            {
                return SpeciesById("horse");
            }
            BaseEntity be = entity as BaseEntity;
            string shortName = be != null ? be.ShortPrefabName : null;
            if (string.IsNullOrEmpty(shortName))
            {
                return null;
            }
            shortName = shortName.ToLowerInvariant();
            if (shortName == "calfmale")
            {
                shortName = "calf";
            }
            if (shortName == "sheep")
            {
                LivestockAnimal livestock = entity as LivestockAnimal;
                return SpeciesById(livestock != null && livestock.IsMale ? "ram" : "sheep");
            }
            return AllSpecies.FirstOrDefault(s => s.Short == shortName);
        }

        // Matches the species to the real prefab paths of this game build.
        private void ResolveSpeciesPrefabs()
        {
            var entities = new List<string>();
            foreach (string path in GameManifest.Current.entities)
            {
                entities.Add(path.ToLowerInvariant());
            }
            foreach (SpeciesDef s in AllSpecies)
            {
                string suffix = "/" + s.Short + ".prefab";
                s.Prefab = entities.FirstOrDefault(p => p.EndsWith(suffix) && (p.Contains("rust.ai") || p.Contains("vehicles/horse") || p.Contains("agents")));
                if (s.Prefab == null)
                {
                    s.Prefab = entities.FirstOrDefault(p => p.EndsWith(suffix));
                }
                if (s.Prefab == null && !s.Unavailable)
                {
                    s.Unavailable = true;
                    s.Note = "Prefab not found in this game build.";
                    Log(1, "Species " + s.Id + ": prefab '" + s.Short + ".prefab' not found - disabled.");
                }
            }
        }

        // Speeds of a species after the config overrides.
        internal float SpeedScaleOf(SpeciesDef s)
        {
            SpeciesOverride o;
            return Cfg.Species.TryGetValue(s.Id, out o) && o.SpeedScale.HasValue ? o.SpeedScale.Value : 1f;
        }

        internal float YawOffsetOf(SpeciesDef s)
        {
            SpeciesOverride o;
            return Cfg.Species.TryGetValue(s.Id, out o) && o.YawOffset.HasValue ? o.YawOffset.Value : s.YawOffset;
        }

        // The third person camera hangs above the animal: how far above depends on how bulky the species is (the camera must not sit inside the body).
        private static readonly Dictionary<string, float> PivotFactors = new Dictionary<string, float>
        {
            { "horse", 1.18f }, { "cow", 1.45f }, { "bull", 1.4f }, { "calf", 1.35f }, { "sheep", 1.4f }, { "ram", 1.35f }, { "lamb", 1.35f },
            { "stag", 1.2f }, { "boar", 1.4f }, { "bear", 1.25f }, { "polarbear", 1.25f }, { "wolf", 1.4f }, { "panther", 1.35f }, { "tiger", 1.35f },
            { "crocodile", 2.2f }, { "chicken", 1.7f }, { "rabbit", 1.7f }, { "squirrel", 1.7f }, { "frog", 2.4f }, { "crab", 2.4f }
        };

        internal float PivotScaleOf(SpeciesDef s)
        {
            SpeciesOverride o;
            if (Cfg.Species.TryGetValue(s.Id, out o) && o.PivotScale.HasValue)
            {
                return o.PivotScale.Value;
            }
            float factor;
            return PivotFactors.TryGetValue(s.Id, out factor) ? factor : 1.25f;
        }

        internal float SeatLiftOf(SpeciesDef s)
        {
            SpeciesOverride o;
            return s != null && Cfg.Species.TryGetValue(s.Id, out o) && o.SeatLift.HasValue ? o.SeatLift.Value : 0f;
        }

        internal bool SpeciesEnabled(SpeciesDef s)
        {
            if (s == null || !s.Available)
            {
                return false;
            }
            SpeciesOverride o;
            return !(Cfg.Species.TryGetValue(s.Id, out o) && o.Enabled.HasValue && !o.Enabled.Value);
        }
    }

    // ======================================== Spray.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- spray can (R11)
        // Spraying an animal with a Spray Can rolls its look again: the horse's breed, the livestock's coat pattern and lineage markers.
        // The gameplay genes (milk, wool, speed ...) are never touched. The re-rolled look is the one the transformation ritual copies.

        private static readonly string[] SprayNames =
        {
            "Daisy", "Bella", "Clover", "Maple", "Poppy", "Willow", "Hazel", "Juniper", "Rosie", "Biscuit", "Pepper", "Ginger", "Olive", "Nutmeg",
            "Barley", "Copper", "Dusty", "Ember", "Flint", "Moss", "Sable", "Storm", "Thistle", "Tansy", "Bramble", "Cinder"
        };

        internal bool RerollLook(BaseEntity animal)
        {
            RidableHorse horse = animal as RidableHorse;
            if (horse != null)
            {
                if (horse.breeds == null || horse.breeds.Length < 2)
                {
                    return false;
                }
                int current = horse.GetBreed() != null ? Array.IndexOf(horse.breeds, horse.GetBreed()) : -1;
                int next;
                do
                {
                    next = UnityEngine.Random.Range(0, horse.breeds.Length);
                }
                while (next == current);
                horse.SetBreed(next);
                return true;
            }
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock != null)
            {
                string name;
                do
                {
                    name = SprayNames[UnityEngine.Random.Range(0, SprayNames.Length)] + (UnityEngine.Random.value < 0.5f ? string.Empty : " " + UnityEngine.Random.Range(2, 99));
                }
                while (name == livestock.AnimalName);
                livestock.AnimalName = name;
                int a = LivestockGenome.RollMarker();
                int b = LivestockGenome.RollMarker();
                if (b == a)
                {
                    b = (a + 1 + UnityEngine.Random.Range(0, 15)) % 16;
                }
                livestock.Genes = LivestockGenome.WithMarkers(livestock.Genes, a, b);
                return true;
            }
            return false;
        }

        // Headless test: the look of a cow and of a horse is rolled again; the gameplay genes stay.
        internal void TestSpray(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            SpeciesDef cowSp = SpeciesById("cow"), horseSp = SpeciesById("horse");
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.3f;
            LivestockAnimal cow = cowSp != null ? GameManager.server.CreateEntity(cowSp.Prefab, pos, Quaternion.identity) as LivestockAnimal : null;
            RidableHorse horse = horseSp != null ? GameManager.server.CreateEntity(horseSp.Prefab, pos + new Vector3(6f, 0f, 0f), Quaternion.identity) as RidableHorse : null;
            if (cow == null || horse == null)
            {
                reply("The cow or the horse is not available.");
                return;
            }
            cow.Spawn();
            horse.Spawn();
            timer.Once(1.5f, () =>
            {
                string nameBefore = cow.AnimalName;
                int genesBefore = cow.Genes;
                int rolled = 0;
                for (int i = 0; i < 6; i++)
                {
                    RerollLook(cow);
                    rolled += (cow.AnimalName != nameBefore || cow.Genes != genesBefore) ? 1 : 0;
                }
                check("the coat changes (name and markers)", rolled > 0 && cow.AnimalName != nameBefore);
                bool same = true;
                foreach (LivestockGene g in LivestockGenome.AllGenes)
                {
                    same &= LivestockGenome.GetAllele(genesBefore, g, 0) == LivestockGenome.GetAllele(cow.Genes, g, 0) && LivestockGenome.GetAllele(genesBefore, g, 1) == LivestockGenome.GetAllele(cow.Genes, g, 1);
                }
                check("the gameplay genes are untouched", same);
                int breed = Array.IndexOf(horse.breeds, horse.GetBreed());
                RerollLook(horse);
                int breed2 = Array.IndexOf(horse.breeds, horse.GetBreed());
                check("the horse gets another breed (" + breed + " -> " + breed2 + ")", breed2 != breed && breed2 >= 0);
                cow.Kill();
                horse.Kill();
                Puts("[test] spray:\n" + string.Join("\n", lines.ToArray()));
            });
            reply("Spray test started: the result comes to the log in ~3 s.");
        }

        internal sealed class SprayModule : Module
        {
            public override int Order => 80;

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["SprayDone"] = "The spray washes the old coat away: your {0} has a new look.",
                    ["SprayNot"] = "That animal cannot be re-rolled with the spray can.",
                };
            }

            // The spray can is about to make its decal: if an animal of the player's is at that spot the look is rolled instead.
            public override object OnSprayCreate(SprayCan spray, Vector3 position, Quaternion rotation)
            {
                if (!Bm.Cfg.Features.Spray || spray == null)
                {
                    return null;
                }
                BasePlayer player = spray.GetOwnerPlayer();
                if (player == null || !Bm.Has(player, PermSpray))
                {
                    return null;
                }
                BaseEntity animal = AnimalAt(position);
                if (animal == null)
                {
                    return null;
                }
                SpeciesDef species = SpeciesOf(animal);
                if (species == null || Bm.BossOf(animal) != null || !(animal is RidableHorse || animal is LivestockAnimal))
                {
                    Bm.Tell(player, "SprayNot");
                    return true;
                }
                TamedRecord rec = Bm.RecordOf(animal);
                if (Bm.Cfg.Spray.OwnerOnly && (rec == null || !Bm.MayUse(player, rec.Owner, "team")) && !Bm.IsAdmin(player))
                {
                    Bm.Tell(player, rec == null ? "NeedTame" : "NotYours");
                    return true;
                }
                if (!Bm.RerollLook(animal))
                {
                    Bm.Tell(player, "SprayNot");
                    return true;
                }
                Bm.FormSmoke(animal.transform.position);
                Bm.Tell(player, "SprayDone", species.Display.ToLowerInvariant());
                return true;
            }

            private static BaseEntity AnimalAt(Vector3 position)
            {
                var near = new List<BaseEntity>();
                Vis.Entities(position, 1.6f, near);
                BaseEntity best = null;
                float bestDist = 0.9f;
                foreach (BaseEntity e in near)
                {
                    if (!(e is BaseNPC2 || e is RidableHorse) || e.IsDestroyed)
                    {
                        continue;
                    }
                    float d = Vector3.Distance(e.ClosestPoint(position), position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = e;
                    }
                }
                return best;
            }
        }
    }

    // ======================================== Stats.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- the needs of an animal form (R9.22 - R9.26)
        // Health, Stamina, Hunger and Thirst of the animal are shown on a small panel. Eating and drinking restore them: the Use key at food on the
        // ground, at a corpse (carnivores), at water or at grass (herbivores), and the items of the inventory that are on the animal's diet.
        // The needs are gentle: nothing dies of hunger unless the server owner switches that on. The human body's own needs stay frozen.

        internal StatsModule Needs;

        internal sealed class FormStats
        {
            public float Hunger = 100f;
            public float Thirst = 100f;
            public float Stamina = 100f;
            public float NextBite;
            public float NextHud;
            public float HealBudget;            // health the animal may still gain this second from food (heal-per-second cap)
            public float HealWindow;
            public float LastSpend;
            public bool Saved;                  // the human's own calories and hydration, put back when the form ends
            public float HumanCalories, HumanHydration;
            public bool HudDrawn;
            public int HudValue;
            public float HudAt;
        }

        // The climate and the needs together slow an animal that is hungry, thirsty or worn out.
        internal float StatsSpeedFactor(BaseEntity animal)
        {
            return Needs != null ? Needs.SpeedFactor(animal) : 1f;
        }

        // The highest gait a worn-out animal can keep up (stamina below 5 % = a jog at most).
        internal int StatsGaitCap(Driver d)
        {
            return Needs != null ? Needs.GaitCap(d) : 5;
        }

        internal void StatsSpend(Driver d, float amount)
        {
            if (Needs != null)
            {
                Needs.Spend(d, amount);
            }
        }

        // The Use key with nothing else to use: eat or drink what is in front of the animal's head.
        internal bool TryEat(Driver d)
        {
            return Needs != null && Needs.Eat(d);
        }

        // Headless test of the diets: who eats what, with the food values read from the item definitions.
        internal string TestStats()
        {
            var lines = new List<string>();
            bool ok = true;
            Func<string, string, bool, bool> expect = (species, item, eats) =>
            {
                SpeciesDef sp = SpeciesById(species);
                bool got = sp != null && StatsModule.OnDiet(sp, item);
                if (got != eats) { ok = false; lines.Add("FAIL " + species + " " + (eats ? "should eat " : "should not eat ") + item); }
                return got == eats;
            };
            expect("cow", "corn", true); expect("cow", "bearmeat", false); expect("wolf", "bearmeat", true); expect("wolf", "corn", false);
            expect("bear", "corn", true); expect("bear", "fish.cooked", true); expect("rabbit", "black.berry", true); expect("rabbit", "wolfmeat.raw", false);
            expect("stag", "potato", true); expect("tiger", "chicken.cooked", true); expect("tiger", "pumpkin", false); expect("horse", "apple", true);
            string[] foods = { "corn", "potato", "pumpkin", "mushroom", "black.berry", "apple", "chicken.cooked", "bearmeat", "fish.cooked", "granolabar", "smallwaterbottle", "bandage", "syringe.medical" };
            var parsed = new List<string>();
            foreach (string f in foods)
            {
                Item item = ItemManager.CreateByName(f, 1);
                if (item == null) { parsed.Add(f + "=?"); continue; }
                ItemModConsumable c = item.info.GetComponent<ItemModConsumable>();
                parsed.Add(f + "=" + (c == null ? "no" : c.GetIfType(MetabolismAttribute.Type.Calories) + "/" + c.GetIfType(MetabolismAttribute.Type.Hydration) + "/" + c.GetIfType(MetabolismAttribute.Type.Health)));
                item.Remove();
            }
            lines.Insert(0, (ok ? "PASS" : "FAIL") + " diets of 12 pairs");
            lines.Add("INFO calories/hydration/health: " + string.Join(", ", parsed.ToArray()));
            lines.Add("INFO snow here: " + IsSnow(Vector3.zero) + ", biome map " + (TerrainMeta.BiomeMap != null) + ", topology map " + (TerrainMeta.TopologyMap != null));
            return string.Join("\n", lines.ToArray());
        }

        internal sealed class StatsModule : Module
        {
            public override int Order => 65;

            private readonly Dictionary<ulong, FormStats> stats = new Dictionary<ulong, FormStats>();
            private float lastTick;

            private const string Hud = "BeastmasterHud";

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Needs = this;
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["EatNothing"] = "There is nothing here that a {0} eats or drinks.",
                    ["EatFull"] = "You are not hungry now.",
                    ["Ate"] = "You eat. Hunger {0} %.",
                    ["Drank"] = "You drink. Thirst {0} %.",
                    ["Grazed"] = "You graze. Hunger {0} %.",
                    ["EatRefused"] = "A {0} does not eat {1}.",
                    ["DrinkSalt"] = "Salt water cannot be drunk. Find a river, a lake or a pond.",
                    ["StatHungry"] = "You are starving - find food.",
                    ["StatThirsty"] = "You are parched - find water.",
                    ["StatTired"] = "You are out of breath.",
                };
            }

            public override void OnDisable()
            {
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p != null && p.IsConnected)
                    {
                        CuiHelper.DestroyUi(p, Hud);
                        FormStats old;
                        if (stats.TryGetValue(p.userID, out old))
                        {
                            Restore(p, old);
                        }
                    }
                }
                stats.Clear();
            }

            public override void Unload()
            {
                OnDisable();
            }

            private FormStats Of(BasePlayer p)
            {
                FormStats s;
                if (!stats.TryGetValue(p.userID, out s))
                {
                    s = new FormStats();
                    stats[p.userID] = s;
                }
                return s;
            }

            public float SpeedFactor(BaseEntity animal)
            {
                if (stats.Count == 0)
                {
                    return 1f;
                }
                Driver d = Bm.DriverOfAnimal(animal);
                FormStats s;
                if (d == null || d.Kind != ControlKind.Form || !stats.TryGetValue(d.Player.userID, out s))
                {
                    return 1f;
                }
                float f = 1f;
                if (s.Hunger <= 0.5f) f *= 0.75f;
                if (s.Thirst <= 0.5f) f *= 0.75f;
                return f;
            }

            public int GaitCap(Driver d)
            {
                FormStats s;
                if (d.Kind != ControlKind.Form || !Bm.Cfg.Form.Needs || !stats.TryGetValue(d.Player.userID, out s))
                {
                    return 5;
                }
                return s.Stamina < 5f ? 2 : 5;
            }

            public void Spend(Driver d, float amount)
            {
                if (d.Kind != ControlKind.Form || !Bm.Cfg.Form.Needs)
                {
                    return;
                }
                FormStats s = Of(d.Player);
                s.Stamina = Mathf.Max(0f, s.Stamina - amount);
                s.LastSpend = Time.time;
            }

            // ---------------------------------------------------------------- ticking: drains, recovery, the panel

            public override void Tick(float now)
            {
                float dt = lastTick > 0f ? Mathf.Min(now - lastTick, 1f) : 0.25f;
                lastTick = now;
                if (Bm.Drivers.Count == 0)
                {
                    if (stats.Count > 0)
                    {
                        OnDisable();
                    }
                    return;
                }
                var present = new HashSet<ulong>();
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || d.Kind != ControlKind.Form || d.Player == null || !Valid(d.Body))
                    {
                        continue;
                    }
                    present.Add(d.Player.userID);
                    Update(d, now, dt);
                }
                foreach (ulong id in stats.Keys.Where(k => !present.Contains(k)).ToList())
                {
                    BasePlayer gone = BasePlayer.FindByID(id);
                    if (gone != null && gone.IsConnected)
                    {
                        CuiHelper.DestroyUi(gone, Hud);
                        Restore(gone, stats[id]);
                    }
                    stats.Remove(id);
                }
            }

            private void Update(Driver d, float now, float dt)
            {
                BasePlayer p = d.Player;
                FormStats s = Of(p);
                if (!Bm.Cfg.Form.Needs)
                {
                    if (now >= s.NextHud)
                    {
                        s.NextHud = now + 1f;
                        CuiHelper.DestroyUi(p, Hud);
                    }
                    return;
                }
                // the game's own bars (health, food, water) show the animal's: the human's values are kept aside and put back later
                if (p.metabolism != null)
                {
                    if (!s.Saved)
                    {
                        s.HumanCalories = p.metabolism.calories.value;
                        s.HumanHydration = p.metabolism.hydration.value;
                        s.Saved = true;
                    }
                    p.metabolism.calories.value = Mathf.Clamp(s.Hunger * p.metabolism.calories.max / 100f, 0f, p.metabolism.calories.max);
                    p.metabolism.hydration.value = Mathf.Clamp(s.Thirst * p.metabolism.hydration.max / 100f, 0f, p.metabolism.hydration.max);
                    p.metabolism.isDirty = true;
                }
                float rate = Bm.Cfg.Form.NeedsRate;
                bool wasHungry = s.Hunger <= 0.5f, wasThirsty = s.Thirst <= 0.5f, wasTired = s.Stamina < 5f;
                s.Hunger = Mathf.Max(0f, s.Hunger - 0.045f * rate * dt);
                s.Thirst = Mathf.Max(0f, s.Thirst - 0.07f * rate * dt);
                int gait = d.Gait;
                float drain = d.Standing ? 0f : gait >= 5 ? 3.5f : gait == 4 ? 2.2f : gait == 3 ? 0.8f : 0f;
                if (drain > 0f)
                {
                    s.Stamina = Mathf.Max(0f, s.Stamina - drain * dt);
                    s.LastSpend = now;
                }
                s.Stamina = 100f;                        // there is no stamina: an animal runs as long as it likes
                if (Bm.Cfg.Form.StarveDamage && (s.Hunger <= 0f || s.Thirst <= 0f) && Valid(d.Body))
                {
                    d.Body.Hurt(0.6f * dt, Rust.DamageType.Hunger, null, false);
                }
                if (s.Hunger <= 0.5f && !wasHungry) Bm.Tell(p, "StatHungry");
                if (s.Thirst <= 0.5f && !wasThirsty) Bm.Tell(p, "StatThirsty");
                if (s.Stamina < 5f && !wasTired) Bm.Tell(p, "StatTired");
                if (now >= s.NextHud)
                {
                    s.NextHud = now + 0.4f;
                    CuiHelper.DestroyUi(p, Hud);                // the game's own bars show the animal: health, food and water
                }
            }

            // Health, food and water of the animal are in the game's own bars; the one thing the game has no bar for, the stamina, gets a bar of the same
            // size above them. It is drawn once and then only updated, so that it does not flicker.
            private void DrawHud(Driver d, FormStats s)
            {
                BasePlayer p = d.Player;
                int value = Mathf.RoundToInt(Mathf.Clamp(s.Stamina, 0f, 100f));
                float now = Time.time;
                if (!d.Spectating)
                {
                    if (s.HudDrawn)
                    {
                        CuiHelper.DestroyUi(p, Hud);
                        s.HudDrawn = false;
                    }
                    return;
                }
                if (s.HudDrawn && value == s.HudValue && now - s.HudAt < 30f)
                {
                    return;
                }
                float frac = value / 100f;
                var c = new CuiElementContainer();
                if (!s.HudDrawn || now - s.HudAt >= 30f)
                {
                    c.Add(new CuiPanel
                    {
                        Image = { Color = "0.09 0.10 0.08 0.45" },
                        RectTransform = { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = "-311 150", OffsetMax = "-29 184" }
                    }, "Overlay", Hud);
                    c.Add(new CuiElement
                    {
                        Name = Hud + ".fill",
                        Parent = Hud,
                        Components = { new CuiImageComponent { Color = "0.86 0.76 0.24 0.85" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = Pt(frac, 1f) } }
                    });
                    c.Add(new CuiElement
                    {
                        Name = Hud + ".num",
                        Parent = Hud,
                        Components = { new CuiTextComponent { Text = value.ToString(), FontSize = 18, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0.03 0", AnchorMax = "1 1" } }
                    });
                    CuiHelper.DestroyUi(p, Hud);
                    CuiHelper.AddUi(p, c);
                    s.HudDrawn = true;
                    s.HudAt = now;
                }
                else
                {
                    c.Add(new CuiElement
                    {
                        Name = Hud + ".fill",
                        Update = true,
                        Components = { new CuiImageComponent { Color = "0.86 0.76 0.24 0.85" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = Pt(frac, 1f) } }
                    });
                    c.Add(new CuiElement
                    {
                        Name = Hud + ".num",
                        Update = true,
                        Components = { new CuiTextComponent { Text = value.ToString(), FontSize = 18, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0.03 0", AnchorMax = "1 1" } }
                    });
                    CuiHelper.AddUi(p, c);
                }
                s.HudValue = value;
            }

            // The human's own food and water come back when the animal form ends.
            private static void Restore(BasePlayer p, FormStats s)
            {
                if (p != null && p.metabolism != null && s != null && s.Saved)
                {
                    p.metabolism.calories.value = s.HumanCalories;
                    p.metabolism.hydration.value = s.HumanHydration;
                    p.metabolism.isDirty = true;
                }
            }

            // ---------------------------------------------------------------- eating and drinking

            public bool Eat(Driver d)
            {
                if (d.Kind != ControlKind.Form || !Bm.Cfg.Form.Needs)
                {
                    return false;
                }
                BasePlayer p = d.Player;
                FormStats s = Of(p);
                float now = Time.time;
                if (now < s.NextBite)
                {
                    return true;
                }
                Vector3 head = d.HeadPosition();
                Vector3 fwd = Quaternion.Euler(0f, d.Heading, 0f) * Vector3.forward;
                Vector3 mouth = head + fwd * 1.0f;
                SpeciesDef sp = d.Species;
                // 1. food lying on the ground
                var near = new List<BaseEntity>();
                Vis.Entities(mouth, 1.6f, near);
                foreach (BaseEntity e in near)
                {
                    DroppedItem dropped = e as DroppedItem;
                    if (dropped != null && dropped.item != null)
                    {
                        float calories, hydration, health;
                        if (Consumable(dropped.item, out calories, out hydration, out health) && OnDiet(sp, dropped.item.info.shortname))
                        {
                            Feed(d, s, calories, hydration, health, "Ate");
                            Bm.FormFed(d);
                            if (dropped.item.amount > 1)
                            {
                                dropped.item.amount -= 1;
                                dropped.item.MarkDirty();
                                dropped.SendNetworkUpdate();
                            }
                            else
                            {
                                dropped.DestroyItem();
                                dropped.Kill();
                            }
                            Bite(d, s, "eat");
                            return true;
                        }
                    }
                }
                // 2. a fresh animal corpse (carnivores and omnivores)
                if (sp.Food == Diet.Carnivore || sp.Food == Diet.Omnivore)
                {
                    foreach (BaseEntity e in near)
                    {
                        BaseCorpse corpse = e as BaseCorpse;
                        if (corpse != null && !(corpse is PlayerCorpse) && !(corpse is LootableCorpse && !(corpse is HorseCorpse)) && !Bm.IsRitualCorpse(corpse))
                        {
                            Feed(d, s, 40f, 0f, 8f, "Ate");
                            corpse.Hurt(corpse.MaxHealth() * 0.34f + 1f, Rust.DamageType.Bite, d.Body, false);
                            Bite(d, s, "eat");
                            return true;
                        }
                    }
                }
                // 3. water: under the animal, at its mouth or in the few metres ahead of it (the shallows count)
                Vector3 drinkSpot = d.Body.transform.position;
                bool water = false;
                foreach (float reach in new[] { 0f, 0.5f, 1.0f, 1.6f, 2.3f })
                {
                    Vector3 q = d.Body.transform.position + fwd * reach;
                    if (WaterLevel.GetWaterDepth(q, true, true, null) > 0.02f)
                    {
                        water = true;
                        drinkSpot = q;
                        break;
                    }
                }
                if (!water && WaterLevel.GetWaterDepth(mouth, true, true, null) > 0.02f)
                {
                    water = true;
                    drinkSpot = mouth;
                }
                if (water)
                {
                    bool sea = IsSeaWater(drinkSpot);
                    if (sea && Bm.Cfg.Form.SeaWaterShare <= 0f)
                    {
                        Bm.Tell(p, "DrinkSalt");
                        s.NextBite = now + 1.5f;
                        return true;
                    }
                    float salt = sea ? Bm.Cfg.Form.SeaWaterShare : 1f;
                    Feed(d, s, 0f, 30f * salt, 1.5f, "Drank");
                    Bite(d, s, "drink");
                    return true;
                }
                // 4. grass (herbivores)
                if (sp.Food == Diet.Herbivore || sp.Food == Diet.Small || sp.Food == Diet.Omnivore)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(mouth + Vector3.up * 0.6f, Vector3.down, out hit, 2.2f, LayerMask.GetMask("Terrain"), QueryTriggerInteraction.Ignore) && !IsSnow(hit.point))
                    {
                        if (s.Hunger > 97f)
                        {
                            Bm.Tell(p, "EatFull");
                            s.NextBite = now + 1.5f;
                            return true;
                        }
                        Feed(d, s, 18f, 0f, 1f, "Grazed");
                        Bite(d, s, "graze");
                        return true;
                    }
                }
                Bm.Tell(p, "EatNothing", sp.Display.ToLowerInvariant());
                s.NextBite = now + 1.5f;
                return true;
            }

            private static bool IsSeaWater(Vector3 pos)
            {
                return TerrainMeta.TopologyMap != null && TerrainMeta.TopologyMap.GetTopology(pos, (int)TerrainTopology.OCEAN);
            }

            private void Bite(Driver d, FormStats s, string word)
            {
                s.NextBite = Time.time + Bm.Cfg.Form.BiteCooldown;
                if (d.Animal == null)
                {
                    return;                  // the horse has no eating clip to play
                }
                List<ClipRef> clips = Bm.Clips(d.Animal).Where(c => Has(c, word) && !Has(c, "death") && !Has(c, "turn")).ToList();
                if (clips.Count == 0)
                {
                    clips = Bm.Clips(d.Animal).Where(c => (Has(c, "eat") || Has(c, "graze") || Has(c, "drink") || Has(c, "head")) && !Has(c, "death") && !Has(c, "turn")).ToList();
                }
                if (clips.Count > 0)
                {
                    PlayClip(d.Animal, clips[UnityEngine.Random.Range(0, clips.Count)]);
                }
            }

            // Food values come from the item definitions, so changed or modded items work (R9.25).
            private static bool Consumable(Item item, out float calories, out float hydration, out float health)
            {
                calories = hydration = health = 0f;
                ItemModConsumable c = item.info.GetComponent<ItemModConsumable>();
                if (c == null)
                {
                    return false;
                }
                calories = c.GetIfType(MetabolismAttribute.Type.Calories);
                hydration = c.GetIfType(MetabolismAttribute.Type.Hydration);
                health = c.GetIfType(MetabolismAttribute.Type.Health) + c.GetIfType(MetabolismAttribute.Type.HealthOverTime);
                return calories > 0f || hydration > 0f || health > 0f;
            }

            private static readonly string[] MeatWords = { "meat", "fish", "chicken", "pork", "mutton", "bacon", "steak", "crocodile", "snake" };

            // What a species eats: herbivores plants and produce, carnivores meat and fish, omnivores both, small animals seeds and small food.
            public static bool OnDiet(SpeciesDef sp, string shortname)
            {
                bool meat = MeatWords.Any(w => shortname.Contains(w));
                bool drink = shortname.Contains("water") || shortname.Contains("juice") || shortname.Contains("milk");
                if (drink)
                {
                    return true;
                }
                switch (sp.Food)
                {
                    case Diet.Carnivore: return meat;
                    case Diet.Omnivore: return true;
                    case Diet.Small: return !meat && (shortname.Contains("seed") || shortname.Contains("berry") || shortname.Contains("corn") || shortname.Contains("bread") || shortname.Contains("mushroom") || shortname.Contains("apple") || shortname.Contains("pickle") || shortname.Contains("granola") || shortname.Contains("grub") || shortname.Contains("worm"));
                    default: return !meat;
                }
            }

            private void Feed(Driver d, FormStats s, float calories, float hydration, float health, string message)
            {
                s.Hunger = Mathf.Min(100f, s.Hunger + calories * 0.25f);
                s.Thirst = Mathf.Min(100f, s.Thirst + hydration * 0.5f);
                if (health > 0f)
                {
                    Heal(d, s, health);
                }
                Bm.Tell(d.Player, message, Mathf.RoundToInt(message == "Drank" ? s.Thirst : s.Hunger));
            }

            // Healing from food is capped per second (R9.26).
            private void Heal(Driver d, FormStats s, float amount)
            {
                float now = Time.time;
                if (now - s.HealWindow > 1f)
                {
                    s.HealWindow = now;
                    s.HealBudget = Bm.Cfg.Form.HealPerSecond;
                }
                float gain = Mathf.Min(amount, s.HealBudget);
                s.HealBudget -= gain;
                if (gain > 0f && Valid(d.Body))
                {
                    d.Body.health = Mathf.Min(d.Body.MaxHealth(), d.Body.health + gain);
                    d.Body.SendNetworkUpdate();
                }
            }

            // ---------------------------------------------------------------- the inventory: food, drink and medicine act on the animal

            public override object OnItemAction(Item item, string action, BasePlayer player)
            {
                if (player == null || action != "consume" || !Bm.Cfg.Form.Needs)
                {
                    return null;
                }
                Driver d = Bm.DriverOf(player);
                if (d == null || d.Kind != ControlKind.Form)
                {
                    return null;
                }
                float calories, hydration, health;
                if (!Consumable(item, out calories, out hydration, out health))
                {
                    return null;
                }
                if (!OnDiet(d.Species, item.info.shortname) && calories + hydration > 0f && health <= 0f)
                {
                    Bm.Tell(player, "EatRefused", d.Species.Display.ToLowerInvariant(), item.info.displayName.english.ToLowerInvariant());
                    return true;
                }
                FormStats s = Of(player);
                if (Time.time < s.NextBite)
                {
                    return true;
                }
                s.NextBite = Time.time + Bm.Cfg.Form.BiteCooldown;
                Feed(d, s, calories, hydration, health, hydration > calories ? "Drank" : "Ate");
                item.UseItem(1);
                return true;
            }

            // Bandage and syringe used on oneself heal the animal, which is the player's health (R9.45).
            public override object OnHealingItemUse(MedicalTool tool, BasePlayer fromPlayer, IMedicalToolTarget toTarget)
            {
                Driver d = fromPlayer != null ? Bm.DriverOf(fromPlayer) : null;
                if (d == null || d.Kind != ControlKind.Form || (object)toTarget != (object)fromPlayer)
                {
                    return null;
                }
                ItemModConsumable c = tool.GetItem() != null ? tool.GetItem().info.GetComponent<ItemModConsumable>() : null;
                float health = c != null ? c.GetIfType(MetabolismAttribute.Type.Health) + c.GetIfType(MetabolismAttribute.Type.HealthOverTime) : 15f;
                Heal(d, Of(fromPlayer), Mathf.Max(health, 10f));
                tool.GetItem().UseItem(1);
                return true;
            }

            public override void SelfTest(List<string> report)
            {
                report.Add("Needs: " + stats.Count + " animal forms tracked, the needs are " + (Bm.Cfg.Form.Needs ? "on" : "off") + (Bm.Cfg.Form.StarveDamage ? ", starvation hurts" : ", nothing starves"));
            }
        }
    }

    // ======================================== Tame.cs
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

    // ======================================== TameFlows.cs
    public partial class Beastmaster
    {
        internal TameFlowModule Flow;

        internal bool TrySyringe(BasePlayer player, BaseNPC2 animal)
        {
            return Flow != null && Flow.Syringe(player, animal);
        }

        internal bool TryDeerBox(BasePlayer player, BaseNPC2 animal)
        {
            return Flow != null && Flow.DeerBox(player, animal);
        }

        // E on a livestock animal that has got used to the player (the game's own familiarity) makes it theirs (R5.1-R5.3).
        internal bool TryClaim(BasePlayer player, BaseNPC2 animal, SpeciesDef species)
        {
            if (species.Tame != TameFlow.Bond || !Cfg.Features.Taming || !Has(player, PermTame))
            {
                return false;
            }
            LivestockAnimal livestock = animal as LivestockAnimal;
            if (livestock == null)
            {
                return false;
            }
            ulong best;
            float seconds = livestock.BestFamiliarity(out best);
            if (best != 0UL && best != player.userID)
            {
                Tell(player, "BondTooEarly");
                return true;
            }
            if (best == 0UL || seconds < Cfg.Taming.BondSeconds)
            {
                Tell(player, "BondProgress", Mathf.RoundToInt(seconds), Mathf.RoundToInt(Cfg.Taming.BondSeconds));
                return true;
            }
            TameAnimal(animal, player);
            return true;
        }

        // The Incapacitate Dart of the Blow Pipe: no ordinary damage, a status instead.
        internal static bool IsIncapacitateDart(HitInfo info)
        {
            BaseProjectile weapon = info.Weapon as BaseProjectile;
            if (weapon != null)
            {
                Item item = weapon.GetItem();
                if (item != null && item.info.shortname == "blowpipe")
                {
                    ItemDefinition ammo = weapon.primaryMagazine != null ? weapon.primaryMagazine.ammoType : null;
                    if (ammo != null && ammo.shortname == "dart.incapacitate")
                    {
                        return true;
                    }
                }
            }
            return info.ProjectilePrefab != null && info.ProjectilePrefab.name.IndexOf("incap", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal sealed class TameFlowModule : Module
        {
            public override int Order => 35;

            private sealed class WolfChase { public BasePlayer Player; public float Seconds; public float LastSeen; }
            private sealed class Slow { public BaseNPC2 Animal; public BasePlayer By; public float Until; public float[] Speeds; }
            private sealed class Trap { public BaseNPC2 Animal; public BasePlayer By; public int Syringes; public bool Freed; public float FreedAt; }
            private sealed class Sedated { public BaseNPC2 Animal; public BasePlayer By; public float Until; public bool Fed; public bool Eating; public float NextClip; public VirtualBox Box; }

            private readonly Dictionary<ulong, WolfChase> chases = new Dictionary<ulong, WolfChase>();
            private readonly Dictionary<ulong, Slow> slows = new Dictionary<ulong, Slow>();
            private readonly Dictionary<ulong, Trap> traps = new Dictionary<ulong, Trap>();
            private readonly Dictionary<ulong, Sedated> sedated = new Dictionary<ulong, Sedated>();
            private float lastTick;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Flow = this;
            }

            public override Dictionary<string, string> Messages()
            {
                return new Dictionary<string, string>
                {
                    ["WolfScared"] = "The wolf is afraid of your torch. Keep chasing it with the lit torch ({0} s).",
                    ["WolfTamed"] = "The wolf stops fleeing, howls - and follows you now.",
                    ["Darted"] = "The {0} is hit: it is slowed down for {1} s.",
                    ["Sedated"] = "The {0} falls asleep for {1} s. Press E to open its inventory.",
                    ["Trapped"] = "The snap trap shuts on the {0}! Use {1} Medical Syringes on it (E).",
                    ["SyringeUsed"] = "Sedative {0}/{1}.",
                    ["PredatorCalm"] = "The {0} calms down, steps out of the trap and watches you ...",
                    ["DeerFood"] = "Put {0} {1} into the first three slots, then the saddle into the last slot.",
                    ["DeerAte"] = "It ate everything. Now put a saddle into the last slot.",
                    ["DeerTamed"] = "The {0} jumps up - fully tamed and saddled.",
                };
            }

            public override void OnDisable()
            {
                Reset();
            }

            public override void Unload()
            {
                Reset();
            }

            private void Reset()
            {
                foreach (Slow s in slows.Values.ToList())
                {
                    Unslow(s);
                }
                foreach (ulong id in chases.Keys.ToList())
                {
                    BaseNPC2 wolf = Find(id);
                    if (wolf != null)
                    {
                        Follower f = Bm.FollowerOf(wolf);
                        if (f != null && f.Reason == "scared")
                        {
                            Bm.StopFollower(wolf);
                        }
                    }
                }
                chases.Clear();
                foreach (Sedated s in sedated.Values.ToList())
                {
                    Wake(s, false);
                }
                foreach (Trap t in traps.Values.ToList())
                {
                    if (Valid(t.Animal))
                    {
                        Bm.Unpark(t.Animal, "trapped");
                    }
                }
                traps.Clear();
            }

            private static BaseNPC2 Find(ulong id)
            {
                return BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseNPC2;
            }

            // ---------------------------------------------------------------- darts

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                BaseNPC2 animal = entity as BaseNPC2;
                BasePlayer shooter = info != null ? info.InitiatorPlayer : null;
                if (animal == null || shooter == null || !Bm.Cfg.Features.Taming || !IsIncapacitateDart(info))
                {
                    return null;
                }
                SpeciesDef species = SpeciesOf(animal);
                if (species == null || Bm.IsTamed(animal) || Bm.DriverOfAnimal(animal) != null || !Bm.Has(shooter, PermTame))
                {
                    return null;
                }
                if (species.Tame == TameFlow.Predator)
                {
                    Slowed(animal, shooter, species);
                    return true;
                }
                if (species.Tame == TameFlow.SleepFeed)
                {
                    Sedate(animal, shooter, species);
                    return true;
                }
                return null;
            }

            private void Slowed(BaseNPC2 animal, BasePlayer by, SpeciesDef species)
            {
                RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
                if (agent == null)
                {
                    return;
                }
                ulong id = animal.net.ID.Value;
                Slow s;
                if (!slows.TryGetValue(id, out s))
                {
                    s = new Slow { Animal = animal, Speeds = new[] { agent.sneakSpeed, agent.walkSpeed, agent.jogSpeed, agent.runSpeed, agent.sprintSpeed, agent.fullSprintSpeed } };
                    slows[id] = s;
                    float k = 1f - Mathf.Clamp01(Bm.Cfg.Taming.DartSlow);
                    agent.sneakSpeed = s.Speeds[0] * k;
                    agent.walkSpeed = s.Speeds[1] * k;
                    agent.jogSpeed = s.Speeds[2] * k;
                    agent.runSpeed = s.Speeds[3] * k;
                    agent.sprintSpeed = s.Speeds[4] * k;
                    agent.fullSprintSpeed = s.Speeds[5] * k;
                }
                s.By = by;
                s.Until = Time.time + Bm.Cfg.Taming.DartSlowSeconds;
                Bm.Play(animal, "growl", "roar", "hurt");
                Bm.Tell(by, "Darted", species.Display, Mathf.RoundToInt(Bm.Cfg.Taming.DartSlowSeconds));
            }

            private void Unslow(Slow s)
            {
                ulong id = s.Animal != null && s.Animal.net != null ? s.Animal.net.ID.Value : 0UL;
                slows.Remove(id);
                if (!Valid(s.Animal))
                {
                    return;
                }
                RustNavMeshAgent agent = s.Animal.GetComponent<RustNavMeshAgent>();
                if (agent != null)
                {
                    agent.sneakSpeed = s.Speeds[0];
                    agent.walkSpeed = s.Speeds[1];
                    agent.jogSpeed = s.Speeds[2];
                    agent.runSpeed = s.Speeds[3];
                    agent.sprintSpeed = s.Speeds[4];
                    agent.fullSprintSpeed = s.Speeds[5];
                }
            }

            // ---------------------------------------------------------------- ticking

            public override void Tick(float now)
            {
                float dt = lastTick > 0f ? Mathf.Min(now - lastTick, 1f) : 0.25f;
                lastTick = now;
                if (!Bm.Cfg.Features.Taming)
                {
                    return;
                }
                TorchTick(now, dt);
                SlowTick(now);
                SedatedTick(now);
            }

            // Wolves flee from a lit torch; chased for long enough they stop, howl and follow (R5.4-R5.5).
            private void TorchTick(float now, float dt)
            {
                float range = Bm.Cfg.Taming.TorchRange;
                foreach (BasePlayer p in BasePlayer.activePlayerList)
                {
                    if (p == null || p.IsDead() || p.IsSleeping() || Bm.DriverOf(p) != null)
                    {
                        continue;
                    }
                    TorchWeapon torch = p.GetHeldEntity() as TorchWeapon;
                    if (torch == null || !torch.HasFlag(BaseEntity.Flags.On) || !Bm.Has(p, PermTame))
                    {
                        continue;
                    }
                    var near = new List<BaseNPC2>();
                    Vis.Entities(p.transform.position, range, near);
                    foreach (BaseNPC2 wolf in near)
                    {
                        SpeciesDef species = SpeciesOf(wolf);
                        if (species == null || species.Tame != TameFlow.Torch || !Alive(wolf) || Bm.IsTamed(wolf) || Bm.DriverOfAnimal(wolf) != null)
                        {
                            continue;
                        }
                        ulong id = wolf.net.ID.Value;
                        WolfChase chase;
                        if (!chases.TryGetValue(id, out chase) || (chase.Player != p && now - chase.LastSeen > 3f))
                        {
                            chase = new WolfChase { Player = p };
                            chases[id] = chase;
                            Bm.Tell(p, "WolfScared", Mathf.RoundToInt(Bm.Cfg.Taming.WolfChaseSeconds));
                        }
                        if (chase.Player != p)
                        {
                            continue;
                        }
                        chase.Seconds += dt;
                        chase.LastSeen = now;
                        Follower f = Bm.FollowerOf(wolf);
                        if (f == null || f.Reason != "scared")
                        {
                            BasePlayer scarer = p;
                            f = Bm.StartFollower(wolf, "scared", () => scarer != null && !scarer.IsDead() ? (Vector3?)scarer.transform.position : null);
                            if (f != null)
                            {
                                f.Flee = true;
                                f.FleeDistance = range * 0.7f;
                            }
                        }
                        if (chase.Seconds >= Bm.Cfg.Taming.WolfChaseSeconds)
                        {
                            chases.Remove(id);
                            Bm.StopFollower(wolf);
                            Bm.Play(wolf, "howl");
                            Bm.TameAnimal(wolf, p);
                            Bm.Tell(p, "WolfTamed");
                        }
                    }
                }
                foreach (KeyValuePair<ulong, WolfChase> pair in chases.ToList())
                {
                    if (now - pair.Value.LastSeen > 5f)
                    {
                        BaseNPC2 wolf = Find(pair.Key);
                        if (wolf != null)
                        {
                            Follower f = Bm.FollowerOf(wolf);
                            if (f != null && f.Reason == "scared")
                            {
                                Bm.StopFollower(wolf);
                            }
                        }
                        chases.Remove(pair.Key);
                    }
                }
            }

            // The dart wears off; a darted predator that steps into an armed snap trap is caught (the game's traps ignore animals).
            private void SlowTick(float now)
            {
                foreach (Slow s in slows.Values.ToList())
                {
                    if (!Valid(s.Animal) || s.Animal.IsDead() || now >= s.Until)
                    {
                        Unslow(s);
                        continue;
                    }
                    ulong id = s.Animal.net.ID.Value;
                    if (traps.ContainsKey(id))
                    {
                        continue;
                    }
                    var found = new List<BearTrap>();
                    Vis.Entities(s.Animal.transform.position, 1.6f, found);
                    foreach (BearTrap trap in found)
                    {
                        if (Valid(trap) && trap.Armed())
                        {
                            Catch(s, trap);
                            break;
                        }
                    }
                }
                foreach (KeyValuePair<ulong, Trap> pair in traps.ToList())
                {
                    Trap t = pair.Value;
                    if (!Valid(t.Animal) || t.Animal.IsDead())
                    {
                        traps.Remove(pair.Key);
                    }
                    else if (t.Freed && now >= t.FreedAt)
                    {
                        traps.Remove(pair.Key);
                        Bm.Unpark(t.Animal, "trapped");
                        if (t.By != null && t.By.IsConnected)
                        {
                            Bm.TameAnimal(t.Animal, t.By);
                        }
                    }
                }
            }

            private void Catch(Slow s, BearTrap trap)
            {
                BaseNPC2 animal = s.Animal;
                ulong id = animal.net.ID.Value;
                trap.Fire();
                animal.OnAttacked(Bm.Cfg.Taming.TrapDamage, Rust.DamageType.Stab, trap, false);
                Unslow(s);
                Bm.Park(animal, "trapped");
                traps[id] = new Trap { Animal = animal, By = s.By };
                Bm.Play(animal, "roar", "growl");
                SpeciesDef species = SpeciesOf(animal);
                Bm.Tell(s.By, "Trapped", species != null ? species.Display : "animal", Bm.Cfg.Taming.SyringesNeeded);
            }

            // E with a Medical Syringe in hand on a trapped predator (R5.9): a sedative; enough of them and it calms down.
            public bool Syringe(BasePlayer player, BaseNPC2 animal)
            {
                Trap t;
                if (!traps.TryGetValue(animal.net.ID.Value, out t) || t.Freed)
                {
                    return false;
                }
                Item active = player.GetActiveItem();
                if (active == null || active.info.shortname != "syringe.medical" || !Bm.Has(player, PermTame))
                {
                    return false;
                }
                active.UseItem(1);
                t.By = t.By ?? player;
                t.Syringes++;
                Bm.Tell(player, "SyringeUsed", t.Syringes, Bm.Cfg.Taming.SyringesNeeded);
                if (t.Syringes >= Bm.Cfg.Taming.SyringesNeeded)
                {
                    t.Freed = true;
                    t.By = player;
                    t.FreedAt = Time.time + Bm.Cfg.Taming.NeutralSeconds;
                    SpeciesDef species = SpeciesOf(animal);
                    Bm.Tell(player, "PredatorCalm", species != null ? species.Display : "animal");
                    Bm.Play(animal, "sit", "idle");
                }
                return true;
            }

            // ---------------------------------------------------------------- deer: sleep, feed, saddle (R5.11-R5.13)

            private void Sedate(BaseNPC2 animal, BasePlayer by, SpeciesDef species)
            {
                ulong id = animal.net.ID.Value;
                if (sedated.ContainsKey(id))
                {
                    return;
                }
                Bm.Park(animal, "sedated");
                var s = new Sedated { Animal = animal, By = by, Until = Time.time + Bm.Cfg.Taming.SleepSeconds };
                sedated[id] = s;
                Bm.Play(animal, "sleep", "lie");
                s.NextClip = Time.time + 5f;
                Bm.Tell(by, "Sedated", species.Display, Mathf.RoundToInt(Bm.Cfg.Taming.SleepSeconds));
            }

            private void SedatedTick(float now)
            {
                foreach (KeyValuePair<ulong, Sedated> pair in sedated.ToList())
                {
                    Sedated s = pair.Value;
                    if (!Valid(s.Animal) || s.Animal.IsDead())
                    {
                        sedated.Remove(pair.Key);
                        continue;
                    }
                    if (now >= s.Until)
                    {
                        Wake(s, true);
                        continue;
                    }
                    if (now >= s.NextClip)
                    {
                        s.NextClip = now + 6f;
                        Bm.Play(s.Animal, "sleep", "lie");     // keeps the animal lying: the parked animal has no AI to loop the clip
                    }
                }
            }

            private void Wake(Sedated s, bool announce)
            {
                sedated.Remove(s.Animal != null && s.Animal.net != null ? s.Animal.net.ID.Value : 0UL);
                if (s.Box != null)
                {
                    Bm.FinishBox(s.Box);
                }
                if (Valid(s.Animal))
                {
                    Bm.Unpark(s.Animal, "sedated");
                }
            }

            // E on the lying deer: its inventory. Three slots for food, the last one for the saddle that completes the taming.
            public bool DeerBox(BasePlayer player, BaseNPC2 animal)
            {
                Sedated s;
                if (!sedated.TryGetValue(animal.net.ID.Value, out s))
                {
                    return false;
                }
                SpeciesDef species = SpeciesOf(animal);
                string food = species != null && species.Id == "stag" ? Bm.Cfg.Taming.StagFood : Bm.Cfg.Taming.DoeFood;
                int need = Bm.Cfg.Taming.FoodNeeded;
                s.By = s.By ?? player;
                VirtualBox vb = Bm.OpenBox(player, "deer", need + 1, null, s,
                    (box, item, slot) =>
                    {
                        if (slot < need)
                        {
                            return !s.Fed && item.info.shortname == food;
                        }
                        return s.Fed && (item.info.shortname == "horse.saddle.single" || item.info.shortname == "horse.saddle.double");
                    },
                    (box, item) => DeerItemAdded(box, item, s, food, need, player),
                    box =>
                    {
                        foreach (Item rest in box.Box.inventory.itemList.ToList())
                        {
                            GiveBack(box.Player, rest);
                        }
                        s.Box = null;
                    });
                if (vb != null)
                {
                    s.Box = vb;
                    Bm.Tell(player, "DeerFood", need, ItemManager.FindItemDefinition(food) != null ? ItemManager.FindItemDefinition(food).displayName.english : food);
                }
                return true;
            }

            private void DeerItemAdded(VirtualBox box, Item item, Sedated s, string food, int need, BasePlayer player)
            {
                ItemContainer inv = box.Box.inventory;
                if (!s.Fed && !s.Eating && inv.itemList.Count(i => i.info.shortname == food && i.position < need) >= need)
                {
                    s.Eating = true;
                    Bm.timer.Once(Bm.Cfg.Taming.FoodSeconds, () =>
                    {
                        s.Eating = false;
                        if (!Valid(s.Animal) || !sedated.ContainsKey(s.Animal.net.ID.Value))
                        {
                            return;
                        }
                        foreach (Item f in inv.itemList.Where(i => i.info.shortname == food).ToList())
                        {
                            f.Remove();
                        }
                        s.Fed = true;
                        Bm.Play(s.Animal, "eat", "sleep");
                        if (player != null)
                        {
                            Bm.Tell(player, "DeerAte");
                        }
                    });
                    return;
                }
                if (s.Fed && item.position == need && (item.info.shortname == "horse.saddle.single" || item.info.shortname == "horse.saddle.double"))
                {
                    int saddle = item.info.shortname == "horse.saddle.double" ? 2 : 1;
                    item.Remove();
                    BaseNPC2 animal = s.Animal;
                    BasePlayer owner = player;
                    Bm.NextTick(() =>
                    {
                        Bm.FinishBox(box);
                        sedated.Remove(animal.net.ID.Value);
                        Bm.Unpark(animal, "sedated");
                        TamedRecord rec = Bm.TameAnimal(animal, owner);
                        if (rec != null)
                        {
                            rec.Gear.Saddle = saddle;
                            SpeciesDef species = SpeciesOf(animal);
                            Bm.Tell(owner, "DeerTamed", species != null ? species.Display : "deer");
                        }
                    });
                }
            }
        }
    }

    // ======================================== Tests.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- headless tests of the newer parts (server console / RCON only)
        // bm.test clips <species> | death | attacks | flames | scare. Results go to the server log ("[test] ...").

        private BaseNPC2 TestSpawn(SpeciesDef sp, Vector3 pos)
        {
            if (sp == null || !sp.Available)
            {
                return null;
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.2f;
            BaseNPC2 npc = GameManager.server.CreateEntity(sp.Prefab, pos, Quaternion.identity) as BaseNPC2;
            if (npc != null)
            {
                npc.Spawn();
            }
            return npc;
        }

        // The clips of a species with the state that holds them: what the sharp turn, the gestures and the attacks have to pick from.
        internal void TestClips(Action<string> reply, string id, Vector3 pos)
        {
            SpeciesDef sp = SpeciesById(id);
            BaseNPC2 a = TestSpawn(sp, pos);
            if (a == null)
            {
                reply("clips: " + id + " is not available");
                return;
            }
            timer.Once(1.5f, () =>
            {
                if (Valid(a))
                {
                    List<ClipRef> clips = Clips(a);
                    Puts("[test] clips " + id + " (" + clips.Count + "): " + string.Join(", ", clips.Select(c => c.State + ":" + c.Name).ToArray()));
                    a.Kill();
                }
            });
            reply("clips " + id + ": the list comes to the log in 2 s");
        }

        // A parked animal, one with a follower and one under control are killed: each has to leave a body and not stand there frozen.
        internal void TestDeath(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            SpeciesDef cow = SpeciesById("cow"), wolf = SpeciesById("wolf");
            BaseNPC2 parked = TestSpawn(cow, pos);
            BaseNPC2 led = TestSpawn(cow, pos + new Vector3(8f, 0f, 0f));
            BaseNPC2 predator = TestSpawn(wolf, pos + new Vector3(-8f, 0f, 0f));
            if (parked == null || led == null || predator == null)
            {
                foreach (BaseNPC2 x in new[] { parked, led, predator }) { if (x != null) x.Kill(); }
                reply("death: a test animal is missing");
                return;
            }
            timer.Once(2f, () =>
            {
                Park(parked, "control");
                StartFollower(led, "tame", () => null);
                Park(predator, "control");
                var before = new Dictionary<BaseNPC2, Vector3> { { parked, parked.transform.position }, { led, led.transform.position }, { predator, predator.transform.position } };
                foreach (BaseNPC2 x in before.Keys) { x.Hurt(x.health + 500f, Rust.DamageType.Bullet); }
                timer.Once(14f, () =>
                {
                    foreach (KeyValuePair<BaseNPC2, Vector3> kv in before)
                    {
                        BaseNPC2 x = kv.Key;
                        bool gone = !Valid(x);
                        bool dead = !gone && x.IsDead();
                        int bodies = 0;
                        foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
                        {
                            BaseCorpse corpse = net as BaseCorpse;
                            if (corpse != null && Vector3.Distance(corpse.transform.position, kv.Value) < 6f)
                            {
                                bodies++;
                            }
                        }
                        string name = ReferenceEquals(x, parked) ? "parked cow" : ReferenceEquals(x, led) ? "cow with a follower" : "parked wolf";
                        lines.Add(((gone || dead) && bodies > 0 ? "PASS " : "FAIL ") + name + ": " + (gone ? "removed" : dead ? "dead" : "STILL STANDING") + ", bodies near " + bodies);
                    }
                    foreach (BaseNPC2 x in before.Keys) { if (Valid(x)) x.Kill(); }
                    foreach (BaseNetworkable net in BaseNetworkable.serverEntities.ToList())
                    {
                        BaseCorpse corpse = net as BaseCorpse;
                        if (corpse != null && before.Values.Any(v => Vector3.Distance(corpse.transform.position, v) < 6f)) corpse.Kill();
                    }
                    Puts("[test] death:\n" + string.Join("\n", lines.ToArray()));
                });
            });
            reply("death test started: the result comes to the log in ~18 s");
        }

        // Every species with an attack hurts a cow in front of it with the damage the game gives that attack.
        internal void TestAttacks(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            List<SpeciesDef> list = AllSpecies.Where(s => s.Available && s.Id != "horse").ToList();
            string[] attackWords = { "attack", "headbutt", "kick", "charge", "flop", "snap", "peck", "bite", "swipe" };
            SpeciesDef cowDef = SpeciesById("cow");
            int index = 0;
            Action next = null;
            next = () =>
            {
                if (index >= list.Count)
                {
                    Puts("[test] attacks:\n" + string.Join("\n", lines.ToArray()));
                    return;
                }
                SpeciesDef sp = list[index];
                Vector3 at = pos + new Vector3((index % 6) * 14f, 0f, (index / 6) * 14f);
                index++;
                BaseNPC2 attacker = TestSpawn(sp, at);
                float gap = attacker != null ? attacker.bounds.extents.z * 1.9f + 0.6f : 1.5f;       // a long body (the crocodile) reaches from further away
                BaseNPC2 victim = TestSpawn(cowDef, at + new Vector3(0f, 0f, gap));
                if (attacker == null || victim == null)
                {
                    lines.Add("SKIP " + sp.Id + ": not spawned");
                    if (attacker != null) attacker.Kill();
                    if (victim != null) victim.Kill();
                    timer.Once(0.2f, next);
                    return;
                }
                timer.Once(1.3f, () =>
                {
                    Park(attacker, "test");
                    Park(victim, "test");
                    attacker.transform.rotation = Quaternion.identity;
                    victim.transform.position = attacker.transform.position + Vector3.forward * gap;
                    Physics.SyncTransforms();
                    timer.Once(0.4f, () =>
                    {
                        try
                        {
                            ClipRef clip = Clips(attacker).FirstOrDefault(c => attackWords.Any(w => Has(c, w)) && !Has(c, "death"));
                            AttackParams p = clip != null ? ReadAttack(attacker, clip, false) : new AttackParams { Damage = 3f, Delay = 0f, Type = Rust.DamageType.Bite, Range = 1.2f, Angle = 60f };
                            float hp = victim.health;
                            string summary = ResolveHits(attacker, null, 0f, p, false);
                            float lost = hp - victim.health;
                            lines.Add((lost > 0f ? "PASS " : "FAIL ") + sp.Id.PadRight(10) + (clip != null ? clip.Name : "(no attack clip: a nip)") + "  damage " + p.Damage.ToString("0") + " " + p.Type + "  range " + p.Range.ToString("0.0") + "  cow lost " + lost.ToString("0.0") + "  [" + summary + "]");
                        }
                        catch (Exception ex)
                        {
                            lines.Add("FAIL " + sp.Id + ": " + ex.Message);
                        }
                        finally
                        {
                            Unpark(attacker, "test");
                            Unpark(victim, "test");
                            if (Valid(attacker)) attacker.Kill();
                            if (Valid(victim)) victim.Kill();
                        }
                        timer.Once(0.3f, next);
                    });
                });
            };
            next();
            reply("attacks test started: " + list.Count + " species, the result comes to the log in ~" + (list.Count * 2 + 4) + " s");
        }

        // The ring of flames of the ritual: made, low, fixed in place, burning, gone again.
        internal void TestFlames(Action<string> reply, Vector3 pos)
        {
            RitualModule ritual = Modules.OfType<RitualModule>().FirstOrDefault();
            if (ritual == null)
            {
                reply("flames: no ritual module");
                return;
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            var rc = new RitualCorpse { Species = SpeciesById("cow"), Position = pos, Rotation = Quaternion.identity, Died = Time.time };
            ritual.SpawnFlames(rc);
            var lines = new List<string>();
            timer.Once(1.5f, () =>
            {
                int alive = rc.Flames.Count(f => f != null && !f.IsDestroyed);
                FireBall first = rc.Flames.FirstOrDefault() as FireBall;
                Rigidbody body = first != null ? first.GetComponent<Rigidbody>() : null;
                lines.Add((alive == Cfg.Ritual.FireCount ? "PASS " : "FAIL ") + "the ring has " + alive + " of " + Cfg.Ritual.FireCount + " flames");
                lines.Add((first != null ? "PASS " : "FAIL ") + "flame prefab " + (first != null ? first.PrefabName : Cfg.Ritual.FirePrefab) + ", scale " + (first != null ? first.transform.localScale.x.ToString("0.00") : "?") + ", damage/s " + (first != null ? first.damagePerSecond.ToString("0.0") : "?") + ", lifetime " + (first != null ? first.lifeTimeMin.ToString("0.0") : "?") + " s");
                lines.Add((body == null || body.isKinematic ? "PASS " : "FAIL ") + "the flames stay where they are (kinematic " + (body != null && body.isKinematic) + ")");
                lines.Add("INFO a flame is " + (first != null ? Vector3.Distance(first.transform.position, pos).ToString("0.00") : "?") + " m from the middle");
                ritual.KillFlames(rc);
                timer.Once(0.5f, () =>
                {
                    lines.Add((rc.Flames.Count == 0 ? "PASS " : "FAIL ") + "the flames are removed with the ritual (" + rc.Flames.Count + " left in the list)");
                    Puts("[test] flames:\n" + string.Join("\n", lines.ToArray()));
                });
            });
            reply("flames test started: the result comes to the log in ~3 s");
        }

        // A hit wild animal that is not on the always-defend list runs from its attacker.
        internal void TestScare(Action<string> reply, Vector3 pos)
        {
            BaseNPC2 cow = TestSpawn(SpeciesById("cow"), pos);
            BaseNPC2 bull = TestSpawn(SpeciesById("bull"), pos + new Vector3(30f, 0f, 0f));
            BaseNPC2 attackerBody = TestSpawn(SpeciesById("wolf"), pos + new Vector3(-3f, 0f, 0f));
            if (cow == null || bull == null || attackerBody == null)
            {
                foreach (BaseNPC2 x in new[] { cow, bull, attackerBody }) { if (x != null) x.Kill(); }
                reply("scare: a test animal is missing");
                return;
            }
            timer.Once(2f, () =>
            {
                Park(attackerBody, "test");
                float cowStart = Vector3.Distance(cow.transform.position, attackerBody.transform.position);
                Wildlife.Scare(cow, attackerBody);
                Wildlife.Defend(bull, attackerBody);
                timer.Once(6f, () =>
                {
                    float cowEnd = Vector3.Distance(cow.transform.position, attackerBody.transform.position);
                    Follower bf = FollowerOf(bull);
                    Puts("[test] scare:\n" + (cowEnd > cowStart + 4f ? "PASS " : "FAIL ") + "the cow runs from its attacker (" + cowStart.ToString("0.0") + " m -> " + cowEnd.ToString("0.0") + " m)\n"
                        + (bf != null && bf.Reason == "defend" ? "PASS " : "FAIL ") + "the bull defends (follower " + (bf != null ? bf.Reason : "none") + ")");
                    StopFollower(cow);
                    StopFollower(bull);
                    foreach (BaseNPC2 x in new[] { cow, bull, attackerBody }) { if (Valid(x)) x.Kill(); }
                });
            });
            reply("scare test started: the result comes to the log in ~8 s");
        }
    }

    // ======================================== Util.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- client RPCs without naming RpcTarget
        // RpcTarget pulls ReadOnlySpan into the signature, which plugin compilers do not always resolve, so it is called by reflection.

        private static readonly MethodInfo RpcTargetPlayerMethod = typeof(BaseEntity).Assembly.GetType("RpcTarget")?.GetMethod("Player", new[] { typeof(string), typeof(BasePlayer) });
        private static readonly MethodInfo ClientRpcNoArg = typeof(BaseEntity).GetMethods().FirstOrDefault(m => m.Name == "ClientRPC" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "RpcTarget");

        internal static void RpcTo(BaseEntity entity, BasePlayer target, string name)
        {
            if (RpcTargetPlayerMethod == null || ClientRpcNoArg == null || entity == null || target == null || !target.IsConnected)
            {
                return;
            }
            ClientRpcNoArg.Invoke(entity, new object[] { RpcTargetPlayerMethod.Invoke(null, new object[] { name, target }) });
        }

        // ---------------------------------------------------------------- effects

        private readonly Dictionary<string, string> effectCache = new Dictionary<string, string>();

        // Finds a prefab path in the game manifest by words (every word has to be in the path), caches it.
        internal string FindPrefabPath(params string[] words)
        {
            string key = string.Join("|", words);
            string path;
            if (effectCache.TryGetValue(key, out path))
            {
                return path;
            }
            path = null;
            foreach (string candidate in GameManifest.Current.pooledStrings.Select(p => p.str))
            {
                string lower = candidate.ToLowerInvariant();
                if (lower.EndsWith(".prefab") && words.All(w => lower.Contains(w)))
                {
                    path = candidate;
                    break;
                }
            }
            effectCache[key] = path;
            return path;
        }

        // Makes an entity look bigger (or normal again) on every client: the game sends the transform's scale of an entity whose
        // networkEntityScale flag is set (the admin command "entity scale" does exactly this).
        internal void ScaleModel(BaseEntity e, float scale)
        {
            if (!Valid(e))
            {
                return;
            }
            if (Mathf.Approximately(scale, 1f))
            {
                if (e.networkEntityScale)
                {
                    e.networkEntityScale = false;
                    e.transform.localScale = Vector3.one;
                    e.SendNetworkUpdate();
                }
                return;
            }
            e.networkEntityScale = true;
            e.transform.localScale = new Vector3(scale, scale, scale);
            e.SendNetworkUpdate();
        }

        // The size of a model relative to its prefab (reach of attacks grows with it).
        internal static float ModelScale(BaseEntity e)
        {
            return e != null ? Mathf.Max(1f, e.transform.localScale.x) : 1f;
        }

        // Everything the player carries: backpack, belt and worn clothes.
        internal static List<Item> PlayerItems(BasePlayer p)
        {
            var list = new List<Item>();
            if (p == null || p.inventory == null)
            {
                return list;
            }
            if (p.inventory.containerMain != null) list.AddRange(p.inventory.containerMain.itemList);
            if (p.inventory.containerBelt != null) list.AddRange(p.inventory.containerBelt.itemList);
            if (p.inventory.containerWear != null) list.AddRange(p.inventory.containerWear.itemList);
            return list;
        }

        // Admin helper: the ENTITY prefabs (things the server can spawn and network) that contain all the words, with their game class.
        internal string ListEntities(string[] words)
        {
            if (words == null || words.Length == 0)
            {
                return "Usage: bm.entities <word> [word ...]   (lists up to 60 spawnable entity prefabs with their class)";
            }
            var found = new List<string>();
            foreach (string path in GameManifest.Current.entities)
            {
                string lower = path.ToLowerInvariant();
                if (!words.All(w => lower.Contains(w.ToLowerInvariant())))
                {
                    continue;
                }
                GameObject go = GameManager.server.FindPrefab(path);
                BaseEntity be = go != null ? go.GetComponent<BaseEntity>() : null;
                found.Add(path + "  [" + (be != null ? be.GetType().Name : "?") + "]");
                if (found.Count >= 60)
                {
                    break;
                }
            }
            return found.Count == 0 ? "No entity matches." : string.Join("\n", found.ToArray());
        }

        // Admin helper: the items of this game build whose shortname or name contains all the words.
        internal string ListItems(string[] words)
        {
            if (words == null || words.Length == 0)
            {
                return "Usage: bm.items <word> [word ...]   (lists up to 60 items: shortname = display name)";
            }
            var found = new List<string>();
            foreach (ItemDefinition def in ItemManager.itemList)
            {
                string key = (def.shortname + " " + def.displayName.english).ToLowerInvariant();
                if (words.All(w => key.Contains(w.ToLowerInvariant())))
                {
                    found.Add(def.shortname + " = " + def.displayName.english);
                    if (found.Count >= 60)
                    {
                        break;
                    }
                }
            }
            return found.Count == 0 ? "No item matches." : string.Join("\n", found.ToArray());
        }

        // Admin helper: the prefab paths of this game build that contain all the words (for choosing effects and bodies).
        internal string ListPrefabs(string[] words)
        {
            if (words == null || words.Length == 0)
            {
                return "Usage: bm.prefabs <word> [word ...]   (lists up to 60 prefab paths that contain every word)";
            }
            var found = new List<string>();
            foreach (string candidate in GameManifest.Current.pooledStrings.Select(p => p.str))
            {
                string lower = candidate.ToLowerInvariant();
                if (lower.EndsWith(".prefab") && words.All(w => lower.Contains(w.ToLowerInvariant())))
                {
                    found.Add(candidate);
                    if (found.Count >= 60)
                    {
                        break;
                    }
                }
            }
            return found.Count == 0 ? "No prefab matches." : string.Join("\n", found.ToArray());
        }

        // Admin helper: any asset path of this game build (sprites, textures, sounds ...) that contains all the words.
        internal string ListAssets(string[] words)
        {
            if (words == null || words.Length == 0)
            {
                return "Usage: bm.assets <word> [word ...]   (lists up to 80 asset paths of any kind)";
            }
            var found = new List<string>();
            foreach (string candidate in GameManifest.Current.pooledStrings.Select(p => p.str))
            {
                string lower = candidate.ToLowerInvariant();
                if (words.All(w => lower.Contains(w.ToLowerInvariant())))
                {
                    found.Add(candidate);
                    if (found.Count >= 80)
                    {
                        break;
                    }
                }
            }
            return found.Count == 0 ? "No asset matches." : string.Join(Environment.NewLine, found.ToArray());
        }

        internal void RunEffect(string prefab, Vector3 position)
        {
            if (string.IsNullOrEmpty(prefab))
            {
                return;
            }
            Effect.server.Run(prefab, position, Vector3.up);
        }

        // The smoke of a transformation (config path, else the nearest looking effect of the game).
        internal void FormSmoke(Vector3 position, BasePlayer hearer = null)
        {
            if (!Cfg.Form.Smoke)
            {
                return;
            }
            string prefab = Cfg.Form.SmokePrefab;
            if (string.IsNullOrEmpty(prefab) || GameManager.server.FindPrefab(prefab) == null)
            {
                prefab = FindPrefabPath("fx", "smoke") ?? FindPrefabPath("fx", "poof") ?? FindPrefabPath("fx", "explosion", "small");
            }
            RunEffectFor(prefab, position + Vector3.up * 0.8f, hearer);
        }

        // The human shows up again: the game's appear effect with its sound.
        internal void FormAppear(Vector3 position, BasePlayer hearer = null)
        {
            if (!Cfg.Form.Smoke)
            {
                return;
            }
            string prefab = Cfg.Form.AppearPrefab;
            if (!string.IsNullOrEmpty(prefab) && GameManager.server.FindPrefab(prefab) != null)
            {
                RunEffectFor(prefab, position + Vector3.up * 0.8f, hearer);
            }
        }

        // The effect for everybody near, and sent straight to the player's own connection: he hears and sees it whatever the networking of his
        // body is at that moment (it is hidden right after the transformation).
        internal void RunEffectFor(string prefab, Vector3 position, BasePlayer hearer)
        {
            if (string.IsNullOrEmpty(prefab))
            {
                return;
            }
            if (hearer == null || !hearer.IsConnected || hearer.net == null || hearer.net.connection == null)
            {
                Effect.server.Run(prefab, position, Vector3.up);
                return;
            }
            Effect.server.Run(prefab, position, Vector3.up, hearer.net.connection);
            EffectNetwork.Send(new Effect(prefab, position, Vector3.up), hearer.net.connection);
        }

        // The game has a setting that switches the player's HUD off; the plugin asks the player's game to use it while the player is an animal
        // (and to switch it on for the inventory view and when the form ends).
        internal void HudSwitch(BasePlayer player, bool show)
        {
            if (player != null && player.IsConnected && Cfg.Form.HideHud)
            {
                player.SendConsoleCommand("graphics.hud " + (show ? "1" : "0"));
            }
        }

        // ---------------------------------------------------------------- positions

        // A standing spot on the ground next to a position (for returning to human form).
        internal static Vector3 GroundNear(Vector3 position, float radius)
        {
            Vector3 best = position;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                Vector3 p = position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                p.y = TerrainMeta.HeightMap.GetHeight(p) + 0.3f;
                if (!Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.4f, LayerMask.GetMask("World", "Construction", "Deployed", "Tree"), QueryTriggerInteraction.Ignore))
                {
                    return p;
                }
                best = p;
            }
            return best;
        }

        internal static string Pretty(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        internal string PresetOf(BasePlayer player)
        {
            string preset = player != null ? SettingsOf(player.userID).Preset : null;
            return string.IsNullOrEmpty(preset) ? Cfg.Camera.ThirdPreset : preset;
        }

        internal BasePlayer FindPlayer(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            ulong id;
            if (ulong.TryParse(text, out id))
            {
                BasePlayer byId = BasePlayer.FindByID(id) ?? BasePlayer.FindSleeping(id);
                if (byId != null)
                {
                    return byId;
                }
            }
            BasePlayer partial = null;
            foreach (BasePlayer p in BasePlayer.activePlayerList)
            {
                if (p.displayName.Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
                if (partial == null && p.displayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    partial = p;
                }
            }
            return partial;
        }
    }

    // ======================================== Wild.cs
    public partial class Beastmaster
    {
        // ---------------------------------------------------------------- how wild animals meet the plugin
        // 1. Defence (R19.18): a wild cow, sheep, horse or doe that is attacked defends itself with a chance (30 % by default), otherwise it flees as in
        //    the game; a stag, bull or ram always defends. 2. Relations (R7): who follows, tolerates, flees from or attacks a rider or a transformed player.

        internal WildModule Wildlife;

        internal sealed class WildModule : Module
        {
            public override int Order => 50;

            public override void Init(Beastmaster bm)
            {
                base.Init(bm);
                bm.Wildlife = this;
            }

            // The one who is reacted to: a ridden or transformed animal (or, in the test, any animal).
            internal sealed class Subject
            {
                public BaseNPC2 Animal;
                public string Species;
                public Func<bool> Live;
                public Func<float> Speed;
            }

            private sealed class Reaction
            {
                public BaseNPC2 Animal;
                public Subject Who;
                public RelationRule Rule;
                public float Seen;
            }

            private readonly Dictionary<ulong, Reaction> reactions = new Dictionary<ulong, Reaction>();
            private readonly Dictionary<ulong, float> rolled = new Dictionary<ulong, float>();
            private float nextRelations;

            public override void OnDisable()
            {
                Release(true);
            }

            public override void Unload()
            {
                Release(true);
            }

            // ---------------------------------------------------------------- defence

            public override object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
            {
                BaseNPC2 npc = entity as BaseNPC2;
                if (npc == null || info == null || !Bm.Cfg.Features.WildDefence)
                {
                    return null;
                }
                BaseCombatEntity by = AttackerOf(info);
                if (by == null || by == npc)
                {
                    return null;
                }
                SpeciesDef species = SpeciesOf(npc);
                if (species == null || npc.IsDead() || Bm.IsTamed(npc) || Bm.DriverOfAnimal(npc) != null)
                {
                    return null;
                }
                Follower busy = Bm.FollowerOf(npc);
                if (busy != null && busy.Reason != "scared" && busy.Reason != "defend")
                {
                    return null;                    // busy with something else of the plugin's (a boss hunting, a chain, a command)
                }
                bool always = Bm.Cfg.Wild.AlwaysDefend.Contains(species.Id);
                bool may = always || Bm.Cfg.Wild.MayDefend.Count == 0 || Bm.Cfg.Wild.MayDefend.Contains(species.Id);
                float now = Time.time;
                float last;
                ulong key = npc.net.ID.Value;
                if (rolled.TryGetValue(key, out last) && now - last < 20f)
                {
                    return null;
                }
                rolled[key] = now;
                if (rolled.Count > 400)
                {
                    foreach (ulong old in rolled.Where(kv => now - kv.Value > 60f).Select(kv => kv.Key).ToList())
                    {
                        rolled.Remove(old);
                    }
                }
                // the animals on the list always fight back; the others run, and now and then they fight as well
                if (always || (may && UnityEngine.Random.Range(0f, 100f) < Bm.Cfg.Wild.DefendChance))
                {
                    Defend(npc, by);
                }
                else
                {
                    Scare(npc, by);
                }
                return null;
            }

            // Who hurt the animal: a player, or the animal body a player rides or has become.
            internal static BaseCombatEntity AttackerOf(HitInfo info)
            {
                BasePlayer p = info.InitiatorPlayer;
                if (p != null)
                {
                    return p.IsNpc ? null : p;
                }
                BaseEntity initiator = info.Initiator;
                Driver d = initiator != null ? Instance.DriverOfAnimal(initiator) : null;
                return d != null ? d.Body : null;
            }

            internal void Scare(BaseNPC2 npc, BaseCombatEntity attacker)
            {
                Follower f = Bm.StartFollower(npc, "scared", null);
                if (f == null)
                {
                    return;
                }
                f.TeleportDistance = 0f;
                f.Flee = true;
                f.FleeDistance = 40f;
                f.Where = () => Alive(attacker) ? (Vector3?)attacker.transform.position : null;
                Bm.timer.Once(Bm.Cfg.Wild.FleeSeconds, () =>
                {
                    Follower cur = Valid(npc) ? Bm.FollowerOf(npc) : null;
                    if (cur != null && cur.Reason == "scared")
                    {
                        Bm.StopFollower(npc);
                    }
                });
            }

            internal void Defend(BaseNPC2 npc, BaseCombatEntity attacker)
            {
                Follower f = Bm.StartFollower(npc, "defend", null);
                if (f == null)
                {
                    return;
                }
                float until = Time.time + Bm.Cfg.Wild.DefendSeconds;
                f.TeleportDistance = 0f;
                f.Target = () => Alive(attacker) && Time.time < until && Valid(npc) && Vector3.Distance(attacker.transform.position, npc.transform.position) < 45f ? attacker : null;
                Bm.Play(npc, "roar", "growl", "snort");
                Bm.timer.Once(Bm.Cfg.Wild.DefendSeconds + 1f, () =>
                {
                    Follower cur = Valid(npc) ? Bm.FollowerOf(npc) : null;
                    if (cur != null && cur.Reason == "defend")
                    {
                        Bm.StopFollower(npc);
                    }
                });
            }

            // ---------------------------------------------------------------- relations

            public override void Tick(float now)
            {
                if (now < nextRelations)
                {
                    return;
                }
                nextRelations = now + 1f;
                if (!Bm.Cfg.Features.Relations || Bm.Drivers.Count == 0)
                {
                    if (reactions.Count > 0)
                    {
                        Release(true);
                    }
                    return;
                }
                foreach (Driver d in Bm.Drivers.Values.ToList())
                {
                    if (d == null || !d.Alive || !Valid(d.Animal) || d.Species == null)
                    {
                        continue;
                    }
                    Driver driver = d;
                    React(new Subject { Animal = d.Animal, Species = d.Species.Id, Live = () => driver.Alive, Speed = () => driver.Speed }, now);
                }
                Release(false, now);
            }

            private static RelationRule RuleFor(string observer, string subject)
            {
                return Instance.Cfg.Relations.Rules.FirstOrDefault(r => string.Equals(r.Observer, observer, StringComparison.OrdinalIgnoreCase) && string.Equals(r.Subject, subject, StringComparison.OrdinalIgnoreCase));
            }

            internal void React(Subject who, float now)
            {
                var near = new List<BaseNPC2>();
                Vis.Entities(who.Animal.transform.position, Bm.Cfg.Relations.Range, near);
                int handled = 0;
                foreach (BaseNPC2 npc in near)
                {
                    if (npc == null || npc == who.Animal || npc.IsDestroyed || npc.IsDead() || handled >= 12)
                    {
                        continue;
                    }
                    SpeciesDef species = SpeciesOf(npc);
                    if (species == null || Bm.IsTamed(npc) || Bm.DriverOfAnimal(npc) != null)
                    {
                        continue;
                    }
                    RelationRule rule = RuleFor(species.Id, who.Species);
                    if (rule == null || rule.Relation == "tolerate")
                    {
                        continue;
                    }
                    ulong id = npc.net.ID.Value;
                    Reaction r;
                    Follower f = Bm.FollowerOf(npc);
                    if (reactions.TryGetValue(id, out r))
                    {
                        r.Seen = now;
                        r.Who = who;
                        if (f == null || f.Reason != "relation")
                        {
                            reactions.Remove(id);
                        }
                        else
                        {
                            Maintain(r, f, who);
                            handled++;
                            continue;
                        }
                    }
                    if (f != null)
                    {
                        continue;       // busy with something else of the plugin's (defending, commanded, scared ...)
                    }
                    if (rule.Relation == "tether")
                    {
                        float dist = Vector3.Distance(npc.transform.position, who.Animal.transform.position);
                        if (dist <= rule.Keep)
                        {
                            continue;       // inside the tether it roams as it likes
                        }
                    }
                    f = Bm.StartFollower(npc, "relation", null);
                    if (f == null)
                    {
                        continue;
                    }
                    r = new Reaction { Animal = npc, Who = who, Rule = rule, Seen = now };
                    reactions[id] = r;
                    f.TeleportDistance = 0f;
                    Maintain(r, f, who);
                    handled++;
                }
            }

            // Points a relation follower at its subject according to the rule.
            private void Maintain(Reaction r, Follower f, Subject subject)
            {
                RelationRule rule = r.Rule;
                f.Target = null;
                f.Flee = false;
                switch (rule.Relation)
                {
                    case "hostile":
                        f.Target = () => subject.Live() && Alive(subject.Animal) ? subject.Animal : null;
                        break;
                    case "follow":
                    case "tether":
                        f.Where = () => subject.Live() && Valid(subject.Animal) ? (Vector3?)subject.Animal.transform.position : null;
                        f.StopDistance = rule.Relation == "tether" ? Mathf.Max(8f, rule.Keep * 0.4f) : Mathf.Max(3f, rule.Keep);
                        f.MatchLeader = rule.Relation == "follow";
                        f.LeaderSpeed = () => subject.Speed();
                        break;
                    case "flee":
                        f.Where = () => subject.Live() && Valid(subject.Animal) ? (Vector3?)subject.Animal.transform.position : null;
                        f.Flee = true;
                        f.FleeDistance = Mathf.Max(12f, rule.Keep);
                        break;
                }
            }

            // Ends the reactions whose subject is gone or far (all of them with `all`).
            private void Release(bool all, float now = 0f)
            {
                foreach (KeyValuePair<ulong, Reaction> kv in reactions.ToList())
                {
                    Reaction r = kv.Value;
                    bool end = all || !Valid(r.Animal) || now - r.Seen > 2.5f;
                    if (!end && r.Rule.Relation == "tether" && r.Who != null && Valid(r.Who.Animal))
                    {
                        // back inside the tether: free again
                        end = Vector3.Distance(r.Animal.transform.position, r.Who.Animal.transform.position) < r.Rule.Keep * 0.55f;
                    }
                    if (!end)
                    {
                        continue;
                    }
                    reactions.Remove(kv.Key);
                    if (Valid(r.Animal))
                    {
                        Follower f = Bm.FollowerOf(r.Animal);
                        if (f != null && f.Reason == "relation")
                        {
                            Bm.StopFollower(r.Animal);
                        }
                    }
                }
            }

            internal int ReactionCount { get { return reactions.Count; } }

            public override void SelfTest(List<string> report)
            {
                report.Add("Wild: " + reactions.Count + " animals reacting to riders or transformed players; " + Bm.Cfg.Relations.Rules.Count + " relation rules; defence " + Bm.Cfg.Wild.DefendChance + " %");
            }
        }

        private BaseNPC2 SpawnTestAnimal(string id, Vector3 pos, bool park)
        {
            SpeciesDef sp = SpeciesById(id);
            if (sp == null || !SpeciesEnabled(sp))
            {
                return null;
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.2f;
            BaseNPC2 npc = GameManager.server.CreateEntity(sp.Prefab, pos, Quaternion.identity) as BaseNPC2;
            if (npc == null)
            {
                return null;
            }
            npc.Spawn();
            return npc;
        }

        // Headless test: a stag defends itself against an attacker; cows follow a bull, a bear goes for a stag (R7.1, R7.2, R19.18).
        internal void TestWild(Action<string> reply, Vector3 pos)
        {
            var lines = new List<string>();
            Func<string, bool, bool> check = (name, ok) => { lines.Add((ok ? "PASS " : "FAIL ") + name); return ok; };
            BaseNPC2 stag = SpawnTestAnimal("stag", pos, false);
            BaseNPC2 victim = SpawnTestAnimal("cow", pos + new Vector3(5f, 0f, 0f), false);
            BaseNPC2 bull = SpawnTestAnimal("bull", pos + new Vector3(0f, 0f, 60f), false);
            BaseNPC2 cow1 = SpawnTestAnimal("cow", pos + new Vector3(22f, 0f, 60f), false);
            BaseNPC2 cow2 = SpawnTestAnimal("cow", pos + new Vector3(-22f, 0f, 60f), false);
            BaseNPC2 deer = SpawnTestAnimal("stag", pos + new Vector3(0f, 0f, 120f), false);
            BaseNPC2 bear = SpawnTestAnimal("bear", pos + new Vector3(18f, 0f, 120f), false);
            var all = new[] { stag, victim, bull, cow1, cow2, deer, bear };
            if (all.Any(a => a == null))
            {
                foreach (BaseNPC2 a in all) { if (a != null) a.Kill(); }
                reply("A test animal is not available in this build.");
                return;
            }
            timer.Once(2.0f, () =>
            {
                Park(victim, "test");
                Park(bull, "test");
                Park(deer, "test");
                float victimHp = victim.health, deerHp = deer.health;
                float cowBefore = Vector3.Distance(cow1.transform.position, bull.transform.position);
                Wildlife.Defend(stag, victim);
                check("the stag defends and parks (follower " + (FollowerOf(stag) != null ? FollowerOf(stag).Reason : "none") + ")", FollowerOf(stag) != null && FollowerOf(stag).Reason == "defend");
                int ticks = 0;
                var subjectBull = new WildModule.Subject { Animal = bull, Species = "bull", Live = () => true, Speed = () => 0f };
                var subjectDeer = new WildModule.Subject { Animal = deer, Species = "stag", Live = () => true, Speed = () => 0f };
                Action tick = null;
                tick = () =>
                {
                    Wildlife.React(subjectBull, Time.time);
                    Wildlife.React(subjectDeer, Time.time);
                    if (++ticks < 14)
                    {
                        timer.Once(1f, tick);
                        return;
                    }
                    float cowAfter = Vector3.Distance(cow1.transform.position, bull.transform.position);
                    check("the stag hurt its attacker (" + Mathf.RoundToInt(victimHp) + " -> " + Mathf.RoundToInt(victim.health) + ")", victim.health < victimHp);
                    check("cows follow a bull (" + Mathf.RoundToInt(cowBefore) + " m -> " + Mathf.RoundToInt(cowAfter) + " m)", cowAfter < cowBefore - 8f);
                    check("a bear goes for a stag (" + Mathf.RoundToInt(deerHp) + " -> " + Mathf.RoundToInt(deer.health) + ")", deer.health < deerHp);
                    check("reactions are tracked (" + Wildlife.ReactionCount + ")", Wildlife.ReactionCount >= 2);
                    Wildlife.OnDisable();
                    check("they end when the relations are switched off", FollowerOf(bear) == null && FollowerOf(cow1) == null);
                    StopFollower(stag);
                    foreach (BaseNPC2 a in all) { if (Valid(a)) { a.Kill(); } }
                    Puts("[test] wild:\n" + string.Join("\n", lines.ToArray()));
                };
                tick();
            });
            reply("Wild test started: the result comes to the log in ~20 s.");
        }
    }
}
