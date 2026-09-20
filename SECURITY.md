# Cube Arena — Security

What's implemented, why, and what's deliberately deferred for this prototype.
See `docs/ARCHITECTURE.md` for the full threat model table and the
token/session sequence diagram this document assumes.

## Identity and passwords

- Passwords are hashed with **Argon2id** (`backend/CubeArena.Api/Features/Auth/PasswordHasher.cs`),
  using the OWASP 2024 cheat sheet's minimum interactive-login profile
  (19 MiB memory, 2 iterations, 1 degree of parallelism), a random 16-byte
  salt per password, and a constant-time comparison on verify. Never
  bcrypt-with-default-cost, never a fast general-purpose hash, never
  reversible encryption.
- Emails are the only account identifier; there is no username, no OAuth/social
  login, no MFA. Account recovery (forgotten password) is **not implemented** —
  out of scope for this prototype (see below).

## Tokens

- **Access tokens**: JWT, HS256, 15-minute lifetime, claims `sub`, `name`,
  `exp`, `iss`, `aud=api`. Signed with a symmetric key
  (`Auth:SigningKey`/`AUTH_SIGNING_KEY`) that lives only in the backend's own
  environment — it is never sent to, or needed by, the Unity client or the
  game server.
- **Refresh tokens**: opaque 256-bit random values, stored only as a SHA-256
  hash (never the raw value) alongside a `FamilyId`. Every refresh **rotates**
  the token: the old one is marked consumed and a new one issued in the same
  family. Replaying an already-consumed refresh token is treated as theft and
  **revokes the entire family**, forcing re-login. This is tested directly
  (`TokenServiceTests`, `AuthEndpointsTests`) — including replaying a token
  that was legitimately rotated once already.
- Logout revokes the current refresh token's family immediately.

## Connect tickets (the part that crosses a real trust boundary)

The connect ticket is the one credential that travels from the backend,
through the player's Unity client, to a completely separate process (the
dedicated game server) that the backend does not otherwise trust or control
in real time. It is treated accordingly:

- **Asymmetric signing (ES256/ECDSA P-256)**, not HMAC. The private key never
  leaves the backend (`backend/CubeArena.Api/Features/Sessions/TicketService.cs`).
  The game server fetches only the *public* key from `GET
  /.well-known/jwks.json` once at boot and caches it — it has no way to forge
  a ticket even if fully decompiled/compromised, only to verify one.
- 60-second lifetime, single-use (`jti`), scoped to one session (`sid`) and
  one audience (`aud=gameserver`) — a ticket for session A cannot be replayed
  against session B, and a ticket cannot be reused to connect twice.
- The game server validates all of this **completely offline** — no callback
  to the backend at connection time — via `TicketValidator.cs`. It checks, in
  order: signature, expiry, issuer, audience, session id, then replay (a
  per-process in-memory `jti` set), then current player count against
  capacity. Every rejection path returns a structured reason
  (`ConnectRejectionReason`), never a generic failure, and is unit-tested
  (`TicketValidatorTests`, 8 cases including a real .NET-signed ticket
  verified by the Unity-side validator).
- **Why not `System.Security.Cryptography`**: on this project's Unity/Mono
  runtime, `ECDsa.Create()` throws and `RSA.Create()` hangs indefinitely
  (verified in an actual built player — see `docs/ARCHITECTURE.md`). Ticket
  verification uses BouncyCastle instead, which is pure managed code with no
  dependency on Unity's broken native crypto provider bindings. This does not
  change the security properties above — it's still asymmetric, still
  offline, still no private key on the client or game server.

## Fleet (game-server-to-backend) trust

- `/fleet/register`, `/fleet/heartbeat`, `/fleet/sessions/confirm`, and
  `/fleet/sessions/release` all require a shared-secret `X-Fleet-Api-Key`
  header (constant-time compared). This isn't in the original brief, but
  without it anyone who can reach the backend could register a fake "game
  server" and harvest real players' connect tickets. The key lives only in
  the dedicated server's own container environment — it is operator
  infrastructure, never shipped in a player-facing client build, so this
  doesn't violate "no secrets in the client."

## Rate limiting and lockout

- Per-IP fixed-window rate limiting (10 requests/minute, configurable) across
  all of `/auth/*`, verified live (429 trips exactly on the 11th request).
- Per-account lockout with exponential backoff on repeated failed logins
  (locks after 5 failures, doubling delay capped at 1 hour), independent of
  the per-IP limit so a distributed attempt against one account is still
  slowed down.

## Server-authoritative gameplay

- The client never sends a position — only input intent (a direction vector),
  via a `ServerRpc`. The server is the sole writer of the replicated position
  (`PlayerController.cs`, `NetworkVariableWritePermission.Server`), simulating
  movement itself via `CharacterController` at a fixed 30Hz tick.
