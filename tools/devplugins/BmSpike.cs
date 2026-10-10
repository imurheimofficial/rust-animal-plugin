using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oxide.Core;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BmSpike", "Beastmaster dev", "0.3.4")]
    [Description("DEV ONLY - never shipped. Phase-0 spikes: ride / play as an animal / steer / gaits / clips / attack + damage + gather / camera / keys / gestures / water.")]
    public class BmSpike : RustPlugin
    {
        private const string SaddleSeat = "assets/prefabs/vehicle/seats/horsesaddle.prefab";
        private const string RideSeat = "assets/prefabs/vehicle/seats/snowmobilepassengerseat.prefab";
        private const string HiddenSeat = "assets/bundled/prefabs/static/chair.invisible.static.prefab";
        internal const string ParkedStateName = "BmParked";
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly Dictionary<ulong, Driver> drivers = new Dictionary<ulong, Driver>();
        private readonly List<string> animalPrefabs = new List<string>();
        // calm (parked) animals: net id -> FSM state to return to on /sp wild (null = the FSM's default idle)
        private readonly Dictionary<ulong, FSMStateBase> parked = new Dictionary<ulong, FSMStateBase>();
        private readonly Dictionary<ulong, int> pingCounts = new Dictionary<ulong, int>();
        // body yaw relative to the direction of travel, per species (a crab walks sideways)
        private readonly Dictionary<string, float> yawOffsets = new Dictionary<string, float> { { "crab_single", 90f } };
        private bool logGestures;
        private bool suppressGestures = true;
        private bool probeKeys;
        internal bool SelfHide;
        internal float OrbitScale = 1f;
        // "dbd": camera independent of the animal's facing, W/A/S/D relative to the camera (Dead by Daylight style);
        // "chase": camera turns with the animal, A/D steer like the horse
        internal string CamMode = "dbd";
        internal readonly HashSet<BaseEntity> Anchors = new HashSet<BaseEntity>();
        internal string RideSeatPrefab = RideSeat;

        // ------------------------------------------------------------------ lifecycle

        private void OnServerInitialized()
        {
            foreach (string path in GameManifest.Current.entities)
            {
                GameObject prefab = GameManager.server.FindPrefab(path);
                BaseEntity entity = prefab != null ? prefab.GetComponent<BaseEntity>() : null;
                if (entity is BaseNPC2 || entity is RidableHorse || entity is SnakeHazard)
                {
                    animalPrefabs.Add(path.ToLowerInvariant());
                }
            }
            // animals parked by an earlier load stay calm
            foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
            {
                BaseNPC2 npc = net as BaseNPC2;
                FSMComponent fsm = npc != null ? npc.GetComponent<FSMComponent>() : null;
                if (fsm != null && fsm.CurrentState != null && fsm.CurrentState.Name == ParkedStateName)
                {
                    parked[npc.net.ID.Value] = null;
                }
            }
            Puts($"BmSpike ready, {animalPrefabs.Count} creature prefabs, {parked.Count} calm animals");
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            Driver driver;
            if (player != null && drivers.TryGetValue(player.userID, out driver) && driver != null)
            {
                driver.Release("disconnected");
            }
        }

        private void Unload()
        {
            foreach (Driver driver in drivers.Values.ToList())
            {
                if (driver != null)
                {
                    driver.Release("plugin unload");
                }
            }
            drivers.Clear();
        }

        // ------------------------------------------------------------------ hooks

        private object OnPlayerInput(BasePlayer player, InputState input)
        {
            Driver driver;
            if (player == null || input == null || !drivers.TryGetValue(player.userID, out driver))
            {
                return null;
            }
            if (driver == null || driver.Animal == null || driver.Animal.IsDestroyed)
            {
                drivers.Remove(player.userID);
                return null;
            }
            driver.ReadInput(input);
            if (driver.Spectating)
            {
                // jump / duck would make the game switch the spectate target to another player
                input.current.buttons &= ~((int)BUTTON.JUMP | (int)BUTTON.DUCK);
            }
            return null;
        }

        // NOTE: no CanDismountEntity hook here - BaseMountable.DismountPlayer() asks that hook too,
        // so blocking it would also block the plugin's own seat swap.
        private void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
        {
            Driver driver;
            if (player != null && drivers.TryGetValue(player.userID, out driver) && driver != null && driver.Seat == mountable && !driver.SwappingSeats)
            {
                driver.Release("dismounted");
            }
        }

        private object CanUseGesture(BasePlayer player, GestureConfig gesture)
        {
            if (player == null || gesture == null)
            {
                return null;
            }
            Driver driver;
            bool driving = drivers.TryGetValue(player.userID, out driver) && driver != null;
            if (logGestures)
            {
                Puts($"[gesture] {player.displayName} '{gesture.convarName}' driving={driving} inGesture={player.InGesture}");
            }
            if (driving && suppressGestures)
            {
                driver.OnGesture(gesture.convarName);
                return false;
            }
            return null;
        }

        // The server cannot bind keys on a client (session 1: "bind" sent from the server was ignored), but some vanilla
        // keys already send a server command: F = "lighttoggle" (inventory.lighttoggle), X = "swapseats" (vehicle.swapseats).
        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.cmd == null || arg.Connection == null)
            {
                return null;
            }
            BasePlayer player = arg.Player();
            if (player == null)
            {
                return null;
            }
            Driver driver;
            bool driving = drivers.TryGetValue(player.userID, out driver) && driver != null;
            if (probeKeys || driving)
            {
                Puts($"[keys] {player.displayName}: command '{arg.cmd.FullName}' args '{arg.FullString}'");
            }
            if (!driving)
            {
                return null;
            }
            switch (arg.cmd.FullName)
            {
                case "inventory.lighttoggle":
                    driver.Attack();
                    return true;
                case "vehicle.swapseats":
                    NextTick(() => SubRelease(player));
                    return true;
            }
            return null;
        }

        // Playing as an animal: the hidden human cannot be hurt - only the animal can (its health is shown as the player's).
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            BasePlayer player = entity as BasePlayer;
            Driver driver;
            if (player != null && drivers.TryGetValue(player.userID, out driver) && driver != null && driver.Possessing)
            {
                return true;
            }
            return null;
        }

        // ...and turrets do not see it either (AI ignores it through SimpleAIMemory.PlayerIgnoreList).
        private object CanBeTargeted(BaseCombatEntity entity, MonoBehaviour turret)
        {
            BasePlayer player = entity as BasePlayer;
            Driver driver;
            if (player != null && drivers.TryGetValue(player.userID, out driver) && driver != null && driver.Possessing)
            {
                return false;
            }
            return null;
        }

        private object CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            return entity != null && Anchors.Contains(entity) ? (object)false : null;
        }

        internal float YawOffsetFor(string shortName)
        {
            float offset;
            return yawOffsets.TryGetValue(shortName, out offset) ? offset : 0f;
        }

        // G = "+markcurrentpos": if it places a map marker, the server sees it here.
        private object OnMapMarkerAdd(BasePlayer player, ProtoBuf.MapNote note)
        {
            if (player == null)
            {
                return null;
            }
            Driver driver;
            bool driving = drivers.TryGetValue(player.userID, out driver) && driver != null;
            if (probeKeys || driving)
            {
                Puts($"[keys] {player.displayName}: map marker at {(note != null ? note.worldPosition.ToString() : "?")} label '{(note != null ? note.label : string.Empty)}'");
            }
            if (!driving)
            {
                return null;
            }
            driver.CycleView();
            return true;
        }

        // ...or it may place a ping instead (no "before" hook for pings - logged only).
        private void OnPlayerPingsSend(BasePlayer player, ProtoBuf.MapNoteList list)
        {
            if (player == null || list == null || list.notes == null)
            {
                return;
            }
            int count = list.notes.Count;
            int last;
            pingCounts.TryGetValue(player.userID, out last);
            pingCounts[player.userID] = count;
            if (count > last && (probeKeys || drivers.ContainsKey(player.userID)))
            {
                Puts($"[keys] {player.displayName}: new ping at {list.notes[count - 1].worldPosition}");
            }
        }

        // ------------------------------------------------------------------ chat commands

        [ChatCommand("sp")]
        private void CmdSpike(BasePlayer player, string command, string[] args)
        {
            // dev server only listens on 127.0.0.1, so no permission check here (this plugin is never shipped)
            if (player == null)
            {
                return;
            }
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            try
            {
                switch (sub)
                {
                    case "spawn": SubSpawn(player, args); break;
                    case "calm": SubCalm(player, true); break;
                    case "wild": SubCalm(player, false); break;
                    case "ride": SubRide(player, possess: false); break;
                    case "possess":
                    case "play": SubRide(player, possess: true); break;
                    case "spec": SubRide(player, possess: true, spectate: true); break;
                    case "release": SubRelease(player); break;
                    case "seat": SubSeatOffset(player, args); break;
                    case "gait": WithDriver(player, d => d.SetGait(IntArg(args, 1, d.Gait))); break;
                    case "speed": WithDriver(player, d => { d.SpeedMultiplier = FloatArg(args, 1, 1f); Reply(player, "speed multiplier " + d.SpeedMultiplier); }); break;
                    case "steer": WithDriver(player, d => { d.MouseSteer = args.Length > 1 && args[1] == "mouse"; Reply(player, "steer " + (d.MouseSteer ? "mouse" : "keys")); }); break;
                    case "clips": SubClips(player); break;
                    case "clip": SubPlay(player, IntArg(args, 1, 0)); break;
                    case "attack": WithDriver(player, d => d.Attack()); break;
                    case "jump": WithDriver(player, d => d.Jump(FloatArg(args, 1, 4f), FloatArg(args, 2, 1.5f))); break;
                    case "cam":
                    case "view": WithDriver(player, d => { if (args.Length > 1 && args[1] != "next") { d.SetView(args[1].ToLowerInvariant() == "third"); } else { d.CycleView(); } }); break;
                    case "selfhide":
                        SelfHide = !(args.Length > 1 && args[1] == "off");
                        Reply(player, "self-hide (ragdoll flag) while playing as an animal: " + (SelfHide ? "ON" : "OFF"));
                        WithDriver(player, d => d.ApplySelfHide());
                        break;
                    case "yaw":
                        WithDriver(player, d =>
                        {
                            float offset = FloatArg(args, 1, 0f);
                            yawOffsets[d.Animal.ShortPrefabName] = offset;
                            d.SetYawOffset(offset);
                            Reply(player, d.Animal.ShortPrefabName + " body yaw offset " + offset + " deg");
                        });
                        break;
                    case "bones": SubBones(player); break;
                    case "cammode":
                        CamMode = args.Length > 1 && args[1] == "chase" ? "chase" : "dbd";
                        Reply(player, "camera mode for the next /sp spec: " + CamMode);
                        break;
                    case "orbit":
                        OrbitScale = Mathf.Clamp(FloatArg(args, 1, 1f), 0.4f, 3f);
                        Reply(player, "third-person camera distance x" + OrbitScale.ToString("0.00"));
                        break;
                    case "seatprefab":
                        RideSeatPrefab = args.Length > 1 && args[1] == "saddle" ? SaddleSeat : RideSeat;
                        Reply(player, "riding seat for the next /sp ride: " + RideSeatPrefab.Substring(RideSeatPrefab.LastIndexOf('/') + 1));
                        break;
                    case "tp": SubThirdPerson(player, args); break;
                    case "keys":
                        probeKeys = !(args.Length > 1 && args[1] == "off");
                        Reply(player, "key probe " + (probeKeys ? "ON - every command / marker / ping you send is written to the server log" : "OFF"));
                        break;
                    case "hide": player.limitNetworking = true; Reply(player, "you are hidden from others (limitNetworking)"); break;
                    case "show": player.limitNetworking = false; Reply(player, "you are visible again"); break;
                    case "gest":
                        logGestures = args.Length > 1 && args[1] == "log";
                        suppressGestures = !(args.Length > 1 && args[1] == "off");
                        Reply(player, $"gestures while riding/playing -> animal clips: {suppressGestures}, log: {logGestures}");
                        break;
                    case "info": SubInfo(player); break;
                    case "water": SubWater(player); break;
                    default: SubHelp(player); break;
                }
            }
            catch (Exception ex)
            {
                Reply(player, "spike error: " + ex.Message);
                PrintError(ex.ToString());
            }
        }

        // A player may bind these by hand in F1 (the server cannot): bind f sp.attack / bind g sp.cam / bind x sp.release
        [ConsoleCommand("sp.attack")]
        private void CcAttack(ConsoleSystem.Arg arg) { BasePlayer p = arg.Player(); if (p != null) { WithDriver(p, d => d.Attack()); } }

        [ConsoleCommand("sp.cam")]
        private void CcCam(ConsoleSystem.Arg arg) { BasePlayer p = arg.Player(); if (p != null) { WithDriver(p, d => d.CycleView()); } }

        [ConsoleCommand("sp.release")]
        private void CcRelease(ConsoleSystem.Arg arg) { BasePlayer p = arg.Player(); if (p != null) { SubRelease(p); } }

        // Headless server-side smoke test (server console / RCON only): spawn each species, suspend its AI,
        // move it along the navmesh, play its attack clip, do a jump + warp, then give the AI back.
        [ConsoleCommand("sp.selftest")]
        private void CcSelfTest(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
            {
                return;
            }
            string[] names = { "bear", "polarbear", "boar", "stag", "wolf2", "tiger", "panther", "cow", "bull", "sheep", "lamb", "calf", "chicken", "crocodile", "rabbit", "squirrel", "frog", "crab_single" };
            Vector3 basePos;
            if (!FindTestSpot(out basePos))
            {
                arg.ReplyWith("selftest: no animal on the map to borrow a navmesh spot from");
                return;
            }
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                Vector3 pos = basePos + new Vector3((i % 6) * 5f, 0f, (i / 6) * 6f);
                timer.Once(i * 5f, () => TestSpecies(name, pos));
            }
            Vector3 calmBase = basePos + new Vector3(0f, 0f, -14f);
            timer.Once(names.Length * 5f + 8f, () => CalmTest(calmBase));
            arg.ReplyWith($"selftest started at {basePos} for {names.Length} species; results as SELFTEST / CALMTEST lines in ~{names.Length * 5 + 18} s");
        }

        // Calm spawn as /sp spawn does it: the AI must still be parked (and a cow not grazing) 8 s later.
        private void CalmTest(Vector3 basePos)
        {
            string[] names = { "bear", "wolf2", "tiger", "cow" };
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                string path = animalPrefabs.FirstOrDefault(p => p.EndsWith("/" + name + ".prefab"));
                if (path == null)
                {
                    continue;
                }
                Vector3 pos = basePos + new Vector3(i * 5f, 0f, 0f);
                pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.2f;
                BaseNPC2 npc = GameManager.server.CreateEntity(path, pos, Quaternion.identity) as BaseNPC2;
                if (npc == null)
                {
                    continue;
                }
                npc.Spawn();
                timer.Once(0.3f, () => ParkTracked(npc));
                timer.Once(2f, () => ParkTracked(npc));
                timer.Once(8f, () =>
                {
                    if (npc == null || npc.IsDestroyed)
                    {
                        Puts($"CALMTEST {name}: destroyed before the check");
                        return;
                    }
                    FSMComponent fsm = npc.GetComponent<FSMComponent>();
                    LivestockAnimal livestock = npc as LivestockAnimal;
                    string state = fsm != null && fsm.CurrentState != null ? fsm.CurrentState.Name : "-";
                    Puts($"CALMTEST {name}: state={state} parked={state == ParkedStateName} grazing={(livestock != null && livestock.IsGrazing())} alive={!npc.IsDead()} moved={Vector3.Distance(pos, npc.transform.position):0.00}m");
                    parked.Remove(npc.net.ID.Value);
                    npc.Kill();
                });
            }
        }

        private bool FindTestSpot(out Vector3 spot)
        {
            // the player spawn spot (DevSpawn on the dev map) is dry, open land near the centre; borrowing a wild animal's
            // position picked a map-edge spot once, where the wild species vanished before the test
            BasePlayer.SpawnPoint spawn = ServerMgr.FindSpawnPoint(null, 0uL);
            if (spawn != null && spawn.pos != Vector3.zero)
            {
                spot = spawn.pos + new Vector3(15f, 0f, 0f);
                return true;
            }
            foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
            {
                BaseNPC2 npc = net as BaseNPC2;
                RustNavMeshAgent agent = npc != null ? npc.GetComponent<RustNavMeshAgent>() : null;
                if (agent != null && !npc.IsDead() && agent.isOnNavMesh && !(npc is CritterAnimal) && !agent.canSwim)
                {
                    spot = npc.transform.position;
                    return true;
                }
            }
            spot = Vector3.zero;
            return false;
        }

        private void TestSpecies(string name, Vector3 pos)
        {
            string path = animalPrefabs.FirstOrDefault(p => p.EndsWith("/" + name + ".prefab"));
            if (path == null)
            {
                Puts($"SELFTEST {name}: no prefab");
                return;
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.2f;
            BaseNPC2 npc = GameManager.server.CreateEntity(path, pos, Quaternion.identity) as BaseNPC2;
            if (npc == null)
            {
                Puts($"SELFTEST {name}: CreateEntity returned null / not a BaseNPC2");
                return;
            }
            npc.Spawn();
            timer.Once(1.5f, () =>
            {
                try
                {
                    if (npc == null || npc.IsDestroyed)
                    {
                        Puts($"SELFTEST {name}: destroyed before the test");
                        return;
                    }
                    RustNavMeshAgent agent = npc.GetComponent<RustNavMeshAgent>();
                    FSMComponent fsm = npc.GetComponent<FSMComponent>();
                    if (agent == null)
                    {
                        Puts($"SELFTEST {name}: no RustNavMeshAgent");
                        npc.Kill();
                        return;
                    }
                    bool onMesh = agent.isOnNavMesh;
                    string stateBefore = fsm != null && fsm.CurrentState != null ? fsm.CurrentState.Name : "-";
                    FSMStateBase returnTo = Park(npc);
                    bool paused = agent.Pause(this);
                    Vector3 start = npc.transform.position;
                    float speed = agent.runSpeed;
                    int steps = 0;
                    timer.Repeat(0.05f, 20, () =>
                    {
                        try
                        {
                            if (npc == null || npc.IsDestroyed)
                            {
                                return;
                            }
                            Vector3 p = npc.transform.position;
                            Vector3 next = p + npc.transform.forward * (speed * 0.05f);
                            agent.Move(agent.WorldToNavSpace(next) - agent.WorldToNavSpace(p));
                            steps++;
                            if (steps < 20)
                            {
                                return;
                            }
                            float moved = Vector3.Distance(start, npc.transform.position);
                            List<ClipRef> clips = ClipCatalog(npc);
                            ClipRef attack = clips.FirstOrDefault(c => Has(c, "attack") || Has(c, "headbutt"));
                            bool played = attack != null && PlayClip(npc, attack);
                            Vector3 from = npc.transform.position;
                            Vector3 to = from + npc.transform.forward * 3f;
                            to.y = TerrainMeta.HeightMap.GetHeight(to);
                            agent.IsJumping = true;
                            npc.transform.position = to;
                            agent.IsJumping = false;
                            bool warped = agent.WarpToWorldPosition(to);
                            bool unpaused = agent.Unpause(this);
                            Unpark(npc, returnTo);
                            Puts($"SELFTEST {name}: onNavmesh={onMesh} stateBefore={stateBefore} paused={paused} moved={moved:0.00}m in 1s (runSpeed {speed:0.00}) clips={clips.Count} attack={(attack != null ? attack.Name : "-")} played={played} warpAfterJump={warped} unpaused={unpaused} stillPaused={agent.IsPaused}");
                            timer.Once(4f, () =>
                            {
                                string stateAfter = fsm != null && fsm.CurrentState != null ? fsm.CurrentState.Name : "-";
                                Puts($"SELFTEST {name}: AI after release state={stateAfter} alive={(npc != null && !npc.IsDestroyed && !npc.IsDead())}");
                                if (npc != null && !npc.IsDestroyed)
                                {
                                    npc.Kill();
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            PrintError($"SELFTEST {name}: EXCEPTION {ex}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    PrintError($"SELFTEST {name}: EXCEPTION {ex}");
                }
            });
        }

        private void SubHelp(BasePlayer player)
        {
            Reply(player, "/sp spawn <name> [n] [wild] | calm | wild | ride | play | release | view first|third | selfhide on|off | yaw <deg> | bones | seatprefab passenger|saddle | tp [on|off] | gait 0-5 | speed m | steer keys|mouse | seat x y z | clips | clip n | attack | jump a|b|c [dist] [h] | keys [off] | gest [off|log] | water | info");
            Reply(player, "keys: W/A/S/D, Shift faster, Ctrl brake, S while running = 180 turn, Space jump, F attack (look back = rear attack), G or R view, X get off, B gestures = animal sounds/moves");
        }

        private void SubSpawn(BasePlayer player, string[] args)
        {
            string name = args.Length > 1 ? args[1].ToLowerInvariant() : "bear";
            int count = Mathf.Clamp(IntArg(args, 2, 1), 1, 10);
            bool wild = args.Any(a => a.Equals("wild", StringComparison.OrdinalIgnoreCase));
            string path = animalPrefabs.FirstOrDefault(p => p.EndsWith("/" + name + ".prefab")) ?? animalPrefabs.FirstOrDefault(p => p.Contains(name));
            if (path == null)
            {
                Reply(player, "no creature prefab matches '" + name + "'. Known: " + string.Join(", ", animalPrefabs.Select(p => p.Substring(p.LastIndexOf('/') + 1).Replace(".prefab", string.Empty))));
                return;
            }
            Vector3 forward = Quaternion.Euler(0f, player.eyes.rotation.eulerAngles.y, 0f) * Vector3.forward;
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = player.transform.position + forward * (6f + i * 3f);
                pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.2f;
                BaseEntity entity = GameManager.server.CreateEntity(path, pos, Quaternion.LookRotation(-forward));
                if (entity == null)
                {
                    Reply(player, "CreateEntity returned null for " + path);
                    return;
                }
                entity.Spawn();
                BaseNPC2 npc = entity as BaseNPC2;
                if (npc != null && !wild)
                {
                    // park right away and once more after the FSM finished its own start-up
                    timer.Once(0.3f, () => ParkTracked(npc));
                    timer.Once(2f, () => ParkTracked(npc));
                }
            }
            Reply(player, $"spawned {count}x {path} - {(wild ? "WILD (own AI)" : "calm (AI parked) - /sp wild gives it its AI back")}");
        }

        private void ParkTracked(BaseNPC2 npc)
        {
            if (npc == null || npc.IsDestroyed || npc.IsDead() || npc.net == null)
            {
                return;
            }
            FSMStateBase returnTo = Park(npc);
            FSMStateBase known;
            if (!parked.TryGetValue(npc.net.ID.Value, out known) || known == null)
            {
                parked[npc.net.ID.Value] = returnTo;
            }
        }

        private void SubCalm(BasePlayer player, bool calm)
        {
            BaseNPC2 animal = FindAimedAnimal(player, 15f);
            if (animal == null)
            {
                Reply(player, "look at an animal within 15 m");
                return;
            }
            if (drivers.Values.Any(d => d != null && d.Animal == animal))
            {
                Reply(player, "someone is riding/playing it - /sp release first");
                return;
            }
            ulong id = animal.net.ID.Value;
            if (calm)
            {
                ParkTracked(animal);
                Reply(player, animal.ShortPrefabName + " is calm (AI parked)");
                return;
            }
            FSMStateBase returnTo;
            parked.TryGetValue(id, out returnTo);
            parked.Remove(id);
            Unpark(animal, returnTo);
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            Reply(player, animal.ShortPrefabName + " has its own AI again (state " + (fsm != null && fsm.CurrentState != null ? fsm.CurrentState.Name : "-") + ")");
        }

        private void SubRide(BasePlayer player, bool possess, bool spectate = false)
        {
            if (drivers.ContainsKey(player.userID))
            {
                Reply(player, "already riding/playing - /sp release first");
                return;
            }
            BaseNPC2 animal = FindAimedAnimal(player, 8f);
            if (animal == null)
            {
                Reply(player, "look at a (new-generation) animal within 8 m");
                return;
            }
            if (drivers.Values.Any(d => d != null && d.Animal == animal))
            {
                Reply(player, "someone else already controls it");
                return;
            }
            Driver driver = animal.gameObject.AddComponent<Driver>();
            driver.Init(this, player, animal, possess, spectate);
            drivers[player.userID] = driver;
            if (!driver.Begin())
            {
                drivers.Remove(player.userID);
                UnityEngine.Object.Destroy(driver);
                return;
            }
            Reply(player, (possess ? "PLAYING AS " : "RIDING ") + animal.ShortPrefabName + " - W/A/S/D, Shift faster, Ctrl brake, S while running = 180 turn, Space jump, F attack, G or R view, X get off");
        }

        private void SubRelease(BasePlayer player)
        {
            Driver driver;
            if (drivers.TryGetValue(player.userID, out driver))
            {
                drivers.Remove(player.userID);
                if (driver != null)
                {
                    driver.Release("command");
                }
                Reply(player, "released");
            }
        }

        private void SubSeatOffset(BasePlayer player, string[] args)
        {
            WithDriver(player, d =>
            {
                Vector3 offset = new Vector3(FloatArg(args, 1, 0f), FloatArg(args, 2, 1f), FloatArg(args, 3, 0f));
                d.SetSaddleOffset(offset);
                Reply(player, "saddle offset " + offset);
            });
        }

        private void SubThirdPerson(BasePlayer player, string[] args)
        {
            bool on = args.Length > 1 ? args[1] != "off" : !player.HasPlayerFlag(BasePlayer.PlayerFlags.ThirdPersonViewmode);
            SetThirdPersonFlag(player, on);
            Reply(player, "third-person view flag " + (on ? "ON" : "OFF") + " - did your camera change?");
        }

        internal static void SetThirdPersonFlag(BasePlayer player, bool on)
        {
            player.SetPlayerFlag(BasePlayer.PlayerFlags.ThirdPersonViewmode, on);
            player.SendNetworkUpdateImmediate();
        }

        private void SubClips(BasePlayer player)
        {
            BaseNPC2 animal = drivers.ContainsKey(player.userID) ? drivers[player.userID].Animal : FindAimedAnimal(player, 10f);
            if (animal == null)
            {
                Reply(player, "ride or look at an animal");
                return;
            }
            List<ClipRef> clips = ClipCatalog(animal);
            for (int i = 0; i < clips.Count; i++)
            {
                Reply(player, $"{i}: {clips[i].State}.{clips[i].Field} -> {clips[i].Name} ({clips[i].Kind})");
            }
            if (clips.Count == 0)
            {
                Reply(player, "no clips found");
            }
        }

        private void SubPlay(BasePlayer player, int index)
        {
            BaseNPC2 animal = drivers.ContainsKey(player.userID) ? drivers[player.userID].Animal : FindAimedAnimal(player, 10f);
            if (animal == null)
            {
                Reply(player, "ride or look at an animal");
                return;
            }
            List<ClipRef> clips = ClipCatalog(animal);
            if (index < 0 || index >= clips.Count)
            {
                Reply(player, "clip index out of range (see /sp clips)");
                return;
            }
            Reply(player, PlayClip(animal, clips[index]) ? "playing " + clips[index].Name : "could not play " + clips[index].Name);
        }

        private void SubInfo(BasePlayer player)
        {
            BaseNPC2 animal = drivers.ContainsKey(player.userID) ? drivers[player.userID].Animal : FindAimedAnimal(player, 12f);
            if (animal == null)
            {
                Reply(player, "look at an animal");
                return;
            }
            RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            Reply(player, $"{animal.ShortPrefabName} [{animal.GetType().Name}] hp {animal.health:0}/{animal.MaxHealth():0} fsm={(fsm != null ? fsm.GetType().Name + ":" + (fsm.CurrentState != null ? fsm.CurrentState.Name : "-") : "-")} calm={parked.ContainsKey(animal.net.ID.Value)}");
            if (agent != null)
            {
                Reply(player, $"gaits sneak {agent.sneakSpeed:0.0} walk {agent.walkSpeed:0.0} jog {agent.jogSpeed:0.0} run {agent.runSpeed:0.0} sprint {agent.sprintSpeed:0.0} full {agent.fullSprintSpeed:0.0} turn {agent.angularSpeed:0} deg/s swim={agent.canSwim} paused={agent.IsPaused}");
            }
        }

        private void SubBones(BasePlayer player)
        {
            BaseNPC2 animal = drivers.ContainsKey(player.userID) ? drivers[player.userID].Animal : FindAimedAnimal(player, 10f);
            if (animal == null || animal.model == null || animal.model.boneTransforms == null)
            {
                Reply(player, "look at an animal (no model bones found)");
                return;
            }
            string spines = string.Join(", ", animal.model.boneTransforms
                .Where(t => t != null && t.name.IndexOf("spine", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => t.name + animal.transform.InverseTransformPoint(t.position).ToString("F2")));
            Reply(player, $"{animal.ShortPrefabName}: {animal.model.boneTransforms.Length} bones, bounds {animal.bounds}; spine: {spines}; back point {BackPoint(animal):F2}");
        }

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

        // Teleports the player (on foot) to dry land next to the nearest shallow water (0.6-1.2 m deep) and faces it.
        private void SubWater(BasePlayer player)
        {
            if (drivers.ContainsKey(player.userID) || player.isMounted)
            {
                Reply(player, "get off first (/sp release)");
                return;
            }
            Vector3 origin = player.transform.position;
            for (float radius = 10f; radius < 400f; radius += 8f)
            {
                int samples = Mathf.CeilToInt(2f * Mathf.PI * radius / 8f);
                for (int i = 0; i < samples; i++)
                {
                    float angle = i * 2f * Mathf.PI / samples;
                    Vector3 wet = origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    float depth = TerrainMeta.WaterMap.GetHeight(wet) - TerrainMeta.HeightMap.GetHeight(wet);
                    if (depth < 0.6f || depth > 1.2f)
                    {
                        continue;
                    }
                    Vector3 toOrigin = origin - wet;
                    toOrigin.y = 0f;
                    toOrigin.Normalize();
                    for (float step = 1f; step < 30f; step += 1f)
                    {
                        Vector3 dry = wet + toOrigin * step;
                        float ground = TerrainMeta.HeightMap.GetHeight(dry);
                        if (TerrainMeta.WaterMap.GetHeight(dry) < ground - 0.2f && TerrainMeta.HeightMap.GetSlope(dry) < 25f)
                        {
                            dry.y = ground + 0.2f;
                            player.Teleport(dry);
                            player.OverrideViewAngles(Quaternion.LookRotation(-toOrigin).eulerAngles);
                            player.SendNetworkUpdateImmediate();
                            Reply(player, $"shore: water {depth:0.0} m deep about {step:0} m in front of you - /sp spawn cow, /sp ride, walk in");
                            return;
                        }
                    }
                }
            }
            Reply(player, "no shallow water within 400 m");
        }

        // ------------------------------------------------------------------ helpers

        private void WithDriver(BasePlayer player, Action<Driver> action)
        {
            Driver driver;
            if (drivers.TryGetValue(player.userID, out driver) && driver != null)
            {
                action(driver);
            }
            else
            {
                Reply(player, "not riding - /sp ride or /sp play first");
            }
        }

        internal bool IsParked(BaseNPC2 animal)
        {
            return animal != null && animal.net != null && parked.ContainsKey(animal.net.ID.Value);
        }

        internal FSMStateBase ParkedReturnState(BaseNPC2 animal)
        {
            FSMStateBase state;
            return animal != null && animal.net != null && parked.TryGetValue(animal.net.ID.Value, out state) ? state : null;
        }

        internal void ForgetDriver(ulong userId, Driver driver)
        {
            Driver current;
            if (drivers.TryGetValue(userId, out current) && (current == driver || current == null))
            {
                drivers.Remove(userId);
            }
        }

        // Takes the animal's AI out cleanly: the FSM leaves its current state through the game's own exit code (that stops the
        // state's montage and clears grazing / drinking / carrying flags), parks in an empty state and stops ticking.
        // Session 1 only stopped the ticking, so a cow taken over while grazing kept its head-down pose while it ran.
        internal static FSMStateBase Park(BaseNPC2 animal)
        {
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
                animal.SetFlagLocal(BaseEntity.Flags.Reserved6, false); // NPCNetworking "alert" stance
                animal.SendNetworkUpdate();
            }
            RustNavMeshAgent agent = animal.GetComponent<RustNavMeshAgent>();
            if (agent != null)
            {
                agent.ResetPath();
            }
            return previous;
        }

        internal static void Unpark(BaseNPC2 animal, FSMStateBase returnTo)
        {
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            if (fsm == null)
            {
                return;
            }
            FSMStateBase target = returnTo ?? DefaultState(fsm);
            fsm.SetFsmActive(true);
            if (target != null)
            {
                fsm.SetState(target);
            }
        }

        private static FSMStateBase DefaultState(FSMComponent fsm)
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

        private static int IntArg(string[] args, int index, int fallback)
        {
            int value;
            return args.Length > index && int.TryParse(args[index], out value) ? value : fallback;
        }

        private static float FloatArg(string[] args, int index, float fallback)
        {
            float value;
            return args.Length > index && float.TryParse(args[index], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        internal void Reply(BasePlayer player, string message)
        {
            if (player != null && player.IsConnected)
            {
                player.ChatMessage("<color=#9fd36b>[spike]</color> " + message);
            }
            Puts("[" + (player != null ? player.displayName : "?") + "] " + message);
        }

        private static BaseNPC2 FindAimedAnimal(BasePlayer player, float range)
        {
            Vector3 eye = player.eyes.position;
            Vector3 dir = player.eyes.HeadForward();
            BaseNPC2 best = null;
            float bestScore = float.MaxValue;
            foreach (BaseNetworkable net in BaseNetworkable.serverEntities)
            {
                BaseNPC2 npc = net as BaseNPC2;
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
                if (angle > 25f)
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

        internal sealed class ClipRef
        {
            public string State;
            public string Field;
            public string Name;
            public string Kind;
            public UnityEngine.Object Asset;
        }

        internal static bool Has(ClipRef clip, string word)
        {
            return clip.State.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 || clip.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Every AnimationClip / RootMotionData the animal's FSM states hold (one level into each state, plus arrays).
        internal static List<ClipRef> ClipCatalog(BaseNPC2 animal)
        {
            var list = new List<ClipRef>();
            FSMComponent fsm = animal.GetComponent<FSMComponent>();
            if (fsm == null)
            {
                return list;
            }
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
        internal static bool PlayClip(BaseNPC2 animal, ClipRef clip)
        {
            RootMotionPlayer player = animal.GetComponent<RootMotionPlayer>();
            if (player == null || clip == null || clip.Asset == null)
            {
                return false;
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
                // the method group also has an AnimationClip overload, so a direct call needs UnityEngine.AnimationModule
                // (not given to plugins by default) - call the RootMotionData overload by reflection instead
                MethodInfo rmMethod = typeof(RootMotionPlayer).GetMethods(AnyInstance)
                    .FirstOrDefault(m => m.Name == "PlayServerAndTakeFromPool" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(RootMotionData));
                if (rmMethod == null)
                {
                    return false;
                }
                rmMethod.Invoke(player, new object[] { rm });
                return true;
            }
            return InvokeClip(player, clip.Asset);
        }

        private static bool InvokeClip(RootMotionPlayer player, object animationClip)
        {
            MethodInfo method = typeof(RootMotionPlayer).GetMethods(AnyInstance)
                .FirstOrDefault(m => m.Name == "PlayServerAndTakeFromPool" && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == animationClip.GetType());
            if (method == null)
            {
                return false;
            }
            method.Invoke(player, new object[] { animationClip, 1f, false });
            return true;
        }

        // ------------------------------------------------------------------ the controller

        internal sealed class Driver : FacepunchBehaviour
        {
            private const float BodyClearance = 0.15f;
            private const float TurnAboutTime = 1.0f;
            private static readonly FieldInfo LookDirectionField = typeof(NPCNetworking).GetField("<LookDirection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly string[] AttackWords = { "attack", "headbutt", "kick", "charge", "flop", "snap", "peck", "bite", "swipe" };
            private static readonly string[] IdleWords = { "idle", "look", "breathe", "shake", "sniff", "ear", "tail", "chew", "scratch", "stretch", "lick", "moo", "bleat", "howl", "bark", "growl", "roar", "call", "search", "sit" };
            private static readonly string[] NeverWords = { "death", "dead", "die", "lie", "lay", "sleep", "knock", "fall", "mount", "getup", "get_up", "standup", "stand_up", "graze", "drink", "turn", "stop", "run", "walk", "canter", "trot" };

            public BaseNPC2 Animal;
            public BasePlayer Player;
            public BaseMountable Seat;
            public int Gait = 1;
            public float SpeedMultiplier = 1f;
            public bool MouseSteer;
            public bool SwappingSeats;
            private BmSpike plugin;
            private bool possess;
            private bool spectate;
            private bool released;
            private RustNavMeshAgent agent;
            private NPCNetworking npcNet;
            private FSMStateBase returnState;
            private bool wasParked;
            private float moveYaw;
            private float yawVel;
            private float yawOffset;
            private float currentSpeed;
            private bool fwd, back, left, right;
            private bool braking;
            private float turnAboutLeft;
            private float turnDir;
            private int attackIndex;
            private float nextAttack;
            private bool jumping;
            private Vector3 jumpFrom, jumpTo;
            private float jumpTime, jumpDuration, jumpHeight;
            private bool thirdPerson;
            private bool selfHidden;
            private Vector3? saddleOverride;
            private Vector3 seatOffset;
            private float seatLift;
            private float waterFactor;
            private float nextWaterCheck;
            private bool deepWater;
            private bool swimSet;
            private float nextLook;
            private Vector3 lookDir;
            private float idleSince;
            private float nextIdle;
            private float nextHealthSync;
            private float lastLog;

            public bool Possessing => possess && !released;
            public bool Spectating => spectate && !released;
            private bool IgnoresWater => Animal.ShortPrefabName == "bear" || Animal.ShortPrefabName == "crocodile";

            public void Init(BmSpike owner, BasePlayer player, BaseNPC2 animal, bool possessMode, bool spectateMode)
            {
                plugin = owner;
                Player = player;
                Animal = animal;
                possess = possessMode;
                spectate = possessMode && spectateMode;
                agent = animal.GetComponent<RustNavMeshAgent>();
                npcNet = animal.GetComponent<NPCNetworking>();
                yawOffset = owner.YawOffsetFor(animal.ShortPrefabName);
                moveYaw = animal.transform.eulerAngles.y - yawOffset;
                lookDir = animal.transform.forward;
            }

            public bool Begin()
            {
                if (agent == null)
                {
                    plugin.Reply(Player, "this creature has no RustNavMeshAgent");
                    return false;
                }
                wasParked = plugin.IsParked(Animal);
                FSMStateBase previous = Park(Animal);
                returnState = wasParked ? plugin.ParkedReturnState(Animal) : previous;
                agent.Pause(this);
                if (spectate)
                {
                    thirdPerson = true;
                    BeginSpectate();
                }
                else if (!MountSeat())
                {
                    Release("mount failed");
                    return false;
                }
                if (possess)
                {
                    Player.limitNetworking = true;
                    Rust.Ai.SimpleAIMemory.PlayerIgnoreList.Add(Player);
                    ApplySelfHide();
                    SyncHealth(true);
                }
                return true;
            }

            // Riding: a straddle seat on the animal's back. Playing as the animal: the hidden seat whose eye sits at the
            // animal's head (first person) or above its middle (third person, the camera then orbits the animal).
            private bool MountSeat()
            {
                string prefab = possess ? HiddenSeat : plugin.RideSeatPrefab;
                BaseEntity seatEntity = GameManager.server.CreateEntity(prefab, Animal.transform.position, Animal.transform.rotation);
                BaseMountable seat = seatEntity as BaseMountable;
                if (seat == null)
                {
                    if (seatEntity != null)
                    {
                        seatEntity.Kill();
                    }
                    plugin.Reply(Player, "seat prefab is not a BaseMountable: " + prefab);
                    return false;
                }
                Vector3 eyeLocal = seat.transform.InverseTransformPoint(seat.GetEyeOverride().position);
                Vector3 anchorLocal = seat.mountAnchor != null ? seat.transform.InverseTransformPoint(seat.mountAnchor.position) : Vector3.zero;
                seat.SetParent(Animal, false, false);
                Quaternion seatRotation = Quaternion.Euler(0f, -yawOffset, 0f);
                seat.transform.localRotation = seatRotation;
                seatOffset = SeatOffset(seatRotation * eyeLocal, seatRotation * anchorLocal);
                seatLift = RequiredLift();
                seat.transform.localPosition = seatOffset + Vector3.up * seatLift;
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
                    plugin.Reply(Player, "MountPlayer did not mount you on " + prefab);
                    return false;
                }
                SetThirdPersonFlag(Player, thirdPerson);
                plugin.Reply(Player, $"{(possess ? (thirdPerson ? "third person" : "first person") : "riding")}: {prefab.Substring(prefab.LastIndexOf('/') + 1)} pose={seat.mountPose} eye={eyeLocal:F2} anchor={anchorLocal:F2} seat at {seatOffset:F2} lift={seatLift:0.00}");
                return true;
            }

            private Vector3 SeatOffset(Vector3 eye, Vector3 anchor)
            {
                Bounds b = Animal.bounds;
                if (!possess)
                {
                    // the rider's root (the seat's mount anchor) sits on the middle of the back
                    return saddleOverride ?? BackPoint(Animal) - anchor;
                }
                if (thirdPerson)
                {
                    return new Vector3(b.center.x, b.max.y * 0.85f, b.center.z) - eye;
                }
                // first person: the eye just in front of the head, in the direction of travel
                Vector3 dir = Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward;
                float reach = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
                Vector3 head = new Vector3(b.center.x, b.max.y * 0.9f, b.center.z) + dir * (reach + 0.05f);
                return head - eye;
            }

            private void Remount()
            {
                SwappingSeats = true;
                bool ok;
                try
                {
                    BaseMountable old = Seat;
                    if (old != null && !old.IsDestroyed)
                    {
                        old.DismountPlayer(Player, true);
                    }
                    ok = MountSeat();
                    if (old != null && !old.IsDestroyed)
                    {
                        old.Kill();
                    }
                }
                finally
                {
                    SwappingSeats = false;
                }
                if (!ok)
                {
                    Release("seat change failed");
                }
            }

            public void SetSaddleOffset(Vector3 offset)
            {
                if (possess)
                {
                    plugin.Reply(Player, "/sp seat is for riding");
                    return;
                }
                saddleOverride = offset;
                Remount();
            }

            public void SetYawOffset(float offset)
            {
                moveYaw += yawOffset - offset;
                yawOffset = offset;
                Remount();
            }

            // G / R / "/sp view": riding toggles the game's third-person camera; playing as the animal switches between the
            // animal's eyes and the third-person camera around the animal.
            public void CycleView()
            {
                SetView(!thirdPerson);
            }

            public void SetView(bool third)
            {
                if (spectate)
                {
                    thirdPerson = third;
                    PlaceSpectateBody(true);
                    plugin.Reply(Player, "view: " + (thirdPerson ? "third person (behind)" : "first person (eyes)"));
                    return;
                }
                thirdPerson = third;
                if (possess)
                {
                    Remount();
                }
                else
                {
                    SetThirdPersonFlag(Player, thirdPerson);
                }
                plugin.Reply(Player, "view: " + (thirdPerson ? "third person" : "first person"));
            }

            // RpcTarget pulls in ReadOnlySpan, which plugin compilers do not always resolve - call it by reflection.
            private static readonly MethodInfo RpcTargetPlayer = typeof(BaseEntity).Assembly.GetType("RpcTarget")?.GetMethod("Player", new[] { typeof(string), typeof(BasePlayer) });
            private static readonly MethodInfo ClientRpcNetId = FindClientRpc(typeof(NetworkableId));
            private static readonly MethodInfo ClientRpcVector3 = FindClientRpc(typeof(Vector3));
            private static int cameraBlockMask;

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

            // Experiment: the game's spectator camera pointed at the animal. A spectating player is not drawn (not even for
            // himself), casts no shadow, is not networked to others and is left out of AI / turret queries. The body is
            // parented above the animal so network updates follow it.
            private void BeginSpectate()
            {
                Player.EnsureDismounted();
                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, true);
                Player.gameObject.SetLayerRecursive(10);
                BaseEntity.Query.Server.RemovePlayer(Player);
                dbd = plugin.CamMode == "dbd";
                if (dbd)
                {
                    anchor = GameManager.server.CreateEntity(HiddenSeat, Animal.transform.position, Quaternion.identity);
                    if (anchor != null)
                    {
                        anchor.Spawn();
                        Rigidbody anchorBody = anchor.GetComponent<Rigidbody>();
                        if (anchorBody != null)
                        {
                            anchorBody.isKinematic = true;
                        }
                        plugin.Anchors.Add(anchor);
                    }
                    else
                    {
                        dbd = false;
                    }
                }
                Player.SetParent(dbd ? anchor : (BaseEntity)Animal, false, true);
                PlaceSpectateBody(true);
                SendSpectateTarget(Player, Animal.net.ID);
                plugin.Reply(Player, "spectate (" + (dbd ? "dbd: W/A/S/D follow the camera" : "chase: A/D steer") + "), " + (thirdPerson ? "third person" : "first person") + " - G switches, mouse orbits, /sp orbit 0.8 = closer");
            }

            // The spectator camera sits at the hidden body's eyes and the mouse only turns the view (session 3), so the body is
            // moved every tick to where the camera should be. Third person: around a pivot over the animal's back, opposite to
            // where the player looks, pulled in before walls and rocks and kept above the ground. First person: at the eyes.
            // The view angles of a parented player are relative to the parent's yaw, so the camera stays behind the animal
            // when it turns and the mouse orbits around it.
            private Vector3 lastBody;
            private BaseEntity anchor;
            private bool dbd;
            private Vector3 lastAnchor;
            private float dbdAlign = 1f;

            private void PlaceSpectateBody(bool faceForward)
            {
                lastBody = Vector3.zero;
                if (faceForward)
                {
                    Vector3 look = Quaternion.Euler(thirdPerson ? 12f : 5f, moveYaw, 0f) * Vector3.forward;
                    SendPlayerRpc(Player, ClientRpcVector3, "ForceViewAnglesTo", look);
                }
                UpdateSpectateCamera();
            }

            private void UpdateSpectateCamera()
            {
                if (!spectate || Player == null || Animal == null)
                {
                    return;
                }
                if (cameraBlockMask == 0)
                {
                    cameraBlockMask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed", "Tree", "Default");
                }
                if (anchor != null && !anchor.IsDestroyed && (Animal.transform.position - lastAnchor).sqrMagnitude > 0.0004f)
                {
                    lastAnchor = Animal.transform.position;
                    anchor.transform.position = lastAnchor;
                    anchor.SendNetworkUpdate();
                }
                Bounds b = Animal.bounds;
                Vector3 camera;
                if (thirdPerson)
                {
                    Vector3 pivot = Animal.transform.TransformPoint(new Vector3(b.center.x, b.max.y * 0.8f, b.center.z));
                    Vector3 fwd = Player.eyes.rotation * Vector3.forward;
                    float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    float dist = Mathf.Clamp(size * 1.4f + 1.0f, 2.2f, 6f) * plugin.OrbitScale;
                    RaycastHit hit;
                    if (Physics.SphereCast(pivot, 0.25f, -fwd, out hit, dist, cameraBlockMask, QueryTriggerInteraction.Ignore))
                    {
                        dist = Mathf.Max(0.4f, hit.distance - 0.1f);
                    }
                    camera = pivot - fwd * dist;
                    float ground = TerrainMeta.HeightMap.GetHeight(camera) + 0.35f;
                    if (camera.y < ground)
                    {
                        camera.y = ground;
                    }
                }
                else
                {
                    Vector3 dir = Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward;
                    float reach = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
                    camera = Animal.transform.TransformPoint(new Vector3(b.center.x, b.max.y * 0.9f, b.center.z) + dir * (reach + 0.1f));
                }
                Vector3 body = camera - PlayerEyes.EyeOffset;
                if ((body - lastBody).sqrMagnitude < 0.0004f)
                {
                    return;
                }
                lastBody = body;
                Player.transform.position = body;
                Player.SendNetworkUpdate();
            }

            private void EndSpectate()
            {
                Vector3 drop = Animal != null && !Animal.IsDestroyed
                    ? Animal.transform.position + Animal.transform.right * (Animal.bounds.extents.x + 1f)
                    : Player.transform.position;
                drop.y = TerrainMeta.HeightMap.GetHeight(drop) + 0.3f;
                Player.SetParent(null, true, true);
                if (anchor != null)
                {
                    plugin.Anchors.Remove(anchor);
                    if (!anchor.IsDestroyed)
                    {
                        anchor.Kill();
                    }
                    anchor = null;
                }
                SendSpectateTarget(Player, default(NetworkableId));
                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, false);
                Player.gameObject.SetLayerRecursive(17);
                BaseEntity.Query.Server.RemovePlayer(Player);
                BaseEntity.Query.Server.AddPlayer(Player);
                Player.Teleport(drop);
            }

            // Experiment: the game hides the player's own body while it is ragdolled (the ragdoll entity shows it instead).
            public void ApplySelfHide()
            {
                bool hide = possess && !spectate && plugin.SelfHide && !released;
                if (hide == selfHidden)
                {
                    return;
                }
                selfHidden = hide;
                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Ragdolling, hide);
                Player.SendNetworkUpdateImmediate();
            }

            public void ReadInput(InputState input)
            {
                fwd = input.IsDown(BUTTON.FORWARD);
                back = input.IsDown(BUTTON.BACKWARD);
                left = input.IsDown(BUTTON.LEFT);
                right = input.IsDown(BUTTON.RIGHT);
                if (input.WasJustPressed(BUTTON.SPRINT))
                {
                    braking = false;
                    SetGait(Mathf.Max(Gait, 1) + 1);
                }
                if (input.WasJustPressed(BUTTON.DUCK))
                {
                    Brake();
                }
                if (!(spectate && dbd) && input.WasJustPressed(BUTTON.BACKWARD) && currentSpeed > GaitSpeed(3) * 0.9f)
                {
                    TurnAbout();
                }
                if (input.WasJustPressed(BUTTON.JUMP))
                {
                    Jump(4f, 1.5f);
                }
                if (input.WasJustPressed(BUTTON.FIRE_THIRD))
                {
                    Attack();
                }
                if (input.WasJustPressed(BUTTON.RELOAD))
                {
                    CycleView();
                }
            }

            public void SetGait(int gait)
            {
                Gait = Mathf.Clamp(gait, 0, 5);
                string water = EffectiveGait() < Gait ? " (deep water: walk only)" : string.Empty;
                plugin.Reply(Player, "gait " + Gait + " = " + GaitSpeed(Gait).ToString("0.00") + " m/s" + water);
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
                    ClipRef stop = side == null ? null : ClipCatalog(Animal).FirstOrDefault(c => Has(c, "stop") && Has(c, side));
                    if (stop != null)
                    {
                        PlayClip(Animal, stop);
                    }
                    plugin.Reply(Player, "brake" + (stop != null ? " (" + stop.Name + ")" : string.Empty));
                }
                else
                {
                    Gait = 0;
                    plugin.Reply(Player, "stop");
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
                List<ClipRef> clips = ClipCatalog(Animal);
                ClipRef clip = clips.FirstOrDefault(c => Has(c, "turn") && Has(c, "180") && Has(c, side)) ?? clips.FirstOrDefault(c => Has(c, "turn") && Has(c, "180"));
                if (clip != null)
                {
                    PlayClip(Animal, clip);
                }
                turnAboutLeft = TurnAboutTime;
                plugin.Reply(Player, "turn about " + side + (clip != null ? " (" + clip.Name + ")" : " (no turn clip on this species)"));
            }

            private int EffectiveGait()
            {
                return deepWater ? Mathf.Min(Gait, 1) : Gait;
            }

            private float GaitSpeed(int gait)
            {
                float speed;
                switch (gait)
                {
                    case 0: speed = 0f; break;
                    case 1: speed = agent.walkSpeed; break;
                    case 2: speed = agent.jogSpeed; break;
                    case 3: speed = agent.runSpeed; break;
                    case 4: speed = agent.sprintSpeed; break;
                    default: speed = agent.fullSprintSpeed; break;
                }
                return speed * SpeedMultiplier;
            }

            // Same measure as the horse: the share of the body height under the water surface.
            private float WaterMultiplier()
            {
                if (IgnoresWater || waterFactor <= 0.05f)
                {
                    return 1f;
                }
                return Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(0.1f, 0.65f, waterFactor));
            }

            private void UpdateWater()
            {
                if (Time.time < nextWaterCheck)
                {
                    return;
                }
                nextWaterCheck = Time.time + 0.25f;
                waterFactor = WaterLevel.Factor(Animal.WorldSpaceBounds().ToBounds(), true, true, Animal);
                bool deep = waterFactor > 0.5f && !IgnoresWater;
                if (deep != deepWater)
                {
                    deepWater = deep;
                    plugin.Reply(Player, deep ? $"deep water ({waterFactor:P0} of the body) - walk only, like the horse" : "out of deep water");
                }
                if (agent.canSwim)
                {
                    bool swim = waterFactor > 0.65f;
                    if (swim != agent.IsSwimming)
                    {
                        agent.IsSwimming = swim;
                        swimSet = true;
                    }
                }
            }

            // Playing as the animal: the animal's health is the player's health.
            private void SyncHealth(bool force)
            {
                if (!possess || (!force && Time.time < nextHealthSync))
                {
                    return;
                }
                nextHealthSync = Time.time + 0.25f;
                float hp = Mathf.Clamp(Animal.health / Mathf.Max(1f, Animal.MaxHealth()) * Player.MaxHealth(), 1f, Player.MaxHealth());
                if (force || Mathf.Abs(Player.health - hp) > 0.5f)
                {
                    Player.health = hp;
                    Player.SendNetworkUpdate();
                }
            }

            public void Attack()
            {
                if (Time.time < nextAttack)
                {
                    return;
                }
                Vector3 look = Player.eyes.HeadForward();
                look.y = 0f;
                Vector3 facing = Quaternion.Euler(0f, moveYaw, 0f) * Vector3.forward;
                bool rear = look.sqrMagnitude > 0.01f && Vector3.Angle(look, facing) > 110f;
                List<ClipRef> all = ClipCatalog(Animal);
                List<ClipRef> clips = rear ? all.Where(c => Has(c, "behind") || Has(c, "kick")).ToList() : new List<ClipRef>();
                if (clips.Count == 0)
                {
                    clips = all.Where(c => AttackWords.Any(w => Has(c, w)) && !Has(c, "death")).ToList();
                }
                if (clips.Count == 0)
                {
                    plugin.Reply(Player, "no attack clip for " + Animal.ShortPrefabName);
                    return;
                }
                ClipRef clip = clips[attackIndex++ % clips.Count];
                PlayClip(Animal, clip);
                AttackParams p = ReadAttack(Animal, clip, rear);
                nextAttack = Time.time + Mathf.Max(0.8f, p.Delay + 0.4f);
                Invoke(() => DoHits(p, rear), p.Delay);
                plugin.Reply(Player, $"{(rear ? "REAR attack" : "attack")}: {clip.Name} - {p.Damage:0} {p.Type}, reach {p.Range:0.0} m, {p.Angle:0} deg");
            }

            // Damage like the game's own animal attack (BaseCombatEntity.OnAttacked) on everything in reach; trees, ore and
            // corpses are gathered for the player the way a hit without a tool gathers (ResourceDispenser.DoGather).
            private void DoHits(AttackParams p, bool rear)
            {
                if (released || Animal == null || Animal.IsDestroyed || Animal.IsDead())
                {
                    return;
                }
                Bounds b = Animal.bounds;
                Vector3 facing = Quaternion.Euler(0f, moveYaw, 0f) * (rear ? Vector3.back : Vector3.forward);
                Vector3 origin = Animal.transform.position + Vector3.up * b.center.y + facing * (rear ? b.extents.z * 0.5f : b.extents.z * 0.8f);
                var found = new List<BaseEntity>();
                Vis.Entities(origin, p.Range + 1.5f, found);
                var done = new HashSet<BaseEntity>();
                int hits = 0, gathered = 0;
                foreach (BaseEntity e in found)
                {
                    if (e == null || e.IsDestroyed || e == Animal || e == Player || e == Seat || e.GetParentEntity() == Animal || !done.Add(e))
                    {
                        continue;
                    }
                    Vector3 point = e.ClosestPoint(origin);
                    Vector3 v = point - origin;
                    v.y = 0f;
                    if (v.magnitude > p.Range || (p.Angle < 180f && v.sqrMagnitude > 0.01f && Vector3.Angle(facing, v) > p.Angle))
                    {
                        continue;
                    }
                    ResourceDispenser dispenser = e.GetComponent<ResourceDispenser>();
                    if (dispenser != null && (e is TreeEntity || e is OreResourceEntity || e is BaseCorpse))
                    {
                        HitInfo gather = new HitInfo();
                        gather.Init(Player, e, p.Type, p.Damage, point);
                        gather.CanGather = true;
                        dispenser.DoGather(gather, e as BaseCorpse);
                        gathered++;
                        continue;
                    }
                    BaseCombatEntity target = e as BaseCombatEntity;
                    if (target == null || target.IsDead())
                    {
                        continue;
                    }
                    target.OnAttacked(p.Damage, p.Type, Animal, false);
                    hits++;
                    BasePlayer victim = target as BasePlayer;
                    if (victim != null && !victim.IsNpc && p.Push != Vector2.zero)
                    {
                        Vector3 away = v.sqrMagnitude > 0.01f ? v.normalized : facing;
                        victim.DoPush(away * p.Push.y + Vector3.up * p.Push.x);
                    }
                }
                if (hits > 0 || gathered > 0)
                {
                    plugin.Reply(Player, $"hit {hits} target(s), gathered from {gathered}");
                }
            }

            public void OnGesture(string gesture)
            {
                List<ClipRef> clips = ClipCatalog(Animal).Where(IsIdleClip).ToList();
                if (clips.Count == 0)
                {
                    plugin.Reply(Player, "gesture '" + gesture + "': no idle/sound clip on " + Animal.ShortPrefabName);
                    return;
                }
                ClipRef clip = clips[Math.Abs(gesture.GetHashCode()) % clips.Count];
                PlayClip(Animal, clip);
                plugin.Reply(Player, "gesture '" + gesture + "' -> " + clip.Name);
            }

            private static bool IsIdleClip(ClipRef c)
            {
                return IdleWords.Any(w => Has(c, w)) && !NeverWords.Any(w => Has(c, w)) && !AttackWords.Any(w => Has(c, w));
            }

            // Standing still under control: the animal keeps its own idle life (breathing, looking, head shake, calls).
            private void UpdateIdle()
            {
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
                List<ClipRef> clips = ClipCatalog(Animal).Where(IsIdleClip).ToList();
                if (clips.Count > 0)
                {
                    PlayClip(Animal, clips[UnityEngine.Random.Range(0, clips.Count)]);
                }
            }

            public void Jump(float distance, float height)
            {
                if (jumping)
                {
                    return;
                }
                Vector3 forward = Quaternion.Euler(0f, moveYaw, 0f) * Vector3.forward;
                jumpFrom = Animal.transform.position;
                Vector3 to = jumpFrom + forward * distance;
                to.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(to), jumpFrom.y - 3f);
                jumpTo = to;
                jumpHeight = height;
                jumpDuration = Mathf.Clamp(0.35f + distance * 0.06f, 0.45f, 1.2f);
                jumpTime = 0f;
                jumping = true;
                agent.IsJumping = true;
            }

            public void Release(string reason)
            {
                if (released)
                {
                    return;
                }
                released = true;
                if (Player != null)
                {
                    if (spectate)
                    {
                        EndSpectate();
                    }
                    if (possess)
                    {
                        Player.limitNetworking = false;
                        Rust.Ai.SimpleAIMemory.PlayerIgnoreList.Remove(Player);
                        if (selfHidden)
                        {
                            Player.SetPlayerFlag(BasePlayer.PlayerFlags.Ragdolling, false);
                            selfHidden = false;
                        }
                    }
                    if (thirdPerson)
                    {
                        SetThirdPersonFlag(Player, false);
                    }
                    if (Seat != null && !Seat.IsDestroyed)
                    {
                        Seat.DismountPlayer(Player, true);
                    }
                    Player.SendNetworkUpdateImmediate();
                    plugin.ForgetDriver(Player.userID, this);
                }
                if (Seat != null && !Seat.IsDestroyed)
                {
                    Seat.Kill();
                }
                if (Animal != null && !Animal.IsDestroyed)
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
                    if (!wasParked && !Animal.IsDead())
                    {
                        Unpark(Animal, returnState);
                    }
                }
                plugin.Puts("driver released: " + reason);
                Destroy(this);
            }

            private void FixedUpdate()
            {
                if (released)
                {
                    return;
                }
                if (Player == null || !Player.IsConnected || Player.IsDead())
                {
                    Release("player gone");
                    return;
                }
                if (Animal == null || Animal.IsDestroyed || Animal.IsDead())
                {
                    BasePlayer player = Player;
                    bool link = possess;
                    Release("animal died");
                    if (link && player != null && !player.IsDead())
                    {
                        plugin.Reply(player, "your animal died - and you with it");
                        player.Die();
                    }
                    return;
                }
                float dt = Time.fixedDeltaTime;
                UpdateWater();
                SyncHealth(false);
                if (jumping)
                {
                    TickJump(dt);
                    KeepAboveTerrain();
                    UpdateSpectateCamera();
                    return;
                }
                float topSpeed = Mathf.Max(0.5f, GaitSpeed(5));
                float speedFrac = Mathf.Clamp01(Mathf.Abs(currentSpeed) / topSpeed);
                float agility = Mathf.Clamp(agent.angularSpeed / 180f, 0.8f, 1.25f);
                float rate = Mathf.Lerp(150f, 105f, speedFrac) * agility;
                if (turnAboutLeft > 0f)
                {
                    turnAboutLeft -= dt;
                    yawVel = turnDir * 180f / TurnAboutTime;
                }
                else if (spectate && dbd)
                {
                    Vector3 camFwd = Player.eyes.rotation * Vector3.forward;
                    camFwd.y = 0f;
                    if (camFwd.sqrMagnitude < 0.0001f)
                    {
                        camFwd = Quaternion.Euler(0f, moveYaw, 0f) * Vector3.forward;
                    }
                    camFwd.Normalize();
                    Vector3 camRight = Vector3.Cross(Vector3.up, camFwd);
                    Vector3 wish = camFwd * ((fwd ? 1f : 0f) - (back ? 1f : 0f)) + camRight * ((right ? 1f : 0f) - (left ? 1f : 0f));
                    float targetVel = 0f;
                    if (wish.sqrMagnitude > 0.01f)
                    {
                        float wishYaw = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
                        float off = Mathf.DeltaAngle(moveYaw, wishYaw);
                        targetVel = Mathf.Clamp(off * 4f, -rate, rate);
                        // slow down while still turning toward the wished direction instead of running a wide circle
                        dbdAlign = Mathf.Clamp01(1f - Mathf.Abs(off) / 150f);
                    }
                    yawVel = Mathf.MoveTowards(yawVel, targetVel, rate * 7f * dt);
                }
                else
                {
                    float targetVel;
                    if (MouseSteer && (fwd || back))
                    {
                        targetVel = Mathf.Clamp(Mathf.DeltaAngle(moveYaw, Player.eyes.rotation.eulerAngles.y) * 3f, -rate, rate);
                    }
                    else
                    {
                        targetVel = ((right ? 1f : 0f) - (left ? 1f : 0f)) * rate;
                    }
                    yawVel = Mathf.MoveTowards(yawVel, targetVel, rate * 7f * dt);
                }
                moveYaw += yawVel * dt;
                float target;
                if (turnAboutLeft > 0f)
                {
                    target = Mathf.Max(GaitSpeed(1), currentSpeed * 0.4f);
                }
                else if (fwd || (spectate && dbd && (back || left || right)))
                {
                    // dbd mode: any direction key moves forward once the animal has turned toward it
                    target = GaitSpeed(Mathf.Max(EffectiveGait(), 1)) * WaterMultiplier() * (spectate && dbd ? Mathf.Lerp(0.25f, 1f, dbdAlign) : 1f);
                }
                else if (back)
                {
                    target = -GaitSpeed(1) * 0.6f * WaterMultiplier();
                }
                else
                {
                    target = 0f;
                }
                if (Gait == 0 && (fwd || (spectate && dbd && (back || left || right))))
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
                    Vector3 pos = Animal.transform.position;
                    Vector3 next = pos + heading * Vector3.forward * (currentSpeed * dt);
                    agent.Move(agent.WorldToNavSpace(next) - agent.WorldToNavSpace(pos));
                }
                Animal.transform.rotation = Quaternion.Euler(0f, moveYaw + yawOffset, 0f);
                KeepAboveTerrain();
                UpdateSpectateCamera();
                UpdateLook(speedFrac);
                UpdateIdle();
                if (Time.time - lastLog > 5f && Mathf.Abs(currentSpeed) > 0.1f)
                {
                    lastLog = Time.time;
                    plugin.Puts($"[drive] {Animal.ShortPrefabName} {(possess ? "play" : "ride")} gait {Gait} speed {currentSpeed:0.00} turn {yawVel:0} deg/s water {waterFactor:0.00} pos {Animal.transform.position}");
                }
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
                Vector3 seatBase = Animal.transform.TransformPoint(seatOffset);
                if (TerrainMeta.HeightMap == null || (TerrainMeta.Collision != null && TerrainMeta.Collision.GetIgnore(seatBase)))
                {
                    return 0f;
                }
                return Mathf.Max(0f, TerrainMeta.HeightMap.GetHeight(seatBase) + BodyClearance - seatBase.y);
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

            private void TickJump(float dt)
            {
                jumpTime += dt;
                float t = Mathf.Clamp01(jumpTime / jumpDuration);
                Vector3 pos = Vector3.Lerp(jumpFrom, jumpTo, t);
                pos.y += jumpHeight * 4f * t * (1f - t);
                if (TerrainMeta.Collision == null || !TerrainMeta.Collision.GetIgnore(pos))
                {
                    pos.y = Mathf.Max(pos.y, TerrainMeta.HeightMap.GetHeight(pos)); // the arc never cuts through a hill
                }
                Animal.transform.position = pos;
                Animal.transform.rotation = Quaternion.Euler(0f, moveYaw + yawOffset, 0f);
                if (t >= 1f)
                {
                    jumping = false;
                    agent.IsJumping = false;
                    if (!agent.WarpToWorldPosition(jumpTo))
                    {
                        agent.WarpToWorldPosition(jumpFrom);
                        Animal.transform.position = jumpFrom;
                        plugin.Reply(Player, "jump landing not on the navmesh - returned to take-off point");
                    }
                }
            }
        }
    }
}
