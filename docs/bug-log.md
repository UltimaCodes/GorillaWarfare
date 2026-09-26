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

# Eighteenth pass — a bananameter, a lean, and a real layout bug found by rendering it, 2026-08-29

Same-day continuation of the seventeenth pass. That pass's real-Unity rebuild was shown to actual
playtesters (not just Ryaan), who said the HUD had personality before and reads as generic slop
now - a fair hit. Removing the tactical-shooter plates and brackets fixed what was wrong, but
nothing in that pass added anything back that was specific to *this* game, and a hard outline on
a common Google Font is exactly the kind of "safe" treatment that ends up looking like every other
cartoon-outlined indie shooter.

## The bananameter

Direct instruction: "make your UI related to the game itself, make the healthbar a bananameter."
The game already has exactly the right visual language sitting unused for this -
`GunInfo.RipenessFor(ammo)`, the function that tints a held banana green-to-yellow-to-brown as its
magazine empties (`SingleShotGun.ApplyRipeness`). `GameHud`'s `healthy`/`hurt`/`critical` fields
now walk that same ramp instead of the arcade-shooter green/amber/red they'd had since M5:
`healthy` (0.6, 0.92, 0.14) an unripe green, `hurt` (1, 0.82, 0.1) ripe yellow, `critical`
(0.85, 0.4, 0.08) a warm burnt orange rather than true overripe brown - a rotting-banana brown
would sit too close to the grass and the wood fence to read as a warning at exactly the moment it
most needs to. The existing three-step fraction logic (`>0.6`/`>0.3`/else) didn't need to change,
only what colours it reaches for.

Ammo got the real version of the same idea, not a copy: `UpdateAmmo`'s colour used to be its own
invented rule (`Reloading ? hurt : Ammo == 0 ? critical : white`), unrelated to anything else in
the game. It's now `info.ripens ? info.RipenessFor(gun.Ammo) : Color.white` - the *exact* function
the weapon model itself calls. The number, the new ammo bar and the banana in your hands all brown
together as the magazine empties, because they're reading the same one function rather than three
separate colour rules that happened to sound similar. Weapons that don't ripen (the pineapple - a
real texture, not a tintable skin, same reason `ApplyRipeness` itself skips it) fall back to plain
white, matching the model.

## The lean

Direct instruction, pointing at ULTRAKILL specifically: its HUD elements sit on a diagonal, not
flat horizontal boxes. Every bar (health, ammo) now carries a 6 degree `localRotation`.

The restructuring this needed is the more interesting part. The frame and the track used to be
*siblings* - both direct children of the Health/Ammo panel, only lining up because their
positions happened to agree. Rotating siblings independently rotates each around its *own* pivot;
with the border's pivot offset from the track's by the border width, the two would have swung
apart from each other instead of leaning together. Fixed by making `Track` (and by extension
`Fill`, `Shine`, `Shadow`, `Shield`, all nested under it already) a *child* of the frame instead of
a sibling - one `localRotation` on the frame now carries the entire cluster as a single rigid
body. `Track`'s own position moved from `Vector2.zero` to `(trackBorder, trackBorder)`, an inset
from the frame's origin rather than from Health's - the same shape of fix the ammo frame needed in
the sixteenth pass, just one level deeper in the hierarchy this time.

## The big callout finally punches in

Also asked for directly - "the text could get some more effects for some oomph (the big text)."
`centreTitle` (the kill callout, "GET READY," a gun-game rung-up) turned out to be the one piece
of text on the entire HUD that had no reaction to appearing at all - every other dynamic element
(the hitmarker, damage numbers, the health/ammo groups, the slide rank) already arrives big and
settles; this one just snapped straight to scale 1 and sat there while `killFlash` faded its
alpha. Added a matching `titlePunchUntil` timer (set alongside `killFlash` in both `ShowKill` and
`ShowRungUp`) driving the same "arrives big, settles" scale curve everything else already uses,
plus a beat of white blended into the colour at the start of the punch - the same "arrives hot,
cools into place" read the hitmarker's own pop already has.

## A real layout bug, found only by rendering a state nobody had checked

The instruction to "find good positions for the different kinds of text" led to extending
`HudPhotographer` with a `GW_HUD_MODE=GunGame` mode (forcing the ladder visible and, via
reflection on `PlayerMovement`'s private `chain`/`chainExpires` fields, a live slide chain) rather
than only ever screenshotting the default Deathmatch warmup state every previous render this
session had used. First render of that state showed `BANANAS!!!` (the slide rank) sitting
directly on top of "CLIMB THE LADDER" and the weapon subtitle - not a spacing issue, an actual
positioning bug that had been there since before this whole HUD rework started and was simply
never rendered in a state that would show it.

Root cause: `slideCombo` has always been built as a child of the `Centre` panel, and `Centre` is
built with `Vector2.zero` size - it only ever needed to anchor its centred children (the title,
the subtitle, the hit combo) at its own origin, which a zero-size RectTransform does just fine.
But a zero-*width* parent breaks anchor fractions for anything that isn't centred: `slideCombo`'s
own `anchorMin`/`anchorMax` of `(1, 0.5)` - "hug the right edge" - has nothing to interpolate
across, so it collapses to the exact same point a centred anchor would. The text was never
actually anchored to any edge; it only ever sat 70 points left of dead centre, which happened to
clear the shorter multikill callouts ("DOUBLE", "TRIPLE") this whole session's testing had used
but not "CLIMB THE LADDER" at 130pt in Jersey 10's wider glyphs. Fixed by moving `slideRank` (and
its `Repair()`/`Retune()` counterparts) onto the root canvas directly, where a real anchor means
what it says, rather than trying to widen `Centre` and risk moving every other child that already
depends on it being zero-sized.

Full `PlayModeProbe` and `SceneCheck` both still pass after the restructure - neither exercises
bar rotation or colour directly, but both confirm the reparenting didn't break any of the HUD's
actual wiring.

# Nineteenth pass — a real pixel-art banana, an ULTRAKILL-style rank meter, four corrections in one go, 2026-08-29

Same-day continuation again. Four separate, specific corrections to the eighteenth pass, all
landed together.

## The ammo bar is gone

"Remove the ammo number bar that looks very weird." The magazine bar added in the seventeenth
pass, mirroring health's own, is deleted outright - `BarFrame`/`Track`/`Fill`/`Shine`/`Shadow`
and the `ammoTrack`/`ammoFill` fields all removed from both `HudBuilder` and `GameHud`. Ammo goes
back to a bare number, `weaponName`/`ammoNumber`/`spareNumber` back at their pre-bar positions.
Not every readout on a HUD needs a bar under it, and this one apparently didn't.

## The health bar is an actual banana now

Direct correction, in almost these words: "what you did is not what I meant by bananameter."
Recolouring a rectangle with ripeness colours (the seventeenth pass) was not it - the ask was
for the shape itself, "find a pixel art banana that fits our vibe."

