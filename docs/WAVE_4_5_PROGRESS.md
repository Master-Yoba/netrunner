# Wave 4.5 progress — automated playtesting

The plan is in `IMPROVEMENT_ROADMAP.md` (Wave 4.5). Bot work was bottlenecked on human playtests: a
session finds a handful of faults, and the ladder can't see them, because it only measures wins and
both seats run the same heuristic.

## P1 Oddity lints

Deterministic detectors over saved self-play games.

- **Saving games:** `scripts/ai-ladder.sh ... --save-games <dir>` saves each game with its deal,
  both entrants, the result and the replay (`LadderGameArchive`).
- **Running the lints:** `scripts/ai-lint.sh <dir>` (or `make ai-lint LINT_ARGS=<dir>`) replays each
  game and reports, per lint, how often each entrant made the play, per game on that side.
  - `--examples N` lists the first findings, and `--json` writes them all.
- **Full state:** the lints read the full state, since nothing is hidden offline, so they can check
  what a bot couldn't see (whether R&D's top card is the one the Runner already accessed).

| Lint | Side | Looks for |
|---|---|---|
| `ice-on-empty-remote` | Corp | ICE on a remote still empty at the end of the Corp's turn, with no agenda or asset in HQ to put there (a glacier stacks its remote first) |
| `upgrade-cannot-fire` | Corp | An upgrade that works only when an agenda is scored from its server, installed in a central |
| `unique-replaces-own-copy` | Corp | A unique card installed over its own copy, or a unique upgrade beside its own copy (added from the P2 review) |
| `unrezzed-ice-runner-profits` | Corp | Approached ICE left unrezzed, though offered, on a server the Runner has already run successfully twice |
| `advanceable-ice-idle` | Corp | Advanceable ICE installed for three turns or more without a counter |
| `archives-farmed` | Corp | The Runner's third successful run on an Archives without ICE, with ICE in HQ to put there |
| `central-open-remotes-grow` | Corp | HQ or R&D without ICE at the end of a Corp turn while two or more remotes hold cards |
| `counters-on-inert-card` | Corp | Advancement counters placed on a card that is neither an agenda nor advanceable |
| `own-agenda-trashed-by-install` | Corp | The Corp trashed its own agenda by installing over it (can be a deliberate denial; triage it) |
| `archives-shuffle-empty` | Corp | An Archives-into-R&D shuffle (Spin Doctor) used with Archives empty |
| `rd-rerun-top-seen` | Runner | R&D run again while its top card is the one the Runner already accessed there this turn |
| `runner-repeats-known-trap` | Runner | The same installed trap (damage on access) accessed a second time |

`OddityLintsTests` ties four of the lints to the recorded playtest games that showed them:
- counters on Palisade (Seamless Launch);
- Offworld Office trashed by Ansel's install;
- Spin Doctor used with Archives empty;
- Urtica Cipher accessed again.

A saved game reads back and lints identically.

### Gate evidence

**Corpus 1:** a Gateway ladder of `standard-w4a` (Corp v9 and Runner v7, the bots before the
28 September repairs) against `standard` (Corp v18 and Runner v13), 240 games on seed 1. Findings
per game on each side:

| Lint | Corp v9 / Runner v7 | Corp v18 / Runner v13 |
|---|---|---|
| ICE on an empty remote | 1.683 | 0.017 |
| Upgrade that can't fire | 0.142 | 0 |
| Central open while remotes grow | 0.108 | 0 |
| Advanceable ICE idle | 0.375 | 0.067 |
| Archives farmed | 0.067 | 0.042 before the ICE-in-HQ condition; 1 game with ICE in HQ |
| Own agenda trashed by an install | 0.225 | 0.017 |
| Counters on an inert card | 0.067 | 0.025 |
| A unique upgrade over or beside its own copy (widened in P2) | 0.308 | 0 |
| R&D re-run, top card seen | 0.242 | 0 |
| A known trap accessed again | 0.333 | 0 |

The lints find the 28 September playtest's faults in v9's games: empty remotes, an upgrade in the
wrong server, an Archives farmed, advanceable ICE never advanced. They also find the Runner faults
from the 30 September playtests in Runner v7's games (a known trap accessed again) and none in
Runner v13's.

