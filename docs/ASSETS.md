# Pocket Heist — Third-party assets

Every imported asset or pack, logged here per `CLAUDE.md`'s asset-licensing
rule: name, author, licence, source URL, date imported, and where it's used.
Only assets whose licence permits commercial Steam release are used — see
`POCKET_HEIST_MASTER_PROMPT.md` section 4 for the approved-sources list.

## Imported (in `Assets/ThirdParty/`)

| Pack | Author | Licence | Source | Date | Used for |
|---|---|---|---|---|---|
| KayKit — Restaurant Bits (1.0) | Kay Lousberg (kaylousberg.com) | CC0 1.0 | `github.com/KayKit-Game-Assets/KayKit-Restaurant-Bits-1.0` | 2026-09-21 | Kitchen level (Level 1): table, chair, fridge, counter, walls/window/door, tableware, food props. Curated subset (19 of 140+ models) — `kitchentable_A_large`, `chair_A`, `fridge_A`, `kitchencounter_sink`, `kitchencounter_straight_A`, `wall`, `wall_window_closed`, `wall_doorway`, `door_A`, `plate`, `plate_dirty`, `food_burger`, `pot_A`, `pan_A`, `crate`, `jar_A_medium`, `knife`, `cuttingboard`, `stove_single` (glTF format — this repo distribution ships OBJ+glTF, no FBX; glTF chosen for proper PBR materials via glTFast). More of the pack's 140+ models are available in the same source zip (`F:\Unity\Downloads\KayKit-Restaurant-Bits-1.0.zip`) if a later milestone needs more without a re-download. |
| Kenney — Furniture Kit (2.0) | Kenney (kenney.nl) | CC0 1.0 | `kenney.nl/assets/furniture-kit` | 2026-09-21 | `rugRectangle` — Level 1's quiet-route rug (KayKit's Restaurant Bits has no rug/carpet piece). FBX format; this kit uses vertex colour, not a texture atlas — see note below. Rest of the kit (150+ generic household models) available in `F:\Unity\Downloads\kenney_furniture-kit.zip` for later levels (bathroom/bedroom pieces, per the master prompt's note that `Laying Sleeping` suits a future bedroom level). |
| Kenney — Impact Sounds (1.0) | Kenney (kenney.nl) | CC0 1.0 | `kenney.nl/assets/impact-sounds` | 2026-09-21 | Footsteps (`footstep_wood_000-004` for tile/loud surfaces, `footstep_carpet_000-004` for rug/quiet surfaces — directly matches the noise model's surface distinction in `docs/GAME_DESIGN.md` section 5) and impacts (`impactGlass_light_000-004` for the glass-rescue mechanic / knocked props, `impactGeneric_light_000-004`, `impactBell_heavy_000`). One pack covers both the "footsteps" and "impacts" placeholder-SFX categories the master prompt asked for — better fit than the genre-generic RPG Audio pack (also downloaded, not imported; per-surface footsteps here are more useful). |
| Kenney — Interface Sounds (1.0) | Kenney (kenney.nl) | CC0 1.0 | `kenney.nl/assets/interface-sounds` | 2026-09-21 | UI SFX: `click_001`, `confirmation_001`, `back_001`, `close_001`, `drop_001` — covers the "UI" placeholder-SFX category. |
| Quaternius — Universal Base Characters (Standard/free tier) | Quaternius (quaternius.com) | CC0 1.0 | Provided in `F:\Unity\Downloads\` | **PLACEHOLDER — not the intended final characters.** `Superhero_Male_FullBody` used for *both* the giant and the thieves (the master prompt's section 4.4 "Regular"/"Teen" proportions are paid-SOURCE-tier only — see below). Imported to `Assets/ThirdParty/Quaternius-BaseCharacters-Placeholder/`, Humanoid-rigged, one URP/Lit material (`SuperheroMale_URP.mat`). See "Placeholder character structure" below for how this swaps out later. |
| Quaternius — Universal Animation Library (Standard/free tier) | Quaternius (quaternius.com) | CC0 1.0 | Provided in `F:\Unity\Downloads\` | Imported to `Assets/ThirdParty/Quaternius-Animations-Placeholder/UAL1_Standard.fbx`, Humanoid-rigged (86 clips as takes, e.g. `Sitting_Idle_Loop`, `PickUp_Table`, `Idle_Loop`, `Walk_Loop`, `Crouch_*`, `Jump_*` — full list retargetable to any Humanoid avatar; not all imported into prefabs yet, just the two used in the sleep-clip preview). |
| Mixamo — `Laying Sleeping.fbx`, `Sleeping Idle1.fbx`, `Sleeping Idle2.fbx` | Mixamo (Adobe) | Mixamo standard licence (free for any use, including commercial, per Adobe's Mixamo ToS) | Provided in `F:\Unity\Downloads\` | Imported to `Assets/ThirdParty/Mixamo-SleepClips-Placeholder/`, Humanoid-rigged, used in the sleep-clip preview scene (`Assets/Scenes/SleepClipPreview.unity`) — see below. |

### Placeholder character structure (swappable later)

`Superhero_Male_FullBody` is used because the free Quaternius download doesn't
contain the proportions the brief names (see the entry above) — this is
explicitly temporary, not a final art decision. To make swapping it out later
a mesh-reference change rather than a rebuild:

- `Assets/ThirdParty/Quaternius-BaseCharacters-Placeholder/Prefabs/SuperheroMale_CharacterModel.prefab`
  is the raw imported model — mesh, Humanoid `Animator`, one material. Nothing
  else references the FBX directly.
- `GiantModel_Placeholder.prefab` and `ThiefModel_Placeholder.prefab` are thin
  wrappers (scale ×25 and ×1 respectively, per `docs/GAME_DESIGN.md` section 2)
  that each nest one instance of that model prefab as a child.
- Getting the real SOURCE-tier Regular/Teen meshes later means building an
  equivalent `..._CharacterModel.prefab` from them and repointing the two
  wrapper prefabs' nested instance at it — the wrappers, and anything that
  ends up referencing them (Milestone 2's actual gameplay thief prefab, the
  giant's future state-machine controller), don't need to change.
- Per-slot thief recolouring isn't wired up yet (still Milestone 2's job,
  "Thief prefab with animations") — `ThiefModel_Placeholder` exists as a
  model reference only, one shared material, no tint applied.

### Sleep-clip preview

`Assets/Scenes/SleepClipPreview.unity` (built via
`Assets/Editor/PocketHeist/SleepPreviewBuilder.cs`, run once via
`-executeMethod PocketHeist.EditorTools.SleepPreviewBuilder.Build` — re-run it
any time after changing the script, Unity closed first per `CLAUDE.md`) seats
four `GiantModel_Placeholder` instances at identical kitchen-table greyboxes
(dimensions from `docs/GAME_DESIGN.md` section 2), each labeled and playing a
different candidate:

1. **Laying Sleeping** (Mixamo, unmodified)
2. **Sleeping Idle 1** (Mixamo, unmodified)
3. **Sleeping Idle 2** (Mixamo, unmodified)
4. **Custom — experimental**: a 2-layer `Animator` — base layer
   `Sitting_Idle_Loop` (from the Quaternius library, sets the seated leg/hip
   pose), upper-body layer (avatar-masked to spine/chest/head/arms/fingers,
   Override blend, full weight) playing `PickUp_Table` for its forward torso
   lean. This is a constructed approximation, not a clip authored for "head on
   arms" — picked because it was the closest forward-lean candidate among the
   library's 86 clips, not because it's known to look right. Labeled
   "experimental" in the scene itself for that reason.

Open the scene in the Editor and press Play (or scrub each `Animator` in the
Animation/Animator windows) to judge. Not yet deleted per the master prompt's
own instruction to keep working artifacts until a decision is made — safe to
delete `SleepPreviewBuilder.cs`, the four `Preview_*.controller` assets, and
the scene itself once a clip is chosen and real level work starts.

### Vertex-colour note (Kenney Furniture Kit)

This kit (2018 vintage) has no texture atlas at all — models are coloured
via vertex colour data baked into the mesh. A standard URP/Lit material
doesn't read vertex colour by default; this needs either a small Shader
Graph (Vertex Color node → Base Color) or an unlit vertex-colour shader
before `rugRectangle` renders with its intended colours instead of flat
white/grey. Not yet built — tracked for whichever milestone first actually
places this rug in the level (Milestone 4, "replace greybox with real
assets"), not blocking the import itself.

## Not imported (kept as source-only reference)

| File | Location | Why |
|---|---|---|
| `KayKit-Restaurant-Bits-1.0.zip` (full pack) | `F:\Unity\Downloads\` | Only 19 of 140+ models imported so far — the rest are available here without a re-download if a later milestone needs more. |
| `kenney_furniture-kit.zip` (full pack) | `F:\Unity\Downloads\` | Only `rugRectangle` imported — the rest (150+ generic household models) available here for later levels. |
| `kenney_rpg-audio.zip` | `F:\Unity\Downloads\` | Downloaded while evaluating footstep sources; Impact Sounds' per-surface footsteps (imported) turned out to be a better fit — kept in case a generic RPG SFX is wanted later. |
