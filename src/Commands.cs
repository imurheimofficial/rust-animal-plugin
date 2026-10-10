using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oxide.Core;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
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
                        int level;
                        Cfg.General.LogLevel = args.Length > 0 && int.TryParse(args[0], out level) ? Mathf.Clamp(level, 0, 3) : 3;
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
                        float radius;
                        if (args.Length == 0 || !float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius))
                        {
                            radius = 0f;
                        }
                        reply(L("Cleared", null, ClearSpawned(player, radius)));
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
            return "As an animal: W/A/S/D move, Shift faster, Ctrl brake, S while running = 180 turn, Space jump (onto raised surfaces too), "
                + "F attack (camera behind the animal = rear attack), E eat / drink / pick up / open, R another skin (cow, bull, sheep, ram, horse), G or Ctrl + G night vision (goggles in the inventory), "
                + "B gestures (or /bm lie, sit, roar, look, stand), X back to human (X at the animal you left = become it again, E at it = ride it, Ctrl + E at it = its bag). "
                + "Riding: the same keys, G or R switch first / third person, X get off. "
                + "Ctrl + R opens the menu (empty hands, riding or as an animal), also /bm. F5 and H work after a one-time bind f5 bm.menu and bind h bm.nv in the F1 console.";
        }

        private string HelpText()
        {
            return "Beastmaster " + Version + "\n" + KeysText() + "\nCommands: /bm menu, species, keys, view, preset, cam, seat, revert, cmd follow|wait|guard|attack|release, ride, form, name, breed, give, feed, nv"
                + (Cfg.Features.Spawning ? ", spawn <species> [count] [tamed|wild] (also /sp)" : string.Empty) + "\nAdmin: bm.enable, bm.disable, bm.grant, bm.revoke, bm.access, bm.grantgroup, bm.revokegroup, bm.clear, bm.reload, bm.selftest, bm.debug, bm.boss, bm.horses, bm.run.";
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
}
