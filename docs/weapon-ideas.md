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

### Replaced 2026-08-22

The Blender and the old Coconut Mortar are gone — told directly they weren't landing, not just
quietly cut. Three ideas below instead of a one-for-one swap, since "I don't like these" is a
better reason to offer a real choice than to guess which single replacement is right.

### The Durian — sticky mortar

Same high arc as the old Coconut Mortar — it physically cannot be fired at someone you can see —
but it doesn't detonate on contact. It **sticks**: to a wall, a floor, or a player, however it
first touches something, then goes off on a short fuse.

Stuck to geometry, it's the old idea done slightly better — you get to choose where the "don't
go there" zone actually is rather than wherever the arc happened to land, since it's now visible
and ticking rather than an invisible landing site.

Stuck to a **player**, it's a different thing entirely: a mark. Everyone can see it, including
whoever's carrying it, and it demands they do something about it in the next couple of seconds —
run somewhere it won't hurt anyone else, or run at their friends and hope the blast catches
someone too. That's the pitch the old version didn't have: a moment of urgency aimed at a
specific person, not a patch of denied floor.

**What it needs:** the same projectile system every arced weapon here already shares, plus a
stick-on-contact state (surface normal for geometry, parent-to-hitbox for a player) and a fuse
timer with a readable tell — a beep, a flashing skin — so getting marked is legible instantly
rather than found out from the explosion.

### The Zest — citrus blind

Short-range spray, almost no damage, more juice can than gun. Catches you in it and the screen
stings — a bright, brief citrus-in-the-eyes flash and a few seconds of blurred vision, not a
one-shot advantage but enough of an opening for whoever threw it to close distance or get out.

The pitch is disable instead of damage, which nothing else on this list does. The Pineapple
changes where you can be; the vine changes how fast you get there; the Zest changes whether you
can *see* for the next couple of seconds, which reads completely differently on both ends of
it — genuinely funny to throw, genuinely infuriating to eat, and that gap is the point.

**What it needs:** a cone check rather than a projectile (no travel time — this is a spray, not a
shell), a screen-space post-process for the sting itself (bloom past white, a blur ramp in and a
slower ramp out), and a duration/intensity pair that's worth tuning by playing — too short and
it's not worth the ammo, too long and it's a Zest fight nobody enjoys losing.

### The Coconut — rolling mine

Kept the name, dropped the mortar. Thrown rather than lobbed on an arc, it hits the ground and
**rolls**, following the terrain — down a slope, off a ledge, however momentum and gravity take
it — until it hits a wall or a player, at which point it sticks and goes off the same way the
Durian does.

The reason to build a second sticky-detonator rather than reuse the Durian outright is the
trajectory: an arc is aimed once and then it's just physics, but a rolling mine keeps moving
after it lands, which makes it read and feel completely differently even sharing the same
detonation logic — it can chase someone down a corridor, get stuck against a doorframe, or
surprise someone by rolling round a corner they thought was safe. Purely physical, no logic of
its own beyond "roll until you hit something", which is what makes it worth having next to the
Durian rather than being the same weapon twice.

**What it needs:** a rigidbody rather than the kinematic travel every other projectile here uses
(this is the one thing on the list that actually needs to roll believably), and the same
stick-and-fuse detonation the Durian already has, reused rather than rebuilt.

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

## 4. Progression: tokens and crates

Planned, not built. Folded in from its own doc 2026-08-22 - it was one topic split across two
files for no reason once both existed, and "too many docs" was the actual complaint that started
this pass.

A CS2-crate-shaped loop: win a match, earn tokens, spend tokens on a crate opening back at the
main menu. Two things need real design before this touches code - what's inside the crates, and
where the tokens actually live, because this game currently has no server to keep either of them
honest.

### Earning

"After every win" was the brief, and it's a good gate on its own - it means the currency tracks
*winning*, not just showing up, which fits a five-friend lobby better than a currency everyone
accumulates identically regardless of how the match went.

**Starting recommendation: 1 token per win, crate costs 3-5.** Not derived from anything but the
session shape already established elsewhere in this project - deathmatch is 5 minutes, gun game
10, so a casual night is maybe 4-8 matches, half of them plausibly won across a group of friends
who are all decent at the game. That puts a crate at roughly one per session or two: often enough
to matter, rare enough that opening one is an event rather than a formality. This is exactly the
kind of number the project has already been explicit about elsewhere - "a feel number and no
check will ever tell you it's wrong" (this doc, on the Pineapple's self-knockback) - so treat
1/3-5 as the number to start playtesting with, not the number to ship.

