# Bug log

Everything found going through the scripts before starting on movement and new features.
Skipped the movement code on purpose (`PlayerController.Move/Look/Jump/FixedUpdate`,
`PlayerGroundCheck`) since it's all getting replaced by the Quake 3 controller anyway.

36 things total. Grouped roughly by how bad they were.

---

## Open now

The live list — reported from play and not yet resolved. Merged 2026-08-22 from a separate
`open-issues.md`, which existed to make this distinction and mostly just said "nothing open right
now" for a week at a time; one file with a section at the top does the same job.

- **The two-handed grip pose.** Reported 2026-08-22 as missing entirely. `PlayModeProbe`'s
  numeric check passes, but the only render available of it is a bad angle for judging a pose by
  eye - see `roadmap.md`'s "Unverified" for the full account. Needs either a better render angle
  or a person looking at it in real play.

Nothing else open. Everything that was here before this merge — the slide's five complaints and
the sandbox loadout bug, both from the pass on 2026-08-21 — is written up in the "Third pass"
section below, which is where a resolved item belongs once it's resolved.

---

## Stuff that actually threw

**`Scoreboard.RemoveScoreboardItem`** indexed the dictionary directly, so any player leaving who'd
never been added threw `KeyNotFoundException`. Happens to anyone who joins and leaves while the
scoreboard is still starting up. Now uses `TryGetValue`.

**`SpawnManager.GetSpawnpoint`** — `Random.Range(0, 0)` returns `0`, so with no spawnpoints it read
`[0]` of an empty array. Now returns null, and logs at `Awake` if there are none.

**`PlayerController.Awake`** chained off `PhotonView.Find(...).GetComponent<PlayerManager>()`. That
lookup returns null if the owner's `PlayerManager` view isn't registered on this client yet, and the
throw killed the rest of `Awake`. Pulled into `ResolvePlayerManager()`, retried from `Die()`.

**`PlayerManager.Find`** used `SingleOrDefault`, which *throws* if there's ever more than one match.
A duplicate should be a cosmetic problem, not an exception. `FirstOrDefault` now.

**`PlayerManager.Die`** called `PhotonNetwork.Destroy(controller)` without checking for null, which
you can reach if `CreateController` bailed. You'd stay dead permanently.

**`RPC_TakeDamage`** called `.GetKill()` on a possibly-null `PlayerManager.Find` result, so a missing
killer took the victim's death down with it.

---

## Wrong behaviour

**Vertical aim was never sent.** `Look()` puts yaw on the root, which `PhotonTransformView`
replicates, but pitch goes on `cameraHolder` — which had no components on it at all. Nobody ever saw
anyone look up or down. `PlayerController` now implements `IPunObservable` and sends pitch as one
float on the root view — considered a `PhotonView` + `PhotonTransformView` directly on
`cameraHolder` instead and rejected it: that's a fourth `PhotonView` per player burning a view ID
on every respawn, sending a full position and rotation just to carry one number.

**You could shoot yourself.** Everything is on layer 0 and the ray starts at the camera, which sits
inside your own capsule collider. Now checks the hit's `PhotonView.Owner` against the shooter.

**Shots got eaten by trigger volumes.** The raycast honoured triggers, so it could stop on the
`GroundCheck` trigger parented under the player, right at the muzzle. `QueryTriggerInteraction.Ignore`.

**Body shots did nothing.** `hit.collider.gameObject.GetComponent<IDamageable>()` finds nothing when
the collider is on a child. `GetComponentInParent` now.

**Gun had no range limit.** Added `maxRange`, 200 by default.

**Dead rooms stayed in the browser.** `Launcher.cachedRoomList` was `static` and never cleared, so
closed rooms hung around for the whole process — and survived across play sessions in the editor.
Clicking one just failed. Now an instance field, cleared on lobby join/leave and disconnect, and
empty rooms get dropped too.

**Failed joins and disconnects were silent.** `OnCreateRoomFailed` existed but `OnJoinRoomFailed` and
`OnDisconnected` didn't, so joining a full room or dropping connection left you on the loading
screen forever with no message.

**Rooms had no player cap** — `CreateRoom` passed no `RoomOptions` at all. 8 now, serialized.

**Blank usernames stuck forever.** `PlayerPrefs.HasKey` is true even when the value is an empty
string, so once you'd cleared the field you came back nameless every launch with no way to fix it
short of wiping PlayerPrefs. Checks for whitespace now, and won't save an empty name.

