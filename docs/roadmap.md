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

Seven suites, all runnable from a closed editor. Unity has to be **shut** or batch mode refuses to
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
| `AudioCheck` | every bank has clips, named clips exist, nothing silent or clipping, shape checks (the slide scrape, the shield break, the vine thwip, the wind bed) |
| `PlayModeProbe` | **runs the actual game** in Photon offline mode |

`PlayModeProbe` is the odd one — it needs play mode, so drop `-quit` and let it exit by itself:

```
"C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe" -batchmode -nographics   -projectPath "C:/DevProjects/Unity/OldProjects/FPS/GorillaWarfare"   -executeMethod PlayModeProbe.Run -logFile -
```

Drop `-nographics` too if the run needs to produce real screenshots — with it, every `Capture()`
call is silently skipped and the last real render can sit in `Logs/probe-shots/` looking current
for days. See `bug-log.md`'s fourth pass.

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

- [x] Banana models, generated in Blender (`tools/banana_variants.py` - supersedes the original
      `banana_generator.py`, deleted once superseded; the new script derives all five weapons
      from one real modelled banana instead of building them from scratch)
- [x] Weapon definitions as ScriptableObjects: damage, fire rate, spread, auto vs semi, range
- [x] Five weapons: pistol, shotgun, rifle, sniper, peel. Roles are asserted in WeaponCheck -
      no two may overlap, and anything that can one-pull a full health player has to pay for it
      in fire rate or range.
- [x] Melee weapon (Peel) - gun game's final rung
- [x] Runtime loadout, so a gamemode can hand out whatever it likes
- [ ] Per-weapon sounds (the bank-by-name lookup already supports this)
- [ ] **Better gun audio.** Current clips are thin and clicky - they read as a click, not a bang.
      Wants weight and low end. The two in there now are trimmed .22 recordings, which is a
      small calibre and sounds like it. Needs bigger source material. Reported 2026-08-29 as not
      punchy enough, the split and big mike specifically - **partly addressed** the same day
      without new recordings: firing volume is now scaled by `GunInfo.Weight` (the same 0-1
      figure the shake and recoil kick already use) instead of every gun playing its layered
      shot at the same flat `ShotVolume`, so the split and big mike (Weight 0.86-0.98) come out
      noticeably louder than a pistol tap (Weight 0.3) rather than identically loud. Muzzle flash
      got the same treatment - `MuzzleFlash.Scale(weight)` sizes the burst, the light and its
      range per weapon, where every gun previously built an identically sized flash regardless of
      what it fired. The recordings themselves are still thin .22s; a mix-level fix can make the
      big guns louder and flashier than the small ones, it can't make the sample itself sound
      like a shotgun. That still needs bigger source material.
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

Three modes now — Team Deathmatch landed after this section was last written and was never added
below. Corrected 2026-08-22.

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

**Team deathmatch**
- [x] Same weapon roll as free-for-all deathmatch, scored by side instead of by player
- [x] Sides assigned through `PlayerColours.AssignTeams`, red against blue
- [x] Winner is whichever side has the higher total when the clock runs out, tied totals draw
      (`WinningTeamKey`, separate from the free-for-all `WinnerKey` so a team result never gets
      misread as a single player's). Read as kills specifically until 2026-09-03: this line was
      never updated when the style-score rework made style score decide free-for-all deathmatch,
      so `PlayerColours.TeamScore` was quietly still summing kills for the team version of the
      exact same mode the whole time that rework was live. Now reads `MatchModeInfo.
      RanksByStyleScore` like everything else this pass switched to, so the team total, the
      scoreboard order and the actual match winner all agree.

Everything about a match lives in room custom properties rather than in fields, which is what
makes late joins and host migration fall out for free instead of each needing its own catch-up
path. The clock is a deadline, not a countdown: `PhotonNetwork.Time` is server synchronised, so
every client works out the remaining time itself and nobody broadcasts a tick.

`MatchCheck` plays a whole gun game against the rules with no server — nine kills, five rungs,
in order. `PlayModeProbe` runs the actual game in Photon's offline mode and checks a player
spawns with the weapons the match rolled, dies, and comes back.

**Done when:** ~~all three modes can be picked, played start to finish, and declare a winner
without anyone touching anything.~~ DONE — played on 3-4 real clients 2026-08-16, see
`working-notes.md`. Team deathmatch specifically hasn't had its own dedicated playtest since
landing after that date.

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

The feedback sounds are synthesised (were `tools/sound_generator.py`; run once, its output
committed, and removed afterward - the generated clips are what's actually in
`Assets/Resources/Audio`, not the script) rather than sourced. The pack
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

- [x] Main menu rebuilt to the ULTRAKILL/Cruelty Squad direction — Ryaan did this himself
- [x] Lobby and room browser restyled to match — same
- [x] In-game HUD: health, ammo, timer, scores. The two IMGUI scripts are gone; `GameHud` drives
      a real Canvas built into the game scene by `HudBuilder`, so every position, size, colour
      and font is scene data rather than a constant in an `OnGUI` call
- [x] Mode selector as real objects, for the same reason
- [x] Five fonts now, four imported originally plus **Jersey 10** added 2026-08-29
      (`Assets/Fonts/Jersey10`, Google Fonts, OFL). Helvetica Punk was defended in an earlier pass
      of this same day as "the only one of four a number is legible at a glance," reasoning that
      held up against the other three but missed the actual ask - hated directly and specifically,
      as its own complaint separate from the plates/brackets/glow that also got rejected. Jersey
      10 replaces it for the in-match HUD only; menus stay on Helvetica Punk/Chomsky for now, per
      Ryaan's own request not to touch that side yet.
- [x] Hitmarkers (headshots read differently)
- [x] Health as ten blocks and an oversized number, flat saturated colour, stepping rather than
      sliding. The old screen space bar is out of the prefab
- [x] Loadout reveal during warmup — names your weapons one at a time before the match goes live
- [x] Damage numbers — pooled, reprojected each frame so they stay stuck to who you hit
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
- [x] **Hit-flash** — a white flash across a gorilla's body the instant a hit lands, broadcast to
      whoever's watching rather than only felt by the victim (the damage RPC itself only ever
      reaches them). Added 2026-08-22, in `MonkeyRig.Flash()`, triggered from `BulletDecal.Spawn`
      since that already runs on every client and already works out locally whether a body was hit.
