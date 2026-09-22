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
(Checklist added once this milestone is built and self-verified.)