**Two `PlayerManager`s each after going back to the menu.** Returning to the menu scene makes a
second `RoomManager`; `Awake` destroys it, but `Destroy` is deferred so its `OnEnable` still ran and
subscribed a second `sceneLoaded` handler.

**`Path.Combine` for the prefab key.** Gives a backslash on Windows. It resolves there, but that
string goes over the network as a Resources key, so it'd break the moment anyone joined from another
platform. Explicit forward slashes now.

**A typo'd menu name closed every menu and opened nothing**, silently. Logs an error now.

**`Billboard` could face the wrong camera.** `FindObjectOfType<Camera>()` returns whichever one it
feels like, and there's a window before `PlayerController.Start` destroys the remote ones.
`Camera.main` isn't an option because the prefab's camera is `Untagged`. Added
`PlayerController.LocalCamera`.

**`UsernameDisplay`** disabled the object when `IsMine` then kept going and touched `playerPV.Owner`,
which can be null on a fresh instantiate.

**Scoreboard showed prefab placeholder text** for anyone who hadn't scored yet, since the custom
property doesn't exist until the first kill or death. Defaults to `0`.

---

## Robustness

- `Scoreboard.AddScoreboardItem` leaked the old row when a player got added twice — dictionary entry
  overwritten, GameObject orphaned, row stuck on screen forever.
- Both `Launcher` list rebuilds walked the container's children and called `Destroy`, which is
  deferred, so rebuilding twice in a frame gave duplicate rows. Tracks its own lists now.
- `Launcher`, `MenuManager` and `SpawnManager` all did a bare `Instance = this` with no guard and
  never cleared it on destroy.
- `Spawnpoint.Awake` threw if `graphics` was unassigned.
- `RoomListItem.OnClick` didn't check `Launcher.Instance`, which is null during a scene change.
- `PV.RPC("RPC_Shoot", ...)` was a string literal. `nameof` now.

---

## Performance

- `PlayerManager.Find` scanned the whole scene on every call, and it's called on every kill. Registry
  now, with the scan kept as a fallback for before `Owner` resolves.
- `Billboard` ran a full-scene type search *every frame* until it found a camera, on every nameplate.
  Also moved to `LateUpdate` — from `Update` it lagged the camera by a frame and visibly swam when
  you turned. `LookAt` + 180° `Rotate` is one `Quaternion.LookRotation` now.
- `Physics.OverlapSphere` allocated an array on every shot. `OverlapSphereNonAlloc` into a shared
  buffer.
- Dropped `System.Linq` from `Launcher` (`players.Count()` on an array).
- Photon was serializing 10x/sec while sending 30x/sec — `sendFrequency = 33`ms but
  `serializationFrequency = 100`ms. Remote positions and aim only updated ten times a second.
  Bumped to 20.