- [x] **A heartbeat** — the critical-health screen edge (`GameHud.UpdateAdrenaline`) was visual
      only; added the audio half the same day, ticking on the exact beat the edge already pulses on.
- [x] **HUD punch** — the slide combo's punch-on-change scale (`GameHud.UpdateSlideCombo`) was the
      only thing on the HUD that reacted to a change instead of snapping to it. Extended to the
      health number, the ammo count and each kill feed line as it arrives, 2026-08-22.
- [x] **Directional damage indicator** — already built and wired (`GameHud.ShowDamageFrom`,
      called from `PlayerController.RPC_TakeDamage`) when this was reviewed 2026-08-22; not new,
      just confirmed rather than rebuilt from a guess that it was missing.
- [x] **Screenshake now shakes the UI too** — reported directly, since a screen-space overlay
      canvas doesn't move with the camera at all and was the one thing on screen a hit never
      touched. `Juice.Amount` exposes the current shake normalised 0-1; `GameHud` reads it to
      offset a shake root with the same Perlin-noise wobble the camera itself uses. First attempt
      drove the canvas's own RectTransform directly and did nothing at all - Unity ignores an
      overlay canvas's own transform when placing it. Fixed 2026-08-23 by shaking a child
      RectTransform instead, which has no such exemption - see `bug-log.md`'s ninth pass.
