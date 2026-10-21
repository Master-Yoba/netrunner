# What chiriboga's opponent AI offers us

A read of [chiriboga](https://github.com/Greenpaulo/chiriboga)'s AI documentation on 12 October 2026: its Corp AI
roadmap and architecture, its Runner AI, its principles, and how it judges an AI change. Chiriboga is a JavaScript
Netrunner client with rule-based Corp and Runner AIs. Our Corp policy already borrowed one idea from it (the Spin
Doctor timing in `CorpHeuristicPolicy.ArchivesShuffleValue`).

## Update, 19 October 2026

Chiriboga's `main` took 66 commits from 12 to 19 October:
- **Cards:** it finished Vantage Point (all 13 of its batches) and the last Elevation cards. Its next set is Downfall, an
  older FFG set outside Startup.
- **Corp AI:** about 29 roadmap items are now closed, up from 16, but several closed as failures. L8.2 (remote deception
  profiles) and L9.0.1 (adopting honest lockout everywhere) failed their full gates, and their policies were removed.
  Next are I2 (choosing the ICE and server that most improve security, weighted by what a breach would cost), then L8.9
  (why ICE placement still reveals which remote holds an agenda), then I3 and the rest of the install track. All three
  are only proposed.
- **Runner AI:** no new items. Only randomness (D2) and information leaks (D3) are done.
- **Measurement:**
  - F11, paired metric guards: a change names its target metric and guards, each judged on paired resamples of the
    same games;
  - F12, realized-defense measurement: how much ICE ends up on each kind of root;
  - a fresh main-against-checkout benchmark;
  - F13, stall diagnostics, still proposed.

What this changes for us:
- **I2** is the same lever as our largest Corp leak, agendas stolen from remotes. Its spec (marginal security per ICE
  and server, breach-cost weighted) is the design to borrow for our Corp's ICE installs.
- **F11** is the measurement idea we listed as "worth taking next" (one target metric and guards), now built and in use.
- **Two more failed deception ideas** (L8.2, L9.0.1) join L7.1. They support keeping bluffing out of our track.

## Where chiriboga stands

- **Corp AI:** about 16 of 50 roadmap items done, mostly the server-security groundwork (ICE and breaker
  arithmetic, shared rez budgets, threat memory, the breach-loss interrupt). Install planning, the next big area, is
  still proposed.
- **Runner AI:** a run calculator searches paths through ICE with weighted costs. Its own roadmap so far is
  principle debt (randomness, information leaks) and a proposed rework of keep and discard decisions.
- **Measurement:** its seeded paired harness (F4) arrived around 5 October 2026. Most behaviour changes are written
  but held off until their gates are run. It has no rating ladder.

We are ahead on measured strength and on breadth of play. Chiriboga is ahead on how it decides whether a change helps,
and on the Corp's reasoning about whether a server is actually secure.

## Measurement: taken

**Paired comparison.** Chiriboga plays every seed twice, with the change off and on, and judges the paired
difference. Our ladder plays the same deals for a candidate and its baseline, but its bootstrap resampled single games,
which threw the pairing away and made a candidate's interval about three times too wide. It now resamples deals (see
"Paired ladder intervals" in `BOT_VERSIONS.md`).

**Changed games.** Chiriboga's gate fails a change that altered no game, and reports how many it altered. Our ladder's
`paired` line now gives the deals a candidate changed. A change confined to one deck (Corp v36, Nebula only) is judged
by those deals without dilution.

## Measurement: worth taking next