- Region was hard-pinned to `uae`, forcing everyone onto that cluster no matter where they are.
  Unpinned. (If we end up geographically split, one explicit shared region beats auto — see
  `roadmap.md`'s "Known limitations".)
- Point light on the player model, imported from the `.3DS`, intensity 271 and range 0. Lit nothing,
  still got processed on every player. Disabled. (Root `MeshFilter`/`MeshRenderer` are already
  disabled, left them.)
- Unused `using`s all over, mostly `System.Collections` from the default template.

---

## Left alone on purpose

**Movement** — dying anyway when the Quake 3 controller lands.

**Proper layers.** The right fix for the self-shooting and trigger-eating bugs is a `Player` layer
and a layer mask, not ownership checks. That means editing `TagManager.asset` and reassigning layers
across prefabs and scenes, which is a bigger change than it sounds and belongs with the collider
rework. Worked around for now, `TODO` left in `SingleShotGun`.

**The late-join visibility bug** — was open as of this pass. Fixed since, by rewriting the spawn
path to Photon's own pattern and deleting `PlayerManager` entirely — see `roadmap.md`'s "Recently
closed".

---

# Second pass — before M3

Everything found going over the project again ahead of the game loop. The theme this time was
**the copy of you that other people see**: every check written so far only ever exercised the
owner's player, because that's the branch that builds a loadout, and a remote copy takes a
completely different path through `PlayerController.Start`.

## The remote copy

**Other people saw you holding the 2024 guns.** `BuildLoadout` lived inside `if (PV.IsMine)`, and
`WeaponLoadout.Build` is the only thing that clears the weapon holder. So a remote copy kept
whatever the prefab shipped with — the M1911 and the AK74 — and `AttachWeaponsToHand` dutifully
parented that onto the gorilla's hand. Weapons are built on every copy now, from a replicated
list, with an ownership flag so only yours may trace a shot.

**Weapon switching threw on every other client.** `itemIndex` is replicated and was being fed
straight into `items[]` with no bounds check. Yours had four entries, theirs had the prefab's
two, so switching to weapon 3 or 4 threw `IndexOutOfRangeException` on everyone else's machine.
Worse than a log line: PUN dispatches callbacks in a bare `foreach` with no `try`/`catch`
(`LoadBalancingClient.cs:4391`), so the throw also dropped that property update for every
callback target queued behind it — `ScoreboardItem` among them.

**Three PhotonViews per player.** The runtime loadout was built on the premise that weapons
don't carry their own view. The prefab never got the message, so every spawn and every respawn
allocated all three.

## The one that explains a lot

**The first person arms were destroyed one frame after being built.** `PlaceViewModel` built
them into the item holder, and the very next line, `BuildLoadout`, emptied that holder. Every
round of tuning the arm position was tuning an object that didn't survive its first frame — and
`WeaponCheck` measured 38% of them on screen because it instantiated its own copy and measured
that, never running the spawn path. Building a loadout only clears weapons now, and the arms are
built after it rather than before.

## Scoring and match state

**Scores lost a kill whenever two landed close together.** `SetCustomProperties` does *not*
update the local cache in an online room — it sends `OpSetPropertiesOfActor` and waits for the
server to echo (`Player.cs:390`). So read-increment-write inside one round trip read the same
value twice and wrote the same number twice. The master keeps its own tally now and publishes
that; it's also the only client that writes scores at all, so two clients can't disagree.

**Nobody but you knew you had died.** `RPC_TakeDamage` was sent to `PV.Owner` alone, so the
victim was the only client that saw its health reach zero. There was no death event for a kill
feed or a match to listen to. It's broadcast now, carrying the weapon and whether it was a
headshot, because the victim is the last client that can still see either.

**Kills and deaths were never reset, and followed you between rooms.** PUN keeps `LocalPlayer`
across rooms and never clears its custom properties — the teardown at
`LoadBalancingClient.cs:3073` says so explicitly — and join publishes them to the new room. So
you arrived in a fresh match carrying the last one's score.

## Found by reading M3 back before testing it

- Deathmatch rolled its three weapons into a field on the master. If the host left, the next
  master had nothing there and rolled a fresh set for the next person who joined.
- A phase transition takes a round trip to come back, and until it does `Phase` and `TimeLeft`
  both still read the old values — so `Update` fired the same transition again every frame, each
  one another property write.
- Leaving the room while dead left the respawn coroutine running, so it came back a few seconds
  later and tried to spawn into a room we'd left.

## Photon settings

**`DevRegion` was still `uae`.** `FixedRegion` was cleared a while back, but `DevRegion`
overrides the best-region pick in the editor and in any development build — so the earlier
"unpinned" fix had never actually applied while developing, and an editor client and a release
build could pick different regions and never see each other's rooms.

**`RpcList` had five RPCs that don't exist** (leftovers from the PUN demos and from methods
deleted with `PlayerManager`) and was missing `RPC_Died`. PUN falls back to sending the method
name as a string when it isn't in the list, so this was bandwidth rather than breakage.

## Smaller

- `Launcher` had no `OnPlayerLeftRoom` at all, so anyone leaving the lobby stayed in the list
  until something else happened to rebuild it.
- `items[itemIndex].Use()` was unguarded, and gun game can briefly leave you holding nothing
  while a rebuild lands.
- Weapon hotkeys were `(i + 1).ToString()` — a string built per weapon per frame purely to ask
  whether a key was down. A `KeyCode` table now.
- The death camera picked a random yaw, so half the time it showed you a wall. It looks at where
  you fell now.

## Performance

- `MonkeyRig` resolved eleven bones by walking the whole skeleton once per bone. `Hitbox` did
  the same for thirteen. One traversal each now.
- Footsteps raycast for ground *before* checking whether you had moved, so eight players idling
  in a lobby cast eight rays a frame to establish that nobody had taken a step.