### Opening one

"Spin the wheel" and "play roulette" read as two names for the same reveal rather than two
separate minigames - CS2 doesn't have a roulette wheel, so this is most likely the user reaching
for two familiar casino images for one idea rather than asking for two systems. **Assuming one
reveal screen**, built like a crate opening: a horizontal reel of items scrolls past and lands on
what you won, weighted by rarity. Flag this assumption explicitly rather than build two screens on
a guess - cheap to confirm, expensive to build twice.

"You gotta leave the game for this" places it as its own panel off the main menu (already rebuilt
to the ULTRAKILL/Cruelty Squad direction per M5), not an in-match overlay. That also sidesteps a
real problem: nobody wants a slot-reel playing while four other people are mid-fight waiting on
them.

### What's actually inside the crate

This is the one place this doc disagrees with a literal reading of "CS2 crates" rather than just
filling in a gap: **cosmetic only, nothing that touches gameplay.** CS2's skins don't affect play
either, and that's not incidental - anything a crate could give that changes a fight (a damage
boost, a starting weapon, extra health) turns "I won more crates" into "I'm now harder to beat for
reasons that have nothing to do with this match," which is a bad feeling in a five-person friend
lobby in a way it isn't in a matchmade game with thousands of strangers to absorb the unfairness.
Keep the stakes social - bragging rights over a rare pull - not competitive.

Candidates that don't require new systems:

- **Kill-feed icons** - the feed already draws an icon per weapon; a cosmetic swap is a sprite
  lookup, not new plumbing.
- **Victory lines** - a line shown on the post-match scoreboard for whoever's holding one, cheap
  to add given the scoreboard already exists (M3).
- **Banana/weapon skins** - a colour or pattern swap on the existing banana models, same shape of
  change as `WeaponNaming` already does for on-screen names.

**Deliberately not on this list: the 12 player colours in `PlayerColours.cs`.** They're already
free, already in the lobby picker, and already played and confirmed working across 3-4 clients -
gating them behind crates would take a shipped, liked feature and make it worse to claw back a
progression hook. Any colour-flavoured reward should be a new cosmetic slot (a trail, an outline,
a muzzle tint) that doesn't touch the existing palette, not a lock bolted onto it after the fact.

### Where the tokens actually live

The honest constraint, from `roadmap.md`'s own "Known limitations": **there is no server, no
database, and no anti-cheat of any kind.** Hit registration is already client-authoritative and
that was an accepted, deliberate call at this scale. A token balance has the same shape of
problem - it can only live in local `PlayerPrefs` on each person's own machine, which means:

- it doesn't follow a player across machines or reinstalls
- it's trivially editable by anyone who'd want to (a text editor and `PlayerPrefs` is all it takes)
- "gambling" implies stakes worth protecting, and there's currently nothing to protect them with

None of that is disqualifying for a private game played only with friends - the project has
already made this exact trade-off once and it's been fine. The recommendation is to make it
explicit rather than silently inherit it: treat the whole system as **for-fun, not fair** - a
slot machine you could technically cheat if you wanted to, same trust model as everything else
here, and the payoff is a cosmetic bragging right, not a competitive edge. If that stops being
good enough later, the fix is a real backend, which is a much bigger project than this one.

**If this gets built:** roughly, kill-feed icon swap first (smallest, reuses the most), then the
crate-opening screen itself, then victory lines, then banana skins last (touches the model
pipeline, the most work of the four). Not a milestone number - this is pure meta-progression and
doesn't block anything else on `roadmap.md`, so it can slot in whenever, not before.

---

## 5. What to build, in what order

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

## 6. Naming

Fruit where it's a fruit, and not otherwise — the Durian, the Zest, the Coconut. Killstreaks and
movement tech don't need to keep the bit going the same way weapons do: "Go Ape" isn't fruit and
is better for it. Half the joke is a gorilla holding something that clearly isn't a banana; the
other half doesn't need forcing onto things that were never bananas to begin with.

(Retired 2026-08-22: this used to cite "The Blender" as the non-fruit example. It's gone now, see
above, so this points at something still in the document instead.)
