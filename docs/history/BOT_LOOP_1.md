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
| 005 | 127 + game end | Left Measured Response in HQ, then ran an unknown remote with 2 cards into Byte!'s lethal ambush | **fixed**, Runner v24 |
| 006, 105, 007 | 607, 433, 47 | Breaks one end-the-run subroutine of several, then passes | **fixed**, Runner v24 (behavior subroutines read; N-Pot's break costs credits) |
| 003 | 329 | Petty Cash's Archives ability used when its play can't happen (cost 3, 2 credits) | **fixed**, Corp v28 |
| 001 | 327 | Replaced an unrezzed Mahkota with its own copy | **fixed**, Corp v28 (no copy replaces one that hasn't run down) |
| 002 | 57 | PT Untaian's 1-credit advancement declined | **fixed**, Corp v28 |
| 006, 005 | 532, 31 | Basic runs instead of Clean Getaway | **fixed**, Runner v24 (on a run it sees succeed) |
| 006 | 251 | A second Marjanah installed beside the first | **fixed**, Runner v24 |
| 006 | 154 | Mutual Favor finds nothing | **fixed**, Runner v24 (no tutor when every ICE type is covered) |

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

## Deferred fixes (4 October)

On the user's direction, the deferred findings were fixed before loop 2: **Runner v24** and **Corp v28**.

- **Elevation's behavior subroutines** (N-Pot, Lamplighter, Semak-samun, Doomscroll, Empiricist, Mycoweb) were invisible
  to the encounter planner and the run model, which saw them as harmless. They are now read for what they do, their
  branches on tags and the threat level included, so the Runner no longer breaks one end-the-run subroutine of several
  and passes. N-Pot's break costs 3 credits, not a click.
- **Ambushes and kill cards:** an unadvanced face-down root card may be an ambush that flatlines a small grip (Byte!),
  priced by its share of the Corp's possible root cards, or at least one in five once seen. A known operation the Corp
  could play next turn for more damage than the Runner can take or pay off (Measured Response) is trashed on access.
- **Clean Getaway** counts its 6 credits on a run with no unknown ICE.
- **No duplicate breakers:** a second copy of an installed breaker isn't installed.
- **No empty tutors:** a breaker tutor (Mutual Favor) isn't played while the rig and grip break every ICE type.
- **No copy over a copy that hasn't run down** (Corp): a card replaces its own copy only when that copy is a rezzed
  card out of the credits it loaded (Mahkota Langit Grid replaced its unrezzed copy).
- **Petty Cash:** its Archives ability isn't used when the Corp can't pay the play.
- **PT Untaian:** a useful counter placed without a click is worth the click an advance would cost.

Each has a regression test at the recorded decision (`BotLoop1Tests`, 18 more cases).

### Verification

- **Ladders** (240 deals each):

  | Change | Core Battle Box | Gateway |
  |---|---|---|
  | Runner v24 against v23 | +34 Elo ([−4, 80]) | +16 ([−21, 53]) |
  | Corp v28 against v27 | +18 ([−17, 64]) | −3 ([−42, 43]) |

- **Runner diagnosis** on the box ladder's games: v24 wins 32%, v23 28%. Flatlines fell from 37% to 34% of games. The
  rest of the "self-flatlines" (13% against 9%) are Measured Response's forced damage: the Runner can't pay 8, and v24's
  longer games reach threat 4 more often.
- **Fuzz** (per slice): official 24, Gateway 288, Gateway + Elevation 288 and NSG Core Sets 2,592 games, all terminal.
- **Gates** (`make gates` on a61dc0e): verify, m5-gate, tactics and bot-check pass.
