# Maps and map voting

Plan only. Nothing here is built.

The game has one map — seven cubes and four planes — and no way to change it. That's the single
biggest reason a session gets old: every fight happens in the same place, so everybody learns
the same three angles and the match becomes about who got to them first.

---

## Part 1: the maps

### What this game's movement actually wants

Worth stating before any layout, because it rules things out. Movement is Quake/Source: you
accelerate to about 8 m/s on the ground, you keep your speed in the air, bunny hopping is off,
and shift *slows you down*. Add the pineapple launcher and the vine from the weapon doc and you
get large vertical jumps and long swings.

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
the launcher and vine to exist first or half of it is unreachable.

**How to build them:** blocked out from code, the way `MapDressing` already works, rather than
placed by hand. A generated blockout is re-runnable, diffable and tunable by changing a number,
which matters enormously while the layout is still wrong. Hand-placed art goes on top once the
shape is settled and nobody is moving walls any more.

---

## Part 2: map voting

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

### Build order

1. The registry, and move `gameSceneIndex` over to it.
2. A second map — the silo, since it's the smaller build — with nothing but the loader. Prove two
   maps can be switched between at all before adding any voting.
3. Voting: the property, the tally, the master resolving it.
4. The vote UI on the results screen.
5. The small live tally on the HUD.
