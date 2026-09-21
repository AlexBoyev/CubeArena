# Cube Arena — Project Brief

A portable primer for starting a fresh Claude session on this repo — what
the project is, how it's built, and where things currently stand. For the
canonical, always-loaded version of the hard rules, see `CLAUDE.md` at the
repo root; for full design detail see `docs/ARCHITECTURE.md`,
`docs/NETCODE.md`, `docs/ROADMAP.md`, `docs/HOSTING.md`, and `SECURITY.md`.

## What it is

A 4-player multiplayer arena prototype, built solo with heavy AI-assisted
iteration. Players connect to a dedicated game server, wait in a lobby,
then one round runs on a 5-minute clock: move around an obstacle-course
arena, collect gold pickups for points, whoever has the most when time
runs out (or the match is vote-ended early) wins.

## Tech stack

- **Unity 6000.5.5f1**, URP, the new Input System — client and dedicated
  server are the same Unity project, split by `#if UNITY_SERVER` compile
  guards on `ServerBootstrap`/`ClientBootstrap`.
- **Netcode for GameObjects 2.13.2 + Unity Transport 2.6.0** for
  networking. No built-in `NetworkTransform` anywhere — position/rotation
  and every piece of game state are manually replicated via
  `NetworkVariable<T>`, for consistency with everything else being
  hand-rolled.
- **ASP.NET Core minimal API** backend (`backend/CubeArena.Api`) — custom
  auth (not ASP.NET Identity), EF Core + Npgsql/Postgres, issues signed
  connect tickets (BouncyCastle, not `System.Security.Cryptography` — that
  package's asymmetric APIs don't work on this Unity/Mono runtime) that the
  game server verifies before letting a client actually spawn.
- **Docker Compose** for local hosting (Postgres + API always in
  containers; the dedicated server runs as a native Windows process on
  this dev machine, since only the Windows Dedicated Server module is
  installed locally — the real Linux build happens in CI via GameCI).

## Architecture, in one pass

1. Client logs in / registers against the backend API, gets access +
   refresh tokens.
2. Client picks a display name and hits Quick Play → backend's
   `/sessions/quickplay` finds (or creates) a game server session, reserves
   a slot, and returns a signed connect ticket plus the server's
   host/port.
3. Client connects to the dedicated server over Unity Transport, presenting
   that ticket. `ConnectionApprovalHandler` validates the ticket's
   signature, audience, session id, and expiry before approving.
4. Once approved, `ServerBootstrap` spawns a `PlayerController` for that
   client, owned by them, at their assigned slot's spawn point.
5. Everyone lands in a **lobby**: they can look around but can't move or
   collect pickups (`PlayerController.IsMatchActive`,
   `PickupController.Update`'s gate) until whoever's been connected longest
   (the "host") clicks Start Match (`MatchManager.RequestStartMatchServerRpc`,
   host-verified server-side, not just hidden client-side).
6. Match runs server-authoritative at 30Hz: `PlayerController.SimulateMovement`
   is the only place movement is actually applied; clients predict locally
   (`PredictAndReconcile`) and soft-correct toward the server's replicated
   position, per `docs/NETCODE.md`.
7. `MatchManager`'s 5-minute clock (`_timeRemaining` NetworkVariable) ends
   the round; `ServerBootstrap.OnMatchEnded` disconnects everyone with a
   result message, resets scores, and resets the lobby for another round.
8. The arena itself (`ArenaBuilder`), every player's visual model
   (`PlayerController.CreateTemplate`), and all UI (`UiFactory`,
   `ClientBootstrap`) are built **entirely from code at runtime** — no
   scene files, no prefab assets, no hand-imported meshes. This is a hard
   rule (`CLAUDE.md`), not a stylistic choice: it means client and server
   independently produce byte-identical geometry with zero sync needed, and
   nothing here requires the Unity Editor to author.

## Current feature set (accumulated well past the original minimal brief)

- Movement: WASD, jump, crouch (Ctrl), crawl (C, for tunnels crouch alone
  doesn't clear), sprint (Shift, gated by a stamina resource with drain/
  regen/exhaustion-hysteresis).
- Obstacle course: crouch tunnel, climbable tower, jump gap, balance beam,
  two houses with interior stairs to a loft, two crawl tunnels.
- Scoring: gold pickups, live scoreboard (hold Tab), a match clock that
  turns reddish under a minute left.
- Lobby + host-starts-match flow; local Esc pause (freezes only your own
  input/camera, doesn't affect the match for anyone else); a rejoin system
  that reconnects you into the same still-running match with your score
  preserved if you disconnect mid-match.
- Nameplates, per-slot colors (red/blue/green/yellow, also the default
  display name if left blank), nameplate billboarding.
- HP/Mana/Stamina bars in the HUD — Stamina is real (drives sprint); HP and
  Mana are deliberate placeholders (`PlayerController.Health`/a similar
  Mana value) with no gameplay behind them yet, wired as real replicated
  values so future combat/ability work only needs to write to them.
- Backend: register/login/refresh with rate limiting and Argon2id hashing,
  session/slot reservation with a rejoin grace period, JWKS-published
  ticket verification, fleet registration/heartbeat.

## Hosting model (Tier 0, the default)

Everything runs on one machine — Postgres + API in Docker, the dedicated
server as a native process. No cloud VM required. For remote friends:
either a Tailscale mesh (no port forwarding, one small app per remote
player) or router port forwarding (zero installs for players, but the
backend port becomes reachable by the open internet while it's up — see
`docs/HOSTING.md`'s security notes and `Stop-CubeArena-Host.bat` for
tearing it down between sessions). `docs/HOSTING.md` also has Tier 2 (a
real cloud VM deploy) fully documented but not yet actually provisioned —
needs the user's own VM + domain.

## What's deliberately out of scope right now

No combat (HP/Mana bars are scaffolding, not live), no chat, no match
history/persistence across rounds, no account recovery (password reset/
email verification), no anti-cheat beyond server-authoritative movement,
no DDoS protection beyond basic rate limiting. See `SECURITY.md`'s
"Deliberately out of scope" section for the full, reasoned list — these
aren't oversights, they're judged not worth the cost yet for a small
friends-only prototype.

## Where things stand

Gameplay mechanics are still actively being iterated on session-to-session
(this doc itself may lag slightly behind the newest features — check
`git log` for what's landed most recently). The core loop (lobby → match →
score → game over → back around) is real and playable end-to-end.
