# Bot loop 1 (3 October 2026)

Loop 1 of the [bot improvement plan](BOT_IMPROVEMENT_PLAN.md).

## Prep and diagnosis

- **Tooling:** `ai-mcp-game` plays any official pool's pairing, and `ai-claude-play.sh --deals` plays a fixed list of
  deals (`data/ai/llm-loops/loop-1.deals`).
- **Card texts:** players and reviewers now read Elevation card texts. The review dossier and the LLM players' metadata
  had read the Gateway-only data, and the trial game showed Topan's ability as "Use a hidden card".
- **Diagnosis:** `ai-runner-diagnosis` on the 960 Battle Box ladder games found that 41% of Standard Runner v22's
  games were lost to its own action. Topan installed its last card into a flatline (85% of its games). It also took
  Semak-samun's 3 net damage, and Measured Response's 4 meat damage when it could pay 8. **Runner v23** prices damage
  (more than the grip is a flatline). It cut self-flatlines to 14% and took Standard's Runner from 19% to 28% on the
  box: +77 Elo (95% [33, 120]) against v22. On Gateway it plays the same, 0 Elo ([−48, 38]).

## Games

Claude Sonnet 5.5 against Standard (Runner v23, Corp v26), 12 deals, $10.65 in total. In the bot's Corp games, the bot
won 3 of 6; in its Runner games, it won 2 of 6.

## Findings

Codex gpt-6.1-sol reviewed every game, one review at a time: 49 findings. Each finding listed as fixed was confirmed
against the replay and has a regression test (`BotLoop1Tests`, `ElevationBatch3Tests`).

| Game | # | Finding | Verdict |
|---|---|---|---|
| 105 | 276 | Semak-samun's suffered damage counted for AU Co.'s "you do damage" | **engine bug, fixed** (rules 10.4.1) |
| 103 | 37 | Ryō's mandatory HQ trash could be skipped | **engine bug, fixed** |
| 002 | 131 | Byte!'s lethal trigger declined: its source was hidden from the Corp in R&D | **fixed** (the owner sees its trigger's card) |
| 101 | 401 | Poétrï's free installs from R&D declined as "unknown metadata" | **fixed** (the chooser sees offered installs) |
| 002 | 52 | Phật Gioan Baotixita never rezzed | **fixed**, Corp v27 |
| 004, 003 | 40, 167 | Humanoid Resources never rezzed | **fixed**, Corp v27 |
| 004, 101 | 547, 149 | Mercia B4LL4RD held back as a scoring upgrade | **fixed**, Corp v27 |
| 102, 101 | 504, 54 | Mahkota Langit Grid misread; its server's rezzes unpaid | **fixed**, Corp v27 (rezzed when its server has work) |
| 005 | 120 | Topan used with only events in the grip | **fixed**, Runner v23 |
| 007 | 128 | Madani hosted nothing, 65 times in one game | **fixed**, Runner v23 |
| 005 | 127 + game end | Ran an unknown remote with 2 cards into Byte!'s lethal ambush | confirmed; deferred (the trap-risk threshold) |
| 006, 105, 007 | 607, 433, 47 | Breaks one end-the-run subroutine of several, then passes | confirmed; deferred (the encounter planner) |
| 003 | 329 | Petty Cash's Archives ability used when its play can't happen (cost 3, not the first action) | confirmed; deferred (legal, wasted) |
| 001 | 327 | Replaced an unrezzed Mahkota with its own copy | deferred |
| 002 | 57 | PT Untaian's 1-credit advancement declined | deferred (advance-target valuation) |
| 006, 005 | 532, 31 | Basic runs instead of Clean Getaway | deferred |
| 006 | 251 | A second Marjanah installed beside the first | deferred |
| 006 | 154 | Mutual Favor finds nothing | deferred |

The other 27 (scoring tempo and lines, ICE placement, tag and economy management, run targeting) are strategy
judgments; they are recorded in `artifacts/llm-loops/loop-1/findings.json` and wait for the next loop's evidence.

## Verification

The batch is Runner v23 (with loop 1's grip checks), Corp v27, the two engine fixes and the two observation fixes.

- **Ladders** (240 deals each, the new Standard against the previous version kept as an entrant):

  | Change | Core Battle Box | Gateway |
  |---|---|---|
  | Runner v23 against v22 | **+52 Elo** (95% [15, 87]) | 0 ([−36, 33]) |
  | Corp v27 against v26 | 0 ([−43, 43]) | 0 ([−42, 45]) |

  The ladders show no difference for Corp v27's rez fixes. They are kept because the replays confirm each mistake and
  the regression tests pin each fix.
- **Runner diagnosis** on the box ladder's 480 games each: v23 wins 28% and loses 6% to its own flatline. v22 wins 21%
  and loses 37% to its own flatline.
- **Fuzz** (per slice, `--games-per-pairing 8 --verify-replays-every 8`): Gateway 288 of 288, Gateway + Elevation 288
  of 288 and NSG Core Sets 2,592 of 2,592 games reached a terminal state.
- **Gates** (`make gates` on ea1927d): verify, m5-gate, tactics and bot-check pass. The first run, on a412778, failed
  verify on a stale card coverage report, regenerated in ea1927d.
