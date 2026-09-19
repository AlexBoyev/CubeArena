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
| 7 | `SECURITY.md`, `HOSTING.md`, real deploy | The game is reachable and playable from a second physical machine over the internet |

## Notes on sequencing

- Netcode for GameObjects and Unity Transport are **not** installed yet —
  they're added at the start of Phase 4, not before, since Phases 1–3 are
  pure backend work.
- Phase 3's "stub validator" exists so ticket issuance/verification logic
  can be tested before any Unity networking code exists — it's a throwaway
  console/test harness, not the real game server.
- Phase 7 is the only phase that touches a real external host; Phases 0–6
  are fully reproducible with `docker compose up` on your own machine.
