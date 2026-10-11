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
                if (LeftUsableBy(animal, player))
                {
                    // the animal a player has left carries him (and whom the riding rule allows), once it has a saddle (like the horse)
                    return !Cfg.Riding.RequireSaddle || CarriedSaddle(player) > 0 || !species.Gear || (Cfg.Riding.AdminBypass && IsAdmin(player)) ? null : "NeedSaddle";
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
                if (Cfg.Riding.RequireSaddle && species.Gear && CarriedSaddle(player) <= 0)
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

        // X (the vanilla seat-swap key) switches between the human and the animal. Pressed again and again, every switch runs to its end first
        // (the views, the effects and their sounds): a new switch is accepted only a moment after the last one.
        private readonly Dictionary<ulong, float> lastSwap = new Dictionary<ulong, float>();

        internal bool SwapAllowed(BasePlayer player)
        {
            float last;
            if (lastSwap.TryGetValue(player.userID, out last) && Time.realtimeSinceStartup - last < 1.2f)
            {
                return false;
            }
            lastSwap[player.userID] = Time.realtimeSinceStartup;
            return true;
        }

        // A player thrown off an animal (a crash, a fall): flung forward and down, the way a horse throws its rider.
        internal void ThrowPlayer(BasePlayer player, Vector3 forward)
        {
            if (player == null || !player.IsConnected || player.IsDead() || player.isMounted)
            {
                return;
            }
            player.Ragdoll(forward * 4f + Vector3.up * 2.5f, true, false, false, null);
            player.Hurt(new HitInfo(null, player, Rust.DamageType.Fall, 5f, player.transform.position));
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
                    d.ReadInput(input);                    // reads the raw keys (with its own edge detection) before anything is taken away
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
                        if (!Bm.SwapAllowed(player))
                        {
                            return true;
                        }
                        BasePlayer p = player;
                        Bm.NextTick(() => Bm.StopControl(p, "key X"));
                        return true;
                }
                return null;
            }

            // G places a map marker with the vanilla bind: while controlling, it is the jump instead.
            public override object OnMapMarkerAdd(BasePlayer player, ProtoBuf.MapNote note)
            {
                Driver d = Bm.DriverOf(player);
                if (d == null)
                {
                    return null;
                }
                d.Jump();                                  // G jumps (Space gets off / back to human); night vision comes by itself
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
                BaseNPC2 animal = entity as BaseNPC2;
                if (animal != null && info != null && Bm.Drivers.Count > 0)
                {
                    Driver d = Bm.DriverOfAnimal(animal);
                    if (d != null && info.damageTypes.Total() > 0.5f)
                    {
                        Bm.NextTick(() =>
                        {
                            if (d.Alive && Valid(animal) && !animal.IsDead())
                            {
                                d.OnHurt();                // the light hit reaction of the species and its voice
                            }
                        });
                    }
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
            public float FeedPoseUntil;     // the grazing or drinking pose of E at food or water, until then (0 = none)
            private float stoppedSince;
            private float rDownSince;
            private bool rHoldDone;
            private bool shiftSinceStop;
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
            private Vector3 seatOffset;          // where the seat sits, in the animal's own space (without the lift)
            private Vector3 seatBaseLocal;       // the same in the space of the seat's parent (the spine bone, or the animal)
            private float seatLift;              // metres the seat is lifted so that the rider never ends up under the ground
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
                // riding keeps the view the player chose last time (G / R or the Controls page); an animal form is always third person
                string view = string.IsNullOrEmpty(settings.View) ? plugin.Cfg.Camera.DefaultRideView : settings.View;
                View = kind == ControlKind.Form ? ViewKind.Third : (view == "first" || !plugin.Cfg.Features.ThirdPerson ? ViewKind.First : ViewKind.Third);
            }

            public bool Begin()
            {
                if (agent == null && Horse == null)
                {
                    bm.TellRaw(Player, "This creature cannot be controlled.");
                    return false;
                }
                if (Kind == ControlKind.Form)
                {
                    bm.FormSmoke(Player.transform.position, Player);          // the human vanishes in the game's effect, at once
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
                if (Kind == ControlKind.Form)
                {
                    return bm.PresetOf(Player) == "B" ? "B" : "A";       // an animal form is played in third person only
                }
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
                    View = tpFlagSet ? ViewKind.Third : ViewKind.First;
                    bm.SettingsOf(Player.userID).View = tpFlagSet ? "third" : "first";      // the next ride starts in this view
                    bm.Tell(Player, tpFlagSet ? "ViewThird" : "ViewFirst", bm.PresetOf(Player));
                    return;
                }
                SetView(View == ViewKind.Third ? ViewKind.First : ViewKind.Third);
            }

            public void SetView(ViewKind view)
            {
                if (Kind == ControlKind.Form && view == ViewKind.First)
                {
                    bm.TellRaw(Player, "An animal form is played in third person only. Riding an animal has both views.");
                    return;
                }
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
                if (Kind == ControlKind.Form && preset != "A" && preset != "B")
                {
                    bm.TellRaw(Player, "An animal form uses the third person cameras A and B only.");
                    return;
                }
                if (Kind == ControlKind.Ride)
                {
                    bm.SettingsOf(Player.userID).Preset = preset;
                    SetView(preset == "D" || preset == "C" ? ViewKind.First : ViewKind.Third);     // riding: the presets choose first or third person
                    return;
                }
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
                string prefab = Kind == ControlKind.Ride ? bm.UsableSeat(bm.Cfg.Riding.SeatPrefab) : bm.Cfg.Camera.FirstPersonSeat;
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
                // the horse's saddle seat is made to hang on the spine of the horse itself (its mount points are on the spine bone): it goes onto
                // the spine as it is; any other seat is placed so that the rider's root sits on the back
                Vector3 anchorLocal = seat.mountAnchor != null && !IsHorseSaddle(prefab) ? seat.transform.InverseTransformPoint(seat.mountAnchor.position) : Vector3.zero;
                Quaternion rotation = Quaternion.Euler(0f, -yawOffset, 0f);
                seatOffset = SeatOffset(rotation * eyeLocal, rotation * anchorLocal) + Vector3.up * bm.SeatLiftOf(Species);
                seatLift = RequiredLift();
                seatBaseLocal = FixSeat(seat, seatOffset, rotation, Kind == ControlKind.Ride);
                ApplySeatLift(seat);
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
                    // on the back where a horse carries its rider, moved forward or back as set for the species (/bm seat forward|back)
                    return BackPoint(Animal) - anchor + Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward * bm.SeatForwardOf(Species);
                }
                // first person: the eye just in front of the head, in the direction of travel
                Vector3 dir = Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward;
                float reach = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
                Vector3 head = new Vector3(b.center.x, Mathf.Max(b.max.y * 0.9f, 0.4f), b.center.z) + dir * (reach + 0.05f);
                return head - eye;
            }

            private static bool IsHorseSaddle(string prefab)
            {
                return prefab != null && prefab.IndexOf("horsesaddle", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // Fixes a seat (not spawned yet) to the animal: on the spine bone for riders, so that the rider moves with the back the way he does on
            // the horse, else on the animal itself. The seat keeps its normal size whatever the scale of the bone or the animal (a boss).
            // Returns the seat's position in its parent's space.
            private Vector3 FixSeat(BaseMountable seat, Vector3 local, Quaternion localRotation, bool onSpine)
            {
                Transform bone = onSpine && bm.Cfg.Riding.SeatOnSpine ? BackBone(Animal) : null;
                if (bone != null)
                {
                    seat.transform.SetPositionAndRotation(Body.transform.TransformPoint(local), Body.transform.rotation * localRotation);
                    seat.SetParent(Body, bone.name, true, false);
                }
                else
                {
                    seat.SetParent(Body, false, false);
                    seat.transform.localRotation = localRotation;
                    seat.transform.localPosition = local;
                }
                Transform parent = seat.transform.parent;
                float parentScale = parent != null ? parent.lossyScale.x : 1f;
                if (parentScale > 0.0001f && Mathf.Abs(parentScale - 1f) > 0.001f)
                {
                    seat.networkEntityScale = true;
                    seat.transform.localScale = Vector3.one / parentScale;
                }
                return seat.transform.localPosition;
            }

            // The seat at its place plus the lift (the lift is straight up in the world, whatever the bone's own axes are).
            private void ApplySeatLift(BaseMountable seat)
            {
                Transform parent = seat.transform.parent;
                Vector3 up = parent != null ? parent.InverseTransformVector(Vector3.up) : Vector3.up;
                seat.transform.localPosition = seatBaseLocal + up * seatLift;
            }

            private Vector3 SeatBaseWorld()
            {
                if (Seat != null && !Seat.IsDestroyed && Seat.transform.parent != null)
                {
                    return Seat.transform.parent.TransformPoint(seatBaseLocal);
                }
                return Body.transform.TransformPoint(seatOffset);
            }

            // A rider looks to the sides at most, like on the horse: a head turned further round is turned back.
            private float nextViewClamp;

            private void ClampRiderView()
            {
                float limit = bm.Cfg.Riding.ViewClamp;
                if (limit <= 0f || limit >= 180f || Time.time < nextViewClamp || Player == null || !Player.isMounted)
                {
                    return;
                }
                Vector3 look = Player.eyes.HeadForward();
                float rel = Mathf.DeltaAngle(moveYaw, Yaw(look));
                if (Mathf.Abs(rel) <= limit + 3f)
                {
                    return;
                }
                nextViewClamp = Time.time + 0.2f;
                float pitch = -Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg;
                ForceView(Mathf.Sign(rel) * limit, pitch);
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
                    if (bm.Cfg.Camera.RefreshSubscriptions)
                    {
                        Player.net.InvalidateSubscriptions(2);
                    }
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
                        if (bm.Cfg.Camera.RefreshSubscriptions)
                        {
                            Player.net.InvalidateSubscriptions(2);
                        }
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
                Player.transform.localPosition = pivot - PlayerEyes.EyeOffset + (duckHeld ? Vector3.up * bm.Cfg.Camera.DuckLift : Vector3.zero);
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
                // next to the animal on whatever it stands on (a floor, a roof, the terrain) - never under a building
                Vector3 drop = Body != null && !Body.IsDestroyed
                    ? GroundNear(Body.transform.position, Body.bounds.extents.x * ModelScale(Body) + 1f)
                    : Player.transform.position;
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
            internal static readonly MethodInfo ClientRpcString = FindClientRpc(typeof(string));

            private static MethodInfo FindClientRpc(Type argType)
            {
                return typeof(BaseEntity).GetMethods().FirstOrDefault(m => m.Name == "ClientRPC" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == argType);
            }

            internal static void SendPlayerRpc(BasePlayer player, MethodInfo rpc, string name, object arg)
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

            // The buttons of the last input, as the player pressed them. The game's own "just pressed" cannot be used: jump and duck are taken out
            // of a spectator's input after it is read, so the game would see a held Space or Ctrl as pressed again and again (a held Space jumped
            // over and over, and Ctrl + R never reached the menu).
            private int lastButtons;
            private bool inputSeen;

            private static bool IsSet(int buttons, BUTTON b)
            {
                return (buttons & (int)b) != 0;
            }

            // ------------------------------------------------------------ where the camera looks
            // The game does not run the normal tick of a spectating player, so its eyes (Player.eyes) keep the direction they had when the
            // form began: the old code read that stale direction and played the rear kick when the player looked forward, and the head of
            // the animal stared to the side. The angles of the player's input are fresh; they are relative to the animal the hidden body
            // is parented to. In case a game build sends them in world space, the frame is learnt from how they behave while the animal turns.
            private float aimYaw, aimPitch, lastAimYaw, lastBodyYaw;
            private bool aimSeen;
            private int aimWorldVotes, aimLocalVotes;

            private void TrackAim(Vector3 aim)
            {
                float bodyYaw = Body.transform.eulerAngles.y;
                if (aimSeen)
                {
                    float dBody = Mathf.DeltaAngle(lastBodyYaw, bodyYaw);
                    float dAim = Mathf.DeltaAngle(lastAimYaw, aim.y);
                    float turn = Mathf.Abs(dBody);
                    if (turn > 1.5f)
                    {
                        if (Mathf.Abs(dAim - dBody) < turn * 0.3f)
                        {
                            aimWorldVotes = Mathf.Min(aimWorldVotes + 1, 60);
                        }
                        else if (Mathf.Abs(dAim) < turn * 0.3f)
                        {
                            aimLocalVotes = Mathf.Min(aimLocalVotes + 1, 60);
                        }
                    }
                }
                aimSeen = true;
                lastAimYaw = aim.y;
                lastBodyYaw = bodyYaw;
                aimYaw = aim.y;
                aimPitch = aim.x > 180f ? aim.x - 360f : aim.x;
            }

            private bool AimIsWorld => aimWorldVotes > aimLocalVotes + 3;

            // The direction of the camera in the world.
            public Vector3 CameraForward()
            {
                if (!Spectating || !aimSeen)
                {
                    return Player.eyes.HeadForward();
                }
                float yaw = AimIsWorld ? aimYaw : Body.transform.eulerAngles.y + aimYaw;
                return Quaternion.Euler(Mathf.Clamp(aimPitch, -89f, 89f), yaw, 0f) * Vector3.forward;
            }

            // Ctrl is the brake: the game's client still lowers the camera of a crouching player, so while Ctrl is held the hidden body is
            // lifted by the same height and the camera stays where it was.
            private bool duckHeld;

            public void ReadInput(InputState input)
            {
                int now = input.current.buttons;
                int prev = inputSeen ? lastButtons : now;      // a key still held from before the control began is no new press
                inputSeen = true;
                lastButtons = now;
                TrackAim(input.current.aimAngles);
                Func<BUTTON, bool> pressed = b => IsSet(now, b) && !IsSet(prev, b);
                fwd = IsSet(now, BUTTON.FORWARD);
                back = IsSet(now, BUTTON.BACKWARD);
                left = IsSet(now, BUTTON.LEFT);
                right = IsSet(now, BUTTON.RIGHT);
                bool duck = IsSet(now, BUTTON.DUCK);
                if (duck != duckHeld)
                {
                    duckHeld = duck;
                    if (Spectating && !released && bm.Cfg.Camera.DuckLift > 0f)
                    {
                        ApplyPreset(false);
                    }
                }
                if (fwd || back || left || right)
                {
                    InterruptClip();                       // walking or running takes over from the idle animation
                    bm.EndFeedPose(this);
                }
                if (PoseName != null && (fwd || back || left || right || pressed(BUTTON.JUMP) || pressed(BUTTON.FIRE_THIRD)))
                {
                    ClearPose();
                }
                if (pressed(BUTTON.RELOAD) && duck)
                {
                    bm.OpenMenu(Player, "home");          // Ctrl + R: the menu, also while riding or as an animal
                    return;
                }
                if (pressed(BUTTON.SPRINT))
                {
                    braking = false;
                    if (!fwd && Mathf.Abs(currentSpeed) < 0.3f && !shiftSinceStop)
                    {
                        Gait = 1;                          // standing: the gaits count up from a walk again
                    }
                    if (!fwd && Mathf.Abs(currentSpeed) < 0.3f)
                    {
                        shiftSinceStop = true;             // a gait chosen before moving off is kept
                    }
                    SetGait(Mathf.Max(Gait, 1) + 1);
                }
                if (pressed(BUTTON.DUCK) && Kind == ControlKind.Ride)
                {
                    Brake();                               // a rider sits: Ctrl does not touch his camera
                }
                if (pressed(BUTTON.BACKWARD) && currentSpeed > 0.6f)
                {
                    // S while going forward: the brake (as an animal Ctrl would lower the game's camera); S with A or D at a run: the turn
                    if ((left || right) && currentSpeed > GaitSpeed(3) * 0.9f)
                    {
                        TurnAbout();
                    }
                    else
                    {
                        Brake();
                    }
                }
                if (pressed(BUTTON.JUMP))
                {
                    // Space: off the animal (riding) or back to human (animal form); G jumps
                    BasePlayer self = Player;
                    if (Kind == ControlKind.Ride || bm.SwapAllowed(self))
                    {
                        bm.NextTick(() => bm.StopControl(self, Kind == ControlKind.Ride ? "key Space" : "key Space"));
                    }
                    return;
                }
                if (pressed(BUTTON.FIRE_THIRD))
                {
                    Attack();
                }
                // R: a tap changes the skin (animal form) or the view (riding); held a moment it opens the window of all the looks
                if (pressed(BUTTON.RELOAD) && !duck)
                {
                    rDownSince = Time.time;
                    rHoldDone = false;
                }
                if (rDownSince > 0f && IsSet(now, BUTTON.RELOAD) && !rHoldDone && Time.time - rDownSince >= 0.45f)
                {
                    rHoldDone = true;
                    bm.OpenSkinMenu(Player, Body);
                }
                if (rDownSince > 0f && !IsSet(now, BUTTON.RELOAD))
                {
                    if (!rHoldDone)
                    {
                        if (Kind == ControlKind.Form)
                        {
                            if (HasSkins)
                            {
                                bm.ReskinForm(this);
                            }
                        }
                        else
                        {
                            CycleView();
                        }
                    }
                    rDownSince = 0f;
                }
                if (Kind == ControlKind.Form && Spectating && pressed(BUTTON.USE))
                {
                    bm.InteractFrom(this, duck);           // E: doors, loot, food, things; Ctrl + E: the animal's inventory
                }
            }

            // The animals whose look can be changed with R: livestock (coat and pattern) and the horse (breed).
            public bool HasSkins => Body is LivestockAnimal || Horse != null;

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
                if (Horse != null && bm.Cfg.Movement.SpeedMode != "table")
                {
                    // the horse form: the horse's own gaits (walk, trot, canter, gallop), one more with each Shift - W alone only walks
                    float[] gaits = bm.HorseFormGaits();
                    speed = gait <= 0 ? 0f : gaits[Mathf.Clamp(gait, 1, 5) - 1];
                    speed += bm.HorseGearBonus(Horse) * (gait / 5f);
                    return Mathf.Max(0f, speed * scale * bm.ClimateSpeedFactor(Body) * bm.StatsSpeedFactor(Body));
                }
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
                    speed += (Animal != null ? bm.GearSpeedBonus(Animal) : 0f) * (gait / 5f);     // horseshoes, saddle bag, armour
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
                if (Kind == ControlKind.Form)
                {
                    bm.FormAnimalDied(this);                // what it carried falls next to its body
                }
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
                // the camera is a circle round the animal: the front half attacks to the front, the rear half kicks behind (hoofed animals)
                float camYaw = Yaw(CameraForward());
                bool rear = KicksBehind(Species) && Mathf.Abs(Mathf.DeltaAngle(moveYaw, camYaw)) > bm.Cfg.Combat.RearAngle;
                if (Horse != null)
                {
                    HorseAttack(rear);
                    return;
                }
                InterruptClip();
                ClearPose();
                List<ClipRef> all = bm.Clips(Animal);
                List<ClipRef> clips;
                if (rear)
                {
                    // the kick of the cow; the sheep and the ram stamp, the stag strikes with its own attack - their models have no kick
                    clips = all.Where(c => (Has(c, "behind") || Has(c, "kick")) && !Has(c, "death")).ToList();
                    if (clips.Count == 0)
                    {
                        clips = all.Where(c => Has(c, "stomp") && !Has(c, "death")).ToList();
                    }
                }
                else
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
                bm.AnimalVoice(Body, Species, "attack");          // the stag's clips carry no sound of their own
                bm.StatsSpend(this, bm.Cfg.Stamina.Attack);
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

            // The hoofed animals kick behind them; the others always attack to the front.
            internal static bool KicksBehind(SpeciesDef s)
            {
                if (s == null)
                {
                    return false;
                }
                switch (s.Id)
                {
                    case "cow": case "bull": case "calf": case "sheep": case "ram": case "lamb": case "stag": case "doe": case "horse": return true;
                    default: return false;
                }
            }

            public void OnGesture(string gesture)
            {
                bm.PerformGesture(this, gesture);
            }

            // Hurt: the light hit reaction of the species (never its knock-down, which would throw the animal to the ground) and its voice.
            private float nextHurt;

            public void OnHurt()
            {
                if (released || Animal == null || jumping || Time.time < nextHurt || Time.time < fallenUntil)
                {
                    return;
                }
                nextHurt = Time.time + 1.2f;
                ClipRef hit = bm.Clips(Animal).FirstOrDefault(c => c.Field.StartsWith("WeakHit", StringComparison.OrdinalIgnoreCase) && !Has(c, "knock") && !Has(c, "death"));
                if (hit != null && idleState == null && Time.time >= nextAttack - 0.2f)
                {
                    PlayClip(Animal, hit);
                }
                bm.AnimalVoice(Body, Species, "hurt");
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
                idleEnds = idleStarted + ClipLength(clip) - 0.05f;
            }

            private float idleEnds;

            public void InterruptClip()
            {
                if (idleState != null)
                {
                    // only a montage that still plays is stopped (a finished one is back in the game's pool and must not be returned twice);
                    // its real length is known now, so long idles (16 s for the stag and the boar) give way to walking too
                    if (Time.time < idleEnds)
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

            // behindDriver: the animal already carries its own rider (a double saddle) - the guest sits behind him.
            public bool AddGuest(BasePlayer guest, bool behindDriver = false)
            {
                if (Animal == null)
                {
                    return false;
                }
                bool rear = behindDriver || Guests.Count > 0;
                string prefab = bm.UsableSeat(rear ? bm.Cfg.Riding.RearSeatPrefab : bm.Cfg.Riding.SeatPrefab);
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
                Vector3 anchor = seat.mountAnchor != null && !IsHorseSaddle(prefab) ? rotation * seat.transform.InverseTransformPoint(seat.mountAnchor.position) : Vector3.zero;
                Vector3 place = BackPoint(Animal) - anchor + Vector3.up * bm.SeatLiftOf(Species) + rotation * Vector3.forward * bm.SeatForwardOf(Species);
                if (rear)
                {
                    // the second rider behind the first, as on the horse (0.4 m there), a little more on a long animal
                    place += rotation * Vector3.back * Mathf.Clamp(Body.bounds.extents.z * 0.35f, 0.4f, 0.65f);
                }
                FixSeat(seat, place, rotation, true);         // on the spine too: the second rider rocks with the back as well
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
                if (bm.OutOfBreath(this))
                {
                    return;                                 // out of breath: no jumps until the stamina is back
                }
                if (Horse != null)
                {
                    HorseJump();
                    return;
                }
                InterruptClip();
                ClearPose();
                float k = ModelScale(Body);
                float distance = bm.Cfg.Movement.JumpDistance * Mathf.Clamp(Body.bounds.size.z / 2.4f, 0.45f, 1.6f) * k;
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
                if (jumpCollision)
                {
                    to.y = jumpFrom.y;
                    platformLanding = offMesh;              // a jump from a roof into a wall falls back onto that roof, not through it
                }
                else
                {
                    // where the jump ends there must be a surface the animal can stand on (the terrain, a floor, a roof, a crate, the top of a rock)
                    // and room for its body: a spot inside or under a rock or an object that is too high is never a landing - the jump is
                    // shortened to the last good spot before it, and when there is none the animal runs into it
                    Vector3 landing;
                    bool onObject;
                    if (ValidLanding(to, clear, k, out landing, out onObject))
                    {
                        to = landing;
                        platformLanding = onObject;
                    }
                    else
                    {
                        bool found = false;
                        Vector3 flat = to - jumpFrom;
                        flat.y = 0f;
                        for (float d = flat.magnitude - 0.3f; d > 0.25f; d -= 0.3f)
                        {
                            if (ValidLanding(jumpFrom + forward * d, clear, k, out landing, out onObject))
                            {
                                to = landing;
                                platformLanding = onObject;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            jumpCollision = true;
                            jumpHitPoint = to + Vector3.up * 0.5f;
                            to = jumpFrom;
                            platformLanding = offMesh;
                            height = Mathf.Min(height, 0.35f * k);
                        }
                    }
                    if (!jumpCollision)
                    {
                        height = Mathf.Max(height, to.y - jumpFrom.y + 0.4f * k);
                    }
                }
                jumpTo = to;
                jumpHeight = height;
                jumpDuration = Mathf.Clamp(0.35f + Vector3.Distance(jumpFrom, to) * 0.06f, 0.4f, 1.2f);
                jumpTime = 0f;
                jumping = true;
                nextJump = Time.time + bm.Cfg.Movement.JumpCooldown;
                agent.IsJumping = true;
                bm.StatsSpend(this, bm.Cfg.Stamina.Jump);
                bm.LandSound(jumpFrom);                         // the push-off
            }

            // A spot the animal can land on: the highest walkable surface there that it can reach (searched from just above the height it can
            // jump to, so a roof overhead does not count), not too far down, and room for the body above it (not inside a rock or an object).
            private bool ValidLanding(Vector3 at, float clear, float k, out Vector3 landing, out bool onObject)
            {
                landing = at;
                onObject = false;
                float terrain = TerrainMeta.HeightMap.GetHeight(at);
                float top = jumpFrom.y + clear + 0.5f;
                float bottom = Mathf.Min(terrain, jumpFrom.y - 3.5f) - 0.5f;
                Vector3 surface = new Vector3(at.x, terrain, at.z);
                RaycastHit g;
                if (top > bottom && Physics.Raycast(new Vector3(at.x, top, at.z), Vector3.down, out g, top - bottom, ObstacleMaskOf() | LayerMask.GetMask("Terrain"), QueryTriggerInteraction.Ignore))
                {
                    if (g.normal.y < 0.35f)
                    {
                        return false;                       // a wall or a steep face: nothing to stand on
                    }
                    surface = g.point;
                    onObject = !(g.collider is TerrainCollider) && g.point.y > terrain + 0.12f;
                }
                if (surface.y < jumpFrom.y - 3.5f || surface.y > jumpFrom.y + clear + 0.1f)
                {
                    return false;
                }
                float bodyHeight = Mathf.Max(0.9f, Body.bounds.size.y * k * 0.6f);
                float r = Mathf.Clamp(Mathf.Min(Body.bounds.extents.x, Body.bounds.extents.z) * 0.5f * k, 0.15f, 0.45f);
                if (Physics.CheckCapsule(surface + Vector3.up * (r + 0.15f), surface + Vector3.up * Mathf.Max(bodyHeight, r + 0.3f), r, ObstacleMaskOf(), QueryTriggerInteraction.Ignore))
                {
                    return false;                           // inside a rock, under a low roof, in a wall
                }
                landing = surface + Vector3.up * 0.03f;
                return true;
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

            // A jump or a full sprint into something solid: the animal hits it, hurts itself a little and falls (with the sound of the hit and of
            // the thing it hit). A rider is thrown off, like from a horse.
            private void Crash()
            {
                jumpCollision = false;
                currentSpeed = 0f;
                Gait = 1;
                fallenUntil = Time.time + 2.4f;
                bm.BumpSound(jumpHitPoint, jumpHitSurface);
                FallAnimation(false);
                Body.Hurt(Mathf.Max(2f, Body.MaxHealth() * bm.Cfg.Movement.CrashDamagePercent / 100f), Rust.DamageType.Blunt, null, false);
                ThrowRider();
            }

            // The fall of each kind: the horse's own ragdoll; the knock-down of the cow, bull and calf; the sheep, ram and lamb have no
            // knock-down, so they fall with their death animation and lie for a moment; the stag and the boar go down in their lying pose.
            private bool restingFromFall;

            private void FallAnimation(bool downhill)
            {
                if (Horse != null)
                {
                    Horse.RagdollHorse();
                    return;
                }
                if (Animal == null)
                {
                    return;
                }
                InterruptClip();
                ClearPose();
                ClipRef knock = bm.Clips(Animal).FirstOrDefault(c => Has(c, downhill ? "canter_knockdown" : "stand_knockdown"))
                    ?? bm.Clips(Animal).FirstOrDefault(c => Has(c, "knockdown") || Has(c, "stumble"));
                if (knock != null)
                {
                    PlayClip(Animal, knock);
                    return;
                }
                LivestockAnimal livestock = Animal as LivestockAnimal;
                ClipRef down = bm.Clips(Animal).FirstOrDefault(c => Has(c, downhill ? "canter_death" : "death_front"))
                    ?? bm.Clips(Animal).FirstOrDefault(c => Has(c, "death_front") || Has(c, "canter_death"));
                if (livestock != null && down != null)
                {
                    PlayClip(Animal, down);
                    livestock.SetResting(true);              // it lies there for a moment and gets up with the game's own animation
                    restingFromFall = true;
                    return;
                }
                if (!bm.Play(Animal, "sleep", "lie", "rest") && livestock != null)
                {
                    livestock.SetResting(true);
                    restingFromFall = true;
                }
            }

            private void EndFallPose()
            {
                if (!restingFromFall)
                {
                    return;
                }
                restingFromFall = false;
                LivestockAnimal livestock = Animal as LivestockAnimal;
                if (livestock != null && Valid(livestock))
                {
                    livestock.SetResting(false);
                }
            }

            // The control ends while the animal is still down after a fall: it gets up when the fall is over, without its driver.
            private void EndFallPoseLater()
            {
                if (!restingFromFall)
                {
                    return;
                }
                restingFromFall = false;
                LivestockAnimal livestock = Animal as LivestockAnimal;
                if (livestock == null)
                {
                    return;
                }
                float wait = Mathf.Max(0.1f, fallenUntil - 0.8f - Time.time);
                bm.timer.Once(wait, () =>
                {
                    if (Valid(livestock) && !livestock.IsDead())
                    {
                        livestock.SetResting(false);
                    }
                });
            }

            // A crash or a fall throws the rider and the passengers off, the way a horse does. An animal form throws its passengers only.
            private void ThrowRider()
            {
                if (released || Player == null)
                {
                    return;
                }
                BasePlayer rider = Player;
                Vector3 forward = Quaternion.Euler(0f, moveYaw, 0f) * Vector3.forward;
                foreach (BaseMountable guestSeat in Guests.ToList())
                {
                    BasePlayer guest = guestSeat != null && !guestSeat.IsDestroyed ? guestSeat.GetMounted() : null;
                    if (guest != null)
                    {
                        BasePlayer thrown = guest;
                        guestSeat.DismountAllPlayers();
                        bm.NextTick(() => bm.ThrowPlayer(thrown, forward));
                    }
                }
                if (Kind != ControlKind.Ride)
                {
                    return;
                }
                bm.StopControl(rider, "thrown off");
                bm.NextTick(() => bm.ThrowPlayer(rider, forward));
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
                bm.StatsSpend(this, bm.Cfg.Stamina.Jump);
                bm.LandSound(Body.transform.position);
            }

            // F as the horse: rear up and strike to the front, or - with the camera behind it - kick behind (the horse has no kick animation:
            // the hit and its sound only).
            private void HorseAttack(bool rear)
            {
                nextAttack = Time.time + bm.Cfg.Horse.AttackCooldown;
                bm.StatsSpend(this, bm.Cfg.Stamina.Attack);
                if (bm.Stable == null)
                {
                    return;
                }
                if (rear)
                {
                    bm.Stable.PlayerKickBack(Horse, this);
                }
                else
                {
                    bm.Stable.PlayerStrike(Horse, this);
                }
            }

            // Running down a very steep slope: the animal may stumble and fall (and sometimes does not get up), like the horse.
            private void CheckSlope(float dt)
            {
                if (bm.Cfg.Movement.SlopeFallChance <= 0f || Time.time < slopeCheckAt || !AtFullSprint())
                {
                    return;                                     // only a full sprint down a steep slope ends in a fall
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
                fallenUntil = Time.time + 2.8f;
                currentSpeed = 0f;
                Gait = 1;
                FallAnimation(true);                            // the horse's own fall; the others with the fall of their kind
                if (Horse == null)
                {
                    bm.BumpSound(pos + Vector3.up * 0.5f, "dirt");
                }
                Body.Hurt(Mathf.Max(3f, Body.MaxHealth() * bm.Cfg.Movement.CrashDamagePercent / 100f), Rust.DamageType.Fall, null, false);
                ThrowRider();
                if (UnityEngine.Random.value < bm.Cfg.Movement.SlopeFallDeathChance)
                {
                    Body.Hurt(Body.health + 1f, Rust.DamageType.Fall);
                }
            }

            // The fastest gait of the animal, nearly at its top speed: only then does it crash into walls, run into others and fall on slopes.
            public bool AtFullSprint()
            {
                return Gait >= 5 && Mathf.Abs(currentSpeed) >= GaitSpeed(5) * 0.8f;
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
                    // moved straight up or down in the world (the seat may hang on a bone with axes of its own); the height stays as set
                    Transform parent = Seat.transform.parent;
                    Vector3 world = Vector3.up * delta * ModelScale(Body);
                    seatBaseLocal += parent != null ? parent.InverseTransformVector(world) : world;
                    seatLift = RequiredLift();
                    ApplySeatLift(Seat);
                    Seat.SendNetworkUpdate();
                }
            }

            // /bm seat forward|back while riding: the seat of this species moved along the back, kept for everybody
            public void AdjustSeatForward(float delta)
            {
                SpeciesOverride o;
                if (!bm.Cfg.Species.TryGetValue(Species.Id, out o))
                {
                    o = new SpeciesOverride();
                    bm.Cfg.Species[Species.Id] = o;
                }
                o.SeatForward = (o.SeatForward ?? 0f) + delta;
                bm.SaveConfig();
                Vector3 along = Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward * delta;
                seatOffset += along;
                if (Seat != null && !Seat.IsDestroyed)
                {
                    Transform parent = Seat.transform.parent;
                    Vector3 world = Body.transform.TransformVector(along);
                    seatBaseLocal += parent != null ? parent.InverseTransformVector(world) : world;
                    seatLift = RequiredLift();
                    ApplySeatLift(Seat);
                    Seat.SendNetworkUpdate();
                }
            }

            public void SuppressCrash(float seconds)
            {
                nextWallCrash = Mathf.Max(nextWallCrash, Time.time + seconds);
            }

            // Another animal (or a horse) close by: the navigation mesh around it does not let the agent come near, so the plugin moves the body.
            private float nearBodyAt;
            private bool nearBodyWas;
            private Vector3 nearBodyPos;

            private bool NearOtherBody(Vector3 at)
            {
                if (Time.time < nearBodyAt && (at - nearBodyPos).sqrMagnitude < 0.25f)
                {
                    return nearBodyWas;
                }
                nearBodyAt = Time.time + 0.25f;
                nearBodyPos = at;
                var found = new List<BaseCombatEntity>();
                Vis.Entities(at, 3f, found);
                nearBodyWas = found.Any(e => e != null && e != Body && !e.IsDestroyed && !e.IsDead() && (e is BaseNPC2 || e is RidableHorse || e is BaseAnimalNPC)
                    && Vector3.Distance(e.ClosestPoint(at), at) < 1.8f);
                return nearBodyWas;
            }

            // Is there an object surface (a floor, a step, a crate) right under the animal that is higher than the navigation mesh found nearby?
            private bool StandsAbove(Vector3 here, float navY)
            {
                RaycastHit g;
                return Physics.Raycast(here + Vector3.up * 0.6f, Vector3.down, out g, 1.6f, ObstacleMaskOf(), QueryTriggerInteraction.Ignore)
                    && g.normal.y > 0.3f && g.point.y > navY + 0.2f;
            }

            // The animal stands where the plugin moved it, without the navigation mesh under it (a platform, a jump): the agent takes the body
            // again at the nearest point of the mesh, so that the animal's own AI does not stay frozen with a switched-off agent.
            private void RestoreAgent()
            {
                if (agent == null || Animal == null || Body == null || Body.IsDestroyed)
                {
                    return;
                }
                bool wasOff = offMesh || jumping || !agent.updatePosition;
                offMesh = false;
                jumping = false;
                agent.IsJumping = false;
                agent.updatePosition = true;
                if (!wasOff || Body.IsDead())
                {
                    return;
                }
                Vector3 onNav;
                if (Animal.TrySampleNavmesh(Body.transform.position, 4f, out onNav))
                {
                    agent.WarpToWorldPosition(onNav);
                }
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

            // The heading of the animal is the plugin's: the navigation agent (sliding along a wall) and the root motion of the game's clips
            // (an idle, a fall) may turn the body between two physics steps, and the camera of the animal form turns with the body. The heading
            // is set again at the end of every frame, so the camera never swings round on its own.
            private void LateUpdate()
            {
                if (released || Animal == null || Body == null || Body.IsDestroyed || Body.IsDead() || Animal.IsMounted)
                {
                    return;                                 // loaded onto a transport, the animal turns with the vehicle
                }
                Quaternion want = Quaternion.Euler(0f, moveYaw + yawOffset, 0f);
                if (Quaternion.Angle(Body.transform.rotation, want) > 0.5f)
                {
                    Body.transform.rotation = want;
                }
            }

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
                // stopped for a moment: the next W walks again, each Shift adds a gait (the chosen gait is not kept for ever)
                if (!fwd && Mathf.Abs(currentSpeed) < 0.3f)
                {
                    if (stoppedSince <= 0f)
                    {
                        stoppedSince = Time.time;
                    }
                    else if (Time.time - stoppedSince > 0.6f && Gait > 1 && !shiftSinceStop)
                    {
                        Gait = 1;
                    }
                }
                else
                {
                    stoppedSince = 0f;
                    if (fwd)
                    {
                        shiftSinceStop = false;
                    }
                }
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
                if (restingFromFall && Time.time >= fallenUntil - 0.8f)
                {
                    EndFallPose();                      // gets up in time to walk on when the fall is over
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
                    // back onto the navigation mesh only where the animal really stands on that ground: next to a low platform the mesh of the
                    // ground beside it is close too, and stepping onto it there pulled the animal down through the platform
                    if (Animal.TrySampleNavmesh(here, 0.8f, out onNav) && new Vector2(onNav.x - here.x, onNav.z - here.z).magnitude < 0.5f && Mathf.Abs(onNav.y - here.y) < 0.7f
                        && !StandsAbove(here, onNav.y) && !NearOtherBody(here))
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
                    if (hitWall && speedBefore > GaitSpeed(5) * 0.8f && Gait >= 5 && Time.time >= nextWallCrash)
                    {
                        nextWallCrash = Time.time + 3f;
                        jumpHitPoint = wallPoint;
                        jumpHitSurface = wallSurface;
                        Crash();                                // a wall or an object at a run: the animal hurts itself and falls
                        return;
                    }
                    Vector3 next = pos + step;
                    next = bm.LeashClamp(this, next);
                    if (!offMesh && (StepOntoObject(pos, next) || NearOtherBody(next)))
                    {
                        // the way leads up onto something without a navigation mesh, or close to another animal (the navigation mesh has a hole
                        // around it that kept small animals away): the plugin walks the animal there
                        offMesh = true;
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
                Vector3 v = horseBody.linearVelocity;               // what the physics made of the last step (a wall stops it)
                horseBody.linearVelocity = new Vector3(forward.x * currentSpeed, v.y, forward.z * currentSpeed);
                bm.LeashHorse(this, horseBody);
                HorseWallCheck(dt, v);
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

            // The horse at a full gallop that is stopped dead by a wall or an object: it falls (its own ragdoll) and hurts itself, like the
            // other animals. A player or an animal in the way is no wall - running into them is an attack.
            private float horseStall;

            // measured: the horse's velocity after the last physics step, before the drive set the new one
            private void HorseWallCheck(float dt, Vector3 measured)
            {
                if (!AtFullSprint() || Time.time < nextWallCrash || horseBody == null)
                {
                    horseStall = 0f;
                    return;
                }
                Vector3 v = measured;
                v.y = 0f;
                horseStall = v.magnitude < Mathf.Abs(currentSpeed) * 0.35f ? horseStall + dt : 0f;
                if (horseStall < 0.2f)
                {
                    return;
                }
                horseStall = 0f;
                Vector3 forward = Horse.transform.forward;
                forward.y = 0f;
                RaycastHit hit;
                if (!Physics.SphereCast(Horse.transform.position + Vector3.up * 1.1f, 0.4f, forward.normalized, out hit, 2.6f, ObstacleMaskOf(), QueryTriggerInteraction.Ignore) || hit.normal.y > 0.35f)
                {
                    return;
                }
                BaseEntity thing = hit.GetEntity();
                if (thing is BasePlayer || thing is BaseNPC2 || thing is RidableHorse)
                {
                    return;
                }
                nextWallCrash = Time.time + 3f;
                jumpHitPoint = hit.point;
                jumpHitSurface = Beastmaster.ColliderSurface(hit.collider);
                currentSpeed = 0f;
                Gait = 1;
                fallenUntil = Time.time + 2.8f;
                bm.BumpSound(jumpHitPoint, jumpHitSurface);
                Horse.RagdollHorse();
                Body.Hurt(Mathf.Max(2f, Body.MaxHealth() * bm.Cfg.Movement.CrashDamagePercent / 100f), Rust.DamageType.Blunt, null, false);
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
                Vector3 local = Quaternion.Inverse(body) * CameraForward();
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (Mathf.Abs(yaw) > 120f)
                {
                    yaw = 0f;                           // the camera behind the animal: it looks ahead, never over its own back
                    pitch = 0f;
                }
                float maxYaw = Mathf.Lerp(70f, 35f, speedFrac);
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

            // The body sits at the seat's position. Antihack (InsideTerrain) kills and kicks a player whose position is more than 0.3 m under
            // the terrain - the OnPlayerViolation hook can stop the kick but not the kill. The seat stays where it was set (/bm seat up|down
            // included); only when it would really sink that deep (a tiny creature in a hollow, a jump arc into a slope) is it lifted, and
            // it goes back down as soon as that is over.
            private float RequiredLift()
            {
                Vector3 seatBase = SeatBaseWorld();
                if (TerrainMeta.HeightMap == null || (TerrainMeta.Collision != null && TerrainMeta.Collision.GetIgnore(seatBase)))
                {
                    return 0f;
                }
                return Mathf.Max(0f, TerrainMeta.HeightMap.GetHeight(seatBase) - 0.2f - seatBase.y);
            }

            private void KeepAboveTerrain()
            {
                if (Seat == null || Seat.IsDestroyed)
                {
                    return;
                }
                if (Kind == ControlKind.Ride)
                {
                    ClampRiderView();
                }
                float lift = RequiredLift();
                if (lift > seatLift + 0.01f || (seatLift > 0f && lift < seatLift - 0.05f))
                {
                    seatLift = lift;
                    ApplySeatLift(Seat);
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
                EndFallPoseLater();
                bm.EndFeedPose(this);
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
                    RestoreAgent();
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
                EndFallPoseLater();                 // a sheep or ram down after a crash gets up in time, also when its rider is thrown off
                bm.EndFeedPose(this);
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
                    if (!form && Player.IsConnected && !Player.IsDead() && Valid(Body))
                    {
                        // the rider gets off beside the animal, standing on what the animal stands on
                        Player.Teleport(GroundNear(Body.transform.position, Body.bounds.extents.x * ModelScale(Body) + 0.9f));
                    }
                    Player.SendNetworkUpdateImmediate();
                    bm.Drivers.Remove(Player.userID);
                }
                if (Body != null && !Body.IsDestroyed)
                {
                    if (agent != null)
                    {
                        RestoreAgent();
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
