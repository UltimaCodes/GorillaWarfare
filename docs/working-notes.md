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

---

## Conventions

**Everything is built at runtime, not wired in a scene.** Weapons, hitboxes, the rig, the HUDs,
`MatchState`. The player prefab carries one `PhotonView` and an empty `ItemHolder`; anything
found sitting in that holder is a leftover and `RemoteCopyCheck` fails on it.

Exceptions, deliberate: the menu UI, the mode selector and the in-game HUD are real objects so
Ryaan can edit them.

**The HUD is scene data.** `GameHud` decides what the labels say and whether they're visible;
where they sit, what size they are and what font they use belongs to the scene. Two things are
driven from code on purpose and shouldn't be moved back: the scope, which has to track the
window's aspect ratio, and the crosshair ticks, which open with the weapon's spread. Everything
else that looks like a layout decision in that script is a bug.

Run `Tools/Gorilla Warfare/Build the in-game HUD` to rebuild it. That **replaces** the whole
`GameHud` root, so any restyling done by hand is lost - it's for starting over, not for updates.
`SceneCheck` walks every serialized slot and names the empty ones, because a reference dragged
loose doesn't throw, it just silently stops drawing and looks like a bug in the health code.

**Weapon keys stay role names** — `Pistol`, `Shotgun`, `Rifle`, `Sniper`, `Peel`. The gun game
ladder is defined in power order and reads at a glance. What players see lives in `itemName` on
the `GunInfo` and is set by `WeaponNaming`.

**Banana models run along +Z**, grip at the origin. `SingleShotGun.AnchorGrip` re-seats every
model at runtime, so a longer weapon reaches further forward instead of further backwards.

**Match state lives in room custom properties**, never in fields. That's what makes late joins
and host migration work without a catch-up path. The clock is a deadline against
`PhotonNetwork.Time`, not a countdown.

**Anything that runs during hitstop must use unscaled time.** `Time.timeScale` drops to 0.06 on
a kill; anything measuring itself with `deltaTime` freezes with the world. This applies to the
HUD, the kill feed timestamps, the aim transition and `Juice` itself.

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
bugs measured fine and looked obviously wrong.

**The HUD can't be photographed.** It's a screen space overlay canvas, and overlay canvases
don't appear in a camera rendered to a texture, which is the only kind of picture the probe can
take. So the probe reads the labels back instead - the health number against the player's
health, the round count against the magazine, the weapon name against what's equipped - which
catches the thing a screenshot wouldn't anyway: a number that's present, correctly placed and
stale.

**What no check can reach:** anything needing a second client. Offline mode is one player.
Remote weapon switching, replicated aim, the kill feed firing on a client that didn't do the
killing, host migration — all reasoned about, none observed.

---

## Lessons about the checks themselves

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
- **Hit registration is client-authoritative.** Fine among friends, trivially cheatable.
- **Music is sourced, SFX are sourced.** Four attempts at synthesising them were all rejected;
  measuring "improvement" is not the same as sounding good.
- **Everyone must run the same build.** RPCs are sent as indices into `RpcList`.

---

## Open, and known

- Nothing has been played with two people. This is the biggest risk in the project.
- Match timings are guesses: warmup 8s, deathmatch 5min, gun game 10min, respawn 3s.
- `warmup.mp3` is 20s against an 8s phase, so only its opening is ever heard. The crossfade
  handles it cleanly; whether the first eight seconds are the good eight seconds needs an ear.

Closed since, and worth recording why:

- **The shotgun ignoring overshield** was real. Fixed by making a shield point absorb two damage
  (`PlayerController.Absorb`), which turns a 108 damage pull from a two-shot into a three-shot
  at full shield. `WeaponCheck` plays the roster through the rule rather than dividing.
- **No `Music/over` track** was never a bug. `MusicPlayer` picks `over ?? lobby ?? menu`, and the
  lobby track is a holding pen, which is exactly what a results screen is.
- **`AppVersion` empty** is fixed — it's `0.5` now, so mismatched builds can't see each other's
  rooms. Bump it whenever the RPC list or a replicated property changes.
- **"Third-person banana sits at hip height"** was never true, or stopped being true when the
  two-handed poses landed. It measures at 73–77% of body height, which is chest level. Worth
  knowing how that was nearly "fixed": the first measurement used the stand-in's transform as
  the floor, which is an arbitrary point partway up the body, and reported the gorilla as 0.87m
  tall — in a probe where another check measures it at 1.95m two lines earlier. Two numbers for
  the same body in the same log, and the wrong one happened to agree with the note. The check
  stays, now measuring both ends off the mesh bounds.
