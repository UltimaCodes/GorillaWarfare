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

**The region token is not the dashboard name.** It is `uae`, not `mea`. Photon's docs are behind
a bot check from here, so `Tools/Gorilla Warfare/List Photon regions` asks the account directly
and prints every enabled token with its ping. Run that rather than guessing - a wrong token does
not error, it just finds no rooms.

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

## Open, and known

**`docs/open-issues.md` is the live list.** Anything reported from play and not yet fixed lives
there with what is suspected and why. The entries below are older and mostly settled.



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
- **`AppVersion` empty** is fixed — it's `0.6` now (bumped from `0.5` on 2026-08-22, when the
  vine's `RPC_Attach`/`RPC_Detach` got added to `RpcList`), so mismatched builds can't see each
  other's rooms. Bump it whenever the RPC list or a replicated property changes.
- **"Third-person banana sits at hip height"** was never true, or stopped being true when the
  two-handed poses landed. It measures at 73–77% of body height, which is chest level. Worth
  knowing how that was nearly "fixed": the first measurement used the stand-in's transform as
  the floor, which is an arbitrary point partway up the body, and reported the gorilla as 0.87m
  tall — in a probe where another check measures it at 1.95m two lines earlier. Two numbers for
  the same body in the same log, and the wrong one happened to agree with the note. The check
  stays, now measuring both ends off the mesh bounds.
