# Roadmap

Where this is going and in what order. Updated 2026-08-15 (second pass).

## Design philosophy

ULTRAKILL, Cruelty Squad, Hotline Miami. What that actually means in practice, so it's
something to check work against rather than a vibe:

- **Loud, not tasteful.** High contrast, saturated, clashing on purpose. Cruelty Squad's palette
  is actively hostile and that's the point. No tasteful dark-grey-with-one-accent.
- **Type as a weapon.** Oversized, condensed, shouting. Text is part of the composition, not a
  label on it.
- **Everything is punchy.** Screenshake, hitstop, oversized hitmarkers, damage numbers. If you
  did something, the screen should tell you loudly.
- **No fades.** Snap between states. Fades read as slow and polite; this shouldn't be either.
- **Funny is allowed.** It's a game about monkeys shooting each other with bananas. Lean in.
- **Readable underneath the noise.** ULTRAKILL is chaotic but you can always find your health.
  Garish is fine, confusing isn't.

---

## Checking it still works

Six suites, all runnable from a closed editor. Unity has to be **shut** or batch mode refuses to
open the project.

```
"C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe" -batchmode -quit -nographics   -projectPath "C:/DevProjects/Unity/OldProjects/FPS/GorillaWarfare"   -executeMethod WeaponCheck.Run -logFile -
```

| suite | what it holds down |
|---|---|
| `WeaponCheck` | weapon balance, roles, models, audio banks, viewmodel framing |
| `PlayerModelCheck` | the gorilla: scale, rig, bones, gait |
| `RemoteCopyCheck` | the copy of you other people see, and the invariants tying it to yours |
| `SceneCheck` | opens both shipping scenes, looks for missing scripts and magenta materials |
| `MatchCheck` | the match rules, including a whole gun game played out |
| `PlayModeProbe` | **runs the actual game** in Photon offline mode |

`PlayModeProbe` is the odd one — it needs play mode, so drop `-quit` and let it exit by itself:

```
"C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe" -batchmode -nographics   -projectPath "C:/DevProjects/Unity/OldProjects/FPS/GorillaWarfare"   -executeMethod PlayModeProbe.Run -logFile -
```

It spawns a player, checks it's carrying what the match rolled, kills it, and watches it come
back. What it can't do is see a second client — offline mode is one player — so anything about
remote copies still needs two people.

`AssetFixups.All` reapplies import settings. `ProjectCleanup.Run` strips leftovers back out of
the player prefab if they ever reappear.

---

## M0 — Stop the bleeding

Regressions and broken basics. Nothing else matters while the game can't be played.

- [x] **You can't damage anyone.** Fixed: the player's only collider is now the
      `CharacterController`, and remote copies disable it — a disabled CharacterController has no
      collider, so the raycast passes straight through everyone. Keep it enabled and only strip
      `PlayerMovement`; a CharacterController you never call `Move()` on does nothing.
- [x] **Gun audio is bursts, not single shots.** Measured: pistol had 5 shots, magnum 3. Cut a
      single shot out of each and re-measured to confirm one onset apiece.
- [ ] Verify shooting end to end with two clients before calling M0 done.

**Done when:** two players can shoot, damage and kill each other, and each shot is one bang.

---

## M1 — The monkey

- [x] `PlayerController` builds `MonkeyRig`, which spawns the model and drives the bones
- [x] `CHIMP_L` and its subtree removed from the prefab — 15 objects, no dangling fileIDs
- [x] Hide own body from own camera, keep the shadow (`ShadowsOnly`)
- [x] Reparent weapons onto the hand on remote copies — they sat on `CameraHolder`, which is
      first-person only, so others saw a gun floating at head height
- [x] Bones carry a 100x scale from the FBX and everything parented to one inherited it — the
      weapon on a remote player's hand was a hundred times too big and the head hitbox was a
      26 metre sphere you couldn't walk into
- [x] Left arm was mirrored, so the pose that lowered the right arm raised the left one
- [x] It renders — `PlayModeProbe` builds the remote rig, stands it in front of the camera and
      photographs it
