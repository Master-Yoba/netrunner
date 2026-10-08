# The bot loop workflow

> **Demoted (4 October 2026):** an outside-player arm runs only when the user asks, under [BOT_PLAN.md](../BOT_PLAN.md)
> section 7. Sections 1–5 here are its how-to; section 7 (verification) is replaced by BOT_PLAN.md section 6, and
> findings become puzzles and lints rather than one-off fixes.

How to run one loop of the [bot improvement plan](BOT_IMPROVEMENT_PLAN.md): LLM games against the bot, LLM reviews of
those games, findings confirmed against the replays, then one batch of fixes. Loops 1 and 2 ([BOT_LOOP_1.md](BOT_LOOP_1.md),
[BOT_LOOP_2.md](BOT_LOOP_2.md)) followed it. A loop runs only when the user asks for one: plans never schedule LLM games or
reviews on their own.

## Roles and budget

- **Player:** Claude Sonnet 5.5 (`scripts/ai-claude-play.sh`), about $1 a game, two games at a time.
- **Reviewer:** Codex gpt-6.1-sol (`scripts/ai-codex-review.sh`, reasoning medium), **one review at a time**, so a quota
  cut-off spoils at most one review.
- **Opponent:** the Standard bot with the Balanced personality, at the current default versions.
- **Size:** 12 games. In 6, Sonnet plays the Runner against the bot's Corp; in 6, the Corp against the bot's Runner.

## 1. Start from a clean baseline

- Branch from `dev` after the previous loop has merged: `git checkout -b feat/bot-loop-<n> dev`.
- The bot versions the loop plays are the defaults on that commit. Record them in `docs/BOT_LOOP_<n>.md`.

## 2. Deals

Write `data/ai/llm-loops/loop-<n>.deals`, one line each: `NNN corp~runner seat`, where seat is Sonnet's side.

- **Box baseline, every loop:** deals 001–007 on the Core Battle Box, unchanged, so loops compare.
- **NSG rotation:** five deals numbered `<n>01`–`<n>05` from the NSG Core Sets decks, taking the plan's rotation table in
  order (gap-covering decks first).
- Sonnet's deck comes from the same set as the bot's: box against box, NSG against NSG.

## 3. Play

```sh
scripts/ai-claude-play.sh --output artifacts/llm-loops/loop-<n> --deals data/ai/llm-loops/loop-<n>.deals \
  --seed 20261003 --opponent standard --parallel 2 --budget-usd 3 > artifacts/llm-loops/loop-<n>.log 2>&1
```

- The log's last line gives the bot's wins per side and the cost.
- A game that ends with `exit 1, error` and few choices is an API failure, not a game result. Replay its deal alone
  into `artifacts/llm-loops/loop-<n>-retry` with a one-line deals file.

## 4. Review

Build the two corpora from the saved games (`game-NNN/game/*-NNN.json`, not `result.json`):

- `corpus-bot-corp`: the games where Sonnet played the Runner;
- `corpus-bot-runner`: the games where Sonnet played the Corp.

Then review them one stream at a time (`artifacts/llm-loops/loop-<n>/review.sh`):

```sh
scripts/ai-codex-review.sh $L/corpus-bot-corp --side corp --output $L/review-bot-corp --games 6 --model gpt-6.1-sol --reasoning medium
scripts/ai-codex-review.sh $L/corpus-bot-runner --side runner --output $L/review-bot-runner --games 6 --model gpt-6.1-sol --reasoning medium
```

Pass `--games 6`: the default is 5. Collect every `review.findings` entry into `artifacts/llm-loops/loop-<n>/findings.json`.

## 5. Triage

Confirm each finding against the replay before fixing anything:

- **Probe the decision.** Replay the game to the finding's sequence and print every candidate with the bot's score and
  reason. Do it in a scratch test that is never committed; the loop 1 and 2 probes are `ZzProbeTests` (a `PROBE`
  environment variable names the corpus, game, side, version and sequences). Games recorded before an engine fix may stop
  replaying after it, so probe them in a worktree at the loop's base commit (`.tools/probe-worktree`).
- **Classify it:**
  - an **engine bug** (the rules or a card's text broken): fix it with a card test in `tests/Netrunner.Cards.Tests`;
  - a **bot mistake** (a legal play the bot valued wrongly): fix the policy, version-gated;
  - a **misread** by the reviewer or the player: record it and drop it.
- **Merge duplicates.** One cause often shows up in several games.
- **Defer** what needs a design change rather than a fix, or what the replay doesn't confirm, and say why.

## 6. Fix in one batch

- **New policy versions:** each side's fixes go into a new version (Runner v<k+1>, Corp v<m+1>), gated by its generation,
  so every earlier version plays as before. Make it the default, add a ladder entrant for the previous version
  (`standard-r<k>`, `standard-c<m>`), and update `PolicyVersionTests`.
- **Fix the cause, not the card.** Prefer a rule that covers the card's family (every behavior subroutine, every copy
  that hasn't run down) over a check for one card name.
- **Regression tests at the recorded decision.** Rebind the game to the current catalog, cut it just after the decision,
  and save it under `tests/Netrunner.Match.Tests/Fixtures/saved-games/loop<n>-<game>`. Each test asserts the previous
  version's choice and the new one's (`BotLoop<n>Tests`).
- **Catalog changes** (a card spec fix) change the catalog fingerprint: rebind every fixture of that pool, and regenerate
  `docs/generated/card-coverage.*` when card tests change.

## 7. Verify

> Replaced by [BOT_PLAN.md](../BOT_PLAN.md) section 6 and the tiers in `AGENTS.md` (bot plan S2.3); kept as the record of
> how loops were verified.

Order the checks so a failure shows before the long runs start, with no other load on the machine. Each step runs only
on a change that passed the steps before it:

1. **Per change, `make bot-check`** (about 7 minutes): formatting, the quick M5 cases, and the AI, Match and CLI suites,
   including the statistical validations that self-play feeds (the run model's forecast agreement, belief accuracy).
   A bot change can fail these without any test of its own failing.
2. **Per candidate, one ladder on the pool the change targets**: the new version against the previous one on the Core
   Battle Box (`gateway-elevation`), 240 deals with `--stop-when-clear` (about 8 minutes), with `--save-games` and
   `ai-runner-diagnosis` on them for Runner changes. Compare variants here, one ladder each, and keep the best.
3. **Once, for the final candidate:**
   - the Gateway ladder, to check the change does no harm there;
   - the per-slice fuzz on the pools `scripts/affected-pools.sh` lists, when the engine or card specs changed (the fuzz
     plays random legal actions, so a bot-only change doesn't need it);
   - `make gates REF=<commit>` (about 27 minutes), last, before the push. By then it should hold no news.

A fix confirmed in the replay is kept even when the ladder shows no change: the bots seldom create each other's rare
positions. Neutral is fine; a separated loss is not.

## 8. Report and ship

- Write `docs/BOT_LOOP_<n>.md`: the games and their results; every finding, with its game, sequence and verdict; the
  deferred items with their reasons; and the verification.
- Commit in scoped pieces: engine fixes, policy batch, docs. Push, then open a PR into `dev`.
- Wait about 10 minutes for the automated review, fix what is valid, reply to every thread, then merge with one push to
  `dev`.
- Report to the user and stop: the next loop waits for their decision.
