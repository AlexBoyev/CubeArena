# CubeArena

4-player multiplayer prototype. See `CUBE_ARENA_PROMPT.md` for the full brief
and `docs/ARCHITECTURE.md` / `docs/ROADMAP.md` for the current design and phase
plan.

## Unity project

- Unity 6000.5.5f1, project lives at the repo root (`Assets/`, `Packages/`,
  `ProjectSettings/` are siblings of `backend/`, `infra/`, `docs/`).
- Render pipeline: URP (17.5.0).
- Input: the new Input System package (1.19.0), not the legacy Input Manager.
- Netcode for GameObjects + Unity Transport are added in Phase 4 — not
  present in the project yet.
- **Never hand-edit `.unity`, `.prefab`, or `.meta` files directly.** Make
  scene/prefab changes through the Unity Editor (or generate them via
  Unity's own APIs/tooling), and let Unity own `.meta` file generation.

## Backend

- ASP.NET Core minimal API (`backend/CubeArena.Api`), custom auth (not
  ASP.NET Core Identity) — see `docs/ARCHITECTURE.md` for the token/session
  design.
- EF Core: PostgreSQL in prod, SQLite for local dev.

## Hard rules

- No host-client mode. The game server is always a separate, authoritative,
  headless process — including in local dev.
- No secrets, signing keys, or connection strings in the Unity client, ever.
- Work in phases per `docs/ROADMAP.md`; stop and summarize at each phase
  boundary rather than continuing into the next phase unprompted.