- [ ] Tune the gait numbers against the real model; current values are guesses
- [x] Arms reach forward rather than gripping — replaced the fixed angle pose with two bone IK
      aimed at a grip point in front of the chest. A fixed rotation can't hold anything: the
      hand lands wherever the angles put it, which on this rig was straight out front

**Done when:** other players look like a monkey that walks, aims where it's looking, and holds
its weapon.

---

## M2 — Weapons

Banana-shaped guns. Plural — the point is variety, not one gun.

- [x] Banana models, generated in Blender (tools/banana_generator.py)
- [x] Weapon definitions as ScriptableObjects: damage, fire rate, spread, auto vs semi, range
- [x] Five weapons: pistol, shotgun, rifle, sniper, peel. Roles are asserted in WeaponCheck -
      no two may overlap, and anything that can one-pull a full health player has to pay for it
      in fire rate or range.
- [x] Melee weapon (Peel) - gun game's final rung
- [x] Runtime loadout, so a gamemode can hand out whatever it likes
- [ ] Per-weapon sounds (the bank-by-name lookup already supports this)
- [ ] **Better gun audio.** Current clips are thin and clicky - they read as a click, not a bang.
      Wants weight and low end. The two in there now are trimmed .22 recordings, which is a
      small calibre and sounds like it. Needs bigger source material.
- [x] **Pistol is the default weapon** (currently index 0 is whatever the prefab ordered)
- [x] **Rifle is automatic** - fires while held, at a fixed rate
- [x] **Maths-based recoil, not animation.** Each shot pushes the view up along a defined
      pattern, then recovers smoothly. Pattern is data, so each weapon can have its own - and a
      learnable pattern is the thing that makes a spray skilful rather than random.
- [x] Muzzle flash, weapon sway
- [x] **Ammo and reloading.** Neither exists at all right now — you have infinite bullets and no
      reload, so every gun is a hose. Fire rate is the only thing separating them until this
      lands, which undercuts the whole point of having several.
- [x] Fire rate limiting — `SingleShotGun` fires once per click with no cooldown, so an auto
      weapon has nothing to hold it back

Balance is asserted in `Assets/Editor/WeaponCheck.cs` rather than eyeballed - the rifle has to
win on sustained dps or the semi-auto is strictly better and nobody picks it. That check caught
exactly that: pistol was doing 221 dps against the rifle's 210.

Current: pistol 34 damage at 5/s semi (3 shots to kill, accurate, 12 round mag), rifle 21 at 10/s
auto (5 shots to kill, sprays, 30 round mag).

Asset names stay as roles, because the gun game ladder is defined in power order and
`Pistol -> Shotgun -> Rifle -> Sniper -> Peel` says what each one does at a glance. What players
read lives in `itemName` and is set by `WeaponNaming`:

| role | on screen | why |
|---|---|---|
| Pistol | **Cavendish** | the supermarket banana - ordinary, dependable, everyone starts with it |
| Shotgun | **The Split** | two bananas taped side by side, and a split is two halves in one dish |
| Rifle | **The Bunch** | a lot of bananas at once, which is also what it does |
| Sniper | **Big Mike** | Gros Michel, the cultivar wiped out in the fifties, and longer than a Cavendish |
| Peel | **Slip Hazard** | what's left after you eat one, and what everyone does about it |

### M2 landed

Five weapons with separate roles, shapes and colours. Deterministic recoil you can learn.
Ammo, reloading, fire rate. Hitboxes with headshots at x2. Damage falloff. Per weapon sounds.
Muzzle flash, sway, view arms, and a HUD with a reactive crosshair and hitmarker.

Moved out rather than dropped:
- two handed poses on remote players -> M7, it's a rig job
- melee swing arc and animation -> M7, same
- death and menu confirm sounds -> M4, they're still the sci-fi placeholders
- hitbox alignment against the actual mesh -> needs eyes on it, see Unverified below