- [x] **The in-game HUD's visual language, reworked** — reported 2026-08-29 as not matching this
      file's own design philosophy above (never actually checked against it since M5 first
      shipped, just built functional and left). The gameplay HUD only — menus are Ryaan's own.
      Took three attempts to land, all logged in `bug-log.md`'s sixteenth pass:
      1. A first pass built the HUD out of solid black plates and hard corner brackets, styled
         after a reading of "ULTRAKILL/Cruelty Squad" that turned out to be closer to a tactical
         shooter's HUD (Counter-Strike, Valorant) than either reference actually looks like -
         rejected directly as leaning into that vibe rather than away from it.
      2. Shown as HTML/CSS mockups rather than built in Unity, on the theory that iterating in a
         browser would be cheaper than round-tripping the engine - rejected for the medium itself,
         not just the specific attempts: a CSS approximation doesn't render like TMP's actual SDF
         font pipeline, and re-litigating font and layout choices against a fake informed nothing
         real. Should have gone to Unity and a real screenshot from the first attempt.
      3. Built for real. **Jersey 10** (Google/Fonts, OFL, `Assets/Fonts/Jersey10`) replaces
         Helvetica Punk for the in-match HUD specifically - a condensed display face built on a
         pixel grid without being an 8x8 arcade font, closer to the skybox's own posterised,
         faceted retro than a literal NES look. Every label TMP builds in `HudBuilder.Text()` gets
         a hard black SDF outline by default (`ShaderUtilities.ID_OutlineWidth/Color`) rather than
         floating raw - the same move `ScreenOutline` already makes on every 3D object in view, so
         the HUD reads as drawn in the game's own ink instead of borrowing another game's chrome.
         No plates, no brackets, no glow, no scanline overlay - all removed. The health bar got a
         hard black frame and a stretched highlight strip along its top half (reads as a flat
         swatch otherwise, reported directly - a colour block with no edge is exactly that,
         wherever it sits over the map).
      Verified each time by an actual screenshot of a real offline match
      (`Assets/Editor/HudPhotographer.cs`, kept as a permanent tool), not eyeballed math or a
      browser preview - see the sixteenth pass for the ammo-frame sign error and the flat-bar
      complaint that only showed up once rendered for real.
      4. Sent to real playtesters, who came back with "personality to it before, generic slop
         now" - a fair hit on step 3, which had spent the whole pass subtracting (no plates, no
         glow, no brackets) without adding anything specific to *this* game back. Fixed same day,
         `bug-log.md`'s seventeenth pass:
         - Health and ammo bars now walk `GunInfo.RipenessFor`'s own green-to-brown palette - the
           health bar as a literal "bananameter," the ammo bar tinted by the *actual* magazine's
           ripeness rather than an invented ammo colour rule, so the HUD and the banana in your
           hands brown together.
         - Every bar leans six degrees rather than sitting flat-horizontal, reported directly
           against ULTRAKILL's own diagonal HUD language. `Track` (and everything under it) is
           now a child of the border frame specifically so the whole cluster rotates as one rigid
           piece rather than each element spinning round its own pivot.
         - The big centre callout (a kill, a rung-up, "GET READY") now punches in and settles
           rather than appearing at a flat scale of 1 - the one piece of text on the whole HUD
           that had never gotten the "arrives big" treatment everything else already had.
         - Caught by the same GunGame-mode render that checked the ladder: `slideCombo`
           ("BANANAS!!!") was a child of a *zero-width* Centre panel, so its own "anchor right"
           setting had silently done nothing since it was built - sitting 70pt left of true centre
           collided with the kill/ladder title the moment Jersey 10's wider glyphs reached that
           far. Reparented onto the root canvas, where the anchor means what it says.
      5. Reviewed again same day - four more direct corrections, `bug-log.md`'s nineteenth pass:
         - The ammo bar (added in step 3, mirroring health's) reported as looking "very weird" -
           removed outright, back to a bare number. Not every readout wants a bar under it.
         - The health bar is a **literal pixel-art banana** now, not a tinted rectangle -
           "what you did is not what I meant by bananameter." Drawn procedurally
           (`HudBuilder.BananaSprite`) the same way every other texture on this HUD already is,
           in three stacked layers using the one sprite: an always-visible dim husk, a pale trail
           that lags behind a drop and catches up (the inertia asked for), and the live reading
           on top. Leans into the corner now, the opposite direction the old rectangular bar's
           lean read as pointing.
         - `slideCombo` ("BANANAS!!!") got the ULTRAKILL/DMC-style meter asked for - a fill bar
           reading `PlayerMovement.ChainWindowFraction` (new, added for this), full the instant a
           slide lands and draining toward empty by the time the chain would expire, so the bar
           itself is the "hurry up" cue the rank name alone never gave.
         - The weapon name's outline widened from the HUD's shared default - reported as wanting
           more depth and visibility; `Text()` now takes an optional per-label outline override.
      6. The banana meter's own shape was reported back directly, same day: a procedurally-drawn
         silhouette (step 5) was "not what I meant," and pointedly not the way to be sourcing art
         going forward. Replaced with a real sprite - one frame of "Spinning Banana" by
         lawrence_laz (OpenGameArt.org, CC0), trimmed and imported at
         `Assets/Textures/UI/BananaHealth.png`. The rank meter's font changed too - "so bad...
         you'd have to change the font for it to stick out" - **Anton** (Google Fonts, OFL) now
         drives the slide rank and the kill callout specifically, a poster-weight face distinct
         from Jersey 10's readouts. Every label's outline widened again (0.38 to 0.55) and picked
         up a soft dark underlay on top of it - "genuinely blends in with everything else" was
         still true of a hard outline alone against a bright, busy background.
      7. Two more direct reworks, 2026-08-29 - see `bug-log.md`'s eleventh pass for the full
         account:
         - **The health bar rebuilt for real** - "style it like the cruelty squad healthbar,"
           named directly, with a list of specifics: top left instead of bottom left, vertical
           and depleting downward instead of horizontal, the peel and the banana as distinct
           layers rather than one shape doing both jobs, a backshadow/outline so "how much you
           lost" and "how much there should be on full" read as a comparison, and a separate
           animated overshield bar beside it tilted like ULTRAKILL's own HUD. `BananaLayer` fills
           `Vertical`/`OriginVertical.Bottom` now instead of `Horizontal`/`Left`. No second sprite
           was sourced for the peel - both layers reuse the one already-credited `BananaHealth.png`
           (see its own CREDIT.txt), tinted differently: the outer layer sits at full size
           permanently, coloured like a peel, and the inner layer is what depletes - which reads as
           "eaten down to the skin" as you take damage, a better fit for the metaphor than an
           unrelated second asset would have given anyway. The overshield got its own vertical
           gauge (`OvershieldMeter`, -25° tilt) rather than sharing the main bar's track, with a
           slow idle glimmer while it's up so it reads as animated even between fills.
         - **The slide-only combo meter unified into a full style score** - "I hate the bar the
           most honestly," with DMC and ULTRAKILL named as the actual reference. See "Style score
           and the combo/scoring rework" below for the mechanic; this entry is the HUD half. The
           rank text (`slideCombo`, kept its field name across the rework) moved from roughly
           screen-centre-right up to the true top right corner, now reads `"RANK  xMULTIPLIER"`
           instead of a bare rank word, and a new always-on `styleScoreText` sits above it as the
           big number - the ammo/health rule ("the number you actually read is the big one")
           applied to the new system. The kill feed moved down to the middle right to make room,
           per direct instruction.
- [x] **Settings**, with:
  - [x] Crosshair — size, thickness, gap, colour, dot, outline, plus a dynamic/override toggle
  - [x] Graphics — resolution, fullscreen, quality level, FOV, shader stack preset, motion blur
  - [x] Sensitivity — plus invert and a separate ADS multiplier
  - [x] Audio — master, SFX, music separately
  - [x] Keybinds — full rebinding, read live so nothing else has to poll a stale key
- [x] Settings persist and apply live, not on restart — every setter in `GameSettings` writes the
      preference immediately, nothing waits on an apply button
- [x] **Reset to default, per page** — 2026-09-03, from a full-codebase review's settings-UX
      research. The one reset button used to always call `GameSettings.ResetAll()`, taking
      keybinds and every tab with it. `ResetAim`/`ResetAudio`/`ResetVideo`/`ResetCrosshair`/
      `ResetKeys` now exist alongside it, and the screen's reset button calls whichever matches
      the open tab, relabelling itself to say which. `ResetAll` still exists for the "everything
      is broken" case.
- [x] **Press-to-aim as a toggle**, alongside hold-to-aim — `GameSettings.AimToggle`, read in
      `PlayerController.Update`. Motor-accessibility research turned this up as the single most
      commonly requested toggle-vs-hold option in the genre. Toggle-to-crouch/slide was considered
      and deliberately skipped — that key shares a long, specific bug history with the slide
      buffer and air brake, and touching it for an option nobody directly asked for wasn't worth
      the risk.
- [x] **An optional PSX filter** — `PsxFilter.cs`/`PsxFilter.shader`, a real PostProcessing v2
      custom effect, its own toggle outside the preset ladder like motion blur. Colour-depth
      quantization plus an ordered dither, fixed at a tasteful intensity rather than exposed as a
      slider. Deliberately no vertex snapping or affine texture warping — both need touching every
      shader already in the project to do safely, not something a single post-process pass can
      add. See `bug-log.md`'s twenty-ninth pass for the full account, including the first attempt
      at the PPv2 API that didn't compile.
- [x] **The tab scoreboard, rebuilt** — 2026-09-03. Reported directly as looking "pretty bad" and
      never actually ranking anyone (rows were join-order, never re-sorted). `ScoreboardItem.cs`
      (one MonoBehaviour per player) retired in favour of the same pooled-`TMP_Text`-row shape
      `GameHud.UpdateStandings` already uses — sorted by whatever `MatchModeInfo` says the mode is
      actually decided by, 1st-3rd medal-coloured via the newly-shared `RankDisplay.cs`, a live
      team-total header in a team mode, a best-streak flourish, and a real visual pass
      (`ScoreboardBuilder.cs`, new — the scoreboard never had a builder before this) matching the
      rest of the HUD's outline-plus-underlay treatment. Surfaced a real bug along the way: see
      the Team deathmatch section above for `PlayerColours.TeamScore` having quietly kept deciding
      team matches by kills after the style-score rework.

Corrected 2026-08-22 — this whole section read as unbuilt (`[ ]` throughout) and wasn't; found
while answering an unrelated question about what's left before Steam. `GameSettings`,
`SettingsMenu` (five tabs: Aim, Audio, Video, Crosshair, Keys) and `Sandbox` are all in and
working. The previous reverted attempt this note used to point at is superseded — nothing left
worth cherry-picking out of `ba4405e`.

---

## Style score and the combo/scoring rework

Built 2026-08-29 - "I hate the bananas!!! style meter... I hate the bar the most honestly," with
DMC's and ULTRAKILL's own style meters named directly as the reference, and an explicit
constraint on the result: has to feel like that shape of mechanic without reading as a copy of
either. See `bug-log.md`'s eleventh pass for the full build account and "The in-game HUD's visual
language" above for the display half.

- [x] `StyleScore.cs` - one component, added mine-only the same way `PlayerMovement`/`SpeedRush`
      are. Tracks a multiplier (climbs on a kill, decays over four seconds of quiet, cut hard by
      a hit taken or a wall smash) and a running score (`multiplier x 100` per kill, banked
      permanently). Rank name is banana/gorilla-themed on purpose - PEELING, RIPE, GOING BANANAS,
      FULL SILVERBACK, RAMPAGE - specifically so the mechanic doesn't read as a lettered ULTRAKILL
      rank wearing a different font.
- [x] **Combines skill and movement combos** - a kill lands its base value, then a headshot, a
      no-scope (a `canAim` weapon's kill landed while not aiming), a point-blank finish (under
      3.5m), switching weapons since the last kill, and an active `PlayerMovement.SlideChain` all
      add to the multiplier gain on top of each other. No-scope and point-blank are classified on
      the *shooter's* own client at the moment of the shot (`SingleShotGun.FirePellet` calls
      `StyleScore.RecordShot`, stashing weapon/aim-state/distance per target actor) rather than
      reconstructed later, since by the time a kill is confirmed over the RPC round trip that
      context is already gone.
