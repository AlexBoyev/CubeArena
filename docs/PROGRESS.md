# Pocket Heist — Progress

Read this first in any new session. Current milestone, what's done, what's
next, known issues. Updated after every completed step per
`AUTONOMOUS_RUN.md`.

## STOPPED: Milestone 4 code complete but unverified live — a real, unrelated
## infrastructure blocker, per AUTONOMOUS_RUN.md section 3 rule 3 (three
## genuine fix attempts, none solved it)

**What's stopped, and why**: all of Milestone 4's code is written and
compiles cleanly in both a fresh client and dedicated server build (confirmed
byte-present in both — see below), and the backend suite is still 59/59. But
the milestone's required live bot-mode verification (and therefore also the
Play-mode screenshot, which needs a live server to connect to) could not run:
launching *any* dedicated server right now hangs forever on
`FleetClient.RegisterAsync` — specifically inside `PostAsync`'s
`UnityWebRequest` POST to `/fleet/register`, confirmed via bracketing
diagnostic logging (`Debug.Log` calls added and removed again around every
step of `ServerBootstrap.RunAsync`) that execution reaches "before
FleetClient.RegisterAsync" and never reaches "after." This is **not** a bug
in this milestone's own diff — `FleetClient.cs` is completely unmodified —
and this exact registration path demonstrably worked earlier in this same
session (the Milestone 3 fork's live 2-bot banking test, and this Milestone
4 fork's own earlier work, both required a dedicated server to have
registered successfully). Something changed in the environment partway
through this very long session, most likely from the sheer number of Unity
batch processes started and force-killed today (build hangs, stalled
processes, etc. — see the session record).

**Three genuine fix attempts, all failed** (per AUTONOMOUS_RUN.md's own
"don't loop" rule, stopping here rather than trying a fourth):
1. Pointed the server at `http://127.0.0.1:8080` instead of
   `http://localhost:8080` (ruling out a DNS/hostname-resolution quirk) —
   same hang.
2. Restarted the backend's Docker container (`docker compose restart api`)
   to rule out a degraded Kestrel-side connection/thread-pool state — same
   hang, even though a direct `curl -X POST` to the exact same endpoint,
   with the exact same headers, from the exact same machine, at the exact
   same time, succeeded instantly (200, ~27ms) — this rules out the backend
   itself being unhealthy.
3. Set `UploadHandlerRaw.contentType` explicitly (a known Unity gotcha:
   relying only on a manually-set `Content-Type` *header* without also
   setting the upload handler's own `contentType` property can leave a
   `UnityWebRequest`'s body-framing metadata inconsistent in some versions)
   — rebuilt, retested, same hang. Reverted this change since it didn't
   help and I don't want an unverified "fix" claim sitting in the code —
   `FleetClient.cs` is back to byte-identical with what Milestone 3 shipped.

**What I could confirm independently of the live test**: both the client
and dedicated server executables were rebuilt after every source change and
searched directly for Milestone 4 symbol names in their compiled managed
DLLs (`ServerShove`, `WristwatchSpawnPosition`, `LootDescentPhase`,
`TablecloudClimb`, `RunLootDescentTestBotBehavior`, `BotTestMode` — all
present in both builds), so the actual code is real and built correctly;
what's unverified is only its *runtime networked behavior*. Backend suite:
59/59.

**What a fresh session (or you) should do next**: this is squarely a "stop
and tell me" case per AUTONOMOUS_RUN.md section 3 rule 3, not something to
keep guessing at. Likely worth trying, roughly in order of cheapness: (a) a
full reboot of this machine (clears any accumulated Windows-level socket/
handle state from today's many force-killed Unity processes — my
`Get-NetTCPConnection` check didn't show obvious exhaustion, but it's the
cheapest thing that rules out a wide class of causes at once), (b) if that
doesn't fix it, checking whether the *client* build can register with the
*same* backend right now (isolates whether this is specific to headless
`-batchmode -nographics` server builds or affects any Unity process), (c)
capturing a packet trace (`netstat`/Wireshark) on the hung POST's specific
TCP connection to see whether the request body is actually leaving the
process at all — if Wireshark shows a fully-sent, well-formed HTTP request
with a correct `Content-Length` and the response never arrives, that
implicates the backend/Docker networking layer despite the curl test;
if the request itself is incomplete/never sent, that implicates Unity's
own `UnityWebRequest`/networking module specifically.

Once resolved: re-run this milestone's live bot-mode verification (chair
climb still works with real geometry, a new loot item can be gripped/
carried/banked, the shove descent path works via the new `lootdescent` bot
test mode — `CUBEARENA_BOT_TEST_MODE=lootdescent`), capture and inspect a
Play-mode screenshot, then finish per the usual pattern (this file,
docs/DECISIONS.md, docs/PLAYTEST.md, commit, push).