**Faults they found that no playtest had reported,** both fixed in Corp v19:
- **Seamless Launch with nothing to aim at.** Corp v18 played it with no agenda or trap installed and
  put its counters on Nico Campaign or ICE. The planner's fitted evaluation counted any counter in a
  remote as progress. Feature set 3 keeps set 2's weights and counts only counters on agendas and
  advanceable cards; v19 plans with it.
- **An agenda installed into the server being run.** Corp v18 used Ansel 1.0 to install an agenda over
  its own agenda in the remote being run, where the Runner takes the new one instead.

`CorpLintV19Tests` asks v18 and v19 at both positions, in saved ladder games kept as fixtures
(`Fixtures/saved-games`).

**Corpus 2:** `standard` (Corp v18) against `standard-c19`, 240 games. v19 has no
`counters-on-inert-card` findings (v18 had 2) and no agenda installed into the server being run.
Its remaining `own-agenda-trashed-by-install` findings are v17's deliberate denial: the Runner had
no click left for Archives, and the Corp gains a free asset for the agenda the Runner would take anyway.

**Ladder:** Corp v19 is level with v18 on the Gateway ladder (−11, +21, 0 and 0 Elo on four seeds,
240 games each), so it is the default.

**A detector fix found on the way:** `unrezzed-ice-runner-profits` found nothing at first, because the
engine offers the rez of approached ICE as an ordinary card rez, not as the run-ICE rez. It now fires
at a low rate: 0.067 per game for Corp v19, and 0.008 for v9.

**Still open:**
- **`advanceable-ice-idle`:** Pharos left unadvanced (0.05 per game for v19). The heuristic advances it
  only when that leaves its rez reserve intact, so this is left for the LLM reviewer (P2) to judge.
- **`archives-shuffle-empty`:** it found nothing in either corpus, though it fires on the Spin Doctor
  playtest game.
- **`unique-replaces-own-copy`:** it found nothing until the P2 reviewer showed v9 installing a second
  Malapert Data Vault or Anoetic Void *beside* the first. Widened to that case, it fires 0.308 times
  per game for v9 and never for v18 or v19 (Corp v11's upgrade placement refuses any second copy).
  A second unique *asset* isn't flagged: Spin Doctor's rez draws 2 once the first copy's ability is used.

## P2 LLM game reviewer

A local model reads one bot's decisions from a saved self-play game and reports plays that look wrong,
as findings: the sequence number, a category, a confidence, the claim, and the rules and cards it relied
on. Game logs stay on the local network: the model is a llama.cpp server with an OpenAI-compatible
endpoint, configured in the git-ignored `.env` (`NETRUNNER_LLM_BASE_URL`, `NETRUNNER_LLM_MODEL`,
`NETRUNNER_LLM_API_KEY`).

- **Running it:** `scripts/ai-llm-review.sh <saved games> --side corp|runner --output <new dir>`, with
  `--entrant`, `--game <id>` (repeatable), `--games N` (default 5), `--temperature`, `--top-k`,
  `--reasoning` (default medium), `--max-tool-calls` (default 60), `--min-tool-calls` and `--model`. It writes, per game, the review, the whole
  transcript and the decision log the model read, and a `triage.md` sheet with a verdict column.
- **What the model reads (`GameReviewDossier`):** the bot's decisions with the options it weighed and
  its scores and reasons. Each turn opens with a line giving the board. The opponent's board-changing
  actions (runs, accesses, installs, rezzes, steals, trashes) appear in brackets between the decisions.
  A decision the current bot makes differently is marked, so a fault that is already fixed shows as one.
- **Its tools:**
  - `rules_lookup`: the vendored comprehensive rules, by number, sub-point or words.
  - `card_text`: a card's printed text.
  - `position`: the whole board before any action.
  - `decision`: every option at a decision.
  - `history`: both players' actions in a range, optionally only those naming a card or server.
- **The prompt:** a short primer on timing (when ICE is rezzed, what a pass is) and on what competent
  players know, since the model can't be assumed to know Netrunner.
- **Grounding:** a finding is "cited" when a citation resolves to a real rule or card. A finding on a
  sequence that isn't one of the bot's decisions is dropped.
- **An optional second look (`--check`):** each finding goes back to the model in a fresh conversation
  told to refute it from the game. It is off by default; see below.

### Gate evidence

On the corpus of Corp v9 games (`standard-w4a` in `artifacts/ai-lint/corpus-w4a-vs-now`, the P1
corpus), with Qwen3.8-27B as byteshape's IQ4_XS quant (3.84 bits per weight, with MTP; the server
named it `qwen3.8-27b-mtp`), temperature 0.6, top-p 0.95, top-k 20, reasoning medium and up
to 40 tool calls. A game takes 3 to 25 minutes, typically 6 to 10.

