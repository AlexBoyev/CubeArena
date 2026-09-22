# Cube Arena — Netcode design

Covers movement replication for Phase 5, per the brief's section 6 requirement
to document the client prediction/reconciliation approach.

## Authority model

The server is the sole authority over every player's position. The client
never sends a position — only input intent (`Vector2` direction, WASD-derived,
clamped to unit length), via `PlayerController.SubmitInputServerRpc`. The
server integrates that input at a fixed 30Hz tick (`Time.fixedDeltaTime` is
set to `1/30` in `ServerBootstrap`) using `CharacterController.Move()`, and
writes the result to a server-owned `NetworkVariable<Vector3>` (`_serverPosition`)
that replicates to every client.

## Owning client: prediction + reconciliation

Waiting for a server round-trip on every keypress would feel laggy, so the
owning client also runs the *same* movement math locally, immediately, against
its own `CharacterController` — this is the "prediction" half.

Each frame, after applying predicted movement, the client compares its
predicted position to the last known `_serverPosition` value:

- **Small drift** (< 2m): blended in gradually (`Time.deltaTime * 5`), so
  corrections aren't visible as jitter.
- **Large drift** (≥ 2m — e.g. right after spawning, or after a long stall):
  snapped instantly, since a slow blend from far away would look worse than a
  cut.

**What this deliberately does not do**, compared to a fuller implementation:
there's no input history buffer or replay. A "textbook" reconciliation client
tags each input with a sequence number, keeps a rolling buffer of unacknowledged
inputs, and — when a server correction arrives — snaps to the corrected
position and *replays* every input since that sequence number to recompute
the predicted position without a visible pop. This project's simpler
predict-then-blend approach was chosen because:

- The arena is small and speeds are low (5 m/s), so prediction error from a
  single tick of latency is on the order of centimeters, not meters — the
  soft-blend already hides it.
- A replay buffer adds real complexity (sequence numbers on the RPC, an input
  ring buffer, re-simulating N ticks on every correction) for a prototype
  where "four clients move around and see each other" is the actual bar, not
  competitive-shooter-grade responsiveness.

If movement feel becomes a problem at higher latency, replacing the blend in
`PlayerController.PredictAndReconcile` with sequence-numbered replay is the
natural next step — the server side (authoritative tick + `_serverPosition`)
doesn't need to change at all.

## Remote players (non-owner clients)

Every client that isn't the owner of a given `PlayerController` just
interpolates: `transform.position` is `Lerp`'d toward `_serverPosition.Value`
every frame (`Time.deltaTime * 10`). No prediction is needed here since the
local player never acts on a remote player's position.

## Why not NGO's built-in `NetworkTransform`

NGO ships a `NetworkTransform` component that already handles interpolation
and basic client-authority modes. It was not used here because the brief
specifically asks for *documented* prediction and reconciliation on the
owning client, which is easier to reason about (and explain) as an explicit,
small, hand-written state machine than as configuration on a built-in
component whose interpolation/extrapolation internals aren't part of this
codebase.

## Physics props: crates (`NetworkTransform` + `NetworkRigidbody`)

Player movement (above) stays on the hand-rolled `NetworkVariable` +
predict/reconcile path — that decision doesn't change. Pushable/grabbable/
throwable physics crates (`CrateController.cs`, 30 of them in the load test
below) are a different problem: an object any of up to 6 players can shove,
pick up, carry, and throw, with real `Rigidbody` collision response against
each other and the arena, needs *someone's* physics simulation to be the
source of truth for its transform at any given moment, and that someone
changes constantly (server while at rest, whichever player is holding it).
Hand-rolling that hand-off with our own `NetworkVariable<Vector3>` would mean
reimplementing exactly what NGO's `NetworkTransform`/`NetworkRigidbody`
already do — interpolation, delta compression, and (critically) an
`AuthorityMode` that can move between server and owner — so crates use those
built-in components instead, rather than duplicating them by hand.

### Authority model

Every crate (`CrateController.CreateTemplate`) carries:

- A `Rigidbody` (mass 5, some linear/angular damping so pushed crates settle
  instead of sliding forever).
- A `NetworkTransform` with `AuthorityMode = NetworkTransform.AuthorityModes.Owner`
  and `Interpolate = true`, set once at creation and never changed.
- A `NetworkRigidbody`, which — paired with that `NetworkTransform` — auto-
  matically flips `Rigidbody.isKinematic` to match whoever currently has
  authority: kinematic (script-driven, no local physics response) on every
  non-authoritative peer, fully dynamic (real physics) on the authoritative
  one.

`AuthorityMode` itself is fixed at `Owner` forever — what actually moves is
*who the owner is*, via `NetworkObject.ChangeOwnership()`:

- **At rest / pushable**: owned by the server (`NetworkManager.ServerClientId`).
  The server's `Rigidbody` is the dynamic one, so pushes and crate-on-crate
  collisions are simulated authoritatively there and replicated out.
- **Grabbed**: `CrateController.ServerGrab(clientId)` transfers ownership to
  the grabbing player. Their `Rigidbody` becomes dynamic; the crate becomes
  kinematic everywhere else, including the server. The holder then drives it
  every `FixedUpdate` via `Rigidbody.MovePosition`/`MoveRotation` toward a
  point in front of their camera — `MovePosition`/`MoveRotation` rather than
  a direct `transform.position =` assignment specifically because the
  Rigidbody is still (locally) non-kinematic while held; teleporting a
  dynamic Rigidbody's transform directly produces jittery collision response,
  where scripting its motion through the physics engine's own mover API
  doesn't.
- **Released / thrown**: `CrateController.ServerRelease(velocity)` transfers
  ownership back to the server and sets `Rigidbody.linearVelocity` to the
  throw's release velocity in the same call, so the crate leaves the throw
  already moving under server-authoritative physics instead of restarting
  from zero velocity.

### Resolved (Milestone 3): multi-carrier loot via permanent server authority

`NetworkObject.ChangeOwnership()` gives an object exactly one owner at a
time, which is what `CrateController`'s "authority moves to whoever's
holding it" pattern above depends on. Pocket Heist's loot-carry mechanic
needs the opposite: 2-5 players gripping the *same* object simultaneously
(see `POCKET_HEIST_MASTER_PROMPT.md` sections 7 and 13) — there's no single
"owner" to hand authority to.

`LootItem` (`Assets/Scripts/Shared/LootItem.cs`) is the resolution:
permanently server-owned, never transferring ownership at all.
`NetworkTransform`/`NetworkRigidbody` stay on the object (unlike a hand-
rolled `NetworkVariable<Vector3>` position — rigidbody sync is still harder
to hand-roll well than a kinematic pose, same reasoning as `CrateController`
originally), just left at their default `AuthorityModes.Server` instead of
`CrateController`'s deliberate `.Owner` override. Grip membership is a
plain server-side `Dictionary<ulong, PlayerController>`; `FixedUpdate`
(server-only) moves the item toward the average position of its current
grippers at `DragSpeed` (below `requiredCarriers`, item stays grounded) or
`CarrySpeed` (at/above `requiredCarriers`, item lifts to `CarryHeight`) —
this stands in for "the server averages grippers' input intent" from the
brief, and naturally handles a carrier under-staffing or disconnecting
(`ServerRemoveGripper` just shrinks the set; the average and therefore the
drag/carry state recompute next tick, no special-casing needed). `CrateController`
itself is left in the tree, unreferenced — see docs/DECISIONS.md.

Verified live: two bots gripped a spawned coin (`grippers=2/2 carrying=True`
in the server log), carried it to the mousehole, and it banked
(`[Bank] ... banked for 10`); a solo-bot run separately confirmed the
under-staffed case (`grippers=1/2 carrying=False`, moving at the tuned
`DragSpeed` rate, not `CarrySpeed`). Full trace in docs/PROGRESS.md's
Milestone 3 section.

### Confirmed gotcha: ownership transfer teleports

