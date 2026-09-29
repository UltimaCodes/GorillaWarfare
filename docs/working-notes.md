# Working notes

Things that cost real time to learn on this project. Not a changelog — `git log` is the
changelog, and `bug-log.md` is the list of what was broken. This is the stuff that will bite
again, and the conventions that exist for a reason.

Read this before changing anything. Most entries here are written because the same mistake was
made twice.

---

## Traps that have caught me more than once

**Serialized prefab values beat C# defaults.** Change a default in a script and an existing
prefab keeps its stored value — the change does nothing. This has cost hours three separate
times: the viewmodel offsets, then `weaponAimOffset`, then the viewmodel offsets again. Two of
them were only caught by looking at a screenshot.

Write the value into **both** the prefab YAML and the code default. `RemoteCopyCheck` prints
every field where they disagree, so run it after touching any serialized number.

Worse variant: if the editor is open when a new `[SerializeField]` is added, Unity bakes the
then-current default into the prefab immediately, and later changes to the default are inert.

**`Awake` does not run on `AddComponent` outside play mode.** Anything built by an editor check
must have an explicit public `Build()`. `MuzzleFlash` and `MonkeyRig` both have one for this.

**In play mode it's the other way round: `AddComponent` runs `Awake` before you can configure
anything.** `WeaponLoadout` adds a `SingleShotGun` and then calls `Configure` - so the weapon's
Awake built its model and flash without a GunInfo. `MuzzleFlash.Scale` was skipped for every gun
for a month, and the food kit models came out unturned. `Configure` now finishes whatever needed
the GunInfo; anything new in Awake that reads `Info` belongs there too.

**Your weapon is drawn by a second, narrower camera** (`ViewModelCamera`, 55° against the world's
wider view). On screen it sits lower and further out than where it really is, so anything the world
camera draws from its tip - fire, smoke - comes out of a point in front of your face. Use
`SingleShotGun.DrawnTipPosition` for those. Tracers still start from the real tip; nobody's
noticed, because they last a twentieth of a second.

**`Mathf.SmoothStep(a, b, t)` is not GLSL's smoothstep.** It blends from `a` to `b` by `t`; it
doesn't turn `t` into a 0-to-1 edge between `a` and `b`. BulletDecal's splat used it that way and
never reached zero, so every mark carried a faint square. For an edge: `InverseLerp`, then
`e * e * (3 - 2e)`.

**`Legacy Shaders/Particles/Multiply` has no tint.** Setting `_TintColor` or `_Color` on it does
nothing - the colour comes from vertex colour times texture. That's why bullet impacts never faded
(they pop out), and on a surface they all but vanished. Every mark on the world uses `Custom/SurfaceMark` now.

**A sound bank with no clips of its own isn't silent - it's everyone's.** `GameAudio.Pick` falls back
to the parent folder, and `Shoot` holds every weapon's clips, so a new weapon with no `Shoot/<key>`
played all of them at random. A fallback bank passed after it never runs. Give a new weapon its own
clips or a `GunInfo.standInSound`.

**A global shader array's size is fixed by the first set.** `GrassMarks` and `BodyMarks` always send
the full array (64, 16) and a count, never a shorter one.

**A hitbox is not the skin.** A mark placed where a ray hit a hitbox is 1.5 to 7cm off the gorilla's
actual surface (measured on a chest), in or out. Anything drawn on the body from a hit point has to
reach that far - `BodyMarks.shader` projects along the shot and takes a window 30cm in, 15cm out,
limited to skin facing the shot.

**A multiply mark can only darken.** On grey fur, a dark red goes near black. Blood on a body has to be
a bright red for the red to survive the multiply.

**In the probe, aim the player, not the camera.** Setting `LocalCamera.transform.rotation` turns
the view but not the weapon, which follows the body's yaw and the look pitch - shots go the right
way and every screenshot shows a gun slanted across the frame. `PlayModeProbe.Face` sets the look
angles the way the mouse does - and clears the recoil, which Look() adds on top: without that, a few
shots into a test the gun was firing over the target's head.

