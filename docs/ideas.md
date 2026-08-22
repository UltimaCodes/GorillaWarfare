# Ideas

Weapons, movement tech, progression and maps that aren't built yet — or were built from a plan
that used to live here. Merged 2026-08-22 from `weapon-ideas.md` and `maps-and-voting.md`: two
separate not-yet-built design docs was exactly the kind of split "too many md files" was
complaining about, and both were the same thing - the backlog - split by topic for no reason
that survived either of them getting this long.

Rewritten once already, back when this was weapons-only. The first version of the weapons section
was a list of guns that differed by damage and fire rate, and Ryaan's read on it was right: it
adds variety without adding a game. A shotgun and an SMG are the same decision at different
ranges.

The pineapple launcher he described is the reason why. It isn't good because it does area
damage — it's good because **the knockback makes it a movement tool**. It changes where you can
be, not how fast the health bar goes down. That's the bar section 1 has to clear:

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

**Built.** `Projectile.cs` and `Resources/Guns/Pineapple.asset` — pooled, replicated, travel
time, contact test, radial damage, knockback into `PlayerMovement`'s velocity, screenshake scaled
by distance, a light flash and a sound with a tail. Self-knockback retuned down (14 → 12) and the
projectile given a glow light, both 2026-08-22.

The projectile system unlocking everything else in this section is also done - see section 7.

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

**On hold, same day.** Told directly none of the three below are being built yet either - not
rejected, just not decided, while more options get thought through. Left in place as candidates
rather than cut, since the design work under each is still good if one of them is picked later.

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

