# Weapons, movement tech and rewards

Rewritten. The first version of this was a list of guns that differed by damage and fire rate,
and Ryaan's read on it was right: it adds variety without adding a game. A shotgun and an SMG
are the same decision at different ranges.

The pineapple launcher he described is the reason why. It isn't good because it does area
damage — it's good because **the knockback makes it a movement tool**. It changes where you can
be, not how fast the health bar goes down. That's the bar everything below has to clear:

> **Does it change how you move, or how the space works?**
> If the answer is only "it does more damage at this range", it isn't on this list.

The five existing weapons are the baseline and they're fine as a baseline — you need boring guns
for the interesting ones to be interesting against. Nothing here replaces them.

---

## 1. Weapons that move you

### The Pineapple — rocket launcher *(Ryaan's, and the anchor for the rest)*

Arcing projectile, travel time, bursts on contact. Splash damage falling off with distance, a
real explosion you can see and hear from across the map, and **knockback applied to the shooter**.

The whole design is in three numbers:

| number | why it matters |
|---|---|
| self-damage | TF2 charges you health for a rocket jump. Valorant's Raze doesn't. |
| self-knockback | how high you can get, and whether it's a movement tool or a nudge |
| arming distance | stops point-blank suicide, and stops it being a shotgun |

**Recommendation: little or no self-damage, large self-knockback.** TF2's health cost exists
because TF2 has healers and a 12v12 economy. Five friends in a deathmatch don't have that, and
"I blew myself to the roof and arrived on 30 health" is a worse story than "I blew myself to the
roof". Keep the cost as *commitment*: you go where the blast sends you, and you can't steer much.

Direct hits should do noticeably more than splash, so there's a reason to aim rather than to
spam feet.

**What it needs built:** a projectile system (pooled, replicated, travel time, contact test), a
radial damage query, knockback applied to `PlayerMovement`'s velocity rather than to the
transform, and an explosion with real weight — screenshake scaled by distance, a light flash, and
a sound with a tail.

That projectile system is the single highest-value thing on this whole document, because it
unlocks everything in this section.

### The Vine — grapple, now specced closer to ODM gear

**Built 2026-08-21**, same day as the spec below — `Assets/Scripts/VineGrapple.cs`, bound to G.
Left the design writeup in place rather than trimming it down after the fact, since it's still
the accurate account of what the feature is and why.

Fires a vine. The original version of this entry only anchored to world geometry and did no
damage; the 2026-08-21 ask is more specific and closer to Attack on Titan's omnidirectional
mobility gear, so this replaces that version rather than sitting next to it.

Latches onto either a vantage point (world geometry) or an enemy player, and pulls you toward the
anchor fast — momentum builds quicker than any other movement tech in the game while attached. Tap
the button again to cut early; hold it and you stay attached until you reach the anchor or it
breaks on its own. Your gun is unusable for as long as you're attached — this isn't a weapon-slot
cost any more, it's an active tradeoff between shooting and moving.

**While attached, you can damage a player by reaching them** — using the same speed-scaled formula
as Momentum melee below, rather than a damage number of its own. That's the right call twice over:
one formula to tune instead of two, and it means the vine and the peel are the same underlying
idea in two different shells — go fast, hit something, the speed is the damage. Worth building
Momentum melee first for its own sake, but it also means the vine inherits an already-tuned,
already-playtested damage curve for free instead of needing its own pass.

Still pairs with the Pineapple the way a rocket launcher pairs with a grappling hook in every game
that has both — the launcher gets you height, the vine gets you distance — and still fixes the
thing the game currently lacks: a reason to look up.

**What it breaks, worth knowing before it's built:** anchoring to a *player* is a moving target in
a networked game in a way a wall never is. World geometry doesn't die, doesn't respawn, doesn't
disconnect, and isn't smoothed by interpolation the way a remote copy's position is — an enemy
anchor can do all four while you're attached to it. Needs an answer for what happens to the rope on
a kill, a disconnect, and a respawn (almost certainly: detach immediately, don't try to follow them
to their new spawn point), and needs to feel right while being pulled toward a target whose
position you're only ever seeing slightly in the past.

### The Blender — vortex

Short-range cone that **pulls enemies toward you** rather than pushing them. Almost no damage.

It's an anti-camping tool and a setup tool: yank someone off a ledge, pull them out of cover into
your friend's line, drag someone into the pineapple you just fired. In a five-player game the
funniest weapon is the one that makes somebody else's shot land.

### Coconut Mortar

High arc, no direct fire — it physically cannot shoot at someone you can see. Sticks where it
lands and detonates after a beat.

The point is that it makes **rooms** dangerous rather than **people** dangerous. Nothing in the
game currently lets you say "you can't go through there for the next three seconds".

