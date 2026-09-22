# Pocket Heist — Decisions

Every design/technical choice made without stopping to ask, per
`AUTONOMOUS_RUN.md` section 2. One entry per decision: the options, what was
chosen, why. Newest first.

---

## Real KayKit visuals layered over unchanged Milestone 2 collision, not re-derived from the meshes

**Options**: (a) keep the Milestone 2 invisible primitive colliders/
`Climbable` zones exactly as-is (already verified working via a real
bot-mode climb test) and add the real meshes purely as visual dressing on
top, scaled to align with those proven anchors; (b) re-derive the climb
route's collision heights directly from the real `chair_A` mesh's own
proportions.

**Chosen**: (a). Checked `chair_A` directly (probed its Renderer bounds and
its full child hierarchy) — it's a single unlabelled mesh with no
discoverable "seat" sub-part, so any seat-height figure derived from it
would be a proportion *guess* (I used ~47% of total height as a plausible
dining-chair ratio, purely for the *visual* chair's own scale — not for
anything collision-relevant). Re-deriving the actual climbable collision
from that guess risked silently breaking the one thing Milestone 2 already
proved works end-to-end (a bot walking the mousehole→chair→table route),
for a purely cosmetic gain. The real mesh's own top-level anchors
(`TableTopHeight`, `ChairSeatHeight`) still drive its scale, so it's not
arbitrarily placed — just not the *source* of gameplay-critical numbers.

**How to apply going forward**: any future real-asset swap for a
climb-critical piece should default to this same pattern (visual dressing
over proven invisible collision) unless the new mesh has clearly-labelled,
reliable sub-part transforms to anchor to instead.

---

## Tablecloth/chair loot descent: one height-tracking fix, not two new systems

**Options**: (a) build "carry down the chair" and "lower down the
tablecloth" as distinct, purpose-built mechanics each aware of their own
route; (b) fix `LootItem`'s carried/dragged target height to track the
*grippers' own current Y* (plus a small offset) instead of a fixed
absolute height, and let both descent methods emerge for free from the
already-proven Milestone 2 climbing mechanic (which is purely
proximity-gated, not route-specific, and already works identically
whether or not the player happens to be gripping something).

**Chosen**: (b). `PlayerController.IsNearClimbable`/`ComputeClimbMove`
never checked grip state at all — a gripping player can already climb any
`Climbable` zone exactly like a non-gripping one. The only reason table-top
loot couldn't previously be "carried down" anything is that
`LootItem.FixedUpdate` targeted a *fixed* `CarryHeight` (≈1.2m above the
floor) regardless of the grippers' actual position — a table-top item
would have instantly sunk toward floor height the moment it was gripped,
regardless of where the grippers physically stood. Changing the target to
`average gripper Y + offset` (new `CarryHeightOffset`/`DragHeightOffset`
tunables, replacing the old absolute `CarryHeight`) makes the item
naturally track a gripper's real 3D position as they walk *any* climb
route — the tablecloth's new `Climbable` zone at the table's east edge and
the existing chair route both "just work" as loot-descent paths with zero
new movement code. Only the tablecloth zone's geometry itself (a new
invisible `Climbable` box) is new; the mechanic is 100% reused.

**Known simplification**: the tablecloth zone spans floor-to-table-top
directly rather than literally stopping "~5m above the floor" per the
brief's flavour text (implying a controlled-descent-then-drop). Extending
it the full height was judged lower-risk than inventing a new
"controlled descent, then fall the last 5m while still gripped" sub-state
for a first pass — revisit if a human playtest specifically wants that
extra beat.

---

## Shove uses real Rigidbody physics, which required removing LootItem's old "always glide toward a target" FixedUpdate

**Options**: (a) keep `LootItem`'s Milestone 3 behavior of calling
`Rigidbody.MovePosition` toward a computed target *every* tick regardless
of grip state (including a "nobody's gripping it, glide down to floor
height" branch for the ungripped case), and bolt a separate shove
mechanism on top; (b) remove the unconditional per-tick `MovePosition`
call for the ungripped case entirely, so an item with no grippers is
simply left to Unity's own Rigidbody physics (gravity + collision,
already enabled — the server's own copy is non-kinematic under
`AuthorityModes.Server`) — then a "shove" is just: clear grippers, apply
an outward+upward velocity, and let physics take over.

