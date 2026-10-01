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
| C | + `history` | 17 | 13 | 0 | 3 | 1 |
| All | | 34 | 23 | 0 | 9 | 2 |

Round C's precision is 13 of 16 distinct findings confirmed, with 3 wrong. The triage also named faults
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

**Still open:**
- **The two faults it missed:** Funhouse unrezzed on a farmed Archives, and Pharos never advanced.
  Both are lint findings, and P1 already finds them.
- **The second look:** it needs to read the whole history and treat strategy as in scope before it can
  filter findings.
- **Reviews of the current bots:** reviews of Corp v19 and Runner v13 games, which would find faults
  P1 doesn't know, come with P3 (its gate counts confirmed new faults).