---

## 2. Movement tech

Abilities are scrapped. They were a cooldown you press, and a cooldown you press is a thing the
game does *for* you - the opposite of what makes movement feel good. What replaces them is tech:
things that are always available, cost nothing, and only work if you are good at them.

The slide already landed and is the template for the rest. Hold the key at speed and you slide;
hold it standing still and you crouch; jump out of a slide and the boost goes with you into the
air. No cooldown, no resource, no button that says SLIDE - just momentum you either had or
didn't.

**Slide hop.** Landing a jump directly into a slide keeps more speed than either alone. Already
half true, because a hop inside the grace window skips friction; making the window slightly wider
when you land *into* a slide turns it into a chain you can practise.

**Wall run.** Hold toward a wall while airborne and above a speed threshold, and you stick to it
for a second or so, gravity mostly off, gaining a little height. Ends when you jump off, slow
down, or run out of wall. The jump off it is the payoff and should push away as well as up.

**Vault.** Run at a ledge below chest height and go over it without stopping. Purely a smoothing
mechanic - it removes the moment where speed dies against a knee-high box, which is the single
most common way momentum is lost on any map.

**Ground slam.** Crouch in the air with speed and you come down hard, keeping the horizontal
component. A way to convert height into distance, and it pairs with rocket jumping - up with the
pineapple, across with the slam.

**Air brake.** Tap slide in the air to kill horizontal speed almost instantly. Sounds
counterproductive on a list about going fast, and it is the most important entry: movement tech
is only fun when you can *stop*, because otherwise every rocket jump ends in a wall. It is also
the counter-play - the person who can stop is the person who can shoot.

**Momentum melee.** The peel does more damage the faster you are travelling when it lands. Turns
a slide into an attack and gives the last gun game rung something to build toward, instead of
being the weapon you dread getting. **Built 2026-08-21** as `PlayerMovement.MomentumDamage` — a
shared formula rather than a peel-only one, since the vine above reuses the exact same curve.

None of these need a system. They are all conditions checked against the velocity the mover
already has, which is what makes them cheap to add and cheap to remove if one turns out to be
horrible.

## 3. Earned rewards

Ryaan's brief was killstreaks, minus the copy-paste. Two things worth saying before the list.

**There is already a killstreak system.** Kills heal you, and past full health they give
overshield up to 200. It works, it's invisible, and it's the model everything here should follow:
the reward makes you harder to stop, not able to press a button that wins.

**Snowballing is the real risk.** In a room of five, one person on a streak can end the match on
their own. So every one of these is **short, loud, and visible**. If somebody is on a streak, the
other four should know instantly and be able to do something about it.

These stay. They are earned rather than pressed, which is a different thing to a cooldown -
you get them by playing well, and everyone can see you have one.

**3 kills — Banana Rain.** Mark an area; a few seconds later a cluster of bananas falls on it.
Telegraphed on the ground for everyone, so it denies a space rather than deleting whoever was in
it. Reuses the projectile system.

**5 kills — Go Ape.** Ten seconds of: faster, melee kills in one hit, screen pushed red, and **a
roar every player in the match hears wherever they are**. You're genuinely dangerous and
everybody knows exactly where you are and what you are. High risk both ways, which is the only
way a rage mode belongs in a five-player game.

**7 kills — The Zookeeper.** Every enemy outlined for a few seconds. The UAV analogue, and the
one thing on this list that's straight-up strong — which is why it's the last rung.

**Streaks reset on death**, obviously, and the existing heal/overshield keeps running underneath
all of it.

---

## 4. What to build, in what order

1. **The projectile system.** Nothing else in section 1 exists without it, and the Pineapple
   alone is worth the work. Pooled, replicated, travel time, contact test, radial damage,
   knockback into `PlayerMovement`'s velocity.
2. **The Pineapple**, on top of it. Get the three numbers right by playing, not by reasoning —
   self-knockback in particular is a feel number and no check will ever tell you it's wrong.
3. **The vault**, because losing all your speed on a knee-high box is the most common way
   momentum dies and it is the cheapest of these to build.
4. **Wall running**, which is the one people will actually talk about.
5. **The air brake and the ground slam**, which are both a condition and an impulse.
6. **Streak rewards**, last, because they are the easiest thing to ruin the balance with.

The five hitscan guns from the previous version of this document — SMG, marksman rifle, hand
cannon, LMG, auto shotgun — are still worth having eventually, as ladder filler for gun game.
But they're filler, and they shouldn't come before any of the above.

---

## 5. Naming

Fruit where it's a fruit, and not otherwise. "The Blender" and "Knuckle Run" aren't fruit and are
better for it. Half the joke is a gorilla holding something that clearly isn't a banana.
