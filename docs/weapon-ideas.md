# Weapon ideas

Five weapons is too few, especially for gun game — the ladder is five rungs, so a match is over
in nine kills and you barely meet half of them.

Grouped by **what the code already does**, because that's the difference between an afternoon
and a week. Everything in group A is a ScriptableObject and a model; group B and C need real
systems built first.

Current roster, so nothing below overlaps it — `WeaponCheck` fails the build if two weapons
share a role:

| role | on screen | damage | rate | range | what it's for |
|---|---|---|---|---|---|
| Pistol | Cavendish | 34 | 5/s | 120 | accurate, 3 shots to kill, everyone starts here |
| Shotgun | The Split | 108/pull | 1.4/s | 22 | one burst up close |
| Rifle | The Bunch | 21 | 10/s | 200 | sustained damage, wins a long fight |
| Sniper | Big Mike | 95 | 0.8/s | 400 | scoped, one mistake kills you |
| Peel | Slip Hazard | melee | — | 2.4m | gun game's last rung |

---

## Group A — hitscan, drops straight into what exists

No new systems. A `GunInfo` asset, a model, an audio bank, a name.

**SMG — "The Finger"** *(a lady finger banana, which is the small one)*
12 damage, 16/s, automatic, 40 rounds, range ~60, wide spread.
The gap it fills: closing distance. The shotgun does that in one burst and the rifle does it
badly; this does it as a stream. It has to fall off hard past 60m or it's just a better rifle.

**Marksman rifle — "Plantain"** *(a banana you can't eat raw — it needs work)*
Three-round burst, 26 a shot, 1 burst/s, range 250, no scope.
Sits between the rifle and the sniper, which is currently a cliff: you're either spraying at
200m or you're scoped at 400m. Rewards trigger discipline without the sniper's commitment.

**Hand cannon — "The Cleaver"**
70 damage, 1.6/s, 6 rounds, range 90.
Two shots to kill, and missing genuinely costs you. Distinct from the pistol (three fast shots)
and the sniper (range, scope). The one to give someone who's good.

**Light machine gun — "Bad Bunch"**
18 damage, 8/s, 100 rounds, 4s reload, spread that opens badly while you move.
Area denial. Nothing in the game currently punishes you for walking into a corridor. The long
reload is the balance — get caught empty and you're dead.

**Auto shotgun — "Overripe"**
45/pull, 3/s, 8 rounds, range 18.
The Split rewards one committed shot; this rewards holding the trigger and walking forward. Even
shorter ranged to keep them apart.

---

## Group B — needs a projectile system

Right now every weapon is a raycast that lands the instant you click. These need something that
travels, which means a pooled projectile with a lifetime, a hit test, and replication so
everyone sees the same thing in the same place. That's the real work; once it exists, all three
are cheap.

**Grenade launcher — "Smoothie"**
Arcing shell, splash damage, arms after 0.3s so you can't suicide-blast point blank.
Adds indirect fire: shooting at a place rather than a person, and denying a doorway. It's also
the funniest thing on this list.

**Speargun — "Skewer"**
Fast projectile, slight drop, one-shot on a headshot, 3-round magazine.
Rewards leading a moving target instead of tracking one. Silent, so it doesn't announce you.

**Boomerang — "Round Trip"**
Thrown, arcs out and comes back, damages on both legs of the trip.
Hits people behind cover on the way back, and you have to catch it to throw again. Pure
gimmick, entirely in keeping.

---

## Group C — needs new firing modes

`SingleShotGun` currently understands "one pull, one or more rays". These don't fit that.

**Railgun — "Gros Michel"** *(the cultivar that got wiped out — appropriate)*
Hold to charge, release to fire. Full charge pierces every player in a line.
Needs a charge state, a charging sound that rises, and penetration in the raycast. The rising
pitch while charging is free dopamine and this game already does that trick on hit combos.

**Flamethrower / spray — "Rot"**
Continuous cone, damage over time, no travel.
Needs a damage-over-time system and continuous fire rather than discrete shots. Also the only
weapon that would let you kill someone after you're already dead, which is a funny thing to
have.

---

## What I'd actually build first

The five in group A, in one pass. They're all the same shape as what exists, they take the gun
game ladder from five rungs to ten — which roughly doubles the length of a match and means you
meet weapons you'd otherwise never see — and none of them need a system that doesn't exist.

Then the projectile system, because it unlocks three at once and the grenade launcher is worth
it on its own.

Group C last. Both are good and both are a rewrite of how firing works.

## Naming

The banana theme is worth keeping for the ones that are bananas, but it shouldn't be a
straitjacket — "The Cleaver" and "Round Trip" aren't fruit and are better for it. Half the joke
is a gorilla holding something that clearly isn't a banana.
