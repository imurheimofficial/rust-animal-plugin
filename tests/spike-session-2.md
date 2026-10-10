# Spike session 2 - in-game checklist (dev server, BmSpike 0.2.0)

What changed after session 1:

| Session 1 finding | Change in 0.2.0 |
|---|---|
| Cow and sheep animations completely wrong | The animal's AI now leaves its current activity through the game's own code before you take over (head comes up from grazing, running clips stop). |
| You were killed by the animals you spawned | `/sp spawn` animals are **calm** (AI parked). `/sp wild` gives the looked-at animal its own AI back. |
| Key binds did nothing | The server cannot bind keys on your client. F and X already send a command to the server, the spike catches those. G is tested now. |
| Third person = lying on the board | Two new tests: the game's own third-person view flag (riding and on foot), and camera views for playing as the animal. |
| New: playing as the animal | `/sp play` - you are hidden and steer the animal from its point of view. |
| New: water | The deeper the water, the slower (the horse's own measure); half the body under water = walk only. Brown bear and crocodile are exempt. |

## Join

1. F1 -> `client.connect 127.0.0.1:28015`. You spawn on dry land near the map centre.
2. Chat: `/sp keys` (key probe: the server log records what each key sends).
3. **On foot**, press **F**, **G** and **X** once each, about one second apart. Nothing visible needs to happen - the log tells me.

## A. Animations after the fix

4. `/sp spawn cow`, look at it, `/sp ride`. W to walk, **Shift** up to the fastest gait, turn with A/D.
   - Do the legs move with the speed? Does the head stay up? Anything still wrong?
5. `/sp release`, `/sp spawn sheep`, the same.
6. `/sp spawn bear`, `/sp ride`. In session 1 the bear "sort of worked" - what exactly was off (legs sliding, jerky turns, wrong speed, head turned, something else)?

## B. Keys while riding (no binds)

7. **F** = attack. Look backwards over your shoulder and press **F** = rear attack (cow/bull kick).
8. **G** = view (riding: switches the game's third-person flag). Chat prints "view: ..." when the key arrived. Did the camera change? If G prints nothing, try **R**.
9. **X** = get off.

## C. Third person on foot

10. On foot: `/sp tp on`. Does the camera move behind you? Walk, look around, open a door. `/sp tp off`.

## D. Playing as the animal

11. `/sp spawn wolf2`, look at it, `/sp play`. Your human body is hidden; you are the wolf.
    - W/A/S/D, Shift/Ctrl gait, Space jump, **F** attack, **B** gestures (the wolf howls / growls instead of your hand gesture).
    - **G** (or **R**) cycles the view: **head** (eyes of the animal) -> **chase** (camera behind the animal) -> **orbit** (real third-person camera; your hidden human may show inside the animal - tell me) -> head.
    - **X** = back to human.
12. The same with `cow` (gestures: moo), `chicken` and `bear`.
13. Optional: `/sp steer mouse` - the animal turns where you look while moving. Better or worse than A/D?

## E. Water

14. `/sp water` teleports you to a shore with shallow water in front of you.
15. `/sp spawn cow`, `/sp ride`, walk into the water. Does it slow down as the water gets deeper? At belly depth the chat says "deep water - walk only".
16. The same with `/sp spawn bear`: the brown bear should **not** slow down.

Report per letter (A-E): **OK** or what was wrong. Screenshots help (F12 in Steam).
