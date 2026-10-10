# Plan - how the plugin gets built, and how it earns "no errors"

Status: **PREPARED. Nothing below has started. The first step runs only when you say START.**
Companion documents: [SPEC.md](SPEC.md) (what), [RESEARCH.md](RESEARCH.md) (what is proven).

## 0. The honest promise

No one can promise a program with zero bugs. What this plan gives you:

1. **Zero known errors at hand-over**, each claim backed by a test result I can show you.
2. **Nothing built on a guess.** Every mechanism the engine decides (not me) is proven in a small test first (a *spike*) with a written pass/fail result.
3. **Compiled against the real server code** (build 25823813) before anything runs, so wrong class names and wrong signatures cannot slip through.
4. **A self-test inside the plugin** (`bm.selftest`) that re-checks every class, prefab, item and hook it needs, so a future Rust update that breaks something is visible in one command instead of silently.
5. **Three honest limits**, already listed in SPEC section 17, are the only places where the literal wording of the brief cannot be met: the ram head-butt on a horse model, the visual speed ceiling above the animations' authored gaits, and third person for an ordinary on-foot player (which gets a real attempt, and a clear verdict if it fails).

## 1. Environment

### Already prepared on this PC (nothing was installed into the game or Windows)
| Item | Where |
|---|---|
| Rust Dedicated Server, build 25823813, vanilla | `C:\RustDev\server` |
| Pristine copy of the managed DLLs | `C:\RustDev\orig-managed` |
| Oxide 2.0.7820 and Carbon (production build) release files | `C:\RustDev\oxide`, `C:\RustDev\carbon` |
| ILSpy 9.1 + decompiled game code (original and Oxide-patched) | `C:\RustDev\tools\ilspy`, `C:\RustDev\decompiled\...` |
| .NET SDK 9 | already on the PC |
Total about 6 GB, deletable any time.

### Added at START (first hour)
1. Copy Carbon into the dev server (a second copy with Oxide is kept for the Oxide-compatibility run).
2. Local test server: tiny map, seed fixed, `server.secure` off, your PC at `localhost`. **Your Rust client must be updated to the server's build first** (Steam does it; yours is one build behind).
3. Compile harness: a project that references the server's DLLs and the framework DLLs, so `dotnet build` reports every error exactly like the server would.
4. Build script: `src/*.cs` modules -> one `dist/Beastmaster.cs`.
5. A throwaway **dump plugin** (dev only) that writes: every animal/variant prefab path, every mountable prefab with camera mode and pose, per-species gait speeds, per-species clip lists, the default gesture set, all relevant item shortnames. These files fill the species registry with real data instead of guesses.

## 2. Spikes - proof before building

Each is a small throw-away test with a pass criterion. Results go into `docs/SPIKES.md` (written during Phase 0). "Eyes" = needs you to look/listen in the game; I tell you exactly what to do and what to report.

