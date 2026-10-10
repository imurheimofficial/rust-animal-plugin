# Research - what is verified, what is not

Date: 2026-10-09. Method: the real game was inspected on this PC, not guessed.
Evidence tags: ✅ verified in the game code or data - 🟡 plausible, only indirect evidence - ❓ unverified - ❌ contradicted - ⚠️ true but with a catch.

Sources used
- **Server code**: Rust Dedicated Server build **25823813** (2026-10-09), decompiled with ILSpy 9.1 (5 123 files) into `C:\RustDev\decompiled\orig`. The same build with **Oxide 2.0.7820** patched in is in `C:\RustDev\decompiled\oxide-patched` (688 distinct hooks, list in `_data/`).
- **Client**: Steam build **25797961** (2026-10-08) in `C:\Program Files (x86)\Steam\steamapps\common\Rust`: the IL2CPP metadata (137 571 identifiers), the asset manifest (16 899 prefab paths) and your key files `keys_default.cfg`, `keys.cfg`.
- **Web**: hosting-provider articles on the Livestock Update (secondary, no official patch notes found), a paid-plugin shop page found by search. The uMod docs page returned 403 to the fetch tool, so the hook list comes from the patched game code instead (better: exact).

Your client is one build behind the server. Steam updates it automatically; it must match before any test.

---

## 1. What the game really contains (build 25823813)

| Fact | Status | Evidence |
|---|---|---|
| **Every animal except the horse is a `BaseNPC2`** (namespace `Rust.Ai.Gen2`), driven by a finite-state-machine AI (`FSMComponent`) and a custom navigation agent (`RustNavMeshAgent`). The old `BaseNpc`/`BaseAnimalNPC` classes still exist but only `Zombie` uses them. | ✅ | `Bear`, `PolarBear`, `Boar`, `Stag`, `Chicken`, `Wolf2`, `Tiger`, `Panther`, `Crocodile`, `Cow`, `Sheep`, `Rabbit`, `Squirrel`, `Frog` all `: BaseNPC2` (critters via `CritterAnimal`, livestock via `LivestockAnimal`) |
| Consequence: how-tos written for the old `BaseNpc`/`NavMeshAgent` animals (and paid ride-an-animal plugins made before this build) **do not apply as they are** - the movement/AI layer is new | ✅ | see above |
| **The horse is `RidableHorse : BaseVehicle`** (physics, wheel colliders, gaits Walk/Trot/Canter/Gallop, stamina, breed). `BaseRidableAnimal` no longer exists, so old plugins that name it break. | ✅ | `RidableHorse.cs`; the name is absent from the whole decompile |
| Livestock = `Cow` and `Sheep` (`LivestockAnimal`), with `IsMale/IsFemale`, `AgeStage {Infant, Adult}`. **Bull = male cow, ram = male sheep** (not separate classes); calf/lamb = infant stage, with male/female prefabs. | ✅ | `LivestockAnimal.cs`, `MaleCalfPrefab`, `FemaleLambPrefab` etc. |
| **Snake is a `SnakeHazard`** (a slithering hazard entity that bites when a player fails to jump), not a navmesh animal. | ⚠️ | `SnakeHazard : WildlifeHazard` |
| Critters are `CritterAnimal : BaseNPC2` with `CritterType {Frog, Rabbit, Squirrel, Crabs}` (class `Crabs`); jellyfish and sea turtle are `SwimmingNPC`; shark is `SimpleShark`. The client also has "swarm" identifiers for crabs and jellyfish, so whether one crab entity shows one crab or a small group is open. | 🟡 | decompile + client metadata |
| **No** `Pig`, `Doe`, `Zombie bear`, `Zombie wolf`, `Cougar` classes in this build. Panther = the "cougar/puma" of the brief. Polar bear and (probably) arctic wolf are prefab variants. | ❌ / ❓ | greps; the prefab dump at START settles the variants |
| Livestock Update (cows, bulls, calves, sheep, lambs, breeding genetics, milk, wool, ranch vendor, animal fences, hitch & trough) went live around 2026-10-01. | ✅ (secondary) | hosting blogs + `LivestockVendor_Stables`, `HitchTrough`, `Genes` in the code |

