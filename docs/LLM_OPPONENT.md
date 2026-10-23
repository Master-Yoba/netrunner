# The local LLM opponent

The LLM opponent (Wave 4.5, P3; see [its record](history/WAVE_4_5_PROGRESS.md#p3-llm-opponent)) is the standard
heuristic of a seat with a model making its strategic choices: the turn's installs, runs, advances, plays, scoring,
draws and credits. This page covers the local model meant as a player-facing opponent: how to run it, the settings,
and the sweeps that chose them. Sonnet and Codex remain the fast outside players for batches of bot games.

## Playing it in the client

New game offers **Heuristic bot** or **Local LLM** as the opponent (not in Tutorial). Choosing the LLM shows its
server in place of the difficulty and style rows: a URL such as `http://localhost:8080/v1`, the model name, an API key
if the server needs one, and **Test connection**. Empty fields fall back to `NETRUNNER_LLM_BASE_URL`,
`NETRUNNER_LLM_MODEL` and `NETRUNNER_LLM_API_KEY`. The fields and the choice are kept in `user://preferences.cfg`, the
API key in plain text.

- **The opponent** is the player's Standard bot (with its deck archetypes) and the model, at the settings below
  (`LlmPlayerOpponent`).
- **While it thinks** the header reads "The local model is thinking · 12 s". A decision past the 30 s deadline, or one
  the server fails, plays the bot's choice and says so in the dock.
- **Saves** keep the bot and mark the LLM opponent (`SoloAgentState.LlmOpponent`, omitted for other saves): a load
  wraps the bot in the LLM again, or, with no server set, plays on with the bot and says so. The save list shows
  "Local LLM". A session trace records the bot too, and crash recovery resumes with the bot.
- **The comprehensive rules** for mechanics in view come from `data/vendor/rules` when the checkout has it; without
  it the rules summary alone goes with each request.
- `make verify` runs `--llm-opponent-smoke`, a game against a stub model server inside the client.

## Running it

`scripts/ai-llm-play.sh --output <new dir> [--content POOL] [--games N] [--side corp|runner|both] [--parallel N]` plays
games of a deck pool (`ai-ladder`'s `--content`: gateway by default, gateway-elevation, nsg-core-sets, startup-2026...)
against the standard bot, with the endpoint from `NETRUNNER_LLM_BASE_URL`, `NETRUNNER_LLM_MODEL` and
`NETRUNNER_LLM_API_KEY` (in the git-ignored `.env`). `--parallel` should match the server's slots. Each game is saved
as the ladder saves it, with the model's decisions and reasons beside it in `<game>.llm.json`.

The defaults are the settings below. Each one can be turned off for an experiment: `--tools rules|all` (with
`--max-tool-calls N`), `--thinking-budget 0`, `--free-answer`, `--no-defer`, `--shortlist 0` and
`--deadline-seconds 0`.

## The settings

- **No lookups.** The system prompt holds a rules summary (`LlmRulesPrimer.Text`, about 1,100 tokens) and each
  request the comprehensive rules for the mechanics it shows (traces, expose, derez, load, charge, mark, sabotage,
  recurring credits, threat, forfeit, persistent, interrupts). Before, the model looked up a card or a rule on every decision, mostly cards already in
  its prompt and basics, and each lookup cost another round of thinking. Offered a rules lookup on top of the summary,
  it used none in 660 decisions.
- **A 400-token thinking budget** (`thinking_budget_tokens`, reasoning effort low). Without a cap a decision took
  50 s at the median and up to 400 s, and won no more games.
- **Structured answers.** A JSON schema allows only `{"option": 1..n, "reason"}`, so an answer is never invalid and a
  model out of thinking budget can't go on thinking in its answer. (llama.cpp can't constrain an answer and offer
  tools in one request, so a lookup, when offered, is an answer too.)
- **Deferring to the heuristic** (`ModelDeciderSettings.Defer`): its choice is the default, to leave only for a gain
  the model can name, such as an agenda to steal or score this turn, a loss avoided, or a card or rule the heuristic's
  reason gets wrong.
- **A shortlist of 3** (`LlmOpponentAgent.Shortlist`): the model chooses among the heuristic's best three options
  (by its priority, then score) and isn't asked when only one is legal.
- **A 30 s deadline.** A decision past it fails and the heuristic's choice stands. No decision of the sweeps came near
  it.

## What the model reads

Each request holds the board as the seat sees it (`LlmBoardText`), the text of every card in view, its last few
choices, and the options with the heuristic's scores and reasons (`HumanGameReview.Describer`). Every card it can act on
reads apart from the others, and the facts it would otherwise guess from card text are on the board:

- **Where each card is:** options name the server, and for ICE its position ("Advance Pharos in Remote 2 (ICE 1 of 3,
  outermost first)"); an agenda the seat can read shows its progress, even at none ("0 of 5 advancements"); score
  areas show points. The Send a Message misread came from two copies reading alike.
- **Counters and hosts:** hosted credits, virus, power and agenda counters on every card; hosted cards under their host
  ("unrezzed ICE hosting [Botulus (1 virus counter)]"); Trojan installs name the host, and an ability its name ("Use
  Red Team: run R&D"); copies of a rig card are told apart by their counters.
- **The players:** identities (with Méliès U's side), bad publicity, core damage, memory, and a hand size other than 5.
- **Costs by kind:** "rez cost" for ICE, assets and upgrades, "play cost" and "install cost" for the rest (the model
  had read Ansel 1.0's rez cost as its install cost).

One gap stays: before any card is hosted, Core sends the Runner no position for unrezzed ICE, so a Trojan's install
onto one reads "on unrezzed ICE" without which (the bots read the same positions, so sending them is a Core change
with the bot verification tiers).

## The sweeps

Qwen3.8-27B (unsloth UD-Q4_K_M with MTP drafting) on llama-server b10964 on an RTX 4090, with 4 slots of 16k
context, against the current standard bots on Gateway deals of `ai-llm-play --seed 1`, the LLM seat
alternating Corp and Runner. The baseline plays the same deals with the heuristic making every choice. With four
games sharing the server, the model generates about 25 tokens a second a stream (88 alone).

**Before (the first Qwen game, with lookups and no budget):** one game took 81 minutes, about 95 s a decision.

**Latency and lookups, 16 deals (games 1-16), free overrides:**

| Arm | Wins | Seconds a decision (median, max) |
|---|---:|---|
| Baseline (heuristic) | 7 | |
| No lookups, 800-token budget | 4 | 20.6, 29.9 |
| Rules lookup, 800 | 6 | 20.5, 27.1 |
| No lookups, 400 | 4 | 13.1, 16.1 |
| No lookups, no budget | 5 | 50.1, 399.5 |

Latency was solved, but the model overrode the heuristic on about 35% of decisions, and lost more games to it than it
won.

**Overrides, 16 deals, 400-token budget:**

| Arm | Wins | Overrides | Deals flipped against the baseline |
|---|---:|---:|---|
| Free | 4 | 35% | +0 -3 |
| Defer | 8 | 14% | +2 -1 |
| Shortlist 3 | 6 | 30% | +2 -3 |
| Defer and shortlist 3 | 9 | 15% | +2 -0 |

**48 fresh deals (games 17-64):**

| Arm | Wins (Corp, Runner) | Deals flipped against the baseline | Seconds a decision (median, max) |
|---|---|---|---|
| Baseline (heuristic) | 24 (13, 11) | | |
| Defer, shortlist 3, 400 | 31 (16, 15) | +11 -4 | 12.6, 18.5 |
| Defer, shortlist 3, 800 | 24 (11, 13) | +7 -7 | 20.2, 31.2 |
| Defer, shortlist 2, 400 | 26 (13, 13) | +10 -8 | 12.7, 17.5 |

- **The defaults** won 40 of 64 deals to the heuristic's 31, flipping 13 deals its way and 4 the other (a sign test
  gives about 2.5% for that split by chance). An override sends the game down another path, so each flip is partly
  luck, and the two variants, at the baseline's level, suggest the true gain is smaller than the 65% to 50% of the
  fresh deals. Every deferring arm together won 98 of 176 deals to the baseline's 86.
- **Deferring is what made the model help.** Free overrides cost games; the shortlist alone barely changed them.
- **A game takes about 10 minutes** of model time with four games sharing the server; alone it should take about a
  third of that, unmeasured.
- **No invalid answers or failures** in any arm, over about 12,000 decisions.
- **What the overrides look like:** mostly sound (scoring for the win, a free run on an unprotected remote with a
  likely agenda, banking credits before a run), some on a misread board: one Corp advanced a second copy of Send a
  Message three times, each time "scoring it for the win", taking it for the copy it had scored two turns before.

## Beyond Gateway

Prompts logged with a stub server over 12 games of each pool (gateway, elevation, gateway-elevation, nsg-core-sets,
vantage-point, startup-2026, official) name every card, with no raw IDs; the remaining identical options are copies in
hand or equal copies in play. Then the defaults played 8 games on each of three pools against the heuristic on the same
deals:

| Pool | LLM wins | Heuristic wins | Deals flipped | Seconds a decision (median, max) |
|---|---:|---:|---|---|
| startup-2026 | 3 | 5 | +0 -2 | 13.5, 17.1 |
| gateway-elevation | 3 | 3 | +1 -1 | 14.0, 17.4 |
| nsg-core-sets | 5 | 5 | +1 -1 | 13.9, 20.2 |

No invalid answers or failures over 1,119 decisions, and its reasons engage the new cards (Botulus on Brân 1.0, Next
Big Thing's draw, Mercia's install, Petty Cash's click). 24 games can't tell its strength there from the heuristic's;
a measure needs the 48-deal arms Gateway had.

## Next

- Kev ([jaredpalmer/kev](https://github.com/jaredpalmer/kev)), a single-pass choice-scoring model, as a faster
  decider: untested; its 27B needs more memory than a 4090, and fine-tuning one on the heuristic's or a teacher's
  choices would reopen the bot plan's closed apprentice.