**The playtest's faults:** the 28 September playtest found five Corp v9 faults. The reviewer re-found
three, with citations, each in several games:
- **ICE on empty remotes:** found in 5 games.
- **Malapert Data Vault in a central, or a second copy over the first:** found in 5 games.
- **Centrals left without ICE or behind one weak ICE:** found in 4 games.

It did not report the other two: Funhouse left unrezzed while the Runner farmed Archives, and Pharos
never advanced.

**Triage of 34 findings** (the user's verdicts) from 24 reviews of 15 games. One review ended with an empty answer; the tool
loop now asks again (the verdicts and evidence are in
[`WAVE_4_5_P2_TRIAGE.md`](WAVE_4_5_P2_TRIAGE.md)). The prompt and dossier changed between rounds, as
the triage showed what the model was missing:

| Round | Configuration | Findings | Confirmed | Unsure | Wrong | Duplicate |
|---|---|---:|---:|---:|---:|---:|
| A | decisions only | 5 | 2 | 0 | 3 | 0 |
| B | + each turn's board, the opponent's actions, the primer | 12 | 8 | 0 | 3 | 1 |
| C | + `history` | 17 | 14 | 0 | 2 | 1 |
| All | | 34 | 24 | 0 | 8 | 2 |

Round C's precision is 14 of 16 distinct findings confirmed, with 2 wrong (finding 34 changed after the
Codex review). The triage also named faults
beyond the findings (agenda placement, Urtica Cipher, Sprint, budgeting), listed at the end of the triage
record.

**Why findings were wrong:**
- Runs and accesses that never happened, the main cause in rounds A and B. The board lines and the
  `history` tool cut it.
- Card properties the card doesn't have: Ballista called unique.
- Misread rules: Urtica Cipher's Ambush damage needing a rez; rezzing ICE as an action.
- A plan the credits couldn't pay for: rezzing Malapert and advancing with 1 credit.

**Faults no playtest had reported:**
- **A second unique upgrade beside the first (Malapert Data Vault, Anoetic Void).** P1's
  `unique-replaces-own-copy` now finds it too. Corp v11 and later already refuse it.
- **An agenda scored from Malapert's server with Malapert unrezzed.** Corp v13 and later already rez it
  before the score.
- **Advancing a trap the Runner never runs on, at match point with no agenda installed, instead of
  drawing for the last agenda.** Still to check against Corp v19.
- **A poverty loop:** each turn it gains a credit and spends it advancing Urtica Cipher, so a Hedge Fund
  held for 15 turns is never affordable. Still to check against Corp v19.

**The second look doesn't help yet.** In round C it refuted all five findings it checked: one rightly
(runs that never happened), and four wrongly, all real faults. It catches false facts but calls bad
strategy "a judgement call". The wrong refutations rest on its own errors: an installed card "entering
rezzed", and reading only part of the history. So it stays opt-in.

**Comparison runs:** the runs below are on round C's three Quick and Dirty against Party Hard games. The
baseline's six findings carry the user's verdicts; the other runs' findings carry Claude's.

**Temperature:** the server's recommended 1.0 against 0.6:

| Temperature | Findings | Confirmed | Unsure | Time |
|---|---:|---:|---:|---:|
| 0.6 | 6 | 6 | 0 | 1002 s |
| 1.0 | 6 | 5 | 1 | 1490 s |

Both found the same core faults: empty remotes, the two Malapert copies, Malapert unrezzed at the
score. At 1.0 the model wrote more and made one factual slip (a score before the upgrade was
installed), so 0.6 stays the default.

