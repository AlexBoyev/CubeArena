# Pocket Heist — Game Design

Derived from `POCKET_HEIST_MASTER_PROMPT.md`, the master brief for the Cube
Arena → Pocket Heist pivot. This is the living design reference going
forward — update it as design decisions are made or revised during later
milestones, rather than treating the master prompt as the ongoing source of
truth once it and this document diverge.

## 1. Pitch and core loop

A crew of 2–6 thumb-sized thieves robs a sleeping giant's house, one night
per run. Heavy loot needs several thieves to carry it together. Teamwork
makes noise. Noise wakes the giant.

**Core loop**: sneak in through a mousehole → find loot → carry it back to
the mousehole → payout → (later, out of scope for now) spend earnings on
gear → next night, harder house.

**Night timer**: each run lasts until dawn (default 10 minutes, tunable via
a `ScriptableObject`, matching the noise model's tuning approach below). At
dawn the giant wakes regardless of noise level; anyone not back in the
mousehole is caught.

**Fail state**: the giant wakes, searches with his phone light, and catches
thieves. A caught thief is trapped under an upturned glass; free teammates
can rescue them. If everyone is caught, the night ends and unbanked loot is
lost.

**Story (light, environmental)**: the crew lives inside the walls of a human
town. Giants are ordinary people, not villains. Each house has an owner told
through objects and notes, not cutscenes — no cutscenes in early milestones.

**Art direction**: stylized low-poly, warm, comedic, lit at night. Ordinary
household objects at enormous scale are the visual identity. Reference feel:
R.E.P.O., RV There Yet?. Lighting matters more than model detail.

## 2. World scale

| Thing | Real | In game |
|---|---|---|
| **Scale factor** | — | **×25** |
| Thief height | 4 cm | **1.0 m** — thieves and loot physics stay at native scale so Unity physics behaves; only the environment and the giant are scaled up |
| Kitchen floor | 400 × 350 cm | 100 × 87.5 m |
| Ceiling | 260 cm | 65 m |
| Table top height | 75 cm | 18.75 m |
| Table top | 120 × 80 cm | 30 × 20 m |
| Chair seat | 45 cm | 11.25 m |
| Counter height | 90 cm | 22.5 m |
| Giant standing | ~175 cm | ~44 m |
| Coin (₪10) | 2.3 cm | 0.58 m disc |

This ×25 factor is fixed project-wide — every future level's dimensions
scale from real-world measurements the same way. Camera far-clip and shadow
distance/cascades need reviewing for the larger world (tracked as part of
the lighting pass, Milestone 4).

## 3. Level 1: "The Midnight Snacker" (kitchen)

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

## 4. Carry mechanics

- Each loot item has `requiredCarriers` and `value`.
- Players grab an item's grip points. With fewer grippers than required, the
  item can only be **dragged slowly and loudly**. At or above the
  requirement, it can be lifted and carried at normal speed.
- Carrying is **server-authoritative**. The group moves as one: the server
  averages grippers' input intent.
- A carrier disconnecting mid-carry drops their grip cleanly; the item
  continues or falls according to the remaining count.
- Dropping loot creates noise by mass × fall height (section 5).
- Delivery: loot entering the mousehole trigger is banked, removed, and
  credited to the team total.

**Implementation note (see `docs/NETCODE.md`'s "known limitation" entry)**:
this needs a different networked-authority model than `CrateController`'s
single-owner `ChangeOwnership` pattern, since loot needs multiple
simultaneous carriers on one object. Design work for this belongs to
Milestone 3 ("the coin test"), not this document.

## 5. Noise model

One server-side float, 0–100, replicated to clients. Decays over time.
Surface type comes from a tag or physics material on each collider.

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

All values live in a `ScriptableObject` for tuning. HUD shows a noise meter.

## 6. The giant

Server-side state machine. Readable and predictable, not smart. Replicate
only state and a few parameters (head angle, light-cone direction); clients
animate.

| State | Enter when | Behaviour | Tell |
|---|---|---|---|
| Asleep | noise < 30 for 5 s | Breathing; breath gust nudges light objects near his face | Steady snore |
| Stirring | noise ≥ 40 | Snore stops. Every 4–8 s, a random twitch or hand sweep across nearby table area; thieves hit are knocked down for 2 s | Snore breaks, mumbling, fingers twitch |
| Awake | noise ≥ 80, or dawn | Lifts head, picks up his phone, sweeps its light cone across table and floor | Head up, groan, bright cone |
| Search timeout | Awake 20 s with no sighting | Back to Stirring | Sigh, head down |

Hysteresis: thresholds going up (40 / 80) are higher than coming down
(30 / 60).

**Catch**: a thief inside the light cone for 1.5 continuous seconds is
grabbed (reach animation) and placed under an upturned glass on the table.
Anything they carried drops.

**Rescue**: two free teammates push the glass together to tip it and
release the trapped thief.

**Everyone caught or dawn with no one home**: the night ends; unbanked loot
is lost; results screen shows banked loot.

## 7. Lighting and presentation (URP, night)

| Light | Type | Colour | Role |
|---|---|---|---|
| Moonlight | Directional through window, shadows on | Cool blue, low intensity | Main readability, long window-frame shadows |
| Fridge | Spot from the door gap | Warm yellow | Floor pool, contrast |
| Phone | Point light, pulses on buzz | Cold white | Lights the giant's face; becomes the search cone when awake |
| Ambient | — | Very dark blue | Under-table areas genuinely dark |

Post-processing: low bloom, vignette, subtle film grain, depth fog for
scale, dust particles in the moonbeam. Audio: snoring loop with state
variants, footsteps per surface, impacts, fridge hum.

## 8. Characters

| Role | Source | Setup |
|---|---|---|
| Giant | Quaternius Base Characters, "Regular" proportions | Scaled ×25, seated in a chair, sleep clip |
| Thieves (placeholder) | Quaternius Base Characters, "Teen" proportions | Native scale (1 m), recoloured per slot |

Thief slot colours: red, blue, green, yellow, purple, orange (unchanged from
Cube Arena's six-slot system). Quaternius locomotion clips (idle, walk, run,
crouch, jump, climb, push/carry) drive thieves, mapped onto the existing
replicated movement state (`PlayerController`).

## 9. Networking and quality requirements

- Everything that matters is server-authoritative: carry state, loot
  position, banking, noise, giant state, catches, glass rescue, night timer.
- Never trust client-supplied identity or positions; validate every
  `ServerRpc` for sender, rate, and ranges — as the existing code already
  does for movement and crate grab/throw.
- Every new networked mechanic gets a headless bot test where practical:
  carry and bank, disconnect mid-carry, catch and rescue, 7th connection
  rejected.
- Performance target: 60 fps on a mid-range PC with 6 players and ~50
  physics objects. Report per-client bandwidth at Milestones F (done), 3,
  and 6.
- LAN Tier 0 must keep working after every milestone.

## 10. Out of scope for now

Gear shop and upgrades, additional houses and levels, cat or other hazards,
Steamworks, settings menu and key rebinding, cloud deploy, story notes and
collectibles, controller support. Ideas for these can be noted in docs if
useful, but not built yet.

## 11. Replace, reuse, remove (from Cube Arena)

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
| Gold pickups, `ArenaBuilder` obstacle course | **Remove** after the kitchen replaces them |
| Code-built player template | **Replace** with Base Characters thief prefab |

Delete dead code instead of leaving it disabled. Keep tests green
throughout.
