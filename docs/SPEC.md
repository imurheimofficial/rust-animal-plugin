# Beastmaster - Requirements Specification

> Working title: **Beastmaster** (rename freely - the class/file name is trivial to change).
> Status: **PREPARED - waiting for the START command.** No plugin code has been written.
> Source of truth for *what* to build. *How/in which order* is in [PLAN.md](PLAN.md), *what is verified* is in [RESEARCH.md](RESEARCH.md).
> Original Czech brief (verbatim): [_archive/brief-original-cs.md](_archive/brief-original-cs.md).

**Legend** - ✅ standard technique, low risk - 🔧 needs a proof-of-concept spike first (see PLAN) - ⚠️ the literal behaviour cannot be done by a server-only plugin; the closest equivalent is specified here and listed in section 17.

---

## 0. Scope and global rules

| ID | Rule |
|---|---|
| G1 | **Everything is in scope.** Every feature of the brief is built. Where the engine prevents the literal behaviour, the closest equivalent is built and the difference is documented (section 17) - nothing is silently dropped. |
| G2 | **All player-facing text is English**: menu, help, chat messages, UI labels, config comments, permission descriptions, log lines. Strings go through the Oxide `lang` API (default `en`); no other language is shipped. |
| G3 | **The F5 menu contains an in-game Help/Guide that explains everything** (section 12.4). Help text that contains numbers (speeds, cooldowns, ranges) is generated from the live config, so it can never contradict the server settings. |
| G4 | Target: Rust build **25823813** (Livestock Update, Oct 2026) and newer. Oxide-API plugin (`RustPlugin`), developed and tested on Carbon and Oxide, so it runs on whichever framework the server uses. |
| G5 | No required dependencies. Optional soft links: `Friends`, `Clans` (party/clan checks). |
| G6 | **Deliverable = one C# source file `Beastmaster.cs`** (a `.cs` plugin; the brief says ".css", read as a typo - `.css` is a stylesheet and a Rust server cannot load it). It is built from several source modules by a build script (maintainable source, single-file deployment). The file name equals the class name exactly (case matters on Linux hosts). |
| G10 | **Deployment target: the user's Rust server at wespner.eu.** The user uploads the single file (`oxide/plugins/` on Oxide, `carbon/plugins/` on Carbon) - the plugin never needs a second file: config, data and language files are generated on first load. No credentials are ever needed or requested by the assistant; the user does the upload and reports the console output. A ready-to-upload copy is placed in `dist/`, with a short `INSTALL.md`. |
| G11 | Written to be **host-agnostic**: no Windows-only API, paths only through the Oxide/Carbon data APIs, nothing that needs OS access, no network calls, no native code. Which framework (Oxide or Carbon) and which OS the Wespner server runs is confirmed at START (A10). |
| G7 | Server-authoritative. **No client mods.** Only vanilla client assets (models, animations, sounds, UI) are used. |
| G8 | Defensive code: every hook null-safe, every entity validated (`IsDestroyed`), a per-tick time budget, no exception escapes a hook, an error is logged once with context. |
| G9 | The whole plugin can be switched on/off live by an authorised user; off means every feature stops cleanly (riders dismount, transformed players revert, AI overrides released). |

---

## 1. Species roster

`Standard` = ride with `beastmaster.ride`. `Restricted` = ride only with the strict admin permission `beastmaster.ride.restricted` (section 13).
"Taming flow" names refer to section 5. Prefab paths are filled from a server dump in the first work step (RESEARCH section 9).

