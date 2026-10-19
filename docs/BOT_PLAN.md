# Bot plan

Status: **authoritative** for bot (AI opponent) work, 19 October 2026. The evidence for every policy version is in
[BOT_VERSIONS.md](BOT_VERSIONS.md). The search-first plan this replaces, with its research findings, is in
[history/BOT_PLAN_SEARCH_FIRST.md](history/BOT_PLAN_SEARCH_FIRST.md), and its steps are in
[history/BOT_PLAN_STEPS.md](history/BOT_PLAN_STEPS.md).

## 1. Where the bots are

- **Defaults: Corp v34 and Runner v37.** Easy, Standard and Hard are tiers of the same heuristic policies:
  - the Corp's Standard adds a small 3×3 turn planner;
  - each side's Hard adds its own planner;
  - Hard knows the opponent's decklist, Standard knows the card pool, and Easy forgets (A9).
- **Deck pools:**
  - the Core Battle Box, the ladder pool the heuristic track was measured on;
  - System Gateway, Gateway + Elevation, NSG Core Sets, and the Elevation and Vantage Point simulation decks;
  - since VP.8, **Startup 2026**: 14 tournament lists, 48 pairings, `--content startup-2026`.
- **Startup 2026, first ladder** (VP.8: random, contract, easy and standard; 1,536 games, 0 errors): both sides order
  random < contract < easy < standard. Only the Corp's order separates; the Runner's random and contract don't.
  `ai-belief` is above its prior on every tier.