- `SingleShotGun` called `LayerMask.NameToLayer` — a string lookup — once per pellet, so a
  shotgun blast did it eight times.
- Both HUDs built strings and `GUIStyle`s inside `OnGUI`, which runs at least twice a frame.

## Left alone on purpose

**The map's placeholder textures.** `Game.unity` uses the meme materials and the chimp skin, and
the chimp skin is also the game icon. They're ugly, they're in use, and replacing them is M7.

**`AppVersion` is empty**, so mismatched builds can still find each other's rooms. Setting it
would keep them apart but would also mean bumping it on every change. Noted in the roadmap
instead.

# Third pass — movement retune and the vine, 2026-08-21

## The sandbox handed out deathmatch loadouts until the game restarted

Reported 2026-08-17, fixed 2026-08-21. `MatchState.WeaponsFor` already checked `Sandbox.Active`
first and returned every weapon — that guard was correct and had been since gamemodes existed.
The bug was that nothing actually called it. `PlayerController.LoadoutFor`, the method a spawn
really goes through, read `player.CustomProperties[LoadoutKey]` directly and never asked
`MatchState` anything at all. A deathmatch's `LoadoutKey` — one random weapon — was still sitting
on `PhotonNetwork.LocalPlayer` after leaving that room, because PUN does not clear custom
properties between rooms (the same class of bug as the scores-not-resetting fix in M3, and now
the third time it's bitten this project). The sandbox's own offline room got created on top of
that stale value, and every spawn in it read the deathmatch's leftover loadout instead of asking
whether a match was even running. Only a domain reload actually clears that cache, which is
exactly why a restart was the only thing that ever fixed it.

Fixed by giving `LoadoutFor` the same `Sandbox.Active` guard `WeaponsFor` already had, checked
first, before the property is ever read - rather than relying on the property being cleared at
the right moment, which is precisely the kind of timing PUN has already been shown not to
guarantee.

## Jumping into a slide queued a crouch instead, silently

Reported 2026-08-22 as "jump into slide is still janky" - jumping out of a slide already worked,
but pressing slide while still airborne, intending to land into one, mostly didn't. Root cause in
`UpdateStance`: the branch that decides slide-vs-crouch ran the moment the key was pressed or
buffered, with no check for whether the player was actually on the ground yet. Pressed while
airborne, `grounded && speed >= slideEntrySpeed` failed (grounded was false), so it fell into the
`else` and set `crouching = true` - mid-air, before landing had even happened. By the time the
player actually touched down, `crouching` was already true, which blocked the slide-entry check
from ever running on the landing frame at all - `!sliding && !crouching` was false before the one
frame that mattered arrived. Queuing a slide silently turned into queuing a crouch.

Fixed by moving `grounded` into the outer gate rather than only the inner one, so nothing about
stance is decided at all until the player is actually on the ground. The buffer timestamp still
does its job of remembering the press; it just isn't allowed to act on it early. Also stopped
re-arming the buffer on a press that lands while already sliding - a press mid-slide was never a
queue for anything, and letting it arm the timestamp anyway meant a slide ending from a natural
speed drop while the key was still held could look identical to a landing queue and refire.

## The slide scrape's release tail, retuned 2026-08-21, was the wrong fix

Reported back 2026-08-22 as the scrape still playing after jumping out of a slide. The previous
day's fix slowed the release rate deliberately, to give the sound a tail instead of a hard cutoff
- reported at the time as fixing "ends too early". That reasoning didn't survive a jump-cancel: a
jump doesn't reduce horizontal speed, so `wanted` drops to zero correctly the instant `sliding`
goes false, but the slow release rate took audibly long to actually reach it, which is exactly
what "still playing after jumping out" describes. Reverted the release rate back to fast (24/s,
under 25ms for the whole tail) while keeping the attack-side fix from the same day, which wasn't
in question. Footsteps don't linger after you stop moving; this shouldn't either.

## The peel's melee hold, wrong a third time until it was actually rendered

Guessed wrong twice before this project's own admission (`GunInfo.meleeHold`'s tooltip says so).
Guessed the numbers a third time on 2026-08-22 too, both times from reasoning about the rotation
rather than seeing it - the exact mistake the tooltip already warned about. Built
`Tools/Gorilla Warfare/Photograph the peel` instead: renders the peel with `SingleShotGun`'s exact
pose maths (identity, then `meleeHold`), from a camera positioned like the one built for
`PlayModeProbe`'s player-model shot, with coloured axis rods so the render can be read in
absolute terms instead of guessed from silhouette. First attempt rendered a flat grey frame -
`-nographics` provides no real graphics device for `Camera.Render()` to write into, which
`PlayModeProbe` never hits because it renders across real frames in an actual play session rather
than a single cold `-executeMethod` call. Dropping `-nographics` for this one tool fixed it.
Iterated by eye from there: old value `(72, 0, 18)` held the peel hanging down, blunt tip low,
which matched the screenshot exactly. New value `(-15, 0, 180)` holds it curving up and forward
instead - verified by rendering it, not by reasoning about it a fourth time.