## 2. How the animals move, animate and can be taken over (the mechanism)

| Fact | Status | Why it matters |
|---|---|---|
| `RustNavMeshAgent` has public control: `SetDestinationWithParams(pos, autoBraking, gait, accel, decel, ...)`, `Move(delta)` (navmesh-constrained), `Pause(source)` / `Unpause(source)`, `overrideDirectionWS`, `speed`, `isStopped`, `ResetPath()`; flags for swimming and jumping. | ✅ | The plugin can steer an animal without touching the AI, and can pause the agent while a rider is on. |
| Six authored **gaits**: Sneak, Walk, Jog, Run, Sprint, FullSprint, each with a speed value per species; the code says animations are authored at these speeds and anything in between "reads as sliding". | ⚠️ | The brief's speed table (e.g. tiger 21.5 m/s) may look like skating; the plan measures a visual cap per species (spike S3). |
| `FSMComponent.SetFsmActive(false)` stops the AI; `SetState(...)` forces a state; FSM states are public fields (e.g. `BasicAnimalFsm.attack`). | ✅ | AI can be suspended while ridden and resumed after. |
| **Animations are "montages"**: the server tells the client `CL_PlayMontageDelayed(hash of the clip name)` through `RootMotionPlayer.PlayServerAndTakeFromPool(clip, speed, pauseAgent)`, plus `CL_PlayAdditiveMontage` and `CL_StopMontage`. An attack is a montage plus damage at a fraction of the clip (`State_AnimalBite`: `DamageAtClipFraction`). | ✅ | The plugin can play any clip that belongs to that animal's own prefab (its states hold them) - attacks, calls, idles, stomp, etc. - and sounds come with the clip. |
| Locomotion is **not** sent as a separate value: the client derives the walk/run blend from the motion itself. Networked NPC state is only flags (swimming, jumping, aiming, relaxed, crouching, alert) and the look direction. | ✅ | Speed drives the visual gait; see the sliding note above. |
| `NpcBarkComponent` voicelines are for scientists. Animal sounds are inside the clips (calls are states: `State_Howl`, `State_Bark`, `State_Growl`, `State_Roar`, hurt states). | ✅ | Reactive sounds = playing the right clip for the stimulus. |
| NPC mounting exists (`NPCMountable`, `BaseNPC2.StartMountTo`): livestock can stand on a flatbed vehicle module. It is not a player-riding mechanism. | ✅ | Rules out "the game already lets players ride cows". |
| **Animals do not jump in normal movement.** They walk on the navigation mesh, step up at most about 0.75 m (`ReachSnapClimb`), go around obstacles, and die if pushed off the mesh (`DieOffNavmesh`). | ✅ | A Space-bar jump does not exist for them; the plugin has to add it. |
| **Scripted jumps are the only ones** and are animations, not physics: the server pauses the agent, sets the networked `IsJumping` flag, plays a **root-motion clip** with `constrainToNavmesh = false`, and **warps** the clip's forward and vertical travel so it ends exactly at the landing point. Used by the wolf (`State_AttackUnreachable`, `leapAway`), the tiger and panther (`State_AttackUnreachableWarped`: jump up, bite, jump back, 3.6 s long) and by livestock climbing onto a flatbed (`State_MountVehicle`). Limits of the game's own pounce: **at most 2.7 m up and 7 m across, with line of sight** (`CanJumpFromPosToPos`). | ✅ | This is the technique the plugin reuses; the long authored clips are made for AI pounces, not for an instant jump. |
| **Hills:** the navigation mesh the animals walk on is built with a maximum walkable slope of **45°**, maximum step **0.4 m**, agent height 1.7 m, radius 0.25 m (defaults of the mesh builder). The **client** component `NPCAnimController` (an `IClientComponent`) tilts the body along the slope (up to 30°), snaps it to the ground and plays the locomotion blend; the server only moves the entity. The same component names the locomotion clips (`prowl`, `walk`, `trot`, `run`) as non-montage, which also tells the emote catalog what to exclude. | ✅ | A controlled animal that moves along the same mesh looks right on hills with no extra work; cliffs stay closed to it as to the AI animals. |
| **The horse has no jump at all**: no jump input in `RidableHorse`; it steers around obstacles automatically and ragdolls if airborne too long (`maxAirTimeBeforeRagdoll`). | ✅ | The brief's "smooth jump over obstacles" for the horse is not vanilla. |

