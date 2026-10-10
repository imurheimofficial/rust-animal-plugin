# Spike session 3 - in-game checklist (dev server, BmSpike 0.3.0)

Join: F1 -> `client.connect 127.0.0.1:28015`. Spawned animals are calm (`/sp wild` gives one its own AI).

## A. Playing as the animal - is the human gone? (most important)

1. `/sp spawn cow`, look at it, `/sp play`. You start in **first person** from the cow's eyes, self-hide ON.
   - Do you still see your knees, hands, held item or your shadow?
   - Can you still look around, move, attack (F)?
2. **G** -> third person: the camera should turn around **the cow**. Any part of the human or its shadow visible? Too far / too close?
3. `/sp selfhide off`, then G twice: what changes? (Tells me what the self-hide trick does.) `/sp selfhide on` again.
4. Stand next to a crate or a door in first person: does the crosshair offer the interaction? Open it.
5. `/sp spawn bear wild` about 20 m away and wait: the bear must go for **the cow**, not for an invisible spot. Let it hit you: your health bar = the cow's health. (If the cow dies, you die - that is intended.)
6. X = back to human.
7. Repeat 1-2 with `wolf2` and `chicken` (small animals hide the human worst).

## B. Riding

8. `/sp spawn cow`, `/sp ride`. New straddle seat: height and pose OK? Twisting gone? Fine-tune: `/sp seat 0 1.2 0` (x y z) and say which numbers look right. Old seat for comparison: `/sp seatprefab saddle`.
9. Turning with A/D at walk and at full speed - natural now?
10. **Ctrl** at full speed = brake (hold A or D while braking = stop-and-turn clip). **S** at full speed = 180 turn (bull slide turn).
11. Stand still 15 s: idle animations? Head: does it slowly follow your camera, and stay forward while running?

## C. Attacks with damage

12. Attack a tree, a stone/ore node, a barrel, a corpse and a calm animal with F (cow: headbutt, look back + F = kick). Wood/stone in your inventory? Barrel breaks? Animal hurt?

## D. Crab

13. `/sp spawn crab_single`, `/sp play`. Walking direction right? Try `/sp yaw 0`, `/sp yaw -90`, `/sp yaw 90` and say which is correct.

## E. Water and on-foot third person (left over)

14. `/sp water`, ride a cow into the water: slower with depth? Brown bear (`bear`) not slower?
15. On foot `/sp tp on`: third person on foot? `/sp tp off`.

Report per letter (A-E): OK / what was wrong (+ screenshots).
