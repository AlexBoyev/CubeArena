# Master brief: "Pocket Heist" (working title) — pivot of the Cube Arena repo

You are the lead engineer on this repo. It currently contains **Cube Arena**, a
working 4-player multiplayer prototype. We are turning it into a real game:
a **2–6 player co-op heist game where thumb-sized thieves rob a sleeping giant's
house**. The infrastructure stays. The gameplay is replaced.

Read this whole document before doing anything. Then read `CLAUDE.md`,
`docs/ARCHITECTURE.md`, `docs/NETCODE.md`, `docs/HOSTING.md` and `SECURITY.md`
so your plan is grounded in what actually exists.

---

## 0. How to work

- Work in **milestones** (section 12). **Stop at the end of each milestone**,
  summarise what you built, how I verify it, and wait for approval.
- Before each milestone, write a short plan and show it to me first.
- When a decision has a real trade-off, present options in a table with your
  recommendation and a one-line reason, then wait.
- If you find yourself building something not listed in the current milestone,
  stop and ask.
- Work on a `pocket-heist` branch. Small commits, conventional-commit messages.
  Merge to master at milestone boundaries.
- Keep `CLAUDE.md` current (see section 3).
- If something in this brief is wrong for our Unity/NGO versions, tell me
  immediately instead of working around it silently.

---

## 1. The game

**Pitch:** a crew of 2–6 thumb-sized thieves robs a sleeping giant's house,
one night per run. Heavy loot needs several thieves to carry. Teamwork makes
noise. Noise wakes the giant.

**Core loop:** sneak in through a mousehole → find loot → carry it back to the
mousehole → payout → (later) spend earnings on gear → next night, harder house.

**Night timer:** each run lasts until dawn (default 10 minutes, tunable). At
dawn the giant wakes regardless; anyone not back in the mousehole is caught.

**Fail state:** the giant wakes, searches with his phone light, and catches
thieves. Caught thieves are trapped under an upturned glass; teammates can
rescue them. If everyone is caught, the night ends and unbanked loot is lost.

**Story (light, environmental):** the crew lives inside the walls of a human
town. Giants are ordinary people, not villains. Each house has an owner told
through objects and notes. No cutscenes in early milestones.

**Art direction:** stylized low-poly, warm, comedic, lit at night. Ordinary
household objects at enormous scale are the visual identity. Reference feel:
R.E.P.O., RV There Yet?. Lighting matters more than model detail.

---

## 2. Current state of the repo (verify, don't assume)

- Unity 6000.5.5f1, URP, new Input System
- Netcode for GameObjects 2.13.2, Unity Transport 2.6.0
- Dedicated server = same Unity project, split by `#if UNITY_SERVER`
  (`ServerBootstrap` / `ClientBootstrap`). Locally it runs as a native Windows
  process; the Linux build happens in CI via GameCI.
- ASP.NET Core minimal API backend (`backend/CubeArena.Api`): custom auth,
  Argon2id, refresh rotation, rate limiting, sessions + slot reservation with
  rejoin grace, BouncyCastle-signed connect tickets, JWKS, fleet heartbeat.
- Postgres + API in Docker Compose. Tier 0 LAN hosting is the default.
- Player movement: server-authoritative at 30 Hz with client prediction and
  reconciliation, hand-replicated via NetworkVariables. Jump, crouch, crawl,
  sprint with stamina.
- Lobby, host-starts-match, rejoin with preserved state, Esc pause, nameplates,
  per-slot colours, HP/Mana/Stamina HUD.
- **In progress when this brief was written:** max players 4 → 6, a
  synced-physics layer (NetworkTransform + NetworkRigidbody with ownership
  transfer on grab), a 30-crate / 6-client test, Unity Multiplayer Tools
  profiler for bandwidth, and a fix for a PreToolUse hook failing with
  `python3: command not found`. Milestone F finishes and verifies these.

---

## 3. Rule changes (apply in Milestone 0)

Update `CLAUDE.md` and all docs that reference these:

| Old rule | New rule |
|---|---|
| Everything built from code; no scenes, prefabs or imported assets | **Imported assets, prefabs and scenes are allowed and expected.** |
| — | Create and modify prefabs and scenes **through Editor scripts or the Unity Editor**, never by hand-editing `.unity`, `.prefab` or `.meta` YAML. |
| — | Only use assets whose licence permits commercial Steam release. Log every one in `docs/ASSETS.md`. |
| Max 4 players | **Max 6 players.** |
| — | World scale ×25 (section 5) is fixed; document it. |