- **One target metric and guards per change, set before the run.** Each gate names the metric the change is for
  ("high-consequence breaches per game go down"), and guards with a tolerance on what it must not worsen ("points
  stolen rise by at most 0.2 a game", "win rate drops by at most 2 points"). Every row is judged on its paired
  interval. Our lints count the targets (remote steals, flooded draws, Measured Response deaths), so a ship rule
  could name its lint as the target and Elo as a guard. Batch 10 shows why the target alone isn't enough: it cut its
  target by 28% and the games ended as agenda losses instead.
- **Thresholds fixed before results, never after.** Chiriboga's owner guide says so in as many words. We declare ship
  rules before each run; the same rule should cover a guard's tolerance.
- **A failed gate closes the idea.** The option and its code are removed in the same change; tools, logging and real
  boards stay; the next idea is a new ticket. We keep every unshipped version constructible, which grows the version
  switches in the policies (Runner v29 to v37). Pruning versions that failed, keeping their records, would keep the
  policy readable.
- **Replay with a diff.** `ai-batch replay --diff` replays one game with the change off and on and prints only the
  lines that differ; the first hunk is the decision the change moved. Our probes compare one decision at a time by
  hand. A ladder-side "first decision where the two versions diverge, per changed deal" would show what a candidate
  changed across all its games.
- **Boards from real logs only.** Chiriboga's gates once passed on boards an agent had edited until the change fired;
  on the real board the change made the Corp worse. Its start boards are now built from logs, with edits limited to
  crash fixes and recorded. Our saved-decision fixtures come from ladder games unchanged; any future "edit the state"
  test (`SavedDecision.ChooseAsync`'s `edit`) should say what it edits and why.
- **Their cautions apply to us.** Better against our own Runner bot may not be better against a person: a Corp can
  learn to exploit the Runner bot's blind spots. A pass by a huge margin deserves a look at the games.

## Corp AI ideas worth trying

In order of what they could do for our largest Corp leak, agendas stolen from remotes (0.71 a game, most behind one
rezzed ICE on the first Runner turn after the install):

1. **Security from the Runner's real capability.** A server is secure only if the Runner can't pay the *mandatory*
   break cost: only what avoids end-the-run or lethal damage counts, optional taxes and tags don't. The Runner's
   money is its effective ceiling: credits, recurring and hosted credits it may use there, bad-publicity credits, and
   clicks it could turn into credits, less one click to start the run. Unrezzed ICE share one rez budget along a route
   rather than each counting the Corp's whole pool. Our Corp's "the Runner gets in" test (v33's `RunnerGetsIn`) is a
   coarser version; this one would let the Corp see that one rezzed ICE is not a scoring server against a full rig.
2. **Rez consistency.** Rez an approached ICE when rezzing it turns a possibly game-winning breach into a prevented
   one, before any reservation for ICE elsewhere. A reservation for another server holds only when a with-and-without
   comparison shows the saved credits change that server from breachable to secure, not merely because it is worth
   more.
3. **The breach-loss interrupt.** When the fair, order-agnostic chance that the next breach of a central wins the game
   for the Runner reaches 35%, a non-winning advancement plan yields to affordable ICE that lowers it, then a purge,
   then emergency acquisition of ICE. A game-winning score is exempt.
4. **Emergency protection.** With HQ insecure and no ICE to install, the Corp draws (or plays a card that finds ICE)
   rather than taking credits that can't change the breach outcome; a full HQ doesn't stop that draw when HQ itself is
   the target.
5. **Purge only when it changes an outcome:** when the post-purge board opens a score or turns a server holding an
   agenda from breachable to secure, not whenever virus counters exist.
6. **Central pressure from the Runner's board.** Installed multi-access and pressure cards (Docklands Pass, Conduit,
   Devadatta Drone) raise a central's threat, capped; hidden run events stay out of it (they belong to threat memory).

What failed in chiriboga, so we don't retry it blind:

- Weighting the central-server penalty by breach consequence raised central steals: HQ's protection score was also
  the bar for scoring in a remote (L7.1, failed gate).
- Rezzing ICE that hosts a Runner card whenever affordable lost points on a real board; its next idea prices the
  tax per run over the runs expected.

## Runner AI ideas worth trying

- **Faction-informed priors for hidden cards.** Chiriboga's Corp estimates the Runner's hidden threats from its
  faction, discounting rather than excluding out-of-faction cards; its Runner prices an advanced unseen remote card
  by the Corp's faction (an advanced card in a Weyland remote may be Clearinghouse). That is our Runner v34 and v37's
  idea, independently.
- **One cost scale for a run path.** The run calculator weighs credits spent 0.6, credits lost 0.7, clicks 0.8,
  virus counters 0.3, damage 1.3, tags 2.4 and serious, moderate and minor effects 3.0, 0.8 and 0.3. When a path
  fails, it retries with unlimited tags, then unlimited damage, to find an exit. Our run model prices these
  separately; a single scale would make paths comparable.
- **Manegarm Skunkworks forks** the path into two clicks or five credits rather than assuming one.

## Process ideas

- **One fact, one place;** split documents by how often they change. Ours mix history and current state in long
  records (`BOT_VERSIONS.md`), which costs every reader context.
- **If a script can check it, don't leave it in prose.** Each rule they turned into a test found violations at once.
- **An outcome line per item** (`adopted`, `not adopted — why`, `blocked — what`) under each title, so its state is
  readable without the whole record.
- **Where agent tokens go.** Their logs showed context re-sent on every call cost far more than output: passing tests
  printing PASS lines were a quarter of all usage, and whole-file reads most of the rest. Silent passing tests and
  small reads are cheap wins.

## Not worth taking

- **Bluffing and deception rules** (unlearnable postures, match-local adaptation, human telemetry): they matter
  against people, and our measurement is bot against bot.
- **Their hook machinery** (`AI*` hooks on card objects, title-literal tests): our behaviour specs already describe
  cards declaratively.
- **Roadmap tooling** (`roadmap.js`, generated blocker headers): our plans are smaller and live in `BOT_PLAN*.md`.