## Status: Milestone 4 (real assets, lighting, full loot, descent methods) —
## code complete, live verification blocked (see above)

### Milestone 1 — done, on `pocket-heist` (not merged to master yet —
### per AUTONOMOUS_RUN.md section 2, master gets it only at Milestone 6's end)
- KayKit Restaurant Bits (19 curated kitchen models) + Kenney Furniture Kit
  (`rugRectangle`) + Kenney Impact/Interface Sounds imported to
  `Assets/ThirdParty/`. Full list and licences in `docs/ASSETS.md`.
- Placeholder giant/thief character (Quaternius `Superhero_Male_FullBody` —
  the brief's assumed "Regular"/"Teen" proportions are paid-SOURCE-tier
  only, not in the free download) structured so swapping to a real mesh
  later is a prefab-reference change, not a rebuild. See docs/ASSETS.md
  "Placeholder character structure."
- Sleep-clip decision made: candidate 4 (custom seated + head-on-arms lean,
  built from Humanoid muscle curves since no stock clip fit), refined and
  verified via real Play-mode screenshots after fixing several real bugs
  along the way (root motion, seat height, lean magnitude, camera FOV/label
  bleed). Full writeup in docs/ASSETS.md.
- Bootstrap fixed so Play mode on a non-gameplay scene (like the sleep
  preview) doesn't launch the whole networked game — `CubeArena.Shared.
  BootConfig` gates `ServerBootstrap`/`ClientBootstrap` auto-boot on the
  active scene name. Verified: rebuild + live bot-mode connect, LAN Tier 0
  unaffected.
- `.claude/settings.json` permission allowlist + `AUTONOMOUS_RUN.md` committed.

### Milestone 2 — done, on `pocket-heist` (not merged to master yet —
### per AUTONOMOUS_RUN.md section 2, master gets it only at Milestone 6's end)
Deliverable: kitchen greybox at ×25 scale with surface tags from primitives,
thief prefab with animations, climb route to the table. Done when a thief
can reach the table top via the chair. All three delivered and verified.

**Done so far:**
- `KitchenBuilder.cs` (new, replaces `ArenaBuilder` as the level geometry —
  `ArenaBuilder.cs` left in place but unreferenced) builds "The Midnight
  Snacker" kitchen at real ×25 scale: floor (tagged `SurfaceType.Tile`),
  walls with a mousehole gap, a rug route (tagged `SurfaceType.Rug`), table
  (18.75m top height), fridge/counter greybox blocks, giant placeholder
  parked at the table, and a chair climb route (leg → seat → table edge,
  each climbable segment marked with a plain `Climbable` marker component
  rather than a Unity tag/layer, per CLAUDE.md's no-hand-editing-
  ProjectSettings rule).
- New **climbing movement mode**: server-authoritative (gated by proximity
  to any `Climbable` via `Physics.OverlapSphereNonAlloc`), predicted
  client-side the same way normal movement is. Reuses the existing
  world-space input/RPC pipeline entirely — no new input scheme. Tunables
  (`ClimbSpeed`, `DetectionRange`, `HorizontalShiftSpeed`) live in a new
  `ClimbSettings` ScriptableObject (`Assets/Resources/ClimbSettings.asset`),
  per AUTONOMOUS_RUN.md's tunables rule.
- `SpawnPoints.cs` rewritten to spawn near the new mousehole instead of the
  old arena's corners. `PickupController.GetRandomPosition()` fixed to use
  the kitchen's real floor bounds.
- Pickup/crate spawning temporarily disabled in `ServerBootstrap` (calls
  commented, not deleted) — see Decisions.
- **Verified via live bot-mode test**: a bot walked from the mousehole,
  crossed the rug, climbed the chair leg, then the seat-to-table strip, and
  reached the table top (final logged position ≈(10.9, 18.83, 15.1),
  matching `TableCenter`=(10,15) at `TableTopHeight`=18.75). Full server
  log trace of the run is in the session record. Milestone 2's core
  done-criterion is met.
- Backend suite re-verified green after all of the above: 59/59.

- **Thief prefab with real animations, done.** `PlayerController.CreateTemplate`
  no longer builds a primitive-cube body — it instantiates
  `ThiefModel_Placeholder` (the real Quaternius Humanoid mesh) under
  "Visual" and wires its `Animator` to a new shared `ThiefLocomotion`
  AnimatorController (`Assets/Resources/ThiefLocomotion.controller`, built
  by `ThiefAnimatorBuilder.cs` from 8 states pulled out of
  `UAL1_Standard.fbx`'s 43 clips: `Idle_Loop`, `Walk_Loop`, `Sprint_Loop`,
  `Crouch_Idle_Loop`, `Crouch_Fwd_Loop`, `Jump_Start`, `Jump_Loop`,
  `Jump_Land`). `PlayerController.UpdateLocomotionAnimation` drives it
  purely via `Animator.CrossFade(stateName, ...)` — no transition graph —
  selecting state from climbing (highest priority) > airborne/jump
  (inferred from vertical position delta, no new networked state needed) >
  pose-based idle/walk/sprint/crouch. Blend/threshold timings live in a new
  `ThiefAnimationSettings` ScriptableObject
  (`Assets/Resources/ThiefAnimationSettings.asset`), per the run's
  tunables rule. Per-slot recolouring needed no new code at all — the
  existing `ApplyColor`/`_renderers` mechanism already generalizes over
  any `Renderer`, mesh or primitive alike (see docs/DECISIONS.md).
  The old scale-squash crouch/crawl visual and the manual limb-swing
  walk-cycle code are both gone, replaced by the real animation clips.
- New reusable verification tool: `ClientConfig.ScreenshotPath`/
  `ScreenshotDelaySeconds` (env vars `CUBEARENA_SCREENSHOT_PATH`/
  `CUBEARENA_SCREENSHOT_DELAY`) — `ClientBootstrap.Update` captures one
  `ScreenCapture.CaptureScreenshot` at a fixed delay after launch when set.
  Lets a live bot-mode standalone client (not just the Editor-only
  preview-scene path `SleepPreviewScreenshotter` uses) produce a real
  Play-mode screenshot for visual verification — used to confirm this
  milestone, will be reused for Milestones 3-6.
- **Verified via live bot-mode test with the real mesh**: two bots
  ("Red", "Blue"), each on its own fresh account, walked from the
  mousehole, climbed the chair, and reached the table top — confirmed both
  via server log (`[Climb] Blue climbing=True/False ... y≈18.8` settling at
  `(9.29, 18.83, 14.91)`, matching `TableCenter`/`TableTopHeight`) and via
  two real screenshots (`preview_screenshots/thief_walk.png`,
  `thief_climb.png` — not committed, gitignored, but inspected directly):
  the first shows "Red" mid-stride on the rug with the real tinted mesh,
  correct nameplate, and HUD; the second shows both "Red" and "Blue" on
  the table top next to the giant's legs (visible for scale), each
  correctly and distinctly recoloured, both mid-walk-cycle. No exceptions
  in either client log or the server log across either run.
- Backend suite re-verified green after all of the above: 59/59.
- `.claude/settings.json`'s deny list further narrowed this stretch (at
  the user's explicit request) — `rm`/`Remove-Item`/`cd` denials removed,
  only `git push --force`/`git reset --hard` remain denied.

**Known imperfections (not blocking, not yet fixed):**
- `_isClimbing` flickers true/false a few times right at zone boundaries
  (~y 10.6–11.3 leg→seat, ~y 18.6–19.0 seat→table) — cosmetic, doesn't
  affect the animation state selection meaningfully (climbing still reads
  correctly enough for Walk_Loop to keep playing through the flicker).
- No dedicated climb or crawl/prone animation clip exists in the
  Quaternius library (43 clips total, not the ~86 docs/ASSETS.md's import
  note estimated) — Walk_Loop is reused for climbing, Crouch_* clips for
  crawling. See docs/DECISIONS.md.
- Jump/airborne animation state (Jump_Start/Loop/Land) is inferred purely
  from frame-to-frame vertical position delta, since remote players don't
  replicate a "grounded" flag — untested against an actual jump during
  this stretch's verification (the climb-route bot never jumps); the
  climb and locomotion states that were actually exercised are confirmed
  bug-free, but a real jump should get a quick manual sanity check next
  time a human is at the keyboard.

## Known issues / not yet fixed
- Placeholder character material only maps skin textures to every renderer
  — renders shirtless/in briefs, no clothing. Previously only visible on
  the giant; now also visible on the thief (both wrapper prefabs nest the
  same `SuperheroMale_CharacterModel`), confirmed in this milestone's own
  verification screenshots. Cosmetic, not blocking. Revisit when the
  placeholder character is replaced.
- Kenney Furniture Kit's `rugRectangle` is vertex-coloured (no texture
  atlas) — needs a URP vertex-colour shader/Shader Graph before it renders
  correctly. **Correction**: this belongs to Milestone 4's "replace
  greybox with real assets" pass, not Milestone 2 — an earlier plan note
  below had folded it into Milestone 2 by mistake; Milestone 2's greybox
  route uses plain primitives with surface tags only, no real Kenney mesh
  placed yet.
- `_isClimbing` boundary flicker, see above.
- `Assets/Scenes/SleepClipPreview.unity` and its supporting Editor scripts
  (`SleepPreviewBuilder.cs`, `SleepPreviewScreenshotter.cs`) and generated
  assets (`Preview_*.controller`, `Custom_HeadOnArms.anim`) are still in the
  repo — safe to delete once Milestone 2's real kitchen scene exists and
  this comparison is no longer needed.

## Milestone 3 — done ("the coin test")

**Scope correction**: AUTONOMOUS_RUN.md section 4's own summary ("all four
loot items, banking at the mousehole, team total... all three table-descent
methods") reads as spanning this run's Milestones 3+4 together, not
Milestone 3 alone — the master prompt's own per-milestone table
(POCKET_HEIST_MASTER_PROMPT.md section 12) is explicit: Milestone 3 is
**"the coin test"** — one item (the floor coin, 2 required carriers, 10
value, per section 6's loot table), 2-player carry to the mousehole,
banking, team total. Done when 2 players on LAN can carry and bank the
coin and carrying feels good. The other three items (ring, wristwatch, the
3 table coins) and all three descent methods (shove/tablecloth/chair) are
Milestone 4's job, alongside the real-asset/lighting pass. Building the
underlying multi-carrier system generally now (so M4 just adds more
`LootItem` instances) but only exercising/shipping the one coin this
milestone, per the master prompt's own done-criterion.

**Design** (see docs/DECISIONS.md's "Multi-carrier loot authority model"
entry for the pointer already sketched, and docs/NETCODE.md's "known
limitation" entry for why `CrateController`'s current per-holder
`NetworkObject.ChangeOwnership()` pattern can't support 2+ simultaneous
carriers): loot stays **permanently server-owned**; each gripping client
sends its own input intent via `ServerRpc`, the server averages all
current grippers' intent and moves the item itself (still via
`NetworkRigidbody`/`NetworkTransform` for the physics/replication layer —
only the *authority* model changes, not the underlying sync mechanism).
Below `requiredCarriers` grippers: slow drag speed + no lift (item stays
grounded, dragged). At/above `requiredCarriers`: full carry speed, lifted.
A carrier disconnecting mid-carry just removes their grip from the average
— server-side cleanup already has a natural place to hook this via
`OnNetworkDespawn`/NGO's disconnect callback, the same way `PlayerController`
already tracks `ActiveServerPlayers`. Banking: a trigger volume at
`KitchenBuilder.MouseholePosition` that despawns the loot item and credits
its `value` to a new shared team-total `NetworkVariable<int>` (not
per-player `PlayerController.AddScore`, which is Cube Arena's old
competitive-score system — master prompt section 11 says "Scoreboard →
repurpose as the team loot total," so decide during implementation whether
that means literally reusing the scoreboard NetworkVariable/UI plumbing
with new semantics, or a new one; log whichever as a decision).

**Built:**
- `LootItem` (`Assets/Scripts/Shared/LootItem.cs`, new) — the multi-carrier
  replacement for `CrateController`'s single-owner authority. Permanently
  server-owned (`NetworkTransform`/`NetworkRigidbody` left at their default
  `AuthorityModes.Server`, unlike `CrateController`'s deliberate `.Owner`
  override). Server-side `Dictionary<ulong, PlayerController>` grip
  membership; `FixedUpdate` (server-only) moves the item toward the average
  position of its current grippers, at `DragSpeed` while below
  `requiredCarriers` (grounded) or `CarrySpeed` at/above it (lifted to
  `CarryHeight`). Banking is a plain horizontal-distance check against
  `KitchenBuilder.MouseholePosition` (`BankRadius`), not a physics trigger —
  simpler than adding scene geometry for it. Tunables
  (`GripRange`/`DragSpeed`/`CarrySpeed`/`CarryHeight`/`BankRadius`) in a new
  `LootSettings` ScriptableObject (`Assets/Resources/LootSettings.asset`),
  per the run's tunables rule.
- `PlayerController`: the old crate grab/throw/push code (`RequestGrabServerRpc`,
  `RequestThrowServerRpc`, `OnControllerColliderHit`'s push logic, the
  crate-seeking bot behavior) is fully replaced by a single
  `RequestToggleGripServerRpc()` (E toggles grip/release — no throw; loot is
  only ever carried) and a coin-test bot routine (`RunBotBehavior` ->
  `FindNearestVisibleLootItem`/walk-to-mousehole-while-gripping). The
  Milestone 2 climb-route bot fallback (`RunClimbTestBotBehavior`) is
  retired — that milestone is closed and documented, and bots now default
  to the coin test instead. `_isGrippingLoot` (a `NetworkVariable<bool>`)
  is the client-visible mirror of server-side grip state, read by both the
  real-keyboard and bot input paths to know whether E means grip or
  release.
- `MatchManager`: new `BankedLootTotal` (`NetworkVariable<int>`) and
  `AddBankedLoot(int)`, reset in `ResetForNewRound`. A new NetworkVariable
  rather than a literal repurpose of `PlayerController._score` (Cube
  Arena's old per-player competitive score, left in place but no longer
  the HUD's main readout) — see docs/DECISIONS.md.
- `ClientBootstrap`: the top-center match panel now shows the team's
  banked loot total alongside the clock (`"Loot: N"`, gold-tinted). The
  per-player Hold-Tab scoreboard is untouched (still reads the old
  per-player `_score`, now vestigial — see Known issues).
- `ServerBootstrap`/`ClientBootstrap`: both sides' `AddNetworkPrefab` lists
  updated to register `LootItem.CreateTemplate()` in place of
  `CrateController.CreateTemplate()` — **this had to be caught and fixed
  explicitly**: NGO requires the client and server's registered network
  prefab lists to match exactly, and the client's own registration call
  was easy to miss when only the server-side spawn logic was the obvious
  target. Left unfixed, this would have silently broken every client
  connection (prefab hash mismatch) despite the server working "fine" in
  isolation.
- One loot item spawned: the floor coin (`KitchenBuilder.LootCoinSpawnPosition`,
  2 required carriers, value 10, per the master prompt's section 6 loot
  table). A flattened gold cylinder placeholder (real coin meshes are
  Milestone 4's job).
- Real bug found and fixed via live testing, not just code review:
  `GameObject.CreatePrimitive(PrimitiveType.Cylinder)` auto-adds a
  `CapsuleCollider`, which does not handle the coin's extreme non-uniform
  squash scale (`localScale` (0.8, 0.075, 0.8)) correctly — it collided
  like a near-sphere of radius ~`CoinRadius` (confirmed live: the coin
  rested at y≈0.39, not the ~0.075 half-thickness expected). Fixed by
  destroying the auto-added `CapsuleCollider` and adding an explicit
  `BoxCollider` sized to the mesh's own local bounds, which does respect
  non-uniform scale correctly. Re-verified live afterward (coin rests at
  y≈0.08, matching expectation).

**Verified via live bot-mode tests** (three separate live runs, fresh
dedicated server + fresh accounts each time, stale-`GameServer`-row race
checked via `docker exec compose-postgres-1 psql` before each):
- **2-bot run** ("Red"/"Blue"): both gripped the coin (`grippers=2/2`),
  the transition log fired `carrying=True`, both then walked toward the
  mousehole with the item following at a rate consistent with `CarrySpeed`
  (~72 units over ~24s of logged movement, ≈3 m/s), and it banked cleanly:
  `[Bank] Loot_Coin(Clone) banked for 10 (grippers=2).` No exceptions in
  either client or the server log. **This is the milestone's actual
  done-criterion** ("2 players on LAN carry and bank the coin").
- **1-bot run** (isolating the under-staffed case, since the 2-bot run's
  bots gripped in near-lockstep and never produced a 1-gripper state to
  observe): confirmed `grippers=1/2 carrying=False`, moving at ≈1.2 m/s
  (matching `DragSpeed`'s default exactly) — i.e. visibly slower than the
  2-gripper carry rate, confirming drag vs. carry are genuinely different
  speeds, not just different in name.
- Backend suite re-verified green after all of the above: 59/59.
- Client + dedicated server both rebuild clean in batch mode, twice (once
  before the collider fix, once after) — zero compile errors either time.

**Not verified this session — environmental blocker, not a code issue:**
A Play-mode screenshot of the 2-bot carry was attempted but the host
machine's desktop was remotely locked during this stretch, which forces
Unity's windowed client onto a null/non-rendering graphics device
(`Forcing GfxDevice: Null` in the client log) regardless of `-nographics`
being passed or not — a screenshot captured under that device is blank/
black, not real visual verification, and retrying doesn't change the
outcome while the desktop stays locked. The functional behavior is fully
confirmed via the server log traces above; only the "does it *look* right"
check is outstanding. See docs/PLAYTEST.md.

**Known imperfections / not yet done:**
- The old per-player `PlayerController._score`/`AddScore`/`ResetScore`
  and the Hold-Tab per-player scoreboard are still present and functional
  but no longer meaningfully used by any gameplay (nothing awards
  per-player score any more — `PickupController`, the only thing that
  ever did, has been disabled since Milestone 2). Left as-is rather than
  removed this milestone — see docs/DECISIONS.md's "not fully repurposed"
  entry.
- `CrateController.cs` itself is left in the tree, fully unreferenced
  (like `ArenaBuilder.cs`/`PickupController.cs`) — not deleted this
  milestone, per the established "defer full dead-code removal" pattern.
- `LootItem`'s "under-staffed drag" and "carry" both move the item toward
  the average gripper *position* rather than literally integrating each
  gripper's live input direction — a simplification flagged and reasoned
  through in docs/DECISIONS.md, not a bug, but worth knowing if a future
  milestone wants tighter "feels like the team is actually pulling it"
  fidelity.

## Milestone 4 — not started

Deliverable per POCKET_HEIST_MASTER_PROMPT.md section 12: replace the
greybox with real assets, a lighting pass, the full table loot set, and
all three descent methods. Done when: "the kitchen looks like the
kitchen, and all descent methods work" (playtest).

**Scope, concretely:**

1. **Real geometry**, replacing `KitchenBuilder`'s primitives with the 19
   already-imported KayKit Restaurant Bits models (see docs/ASSETS.md —
   `kitchentable_A_large`, `chair_A`, `fridge_A`, `kitchencounter_sink`,
   `kitchencounter_straight_A`, `wall`, `wall_window_closed`,
   `wall_doorway`, `door_A`, plus tableware/food props for set dressing:
   `plate`, `plate_dirty`, `food_burger`, `pot_A`, `pan_A`, `jar_A_medium`,
   `knife`, `cuttingboard`, `stove_single` — per section 6's table
   description, "wallet with 3 coins, ring, wristwatch, glowing phone,
   half sandwich, mug" should be visible on the table top, using whatever
   of these props reads closest). Geometry-critical pieces (table, chair,
   walls, fridge, counter) are must-have; food/tableware set-dressing is
   nice-to-have, not blocking the done-criterion. The existing `Climbable`/
   `SurfaceType` marker components need to move from the primitive
   placeholders onto the real meshes' colliders — don't lose the working
   climb route or noise-model surface tags from Milestone 2 in the swap.
2. **Kenney vertex-colour URP shader for the rug** (`rugRectangle`,
   already imported, no texture atlas — see docs/ASSETS.md's note) —
   deferred from Milestone 2 to here, see the correction in the old
   Milestone 2 plan note below.
3. **Lighting pass** per section 10: moonlight (directional through the
   window, cool blue, low intensity, shadows on), fridge spot (warm
   yellow, floor pool), phone point light (cold white — static placement
   is fine this milestone, the "pulses on buzz"/search-cone behavior is
   Milestone 6's giant-state-machine job), ambient very dark blue
   (under-table areas genuinely dark). Post-processing: low bloom,
   vignette, subtle film grain, depth fog for scale; dust particles in
   the moonbeam if time allows (nice-to-have).
4. **Full loot set**: the 3 remaining table coins (by the wallet, 2
   carriers, value 10 each), the ring (beside the giant's hand, 3
   carriers, value 50), the wristwatch (far corner, 5 carriers, value
   120) — all as more `LootItem` instances per Milestone 3's now-general
   system, per section 6's loot table. The floor coin from Milestone 3
   stays as-is.
5. **All three descent methods** (section 6): the chair climb route
   already works (Milestone 2). Add:
   - **Shove off the edge**: instant, drops the item straight off the
     table (real physics fall, not a teleport) — the noise spike (+60)
     itself is Milestone 5's job (noise model doesn't exist yet), so this
     milestone just needs the shove action and the fall; leave an obvious
     hook point for Milestone 5 to add the noise emission, the same
     pattern `LootItem`'s under-staffed drag already left for noise.
   - **Lower down the tablecloth**: a slower, controlled descent at the
     tablecloth corner (east edge, per section 6) — likely a second
     `Climbable`-style zone thieves can use while gripping loot, moving
     it down at a controlled rate rather than a free fall. Design the
     specifics during implementation and log the decision.
   Playtest done-criterion is explicit that all three must stay viable,
   not just work — don't let one trivially dominate the other two.

**Built** (see the STOPPED notice at the top of this file for why live
verification is outstanding):

1. **Real geometry, layered over unchanged Milestone 2 collision.** 10 new
   wrapper prefabs under `Assets/Resources/Kitchen/` (built by
   `Assets/Editor/PocketHeist/KitchenAssetPrefabBuilder.cs`, same
   runtime-loadable-wrapper convention as `GiantModel_Placeholder`) nest
   the real KayKit `kitchentable_A_large`/`chair_A`/`fridge_A`/
   `kitchencounter_straight_A`/`kitchencounter_sink`/`wall`/
   `wall_window_closed`/`wall_doorway`/`door_A` and the Kenney `rugRectangle`
   models. `KitchenBuilder.cs` now instantiates these as **purely visual**
   dressing (their own colliders stripped) positioned/scaled to align with
   the exact same invisible primitive colliders and `Climbable` zones
   Milestone 2 already validated — deliberately *not* re-deriving climb/
   collision geometry from the real meshes' own bounds (the chair especially
   is a single unlabelled mesh with no discoverable seat sub-part), to avoid
   risking the working climb route for a cosmetic gain. Table/chair visual
   scale is derived from real probed mesh dimensions to land at the exact
   `TableTopHeight`/`ChairSeatHeight` anchors; fridge/counter get their own
   real `BoxCollider`s (non-climb items, safe to fully replace); walls stay
   collision-primitive with a single real wall panel stretched per segment
   (tiling many modules was descoped — see docs/DECISIONS.md).
   Food/tableware set-dressing props (plate, burger, pot, etc.) were
   descoped entirely this pass — explicitly nice-to-have per the plan above,
   and this milestone already grew large from the loot/descent-method work.
2. **Vertex-colour URP shader**: `Assets/Shaders/VertexColorURP.shader`, a
   hand-written HLSL forward-lit pass (URP main-light Lambertian, not full
   PBR) reading per-vertex colour — built as plain shader source rather
   than a Shader Graph asset since Shader Graphs are authored interactively
   in the Editor, not scriptable/batch-buildable like everything else in
   this project. Compiled clean, applied to a new `RugVertexColor.mat`
   referenced by `Kitchen_Rug.prefab`.
3. **Lighting pass**: moonlight (directional, cool blue, shadows on,
   angled through the window), fridge spot (warm yellow), a static phone
   point light (cold white — no pulse/search-cone yet, that's Milestone
   6's state-machine job), very dark blue ambient, and a URP volume (low
   bloom, vignette, subtle film grain, depth fog). Dust particles in the
   moonbeam were skipped (nice-to-have).
4. **Full loot set**: `ServerBootstrap` now spawns all six items from
   section 6's loot table (the Milestone 3 floor coin plus 3 wallet coins,
   the ring, the wristwatch) as `LootItem` instances with a new `LootKind`
   enum (`Coin`/`Ring`/`Wristwatch`) driving purely cosmetic per-kind
   colour/size (no jewelry/watch meshes in the imported KayKit set —
   placeholders, same as the floor coin already was).
5. **All three descent methods.** The chair climb (Milestone 2) is
   unchanged. The other two share one underlying fix, not two separate
   systems: `LootItem`'s carried/dragged height is now the *grippers' own
   current Y* plus a small offset, instead of a fixed absolute height —
   this alone makes carrying an item down the chair (or the new tablecloth
   route) work automatically, since a gripper's own climbing already moves
   them in 3D and the item just follows. **Tablecloth**: a new second
   `Climbable` zone at the table's east edge, spanning floor-to-table-top
   directly rather than literally stopping "~5m above the floor" (see
   docs/DECISIONS.md). **Shove**: a new F key (while gripping) calls
   `LootItem.ServerShove`, which clears all grippers and hands the item to
   real, un-overridden Rigidbody physics with an outward+upward velocity —
   `FixedUpdate` was restructured so the "nobody's gripping it" case no
   longer force-glides the item toward a hardcoded floor height every tick
   (the Milestone 3 version), which also fixes a latent bug where a
   dropped table-top item would have sunk straight through the table
   toward the floor. The Milestone 5 noise-spike hook point (+60 on shove)
   is left as an explicit comment, matching the pattern Milestone 3 already
   established for under-staffed-drag noise.
6. **New bot-mode verification routine**: `CUBEARENA_BOT_TEST_MODE=lootdescent`
   drives a bot through climb-to-table → grip a table-top item → shove —
   built specifically to verify this milestone's two genuinely new code
   paths (a table-top grip, and `ServerShove`'s real-physics fall)
   end-to-end via server log, the same way Milestone 2's climb-test and
   Milestone 3's coin-test bots verified those. **Not yet actually run** —
   blocked by the infrastructure issue at the top of this file.

**Verified so far**: both client and dedicated server rebuild with zero
compile errors; the compiled DLLs contain every new Milestone 4 symbol
(checked directly, not assumed); backend suite 59/59. **Not verified**:
anything requiring a live server connection (the whole point of this
milestone's playtest bar — "the kitchen looks like the kitchen" needs an
actual screenshot, and "all descent methods work" needs the bot-mode
routines above to actually run).

## Milestone 2 plan

1. ~~Kitchen greybox~~ — done.
2. ~~Kenney vertex-colour URP shader for the rug~~ — **moved to Milestone
   4** (see correction above; this was mis-scoped into Milestone 2 in the
   original plan note).
3. ~~Thief prefab~~ — done (see above): real Quaternius mesh + Animator
   replaces the primitive-cube body, driven by a new shared
   `ThiefLocomotion` controller.
4. ~~Climb route~~ — done (see above), including the climbing mechanic
   itself (not present anywhere in Cube Arena's original movement code).
5. ~~Verify~~ — done via live bot-mode test with the real mesh, two real
   screenshots inspected directly, no exceptions in any log. **Milestone 2
   is closed.**