| ID | Question | Pass when | If it fails | Eyes |
|---|---|---|---|---|
| S0 | Does a hello-world plugin compile and load on Carbon and on Oxide? | Loads, command answers, no errors | fix environment | no |
| S1 | Can the horse's saddle seat prefab be fitted to bear, wolf, tiger, panther, boar, stag, cow, sheep and a player mount and ride along? Pose, saddle picture? | Player sits on the moving animal in a sane pose | other seat prefab / offsets per species | yes |
| S2 | Best way to steer a new-generation animal: `agent.Move` vs `SetDestinationWithParams`, AI suspended, agent paused. | Smooth, no teleport/desync, respects terrain and water; climbs hills up to 45° and steps up to 0.4 m like the AI animals (measured on test hills) | alternative strategy | yes |
| S3 | Gaits, real speeds per species, shoe bonus formula from the horse code, how bad the sliding is at the brief's speeds. | Per-species visual cap chosen | cap lowered, documented in Help | yes |
| S4 | Clip catalog per species; play attacks (front, cycled, rear), calls, stomp, idles, hurt, fear; horse rear-up; is there sound? | Each species has a verified list of clips that play with sound | nearest clip, documented | yes |
| S5 | Suspend and resume AI/needs without side effects (livestock starvation, population cull, despawn); force the sleep state and hold it. | Animal ridden for 10 min, released, behaves normally; sleep holds | adjust | no/yes |
| S6 | Possession: hidden human body, invisible driver seat (a seat that allows wielding and weapon use), health link, damage redirect, death, night vision, passengers; inventory and backpack usable, any item can be held and used (weapon, tool, food, plan, deployable), item drop on death. | Others see only the animal; death sync correct; inventory opens; held items work and **none is visible to a second client** | alternative hide method | yes |
| S7 | Camera: list mountable prefabs by camera mode; third-person for riding/animal form by seat swap; **on-foot third person candidates A/B/C** (SPEC R10.3). | A mode that works for mounts; verdict for on-foot | mounts first-person only, on-foot reported as not achievable with evidence | yes |
| S8 | Server-sent key binds: are `bind` commands accepted? restore the defaults? Which input flags does the server see? | F, F5, G, X reach the plugin | menu + commands + vanilla buttons only | yes |
| S9 | Gear panel: container, item filters, saddle picture on non-horse, double seat, bag, persistence. | Gear fits, persists | in-menu indicator | yes |
| S10 | Taming parts: dart hit detection and slow status, bear-trap spring and lock, syringe counting, torch chase timer, forced sleep, feed-into-container. | Each part works with a real player | per-part alternative | yes |
| S11 | Gestures: how `CanUseGesture` fires, which gestures every player owns, suppressing the human gesture, mapping to animal clips. | Gesture -> animal action works for riding, animal form, commands on foot | Bone item/menu | yes |
| S12 | Variants: `OnEntityReskin`, horse `SetBreed`, livestock `Genes` bit layout (cosmetic bits only), look transfer from corpse to puppet. | Spray re-rolls looks only; ritual copies looks exactly | skip spray on livestock | yes |
| S13 | Horse: AI with synthetic input, temperaments, sedation and the best sleep visual, hidden driver + double-saddle passenger, lead beyond 7 m. | Wild horses roam, graze, flee/attack/calm per spec | simpler AI | yes |
| S14 | Persistence: which animals the game saves, re-creating tamed/sleeping ones after a restart, offline animal body rules. | Restart keeps everything | re-create from records | no |
| S15 | Handling by others: milk trust credit, shearing, vanilla lead with 50 m break, invisible lead column, passenger on a player-controlled puppet. | All work as in SPEC 9B/9D/6B | per case | yes |
| S16 | Eating/drinking: grass/water/item/corpse detection, `SetNeedValue`, trough/barrel, stats HUD. | Stats move as specified | simplified diet | yes |
| S18 | Jumping: the game's warped root-motion jump on wolf, tiger and panther (trim to a short jump?), the three candidate looks for the other species (SPEC R2A.2), `IsJumping` flag effect, landing validation, horse physical jump under the ragdoll limit, panther +25 % and frog x3. | A jump that looks right and lands safely for every ridable species | per-species fallback, documented | yes |
| S17 | Doors, vehicles, transport, looting, building (plan, hammer, deployables; held items invisible) and crafting in animal form: door open/close from the hidden seat (lock rights), fit through door gaps, refuse driver seats and animals, board the flatbed (`MountImmediate` for livestock forms, own anchors for other species), ride along while another player drives, safe dismount. | All of SPEC 9G works | per case | yes |

Spike order: S0 -> S1, S2, S4 (the riding core) -> S3, S18, S8, S9, S11 -> S6, S7 -> S5, S12, S14 -> S10, S13, S15, S16, S17.

