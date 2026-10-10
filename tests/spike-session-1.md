# Spike session 1 - in-game checklist (dev server, BmSpike)

Goal: see with your own eyes what only the client can show - seat pose, animation while steering, sliding at speed,
clip playback and sound, jumps, camera, key binds, gestures, hiding. Each step: say **OK** or describe what looked wrong.

## Join
1. Update Rust in Steam (the dev server runs the newest build).
2. Start Rust normally. Press F1 and type: `client.connect 127.0.0.1:28015`
3. In chat type `/sp help` - you should get the command list.
   You spawn on dry land near the map centre (dev plugin DevSpawn). A kick that says "InsideTerrain" or any other antihack
   reason is a finding - report it with the time.

## A. Riding a bear (spikes S1, S2, S3)
4. `/sp spawn bear` - a bear appears 6 m ahead. Look at it, type `/sp ride`.
   - Do you sit on its back? Is the pose sensible? Is a saddle visible? (S1)
   - Wrong height? Try `/sp seat 0 1.6 0`, `/sp seat 0 1.3 0.2` and say which looks best.
5. Drive: **W/S** throttle, **A/D** turn, **Shift** gait up, **Ctrl** gait down (chat shows gait and m/s).
   - Smooth or jerky? Does the walk/run animation match the movement at every gait? (S2, S3)
   - `/sp steer mouse` = the bear follows where you look while moving. Better or worse than A/D?
6. Sliding: at the top gait type `/sp speed 1.5`, then `/sp speed 2`. At which multiplier does it start to look like skating? `/sp speed 1` to reset.
7. Hills: ride up and down a slope. OK?

## B. Clips, attack, sound (spike S4)
8. `/sp clips` lists the bear's clips. Try `/sp play 0`, `/sp play 1`, ... Do the animations play? Is there sound?
9. `/sp attack` (or the middle mouse button) - attack animation + sound?

## C. Jump (spike S18)
10. **Space** = arc jump (mode b). How does it look?
11. `/sp jump c 6 1.5` - bound with running legs. Better?

## D. Camera (spike S7)
12. `/sp cam third` - moves you onto a third-person seat. Is the view third person? Can you still steer? `/sp cam first` to go back.

## E. Keys (spike S8)
13. `/sp bind` - then press **F** (attack), **G** (camera swap), **X** (get off). Do they react?
14. `/sp unbind` - restores your own F / G / X binds. Check that F toggles your light again.

## F. Gestures (spike S11)
15. `/sp gest on suppress`, then hold **B** and pick *Wave* or *Thumbs Up*. Does the bear play an animation instead of your hand gesture?
16. `/sp gest off`

## G. Other species (repeat A-C briefly)
17. `/sp release`, then `/sp spawn wolf2`, `tiger`, `cow`, `sheep`, `chicken`, `crab_single`, `frog`, `rabbit` - ride each for half a minute. Wolf and tiger: also `/sp jump a 5 2` (their own leap clip).

## H. Possession (spike S6)
18. `/sp spawn cow`, look at it, `/sp possess` - you are hidden and control the cow from its head.
   - Is your view at the cow's head? Can you drive it? `/sp cam third` - third person on the cow?
   - Night: if you have night-vision goggles, do they work in this view?
19. `/sp release`.

Report per letter (A-H): OK / what was wrong. Screenshots help (F12 in Steam).
