# Pocket Heist — Decisions

Every design/technical choice made without stopping to ask, per
`AUTONOMOUS_RUN.md` section 2. One entry per decision: the options, what was
chosen, why. Newest first.

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
