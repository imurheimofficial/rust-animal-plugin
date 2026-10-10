# Test of build 2.0 (everything written so far)

The dev server runs `dist/Beastmaster.cs`. You are server owner there and have `beastmaster.admin`.
Join: F1 -> `client.connect 127.0.0.1:28015`. Start the server with `C:\RustDev\tools\start-server.ps1` if it is not running.

Parts 1 and 2 are the camera/pose checks that only a real client can judge (everything else below was also run headless on the server and passed).
Tell me per numbered part: OK, or what looked wrong (screenshot + the exact command).

## 1. Riding (first and third person like the horse)
1. `/bm spawn cow` -> look at it -> `/bm ride`. First person like on a horse; **G** = third person; W/A/S/D, Shift, Ctrl, Space, F, X.
2. Pose wrong? `/bm seat 1` (horse saddle) or `/bm seat 0` (straddle).
3. Real flow: `/bm spawn bull 1 tamed saddled`, look at it, **E** mounts.

## 2. Playing as an animal (the human invisible in BOTH views)
1. `/bm spawn bear` -> look at it -> `/bm form`. Third person: no body, no shadow, no held item. **G** = first person: still nothing of you.
2. Mouse = look around, after 3 s without moving it the camera glides back behind the animal. Presets if needed: `/bm preset A|B|C|D`.
3. Feet on the ground on hills, water slows (not the bear), steep slopes can make a running animal fall.
4. F = attack (`cow 450>415` in chat), look back first = rear attack, B = gestures, X = back (smoke).
5. **E** at a door / crate with the head in front of it: opens (third person).
6. The small panel at the bottom left: Health / Stamina / Hunger / Thirst. Hold Shift to run: stamina falls.
7. **Eating** (E, nothing else in front): bear on grass = graze, at water = drink, at a corpse = eat; drop corn (`give`-style: drop it on the ground in front) = eat it. A wolf does not eat corn, a cow does not eat meat (chat tells). Inventory: eat/drink items in the inventory = they feed the animal.
8. Leading: tame a sheep (`/bm spawn sheep 1 tamed`), as a bear/cow stand with the head at it, **E**: it follows you in a line behind (up to 4), **E** again lets it go.

## 3. The menu
`/bm` opens the window (again `/bm` closes it). Pages: Home, Help (15 topics, search box, "What I'm looking at" button on an animal), My animals (follow / wait / guard / release per animal), Controls (view, camera preset, switches), Transformation (status). The control layer (you see it as admin): Admin (plugin on/off, every feature, every species), Spawn (species, amount, wild/tamed, saddled), Players (grant/revoke menu access).

## 4. Taming and gear (livestock, predators, deer, wolf)
* **Cow / bull / sheep / ram:** stand near a wild one for 5 minutes (`Taming.BondSeconds`, set it lower in the config for the test), press **E**.
* **Wolf:** lit torch, chase it for 60 s -> it howls and follows.
* **Bear / tiger / panther / boar:** Blowpipe + Incapacitate Dart (slowed), Bear Trap (it springs), 3 Medical Syringes with **E**.
* **Stag:** dart (sleeps 60 s), **E** opens its inventory, 3 potatoes + saddle in the last slot.
* Owned animal: **E** opens the gear panel (slot 1 saddle, 2 horseshoes, 3 saddle bag), **E** again mounts. Commands: `/bm cmd follow|wait|guard|attack|release`, a Bone in hand, or the "My animals" page.

## 5. Horses
* New wild horses are born **without a saddle**; `/bm spawn horse` makes one (wild). `bm.horses list` shows the temperament (calm / shy / aggressive).
* Calm: stand near it, **E** after the bond time. Shy: it flees when you come closer than 10 m and calms 15 s later. Aggressive: charges you, rears up and strikes; put it to sleep with the dart, it wakes calm.
* Tamed horse: its normal inventory (equipment slot) takes the saddle; the saddle gives the seats. F while riding = rear-up strike, Space = small jump.
* Leading with the rope: walk away from it past 7 m - it keeps following you on an invisible tether up to 50 m and takes the rope again when it is near.
* `bm.horses set shy|aggressive|calm` (look at a horse) and `bm.horses sleep` help testing.

## 6. The ritual of transformation
1. `/bm spawn bear`; kill it yourself (not a test kill by console). The chat says the body stays 10 minutes.
2. Hold a **lit torch**, walk around the body 3 times (within 6 m). On the third circle it burns.
3. Stand next to the fire and press **E**: -50 % of your health, bleeding, smoke - you are the bear (same look), full animal health.
4. X returns (smoke). Cooldown 2 minutes. Harvesting the body is blocked while it is eligible. Rabbit/squirrel/frog/crab leave a stand-in body.

## 7. Wild behaviour, relations, bosses, climate, spray
* Hit a wild stag: it always fights back. Hit a cow/sheep/horse: about 30 % fight back, the rest flee.
* As a bull (`/bm form` on a bull): cows follow you. As a stag: a bear attacks you. As a wolf: wolves keep within 50 m.
* `bm.boss spawn horse|bull|ram|stag`: 4000 HP, 99 damage, fire, effects around it, **25 % bigger**. Kill it: scrap + a Boss Saddle; E on the next boss while carrying the saddle tames it; then ride it: **the seat must be at the right height on its (higher) back** and you must not look enlarged - tell me if the height is off (`Boss.Scale` in the config).
* Polar bear form outside the snow without a water jug in the inventory = slower and weaker (a chat warning). Other forms in the snow without cooked meat: the same.
* Spray Can on an owned cow/horse: its look is rolled again (only the look).

## 8. Honest list of what is not in this build
Horse **form** (becoming a horse), breeding of animal forms, transport boarding, saddles/passengers on somebody else's animal form, leading a transformed player on a rope, glowing boss eyes and boss textures (the server cannot do those), the F5 key (the server cannot bind keys). See `docs/STATUS.md`.