**`SetCustomProperties` does not update the local cache in an online room.** It sends an op and
waits for the server to echo. Read-modify-write inside one round trip silently loses data —
that's why the master keeps its own score tally in `MatchState` rather than incrementing the
replicated value.

**PUN never clears player custom properties**, not even between rooms. Scores follow you into
the next room you join unless something resets them.

**PUN dispatches callbacks in a bare `foreach` with no try/catch.** One throwing target drops
the update for everything queued behind it — an exception in a `PlayerController` callback can
silently stop scoreboard rows updating.

**Entering play mode reloads the domain**, wiping statics and delegate subscriptions. Use
`SessionState` to carry intent across it (`PlayModeProbe` does).

**Anything under `Resources/` is loaded by name**, so it never appears in a GUID reference scan
and will look orphaned. Never delete on that basis alone.

**`~0` as a layer mask hits your own colliders.** Ground probes using it were hitting the
player's own leg hitboxes, so everyone read as permanently grounded and got footsteps mid-jump.
Use `Hitbox.WorldMask`. This was invisible while hitboxes were the wrong size — two bugs
cancelling out is not the same as no bugs.

**A shader only ever reached with `Shader.Find` and never referenced by a material can get
stripped from a build.** `Custom/ScreenOutline` wasn't in Graphics Settings' Always Included
Shaders list — worked fine in every Editor Play Mode test (the Editor never strips anything) and is
the likely real explanation for an earlier report of the outline missing "in the actual game." Any
new shader looked up this same way needs adding to that list, via `Tools/Gorilla Warfare/
Always-include the custom shaders` (`AlwaysIncludeShaders.cs`) rather than by hand — a
hand-typed GUID in `GraphicsSettings.asset` is a guess, and a wrong one doesn't error, it just
points at nothing.

**PostProcessing v2's `BlitFullscreenTriangle` takes a `PropertySheet`, not a `Material`, when a
custom shader is involved.** `context.command.BlitFullscreenTriangle(source, dest, material, 0)`
compiles-looks-right and fails with a confusing `Rect?` conversion error, because that's a
different overload entirely. The actual pattern: `var sheet = context.propertySheets.Get(shader);
sheet.properties.SetFloat(...); cmd.BlitFullscreenTriangle(source, dest, sheet, pass);` — PPv2's
own factory owns the Material's lifetime through the sheet, which is also one less thing for a
custom effect to manage by hand.

**A PPv2 custom effect records commands, it doesn't draw.** Anything temporary it needs has to be
allocated and released through the command buffer (`cmd.GetTemporaryRT`/`cmd.ReleaseTemporaryRT`),
never `RenderTexture.GetTemporary` — the effect's `Render()` returns long before the GPU work
runs, so a texture released there is back in the pool before anything has drawn into it.

**A bare `-` (no trailing space) for an empty YAML list entry is not the same as `- ` to Unity's
own parser**, even though both look like "nothing here" to a person reading the file.
`ProjectSettings/TagManager.asset` had one blank layer slot missing the trailing space every other
blank slot had, and that alone was enough to make Unity log `Unable to parse file
ProjectSettings/TagManager.asset` on every single batch-mode run — tolerated silently (the actual
layers still resolved fine) rather than failing anything, which is exactly how it went unnoticed
for so long. If a `.asset`/`.unity` file logs a parse warning, check for exactly this before
assuming it's benign noise.

**A shader property set from code has to be declared in `Properties`.** The grass shader read
`_TopTint`/`_BottomTint` without declaring them, and `Material.SetColor` on an undeclared property
can be silently dropped - it was, whenever that shader was the first thing on screen, and the whole
field rendered black. Read a value back (`GetColor`) when something renders the wrong colour.