**Chosen**: (b). Milestone 3's original ungripped-case behavior
(`MoveTowards` straight down to a hardcoded near-floor height every tick)
was never collision-aware — it would have silently sunk a dropped
table-top item straight through the table toward the floor, a latent bug
that never surfaced in Milestone 3 because the only item that milestone
shipped (the floor coin) never left floor height in the first place. Since
Milestone 4 adds table-top items that *can* be dropped mid-air, this bug
needed fixing regardless of shove — and fixing it (just stop overriding
position when nobody's gripping) is also exactly what a real, believable
"shove it off the table" needs: genuine gravity/collision, not a scripted
glide. One fix serves both. A `_isFreeFalling`-style extra flag turned out
unnecessary — "no grippers" already means "let physics run," so a shove
is just a velocity kick into that same state, nothing more.

---

## Modular wall/counter tiling descoped this pass — one stretched panel per segment instead

**Options**: (a) tile several real KayKit wall/counter modules
end-to-end to cover each wall/counter run accurately; (b) use a single
module per segment, non-uniformly scaled/stretched to span the whole run.

**Chosen**: (b), for this pass. Tiling requires knowing exactly how each
module's own footprint should repeat (module width, seams, corner pieces)
— real content but meaningfully more code and per-asset judgment calls
than this milestone had budget for alongside the loot/descent-method work
(the larger, more novel piece of this milestone). A single stretched panel
is visually rougher (proportions/UVs won't read as "tiled brick," more
like a stretched banner) but still real KayKit geometry, not a primitive,
and satisfies "the kitchen looks like the kitchen" as a first pass.
Revisit with proper tiling in a future art-polish pass if a human playtest
flags the stretching as too rough.

---

## LootItem moves toward average gripper *position*, not integrated input

**Options**: (a) each tick, move the item toward the average of its current
grippers' `transform.position` (what `CrateController.FixedUpdate`'s single-
holder hold-point-following already did, generalized to N positions instead
of one); (b) have each gripper send their own movement-intent vector (reuse
`PlayerController`'s own world-space input, already computed every tick for
their own movement) and have the server average *those vectors* and
integrate the item's position from that average, closer to the brief's
literal "the server averages grippers' input intent" wording.

**Chosen**: (a). Operationally the two produce very similar results for
this milestone's single coin (the item visibly follows wherever its
grippers are standing, which is what "being carried by the group" should
look like), but (a) is meaningfully simpler and more robust: it needs no
new accessor into each `PlayerController`'s current input, has no edge
case for "0 grippers" beyond "nothing to average" (handled the same way as
"settle to the floor"), and its correctness doesn't depend on grippers
actually facing/walking toward a shared destination coherently. A future
milestone could revisit (b) if "average gripper position" starts to feel
wrong in practice (e.g. grippers standing still but the item still
drifting toward some group centroid that isn't where anyone's walking) —
no evidence of that in this milestone's live testing.

---

## `LootItem`/`PlayerController` grip flow: single toggle RPC, no throw

**Options**: (a) mirror `CrateController`'s separate grab/throw pair
(compute a release velocity, `RequestThrowServerRpc`); (b) a single
`RequestToggleGripServerRpc()` — grip if not gripping anything, release
whatever's currently gripped if already gripping.

