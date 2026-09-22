# Pocket Heist — Progress

Read this first in any new session. Current milestone, what's done, what's
next, known issues. Updated after every completed step per
`AUTONOMOUS_RUN.md`.

## Status: Milestone 1 closed, starting Milestone 2

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

### Milestone 2 — in progress, core traversal mechanic done
Deliverable: kitchen greybox at ×25 scale with surface tags from primitives,
thief prefab with animations, climb route to the table. Done when a thief
can reach the table top via the chair.

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

**Still open for Milestone 2:**
- Thief prefab with real animations (replace `PlayerController`'s
  primitive-cube visual with `ThiefModel_Placeholder`'s mesh + Quaternius
  locomotion clips driving an Animator off the existing movement/pose
  state) — next piece of work.
- Minor polish: `_isClimbing` flickers true/false a few times right at
  zone boundaries (~y 10.6–11.3 leg→seat, ~y 18.6–19.0 seat→table). Doesn't
  block the mechanic, not yet fixed.

## Known issues / not yet fixed
- Placeholder giant material only maps skin textures to every renderer —
  renders shirtless/in briefs, no clothing. Cosmetic, not blocking. Revisit
  when the placeholder character is replaced.
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

## Milestone 2 plan

1. ~~Kitchen greybox~~ — done.
2. ~~Kenney vertex-colour URP shader for the rug~~ — **moved to Milestone
   4** (see correction above; this was mis-scoped into Milestone 2 in the
   original plan note).
3. Thief prefab: replace the placeholder character's *role* — build a real
   gameplay thief prefab using the same placeholder Quaternius mesh
   (`ThiefModel_Placeholder`, already structured for an easy mesh swap
   later) wired to `PlayerController`'s existing replicated movement state,
   with Quaternius locomotion clips (idle/walk/run/crouch/jump/climb) from
   `UAL1_Standard.fbx` driving an Animator off that movement state. **Not
   yet started — next step.**
4. ~~Climb route~~ — done (see above), including the climbing mechanic
   itself (not present anywhere in Cube Arena's original movement code).
5. ~~Verify~~ — done via live bot-mode test, see above. (Manual/visual
   Play-mode screenshot still worth doing once the thief has a real mesh,
   since a bot log trace confirms position but not appearance.)