**Anton's line height is about 1.5x its size.** A single-line label with Ellipsis overflow in a
rect shorter than that renders as nothing - TMP drops a line that doesn't fit vertically. Size
label rects to the line, not the cap height.

**One writer per transform.** Two scripts each writing a transform's absolute pose from their own
remembered "rest" is a bug waiting for the wrong frame: whichever captures its rest while the
other has it displaced pins it there. The player camera's local pose belongs to `CameraPose`
alone; shake and slide lean publish offsets to it. Anything new that wants to move the camera
publishes an offset too.

**`WaitForEndOfFrame` never resumes in batch mode.** No game view means no end of frame - a probe
coroutine waiting on it hangs until the three-minute watchdog. Read final poses with a
late-execution-order component instead (`FinalPoseRecorder` in `PlayModeProbe`).

**Batch-mode tests share the Editor's PlayerPrefs.** Anything a probe writes through the real
prefix lands on the settings used in normal Play Mode — for months, every `PlayModeProbe` run
quietly reset the sensitivity, keybinds and sliders. Probes switch `GameSettings`, `KeyBinds` and
`PlayerWallet` to their own prefix (`UsePrefsNamespace`) before touching anything; any new
PlayerPrefs-backed system needs the same switch, or the probes will overwrite it too.

---

## Conventions

**Where things live** (reorganised 2026-09-28 - new files go in the folder that fits, never loose):