**Another model, gemma-4-26B-A4B (MoE, Q4, 1 October):** run with its model-card sampling (temperature
1.0, top-p 0.95, top-k 64) on round C's last eight games.

| | Qwen 27B | Gemma 26B-A4B |
|---|---:|---:|
| Findings | 13 | 6 |
| Confirmed | 7 | 0 |
| Wrong | 2 | 6 |
| Tool calls | about 230 | 0 |
| Time | 3421 s | 615 s |

Gemma never got a tool call through, so it guessed card costs and rules. It said agendas score from
HQ, invented text for Retribution, and read a declined replacement as taken. The cause looks like the
server, not the model:
- Its reasoning ends with the call it means to make ("I'll use `card_text`"), and the reply holds only
  a placeholder answer.
- A short prompt does get a tool call through.
- Forcing a tool call (`tool_choice: required`) hung the long requests.

It is about 5.5 times faster, so it is worth another try once this llama.cpp build gets its tool calls
after thinking through. `--model` names the model in the reviews. `--min-tool-calls N` sends an answer
back once when it came with fewer tool calls; gemma resubmitted unchanged.

The same 26B on LM Studio (a llama.cpp engine too) failed the same way.

**gemma-4-31b (dense, on LM Studio):** this one does make tool calls, several at once. It ran on round
C's three Quick and Dirty against Party Hard games, with its model-card sampling:

| | Qwen 27B (temperature 0.6) | Gemma 31B |
|---|---:|---:|
| Findings | 6 | 5 |
| Confirmed | 6 | 3 |
| Unsure | 0 | 0 |
| Wrong | 0 | 2 |
| Tool calls | 93 | 17 |
| Time | 1002 s | 455 s |

Its confirmed findings are faults Qwen also found: the empty remotes, and the second Malapert. Its two
wrong ones:
- **"Upgrades can't be rezzed":** from a game where it made no tool calls.
- **A trigger misread:** the log showed it only by its catalog id. The dossier now names a trigger's
  source card, Send a Message's free rez.

It looks things up far less than Qwen, and is wrong where it doesn't.

**A larger Qwen quant, unsloth's UD-Q4_K_M (also with MTP), against byteshape's IQ4_XS, same three
games and settings:**

| | Qwen 27B IQ4_XS (round C) | Qwen 27B UD-Q4_K_M |
|---|---:|---:|
| Findings | 6 | 5 |
| Confirmed | 6 | 3 |
| Unsure | 0 | 1 |
| Wrong | 0 | 0 |
| Duplicate | 0 | 1 |
| Distinct faults confirmed | 6 | 3 |
| Tool calls | 93 | 67 |
| Time | 1002 s | 916 s |

**What each quant found:**
- **Both:** the empty-remote sprawl in two games, and Malapert unrezzed at the score.
- **UD-Q4_K_M only:** with the sprawl, that R&D was left open on turn one.
- **The IQ4_XS run only:** the second Malapert beside the first, the second Malapert that trashed the first in
  HQ, R&D behind one Whitespace all game, and the Sprint that shuffled away Government Subsidy.

Neither quant made a wrong finding. Three games are too few to separate the quants: a single review
varies more from run to run than this.

**Codex as the reviewer (`ai-codex-review`, model gpt-6.1-sol, reasoning medium):** the Codex CLI can't call
the reviewer's tools. Instead it reads the same material as files in a read-only sandbox: the decision log,
every decision's options, the board before each decision, the history, every card's text and the rules. Its
answer is held to the findings schema with `--output-schema`, and it shares the local reviewer's primer and
parser. Unlike the local model, it sends the game and the card texts to OpenAI.

Round C's eight games (the user agreed with Codex's findings):

| | Qwen 27B IQ4_XS | Codex gpt-6.1-sol |
|---|---:|---:|
| Findings | 12 | 26 |
| Confirmed | 10 | 25 |
| Unsure | 0 | 0 |
| Wrong | 2 | 0 |
| Duplicate | 0 | 1 |
| Time per game | about 7 minutes | about 100 s |
| Tokens per game | 30k to 60k generated | about 280k input (78% cached), 1.8k output |

**What Codex found that Qwen didn't:**
- **A winning agenda shuffled away:** at 6 points, with 44 credits and a remote behind three ICE, Sprint put
  Send a Message, the 3 points that would win, back into R&D, where the Runner later stole it.