- **What the research found** (search-first, closed 9 October 2026): more search budget, fitted leaves and learned
  rankers don't make the bots stronger; missing strategic knowledge in the heuristic does. The record is
  [section 10](history/BOT_PLAN_SEARCH_FIRST.md#10-after-the-research-9-october-2026) of the old plan. Search, leaves and
  the apprentice are not reopened without a new reason recorded here.

## 2. How the work is done: the heuristic track

The heuristic is improved one concept at a time against [STRATEGY_FUNDAMENTALS.md](STRATEGY_FUNDAMENTALS.md) (rules R01
to R24, C01 to C24) and the matchup notes ([PT Untaian](MATCHUP_PT_UNTAIAN.md), [Zwicky](MATCHUP_ZWICKY.md),
[LEO](MATCHUP_LEO.md), [Nebula](MATCHUP_NEBULA.md)).

1. **Find a misplay by counting games, not by reading one.** The tools are:
   - per-deck win rates, and click and card use per deck (Runner v35 found Madani this way);
   - the misplay sweep (`ai-offers`), which finds cards offered often and never taken;
   - the lints;
   - `ai-ladder-diff`, which finds a candidate's first departure in each changed game.
2. **State a rule, not a card,** unless the card really is unusual. The reason string carries the rule ID
   (`"R16/asset-trash"`).
3. **Ship only on the ladder.** One policy version per batch, with a puzzle per rule, the regret line, the lints, and
   the ladder on the target pool, plus Gateway for harm (section 4).

What eleven batches taught (the H3 batches in [BOT_VERSIONS.md](BOT_VERSIONS.md)):
- **Only clear, large misplays ship:** Runner v30, v32, v35 and v37; Corp v34. Deck-specific rules with a plausible
  mechanism landed inside the noise.
- **Fixing how lost games end doesn't move Elo;** the decisions that matter come earlier, in winnable games.
- **A change confined to one deck is diluted** about fourfold on a seven-deck ladder.
- **Never trust one matchup cell,** only the ladder's paired interval.

## 3. Measurement

| Instrument | What it says | Where |
|---|---|---|
| Regret suite (teacher v3) | Paired regret of a version on held-out roots; per change | `ai-regret`, `bot-check` |
| Puzzles | One rule's position with one fact changed | `data/ai/puzzles/`, `ai-puzzles` |
| Lints (Oddity) | Counts of the strategy guide's mistakes over saved games | `ai-lint` |
| Misplay sweep | Each deck's plays offered against taken | `ai-offers` |
| Ladder | Elo with paired, deal-resampled intervals and the deals a candidate changed | `scripts/ai-ladder.sh` |
| Ladder diff | Each changed game's first decision where the versions diverge | `ai-ladder-diff` |
| Belief | Tracker accuracy against its prior | `ai-belief` |

**Ship rule** (each version's record states it before its run): the ladder's lower bound above zero on the target pool,
and no harm on Gateway. Step N2 adds the target metric and guards.

## 4. Verification tiers

`AGENTS.md` gives the commands. These are the tiers it refers to.

| When | What | Time |
|---|---|---|
| Per change | `BOT_CHECK_SUITES=1 make bot-check`; `ai-regret score` on the target pool; `ai-puzzles`; lint counts on the last saved run | about 20 min |
| Per candidate that claims strength | Fast ladder of about 400 deals (what 10 minutes buys) on the target pool, `--stop-when-clear`; 240 deals on Gateway for harm | about 15 min |
| Per policy version | Budget-scaling ladder; anchors; `PolicyPerformanceTests` on the reference box; the record in `BOT_VERSIONS.md` | about 30 min |
| Per batch, before push | `make gates REF=<commit>`; per-slice fuzz on the pools `scripts/affected-pools.sh` lists | about 30 min |

A change that moves no number (regret, puzzles, lints, ladder) is not shipped as a behaviour change.

## 5. Next steps

In order. Each step is a batch with its own record in [BOT_VERSIONS.md](BOT_VERSIONS.md).

- **N1. Startup 2026 becomes the target pool.** It is the format the game plays, and VP.8 gave it a ladder of its own.
  Record a baseline: a 400-deal ladder of Standard against itself, per-deck win rates, lint counts, and the sweep.
  - The Core Battle Box stays as a harm guard beside Gateway.
  - The regret suite's roots come from the box, and the puzzles from the box and NSG Core Sets. Startup roots and
    puzzles are added when a batch needs them, not up front.
- **N2. Target metric and guards on the ladder.** Borrowed from chiriboga's F11 ([notes](CHIRIBOGA_NOTES.md#measurement-worth-taking-next)):
  - a batch names one target, a lint count or a rate per game, and guards with tolerances, before its run;
  - `ai-ladder` reports the target's paired interval over the same deals, beside Elo;
  - the ship rule becomes: the target moves the right way and no guard is broken.

  This is a tooling step with no behaviour change. It makes deck-confined changes measurable without the fourfold
  dilution.
- **N3. Agendas stolen from remotes: ICE placement by marginal security.** This is our largest Corp leak (0.71 steals a
  game on the box, most behind one rezzed ICE). Borrowed from chiriboga's I2 ([notes](CHIRIBOGA_NOTES.md#update-19-october-2026);
  [their spec](https://github.com/Greenpaulo/chiriboga/blob/main/documentation/corp-ai/specs/I2-ice-selection-by-marginal-security.md)):
  - the Corp picks the ICE and server that most improve a server's security against the Runner's real capability,
    weighted by what a breach would cost, where today it picks by card order and the server's value;
  - target: remote steals a game; guards: Elo, central steals and Corp tempo (agendas scored by turn);
  - their L7.1 failure applies: HQ's protection is also the bar for scoring in a remote, so the guard on central steals
    isn't optional ([notes](CHIRIBOGA_NOTES.md#corp-ai-ideas-worth-trying)).
- **N4. The four cards the Startup sweep found unused.** Idiosyncresis and Plutus are never rezzed, Syailendra is never
  advanced, and Tread Lightly is never played. All four are System Gateway or Elevation cards, so a fix is a policy
  version measured on Startup 2026 and on the Elevation pools. Corp v39 (Idiosyncresis) was level on Elevation: the
  Runner trashes it before it pays. Start with the use rate as the target (N2), and keep a fix only if Elo holds.
- **N5. Startup 2026 diagnosis by deck.** Batch 11's method: find the decks with the widest win-rate gaps against the
  rest, and profile their card and click use from saved games. Work the largest gap first.
  - Look first at the decks built on Vantage Point's new mechanics: Méliès U's secret copy, Reality Plus's tags and
    Read-Write Share's face-down hosting.
- **N6. A phase-aware sweep.** `ai-offers` split into early, middle and late game, for the timing and sequencing
  misplays a whole-game count can't see (the lead the box left, BOT_VERSIONS "The misplay sweep").
- **N7. One cost scale for the Runner's run paths.** Borrowed from chiriboga's run calculator ([notes](CHIRIBOGA_NOTES.md#runner-ai-ideas-worth-trying)):
  credits, clicks, damage, tags and effects on one scale, with fallbacks when no path is clean. Lower priority: the
  Runner's losses so far traced to valuation, not to path choice.

Carried over: Runner v38 read +0.021 [0.004, 0.043] on an incidental 800-deal set, against +5 [−5, 15] declared. It gets
a confirmation run on the new target pool before anything else builds on it.

## 6. Not doing

- **Bluffing and deception** (remote deception profiles, lockout honesty, consequence-weighted central pressure):
  chiriboga gated all three, and all three failed ([notes](CHIRIBOGA_NOTES.md#update-19-october-2026)). They matter against
  people, and our measurement is bot against bot.
- **Search, leaf fitting and the apprentice:** closed by the research (section 1).
- **LLM games and reviews:** an outside-player arm runs only when the user asks
  ([history/BOT_LOOP_WORKFLOW.md](history/BOT_LOOP_WORKFLOW.md)).
