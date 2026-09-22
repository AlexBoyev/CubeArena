# Pocket Heist — Playtest checklist

For each milestone: what to test by hand, and what couldn't be verified
without a human. Per `AUTONOMOUS_RUN.md` section 1/5.

## Milestone 1 — Asset import + placeholder character + sleep-clip choice

**Verified automatically**: full headless Editor import (no errors), 59/59
backend tests, live bot-mode connection through the fixed bootstrap.

**Verified via Play-mode screenshot (by Claude, not a human)**: the four
sleep-pose candidates, seated positioning, root motion off.

**Needs your eyes** (not yet done, optional — the sleep-clip decision is
already made, this is just a sanity check if you want it): open
`Assets/Scenes/SleepClipPreview.unity` yourself and confirm candidate 4
still looks acceptable as a base to build on — it's an approximation
("experimental" per its own label), not a final pose.

## Milestone 2 — Kitchen greybox, thief prefab, climb route

**Verified automatically**: Windows client + dedicated server both rebuild
clean in batch mode with no compile errors; 59/59 backend tests; two
fresh-account bots walked from the mousehole, across the rug, up the
chair, and onto the table top, confirmed via server-side `[Climb]`/`[Pos]`
log traces (both settled at the table's real position/height, no
exceptions in any client or server log across either run).

**Verified via Play-mode screenshot (by Claude, not a human)**: two real
screenshots from a live bot-mode standalone client (not just the Editor
preview-scene path) — one showing "Red" walking on the rug with the real
Quaternius mesh (not primitive cubes, not a magenta missing-shader
fallback), mid-stride animation, correct nameplate and HUD; one showing
both "Red" and "Blue" on the table top next to the giant's legs (visible
for scale), each distinctly and correctly recoloured, both mid-walk-cycle.

**Needs your eyes**:
- The placeholder character's shirtless/no-clothing look (known issue,
  cosmetic, see docs/PROGRESS.md) — now visible on the thief too, not just
  the giant, since both share the same mesh/material.
- A real jump (Space bar) hasn't been checked by a human or a bot — the
  Jump_Start/Jump_Loop/Jump_Land animation states exist and compile, but
  this milestone's automated verification only exercised movement/crouch/
  climb, not jumping. Worth a quick manual check next time you're playing.