Keep in `CLAUDE.md`: Unity version, render pipeline, input system, world
scale, asset rules, the no-hand-editing rule, and that Unity must be closed
before any batch-mode run.

---

## 4. Assets

### 4.1 Download yourself (official sources only)

| Pack | Source | Licence |
|---|---|---|
| **KayKit Restaurant Bits** (free version) | `github.com/KayKit-Game-Assets/KayKit-Restaurant-Bits-1.0` | CC0 |
| **Kenney Furniture Kit** | `kenney.nl/assets/furniture-kit` | CC0 |
| Kenney audio packs for placeholder SFX (footsteps, impacts, UI) | `kenney.nl/assets` | CC0 |

**Never** download from mirror or "free Unity asset" sites. Paid Unity Asset
Store versions redistributed there are not licensed for commercial use. If a
download needs a browser click-through or a login, stop and tell me which one.

### 4.2 Already downloaded manually — in `F:\Unity\Downloads\`

| File | Contents |
|---|---|
| `Universal Base Characters[Standard].zip` | Quaternius, CC0. Rigged humanoids. Used for the giant and the thieves. |
| `Universal Animation Library[Standard].zip` | Quaternius, CC0. 120+ retargetable humanoid animations. |
| `Laying Sleeping.fbx` | Mixamo, no skin |
| `Sleeping Idle1.fbx` | Mixamo, no skin |
| `Sleeping Idle2.fbx` | Mixamo, no skin |

Retarget all three Mixamo clips to the Quaternius rig. Tell me which one works
best for the giant **slumped forward at the kitchen table, head on arms**.
Keep the others for later levels (`Laying Sleeping` suits a bedroom level).
If none fits, propose adjusting a pose via an Editor script and ask me.

### 4.3 Import rules

- Unzip into a temp folder. Copy **only** what's needed (FBX/GLTF models,
  textures, animation clips) into `Assets/ThirdParty/<PackName>/`. Don't import
  Blender sources, OBJ duplicates, or engine-specific folders for other engines.
- Downloads and temp folders must stay **outside** the repo. Never commit zips.
- Add glTFast (`com.unity.cloud.gltfast`) only if a pack is GLTF-only.
- Convert all materials to URP via an Editor script.
- Style: **KayKit is the primary style.** Use Kenney only where KayKit has no
  equivalent, and recolour to match.
- Build prefabs via Editor scripts run with
  `Unity.exe -batchmode -projectPath <root> -executeMethod ... -quit -logFile`.
  Tell me to close Unity before every batch run.
- `docs/ASSETS.md`: asset/pack name, author, licence, source URL, date, and
  where it's used.

### 4.4 Characters

| Role | Source | Setup |
|---|---|---|
| Giant | Base Characters, "Regular" proportions | Scaled ×25, seated in a chair, sleep clip |
| Thieves (placeholder) | Base Characters, "Teen" proportions | Native scale (1 m), recoloured per slot |

Thief slot colours: red, blue, green, yellow, purple, orange. Use Quaternius
locomotion clips (idle, walk, run, crouch, jump, climb, push/carry) for
thieves, driven by the existing replicated movement state.

---

## 5. World scale

| Thing | Real | In game |
|---|---|---|
| **Scale factor** | — | **×25** |
| Thief height | 4 cm | **1.0 m** — never scale the player |
| Kitchen floor | 400 × 350 cm | 100 × 87.5 m |
| Ceiling | 260 cm | 65 m |
| Table top height | 75 cm | 18.75 m |
| Table top | 120 × 80 cm | 30 × 20 m |
| Chair seat | 45 cm | 11.25 m |
| Counter height | 90 cm | 22.5 m |
| Giant standing | ~175 cm | ~44 m |
| Coin (₪10) | 2.3 cm | 0.58 m disc |

Environment and giant are scaled up; thieves and loot physics stay at native
size so Unity physics behaves. Raise camera far-clip and review shadow
distance/cascades for the larger world.

---

## 6. Level 1: "The Midnight Snacker" (kitchen)

The giant came down for a late sandwich, sat at the kitchen table and fell
asleep with his head on his arms. His wallet spilled beside him. The fridge
door is still ajar.

