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

### Vertex-colour note (Kenney Furniture Kit)

This kit (2018 vintage) has no texture atlas at all — models are coloured
via vertex colour data baked into the mesh. A standard URP/Lit material
doesn't read vertex colour by default; this needs either a small Shader
Graph (Vertex Color node → Base Color) or an unlit vertex-colour shader
before `rugRectangle` renders with its intended colours instead of flat
white/grey. Not yet built — tracked for whichever milestone first actually
places this rug in the level (Milestone 4, "replace greybox with real
assets"), not blocking the import itself.

## Downloaded, not yet imported (pending a decision — see the Milestone 1 status report)

| File | Author | Licence | Location | Status |
|---|---|---|---|---|
| `Universal Base Characters[Standard].zip` | Quaternius (quaternius.com) | CC0 1.0 | `F:\Unity\Downloads\` | The free "Standard" tier only ships a `Superhero_Male/Female_FullBody` mesh pair — no "Regular"/"Teen" proportion variants exist in this download (confirmed by inspecting every folder in the archive and its own license file, which explicitly says the free tier "only contains a portion of the models" vs. the paid SOURCE version). The master prompt's section 4.4 assumes "Regular"/"Teen" proportions exist in this file; they don't. Needs a decision before the giant/thief character prefabs can be built. |
| `Universal Animation Library[Standard].zip` | Quaternius (quaternius.com) | CC0 1.0 | `F:\Unity\Downloads\` | A single generic-humanoid FBX (`UAL1_Standard.fbx`) with 120+ animation clips baked in as takes. Not blocked by the character-mesh issue — retargetable to any Humanoid-rigged mesh via Unity's Avatar system once a character mesh is decided. |
| `Laying Sleeping.fbx`, `Sleeping Idle1.fbx`, `Sleeping Idle2.fbx` | Mixamo | Mixamo standard licence (free for any use, including commercial, per Adobe's Mixamo ToS) | `F:\Unity\Downloads\` | Retargeting to evaluate which fits "slumped forward at the kitchen table, head on arms" is pending the same character-mesh decision above — retargeting quality depends on the target rig's proportions. |
