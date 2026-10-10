using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Oxide.Core;
using Oxide.Core.Plugins;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Beastmaster", "Claude", "1.1.0")]
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
}
