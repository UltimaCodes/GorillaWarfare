# Weapons, abilities and rewards

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

### The Vine — grapple

Fires a vine. Sticks to world geometry, pulls you toward the anchor, cuts if you're shot or if
you let go. Not a weapon at all — it takes a weapon slot and does no damage.

Pairs with the Pineapple the way a rocket launcher pairs with a grappling hook in every game
that has both: the launcher gets you height, the vine gets you distance, and putting them
together is a skill you can spend a year getting good at.

Also fixes something the game currently lacks: a reason to look up.

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

## 2. Abilities

Chosen before you spawn, one per life, on a cooldown. Not earned — the earned things are in the
next section. Everyone always has one, so it's a loadout decision rather than a reward.

Gorilla-shaped on purpose: these should read as things an enormous ape can do, not as
sci-fi powers.

**Chest Beat** — shockwave in a ring around you. Knocks everyone back, and for about a second
their screen shakes and their audio ducks. A panic button that solves being surrounded without
solving being outplayed. Loud enough that everyone in the room knows you used it.

**Knuckle Run** — drop to all fours. Much faster, camera drops to waist height, and **you can't
shoot**. Pure rotation and escape. The camera drop is half the appeal; it changes what the map
looks like.

**Brachiate** — one mid-air lunge in the direction you're looking. Small, but it turns every gap
into a decision and every rocket jump into something you can steer.

**Silverback** — a few seconds of heavy damage resistance and immunity to knockback, during
which **you glow and everybody can see it**. Committing to a fight rather than winning one. The
tell is the balance.

**Scent** — enemy footprints light up through walls for a few seconds. Information rather than
force. The counter is standing still, which is its own punishment.

**Peel Trail** — drop peels behind you for a few seconds. Anyone who runs through one slips: no
damage, they just lose control of their aim for a moment. Chase-breaking, and it makes the Slip
Hazard's whole joke into a mechanic.

---

## 3. Earned rewards

Ryaan's brief was killstreaks, minus the copy-paste. Two things worth saying before the list.

**There is already a killstreak system.** Kills heal you, and past full health they give
overshield up to 200. It works, it's invisible, and it's the model everything here should follow:
the reward makes you harder to stop, not able to press a button that wins.

**Snowballing is the real risk.** In a room of five, one person on a streak can end the match on
their own. So every one of these is **short, loud, and visible**. If somebody is on a streak, the
other four should know instantly and be able to do something about it.

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
3. **One ability, to build the ability system.** Chest Beat is the right first one: it needs a
   cooldown, a radius query, a replicated effect and a sound, which is the whole framework.
4. **The rest of the abilities**, which are then mostly data.
5. **Streak rewards**, last, because they need the abilities framework and because they're the
   easiest thing to ruin the balance with.

The five hitscan guns from the previous version of this document — SMG, marksman rifle, hand
cannon, LMG, auto shotgun — are still worth having eventually, as ladder filler for gun game.
But they're filler, and they shouldn't come before any of the above.

---

## 5. Naming

Fruit where it's a fruit, and not otherwise. "The Blender" and "Knuckle Run" aren't fruit and are
better for it. Half the joke is a gorilla holding something that clearly isn't a banana.