`NetworkObject.ChangeOwnership()` forces `NetworkTransform` through a full
teleport/resync (`OnOwnershipChanged` → re-initialization → a state send with
teleport, not interpolation) on both the old and new owner, and flips
kinematic state on both sides in the same moment. In practice this means a
grab or throw is a visible snap, not a smooth hand-off — acceptable for this
prototype's arena scale (crates are close to the player when grabbed, so the
snap is small), but worth knowing if a future pass wants a seamless carry.

### Push / grab / throw flow (historical — CrateController, Milestone F)

This described `CrateController`'s single-holder push/grab/throw flow,
still accurate for that class (left in the tree, unreferenced — see
docs/DECISIONS.md), but no longer reachable from `PlayerController`, which
now drives `LootItem`'s grip/release flow instead (see the "Resolved
(Milestone 3)" entry above and docs/DECISIONS.md's grip-flow entry). Kept
here for historical reference to the crate/bandwidth test it was built for.

- **Push**: `PlayerController.OnControllerColliderHit` (server-only —
  `CharacterController.Move()` doesn't push `Rigidbody`s on its own, so this
  Unity message is the manual hook for it) detected a collision with a
  `CrateController` that wasn't currently held, and applied an impulse along
  the character's move direction. Removed from `PlayerController` in
  Milestone 3 — loot is never pushed, only gripped.
- **Grab**: pressing E with no crate held called
  `PlayerController.RequestGrabServerRpc()`, which found the nearest un-held
  crate within `CrateController.GrabRange` roughly in front of the player and
  called `ServerGrab`. Replaced by `RequestToggleGripServerRpc()`/
  `LootItem.ServerAddGripper`, which supports any number of simultaneous
  grippers instead of exactly one.
- **Throw**: pressing E while holding a crate computed a release velocity
  from camera-forward plus an upward boost and called
  `PlayerController.RequestThrowServerRpc(velocity)`, which called
  `ServerRelease` on the held crate. No equivalent for loot — it's only ever
  carried, never thrown.

Both RPCs defaulted to `RequireOwnership = true` (NGO's default for
`[ServerRpc]`), which is correct here since each player can only ever call
these through their own owned `PlayerController` — no `RequireOwnership =
false` override needed, unlike e.g. `MatchManager`'s vote RPC, which is
called on a `NetworkObject` the caller doesn't own. `RequestToggleGripServerRpc`
keeps the same default for the same reason.

## Bandwidth: 6 bots, 30 crates

### Measuring it

NGO's own metrics (`NetworkManager.NetworkMetrics`) are `internal` and
unreachable from project code. `com.unity.multiplayer.tools` (installed for
this) turned out to be the same story one layer up: its entire `NetStats`
dispatch/observer API is `internal`, and its only public runtime surface,
`RuntimeNetStatsMonitor`, is a UI Toolkit visual overlay with no public
numeric getter — useless in a headless (`-batchmode -nographics`) build with
nothing rendering it.

What *is* public and loggable: Unity Transport 2.6.0's
`UnityTransport.GetNetworkDriver().GetStatistics()`, which returns a
`DriverStatistics` struct with real cumulative byte counts
(`RxTotalBytes`/`TxTotalBytes`) and bandwidth in kbit/s
(`RxBandwidth`/`TxBandwidth`, each with `Current`/`Mean`/`Minimum`/`Maximum`).
`BandwidthLogger.cs` polls this every 5s on both `ClientBootstrap` and
`ServerBootstrap` and writes it to `Debug.Log`, which lands in each process's
log file even fully headless. On a client the driver has exactly one
connection (to the game server), so its numbers *are* that client's own
bandwidth; on the server the driver is shared across every connection, so its
numbers are the aggregate across all connected clients, not a per-client
breakdown.