- **An agenda behind ICE the Corp couldn't afford to rez:** the Runner passed it unrezzed and stole it.
- **Built to Last unused:** Weyland gets 2 credits for advancing a card with no counters. Clicking for a
  credit instead of advancing gives up a free counter, in two games.
- **Removal aimed at the wrong breaker:** Retribution trashed Cleaver while Buzzsaw was getting through both
  centrals' code gates; Ballista's subroutine trashed Unity instead of the Cleaver breaking HQ's Palisade.
- **The tied scoring server:** Orbital Superiority went behind one unrezzed Palisade rather than two rezzed ICE
  and Malapert, on a tie the heuristic broke at random.
- **Finding 34 differently:** with three clicks the Corp could have rezzed Malapert and still scored (credit,
  rez, credit, advance), so the rez wasn't out of reach; the triage now confirms it.

**What it missed:** Qwen's confirmed Sprint finding in one game (Government Subsidy shuffled away).

**Use:** about 2.2 million input tokens and 14k output for the eight reviews, about 1% of the user's ChatGPT
plan. Codex is the reviewer for now; Qwen runs later.
`--model`, `--reasoning` and `--timeout-minutes` set the run.

### Codex reviews of the current bots (1 October)

A fresh corpus of the current bots (`artifacts/ai-lint/corpus-v19`): Corp v19 against Runner v13, 96 games,
of which Codex reviewed 8 the Corp lost and 8 the Runner lost, across the decks. It made 22 Corp findings and
26 Runner findings. The user agreed with Codex's round-C findings; these are Claude's first pass.

**Corp v19, by theme:**
- **Urtica Cipher over-investment (3 games):** it keeps advancing a trap the Runner has already seen, long
  after its damage exceeds the Runner's hand, while scoring stalls.
- **The winning agenda thrown away:**
  - Sprint shuffles the last agenda back into R&D at 6 points, with a protected remote ready.
  - At match point it plays economy instead of advancing an installed winning agenda.
- **The rez reserve blocks a central rez that would end the run (3 games):** "keep credits for the agenda
  ICE" and "preserve the ICE budget" decline Diviner, Ansel or Whitespace on a central, and the Runner steals
  from it.
- **Agendas exposed:**
  - installed behind ICE the Runner's breakers pass cheaply (4 times in one game);
  - installed behind ICE the Corp can't afford to rez.
- **ICE placement:** Archives gets ICE while HQ or R&D is the server under attack (3 games).
- **Spin Doctor's ability unused (3 games):** a second copy rezzed, an install over it, or a run that trashes
  it, without first shuffling useful cards back from Archives.
- **Economy:**
  - Planogram's draw chosen at 1 credit;
  - a rezzed Regolith left unused;
  - expensive ICE installed without the money to rez it.
- **One-offs:** Anoetic Void used where nothing is worth protecting; Hansei Review forcing an agenda into
  Archives; AMAZE Amusements against a steal that wins the game.

**Runner v13, by theme:**
- **Runs that can't succeed (4 games):** into rezzed Diviner or Ansel 1.0 with no breaker for it. Against
  Ansel the run loses a program each time and can't steal anyway.
- **Flatline risk ignored (3 games):** runs with one or two cards in grip into a heavily advanced card or past
  damage ICE; two of them end the game.
- **Free credits declined (2 games):** Zahya's optional income after an access scores 0 and is refused,
  repeatedly, while the Runner is broke.
- **Tags removed one at a time (2 games):** 2 credits and a click per tag, staying tagged.
- **Runs for nothing:** on an Archives with no agendas or rewards; on a known Regolith it can't afford to
  trash; on a known Urtica Cipher it saw in R&D.
- **Economy:**
  - Smartware Distributors discarded while a duplicate Cleaver is kept;
  - Fermenter never installed;
  - Creative Commission played with clicks left;
  - credits loaded onto Smartware while tagged.
- **Installs that help nothing:** Buzzsaw against no code gates; a second Cleaver.
- **Missed pressure:**
  - Tread Lightly not used to make a remote's ICE unaffordable;
  - a remote behind one Funhouse never contested;
  - a Jailbreak abandoned over damage it could take.

