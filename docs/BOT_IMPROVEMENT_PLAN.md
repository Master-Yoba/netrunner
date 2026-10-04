# Bot improvement plan: LLM review loops on the Elevation pools

> **Closed (4 October 2026):** LLM review loops are no longer the improvement engine. [BOT_PLAN.md](BOT_PLAN.md) is
> authoritative; loops 1 and 2 stay as records, and their findings become puzzles ([BOT_PLAN_STEPS.md](BOT_PLAN_STEPS.md), S2).

Status: written 3 October 2026, on the user's direction. Loops 1 and 2 are done ([BOT_LOOP_1.md](BOT_LOOP_1.md),
[BOT_LOOP_2.md](BOT_LOOP_2.md)); the loops wait for the user's decision. Each loop follows
[BOT_LOOP_WORKFLOW.md](BOT_LOOP_WORKFLOW.md).

## Why

On the Gateway + Elevation card pool the bots play the Runner side badly. In the deck-pool ladders, Standard's Runner
against Easy's Corp scores:

| Deck pool | Standard Runner score |
|---|---|
| EL.8's retired decks | 39% |
| NSG Core Sets | 31% |
| Core Battle Box | 21% |

Both seats are our bots, so this measures the bots, not the decks. The Core Battle Box is balanced for human play. Its
lists are where human players meet the bots, so they are where the bots must improve.

## The steps

1. **Prep the tooling** (below).
2. **Diagnose the Runner bot without an LLM.** Read the saved Battle Box ladder games (960, Standard against Easy) and
   break the Runner's losses down by cause and by deck: economy, runs into ICE it can't break, pressure on R&D and HQ,
   damage, and the Corp's scoring pace.
   - Blunt policy gaps found here are fixed with the loop's batch.
   - The rest steer what the reviewers are asked to look at.
3. **Loop:** 12 LLM-against-bot games, a Codex review of each, findings confirmed against the replay, then fixes in one
   batch (below).
4. **Stop and report** after loop 1's batch is implemented. The user decides whether and how the loops continue.

## Prep

- `ai-mcp-game` plays any known pairing, not only the Gateway pool's.
- `ai-claude-play.sh --deals FILE` plays a fixed list of deals, one line each: number, `corp~runner` pairing, Claude's
  seat. A deal's game seed comes from `--seed` and its number, so a deal kept under the same number replays the same
  shuffle in a later loop.
- `ai-codex-review` reads Elevation games. Codex runs **one review at a time**, so a quota cut-off spoils at most one.
- One trial game, to check cost and the tooling end to end before the loop.

## The loop

- **Players:** Claude Sonnet 5.5 plays (`ai-claude-play.sh`, two games in parallel). Codex gpt-6.1-sol reviews
  (`ai-codex-review`, reasoning medium, one stream).
- **Size:** 12 games per loop. In 6, Sonnet plays the Runner against the bot's Corp; in 6, it plays the Corp against the
  bot's Runner.
- **Fixed opponent:** the Standard bot with the Balanced personality, so loops compare.
- **Coverage:**
  - The Core Battle Box covers 54 of Elevation's 82 cards. Eleven NSG decks (7 Corp, 4 Runner) cover the other 28.
  - Each loop plays the 7 box decks on the bot's side, on the same deals every loop. That is the baseline.
  - It fills 5 more slots from the NSG decks, rotating. The gap-covering decks come first.
  - Sonnet's deck comes from the same set as the bot's (box against box, NSG against NSG). Box matchups are the ones
    its author balanced.

### Deals (seed 20261003)

Box slots, the same in every loop. The bot plays the named deck:

| Deal | Pairing | Bot plays | Sonnet plays |
|---|---|---|---|
| 001 | `elevation-box-nebula-fast-advance~elevation-box-topan` | Corp, Nebula | Runner, Topan |
| 002 | `elevation-box-untaian-shell-game~elevation-box-muslihat` | Corp, PT Untaian | Runner, MuslihaT |
| 003 | `elevation-box-zwicky-supermodernism~elevation-box-magdalene` | Corp, Zwicky | Runner, Magdalene |
| 004 | `elevation-box-leo-glacier~elevation-box-topan` | Corp, LEO | Runner, Topan |
| 005 | `elevation-box-untaian-shell-game~elevation-box-topan` | Runner, Topan | Corp, PT Untaian |
| 006 | `elevation-box-leo-glacier~elevation-box-muslihat` | Runner, MuslihaT | Corp, LEO |
| 007 | `elevation-box-nebula-fast-advance~elevation-box-magdalene` | Runner, Magdalene | Corp, Nebula |

NSG slots, a new deal number each loop. The bot plays the first-named deck on its side:

| Loop | Bot's Corp (Sonnet Runner) | Bot's Runner (Sonnet Corp) |
|---|---|---|
| 1 | 101 Fashion Lab (vs Dashing Mad), 102 Not so subtle (vs Enthusiasm) | 103 Dashing Mad (vs Gimbatul), 104 Bowel Movements (vs Fine Print), 105 Enthusiasm (vs Glyph of Warding) |
| 2 | 201 Fine Print (vs Professional Opportunities), 202 Gimbatul (vs Bowel Movements) | 203 Professional Opportunities (vs Hidden Funds), 204 Flow and Ebb (vs Fashion Lab), 205 Prick Thyself (vs Pork Chops) |
| 3 | 301 Glyph of Warding (vs Sabbatical), 302 Hidden Funds (vs Tickets, please) | 303 Sabbatical (vs Not so subtle), 304 Shootin' 'n' Lootin' (vs Peculiarity), 305 Tickets, please (vs Agency) |

Pork Chops, the last gap-covering Corp deck (one card), plays as Sonnet's Corp in loop 2 and on the bot's side in a
fourth loop, if there is one.

### Review and triage

- **Codex's brief** for each game:
  - the bot's blunders, mistakes, oddities and nonsense plays on its side;
  - gameplay or rules bugs;
  - the text of every Elevation card played in the game, checked against what the engine did.
- **Triage:** I confirm each finding against the replay and classify it:
  - a **bot mistake**: fixed in the policy, the valuation or the tracker;
  - an **engine bug**: fixed with a regression test;
  - a **misread** by the reviewer or the player: recorded and dropped.
  Duplicates are merged. Findings are kept in `docs/BOT_LOOP_<n>.md`.
- **One batch per loop:** every confirmed fix, then the AGENTS.md tiers:
  - `make bot-check`;
  - `affected-pools.sh` with the per-slice fuzz;
  - for a policy change, a ladder of the new Standard against the previous on the box pool, plus a Gateway check that
    the change does no harm there;
  - `make gates` before the push.

## Usage

- **Sonnet:** about $0.90 a game at list price, so about $11 a loop. Each game is capped by `--budget-usd`.
- **Codex:** 12 reviews a loop, one at a time.
- **No other LLM use:** no LLM games or reviews outside the loop.
