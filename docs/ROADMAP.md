# Cube Arena — Roadmap

One phase per work session. Each phase ends with a summary and a stop for
approval before the next one starts. See `docs/ARCHITECTURE.md` for the
component diagram, token/session sequence diagram, and threat model this
roadmap implements.

Locked decisions (see Phase 0 discussion, not to be silently revisited):

- Repo layout adapted so the Unity project lives at the repo root, with
  `backend/`, `infra/`, `docs/` as siblings — not nested under `unity/`.
- Auth is a custom implementation (users + refresh-token table), not
  ASP.NET Core Identity.
- Hosting stays local-only (`docker compose`) through Phase 6; the single-VM
  tier is designed on paper in `docs/HOSTING.md` and only actually deployed
  in Phase 7 if still wanted at that point.
- Source repo: https://github.com/AlexBoyev/CubeArena

| Phase | Deliverable | Done when |
|---|---|---|
| 0 | `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, threat model | You approve both documents |
| 1 | Repo scaffold (`backend/`, `infra/`, `.github/workflows/` skeletons), `docker-compose.yml`, `.env.example` | `docker compose up` starts an empty API container and a Postgres container that pass their health checks |
| 2 | Auth: register, login, refresh, rotation, rate limits | Unit + integration tests for issuance/validation/expiry/rotation/reuse-detection are green; tokens are inspectable (e.g. via jwt.io) and contain the specified claims |
| 3 | Sessions, connect tickets, JWKS, fleet registration | A stub validator can verify a ticket issued by `/sessions/quickplay` end to end: signature, `aud`, `sid`, `jti`, expiry all checked |
| 4 | Unity dedicated server build (headless Linux), NGO + Unity Transport added, `ConnectionApprovalCallback` implemented | A Dockerised game server container rejects a deliberately bad ticket (bad signature, wrong `sid`, replayed `jti`, expired, server full) and accepts a good one, with structured rejection reasons |
| 5 | Unity client: character-select screen, connect flow, arena + character cubes, server-authoritative movement with client prediction/reconciliation | Four client instances on one machine connect, move independently, and see each other's positions update correctly |
| 6 | Minimap, disconnect handling, rejoin into a reserved slot, general polish | Manual playtest: all four colours render correctly, minimap dots track players, a disconnect-and-rejoin round trip preserves the player's slot |
| 7 | `SECURITY.md`, `HOSTING.md`, real deploy | The game is reachable and playable from a second physical machine over the internet — **docs and CI done; the actual deploy needs your infrastructure, see below** |

## Notes on sequencing

- Netcode for GameObjects and Unity Transport are **not** installed yet —
  they're added at the start of Phase 4, not before, since Phases 1–3 are
  pure backend work.
- Phase 3's "stub validator" exists so ticket issuance/verification logic
  can be tested before any Unity networking code exists — it's a throwaway
  console/test harness, not the real game server.
- Phase 7 is the only phase that touches a real external host; Phases 0–6
  are fully reproducible with `docker compose up` on your own machine.

## Phase 5 scope notes

- Display name (section 6) is captured in the character-select UI and stored
  locally (`PlayerPrefs`) but **not sent to the backend** — there's no
  endpoint to update it, and it has no gameplay effect in this prototype.
  Only the server-assigned colour (from the ticket's `slot` claim) identifies
  players to each other.
- "See which colours are free" is satisfied narrowly: quickplay's response
  (host/port/ticket/slot) already tells the player their own assigned colour
  once they commit to joining. There's no pre-join lobby view of a session's
  current occupancy — the backend has no endpoint for that, and adding one
  was judged out of scope for a client-focused phase.
- Movement (WASD, prediction, reconciliation) was verified by direct code
  review and a live 4-client connectivity test (see docs/NETCODE.md), not by
  automated input simulation — headless batchmode has no real keyboard/window
  to script against. A hands-on interactive check (Editor Play mode or a
  couple of built clients) is worth doing before calling movement itself
  fully verified.

## Phase 6 scope notes

- The backend needed a small addition beyond "client-only" work: a
  `SessionSlot`'s reservation otherwise expires with its 60s connect ticket,
  which would incorrectly free a still-connected player's slot mid-match.
  Added `POST /fleet/sessions/confirm` (called on connection approval, extends
  the reservation to 24h) and `POST /fleet/sessions/release` (called on
  disconnect, starts a 2-minute rejoin grace period) — both fleet-API-key
  protected like the existing `/fleet/*` endpoints. Verified live: confirm
  extends a slot's expiry to +24h, release drops it to +2min, and a rejoin
  within that window returns the identical session and slot (also covered by
  4 new deterministic backend unit tests).
- The minimap is literally the documented design: a top-down orthographic
  camera rendering the real arena to a RenderTexture — player cubes, already
  coloured per slot, naturally appear as coloured dots from directly above.
  No separate marker/billboard system was needed.
- Live multi-process testing surfaced a reproducible hang in the auto-test
  client bootstrap when a *new process* reuses login credentials from a
  *just-closed* process within the same few seconds — isolated to this
  specific rapid-relaunch pattern (a fresh email always works instantly, and
  a real interactive player never relaunches the whole client process to
  "reconnect"). The underlying confirm/release/rejoin behavior was instead
  proven correct directly over HTTP and via the database, independent of
  this client harness quirk. Worth a closer look if it ever surfaces outside
  of scripted testing.

## Phase 7 status

`SECURITY.md` and `HOSTING.md` are written, and `backend.yml`/`gameserver.yml`
both build and push their Docker images to GHCR using the built-in
`GITHUB_TOKEN` — no user credentials needed for that part.

**The actual deploy is not done** — it genuinely can't be, from here. It
needs:

1. A real VM from a cloud provider (an account, payment, and a provider
   choice are yours to make — `docs/HOSTING.md` tier 2 has cost estimates
   for a few options).
2. A real domain name pointed at that VM (Caddy's automatic TLS needs one).
3. `UNITY_LICENSE` (+ `UNITY_EMAIL`/`UNITY_PASSWORD`, or `UNITY_SERIAL` for
   Pro) added as GitHub repo secrets, so `gameserver.yml` can actually build
   the real Linux server in CI — see the workflow's header comment and
   game-ci.org/docs/github/activation. Until that secret exists,
   `gameserver.yml`'s job skips itself (`if: secrets.UNITY_LICENSE != '' ||
   secrets.UNITY_SERIAL != ''`) instead of failing, so it doesn't sit
   permanently red on a check nobody can act on yet.

Once you have those three things, `docs/HOSTING.md`'s tier 2 section is a
literal, copy-pasteable runbook for the rest.
