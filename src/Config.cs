using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
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
            [JsonProperty("Stamina (like the horse's)")] public StaminaCfg Stamina = new StaminaCfg();
            [JsonProperty("Animal life (prompts, dung, eggs, wild jumps)")] public LifeCfg Life = new LifeCfg();
            [JsonProperty("Taming")] public TamingCfg Taming = new TamingCfg();
            [JsonProperty("Gear")] public GearCfg Gear = new GearCfg();
            [JsonProperty("Transformation ritual")] public RitualCfg Ritual = new RitualCfg();
            [JsonProperty("Lead")] public LeadCfg Lead = new LeadCfg();
            [JsonProperty("Controls (key binds)")] public ControlsCfg Controls = new ControlsCfg();
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
            [JsonProperty("Seat used for riders on non-horse animals (the horse's saddle seat: the rider sits the way he sits on a horse)")] public string SeatPrefab = "assets/prefabs/vehicle/seats/horsesaddle.prefab";
            [JsonProperty("Seat of the second rider (double saddle)")] public string RearSeatPrefab = "assets/prefabs/vehicle/seats/horsesaddlerear.prefab";
            [JsonProperty("The rider's seat is fixed to the animal's spine, so the rider moves with its back like on the horse")] public bool SeatOnSpine = true;
            [JsonProperty("A rider can turn his head this far to each side (degrees; the horse's saddle seat itself allows 120; 0 = no limit)")] public float ViewClamp = 120f;
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
            [JsonProperty("Animal form: lift the hidden body by this much while Ctrl (the brake) is held, should the camera still sink in your game build (m, 0 = off)")] public float DuckLift = 0f;
            [JsonProperty("Animal form: refresh the network subscriptions when the form begins and ends (makes the game load the surroundings again; switch on only if other players stay unseen in animal form)")] public bool RefreshSubscriptions = false;
            [JsonProperty("First person of the animal form (spectate = the human is completely invisible, seat = hidden seat, legs may show)")] public string FirstPersonMode = "spectate";
            [JsonProperty("Seat of the hidden body when the first person mode is seat")] public string FirstPersonSeat = "assets/bundled/prefabs/static/chair.invisible.static.prefab";
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
            [JsonProperty("Speed change of the wooden and of the roadsign horse armour (m/s)")] public float ArmorWood = -2.25f;
            public float ArmorRoadsign = -4.5f;
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
            [JsonProperty("Damage of running into a player or an animal at a full sprint (bull, ram, cow, sheep, stag, boar, horse)")] public float ChargeDamage = 22f;
            [JsonProperty("A charge counts when the nose is this close to the other body (m)")] public float ChargeContact = 0.35f;
            [JsonProperty("An attack reaches this far beyond the nose (or the tail, for a kick) (m)")] public float HitReach = 1.0f;
            [JsonProperty("Stag: effect prefab of its call when it attacks (empty = search the game for a stag sound)")] public string StagAttackSound = string.Empty;
            [JsonProperty("Stag: effect prefab of its call when it is hurt (empty = search the game for a stag sound, else the thud of the hit)")] public string StagHurtSound = string.Empty;
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
            [JsonProperty("Length of the leash of an animal form (m)")] public float LeashLength = 5f;
        }

        // Running tires an animal the way it tires the horse: a full sprint lasts a quarter of a minute or so, walking and standing give the breath
        // back. Out of breath, the animal trots at most until enough has come back. Ridden animals and animal forms alike; the bar is shown.
        internal partial class StaminaCfg
        {
            [JsonProperty("Animals have stamina (riding and animal form)")] public bool Enabled = true;
            [JsonProperty("Stamina used per second at the full sprint (of 100)")] public float FullSprint = 6f;
            [JsonProperty("Stamina used per second at a sprint")] public float Sprint = 3f;
            [JsonProperty("Stamina used per second at a run")] public float Run = 0.8f;
            [JsonProperty("Stamina won back per second while walking or standing")] public float Recover = 7f;
            [JsonProperty("Seconds after the last effort before the stamina comes back")] public float RecoverDelay = 1.2f;
            [JsonProperty("Stamina a jump costs")] public float Jump = 8f;
            [JsonProperty("Stamina an attack costs")] public float Attack = 4f;
            [JsonProperty("Out of breath below this stamina (a trot at most, no jumps)")] public float ExhaustedBelow = 5f;
            [JsonProperty("Stamina needed to run again after being out of breath")] public float RecoverTo = 25f;
            [JsonProperty("Show the stamina bar (and the animal's health while riding) above the game's own bars")] public bool ShowBar = true;
            [JsonProperty("The bar: distance from the right edge, from the bottom, width and height (pixels at 1080p)")] public float BarRight = 29f;
            public float BarBottom = 150f;
            public float BarWidth = 192f;
            public float BarHeight = 24f;
        }

        internal partial class LifeCfg
        {
            [JsonProperty("Show on the screen of an animal form what E does (open, loot, eat, drink, lead)")] public bool Prompts = true;
            [JsonProperty("A played animal leaves dung now and then")] public bool Dung = true;
            [JsonProperty("Species that leave dung", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> DungSpecies = new List<string> { "horse", "cow", "bull", "calf", "sheep", "ram", "lamb" };
            [JsonProperty("Minutes between two droppings, at least")] public float DungMinMinutes = 4f;
            [JsonProperty("Minutes between two droppings, at most")] public float DungMaxMinutes = 9f;
            [JsonProperty("Item of the dung")] public string DungItem = "horsedung";
            [JsonProperty("A played chicken lays eggs now and then (very rarely, sometimes more at once)")] public bool Eggs = true;
            [JsonProperty("Chance (%) per minute that a played chicken lays")] public float EggChancePerMinute = 2f;
            [JsonProperty("Item of the egg")] public string EggItem = "egg";
            [JsonProperty("Wild animals do not jump over obstacles (only an animal a player controls jumps)")] public bool NoWildJumps = true;
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

        // Every animal has the inventory of the horse: saddle, horseshoes, saddle bag and horse armour, plus the storage the bag and the wooden
        // armour give.
        internal partial class GearCfg
        {
            [JsonProperty("Storage slots a saddle bag gives")] public int BagSlots = 12;
            [JsonProperty("Storage slots the wooden horse armour gives")] public int WoodArmorSlots = 6;
            [JsonProperty("Only owner and team may open the inventory of a tamed animal")] public bool BagPrivate = true;
            [JsonProperty("Damage the wooden horse armour takes off (0-1)")] public float ArmorWoodProtection = 0.25f;
            [JsonProperty("Damage the roadsign horse armour takes off (0-1)")] public float ArmorRoadsignProtection = 0.4f;
            [JsonProperty("A saddle in the inventory is seen on the animal's back (the horse shows its own)")] public bool ShowSaddle = true;
            [JsonProperty("The saddle on the back: turn of the model (x y z, degrees)")] public string SaddleRotation = "0 0 0";
            [JsonProperty("The saddle on the back: height above the spine (m)")] public float SaddleLift = 0.06f;
            [JsonProperty("The saddle on the back: size on a horse-sized animal (it is scaled with the animal's length)")] public float SaddleScale = 1f;
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
            [JsonProperty("Seconds a player stands near a calm wild horse until it is tamed (it is then his, no key needed)")] public float BondSeconds = 20f;
            [JsonProperty("An aggressive horse chases a player this many seconds at most, then gives up")] public float ChaseSeconds = 6f;
            [JsonProperty("Seconds an aggressive horse leaves a player alone after it has charged him")] public float CalmAfterCharge = 25f;
            [JsonProperty("Horse form: speeds of walk, trot, canter, gallop, full gallop (m/s), Shift climbs one gait each press")] public string FormGaits = "1.9 4.3 7.5 11 14";
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
            [JsonProperty("The rider's seat moved forward (+) or back (-) in metres (what /bm seat forward|back stores)")] public float? SeatForward;
            [JsonProperty("The saddle on the back: height added (m), size factor, turn added (x y z)")] public float? SaddleLift;
            public float? SaddleScale;
            public string SaddleTurn;
        }
    }
}
