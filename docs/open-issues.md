# Open issues

Reported from play and **not yet worked on**. Written down at Ryaan's request rather than fixed,
so nothing here has been touched in code.

`bug-log.md` is the historical list of what was already broken and fixed. This is the live one.

---

## The slide, which is not finished

Reported 2026-08-17, after the scrape audio landed. Five separate complaints. As of 2026-08-21,
three of them — not smooth, no jump boost, still not chainable — turn out to be one economic root
cause with the numbers to prove it, below.

### The scrape is mistimed

Plays too early and ends too early. Two candidates, neither verified:

- The loop starts the moment `Sliding` goes true, which is the frame the *state* changes rather
  than the frame the camera has finished dropping — so the sound leads the visual by however long
  `slideEase` takes.
- It stops on `volume <= 0.001`, and volume is driven by speed, so a slide that is still going but
  has slowed below the fade floor goes silent while you are visibly still sliding.

The second one would explain "ends too early" exactly and is worth checking first.

### It is not smooth, and a slide is genuinely slower than just running

Reaffirmed 2026-08-21, with a much more useful data point this time: "the slide somehow makes you
slower than giving you an initial speedboost." That checks out against the real numbers in
`PlayerMovement.cs`, and writing it out properly here also replaces the guesswork in the next two
sections instead of needing a separate theory each.

| field | value | role |
|---|---|---|
| `maxGroundSpeed` | 8.13 | normal run, continuously maintained by `Accelerate()` |
| `slideKick` | 1.18 | one-time multiplier applied on entry |
| `slideDrag` | 7 | m/s² lost every frame while sliding |
| `slideExitSpeed` | 3.5 | speed at which the slide ends on its own |
| `slideJumpBoost` | 1.6 | flat m/s added if you jump out while still sliding |

Entering a slide at max run speed gives `8.13 * 1.18 ≈ 9.59 m/s`. While sliding, `GroundMove`
takes a branch that subtracts `slideDrag * dt` every frame and returns immediately — no
`Accelerate()` call happens during a slide at all, so there is no re-acceleration, only decay.
Bleeding from 9.59 down to the 3.5 exit floor at 7/s takes about 0.87 seconds, covering roughly
**5.7 metres** at the average speed over that stretch. Running for the same 0.87 seconds at a
steady 8.13 covers roughly **7.1 metres**. The entry kick is real but small — +18% — and drag
erases it well inside a second, with nothing available to fight back with until the slide ends on
its own. A slide is, numerically, a worse way to cross that ground than not sliding.
`chainBonus` (+0.09 to the kick per chain link, so the fourth slide kicks at 1.45x) narrows the
gap higher up the chain but doesn't close it — the drag is too aggressive relative to the entry
speeds involved at every depth, not only the first slide.

Tuning problem, not a bug: the kick needs to be bigger, the drag needs to be gentler, or a slide
needs some acceleration back instead of pure decay. Not touched, per instruction — the numbers
above are so a fix doesn't have to start from a guess.

### Jumping out of a slide gives no boost — the earlier theory here was wrong

Previously suspected an ordering bug: that `UpdateStance` clears `sliding` before `GroundMove`
reads it in the same frame, making the boost dead code. Having now actually read both functions
side by side, that doesn't hold up — `UpdateStance` only clears `sliding` when speed has already
dropped below `slideExitSpeed` or the player has left the ground, and neither is true on the frame
a still-fast, still-grounded player presses jump. `GroundMove` does see `sliding == true` in the
normal case and does apply `slideJumpBoost`. Retracting that theory rather than carrying it
forward wrong.

The better-supported explanation is the drag problem above. `slideJumpBoost` is a flat +1.6 added
to whatever horizontal speed exists the instant you jump — and since a slide loses 7 m/s every
second from the moment it starts, by the time a player reacts and presses jump, drag has usually
already taken more than 1.6 back. The "boost" is frequently a partial refund on a loss already
taken, which reads as no boost at all because most of the time it isn't one.

### Still not chainable

Reaffirmed 2026-08-21, same wording as before:

> when youre in the air or not you should be able to queue a slide in a small window so you can
> slide when you land and then you jump and its like a jump into the air with momentum forwards
> and then you can chain a slide when you land

The queue half of this is built and works — `slidePressedAt`/`slideBuffer` correctly catch a
press made before landing. What's missing isn't input timing, it's economics: the loop only feels
like a chain if jumping out and sliding again nets more speed than not bothering, and per the two
sections above it currently doesn't. The drag/kick balance above is very likely the actual fix for
this entry too, not a separate one.

### The rank text is weak, and too small

Reaffirmed 2026-08-21, with one more specific: it's also physically too small on screen, on top of
"SLIDE / SLIDE! / SMOOTH!! / BANANAS!!!" not doing the job as copy. Wants to be genuinely
satisfying rather than a label that changes size — the Devil May Cry comparison was the brief and
the current version is not close to it on either count.

---

## Bhopping has no ceiling, and may not be reaching the speed effects at all

New 2026-08-21. Two claims, both plausible from the code, neither confirmed in play.

**No ceiling.** `Accelerate()` only ever adds speed toward `wishDir` — `addSpeed = wishSpeed -
currentSpeed`, and it returns early rather than going negative — so it cannot remove speed already
held, and nothing else in `GroundMove` or `AirMove` clamps total velocity magnitude either. Two
real, additive sources of speed have no ceiling of their own:

- Air-strafing while bhopping (turning `wishDir` away from current velocity mid-air) is the
  standard Quake/Source trick, and it works here the same way it does there — each hop can add
  more speed along the new direction, indefinitely, because nothing ever subtracts it back down.
- Slide-jump chaining adds `slideJumpBoost` on top of existing velocity every cancel, capped at 4
  chain links before a 10-second lockout — but that limits how often it can happen, not how fast
  it can leave you. String chains back to back and there is still no speed ceiling between them.

Both are the same shape as the slide-drag problem above: an event-count limit, but no magnitude
limit.

**Speed effects.** `SpeedRush.cs` reads raw horizontal velocity magnitude every frame and is not
gated on sliding or anything else — `threshold = 15`, `full = 32` — so it should fire during a
fast bhop the same as during a fast slide. Two ways the report could still be true: either a pure
jump-timing bhop with no air-strafe turning never actually clears 15, because `Accelerate` won't
push straight-line speed past `wishSpeed` (≈8.13) on its own, so it only *feels* fast against
normal running without crossing the effect's threshold — or there's a real disconnect this reading
didn't find. Worth logging `movement.Velocity.magnitude` against `SpeedRush.Intensity` in play
before assuming which one it is.

---

## Sandbox inherits the previous match's loadout rules

Reported 2026-08-17, reaffirmed 2026-08-21 with no new detail. Reproducible:

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
