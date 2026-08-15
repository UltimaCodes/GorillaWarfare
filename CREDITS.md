# Credits

## Audio

Everything synthesised has been deleted. What's here is recorded.

**Gunshots** - "The Free Firearm Sound Library" by Ben Jaszczak, Brian Nelson, Kevin Heras and
Matthew Nanney, [OpenGameArt](https://opengameart.org/content/the-free-firearm-sound-library),
**CC0**. Studio recordings at 96kHz/24bit: a 1911 for the Cavendish, an AK-47 for the Bunch, a
Mossberg for the Split, a Mosin Nagant for Big Mike.

Each source file is a whole session, so `tools/extract_shot.py` cuts one shot out. That took
several goes and the failures are worth recording, because every one of them produced a file
that measured fine:

- Detecting shots by level found a "shot" every 50ms in one recording and none in another. An
  earlier pack was recorded with automatic gain that dragged the reverb tail up to 90% of the
  shot, so the tail was exactly as loud as the thing that caused it.
- Detecting by attack instead works, but merging anything within 90ms hid genuine rapid fire -
  the extractor then cut a whole burst and the onset counter, using the same broken logic,
  called it one shot.
- Cutting when the level fell below 5% of the peak ended the clip 40ms after the bang, which is
  a click rather than a gunshot. The decay is most of what makes a gun sound like a gun.

It picks the most isolated loud transient rather than the loudest, holds a floor low enough to
keep the tail, and never returns anything shorter than 220ms. `AudioCheck` counts onsets
independently and fails on more than one, which is what finally settled it.

**Reload** - "Gun reload sounds" by SpringySpringo, [OpenGameArt](https://opengameart.org/content/gun-reload-sounds),
**CC0**.

**Hits, headshots, kill, death, melee swing, menu taps** - "Punches, hits, swords and squishes"
by Philippe Groarke (Socapex), [OpenGameArt](https://opengameart.org/content/punches-hits-swords-and-squishes),
**CC BY-SA 3.0**, itself compiled from Freesound samples under CC-BY 3.0 and CC0. Attribution is a
condition, so this entry stays.

Chosen by measurement rather than by filename: the melee swing is the brightest of the swishes
at 71% of its energy above 4kHz, which is what makes it read as air rather than impact; the
headshot marker is brighter than the ordinary hit so the two are told apart by ear alone; and
the menu sounds are percussive taps rather than tones, which is what the old ones got wrong.

**Footsteps, impacts, hurt, UI clicks** - [Kenney](https://kenney.nl), **CC0**.

### Still missing

Four banks have nothing in them, and the game is quieter than it should be until they do:

| bank | what it needs |
|---|---|
| `Hit/hit`, `Hit/headshot` | the tick when your shot lands. **The most important sound in the game** - it's the difference between aiming and guessing |
| `Kill/kill` | distinct from a hit, and it should go downward where the hit goes up |
| `Death/death` | heavy and organic. Not a sci-fi explosion, which is what it used to be |
| `Shoot/Peel/swing` | a whoosh for the melee |

`GameAudio` resolves banks by folder name, so dropping a wav or ogg into any of those folders is
the whole installation - no wiring, no references. `AudioCheck` will tell you the moment they're
filled, and it fails on a clip containing more than one shot, which is the bug that shipped once
already.

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

**Combat** - "Drum and bass" by bertsz, [OpenGameArt](https://opengameart.org/content/drum-and-bass),
**CC0**. 96 seconds, instrumental, written as a loop for a game jam. Roughly 21 strong transients
a second, which is the breakbeat density the match wanted.

Menu, lobby and warmup are Ryaan's own tracks. The scoreboard slot is still empty; MusicPlayer falls back to a slot that does have something.

Slots, and what MusicPlayer picks them up as. Drop them into
`Assets/Resources/Audio/Music/` and MusicPlayer picks them up by filename:

| file | when it plays | length |
|---|---|---|
| `menu.ogg` | title screen, room browser | 2-3 min, loops |
| `lobby.ogg` | in a room, waiting for the host to start | 2-3 min, loops |
| `warmup.ogg` | the 8 seconds before a match goes live | 8s, no build-up wasted |
| `combat.ogg` | the match | 2-3 min, loops |
| `over.ogg` | scoreboard | ~20s, loops |

Every slot falls back rather than going silent, so a partial set works: no lobby track and the
menu one carries on, no warmup and combat starts early.

## Movement

Quake 3 / CPM movement is based on
[IsaiahKelly/quake3-movement-for-unity](https://github.com/IsaiahKelly/quake3-movement-for-unity),
which is released under the Unlicense (public domain).

## Networking

[Photon PUN 2](https://www.photonengine.com/pun) — free tier.

---

Everything here is CC0, Unlicense or CC BY. The CC BY one needs its credit kept; the rest are
courtesy. Keep it that way — if you add an asset, check the licence first and add it here.