- Minor `_isClimbing` flicker right at the leg→seat and seat→table zone
  boundaries (cosmetic only, doesn't block the mechanic) — see if it reads
  as noticeable/annoying in person; if so it's a candidate for a small
  hysteresis fix later.
- General animation feel (crossfade timing, whether Sprint_Loop/Walk_Loop
  read as clearly different at a glance) — `ThiefAnimationSettings.asset`
  exposes the crossfade duration and jump timing thresholds for retuning
  without a code change if anything feels off.

## Milestone 3 — The coin test (multi-carrier loot, banking, team total)

**Verified automatically**: client + dedicated server both rebuild clean
in batch mode (twice — once before, once after a real bug fix, see below);
59/59 backend tests; two separate live bot-mode runs against a fresh
dedicated server each time, confirmed via server-side `[Loot]`/`[Bank]`
log traces:
- **2 bots** ("Red"/"Blue"): both gripped the floor coin
  (`grippers=2/2 carrying=True`), carried it toward the mousehole at the
  tuned `CarrySpeed` rate, and it banked cleanly
  (`[Bank] Loot_Coin(Clone) banked for 10 (grippers=2).`) — this is the
  milestone's actual done-criterion ("2 players on LAN carry and bank the
  coin"), confirmed with no exceptions in any client or server log.
- **1 bot** (isolating the under-staffed case, since the 2-bot run's bots
  gripped in near-lockstep and never produced an observable 1-gripper
  state): confirmed `grippers=1/2 carrying=False`, moving at the tuned
  `DragSpeed` rate — visibly slower than the 2-gripper carry rate,
  confirming drag and carry are genuinely different speeds.
- A real bug was found and fixed during this verification, not just
  assumed correct from code review: the coin's auto-added
  `CapsuleCollider` (from `GameObject.CreatePrimitive(Cylinder)`)
  mishandled its extreme non-uniform squash scale, resting at y≈0.39
  instead of the intended ~0.075 — fixed with an explicit `BoxCollider`,
  re-verified live afterward (rests at y≈0.08, as expected).
- A second real bug was caught before it could ship: the client's own
  `AddNetworkPrefab` registration list (`ClientBootstrap.ConnectToGameServer`)
  still referenced the retired `CrateController.CreateTemplate()` instead
  of the new `LootItem.CreateTemplate()` — left unfixed, this would have
  silently broken every client connection (NGO requires matching prefab
  hash lists between client and server) despite the server-side code
  looking correct in isolation.

**Not verified this session — environmental blocker, not a code issue**:
Visual/screenshot verification of the 2-bot carry was not possible this
session — the host desktop was remotely locked partway through
verification, which forces Unity's windowed client onto a null/non-
rendering graphics device (`Forcing GfxDevice: Null`) regardless of
`-nographics`, so any screenshot captured under that state is blank/black,
not real visual confirmation. The server log traces above confirm the
mechanic works correctly end-to-end (grippers=2/2 -> carrying=True ->
banked), but a human should visually confirm the coin-carry actually
*looks and feels* right (does it read clearly as "being carried," is the
carry height/speed pleasant to watch, does the gold coin read well against
the greybox floor) once at an unlocked desktop — this is exactly the kind
of check AUTONOMOUS_RUN.md's own "carrying feels good" done-criterion
implies but that a server log alone can't confirm.

**Needs your eyes**:
- The visual carry feel noted above — first priority once you're back at
  the machine.
- The old per-player scoreboard (Hold-Tab) still works but no longer
  reflects any active gameplay (nothing awards per-player score any
  more) — worth a glance to confirm it doesn't look broken/confusing
  sitting next to the new team-total readout, even though it's flagged
  for eventual removal (see docs/DECISIONS.md).
- `LootSettings.asset` exposes `GripRange`/`DragSpeed`/`CarrySpeed`/
  `CarryHeight`/`BankRadius` for retuning without a code change if the
  carry feel needs adjusting.

## Milestone 4 — Real assets, lighting, full loot, all three descent methods

**Status: STOPPED — a real gameplay/geometry bug, not the infra hang.**
See the STOPPED notice at the top of `docs/PROGRESS.md` and
docs/DECISIONS.md's "STOPPED after three genuine fix attempts" entry for
the full writeup. The prior dedicated-server registration hang (this
section used to describe it) is fully resolved — the machine reboot fixed
it, confirmed across 5 fresh-server launches this session. What's actually
blocking now: **no table-top loot item can be reached end-to-end via the
chair climb** — a bot reaches table height cleanly but then falls straight
through to the floor every time, across three different genuine fix
attempts. The Milestone 3 floor coin (ground-level, no climbing) still
works perfectly on the real M4 geometry.

**Verified via live testing this session**: dedicated server registration
(fully working now); the M3 floor coin's grip/carry/bank cycle on the real
M4 geometry (live 2-bot test, banked cleanly — `[Bank] ... banked for 10`);
both client and dedicated server rebuild clean through 4 full fix-rebuild-
retest cycles. Backend suite: 59/59.

**Not verified — blocked by the table-top climb bug above**:
- The chair climb route reaching a table-top item and staying there (it
  reaches table height but falls through every time — see docs/DECISIONS.md).
- Any of the five table-top loot items (3 wallet coins, ring, wristwatch)
  being gripped/carried/banked.
- The two new descent methods: shove (`F` while gripping) and the
  tablecloth climb-down route — both depend on already being on the table.
- The `CUBEARENA_BOT_TEST_MODE=lootdescent`/`banktest` verification bot
  routines completing (both built, both blocked by the same underlying bug).
- A proper "kitchen looks like a kitchen at night" screenshot — every
  screenshot captured this session (including the working floor-coin
  carry) shows flat, bright, daytime-like lighting with a plain procedural
  sky, not the described night pass. Not investigated this session (the
  climb blocker took priority) — worth a look once that's resolved, since
  it may be as simple as the lighting pass GameObjects not actually being
  built/applied, or may be a legitimate framing/angle artifact.

**Needs your eyes, once the table-top climb bug and the lighting question
above are both resolved and a live session is possible**:
- Whether the kitchen genuinely reads as "a kitchen at night" — the
  lighting pass (moonlight/fridge/phone/ambient + bloom/vignette/film
  grain/depth fog) was tuned by feel, not validated against a real render.
- Whether the table/chair visual proportions look acceptable — both are
  scaled to hit the *gameplay* anchors (`TableTopHeight`/`ChairSeatHeight`)
  exactly, not necessarily to look naturally proportioned (the table in
  particular may read as a bit tall/spindly — see docs/DECISIONS.md).
- Whether the single-stretched-panel walls/counter (real geometry, but not
  properly tiled — see docs/DECISIONS.md) look acceptable or too obviously
  stretched for this pass.
- Whether shoving an item off the table actually *feels* distinct/risky
  compared to carrying it down, given noise doesn't exist yet to
  differentiate them mechanically (that's Milestone 5).
- The 4 remaining loot items' placeholder visuals (recoloured/resized
  flattened cylinders, same shape as the floor coin) — acceptable as
  placeholders, or worth a slightly more distinct shape before Milestone 6?