**Done when:** ~~you can carry several distinct banana weapons that feel different to fire.~~ DONE

---

## M3 — Game loop

Two modes.

**Shared plumbing**
- [x] Match state machine: warmup → live → over → next match
- [x] Round timer, replicated, visible
- [x] End-of-match scoreboard with a winner
- [x] Respawn delay instead of instant, with a camera so death isn't a black screen
- [x] Master client owns match state; survives host migration
- [x] Mode picked in the lobby by the host, live, and shown in the room browser as it changes
- [x] Scores reset between matches — PUN never clears player properties by itself, they even
      follow you into the next room you join
- [ ] Next *map* rather than next match — that's M6, there's only one map to rotate to

**Deathmatch (timed)**
- [x] Each match rolls a random set of 3 weapons; everyone carries the same three
- [x] Most kills when the clock runs out wins

**Gun game**
- [x] Fixed weapon ladder, 2 kills to advance
- [x] Everyone works up the same order
- [x] Final rung is melee — killing with it wins the match
- [x] Melee weapon exists (Peel, landed in M2)

Everything about a match lives in room custom properties rather than in fields, which is what
makes late joins and host migration fall out for free instead of each needing its own catch-up
path. The clock is a deadline, not a countdown: `PhotonNetwork.Time` is server synchronised, so
every client works out the remaining time itself and nobody broadcasts a tick.

`MatchCheck` plays a whole gun game against the rules with no server — nine kills, five rungs,
in order. `PlayModeProbe` runs the actual game in Photon's offline mode and checks a player
spawns with the weapons the match rolled, dies, and comes back.

**Done when:** ~~both modes can be picked, played start to finish, and declare a winner without
anyone touching anything.~~ DONE, apart from playing it with a second person.

---

## M4 — Audio

- [x] Death sound - was the sci-fi explosion, now a low organic thud
- [x] Menu confirm and back - were the same problem
- [x] Melee swing, its own sound rather than a borrowed punch
- [x] Reload - it played a random clip out of the UI bank, so reloading sounded like
      clicking a button. It's fruit now: a peel, a bite, and a fresh one out of the bunch
- [x] Hit confirmation, with a separate brighter one for headshots. It used to play a generic
      impact, which is the same sound a shot into a wall makes - so the one thing you most
      wanted to know was indistinguishable from missing
- [x] Kill sound, downward where the hit goes up, and only for the person who did it
- [x] Music: menu and combat, crossfaded, hosted on RoomManager so it survives the scene change
- [x] Volume mix pass - every level lives in GameAudio rather than at each call site

The feedback sounds are synthesised (`tools/sound_generator.py`) rather than sourced. The pack
sounds standing in were wrong in specific ways and finding replacements means trawling for
something that happens to fit; these were written to fit, and they're CC0 by construction and
tunable by editing a number.

Music is sourced, because generated music goes wrong quickly and the failure mode is a tune
that's noticeably bad rather than merely absent.

`AudioCheck` covers it: every bank the code names has clips, every clip asked for by name
exists, no gun clip contains more than one shot (the bug that shipped), nothing is inaudible or
clipped, and the music loops without a click.

**Done when:** ~~everything has a sound and the mix is even.~~ DONE, but nobody has heard it -
see Unverified.

---

## M5 — UI

The big aesthetic one. See the philosophy section above.

- [ ] Main menu rebuilt to the ULTRAKILL/Cruelty Squad direction
- [ ] Lobby and room browser restyled to match
- [ ] In-game HUD: health, ammo, timer, scores. **A temporary IMGUI one exists**
      (`CombatHud.cs`) with ammo, hitmarker and a reactive crosshair - it works but it's
      programmer art and gets replaced wholesale here.
- [x] Hitmarkers (headshots read differently)
- [x] Health as ten blocks and an oversized number, flat saturated colour, stepping rather than
      sliding. The old screen space bar is out of the prefab
