# Build status against the specification

Build 1.0.0 (2026-10-10), `dist/Beastmaster.cs`, 12 000 lines, 13 modules. Written and tested against Rust build 25823813 with Oxide 2.0.7820.

Legend: **T** = built and run headless on the dev server (result in `bm.test ...`), **B** = built, compiles, needs a real player to judge, **P** = partly built, **-** = not built (reason given).

`bm.test` headless results at the end of the build: combat (bear bites cow 450 -> 424), tame (record, follow, release, persist), ritual 12/12, menu 82 windows / 2680 buttons valid, horse 11/11, wild 6/6, boss 8/8, spray 3/3, diets 12 pairs.

| Spec section | State | Notes |
|---|---|---|
| 1 Species roster | T | 20 of 22 species exist in the build (no doe prefab, the snake is a hazard entity, not a creature). The table of speeds is the default; authored gait speeds are used (`Movement.SpeedMode`). |
| 2 Riding (steering, gait ladder, brake, slide turn, water, slopes) | B | Straddle seat, first and third person, camera presets A-D, recentre in 3 s. Needs your eyes. |
| 2A Jumping | B / T | Gen2 animals: warped-clip jump (B). Horse: velocity impulse (B). |
| 3 Gear (saddle, bag, shoes) | B | Gen2 animals: a virtual gear panel (slots 1-3 + bag), speed bonus. Horse: the game's own equipment slot, a saddle there gives the seats (T). |
| 4 Combat (F, rear attack, gather, damage) | T | The game's own damage; trees, ore, corpses gather. Horse: rear-up + strike (A14, config `Horse.RearUpAttack`). |
| 4A Stimulus-driven reactions and sounds | P | Gestures play the species' clips, attacks and pain use the real clips. Hesitation on descents and the full stimulus table are not built. |
| 5 Taming (bond, torch, predator, sleep-and-feed) | T / B | Headless: record, follow, release, persistence, horse bond / dart. Dart + trap + syringe, wolf torch, deer inventory: B. |
| 6 Commands (follow, wait, guard, attack, release; gestures, bone, menu, chat) | T / B | Animals T, horses T (follow / wait / attack by velocity driver). |
| 6A Gestures on foot command animals | B | |
| 6B Lead: 50 m limit | T / B | Livestock rope: the game's convar raised to 50 m (T). Horse: invisible tether beyond the game's 7 m (B). |
| 7 Relations between animals | T | Rules in the config; bear vs stag, cows follow a bull, wolves tethered, bull vs bull. |
| 8 Horse AI (temperaments, shy / aggressive, sleep) | T | Shy flees and calms in 15 s, aggressive charges and strikes, dart sleeps 60 s and wakes calm. Wild horses are born without a saddle. Existing horses are never touched. Grazing and water needs of wild horses: not built (they wander a little). |
| 9 Ritual of transformation | T / B | Circles, protected body, stand-in bodies, same look (T). The final step (E at the fire) needs a real player (B). **Not for the horse.** |
| 9B Others handle the transformed player (milk, shear, saddle, passengers, rope) | - | Not built: the transformed player's animal is parked by the plugin; the game's own milk / shear / rope actions on it were not wired. Gear panel / saddle / passengers on a form animal: not built. |
| 9C Eating, drinking, stats | T / B | Diets and food values (T); panel, bites, inventory food and medicine act on the animal (B). |
| 9D Leading tamed animals while transformed | B | E at a tamed animal of yours / your team: it follows in a line (up to 4), invisible lead. |
| 9E Quitting as an animal | B | The animal sleeps where it stands; on reconnect the form continues. |
| 9F Small creatures, crocodile, crab | P | Controls through the same driver; critters have a call and a tiny nip instead of an attack. |
| 9G Doors, vehicles, transport | P | Doors, loot, build, craft (B). Driving and riding are refused. **Transport boarding: not built.** |
| 10 Camera | B | The server cannot change the game's third-person camera (no wall collision, parallax) - see SPEC 19.4. |
| 11 Spray can | T | Horse breed / livestock coat and markers rolled again, gameplay genes untouched. |
| 12 Menu and Help | T | Home, Help (15 topics, search, species cards, "what I'm looking at"), My animals, Controls, Transformation, Admin, Spawn, Players. F5 itself cannot be bound by a server (`/bm` or `bind f5 "bm.menu"`). |
| 13 Permissions and commands | T | See INSTALL.md. |
| 14 Config and data | T | Config, data file, lang file; lists tidy themselves; reload / unload safe. |
| 19 Being the animal (invisible, untargetable, HP link, climate, defence) | B / T | Defence 30 % (T). Climate (B). Spectator trick for the invisibility in both views (B, needs your eyes). |
| 20 Boss animals | T / B | 4000 HP, 99 damage, fire immunity, burning hits, effects, scrap with the group bonus, boss saddle that tames, mini boss from the offspring of a tamed boss (the last two B). Bigger body: 1.25x (T: scale networked, seats keep normal size, the bigger horse still walks); riders sit at the scaled height of the back (B, needs your eyes). **Not possible on a server:** glowing eyes, textures; the black body exists only for the horse (the game's breed). |

## Not built, with the reason

| Item | Why |
|---|---|
| Horse form (becoming a horse) | The horse is a physics vehicle with its own seats; the animal-form driver is written for the navigation-mesh animals. It needs its own driver and camera rig. |
| Milk / shear / saddle / passengers / rope on a transformed player | Needs the game's own interactions to accept the plugin's parked animal; cannot be judged without two clients. |
| Breeding of animal forms | The game's mating state machine would have to be driven for two controlled animals. |
| Transport boarding | Needs the flatbed module and its loading spots. |
| Glowing eyes and textures of bosses | A server cannot change textures or colour eyes. (The size is built: 1.25x, `Boss.Scale`.) |

## Things only a real player can confirm (your test)

Camera and pose (riding views, form invisibility in both views, presets), interactions from the animal's head (E), the gear panel, the final step of the ritual, the lead chain and the horse tether, the HUD, climate and needs with a real inventory, boss hunting and the boss saddle, the look of the menu.

## Final build (2026-10-10)

dist/Beastmaster.cs (~15k lines). **Delete oxide/config/Beastmaster.json when upgrading** (old defaults would stay).

Done and tested on the dev server: death of parked/followed animals (own dead state), attacks of every species, ritual flame ring, scare/defend, boss spawn with eye lights.
Done, untested in game: horse form, 10/50-lap silent ritual, camera factors (`/bm cam up|down|reset|save`), key hint cells, F5/H binds, night vision goggles, gestures, riders after feeding (2 / 1), leash (Ctrl+E), milk/shear/lead via trust credit, `/bm breed`, menu (bosses, Aim tools, rename, `/bm run`), sharp turns, frog on water.
Eyes: two invisible narrow cinematic spot lights per boss on the eye bones (config Boss.Eyes). Coat: darkest genes for bull/ram, black horse breed.
Inventory: a panel on the game's menu layer (visible only while the inventory is open) covers the player model and avatar with the animal: set a PNG address per species in Inventory.Images and the box offsets (ModelMin/Max, IconMin/Max) to fit your screen.
Transport: through the game's own lead + livestock flatbed module (trust is credited to nearby players).

## Version 1.1.0 (2026-10-10) - audit

The whole code was reviewed against the owner's requests; 23 bugs and unfinished parts were fixed. The list with the cause and the fix of
each one, the limits a server cannot get past and the test list are in [AUDIT.md](AUDIT.md).
The inventory picture (the panel over the player model) described above was removed at the owner's request; its config section is gone.
