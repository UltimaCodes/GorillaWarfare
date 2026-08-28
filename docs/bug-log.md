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

Nothing open. The two-handed grip pose (the only thing that was here) turned out not to be a
pose problem at all - it was resolved 2026-08-23 by finding what was actually wrong, see the
tenth pass below.

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

**Resolved 2026-08-23, and it was never a pose problem at all** - see the tenth pass. A proper
camera angle (`Tools/Gorilla Warfare/Photograph the grip`) showed the weapon rendering roughly two
metres off the character's actual hand, which is why every angle looked equally wrong: there was
no pose to judge, the gun simply wasn't where the hand was.

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

## Dead code, second pass: `MenuCleanup.cs`, `ModelProbe.cs`, an orphaned field

Audited every script in `Assets/Scripts` (54, all confirmed live - this project's heavy use of
runtime `AddComponent` makes a naive scene/prefab-only reference scan produce false positives, so
each one was checked against actual code call sites too) and `Assets/Editor` (35 - 26 carry
`[MenuItem]` and are deliberately-kept manual tools, 7 more are the documented check suite plus
`AssetFixups`, both cross-referenced in `roadmap.md`/`working-notes.md`).

Two editor tools had no caller, no `[MenuItem]`, and no doc mention anywhere:

- **`MenuCleanup.cs`** — its entire job was removing a stray looping `AudioSource` from
  `Menu.unity`. That scene has zero `AudioSource` components on it now - the job it existed to do
  is already done.
- **`ModelProbe.cs`** — a batch-mode probe for the gorilla FBX import, from before `MonkeyRig`
  existed. Confirmed stale rather than just unreferenced: it checks for bone names like
  `b_Spine02` and `b_Head` (Mixamo-style naming), but the rig actually shipped uses `RIGHTSHOULDER`,
  `LEFTELBOW` and so on - a different scheme entirely, so even run today it would report every
  bone missing. It also carries a `TryHumanoid()` method that mutates the model's import settings
  to test Mixamo retargeting, an approach `working-notes.md`'s "Decided, don't relitigate" section
  already records as rejected in favour of driving the bones directly.

Also found by hand while re-reading the movement tech's own code, not by the audit: a
`wallRunMinSpeed` field left declared and tooltipped but never read anywhere, from the wall-run
rewrite two passes up - the speed gate it used to enforce was dropped on purpose (direct request:
proximity plus holding the key, no speed requirement) but the now-dead field describing it never
got removed alongside the logic. Removed.

# Eighth pass — reload, wall-run's own sound, UI shake, and a two-bug sandbox, 2026-08-22

## The reload spin was "wayy too weird"

The previous pass's fix (a single 360° spin, rate fixed so it always lands on a whole lap) was
still a full spin - reported directly as not matching how reloads actually read elsewhere.
Searched for Redmatch 2's specific reload animation for reference and found nothing precise (the
only search results describing its reload were about the mechanic - shooting cancels it - not the
visual), so rather than guess at a specific reference that couldn't be confirmed, replaced the
spin with the genre-standard shape instead: a dip down and slight tilt, a hold, a return - no
rotation-spin at all. `SingleShotGun.UpdateReloadFlip()` eases out into the dip over the first 20%
of `reloadTime`, holds through the middle 60%, eases back in over the last 20%.

## Wall run's scrape was reported as identical to sliding's

Same root cause as the air brake's fallback sound two passes up, different mechanic: wall run's
tick effects reached for `GameAudio.Slide` as a fallback pitched up, which is close enough to an
actual slide to read as literally the same sound. Built wall run its own persistent-`AudioSource`
scrape, same pattern `SpeedRush` already uses for the slide's own loop (attack/release easing,
Perlin-wobbled pitch) rather than another one-shot fallback - loads from `Audio/WallRun` if
populated, falls back to the vine's clip folder otherwise. The old one-shot calls in
`WallRunStartEffects`/`WallRunTickEffects` are gone; those methods now only handle the dust burst.

## Screenshake didn't touch the UI

Reported directly - a screen-space overlay canvas doesn't move with the camera at all, so a hit
that visibly knocked the world left the HUD dead still. Added `Juice.Amount`, the current shake
normalised to 0-1 (read-only, so nothing downstream needs to know the actual `maxShake` constant),
and `GameHud.UpdateHudShake()`, which offsets the whole canvas `RectTransform` with the same
Perlin-noise wobble the camera's own shake already uses, scaled to a maximum of 14 pixels.

## Ground slams could be spammed

Reported directly. Nothing gated repeated presses - each slam fires on its own key now (see the
seventh pass) with no cooldown behind it at all, so mashing the key in the air queued another one
the instant the last one's `velocity.y` override wore off. Added a 1.4s cooldown that starts from
the *landing*, not the press - starting it from the press would still allow a slam queued deep
into the previous one's descent, and landing is the moment that actually ends its effect on the
player.

## Vault, removed

Retuned once already this same day (grounded-and-fast trigger to a double-jump-press trigger,
seventh pass), reported as still not working on the very next playtest. Removed outright per
direct instruction rather than guessed at a third time - see `ideas.md`'s movement tech section
for the design record and `roadmap.md`'s movement tuning section for the current status. Deleted
`TryVault()`, `VaultEffects()`, every vault-only field, and the `GameAudio.Vault` bank constant it
was the only reader of.

## The camera could see through walls

Reported as "basically wallhacks" - standing close to a wall could put the inner `Camera` on the
far side of it, since nothing had ever kept the camera itself out of solid geometry, only the
`CharacterController` driving the body. Added `PlayerController.PullCameraOutOfWalls()`, a
spherecast from `cameraHolder` (the parent) to the camera's actual position each frame; if it would
clip something, the *holder* is pulled back along that direction in world space by the clipped
amount. Deliberately not applied to the `Camera` child's own local transform - `Juice.cs` owns and
caches that child's local rest position/rotation for screenshake, and writing to it here would
either get silently overwritten by Juice next frame or corrupt what Juice considers "rest."

## The sandbox's training dummies had never worked, and it took two bugs to explain why

Reported directly, in those words. The first bug looked like the whole explanation:
`RoomManager.PlaceDummies()` ran from `OnJoinedRoom`, which fires the moment the room is created -
while still standing in the *menu* scene, before `SpawnManager` or the map itself exist - so
dummies were placed relative to world origin in a scene about to be torn down, and died with it a
moment later. Moved the call to `OnSceneLoaded`, gated on the game scene the same way `TrySpawn`
already is.

