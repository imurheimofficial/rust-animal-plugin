using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
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
}