- [x] Loadout reveal during warmup — names your weapons one at a time before the match goes live
- [ ] Damage numbers
- [x] **Kill feed** — who killed who with what, top corner, fading entries. Laid out from the
      right so names line up down the feed instead of jittering with their length, and anything
      you were part of is drawn brighter — in a room of eight the feed is mostly other people's
      business and your own kills shouldn't have to be hunted for.
- [x] **Join / leave messages** — Photon had always fired the callbacks with nothing listening,
      so people vanished mid-fight with no explanation, which reads as a bug rather than as
      someone closing the game.

Kills, joins and leaves share one list rather than having one each. They compete for the same
few lines of screen and the only thing deciding what you see is what happened most recently;
two feeds would either overlap or need a third thing to arbitrate.
- [ ] **Settings**, with:
  - [ ] Crosshair — shape, size, thickness, gap, colour, dot, outline
  - [ ] Graphics — resolution, fullscreen, quality, FOV, and room for shaders later
  - [ ] Sensitivity — plus invert, and per-scope multiplier if zoom happens
  - [ ] Audio — master, SFX, music separately
  - [ ] Keybinds — full rebinding
- [ ] Settings persist and apply live, not on restart

Note: a previous settings menu was built and reverted at your call. The code is recoverable from
`git cherry-pick ba4405e` if any of it is worth keeping.

---

## M6 — Maps

- [ ] **Osama bin Laden's compound** as the first real map. Walled compound, inner courtyard,
      multi-storey building — that layout is genuinely good deathmatch geometry: clear sightlines
      across the yard, tight interior fights, roof access.
- [ ] Replace the seven cubes and four planes
- [ ] Map selection in the lobby
- [ ] Spawn system that doesn't drop people on each other
- [ ] Second map

---

## Later polish

- [ ] Bananas could bruise and spot rather than just tint, and the eat-and-swap on reload wants
      an actual animation instead of the model simply changing colour
- [ ] Ammo pickups, now that magazines are finite and a weapon can genuinely run dry

## M7 — Art and shaders

- [x] Map surfaced with a generated 1m grid and flat panels instead of a giant eyeball and a
      wall of embers. Not the art pass — the minimum needed to see a dark gorilla against a
      wall, and something for Quake movement to read speed against
- [x] Impact marks multiply rather than draw over, so they come out as a darker version of
      whatever surface they land on. Blood is red, sticks to the body, and goes when it does
- [ ] Replace the remaining placeholder textures (the menu still uses them)
- [ ] Environment art matching the philosophy
- [ ] Post-processing: the palette-mangling that sells the Cruelty Squad look
- [ ] Screenshake and hitstop
- [x] **Two handed weapon poses.** Both hands solve to their own target, the left further along
      the weapon than the right. `twoHanded` on the GunInfo decides: the Cavendish and the Slip
      Hazard are one handed and put the off hand on the hip, everything longer braces with both.
- [ ] **Melee swing.** The Peel is a 2.4m hitscan with no arc and no animation, so it reads as
      an invisible short gun rather than a swing.
- [ ] **Check hitbox alignment against the mesh.** They're spheres at bone origins and have
      never been looked at next to the actual gorilla, so they may not match its shape.

---

## Movement tuning

Separate from M0 because it needs a person playing it, not a fix.

- [x] Numbers replaced with converted Quake 3 / Source defaults rather than guesses — 1 unit is
      1 inch in both, so units/s * 0.0254 = m/s. `maxGroundSpeed 8.13 / groundAccel 10 /
      friction 6 / stopSpeed 2.54 / airAccel 100 / airSpeedCap 0.762 / jump 6.86 / gravity 20.32`.
- [ ] Play it and see whether the sourced values actually feel right here. They're right for
      Quake; this has a different scale and a different character.
- [ ] **Shift is now walk, not sprint.** Source-style: you run by default and shift slows you
      down. That's inverted from the old build and you haven't tried it yet, so it may just feel
      wrong.
- [x] Auto-bhop off. Holding space to keep speed for free was most of the skill gone.

## Unverified

Things the checks can't reach, so they need a person:

- whether the recoil, gait and weapon framing feel right
- whether the hitboxes line up with the gorilla visually
- whether the bananas read as their weapons at a glance
- **all of M4.** Every sound is verified to exist, to be one shot, and not to clip. Whether any
  of it sounds *good* is not something a check can answer, and the synthesised ones especially
  are worth a listen before they're trusted.
- the scope overlay composited over the game - IMGUI doesn't appear in a camera render, so the
  mask is checked on its own and the rest is unseen
- **anything that needs a second client.** The probe runs a real match in offline mode, which
  covers spawning, loadouts, dying and respawning — but offline mode is one player, so it can't
  see a remote copy of anybody. Weapons on someone else's hand, replicated aim, and the kill
  feed firing on a client that didn't do the killing all still need two people.
- whether match and respawn lengths feel right. Warmup 8s, deathmatch 5min, gun game 10min,
  scoreboard 12s, respawn 3s — all guesses, all one field each in `MatchState`.

## Known limitations

Not tasks, but things that are true and worth knowing before they bite.

- **Hit registration is client-authoritative.** The shooter raycasts locally and tells the
  victim they were hit. Fine among friends, trivially cheatable if this ever goes wider.
- **PUN 2 is end-of-life.** No more updates from Exit Games. It works and we deliberately chose
  to stay (see below), but it won't get fixes.
- **The Photon App ID is committed** in `PhotonServerSettings.asset`, and the repo is public.
  It's a client-side ID so it was always going to ship inside builds, but anyone reading the repo
  can now burn your free-tier quota. Worth regenerating if the game gets any attention.
- **No anti-cheat, no server authority** of any kind. Correct call at this scale; just don't be
  surprised later.
- **Everyone has to be on the same build.** PUN sends an RPC as an index into `RpcList` in
  `PhotonServerSettings`, so two clients with different lists would resolve the same index to
  different methods. `AppVersion` is empty, which means mismatched builds can still find each
  other's rooms rather than being kept apart. Fine while everyone updates together.
- **Region is automatic again, properly this time.** `FixedRegion` was cleared a while back but
  `DevRegion` was still `uae`, and that overrides the best-region pick in the editor and in any
  development build. So the editor and a release build could land on different regions and not
  see each other's rooms. Both pick their own best region now. If that splits people up, pin
  everyone by setting `FixedRegion` in `PhotonServerSettings.asset` — one field, and it beats
  automatic for a group that's geographically spread.

## Decided against

Recorded so it doesn't get quietly relitigated.

- **P2P instead of Photon.** Considered properly and rejected. PUN is already peer-hosted — the
  master client owns game state and Photon supplies matchmaking and relay, not a game server.
  Real P2P still needs NAT punchthrough from Steam, EOS or a relay, so the migration bought very
  little for a lot of work. Revisit only if PUN's limits actually start hurting.
- **A clip-based animation library.** Superseded by driving the bones directly, which is what
  M1 does.

## Ongoing

- [ ] Player layer — **downgraded.** The original reason was the GroundCheck trigger eating
      shots, and GroundCheck no longer exists; the ownership check covers self-hits. Only real
      remaining use is camera culling masks for shaders later, so it can wait for M7.
- [x] ~~Delete `NetworkDebugOverlay` and the `LogSpawn` calls~~ — done, the bug they were for is fixed
- [ ] `Game.unity` will do a Unity 6 format migration on first open

## Recently closed

- The copy of you that other people saw was still holding the 2024 M1911 and AK74, because a
  loadout was only ever built for the owner
- The first person arms were destroyed one frame after being built — the loadout cleared the
  whole holder and the arms live in it. The geometry was right the whole time
- Weapon switching threw `IndexOutOfRangeException` on every other client
- Scores lost a kill whenever two landed inside one server round trip

- Late-join invisibility — fixed by rewriting the spawn path to Photon's own pattern and
  deleting `PlayerManager` entirely
- Movement — Quake/Source acceleration on a `CharacterController`
- Footsteps firing mid-air
- 36 assorted bugs, see `bug-log.md`