**Still open:**
- **The two faults it missed:** Funhouse unrezzed on a farmed Archives, and Pharos never advanced.
  Both are lint findings, and P1 already finds them.
- **The second look:** it needs to read the whole history and treat strategy as in scope before it can
  filter findings.
- **Reviews of the current bots:** reviews of Corp v19 and Runner v13 games, which would find faults
  P1 doesn't know, come with P3 (its gate counts confirmed new faults).

## P3 LLM opponent

An agent that makes a seat's strategic choices from a model, while the heuristic plays the rest of the seat:
breaking, paying and the priority windows. The strategic choices are the turn's actions: installs, runs,
advances, plays, scoring, drawing and credits.

- **What it reads (`LlmOpponentAgent`):** only its own observation, through the same view a player gets, so
  face-down cards stay unnamed and the other hand is only counted. It gets the board, the text of every card
  in view, its last few choices, and the numbered legal options with the heuristic's scores and reasons.
- **What it can play:** only the engine's own actions.
  - An answer that isn't one of the options plays nothing.
  - The heuristic's choice stands after a second bad answer, or when the model doesn't answer.
  - The heuristic is asked every decision, so its memory stays in step.
- **Who decides (`IStrategicDecider`):**
  - **A local model over chat (`ModelDecider`, `scripts/ai-llm-play.sh`):** one conversation per decision.
  - **Codex (`scripts/ai-codex-play.sh`):** it plays a whole game in one session through `ai-mcp-game`, a
    Model Context Protocol server over stdio.
    - Its tools are `get_choice`, `choose(option, reason)`, `card_text` and `rules_lookup`.
    - An option out of range returns an error and plays nothing.
    - If Codex leaves, the heuristic finishes the game.
    - Each decision is logged to `progress.log`.
- **Saved games:** games of both are saved as the ladder saves them, with the decisions and reasons beside
  them, so `ai-lint`, `ai-llm-review` and `ai-codex-review` read them. Saved games name the seat `llm` or
  `codex`, which replays with the heuristic, so a review shows where the model's choices differ from it.

### Gate evidence

**Qwen 27B (IQ4_XS, reasoning low):** one game as Corp, which it won. 51 decisions were asked, it chose
against the heuristic 15 times, and it gave no invalid answers and had no failures. But at about 95 s a
decision the game took 81 minutes, too slow for 50 games.

**Codex (gpt-6.1-sol, reasoning medium) against Corp v19 and Runner v13:** batch 1, 20 games in 4 parallel
streams.

| Seat | Games | Codex won | Stalls or errors | Invalid choices |
|---|---:|---:|---:|---:|
| Corp | 10 | 9 | 0 | 0 |
| Runner | 10 | 9 | 0 | 0 |

- **Who chose:** Codex made every strategic choice (about 800), against the heuristic about half the time.
- **Time:** a game takes 2 to 14 minutes, typically 5.
- **Cost:** about 1.6 million input tokens a game (over 90% cached) and 3k output.
- **Against the repaired bots (batch 2):** 30 games against Corp v20 and Runner v14 on 8 streams. As Corp,
  Codex won 11 of 15; as Runner, 15 of 15. Runner v14 took four games where v13 had taken one.
- **The P3 gate's first half:** the 50 games had no stalls, errors, invalid choices or heuristic fallbacks, across
  2,074 strategic decisions.