- [x] **Getting hit or hitting a wall hurts it**, per direct request to balance the mechanic
      rather than let it only ever climb. A hit taken cuts the multiplier to 75% of itself
      (`PlayerController.RPC_TakeDamage`); a wall smash - `OnControllerColliderHit` judging the
      speed the wall is about to remove, not the speed you were carrying, so a shallow graze at
      top speed never counts but a square hit at a much lower one still can - cuts it to 40% and
      resets the decay clock, with its own effect: a red/orange particle burst
      (`PlayerMovement.WallSmash`, reusing the same `MovementBurst` helper the ground slam's dust
      already uses), a camera shake scaled to impact speed, and `GameAudio.WallSmash` (no sourced
      clip yet, falls back to `Impact` pitched up - same graceful-empty-folder convention as every
      other bank in `GameAudio.cs`).
- [x] **The score decides deathmatch now, not kills** - "your score will show up on the
      leaderboard and this is now the determining factor for winning in deathmatches (anything
      that isnt gungame honestly)." `MatchState`'s end-of-match winner check now branches on mode:
      gun game keeps deciding on kills at its own timeout (the ladder is already gun game's
      progression system), everything else uses the new `RoomManager.StyleScoreKey` custom
      property instead. Written by each client into its own property directly rather than by the
      master the way kills are - the classification a kill earns only ever exists on the killer's
      own client, and unlike kills (where two different killers can race over one victim's death
      count) nothing else ever writes this specific player's own score, so there's no race to
      arbitrate. The post-match standings screen sorts by it and shows it alongside kills outside
      gun game, and a new "MOST STYLISH" award joins TOP BANANA/HEADHUNTER/ON A ROLL/CRASH TEST
      DUMMY.
- [x] **Fleshed out well past the original five bonuses**, reported directly as "only a few
      combos and its not that fun at all": long range (point blank's opposite), airborne, a
      blade-finish bonus for the signature melee weapon, a killstreak bonus, and a named multikill
      callout (DOUBLE PEEL/BUNCH KILL/GORILLA WARFARE/GOING FERAL). Full account in `bug-log.md`'s
      twenty-fifth pass.
- [x] **The multiplier starts on damage, banks on the kill.** "Your multiplier starts when you
      damage someone and do stuff but when you kill someone you actually get the points" - new
      `StyleScore.RegisterHitLanded()` grows the multiplier (small, cooldown-gated) the moment any
      hit connects; `score` itself still only ever changes at the kill, unchanged from before.
- [x] **Dummy kills now feed the meter.** Reported 2026-08-29 as "STILL does not work" after the
      previous pass's sweep had already checked every bug it could find *inside* `StyleScore`
      itself - the actual cause was outside it: a training dummy's death never goes through
      `PlayerController.RPC_Died`, which is the only place `RegisterKill` ever fires from, so
      every kill the sandbox could actually produce was invisible to the system. `IDamageable.
      TakeDamage` now returns whether that specific call was fatal; `TrainingDummy` answers `true`
      exactly once per death, `PlayerController` always answers `false` (a real kill still only
      ever credits via the RPC, same as before). New `StyleScore.RegisterDummyKill` shares its
      actual scoring math with `RegisterKill` through an extracted `ApplyKillGain` rather than
      duplicating it, and is wired into every place a shot can land a fatal blow on something
      that isn't a player. Full account in `bug-log.md`'s twenty-third pass.
- [ ] Tuning. Every number above - the per-cause bonuses, the hit/wall-smash penalties, the decay
      rate and window, the wall-smash speed threshold, the tier breakpoints - is a first pass, the
      same way every other feel number in this project has needed a person playing it before it
      could be trusted. See Unverified.

---

## Movement-tech combo

Built 2026-08-29, same day as the dummy-crediting fix above - "no slide combo text still which
should show up... make new movement tech combos and stuff... this includes grappling, grenade
jumping, bhopping, slide hopping, etc and make this be affected by you crashing into something."
A second, separate meter from the style score: traversal instead of combat, bottom left instead of
top right, and it never touches the score or the win condition. Full account in `bug-log.md`'s
twenty-fourth pass.

