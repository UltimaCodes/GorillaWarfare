# Open issues

Reported from play and **not yet worked on**. Written down at Ryaan's request rather than fixed,
so nothing here has been touched in code.

`bug-log.md` is the historical list of what was already broken and fixed. This is the live one.

---

## The slide, which is not finished

Reported 2026-08-17, after the scrape audio landed. Five separate complaints, and at least two of
them are probably the same underlying thing.

### The scrape is mistimed

Plays too early and ends too early. Two candidates, neither verified:

- The loop starts the moment `Sliding` goes true, which is the frame the *state* changes rather
  than the frame the camera has finished dropping — so the sound leads the visual by however long
  `slideEase` takes.
- It stops on `volume <= 0.001`, and volume is driven by speed, so a slide that is still going but
  has slowed below the fade floor goes silent while you are visibly still sliding.

The second one would explain "ends too early" exactly and is worth checking first.

### It is not smooth

Unqualified so far — could be the camera easing, the drag curve, or the capsule resize fighting
the controller. Needs watching before guessing.

### Jumping out of a slide gives no boost

`slideJumpBoost` is 1.6 and applied in `GroundMove` when `wantsJump` is true and `sliding` is
true. Ryaan reports getting nothing. Most likely: by the time the jump branch runs, `sliding` has
already been cleared by `UpdateStance` earlier in the same `Update` — the stance machine ends a
slide when `!grounded` or when speed drops, and it runs *before* `GroundMove`. If so the boost
is dead code and the whole chain has no reward, which would also explain the next item.

### Still not chainable

This is the one that matters and the buffer did not fix it. What is wanted, stated exactly:

> when youre in the air or not you should be able to queue a slide in a small window so you can
> slide when you land and then you jump and its like a jump into the air with momentum forwards
> and then you can chain a slide when you land

So the loop is: **queue slide (any time) → slide fires on landing → jump out for a forward
momentum boost → queue again mid-air → slide on landing → repeat**, up to the chain ceiling.

The buffer exists but something downstream is still eating it. Suspect the same ordering problem
as the jump boost.

### The rank text is weak

"SLIDE / SLIDE! / SMOOTH!! / BANANAS!!!" is not doing the job. Wants to be genuinely satisfying
rather than a label that changes — the Devil May Cry comparison was the brief and the current
version is not close to it.

---

## Sandbox inherits the previous match's loadout rules

Reported 2026-08-17. Reproducible:

1. Start a deathmatch
2. Go back to the main menu
3. Start a sandbox

The sandbox hands out deathmatch loadouts — one random weapon rather than all of them — and
keeps doing so **until the game is restarted**.

"Until you restart" points hard at a static that is never cleared, since statics survive a scene
change and a domain reload is what a restart provides. Candidates, none verified:

- `Sandbox.Active` is set in `Enter` and cleared in `Leave`, but nothing clears it if the room is
  left by any other path — and `MatchState.WeaponsFor` checks it first, so a stale `false` would
  send the sandbox down the deathmatch branch.
- The room's `ModeKey` property, or the local player's `LoadoutKey`, carried over from the
  previous room. PUN never clears player properties between rooms, which has already caused this
  class of bug twice.
- `PhotonNetwork.OfflineMode` not actually taking, so the sandbox is not the room it thinks it is.

Worth reproducing with the loadout logged before guessing, because all three would look identical
from the outside.
