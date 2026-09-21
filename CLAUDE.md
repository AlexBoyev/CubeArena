# Pocket Heist (formerly Cube Arena)

A 2-6 player co-op heist game: thumb-sized thieves rob a sleeping giant's
house. Built on the Cube Arena prototype's infrastructure (backend, netcode,
fleet, hosting) with the gameplay replaced. See
`POCKET_HEIST_MASTER_PROMPT.md` for the full pivot brief,
`docs/GAME_DESIGN.md` for the living design reference (loop, world scale,
carry mechanics, noise model, the giant's state machine), and
`docs/ARCHITECTURE.md` / `docs/ROADMAP.md` for the backend/netcode design and
build history. `CUBE_ARENA_PROMPT.md` is kept as the original brief, a
historical record of the prototype this pivoted from.

Work proceeds in milestones (see the master prompt's section 12) on the
`pocket-heist` branch, merged to `master` at each milestone boundary.

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
- This dev machine has the Windows Dedicated Server Build Support module
  installed (`BuildScript.BuildWindowsDedicatedServer`, used by Tier 0
  hosting/`run-gameserver-native.ps1`) — local Windows dedicated server
  builds work fine here. It has no Linux Dedicated Server module, so the
  real Linux build happens in CI (`.github/workflows/gameserver.yml`, via
  GameCI) instead.
- **Imported assets, prefabs, and scenes are allowed and expected** (this
  reverses Cube Arena's original "everything built from code" rule — see
  the master prompt's section 3). **Never hand-edit `.unity`, `.prefab`, or
  `.meta` files directly, though** — that rule survives the pivot
  unchanged. Create and modify prefabs and scenes through Editor scripts or
  the Unity Editor itself, and let Unity own `.meta` file generation either
  way.
- **Close the Unity Editor before any `-batchmode` run** (build, asset
  import script, etc.) — a batch-mode invocation and an open Editor can't
  both hold the project lock.
- **Asset licensing**: only use assets whose licence permits commercial
  Steam release (CC0 packs like KayKit/Kenney are fine; never a "free
  Unity asset" mirror site — see the master prompt's section 4 for the
  approved sources). Log every imported asset/pack in `docs/ASSETS.md`
  (name, author, licence, source URL, date, where it's used).
- **World scale is fixed at ×25** (real-world cm → game meters, environment
  and the giant scaled up, thieves and loot physics kept at native scale so
  Unity physics behaves) — see `docs/GAME_DESIGN.md` section 2 for the
  full conversion table. Every future level scales the same way.
- Cube Arena's original "everything built from code at runtime" pattern
  (`ArenaBuilder`, `PlayerController.CreateTemplate`, `UiFactory`,
  `CrateController.CreateTemplate`) still exists and still works — it isn't
  being ripped out wholesale, just no longer the *only* allowed approach.
  Expect it to be replaced piece by piece as milestones bring in real
  levels/characters (see `docs/GAME_DESIGN.md` section 11's replace/reuse/
  remove table) rather than all at once.
- Player state is hand-rolled replication (`NetworkVariable` + predict/
  reconcile) everywhere except `CrateController`, which deliberately uses
  NGO's built-in `NetworkTransform`/`NetworkRigidbody` instead — rigidbody
  physics is harder to hand-roll well than the simple kinematic pose used
  for players. See `docs/NETCODE.md` before touching either.
- `ServerBootstrap`/`ClientBootstrap` auto-run via `#if UNITY_SERVER` /
  `#if !UNITY_SERVER` respectively — both scripts live in every build target
  (no asmdef platform restriction), so this compile-time gate is what keeps
  a Dedicated Server build from also trying to boot as a client, and vice
  versa. Keep any future "only the server does X" / "only the client does X"
  bootstrap logic behind the same gate.

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
- Max 6 players.
- Cube Arena's original phases (`docs/ROADMAP.md`) are done; going forward,
  work in milestones per `POCKET_HEIST_MASTER_PROMPT.md` section 12 on the
  `pocket-heist` branch, summarizing and merging to `master` at each
  milestone boundary.