**Chosen**: (b). The brief's carry mechanic (section 7) never mentions
throwing loot — items are shoved off the table, lowered down the
tablecloth, or carried down the chair, never thrown — so a throw affordance
for loot would be dead UX at best, actively wrong at worst (a "yeeted"
coin isn't in scope). Dropping the velocity computation and the second RPC
also simplified `PlayerController`'s E-key handler down to one line
(`RequestToggleGripServerRpc()`), no client-side branching needed at all
(the server, not the input handler, decides grip vs. release based on
`_grippedLootItemServer`).

---

## `CrateController`/old per-player score: left in place, not removed this milestone

Master prompt section 11 lists "Code-built player template -> Replace" and
implicitly expects the crate-throw mechanic to go away, but doesn't
explicitly call out deleting `CrateController.cs` or `PlayerController`'s
old `_score`/`AddScore`/`ResetScore`/Hold-Tab-scoreboard machinery by name.
`PlayerController` no longer references `CrateController` at all (the
grab/throw/push code was fully replaced by `LootItem`'s grip flow), so
`CrateController.cs` is now genuinely dead — but it's left in the tree
unreferenced, matching the same "defer full removal" pattern already
established for `ArenaBuilder.cs`/`PickupController.cs` in Milestone 2,
rather than doing a partial cleanup pass mid-milestone. Likewise, the
per-player score/scoreboard still compiles and displays correctly (nothing
crashes, nothing shows wrong numbers), it's just no longer fed by any
active gameplay — removing it cleanly would also mean reworking the
Hold-Tab scoreboard panel and the match-end "winner" announcement logic in
`ServerBootstrap.FormatMatchResult` (which still ranks by per-player
score), which is more surface area than this milestone's own scope
warrants. Flagged here so a future dead-code sweep (Milestone 4 or later,
per the master prompt's section 11 "delete dead code... keep tests green"
rule) knows to look at all of: `CrateController.cs`, `PlayerController`'s
`_score`/`AddScore`/`ResetScore`/`ScoreChanged`, `ClientBootstrap`'s
Hold-Tab scoreboard panel, and `ServerBootstrap.FormatMatchResult`'s
score-based winner logic, together — they're one connected unit of dead
code, not several independent ones.

---

## Team loot total: a new `MatchManager` NetworkVariable, not a literal
## repurpose of the per-player score field

**Options**: (a) add a new `NetworkVariable<int> BankedLootTotal` on
`MatchManager`; (b) literally repurpose `PlayerController._score` (make it
mean "this player's share of the team total" or redirect all writes to a
single shared instance somehow).

**Chosen**: (a). A per-player score and a shared team total are different
shapes of data (one value per player vs. one value for the whole match) —
trying to force the existing per-player `NetworkVariable<int>` machinery to
represent a single shared number would need its own workarounds (which
player's copy is "the" total? do all six need to stay in sync?) that a
plain new variable on `MatchManager` — already the one match-wide-state
singleton every client reads (`TimeRemaining`, `MatchStarted`) — sidesteps
entirely. Master prompt section 11's "Scoreboard -> repurpose as the team
loot total" is satisfied in spirit (the HUD's main always-visible readout
now shows the team total instead of individual scores) without needing to
literally reuse the old field.

---

## Thief animation: Walk_Loop reused for climbing, Crouch_* reused for crawling

**Context**: `UAL1_Standard.fbx` (Quaternius Universal Animation Library)
actually has 43 clips, not the ~86 docs/ASSETS.md's original import note
estimated (confirmed by listing every sub-asset in the FBX directly) — and
none of the 43 is a dedicated climb/ladder or crawl/prone clip.

**Options for climbing**: (a) reuse Walk_Loop as a generic "limbs are
moving" cue, (b) author a custom climb pose the way `Custom_HeadOnArms`
was authored for the sleep pose (direct Humanoid muscle curves), (c) leave
climbing visually static (no animation change).

**Chosen**: (a). The actual vertical motion during climbing comes from
`ComputeClimbMove` moving the CharacterController directly
(`applyRootMotion` is off, same as the giant) — the animation is purely a
"something is happening" visual cue, not the thing selling the climb
itself (the camera framing and the character's real upward motion already
do that). A custom muscle-curve clip is real, nontrivial authoring effort
(see how much tuning `Custom_HeadOnArms` took) for a payoff that's
secondary to the actual mechanic. Revisit only if a Play-mode screenshot
or human playtest specifically flags the walk pose looking wrong while
climbing — none has so far.

**Options for crawling**: (a) reuse the Crouch_Idle_Loop/Crouch_Fwd_Loop
clips (same as Crouching), (b) same custom-authoring path as climbing.

**Chosen**: (a), same reasoning — and the part of crawling that actually
matters for the gameplay (fitting under the crawl tunnels) is the
CharacterController's collider height, which is completely unaffected by
which clip plays. Crouching and Crawling are now visually identical; only
their collision heights differ (`CrouchControllerHeight`=1.1m vs
`CrawlControllerHeight`=0.6m, both unchanged by this milestone).

---

## Per-slot thief recolouring: no new code needed, reused the existing `ApplyColor` mechanism

`docs/ASSETS.md` flagged this as "not wired up yet" when `ThiefModel_
Placeholder` was first built in Milestone 1. Turned out nothing new was
needed: `PlayerController.ApplyColor` already does `renderer.material.
color = color` over every `Renderer` found via `GetComponentsInChildren
<Renderer>()` on the root — this was written for the old primitive-cube
body but is completely generic over renderer type. Once `CreateTemplate`
instantiates the real mesh's `SkinnedMeshRenderer` under "Visual" instead
of the cube parts, `_renderers` picks it up automatically and Unity's
`Renderer.material` getter auto-instances a per-object material copy the
first time it's touched — the same mechanism `MaterialUtil.ApplyLitColor`
already relied on for the cubes. The visible effect is a colour *tint*
over the mesh's existing skin/costume texture rather than a flat solid
colour (unlike the plain-white cubes, which had nothing to tint against) —
confirmed as an acceptable, clearly-distinguishable result in the
two-bot ("Red"/"Blue") verification screenshot for this milestone, not
worth a MaterialPropertyBlock-based system for a placeholder mesh that's
getting replaced later anyway.

---

## Jump/airborne animation state inferred from position delta, not a new NetworkVariable

`PlayerController` had no existing replicated "grounded" state — remote
players are simple-interpolated (`Vector3.Lerp` toward `_serverPosition`),
never running their own `CharacterController.Move()`, so `_characterController.
isGrounded` is meaningless on a remote copy. Adding a `NetworkVariable<bool>`
for grounded state was one option; instead, `AnimateVisuals` computes a
`verticalSpeed` heuristic from frame-to-frame position delta (`rawDelta.y /
Time.deltaTime`) — the exact same technique the pre-existing code already
used for horizontal walk-cycle detection, just on the other axis. Works
identically for the owner's predicted position and a remote's interpolated
one with zero new networked state. `ThiefAnimationSettings.
AirborneVerticalThreshold` (default 1.5 m/s) is the tunable that separates
a real jump (`JumpSpeed`=8 initial) from ordinary reconcile jitter. Not
exercised by this milestone's bot-mode verification (the climb-route bot
never jumps) — flagged in docs/PROGRESS.md as worth a human sanity check.

---

## Climb zones as single continuous `Climbable` segments, not sub-staged

**Options**: (a) one `Climbable`-marked collider per major surface
transition (chair leg, seat-to-table strip), climbed as one continuous
motion within each; (b) many small sub-stage colliders (individual "rungs")
with per-rung logic; (c) a spline/path-based climb.

**Chosen**: (a). The brief's own route description ("leg → rung → seat →
table edge") reads as flavour text for a single continuous climb, not a
literal per-rung mechanic — Cube Arena/Pocket Heist has no existing
path-following code to build (c) on, and (b) adds bookkeeping (which rung
is "current," transition logic between them) for no gameplay benefit over
just letting `Vector3.Dot`-projected input move the player continuously
while inside any `Climbable` zone. Verified working end-to-end via bot-mode
test (see docs/PROGRESS.md Milestone 2). Known imperfection: brief flicker
of `_isClimbing` right at zone boundaries — cosmetic, not fixed yet.

---

## `FindNearestVisibleCrate` must check `IsSpawned`, not just gameplay state

Found while debugging the climb-mechanic bot test: every client keeps a
parked-but-active `NetworkBehaviour` template instance for each runtime
prefab (`PlayerController.CreateTemplate`, `PickupController.CreateTemplate`,
`CrateController`'s equivalent) — required by NGO, since a prefab template
must stay active in the scene to register via `AddNetworkPrefab`.
`FindObjectsByType<T>()` scans **will** find these templates. Checking only
gameplay-state fields on them (e.g. `IsHeld`, which reads `false` for a
never-spawned object since `OwnerClientId` defaults to 0 = the server's own
id) silently treats the template as a real, valid object — in this case
causing a bot to walk toward the template's parked position instead of a
real target. **Rule going forward: any `FindObjectsByType` scan over a
runtime-prefab type must also check `.IsSpawned` before treating a result
as real.** Recorded here since it's a general bug class, not specific to
crates — worth checking any future bot/AI targeting code that does a type
scan.

---

## Pickup/crate spawning disabled, not removed, pending Milestone 3

`ServerBootstrap`'s `SpawnPickups()`/`SpawnCrates()` calls are commented
out rather than deleted, and `PickupController`/`CrateController`/
`ArenaBuilder` are left in the tree unreferenced rather than deleted now.

**Why**: AUTONOMOUS_RUN.md's scope for this run explicitly calls for
redesigning the loot layer in Milestone 3 (single-owner → server-owned
multi-carrier), and section 11 of the master prompt separately calls for
removing dead Cube Arena gameplay. Doing the removal now, before the
redesign exists, risks deleting code/patterns (the `NetworkVariable`
position-replication convention, the runtime-prefab-template convention)
that the Milestone 3 rebuild will want to reference or reuse. Full removal
is deferred to align with that milestone instead of happening twice.

---

## Placeholder character mesh: Quaternius `Superhero_Male_FullBody`

**Options**: (a) buy the Quaternius SOURCE tier to get the brief's assumed
"Regular"/"Teen" proportions, (b) use the free tier's only available mesh
(`Superhero_Male/Female_FullBody`) as an explicit placeholder, (c) find a
different CC0 pack with real proportion variants.

**Chosen**: (b), per explicit user instruction after I flagged the mismatch
(the free download genuinely doesn't contain what the brief assumed — its
own licence file says so). Structured so swapping to (a) or (c) later is a
prefab-reference change: `SuperheroMale_CharacterModel.prefab` holds the raw
mesh/rig, `GiantModel_Placeholder`/`ThiefModel_Placeholder` are thin
wrapper prefabs that nest one instance of it.

**Why it matters for future milestones**: any milestone touching the
giant/thief's visual appearance is working with a temporary mesh. Don't
invest in mesh-specific tuning (exact proportions, clothing fit) that would
need redoing on a real swap.

---

## Sleep-clip choice: candidate 4 (custom seated + head-on-arms lean)

**Options**: the three Mixamo clips (`Laying Sleeping`, `Sleeping Idle 1`,
`Sleeping Idle 2`) or a custom-authored pose.

**Chosen**: custom pose, confirmed by the user after seeing all four in a
Play-mode screenshot comparison — the three Mixamo clips are all lying-down
poses, not a fit for "asleep at the kitchen table." `Laying Sleeping` kept
in `Assets/ThirdParty/Mixamo-SleepClips-Placeholder/` for a future bedroom
level per the master prompt's own note.

**Implementation**: 2-layer `Animator` (base layer `Sitting_Idle_Loop` from
the Quaternius animation library for the seated leg/hip pose, upper-body
layer avatar-masked to spine/chest/head/arms playing a custom-authored
Humanoid muscle-curve clip for the forward lean + slow breathing). Full
technical writeup, including the tuning history, in docs/ASSETS.md.

---

## Multi-carrier loot authority model — not yet decided, flagged for
## Milestone 3

`CrateController`'s single-owner `NetworkObject.ChangeOwnership` pattern
can't support 2-5 simultaneous carriers (see docs/NETCODE.md's "known
limitation" entry, written during Milestone F). The master prompt's section
4 for this run explicitly calls out redesigning this. Most likely direction
already sketched in docs/NETCODE.md: keep loot permanently server-owned/
server-authoritative, with the server reading and averaging each gripping
client's input via ServerRpc instead of transferring ownership at all — but
this is a real design decision that needs to happen when Milestone 3
actually starts, not assumed here. Recording the pointer now so a resumed
session knows where to pick this up.
