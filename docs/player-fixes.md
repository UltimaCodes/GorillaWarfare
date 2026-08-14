# Player fixes + the plan

Notes from picking this project back up in Aug 2026. Unity 6000.2.9f1, built-in RP, Photon PUN 2.

The goal is just to make it fun to play with friends. Game feel and working netcode first, art and
polish after.

## The plan

Broke the wishlist into chunks so I'm not doing nine things at once:

- **A1 — player correctness** (this doc): late-join visibility bug, networked aim, Photon settings
- **A2 — player feel**: Quake 3 movement, a model that can actually animate
- **B — game feel**: gun sounds, footsteps, hit feedback, impact FX, music
- **C — front end**: UI restyle, settings menu
- **D — maps**: a real first map, map selection, spawn system
- **E — art**: replace the placeholder textures and materials

### Staying on Photon

Looked hard at ripping out PUN for "real" P2P and decided against it.

PUN is already peer-hosted — the master client owns the game state. Photon does matchmaking and
relay, it's not a game server. Actual P2P over the internet still needs NAT punchthrough from
somewhere: Steam ($100 one-off), EOS (free but everyone needs an Epic account), or a relay. So the
migration buys very little and would eat all the time I have.

Not revisiting unless PUN's limits actually start hurting.

## What was wrong

Found by reading the code, not guessing:

**Pitch was never networked.** `Look()` puts yaw on the root, which `PhotonTransformView`
replicates. Pitch goes on `cameraHolder`, which has *no components on it at all*. So nobody ever saw
anyone aim up or down. This is the "model doesn't move with the camera" thing.

**Region hard-pinned to `uae`** in `PhotonServerSettings`, forcing everyone onto that cluster
regardless of where they are.

**Serializing at 10/sec.** `sendFrequency = 33`ms (~30Hz) but `serializationFrequency = 100`ms, so
remote positions and aim only updated ten times a second and got interpolated between. Probably a
big chunk of why remote players look bad.

**The monkey can't be animated.** `CHIMP_L.3DS` — `.3DS` has no skeleton and no skin weights.
`chimpan` is a plain `MeshRenderer`, not a `SkinnedMeshRenderer`, and there's no Animator or single
`.anim` in the project. So "animate the monkey" and "replace the bad model" are the same job. That's
A2's problem, noting it here so I don't waste time looking for a broken Animator.

**Junk on the prefab.** A point light imported from the `.3DS` (intensity 271, range 0 — lit nothing,
still processed every frame). Root `MeshFilter`/`MeshRenderer` are leftovers but already disabled.

Sync mode was fine, by the way — I initially thought it was `ReliableDeltaCompressed`, but
`Enums.cs:69` is `{ Off, ReliableDeltaCompressed, Unreliable, UnreliableOnChange }` so the stored `3`
is `UnreliableOnChange`, which is correct. Worth knowing that under that mode **a stationary player
sends nothing at all**, which matters for reasoning about what a late joiner does and doesn't get.

## Networking the aim

Three options:

- Stick a `PhotonView` + `PhotonTransformView` on `cameraHolder`. No code, but that's a 4th
  PhotonView per player, burning view IDs on every respawn, sending a full position+rotation to
  carry one number.
- **`IPunObservable` on `PlayerController`, observed by the root view.** ~15 lines, sends one float,
  no extra view, and gives somewhere to interpolate. Went with this.
- Custom properties or an RPC — wrong tool, those are for discrete events.

Remote pitch gets lerped rather than snapped since serialization is only 20/sec.

Right now it drives `cameraHolder`, which also aims the gun since `ItemHolder` is parented under it.
**In A2 the same float drives a spine bone instead** — treating it as "one networked float" rather
than "a synced transform" is what makes that swap trivial.

One gotcha worth writing down: the root view has `observableSearch: AutoFindAll`, but
`PhotonView.Awake` early-returns when `ViewID != 0`, which is always true for anything created by
`PhotonNetwork.Instantiate`. So `FindObservables` **never runs at runtime** and the serialized list
is what actually ships — the prefab has to be edited by hand. Order matters too, so I put
`PlayerController` before `PhotonTransformView` to match root component order, in case the editor
ever regenerates it.

## The late-join visibility bug — STILL OPEN

Symptom: whoever makes the room never sees anyone who joins after them, can't shoot them, and never
gets them on the scoreboard. But they move around fine and *are* seen and shot by the joiners. Stacks
with every new join — everyone sees the people who were already there, nobody sees anyone who arrives
later.

The scoreboard is the tell. `Scoreboard.OnPlayerEnteredRoom` is a plain Photon callback with nothing
to do with spawning, so if that never fires, **the problem is inbound events generally, not
Instantiate**.

Which also means the late joiners are probably just as broken and only look fine —
`Scoreboard.Start()` reads `PlayerList` once at join, and buffered spawns get replayed on join, so
everything they know could be from that one snapshot. Testable prediction: **earlier players should
look frozen at their spawn points**.

Ruled out so far:

- Camera destroy taking the model with it — `CHIMP_L` is a *sibling* of `CameraHolder`, not a child
- Wrong build index — Menu is 0, Game is 1, the check is right
- Managers in the wrong scenes — `RoomManager`/`Launcher` in Menu, `SpawnManager` in Game, all fine
- The lobby player list — confirmed the failure is in-game
- `PhotonTransformView` dumping remote players at the origin — `m_Distance` starts at 0 so the
  `MoveTowards` is a no-op before the first packet, and `m_firstTake` snaps to the received position.
  It's well behaved.

Fixed one real ordering problem that might be it: `RoomManager` subscribes to `sceneLoaded` from
`OnEnable` in the menu scene, but `PhotonHandler` subscribes in `Start()`, which only happens once
`Launcher.Start()` calls `ConnectUsingSettings()`. Unity fires handlers in subscription order, so
ours ran first and raised the spawn *before* PUN could undo the `IsMessageQueueRunning = false` that
`LoadLevel` set. Now deferred until the queue is actually running.

Not convinced that's the whole story though — the `IsMessageQueueRunning` setter is a plain field
assignment with no side effects, so a queued event should still flush. Needs a live test.

`NetworkDebugOverlay` (F3) puts the message queue flag and live callback counters on screen. If
`MESSAGE QUEUE RUNNING` is False on the room creator, that's it. If it's True but `entered=0`, it's
callback registration instead, which is a different fix.

## Risks

- **Unpinning the region changes matchmaking.** Players on different auto-picked regions can't see
  each other's rooms. If we end up geographically split, one explicit shared region is better than
  auto.
- PUN 2 is EOL. Accepted, revisit if it bites.

## Open

- Where is everyone actually located? Decides whether unpinning was right or whether it should just
  point at a different fixed region.
