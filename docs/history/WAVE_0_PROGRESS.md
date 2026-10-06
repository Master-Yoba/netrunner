# Wave 0 progress

Roadmap: [improvement roadmap](IMPROVEMENT_ROADMAP.md), Wave 0 (tooling,
safety nets and measurement).
Evidence date: 25 September 2026, on the Linux x86_64 development box (8
cores, Godot on Xvfb with Mesa llvmpipe for client captures).

## Branches

Each item is its own branch, stacked in order on `docs/improvement-plans`
(which is itself stacked on the M5 branch, PR #30):

| Item | Branch | Commits |
|---|---|---|
| E3 random-legal fuzzing | `feat/e3-random-legal-fuzzing` | `ec55374` |
| E4a prompts and history subjects | `feat/e4a-prompts-and-history-subjects` | `90b7b2f` |
| U0 capture harness and `Main` split | `feat/u0-capture-and-main-split` | `3f58134` |
| A1 rating ladder and human review | `feat/a1-rating-ladder` | `9808d9b`, `128acfd` |

## E3: random-legal fuzzing

- `RandomLegalAgent` (seeded, pass-like actions weighted 1 against 4 for the
  rest) and `RulesFuzzRunner`, which cycles random/random, random/heuristic and
  heuristic/random games across the content's preset pairings.
- Every action runs through the authoritative reducer with full invariant
  validation and a JSON round-trip, and every finished game has its replay
  verified. A failure keeps a verifiable replay prefix, the prior state and
  the failing submission (`fuzz --output`).
- `make verify` runs 50 developer games; the official lane runs 50 official
  games. `make fuzz` and `make fuzz-developer` run larger batches.

| Run | Games | Result | Mean actions |
|---|---:|---|---:|
| Official, seed 2, 8 workers | 400 | 400 terminal, 0 caps, 0 defects | 389 |
| Official lane of `make m4-gate` | 50 | 50 terminal | 403 |
| Developer lane of `make m4-gate` | 50 | 50 terminal | 51 |

Official games cost about 3 s each per worker. The developer decks are tiny by
design (they prove a deck-out win), so developer fuzzing is cheap but shallow;
official content is where fuzzing finds anything.

## E4a: prompts and history subjects

- **Decision:** both parts are derived in `Netrunner.Match` from typed data
  Core already publishes, rather than adding serialized fields to Core. Saves
  and frozen benchmark results are unchanged. The engine plan records this.
- `DecisionPromptPresenter` gives player wording for every decision kind and
  run step, names only visible ICE, and never shows rules citations or step
  identifiers. The dock uses it; Core's prompt stays as a tooltip.
- `MatchHistoryProjector` tags each entry with the side its summary describes.
  Generic lines ("Corp: advance card.") got specific wording, including where
  a card was advanced and its public counter count.
- Regression test: the new side-label test fails on the old projector with
  exactly the labels seen in the live session ("'Corp turn 1 begins.' is
  tagged Runner.") and passes on the new one.
- Checked in the live client on seed 42: "Corp turn 1 begins" and "Corp ends
  corp action" are tagged CORP, "Runner turn 1 begins" RUNNER.
- Found for E4b: the Runner receives no event when the Corp advances a
  face-down card (only `AgendaAdvanced` exists, and it is correctly
  suppressed). Recorded in the engine plan.

## U0: capture harness and `Main` split

- `--capture-output` launch mode, `scripts/ui-capture.sh` and `make
  ui-capture` ([usage](../UI_CAPTURE.md)). The contract agent plays the local
  seat; bursts start when card-table playback *renders* a chosen event kind.
  An earlier version triggered on coordinator publication and captured the
  wrong moment, because playback deliberately lags publication by about
  0.9 s per opponent history entry.
- Evidence (private, contains card art): seed 42 bursts for `run-initiated`
  and `agenda-scored`. The score burst confirms the design review: the scored
  card simply disappears from Remote 1 with no emphasis.
- All 29 validation partials of `Main` (smokes, gates, gallery, demo) moved to
  `scenes/validation/`, with the six `Slice4x` files named after what they
  validate. Flag parsing and launch dispatch moved out of `Main._Ready` into
  `validation/Main.ValidationLaunch.cs`; flag names and precedence are
  unchanged. `scenes/Main*.cs` is now production code only (about 7.5K lines);
  `scenes/validation/` holds about 6.6K lines, including the capture mode.
- Not done: extracting validation into its own class. It references 166
  private members of `Main`; that stage waits until the U2 renderer work
  settles `Main`'s shape.

## A1: rating ladder and human review

- `RatingLadder` and `ai-ladder`: every Corp entrant against every Runner
  entrant on shared seeds across the four official preset pairings, a
  ridge-regularized Bradley–Terry fit, Elo relative to the seat's weakest
  entrant, seeded bootstrap intervals, and a paired bootstrap interval for
  each adjacent gap. An order is separated when every gap interval is above
  zero. Capped games count as draws and stay in the denominator.
- `HumanGameReview` and `ai-review <session-directory>`: replays a recorded
  client session and shows, at each of the local player's decisions, the
  Standard bot's choice and top candidates. Names come from the reviewed
  side's own view model, so hidden cards stay unnamed.

Four-entrant ladder (5 seeds per pairing, 320 games, no caps or errors):

| Seat | random | contract | Easy | Standard |
|---|---:|---:|---:|---:|
| Corp | 0 | 82 [−31, 170] | 335 [221, 445] | 425 [326, 529] |
| Runner | 0 | 252 [162, 349] | 252 [165, 341] | 307 [217, 397] |

Gate ladder (random, Easy, Standard; 20 seeds per pairing, 720 games, no caps
or errors):

| Seat | Easy − random | Standard − Easy | Separated |
|---|---:|---:|---|
| Corp | 292 [237, 351] | 70 [18, 134] | yes |
| Runner | 209 [142, 274] | 45 [−10, 103] | no |

Head-to-head ladder (Easy and Standard only; 60 seeds per pairing, 960 games,
no caps or errors):

| Seat | Standard − Easy |
|---|---:|
| Corp | 12 [−30, 60] |
| Runner | −15 [−56, 19] |

Findings:

- Against each other, Standard and Easy are statistically indistinguishable
  in both seats. The Corp gap in the gate ladder came mostly from how each
  profile fares against random play. The ladder discriminates well (intervals
  of about ±40 Elo at this size); what it found is that the two difficulty
  levels are not distinct. This is a bot finding, not an instrument failure,
  and it argues for moving A6's difficulty design earlier.
- The Runner policy is barely stronger than the contract baseline: Runner Easy
  rates the same as contract v6 and Runner Standard only about 50 Elo above
  it. This supports doing A2's Runner fixes first.
- Random-legal agents beat the heuristic bots in about 10% of games in either
  seat (400-game fuzz run).
- The review tool on a capture session showed the contract agent discarding
  Cleaver, its only icebreaker, at hand size, where Standard would discard
  Jailbreak.

## Gate status

| Criterion | Result |
|---|---|
| `make verify` | Pass (run as part of `make m4-gate`) |
| `make m4-gate` | Pass with one local override (see below) |
| `make m5-gate` | Pass: every case, including 1,000 Standard games with the same deterministic digest as M5 (`202291b9…`) |
| `make m5-client-smoke` | Environmental timeout in the parallel run; every case passes run alone (see below) |
| Policy performance run | Not required: no engine or policy code changed |
| Capture contact sheets for run and score | Pass |
| `Main` split changes no behavior | Pass: `m4-gate`, `event-smoke` unchanged |
| No rules citations or step names in the dock; history side labels correct | Pass |
| 50 fuzz games in CI | Pass (developer lane in `make verify`, official lane in `make m4-gate`) |
| Ladder separates random, Easy and Standard per seat | Random vs Easy: yes in both seats. Easy vs Standard: Corp yes in the gate ladder, Runner no; head to head, neither seat separates, because the profiles are nearly equal in strength |

**M4 full-match smoke cap.** The official Runner basic full-match smoke has a
180-second wall-time cap. On this machine it reaches the cap at about action
1,370 on both the M5 base commit (`ae2a341`) and this branch, with the same
throughput, so the failure is environmental rather than a regression. With the
cap raised locally to 600 s (not committed), the whole `make m4-gate` passes,
including all four official full-match games. Whether to make the cap
configurable for slower machines is left open.

**M5 client smoke.** The make target runs two Godot processes in parallel. On
this machine a resume phase takes about 9 minutes, close to the script's
10-minute limit, and under that load `corp-expanded-standard` resume hit the
driver's 30-second wait for the bot's decision. Run alone, the same case passes
on both the M5 base commit (save 12 s, resume 539 s) and this branch (13 s,
555 s). Every phase of the matrix passes on this branch:

| Case | Save | Resume |
|---|---|---|
| Runner basic Standard | pass (parallel run) | pass (parallel run) |
| Corp basic Standard | pass (parallel run) | pass (parallel run) |
| Corp expanded Standard | pass (parallel run) | pass alone, 555 s |
| Runner expanded Standard | pass (parallel run) | pass alone, 1,273 s |
| Runner expanded Easy | pass alone, 14 s | pass alone, 814 s |

The two Runner expanded resumes are long games (over 1,300 actions) that need
more than the script's 10-minute subprocess limit on this machine. Throughput
matched the M5 base commit in every comparison made. Whether the smoke limits
should scale with machine speed is left open, like the full-match cap.

