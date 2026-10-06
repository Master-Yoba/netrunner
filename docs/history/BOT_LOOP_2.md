# Bot loop 2 (4 October 2026)

Loop 2 of the [bot improvement plan](BOT_IMPROVEMENT_PLAN.md), played off `dev` after the Elevation stack and bot loop 1
merged (0f85d7a): Runner v24 and Corp v28.

## Games

Claude Sonnet 5.5 against Standard, 12 deals (`data/ai/llm-loops/loop-2.deals`), $13.14 in total. Deal 006 was replayed
after an API error ended its first session at turn 6.

| Bot side | Loop 1 | Loop 2 |
|---|---|---|
| Corp | won 3 of 6 | won 3 of 6 |
| Runner | won 2 of 6 | won 3 of 6 |

## Findings

Codex gpt-6.1-sol reviewed every game, one review at a time: 43 findings. Each one listed as fixed was confirmed against
the replay. The fixes have regression tests at the recorded decision (`BotLoop2Tests`) or in the engine
(`ElevationBatch3Tests`), except where the engine fix changed the game's legal actions before the decision (marked *).

### Engine bugs

| Game | # | Finding | Verdict |
|---|---|---|---|
| 201, 204 | 366, 119 | Scatter Field installs from Archives; its text says HQ | **fixed** (it shared Ansel 1.0's "HQ or Archives") |
| 205 | 191 | Botulus breaks Semak-samun's subroutine, which only a fracter may break | **fixed** |
| 005 | 249 | Topan's install offers no card installed only on a host (Botulus) | **fixed** |
| 002 | 207 | Phật Gioan Baotixita's steal trigger resolves at the run's end, not after the steal | plausible; deferred (the outcome rarely differs) |
| 003 | 172 | Key Performance Indicators takes both modes and their order at once | unconfirmed reading of the rules; deferred |

### Runner (v25)

| Game | # | Finding | Verdict |
|---|---|---|---|
| 204 | 78 and later | Runs a lone Scatter Field with credits for strength 0 | **fixed** (its +4 as the only ICE is seen) |
| 204 | 672 | Jacks out of an open path to an agenda | **fixed*** (the unrezzed ICE it had passed counted as ahead) |
| 005 | 539, 645 | Topan used with nothing it can pay for, then to replace its own Verbal Plasticity | **fixed*** |
| 006 | 140 | MuslihaT's free Marjanah declined as a second copy | **fixed** (a free card into the grip is a draw) |
| 205 | 357 | Accesses a faceup agenda with one card against BANGUN's 2 meat damage | **fixed*** |
| 203 | 704 | Runs unknown ICE with one card in the grip and clicks to draw | **fixed** |
| 203 | 282 | Jacks out with unrezzed Empiricists ahead and a Corp short of credits | recorded; the unknown-ICE fear, deferred |

### Corp (v29)

| Game | # | Finding | Verdict |
|---|---|---|---|
| 004 | 414 | LEO trashes the Brân it just rezzed, which would have stopped the run | **fixed*** (its own bioroid had counted as a Runner card; a run the ICE stops anyway isn't worth ending) |
| 004 | 907 | Bumi declines to trash Botulus | **fixed*** (trashing the Runner's card is worth its threat) |
| 201 | 137 | Public Access Plaza never rezzed | **fixed** (turn income) |
| 202 | 394 | AMAZE Amusements rezzed in a remote with no agenda | **fixed** |

### Deferred

- **Corp economy and sequencing:** Petty Cash before installs (001 #492, 004 #502); Mercia's free install (004 #677);
  Synapse Global's tagged gain (202 #702); Touch-ups for an advance (202 #526); Spin Doctor before it is trashed
  (003 #176).
- **Corp cards never used:** IP Enforcement, including a missed win at 001 #837 (also 201 #139, 202 #301); Idiosyncresis
  never advanced (201 #204).
- **Runner sequencing:** Ritual before Fermenter (007 #232); GAMEDRAGON™ Pro (204 #175); Maglectric Rapid's target
  (203 #270); a second Jailbreak on unchanged R&D (005 #205); an Archives run with nothing to gain (005 #309).
- **Strategy judgments** (agenda exposure, ICE choice, run targeting, investment): the rest, kept in
  `artifacts/llm-loops/loop-2/findings.json`.

## Verification

- **Ladders** (240 deals each, the new Standard against the version loop 2 played):

  | Change | Core Battle Box | Gateway |
  |---|---|---|
  | Runner v25 against v24 | +6 Elo ([−28, 49]) | −11 ([−51, 29]) |
  | Corp v29 against v28 | +12 ([−23, 54]) | −3 ([−38, 36]) |

  Neither change separates. The fixes are narrow (single cards, rare positions), and the bots seldom create those
  positions for each other; they are kept because the replays confirm each mistake.
- **Runner diagnosis** on the box ladder's games: v25 wins 32%, v24 31%; both lose 12% to their own (mostly forced)
  flatlines.
- **Fuzz** (per slice): official 24, Gateway 288, Gateway + Elevation 288 and NSG Core Sets 2,592 games, all terminal.
- **Gates** (`make gates` on 6042f15): verify, m5-gate, tactics and bot-check pass.
