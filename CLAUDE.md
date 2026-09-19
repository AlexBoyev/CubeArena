# CubeArena

4-player multiplayer prototype. See `CUBE_ARENA_PROMPT.md` for the full brief
and `docs/ARCHITECTURE.md` / `docs/ROADMAP.md` for the current design and phase
plan.

## Unity project

- Unity 6000.5.5f1, project lives at the repo root (`Assets/`, `Packages/`,
  `ProjectSettings/` are siblings of `backend/`, `infra/`, `docs/`).
- Render pipeline: URP (17.5.0).
- Input: the new Input System package (1.19.0), not the legacy Input Manager.
- Netcode for GameObjects 2.13.2 + Unity Transport 2.6.0 (NGO 1.x doesn't
  compile against this Editor version — see docs/ARCHITECTURE.md). Ticket
  signature verification uses BouncyCastle, not `System.Security.Cryptography`
  (its asymmetric APIs are non-functional on this Unity/Mono runtime — see
  docs/ARCHITECTURE.md's Phase 4 platform findings before touching crypto
  or NGO connection code).
- This dev machine only has Windows Standalone + WebGL build support
  installed — no Dedicated Server module for either OS. Local dedicated
  server builds aren't possible here; the real Linux build happens in CI
  (`.github/workflows/gameserver.yml`, via GameCI).
- **Never hand-edit `.unity`, `.prefab`, or `.meta` files directly.** Make
  scene/prefab changes through the Unity Editor (or generate them via
  Unity's own APIs/tooling), and let Unity own `.meta` file generation.

## Backend

- ASP.NET Core minimal API (`backend/CubeArena.Api`), custom auth (not
  ASP.NET Core Identity) — see `docs/ARCHITECTURE.md` for the token/session
  design.
- EF Core + Npgsql, Postgres both in prod and local dev (via `docker
  compose`) — the brief's original "SQLite for local dev" was simplified
  away in Phase 1 for dev/prod parity; flagged to the user, not yet revisited.

## Hard rules

- No host-client mode. The game server is always a separate, authoritative,
  headless process — including in local dev.
- No secrets, signing keys, or connection strings in the Unity client, ever.
- Work in phases per `docs/ROADMAP.md`; stop and summarize at each phase
  boundary rather than continuing into the next phase unprompted.
