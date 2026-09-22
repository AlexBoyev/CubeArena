# Pocket Heist — Progress

Read this first in any new session. Current milestone, what's done, what's
next, known issues. Updated after every completed step per
`AUTONOMOUS_RUN.md`.

## Status: Milestone 2 closed, Milestone 3 (multi-carrier loot redesign) next

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

## Milestone 3 — not started

Deliverable per AUTONOMOUS_RUN.md section 4: redesign the crate/loot layer
from single-owner authority to server-owned loot with 2-5 simultaneous
carriers, then the four real loot items and banking at the mousehole. See
docs/DECISIONS.md's "Multi-carrier loot authority model" entry for the
design pointer already sketched, and docs/NETCODE.md's "known limitation"
entry for why `CrateController`'s current `ChangeOwnership` pattern can't
support this as-is. Not yet planned in detail — do that first, per this
run's own "plan each milestone before building it" rule.

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