| Area | Contents | Purpose |
|---|---|---|
| Mousehole | Baseboard, south-west wall | Spawn and delivery point. Loot inside = banked. |
| Rug | Between mousehole and table | Quiet route (soft surface) |
| Tiled floor | Everything else | Loud surface |
| Tutorial coin | Floor, under the table | First 2-player lift, no height |
| Chair (tucked) | North side of table | Climb route: leg → rung → seat → table edge |
| Giant + his chair | South side of table | The hazard; loot within arm's reach |
| Table top | Wallet with 3 coins, ring, wristwatch, glowing phone, half sandwich, mug | Main loot |
| Tablecloth corner | Hangs off east edge to ~5 m above floor | Climb down, lower loot |
| Fridge (ajar) | North-east corner | Warm light pool; bonus loot later |
| Counter + sink | North wall | Out of bounds in early milestones |
| Window | Above sink | Moonlight source |

### Loot

| Item | Location | Carriers needed | Value |
|---|---|---|---|
| Coin | Floor under table | 2 | 10 |
| Coin ×3 | Table, by wallet | 2 | 10 each |
| Ring | Table, beside the giant's hand | 3 | 50 |
| Wristwatch | Table, far corner | 5 | 120 |

### Getting loot off the table — keep all three viable

| Method | Speed | Noise | Risk |
|---|---|---|---|
| Shove it off the edge | Instant | +60 spike | Wakes a stirring giant immediately |
| Lower it down the tablecloth | Slow | Low | Carriers exposed on the edge |
| Carry it down the chair | Slow | Medium | Hard with 3+ carriers |

---

## 7. Carry mechanics

- Each loot item has `requiredCarriers` and `value`.
- Players grab an item's grip points. With fewer grippers than required, the
  item can only be **dragged slowly and loudly**. At or above the requirement,
  it can be lifted and carried at normal speed.
- Carrying is **server-authoritative**. The group moves as one: the server
  averages grippers' input intent. Build on the synced-physics layer from
  Milestone F.
- A carrier disconnecting mid-carry drops their grip cleanly; the item
  continues or falls according to the remaining count.
- Dropping loot creates noise by mass × fall height (section 8).
- Delivery: loot entering the mousehole trigger is banked, removed and credited
  to the team total.

---

## 8. Noise model

One server-side float, 0–100, replicated to clients. Decays over time. Surface
type comes from a tag or physics material on each collider.

| Source | Noise |
|---|---|
| Walking on rug or cloth | 0 |
| Walking on tile or wood | +1 per second |
| Sprinting | +4 per second |
| Jump landing | +3 |
| Under-staffed drag | +3 per second |
| Loot dropped | mass × fall height, capped at 60 |
| Loot shoved off the table | +60 |
| Knocking a prop (mug, sandwich) | +10 to +25 |
| Phone buzz (scripted, every 90–150 s) | +15, not player-caused |
| Decay | −5 per second |

All values live in a ScriptableObject for tuning. HUD shows a noise meter.

---

## 9. The giant

Server-side state machine. Readable and predictable, not smart. Replicate only
state and a few parameters (head angle, light-cone direction); clients animate.

| State | Enter when | Behaviour | Tell |
|---|---|---|---|
| Asleep | noise < 30 for 5 s | Breathing; breath gust nudges light objects near his face | Steady snore |
| Stirring | noise ≥ 40 | Snore stops. Every 4–8 s, a random twitch or hand sweep across nearby table area; thieves hit are knocked down for 2 s | Snore breaks, mumbling, fingers twitch |
| Awake | noise ≥ 80, or dawn | Lifts head, picks up his phone, sweeps its light cone across table and floor | Head up, groan, bright cone |
| Search timeout | Awake 20 s with no sighting | Back to Stirring | Sigh, head down |

Hysteresis: thresholds going up (40 / 80) are higher than coming down (30 / 60).

**Catch:** a thief inside the light cone for 1.5 continuous seconds is grabbed
(reach animation) and placed under an upturned glass on the table. Anything
they carried drops.

**Rescue:** two free teammates push the glass together to tip it and release
the trapped thief.

**Everyone caught or dawn with no one home:** the night ends; unbanked loot is
lost; results screen shows banked loot.

---

## 10. Lighting and presentation (URP, night)

