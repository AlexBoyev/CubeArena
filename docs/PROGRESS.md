# Pocket Heist — Progress

Read this first in any new session. Current milestone, what's done, what's
next, known issues. Updated after every completed step per
`AUTONOMOUS_RUN.md`.

## Status: Milestone 1 closed, starting Milestone 2

### Milestone 1 — done, merged to master
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

### Milestone 2 — in progress
Deliverable: kitchen greybox at ×25 scale with surface tags from primitives,
thief prefab with animations, climb route to the table. Done when a thief
can reach the table top via the chair.

See "Milestone 2 plan" below for the breakdown. Nothing built yet — this
section updates as steps complete.

## Known issues / not yet fixed
- Placeholder giant material only maps skin textures to every renderer —
  renders shirtless/in briefs, no clothing. Cosmetic, not blocking. Revisit
  when the placeholder character is replaced.
- Kenney Furniture Kit's `rugRectangle` is vertex-coloured (no texture
  atlas) — needs a URP vertex-colour shader/Shader Graph before it renders
  correctly. Deferred to whenever it's actually placed in the level
  (originally slated for Milestone 4, now folded into Milestone 2's greybox
  pass per the master prompt's own scope list — see Milestone 2 plan).
- `Assets/Scenes/SleepClipPreview.unity` and its supporting Editor scripts
  (`SleepPreviewBuilder.cs`, `SleepPreviewScreenshotter.cs`) and generated
  assets (`Preview_*.controller`, `Custom_HeadOnArms.anim`) are still in the
  repo — safe to delete once Milestone 2's real kitchen scene exists and
  this comparison is no longer needed.

## Milestone 2 plan

1. Kitchen greybox (primitives, ×25 scale, docs/GAME_DESIGN.md section 2/3
   dimensions): floor, mousehole wall segment, rug patch (quiet route),
   tiled floor (loud route), table, chair (climb route: leg → rung → seat →
   table edge), fridge silhouette, counter silhouette. Surface tags/physics
   materials for noise-model surface detection later (Milestone 5), even
   though noise itself isn't built yet — tag now while placing geometry so
   Milestone 5 doesn't need to revisit every object.
2. Kenney vertex-colour URP shader for the rug (master prompt lists this
   under Milestone 2's asset-pass scope, alongside the greybox — building
   it now rather than deferring again).
3. Thief prefab: replace the placeholder character's *role* — build a real
   gameplay thief prefab using the same placeholder Quaternius mesh
   (`ThiefModel_Placeholder`, already structured for an easy mesh swap
   later) wired to `PlayerController`'s existing replicated movement state,
   with Quaternius locomotion clips (idle/walk/run/crouch/jump/climb) from
   `UAL1_Standard.fbx` driving an Animator off that movement state.
4. Climb route: chair leg → rung → seat → table edge, per
   docs/GAME_DESIGN.md section 3. Needs a climbing mechanic — not present in
   Cube Arena's movement code at all (jump/crouch/crawl/sprint exist, climb
   doesn't). Design this as its own step once the greybox geometry exists to
   test against.
5. Verify: a thief (bot-mode or manual) can walk from the mousehole, across
   the rug, climb the chair, and reach the table top. Screenshot/play-mode
   check.

Not yet started on any of these steps.