- **How it plays:** its reasons read like a player's. It weighs the Runner's breakers against its ICE ("Buzzsaw
  makes Whitespace porous, so dig for stronger remote ice before exposing another agenda"), baits with an
  advanced Urtica Cipher against a short grip, and plans scoring windows.

### Bots repaired from the Codex reviews

The Codex reviews of the current bots (above) became two versions, each with saved-game regression tests that
ask the old and the new version at the recorded decision (`RunnerCodexV14Tests`, `CorpCodexV20Tests`).

| Version | Fixes | Gateway ladder, 240 games a seed |
|---|---|---|
| Runner v14 | Zahya's income taken; no run into a Diviner certain to end it (all-odd grip), or past a steal-forbidding Ansel 1.0 | -11 and +32 over v13 |
| Corp v20 | No advancing a trap already lethal; Sprint keeps its best cards and the winning agenda; R&D, and HQ holding an agenda, are agenda servers for the rez reserve | +22 and +54 over v19 |

Both are level or better, so both became the defaults. A second round came from the reviews of the bots'
losses to Codex (below):

| Version | Fixes | Gateway ladder, 240 games a seed |
|---|---|---|
| Runner v15 | Contests the remote at the Corp's match point, a possible trap notwithstanding; installs Fermenter for its growing counters; no Archives run whose only gain is a discount on a redundant card | +86 and +44 over v14 |
| Corp v21 | An agenda that can't score this turn waits in HQ while the Runner's rig, given a full turn of credits, gets into its server; no ICE on an Archives that protects nothing | 0 and +11 over v20 |

Both are now the defaults. Corp v21's two rules rank below every other play, like the rule against an
unprotected agenda: as score penalties, the planner's turn-end rollouts, which can't see the steal, brought the
plays back.

### Reviews of the games against Codex

`ai-codex-review` on the bot's side of 16 batch-2 games the bot lost: 8 for Corp v20 (20 findings) and 8 for
Runner v14 (23 findings). That gives the P3 gate's second half: confirmed new faults, which the next versions
take up.

**Corp v20:**
- **Agendas behind ICE the Runner's installed breakers beat (6 games):**
  - a lone Palisade against Cleaver;
  - Whitespace that Botulus already bypassed, or against Buzzsaw;
  - Funhouse against a Runner with clicks to clear its tags.

  The bot counts any ICE as protection, without checking the rig.
- **ICE on Archives** while it holds nothing worth protecting (3 games).
- **Urtica Cipher advanced after the Runner has seen it,** below v20's lethal cap.
- **Built to Last unused,** and Pharos advanced to just below its threshold or on the wrong server.
- **Smaller ones:**
  - Planogram's draw mode at the wrong time;
  - Conduit's counters never purged;
  - Send a Message's free rez on a remote nobody runs;
  - Spin Doctor discarded over a duplicate unique card.

**Runner v14:**
- **The scoring remote not contested (5 games):** an advanced agenda in a remote is left alone with the Corp
  at match point and the money to get in, and in two games that agenda wins the Corp the game.
- **Fermenter never installed** (2 games, 3 with the v13 reviews).
- **Runs on an Archives with no agendas or rewards** (2 games, 3 with the v13 reviews).
- **Duplicate installs:** a second Marjanah, Mutual Favor fetching one, and Verbal Plasticity replacing itself.
- **Clearinghouse's damage left on the board** when trashing it was cheap.
- **Smaller ones:** Tāo's ICE swaps, the order of economy plays, and break planning in an encounter.

### Round three: batch 3 and its reviews

**Batch 3, 24 games against Corp v21 and Runner v15:** Codex won 6 of 11 as Corp and 10 of 12 as Runner. The bots
won 30% of their games against Codex, against 10% and 13% in the first two batches. Runner v15 alone took 5 games.
One game failed when OpenAI's model was at capacity mid-game. Such a game is now finished by the heuristic and
replayed, up to three times.

**Codex's reviews** of the 14 games the bots lost (17 Corp and 17 Runner findings) became two more versions:

| Version | Fixes | Gateway ladder, 240 games a seed |
|---|---|---|
| Corp v22 | A full HQ no longer pushes an agenda into a server the Runner's rig breaks (v21's rule exempted it, three steals); an asset loading three times its cost is rezzed despite the ICE budget (Regolith Mining License); a broke Corp builds money before purging | +11 and 0 over v21 |
| Runner v16 | No unique card over its own copy (Verbal Plasticity, three times); Creative Commission waits for the last click; Docklands Pass's extra HQ access is worth installing | +22 and +45 over v15 |

Both are now the defaults.

**Still open from the reviews:**
- **The Corp's memory of seen traps:** it keeps advancing an Urtica Cipher the Runner has accessed. Remembering
  that needs a field in the Corp agent's checkpoint.
- **Money for a match-point run:** the Runner doesn't plan it.
- **Conduit:** multi-access programs aren't valued.
- **Break planning against Ansel 1.0.**
- **Tread Lightly against a remote.**