Built `SandboxDummyCheck.cs` (temporary, removed once this concluded) to confirm it with a real
run rather than trust the reasoning, since this project's rig has been guessed wrong before. It
failed - zero dummies, and the active scene's name was still blank long after `PhotonNetwork.
InRoom` had already gone true. That's not what a race condition between two callbacks looks like;
that's a scene that never loaded at all.

Traced the actual call chain instead of re-guessing: `SettingsMenu.EnterSandbox()` calls
`Sandbox.Enter()` and nothing else. `Sandbox.Enter()` disconnects, sets `OfflineMode`, and calls
`CreateRoom` - and nothing else either. The one place in the whole project that calls
`PhotonNetwork.LoadLevel` for a fresh room is `Launcher.StartGame()`, a button the *lobby* host
presses once everyone's ready - and `RoomManager.OnJoinedRoom()`'s own `LoadLevel` call exists only
for a late joiner arriving at a match already in progress, deliberately gated on `!PhotonNetwork.
IsMasterClient`. Creating your own room always makes you its master client. So a sandbox room got
created, and absolutely nothing in the codebase was ever going to load the map to go with it - the
menu-scene timing bug fixed first was real, but fixing it changed nothing, because the scene load
it was racing against didn't exist yet either.

Fixed in `Sandbox.cs`'s own `Open()` coroutine: after `CreateRoom`, wait for `PhotonNetwork.
InRoom`, then call `PhotonNetwork.LoadLevel(RoomManager.gameSceneIndex)` directly - the same
create-room-then-load pattern `PlayModeProbe` already uses successfully for its own automated
runs. Re-ran `SandboxDummyCheck` after this second fix: 4 dummies found, each with its 13 hitboxes,
positioned in the actual loaded `Game` scene. Removed the check afterward, per the usual
convention for these throwaway diagnostics.

# Ninth pass — UI shake for real, a slide-hop regression, and the peel's actual orientation, 2026-08-23

## The HUD still didn't shake

The previous pass's fix drove `canvasRect.anchoredPosition` directly - and that field belongs to
the root `Canvas`, which `HudBuilder` sets to `RenderMode.ScreenSpaceOverlay`. Unity ignores an
overlay canvas's *own* RectTransform entirely when placing it on screen; it always fills the
viewport exactly regardless of anchored position, local rotation or scale. Driving it was writing
to a value nothing ever reads. Fixed by inserting a child RectTransform (`~HudShakeRoot`) between
the canvas and everything currently under it - built at runtime in `GameHud.Awake()` by
reparenting every existing direct child into it once, so nothing about `HudBuilder`'s own prefab
had to change. A canvas's *children* have no such exemption, so the shake now actually moves
something the renderer looks at.

## Bhop circle-strafing gained speed "a lot, really quickly"

`airSpeedCap` (the per-hop air-strafe ceiling) was raised from the authentic CS:S value (0.762)
to 2.5 two passes ago, specifically so a slide-hop's redirect had room to work - but `AirMove`
handed that same generous cap to *every* jump, chained off a slide or not. Combined with
`airAccel` already sitting above the CS:S reference value too, plain circle-strafing (no slide
involved at all) was gaining speed at roughly 4x the authentic rate. Split the cap in two:
`pureBhopAirSpeedCap` (0.9, close to the original) applies outside an active slide-hop chain,
`airSpeedCap` (2.5) still applies while one is running (`Time.time < chainExpires`, the same
signal the chain-bonus system already reads). A slide-hop keeps the room it was given; naked
circle-jumping does not.

## Slide-hopping stopped working, and it was the air brake's fault

Reported as "you can't slide-hop any more for some reason." Ground pound moved off Walk a pass ago
specifically because landing is always falling, and the air brake was left behind on the reasoning
that "you can't be about to land while still going up" - true for an ordinary jump, false for a
slide-hop chain specifically: re-pressing Walk *while still rising* out of the last hop, to buffer
the next slide for touchdown, is exactly the input pattern a chain runs on. That press satisfied
the air brake's rising-only condition just as well and fired it, cutting horizontal speed to 12%
on the exact input meant to extend the chain. Gave the air brake its own key
(`KeyBinds.Action.AirBrake`, `LeftAlt` by default) - same fix shape as ground pound's, and for the
same underlying reason: the "vertical velocity tells them apart" premise never accounted for a
press that means two different things depending on what happens next, only different things
depending on what already happened.

## Wall running "gets you stuck on walls for a second"

Every frame of an active wall run clips velocity to exactly zero along the wall's own normal, on
purpose - that's what keeps you tracking the wall's surface instead of drifting off it. The
consequence: at the instant a run ends *passively* (released, lost the wall, timed out), velocity
has precisely nothing pointing away from that surface. Nothing carries you off it - you keep
contacting the same wall under gravity, and `OnControllerColliderHit` keeps re-clipping the same
nothing, which reads as being stuck rather than falling away. The deliberate wall-jump exit already
had a payoff push (`wallJumpAway`); the passive exit had none at all. Added a much smaller
`wallRunPassiveSeparation` push (1.1 against wall-jump's 5.5) on any non-jump `EndWallRun` - just
enough to actually separate, not a second payoff.

## The peel's melee hold was pointing backward, not forward

Reported as "hasn't been rotated yet" and "should have a crosshair." Two separate, real bugs:

**The rotation.** `meleeHold: {x: -15, y: 0, z: 180}` was verified by rendering it, four passes
ago - and misread. `WeaponCheck`'s own independent measurement (`bananaPeel points forward, z
should be the longest axis`) confirms the raw mesh's long axis is +Z at identity, same as every
other weapon; a 180° rotation around Y maps +Z to -Z, which reverses a shape's forward direction
outright rather than merely angling it. The old calibration render's "front" view looked plausible
at a glance because a foreshortened cross-section of a curved shape still reads as *something* -
it just wasn't reading as "pointed at the enemy." Re-rendered with `Tools/Gorilla Warfare/
Photograph the peel` at several candidate values instead of reasoning about it a third time (this
exact system has been wrong twice before, see the third pass above); `{x: -20, y: 0, z: 0}` - no Y
rotation at all - reads correctly forward-and-down from both the oblique and first-person-facing
camera angles. Also added `Peel` to `PlayModeProbe.CaptureEveryWeapon`'s own screenshot loop
(`viewmodel-peel.png`) - it was never in `WeaponLoadout.AllWeapons` (deliberately; that list is
the random-roll pool and melee is guaranteed separately), which meant nothing had ever screenshotted
it being *held*, only the isolated calibration renders.

**The crosshair.** `Peel.asset` had `reticle: Dot` - correct for a spread weapon where drawing the
cone is noise, wrong for melee, which has no spread to hide. `Dot` style suppresses all four tick
marks unconditionally and the dot itself only draws when `GameSettings.CrosshairDot` is on, which
defaults `false` - so with default settings the peel drew no reticle at all. Changed to `Cross`,
same as everything else that doesn't have a specific reason not to.

**A found-but-not-the-bug side note:** the `viewmodel-X` screenshot loop's own camera framing
turned out to show nothing recognizable for *any* weapon, not just the peel - `map-floor.png`,
meant to be a deliberately tilted-down shot, is pixel-identical to a normal level view. Traced to
`PlayerController.Look()` recomputing `cameraHolder`'s rotation from `verticalLookRotation` every
single frame regardless of what anything else just set - correct for real input, but batch mode
has no real mouse, so `verticalLookRotation` never moves and any manual test override to the
camera's rotation is overwritten the very next frame. Pre-existing, unrelated to anything changed
this pass, and not fixed here - noted so the next person chasing a "why is nothing in this
screenshot" question doesn't spend as long on it as this one did.

## Grenade (pineapple) glow, brightened rather than re-diagnosed

Reported as still not reading as glowing. The existing implementation (a real point light plus a
camera-facing additive billboard, from the sixth pass) is structurally sound - reviewed rather than
rebuilt, since nothing about it looks broken. Raised the light's intensity (2.2 to 3.2) and the
billboard's base scale (3.4x radius to 5x), and added a slow Perlin pulse to the billboard's size
so it reads as something still burning in flight rather than a static decal riding along with it.

## New: the picture itself reacts to a hit, not just the camera and the HUD

Added while asked for "more oomph" generally, rather than for a specific report. `ShaderStack`'s
post-processing presets (`Full`/`Overripe`) already carry a `Vignette` and, on `Overripe`, a
`ChromaticAberration` - both static, set once per preset and never touched again. Added
`ShaderStack.Pulse(strength)`, called from `Juice.Hit`/`Juice.Shake` alongside the existing
screenshake, which bumps both settings' intensity above the preset's own baseline and decays it
back on unscaled time (same reasoning as everything else that has to survive hitstop). Only wired
up when the active preset already added that setting - a player who picked `Clean` for a plainer
picture or a better framerate doesn't get `Overripe`'s aberration smuggled in through a hit
reaction. `Full`'s post-processing stack itself was already built and working (`ShaderStack.cs`,
undated in its own comments but clearly pre-existing) - `roadmap.md`'s M7 section had this listed
as `[ ]` despite it being live; corrected alongside this pass. Same story for the multikill
callout system (`GameHud.ShowKill`/`MultikillName`, `PlayerController.RewardKill`) - fully built,
already themed ("OVERRIPE", "BLENDED", "FRUIT SALAD"), found while looking for exactly this
feature to build and confirmed already shipped instead.

# Tenth pass — remote players couldn't actually see what you were holding, 2026-08-23

Asked directly to properly fix it: "like Counter-Strike, where other players can see what weapon
you're holding." Picked up mid-investigation of the still-open two-handed grip pose report, which
turned out to be the same underlying bug wearing a different description.

## The weapon rendered roughly two metres off the hand

Built `Tools/Gorilla Warfare/Photograph the grip` - the same real `SingleShotGun`/
`AttachWeaponsToHand` path a remote copy actually uses, from an angle that isn't dead-on front -
to finally get a real look at this instead of judging it from `PlayModeProbe`'s foreshortening
angle again. First render: the weapon floating in the corner of frame, disconnected from the body
entirely.

Root cause: `PlayerController.AttachWeaponsToHand` parents the weapon holder onto the hand bone,
calls `Hitbox.Neutralise` (which sets the holder's own `localScale` to cancel the bone's 100x
import scale), then sets `itemHolder.localPosition = weaponHandOffset` - a small, real-world-scale
nudge, `(0.02, 0, 0.06)`. `Neutralise` only ever fixes the weapon's own *rendered size*; it does
nothing about position, because Unity multiplies a child's `localPosition` by the *parent's*
`lossyScale` when composing world position, regardless of what the child's own scale has just been
set to. Measured directly rather than assumed: `hand.lossyScale` reads `(100, 100, 100)` on this
rig, today, not a leftover comment from an old one. A 2-6cm offset parented under a 100x-scaled
bone lands one to six *metres* off the hand - which is a bug old enough that a comment already on
this exact line described the class of it ("positioned metres off your hand because the offset
was being multiplied by a hundred too") while the actual fix next to it only ever addressed size.

Fixed by pre-dividing the offset by the bone's own `lossyScale` before assigning it, so the
multiply Unity does on the way back out cancels to the originally-intended few centimetres.

## The weapon still wasn't in the hand - a second, unrelated bug

Fixing the scale issue moved the weapon roughly onto the character's body, but not into the hand:
it rendered near the hip while the visible fist was clearly gripping near the chest. Measured the
actual bone chain rather than guess again: `RIGHTHOLD` (the weapon attach bone) is *not* a direct
child of `RIGHTELBOW` on this rig - there's a `RIGHTWRIST` bone in between that the arm IK's own
code never accounted for. `MonkeyRig.MeasureArms`'s fallback logic (`rightHandEnd` = RIGHTHOLD if
its parent is the elbow, otherwise the elbow's first child) silently picked `RIGHTWRIST` instead,
which is exactly why the *fist* always looked right - the two-bone solve was correctly aiming at
the wrist the whole time. Nothing, though, ever rotated the wrist itself; `RIGHTHOLD` just rode
along as a rigid child of it, landing wherever the wrist's own bind-pose offset pointed once the
arm had rotated through however many degrees the reach needed - a direction that has nothing to
do with the grip target once the rest pose (arms out to the sides, per this rig's own T-pose) has
been rotated significantly away from it.

First fix attempt tried correcting this the way the existing code corrects everything else - aim
the wrist at the target with the same `AimBone` helper the shoulder and elbow already use.
Measured before shipping it (logging the actual target and wrist position) and found the wrist was
already sitting exactly on the target - unsurprising, since that's what its own aim/length were
measured against. There was no leftover direction to aim it *toward*; the whole approach was
solving a problem that didn't exist at that bone.

The actual fix: `RIGHTHOLD` carries no skin weights - nothing about the visible mesh depends on it
staying a rigid child of the wrist through this hierarchy, since it exists purely as a weapon
attachment point. So it's placed directly now, position matched to the wrist (already correct) and
rotation inherited from the forearm, rather than corrected through a parent-child relationship that
was producing the wrong answer. Confirmed by rendering both a two-handed weapon (Rifle) and a
one-handed one (Pistol) from the fixed path - both now sit visibly gripped rather than floating.

Left `Tools/Gorilla Warfare/Photograph the grip` in as a permanent tool, same reasoning as
`PeelPhotographer`/`HitboxPhotographer` - this is exactly the kind of thing that goes unnoticed
without a real render, and it's now the second time on this project that a "the pose looks wrong"
report turned out to actually be a position/scale bug wearing a pose-shaped description.

Not investigated further: the off-hand's own bracing position on a two-handed weapon. Nothing
attaches to it (no equivalent of RIGHTHOLD on the left side), so it isn't subject to either bug
found here, and no report has specifically called it out - worth a look next time someone is
actually staring at a two-handed grip in play.

# Eleventh pass — the ground slam's impact was invisible to everyone but the person doing it, 2026-08-23

Asked directly for "ground pounding impact effects." The effect itself already existed (sixth and
seventh passes: a dust burst at the feet, a thud), so the ask was read as "make it read as an
actual impact" - and checking what was there turned up something bigger than a tuning problem.

## Nobody standing nearby ever saw or heard it

`PlayerMovement` only exists on the owner's own copy - `PlayerController.Start` destroys it on
every remote copy, since movement is simulated locally and a `PhotonTransformView` drives
everyone else's position instead. Every effect method in that file (`SlamLandingEffects`,
`WallRunStartEffects`, `AirBrakeEffects`, all of it) calls local-only helpers -
`GameHud`-independent particle bursts and `GameAudio.PlayShaped`, which is explicitly
non-positional. None of it was ever networked. A player landing a ground slam two metres from
someone else produced nothing on that other person's screen or speakers at all - not muted, not
faint, just never sent.

Fixed the same way gunfire already is: `PlayerController.ReportShot`/`RPC_WeaponFired` was the
existing pattern for "a local-only action needs everyone to see it," so `SlamLandingEffects` now
calls a new `PlayerController.ReportGroundSlam(point)`, which fires `RPC_GroundSlamImpact` at
`RpcTarget.All` - every client, including the one who did it, builds the identical burst and
sound. `Juice.Hit` (hitstop, screenshake, the post-processing pulse from the ninth pass) stays a
direct, local-only call in `SlamLandingEffects` itself - a bystander's camera has no reason to
shake for someone else's landing.

`PlayerMovement.MovementBurst` and `DustTint` were made `public static` rather than duplicated,
so the RPC (which lives on `PlayerController`, and has no `PlayerMovement` to call through on a
remote copy either) can build the exact same particle system everything else in that file
already uses.

## Made the burst itself bigger while already in there

The original was one ten-particle dust puff, which read as a footstep even once it was actually
visible - reported as wanting real "impact effects," not just visibility. Now two layered bursts:
a wider dust cloud (22 particles, up from 10) plus a faster, tighter "spark" layer reading as
thrown debris rather than settling dust. `GameAudio.PlayAtShaped` is new too - `PlayShaped`'s
same missing-bank fallback, but positional (`PlayAt`'s spatialBlend/distance falloff) instead of
2D, since the whole point this pass was a sound everyone nearby hears coming *from* where it
happened.

Verified end to end with a temporary offline-room test (`ReportGroundSlam` called directly on a
spawned player, checked for a thrown exception and confirmed the particle GameObject actually got
built) rather than trusting the RPC wiring compiled clean and calling it done - removed after
confirming both.

# Twelfth pass — a new sky, wall running cut for good, and a simpler ledge hop, 2026-08-23

## The skybox, rebuilt against a reference

Asked directly to match a reference screenshot - a soft, photographic blue sky with wispy
streaked clouds and a glowing sun, nothing like the flat-banded jungle-canopy look the shader had.
Rewrote `Assets/Shaders/JungleSky.shader` entirely: a smooth three-stop gradient (horizon to mid
sky to zenith, continuous rather than the old hard bands) and a cloud layer built from hand-rolled
value noise - four octaves of FBM, the sample domain squashed hard along one axis before reading
it (round noise blobs read as cauliflower; squashed ones read as wind-blown streaks), plus a
second coarser noise field bent into the sample position first (domain warp) for the slightly
turbulent quality a single noise octave never quite gets. Dropped the old shader's "god rays"
entirely - the reference has none, just the sun itself with a soft halo.

Kept the shader's registered name (`Skybox/JungleSky`) and file name so nothing that already
pointed at either (`SkyboxPhotographer.cs`, the `JungleSky.mat` asset) needed to change, even
though the theme is no longer jungle-canopy - a rename would have meant chasing down every
reference for a purely cosmetic win.

**First render was wrong in a way that had nothing to do with the new shader.** The sky came out
half solid yellow, the old `_HazeHeight`/`_HorizonColor`/`_ZenithColor`/`_SunSize` values baked
into `JungleSky.mat` were still there, and several of those property *names* are reused by the
new shader too (`_HorizonColor`, `_ZenithColor`, `_SunSize`, `_SunDirection`) - Unity carries a
material's saved overrides forward across a shader swap for any property name that still exists,
so the old jungle-canopy yellow/green values were silently overriding the new shader's own
defaults rather than the material falling through to them. Cleared every saved property in
`JungleSky.mat` rather than track down which specific names collided.

Sun halo was next - the first pass rendered as a wash covering most of the frame, an overexposed-
photo look rather than a glowing disc. Retuned by rendering, not guessing again: halo tightened
(`_SunHaloSize` 10 to 26) and both the halo and the ambient sun-side sky brightening turned down
(`_SunHaloIntensity` 0.6 to 0.32, `_SunScatter` 0.35 to 0.16). Confirmed against the real
in-game camera afterward, not just the isolated `SkyboxPhotographer` rig - `PlayModeProbe`'s own
screenshots show the map's fence line against the new sky, post-processing and all.

## Wall running removed entirely, vault confirmed already gone

Direct request. Wall running (`UpdateWallRun`, `EndWallRun`, its scrape-audio loop, its dust
effects, the camera roll `PlayerController.Look()` added for it) all deleted outright, along
with the `WallRun` audio bank it was the only reader of. Vault was already gone - cut two passes
ago after its own "make vaulting possible by doubling jumping" retry still didn't work - confirmed
by grepping the whole `Scripts` folder for either name and finding nothing left to remove.

## The ledge hop, deliberately simpler than what it replaces

"Add double jumping when you're at a ledge but make sure you can't spam it" - read as a second
jump, gated on genuinely being near a wall, not a scripted mantle. The vault this replaces (twice,
now) tried to calculate an actual landing point from three raycasts and carry the player to it;
neither attempt survived contact with an actual playtest. This is one raycast, forward from
roughly chest height, and an ordinary jump impulse if it hits a near-vertical surface - the
player's own momentum does the actual climbing, the same way a normal jump already does. The
failure mode of over-triggering (hopping when there wasn't much of a ledge there) costs far less
than the old system's failure mode of never triggering at all.

Can't-spam is two separate limits, not one: `ledgeHopUsedThisAirtime` (reset on landing) stops one
jump from chaining several hops, and a 1.2s cooldown starting from the hop itself (not the
landing after it) stops a low ledge being landed on almost immediately from letting a fresh hop
chain right back off it.

Verified with a temporary test rather than trusted on read-through - built a real wall in a real
offline room and called `TryLedgeHop` via reflection, since it's a private method with no reason
to be anything else. First attempt reported a miss even standing directly against the wall;
turned out to be `Physics.SyncTransforms()` again (see `bug-log.md`'s earlier note on
`MapExpansion`'s own detail-scatter pass finding the same thing) - a collider positioned the same
frame it's created doesn't register for a raycast until transforms are explicitly synced, purely
a test-rig issue since real map geometry is never moved at runtime. Passed once that was added:
fires once near a wall, refuses a second attempt in the same airtime, and still refuses after the
airtime flag is reset (simulating a landing) because the cooldown is a separate clock.

# Thirteenth pass — the sky actually pixelated, and a cleanup sweep, 2026-08-23

## The skybox wasn't pixelated at all

Reported directly against the reference again: the previous pass built the right shapes (gradient,
streaked clouds, glowing sun) but rendered them smooth and continuous, missing the reference's
actual retro pixel-art texture entirely. Fixed in `JungleSky.shader` two ways, stacked: the view
direction itself is snapped to a coarse grid before any gradient/cloud/sun maths runs, so whole
patches of sky come out bit-identical instead of shading continuously (adjacent screen pixels that
land in the same cell are now literally the same colour), and the final colour is posterized to a
small number of steps per channel afterward - the blocky shapes alone still read as blurry/low-res
without also cutting the colour precision to match. Verified against both the isolated
`SkyboxPhotographer` render and the real in-game camera with the full `ShaderStack` post-processing
stack on top, not just the bare shader.

## The clouds streaked vertically instead of horizontally

Reported immediately after, from the same render. The streak effect scales one axis of the
sample position up before reading the noise, which makes the noise vary *faster* along that axis -
correct instinct, wrong axis: `dir * float3(_CloudStretch, 1, 1)` sped up the *horizontal* (X)
component, which narrows features running left-right and, as a direct consequence, elongates them
top-to-bottom instead - clouds streaking down the sky rather than across it. Scaling Y instead of
X (`dir * float3(1, _CloudStretch, 1)`) speeds up the vertical axis, narrowing bands vertically and
stretching them horizontally, which is what actually reads as wind-blown cloud cover sweeping
across the dome. Re-rendered both the isolated shader and the real camera to confirm rather than
reasoning about the axis a second time from the maths alone.

## A cleanup sweep

Asked directly: "delete stuff that isn't needed anymore, optimize the project." Dispatched an
Explore agent for the broad "find every dead script/tool/asset" pass (same pattern as the earlier
dead-code passes this project has already been through) while doing a parallel, narrower sweep by
hand for things that pattern doesn't catch - empty folders, accidentally-committed build artifacts,
and assets that are *referenced* but functionally dead rather than orphaned outright.

**From the agent, confirmed and applied:**
- `PlayerMovement.cs`'s `walkSpeed` field (and its now-empty `[Header("Walk")]`) - declared,
  never read anywhere, the exact shape of bug the old `wallRunMinSpeed` field was caught as
  earlier this project.
- `Assets/Resources/Models/Monkey/` - an empty folder, leftover from whenever the model naming
  convention moved to "Gorilla" (the class is still called `MonkeyRig`, but nothing has loaded
  from `Models/Monkey` in a long time - the real model path is `Models/Gorilla/gorilla`).
- Everything else the agent checked (all 55 Scripts, all 35 Editor tools, both audio banks and
  `PlayerController.cs`/`GameAudio.cs` on a full close-read) came back clean - the wall-run/vault
  removal earlier today left nothing orphaned behind it.

**Found separately, by hand:**
- `tools/__pycache__/extract_shot.cpython-313.pyc` was committed to git by accident - a compiled
  Python bytecode cache, regenerated on every run and specific to whatever interpreter last
  touched it. Untracked it and added `__pycache__/`/`*.pyc` to `.gitignore` so it can't happen
  again.
- Two more empty, unreferenced folders: `Assets/Resources/Prefabs/` and `Assets/Items/Guns/`
  (the latter took `Assets/Items/` itself down to empty too once removed) - both just a stray
  `.meta` and nothing else, no asset ever lived in either.
- **`Assets/PostProcessing/PostProcessing Profile.asset`** - not orphaned in the usual sense
  (`Game.unity` and `PlayerController.prefab` both still reference it by GUID), but functionally
  dead: `ShaderStack.cs`'s own doc comment already documents this exact asset as the 2024
  profile that's "never rendered" and gets actively suppressed on every scene load
  (`SuppressAuthoredVolumes`) now that `ShaderStack` builds its own profile from the player's
  settings at runtime instead. Wrote a temporary editor tool (`StripDeadPostProcessVolume.cs`,
  same shape as the existing `StripRoomManagerView.cs`) to remove the `PostProcessVolume`
  component from both the scene and the prefab properly through Unity's own APIs rather than
  hand-editing the YAML, then deleted the now-unreferenced asset and the tool itself. Re-ran the
  full check suite plus a real screenshot afterward to confirm post-processing still looks
  identical - it does, since `ShaderStack` was never actually reading this profile to begin with.
- Corrected two stale doc references while in there: `roadmap.md` pointed at
  `tools/banana_generator.py` (superseded by `tools/banana_variants.py`, per that script's own
  doc comment) and `tools/sound_generator.py` (deleted after its one-time run; the generated
  clips it's still describing live in `Assets/Resources/Audio`, not the script itself).

Left alone on purpose: `Assets/Editor/HitboxProfileSeed.cs` - its target asset already exists so
the tool is a no-op if run again, but it's cheap insurance against that asset ever being deleted
by hand, not clutter. Matches the precedent already set by keeping the other one-time "Builder"
tools around as re-runnable safety nets.

# Fourteenth pass — the ground slam dust and the pineapple's explosion, both too small to notice, 2026-08-23

Reported directly: both read as small and hard to see. Worth recording what rendering them
actually showed, because the two turned out to need different fixes even though the report
grouped them together.

## Ground slam dust - genuinely, simply too small

Built a temporary tool that fires the real networked impact path (`PlayerController.
BuildGroundSlamImpact`, the same call every client makes) at a realistic combat distance (4m,
not standing on top of it) and screenshots the result. The dust puff was completely invisible -
not faint, not hard to spot, actually absent from the frame. At 0.06-0.16m across it is smaller
than the gorilla model's own foot; there was nothing wrong with the reasoning, the numbers were
just never checked against anything human-scale. Raised both layers roughly 6-8x (dust to
0.5-1.2m, spark to 0.25-0.55m) and gave them a longer life so particles that size have room to
actually spread before fading. Re-rendered the same way: now a clearly visible bright burst at
the same distance.

## The pineapple's explosion - already huge, but unreadable

The same test on the explosion told a different story. `Effects()`'s own multipliers, applied to
a 7.5m blast radius, were already producing an 11-31m fireball - genuinely massive in world
units, confirmed by rendering it point-blank (the white core flash filled most of the frame).
The actual problem only showed up a few frames later: by the time the core fades and the fireball
sprite should be the readable shape, the whole thing had collapsed into a soft, shapeless colour
wash with no discernible fireball silhouette - a flash, then an afterglow, nothing in between that
read as "an explosion happened here." Simply making it bigger again would have made this worse,
not better: `FlashSprite` blends additively, so spreading a fixed amount of colour over a larger
area lowers the brightness at every point on it, which is exactly the diluted, washed-out quality
that was the actual complaint.

Two changes, not one: raised the size multipliers further anyway (they were reported as too
small, and a bigger fireball is still more convincing once it's readable), and pushed the
fireball/core colours past 1.0 on every channel - values like `(1.5, 0.85, 0.22)` rather than
`(1, 0.62, 0.18)`. Overbright colour is what lets an additive sprite stay punchy once it's spread
over a large area; capping at 1 is what was reading as dim no matter how big the sprite got.
Re-rendered afterward and could actually see a distinct fireball shape against the world at 0.2s
in, rather than an undifferentiated tint - the middle phase the previous render was missing
entirely.

Both verified with real renders at each step rather than trusted from the radius maths alone -
the pineapple specifically would have been fixed wrong (bigger, again) if the second render
hadn't shown the actual problem was colour intensity, not size.

# Fifteenth pass — a full cleanup and bug sweep, 2026-08-23

Asked directly to confirm the earlier cleanup sweep actually covered the whole project, plus a
bug sweep alongside it. Checked every one of the 53 `Assets/Scripts` files and the ~35
`Assets/Editor` tools against scene/prefab GUIDs, `AddComponent`/`GetComponent` call sites, and
`Resources.Load`/`[MenuItem]` string references - not just a grep for the class name, which this
project's own heavy use of runtime `AddComponent` and static factories has already proven
produces false positives more than once this session.

## The cleanup itself: nothing left to remove

All 53 scripts are live - every one resolves to a scene/prefab GUID, a runtime `AddComponent<T>`,
a static factory (`Projectile.Launch`, `TrainingDummy.Build`), or a base-class/field-type
reference. All ~35 editor tools are either the permanent check suite or genuinely re-runnable
maintenance tools written idempotent on purpose (the established convention in this codebase -
see `StripRoomManagerView.cs`/`HitboxProfileSeed.cs`, both "already applied, but cheap insurance
if the fix is ever undone by hand"). `Assets/Resources/Audio` has no orphaned `WallRun` folder or
any other bank without a matching `GameAudio.cs` constant, and no empty directories anywhere
under `Assets/Resources` - the wall-run/vault removal earlier this session was already clean.

One genuinely dead field found and removed: **`PlayerController.mouseSensitivity`** - serialized
on the player prefab, never read anywhere in code. `Look()` uses `GameSettings.Sensitivity`
instead, a separate persisted setting that replaced it at some point without this leftover ever
being cleaned up.

## The bug sweep: one real, currently-masked bug

**`PlayerController.ShieldBreak()`** called `GameAudio.PlayPitched(GameAudio.Impact, null,
GameAudio.ShieldVolume, 1.9f)` as its fallback when the `Shield` audio bank is empty.
`PlayPitched(bank, clipName, ...)` always wants a *specific* clip name - it calls the two-argument
`Pick(bank, clipName)` overload, which does `Resources.Load<AudioClip>($"Audio/{bank}/{clipName}")`.
With `clipName` null, C# string interpolation turns that into `"Audio/Impact/"` - a path with
nothing after the trailing slash, which can never resolve to a real asset. The fallback this
method's own doc comment promises ("falling back to something breakage-shaped if no clip has been
dropped in yet") could never actually fire.

Why nobody noticed: `Assets/Resources/Audio/Shield/` has real clips today, so the primary branch
always wins and the broken fallback path never runs. It was a landmine sitting under a folder
that happens to be full right now - the moment `Shield` is ever emptied or renamed, shield breaks
would go silent (with a misleading `No clip Audio/Impact/` warning in the log) instead of falling
back the way the code claims to.

Fixed by replacing the whole two-branch method (which also hand-checked `Resources.LoadAll`
*uncached*, re-scanning the folder on every single shield break) with one call to
`GameAudio.PlayShaped(GameAudio.Shield, GameAudio.ShieldVolume, 1f, GameAudio.Impact, 1.9f)` -
`PlayShaped` is already the established helper for exactly this "bank, with a fallback bank and
an explicit pitch" shape, used the same way for the air brake and the ledge hop's own fallback
sounds, and it caches the same way `Pick()` does.

Also checked and found clean: `PhotonServerSettings.asset`'s `RpcList` has all seven current
`[PunRPC]` methods present (confirms `RPC_GroundSlamImpact` registered correctly on its own,
matching the ninth pass's expectation). Two stale entries remain in the list from before this
session (`ClickRpc`, `DestroyRpc` - PUN demo leftovers, already noted as harmless in the second
pass: an unmatched list entry costs nothing, only a *missing* one would). Left alone rather than
hand-edited - not worth the risk to a Photon-internal asset for a purely cosmetic two-line cleanup.

# Sixteenth pass — guns made louder and punchier, the HUD reworked to actually match the design philosophy, 2026-08-29

## Guns not punchy enough, the split and big mike specifically

Direct playtesting feedback. Traced what "punchy" actually has code behind it already:
`Juice.Shake` and `PlayerController.AddFirePunch` both already scale off `GunInfo.Weight` (pull
damage over 110, clamped), so a heavier gun already kicks the camera and the view FOV harder on
fire. Two things didn't get the same treatment and were flat across every weapon regardless of
what fired:

**Volume.** `RPC_WeaponFired` and `RPC_ProjectileFired` both played every shot's base layer and
its weight-gated second/third layers at the same flat `GameAudio.ShotVolume`, no matter which gun
fired. The split and big mike (`Weight` 0.98 and 0.86 - both already crossing `layeredAbove` and
the 0.8 third-layer threshold, i.e. already playing all three audio layers) still came out of the
speaker at the identical volume as a pistol tap (`Weight` 0.3). Fixed by scaling `ShotVolume` by
`Mathf.Lerp(0.9f, 1.55f, weight)` before any of the three layers play, in both RPCs - light guns
move a few percent, the two heaviest guns in the game land near the top of the range.

**Muzzle flash.** `MuzzleFlash` built every one of its fields (`flashSize`, `burstCount`,
`intensity`, `range`) from the same serialized defaults regardless of the weapon carrying it -
confirmed by reading `SingleShotGun.Awake`, which called `AddComponent<MuzzleFlash>()` and never
touched a single field on it afterward. The pistol's tap and the shotgun's blast lit an
identically sized burst. Added `MuzzleFlash.Scale(weight)`, called right after the flash is
built, using the same weight-lerp shape as the volume fix. `range` needed an explicit
`flash.range = range` inside `Scale` - it's only ever read into the `Light` once, in `Build()`,
which has already run by the time a weapon knows its own weight.

What this doesn't fix: `roadmap.md`'s M2 already flags the source recordings themselves as thin
`.22` clips that read as a click rather than a bang. A mix-level fix (louder, bigger flash) makes
the big guns bigger *relative to* the small ones - it can't make the sample itself sound heavier.
That's unchanged and still needs real source material.

Verified with the existing `WeaponCheck` suite (all pass, including the shape/audio-path
assertions unaffected by the scaling) rather than by ear, since neither change is something a
batch-mode run can judge for loudness - the numbers are the fix, and they're derived from the
same `Weight` figure the rest of the game's feel already trusts.

## The HUD, reworked against the project's own stated direction

Direct request, with a specific complaint: the gameplay HUD didn't read as ULTRAKILL/Cruelty
Squad, and to find a font that does. Scope explicitly excluded the main menu, lobby and settings
screens - Ryaan's own, per M5's existing notes.

**The font wasn't actually the problem.** `HudBuilder.FindFont()` already picks Helvetica Punk
out of the project's four fonts, and its own doc comment already explains why (the only one a
number is legible in at a glance). Rather than trust that reasoning secondhand, rendered a
specimen crop of all four at HUD-relevant sizes (`ImageFont`/`PIL`, not Unity - cheaper than a
play mode round trip for a pure font-shape question). Confirms it: Helvetica Punk is a genuinely
industrial/stencil face, distressed at the edges, and it's the only one of the four in contention
- Chomsky is blackletter (and already spoken for, menu-side), The Wildeast is a western slab, and
Bring Me A Helicopter is a horror display face nobody could read mid-fight. The mismatch was
never the typeface.

**What actually needed changing was the HUD's own visual language**, checked directly against
`roadmap.md`'s design philosophy section (loud/clashing, not tasteful; type as a weapon;
everything punchy) rather than redesigned from taste alone:

- `GameHud`'s colour palette pushed off a soft traffic-light green/yellow/red toward something
  less pastel. `killColour` is the one real hue change, orange to hot magenta - the accent both
  reference games reach for on anything about hurting someone else rather than your own status.
  It touches the kill callout, the streak, feed lines you were part of, and the ladder's filled
  pips, so the shift reads consistently rather than in one spot.
- Health and ammo - the two readouts that are always on screen - now sit on a solid black plate
  (0.78 alpha, not the soft 0.55 the health bar's track alone used to carry) instead of floating
  directly over the game world.
- A viewfinder frame - four short right-angle ticks per corner, not a full outline - clamps round
  each plate: acid green for health, magenta for ammo. This is the one move a colour or font
  change alone can't make; both reference HUDs corner their numbers rather than just printing
  them. New `HudBuilder.CornerFrame`/`CornerTick` helpers, generic over which anchor a panel uses
  so the same call frames a bottom-left cluster and a bottom-right one without either mirroring
  coordinates by hand.
- A faint scanline overlay - one dark row in every two, alpha 0.05, point filtered rather than
  blurred - sits over the whole canvas as the last sibling. Built the same way
  `GameHud.BuildScopeMask` already builds its texture: real alternating pixels, not a shader,
  because a soft scanline reads as compression noise and a hard one reads as a screen. Confirmed
  visible on a zoomed crop of open sky, at an alpha deliberately low enough that it's easy to miss
  at normal viewing size and easy to see once you look for it.

**The ammo frame was wrong on the first render, and the bug is exactly the kind this project's
UI maths keeps producing.** `Image(anchor, anchor, pivot, position, size, ...)` for a
`BottomRight`-pivoted element places `position` at the rect's own *right* edge and grows the box
*leftward* by `size` - the mirror image of health's `BottomLeft` maths (`position` at the *left*
edge, grows rightward), not the same formula with the sign left unflipped. First pass used the
`BottomLeft` formula for both panels. It compiled, `SceneCheck` passed (it checks wiring, not
geometry), and the result was a black plate and a magenta frame sitting a full panel-width to the
left of the actual "30"/"5" text - visually disconnected from the numbers they were meant to
frame. Caught by an actual screenshot, not by re-reading the maths harder: this project's memory
already carries a standing rule that a pose or a visual claim gets rendered before it's called
fixed, for exactly this reason (see the tenth pass's grip bug, the fourteenth pass's explosion
size, and now this).

Building that screenshot needed its own new tool. `PlayModeProbe` already documents why a
straightforward camera render can't do it: the HUD is a Screen Space - Overlay canvas, which
`Camera.Render` never draws, and `ScreenCapture` - the API that *can* see it - needs a real
end-of-frame that batch mode never delivers, hanging the process instead of producing a file.
New `Assets/Editor/HudPhotographer.cs` (kept as a permanent tool, same call as
`TwoHandedPhotographer`) runs the same offline-room boot `PlayModeProbe` uses to get a real local
player without a network, seeds the HUD with representative sample state (a hit, a kill callout,
two feed lines, both damage-number variants, a damage bearing), and calls `ScreenCapture` after a
real `WaitForEndOfFrame` - which only works because the whole run is launched *without*
`-batchmode`, a real if invisible Editor window rather than a headless one. Fixed the ammo frame's
bounds from what that first screenshot actually showed, rebuilt, rendered again, and confirmed the
plate and both frames now sit exactly under "140" and "12"/"6" respectively.

Full `PlayModeProbe` and `SceneCheck` both still pass - neither the weight-scaled audio/flash
change nor the HUD rework touched anything either suite actually asserts on.

# Seventeenth pass — the HUD, actually landed this time, 2026-08-29

Direct continuation of the sixteenth pass's HUD rework, same day - that pass's actual result was
rejected hard and repeatedly. Recorded in full because the sequence of wrong turns is exactly the
kind of thing worth not repeating.

## What went wrong, in order

**First reaction:** "font is pretty bad and I still dont like these visuals... I HATE the font."
The sixteenth pass's plates-and-brackets treatment read as a tactical shooter's HUD (Counter-
Strike, Valorant - "youre leaning too much into the CS2 vibe"), not ULTRAKILL or Cruelty Squad.
Sent a pile of real reference screenshots across a dozen different games rather than more
description, which is the actual useful signal in this whole pass - ULTRAKILL's own HUD is thin
glowing outlines and clean type over the scene, nothing like a solid black bracketed box.

**Second attempt:** dropped the plates and brackets for a thin neon-glow-outline treatment,
shown as an HTML/CSS mockup rather than built in the actual project. Reaction: liked the
direction (away from tactical-HUD), but "I DO NOT LIKE THE GLOW LINE," the text read as generic,
and the layout was wrong too - three separate complaints landing on one attempt.

**Third attempt**, after a two-question `AskUserQuestion` to narrow "pixelated but not too NES"
and "how much chrome": swapped the glow for a posterised halo (hard colour rings instead of a
soft blur, the skybox's own quantise-not-blur move at a much smaller scale), still as an HTML
mockup. Reaction: **"what the fuck is this, dont make html demos man youre really bad at doing
this... REALLY think this time."** The medium itself was the problem, not just that attempt - a
CSS approximation doesn't render with TMP's actual SDF font pipeline, doesn't show the game's own
toon-outline shader sitting next to it, and asking for reactions to a fake kept every round
one layer removed from what would actually ship. Should have gone straight to Unity and a real
screenshot the first time a font was on the table.

## What actually shipped

**A real font, sourced and built for real.** Tried to fetch `m6x11` (Daniel Linssen, the font
Celeste uses) from itch.io first - its download is gated behind async JS calling a session-scoped
API `managore.itch.io/m6x11` never exposes in static HTML, and neither a direct `fetch` from the
page's own context nor a guessed endpoint (`/file/<upload_id>?source=game_download`, tried on
both the subdomain and `itch.io` proper) returned anything but a 404 or a CORS failure. Abandoned
rather than keep guessing at a private API. **Jersey 10** instead - Google Fonts, OFL licensed,
its raw TTF sitting at a stable, directly-curlable GitHub path
(`google/fonts/main/ofl/jersey10/Jersey10-Regular.ttf`) - a condensed display face built on a
pixel grid without being an 8x8 arcade font, which is what "pixelated but not too NES" actually
asks for. Downloaded straight into `Assets/Fonts/Jersey10/` alongside its `OFL.txt`, built through
the existing `FontAssetBuilder`, no new tooling needed.

**`HudBuilder.FindFont()`** now looks for `Jersey10` instead of `Helvetica Punk` - in-match HUD
only, menus untouched per Ryaan's own instruction not to touch those yet even though he's said
directly he wants Helvetica Punk gone from the project eventually.

**The plates, brackets and scanline overlay from the sixteenth pass, all removed outright**
rather than retuned - `Image(..., "Backer", ...)` and `CornerFrame(...)` deleted from both the
health and ammo blocks, the `Scanlines` image and its `BuildScanlineTexture` generator deleted
entirely, `CornerFrame`/`CornerTick` deleted as now-dead code. Replaced with a single move applied
to every label the HUD builds: `HudBuilder.Text()` now sets a hard SDF outline
(`ShaderUtilities.ID_OutlineWidth`/`ID_OutlineColor`) on every `TMP_Text` it creates, by default,
in one place - the same thing `ScreenOutline` already does to every 3D object in view, so the HUD
is drawn in the game's own ink instead of a convention borrowed from another game's UI.

**Two more fixes that only showed up once actually rendered**, both from mid-turn messages
reacting to the real screenshot as it happened:

- *"dont use colors that clash with the map colors... try using borders on text, dont make it
  raw."* The first real render (0.2 outline width) showed exactly that: `140` in the health
  colour sat nearly unreadable against grass, the outline present but too thin to read as a
  border rather than a hairline. Raised to 0.38 - a proper cartoon sticker edge, not a line.
- *"i do not like the healthbar at all, it looks REALLY flat even now, there is literally ZERO
  depth to it."* True independent of the outline fix - `Fill` was and had always been a single
  flat `Image` with a solid colour, going back to the original M5 build, and nothing in any pass
  this session had ever touched it. Added a solid black frame round the whole track (a
  background-behind-foreground border, not `UnityEngine.UI.Outline` - that component only offsets
  a copy in one direction, which is right for the crosshair ticks it was built for and wrong for
  framing all four sides of a bar) and a highlight strip stretched across the top half of `Fill`
  itself, anchored so it tracks whatever width `GameHud.UpdateHealth` resizes the bar to at
  runtime without any code on the `GameHud` side needing to know it exists.

Verified with a real offline-match screenshot after every change, using `HudPhotographer.cs`
(built during the sixteenth pass, reused here) - not the CSS mockups that were the whole reason
this pass took three extra rounds. `SceneCheck` and the full `PlayModeProbe` suite both still
pass; neither exercises the visual treatment directly, but both confirm nothing about the HUD's
actual wiring or runtime behaviour broke underneath it.