This gives total bytes/bandwidth per connection, not a *per-object*
breakdown (e.g. "how many bytes does this one crate's `NetworkTransform`
cost"). If that finer-grained view is ever needed, it's still available —
run a single non-headless client (a normal windowed build or the Editor)
with `com.unity.multiplayer.tools`'s Runtime Net Stats Monitor overlay
visible; its UI Toolkit component works fine outside a headless build, it's
only unusable *there*.

### Load test setup

6 headless bot client processes (`CUBEARENA_BOT_MODE=1`, driving
`PlayerController.RunBotBehavior` — the exact same `SubmitInputServerRpc`/
`RequestGrabServerRpc`/`RequestThrowServerRpc` calls real input would make,
just sourced from scripted C# instead of the keyboard, so no OS-level input
injection is involved) plus one dedicated server with
`CUBEARENA_CRATE_COUNT=30`. Bots continuously walk toward, grab, carry, and
throw the nearest unheld crate. Run for several minutes at 6/6 connected
before sampling.

### Bug found and fixed during the first test run

The first load-test pass produced one wildly inconsistent bot (~600 kbit/s
down / ~1700 kbit/s up, vs. ~60/40 kbit/s for the rest). Root cause:
`PlayerController.ReadAndSendInput()` — and so `RunBotBehavior` — runs from
`Update()`, which is uncapped in a `-batchmode -nographics` build (no
`Application.targetFrameRate`, no vsync, nothing to render), and can execute
many thousands of times per second. The real-keyboard path is naturally
throttled by `wasPressedThisFrame` (one keypress = one RPC), but the bot's
grab call had no equivalent edge-detection — it called
`RequestGrabServerRpc()` on *every* `Update()` while in range and unheld,
effectively spamming the server every frame until ownership actually
resolved. Fixed with an explicit `_botGrabRequestCooldown` (0.5s) in
`RunBotBehavior`, giving a grab request time to round-trip before retrying.
All results below are from the *fixed* build; the original run's numbers
(including a bogus "375.5 kbit/s throw-teleport burst" attributed to the
wrong cause) have been discarded rather than reported.

### Results

Per-client (from each bot's own driver — this is genuinely that client's
bandwidth, both directions):

| Bot | Down (current) | Down (session mean) | Up (current) | Up (session mean) |
|---|---|---|---|---|
| 1 | 140.9 kbit/s | 108.2 kbit/s | 50.6 kbit/s | 29.5 kbit/s |
| 2 | 130.1 kbit/s | 103.3 kbit/s | 49.8 kbit/s | 37.2 kbit/s |
| 3 | 140.4 kbit/s | 109.0 kbit/s | 52.8 kbit/s | 37.2 kbit/s |
| 4 | 138.2 kbit/s | 109.4 kbit/s | 48.3 kbit/s | 35.4 kbit/s |
| 5 | 138.0 kbit/s | 108.9 kbit/s | 50.5 kbit/s | 29.3 kbit/s |
| 6 | 138.0 kbit/s | 158.3 kbit/s* | 46.2 kbit/s | 140.2 kbit/s* |

\* bot 6 hit the backend's login rate limiter on the initial launch burst and
had to be relaunched alone a few minutes into the run; its much shorter
session means its lifetime mean is still dominated by its initial-connection
burst rather than diluted by steady-state traffic like the others. Its
*current* figures (the steady-state ones) match the rest exactly, which is
the more meaningful comparison here.

**Rough per-client average (steady-state "current" values, all 6): ~137
kbit/s down (~17 KB/s), ~50 kbit/s up (~6.2 KB/s).** Using the session mean
for bots 1–5 only (excluding bot 6's skewed short session): ~108 kbit/s down
(~13.5 KB/s), ~34 kbit/s up (~4.2 KB/s).

Server aggregate (sum across all 6 connections, one shared driver): rx
(client uploads) mean 181.4 kbit/s / current 248.7 kbit/s; tx (client
downloads) mean 515.1 kbit/s / current 794.8 kbit/s. Total over the session:
~12.1 MB received, ~34.4 MB sent. No connection errors or rejections during
the run.

For context, 30 crates × 6 players' worth of continuous grab/push/throw
traffic sits well under 150 kbit/s down per client — cheap enough that crate
count or player count both have real headroom to grow before bandwidth
becomes the constraint.