Rather than source or hand-paint one, `HudBuilder.BananaSprite()` draws it procedurally - the
same house style every other texture on this HUD already uses (`GameHud.BuildScopeMask`, the
skybox's own pixelation): real maths, quantised, not an asset pulled from anywhere. A 52x18 texel
silhouette (tuned up from an initial 64x14 pass that rendered as a thin blade rather than a
banana - 7:1 length to thickness read as a sliver, 52x18/maxThickness 13 is closer to 4:1 and
unmistakably a banana once rendered), curved by a single sine arc along its spine, tapered to
points at both ends by a matching sine on thickness, with a 1-texel black outline baked in
wherever a filled texel touches an empty or off-texture neighbour, and a top-lit/bottom-dark
greyscale shade baked in per-column so tinting the sprite with `Image.color` recolours the whole
banana while keeping its own light curve.

Three layers of the one sprite stack on top of each other in a `BananaMeter` container:
- `Husk` - `Image.Type.Simple`, always the full shape, dim brown-green, the "empty tube" backing
  every bar needs so a low reading still shows the whole banana's outline.
- `Trail` - `Image.Type.Filled`, a pale near-white ghost. This is the inertia asked for directly:
  eases toward the real fraction on a drop (`Mathf.MoveTowards` at a fixed rate in
  `GameHud.UpdateHealth`) rather than snapping with it, so a big hit reads as a bite taken out of
  the banana that visibly closes up rather than the bar just being smaller a frame later. Snaps
  immediately on a heal - nothing about recovering health wants a lag.
- `Fill` - `Image.Type.Filled`, the live reading, tinted through the same `healthy`/`hurt`/
  `critical` ripeness ramp the sixteenth pass already set up. This is the one part of the
  seventeenth pass's health work that survives unchanged - the colours were never the complaint,
  only the shape they were painted onto.

`Image.Type.Filled` clips the rendered quad, not the sprite's own pixels, so a transparent-
background sprite like this one clips cleanly with no extra masking work - and critically, it
clips the *Image it's set on*, not that Image's children, which is exactly why the old
rectangular bar's separate Shine/Shadow overlay children couldn't carry over here: an overlay
child sitting on top of a `Filled` parent isn't clipped by the parent's own `fillAmount` and
would spill past wherever the fill happens to cut off. Baking the shading into the texture itself
sidesteps that entirely.

**The lean flipped direction.** "Having the bar rotated inwards instead of outwards" - the
seventeenth pass's rectangular bar rotated its far end up and away from the screen corner it sat
in; this one rotates the opposite way, tucking toward the corner instead of lifting away from it. `barLean` went from +6 to -8 degrees. There's no separate border frame to carry as a
rigid rotating body any more either - the outline lives in the sprite now, so the `BananaMeter`
container holding the three layers rotates directly.

## The slide rank got its meter

"Make the BANANAS!!! thingy work like the ULTRAKILL or DMC style meter." Those readouts (the
style bar under ULTRAKILL's "CHAOTIC," the stale-combo multiplier in the DMC reference) all pair
a name with a bar underneath it - the rank text alone, four words swapping in and out, was never
that.

Needed a new number from `PlayerMovement` to drive it: `ChainWindowFraction`, added alongside the
existing `SlideChain`/`Exhausted`/`ExhaustedFor` - `Mathf.Clamp01((chainExpires - Time.time) /
chainWindow)` while a chain is live, zero otherwise. `chainExpires` and `chainWindow` were already
private fields driving `SlideChain`'s own boolean logic; this just exposes the same window as a
fraction instead of a yes/no. The new `MeterTrack`/`Fill` bar is a *child of `slideRank`'s own
GameObject* rather than a sibling - unlike `Centre`, that box has real width and height (620x80),
so a corner anchor on a child of it actually means something (see the eighteenth pass for what
happens when it doesn't), and it shows/hides for free with the text's own `SetActive` rather than
needing a second reference threaded through just to keep two things in sync.

Full while a slide just landed, draining to empty by the time the chain would expire, snapped to
zero outright while `Exhausted` (nothing left to count down, only that it's spent) - the rank name
says how deep the chain is, the bar now says how long there is left to go deeper.

## The weapon name got more presence

"Make the gun name text a bit more depth and visible." `HudBuilder.Text()` takes an optional
`outlineWidth` parameter now, defaulting to the HUD's shared 0.38, with the weapon name specifically
built at 0.5 - alpha was already close to opaque (0.75, now a flat 1), so the outline was the
actual lever for "more depth."

## A compiler error worth noting

Both new pieces (the banana layers, the slide meter's fill) hit `CS0119: 'HudBuilder.Image(...)'
is a method, which is not valid in the given context` on their first pass - this class has its own
`static Image Image(...)` helper, and a class's own method names shadow types of the same simple
name for static-member access (`Image.Type.Filled`, `Image.FillMethod.Horizontal`) even though a
local variable declaration (`Image image = Image(...)`) resolves fine, since declaration and
invocation contexts disambiguate differently than static-member access does. Fixed by fully
qualifying `UnityEngine.UI.Image.Type`/`FillMethod`/`OriginHorizontal` at every call site that
needed them, rather than renaming the long-established local helper.

All four fixes verified by real `HudPhotographer` screenshots (both the default Deathmatch state
and `GW_HUD_MODE=GunGame`, to actually see the new slide meter) before being called done, not
eyeballed against the code. `WeaponCheck`, `SceneCheck` and the full `PlayModeProbe` suite all
still pass - `PlayModeProbe` in particular exercises `PlayerMovement` directly, so the new
`ChainWindowFraction` property compiling and returning sane numbers is more than just a hopeful
read of the source.

# Twentieth pass — a sourced banana, a font that actually sticks out, and text that stops blending in, 2026-08-29

Same-day continuation. Direct, sharp correction to the nineteenth pass's procedural banana:
"WHY ARE YOU SO RELUCTANT ON USING EXTERNAL ASSETS THAT FIT BETTER... YOUR JOB IS TO CODE NOT TO
BE THE ART DIRECTOR." Fair - the procedural sine-curve banana was a worse banana than a real one
a real artist drew, and reaching for "draw it with maths" as the default instead of actually going
and finding a sourced asset first is exactly the reluctance called out.

## A real banana, sourced this time

Searched properly rather than guessing at a URL: `opengameart.org`'s **Spinning Banana** by
lawrence_laz, CC0, a 20-frame rotation sheet (`spinning_banana.png`, 500x25, 25x25 per frame) -
direct static file, no itch.io-style JS-gated download flow to fight this time. Downloaded,
inspected all 20 frames for bounding box, picked frame 9 (a clean side-on crescent, already close
to the pose a horizontal bar wants) and trimmed it to its opaque bounds (19x17). Credited in
`Assets/Textures/UI/BananaHealth-CREDIT.txt` even though CC0 needs no attribution, matching this
project's existing convention of a `LICENSE.txt` beside every sourced font.

`Assets/Editor/BananaAssetSetup.cs` (new, permanent, idempotent like this project's other
one-shot asset tools) sets the actual import settings a fresh PNG doesn't get by default - Sprite
type, point filtering (no blur when a 19px-wide image gets stretched to HUD scale), uncompressed
(keeps the flat colour edges crisp rather than block-compressed into mush). `HudBuilder.BananaSprite`
now just loads it (`AssetDatabase.LoadAssetAtPath`) - the entire sine-curve generator, its edge-
detection outline pass and its baked greyscale shading are deleted outright, not kept as a fallback.

**The tint scheme needed to change along with it.** The procedural sprite was drawn in greyscale
specifically so multiplying it by the ripeness palette (green/yellow/brown) would recolour it
cleanly. A real banana sprite is already yellow - multiplying yellow by green reads as khaki, not
"healthy." `GameHud.UpdateHealth` now computes a dedicated tint for the sprite layer, separate
from the number's own `healthy`/`hurt`/`critical` fields (which are untouched and still drive the
text): white at good health (shows the banana's own true colour, which looks right rather than
tinted), warming toward amber then red as health drops - white multiplied by anything is that
colour unchanged, so this is the one mapping that actually works against a real, already-coloured
sprite rather than a neutral grey shape built to be recoloured.

## The rank meter gets its own face

"THE ULTRAKILL STYLE METER IS SO BAD... OBVIOUSLY YOU'D HAVE TO CHANGE THE FONT AND STYLE OF IT
FOR IT TO STICK OUT." Fair again - it was built in Jersey10, the same face as every numeric
readout on the HUD, which is exactly why a supposedly special "hype" moment read as just more of
the same text. Sourced **Anton** (Google Fonts, OFL, same reliable GitHub-mirror curl this
session already used for Jersey 10) - a poster-weight impact face, about as far from a pixel-grid
readout font as this project now has. `HudBuilder.FindRankFont()` sits alongside the existing
`FindFont()`, both now routed through one `FindFontNamed(string)` helper rather than two near-
identical copies of the same search loop. Used for the slide rank ("BANANAS!!!") and the centre
kill callout ("DOUBLE," "GET READY") - the two moments on the HUD that are supposed to shout,
now visibly a different kind of text from the numbers around them rather than the same font in a
different colour.

The meter bar itself also got the same treatment health's already had: a black frame round it
(background-behind-foreground border, the same technique used everywhere else on this HUD that
wants one), since a bare flat-coloured rectangle was part of what read as "bad" alongside the font.

## Text was still blending in

"USE BORDERS OR SOMETHING ON THE TEXT ON THE SCREEN THIS SHIT GENUINELY BLENDS IN WITH EVERYTHING
ELSE" - direct, and true even with every label already carrying a hard SDF outline
(`OutlineWidth`, raised twice already this session, 0.2 to 0.38 to now 0.55). An outline alone
traces a letterform's own edge; it does nothing to separate the glyph as a whole from a busy,
similarly-toned background behind it; a bright number against a bright wall can still lose the
fight even with a dark ring round every stroke. Added a soft dark underlay
(`ShaderUtilities.Keyword_Underlay` plus its `ID_Underlay*` properties - confirmed the exact
property names against the installed TMP package source rather than guessing, since a wrong
property name silently does nothing) to every label alongside the outline, not instead of it: a
blurred dark shape sitting behind the whole glyph, offset slightly down, which is what actually
reads as "this text is sitting in front of the world" rather than "this text is drawn on the
world."

Verified with real `HudPhotographer` screenshots (`GW_HUD_MODE=GunGame`, to see the rank meter and
the banana together in one frame) - the banana's actual pixel art, the underlay's visible shadow
behind "140," and the meter's new border all confirmed by looking at the render, not by reading
the code back. `SceneCheck` and the full `PlayModeProbe` suite both still pass.

# Twenty-first pass — a giant backlog in one sitting: the player prefab, reload, the style score, and the health bar rebuilt again, 2026-08-29

One long session working through an eighteen-item list in order, closing with an explicit
instruction not to leave any of it for later. Grouped here by system rather than in the order they
landed, since several were independent.

## The gorilla model and hitboxes existed only at runtime

"Put the gorilla player 3d model and the hitboxes on the prefab so I can change them to my
liking." Both `MonkeyRig.Build` and `Hitbox.BuildFor` instantiated/generated everything fresh on
every spawn - nothing about the model or a single collider ever existed on the prefab asset, so
there was nothing in it to select, move or resize between matches.

Rather than rearchitect either system, made both idempotent and added a bake step on top:
`MonkeyRig.Build` now checks for a child literally named `"Model"` before instantiating anything,
and reuses it if present; `PlayerController.Start` checks for existing `Hitbox` children before
calling `Hitbox.BuildFor` and re-binds them to the live instance instead of building a second,
overlapping set (which would have doubled every hit). `Tools/Gorilla Warfare/Bake the player rig`
(`PlayerRigBaker.cs`, new, re-runnable) opens the prefab, runs the exact same `Build`/`BuildFor`
calls once against it, and saves the result - real, hand-editable content in the prefab, with the
runtime path now just reusing whatever's there instead of building its own copy on top. Verified
with the full `PlayModeProbe` suite (spawn, respawn, hitbox coverage) rather than assumed safe from
reading the diff - a subtle miss here would have meant either a duplicated model or damage being
silently double-counted, neither of which a compiler catches.

## The reload animation and its sound

"Nothing you have ever added for reload has worked" territory, again - the previous pass's dip
(down, hold, back up, all one shape) was reported as unsatisfying at any speed tried, with an
actual concept offered this time: eat the old one, pull a fresh one out of nowhere. Replaced the
symmetric ease with three distinct beats - fast hard drop, a held moment fully retracted, then a
sharp pull-back that overshoots past rest (`EaseOutBack`) before landing exactly on it - see
`roadmap.md`'s "Later polish" for the full account. Reload sound moved off the single generic clip
onto a per-weapon lookup (`Resources/Audio/Reload/<WeaponName>`, same parent-folder fallback
`Shoot/<WeaponName>` already established) pitched by the weapon's own `Weight` in the meantime,
since no fruit-specific audio has actually been sourced yet - flagged directly rather than quietly
passed off as solved, per this project's own standing rule about not inventing assets.

## The style score

The biggest single piece: a full rework of the slide-only combo meter into a unified scoring
system, and the new decider for who wins a deathmatch. Design, the "why" behind each number, and
what's still unverified all live in `roadmap.md`'s new "Style score and the combo/scoring rework"
section rather than duplicated here. Worth recording the one real design problem solved along the
way: classifying a kill as a no-scope or a point-blank needs the *shooter's* aim state and the hit
distance at the *moment of the shot*, but a kill is only confirmed several frames later once the
death RPC round-trips back - by then that context is gone. Solved by having `SingleShotGun`
stash it (`StyleScore.RecordShot`, keyed by target actor number) at the moment damage is dealt, and
having the kill-confirmation RPC (`PlayerController.RPC_Died`, which every client already receives,
including the killer's own) look it up rather than try to reconstruct it after the fact.

## The health bar, rebuilt a third time

"Style it like the Cruelty Squad health bar" - top left, vertical, depletes downward, the peel and
the banana as separate layers, a backshadow/outline for comparing current health against full, and
a diagonal animated overshield bar beside it. Full account, including the deliberate choice not to
source a second sprite for the peel, in `roadmap.md`'s HUD section (item 7). The one implementation
trap worth naming: the overshield bar used to be a horizontal extension of the main bar's own
track, resized by `RectTransform.sizeDelta` directly. Once the main bar became a fixed-size
vertical `Image.Type.Filled` gauge (matching how the main health fill already worked, rather than
inventing a second mechanism), the shield had to move to the same `fillAmount` approach against its
own ceiling (`OvershieldCeiling - MaxHealth`) - reusing the old resize code against a bar that no
longer resizes would have silently done nothing.

All four verified together: a batch-mode compile check, `HudBuilder.Repair` reporting exactly the
pieces it expected to add, the full `PlayModeProbe` suite passing (spawn, hitboxes, a real kill
exercising `StyleScore.RegisterKill`, death and respawn), and a real `HudPhotographer` screenshot of
the rebuilt HUD - not eyeballed from the diff alone, per this project's own standing rule about
declaring a visual change done without rendering it first.

# Twenty-second pass — a real screenshot, a real sweep, 2026-08-29

Same-day continuation. A real screenshot of the twenty-first pass's work surfaced three separate
bugs the automated checks above never could - none of them are things `PlayModeProbe` or a compile
pass can see. Followed by a direct request for a full bug/optimization sweep, which turned up
several more that had nothing to do with what the screenshot showed.

## What the screenshot actually caught

- **The health banana rendered as a plain yellow wedge**, not a banana at all. Two compounding
  causes: the sprite was force-stretched into a box far taller than its own aspect ratio (fixed by
  giving up on `Image.preserveAspect` - its behaviour wasn't matching what the code assumed and
  there was no way to debug it further than "it's wrong" without more targeted tooling - and using
  a box close enough to the sprite's own 285x250 aspect that plain stretch-fill barely moves
  anything), and the replacement sprite had never been re-run through
  `Tools/Gorilla Warfare/Configure banana sprite import` after being swapped in, so it likely wasn't
  even importing as a `Sprite` yet.
- **The style meter's rank+multiplier line never appeared in real play**, only in the forced
  `HudPhotographer` render - correct behaviour actually (it's meant to hide until a run is active),
  but with nothing on screen to explain a bare "0" next to it, reported as looking like "a random
  number." Removed the permanent score readout entirely rather than labelling it - "why is there a
  permanent score on the screen all the time I dont need to see it like that, i Like the ultrakill
  style."
- **Two Unity batch-mode invocation bugs**, unrelated to any of the above but found while chasing
  the screenshot: `-nographics` suppresses rendering but not audio, so every automated check that
  entered play mode had been playing real sound the whole time - fixed with
  `AudioListener.volume = 0f` at the top of both `HudPhotographer` and `PlayModeProbe`'s boot
  coroutines. And `HudPhotographer` was being run with `-batchmode`, which its own doc comment
  already explained breaks `ScreenCapture` (no real end-of-frame ever arrives) - it wasn't failing,
  it was hanging forever, which is worse. Runs without `-batchmode` now, exactly as documented.

## The bug and optimization sweep

Requested directly after the above. Found by re-reading each system built this same day with fresh
eyes rather than assuming yesterday's review caught everything:

- **Three UI row templates were permanently visible** (`breakdownTemplate`, `feedTemplate`,
  `standingsTemplate`) - a template exists only to be cloned (`GameHud.Row`), but none of the three
  were ever given `gameObject.SetActive(false)` at build time, so the original template object sat
  on screen forever as an extra, untracked row. Only the breakdown one was ever actually reported
  ("a constant 1.35x headshot thingy on the right at all times") - the other two were presumably
  just less noticeable, not actually fine.
- **Ground pound dealt damage with no hitmarker, damage number or hit sound** - `DealSlamDamage`
  called `TakeDamage` directly and stopped there, never calling the same `ShowHit`/`ShowDamage`/
  `RegisterHit` sequence `Projectile.Explode`'s own AOE hits already give. Added, matching that
  exact pattern.
- **`StyleScore` used scaled time throughout**, against this project's own standing rule (a kill's
  hitstop drags `Time.timeScale` down right when the multiplier is freshest). Switched every timer
  to unscaled - and fixing `SingleShotGun`'s reload timer the same way immediately surfaced a second
  bug it caused: `UpdateReloadFlip` was still reading `reloadDoneAt` against scaled `Time.time`,
  so the two clocks disagreed the moment they diverged. Fixed the same pass rather than left as a
  new inconsistency.
- **`RegisterHitTaken`/`RegisterWallSmash` were publishing a Photon property write on every single
  hit or wall smash**, despite neither ever changing the score - only the multiplier. Removed; only
  `RegisterKill` publishes now.
- **A dead property** (`TierFraction`) that nothing had ever read. Deleted.
- **The style score was never reset between matches** - the single most serious find. `PUN never
  clears player custom properties` is this project's own oldest, most-repeated bug class (three
  separate prior incidents, all in `working-notes.md`'s "Decided, don't relitigate" and bug-log's
  own second/third passes), and the style score walked straight into a fourth: `MatchState.
  BeginWarmup` resets kills/deaths/headshots/best streak/rung for everyone already in the room at
  match start, but never touched the new `StyleScoreKey` - and `OnPlayerEnteredRoom` reset *only*
  the gun game rung for anyone joining afterward, not even the older stats. A player who finished a
  match with any style score would carry it into the next one and corrupt the actual win condition,
  not just a cosmetic number. Fixed both reset points, and widened the join-time one to match
  `BeginWarmup`'s full set - it had the identical gap for kills/deaths/headshots/streak all along,
  just never on anything that could be blamed for deciding a match's winner before this.
- A second, related gap in the same area: `StyleScore` itself only rebuilds on death/respawn, not
  on a new match starting - a player alive and standing around when a new match begins keeps their
  old component, old `score` and all, and their first kill of the new match would have published
  last match's total right back over the property reset above. Added a same-frame check in
  `StyleScore.Update()`: entering Warmup with a nonzero local score clears it and republishes.
- Assorted smaller items: a stale doc comment still saying "husk" after the rename to "peel", a
  redundant double-read of `player.Overshield` in the same method.

Verified with a batch-mode compile check and the full `PlayModeProbe` suite after each round of
fixes, same discipline as every other pass. The visual half (does the health bar actually look
right now) is explicitly not verified here - see the twenty-first pass's own note on why that
verification loop moved to Ryaan doing it directly.

# Twenty-third pass — why the style meter still didn't work, plus tuning, 2026-08-29

Same-day continuation, after Ryaan tested the twenty-second pass's fixes directly and reported
back: "style meter STILL does not work and the slide meter exhaust only shows up but not the other
thingies" - despite the sweep above having already fixed every bug it could find *in* `StyleScore`
itself. That was the tell that the actual bug was somewhere else entirely.

## The style meter never had a kill to react to

Root cause: every kill Ryaan could actually produce in solo sandbox testing was architecturally
invisible to `StyleScore.RegisterKill`, which only ever fires from
`PlayerController.RPC_Died`. Two separate reasons, both structural rather than logic bugs:

- **A training dummy's death never goes through `RPC_Died` at all.** `TrainingDummy` has its own
  entirely separate `FallOver` path with no networked actor, no kill feed entry, and no call
  anywhere near `MatchState.ReportKill` or `StyleScore.RegisterKill`. It was built to answer
  weapon-tuning questions ("does the shotgun fall off where it should"), and never wired to the
  scoring system because scoring didn't exist yet when it was built.
- **A self-kill can't satisfy `RegisterKill`'s own gate either.** `RPC_Died`'s crediting code reads
  `if (killer != null && killer.IsLocal && killer != PV.Owner)` - and `PlayModeProbe`'s own death
  test (`player.TakeDamage(500f, "Pistol", true)`) makes killer and victim the same `Player`,
  meaning this entire code path had only ever been exercised by reflection-forcing fields directly
  in `HudPhotographer`, never through real gameplay logic. That's a real gap in the probe suite,
  not just the sandbox - it explains why nothing already automated had caught this.

Both point at the same fix: give a dummy kill its own, synchronous crediting path rather than
trying to route it through a mechanism built for a networked death. `IDamageable.TakeDamage`
changed from `void` to `bool` - true if this specific call was the fatal blow.
`PlayerController.TakeDamage` always returns `false` (a real kill still only ever gets credited via
`RPC_Died`, several frames later, exactly as before); `TrainingDummy.TakeDamage` returns `true`
exactly once, on the call that brings health to zero (safe against a shotgun's other pellets
landing lethal in the same frame - `StartCoroutine(FallOver)` runs synchronously up to its first
`yield`, which sets the `down` guard as its very first line, before `TakeDamage` even returns).
`Hitbox.Apply`'s existing `bool` return - unused by its only caller, confirmed by grep before
touching it - now passes this straight through instead of meaning "was damage applied."

New `StyleScore.RegisterDummyKill(weapon, headshot, noscope, pointBlank)` sits next to
`RegisterKill` rather than behind it - a dummy kill resolves synchronously at the point of death,
so it has nothing to look up in `pendingShots` the way a real kill's async RPC confirmation does.
Both now share the actual scoring math through a new `ApplyKillGain` (extracted from what used to
be the back half of `RegisterKill` verbatim) so there's exactly one place that turns a
classification into a multiplier/score change, not two copies that could drift.

Wired into every place a shot can land a fatal blow on something that isn't a player:
`SingleShotGun.FirePellet` (both the normal hitbox branch and the no-hitbox `IDamageable` fallback
right below it - found while wiring the first one, same gap, same fix), `PlayerMovement.
DealSlamDamage`'s non-player loop, `Projectile.Explode`'s non-player loop, and
`VineGrapple.LandDummyHit`. Only the gun path computes a real noscope/point-blank read (it has
`owner.IsAiming` and `hit.distance` sitting right there, same data `RegisterKill` itself uses for a
real kill on the same weapon); ground pound, grenade and vine dummy kills credit flat, no bonus
tags - deliberately, to match what a real *player* kill through those same three weapons already
gets today (none of them ever populate `pendingShots`), rather than making a dummy kill more
generous than the real thing it's standing in for.

This should also explain "the slide meter exhaust only shows up but not the other thingies"
without being a second bug: `StyleScore.Active` (`multiplier > 1.001f`) could never go true with
zero kills ever credited, so anything in `GameHud` gated on it - the rank line, the breakdown rows
- had nothing to show, while whatever "exhaust" effect Ryaan's seeing is presumably driven by
something that doesn't depend on `Active` (the base slide/movement system itself, still intact).
Reasoned through rather than independently confirmed - flagged for Ryaan to check in the same
sandbox pass that found the original bug.

## Requested tuning, same message

- **Ground pound damage now scales with impact speed.** `GroundSlam` sets a fixed initial
  `velocity.y`, but actual impact speed grows under gravity during the fall - captured at the top
  of `SlamLandingEffects`, before anything else that frame can touch it, as `impactSpeed`, turned
  into `speedScale = clamp(impactSpeed / groundSlamSpeed, 1, 2.5)`, and applied only to damage (not
  knockback - a harder landing hitting harder is the point; flinging things further on top of that
  wasn't asked for and would fight the AOE falloff tuned two passes ago).
- **Grenade AOE damage now has the same harsh falloff ground pound already got.** Re-read
  `Projectile.Explode` in full before touching it, since the report ("it doesnt right now") implied
  no falloff existed at all - it did, linear, already correct for knockback. What was actually
  missing was the *harsher* squared curve ground pound's own damage got in the previous pass;
  mirrored it here rather than building a second AOE system, keeping knockback linear on purpose -
  a rocket-jump-style launcher should stay forgiving for mobility even where it bites harder for
  damage.
- **The knife's hold pose rotated 180° on Y** (`meleeHold`), a one-line data change on
  `Peel.asset` - the previous pass's melee-pose work got the tilt right but left it facing backward.
- **Renamed again**: "Peel Steel" → "Yellow Fang." Reported directly as still not reading as a
  knife name, "going too deep into the banana names format without giving it room to be a knife" -
  the whole point of moving this weapon off the fruit-pun pattern in the first place. "Fang" reads
  as an actual blade name on its own (karambits are sometimes called this) rather than another
  banana joke wearing a knife's clothes.

## Self-inflicted: forgot this project's own documented rule about `-quit`

First verification attempt this pass ran `PlayModeProbe` with `-quit` on the command line, same as
every other batch tool here - and got a clean exit, zero output, no failures logged, nothing. Not a
hang, not a crash: it looked like a suite that ran and found nothing wrong, which is the most
dangerous shape a broken check can take. `PlayModeProbe.Run()` calls `EditorApplication.
EnterPlaymode()`, which only *schedules* the transition rather than blocking on it, so `-quit` won
the race and shut Unity down before play mode, `Boot()`, or a single check ever ran.

This exact rule was already written down - `working-notes.md`'s "How to verify" section, plainly:
"`PlayModeProbe` must not [take `-quit`] — it enters play mode and exits itself." Should have been
checked before running the tool, not after getting a suspiciously-empty result back. Re-ran without
`-quit` and got the real output (150+ checks, `[play] ===== ALL PASS =====`). No code or doc change
needed here - the rule was already correct and already in the right place - just a reminder that
"how to verify" is worth reading, not just having written once.

Verified with a batch-mode compile check (`PlayerRigBaker.Run`, which also re-baked the weapon
preview onto the player prefab to pick up the rotated knife) and the full `PlayModeProbe` suite,
correctly invoked this time - all existing checks still pass, confirming the `IDamageable` signature
change didn't break anything already covered. The new dummy-crediting wiring itself isn't exercised
by an automated check yet (`PlayModeProbe` has no dummy-kill test at all, and building one properly
means firing a real raycast at a real dummy rather than calling `RegisterDummyKill` directly, which
would only prove the method works and not that anything actually calls it) - verified by hand
instead, reading every one of the five call sites against the actual field types (confirmed via
grep, not assumed) before editing. Real confirmation is Ryaan's own sandbox pass.

# Twenty-fourth pass — a real screenshot again, a movement combo, dummies ragdoll, 2026-08-29

Same-day continuation. Ryaan ran the fixed style meter himself and sent back a real screenshot -
proof the dummy-crediting fix landed (a real kill, "FULL SILVERBACK x5.4" with a real breakdown)
but also proof of a new bug the fix itself exposed: the rank text and the breakdown rows
overlapped. Same message asked for three more things: a second, separate movement-tech combo
meter bottom left, real ragdoll physics for training dummies (not just players), and permission to
source real SFX regardless of copyright.

## The overlap

`slideRank`'s own box uses a TopRight pivot (its top edge sits at the anchored Y, not centred on
it - confirmed by reading `Text()`'s implementation directly rather than assumed a second time),
so the meter bar hanging off its bottom - built as a child anchored to `slideRank`'s own bottom-
right corner - actually extends to Y=-129, not the Y=-99 an earlier, wrong pivot assumption would
have given. `breakdown`'s own top edge sat at Y=-118, eleven pixels inside that. Moved to Y=-150 in
both `Run()` and `Repair()` (the second as an unconditional position correction, same shape the
kill feed's own retune already uses - Breakdown is a plain RectTransform, not a TMP_Text, so it
can't go through `Retune()`). `HudPhotographer` also needed the same breakdown rows actually
populated to reproduce the bug at all - its reflection-forcing previously only set `multiplier`/
`comboExpiresAt`/`score` directly, never touching `lastBreakdown` (only `ApplyKillGain` populates
it), so the panel it photographed had a rank line and no rows under it. Fixed by reflection-adding
real `BreakdownEntry` values matching the reported scenario (POINT BLANK + WEAPON SWAP) - readonly
on a `List<T>` field only blocks reassigning the field, not mutating the list already in it.

## The movement-tech combo

"No slide combo text still which should show up... put that on the middle/bottom left now I guess...
make new movement tech combos and stuff... this includes grappling, grenade jumping, bhopping,
slide hopping, etc and make this be affected by you crashing into something." A second, separate
counter from the kill-based style score - traversal instead of combat, bottom left instead of top
right, never touching the score or the win condition.

New `MovementCombo.cs` - deliberately not folded into `StyleScore`, and deliberately not built by
rewiring `PlayerMovement.SlideChain` into something broader: that field's speed math has been
tuned across several passes (the chain-bonus compounding fix, the fatigue system, bhop's own
separate air-speed caps) and rewiring its *meaning* to also drive a HUD counter would have risked
the actual gameplay feel for a cosmetic display. Instead a lightweight, independent counter
(`Register(tech)` extends a chain and remembers the name; `Break()` zeroes it) that four systems
call into without needing to know about each other:

- **Slide hop** - `PlayerMovement`'s existing chain-increment line, the same moment `StyleScore.
  RegisterKill` already reads `SlideChain` from.
- **Bhop** - a jump landing inside `bhopGrace` of the last touchdown (`GroundMove`'s own jump
  branch) - the same timing window `bhopKeep` already rewards with kept speed, credited to the
  combo too rather than only to velocity.
- **Grapple** - `VineGrapple.RPC_Attach`. Runs on every client same as the rope's thwip sound
  right below it; `MovementCombo` only ever exists on its owner's own body, so `GetComponent`
  quietly finds nothing on a client just watching somebody else's rope land.
- **Grenade jump** - `Projectile.Explode`'s self-knockback branch, gated on `strength > 0.3f` so
  a nearby-but-not-really-a-boost tap on the blast's edge doesn't count as a trick.

`PlayerMovement.WallSmash` now breaks this the same call breaks `StyleScore`'s own multiplier -
"make this be affected by you crashing into something" answered by the existing crash-detection
threshold rather than a second one.

Hit one real naming collision building this: `PlayerController` already had an unrelated `combo`/
`Combo` (`RegisterHit`'s consecutive-hits counter, feeding the hit-pitch climb) - a same-name field
and property for a completely different concept, caught immediately by the compiler (`CS0102`) on
the first compile-check rather than silently shadowing anything. Renamed the new one to
`movementCombo`/`MoveCombo`.

New bottom-left HUD block mirrors the style meter's own shape (name + chain count, tilted, a
draining bar) rather than inventing a new visual language - tilted the *other* way (+6° rather than
-6°) so both corners lean inward symmetrically, and the meter bar hangs *above* the text instead of
below it, since this cluster sits near the bottom of the screen and "below" would push it off-screen
entirely. Built in both `Run()` and `Repair()`, the latter as a fallback off `slideCombo`'s own font
- same shape the style meter's own Repair fallback already used.

## Training dummies ragdoll now too

"Still no source like ragdoll for dummies" - `Corpse.cs` already answered this for real players two
passes ago; dummies were left on their old scripted topple specifically because a dummy has to
snap back to a clean standing pose to respawn, which a settled physics simulation can't undo. The
fix is the same one that resolved "how does a dummy explode from a grenade with no rigidbody" for
damage: don't ragdoll the dummy itself, hand the visual off to a disposable `Corpse.Spawn` copy at
the dummy's exact position/rotation/tint, while the original object goes invisible underneath it
immediately (not after a pause - a standing, unhit-reacting dummy still visible next to its own
falling ragdoll would look broken, not dead) and resets cleanly once its own timer's up. New `tint`/
`homeRotation` fields on `TrainingDummy` exist only to carry data `Corpse.Spawn` needs into
`FallOver`, which no longer touches `transform` at all before the reset.

## Sourcing real SFX

Explicit, broad permission given this pass: "I want you to find ANY sound effect online that fits
the profile or need we have regardless of it being copyrighted or not since I will be remaking them
later and this will not go anywhere beyond my computer." Extends the standing asset-sourcing rule
(previously exercised once, for a single reference image) to this session's remaining `GameAudio.cs`
gaps.

Checked the real `Resources/Audio` folders rather than trusting `GameAudio.cs`'s own doc comments,
which turned out to be stale in three places - Shield, Slide and Vine all already have real sourced
clips (from an earlier pass this session's transcript doesn't cover in this window) despite their
comments still describing empty folders waiting for one. Fixed those comments while in the file.
The genuinely empty ones - confirmed by listing the actual directories, not assumed - were
`AirBrake`, `Slam` and `WallSmash`. `Reload` has exactly one generic clip and no per-weapon fruit
sounds yet; left alone this pass, in scope for a future one specifically about it rather than
squeezed in here.

All three sourced from Kenney's asset packs (kenney.nl) - CC0, no login wall, and (confirmed by the
filenames already present in Shield/Footstep/Impact) the same library this project's existing
sounds already come from, so the new ones match in recording style rather than introducing a
different mic/room into the mix:

- **Slam** (ground pound) - `impactSoft_heavy` x3, from Impact Sounds. "Soft" over the pack's
  Wood/Metal/Plate/Bell variants because a fist and the ground it hits are both flesh-and-earth,
  not a hard material ringing.
- **WallSmash** - `impactPlank_medium` x3, from the same pack. Picked "plank" specifically because
  this game's arena walls are wooden fencing (visible in the sandbox screenshot earlier this pass),
  not masonry or metal - the material in the sound now matches the material on screen.
- **AirBrake** - `thrusterFire` x3, from Sci-fi Sounds. A short burst rather than the pack's
  sustained engine loops, matching this being a one-shot (air brake, and the ledge hop that reuses
  its fallback) rather than a held state.

A `SOURCES.txt` in each new folder, matching the credit-file convention `Shield/README.txt` and
`Explosion/SOURCES.txt` already established - what was picked, why, and exactly which call sites
use it and at what volume, so a future pass replacing these with hand-made audio knows what it's
replacing and why that choice was made. Caught and corrected one wrong claim while writing Slam's
own file: first draft said the ground-pound landing had no dedicated impact sound at all, based on
`PlayerMovement.cs` alone - re-checking found it does, in `PlayerController.BuildGroundSlamImpact`
(reached over an RPC, which is why a `PlayerMovement.cs`-only grep missed it). Fixed before it
became a false "known gap" note baked into the repo.

Verified: a batch-mode compile check that caught the `Combo` naming collision immediately, the full
`PlayModeProbe` suite (all pass, no regressions from any of the movement-tech hooks or the dummy
rewrite), and a real `HudPhotographer` screenshot - not eyeballed from the diff, the same discipline
the last two passes both leaned on - confirming both the breakdown no longer overlaps the meter bar
and the new "GRENADE JUMP x3" block renders correctly bottom left. The dummy ragdoll itself has no
screenshot here - `HudPhotographer` doesn't spawn a sandbox or a dummy, and it reuses `Corpse.Spawn`
verbatim rather than a new code path, so confidence comes from that reuse plus the passing probe
suite rather than a fresh render. Real confirmation is Ryaan's own sandbox pass, same as the style
meter fix above it.

The SFX work got its own final compile check after landing (clean) and each new `.ogg` confirmed
carrying a real Unity-generated `.meta` file, proving the import actually happened rather than
just sitting as an untracked file. Not verified by ear - nothing in this session's tooling can play
and judge a sound, the same limitation `AudioCheck` has always had for anything beyond measuring
shape (clip length, peak, onset count). Worth a listen in a real client before trusting the volumes
picked here.

# Twenty-fifth pass — the spent indicator, a bigger combo, global movement effects, 2026-08-29

Same-day continuation, four more items in one message.

## The spent indicator gets its own spot

Used to borrow the style meter's own rank text (`slideCombo`) for "SPENT 2.1" while a slide chain
was on cooldown - reported directly: "add the spent (for the sliding) to the right, not connected
to either combo bar." New `GameHud.spentText`, its own field, its own `UpdateSpentIndicator`,
reading `PlayerMovement.Exhausted` directly rather than borrowing anything. First placement
(middle-right, Y=48) collided with the kill feed - a real screenshot caught "SPENT 3.0" printed
directly over "You took Rival's head clean off." Root cause: the feed's own box is middle-right
*pivoted* (320 tall, centred on its Y=-60 anchor), so its content starts near the top of that box
at roughly Y=100, not near the anchor point the way an empty-growing list would suggest - the same
category of pivot-direction mistake as the health bar's very first build a few passes back. Moved
to Y=140, clear of the feed's box entirely, confirmed with a second render.

## The combat combo, fleshed out

Reported directly: "flesh out the combat combo bar more because it has only a few combos and its
not that fun at all." Five new bonuses alongside the original headshot/no-scope/point-blank/
weapon-swap/slide-chain:

- **LONG RANGE** - point blank's opposite, a kill confirmed past `StyleScore.LongRangeThreshold`
  (18m, a first guess - there's no real map to measure a "long" shot against yet).
- **AIRBORNE** - `!movement.Grounded` at the moment of the kill. A ground pound can only ever land
  airborne, so it always carries this too - intentional, not a loophole - jumping into a slam is
  exactly the committed risk this exists to reward.
- **BLADE FINISH** - the signature melee weapon specifically (`weapon == "Peel"`), not "any melee"
  in general, since this game only has the one.
- **KILL STREAK xN** - consecutive kills without dying (`PlayerController.Killstreak`, already
  existed for the heal-per-streak system, just never fed the style score before).
- A named multikill callout (**DOUBLE PEEL**, **BUNCH KILL**, **GORILLA WARFARE**, **GOING
  FERAL** for 2/3/4/5+) rather than a bare number - reads as its own moment rather than a bigger
  version of a normal kill, the same reasoning the rank tiers use banana/gorilla names instead of
  letters. Needed a new `PlayerController.Multikill` getter; the field already existed
  (`RewardKill` already fed it to `Hud.ShowKill`), just had no public accessor yet.

Four of the five needed zero new parameters threaded through every call site - airborne, blade,
killstreak and multikill are all computed straight off `StyleScore`'s own cached `owner`/`movement`
references inside `ApplyKillGain` itself. Only long-range needed real plumbing, mirroring
point-blank's own existing shape exactly (same `pendingShots`/`hit.distance` data, opposite
comparison).

Quaake/UT's own multi-kill words ("Double Kill", "Ultra Kill") were avoided on purpose, same
reasoning the rank tiers dodge lettered ULTRAKILL-style names - and "RAMPAGE" specifically was
already spoken for by the top rank tier, so it couldn't be reused here even though it's the
classic-arena-shooter word every one of these lists reaches for.

## The multiplier starts on damage, not on the kill

The actual redesign underneath the new bonuses: "your multiplier starts when you damage someone
and do stuff but when you kill someone you actually get the points." Before this, the multiplier
sat completely cold through an entire gunfight and only ever moved the instant something died -
new `StyleScore.RegisterHitLanded()` now bumps it (a small, capped +0.12, gated by a 0.35s
cooldown so a fast weapon spraying a whole magazine into one target can't trivially max it out
before the fight is decided) the moment *any* damaging hit connects, on a real player or a dummy
alike. Score itself is untouched by this - `ApplyKillGain` is still the only place `score` ever
changes, exactly as before, so "the kill is what actually pays out" holds; landing hits just means
the payout is bigger by the time the kill lands, because the multiplier had already been climbing
through the fight instead of starting cold.

Wired into the same eight call sites the dummy-kill-crediting fix touched two passes ago - every
place a shot, a slam, a blast or a vine pull can land non-fatal damage now credits this alongside
whatever HUD hit-confirmation it already gave. Two things worth being careful about, both handled:
self-damage from a grenade jump doesn't credit a hit against yourself (`Projectile.Explode` already
distinguishes `self` for the knockback formula, reused here), and a teammate hit in a team mode
doesn't credit anything either, because every one of these call sites already returns early on a
teammate before reaching the point this new call sits at.

## Movement-tech effects were local only

"Fix the movement tech effect being local only, make it global." `PlayerMovement.WallSmash`,
`AirBrakeEffects` and `LedgeHopEffects` all built their dust/spark/sound directly inline rather
than over an RPC - and `PlayerMovement` only ever exists on its owner's own client (destroyed on
every remote copy in `PlayerController.Start`), so none of the three had ever been visible or
audible to anyone standing nearby when someone else did them. The exact same bug `PlayerController.
ReportGroundSlam` already fixed for the ground pound, just never applied to the other three
movement-tech impacts that share its shape.

Same fix, same pattern, three more times: `ReportWallSmash`/`ReportAirBrake`/`ReportLedgeHop` on
`PlayerController`, each an RPC to `RpcTarget.All`, each landing on a shared static
`BuildXxxImpact` that does the actual particle/sound work (`BuildGroundSlamImpact`'s own shape,
copied rather than reinvented). Hitstop/shake/the shader pulse stay local-only in all three, same
reasoning `BuildGroundSlamImpact`'s doc comment already gives - it's your own impact, not something
a bystander's camera should shake for. `AirBrakeEffects`/`LedgeHopEffects`'s sound moved from
`PlayShaped` (flat, 2D, only ever meant for a solo listener) to `PlayAtShaped` (positional) as part
of the same change, since everyone hearing it now needs to hear it coming from where it happened.

Verified: a batch-mode compile check after each round (the first one caught a real naming
collision immediately, see below), the full `PlayModeProbe` suite passing with zero regressions,
and two real `HudPhotographer` screenshots - one that caught the spent-indicator/kill-feed overlap
before it shipped, a second confirming the fix and separately confirming the longest new breakdown
label ("1.60x GORILLA WARFARE") fits its box without overflowing. The movement-effect networking
fix has no screenshot of its own - a single-client render can't show whether a *second* player
would have seen or heard something - so that one is reviewed-correct by pattern-matching against
`BuildGroundSlamImpact`'s already-proven shape, not independently rendered.

**One near-miss worth recording**: while sourcing the SFX two passes ago, `PlayerController`
already had an unrelated `combo`/`Combo` (the consecutive-hits counter feeding the hit-pitch
climb) - unrelated to this session's own `MovementCombo`, but the exact same name. Compiler caught
it immediately (`CS0102`) the first time either was built; renamed the movement-tech one to
`movementCombo`/`MoveCombo`. Recorded again here as a reminder this class of collision is a real,
recurring risk in a file this large, not a one-off - grep for a name before introducing it,
not just after the compiler complains.

# Twenty-sixth pass — tokens and the crate opening system, 2026-08-29

Same-day, same message as the four items above. The biggest single ask of the whole session:
"add the gambling crate stuff... make sure people get tokens at the end of each round... add a
button for people to open crates... three tiers... make the entire opening experience with the
whole counter strike style stuff... make sure this is the BEST crate opening feature ever."

## What research actually changed about the build

Two searches before touching code: how real case-opening screens are built, and what's actually
known about why they work. The concrete takeaways that shaped this:

- **The spin is long on purpose.** CS2's own runs close to six seconds - a Stanford study
  (Knutson) found the anticipation phase, not the reveal itself, is where the dopamine response
  actually peaks. `CrateOpeningScreen`'s spin runs 5.6 seconds for the same reason - the reveal at
  the end is comparatively quick, because it isn't where the real payoff is.
- **Near-miss rigging is a real, documented dark pattern** - engineering the reel to
  almost-but-not-quite land on a rare item to manufacture false hope, independent of what the
  actual result is. Deliberately not built here: `CrateInfo.Roll()` decides the honest result up
  front, and every filler card around it is drawn from that same crate's real odds table
  (`crate.Roll()` again, not a uniform shuffle) - a rare-looking card can land next to the result
  by genuine chance, never by design.
- **Escalating presentation by rarity** isn't a loot-box-specific trick - it's the same "bigger
  moments get more of the existing budget" rule this project already applies everywhere (a
  headshot over a body shot, a kill over a hit). `CrateRarityInfo.Weight()` turns that into one
  formula (particle/shake/sound scale with it) rather than five hand-tuned copies, one per tier.

## The economy

New `PlayerWallet.cs` - tokens, `PlayerPrefs`-backed with the same `Prefix` convention
`GameSettings.cs` already established, because a wallet has to survive between sessions the way a
Photon room property never does (the opposite of this project's usual "PUN never clears custom
properties" bug class - this one actually needs to persist past the room, not get cleared with
it). Flat 50 tokens at the end of every round (`GameHud.AwardRoundTokens`, hooked into the
existing `MatchPhase.Over` transition with a one-shot guard so it fires once per round, not once
per frame across the whole 12-second results screen) - no performance scaling, because the brief
only asked for "make sure people get tokens," not a tuned economy on top of it.

The reward callout itself is local and personal - "a whole animation plays for the person who got
it" - same reasoning a kill sound only plays for the killer: what you earned is your own moment,
reusing the existing punch-scale/Juice/GameAudio.Kill language this HUD already speaks everywhere
else rather than inventing a second one for tokens specifically.

## Five rarities, three crates

`CrateRarity.cs`: SCRAP/SPROUT/PRIMAL/MYTHIC/APEX, gray/green/blue/purple/gold - the standard
Diablo II/WoW ladder, kept because it reads instantly to anyone who's played a loot-bearing game
since, but every name is this game's own and deliberately shares zero words with StyleScore's rank
tiers or multikill callouts (three systems all naming escalating tiers of roughly the same five
rungs would blur into each other with any overlap).

`CrateInfo.cs`: Rotten (100 tokens) / Ripe (300) / Holy (750), continuing the same ripeness
vocabulary the guns already use rather than starting a fourth naming scheme. Odds scale with
price - Holy's APEX chance (6%) is twelve times Rotten's (0.5%) - because a top crate that's
basically the same as the cheap one would make the whole tier system pointless. All of this is a
first guess nobody has opened a real one against yet, the same caveat every other feel number in
this project carries.

## The build

`CrateOpeningScreen.cs` (runtime) + `CrateShopBuilder.cs` (Editor, builds it as a prefab). Prefab
rather than scene content - same reasoning `SettingsMenuBuilder.cs` already established for the
settings menu: this has to be reachable from wherever a "open crates" button ends up living, and
scene content would tie it to one scene for no reason. **Not yet wired to any actual button in any
scene** - built and verified standalone; whoever adds the entry point drags `CrateShop.prefab` in
and calls `SetActive(true)` on it.

The carousel: a masked viewport, a wide strip of cloned cards sliding left under a fixed pointer,
quintic ease-out (`1-(1-k)^5`) rather than linear-then-stop so slowing down reads as losing
momentum rather than hitting a wall - tried cubic first, it still felt like arriving rather than
settling. A tick sound fires whenever a new card crosses the pointer rather than on a fixed timer,
pitched down as the real speed decays under the curve, so the audio sells the deceleration even
without watching closely.

Two real bugs a real screenshot caught, both fixed the same pass:

- **The backdrop rendered almost fully see-through** despite 0.95 alpha on a near-black colour -
  the game world behind it was clearly, brightly visible rather than the near-blackout the maths
  says 95% opacity should produce. Root cause not fully chased down (no debugger reaches into a
  batch-mode render) - sidestepped instead by going fully opaque, since a modal shop screen never
  needed to show the game behind it in the first place.
- **Every button rendered as an empty rectangle** - "OPEN ANOTHER", "CLOSE", all of them.
  `BuildTextButton`'s own `label` parameter sat right there and was never actually assigned to the
  text component it built; the `Text()` call only ever received the GameObject's internal name
  ("Label") as content. A real screenshot of the reveal screen caught blank buttons before this
  shipped; fixed by actually setting `labelText.text = label`.

A third, smaller one found the same way: the reveal's own full-screen glow only reaches 60% alpha
even at APEX, low enough that the landing pointer's white line was still faintly visible bleeding
through it in a screenshot. Fixed by having `Reveal()` hide the carousel viewport outright rather
than trusting the glow to cover it - the reel has no reason to still be there once the result is
showing.

Verified: a batch-mode compile check after each round, the full `PlayModeProbe` suite passing with
zero regressions (nothing in it exercises the crate system directly - it isn't wired to a match or
a button yet - so this only confirms the addition didn't break anything already covered), and
three real `HudPhotographer` screenshots - the selection page, and the reveal forced to APEX via
reflection (spending 750 real tokens and sitting through a real 5.6s spin isn't a reasonable way to
check a screenshot). Both real bugs above were caught by those renders, not by reading the code
back.

**What's still genuinely unverified, beyond the usual "numbers are a first guess"**: nobody has
watched a real spin decelerate and land - the carousel's physics were verified by forcing the
*end* state (Reveal) directly, not by watching `SpinCarousel()` actually run and checking the tick
timing/easing feel correct in motion. The three crate accent stripes' actual on-screen width
relative to each card wasn't independently confirmed either - the selection-page screenshot shows
them, but this pass didn't zoom in and measure.

# Twenty-seventh pass — the follow-up list, then a sweep, 2026-08-29

Same-day, a follow-up message with ten more items plus "how do I access the crates?" (answered by
one of the ten - see below) and a mid-pass note that the settings screen's look and font still
weren't right. Grouped by system.

## Movement combo and the spent indicator

Bhop pulled back out of `MovementCombo`, per direct request - the `GroundMove` jump branch that
credited it is gone, doc comments updated to stop describing a trick that no longer counts.
`spentText` moved a second time - middle-right had already been retuned once this session (see
the twenty-fifth pass) to clear the kill feed, then reported directly as wrong entirely: "put the
spent from the sliding to the left on top of the movement combo thing." Bottom-left now, Y=150,
clearing the movement combo's own cluster (which tops out at Y=129 including its meter border).

## The leaderboard, reworked

Reported directly as looking "pretty bad right now and not updated with the game," plus stale
fonts. The data was already correct (style score for everything but gun game, kills/deaths
otherwise) - this was entirely presentation. 1ST/2ND/3RD now colour gold/silver/bronze via a
`<color=#HEX>` rich-text tag on just the rank-number span (the same medal colours the crate
rarities' own top tier reuses), row font bumped 28->32pt, container widened 700->820 to carry the
new prefix without cramping, and the awards list gets a real "AWARDS" header instead of a blank
line. `Repair()` needed two new checks Retune can't do on its own - font asset and container
width aren't things Retune touches, and a scene built before either changed would otherwise never
pick them up.

**A real bug caught rendering the results screen for the first time**: `tokenRewardBurst?.Play()`
threw `UnassignedReferenceException`. `tokenRewardBurst` is a deliberately-unwired optional
`ParticleSystem` field (no particle flourish built for it yet), and Unity's "missing reference"
state on a `SerializeField` isn't true C# `null` - `?.` uses a raw reference check that misses it,
where `if (x != null)` correctly goes through `UnityEngine.Object`'s own overloaded `==` and
catches it. A `?.` on any Unity-Object-typed field is only actually safe once something has
confirmed it's either really null or really assigned - never for a field that might be sitting at
Unity's own "None." Swept the rest of the session's own code for the same shape afterward and
found nothing else - this was the only optional, deliberately-unwired reference in the batch.

**A second, more serious bug found the same way**: forcing a player's style score for that same
screenshot and reading `+0 TOKENS` back from the new reward formula (see below) exposed that
`StyleScore.Awake()` never seeded `score` from the room's own existing custom property. A fresh
`StyleScore` is built on every respawn (`PlayerController.Start`'s own `AddComponent`, same as
`SpeedRush` - there's no previous instance to carry state over from), so any player who died even
once during a match would have their *entire accumulated score silently overwritten* the next
time they killed anyone, because `Publish()` unconditionally writes whatever `score` currently
holds over the room property - and a fresh instance starts at 0. This is the stat
`MatchState.LeaderByScore` uses to decide who wins deathmatch, corrupted by the single most
ordinary thing that happens in a match. Fixed by seeding `score` from
`RoomManager.GetStat(PhotonNetwork.LocalPlayer, RoomManager.StyleScoreKey)` in `Awake()` - the
existing warmup-boundary reset in `Update()` is untouched and still the thing that actually zeroes
it for a new match.

## Ground pound's sound

"SO BAD, it sounds like someone hitting their microphone." The original pick (`impactSoft_heavy`,
Impact Sounds) was the wrong genre entirely for what a ground pound needs - a soft-material Foley
thud, not a boom. Replaced with `lowFrequency_explosion` from Sci-fi Sounds (the same pack
`AirBrake` already uses) - a real low-end boom instead of another impact-pack thud sitting in the
same boxy mid-range as everything else. Only two variants exist in that pack, down from three -
an acceptable trade for actually sounding like the thing it's supposed to.

## The settings screen: colour, font, a live crosshair preview, joke settings

Fonts and colours were this screen's own choices, made independently of and before Jersey10/Anton
became the rest of the reworked UI's actual pairing - reported directly, twice in the same pass,
first as part of the general list and then again on its own once the first fix hadn't gone far
enough: "i dont like the settings menu look rn and it also uses the old font too." `FindFont()` was
hardcoded to search for "Helvetica Punk" by name; now resolves "Jersey10" the same way
`HudBuilder.FindFont` does, with a new `FindHeadingFont()` ("Anton") for the page heading and tab
labels specifically - the screen had exactly one type weight before this, a title and a slider
value in the same voice. Accent colour moved from a plain orange to the same hot magenta
`GameHud.killColour` already uses, so this screen reads as part of the same game instead of a
different one bolted on.

New live crosshair preview - "I want there to be a crosshair visual on the crosshair menu so you
can see what crosshair youre working with." `CrosshairPreview.cs` is deliberately simpler than
`GameHud.UpdateCrosshair`, not a shared copy of it - no weapon equipped on the settings screen
means no reticle style or dynamic spread to preview, just the plain static cross most of a match
is actually spent looking at, redrawn from the same `GameSettings` values the sliders next to it
write to (subscribed to `GameSettings.Changed`, so it updates live while dragging a slider rather
than only on the next tab switch). Floats outside the settings frame's own right edge rather than
inset into the scrolling content - content already uses the panel's full width for its rows, and
an inset preview there would sit on top of whatever happened to be scrolled underneath it. Shown
only on the Crosshair tab (`SettingsMenu.Show` toggles it alongside switching which rows build).

Two joke settings, real PlayerPrefs-backed entries wired through the exact same Toggle/Slider
helpers as every real setting on the screen, which is the actual joke - "believe in bigfoot" and
"monkey business level" persist and toggle exactly like Fullscreen or Sensitivity do, right up
until you go looking for what either one is actually connected to.

## Tokens: given at the start, spent on cheaper crates, earned exponentially

"Give every player 100 points from the start" - `PlayerWallet.Tokens`'s own `PlayerPrefs` fallback
doubles as the starting grant now (100 instead of 0), so a fresh install reads 100 until the first
real Add/Spend writes an actual value. Crate costs cut roughly 10x across the board (Rotten
100->10, Ripe 300->50, Holy 750->100) - the round-end reward needed to actually be reachable
against them.

The reward itself: "proportional to how many points you got in that game... cap it at 50...
exponential and not linear or logarithmic." `GameHud.RoundTokensFor` is a base-2 exponential,
`50 * (2^(score/2500) - 1)`, clamped to the 0-50 range - climbs slowly at low scores and
accelerates toward the cap rather than paying out most of the reward for a merely middling
performance. 2500 is a first guess with nothing played against it yet, same caveat as the crate
odds and everything else numeric this pass added.

## Answered directly: how do you access the crates

A new "CRATES" button in the title menu, next to Settings - `TitleMenuCrateButton.cs` (Editor,
re-runnable, additive) clones the existing SettingsButton in `Canvas/TitleMenu/ButtonContainer`
rather than hand-building a new one, swaps `OpenSettingsButton` for a new `OpenCrateShopButton`
(same shape, same reasoning: the crate shop doesn't exist until `RoomManager` has instantiated it
from a prefab, so there's nothing in the scene for an inspector reference to point at), and
retitles the cloned label. `CrateOpeningScreen` picked up the same singleton-on-a-persistent-object
shape `SettingsMenu` already used (`Instance`, instantiated once by `RoomManager.Awake` alongside
the settings screen) specifically so this button had something to call.

## The loading screen gets a spinning model

"I want you to improve it by adding a player model in the middle that just spins around like the
gorilla spinning meme." `LoadingScreenSpinner.cs` builds its own self-contained diorama - a
`MonkeyRig` model, a directional light, a camera rendering to a `RenderTexture` - rather than
depending on whatever 3D scene happens to be behind the loading screen's own UI canvas, because
the loading screen shows up exactly when a scene transition is in progress, the one moment there's
no guarantee a normal game camera exists at all, let alone one pointed somewhere useful. Placed
20,000 units from true world origin rather than on a dedicated culling-mask layer - simpler than
adding and wiring a new layer for one spinning prop, and the preview camera's own short (10 unit)
far clip plane means it physically cannot render anything else regardless of what layer it ends up
sharing. Not independently screenshotted - the loading screen only exists mid-scene-transition,
which `HudPhotographer`'s own boot flow doesn't pass through, and reproducing that timing
specifically wasn't worth building a fourth diagnostic tool for at the end of this pass. Reviewed
by hand instead: `MonkeyRig.Build` is already proven to work standalone from `TrainingDummy`
and `Corpse`, neither of which is networked either.

## The requested sweep

Checked for the same `?.`-on-possibly-unassigned-Unity-reference shape that caused this pass's
first real bug, across every script this session touched - found nothing else matching it. Found
one real latent fragility instead: nothing in the current UI can close `CrateOpeningScreen` while
a spin is running (its only close button lives on `selectPage`, which isn't showing during one),
but Unity stops every coroutine on a GameObject the instant it deactivates regardless of cause,
which would skip `RunOpening`'s own `spinning = false` at the end and leave that guard stuck true
forever - permanently refusing every future crate open, through a path nothing currently exercises
but that costs one line in `OnDisable` to close off before something else that closes menus from
outside ever gets added.

**Found but deliberately not fixed, flagged instead**: `GameHud.UpdateStandings`'s new rank-number
rich-text tag and the kill feed's own long-standing text interpolation both drop a player's own
display name straight into a `TMP_Text` with rich text enabled by default - a player who named
themselves with a stray `<color=...>` or similar could corrupt formatting on everyone's screen,
not just their own. Not new to this pass (the feed has always done this; standings only just
started using rich text itself) and not a security issue in the sense this project's own "Decided,
don't relitigate" list already accepts (client-authoritative, small trusted rooms) - a low-stakes
display prank, not an exploit. Worth a real fix (strip or escape `<` once at the name-input layer
rather than everywhere a name gets displayed) but not one made under an already-enormous pass on
someone else's actual ask list.

Verified: a batch-mode compile check after every round of changes, the full `PlayModeProbe` suite
passing with zero regressions at the end of the whole pass, and real `HudPhotographer` screenshots
of the results screen (caught both real bugs above) and the settings screen (confirmed the new
font, colour and live crosshair preview together, not just individually).

# Twenty-eighth pass — the crates button did nothing, a leftover preview box, the wrong yellow, 2026-08-29

Same day, a follow-up report with a console screenshot: clicking CRATES in the real game logged
`[crates] nothing to open - RoomManager never instantiated the screen`, twice. Three fixes.

## The crate shop's Awake never ran

Root cause: `CrateShopBuilder` built the prefab's own root already inactive
(`root.SetActive(false)`), meaning a button elsewhere could later call `SetActive(true)` on it to
"open" it. But `RoomManager.Awake` instantiates that prefab once, up front, same moment it
instantiates `SettingsMenu` - and Unity never calls `Awake` on a GameObject that's inactive at the
moment it's created, including one instantiated already-inactive. `CrateOpeningScreen.Instance`
was therefore never set, silently, from the very first build of this feature - every click on the
real button hit the `Instance == null` branch and logged exactly the warning reported.

Missed by every verification this session ran on the crate shop, because `HudPhotographer`'s own
check loaded and instantiated a *second*, separate copy of the prefab directly and activated
*that* one - which fires Awake completely normally, and produced a perfectly good-looking
screenshot every time, of a code path a real player's button never actually uses. Fixed in two
places: the actual bug (root stays permanently active now; a new child `panel` - matching
`SettingsMenu`'s own already-correct shape exactly - is what `Open()`/`Close()` toggle instead),
and the test that missed it (`HudPhotographer` now calls `CrateOpeningScreen.Instance.Open()`,
the same call the real button makes, rather than building its own separate instance). The second
fix matters as much as the first - without it, this exact class of bug would pass every future
screenshot check again the next time it happens somewhere else.

## The crosshair preview's background box never hid

The `CrosshairPreview` component lived on an empty child transform *inside* the visible dark
background box, not on the box itself. `SettingsMenu.Show` toggling `crosshairPreview.gameObject`
therefore only ever hid the tick marks - the box behind them, with its own `Image`, stayed on
screen on every tab. Fixed by moving the component onto the box GameObject directly and parenting
the ticks under it instead of under a redundant middle layer - one toggle now hides the whole
thing, ticks and background together.

## The magenta accent didn't fit

The twenty-seventh pass's own fix (plain orange -> `GameHud.killColour`'s hot magenta, reasoning
that sharing an accent with the rest of the HUD beat an independently-chosen one) was reported
right back as wrong: "i dont like the pink purple color it doesnt fit with the game." Reconsidered
rather than just reverted - `killColour` is specifically this game's *violence* accent, used for
kills and headshots, and a neutral settings screen borrowing it was reaching for the wrong half of
the palette. Banana yellow instead - the colour that actually runs through the whole game's
identity (ripening guns, the health banana) rather than its combat half.

Verified: batch-mode compile checks, the full `PlayModeProbe` suite (all pass), and real
`HudPhotographer` screenshots of both - the crate shop screenshot now goes through
`CrateOpeningScreen.Instance.Open()` and shows the real select page (100 tokens, the three
rebalanced prices) exactly as a real player would see it; the settings screenshot confirms the new
yellow accent on the Crosshair tab.

## A first pass of the requested full-game sweep

Time-boxed rather than exhaustive - "the entire game" is 800+ scripts deep at this point in the
project, and a real line-by-line pass on all of it isn't a same-session task on top of everything
above it. Started with the mechanically-checkable version of this pass's own two real bugs
(`?.` on a possibly-unassigned Unity reference, dead state that's written but never read) across
this session's own new files specifically, since those are both the least reviewed and the most
likely to still be carrying something:

- `CrateOpeningScreen.TickInterval` - declared, commented, never once used. The actual tick timing
  works by comparing which card index the pointer has reached frame to frame, not by a fixed
  interval at all; this constant was left over from an earlier version of that idea. Deleted.
- `spawnedCardImages`/`spawnedCardLabels` - two full lists, populated every single card of every
  single spin, never read anywhere. Destroying each card's own root (`spawnedCards`, which *is*
  used) already destroys its children - these two were pure dead weight. Deleted.
- No other `?.`-on-Unity-reference matches found outside the one already fixed.

Continuing this properly - the rest of the movement/weapon/networking code, not just today's own
additions - is real, valuable, and explicitly still owed; it just isn't something to compress into
the tail end of an already enormous pass. Flagging that honestly here rather than claiming a full
audit that didn't happen.

One pre-existing, unrelated item noticed in passing: both `PlayModeProbe` runs this pass logged
`Unable to parse file ProjectSettings/TagManager.asset: [Parser Failure at line 41...]` - present
before any of today's edits too (confirmed against this session's earlier probe log), so not
something today's changes caused. Cosmetic so far - the Ragdoll layer it presumably concerns still
resolves correctly (no "No 'Ragdoll' layer" error from `Corpse.PrepareRagdollLayer`, and the full
suite still passes) - but worth a look if TagManager ever needs hand-editing again.

# Twenty-ninth pass — the sweep this project's own notes called "explicitly still owed", 2026-09-03

The twenty-seventh pass time-boxed the full-game sweep to this session's own new files, on the
mistaken belief the project was "800+ scripts deep" - later corrected in conversation to the real
number (107: 67 under `Assets/Scripts`, 40 under `Assets/Editor`; the 800+ figure was Unity's own
asset-count log counting every installed package's scripts, not this project's code) and explicitly
flagged as "real, valuable, and explicitly still owed." This is that pass: every file in both
folders, read in full, via four parallel review agents (one per third of `Assets/Scripts`, one for
all of `Assets/Editor`) rather than one pass reading all 107 serially. Also asked for in the same
sitting: preparing the codebase for new game modes ahead of the Party-mode minigames being designed
(see `ideas.md`), an optional PSX filter, settings-menu UX research, and a main-menu pass.

## Every match mode was re-derived from the raw enum in eight different files

The reviews' single biggest recurring finding: `MatchState.Mode == MatchMode.X` (or `!=`)
comparisons scattered across `GameHud`, `ColourPicker`, `ModeSelector`, `PlayerColours` (five
separate call sites on its own), `RoomListItem`, `ScoreboardItem`, `SingleShotGun` and `VineGrapple`
- each independently re-deriving "does this mode use teams," "does it rank by style score," "does
it show the gun-game ladder," or its display name. A new mode meant hunting down and hand-editing
every one of those call sites, with no compiler error if one was missed - exactly the shape of
problem Party's minigames were about to make much worse.

Fixed with one new file, `MatchModeInfo.cs`: a `MatchModeInfo` struct (display name, short name,
description, `UsesTeams`, `RanksByStyleScore`, `ShowsLadder`) and a `MatchModes.Of(MatchMode)`
lookup. All eight files now read a property instead of re-deriving one. Deliberately does *not*
own match length, loadout rules or win-condition selection - those stay as `MatchState`'s own
internal branches, which is already the one place they live and which this project's own docs
mark too fragile (client-authoritative, room-property-driven, a long documented history of
same-shape bugs) to restructure into a bigger strategy-object refactor without a reason better
than tidiness. That deeper refactor is a real, separate follow-up, not done here.

Concrete bugs this same refactor fixed along the way, found because they were the exact places the
old per-file logic had drifted from each other:
- `GameHud`'s mode label showed "DEATHMATCH" for Team Deathmatch too - the ternary only ever
  checked for Gun Game.
- The lobby room list badged every Team Deathmatch room `[DM]`, indistinguishable from a plain
  Deathmatch room - same missing branch, different file.
- Team Deathmatch's scoreboard rows looked identical to free-for-all's - no side shown anywhere
  despite `PlayerColours` already tracking one per player. Now tints the name in the player's own
  drawn colour (`PlayerColours.For`) when the mode uses teams.
- `SingleShotGun` and `VineGrapple` each re-checked `MatchState.Mode == MatchMode.TeamDeathmatch`
  on top of calling `PlayerColours.SameTeam`, which already returns false outside a team mode on
  its own - redundant, and each had its own copy of "resolve a hit to a PlayerController, then ask
  if it's a teammate." Both now call a single `PlayerController.IsTeammate(PlayerController)`.

## Other real bugs found reading every file

- `GameHud.ShowDamageFrom`'s eviction-when-full path always reused `damageArrows[0]`, unlike the
  near-identical `FreeDamageLabel` right below it in the same file, which correctly scans for the
  oldest by `born` time - a just-spawned indicator could get stolen out from under a much older
  one. Mirrored the correct pattern.
- `MatchState.Update`'s "no phase in the room yet" branch called `BeginWarmup()` - which itself
  calls `Requested(Warmup)`, setting `awaitingEcho` - but never checked `awaitingEcho` before
  re-entering that branch. `SetCustomProperties` doesn't update the local cache until the server
  echoes it back (a documented trap in this file already), so `ContainsKey(PhaseKey)` kept failing
  for however many frames that round trip took, and `BeginWarmup` - which clears every score dict
  and reassigns teams - fired again on *every one* of those frames instead of once. Same
  `awaitingEcho` guard every other transition in this file already uses.
- `Launcher`'s region-fallback path wrote `PhotonNetwork.PhotonServerSettings.AppSettings
  .FixedRegion = string.Empty` directly - mutating the committed `PhotonServerSettings.asset` in
  place. Unity does not revert ScriptableObject field writes made in Play Mode the way it reverts
  scene changes, so hitting this fallback once during editor testing would have permanently wiped
  the project's own `FixedRegion` (deliberately `uae`, chosen for the actual Pakistan/UAE/Italy
  test group) off disk. Now clones `AppSettings` via a JSON round-trip and hands
  `ConnectUsingSettings` the clone, leaving the real asset untouched. Found the same setting
  already blank on disk while fixing this - restored to `uae`, confirmed by `SceneCheck`, which
  fails outright if it's ever empty.
- `TrainingDummy.FallOver` waited on scaled `WaitForSeconds` for its respawn timer, but a dummy's
  death is always accompanied by the killing weapon's own `Juice.Hit()`, which drops
  `Time.timeScale` - the exact hazard this project's own conventions already call out for anything
  that can coincide with hitstop. Switched to `WaitForSecondsRealtime`.
- `SpeedRush.Update` read `player.View.IsMine` with no null check on `View`, unlike the equivalent
  ownership check everywhere else in the codebase. Added the guard.
- `WeaponCheck`'s shotgun-width assertion loaded `"Models/Weapons/BananaShotgun"` directly instead
  of through the file's own `Weapon<T>()` helper (which tries both the plain and `Banana`-prefixed
  names specifically because assets are mid-migration off that prefix) - would have thrown
  `NullReferenceException` outright rather than failing the check cleanly if the shotgun model
  were ever renamed the way others already have been.
- A stray invisible soft-hyphen (U+00AD) inside `KillFeedLines.cs`'s "harvested" line, a paste
  artifact risking a missing-glyph box in exactly the fonts already flagged as having incomplete
  character coverage.
- `ProjectSettings/TagManager.asset` had inconsistent blank-layer-slot formatting (one `-` line
  missing the trailing space every other blank entry had), which made Unity's own YAML parser log
  `Unable to parse file ProjectSettings/TagManager.asset` on *every single batch-mode run this
  entire project has ever done* - noted as a known, pre-existing, "cosmetic so far" item at the end
  of the twenty-seventh pass and left for later. Traced properly this time by testing the actual
  hypothesis (trailing whitespace) rather than guessing twice more at the format - confirmed by the
  warning disappearing entirely on the next run once every blank entry matched.
- `Assets/Editor/GraphicsSettings.asset`'s Always Included Shaders list never had `Custom/
  ScreenOutline` in it - the cell-shading outline is found at runtime with `Shader.Find` and isn't
  referenced by any material, which is exactly the condition under which Unity's shader stripping
  can cut a shader from a *built* player while it keeps working fine in the Editor (which never
  strips anything). This is the likely real explanation for an earlier, never-resolved report this
  session of the outline being missing "in the actual game" specifically. Fixed via a new tool,
  `Tools/Gorilla Warfare/Always-include the custom shaders` (`AlwaysIncludeShaders.cs`), rather
  than hand-typing a GUID into the settings asset - confirmed the hard way first, by typing two
  fabricated-looking GUIDs into `GraphicsSettings.asset` directly and catching it before running
  anything, then building the tool to let `SerializedObject` assign the real ones instead.

## Performance

- `MonkeyRig.ApplyTint` allocated a fresh `MaterialPropertyBlock` and a fresh
  `GetComponentsInChildren<Renderer>` array on every call - and it's called every frame for the
  whole ~2s spawn-protection window after every respawn, for every player, on every client, plus
  again on every hit-flash. Both cached now, rebuilt only if the model itself changes.
- `MuzzleFlash.Fire` allocated a `MaterialPropertyBlock` per shot - real GC pressure on an
  automatic weapon. Cached as a field.
- `Projectile.Update`'s per-frame `SphereCast` resolved `LayerMask.NameToLayer` fresh every call
  for every live projectile, instead of caching it the way `SingleShotGun.TraceMask` already does
  for the equivalent lookup. Same lazy-init pattern applied.
- `KillCam.Find` and `PlayerController`'s own damage-direction lookup both scanned every
  `PlayerController` in the scene with `FindObjectsByType` to find the one owned by a given Photon
  player - `KillCam`'s copy did this every `LateUpdate` frame for the whole killcam duration.
  Replaced both with `PlayerController.ByOwner(Player)`, a dictionary maintained on
  `Awake`/`OnDestroy`.

## Duplication

- The rising-pitch hit-confirmation sound (`RegisterHit()` then `GameAudio.PlayPitched` at a pitch
  derived from the combo count) was copy-pasted seven times across `SingleShotGun`, `Projectile`
  (twice), `PlayerMovement` (twice) and `VineGrapple` (twice). One shared
  `PlayerController.PlayHitConfirm(PlayerController, bool headshot = false)` now, static and
  null-tolerant on the owner specifically because `SingleShotGun`'s own call site could reach it
  with no owner and still wants the sound to play.
- `Gun.cs` added nothing over `Item` - it only re-declared an already-required abstract override,
  and had exactly one subclass. Folded away; `SingleShotGun` now extends `Item` directly.
- `SceneCheck`'s exhaustive empty-serialized-reference walk only ever covered `GameHud`. Pulled
  into a shared `CheckWiring` and extended to `ModeSelector`, `ColourPicker` (both real scene
  objects in the menu, same "can have a reference dragged loose" risk `GameHud` already has) and
  the `SettingsMenu`/`CrateShop` Resources prefabs (checked by loading the prefab asset directly,
  since `FindFirstObjectByType` only ever finds scene instances). All four came back fully wired -
  new coverage, not a bug caught, but coverage that didn't exist before this pass.

**Found and flagged, not fixed this pass**: the editor tools folder's single biggest duplication is
its screenshot-capture sequence (create a `RenderTexture`, swap `targetTexture`, `Render()`, swap
`RenderTexture.active`, `ReadPixels`/`Apply`, restore, `EncodeToPNG`) - hand-written independently
at least seven times across `OutlineCheck`, `HitboxPhotographer`, `PeelPhotographer`,
`SkyboxPhotographer`, `TwoHandedPhotographer`, `OutlinePlayCheck` and `PlayModeProbe`, well over a
hundred duplicated lines. Also: `Find(scene, name)` reimplemented in four different scene builders,
a "find descendant by name" helper reinvented five more times under different names, three separate
`Wire(SerializedObject, ...)` helpers, and the Jersey10 font asset looked up three different ways
across three files. All real, all editor-only (zero runtime risk either way), and all left alone
this pass rather than mechanically refactored across seven-plus files with no dedicated verification
budget left to spend on tooling that doesn't ship. A good first project for whenever editor tooling
gets its own pass.

## Settings menu: reset per page, a PSX filter, toggle-to-aim

"Reset to default (page specific)" - `GameSettings` used to have exactly one `ResetAll()`, and the
settings screen's one reset button always called it, taking keybinds and every other tab with it
just because someone wanted their crosshair colour back. Split into `ResetAim`/`ResetAudio`/
`ResetVideo`/`ResetCrosshair`/`ResetKeys`, each only touching the PlayerPrefs keys that page can
actually change and reloading through the existing `Load()` rather than duplicating what each
property's default is a second time. The settings screen's reset button now calls whichever one
matches the open tab and relabels itself ("RESET VIDEO", etc.) each time the tab changes.
`ResetAll()` still exists and still does everything at once.

Researched what actually makes a settings menu good rather than guessing: colourblind modes,
scalable text and toggle-vs-hold options for motor accessibility came up repeatedly. Colourblind
work was scoped out deliberately - this HUD's actual colour-coding (red/blue teams, brightness-led
emphasis, a white hit-flash) is already fairly safe, and the one real green-adjacent case (the
ripeness-based health bar) has been through enough rounds of Ryaan's own direct, specific correction
this session that re-theming its palette without asking first would be overstepping, not helping.
Toggle-to-aim shipped instead - a `GameSettings.AimToggle` bool, read in `PlayerController.Update`
(latches on `KeyBinds.Pressed(Aim)` instead of reading `Held` every frame when it's on), new toggle
row on the Aim tab. Toggle-to-crouch/slide was considered and dropped - that key has its own long,
specific bug history (shared with the slide buffer and air brake, several past passes' worth) and
touching it for an accessibility option that wasn't directly asked for wasn't worth the risk.

The PSX filter: a genuine PPv2 custom effect (`PsxFilter.cs` + `PsxFilter.shader`), added to
`ShaderStack` as its own toggle outside the preset ladder, same reasoning as motion blur already
uses - a vibe, not a quality tier. Scoped after research (PS1-shader writeups, PPv2's own
`PropertySheet`-based custom-effect pattern - the actual `BlitFullscreenTriangle` overload takes a
`PropertySheet`, not a raw `Material`, which the first pass got wrong and the compile check caught)
to what a single screen-space pass can actually do safely: 5-bit-per-channel colour quantization
with an ordered 4x4 dither, fixed at a tasteful 0.6 intensity rather than exposed as a slider nobody
asked for. Deliberately does not attempt vertex snapping or affine texture warping - both are
per-material vertex-shader techniques that would mean touching every shader already in the project
(the banana materials, the skybox, `ScreenOutline` itself) to apply safely, not something one
post-process pass can add. Compiles clean and passes the full suite; not yet screenshotted with the
toggle on specifically - worth a look in a real session before calling the *look* of it finished,
same as every other visual thing on the Unverified list below.

## Main menu

Reviewed rather than re-themed. No functional bug turned up in `Menu.cs`/`MenuManager.cs` by either
review agent, and this project's own docs mark the menu's visual language as Ryaan's own repeatedly
and specifically (M5: "the gameplay HUD only - menus are Ryaan's own"; the font section: "menus
stay on Helvetica Punk/Chomsky for now, per Ryaan's own request not to touch that side yet") - a
stronger, more specific, more repeated signal than this pass's own general permission to go wide.
What this pass *did* improve there: `ModeSelector` and `ColourPicker`'s new `SceneCheck` coverage
(above) catches a dropped reference on either before it ships silently broken, and the mode-info
refactor means `ModeSelector.Apply` no longer hand-lists every mode's name/description in a ternary
that would need editing per new mode.

## Verified

Batch-mode compile checks throughout, then the full seven-suite check (`WeaponCheck`,
`PlayerModelCheck` via the others, `RemoteCopyCheck`, `SceneCheck`, `MatchCheck`, `AudioCheck`,
`PlayModeProbe`) at the end - all pass, zero regressions from any of the above. `SceneCheck`
specifically re-run five times over the course of this pass while chasing the TagManager format
issue, each run a real data point rather than a guess.

# Thirtieth pass — the tab scoreboard, rebuilt the same way, 2026-09-03

Same request shape as the twenty-ninth pass's process, aimed at one screen: "we need a better
scoreboard (the tab one)." Read the actual current code first rather than guessing what was wrong
with it (`Scoreboard.cs`, `ScoreboardItem.cs`), researched what makes a competitive-shooter
scoreboard and this game's own cited reference (ULTRAKILL's letter-grade/style-rank identity, which
`StyleScore`'s rank names already borrow) actually work, proposed six concrete changes, got a plain
"do all of these."

## What was actually wrong

Reading the code rather than assuming: rows were added in Photon join order and never re-sorted -
no re-sort call existed anywhere. It showed kills/deaths, which stopped being the win condition for
Deathmatch and Team Deathmatch once the earlier style-score rework landed - the live scoreboard
never caught up to that. Nothing from `StyleScore`, killstreaks or multikills showed anywhere
despite all of it already being tracked. Team Deathmatch got individually name-tinted rows with no
team grouping or team total. And it never went through the HUD's own visual overhaul - still plain
text while `GameHud.UpdateStandings` (the post-match version of basically the same information)
had already been reworked twice this project's history.

## A real bug found while wiring the team total

Needed a "current team total" for the new team-header row, which `PlayerColours.TeamScore(int)`
already computes - except it always summed kills, unconditionally. That's the exact same stat the
style-score rework was supposed to retire as Team Deathmatch's win condition ("this is now the
determining factor for winning in deathmatches - anything that isn't gungame honestly"), which
means `MatchState.FinishMatch` had been deciding Team Deathmatch by kills the whole time that rework
was live, silently disagreeing with what the individual scoreboard rows were already ranked by.
`roadmap.md`'s own M3 section still said "more kills" too - stale documentation from before the
rework, never caught because nothing had gone looking at `TeamScore` specifically until this pass
needed it for something else. Fixed to read `MatchModeInfo.RanksByStyleScore` like everything else
switched to this session, so the team total, the scoreboard order and the actual match winner now
all agree on which stat is real.

## The rebuild

`ScoreboardItem.cs` (one MonoBehaviour per player, three separate `TMP_Text` fields) retired
outright in favour of the same shape `GameHud.UpdateStandings` already proved out for the post-match
screen: one pooled `TMP_Text` per row, rebuilt fresh on every refresh rather than tracked per-player.
That shape is what makes the rest of this cheap - sorting is "build the list in the right order,"
and a team header is "insert one more row before the block," rather than either needing its own
bespoke mechanism.

- **Sorted for real** - `RankStat` reads `MatchModeInfo` (style score, kills, or ladder rung with
  rung-kills as a tiebreak) instead of leaving rows in join order.
- **1st-3rd medal-coloured** - `RankDisplay.cs`, new, pulled the gold/silver/bronze arrays straight
  out of `GameHud.UpdateStandings` rather than growing a second copy of them; `UpdateStandings`
  itself now calls into it too.
- **A score-based rank name** - `StyleScore.RankForScore(int)`, new, alongside the existing
  `RankFor(float)`. The live HUD's rank name is driven by the *multiplier* (a fast-decaying combat
  stat that's never replicated to other clients, on purpose - it's local combat feel, not something
  worth a property write every frame), so a scoreboard showing *other* players can't reuse it
  directly. Same five tier names, a new threshold table scaled to the banked, replicated
  `StyleScoreKey` total instead - anchored against `GameHud.RewardScoreScale` (2500), the one number
  this game's own economy already treats as "a genuinely good match."
- **Team grouping with a live total** - two blocks under a coloured team header
  (`PlayerColours.TeamNames[team]` plus the now-fixed `TeamScore`) in a team mode, found via
  `MatchModeInfo.UsesTeams` rather than a `MatchMode.TeamDeathmatch` check growing yet another call
  site.
- **A best-streak flourish** - `RoomManager.BestStreakKey`, already replicated for the post-match
  "ON A ROLL" award, shown inline past a 3-streak threshold. Deliberately not the live, current-run
  `PlayerController.Killstreak`/`Multikill` - both are incremented locally on the killer's own
  client only (`RewardKill()`), never networked, so reading another player's copy of either would
  show stale or wrong numbers depending on whose client is asking. Caught by checking how they're
  set before wiring them in, not after something looked wrong in a screenshot.
- **Every row tinted the player's own colour** (`PlayerColours.For`) rather than plain white,
  matching how the kill feed and crosshair-picker swatches already use that same association; the
  local player's own row is bolded on top of that instead of recoloured, so it's distinguishable
  even sitting next to a same-coloured teammate.
- **A real visual pass** - `ScoreboardBuilder.cs`, new (the scoreboard never had a builder before
  this pass). Same outline-plus-underlay treatment `HudBuilder.Text` gives every HUD label,
  duplicated rather than shared - extracting `HudBuilder`'s private helpers into something both
  tools could call would mean restructuring the single most hand-edited, highest-history file in
  the project for a styling helper, which wasn't worth the risk to a file nothing about this task
  required touching. Unlike `HudBuilder`, nothing here was hand-styled to protect, so `Run()`
  rebuilds the whole thing outright rather than needing its own narrower `Repair()`.

`ScoreboardItem.prefab` deleted along with the script; `FontRetarget.cs`'s hardcoded prefab list
(one more small duplication - a prefab path list a second tool also had to know about) updated to
stop pointing at it.

## Verified

Compile check, then `SceneCheck` (every wired reference present) and `PlayModeProbe` (builds and
refreshes without throwing against a real offline-mode spawn) both pass, plus `MatchCheck` since
`MatchState.cs` and `PlayerColours.cs` both changed. What none of that reaches: offline mode is one
player, so the actual sort order, the team-header block and the medal colours have only been reasoned
through and read back as text, never seen rendered with more than one person on the board - flagged
honestly in `roadmap.md`'s Unverified list rather than claimed as looked-at.

## Reported back: not visible enough

Exactly the gap the Unverified note above admitted to - reported directly as "cheeks," text too
small, no real backdrop separating it from the game behind it, CS2 named as the reference. Fixed in
`ScoreboardBuilder.cs`: row text 30pt -> 46pt, and the backdrop rebuilt as an actual bounded card
(1040x660, opaque, a lighter header bar across its own top) instead of a screen-wide translucent
wash. The opacity choice specifically isn't a fresh guess - `CrateShopBuilder`'s own backdrop hit
this exact failure mode in the twenty-sixth pass (95% alpha reporting back as "almost fully
see-through"), so this one goes straight to fully opaque rather than re-discovering the same lesson
a second time.

Still can't screenshot it to confirm - `ScoreboardCanvas` is Screen Space Overlay, same as
`GameHud`'s, and overlay canvases don't render into anything `HudPhotographer`'s camera-to-texture
technique can capture. Verified what a check can reach (compiles, `SceneCheck` passes, the header
bar and row column don't overlap by the actual numbers) and no further - a real look in a real
session is the only way left to confirm this one landed, same as the PSX filter above.

## Reported back again: the text still wasn't on the backdrop, and PSX "does nothing"

Both turned out to be real, and both got a real check built rather than a third guess.

**PSX filter**: not actually broken - `PlayModeProbe`'s new before/after pixel comparison (see
below) found a genuine 1.55-per-channel average difference at the shipped 0.6 intensity, just too
small to notice. The intensity-to-colour-depth curve is a straight lerp, so 0.6 only bought 60% of
the way from "off" toward "strong"; raised to 0.88 and re-checked the same way (now 2.66 average
difference). Good example of the two failure modes looking identical from the outside - "does
nothing" and "does something too subtly to see" need different fixes, and only measuring which one
it actually was stopped this from turning into a third blind guess.

**The scoreboard**: a real, confirmed layout bug, not a rendering-technique dead end. `Scoreboard.
cs` gained two read-only accessors (`ContainerRect`/`BackdropRect`) and a static `OpenOverride` (the
same shape `PlayerController.AimInputOverride` already uses for input-less batch tests), and
`PlayModeProbe` gained `CheckScoreboardLayout` - forces the board open, waits a few real frames, and
reads `RectTransform.GetWorldCorners()` off both the backdrop and the row column, since an overlay
canvas can't be screenshotted but its actual laid-out positions can absolutely be read back
numerically. First run of the check failed immediately and named the exact problem: the row column
sat entirely *below* the backdrop, not inside it - `Backdrop` and `Rows` had been built as two
siblings with independently hand-computed offsets that were supposed to land one inside the other
by arithmetic alone, and something in that arithmetic (never fully isolated - not worth the time
against just fixing the structure) put them adjacent instead of overlapping. Rebuilt so `Rows` is a
literal child of `Backdrop`, stretch-anchored to fill it minus margins for the header - "inside the
backdrop" is now true by construction, not by two numbers agreeing. Re-ran the same check: passes,
with real corner coordinates confirming the row text sits inside the card this time.

`CheckScoreboardLayout` and the PSX before/after comparison are both permanent additions to
`PlayModeProbe` now, not one-off diagnostics - the first real, general technique this project has
for catching an overlay-canvas layout bug without a human looking at it, and worth reaching for
again the next time a UI panel's "is this actually inside that" claim needs checking rather than
trusting.

## Reference images landed: real pixelation, real columns, a goofy Bigfoot

Five reference shots followed the previous report - a CS2 scoreboard, and four PS1-era shots (a
PS1 demake, a period FPS) dominated by something the colour-only PSX pass never touched: real
internal resolution, not full-resolution colour banding. Superseded the earlier "not too grainy or
pixelated" brief with what the pictures actually showed rather than defending the old, subtler
reading of a text description against harder evidence.

**PSX filter**: `PsxFilterRenderer.Render` now renders to a quarter-resolution `RenderTexture`
(`FilterMode.Point`, the actual pixelation step) and upscales that to the screen, running the
existing quantize-and-dither shader *during* the downscale so the dither pattern survives into the
blocky result instead of being blurred back out by a naive shrink. Re-checked with the same
before/after pixel comparison each step: 1.55 -> 2.66 (raising intensity to 0.88) -> 3.39/4.38
(adding the resolution pass) average per-channel difference - real, monotonic, measured, not
asserted.

**The scoreboard**: rebuilt as an actual table - Rank/Name/Primary/Secondary/Streak, five
fixed-width columns (`ScoreboardBuilder.BuildRow`) instead of one rich-text line, so stats line up
regardless of name length the way CS2's own Money/K/A/D/Score columns do. Explicitly not a copy -
adapted to this game's own fields: no money or MVP stars, "Primary" reads style score (with its
rank name) or gun-game ladder progress depending on the mode, K/D stays visible always since it
still means something everywhere, still styled in the HUD's own outline-and-underlay language
rather than CS2's. A column-header row (NAME / SCORE-or-LADDER / K/D / STREAK) is now row zero,
built by the same pooling path as every other row rather than a separate hand-placed element.
`Scoreboard.cs`'s row-filling logic and `PlayModeProbe.CheckScoreboardLayout` both updated for the
new per-column structure; re-run and confirmed still landing inside the backdrop.

**Bigfoot**: "make it goofy" landed as `BigfootSighting.cs` - an occasional (30-75s), brief (4s),
purely cosmetic cryptid cameo at a distance, built from the game's own `MonkeyRig` tinted dark
rather than any new art, added mine-only next to `SpeedRush` in `PlayerController.Start` and kept
local-only the same way several other personal flourishes in this project already are. `GameSettings.
BelieveInBigfoot`'s doc comment updated - it's the one joke setting that's no longer a joke.

Verified: compile check, `SceneCheck`, and the full `PlayModeProbe` suite including both new
checks above - all pass.

## The gap wasn't spacing, it was a layout group fighting itself

A real screenshot arrived mid-report this time - the column header sitting near the top, the one
player row stranded far below it, most of the card empty in between. Reported as "the spacing is a
bit off," but the actual mechanism was a bug, not a number: each row's own `HorizontalLayoutGroup`
(arranging its five columns) had `childForceExpandHeight = true`, which also makes the row itself
report to the *outer* vertical list that it wants to expand - so the list handed each row a share
of all the leftover vertical space instead of packing them together, and each row's text just sat
centred inside its own now-oversized rect. Set to `false`, with an explicit `flexibleHeight = 0` on
each row's own `LayoutElement` so there's no ambiguity left for anything to fall back on. Re-checked
with `PlayModeProbe.CheckScoreboardLayout`: the first player row now sits directly under the header
instead of near the bottom of the card.

Found while in there and fixed alongside it: the card was sized for however many rows happened to
be tested, not the real worst case - a full 8-player Team Deathmatch lobby is the column header
plus two team headers plus eight player rows, 11 total, which the original height never actually
had room for. Grown to fit that properly rather than the number that happened to look fine with
one player in a solo test.

Also this pass: backdrop opacity 1.0 -> 0.9 (some transparency back, nowhere near the 0.72/0.95
range that's caused "not really visible"/"almost see-through" reports twice already), row and
column spacing widened a second time now that the layout bug behind the first complaint is actually
fixed rather than papered over.

**PSX filter now on by default** - `GameSettings.PsxFilter`'s default flipped `false` -> `true`,
both the property initializer and the `Load()` fallback. Broke an existing check in the process:
`PlayModeProbe`'s "the preset is the only thing driving the picture" test assumed the Off preset
meant literally zero post-processing, which stopped being true the moment an independent toggle
defaulted to on. Not a check to tolerate failing or delete - re-pointed to pin motion blur and the
PSX filter off specifically while it isolates what the *preset* does, which is what it was actually
trying to verify all along.

**Bigfoot reverted** - `BigfootSighting.cs` removed, `PlayerController.Start`'s wiring reverted,
`GameSettings.BelieveInBigfoot`'s doc comment back to describing a deliberately inert joke setting.
"Remove for now" rather than a rejection of the idea - the implementation is preserved in the
previous pass's entry above and in git history if it comes back.

Verified: compile check, `SceneCheck`, and the full `PlayModeProbe` suite (including the re-pointed
preset-isolation check) - all pass.