## The fitted hitboxes were worse than the spheres they replaced

Reported 2026-08-22 as "you can't headshot or anything, they're not really accurate." Built
`Tools/Gorilla Warfare/Photograph the hitboxes` the same way the peel got photographed, to
overlay the actual colliders on the actual mesh instead of reading numbers and guessing - and the
overlay showed the arm hitboxes as two purple blobs bigger than the entire torso.

Measured why: the fitting algorithm assigned every sampled vertex to whichever of the thirteen
bone segments it sat geometrically nearest to, and a shoulder joint sits *inside* the body, not
on its surface - so a joint can genuinely be the closest point in space to a swath of chest and
back skin that has nothing to do with the arm. First fix tried was smarter: use the mesh's own
skin weights to decide which limb a vertex belongs to, since that's what it actually moves with,
rather than raw distance. Vertex counts shifted for the torso parts and the arm didn't move -
`p50=0.570` for the arm's own weight-correct vertices, meaning half the skin genuinely dominant-
weighted to the shoulder bone sits over half a metre from it. That's not a fitting bug, it's this
rig's weight painting: broad around the shoulder and hip joints, almost certainly from whatever
auto-weighting produced a free CC0 model, and no formula run against it was ever going to land
somewhere sane.

Replaced fitting entirely with `HitboxProfile.asset` - one hand-set radius per part, per Ryaan's
own call once the cause was clear: stop trying to derive correct numbers from data that measured
as untrustworthy, and let a person who can see the result set them instead. Seeded with a rough
anatomical taper (torso biggest, tapering to the extremities) and checked against
`PlayModeProbe`'s existing coverage floor - the same one written when this problem was 20%
coverage the first time, still 66% - which the auto-fit's replacement now clears at 70%. Final
tuning is intentionally not done here.

Coverage log also names where the remaining gaps are nearest to, which is worth reading before
the next tuning pass: chest and leg carry the most of what's left uncovered at these starting
numbers.

# Fourth pass — feel fixes and the two-handed pose question, 2026-08-22

## Screenshake wasn't noticeable

