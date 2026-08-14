# A1 — Player Correctness Pass

**Date:** 2026-08-14
**Status:** Design, awaiting approval
**Project:** GorillaWarfare (Unity 6000.2.9f1, Built-in RP, Photon PUN 2)

## Context

The project is a multiplayer FPS built ~2 years ago from a Photon PUN tutorial skeleton, with
custom art layered on top. It has not been touched since 2024-11-17. The owner's goal is to make
it **genuinely fun to play with friends** — game feel and working netcode lead; art and polish
follow.

The full wishlist was decomposed into five sub-projects:

| # | Sub-project | Contains |
|---|-------------|----------|
| **A1** | **Player correctness** (this doc) | Late-join spawn bug, networked pitch, Photon sync/region, prefab cleanup |
| **A2** | Player feel | Quake 3 / CPM movement, rigged + animated model |
| **B** | Game feel | Weapon sounds, footsteps, hit feedback, impact FX, music |
| **C** | Front end | UI restyle + colour scheme, settings menu |
| **D** | Maps | Real first map, map selection, spawn system |
| **E** | Art pass | Replace placeholder textures/materials, environment art |

**A decision already taken:** migrating off Photon was considered and **rejected**. PUN is already
peer-hosted (the master client owns the game state); Photon supplies matchmaking and relay, not a
game server. Replacing it with true P2P still requires NAT traversal from Steam, EOS, or a relay,
and the migration would have consumed the entire budget without making the game more fun. We stay
on PUN and optimise it instead.

## Goals

1. A late joiner's character is correctly visible, at the correct position, to everyone.
2. Remote players can see where a player is aiming vertically.
3. Player transform replication uses an appropriate delivery mode and region.
4. Dead weight removed from the player prefab.

## Non-goals

Movement feel, the rig and animation, audio, UI, settings, maps, and textures are all explicitly
**out**. Problems found in those areas get written down, not fixed. This pass must land small.

## Confirmed findings

Established by reading the project files, not assumed:

- **Pitch is never networked.** `PlayerController.Look()` puts yaw on the root transform and pitch
  on the `cameraHolder` child. The prefab has three `PhotonView`s; only the root observes a
  `PhotonTransformView`. `CameraHolder` has **no components at all**, so vertical aim is never
  transmitted. This is the reported "model doesn't move with the camera".
- **Sync mode is already correct — an earlier claim in this spec was wrong.** All three
  `PhotonView`s use `Synchronization: 3`. `Enums.cs:69` defines
  `enum ViewSynchronization { Off, ReliableDeltaCompressed, Unreliable, UnreliableOnChange }`,
  so index 3 is **UnreliableOnChange**, which is the right mode for a player transform. The
  original claim that this was ReliableDeltaCompressed (index 1) was an off-by-position error.
  **No change required.** Noted because the wrong version was briefly acted on.
  One real consequence does follow: under `UnreliableOnChange` a **stationary player transmits
  nothing at all**, which matters when reasoning about what a late joiner does and does not
  receive.
- **The region is hard-pinned:** `FixedRegion: uae` in `PhotonServerSettings`. Every player is
  forced to the UAE cluster regardless of where they are.