| Light | Type | Colour | Role |
|---|---|---|---|
| Moonlight | Directional through window, shadows on | Cool blue, low intensity | Main readability, long window-frame shadows |
| Fridge | Spot from the door gap | Warm yellow | Floor pool, contrast |
| Phone | Point light, pulses on buzz | Cold white | Lights the giant's face; becomes the search cone when awake |
| Ambient | — | Very dark blue | Under-table areas genuinely dark |

Post-processing: low bloom, vignette, subtle film grain, depth fog for scale,
dust particles in the moonbeam. Audio: snoring loop with state variants,
footsteps per surface, impacts, fridge hum.

---

## 11. Replace, reuse, remove

| Cube Arena system | Action |
|---|---|
| Backend, tickets, JWKS, fleet, sessions, rejoin | **Keep** |
| Dedicated server, connection approval, LAN Tier 0, CI, secret scan, Docker | **Keep** |
| Movement with prediction and reconciliation, stamina, crouch, crawl, jump | **Keep**, retune for climbing and carrying |
| Lobby and host-starts-match | **Keep** as pre-night lobby |
| Match clock | **Repurpose** as the night timer |
| Scoreboard | **Repurpose** as the team loot total and the results screen |
| HP bar | **Remove** unless a milestone needs it |
| Mana bar | **Remove** |
| Gold pickups, ArenaBuilder obstacle course | **Remove** after the kitchen replaces them |
| Code-built player template | **Replace** with Base Characters thief prefab |

Delete dead code instead of leaving it disabled. Keep tests green throughout.

---

## 12. Milestones

| # | Deliverable | Done when |
|---|---|---|
| **F** | Finish and verify in-progress work: 6 players everywhere, synced-physics layer, 30-crate test with 6 headless bot clients and per-client bandwidth from the Multiplayer Tools profiler, the `python3` hook fixed and confirmed running | 6 bots push and throw crates smoothly; bandwidth reported; hook proven to run |
| **0** | `docs/GAME_DESIGN.md` from this brief (loop, scale, carry rules, noise model, giant, out of scope). Rule changes in `CLAUDE.md` (section 3). | I approve |
| **1** | Asset download and import (section 4): all packs in `Assets/ThirdParty/`, URP materials, prefabs, `docs/ASSETS.md`, Mixamo retarget report | Everything imports without errors; I choose the sleep clip |
| **2** | Kitchen **greybox** from primitives at correct scale with surface tags. Thief prefab with animations. Climb route to the table. | A thief can reach the table top via the chair |
| **3** | **The coin test:** 2-player carry of the floor coin to the mousehole, banking, team total | 2 players on LAN carry and bank the coin; carrying feels good |
| **4** | Replace greybox with real assets; lighting pass; table loot; all three descent methods | Playtest: the kitchen looks like the kitchen, and all descent methods work |
| **5** | Noise model, noise HUD, giant with states and tells (placeholder animation is fine) | A clumsy team wakes him; a careful one doesn't |
| **6** | Giant animations, phone light cone, catch, glass rescue, night timer and dawn, results screen, return to lobby | Full night playable start to finish with 4–6 players |
| **7** | Proximity voice: evaluate options (Unity Vivox and alternatives), trade-off table first, then implement; loud speech adds noise | Whispering is safe, shouting is heard |

Stop after each milestone. For 3, 4 and 6, also give me a manual playtest
checklist.

---

## 13. Networking and quality requirements

- Everything that matters is server-authoritative: carry state, loot position,
  banking, noise, giant state, catches, glass rescue, night timer.
- Never trust client-supplied identity or positions; validate every ServerRpc
  for sender, rate and ranges (as the existing code does).
- Every new networked mechanic gets a headless bot test where practical:
  carry and bank, disconnect mid-carry, catch and rescue, 7th connection
  rejected.
- Performance target: 60 fps on a mid-range PC with 6 players and ~50 physics
  objects. Report per-client bandwidth at Milestones F, 3 and 6.
- LAN Tier 0 must keep working after every milestone.

---

## 14. Out of scope for now

Gear shop and upgrades, additional houses and levels, cat or other hazards,
Steamworks, settings menu and key rebinding, cloud deploy, story notes and
collectibles, controller support. Mention ideas for these in docs if useful,
but don't build them.

---

Start with **Milestone F**. Show me the plan first.