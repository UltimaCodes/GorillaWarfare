# Roadmap

Where this is going and in what order. Updated 2026-08-14.

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

## M0 — Stop the bleeding

Regressions and broken basics. Nothing else matters while the game can't be played.

- [ ] **You can't damage anyone.** Diagnosed: the player's only collider is now the
      `CharacterController`, and remote copies disable it — a disabled CharacterController has no
      collider, so the raycast passes straight through everyone. Keep it enabled and only strip
      `PlayerMovement`; a CharacterController you never call `Move()` on does nothing.
- [ ] **Gun audio is bursts, not single shots.** The OpenGameArt clips are full-auto recordings.
      Need one-shot samples, or trim these to the first transient.
- [ ] Verify shooting end to end with two clients before calling M0 done.

**Done when:** two players can shoot, damage and kill each other, and each shot is one bang.

---

## M1 — The monkey

The model is imported and scaled but nothing renders it — `CHIMP_L` is still what you see, which
is why it looks unchanged (and terrifying).

- [ ] `PlayerController` calls `MonkeyRig.Build()` and feeds it `LookPitch`, `PlanarSpeed`, `Grounded`
- [ ] Remove `CHIMP_L` and its stray light from the prefab
- [ ] Reparent weapons to `b_Right_Hand` on remote copies — they currently sit on `CameraHolder`,
      which is first-person only, so others see a gun floating at head height
- [ ] Tune the gait numbers against the real model; current values are guesses
- [ ] Hide own body from own camera, keep the shadow

**Done when:** other players look like a monkey that walks, aims where it's looking, and holds
its weapon.

---

## M2 — Weapons

Banana-shaped guns. Plural — the point is variety, not one gun.

- [ ] Source or build CC0 banana / fruit models
- [ ] Weapon definitions as ScriptableObjects: damage, fire rate, spread, auto vs semi, range
- [ ] At least four: something fast and weak, something slow and hard, a shotgun-ish spread, a
      joke tier
- [ ] Per-weapon sounds (the bank-by-name lookup already supports this)
- [ ] **Better gun audio.** Current clips are thin and clicky - they read as a click, not a bang.
      Wants weight and low end. The two in there now are trimmed .22 recordings, which is a
      small calibre and sounds like it. Needs bigger source material.
- [ ] **Pistol is the default weapon** (currently index 0 is whatever the prefab ordered)
- [ ] **Rifle is automatic** - fires while held, at a fixed rate
- [ ] **Maths-based recoil, not animation.** Each shot pushes the view up along a defined
      pattern, then recovers smoothly. Pattern is data, so each weapon can have its own - and a
      learnable pattern is the thing that makes a spray skilful rather than random.
- [ ] Muzzle flash, weapon sway
- [ ] **Ammo and reloading.** Neither exists at all right now — you have infinite bullets and no
      reload, so every gun is a hose. Fire rate is the only thing separating them until this
      lands, which undercuts the whole point of having several.
- [ ] Fire rate limiting — `SingleShotGun` fires once per click with no cooldown, so an auto
      weapon has nothing to hold it back

**Done when:** you can carry several distinct banana weapons that feel different to fire.

---

## M3 — Game loop

Two modes.

**Shared plumbing**
- [ ] Match state machine: warmup → live → over → next map
- [ ] Round timer, replicated, visible
- [ ] End-of-match scoreboard with a winner
- [ ] Respawn delay instead of instant
- [ ] Master client owns match state; must survive host migration
- [ ] Mode picked in the lobby

**Deathmatch (timed)**
- [ ] Each match rolls a random set of 3 weapons; you spawn holding one of them
- [ ] Most kills when the clock runs out wins

**Gun game**
- [ ] Fixed weapon ladder, 2 kills to advance
- [ ] Everyone works down the same order
- [ ] Final rung is melee — killing with it wins the match
- [ ] Needs a melee weapon, which M2 doesn't currently plan for

**Done when:** both modes can be picked, played start to finish, and declare a winner without
anyone touching anything.

---

## M4 — Audio

- [ ] Replace the death sound (still a sci-fi explosion) and the menu confirm (still sci-fi)
- [ ] Hit confirmation sound — the single best piece of feedback in a shooter
- [ ] Kill sound, distinct from hit
- [ ] Music: menu and combat
- [ ] Volume mix pass; nothing should clip or vanish

---

## M5 — UI

The big aesthetic one. See the philosophy section above.

- [ ] Main menu rebuilt to the ULTRAKILL/Cruelty Squad direction
- [ ] Lobby and room browser restyled to match
- [ ] In-game HUD: health, ammo, timer, scores
- [ ] Hitmarkers, damage numbers
- [ ] **Kill feed** — who killed who with what, top corner, fading entries
- [ ] **Join / leave messages** — "X joined", "X left". Photon already fires
      OnPlayerEnteredRoom and OnPlayerLeftRoom, so the data is there and unused.
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

## M7 — Art and shaders

- [ ] Replace placeholder textures (currently screenshots and memes)
- [ ] Environment art matching the philosophy
- [ ] Post-processing: the palette-mangling that sells the Cruelty Squad look
- [ ] Screenshake and hitstop

---

## Movement tuning

Separate from M0 because it needs a person playing it, not a fix.

- [ ] Tune `maxGroundSpeed 7 / groundAccel 14 / friction 6 / airAccel 100 / airSpeedCap 0.9 /
      jump 7 / gravity 20`. All guesses — nobody has played it yet.
- [ ] **Shift is now walk, not sprint.** Source-style: you run by default and shift slows you
      down. That's inverted from the old build and you haven't tried it yet, so it may just feel
      wrong.
- [ ] Decide whether auto-bhop stays on. It's on by default, which makes speed trivially easy to
      keep. Off is more demanding and more satisfying to learn.

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

- Late-join invisibility — fixed by rewriting the spawn path to Photon's own pattern and
  deleting `PlayerManager` entirely
- Movement — Quake/Source acceleration on a `CharacterController`
- Footsteps firing mid-air
- 36 assorted bugs, see `bug-log.md`