## 3. Livestock details that matter

| Fact | Status |
|---|---|
| Needs: `Fullness`, `PersonalSpace`, `Hydration` (public `GetNeed`, `SetNeedValue`); `ConsumeFromTrough`, `ConsumeFromBarrel`; states `Graze`, `Drink`, `ReturnToHerd`, `HerdFlee`, `Frolic`, `Mate`, `Sleep`, `Rest`. | ✅ |
| **Trust ladder** `Stranger, Known, Tolerated, Bonded, Herd`; familiarity is credited by `AddFamiliarity(userId, seconds, reason {Handled, Purchased, Debug})`; hooks `OnLivestockAnimalFamiliarityAdd/Added`, `CanLivestockAnimalRefuseTarget`. This is the vanilla "taming" the brief wants copied for the horse. | ✅ |
| **Males are protective**: adult males (and females with calves) defend dependents: warning stomp (`State_Stomp`), approach, charge (`chargeRun`), head-butt (`headbutt`), kick (`kick`) and **rear kick (`kickBehind`)**, `State_HoldGround` (stand between threat and cow/calf). A male does not attack a fully bonded player. The brief's "territorial bull/ram" is right. | ✅ |
| Cow milking `Cow.TryMilk` needs trust (otherwise `OnGrabbedBy` and refusal); **shearing `Sheep.Shear` works without trust** (it only startles). | ✅ |
| Lead: **`LivestockAnimal.TryLead(BasePlayer, bool)` is public**; the server keeps `LeadingPlayer`; the rope is only a client visual (`LeadingRope`, `LivestockLeadingRope`, `HorseLeadingRope`). | ✅ |
| Appearance is stored as an integer **`Genes`** (+ `ShornFleece`) on the animal **and on its `LivestockCorpse`**; horse: `breedIndex` on the animal and on `HorseCorpse`, `SetBreed(int)`. | ✅ |
| Genes also hold gameplay traits (yield) in the same number. | 🟡 (layout unverified; spike S12 before anything is written) |
| **Transport:** `LivestockMountable : NPCMountable` sits on vehicle modules (the flatbed). It has size classes (`AcceptedSizes` Small/Large) and the client calls `RPC_LoadAnimal` / `RPC_UnloadAnimal` (3 m) while **leading** the animal; the setting `livestock.allowmounting` must be on. `NPCMountable.MountImmediate(npc, index)` / `RequestMount(npc)` are public, `BaseNPC2.OnMountFinalized` parents the animal to the anchor and pauses its agent. | ✅ |
| **Doors:** `Door.RPC_OpenDoor`/`RPC_CloseDoor` accept a player within 3 m whose `CanInteract(true)` holds (mounted is fine; **spectating is not**), then ask the lock (`baseLock.OnTryToOpen(player)`), so the animal form gets exactly the human's rights. | ✅ |

## 4. The horse

