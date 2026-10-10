# Spike report - Phase 0

Started 2026-10-09 (START). Dev server: Rust 2634.289.1 (network 2634, build 25823813) + Oxide 2.0.7820, Unity 6000.3.15x1,
`C:\RustDev\server-oxide`, 127.0.0.1:28015. Raw game data: [_data/dump-summary.md](_data/dump-summary.md).

Legend: ✅ passed - ⏳ waiting for the in-game session - ⚠️ passed with a rule for the real plugin - ❌ failed.

## Summary

| Spike | Server side (no player) | In game (your eyes) | Decision / rule for the plugin |
|---|---|---|---|
| S0 environment | ✅ server, Oxide compiler, RCON, harness, dump | - | Harness mirrors the server (see "Compile rules") |
| S1 seat on animals | - | ⚠️ session 1: bear ridden, "sort of worked"; details in session 2 | saddle seat = `assets/prefabs/vehicle/seats/horsesaddle.prefab` (pose Horseback) |
| S2 steering | ✅ all 18 species move along the navmesh with the AI parked (0.2.0: `FSMComponent.SetState(State_Nothing)` + `SetFsmActive(false)` + `RustNavMeshAgent.Pause` + `Move`) | ❌ session 1: cow and sheep animations "totally broken" -> cause found and fixed in 0.2.0 (clean takeover, below); ⏳ session 2 (A) | steering = parked AI + paused agent + `Move(delta)` each physics step + own rotation |
| S3 gaits / sliding | ✅ gait speeds read per species (table below) | ⏳ visual cap | ladder = the species' authored gaits |
| S4 clips / sound | ✅ attack clip played on every species that has one | ⏳ played on the bear in session 1, not commented; session 2 | clip catalog read from the FSM states at runtime |
| S5 AI suspend/resume | ✅ AI resumes normally (graze, idle, sleep, howl, flee, kick) after a clean park | - | critters stay dormant (`Deactivate`) when no player is near - normal |
| S6 possession ("play as the animal") | - | ⏳ not reached in session 1; session 2 (D) | hidden seat = `assets/bundled/prefabs/static/chair.invisible.static.prefab` + `limitNetworking`; views head / chase / orbit |
| S7 camera | ✅ seat camera modes listed (below) | ❌ session 1: the only third-person seat (boogie board) lays the rider on the belly; ⏳ session 2 (B, C, D): `PlayerFlags.ThirdPersonViewmode`, chase and orbit views | riding: flag test; animal form: chase/orbit |
| S8 key binds | - | ❌ session 1: `bind` sent by the server is ignored by the client | catch the commands the vanilla binds already send: F `inventory.lighttoggle`, X `vehicle.swapseats` (`OnServerCommand`); G under test (`OnMapMarkerAdd`) |
| S11 gestures | ✅ session 1 log: `CanUseGesture` fired for all 10 gestures while riding, the human gesture was blocked and an animal clip played | ⏳ look not commented; session 2 (D) | 10 gestures every player owns (below) |
| S18 jump | ⚠️ jump + `WarpToWorldPosition` worked on 17/18; one landing point (panther) was off the navmesh | ⏳ look | **validate the landing on the navmesh before jumping** |
| S9, S10, S12-S17 | later sessions | | |

## Facts from the running game that change or confirm the SPEC