Confirmed 2026-08-22, ahead of building any of the next three: map geometry will carry mesh
colliders, not box colliders. Detection for all three has to be written against that from the
start - a raycast or spherecast against an arbitrary `MeshCollider` works fine and needs no
special case, but anything that reasons about a wall or ledge from `Collider.bounds` (an
axis-aligned box even when the collider underneath isn't one) will read garbage the moment the
geometry isn't itself box-shaped. Non-convex static mesh colliders raycast normally; the
`convex = true` requirement only bites a collider that also carries a non-kinematic `Rigidbody`,
which none of this project's level geometry does.

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

The rules above stay - short, loud, visible, earned not pressed, streaks reset on death, the
existing heal/overshield keeps running underneath whatever ends up here. What doesn't stay is the
three specific rewards that used to be listed under them:

**Retired 2026-08-22.** Banana Rain (3 kills), Go Ape (5) and The Zookeeper (7) were told
directly not to be the ones built - the killstreak system itself is still wanted, these three
specifically aren't. Removed rather than left checked-out-but-present, since a rejected idea
sitting in a numbered list reads as the plan. Whatever replaces them has to clear the same bar
this section already sets: short, loud, visible to the other four players, and dangerous both
ways rather than a free win button.

---

## 4. Progression: tokens and crates

Planned, not built.

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

## 5. Maps

Plan only. Nothing here is built. The game has one map — seven cubes and four planes — and no way
to change it. That's the single biggest reason a session gets old: every fight happens in the same
place, so everybody learns the same three angles and the match becomes about who got to them
first. Confirmed 2026-08-22: this is an active track now, not just a plan.

### What this game's movement actually wants

Worth stating before any layout, because it rules things out. Movement is Quake/Source: you
accelerate to about 8 m/s on the ground, you keep your speed in the air, bunny hopping is off,
and shift *slows you down*. Add the pineapple launcher and the vine from section 1 and you get
large vertical jumps and long swings.

So:

- **Rooms have to be tall.** Corridors sized for walking are wasted on someone who can rocket
  jump, and a ceiling you can hit is a ceiling that makes the best weapon in the game feel bad.
- **Gaps are good.** Something you can only cross with momentum or a launcher gives movement
  something to be *for*.
- **Long sightlines need to be breakable.** The sniper reaches 400m and nothing on the current
  map is 400m long, so the moment there's a real map, cover has to be deliberate.
- **Falling has to mean something but not everything.** The void kill already exists at y < -10.
  A map where you die by mis-stepping is a map where nobody uses the movement.

### The Compound

*Already on the roadmap, still the right first real map.*

A walled rectangle with an open courtyard and a two-storey building in one corner. Roof access up
the outside. Walls high enough to block sightlines from outside but not so high you can't get on
them with a launcher.

Why it works: it's three fight types in one space. The courtyard is open and reads at a glance —
that's where snipers and rifles live. The building interior is tight and cornered — shotgun and
peel territory. The roof and walls are the movement layer, exposed but fast, and getting up there
should feel like a decision.

Size: about 60×60m of playable ground, walls at 8m, building at 12m.
Spawns: four, one per wall, all with cover within two seconds of landing.

### Canopy

Treetop platforms at three or four distinct heights, connected by branches, with real gaps
between them and nothing underneath. Fall and you die.

Why it works: it's the map that only exists because of the movement system, and it's completely
different to play than anything on the ground. Fights become about height rather than distance,
and the vine and launcher stop being toys.

The risk is obvious — a map where you can fall off is frustrating if the gaps are guesswork.
Mitigation: make every gap either clearly jumpable at run speed or clearly not, with nothing in
between, and put a wide safe platform under the middle so a bad jump costs position rather than
a life. Build it second, after the compound, and expect to retune the gaps by playing.

Size: platforms spread over about 70×70m, heights from 0 to 25m.
Spawns: on the outer ring, facing inward.

### The Zoo

An abandoned zoo. Enclosures are the rooms — a drained pool, a rock enclosure, a glass-fronted
reptile house — arranged around a central plaza with a bandstand in it.

Why it works: it's the funniest premise available for a game about gorillas shooting each other,
and thematically the joke lands without anybody having to explain it. Mechanically it's a hub
with spokes, which is a proven layout: the plaza is where fights start, the enclosures are where
they finish, and each one plays differently because each one is a different shape.

The glass is worth building properly — shootable, breakable, and see-through until it isn't.

Size: about 80×80m, mostly single storey, plaza open to the sky.
Spawns: one per enclosure, none in the plaza.

### The Silo

One tall cylindrical shaft with a ramp spiralling up the inside wall and platforms cantilevered
into the middle. Small footprint, enormous height.

Why it works: it's the small map. Every session needs one place where you cannot avoid each
other, and a vertical map means "small" doesn't mean "corridor". Everyone can see everyone,
almost always, so it's pure mechanics.

Size: 30m across, 60m tall.
Spawns: staggered up the ramp, so nobody starts at the bottom.

### Which two I'd build

**The Compound and The Silo.** The compound because it's the general-purpose map every other
one gets compared against, and the silo because it's small, fast to block out, and maximally
different from the compound — two maps that play the same way is the same as one map.

Canopy and the Zoo are better maps but both are much more work, and Canopy in particular needs
the launcher and vine to exist first or half of it is unreachable. Both now do.

**How to build them:** blocked out from code, the way `MapDressing` already works, rather than
placed by hand. A generated blockout is re-runnable, diffable and tunable by changing a number,
which matters enormously while the layout is still wrong. Hand-placed art goes on top once the
shape is settled and nobody is moving walls any more.

---

## 6. Map voting

### The rules

- Voting is open **from the moment a match goes live until the results screen ends**. Ryaan asked
  for during the round or after it; making it one continuous window means there's no moment where
  the button is there but dead.
- Everyone gets one vote and can change it freely until the window closes.
- The tally is visible while voting — on the results screen at full size, and small on the HUD
  during the match. Seeing that three people have already voted for the silo is half the fun.
- **The current map is excluded.** Not down-weighted, excluded. Nobody wants the same map twice
  and a system that allows it will produce it.
- **Ties break randomly** among the tied maps.
- **Nobody voted** — pick at random from everything except the current map.

### How it hangs together with Photon

Follows the existing pattern exactly: state lives in properties, the master decides, nobody
broadcasts a tick.

- Each player's vote is a **player custom property** (`mapVote`, an int index). Player properties
  because a vote belongs to a person, they're replicated automatically, and someone leaving takes
  their vote with them without any cleanup code.
- The tally is computed locally by every client from `PhotonNetwork.PlayerList`. No shared
  counter, so no read-modify-write race — which matters, because `SetCustomProperties` doesn't
  update the local cache until the server echoes and a shared counter would silently lose votes.
  That trap has already cost this project a day.
- **The master resolves it** when the results phase ends, writes the chosen map to a room
  property, and calls `PhotonNetwork.LoadLevel`. Same shape as every other phase transition.
- **Votes clear** when the new map loads, along with the rest of the per-match state in
  `OnLeftRoom`/`BeginWarmup`.

### A registry, not scene indices scattered about

One `ScriptableObject` in `Resources` listing every map: display name, scene build index, and a
preview image. Everything else — the vote UI, the loader, the room browser — reads that. Adding a
map becomes one row in one asset.

### What it breaks, which is the part worth knowing up front

**`RoomManager.gameSceneIndex` is a const equal to 1**, and three separate things depend on it:
the spawn path waits for that index, the message-queue guard checks it, and `MatchState` now uses
it to decide whether the match has started. `SceneCheck` asserts the game scene *is* index 1.

That const has to become "whichever map is loaded", which means:

- the spawn wait becomes "any scene that isn't the menu"
- `SceneCheck` asserts every map is in build settings and that the menu is index 0, instead of
  asserting a single game scene is index 1
- every map scene needs its own `SpawnManager`, `GameHud` and `PostProcessing` setup — which is
  an argument for making the HUD a prefab like the settings screen, so a new map gets it by
  instantiation rather than by somebody remembering

None of that is hard. All of it is the sort of thing that turns a two-hour job into a day if it's
discovered halfway through, which is why it's written down here first.

### Build order (maps track)

1. The registry, and move `gameSceneIndex` over to it.
2. A second map — the silo, since it's the smaller build — with nothing but the loader. Prove two
   maps can be switched between at all before adding any voting.
3. Voting: the property, the tally, the master resolving it.
4. The vote UI on the results screen.
5. The small live tally on the HUD.

---

## 7. What to build, in what order (weapons/movement track)

Updated 2026-08-22 — the first two rungs are done, and the streak rewards that were rung six no
longer have a specific design (see section 3). Runs in parallel with the maps track above, which
is its own active workstream now rather than competing with this list for the same time.

1. ~~The projectile system.~~ Built - `Projectile.cs`, pooled, replicated, travel time, contact
   test, radial damage, knockback into `PlayerMovement`'s velocity.
2. ~~The Pineapple.~~ Built on top of it, self-knockback retuned once already (14 → 12) by
   playing rather than reasoning, same as the note below still recommends for whatever's next.
3. **The vault**, because losing all your speed on a knee-high box is the most common way
   momentum dies and it is the cheapest of these to build. Build against a `MeshCollider`, not a
   `Collider.bounds` box — see the note in section 2.
4. **Wall running**, which is the one people will actually talk about. Same collider note applies.
5. **The air brake and the ground slam**, which are both a condition and an impulse.
6. **Streak rewards**, once there's a specific design to build - the old three are retired, not
   replaced yet.

The five hitscan guns from the previous version of this document — SMG, marksman rifle, hand
cannon, LMG, auto shotgun — are still worth having eventually, as ladder filler for gun game.
But they're filler, and they shouldn't come before any of the above. The Durian/Zest/Coconut
weapons in section 1 are a separate, currently-on-hold decision - see that section.

---

## 8. Naming

Fruit where it's a fruit, and not otherwise — the Durian, the Zest, the Coconut. Killstreaks and
movement tech don't need to keep the bit going the same way weapons do: "The Vine" isn't fruit and
is better for it. Half the joke is a gorilla holding something that clearly isn't a banana; the
other half doesn't need forcing onto things that were never bananas to begin with.

Maps get placenames, not fruit or bits — the Compound, the Silo, Canopy, the Zoo. Consistent with
the same rule: the joke is what's in your hands, not what's under your feet.
