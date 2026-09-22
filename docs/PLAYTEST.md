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
