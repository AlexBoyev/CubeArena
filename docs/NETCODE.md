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
