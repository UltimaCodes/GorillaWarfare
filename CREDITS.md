# Credits

## Audio

Everything synthesised has been deleted. What's here is recorded.

**Gunshots** - "The Free Firearm Sound Library" by Ben Jaszczak, Brian Nelson, Kevin Heras and
Matthew Nanney, [OpenGameArt](https://opengameart.org/content/the-free-firearm-sound-library),
**CC0**. Studio recordings at 96kHz/24bit: a 1911 for the Cavendish, an AK-47 for the Bunch, a
Mossberg for the Split, a Mosin Nagant for Big Mike.

Each source file is a whole session, so a one-off script (`tools/extract_shot.py`, now in git
history) cut one shot out. That took
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

**Vine grapple (thwip)** - "Swishes Sound Pack" by artisticdude,
[OpenGameArt](https://opengameart.org/content/swishes-sound-pack), **CC0**. Two of the thirteen,
picked by measurement (numbers in the bank's README): highest crest factor and earliest peak in the
pack for `swish-10`, shortest overall for `swish-13` - a thwip wants a sharp, early crack rather
than a swell, which is a different shape from Slide's sustained scrape and why this didn't just
reuse that bank.

**Wind, under going fast** - "Wind Whoosh Loop" by SketchMan3,
[OpenGameArt](https://opengameart.org/content/wind-whoosh-loop), **CC0**. Measured with
`AudioCheck.WindIsSmooth` (0.00 events/second) before trusting it - the same check that would
have caught the wind chimes buried in the ambient jungle track cut earlier in the project.

**Hits, headshots, kill, death, melee swing, menu taps** - "Punches, hits, swords and squishes"
by Philippe Groarke (Socapex), [OpenGameArt](https://opengameart.org/content/punches-hits-swords-and-squishes),
**CC BY-SA 3.0**, itself compiled from Freesound samples under CC-BY 3.0 and CC0. Attribution is a
condition, so this entry stays.

Chosen by measurement rather than by filename: the melee swing is the brightest of the swishes
at 71% of its energy above 4kHz, which is what makes it read as air rather than impact; the
headshot marker is brighter than the ordinary hit so the two are told apart by ear alone; and
the menu sounds are percussive taps rather than tones, which is what the old ones got wrong.

**Footsteps, impacts, hurt, UI clicks** - [Kenney](https://kenney.nl), **CC0**.

`GameAudio` resolves banks by folder name, so dropping a wav or ogg into a bank folder is the
whole installation - no wiring, no references. `AudioCheck` fails on a clip containing more than
one shot, which is the bug that shipped once already.

## Models

**Most of the 3D models are placeholders**, to be replaced before the Steam release - they can't
ship as they are. What's here is what's in the build today.

**Gorilla** (`Assets/Resources/Models/Gorilla`) - a placeholder, supplied by Ryaan as a zip whose rig
only survived in a Source `.smd`, rebuilt into an FBX in Blender. Source and licence unrecorded;
it's being replaced rather than cleared.

Every weapon in the game is one banana at a different size, derived from it by a one-off Blender
script (`tools/banana_variants.py`, now in git history) — 9,356 triangles, one 2K texture shared
between them.

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

## Fonts

In `Assets/Fonts/`, each with its licence file alongside it where one shipped.

| font | used for | licence |
|---|---|---|
| Anton | menu and HUD headings | SIL Open Font License 1.1 — The Anton Project Authors |
| Jersey 10 | menu and HUD body text | SIL Open Font License 1.1 — The Soft Type Project Authors |
| Helvetica Punk | in-game text | **no licence file shipped with it** — check before release |

## Grass

Compute grass system by Minions Art (Joyce) — the free demo version from her public Patreon post,
not a paid tier. See `Assets/Grass/CREDIT.txt` for the terms and every local change.

## Art

Jungle props — Kenney's Nature Kit, and particle sprites — Kenney's Particle Pack, both **CC0**
(licence files in `Assets/Art/Jungle` and `Assets/Resources/Particles`). The health banana sprite
is credited in `Assets/Textures/UI/BananaHealth-CREDIT.txt`.

The Grenada's pineapple, Purple Haze's grapes and Red Hot Chili Pepper's chili — Kenney's Food Kit,
**CC0** (licence file in `Assets/Resources/Models/Weapons`, with the shared `foodColormap.png`).

## Movement

Quake 3 / CPM movement is based on
[IsaiahKelly/quake3-movement-for-unity](https://github.com/IsaiahKelly/quake3-movement-for-unity),
which is released under the Unlicense (public domain).

## Networking

[Photon PUN 2](https://www.photonengine.com/pun) — free tier.

---

Everything here is CC0, Unlicense or CC BY. The CC BY one needs its credit kept; the rest are
courtesy. Keep it that way — if you add an asset, check the licence first and add it here.