| Fact | Status |
|---|---|
| Gaits (`Walk, Trot, Canter, Gallop`), `equipmentSpeedMod` from worn items (`ItemModAnimalEquipment.speedModifier`), `currentBreed.maxSpeed`, stamina, roads give a gallop bonus. | ✅ |
| Items: `horse.saddle.single`, `horse.saddle.double`, `horse.saddlebag`, `horse.shoes.basic`, `horse.shoes.advanced` (almost certainly the "High Quality Horse Shoes"), wood and roadsign armour. Seat prefabs `HorseSaddle` and `HorseSaddleRear`. Mount pose `Horseback`. | ✅ / 🟡 (display name) |
| Own client animations triggered by the server: **`CLIENT_Stand` (rear up)**, `CLIENT_Skid`, `CLIENT_HorseEat`, `CLIENT_StartLeading/StopLeading`, `CLIENT_HorseUpdate`. No attack and no generic "play this clip". | ✅ |
| Implements `IMedicalToolTarget` (syringes and healing items work on horses). Leading, hitching, dung, claim hooks exist (`OnHorseLead`, `OnHorseHitch`, `OnRidableAnimalClaim`, `OnAnimalDungProduce`). | ✅ |

## 5. Attack and animation inventory (from the FSM classes)

See the matrix in SPEC section 4. Short version: livestock head-butt/kick/rear kick; bear, boar, stag, chicken one bite; wolf bite + leap; tiger/panther one deadly strike; crocodile four attacks; horse rear-up only; rabbit/squirrel/frog none.
Idle/call clips per species (random idle, howl, bark, growl, roar, stomp, raise head, sleep loop, graze, drink, eat) are held by public state objects, so the plugin can enumerate them at runtime (SPEC R6A.3).

## 6. Camera facts

- `BaseMountable.MountedCameraMode` (`FirstPerson`, `ThirdPerson`, `Eyes`, `FirstPersonWithArms`; enum has `LastCyclableMode = 3`) is a **per-seat-prefab setting read by the client**; the server never sends it. A third-person seat needs a prefab that already has it - spike S7 lists every mountable prefab with its mode.
- `GestureConfig.viewMode` shows some gestures switch the camera.
- `PlayerFlags.ThirdPersonViewmode` exists but the server code never sets it. No console command for a third-person camera exists in the client for a normal player (only free-camera/debug tools for admins).
- ⚠️ Third person for an ordinary on-foot player has **no known vanilla mechanism**; SPEC R10.3 lists the candidate approaches and the honest fallback.

## 7. Keys on this client (from your files)

| Key | Default | Your file | Note |
|---|---|---|---|
| F | `lighttoggle`, `+focusmap` | same | rebind while mounted/transformed |
| F5 | unbound | unbound | free |
| G | `+map` | `+markcurrentpos` | rebind while mounted/transformed |
| X | `swapseats` | `swapseats` | free to reuse only when the animal form has no seats to swap |
| B | `+gestures` | `+gestures` | the gesture wheel |
| P | `+pets` | `+pets` | vanilla pet-command wheel (for `BasePet`, not usable for these animals) |
| Shift/Ctrl/Space/E/R/LMB/RMB/MMB | `+sprint` `+duck` `+jump` `+use` `+reload` `+attack` `+attack2` `+attack3` | same | read by the server as input actions |

The server can send console commands to a client (`ConsoleNetwork.SendClientCommand` exists). Whether the client accepts `bind` from the server is checked in spike S8 on your client.

## 8. Hooks (Oxide 2.0.7820 on this build)

Present and relevant: `OnPlayerInput`, `CanMountEntity`, `OnEntityMounted`, `OnEntityDismounted`, `CanDismountEntity`, `OnPlayerWantsMount`, `OnPlayerWantsDismount`, `CanSwapToSeat`, `OnEntityTakeDamage`, `OnEntityDeath`, `OnEntityKill`, `OnEntitySpawned`, `OnPlayerAttack`, `OnTrapTrigger`, `OnTrapArm`, `OnTrapDisarm`, `OnHealingItemUse`, `OnLootEntity`, `CanLootEntity`, `OnItemAction`, `OnItemUse`, `OnActiveItemChanged`, `OnEntityReskin`, `OnEntityReskinned`, `OnSprayCreate`, `CanUseGesture`, `OnTurretTarget`, `CanBeTargeted`, `OnPlayerDeath`, `OnPlayerDisconnected`, `OnPlayerSleep`, `OnPlayerRespawn`, `OnWeaponFired`, `OnLivestockAnimalFamiliarityAdd(ed)`, `CanLivestockAnimalRefuseTarget`, `OnHorseLead`, `OnHorseHitch`, `OnRidableAnimalClaim`, `OnAnimalDungProduce(d)`.
**Not present:** a hook for the new animals' target selection (the existing `OnNpcTarget` belongs to the old brain). The plugin overrides the animals' targeting itself where needed.
Full list: `_data/oxide-hooks-2.0.7820-rust-25823813.txt`.