| folder | what goes in it |
|---|---|
| `Scripts/Player` | the local and remote player: controller, movement, rig, camera, vine, hitboxes, nametags |
| `Scripts/Weapons` | items, `GunInfo`, `SingleShotGun`, loadouts, projectiles, `IDamageable` |
| `Scripts/Match` | match rules and phases, rooms, spawns, the map list, style score, the sandbox and its dummies |
| `Scripts/Menu` | the main menu: launcher, screens, lobby widgets, loading screen |
| `Scripts/Hud` | the in-match HUD and the scoreboard |
| `Scripts/Settings` | `GameSettings`, `KeyBinds`, the settings screen |
| `Scripts/Crates` | the crate shop, crate data, the wallet |
| `Scripts/Effects` | juice, flashes, tracers, decals, corpses |
| `Scripts/Rendering` | the post stack, PSX, outline, grass placement |
| `Scripts/Audio` | sound banks, footsteps, music |
| `Editor/Checks` | the verification suites (roadmap.md's table) |
| `Editor/Builders` | tools that build a scene, prefab or map piece |
| `Editor/Reports` | photographers and reports - read-only, change nothing |
| `Editor/Setup` | one-time project setup that live errors point at |
| `Prefabs/UI`, `Prefabs/World` | prefabs placed by scenes or code, by kind |
| `Art/<kit>` | a sourced art kit - `Models/`, `Materials/`, its licence and a `SOURCES.txt` |
| `Textures`, `Fonts`, `Shaders`, `Scenes` | what they say |
| `Resources` | anything loaded by name at runtime - its paths are code, so don't move things inside it |
| `Grass`, `Photon`, `TextMesh Pro` | vendor packages, left as shipped |

**Adding a map:** its scene in `Scenes/` with a `SpawnManager` (at least 6 spawnpoints, each with
ground under it and room to stand), an `EventSystem`, a light, and grass if it wants it - no HUD, the
shared one arrives on its own. Then a row in `Resources/Maps.asset` (key, name, scene) and the scene
in Build Settings, the menu staying index 0. Then `Tools/Gorilla Warfare/Set up the maps`, which
also copies it in behind the lobby (`~MenuBackdrop/<key>`, with a `CameraSpot` and a `GorillaSpot`
to move by hand) so the lobby's backdrop can show it. `ZooBuilder` is a worked example;
`SceneCheck` checks all of it for every map in the list, and the host picks from the list in the
lobby.

Moving an asset: move it **with its `.meta`** (`git mv` both, or drag it inside Unity), so its GUID
and every reference to it survive. Then search the code for its old path - a few tools load by path
(`MapExpansion`, `MenuBuilder`'s row prefabs, the builders' `BananaHealth.png`) - and run the suite.

**Everything is built at runtime, not wired in a scene.** Weapons, hitboxes, the rig, the HUDs,
`MatchState`. The player prefab carries one `PhotonView` and an empty `ItemHolder`; anything
found sitting in that holder is a leftover and `RemoteCopyCheck` fails on it.

Exceptions, deliberate: the menu UI, the mode selector and the in-game HUD are real objects so
Ryaan can edit them.

**The HUD is prefab data** - `Resources/MatchHud.prefab`, the HUD and the tab scoreboard together,
spawned by RoomManager into whichever map loads (it was scene data inside `Game.unity` until the
second map, 2026-09-28). Edit it there, not in a map; a map carrying its own HUD fails SceneCheck,
since it would draw two. `GameHud` decides what the labels say and whether they're visible; where
they sit, what size they are and what font they use belongs to the prefab. Two things are
driven from code on purpose and shouldn't be moved back: the scope, which has to track the
window's aspect ratio, and the crosshair ticks, which open with the weapon's spread. Everything
else that looks like a layout decision in that script is a bug.

**The grass is a vendored package, patched in place.** `Assets/Grass` is MinionsArt's system;
every change to it is marked `Gorilla Warfare:` (list in its `CREDIT.txt`) so a newer download
can be re-patched rather than silently losing the fixes. The game's own side is `GrassField`
(what grows where, seeded) plus one interactor child at each player's feet. Tuning lives in
`GrassField.cs`'s defaults and `GrassSetup.cs`'s settings block - re-running `Set up the grass`
resets the scene to them, so change the code, not the inspector.

**The main menu is scene data too.** `Tools/Gorilla Warfare/Build the main menu` builds it once
and then refuses to run over it; `Rebuild the main menu (replaces hand edits)` is the deliberate
start-over, and once the menu has been edited by hand it shouldn't be used at all. The arena behind
it is a copy (`~MenuBackdrop`), so a map change reaches the menu only after `Refresh the main menu
backdrop`, which touches nothing else. `Photograph the main menu` renders every screen with the
live backdrop - use it to check a change, not a guess.

Run `Tools/Gorilla Warfare/Build the in-game HUD` to rebuild it. That **replaces** the whole
`GameHud` root, so any restyling done by hand is lost - it's for starting over, not for updates.
`SceneCheck` walks every serialized slot and names the empty ones, because a reference dragged
loose doesn't throw, it just silently stops drawing and looks like a bug in the health code.

**Weapon keys stay role names** — `Pistol`, `Shotgun`, `Rifle`, `Sniper`, `Peel`. The gun game
ladder is defined in power order and reads at a glance. What players see lives in `itemName` on
the `GunInfo`, set by hand in the inspector.

**Banana models run along +Z**, grip at the origin. `SingleShotGun.AnchorGrip` re-seats every
model at runtime, so a longer weapon reaches further forward instead of further backwards.

**Match state lives in room custom properties**, never in fields. That's what makes late joins
and host migration work without a catch-up path. The clock is a deadline against
`PhotonNetwork.Time`, not a countdown.

**Who writes a custom property depends on whether it can race.** Kills/deaths/streaks are written
by the master only (`MatchState.ScoreKill`) because two different killers can land on the same
victim at once and a read-modify-write from two clients loses one. The style score
(`StyleScore.cs`, `RoomManager.StyleScoreKey`) is written by each client into its own property
instead - nothing else ever touches one player's own score, so there's nothing to race, and
classifying a kill's bonuses only ever has the data it needs on the killer's own client anyway.
Don't reflexively route a new stat through the master just because kills are - ask whether two
clients could actually write the same key first.

**Anything that runs during hitstop must use unscaled time.** `Time.timeScale` drops to 0.06 on
a kill; anything measuring itself with `deltaTime` freezes with the world. This applies to the
HUD, the kill feed timestamps, the aim transition and `Juice` itself.

**A new mode's display/capability properties go in `MatchModeInfo.cs`, not a new `MatchState.Mode
== MatchMode.X` comparison.** Added 2026-09-03 after a full-codebase review found the same
question - does this mode use teams, does it rank by style score, does it show the ladder, what's
its name - independently re-derived from the raw enum in eight different files, several of them
disagreeing with each other by the time anyone checked. `MatchModes.Of(MatchState.Mode)` is the one
place now; extend the struct and its lookup table rather than adding a ninth call site. Match
length, loadout rules and win-condition selection are the one exception - those stay as
`MatchState`'s own internal branches on purpose, see this file's own warning above about that
class being too load-bearing to restructure without a real reason.

---

## How to verify

Seven suites. Unity must be **closed** or batch mode refuses to open the project.

```
"C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe" -batchmode -quit -nographics \
  -projectPath "C:/DevProjects/Unity/OldProjects/FPS/GorillaWarfare" \
  -executeMethod WeaponCheck.Run -logFile -
```

`WeaponCheck`, `PlayerModelCheck`, `RemoteCopyCheck`, `SceneCheck`, `MatchCheck`, `AudioCheck`
all take `-quit`. **`PlayModeProbe` must not** — it enters play mode and exits itself.

`PlayModeProbe` runs the real game in Photon offline mode and writes screenshots to
`Logs/probe-shots/`. Reading those images is the only way to judge anything visual; several
bugs measured fine and looked obviously wrong. **Drop `-nographics` for a run you actually want
screenshots from** — with it, every `Capture()` call is silently skipped ("no graphics device")
while the rest of the probe's checks run fine and report success, so a stale image can sit there
looking current. Found 2026-08-22, `bug-log.md`'s fourth pass.

**The HUD can't be photographed.** It's a screen space overlay canvas, and overlay canvases
don't appear in a camera rendered to a texture, which is the only kind of picture the probe can
take. So the probe reads the labels back instead - the health number against the player's
health, the round count against the magazine, the weapon name against what's equipped - which
catches the thing a screenshot wouldn't anyway: a number that's present, correctly placed and
stale.

**A layout claim about an overlay canvas can still be checked without a screenshot.** "Is this
element actually inside that one" doesn't need a picture - `RectTransform.GetWorldCorners()` after
a couple of real frames (so the layout groups and the canvas have actually settled) gives real,
laid-out pixel coordinates for any RectTransform, overlay canvas or not. `PlayModeProbe.
CheckScoreboardLayout` is the reference example: it caught a real bug (the scoreboard's row column
sitting entirely below its own backdrop instead of inside it) that reading text values alone never
would have, since the text itself was correct - just positioned somewhere the backdrop wasn't. An
input-gated UI element needs the same treatment `PlayerController.AimInputOverride` already gives
aim input to make it testable at all in batch mode: a static nullable override (`Scoreboard.
OpenOverride`) rather than trying to fake a held key.

**Played on 3-4 clients, and it works.** Ryaan confirmed this on 2026-08-16. Remote weapon
switching, replicated aim, the kill feed firing on a client that didn't do the killing and host
migration were all reasoned about and never observed for months; they are observed now. Treat
the replication design as sound rather than as a standing risk.

That does not make the probe redundant - it still catches regressions in one client faster than
anyone can by playing - but "this has never been tested with two people" is no longer the
sentence to hang every doubt on.

**What no check can reach:** anything needing a second client. Offline mode is one player.
Remote weapon switching, replicated aim, the kill feed firing on a client that didn't do the
killing, host migration — all reasoned about, none observed.

---

## Lessons about the checks themselves

**The probe's matches roll over under you.** It shortens a deathmatch to six seconds, so a run goes
through several warmups, and each one resets stats and hands out fresh random weapons. A check that
gives itself a weapon or reads stats has to `yield return LiveWithTimeLeft(n)` first - two checks
passed for months on where in the match they happened to land, and failed the day the run got a few
seconds longer.

**A check written from the same wrong assumption as the code will pass.** The gun audio onset
counter used a threshold that couldn't see rapid fire, the extractor used the same logic, and a
clip containing a whole magazine was certified as one shot. Printing the envelope as ASCII
exposed it in seconds.

**Prefer physical sanity checks.** "This bolt-action fired every 55ms" is a better bug report
than any threshold.

**Measure the thing, not a proxy.** Clipping is a flat-topped waveform, not a peak at full
scale — checking the peak failed seven perfectly good files. A loop clicks when the seam jump is
large *relative to the local level*, not in absolute terms.

**Re-point a check when the design changes, don't delete it.** The decal check asserted marks
must never parent to players; when that became backwards, it was rewritten to assert the new
rule rather than removed.

---

## Editing source from scripts

Patch with unique anchors, or match braces. Using a string marker as the "end" of a region
mangled `CombatHud` into three copies of `DrawHealth`, because the marker appeared earlier in
the file than assumed. When replacing a whole method, find it by name and walk its braces, then
assert the method count is unchanged.

---

## Decided, don't relitigate

- **Photon stays.** P2P was considered properly and rejected; PUN is already peer-hosted.
- **No clip-based animation.** The rig is driven procedurally by `MonkeyRig`.
- **Hit registration is client-authoritative.** Fine among friends, trivially cheatable. Kept
  deliberately for the feel of instant shots - see `roadmap.md`'s "Known limitations" for the
  2026-08-22 note on a server-side-plausibility-check workaround, once there's a server to run it.
- **Music is sourced, SFX are sourced.** Four attempts at synthesising them were all rejected;
  measuring "improvement" is not the same as sounding good.
- **Everyone must run the same build.** RPCs are sent as indices into `RpcList`.
- **Most 3D models are placeholders** (decided 2026-09-27) - the gorilla included - and get replaced
  before the Steam release; they can't ship as they are. Don't spend effort tracing their licences
  or polishing them. Anything built on a model (the rig, hitboxes, grip offsets) should keep working
  when the model is swapped, which is the argument for the measured-not-hardcoded approach the rig
  code already takes.
- **The grass is fine to ship.** MinionsArt's free public demo, not a paid tier - the same one Muck
  shipped with. See `Assets/Grass/CREDIT.txt`.

**Steam launch sequencing, decided 2026-08-22.** The game is going up on Steam; the target moved
from "a month or two" to six months the same day, specifically to leave room for the game to feel
finished rather than shipped early. What that changes, in order:

- Steamworks integration and the move off PUN 2 happen **after** the Steam launch, not before it -
  ship on the current Photon-peer-hosted networking first, migrate once it's out.
- Controller support is real but not near-term - "down the line".
- First-time onboarding was blocked on a main menu rework, so it wasn't built against the old menu.
  The rework landed 2026-09-27 (bug-log.md's thirty-third pass), so this is unblocked.
- Store page assets (capsule art, trailer, screenshots) wait until the Steam Direct filing itself,
  near the end - not sequenced early the way the filing's review lag alone would otherwise argue
  for, because there's six months of runway now instead of eight weeks.
- Public/private rooms with a password on private ones are wanted, but filed under the same
  post-migration bucket as the rest of the netcode work - see `roadmap.md`'s "Known limitations".

---

**Never load a scene synchronously from a PUN callback.** PUN dispatches callbacks in a bare
`foreach` with no try/catch over every target it knows about, and loading a scene destroys half
of those targets mid-iteration. `OnLeftRoom` doing `SceneManager.LoadScene(0)` directly took the
game down. Wait a frame.

**A GameObject can only carry one Graphic**, and `TextMeshProUGUI` is one. `AddComponent<Image>()`
on an object that already has a label returns **null** rather than failing loudly, and the next
line throws a NullReferenceException that looks like it came from nowhere. Use the label as the
button's `targetGraphic` - TMP raycasts over its whole rect anyway, so the row is clickable
rather than the letters.

**`AddComponent` on a prefab loaded with `LoadAssetAtPath` does not work either.** Use
`PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` / `UnloadPrefabContents`.

**`onClick.AddListener` in an editor builder saves nothing.** It's a runtime listener; the prefab or
scene it's saved into keeps no trace of it, so the button ships dead while looking perfectly wired
in the builder. Use `UnityEditor.Events.UnityEventTools.AddPersistentListener`, or a serialized field
the component wires in its own `Start`. The crate result's CLOSE shipped dead this way.
`MenuButtonAudit` catches it.

**The cursor has no owner in the menu.** `PlayerController` captures and releases it, and there
is no PlayerController in the menu scene - so whatever state the game left it in persists. Any
path back to the title has to free it explicitly or the menu is unclickable, which reads as a
hang.

## Everybody has to be on one Photon region

**Rooms exist per region.** With no `FixedRegion`, PUN connects each client to its own nearest
cluster - so a player in Italy and a player in Pakistan sit on different servers and each sees an
empty room browser. That reads as the game being broken, not as a setting.

`FixedRegion` is `uae`, chosen because the testers are in Pakistan, the UAE and Italy and it is
the only cluster that is not badly unfair to one of them. `SceneCheck` fails if it is ever blank.

**The region token is not the dashboard name.** It is `uae`, not `mea`. A wrong token does not
error, it just finds no rooms. The tool that listed the account's tokens (`RegionCheck.cs`) was
removed because it wrote a blank `FixedRegion` into the committed PhotonServerSettings as a side
effect; if a region ever needs checking again, restore it from git history and revert that asset
afterwards.

Also worth knowing: `Launcher` falls back to best-region once if the fixed one is unreachable,
and logs the region it actually landed on. If two people ever cannot see each other's lobbies,
compare those two log lines first.

## Leaving a room is its own code path, and it breaks things

**Never `SceneManager.LoadScene` while `AutomaticallySyncScene` is on.** PUN watches the room's
level and loads it on every client; loading one behind its back leaves its idea of the current
level disagreeing with reality and it complains constantly. Turn the sync off first - the
Launcher turns it back on when it reconnects.

**PUN shuts its message queue while a level loads** and reopens it from its own `sceneLoaded`
handler. Turn `AutomaticallySyncScene` off and that handler stops running, so the queue stays
shut - and a client with a shut queue is deaf to everything, including its own room join.
Reopen it by hand after the load.

**Coroutines outlive the room.** `SpawnWhenReady` spends nearly all its life waiting, so leaving
almost always leaves one in flight. It was never stopped, and because `TrySpawn` refuses to start
a second while one exists, the stale one held the slot shut and nothing ever spawned again. Stop
every routine in `OnLeftRoom`.

**Statics outlive the scene**, which is the point of them and also the problem. `PlayerController`
has a `ForgetLocals` that clears the lot; call it on the way out rather than making every reader
defend itself.

**What no check can reach:** a genuine disconnect and rejoin. Offline mode has no server -
leaving destroys the only room there is, and returning to the menu makes the Launcher reconnect,
which tears down anything staged after it. Instead `SpawnWhenReady` logs which of its four
conditions it is stuck on after three seconds, which turns "I rejoined and had nothing" into a
line naming the cause.

## Where input is read

Four separate scripts read the mouse and keyboard, and every one of them has to be told when the
settings screen is up: `PlayerController` (look, fire, reload, cursor), `PlayerMovement` (walk,
jump), `Scoreboard` (tab) and `WeaponSway` (mouse). All four were wrong at once, because each was
written on the assumption that it was the only thing listening.

Anything new that reads input goes on that list. The rule is `SettingsMenu.IsOpen`.

## Phases have a default, and the default is a trap

`MatchState.Phase` falls back to `Warmup` when the room has no phase key, and `TimeLeft` falls
back to zero. Together that reads as "a warmup that has already run out", so the phase machine
promotes it straight to Live - which is why there was no warmup for months. A missing phase now
means "the match has not started" and is handled before the switch.

Anything else that reads a phase or a deadline out of room properties needs to distinguish
"absent" from "expired". They are not the same and the defaults make them look identical.

**A new phase doesn't respawn anyone.** Warmup going live and one match rolling into the next both
happen around players who are already standing there, so anything that only resets on a fresh body
(health, overshield, streaks, ammo, a component's local state) carries straight across unless
something resets it on the phase change. `PlayerController.StartRoundFresh` and StyleScore's warmup
reset are the two that do; anything new that's per-round needs the same.

**A disconnect we asked for is not an error.** The sandbox drops the connection on purpose to go
offline, and switching offline mode on fires `OnConnectedToMaster` with no server behind it.
`Launcher` ignores both; anything else listening for connection callbacks has to as well.

**Nothing in the menu talks to Photon without a lobby.** A connection can drop at any moment - a
DNS blip, a sleep - and a create or join sent into it is refused ("CreateRoom failed. Client is on
NameServer..."). Room actions go through `Launcher.WhenInLobby`, which runs them now or, if the
lobby isn't up, shows loading, reconnects if nothing is, and runs them when it's back. A real
disconnect says so once and keeps retrying with backoff; a DNS failure retries the same region
rather than falling back to another. `SandboxFlowCheck` drops the connection and creates a room to
prove it.

## Open, and known

**`bug-log.md`'s "Open now" section is the live list.** Anything reported from play and not yet
fixed lives there with what is suspected and why. Used to be its own `open-issues.md`; merged
2026-08-22 since it spent most of its life saying "nothing open right now" next to a file whose
whole other job is the same list, closed. The entries below are older and mostly settled.

- ~~Nothing has been played with two people.~~ Played on 3-4 clients and working, 2026-08-16.
- Match timings are guesses: warmup 8s, deathmatch 5min, gun game 10min, respawn 3s.
- `warmup.mp3` is 20s against an 8s phase, so only its opening is ever heard. The crossfade
  handles it cleanly; whether the first eight seconds are the good eight seconds needs an ear.

Closed since, and worth recording why:

- **The shotgun ignoring overshield** was real. Fixed by making a shield point absorb two damage
  (`PlayerController.Absorb`), which turns a 108 damage pull from a two-shot into a three-shot
  at full shield. `WeaponCheck` plays the roster through the rule rather than dividing.
- **No `Music/over` track** was never a bug. `MusicPlayer` picks `over ?? lobby ?? menu`, and the
  lobby track is a holding pen, which is exactly what a results screen is.
- **`AppVersion` empty** is fixed — it's `0.8` now (`0.6` on 2026-08-22 for the vine's
  `RPC_Attach`/`RPC_Detach`; `0.7` on 2026-09-28 for Purple Haze and Red Hot Chili Pepper's four
  RPCs and the two flags they added to the player's stream; `0.8` on 2026-09-29 for the gatling's
  rev flag on the same stream), so mismatched builds can't see each
  other's rooms. Bump it whenever the RPC list or a replicated property changes. `SceneCheck` now
  fails on any `[PunRPC]` missing from `RpcList` - which is how the four were caught, since a
  missing one still works and says nothing.
- **"Third-person banana sits at hip height"** was never true, or stopped being true when the
  two-handed poses landed. It measures at 73–77% of body height, which is chest level. Worth
  knowing how that was nearly "fixed": the first measurement used the stand-in's transform as
  the floor, which is an arbitrary point partway up the body, and reported the gorilla as 0.87m
  tall — in a probe where another check measures it at 1.95m two lines earlier. Two numbers for
  the same body in the same log, and the wrong one happened to agree with the note. The check
  stays, now measuring both ends off the mesh bounds.