- The server clamps/rejects any input vector implying speed above the
  allowed maximum (normalizes to unit length before applying `MoveSpeed`) —
  see `SubmitInputServerRpc`.
- The player's identity for every RPC is NGO's own connection-derived
  `OwnerClientId` / `RequireOwnership`, never a client-supplied id. A client
  cannot act on another player's `PlayerController`.

## Logging

- All backend logs are structured JSON (`AddJsonConsole`) with a correlation
  id (the request's `TraceIdentifier`) on every auth/ticket rejection path.
- Tokens, refresh token values, and password material are **never** logged —
  only outcomes (e.g. `"Login rejected: wrong password"`), ids, and
  correlation ids.

## Secrets and source control

- `.env` files are gitignored everywhere (`**/.env.example` is the only
  tracked variant); nothing secret has ever been committed.
- `.github/workflows/secret-scan.yml` runs gitleaks on every push/PR as a
  backstop against accidental commits.
- All configuration (signing keys, connection strings, the fleet API key) is
  environment-variable driven — nothing is hardcoded, and no secret is baked
  into a Unity build.

## TLS

- The backend is expected to run behind TLS in any real deployment (see
  `docs/HOSTING.md`'s tier 2/3 for exactly how — Caddy/nginx with automatic
  certificates, or a managed platform's own TLS termination). The API
  container itself always serves plain HTTP directly — it never terminates
  TLS itself in any tier; a reverse proxy in front of it does that when one
  exists.
- **Certificate pinning is explicitly deferred.** For a prototype this size,
  the operational cost (pin rotation, the risk of bricking the client on a
  routine cert renewal) outweighs the benefit against the realistic threat
  model here. If this ever handles real payment or highly sensitive data,
  revisit this.

### LAN_MODE

`docs/HOSTING.md`'s tier 0 runs the full stack — Postgres, API, and the game
server — on one machine, with the API bound to that machine's real LAN IP
(via `PUBLIC_HOST`) instead of only `127.0.0.1`, so other devices on the same
network can reach it. That's a deliberate, narrower trust boundary than
"only this one machine": every password, access/refresh token, and connect
ticket travels as **plain, unencrypted HTTP** to anyone who can observe LAN
traffic (any other device on the same Wi-Fi/switch, a compromised router,
etc.) — there is no reverse proxy in front of it to add TLS.

This is an accepted trade-off for a same-room LAN party among people who
already trust each other and the network, not a gap to "fix" — adding TLS
here would mean either a self-signed cert (which the Unity client doesn't
validate against a trust store, defeating the point) or a real domain +
Caddy, which is exactly what tier 2 already exists for.

`LAN_MODE=true` doesn't change what the API serves (it's always been plain
HTTP — see above); it makes the server print a loud, impossible-to-miss boot
warning acknowledging that this plain-HTTP endpoint is intentionally
reachable beyond localhost, so it's never silently exposed. **Never set
`LAN_MODE=true` on a machine whose port is also forwarded to the internet**
— that turns "trusted LAN only" into "trusted LAN plus anyone on the
internet who finds the port."

**Unity client-side gotcha (verified in a built player):** Unity blocks
plain-HTTP `UnityWebRequest` calls from non-development builds by default
(`PlayerSettings.insecureHttpOption`, `NotAllowed` unless set otherwise —
the request fails immediately with "Non-secure HTTP connections disabled in
release builds," not a connection-refused error). Since tier 0 is exactly
plain HTTP by design, this project sets it to `AlwaysAllowed`
(`ProjectSettings/ProjectSettings.asset`'s `insecureHttpOption: 2`) — the
same accepted trade-off as the rest of this section, just enforced
client-side too. `DevelopmentOnly` (`1`) is *not* sufficient — a release
build (what you'd actually hand out for a LAN party) is still blocked by
that setting.

## Deliberately out of scope for this prototype

These are conscious omissions, not oversights — see `docs/ARCHITECTURE.md`'s
"Out of scope" section for the full list. Security-relevant ones called out
again here:

- **DDoS protection** beyond the per-IP/per-account rate limiting above. A
  real internet-facing deployment would sit behind a provider that offers at
  least basic volumetric protection (see `docs/HOSTING.md`).
- **Anti-cheat** beyond server-authoritative movement/input validation — no
  heuristic or ML-based cheat detection, no client integrity checks.
- **Account recovery** (password reset, email verification) — there is
  currently no way to regain access to an account with a lost password other
  than registering a new one.
- **Horizontal scaling / load balancing of the backend API itself** — this
  is a single-instance service; the fleet/session model doesn't assume or
  require multiple backend instances behind a shared load balancer, though
  nothing here would prevent adding one later since all state lives in
  Postgres.