| Topic | Fact | Effect |
|---|---|---|
| Bull | has its own prefab `assets/rust.ai/agents/bull/bull.prefab` (class `Cow`) | bull = separate spawnable species |
| Ram | **no own prefab** - only `sheep.prefab` and `lamb.prefab` | ram = male sheep (sex is in the genes); confirmed at the genes spike S12 |
| Crab | `assets/rust.ai/agents/crabs/crab_single.prefab` exists (one crab) besides the group prefab | crab transformation is fine |
| Pig, doe, arctic wolf, zombie bear/wolf | **no prefabs in this build** | dropped from the roster unless a later update adds them |
| Corpses | every species has a corpse prefab **except rabbit, squirrel, frog, crab** | ritual stand-in body needed for those four (SPEC R9.35) |
| Gait speeds (authored, m/s) | bear 8.0, polar bear 8.0, boar 5.0, stag 8.25, wolf 9.0, tiger 15.0, panther 15.0, cow/bull 7.0, sheep 5.64, chicken 2.0, crocodile 8.0, rabbit 7.01, squirrel 5.0, frog 1.1 (hops), crab 2.0 | the brief's top speeds exceed these by 15-190 % -> visual cap per species in S3 |
| Clips | bear, polar bear, boar, stag: only attack, idle, sleeping. Cow/bull: head-butt, kick, stomp, moo x2, head shake, look, food search, knock-downs, turns, mount up/down. Sheep/lamb: charge (head-butt), stomp, idles. Wolf: run attack, ledge pounce, bark, howl, growl, eat, sleep, 6 idles. Tiger/panther: run attack, ledge pounce, roar, fire growl, eat, drag, sit, lick, stretch, sleep. Crocodile: 4 attacks, idles, turns. Chicken: peck + 7 idles. Rabbit, squirrel: idles. Frog: none (death only). | per-species action lists for gestures and reactions |
| Rear kick | the livestock `kickBehind` state reuses the cow kick clip (no separate clip); sheep have **no kick** at all | rear attack: cow/bull yes, sheep/ram head-butt only |
| Seats | `chair.invisible.static` (no model, can wield items) exists; **one** seat has a fixed third-person camera: `boogieboard.deployed` (lying pose). Correction: the 55 seats listed as "LastCyclableMode" are plain first person with arms - in the game's enum `LastCyclableMode = 3` is only another name of `FirstPersonWithArms` | hidden body seat; no sitting third-person seat exists |
| Gestures every player owns | clap, hurry, ok, point, shrug, surrender, thumbs down, thumbs up, victory, wave (plus hidden: kick, petting, push, parachute land) | default gesture mapping uses these 10 |
| Items | `dart.incapacitate`, `blowpipe`, `syringe.medical`, `trap.bear` (shown in game as "Snap Trap"), `spraycan`, `torch`, `bone.fragments`, `horse.saddle.single`, `horse.saddle.double`, `horse.saddlebag` (speed -1.0), `horse.shoes.basic` (+1.5), `horse.shoes.advanced` "High Quality Horse Shoes" (+2.5), foods with calories/hydration/health values | gear and diet tables use the real numbers |
| Horse | seats on bone `spine_3`: two driver positions + rear seat; breeds: Appaloosa, Bay, Buckskin, Chestnut, Dapple Grey, Piebald, Pinto, Red Roan, White, Black; slope (10-40°); airborne-ragdoll after 2.5 s | horse jump must stay under 2.5 s in the air |
| Livestock taming | `trustToLead` = 300 s of familiarity, `maxTrust` (bonded) = 900 s, gained within 20 m; lead breaks at 7 m (`leadBreakDistance`) | "tame the horse like a cow" = the same numbers |

## Compile rules learned (the real plugin follows them)

1. Plugins get a fixed reference set. PhysicsModule, AIModule and Newtonsoft (from `Oxide.References`) are available; **UnityEngine.AnimationModule is not** - any code that names `AnimationClip` (even an overload in a method group) needs `// Reference: UnityEngine.AnimationModule` or reflection. The harness reproduces this.
2. Newtonsoft must come from `Oxide.References`, never from the standalone `Newtonsoft.Json.dll`.
3. Oxide makes private game members visible to plugins (publicizer); the plugin does **not** rely on that (Carbon compatibility) - private access goes through cached reflection.
4. `BaseMountable.DismountPlayer()` asks the `CanDismountEntity` hook too - blocking that hook would also block the plugin's own seat swaps.
5. `OxideMod.Version` is static in this Oxide version.

## Runtime rules learned (the real plugin follows them)

1. **Antihack InsideTerrain** (`AntiHack.ValidateAgainstTerrain`): every 64 frames per player (`antihack.terrain_timeslice`), a player whose
   position is more than 0.3 m under both the terrain heightmap and the terrain mesh (`antihack.terrain_padding`, terrain holes excepted)
   gets +100 violation (`terrain_penalty`) **and** is killed (`terrain_kill`, damage type Suicide); above 100 the player is kicked.
   The Oxide hook `OnPlayerViolation` cancels the violation (no kick) but **not** the kill - the kill runs on its own.
   A mounted player is parented to the seat with local position zero, so the body is exactly at the seat's position.
   -> rule SPEC N6: the seat (= the body) stays >= 0.15 m above the terrain; jump arcs are clamped to the surface. BmSpike 0.1.1 does this.