**Design constraint found while preparing:** a *spectating* player cannot interact with the world (the game's `CanInteract` is false while spectating), so a spectate-based camera is **not usable for animal forms** - doors (SPEC R9.38), eating, milking and boarding all need interaction. Animal forms therefore use the seat-based hidden body (S6). Spectate-based third person stays a candidate only for the plain on-foot player (S7, candidate A), where interaction can be handed over to the proxy.

## 3. Build phases

Each phase ends the same way: clean compile, `bm.selftest` green, dev-server run on Carbon **and** Oxide, a written in-game checklist that you run, fixes, then the next phase. Nothing moves on with a known failure.

| Phase | Content | SPEC |
|---|---|---|
| 0 | Environment, dump plugin, all spikes, **Spike report for your review** | - |
| 1 | Foundation: species registry (from the dump), config/lang/data/permissions, access commands, menu skeleton with the Help framework, **Spawn tool** (so every later test is easy), selftest, build script, `dist/` | 0, 12, 13, 14 |
| 2 | Riding core: seats, steering, gaits, shoes, gear, attacks, reactions and sounds, gesture actions, key binds, third person for mounts | 2, 3, 4, 4A, 6A, 10 (mounts), 16 |
| 3 | Taming and companions: bond, torch, predator, sleep-and-feed, commands, relations, leads (50 m), spray | 5, 6, 6B, 7, 11 |
| 4 | Horse AI: temperaments, sedation, free roaming | 8 |
| 5 | Transformation: ritual, same look, hidden body, stats and eating, handled by others, invisible lead column, sleep on logout | 9, 9B-9E |
| 6 | Third person for the on-foot player (per S7), full Help text, polish, final acceptance, `INSTALL.md`, upload-ready file | 10.3, 12.4 |

## 4. Quality gates

1. **Compile against the real DLLs**, warnings treated as errors in my own code.
2. **Hook discipline:** every hook null-safe, returns `null` unless it truly owns the decision, wrapped so an exception is logged once and never escapes.
3. **Unit tests for pure logic** (gait ladder, speed/shoe maths, circle-around-corpse detector, relations matrix, config validation, diet tables, lead chain spacing) that run without a game.
4. **Dev-server smoke run** (scripted): load, config creation, commands, spawn, AI takeover on NPCs, unload cleanly with nothing left behind.
5. **In-game acceptance checklists**, one per phase, in plain steps ("do this, you should see/hear that"); you report; I fix.
6. **Self-test** command and a **version guard** at load.
7. **Performance check** with 50 animals and 10 riders on the dev server (SPEC N1).
8. **Oxide and Carbon** both run the same file.

What I cannot see: your screen and speakers. Anything about looks, sound, camera feel and animation needs your eyes in the acceptance steps. That is why they are written as exact steps.

## 5. Deployment to wespner.eu

1. You receive `dist/Beastmaster.cs` (one file) and a short `INSTALL.md`.
2. Upload to `oxide/plugins/` (Oxide) or `carbon/plugins/` (Carbon). The server compiles it; config, language and data files are created by the plugin on first load.
3. Grant permissions (`oxide.grant` / `c.grant`, or the `bm.grant` commands from the console).
4. Run `bm.selftest` once; it prints a green/red report.
5. If anything is red, paste the console lines to me; I fix and send a new single file.
I never need your server password or access; you do the upload.

## 6. Risks (the real ones)

| Risk | Why | Mitigation |
|---|---|---|
| New animal layer not designed for rider control | The AI/navigation is new and complex | S1, S2, S5 first; fall back between steering strategies |
| Animation sliding at the brief's speeds | Clips authored at fixed gaits | S3 measures a cap; visible in Help; config |
| Third person for on-foot players | No vanilla mechanism known | S7 candidates; clear verdict |
| Hidden-body possession edge cases | Disconnects, deaths, passengers, restarts | S6, S14, explicit rules 9E |
| Horse is a physics vehicle, not an NPC | Different control path, 7 m rope limit | S13 synthetic input, own tether |
| Game updates break a call | Rust updates weekly | selftest + version guard + fast re-spike |
| Client key binds refused | Server-sent binds may be restricted | menu + commands + vanilla buttons always available |
| Size and complexity of one file | ~10 modules | modules in `src/`, one-file build, tests |

## 7. What I need from you at START

1. Say START (and the working name, or keep `Beastmaster`).
2. Update your Rust client (Steam), then keep Rust closed until I ask you to join `localhost`.
3. Tell me which framework (Oxide or Carbon) and OS the wespner.eu server runs (console: `version`, then `oxide.version` or `c.version`) - not blocking. You said the Rust build there is the latest one, which is what the dev server uses; the framework is the open part.
4. Confirm assumption **A14** (horse attack = rear-up + forward strike) and glance at the other proposals in SPEC section 18.
5. Be around for short in-game tests (each is a few minutes; I give exact steps).

## 8. Settings checklist before START

- Keep the strongest model with high reasoning for the whole job (this is long, mechanism-heavy work).
- `/fast` off.
- One multi-step workflow with several agents is not useful for the spikes (one test server, one game client). It can help later for independent modules; I will propose it only if you want.
- Allow background tasks and PowerShell prompts: I start and stop the dev server and the build.
- About 1-2 GB extra disk for the Carbon and Oxide copies of the server.