- **The player mesh cannot be animated.** `CHIMP_L.3DS` is a `.3DS` file — a format with no
  skeleton and no skin weights. The prefab confirms it: `chimpan` is a plain `MeshRenderer`, not a
  `SkinnedMeshRenderer`. There is no Animator and not one `.anim` or `.controller` in the project.
  (A2's problem, recorded here as the reason the T-pose is not a bug.)
- **Prefab junk:** the root object carries its own `MeshFilter` + `MeshRenderer` (a leftover
  primitive rendering inside the chimp), and `CHIMP_L` carries a stray `Light` component imported
  from the `.3DS`.

### Ruled out for the visibility bug

- *Destroying the camera also destroys the model* — `CHIMP_L` is a **sibling** of `CameraHolder`,
  not a child. `Destroy(GetComponentInChildren<Camera>().gameObject)` cannot touch it.
- *Wrong build index* — `Menu` = 0, `Game` = 1. The `scene.buildIndex == 1` check is correct.
- *Managers in the wrong scenes* — `RoomManager` and `Launcher` are in Menu, `SpawnManager` is in
  Game. All correct.
- *It is the lobby player list* — confirmed with the owner: the failure is **in-game**, others
  cannot see the late joiner's character. `Launcher.OnJoinedRoom` is not implicated.

## Design

### D1. Networked pitch

Three options were considered:

| Approach | Cost | Verdict |
|---|---|---|
| `PhotonView` + `PhotonTransformView` on `CameraHolder` | No code, but a 4th PhotonView per player, ViewIDs burned on every respawn, and a full position+rotation sent to carry one number | Rejected — wasteful |
| **`IPunObservable` on `PlayerController`, observed by the existing root view** | ~15 lines; sends **one float**; no new PhotonView; gives an interpolation hook | **Chosen** |
| Custom properties / RPC | Built for discrete events, not continuous per-frame data | Rejected |

`PlayerController` implements `OnPhotonSerializeView`, writing `verticalLookRotation` when
`stream.IsWriting` and reading into a target field otherwise. It is added to the root
`PhotonView`'s `ObservedComponents` alongside `PhotonTransformView`. Remote pitch is **lerped**
toward the received value rather than snapped, so it reads smoothly at a 10 Hz send rate.

On remote players the value drives `CameraHolder`, which also aims the weapon correctly since
`ItemHolder` is parented under it. **In A2 the same float will drive a spine bone instead.**
Modelling this as "one networked float" rather than "a synced transform" is precisely what makes
that swap a one-line change.

### D2. The late-join visibility bug — diagnosis before fix

No fix is designed for an unconfirmed cause. Surviving hypotheses, ranked (the four listed under
"Ruled out" above were eliminated by inspection and are not restated here):

- **H1 — the character is mislocated, not unrendered. DOWNGRADED after reading the source.**
  The original theory was that `PhotonTransformView` lerps a remote object toward an
  uninitialised network position, parking it at world origin. Reading
  `PhotonTransformView.cs` shows the component is well-behaved: `Awake` sets
  `m_NetworkPosition = Vector3.zero` **but** `m_Distance` defaults to `0`, so the `MoveTowards`
  in `Update` is a no-op before the first packet, and on first receipt the `m_firstTake` branch
  **snaps** `transform.position` to the received value and only then starts interpolating. The
  object therefore holds its instantiate position until real data arrives. This hypothesis is
  not supported by the code and is retained only because the diagnostic logging tests it for
  free.
- **H2 — unguarded network lookup.** `PlayerController.Awake` calls
  `PhotonView.Find((int)PV.InstantiationData[0]).GetComponent<PlayerManager>()` with no null
  check. If that view is not yet registered on a given client it throws, `Awake` aborts partway,
  and the object comes up half-initialised. **Regardless of whether this is the bug, it is an
  unguarded lookup on a network race and will be given a guard.**
- **H3 — buffered-instantiate ordering** versus PUN's message queue during the
  `AutomaticallySyncScene` load.

**Method:** two instances, structured logging on the instantiate path recording *where each remote
object actually is* (not merely whether it rendered), reproduce, read the logs, fix the confirmed
cause. Explicitly not "apply all three and hope".

### D3. Photon optimisation

- Player transform `Synchronization` → **`Unreliable On Change`**.
- **Unpin `FixedRegion`**, letting PUN select by ping. No effect if everyone is in the Gulf; a
  large latency win for a one-line change otherwise.
- **Raise `SerializationRate`.** Verified from `PhotonNetwork.cs`: `sendFrequency = 33` ms
  (SendRate ≈ 30 Hz) but `serializationFrequency = 100` ms — so `OnPhotonSerialize` fires only
  **10×/second**. Every remote player's position and pitch updates ten times a second and is
  interpolated between. That is a plausible contributor to choppy remote movement independent of
  any bug. Raise `SerializationRate` toward 20–30 Hz and measure; `SendRate` must stay at or above
  it, since serialised updates are queued and flushed on send.

### D4. Prefab cleanup

Remove the root `MeshFilter` + `MeshRenderer` and the stray `Light` on `CHIMP_L`.

## Interaction with A2 — important

The chosen movement reference is
[IsaiahKelly/quake3-movement-for-unity](https://github.com/IsaiahKelly/quake3-movement-for-unity)
(Unlicense / public domain). `Q3PlayerController` is declared
`[RequireComponent(typeof(CharacterController))]`, with tunables `m_Friction = 6`,
`m_Gravity = 20`, `m_JumpForce = 8`, `m_AirControl = 0.3f`, and an `m_AutoBunnyHop` toggle.

The current player is a **`Rigidbody`**. These are not interchangeable. When A2 makes the swap, the
following are **deleted outright**:

- the `Rigidbody` and the root `CapsuleCollider`
- **all of `PlayerGroundCheck.cs`** (64 lines of overlapping `OnTrigger*`/`OnCollision*` handlers)
  — `CharacterController` exposes `.isGrounded` natively
- `rb.MovePosition` in `FixedUpdate`, replaced by `m_Character.Move()` in `Update`

**Consequences for A1:**

1. Spend **zero** effort improving the `Rigidbody` path or `PlayerGroundCheck`. They are dead code
   walking.
2. The `IPunObservable` pitch sync is unaffected by the swap — it sends a float and does not care
   what moves the capsule. Confirms D1 as the right choice.
3. `PhotonTransformView` is unaffected; it reads and writes the transform either way.
4. If the visibility bug's root cause turns out to be Rigidbody- or physics-specific, check whether
   it survives the `CharacterController` swap before investing in a fix.

## Verification

- **Compile:** Unity 6000.2.9f1 is installed locally, so batch-mode compile checks run from the CLI
  after each change without the owner alt-tabbing.
- **Behaviour:** network behaviour requires a genuine two-instance test — owner-driven.
- **Granularity:** one commit per fix, so any single change can be reverted independently.
- No success is claimed for any item until it has been observed working in a two-instance run.

## Risks

- **The leading hypothesis may be wrong.** Mitigated by diagnosing before fixing, and by the
  logging capturing position rather than visibility.
- **Region unpinning changes matchmaking.** Players on different auto-selected regions will not see
  each other's rooms. If the group is geographically split, a single explicit shared region is
  better than auto — decide once we know where players actually are.
- **PUN 2 is end-of-life.** Accepted deliberately; revisit only if its limits bite.

## Open questions

- Where are the players actually located? Determines whether unpinning the region is correct, or
  whether it should simply be repointed at a different fixed region.
