# Project brief: "Cube Arena" — 4-player multiplayer prototype

You are the lead engineer on a greenfield multiplayer game project. Build the
**infrastructure, backend, and networking foundation first**. Gameplay is
deliberately trivial so that the plumbing is the actual deliverable.

Work in phases. **Stop at the end of each phase**, summarise what you built and
how to verify it, and wait for my approval before starting the next phase.

---

## 0. Before writing any code

Read this whole brief, then:

1. Ask me any blocking questions (max 6). Specifically confirm: Unity version,
   OS I develop on, whether I have Docker installed, and where I intend to host.
2. Produce `docs/ARCHITECTURE.md` containing:
   - A component diagram (mermaid) of client / backend / game server / DB.
   - The full token and session lifecycle as a sequence diagram (mermaid).
   - A threat model table: threat, vector, mitigation, phase where it lands.
   - Explicitly list what is **out of scope** for the prototype.
3. Produce `docs/ROADMAP.md` — the phases below, each with acceptance criteria.

Do not scaffold code until I approve those two documents.

---

## 1. Target architecture (these are decisions, not suggestions)

| Layer | Technology |
|---|---|
| Client | Unity (C#), Netcode for GameObjects + Unity Transport |
| Game server | The same Unity project, built as a **headless Linux dedicated server** |
| Backend | ASP.NET Core minimal API, single service, modular folders |
| Persistence | PostgreSQL in prod, SQLite for local dev, EF Core migrations |
| Auth | ASP.NET Core Identity or custom; JWT access + rotating refresh |
| Packaging | Docker for backend and game server; docker compose for local dev |
| CI | GitHub Actions; GameCI for Unity builds |

**Hard rule: no host-client mode, ever.** The server is always a separate,
authoritative, headless process. Even in local dev I run a real server binary.
The client is never trusted for position, health, score, or identity.

If you believe one of these choices is wrong, say so in Phase 0 with a reason —
do not silently substitute something else.

---

## 2. Repository layout

```
cube-arena/
  unity/                       # single Unity project, two build targets
    Assets/
      Scripts/
        Shared/                # netcode messages, constants, enums
        Client/
        Server/
      Scenes/
  backend/
    CubeArena.Api/             # minimal API host
      Features/Auth/
      Features/Sessions/
      Features/Fleet/          # game-server registration + heartbeat
    CubeArena.Domain/
    CubeArena.Tests/
  infra/
    docker/                    # Dockerfile.api, Dockerfile.gameserver
    compose/docker-compose.yml
    scripts/
  .github/workflows/
  docs/
```

---

## 3. Token, session and security design

Implement exactly this flow.

### 3.1 Identity

- `POST /auth/register` — email + password. Argon2id or ASP.NET Identity hasher.
  Never bcrypt-with-default-cost, never plain SHA.
- `POST /auth/login` — returns `accessToken` (JWT, 15 min) and `refreshToken`
  (opaque, 30 days, stored hashed in the DB, **rotated on every use**, with
  reuse detection that revokes the whole family).
- `POST /auth/refresh`, `POST /auth/logout`.
- Access token claims: `sub` (user id), `name` (display name), `exp`, `iss`, `aud=api`.

### 3.2 Connect ticket (the important part)

- `POST /sessions/quickplay` with a valid access token:
  - Finds an open session with fewer than 4 players, or allocates a new one.
  - Reserves a slot for this user with a 60-second expiry.
  - Returns `{ host, port, sessionId, ticket, slotIndex }`.
- The `ticket` is a separate JWT: `aud=gameserver`, `sid=<sessionId>`,
  `sub=<userId>`, `slot=<0..3>`, **`exp = now + 60s`**, `jti` unique.
- The game server validates the ticket **offline**, by signature, in
  `NetworkManager.ConnectionApprovalCallback`. It must check:
  1. Signature and `exp`.
  2. `aud == "gameserver"` and `sid` equals its own session id.
  3. `jti` not already used on this server (in-memory set).
  4. Current player count < 4.
  Reject with a structured reason code on any failure.
- Asymmetric signing (RS256/ES256). The **private key never leaves the backend**;
  the game server holds only the public key, fetched from a JWKS endpoint at
  boot and cached. No shared secret is ever embedded in a client build.

### 3.3 Non-negotiable security requirements

- No secret, connection string, or signing key in the Unity client. Assume every
  client build is fully decompiled by an attacker.
- TLS on the backend; certificate pinning is not required for the prototype but
  note it in the threat model.
- Rate limit `/auth/*` per IP and per account. Lockout with exponential backoff.
- Server-authoritative movement: the client sends **input intent only**
  (a direction vector and button flags). The server simulates and broadcasts
  state. Reject inputs whose implied speed exceeds the allowed maximum.
- Validate every ServerRpc: sender identity, rate, and value ranges. Never trust
  a client-supplied player id — derive it from the NGO client id.
- Log auth failures and rejected connections in structured JSON with a
  correlation id. Never log tokens or password material.
- `SECURITY.md` documenting all of the above and what is deliberately deferred.

---

## 4. DevOps requirements

- `docker compose up` must bring up: postgres, backend API, and one game server
  container, with the game server registering itself with the backend on boot
  and heartbeating every 10 seconds.
- Game server lifecycle: registers → reports `players/capacity` → marks itself
  drained when empty for 5 minutes → exits. Backend removes stale entries after
  3 missed heartbeats.
- Configuration entirely via environment variables. Provide `.env.example`.
  Nothing secret is committed; add a pre-commit hook or CI step that greps for
  obvious secret patterns.
- Health endpoints: `/health/live` and `/health/ready` on the API; a TCP or HTTP
  health port on the game server separate from the UDP game port.
- GitHub Actions:
  - `backend.yml` — build, unit tests, integration tests against a Postgres
    service container, build and push the API image.
  - `gameserver.yml` — GameCI Linux dedicated-server build, wrap in a Docker
    image, push.
  - `client.yml` — Windows client build on tag only.
- Unity build artefacts must not enter git. Use the standard Unity `.gitignore`.

---

## 5. Hosting

Write `docs/HOSTING.md` covering three tiers, with cost estimates and the
concrete deployment steps for tier 2:

1. **Local** — docker compose, everything on my machine.
2. **Single VM** — one small Linux VM running compose, backend behind Caddy or
   nginx with automatic TLS, game servers on a UDP port range (7777-7787),
   firewall rules spelled out explicitly.
3. **Managed** — backend on a PaaS, game servers on a VM scale set or Kubernetes
   with `hostPort`, or Unity Game Server Hosting.

Call out clearly that most HTTP-oriented PaaS products do not route arbitrary
UDP, and that this constrains where the game server can live.

---

## 6. Gameplay scope (keep it minimal)

Only build this much:

- **Arena**: a 40x40 flat plane, a boundary wall, 6-8 static obstacle cubes.
- **Character**: one 1x1x1 body cube with a 0.5 cube "head" on top. No models,
  no animation, no physics beyond a kinematic controller.
- **Colours**: four fixed slots — red, blue, green, yellow — assigned by
  `slotIndex` from the connect ticket. The server owns the assignment; the
  client only renders it.
- **Character select**: a screen before connecting where I pick a display name
  and see which colours are free in the session I'm joining.
- **Movement**: WASD, server-authoritative, 30 Hz server tick, client-side
  interpolation of remote players and prediction plus reconciliation for the
  local player. Document the reconciliation approach in `docs/NETCODE.md`.
- **Minimap**: an orthographic camera rendering to a RenderTexture, shown in a
  UI RawImage corner, with a dot per player in that player's colour.
- **Disconnect handling**: graceful leave, timeout, and rejoin into the same
  session if a slot is still reserved.

No combat, no scoring, no chat, no persistence of match results. If you find
yourself adding a feature not on this list, stop and ask.

---

## 7. Testing (treat this as first-class, not an afterthought)

- Unit tests for token issuance, validation, expiry, and refresh rotation
  including the reuse-detection path.
- Integration tests for the full happy path and for each rejection reason:
  expired ticket, wrong session id, replayed `jti`, server full.
- A headless test harness that launches one game server and four scripted
  clients in CI and asserts all four connect, move, and disconnect cleanly.
- A load smoke test that confirms the 5th connection is rejected.

---

## 8. Phase plan

| Phase | Deliverable | Done when |
|---|---|---|
| 0 | `ARCHITECTURE.md`, `ROADMAP.md`, threat model | I approve both documents |
| 1 | Repo scaffold, compose, CI skeleton, `.env.example` | `docker compose up` starts an empty API and Postgres |
| 2 | Auth: register, login, refresh, rotation, rate limits | Auth tests green; tokens inspectable |
| 3 | Sessions, connect tickets, JWKS, fleet registration | Ticket issued and verified by a stub validator |
| 4 | Unity dedicated server build, NGO, connection approval | Dockerised server rejects a bad ticket, accepts a good one |
| 5 | Unity client: select screen, connect, cubes, movement | Four clients on one machine move around and see each other |
| 6 | Minimap, disconnect and rejoin, polish | Manual playtest passes |
| 7 | `SECURITY.md`, `HOSTING.md`, deploy to a real VM | Reachable from a second machine over the internet |

---

## 9. How I want you to work

- One phase per session. Summarise and stop at each boundary.
- Small, reviewable commits with conventional-commit messages.
- When a decision has a real trade-off, present the options in a table with your
  recommendation and a one-line reason — then wait, don't assume.
- Prefer boring, well-documented solutions over clever ones.
- If something in this brief turns out to be wrong or impossible with the Unity
  version I'm on, tell me immediately rather than working around it silently.
- Keep `CLAUDE.md` in the repo root up to date with Unity version, render
  pipeline, input system, and the rule that you must never hand-edit `.unity`,
  `.prefab`, or `.meta` files.

Start with Phase 0.