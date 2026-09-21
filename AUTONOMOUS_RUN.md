# Autonomous run: Milestones 2–6 of POCKET_HEIST_MASTER_PROMPT.md

You now have standing approval to work through Milestones 2, 3, 4, 5 and 6
**without stopping for approval between them**. This overrides the
"stop at every milestone" rule in section 0 of the master prompt, for this run
only. Everything else in the master prompt still applies.

Before starting: finish the open #4 giant-pose fixes and close Milestone 1.

---

## 1. Keep a resumable record

This run will outlast your context window and possibly my usage limits.
Maintain these files so any fresh session can pick up exactly where you left
off by reading them:

| File | Contents |
|---|---|
| `docs/PROGRESS.md` | Current milestone and step, what's done, what's next, known issues. Update after every completed step. |
| `docs/DECISIONS.md` | Every design or technical choice you made without asking me: the options, what you chose, why. One entry per decision. |
| `docs/PLAYTEST.md` | For each milestone, a checklist of what I should test by hand, and what you could not verify yourself. |

If you start a session and `docs/PROGRESS.md` exists, read it first and
continue from there.

---

## 2. How to work unattended

- **Plan each milestone** in `docs/PROGRESS.md` before building it. Don't wait
  for approval.
- **Decisions with trade-offs:** pick your recommended option, log it in
  `docs/DECISIONS.md`, and continue. Don't stop to ask.
- **Verify your own work** before moving on. Every milestone needs:
  - All backend tests green.
  - Headless bot tests for every new networked mechanic (section 13).
  - Play-mode screenshots from the relevant camera angles, which you inspect
    yourself. A mechanic isn't done because it compiles.
  - LAN Tier 0 still boots and accepts connections.
- **Commit** after each working step, on `pocket-heist`, with conventional
  commits. Push at the end of each milestone. Merge to master only at
  Milestone 6's end.
- **Tunables** (noise values, speeds, timers, thresholds) go in
  ScriptableObjects with the brief's starting values, so I can tune them
  after playtesting without code changes.
- **Context:** when your context gets long, update `docs/PROGRESS.md`, then
  compact. Don't carry stale detail.

---

## 3. Stop only for these

Stop, write the reason at the top of `docs/PROGRESS.md`, and tell me when:

1. Something needs a login, a purchase, a browser, or my credentials.
2. A step would delete data, rewrite pushed git history, or touch anything
   outside this repo (other than temp/download folders).
3. Tests or a verification fail and three genuine fix attempts haven't solved
   it. Don't loop.
4. A change would weaken security: auth, tickets, connection approval, secret
   handling, or server authority.
5. You discover the brief itself is wrong or impossible with our versions.
6. Milestone 6 is complete.

Otherwise, keep going.

---

## 4. Scope for this run

Build exactly Milestones 2–6 as specified in the master prompt, including:

- Kitchen greybox at ×25 scale with surface tags, then real KayKit/Kenney
  assets and the lighting pass (sections 5, 6, 10)
- Thief prefab with animations, climbing route to the table
- Server-authoritative multi-carrier loot: **redesign the crate layer from
  single-owner authority to server-owned loot with 2–5 carriers**, as noted
  in docs/NETCODE.md (section 7)
- All four loot items, banking at the mousehole, team total
- All three table-descent methods
- Noise model and HUD (section 8)
- Giant state machine, tells, phone light cone, catch, glass rescue (section 9)
- Night timer, dawn, results screen, return to lobby
- Removal of dead Cube Arena gameplay (section 11)
- Kenney vertex-colour shader for the rug

**Not in this run:** proximity voice (Milestone 7), gear shop, extra levels,
cat or other hazards, Steamworks, cloud deploy. Don't start them.

---

## 5. When you finish

Merge `pocket-heist` to master, then give me:

1. What's playable, and how to launch a LAN session with 2–6 clients.
2. The `docs/PLAYTEST.md` checklist.
3. The decisions from `docs/DECISIONS.md` I'm most likely to disagree with.
4. Known issues and anything you couldn't verify.