| # | Species | Ride | Gear (saddle / bag / shoes) | Taming flow | Transform |
|---|---|---|---|---|---|
| 1 | Horse | Standard (vanilla seat) | yes | Bond (same as cow) | yes |
| 2 | Pig (domestic) | - | - | - | ❓ no `Pig` entity class exists in build 25823813; kept only if a prefab turns up in the START dump |
| 3 | Cow | Standard | yes | Bond | yes |
| 4 | Bull | Standard | yes | Bond | yes |
| 5 | Calf | - | - | - | yes |
| 6 | Sheep | Standard | yes | Bond | yes |
| 7 | Ram | Standard | yes | Bond | yes |
| 8 | Lamb | - | - | - | yes |
| 9 | Chicken | **Restricted** | - | admin-set | yes |
| 10 | Boar | Standard | yes | Predator (3-phase) | yes |
| 11 | Stag | Standard | yes | Sleep-and-feed | yes |
| 12 | Doe / Hind | Standard | yes | Sleep-and-feed | ❓ only `Stag` exists as a current animal class; doe is kept only if the START dump finds a prefab (otherwise the stag covers "deer") |
| 13 | Brown bear | Standard | yes | Predator | yes |
| 14 | Polar bear | Standard | yes | Predator | yes |
| 15 | Wolf | Standard | yes | Torch | yes |
| 16 | Arctic wolf | Standard | yes | Torch | yes |
| 17 | Cougar / Panther | Standard | yes | Predator | yes |
| 18 | Tiger | Standard | yes | Predator | yes |
| 19 | Crocodile | **Restricted** | - | admin-set | yes |
| 20 | Snake | **Restricted** | - | admin-set | ⚠️ in this build the snake is a `SnakeHazard` (a slithering hazard entity, no navmesh animal) - it is puppeted by the plugin; visuals are checked in a spike |
| 21 | Crab | - | - | - | yes (the game's crab is a critter entity; whether one entity shows one crab or a small group is checked in spikes S2/S4) |
| 22 | Rabbit | **Restricted** | - | admin-set | yes |
| 23 | Squirrel | **Restricted** | - | admin-set | yes |
| 24 | Frog | **Restricted** | - | admin-set | yes |
| 25 | Zombie bear (event) | Standard (bear variant) | yes | Predator | ❓ no such class; only a prefab variant of `Bear` could exist - decided by the START dump |
| 26 | Zombie wolf (event) | Standard (wolf variant) | yes | Torch | ❓ same as above (variant of `Wolf2`) |

"All variants" of bear and wolf (brief) = brown, polar, zombie / wolf, arctic, zombie.
Not rideable and not transformable (not in the brief's lists): shark, jellyfish, sea turtle, fish.

### 1.1 Movement table (default config values - all editable)

Three stages per the brief: *base walk* - *vanilla-style sprint (no gear)* - *top speed with High Quality Horse Shoes*.

| Species | Walk m/s | Sprint m/s | Top m/s | Special movement trait |
|---|---|---|---|---|
| Tiger | 6.0 | 16.5 | 21.5 | Fastest mount. Extreme acceleration, slight drift in turns. |
| Horse (Thoroughbred) | 5.5 | 14.5 | 19.0 | Best handling, smooth obstacle jumps. |
| Cougar / Panther | 5.8 | 15.0 | 18.5 | Climbs/jumps onto rocks 25 % higher than other animals. |
| Stag / Doe | 5.0 | 14.0 | 17.5 | Silent run: hoof sounds reduced by 90 %. |
| Boar | 4.0 | 11.0 | 14.5 | Low centre of mass: harder to hit with projectiles from the side. |
| Wolf (all versions) | 4.8 | 12.0 | 14.0 | Full speed in dense forest, no tree penalty. |
| Bull / Ram | 4.2 | 11.5 | 13.5 | Sprinting breaks wooden barricades (damages them). |
| Brown bear | 3.5 | 10.5 | 13.0 | Ignores slowdown in deep water and swamp. |
| Polar bear | 3.2 | 9.5 | 12.5 | +15 % speed on snow and ice. |
| Cow / Sheep | 3.0 | 8.0 | 10.5 | Slow, heavy loot hauler. |
| *Restricted:* Crocodile | 3.0 (land) | 7.0 | 9.0 | Water rocket: sprint in water 18.0 m/s. |
| *Restricted:* Frog | 2.5 | 5.0 | 7.5 | Super jump: SPACE = huge vertical jump (jump force x3). |
| *Restricted:* Snake | 2.0 | 6.0 | 8.0 | Extremely low profile: enemy auto-turrets cannot target the rider. |
| *Restricted:* Chicken / Rabbit / Squirrel | 1.5 | 4.5 | 6.0 | Tiny hitbox; the rider visually "levitates" above the animal. |

Not in the brief's tables (defaults to be proposed in PLAN): Pig, Zombie bear, Zombie wolf, Arctic wolf, Calf, Lamb (transform-only species use their own table row once the transform speeds are chosen - default: same as their adult/base species).

---

## 2. Riding

| ID | Requirement | |
|---|---|---|
| R2.1 | Mount with **E** on an animal that is tamed, owned/shared with the player, has a saddle in its gear, and is ridable for the player's permission. | 🔧 |
| R2.2 | Ownership: owner always; team members / friends / clanmates by config (`Ownership.*`); optional "anyone". | ✅ |
| R2.3 | Steering like the vanilla horse: **W/S** throttle (S = slow reverse), **A/D** turn, mouse look is free. Exact feel copied from the vanilla horse code. | 🔧 |
| R2.4 | **Gait ladder:** **Shift** steps up through several gaits to full speed; **Ctrl** brakes (step down / stop) - as on the vanilla horse. Number of gaits per species is configurable (default 5). | 🔧 |
| R2.5 | Speeds from table 1.1. Horseshoes raise the speed ceiling: no shoes = sprint speed, advanced shoes = top speed, basic shoes = in between (exact bonus copied from the vanilla horse code - RESEARCH open item). **Visual limit:** the current animals play animations authored for fixed gait speeds (the game's own comment: anything in between "reads as sliding"). The ladder therefore starts on the species' authored gaits; speeds above the authored top gait are applied up to a per-species visual cap measured in spike S3 (default = the brief's value, lowered only if the sliding looks wrong in your client). | 🔧 |
| R2.6 | Species traits from table 1.1 are implemented as listed (terrain modifiers, jump height, barricade damage, projectile side-hit reduction, turret blindness, hoof-sound reduction). | 🔧 |
| R2.7 | **Double saddle:** a second rider can mount the rear seat. Vanilla seat swap key stays vanilla. | 🔧 |
| R2.8 | Dismount with the normal key; if the animal dies, all riders are dismounted. | ✅ |
| R2.9 | Restricted species (chicken, crocodile, snake, rabbit, squirrel, frog): ride only with `beastmaster.ride.restricted`. No taming or saddle needed (assumption A4); the animal is made passive while ridden. | 🔧 |
| R2.10 | Each species keeps its **own vanilla animations and sounds** while ridden (walk/run cycle, breathing, footsteps, calls). Nothing is faked with another species' assets. | 🔧 |
| R2.11 | While ridden, the animal's own AI is suspended; it resumes the moment the rider leaves. | 🔧 |
| R2.12 | Optional restrictions in config: safe zones, combat block, building-blocked areas, max riders per server. | ✅ |

### 2.1 Hills

| ID | Requirement | |
|---|---|---|
| R2.14 | **Water slows animals down like the horse** (requested 2026-10-09). The plugin uses the horse's own measure - the share of the body height under the water surface (`WaterLevel.Factor` on the body bounds) - so shallow water slows a little and deeper water more; from half the body height under water the animal only walks, as the horse does when it swims. Applies when ridden and in animal form. Exceptions from the brief's table: the brown bear has no water/swamp slow-down, the crocodile is faster in water. Exact curve is tuned in spike session 2. | 🔧 |
| R2.13 | **The controlled animal climbs and descends hills exactly like the game's own animals.** It moves on the same navigation mesh as the AI animals - walkable slope up to **45°**, steps up to **0.4 m** (the mesh builder's defaults; the real values of the map are measured in spike S2) - at the same gaits, and the client tilts the body along the slope by itself (up to 30°). No sliding, no floating. Ground the AI animals cannot walk (cliffs, steep rock faces) is closed to the controlled animal too, except by a jump (section 2A). Config can add an uphill slow-down (default off). The horse keeps its own physics: slope speed curve, and it slides on very steep ground. | 🔧 |

## 2A. Jumping (Space bar)

The game's animals do not jump in normal movement (RESEARCH section 2); the horse has no jump either. The brief needs jumps (panther higher onto rocks, frog x3, horse over obstacles), so the plugin adds one, using the game's own technique.

| ID | Requirement | |
|---|---|---|
| R2A.1 | **Space jumps** for a rider or an animal form. The jump follows the game's technique: the agent is paused, the networked `IsJumping` flag is set, the body moves along a vertical-plus-forward path that is not tied to the navigation mesh, and it **lands on a validated point** (a navigation-mesh sample at the landing; an invalid landing is refused or shortened). | 🔧 |
| R2A.2 | **Look of the jump, per species,** chosen in spike S18 from three candidates: (1) the species' own scripted leap clip with the game's warping (wolf, tiger and panther have one; it is long, so a trimmed or shortened use is tested), (2) a short arc with the airborne flag only, (3) a bound: the fastest gait animation plays along the arc so the legs look extended. Whichever looks right in your client is used and written into the species card. | 🔧 |
| R2A.3 | **Jump profile per species** (config; proposals): height, distance, cooldown, stamina cost. The game's own predator pounce reaches 2.7 m up and 7 m across; defaults stay at or below that. **Panther +25 % higher** than the tiger/other species; **frog x3** (R9.36, restricted species); stag high, boar and bears low. | 🔧 |
| R2A.4 | **Horse:** the vanilla horse cannot jump, so the plugin adds a physical jump (an impulse on the horse's body with obstacle assist) that stays below the horse's own "airborne too long" ragdoll limit. | 🔧 |
| R2A.5 | Optional **auto-hop** over a low obstacle the rider drives straight into (config, default off). | 🔧 |
| R2A.6 | Landing: no fall damage for a normal jump; heavy drops follow the animal's normal fall rules (config). A jump never leaves the animal below the ground, inside a wall, or off the playable area. | ✅ |

## 3. Gear (saddle, saddle bag, horseshoes)

| ID | Requirement | |
|---|---|---|
| R3.1 | Gear applies to: horse, cow, bull, sheep, ram, stag, doe **and** wolf, bears (all), tiger, panther, boar. | |
| R3.2 | Items are the **vanilla items**: single saddle, double saddle, saddle bag, basic horse shoes, high quality horse shoes. | ✅ |
| R3.3 | **E** on an owned, tamed animal (not mounted) opens its gear panel (saddle slot, bag slot, shoes slot). The bag is real storage. | 🔧 |
| R3.4 | Shoes change speed (R2.5); double saddle enables the second seat (R2.7); bag content is private to owner/team by config. | 🔧 |
| R3.5 | Gear and bag contents persist across restarts and are dropped with the corpse when the animal dies. | 🔧 |
| R3.6 | The saddle should be **visible** on the animal. If the vanilla assets cannot show it on a non-horse, the gear panel and a chat/HUD hint show the state instead (decided by spike). | 🔧 |
| R3.7 | Horse armour is **not** in the brief; not built (listed as a possible later extension). | - |

## 4. Combat and animations

| ID | Requirement | |
|---|---|---|
| R4.1 | Attack key: **F** by default, changeable per player in the menu (Controls page). Works while riding any rideable animal **and** while transformed. | 🔧 |
| R4.2 | If the species has several attack animations they are **cycled in a fixed order** (not random). Confirmed in the game code: cow/bull/sheep/ram have head-butt, kick and rear kick; crocodile has four (static bite, belly flop, intimidation snap, swim attack); bear, boar, stag, chicken have one bite; wolf one bite plus a leap; tiger and panther one deadly strike (matrix below). | 🔧 |
| R4.3 | **Rear attack, exactly as specified:** if, before pressing the attack key, the rider looks backwards from the seat (view yaw more than a configurable angle, default 120°, away from the animal's heading), the rear attack plays and hits behind the animal. The game code has a real rear attack for cow, bull, sheep and ram (`kickBehind`). For species without one the press plays the normal attack (the brief: "if it has"). | 🔧 |
| R4.4 | **Horse attack.** The brief wants the ram's head-butt on the horse. That exact clip cannot be shown: the horse is a different model with its own animator, and the server can only trigger animations the horse already has. ⚠️ **What the horse does have:** a vanilla **rear-up** (`CLIENT_Stand`, the horse rises on its hind legs; the game itself triggers it with a double-tap of crouch). The horse attack = rear-up animation + a forward strike (damage and knock-back timed to the rise) + the horse's own sound. If you want something different (for example no animation, damage only), say so at START. | ⚠️ |
| R4.5 | Damage, range, cooldown per species in config. Targets: players, animals/NPCs, deployables only where config allows. Team/party/clan members and safe zones are never hit. Other plugins can veto with the hook `CanBeastmasterAttack`. | ✅ |
| R4.6 | Bull/Ram sprint barricade-breaking (R2.6) uses the same damage pipeline. | 🔧 |
| R4.7 | Mechanics: the plugin plays the species' own attack clip through the game's animation-montage channel and applies the damage itself at the clip's hit moment (the same way the game's own attack states do), so the attack looks and sounds like the real animal's. | 🔧 |

**Attack matrix read from the game code (build 25823813)** - this is what "cycled" and "rear" mean per species:

| Species | Attack clips (cycled in this order) | Rear attack | Source |
|---|---|---|---|
| Cow / Bull / Sheep / Ram | head-butt, kick, (rear kick when looking back) | **yes** (`kickBehind`) | `LivestockAnimalFSM` |
| Brown bear, polar bear, boar, stag, chicken | one bite/strike | no | `BasicAnimalFsm` |
| Wolf / arctic wolf | one bite, plus a leap-away move | no | `Wolf2FSM` |
| Tiger / panther | one deadly strike | no | `TigerFSM` |
| Crocodile | static bite, belly flop, intimidation snap, swim attack (in water) | no | `CrocodileFSM` |
| Horse | rear-up (see R4.4) | no | `RidableHorse` |
| Rabbit, squirrel, frog, chicken-sized critters | no attack clip in the game | no | `CritterAnimalFSM` - the attack key plays the species' call instead (assumption A12) |

## 4A. Reactions and sounds (stimulus-driven)

| ID | Requirement | |
|---|---|---|
| R4A.1 | Animal sounds and reaction animations are **stimulus-driven**, like a real animal: each one fires because something happens to or around the animal, and it uses the species' **own vanilla clip** (in the current game the sound is part of the clip, so animation and sound always match). | 🔧 |
| R4A.2 | **Pain:** when the animal (ridden, transformed or tamed) is hurt it plays a hurt reaction scaled by the damage (light flinch up to a heavy stumble/knock-down where the species has one) with its pain sound. Death keeps the vanilla death. | 🔧 |
| R4A.3 | **Fear of descents:** when the rider steers down a slope steeper than the species' comfort angle (config, per species) at trot or faster, the animal hesitates: it slows to a walk, refuses to speed up for a short time and plays its unsure/fear reaction with its sound. Holding forward through the hesitation overrides it (config). | 🔧 |
| R4A.4 | Other stimuli (all configurable, each with a cooldown so the animal never spams): deep water for non-swimmers, a ledge or drop ahead, fire nearby, gunfire or explosions nearby, a bigger predator nearby, exhaustion (stamina low), a sudden stop or collision, contentment when fed or petted, acknowledging mount and dismount. | 🔧 |
| R4A.5 | **Through gestures:** every one of these sounds/animations can also be performed on demand through the gesture wheel (section 6A). | 🔧 |
| R4A.6 | A species without a given clip uses the nearest clip it owns. Which stimulus plays which clip is generated from the species' real clip list in spike S4 and shown in the Help (species cards). | 🔧 |

## 5. Taming flows

General: `beastmaster.tame` required. A tamed animal has an owner, a bond value, a command state and gear (section 14 data model). Tamed animals persist (R14.3).

### 5.1 Bond (horse, cow, bull, sheep, ram) - "tame the horse like a cow"
| ID | Requirement | |
|---|---|---|
| R5.1 | Wild horses spawn **without saddle** and must be tamed before a saddle can be fitted. | 🔧 |
| R5.2 | Taming uses the **same mechanic as the vanilla livestock** (familiarity/bond that builds over time and decays - exact rules copied from the decompiled game code). For the horse the plugin replicates it on top of the horse. | 🔧 |
| R5.3 | After taming a saddle can be fitted (R3.3); then the animal is ridable. | 🔧 |

### 5.2 Torch (wolf, arctic wolf, zombie wolf)
| ID | Requirement | |
|---|---|---|
| R5.4 | A wild wolf flees from a player holding a **lit torch** (vanilla flee-from-fire behaviour; spike checks that wolves have it). | 🔧 |
| R5.5 | If the player keeps chasing it with the lit torch for **60 s** (config; the timer pauses briefly if contact is lost and resets if the torch goes out), the wolf stops fleeing, **howls**, and starts to follow the player slowly everywhere. | 🔧 |
| R5.6 | Aiming at the wolf's head and using the command input gives **Wait**. Further commands: section 6. | 🔧 |

### 5.3 Predator (tiger, panther, brown/polar/zombie bear, boar) - three phases
| ID | Requirement | |
|---|---|---|
| R5.7 | **Phase 1 - Dart:** a **Blowpipe** with an **Incapacitate Dart** hitting the animal applies *no ordinary damage* but a slow status: speed -60 % for 30 s (config); the animal staggers and growls. | 🔧 |
| R5.8 | **Phase 2 - Trap:** while the animal is darted/slowed and enters a **Bear Trap**, the plugin springs the trap (vanilla traps ignore animals, so the plugin detects the overlap itself), deals a fixed **50 HP** (config) and **locks the animal in place** with a roar. | 🔧 |
| R5.9 | **Phase 3 - Syringes:** each **Medical Syringe** used on the trapped animal from close range counts as a sedative. After **3** (config) the animal calms: the trap opens, the animal steps out, sits **5 s** neutral, then follows the player in a friendly way. | 🔧 |
| R5.10 | The tamed animal can now receive a saddle (R3.3) and be ridden. The animal does not attack its owner or team. | 🔧 |

### 5.4 Sleep-and-feed (stag, doe)
| ID | Requirement | |
|---|---|---|
| R5.11 | Blowpipe + Incapacitate Dart on a stag/doe: it drops instantly and lies down for **60 s** (config). | 🔧 |
| R5.12 | **E** on the lying animal opens its inventory. The player puts in **3 food items** (potatoes for the stag, corn for the doe - config); it eats them automatically within **5 s**. | 🔧 |
| R5.13 | A **saddle** put into the last free slot completes the taming: the animal jumps up fully tamed and can be mounted at once. | 🔧 |

### 5.5 Other species
Pig, calf, lamb, chicken, crocodile, snake, rabbit, squirrel, frog, crab, zombie variants: no taming flow is defined in the brief (assumption A4/A5): they are available through **transformation** (section 9), through the **Spawn** tab (section 12.6) and, for the restricted six, through permission-based riding (R2.9).

## 6. Commanding tamed animals

| ID | Requirement | |
|---|---|---|
| R6.1 | Commands: **Follow**, **Wait/Stay**, **Guard** (stay within 50 m of the owner), **Attack** (hostile players/animals nearby), **Release**. | 🔧 |
| R6.2 | Input methods: (a) **gestures** - confirmed readable: the game calls the hook `CanUseGesture(player, gesture)` for every gesture attempt and the current gesture can be polled (section 6A), (b) holding a **Bone** item (primary/secondary use cycles/selects), (c) the **menu** (My Animals page), (d) chat/console commands. (b), (c), (d) always work. | 🔧 |
| R6.3 | **Party members and teammates are never attacked** by the owner's animals; the owner's own team too. | ✅ |
| R6.4 | Aiming at an animal's head and giving the command targets that one animal; a command without aim applies to all tamed animals within range (config). | 🔧 |
| R6.5 | Tamed animals follow the owner through water, up slopes, and teleport to the owner if left too far behind (config distance), never into bases/structures. | 🔧 |

## 6A. Animal actions through gestures

| ID | Requirement | |
|---|---|---|
| R6A.1 | While riding or transformed, the **gesture wheel** (default key B on this client, `+gestures`) performs the animal's **other animations**: idle variants, calls (growl, howl, bark, roar, snort), stomp, raise head, graze/eat, drink, look around, sit/lie down/sleep/get up - whatever clips the species really has. | 🔧 |
| R6A.2 | A config table maps gestures to animal actions per species (defaults provided, shown in Help and in Menu - Controls). The plugin uses gestures every player owns by default; the human gesture is suppressed (the hook returns false) and the animal clip plays instead. | 🔧 |
| R6A.3 | The actions available for a species are **discovered from the species' own clip list** at load (no hand-typed clip names), so a game update that renames or adds clips is picked up and reported by `bm.selftest`. | 🔧 |
| R6A.4 | The same gestures on foot (not riding, not transformed) command nearby tamed animals (follow, wait, guard, attack target) when enabled. | 🔧 |
| R6A.5 | Fallbacks that always work: Menu - Animal actions page and `bm.emote <action>`. | ✅ |

## 6B. Lead (leash) behaviour - applies to every lead

| ID | Requirement | |
|---|---|---|
| R6B.1 | **The leash cannot be torn by accident.** It breaks only at **50 m** (`Lead.BreakDistance`, default 50). Nothing below that distance breaks it: sprinting, turning, jumping, swimming, obstacles and the animal being snagged do not. | 🔧 |
| R6B.2 | Intentional or logical ends still work: the player lets go, the animal or leader dies, the leader logs out (R9.31) or teleports, or the distance reaches 50 m. Whether the leash also breaks when the animal is hurt is a setting (`Lead.BreakWhenHurt`, default **off**). | ✅ |
| R6B.3 | **Animals hold on the leash stably:** they keep the line behind the leader without jitter, match the leader's gait, path around obstacles and up slopes, and catch up when the leader moves away. The game's slow-down of a leader who pulls the rope taut stays but is capped (setting). | 🔧 |
| R6B.4 | How it is done per case: **livestock** with the game's rope - the game's own break distance setting (`livestock.leadbreakdistance`, 7 m by default) is raised to the configured value while the plugin is loaded and restored on unload, and a watchdog re-attaches a lead that stops for no logical reason. **Horse** with the game's rope - the 7 m limit is hard-coded in the horse code, so the plugin adds assisted following that keeps the horse with the leader and, beyond 7 m, continues with its own invisible tether (the rope picture returns when the horse is back in range) until 50 m. **Other species and animal-form leaders** (R9.27): plugin-driven following with the same rules. | 🔧 |

## 7. Relations between animals (applies to transformed players)

| ID | Requirement | |
|---|---|---|
| R7.1 | Player is a **doe/stag** near a bear: the bear attacks. Player is a **bear** near another bear (same kind): they tolerate each other. | 🔧 |
| R7.2 | Player is a **bull**: cows follow. Player is a **ram**: sheep follow. | 🔧 |
| R7.3 | Player is a **wolf**: other wolves keep within **50 m** around the player. | 🔧 |
| R7.4 | Player is a **bull**: other bulls do not tolerate it (fight). Same for **ram** vs ram. | 🔧 |
| R7.5 | The relations are a **configurable matrix** (species x species: tolerate / follow / ignore / hostile / flee). Defaults above; everything else = vanilla behaviour. | ✅ |
| R7.6 | Ridden/tamed animals use the same matrix toward wild animals, except where the owner commands otherwise. | 🔧 |

## 8. Horse AI

| ID | Requirement | |
|---|---|---|
| R8.1 | Wild horses move **freely**, **graze** like cows and **need water** like cows (needs-driven, same style as the livestock system). | 🔧 |
| R8.2 | **Temperament.** Each free-roaming wild horse is **calm**, **shy** or **aggressive**. It is assigned when the horse spawns (config probabilities; proposed defaults in assumption A15) and saved with the horse. | 🔧 |
| R8.3 | **Shy:** runs away from players that come close, shoot near it or hurt it. It **calms down 15 seconds** (config) after the last scare and from then on behaves as calm; every new scare restarts the 15 seconds. | 🔧 |
| R8.4 | Wild horses must be **tamed** (R5.1-R5.3) and are spawned unsaddled. A **calm** horse can be tamed right away. | 🔧 |
| R8.5 | **Aggressive:** warns (snort, rear-up) and attacks players that come close or hurt it, with the horse attack of R4.4 (rear-up strike; ⚠️ no ram head-butt on a horse model). It **cannot be tamed while it is aggressive**. | ⚠️ |
| R8.6 | **Putting an aggressive horse to sleep:** a Blowpipe with an Incapacitate Dart makes it stop and sleep (default 60 s, config). When it **wakes up it is calm** for good. A dart on a calm or shy horse also puts it to sleep and it wakes calm. | 🔧 |
| R8.7 | The sleeping horse: the best available vanilla visual is chosen in spike S13 (a fall/ragdoll, or standing motionless with a lowered head). Either way it is stationary and cannot be ridden while asleep. | 🔧 |
| R8.8 | A player can recognise the temperament: the horse's behaviour shows it, and the Help button "what I'm looking at" (plus an optional on-screen label, config) names it. | ✅ |

## 9. Transformation (ritual)

Permission: `beastmaster.transform` (restricted species: assumption A6).

| ID | Requirement | |
|---|---|---|
| R9.1 | **Phase 1 - Kill:** the player kills a wild animal of the species they want to become. The corpse must stay on the ground and must **not be harvested** (axe/knife gathering cancels the ritual for that corpse). | 🔧 |
| R9.2 | **Phase 2 - Circle:** holding a **lit torch**, the player circles the corpse **3 full times** (within a tight radius). On the third circle the corpse **ignites with magical fire** (visual effect). | 🔧 |
| R9.3 | **Phase 3 - Blood:** the player presses **E** at the burning corpse. Cost: **-50 % of current health** and a strong **bleeding** status. | 🔧 |
| R9.4 | **Transformation:** the human is hidden (no networking to others, untargetable, invulnerable, parked safely) and the player is reborn **as the live animal at the corpse position** with the animal's **full health**. | 🔧 |
| R9.5 | While transformed the player's health **is the animal's health**. When the animal dies the player dies. Damage dealt to the animal never reaches the hidden human. | 🔧 |
| R9.6 | The player stays transformed until the animal is killed or the player presses **X** (return to human form at the animal's position). | 🔧 |
| R9.7 | **Controls as the animal:** walk slowly, Shift gait ladder (several steps to full speed), Ctrl brake, **F** attack (looking back = rear attack), species sounds replace gestures and pain sounds. | 🔧 |
| R9.8 | **Leash:** another player can tie and lead a transformed player on a leash. ⚠️ The vanilla lead rope is client-bound to livestock/horse; equivalent: a plugin leash (behaviour + feedback) without the rope visual unless the spike finds a way. | ⚠️ |
| R9.9 | Applies to all species marked "yes" in table 1. Horse form: the player is invisible, the horse moves with the player's input. | 🔧 |
| R9.10 | Safety rules (config): cooldown after reverting, no ritual in safe zones/combat, ritual cancelled if another player harvests the corpse or the corpse despawns. What happens when the player quits is defined in section 9E. | ✅ |
| R9.11 | Night vision goggles equipped in the hidden human's inventory take effect for the animal view (vanilla vision effect). Verified by spike S6. | 🔧 |
| R9.12 | **Same animal, same look.** The player becomes exactly the animal that was sacrificed, with the same appearance: the same prefab (polar bear stays polar bear, arctic wolf stays arctic wolf) and, for animals with visual variants, the exact variant data of the dead animal - livestock genes (coat, pattern, wool tint, size), shorn-fleece state, sex and age stage (a bull stays a bull, a cow a cow), horse breed. The game stores this on the corpse (livestock corpse: `Genes`, `ShornFleece`; horse corpse: `breedIndex`), and the plugin additionally keeps a short death record (prefab, killer, look) to cover species whose corpse carries nothing. | 🔧 |

## 9B. Handled by other players while transformed

| ID | Requirement | |
|---|---|---|
| R9.13 | **Milking:** another player can milk a transformed cow that has milk ready (female, adult) through the normal game interaction; amount, cooldown and the "milk ready" state follow the animal's copied genes and the normal timers. The game refuses milking without "trust"; the plugin credits the milker that trust for the puppet. | 🔧 |
| R9.14 | **Shearing:** another player can shear a transformed sheep or ram that has wool with the normal shearing tool; the shorn state is copied from the corpse and the wool regrows with the normal timers. (Confirmed in the game code: shearing works without trust.) | 🔧 |
| R9.15 | **Leash:** another player can lead a transformed player. Cow, bull, sheep and ram forms are real livestock entities, so the vanilla leading rope (with its rope visual) works, and the plugin turns the rope's pull into movement of the controlled puppet: the transformed player cannot walk away from the leader beyond the rope length and is drawn along, but can still steer within it. Other forms (stag, doe, bears, wolves, ...) use a plugin leash with the same behaviour; the rope visual exists only where the game supports it (section 17, #4). | 🔧 ⚠️ |
| R9.16 | **Saddle:** another player can open the transformed animal's gear panel (E on it) and fit a saddle, a bag and shoes. Config: who may do it (anyone / team only / nobody) and a per-player switch in the menu so the transformed player can refuse. | 🔧 |
| R9.17 | **Ride, with the transformed player in control:** another player mounts the saddle as a **passenger**. **Only the transformed player steers** (movement, gaits, attack, gestures, camera); the passenger's input never drives the animal. A double saddle carries two passengers. | 🔧 |
| R9.18 | The horse form follows the vanilla horse seat rule: a passenger needs the **double saddle** (seat 1), because the hidden controller occupies the driver seat (R9.19). | 🔧 |
| R9.19 | **Seat model (all forms):** the transformed player's hidden body sits in an invisible *driver seat* (camera and input source); the visible saddle seats are separate *passenger seats*. The same model serves normal riding, where the rider in saddle seat 0 is the controller. | 🔧 |
| R9.20 | Safety: a passenger can dismount at any time; if the controller reverts, dies or disconnects, passengers are dismounted safely; the controller can eject a passenger (menu button). | ✅ |
| R9.21 | Applies to cow, bull, sheep, ram, stag, doe (named in the brief) and to every other species marked rideable in table 1. Species that yield nothing (stag, doe, ...) simply have no milk or wool. | 🔧 |

## 9C. Eating, drinking and stats while transformed

| ID | Requirement | |
|---|---|---|
| R9.22 | As an animal the player can **eat what that animal eats and drink**, to restore health and the other stats. Diets (config tables, defaults): herbivores (horse, cow, bull, sheep, ram, stag, doe) graze grass, eat crops and produce, and take food from troughs and drinking water; omnivores (boar, bears) also eat fruit, meat and fish; carnivores (wolf, tiger, panther, crocodile) eat raw meat, fish and fresh animal corpses; small animals eat seeds and small food items. Water: rivers, lakes, the sea (less effective, config), water barrels and troughs. | 🔧 |
| R9.23 | **Controls:** a contextual action on what is in front of the animal (graze on grass, drink at water, eat an item or a corpse): the **Use key**, plus the same actions in the gesture list (section 6A: "eat/graze", "drink"). The matching vanilla clip plays (graze, drink, eat) with its sound. | 🔧 |
| R9.24 | **Stats of an animal form**, shown on a small HUD (configurable): **Health, Stamina, Hunger, Thirst**. Eating restores hunger and, by the food's value, health; drinking restores thirst (and a little health); resting restores stamina; high gaits and attacks spend stamina. Defaults are gentle (no death from hunger unless the server owner enables it). The human body's own stats are frozen while the animal form is active. | 🔧 |
| R9.25 | For livestock forms the game's own needs and condition system (fullness, hydration, personal space, frailty) is suspended on the puppet and replaced by the plugin's stats, using the livestock's public need functions (`SetNeedValue`, `ConsumeFromTrough`, `ConsumeFromBarrel`) to keep the entity consistent. Food values come from the vanilla item definitions (calories, hydration, health), so changed or modded items work. | 🔧 |
| R9.26 | Anti-abuse: eating consumes the item, crop or corpse as it would for a real animal; per-bite cooldown and a heal-per-second cap in config. | ✅ |

## 9D. Leading tamed animals while transformed (invisible lead)

| ID | Requirement | |
|---|---|---|
| R9.27 | An animal-form player can **lead tamed animals in a line**. Looking at an eligible tamed animal (owned by the player or the team) within about 3 m offers "Lead" (Use key, also in the gesture list). Led animals follow in a column behind the player, keep a spacing, match the leader's gait, up to a configured chain length (default 4), and break away if left too far behind. Example: a cow player leads a tamed sheep. | 🔧 |
| R9.28 | **The lead is invisible.** No rope is drawn, because an animal holding a rope looks wrong. Implemented as plugin-driven following; the game's own lead flag (the thing that draws the rope) is not used for this. | 🔧 |
| R9.29 | Release with the same prompt; automatic release on revert, death, logout, when the distance exceeds the break distance, or when the led animal is attacked (it then reacts as a normal animal). | ✅ |
| R9.30 | Ordinary humans keep the vanilla rope for livestock and horses (R9.15 covers being led, this covers leading). | ✅ |

## 9E. Quitting the game as an animal (the animal sleeps)

| ID | Requirement | |
|---|---|---|
| R9.31 | **If the player quits (or loses connection) in animal form, the animal goes to sleep where it stands**: it plays the species' own sleep or lie-down animation and stays there as a sleeping body, like a sleeping player. The hidden human body is kept safely with it. Passengers are dismounted and led animals are released at that moment. | 🔧 |
| R9.32 | While asleep the animal does nothing on its own (no roaming, no AI, no attacks), takes damage like any sleeper, and **if it dies the player dies** - it is processed at once, so the player meets the death on the next login. It keeps its look, health, stats, gear and bag. | 🔧 |
| R9.33 | **On reconnect the player is still in animal form**: the animal wakes with the wake-up animation and control returns. If the server restarted meanwhile, the sleeping animal is re-created from the saved record (R14.3) in the same place. | 🔧 |
| R9.34 | Limits (config): how long an offline animal body may stay (default follows the server's normal sleeper rules); after that the body is removed and the player is returned to human form at that place on the next login. A sleeping animal body counts as a sleeper for base-raid and anti-grief rules (configurable). | ✅ |

## 9F. Small creatures, snake, crocodile, crab: bodies, controls, water

| ID | Requirement | |
|---|---|---|
| R9.35 | **The bodies stay.** After a rabbit, squirrel, frog, crab, snake or crocodile (and every other ritual species) is killed, its body stays long enough for the ritual (config, default 10 minutes, instead of the game's short despawn). If the game leaves no body for a species (some critters vanish on death), the plugin leaves a stand-in body of that species at the spot, with the same look (R9.12). Harvesting the body by hand, knife or axe is blocked while it is ritual-eligible. | 🔧 |
| R9.36 | **Correct controls for every creature.** Movement uses the creature's own way of moving, with the matching animation and sound: hops for rabbit and frog (frog: Space = super jump), scuttle for crab, scurry for squirrel, slither for snake, walk and swim for crocodile. Spikes S2/S4 check each species; some small animals move through the game's hop/root-motion clips rather than a walk cycle, which changes how they are steered (turn, then hop in that direction). | 🔧 |
| R9.37 | **Water rules per species (config table).** Frog, crab, crocodile (and sea turtle) are **immune to drowning** and move freely through water; the snake is configurable (default immune). Strong swimmers (bears, wolves, boar, stag, tiger, panther) swim with a speed penalty. Non-swimmers (cow, bull, sheep, ram, chicken, rabbit, squirrel) are slowed and, when submerged deeper than their depth for longer than a limit, take drowning damage like a player (limit and damage in config). The horse follows the vanilla horse water rules. | 🔧 |

## 9G. Doors, vehicles and transport in animal form

| ID | Requirement | |
|---|---|---|
| R9.38 | **Doors:** an animal-form player can open and close doors, hatches and gates with the Use key, with exactly the rights the human body has: a lock the player is authorized on opens, other locks stay shut, and no code entry is needed or possible. The animal must physically fit through the opening (checked per species; large animals use wide doorways and gates). | 🔧 |
| R9.39 | **No driving:** an animal-form player cannot take the driver seat of any vehicle (cars, bikes, boats, helicopters, trains, ...). The mount is refused with a clear message. | ✅ |
| R9.40 | **No riding animals:** an animal-form player cannot mount a ridable animal - the horse or any saddle seat of this plugin - neither as driver nor as passenger. | ✅ |
| R9.41 | **Transport:** an animal-form player can board a **transport vehicle** so that another player carries them. Livestock forms use the game's own loading spots (the flatbed module that carries livestock, with its size classes); other forms are placed on the same spots by the plugin; small species (chicken, rabbit, squirrel, frog, crab, snake) may also use an ordinary passenger seat. A boarded animal cannot move; it can look around and use gestures; it leaves with the Use key, or safely when the vehicle is destroyed. The driver is always another player. | 🔧 |
| R9.42 | An animal form **can hold and use items, loot, build and craft** (R9.44, R9.47-R9.49). Everything else not listed in this spec is blocked by default (driving vehicles, riding animals, trading, ...); a config table switches single permissions on or off. | ✅ |
| R9.43 | **Inventory and backpack stay available.** The animal-form player keeps the human inventory, belt contents, worn clothing and backpack: they can open and rearrange them, move and drop items, and worn items such as night-vision goggles keep working (R9.11). | 🔧 |
| R9.44 | **Hands: any item can be held and used, and none is ever visible.** As an animal the player can take items in hand and use them like a human - tools, weapons, food, medical items, the Building Plan, the Hammer and deployables. **A held item is never visible on the animal or to other players**: the hidden body keeps its held item hidden with it (spike S6 verifies this on a second client). The player's own first-person view of the hands is shown only if the game cannot hide it. Config `AnimalForm.HeldItems`: `all` (default, as in the brief), `buildOnly` (Building Plan, Hammer, deployables) or `none`, plus an item blacklist for anything a server owner wants to keep out. Effects that are not the held model - gunshot sound, projectiles, hit effects, gathered resources - still happen normally. Putting on clothing, armour and a backpack remains possible. | 🔧 |
| R9.45 | **Eating from the inventory:** a food or drink item in the inventory that is on the species' diet list (R9.22) can be eaten or drunk through the inventory action; it restores the animal's stats (R9.24), not the frozen human ones. Other food and drink is refused with a message (config); medical items heal the animal's health, which is the player's health. | 🔧 |
| R9.46 | When the animal form dies, the human body dies with its inventory and backpack: they drop in a normal player corpse at that spot (the animal's own corpse and loot are separate). While the player is logged out (R9.31) the inventory stays safe in the hidden body. | 🔧 |
| R9.47 | **Looting:** an animal-form player can loot containers, crates, barrels and corpses (animal and human) like a human: Use key, normal reach from the animal's head, with all the usual locks, authorization and other plugins' loot rules. | 🔧 |
| R9.48 | **Building:** an animal-form player can build, upgrade and repair with the **Building Plan and the Hammer in hand** (held invisibly, R9.44), aiming from the animal's head, with the same building-privilege and distance rules as a human. Placing **deployables** (doors, boxes, furnaces, sleeping bags, ...) works too: the item is held, but like the plan and hammer it is **never visible on the animal** (`AnimalForm.AllowDeployables`, default on). | 🔧 |
| R9.49 | **Crafting:** an animal-form player can craft from the inventory's crafting menu like a human, including the need for a nearby workbench; crafted items go into the human inventory and the crafting queue keeps running while transformed. | ✅ |

## 10. Camera

| ID | Requirement | |
|---|---|---|
| R10.1 | **G** toggles a third-person view while riding any animal or playing as an animal. | 🔧⚠️ |
| R10.2 | Mechanism is decided by spike S7 (the client camera is defined by the mount prefab: the client has a `MountedCameraMode` setting). Candidate order: (1) a vanilla mount whose camera mode supports third person, (2) a camera-follow entity approach, (3) first person only with a clear in-menu note. Whatever works is documented in Help. | 🔧⚠️ |
| R10.3 | **Third person for the plain player on foot** (the brief: "also for the player himself"). The base game has no third-person camera for an ordinary on-foot player and the server has no switch for it. Candidates, tested in spike S7 in this order: (A) the camera system the game already has for spectating (`CameraMode.ThirdPerson`), driven through a hidden-body proxy; (B) a standing third-person seat on an invisible movable platform; (C) a gesture whose view mode is third person. The admin-flag trick that unlocks the developer camera is **rejected** (it exposes admin client features to the player). The first candidate that behaves acceptably (walk, look, interact, no exploit) is built; if none does, this one item is reported as not achievable with a server-only plugin, with the evidence, instead of shipping something broken. | 🔧⚠️ |

## 11. Spray can variants

| ID | Requirement | |
|---|---|---|
| R11.1 | A **Spray Can** used on an animal that has visual variants changes its variant, always **randomly**. Animals with no variants: nothing happens (hint message). | 🔧 |
| R11.2 | Species with variants in the current build: **horse** (breed), **cow/bull/sheep/ram** (coat group, pattern, wool tint from the livestock genetics data). Others: none. | 🔧 |
| R11.3 | Permission `beastmaster.spray`; the spray can is consumed as usual; ownership rule: owner/team (config). | ✅ |
| R11.4 | The re-roll changes **only cosmetic traits** (horse breed; livestock coat group, pattern, wool tint). Livestock gameplay genes (milk/wool yield and so on) share the same stored number and are never touched; the exact bit layout is confirmed in spike S12 before anything is written. A re-rolled look is what the ritual later copies (R9.12). | 🔧 |

## 12. Interaction menu and Help

### 12.1 Opening
| ID | Requirement | |
|---|---|---|
| R12.1 | The menu opens and closes with **F5** (a client key bind to `bm.menu`, applied on join; also `/bm`). Escape and a close button also close it. | 🔧 |

### 12.2 Two layers
| ID | Requirement | |
|---|---|---|
| R12.2 | **Player layer** (everyone with `beastmaster.use`): *Home, Help/Guide, My Animals, Controls, Transformation status*. | ✅ |
| R12.3 | **Control layer** (only authorised users): *Admin (plugin on/off, feature toggles, species toggles), Spawn, Players (access list)*. Authorised = server owner, moderators (config), and any user the admin has granted menu access (R12.7). Config `Menu.OpenForEveryone` (default true) can restrict even the player layer to authorised users (assumption A1). | ✅ |

### 12.4 Help / Guide (explains everything)
| ID | Requirement | |
|---|---|---|
| R12.4 | The Help section is paged, searchable by topic, written in plain English, and covers: **Overview - Controls (all keys) - Riding - Gear and horseshoes - Taming (one page per method) - Commanding animals - Combat and rear attacks - Transformation ritual step by step - Third person - Night vision - Spray can - Animal relations - Species cards (speeds, traits, how to tame, gear) - Permissions and commands - FAQ / troubleshooting.** | ✅ |
| R12.5 | Help text is generated from the live config (numbers, keys, ranges, enabled features), and disabled features are marked as disabled. A **"Help for what I'm looking at"** button opens the species card or the right topic for the aimed animal. | ✅ |

### 12.6 Spawn (control layer)
| ID | Requirement | |
|---|---|---|
| R12.6 | Menu access includes **spawning animals**: pick species, amount, **wild** or **tamed to me**, optional variant, optional "saddled" preset; spawn where the player aims (max distance config) or next to them. Limits in config (max per action, cooldown, max alive plugin-spawned animals); every spawn is logged (who/what/where) and tagged so `bm.clear` can remove them. An admin switch can disable spawning globally. | 🔧 |

### 12.7 Access management by the admin (commands)
| ID | Requirement | |
|---|---|---|
| R12.7 | The admin decides **who gets menu access** (which also allows spawning) with commands: `bm.grant <player|steamid>`, `bm.revoke <player|steamid>`, `bm.access` (list) - available from the server console/RCON and in game (`/bm grant ...`) for owners; also `bm.grantgroup`/`bm.revokegroup` for Oxide groups. Implemented on top of the Oxide permission `beastmaster.menu`, so `oxide.grant`/`o.grant` and groups work too. The Players page of the control layer does the same with buttons. | ✅ |
| R12.8 | Defaults (config): server owners and moderators have menu access automatically; ordinary players do not. | ✅ |
| R12.9 | The control layer switches: whole plugin on/off (persisted), per feature (riding, gear, combat, taming, commands, relations, horse AI, transformation, spray, third person), per species. | ✅ |

## 13. Permissions and commands

| Permission | Meaning | Default |
|---|---|---|
| `beastmaster.use` | Open the menu (player layer), use own animals, commands, gear | grant to `default` group |
| `beastmaster.ride` | Ride the Standard species | grant as wanted |
| `beastmaster.ride.restricted` | Ride chicken, crocodile, snake, rabbit, squirrel, frog | **admin only** |
| `beastmaster.tame` | Use the taming flows | grant as wanted |
| `beastmaster.transform` | Perform the transformation ritual | grant as wanted |
| `beastmaster.transform.restricted` | Transform into the six restricted species (assumption A6) | **admin only** |
| `beastmaster.spray` | Re-roll animal variants with the spray can | grant as wanted |
| `beastmaster.menu` | Control layer + Spawn (R12.3/R12.6) | owners/mods auto; others by `bm.grant` |
| `beastmaster.admin` | Everything, incl. enable/disable, clear, selftest, grant/revoke | owners auto |

Commands (chat `/bm ...` and console `bm.*`): `bm.menu`, `bm.attack`, `bm.view`, `bm.revert`, `bm.cmd <follow|wait|guard|attack|release>`, `bm.keys` (re-apply / reset binds); admin: `bm.enable`, `bm.disable`, `bm.grant`, `bm.revoke`, `bm.access`, `bm.spawn <species> [count] [tamed|wild] [player]`, `bm.clear [radius]`, `bm.reload`, `bm.selftest`, `bm.debug`.

## 14. Config and data

| ID | Requirement | |
|---|---|---|
| R14.1 | `oxide/config/Beastmaster.json`: every number in this spec is a config value (speeds, timers, ranges, damages, probabilities, matrix, keys, feature/species toggles) with English comments in the generated file. Config is validated on load (bad values reset with a log line). | ✅ |
| R14.2 | `oxide/data/Beastmaster/`: tamed-animal records (owner, species, bond, command state, gear, position, health), per-player settings (keys), enable state, spawn tags. | ✅ |
| R14.3 | **Persistence:** animals that the game does not save across restarts are **re-created from the records** at startup (same species, position, health, gear, owner). Verified by spike S14. | 🔧 |
| R14.4 | Version guard + `bm.selftest`: on load the plugin checks that every class, prefab path and item it needs still exists and prints a clear report, so a game update never fails silently. | ✅ |

## 15. Non-functional

| ID | Requirement |
|---|---|
| N1 | Budget: steering/AI updates at 10 Hz per active animal with a per-frame time cap; idle tamed animals update at 1-2 Hz; nothing allocates per tick on the hot path. |
| N2 | Compatible with other NPC/animal plugins: all overrides are removable and reversible; hooks return `null` unless the plugin truly owns the decision. |
| N3 | A crash of one animal's logic must never affect others (isolated try/catch per animal tick). |
| N4 | Clean unload: every spawned entity, UI, bind, and override is released on `Unload`. |
| N5 | Logging levels (error/warn/info/debug) switchable by `bm.debug`. |
| N6 | Antihack safety: the server kills **and** kicks a player whose position is more than 0.3 m (`antihack.terrain_padding`) under the terrain ("InsideTerrain"), checked for every player, mounted ones too. The `OnPlayerViolation` hook can stop the kick but **not** the kill, so the plugin never relies on it: the rider's or hidden human's body (= the seat position) always stays at least 0.15 m above the terrain - the seat lifts on small creatures and slopes, jump arcs never cut through a hill, caves/tunnels (terrain holes) excepted. Verified 2026-10-09 (SPIKES.md). |

## 16. Controls cheat-sheet - how each requested key is really captured

The server never sees raw keys. It sees (a) the **vanilla input actions** (move, jump, duck, sprint, use, fire, reload...) and (b) **console commands** a key can be bound to.

| Requested | Default Rust bind on this client | Mechanism |
|---|---|---|
| **F** attack | `lighttoggle;+focusmap` | **Session 1: the server cannot bind keys** (the client ignores `bind` from a server). F already sends the server command `inventory.lighttoggle`; while riding / in animal form the plugin catches it (hook `OnServerCommand`) and attacks instead. |
| **F5** menu | unbound | Nothing reaches the server from an unbound key. Menu opens with `/bm`; a player may bind F5 once by hand: `bind f5 bm.menu` (shown in Help). |
| **G** third person | `+markcurrentpos` (map mark) | Session 2 checks what G sends; if it places a map marker, the plugin catches it (hook `OnMapMarkerAdd`, the marker is not placed) while riding / in animal form. |
| **X** return to human / get off | `swapseats` | Sends the server command `vehicle.swapseats`; caught while riding / in animal form. |
| **Shift / Ctrl / Space / W A S D / E** | `+sprint`, `+duck`, `+jump`, ... | Vanilla actions, read server-side from the input state. No bind needed. |
| Gestures (command animals) | `B` = `+gestures` | Detected if the server exposes them (spike S11); otherwise Bone item / menu / commands. |

There are no automatic binds (the server cannot set them). Every action also has a chat command, and a player may bind any key by hand in F1 (for example `bind f5 bm.menu`).

## 17. Literal-vs-equivalent register (the honest list)

These are the places where the engine, not effort, sets the limit. Each has a defined equivalent and gets a spike before anything is built on it.

| # | Brief says | Why the literal version is not possible | What is built instead |
|---|---|---|---|
| 1 | Horse attacks with the **ram's head-butt animation** | The client plays animations from the animal's own animator; a horse model has no head-butt clip and the server cannot upload one. | Horse uses its own best vanilla clip (rear/stomp if present) + lunge + damage + sound. Ram/bull show their real head-butt. |
| 2 | Keys **F / F5 / G / X** read by the plugin | The server only sees input actions, not keys, and (verified in spike session 1) **cannot bind keys on a client** - a `bind` sent by the server is ignored. | F and X already send a server command with the vanilla binds (`lighttoggle`, `swapseats`); the plugin catches those while riding / in animal form. G is tested in session 2 (`+markcurrentpos` -> map marker). **F5 is unbound in vanilla, so nothing reaches the server**: the menu opens with the chat command `/bm`, or after the player binds F5 once in F1 (`bind f5 bm.menu` - the Help shows that line). |
| 3 | **Third person with G** - riding, animal form **and the plain on-foot player** | The client camera mode comes from the mount prefab or from spectating. A normal on-foot player has no third-person camera and the server has no switch for it. | Mounts and animal forms: choose the seat/camera mode that supports it (spike S7). On-foot player: candidates A/B/C of R10.3; if none behaves acceptably, the item is reported as not achievable with a server-only plugin, with the evidence. |
| 4 | **Leash** a transformed player | The vanilla lead rope is client-bound to livestock/horses. | Plugin leash (behaviour + feedback), rope visual only if the spike finds a way. |
| 5 | **Crab** transformation | The game's crab entity may show a small group of crabs instead of one. | Built like the other critters; if the spike shows it cannot serve as one crab, that single species is reported as a limit with evidence. |
| 6 | **Gestures** as commands and as animal animations/sounds | The wheel only offers gestures the player owns, and the human gesture has to be suppressed. | Confirmed possible: the game calls the hook `CanUseGesture` for every attempt; gestures are mapped to the species' own clips (section 6A). Bone item, menu and commands remain as fallbacks. |
| 7 | Animals have **their own sounds/animations** everywhere | Only vanilla clips exist; some species lack some clips. | Per-species matrix from spike S4; missing clip -> nearest vanilla clip or sound, documented per species. |
| 8 | Speeds from the table at **every gait** | The animations are authored for fixed gait speeds; much faster movement looks like sliding. | Ladder on the authored gaits; speeds above them are applied up to a visual cap measured in spike S3 and only kept if it looks right in your client. |
| 9 | **Snake** as a walking creature | In this build the snake is a hazard entity (`SnakeHazard`), not a navmesh animal. | The plugin puppets it (slither movement); how it looks is checked in a spike. |
| 11 | **Jumping** (Space; panther +25 % higher, frog x3, horse over obstacles) | Animals walk on a navigation mesh and never jump freely; only scripted pounces and climbs exist; the horse has no jump. | A plugin jump built on the game's own warped-clip technique, with a per-species fallback look chosen in spike S18 (SPEC section 2A). |
| 10 | **Domestic pig, zombie bear/wolf, doe** | No such classes exist in the game code of this build. | Kept only if the START prefab dump finds a matching prefab; otherwise reported and dropped from the roster. |
| 12 | **Third person** that behaves like a real third-person game (pivot around the character, accurate crosshair, camera that does not go into walls) | The game's own third-person camera is client code; the server can only switch it on. | Pivot placed inside the animal (R19.6), interactions from the animal's head (R19.4); crosshair parallax and wall clipping stay (R19.20). |
| 13 | **Boss looks** - black colour, glowing eye colours, bigger size, slime/ice textures (R20.2, R20.5) | A server cannot change models or textures. **Size is possible** (corrected 2026-10-10 on the user's hint): the game networks the scale of an entity whose `networkEntityScale` flag is set - the admin command `entity scale` does exactly that. | Vanilla black breed / coat genes, the game's existing effects, **a 1.25x bigger body (config `Boss.Scale`)**; the seats keep normal size so that a rider sits at the right height and is not enlarged; glowing eyes and textures are not possible. |

## 18. Assumptions to confirm at START (defaults in bold)

| # | Assumption | Default |
|---|---|---|
| A1 | The Help and the player layer are open to everyone; only the control layer (on/off, Spawn, Players) needs menu access. | **Yes** (`Menu.OpenForEveryone`) |
| A2 | Riding any non-vanilla animal needs: tamed + saddle + permission. | **Yes** |
| A3 | Shoes: basic = between sprint and top, advanced = top (exact numbers copied from the vanilla horse). | **Yes** |
| A4 | Restricted species (chicken, croc, snake, rabbit, squirrel, frog): no taming/saddle needed, permission is enough, animal is passive while ridden, no gear in v1. | **Yes** |
| A5 | Pig, calf, lamb and zombie variants have no taming flow in v1 (spawn, transform only; zombie bear/wolf follow the bear/wolf flows). | **Yes** |
| A6 | Transforming into the six restricted species needs `beastmaster.transform.restricted`. | **Yes** |
| A7 | Plugin name `Beastmaster`. | **Yes** |
| A8 | Crab transformation is built; only if the game's crab entity cannot serve as one crab is it reported as a limit, with evidence. | **Yes** |
| A9 | Horse armour is out of scope. | **Yes** |
| A10 | The Wespner server runs the **latest Rust build** (confirmed by you), the same as the dev server. Which framework (Oxide or Carbon) and which OS it uses is still unknown. Not blocking - the plugin targets the common Oxide API and is tested on both. To tell me: run `version` and `oxide.version` or `c.version` in the server console. | **Common API** |
| A11 | `.css` in the brief means `.cs`. | **Yes** |
| A12 | Critters without an attack clip (rabbit, squirrel, frog): the attack key plays the species' call/hop and deals a minimal nip (config) instead of nothing. | **Yes** |
| A13 | The ritual needs a corpse killed by the performing player or their team (anti-abuse; config `Ritual.RequireOwnKill`). | **Yes** |
| A14 | Horse attack = vanilla rear-up animation + forward strike (R4.4), because the ram head-butt cannot be shown on a horse model. | **Needs your OK** |
| A15 | Horse temperament split at spawn: calm 40 %, shy 35 %, aggressive 25 % (all in config). Sedation default 60 s. | **Proposal - change freely** |
| A16 | In animal form any item can be held and used (default `all`) and no held item is ever visible on the animal. This replaces the earlier "no items in hand". | **Confirmed by you (latest word)** |

## 19. Additions from spike session 2 (2026-10-09/10)

### 19.1 Being the animal (overrides older wording in section 9 where they differ)

| ID | Requirement | |
|---|---|---|
| R19.1 | While playing as an animal the **human is completely invisible** - body, held items, shadow, name tag - to everyone **including the player himself**, and **undetectable**: animals, NPCs, helicopters, Bradley and turrets ignore the human (the game's own AI ignore list `SimpleAIMemory.PlayerIgnoreList` + hook `CanBeTargeted`). A wild bear that attacks attacks **the animal**. | 🔧⚠️ self-view under test (BmSpike 0.3.0 `selfhide`) |
| R19.2 | The human is **invulnerable**; only the animal takes damage. The animal's health **is** the player's health (shown on the player's health bar). The animal dies -> the player dies. | 🔧 |
| R19.3 | Same species tolerate each other because the wild animals only see the animal (R7.1): a player-bear is not attacked by bears, a player-boar not by boars. | 🔧 |
| R19.4 | **Interactions** (open a crate, a door, ...): in first person through the crosshair from the animal's eyes; in third person from the animal's **head** - the head has to be just in front of the object for the interaction to be offered. | 🔧 |
| R19.5 | **X** leaves the animal form: the player becomes human and the animal body **vanishes in smoke**. Smoke also appears when the transformation happens. | 🔧 effect chosen in a spike |
| R19.6 | Third-person view: the camera orbits **the animal** (pivot inside the animal), slightly closer than the board test; first person from the animal's eyes. See R19.20 for the honest limits of the game's third-person camera. | 🔧⚠️ |
| R19.7 | Breeding in animal form: a player-cow can breed with a bull (and a player-bull with a cow), a player-sheep with a ram and back; a real calf/lamb is born (vanilla livestock pregnancy). | 🔧 |
| R19.8 | Others can milk a player-cow and shear a player-sheep (R9.13-R9.15 confirmed again). | 🔧 |

### 19.2 Movement and animation

| ID | Requirement | |
|---|---|---|
| R19.9 | Default movement uses each species' **own animation speeds** for walk, trot/jog, run and run variants (the authored gaits); faster table speeds only on top of that (R2.5). | 🔧 |
| R19.10 | **Turning** looks like the free animals / the horse: limited turn rate from the species' own navigation agent, turn momentum, slower turns at speed, turn-in-place at standstill. | 🔧 0.3.0 |
| R19.11 | **Ctrl brakes** cow, bull, sheep, ram, doe and stag like the horse; species with their own stop clip use it (livestock: the canter stop-and-turn clips when turning). | 🔧 0.3.0 |
| R19.12 | The bull's (livestock) **sliding 180-degree turn** at speed works for the player too (the game's `turnCantering` clips): S while running. | 🔧 0.3.0 |
| R19.13 | Standing still under control = the species' **idle animations** (breathing, looking, head shake, calls), ridden or played. | 🔧 0.3.0 |
| R19.14 | The **head** turns slowly toward where the camera looks, less at speed, never staring sideways while running. | 🔧 0.3.0 |
| R19.15 | **Crab** walks the crab way (body yaw offset per species, tuned in game). | 🔧 0.3.0 |
| R19.16 | **Steep slopes like the horse**: walking slowly down a very steep slope usually goes fine, sometimes the animal gets scared; running down it gives a high chance to **fall** - sometimes the animal gets up, sometimes it dies. For every ridden/played animal. Equivalent of the horse ragdoll: the species' knock-down / canter-death clips, the rider thrown off. | 🔧 spike |
| R19.17 | **Attacks deal the game's own damage** of that animal (damage, type, reach, angle, shove from the animal's attack states) to players, animals and objects - and **gather** from trees, ore and corpses for the player. | 🔧 0.3.0 |

### 19.3 Wild behaviour and climate

| ID | Requirement | |
|---|---|---|
| R19.18 | Horses, cows, sheep and does: **30 %** of them defend themselves when attacked, the rest flee. Bulls and rams always defend (vanilla). **Stags always defend.** | 🔧 |
| R19.19 | **Climate:** a polar bear outside the snow needs a lot of water or it gets slower and weaker; water is given into its inventory (water bottle / water jug). Same for the arctic wolf (no arctic wolf exists in this build - kept for when it appears). Animals that do not belong in the snow need warming there: any cooked meat in their inventory. | 🔧 |

### 19.4 Honest limit of the game's third-person camera

| ID | Note |
|---|---|
| R19.20 | The third person used here is the game's own client camera, switched on by the server (`PlayerFlags.ThirdPersonViewmode`, verified in session 2). The server cannot change how that camera works: its pivot is the player's eyes (the plugin therefore puts the hidden body inside the animal so the camera turns around the animal), the crosshair has the usual third-person parallax, and the camera has **no wall collision** (it can slide into rocks and walls). These three points cannot be fixed by a server plugin. |

## 20. Boss animals (requested 2026-10-10)

| ID | Requirement | Feasibility |
|---|---|---|
| R20.1 | Rare boss versions of **horse, bull, ram and stag**. Spawn **only at night**, chance **4 %** per check; they appear **in fire**; untamed bosses vanish when the night ends. | ✅ plugin logic |
| R20.2 | Look: **black** body; eyes glowing - horse **orange**, bull **green**, ram **yellow**, stag **blue**; a light smoke around them; slightly **bigger** than normal. | ⚠️ black horse = the vanilla "Black" breed ✅; black bull/ram only if the coat genes allow it (spike S12); glowing eyes = small coloured lights attached at the head (needs a light entity with that colour - spike); **size** ✅ (corrected: `networkEntityScale` + `localScale`, 1.25x by default, `Boss.Scale`; the seats are children of the animal so the height of the back scales with it, and they keep normal size so the rider is not enlarged); glowing eyes ❌ |
| R20.3 | On spawn a distant **echo of the animal's call** is heard. | ⚠️ the calls live inside animation clips: the boss plays its call (heard in normal range) + a message; a true map-wide echo is not available |
| R20.4 | Extremely aggressive, **1.5x faster**, **2x stronger**, **4000 HP**, **99 damage** per hit, attack animations slightly **slower**, **immune to fire**. Bosses may fight each other but deal **no damage** to each other. | ✅ (speed, health, damage, playback speed, damage filter) |
| R20.5 | **Fire attacks**: hits set the target slightly on fire (like stepping into a campfire). Per-boss effects - horse: flames, embers, fire trail, spark burst; bull: toxic green slime, acid drops, green poison smoke; ram: blue-white lightning, electric shock wave (tesla-like sparks); stag: white frost mist, ice crystals, shatter. | ⚠️ burning ✅ (heat damage over time); effects only from the game's existing effect prefabs (fire, sparks, shock, smoke); textures such as slime running down the body ❌ (no custom textures from a server) |
| R20.6 | Loot: special **saddle** (unique per boss) + scrap: bull and ram **4000**, horse **6000**, stag **7000**; killing several bosses together = **1.5x** scrap each. | ✅ item with a unique name; a unique **colour** only if a matching skin exists ⚠️ |
| R20.7 | The special saddle tames the next boss of that kind instantly; a player carrying it is **not attacked** by that boss. Tamed bosses do not vanish by day. | ✅ |
| R20.8 | Breeding a tamed boss: **10 %** chance of a **mini-boss** offspring - same effects and eyes, **2000 HP**, no scrap. | ✅ plugin logic on the vanilla pregnancy |