- [x] `MovementCombo.cs` - one component, mine-only, added by `PlayerController.Start` the same
      way `StyleScore` is. `Register(tech)` extends a chain and remembers the tech name;
      `Break()` (called from `PlayerMovement.WallSmash`, same trigger `StyleScore.
      RegisterWallSmash` already answers to) zeroes it outright.
- [x] **Four techs feed one chain** rather than each getting its own counter, so switching
      between them mid-run reads as one continuous run: a slide-hop entry, a jump landed inside
      `bhopGrace` of the last touchdown, a grapple attach (`VineGrapple.RPC_Attach`), and a
      self-knockback grenade jump strong enough to matter (`strength > 0.3f`, so grazing the edge
      of your own blast doesn't count).
- [x] Bottom-left HUD block, built to mirror the style meter's own shape (name + chain count,
      tilted, a draining bar) deliberately rather than inventing a second visual language - tilted
      the *other* way so the two corners lean inward symmetrically, with the meter bar sitting
      *above* the text since this cluster is near the bottom of the screen already.
- [ ] Tuning - the 3-second window, the bhop/grenade-jump qualifying thresholds, none of it played
      against yet. See Unverified.

---

## Tokens and the crate opening system

Built 2026-08-29 - "add the gambling crate stuff... make sure this is the BEST crate opening
feature ever." No actual rewards yet, by explicit request - this is the whole ceremony (earn
tokens, spend them, watch a carousel decide what you got) built and working end to end, with real
items left for later. Full account, including the design research behind it, in `bug-log.md`'s
twenty-sixth pass.

- [x] `PlayerWallet.cs` - tokens, persisted locally (`PlayerPrefs`, survives between sessions
      unlike anything Photon-networked would). Every player starts with 100.
- [x] **Reworked same day**: round-end reward is now exponential in that match's style score
      (`GameHud.RoundTokensFor`, base-2, capped at 50) rather than a flat amount - see
      `bug-log.md`'s twenty-seventh pass.
- [x] Five rarities (SCRAP/SPROUT/PRIMAL/MYTHIC/APEX, gray/green/blue/purple/gold) and three
      crates (Rotten/Ripe/Holy, 10/50/100 tokens - cut roughly 10x from the first pass the same
      day, to actually be reachable against the round-end reward), odds scaling with price.
- [x] The opening screen itself - a real carousel (masked viewport, eased deceleration, a tick
      sound tied to what's actually on screen), an escalating reveal scaled by rarity, built as a
      prefab (`CrateShopBuilder.cs`) the same way the settings menu is, so it's reachable from
      wherever a button ends up living.
- [x] **A title menu button**, next to Settings - answers "how do I access the crates."
- [ ] Real rewards. Every rarity currently resolves to "you got an APEX" and nothing else -
      that's the next real piece of work here, whenever there's actual loot to hand out.
- [ ] Tuning - the odds, the token amount, the spin duration, the reward curve's own 2500-score
      scale, none of it played against yet. See Unverified.

---

## M6 — Maps

Full plan - four map concepts, the compound and the silo picked as the first two to build, and
the map-voting design - lives in `ideas.md` (sections 5 and 6) rather than duplicated here.
Summary:

- [ ] **The Compound** as the first real map. Walled compound, inner courtyard, multi-storey
      building — clear sightlines across the yard, tight interior fights, roof access.
- [ ] Replace the seven cubes and four planes
- [ ] Map selection in the lobby, backed by map voting
- [ ] Spawn system that doesn't drop people on each other
- [ ] **The Silo** as the second map — small, vertical, maximally different from the compound

**The current jungle map got a density and verticality pass 2026-08-22** - not a replacement for
the above, which is still the real plan. `Tools/Gorilla Warfare/Expand the jungle map`
(`Assets/Editor/MapExpansion.cs`) adds climbable cliff clusters (staggered cliff_block_rock
tiers, real height now checked in the scene file after a bug where every tier landed at ground
level instead of stacking) built to reward the new movement tech, plus a denser scatter of the
existing prop kit across the open ground. Re-runnable and idempotent, same convention as
`MapDressing`: everything it places lives under one `~MapExpansion` group, replaced whole on
every run rather than accumulating. Explicitly a first pass on the numbers (tier count, scale,
how many clusters) - not played yet, see Unverified.

---

## Tried and dropped

- **Ambient jungle audio.** Built, sourced, and cut after listening to it. The CC0 forest bed
  that was available had wind chimes in it, and measuring the alternatives put numbers on why
  that was wrong: a bed wants a low crest factor and almost no distinct events, and the chime
  track ran 0.49 events a second. A dense cricket wall measured far better (crest 5.8 against
  9.3, 0.09 events a second) but by then the answer was that the feature was not worth the
  hunt. The component and the import tool are in git if it ever is.

## Later polish

- [x] The eat-and-swap on reload got its actual animation, 2026-08-29 - the old symmetric dip
      (down, hold, back up) was reported as unsatisfying at any speed tried. `SingleShotGun.
      UpdateReloadFlip` now plays three distinct beats instead of one shape held the whole time:
      a fast, hard drop out of view (down the hatch), a held moment fully retracted (the eating),
      then a sharp pull back that overshoots past rest on an `EaseOutBack` curve and settles - the
      flourish that reads as pulling a fresh one out rather than a mechanism sliding into place.
      Reload sound is per-weapon now too (`Resources/Audio/Reload/<WeaponName>`, same graceful
      fallback `Shoot/<WeaponName>` already uses) and pitched by the weapon's own `Weight` in the
      meantime - every weapon still shares the one sourced clip, nothing fruit-specific has been
      recorded yet, so the pitch shift is what keeps the pistol and the shotgun from sounding
      identical until real per-weapon audio exists. Bananas bruising and spotting rather than just
      tinting is still open, unrelated to either of these.
- [ ] Ammo pickups, now that magazines are finite and a weapon can genuinely run dry

## M7 — Art and shaders

- [x] Map surfaced with a generated 1m grid and flat panels instead of a giant eyeball and a
      wall of embers. Not the art pass — the minimum needed to see a dark gorilla against a
      wall, and something for Quake movement to read speed against
- [x] Impact marks multiply rather than draw over, so they come out as a darker version of
      whatever surface they land on. Blood is red, sticks to the body, and goes when it does
- [ ] Replace the remaining placeholder textures (the menu still uses them)
- [ ] Environment art matching the philosophy
- [x] **The skybox, rebuilt against a reference.** The old shader was a stylised jungle-canopy
      look (flat colour bands, god rays) - replaced entirely 2026-08-23, direct request against a
      screenshot: a smooth photographic gradient plus streaked, noise-based clouds and a glowing
      sun, no texture or cubemap involved, same procedural-shader approach the old one used. See
      `bug-log.md`'s twelfth pass, including why the first render came out half solid yellow (a
      stale material override, not the new shader itself).
