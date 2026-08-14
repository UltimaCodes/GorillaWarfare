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
- [ ] Muzzle flash, recoil kick, weapon sway

**Done when:** you can carry several distinct banana weapons that feel different to fire.

---

## M3 — Game loop

Timed free-for-all deathmatch. One mode, done properly; more later.

- [ ] Match state machine: warmup → live → over → next map
- [ ] Round timer, replicated, visible
- [ ] Score limit as an alternative end condition
- [ ] End-of-match scoreboard with a winner
- [ ] Respawn delay instead of instant
- [ ] Master client owns match state; must survive host migration

**Done when:** a match starts, runs, ends, declares a winner, and starts again without anyone
touching anything.

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
- [ ] Hitmarkers, damage numbers, kill feed
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

- [ ] One real map to replace the seven cubes and four planes
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

## Ongoing

- [ ] Player / GroundCheck layers — `TODO` sitting in `SingleShotGun`, currently worked around
      with ownership checks
- [ ] Delete `NetworkDebugOverlay` and the `LogSpawn` calls — the late-join bug they were built
      for is fixed
- [ ] `Game.unity` will do a Unity 6 format migration on first open

## Recently closed

- Late-join invisibility — fixed by rewriting the spawn path to Photon's own pattern and
  deleting `PlayerManager` entirely
- Movement — Quake/Source acceleration on a `CharacterController`
- Footsteps firing mid-air
- 36 assorted bugs, see `bug-log.md`
