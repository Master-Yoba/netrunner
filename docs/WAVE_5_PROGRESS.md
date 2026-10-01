# Wave 5 progress — difficulty, hardening and the next decision

Roadmap: [improvement roadmap](IMPROVEMENT_ROADMAP.md), Wave 5 (A6 compute
tiers and personalities, a hardening pass, and the decision point).
Evidence date: 1 October 2026, on the Linux x86_64 development box.

## Branches

| Item | Branch |
|---|---|
| A6 tiers and personalities, the style check, hardening | `feat/wave-5` (on `feat/codex-round-3`) |

## A6: difficulty tiers

Difficulty comes from compute and knowledge, not noise.

| Tier | Corp (v23) | Runner (v18) |
|---|---|---|
| Easy (A6a) | No planning, a wide near-tie band (150), no breaker-coverage reasoning | A wide near-tie band, ignores advancement and density cues |
| Standard | The turn planner, 3 samples of 3 actions | No planning |
| Hard | Standard's planning (see below) | **The turn planner**, 4 samples of 3 actions, scored by the corrected fitted evaluation (set 3) |

Hard is a profile of the current versions, so Standard's configurations, and every frozen benchmark and saved game,
are unchanged. Runner versions from v18 can plan, but only the Hard profile carries planner settings.

**The ladder (Gateway pool, 30 or 40 deals a seed):**

| Measure | Seed 1 | Seed 4 | Seed 2 | Seed 5 |
|---|---|---|---|---|
| Corp Standard − Easy | +209 [111, 309] | +179 [95, 273] | | |
| Corp Hard − Standard | +22 [−92, 112] (6×4) | 0 [−83, 91] (6×4) | −16 [−95, 60] (6×4) | 0 [−57, 77] (6×4) |
| Runner Standard − Easy | +107 [22, 223] | +67 [−10, 159] | | |
| Runner Hard − Standard | +90 [−4, 187] | +51 [−42, 138] | +68 [−10, 152] | **+105 [37, 169]** |

**The order holds in both seats:** Easy < Standard < Hard.
- **Runner Hard** is about 80 Elo above Standard over four seeds, and separated on seed 5.
- **Corp Hard** isn't measurably stronger. The Corp planner is saturated, and every setting tried was level or weaker,
  all against Hard at 6×4:

| Corp Hard variant (seed 2 or 5) | Elo against Hard at 6×4 |
|---|---|
| 10 samples of 4 actions (`hard-wide`) | 0 [−78, 85] |
| Rollouts counted double (`hard-weight`) | −27 [−94, 34] |
| Plus the A4 rez search (`hard-rez`) | −8 |
| Modelling the current Runner (v18) instead of v7 (`hard-model`) | 0 |

So Corp Hard plans as Standard does, and its turns stay as quick as Standard's. The variants stay as ladder entrants.
The Corp's gains this wave came from heuristic fixes, not compute, so the next lever is the heuristic and the
evaluation (A7, with the larger Elevation pool), not more rollouts.

**Against Codex** (gpt-6.1-sol, the 24 deals of batch 3):
- **Runner Hard:** won 3 of 12, level with Standard Runner v18's 6 of 24.
- **Corp Hard** (then at 6×4): won 3 of 12, where Standard Corp v23 won 10 of 24.

Arms of 12 can't separate these. Hard beats Standard bots, but isn't shown to beat a strong outside player more often.

**The human half of the gate** (Easy, Standard and Hard feel ordered) is the playtest. The expectation to check there:
the Runner bot's Hard is clearly harder, and the Corp bot's Hard plays like its Standard.

## A6: personalities

A personality isn't a separate bot. It's one `personality` parameter on the current versions (Corp v19 and later,
Runner v18 and later) that does two things:
- **It leads the plan-level traits the heuristic reads.** These are the A8 archetype profile, with the deck's own
  archetype counting 30%.
- **It shifts a few named terms of the fitted evaluation** the planner scores rollouts with, so planning keeps the
  style instead of planning it away.

| Personality | Traits | Evaluation shift (thousandths of log-odds per unit) |
|---|---|---|
| Glacier (Corp) | Glacier: up to 2 remotes, 3 ICE before an agenda | ICE +120, rezzed ICE +80, remote run cost +40, occupied remotes −150 |
| Rush (Corp) | Rush: 1 ICE before an agenda, the rush bonus | Own points +300, agenda progress +300, ICE −60 |
| Asset spam (Corp) | Horizontal with the asset bonus raised to 2.5 and up to 5 remotes | Occupied remotes +400, money +200 |
| Aggressive (Runner) | Aggro: runs whenever it can afford to | Own points +300, HQ and R&D open +100 each, rig value −150 |
| Rig-builder (Runner) | Big rig: holds back until its rig covers every ICE type | Breaker types +250, rig value +400, money +150 |

**The menu and the setup form** offer a personality for the opponent's side, beside Easy, Standard and Hard. The
tutorial bot has neither Hard nor personalities, so the Tutorial area hides both. A save keeps the personality: it's
part of the opponent's configuration.

**The style check (`ai-style`)** replays a ladder's saved games and measures each side's style. For each personality:
- **Band:** the ladder against Standard (seed 3, 20 deals, 120 games per seat; asset spam on seed 6, 40 deals, after its
  retune).
- **Style:** how it plays against Standard on the same games.

| Personality | Elo vs Standard | Style vs Standard |
|---|---|---|
| Glacier | −19 [−92, 47] | First point at turn 7.5 (Standard 6.5); 5.0 ICE at turn 6 (4.5); 0.95 remotes (1.18) |
| Rush | −52 [−113, 16] | First point at turn 5.6 (6.5); 57% scored by turn 6 (50%); 4.0 ICE (4.5) |
| Asset spam | −17 [−123, 91] | 3.75 assets installed (2.78); 1.48 remotes at turn 6 (1.23) |
| Aggressive | −81 [−146, −11] | 5.6 runs by turn 6 (4.4); first run at turn 1.3 (1.8); first point at turn 4.1 (5.5) |
| Rig-builder | −62 [−128, 27] | 3.5 runs by turn 6 (4.4); 2.4 programs and hardware at turn 6 (2.1) |

**Each plays differently, and each stays within 100 Elo of Standard.** That's the band used here, about the width of a
ladder confidence interval. All five are a little weaker than Balanced, which is expected: Balanced is what the
heuristic and the fit were tuned for.

**Asset spam was retuned.** With Horizontal's traits as they are, it installed 3.3 assets a game to Standard's 3.2.

## Hardening

<!-- hardening -->

## Decision point

Recorded in the [implementation plan](IMPLEMENTATION_PLAN.md), section 2:
**the next card set is Elevation**, chosen ahead of M6 and an RC, because the
bots' remaining faults come more and more from a pool too small to need
deck-aware play. Before its first batch, the card-set wave sets the E6
Core-growth budget (at least 80% of the set needs no Core change, measured per
batch), adds C2's format snapshots and starts A7.

## Playtest

<!-- playtest -->