2. **Clean takeover of an animal.** `FSMComponent.SetFsmActive(false)` only stops the ticking - the current state keeps its effects
   (a livestock animal keeps its networked "grazing" flag = head down, a running montage keeps playing). That is why the cow and
   sheep looked broken in session 1 (both were grazing). Correct order: `fsm.SetState(new State_Nothing { Name = "BmParked" })`
   (runs the current state's own `OnStateExit`: montage stopped via `StopServerAndReturnToPool`, consume flags cleared), then
   `SetFsmActive(false)`; for livestock also `SetGrazing/SetDrinking/SetMating/SetResting/SetLyingIn/SetSleeping(false)`; clear the
   NPCNetworking "alert" flag (Reserved6). Giving the AI back = `SetFsmActive(true)` + `SetState(previous state)`.
   Livestock networked flags: Leading R7, Grazing R8, Pregnant R10, Sleeping R11, Mating R12, Drinking R13, Resting R14, Frisky R15,
   Leadable R16, LyingIn R17. NPCNetworking: swimming R1, jumping R2, carrying/aiming R3, relaxed R4, crouching R5, alert R6, plus the
   head look direction - all only updated while the FSM ticks, so the controller drives the look direction itself.
3. **Keys.** A `bind` sent by the server is ignored by the client (session 1, sent 4 times, no reaction). The vanilla binds already send
   server commands for F (`inventory.lighttoggle`) and X (`vehicle.swapseats`); `OnServerCommand` catches them. G (`+markcurrentpos`)
   is probed in session 2 (`OnMapMarkerAdd` / pings). An unbound key (F5) sends nothing - menu by `/bm` or a manual bind (SPEC 17 #2).
4. **Water (SPEC R2.14).** The horse measures `WaterLevel.Factor(body bounds)` = share of the body height under the surface; damping
   grows with it and when swimming (factor > 0.5 off the ground, or > 0.65) it is forced down to walk. The controller uses the same
   factor: speed x lerp(1 -> 0.35) between 0.1 and 0.65, walk only above 0.5; brown bear and crocodile exempt.
5. **Calm spawn.** An animal parked 0.3 s and again 2 s after spawn stays parked (CALMTEST below) - the FSM start-up does not undo it.

## Session 1 in game - 2026-10-09 evening (BmSpike 0.1.1)

| Letter | Your report | Server log | Result |
|---|---|---|---|
| A | bear "sort of worked" | rode the bear at gaits 1-5 (up to 8 m/s), tried `/sp seat 0 1.6 0` and `/sp speed 1.5` | ⚠️ details asked in session 2 |
| B | - | `/sp clips`, `play` (idle, sleeping), `attack` on the bear | ⏳ look/sound not reported |
| D | third person: lying on the belly on the board | seat `boogieboard.deployed`, pose `SitSummer_BoogieBoard`, camera ThirdPerson | ❌ for riding |
| E | binds did not work | 4x "sent: bind f/g/x", no `sp.attack` / `sp.cam` / `sp.release` afterwards | ❌ server binds are blocked |
| F | - | all 10 gestures reached `CanUseGesture` while riding and were turned into a bear clip | ✅ server side |
| G | cow and sheep: animations totally broken; probably the rest too | cow and sheep ridden at gaits 1-5; tiger mounted briefly | ❌ -> rule 2 above, fixed in 0.2.0 |
| - | - | killed 4x by spawned bears and 1x by the spawned tiger | -> calm spawn in 0.2.0 |

## Server-side selftest 0.2.0 - 2026-10-09 ~20:55 (centre spot)

All 18 species: parked cleanly (`SetState` -> `BmParked`), moved ≈ run speed x 1 s, attack clip played, jump + warp, AI given back
(states after: Roam, Flee, Graze, KickBehind, Headbutt, attack, roam, Deactivate for critters). No exceptions.
CALMTEST (spawned like `/sp spawn`): bear, wolf2, tiger, cow - state `BmParked` 8 s later, cow not grazing, moved < 0.5 m.
A first run at a map-edge spot (-872, 27, 1034) lost the 7 wild species and the chicken before the test (destroyed within 1.5 s,
livestock unaffected); the selftest now uses the player spawn spot near the centre.

## Dev environment incident - 2026-10-09 19:48

| | |
|---|---|
| Symptom | The first join ended on the loading screen: "Disconnected (InsideTerrain Violation Level 200)". |
| Cause | The 1500 m test map (seed 4242) has no valid beach spawn point. `ServerMgr.FindSpawnPoint` then logs "Couldn't find an appropriate spawnpoint for the player - so spawning at camera" and uses (0, 0, 0), 4.8 m under the ground: antihack killed the new player, the respawn landed at (0, 0, 0) again and the second violation (200) kicked the player. Not caused by BmSpike. |
| Fix (dev only) | `tools/devplugins/DevSpawn.cs` answers the `OnFindSpawnPoint` hook with one of 5 dry, flat, open spots near the map centre (no rock/tree on top, slope <= 20°, 60 m from monuments, not in water). Server console `devspawn.test` lists the spots and calls the game's own spawn search: it now returns (19.69, 3.26, 30.14), insideTerrain=False. |
| Wespner | Not affected - a normal map has beach spawn points. |

## Server-side selftest (sp.selftest) - 2026-10-09 19:5x

All 18 species: spawned on the navmesh, AI suspended, moved ≈ run speed x 1 s, attack clip played (where one exists), jumped 3 m and re-anchored with `WarpToWorldPosition` (panther: landing off the mesh), AI resumed. No exceptions.

## Session 2 in game - 2026-10-09/10 (BmSpike 0.2.0)

| Topic | Your report | Server log / finding | Result -> 0.3.0 |
|---|---|---|---|
| Keys | F, G, X | on foot: F = `inventory.lighttoggle`, **G = map marker** (`OnMapMarkerAdd`), X = `vehicle.swapseats`; all three caught while riding | ✅ |
| Third person | camera behind the rider (screenshot) | `PlayerFlags.ThirdPersonViewmode` works for a normal player | ✅ riding; rider pose bad (below) |
| Riding pose | rider too high, twisted and deformed | horse saddle seat (pose Horseback) on a cow | ❌ -> straddle seat `snowmobilepassengerseat` (free hands), height from the spine bones |
| Attacks | work, rear attack works; no damage | F -> headbutt / kick clips | ✅ look; damage added in 0.3.0 (the game's own attack numbers) + gathering |
| Gestures | work | two gestures picked a cow death clip ("death" contains "eat") | ✅ filter fixed |
| Head | cow stared sideways while running | look direction followed the camera without limits | -> slow follow, clamped by speed |
| Ctrl / turns | Ctrl must brake like the horse; bull slide turn; turning not natural | Ctrl stepped the gait down; fixed 150 deg/s turn | -> brake, S-at-speed 180 turn with the `turnCantering` clips, agent turn rate with momentum |
| Idle | idle animations wanted when standing | none played | -> idle clips every 8-14 s |
| Play as animal - first person | own body (knees) visible | hidden chair, pose SitGeneric, eye 1.33 m above the seat | ❌ -> body inside the animal + self-hide experiment |
| Play as animal - chase | "great view", a bit closer; own knees, shadow and held item visible; camera turns around the hidden body behind the animal | first-person camera on a seat behind the animal | ❌ -> third person with the pivot inside the animal |
| Play as animal - orbit (board) | lying under the cow on the board; cow floating | board seat under the cow | ❌ dropped |
| AI | a bear attacked the hidden human (camera) instead of the cow | Gen2 senses target any non-NPC player | -> `SimpleAIMemory.PlayerIgnoreList` + `CanBeTargeted` |
| Crab | turned to the side | body yaw = direction of travel | -> per-species yaw offset (`/sp yaw`) |
| Water, bear details, tp on foot | not tested | - | session 3 |

Facts found for 0.3.0: Gen2 `SenseComponent.CanTarget` skips players in `SimpleAIMemory.PlayerIgnoreList` (also used by Bradley, patrol helicopter, boats, bees); `State_Attack` (Damage, Delay, DamageType) and `State_ShovingAttack` (+ hitRange, hitAngle, pushForce) apply damage with `BaseCombatEntity.OnAttacked(amount, type, attacker, ignoreShield)`; `ResourceDispenser.DoGather` gathers without a tool from the hit's damage (destroy fraction 0.5); entities can be parented to a bone (`SetParent(entity, boneName)`); `PlayerFlags.Ragdolling` is only a model-state flag on the server (the client hides the body while ragdolled - tested in session 3 as "self-hide").