- [x] **Post-processing: the palette-mangling that sells the Cruelty Squad look.** Corrected
      2026-08-23 - this was already fully built (`ShaderStack.cs`: ambient occlusion, bloom,
      colour grading, vignette, an `Overripe` preset with chromatic aberration and coloured grain
      pushed "past the point of good taste") and had just never been marked done here. Found while
      looking for a post-processing gap to fill for "more oomph" and discovering there wasn't one -
      extended instead with `ShaderStack.Pulse()`, a temporary vignette/aberration boost on every
      hit and kill, see `bug-log.md`'s ninth pass.
- [x] Screenshake and hitstop — `Juice`, on the camera, all unscaled time. Both retuned
      2026-08-22: shake gained a rotational component (position alone read as nothing), and the
      freeze itself was reported as "doesn't work" — 110ms at 6% speed on a kill was closer to a
      flicker than the ULTRAKILL-style stop this was meant to be. Now 260ms at 4.5%.
- [x] **Two handed weapon poses.** Both hands solve to their own target, the left further along
      the weapon than the right. `twoHanded` on the GunInfo decides: the Cavendish and the Slip
      Hazard are one handed and put the off hand on the hip, everything longer braces with both.
      **Confirmed working 2026-08-23**, not just built - reported as "missing entirely" and this
      entry's own arm-solve description was never actually the problem. The weapon itself was
      rendering meters off the hand (a scale bug in `AttachWeaponsToHand`) and, separately, an
      unmodelled wrist bone between the elbow and the weapon's attach point meant even a
      correctly-solved arm left the gun somewhere else entirely. Both fixed - see `bug-log.md`'s
      tenth pass. A proper-angle photography tool (`Tools/Gorilla Warfare/Photograph the grip`)
      now exists for checking this kind of thing without guessing from a bad camera angle again.
- [x] **Melee swing.** `SingleShotGun.StabSwing()` - a fast jab forward, a slower settle back,
      driven by maths off the weapon's own held pose rather than a clip, unscaled time so
      hitstop doesn't freeze it mid-jab.
- [x] **The peel's held rotation actually points forward now, and it has a crosshair.** The old
      `meleeHold` included a 180° turn around the model's own vertical axis, which - for a shape
      whose long axis is forward at identity, confirmed independently by `WeaponCheck` - reverses
      it outright rather than merely angling it; verified wrong by rendering several candidates,
      not reasoned about a third time. Its reticle was also `Dot`, which draws nothing at all
      unless a `CrosshairDot` setting most players never turn on is active. Both fixed 2026-08-23,
      see `bug-log.md`'s ninth pass.