## 9. Prior art

- Paid plugins "Personal Animal" (ride and command bear, boar, chicken, deer, wolf; inventory; attack) and "IQ Bear Ride" exist for older builds, which proves the idea works on the server side; they predate the new animal layer, so nothing is reused and nothing is copied.
- No "become an animal" plugin was found.

## 10. Claims check of the community notes

| Claim in the brief | Status | Evidence / catch |
|---|---|---|
| Cow, bull, calf, sheep, lamb exist | ✅ | Livestock Update, `IsMale`, infant stage |
| Ram exists | 🟡 | male sheep (`IsMale`), not named in the articles |
| Livestock need a server-activated navmesh, have hunger, thirst, herding, walk to troughs, can enter structures | ✅ / 🟡 | `Need`, `State_Graze/Drink`, `HitchTrough`, `Trans_IsNavmeshReady`; doors: `canOpenDoors` |
| Bulls and rams: defensive territorial AI that guards the herd and attacks strangers | ✅ | protective males, stomp, charge, head-butt, kicks, hold-ground |
| Horse has only "stand" and "flee", no combat AI | 🟡 | horse is a physics vehicle without any FSM; nothing contradicts it |
| Horse has a seat; cow has only a lead | ✅ | `HorseSaddle` seat prefabs; `LivestockLeadingRope` / `TryLead` |
| Horse gear: saddle single/double, saddle bag, basic and high-quality shoes; shoes add speed | ✅ | item prefabs, `equipmentSpeedMod` |
| Wolves flee from a torch | 🟡 | `State_FleeFire`, `State_ApproachFire`, `growlFire` in `Wolf2FSM` and `TigerFSM` (tigers and panthers react to fire too) |
| Chase a wolf with a torch for a minute and it follows you | ❌ vanilla | a plugin feature, as the brief says |
| Bear trap is not triggered by animals | ❓ | the hook `OnTrapTrigger(BearTrap, GameObject)` exists; the trap's own filter is checked in spike S10 |
| Blowpipe and Incapacitate darts | 🟡 | `BlowPipeWeapon`, `ItemModProjectileDart`; the dart item names are read from the server at START |
| Medical Syringe | ✅ | `MedicalTool`; horses already accept it |
| Gestures can command animals | ✅ | `CanUseGesture` hook; gestures are readable |
| Spray can changes variant | 🟡 | `OnEntityReskin(ed)`; horse `SetBreed`, livestock `Genes` (cosmetic bits only) |
| Night vision works for the animal view | ❓ | spike S6 |
| Pig, doe, zombie bear/wolf, cougar | ❌ / ❓ | no such classes (SPEC section 1) |
| Snake rideable / transformable | ⚠️ | it is a hazard entity |
| Crab | 🟡 | critter entity `Crabs`; single-crab question open (spikes S2/S4) |
| Loot lists (bear meat, animal fat, cougar claw, crocodile head bag, ...) | ❓ | not needed by any feature; ignored |
| F5 free, X = seat swap, G = map | ✅ | your key files |

## 11. What stays unknown until a server is running

Prefab paths of every animal and variant; per-species gait speeds; every mountable prefab's camera mode and pose; the horse's real shoe bonus numbers; which clips each species holds; whether the client accepts server-sent `bind`; whether a hidden player can mount and stay hidden; how gestures behave at the wheel; the genes bit layout; which animals the game saves across restarts. Each is a numbered spike in PLAN.md with a pass/fail test.
