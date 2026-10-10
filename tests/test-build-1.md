# Test of build 1.0.0 (production plugin on the dev server)

The dev server runs the real plugin `dist/Beastmaster.cs` now (the spike plugin is gone). You are server owner there and have
`beastmaster.admin`. Join: F1 -> `client.connect 127.0.0.1:28015`.

## 1. Riding
1. `/bm spawn cow` (spawns where you look) -> look at it -> `/bm ride`. You sit on its back, first person like on a horse.
2. W/A/S/D, Shift = faster, Ctrl = brake, S while running = 180 turn, Space = jump, F = attack, **G = third person** (camera behind), X = get off.
3. Pose looks wrong? `/bm seat` lists seat models, `/bm seat 1` = the horse saddle model, `/bm seat 0` = the straddle seat.
4. Saddled animal with E: `/bm spawn horse`... for other animals `/bm spawn bull 1 tamed saddled`, look at it, press **E**.

## 2. Playing as an animal (the human must be invisible in both views)
1. `/bm spawn bear` -> look at it -> `/bm form`.
2. Third person (default): the camera follows the animal. Move the mouse to look around; after 3 s without moving it the camera glides back behind the animal.
3. **G** = switch to first person (from the animal's head). Both views: no body, no shadow, no held item - only the animal.
4. Camera presets if something looks wrong: `/bm preset A` (default), `B` (with the game's third-person flag), `C` (camera at the head), `D` (first person).
5. F = attack (the animal turns to the nearest target; look back first = rear attack), B = gestures, X = back to human (smoke).
6. Feet on the ground? Walk up and down hills, into water (slows down, bear does not), run down a steep slope (may fall).

## 3. Attacks must hurt
`/bm spawn cow` + `/bm form` as a bear, attack the cow: the chat shows e.g. `cow 450>424`. Also try a tree, a stone, a barrel.
(Headless check already done: bear bites cow 450 -> 424.)

## 4. Interactions from the animal (third person and first person)
Walk the animal up to a door or a crate and press **E** with the animal's head just in front of it: the door opens / the crate opens.

## 5. Report
For each numbered part: OK, or what looked wrong (screenshot + the exact command you used).