- [x] **Check hitbox alignment against the mesh.** Actually looked at, 2026-08-22, with
      `Tools/Gorilla Warfare/Photograph the hitboxes` - and it was worse than "never checked":
      the auto-fit measured the arm at 0.66m radius, wider than the torso, because this rig's
      skin weights are painted broadly around the shoulder and hip joints and the fitter trusted
      them. Replaced with `HitboxProfile.asset`, a hand-set radius per part - see bug-log.md.
      Seeded and roughly tuned (`PlayModeProbe`'s coverage check passes at 70%, floor is 66%);
      final pass is Ryaan's, by design.

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
- [x] **Wall run, ground slam, air brake.** Built 2026-08-22, same day as designed - see
      `ideas.md`'s movement tech section for the mechanics and `bug-log.md`'s sixth pass for the
      other fixes that landed alongside them. First playtest the same day (`bug-log.md`'s seventh
      pass) found real problems in all three and a fourth, vault, which didn't survive the
      playtest at all - see below.
- [x] **Vault, removed.** Retuned once the same day it was built, then reported as still not
      working on the very next playtest and cut outright rather than retuned a third time. See
      `bug-log.md`'s seventh pass and `ideas.md`'s movement tech section for the record of why.
- [x] **Wall run rebuilt hold-based, then removed entirely.** The auto-latch original (speed
      threshold + moving toward the wall) was reported "really weird" - no player-controlled start
      or stop - and rebuilt hold-based: hold the slide/crouch key near a qualifying wall to stick,
      release to fall immediately, jump for the payoff push-off, plus its own continuous scrape
      audio. Cut outright 2026-08-23, direct request, in favour of the much simpler ledge hop
      below - see `bug-log.md`'s twelfth pass.
- [x] **The ledge hop.** Replaces both wall running and vault. "Add double jumping when you're at
      a ledge but make sure you can't spam it" - a single forward raycast gates an ordinary second
      jump, rather than either wall running's sustained latch-and-stick state or vault's scripted
      landing-point calculation (which never survived a playtest, twice). Can't-spam is two
      separate limits: once per airtime, plus a 1.2s cooldown starting from the hop itself so a
      low ledge landed on almost immediately can't chain a fresh one right back off it. See
      `bug-log.md`'s twelfth pass.
- [x] **Ground slam can no longer be spammed.** Reported directly. A 1.4s cooldown starts from
      landing, not from the press, so it isn't crouch-key-mashable in the air.
- [x] **The ground slam's impact is networked now, and bigger.** Asked directly for "impact
      effects" - what was there (sixth/seventh pass) turned out to be entirely local:
      `PlayerMovement` only exists on the owner's own copy, so nobody standing nearby when someone
      else landed a slam ever saw or heard it. Routed through a `PhotonRPC` the same way gunfire
      already is (`PlayerController.ReportGroundSlam`/`RPC_GroundSlamImpact`), and the burst itself
      doubled up - a wider dust cloud plus a faster debris layer - while already in there.
      **Sized up again 2026-08-23** - rendered at combat distance rather than close up and the
      original particles were smaller than the gorilla's own foot, genuinely invisible rather than
      just modest. Roughly 6-8x bigger now. See
      `bug-log.md`'s eleventh pass.
- [x] **The camera no longer clips into map geometry.** Reported as "basically wallhacks" - the
      inner camera itself had nothing keeping it out of walls when close to one. A spherecast from
      `cameraHolder` pulls the camera back along its own offset when it would clip, done in world
      space on the holder rather than the camera's own local transform so it doesn't fight
      `Juice`'s screenshake, which owns that transform's rest state.
- [x] **Bhop circle-strafing no longer gains speed absurdly fast outside a slide-hop.** The
      generous `airSpeedCap` (2.5, raised for the slide-hop redirect fix) was being handed to
      every jump regardless of whether a slide was involved. Now gated on an active chain window;
      plain circle-jumping uses a much smaller, closer-to-authentic cap instead. See `bug-log.md`'s
      ninth pass.
- [x] **Slide-hopping works again.** Was broken by the air brake, still sharing Walk with the
      slide buffer - re-pressing Walk to buffer the next slide while still rising out of the last
      hop fired the brake instead, killing the chain's speed. Air brake now has its own key.
- [x] **Wall running no longer leaves you stuck against the wall on a passive exit.** The run's own
      per-frame clip zeroes velocity away from the wall by design; ending without jumping off left
      nothing to carry you away from it. Small separation push added on that exit only.
- [x] **Bhop retuned again.** Reported as gaining way too much speed way too fast - a side effect
      of the same-day airSpeedCap raise for slide-hop redirect making *every* jump gain more from
      strafing, not just a slide-hop one. `bhopKeep` down from 1 to 0.92 so a perfect landing
      bleeds a little rather than nothing, capping how fast pure jump-chaining compounds without
      touching the slide-hop chain (which never went through that code path to begin with).
- [x] **Wall collisions now cost speed.** Nothing ever clipped `velocity` against a hit before
      2026-08-22 - `controller.Move()` stopped your position at a wall but never told the stored
      velocity, so it kept its full magnitude and a slight turn let you carry on at full speed.
      `PlayerMovement.OnControllerColliderHit` clips it properly now, wall normals only.
- [x] **Slide chain no longer compounds.** The kick used to multiply whatever velocity a player
      already had, so each chained slide-hop compounded on the last one's compounding and reached
      the safety ceiling within a couple of seconds. Now multiplies a fixed baseline instead, see
      `bug-log.md`'s fifth pass.
- [x] **The camera drops into a slide again.** Regression, cause not found further back than
      confirming nothing currently moves the camera for this - `PlayerMovement.StanceFraction`
      plus a `crouchCameraDrop` field in `PlayerController.Look()` fixed it going forward.

## Unverified

Things the checks can't reach, so they need a person:

- whether the recoil, gait and weapon framing feel right
- whether the bananas read as their weapons at a glance
- **all of M4.** Every sound is verified to exist, to be one shot, and not to clip. Whether any
  of it sounds *good* is not something a check can answer, and the synthesised ones especially
  are worth a listen before they're trusted.
- the scope overlay composited over the game - a screen space overlay canvas doesn't appear in
  a camera render, so the mask is checked on its own, the overlay is checked to raise and drop
  with the aim, and how the two look together is unseen
- **the HUD's layout.** Every label is verified to say the right thing, and none of it has been
  looked at. The starting positions are arithmetic against a 1920x1080 reference, not taste
- whether match and respawn lengths feel right. Warmup 8s, deathmatch 5min, gun game 10min,
  scoreboard 12s, respawn 3s — all guesses, all one field each in `MatchState`.
- **team deathmatch specifically.** Landed after the 3-4 client playtest on 2026-08-16 covered
  the rest of M3, so it's shared the same client-authoritative, unreviewed-by-a-server plumbing
  as everything else but hasn't had its own dedicated session.
- **the cliff clusters added to the jungle map.** Numerically confirmed to stack at the right
  heights (checked directly in the scene file after a real bug in that same math), but the
  numbers themselves - tier count, scale, how tall a formation ends up, how far apart they sit -
  are a first pass same as everything else on this list.
- **wall run, ground slam and air brake.** Had their first real playtest 2026-08-22, which is
  exactly what this entry existed to wait for - all three came back with real problems (ground
  pound firing on every slide-buffer attempt, wall run's auto-latch being unpredictable to start
  or stop on purpose, ground slam being spammable in the air) and were redesigned or retuned the
  same day - see `bug-log.md`'s seventh and eighth passes. Vault, the fourth mechanic on this same
  list, didn't survive its playtest and was removed rather than redesigned again. Still true of
  what's left: every number in the surviving three (the wall-run timer, the slam cooldown, the
  impulses) is a first guess, the same way every other feel number in this project has needed a
  person before it could be trusted - these passes changed *what* they do, not whether the
  specific numbers are right yet.
- **the sandbox's training dummies**, specifically. Reported 2026-08-22 as never having worked at
  all. Two separate bugs, both fixed the same day, only the second of which explains the "never":
  `RoomManager` placed them from `OnJoinedRoom`, which can fire before the game scene has loaded,
  so they spawned relative to world origin in a scene about to be replaced - fixed by moving that
  call to `OnSceneLoaded`. But that fix alone was moot, because `Sandbox.Enter()` never actually
  loaded the game scene in the first place: a normal room only reaches the map once someone in the
  lobby presses Start (`Launcher.StartGame`), and creating your own room makes you its master
  client, which is exactly the case `RoomManager`'s one other `LoadLevel` call deliberately
  excludes. Sandbox has no lobby and nobody to press Start, so the room existed and nothing ever
  left the menu scene to go with it. Confirmed by an editor batch-mode test that drove the real
  `Sandbox.Enter()` path end to end and found the dummies present in the loaded `Game` scene with
  their hitboxes intact - not just read as plausible from the code.

