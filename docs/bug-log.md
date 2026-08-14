# Bug log

Everything found going through the scripts before starting on movement and new features.
Skipped the movement code on purpose (`PlayerController.Move/Look/Jump/FixedUpdate`,
`PlayerGroundCheck`) since it's all getting replaced by the Quake 3 controller anyway.

36 things total. Grouped roughly by how bad they were.

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
float on the root view. Reasoning for not just slapping a `PhotonView` on `cameraHolder` is in
[player-fixes.md](player-fixes.md).

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
  [player-fixes.md](player-fixes.md).)
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

**The late-join visibility bug** — still open, see [player-fixes.md](player-fixes.md).