Reported 2026-08-22. `Juice.cs`'s shake moved the camera's *position* only, up to 0.085m at a
kill - a few centimetres of lateral drift, which barely registers on screen at a normal FOV
because there's nothing nearby to measure it against. Roughly doubled the position (to 0.16m) and
added a rotational component (up to 3.5° of roll and pitch), which is doing most of the new work -
a couple of degrees of camera roll reads as being knocked, where the same magnitude of pure
position reads as nothing. Rotation is applied to `Camera.transform.localRotation` specifically,
same as the position fix already did for `localPosition` - `Look()` only ever writes
`CameraHolder`'s rotation, so the camera's own local rotation was free to use and nothing else in
the project touches it (checked `ViewModelCamera.cs` and `WeaponSway.cs`, which sway the weapon's
own transform, not the camera's).

## The peel's swing was "too static"

Reported 2026-08-22. `SingleShotGun.StabSwing()` was one rotation on one axis (pitch), linearly
interpolated from rest to full extension in 60ms - which is exactly what a hinge does, and reads
as one, because there was nowhere for the motion to come *from*. Added a short windup (a small
pull-back and twist before the stab), a second rotation axis so it reads as a stab rather than a
swing on rails, eased time instead of linear so the strike accelerates into contact instead of
moving at one constant speed throughout, and a small `Juice.Shake` on contact - the peel is the
one weapon that lands its hit right in front of the camera, at melee range, and had nothing
marking the moment at all.

## The two-handed grip pose, investigated but not resolved

Reported 2026-08-22 as missing entirely - see "Open now" above for the live status. Worth
recording what was actually checked, since it's inconclusive rather than clean.

`PlayModeProbe`'s own numeric check (`CheckArmsAreGripping`) passes: the off hand sits reliably
above the gun hand on a two-handed weapon, below it on a one-handed one. But the only visual
render available of it is the probe's enemy stand-in shot, and that camera is placed dead-on in
front of the character, looking straight at it - a pose with both arms reaching *toward* the
camera foreshortens hard from that specific angle regardless of whether the pose itself is right,
and the rifle and pistol renders looked nearly identical from it, which isn't conclusive either
way.

Also worth knowing for next time: `-nographics` silently skips every screenshot `PlayModeProbe`
tries to take (logged as "skipped, no graphics device") without failing the run. The
`Logs/probe-shots/` files this pass initially checked were seven days stale as a result - actual
current renders needed a second run without `-nographics`. This pass's own earlier note above,
on photographing the peel, claims dropping `-nographics` was a `PeelPhotographer`-only problem
because "`PlayModeProbe` never hits" it - true for every check *except* the screenshots, which
this pass found it hits too. The rest of the probe's checks (the numeric ones) do run fine under
`-nographics`; only the `Capture()` calls need it dropped.

Deliberately not re-guessed a third time from reasoning about the rig alone - this is the same
system the peel's melee hold was wrong about twice doing exactly that, see the pass above. Needs
either a better camera angle or a person watching it in real play.

# Fifth pass — the freeze frame, a real momentum bug, and the slide chain, 2026-08-22

## The freeze frame "doesn't work"

Reported outright as broken, not just weak. Checked for a competing writer first rather than
assume it was a numbers problem again - grepped every script for `Time.timeScale` and `Juice` is
the only thing that ever touches it, so nothing was fighting it. The actual number: 110ms at 6%
speed on a kill, which is under a tenth of a second of real time - closer to a flicker than
anything ULTRAKILL's own doc comment in this file already promises. Raised the hold to 260ms and
the stop itself slightly harder (6% to 4.5%). Body shots and headshots scale off the same
`strength` as before, so a stray pellet still barely stutters - only the top end changed.

## Camera didn't drop into a slide

Reported as a regression - "used to happen, doesn't now." Searched every script for anything that
ever wrote to `cameraHolder`'s local *position* (not rotation) and found nothing at all - the
capsule genuinely shrinks in `PlayerMovement.UpdateStance`, but nothing was reading that and
moving the eye down with it. Whatever produced the old behaviour is gone without a trace now, not
worth chasing further back than confirming it doesn't exist today. Added `PlayerMovement.
StanceFraction` (the capsule's own eased height ratio, so the camera can't drift out of sync with
the collider it's supposed to track) and a `crouchCameraDrop` field in `PlayerController.Look()`
that reads it.

## Wall collisions never cost any speed

Reported as: hit a wall at speed, and as long as forward is still held, turning slightly left or
right keeps all of it - "momentum doesn't immediately die out." Root cause: nothing in
`PlayerMovement` ever clipped `velocity` against a collision at all. `controller.Move()` stops
your *position* at a wall, but the `velocity` field driving it next frame was never told - so it
kept its full pre-collision magnitude forever, and the CharacterController's own wall-sliding
made it *look* like physics was handling it when nothing was correcting the number underneath.
Added `OnControllerColliderHit`, doing the classic Quake `ClipVelocity`: remove the component of
velocity pointing into whatever was hit, but only for near-vertical surfaces (a wall, not a floor
or ceiling - the floor's own handling in `GroundMove` already owns vertical speed and clipping it
here too would fight that). A graze along a wall keeps its tangential speed, which is correct
wall-sliding; a square hit has almost all its velocity pointing into the normal and comes out
near zero, which is the actual fix.

## The slide chain compounded into "quickly, way too much speed"

Reported as capable of reaching absurd speed fast, "not ideal." The chain's own safety ceiling
(`maxHorizontalSpeed`, 45 m/s) already existed and was doing its job - the actual problem was how
fast a chain reached it. The slide kick (`flat *= kick`) multiplied whatever velocity a player
already had, not a stable baseline - so chain 2's kick landed on chain 1's *already-boosted*
result, chain 3's landed on that, and three or four chained slide-hops reached the ceiling within
a couple of seconds. Changed the kick to multiply `maxGroundSpeed` (a fixed reference) and add
that as a flat bonus on top of current speed instead of re-multiplying it - reproduces the exact
same chain-1 number (current speed happens to equal the baseline there, entering from a normal
run), while every extra link now adds a bounded amount instead of compounding on a stack.

## The bullet-impact mesh-collider report, investigated and not confirmed

Reported as: impacts show up on box colliders but not mesh colliders (the palm trees, rocks and
logs `MapDressing` places). Built a throwaway diagnostic (`Tools > Gorilla Warfare`, removed once
done) that opened the real `Game.unity`, entered play mode, and ran `BulletDecal.Spawn`'s exact
re-raycast against every mesh and box collider actually in the scene - all 67 mesh colliders and
all 120 box colliders re-raycast clean, and a decal spawned on a scaled, rotated tree came out an
undistorted, correctly sized quad (its `lossyScale` was uniform despite the tree's own 6×8×6
non-uniform scale, which rules out a shear from `SetParent(..., true)`). A real screenshot of both
cases showed the same faint-but-present mark on both - the decal has always been a subtle multiply
blend by design (see `BulletDecal`'s own class doc), on both surfaces equally.

No differential bug was ever found between the two collider types. Raised `BulletDecal`'s
`LiftOff` and the impact puff's own offsets as a hedge against the one plausible mechanism that
couldn't be ruled out either way - a coarse, low-poly mesh's per-triangle normal has more room to
be a few degrees off the true local surface than a flat wall's, which at the old tiny offset could
occasionally place an effect just inside the mesh instead of just outside it. If impacts on mesh
props still read as missing after this, it needs a person shooting one and saying so - this was
checked as hard as it can be checked without that.

## `SpeedRush` threw on every single spawn

Found while verifying the above, unrelated to any of it. `readonly float wobbleSeed =
Random.Range(0f, 100f);` is a field initializer, and Unity doesn't allow `Random.Range` to be
called from one - `PlayModeProbe`'s log had it twice, once per player build in that run, silently
eaten rather than failing anything. Moved into `Awake()`.

## New this pass: hit-flash, a heartbeat, and HUD punches

Not bugs - added while addressing feedback that specifically asked for these, so recorded here
rather than scattered across commit messages. See `roadmap.md`'s M5/M7 sections for what shipped:
a white flash across a gorilla's body on taking a hit (broadcast to everyone watching, not just
the victim - see the note in `BulletDecal.cs`), an audio heartbeat under the existing critical-
health screen edge (which was visual-only until now), and the slide combo's punch-on-change
treatment extended to the health number, ammo count and kill feed lines.

# Sixth pass — particles done properly, and the movement tech, 2026-08-22

## The reload spin was "unbearably slow"

One full 360 stretched across the whole `reloadTime`, so a longer reload just meant a slower
single spin instead of more of them. Fixed the *rate* (900°/s) rather than the rotation count -
`laps = round(reloadTime * rate / 360)`, always at least one, always landing exactly on a whole
lap by the moment the magazine actually swaps, same guarantee the original had.

## Bhop was gaining speed way too fast

See `roadmap.md`'s movement tuning section - a direct side effect of the same-day `airSpeedCap`
raise (0.762 to 2.5) for the slide-hop redirect complaint. `bhopKeep` down from 1 to 0.92.

## The pineapple's glow didn't look like a glow

A point light next to an object lights the room, not the object itself - `Projectile.BuildGlow()`
only ever had the light. Added a soft additive billboard riding alongside it, camera-faced every
frame independently of the shell's own tumble, the same trick every other flash in this game
already uses to look like the thing making the light rather than a thing sitting near one.

## Muzzle flash and bullet impact, rebuilt on real ParticleSystems

Reported as looking bad and specifically as not using Unity's particle system properly - both
fair. `MuzzleFlash` was a single `FlashSprite` billboard per shot; bullet impact's "debris" was a
loop of `FlashSprite`s that faded in place at a fixed offset with no velocity at all, which is
likely the real reason impacts kept reading as not appearing regardless of what collider they
landed on - nothing in the effect ever actually moved. Looked at how impact VFX is actually built
elsewhere (start speed 2-5 m/s, a Cone shape, lifetime 0.1-0.25s is the standard shape) rather than
patch the old approach a third time. Both are real `ParticleSystem` bursts now, with actual
outward velocity and gravity. Caught by `PlayModeProbe` immediately after: the muzzle flash's new
particle system is a *persistent* renderer (unlike the old one-shot `FlashSprite`s), and nothing
had ever hidden it while aiming - added `MuzzleFlash.SetVisible`, called from
`SingleShotGun.SetVisible` alongside the weapon model's own renderers.

## Critical health pulse, too intense

Peak alpha 0.72 on a 0.45-wide beat swing meant the worst moments were most of the screen edge
solid red. Down to 0.42 peak, 0.32 swing.

## Wall run, vault, ground slam, air brake - built

See `ideas.md`'s movement tech section for the design (written down the same day, before these
were built, so it doubles as the account of what these actually do) and `roadmap.md`'s Unverified
section for what still needs a person playing it. Worth naming here what each one's balance
argument actually is, since "make sure it's balanced" was the direct ask: none of the four add
net horizontal speed on their own. Vault and the ground slam trade height for control, not for
more speed. The air brake only ever removes speed. Wall run preserves whatever horizontal speed
was already there rather than adding to it - no `Accelerate()` call runs during one. The existing
`maxHorizontalSpeed` safety ceiling still applies underneath all four regardless, the same as it
already did for slide chains and bhop.

# Seventh pass — the movement tech's first playtest, 2026-08-22

Everything below was reported after actually trying the four mechanics from the sixth pass -
they compiled clean and passed every automated check, but a person pressing the keys found real
problems none of that could see, which is exactly the gap `roadmap.md`'s Unverified section
already existed to flag.

## Ground pound was firing every time a slide was buffered

Reported as "sliding is kinda hard to do when landing because it just ground pounds you." Root
cause: ground pound and the slide buffer both read `KeyBinds.Action.Walk`, and ground pound's own
trigger condition was "falling" - which is also true of every single landing, buffered slide or
not. Pressing Walk in the air to queue a slide for touchdown satisfied the slam's condition just
as well, and fired it instead, every time. Gave ground pound its own binding
(`KeyBinds.Action.GroundPound`, `LeftControl` by default) rather than retuning the shared-key
logic further - the air brake's half of that same design (rising velocity triggers it) never had
this problem, since you can't be about to land while still going up, so it stayed on Walk.

## No particle effect on ground pound

Reported separately, same session. `SlamLandingEffects()` spawned its burst at `transform.position`,
which is the `CharacterController`'s own pivot - roughly chest height, not the ground. The effect
was firing; it was floating at head height instead of reading as a landing impact. Now built from
`controller.bounds.min.y`, which accounts for the capsule's actual center/height rather than
assuming the transform's origin sits at the feet.

## Wall running was "really weird"

The original latched on automatically - airborne, fast enough, moving toward a wall - with no
button, which made it unpredictable to start on purpose and impossible to end on purpose short of
losing the wall or timing out. Direct request was explicit: hold Walk near a wall to stick to it,
let go and you fall immediately. Rebuilt as exactly that - proximity plus holding the key, no
speed threshold, no "moving toward it" requirement. Release is now the primary way out; a jump is
still the secondary one, and the only one with a push behind it.

## Vaulting "doesn't work right now"

The original only checked while `grounded`, at speed, with nothing pressed - but the one thing an
actual player does at a ledge worth vaulting is jump at it, and the moment they did, `grounded`
went false and the check never ran again for that approach. It wasn't unreliable, it was checking
a condition a real attempt at using it would never be in. Retriggered on a second jump press while
already airborne instead - direct request ("make vaulting possible by doubling jumping") - which
also means it costs nothing when there's no ledge: press it in open air and nothing happens,
no free extra jump granted.

## The air brake's fallback sound was reported as identical to sliding's

Both used to reach for the same `Slide` bank - an actual slide reads it directly
through `SpeedRush`'s scrape loop, and the air brake's fallback pitched the same bank up as a
one-shot. Close enough in practice that the report was "the sound effect for sliding shouldn't be
the same as [the air brake's]." Air brake's fallback now pitches `Footstep` instead - a shape nothing
else in the game already draws its own sound from - sharply up and short, closer to a skid than a
slide.

## Dead code: `ToonOutline.cs` / `ToonOutline.shader`

Removed outright rather than kept as reference. Confirmed unused first - no `AddComponent<ToonOutline>`
or `ToonOutline.ApplyTo` call anywhere in the project - not just "looked unused." See
`Assets/Shaders/README.txt` for the full account of why it was built and replaced; the technique
itself is standard enough to rebuild from scratch if a future simple-convex-prop case wants it.