Previously listed here and corrected 2026-08-22: "anything that needs a second client" — this
was verified on 3-4 real clients 2026-08-16 (`working-notes.md`), including remote weapon
switching, replicated aim and the kill feed firing on a client that didn't do the killing. That
entry sat here for over a week after it stopped being true.

- **The whole style score system, added 2026-08-29.** `PlayModeProbe` confirms a kill lands, the
  local player's own custom property updates and the standings screen reads it back correctly,
  and a same-day fix closed the specific gap that made it look completely dead in solo sandbox
  testing (dummy kills now credit it - see the "Dummy kills now feed the meter" item above), but
  every actual *number* in it (how much a headshot/no-scope/point-blank/weapon-swap/movement
  chain should add, how hard a hit or a wall smash should cut it, the decay rate, the tier
  breakpoints, the wall-smash speed threshold) is still a first guess nobody has played against
  yet - see the "Style score and the combo/scoring rework" section above. The dummy-crediting
  wiring itself is reviewed, not test-covered - `PlayModeProbe` has no dummy-kill check yet.
- **The crate opening system, added 2026-08-29.** Verified standalone with real screenshots (two
  real bugs caught and fixed that way - see bug-log.md), but nobody has watched a real spin
  decelerate and land in motion, only the forced end state. Not wired to a button in any scene
  yet either, so nobody has opened one starting from an actual round's earned tokens.
- **The movement-tech combo, added 2026-08-29.** `HudPhotographer` confirms the bottom-left block
  renders and doesn't collide with anything else on screen, but which techs qualify (the bhop
  timing window, the grenade-jump strength threshold) and how the chain feels to actually build
  are first guesses nobody has played against - see the "Movement-tech combo" section above.
- **Training dummies ragdolling, added 2026-08-29.** Reuses `Corpse.Spawn` verbatim rather than a
  new code path, and the full `PlayModeProbe` suite passes with it wired in, but nothing has
  screenshotted or watched a dummy actually die - `HudPhotographer` doesn't spawn a sandbox or a
  dummy. Confidence here is code reuse plus a passing regression suite, not a render.
- **The rebuilt health bar's actual placement and readability**, same reasoning the HUD's own
  entry above has always carried - verified to say the right numbers, not looked at by a person.
  Vertical, top left, split peel/banana layers, the diagonal overshield bar - all built from the
  Cruelty Squad/ULTRAKILL description given, none of it eyeballed in a real match yet.
- **The gorilla model and hitboxes now baked onto the player prefab**
  (`Tools/Gorilla Warfare/Bake the player rig`, `PlayerRigBaker.cs`), so they're real, editable
  prefab content rather than only existing once `Start()` has run - direct request. `PlayModeProbe`
  confirms spawning and respawning both reuse the baked content rather than duplicating it (the
  hitbox-coverage and scale checks it already ran are unchanged), but nobody has actually opened
  the prefab and hand-adjusted anything on it yet, which was the entire point of building this.
- **The PSX filter, added 2026-09-03.** Compiles clean, passes the full suite, and follows PPv2's
  own documented custom-effect pattern - but nobody has turned the toggle on in a real session and
  looked at it. "Not too grainy or pixelated" was the brief; whether the fixed 0.6 intensity
  actually lands there is a taste call a render settles, not a compile check.
- **The rebuilt tab scoreboard, added 2026-09-03.** `PlayModeProbe` confirms it builds and refreshes
  without throwing against a real (single-player, offline-mode) spawn, and `SceneCheck` confirms
  every serialized reference is wired. What that can't reach: offline mode is one player, so the
  actual sort order, the team-grouped header, and the medal colours have never been seen against a
  real multi-player scoreboard - only reasoned through and read back as text, not rendered.

## Known limitations

Not tasks, but things that are true and worth knowing before they bite.

- **Every other movement-tech effect is still local-only, not just the ground slam that got
  fixed.** Found while fixing the ground slam's own version of this (2026-08-23, see
  `bug-log.md`'s eleventh pass): `PlayerMovement` only exists on the owner's own copy, and wall
  run's scrape, the air brake's burst, the slide's dust and sound all call the same local-only
  helpers the slam used to. Nobody standing near someone wall-running or air-braking sees or hears
  any of it. Only the slam was actually reported and fixed - the same `PhotonRPC` pattern
  (`ReportGroundSlam`/`RPC_GroundSlamImpact`) would apply to the rest if any of them get reported
  too.
- **Hit registration is client-authoritative.** The shooter raycasts locally and tells the
  victim they were hit. Fine among friends, trivially cheatable if this ever goes wider. Kept on
  purpose, not just left - it's a real part of why shots feel instant. Confirmed 2026-08-22:
  staying client-authoritative for the Steam launch itself, but a workaround worth a real look
  once the PUN migration below happens - server-side plausibility checks on top of the same
  instant local feedback, rather than moving authority off the client entirely and losing the
  feel that was the reason to keep this in the first place.
- **PUN 2 is end-of-life.** No more updates from Exit Games. It works and we deliberately chose
  to stay (see below), but it won't get fixes. Confirmed 2026-08-22: the migration off PUN is
  planned but deliberately sequenced *after* the Steam launch, not before it - see
  `working-notes.md`'s open items for the reasoning. Also on the list for whenever that migration
  happens: public/private rooms, with a password on private ones, since Photon's peer-hosted
  model has never actually stopped anyone from inviting whoever they want into a room today - the
  friends-only posture is a convention, not something enforced anywhere.
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
- Remote players couldn't actually see what weapon you were holding — it rendered metres off the
  hand (a scale bug) and, separately, an unmodelled wrist bone left even a correctly-solved arm
  pointing the gun somewhere else entirely. Both fixed 2026-08-23, see `bug-log.md`'s tenth pass.
- 36 assorted bugs, see `bug-log.md`
