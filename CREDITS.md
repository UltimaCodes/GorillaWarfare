# Credits

## Audio

Sound effects by [Kenney](https://kenney.nl) — CC0 1.0 (public domain). Credit isn't required,
but the packs are good and free so here it is.

- Interface Sounds — menu clicks, confirm, error, back
- Impact Sounds — footsteps, bullet impacts, hit sounds
- Sci-Fi Sounds — weapon fire, death

## Models

Rigged monkey from [OpenGameArt](https://opengameart.org/content/monkey-3d-model-rigged-fbx) —
CC0. 34 bones, skinned, and it maps to Unity's Humanoid rig so humanoid animations can be
retargeted onto it.

Every weapon in the game is one banana at a different size. `tools/banana_variants.py` derives
all five from it — 9,356 triangles, one 2K texture shared between them.

It is **CC BY 4.0**, not CC0, so attribution is a condition of the licence rather than a
courtesy. The author's own wording, which has to travel with anything the game ships in:

> This work is based on "Banana low poly 9.4k 7mb 2k"
> (https://sketchfab.com/3d-models/banana-low-poly-94k-7mb-2k-783b4703c8214cca99a9e2c7ba1eddfa)
> by 3dUVpro (https://sketchfab.com/3dUVpro) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/)

## Music

**Nothing in yet.** The first attempt was wrong for the game and has been taken out.

What fits this thing is darksynth and industrial - the Cruelty Squad and ULTRAKILL register.
**Karl Casey / White Bat Audio** is the obvious source: it's that exact sound, and it's cleared
for use in games as long as the credit line is present.

> Music by Karl Casey @ White Bat Audio

His terms: use it in a game, credit him, don't sell the soundtrack separately from the game and
don't re-release it under another name. None of which is a problem here.

- Bandcamp: https://karlcasey.bandcamp.com
- YouTube: White Bat Audio

Drop two tracks into `Assets/Resources/Audio/Music/` as `menu.ogg` and `combat.ogg` and
MusicPlayer picks them up. It disables itself while they're missing, so the game runs fine
without them.

## Movement

Quake 3 / CPM movement is based on
[IsaiahKelly/quake3-movement-for-unity](https://github.com/IsaiahKelly/quake3-movement-for-unity),
which is released under the Unlicense (public domain).

## Networking

[Photon PUN 2](https://www.photonengine.com/pun) — free tier.

---

Everything here is CC0, Unlicense or CC BY. The CC BY one needs its credit kept; the rest are
courtesy. Keep it that way — if you add an asset, check the licence first and add it here.
