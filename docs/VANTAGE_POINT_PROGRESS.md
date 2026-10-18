# Vantage Point progress

The record of the [Vantage Point plan](VANTAGE_POINT_PLAN.md)'s slices: status, deviations, evidence and pins.

## VP.1 — Preparation (15 October 2026)

**Status:** complete.

**Card data.**
- `update-card-data.sh` and `build-card-data.py` add the `vp` pack: 66 records from the pinned commit
  `236140cf84491c23995b028bc791281837520b97` into `data/vendor/vantage-point` (local-only and gitignored), with its own
  manifest and the official checks.
- `OfficialCardPacks.VantagePoint` pins its count, path and three sentinel titles. `CardDataResource` embeds it as a third
  pack, and an official build without it fails with an actionable DATA-400 diagnostic, which `verify.sh` checks.
- Fetched again from the pinned commit, the System Gateway and Elevation files came out byte for byte the same, so no
  frozen pin moved.
- The gate worktree links the new directory.

**The Startup pool.**
- `CardPools.Startup` (`sg`, `elev`, `vp`) plays with `VantagePointBehaviorCatalog` (`startup-vp.1`). That catalog
  composes the Gateway + Elevation catalog unchanged and registers no Vantage Point card yet.
- The official fuzz content now loads the Gateway + Elevation pool's own packs, not every embedded pack, so Elevation
  games never see the new cards.

**Formats.**
- Startup 2026 moves to Startup Balance Update 26.09 (D-VP-5), taken from Null Signal's announcement because the pinned
  card data predates it:
  - NBN: Reality Plus is unbanned;
  - Cleaver, Seamless Launch, Offworld Office, Mercia B4LL4RD and Let Them Dream stay banned;
  - a Corp deck may hold at most 4 agendas worth 3 or more points.
- FORMAT-401 tests: Reality Plus isn't banned; five 3-point agendas break the limit; a Vantage Point card is not yet
  legal in the format.

**Coverage.**
- The coverage tool reads a third registry, `data/vantage-point/batch-registry.json`: 66 cards in batches VP.3 (33),
  VP.5 (25) and VP.7 (8), status `planned`.
- The report lists 225 cards: 159 complete, 66 unregistered, no diagnostics. `verify.sh` checks the Vantage Point counts.

**The audit's review.**
- [VANTAGE_POINT_PRIMITIVES.md](VANTAGE_POINT_PRIMITIVES.md) has a "VP.1 review" section. Every card's citations,
  observation, hidden-information and serialization notes are in the registry.
- `data/vantage-point/rulings.json` has 31 rulings citing CR 26.03, each cross-checked against jinteki.net's
  implementation.
- The eight rows "to confirm" use existing vocabulary, so the audit stands at 18 of 66 cards (27%).

**E6.** `card-vocabulary` reads the Startup pool, and `scripts/core-growth.sh` passes the third data root and takes the
third registry. Elevation's whole-set figure was 26% of cards needing no Core change.

**Deviations from the plan.**
- **Vantage Point is not yet a legal pack of Startup 2026.** The plan has it join the legal packs in VP.1. But a
  format is complete exactly when it misses no pack, and a complete format is offered for play. So `vp` stays in
  Startup 2026's missing packs, by its pack code (it was the NetrunnerDB pool id `vantage_point`), until VP.8 completes
  the format. FORMAT-401 now accepts a ban on a card from a missing pack this build embeds (Let Them Dream).
- **Startup 2026 keeps the Gateway + Elevation pool** until VP.8 moves it to the Startup pool, for the same reason.

**Evidence.**
- The official build is clean.
- The Cards tests pass in both content lanes (543), with `VantagePointCardDataTests` new.
- The coverage report is regenerated; its counts are above.

## VP.2 — Primitives A

VP.2 lands in two parts: **VP.2a** (bad publicity, credit sources, scoring and agendas; below) and **VP.2b** (costs and
values: play, click and rez cost modifiers, the new amounts, run facts and the uninstall interrupt).

### Design: bad publicity and credit sources

**Bad publicity** is one counter on the Corp (CR 1.9.5d), kept on `RulesState` and omitted from serialization while it is
zero, so every existing state, replay and pin keeps its bytes. Taking and removing it are instructions with an event, a
cost kind removes it (CR 1.14.3a), and the first time each turn the Corp takes it is an occurrence (Editorial Division).
Bad publicity hosted on a card (Luana Campos) is a card counter, which has no effect while hosted (CR 1.13.3).

**The bad publicity fund** is a run-scoped pool on `RunState`, also omitted while zero: at step 6.9.1b the Runner adds a
credit for each bad publicity the Corp has, and at step 6.9.6b what is left returns to the bank (CR 10.6.3).

**Credits from outside the pool.** A Runner payment during a run already chooses how much comes from the credit pool and
how much from run-scoped credits (Overclock's). That one choice stays, and its second share becomes "credits from
outside the pool", spent in a fixed order:

1. the bad publicity fund;
2. credits on the active run event that isn't stealth (Overclock);
3. credits on the active run event that is stealth (Aircheck);
4. hosted credits on installed stealth cards that may be spent during runs (Methuselah, Touchstone).

Non-stealth credits go before stealth ones and expiring credits before lasting ones, so the order never costs the Runner a
payment it could otherwise make (an engine ruling, as Elevation's for declared-use credits). A cost payable only with
credits from stealth cards (Corsair, Baker, Lampades) draws only on 3 and 4, in that order. Shackleton Grid's occurrence
fires whenever a payment's outside share is above zero, so it counts the fund as Null Signal's rules and jinteki.net do.
While Aircheck is active the Runner's credit pool offers nothing to a payment and can't lose credits.

### VP.2a: what landed (15 October 2026)

No Vantage Point card is registered yet; each primitive is proven on test-only probe cards in the harness
(`ElevationHarness.VantagePointProbe`), and every existing game, replay and pin keeps its bytes: each new field is omitted
while unused, and the opt-in turn events are recorded only by a catalog that declares them.

- **VP-BAD-PUBLICITY:** the Corp's counter on `RulesState`, the take and remove instructions with their event, the first
  each turn as an occurrence, and the run's fund (`RunState.BadPublicityCredits`, filled at step 6.9.1b, spent first,
  returned at the end).
- **VP-STEALTH-CREDITS, VP-POOL-LOCK, VP-CREDITS-OUTSIDE-POOL:** as the design above. `CreditSources` computes the outside
  pool for the engine and the validator alike; costs may name a source subtype (`BehaviorCost.CreditSourceSubtype`); run
  events may lock the pool (`RunEventBehaviorSpec.LocksCreditPool`); hosted credits restricted to run costs pay any cost
  during a run; spending from outside the pool is an occurrence a card on the attacked server may trigger on.
- **VP-SCORE-STEAL-COST:** an agenda's own steal clicks (`AgendaBehaviorSpec.StealClicks`) and active cards' steal credits
  (`CardBehaviorDefinition.StealCredits`) turn the automatic steal into a paid one, with "no action" declining it
  (CR 7.1.6a); `StealCosts` computes them for the engine and the validator. A score cost moves a card to the Corp's score
  area as an agenda (`BehaviorScoreCostAsAgenda`, Word on the Street).
- **VP-CARD-AS-AGENDA:** `AddSelfToScoreAreaAsAgenda`, recorded in `RulesState.ScoredAsAgendas` with its points and whether
  it may be forfeited; forfeits read the recorded points, and one that can't be forfeited never matches an agenda filter.
  An operation that becomes an agenda finishes its play in the score area.
- **VP-AGENDA-POINTS-MODIFIER:** `AgendaBehaviorSpec.RunnerScoreAreaPoints`, applied when stolen and when it leaves.
- **VP-SCORING-FACTS:** opt-in turn events (`BehaviorCatalogBuilder.RecordTurnEvents`) for scores and scores of agendas
  installed earlier; `TurnEventCountAtLeast`, `SourceScoredThisTurn`, and the trigger conditions
  `SubjectInstalledThisTurn` and `SubjectNotInstalledThisTurn`.
- **VP-ADVANCED:** `CardAdvanced`, raised by the basic advance action (placing counters by an effect isn't advancing).

**Evidence.** 21 probe tests (`tests/Netrunner.Cards.Tests/VantagePoint/`); the Core, Cards, AI and Match suites pass
(Match 1,154, including the frozen tactical pins, which caught two definition properties serialized without their
omit-when-default attributes before they could move a pin).


### VP.2b: what landed (15 October 2026)

Again on probe cards only (`ElevationHarness.VantagePointProbe`, codes 90013 to 90023), with every existing game, replay
and pin keeping its bytes.

- **VP-TRASH-AS-COST:** a card play may carry the existing structured additional cost `TrashCards` for one card of a kind
  (Sell Out's installed resource). The play action names the card (`PlayCardParameters.TrashCostCardId`, omitted while
  null), so a play is issued once for each card that may pay it, and is not offered when none may. Trashing a card from
  the grip as the cost of an optional trigger (Methuselah) is already expressible: a choice option with the same cost
  over the grip. A play by an effect has no action to name the card, so a card with a trash cost can't be played that
  way (an engine limit; no Vantage Point card plays another card that has one).
- **VP-DYNAMIC-REZ-COST:** the cost modifier `SelfRezCreditCost`, counting other installed Corp cards matching a filter
  (Reverb) or applying under a turn-event condition (Hype Machine); ICE and non-ICE rez quotes include it.
- **VP-PLAY-COST-MODIFIER:** `RunEventBehaviorSpec.CostLoweredPerIceOn`, a run event's play cost lowered for each piece of
  ICE protecting a named server (Tailgate), in the affordability check, the issued cost and the payment alike.
- **VP-CLICK-COST-MODIFIER:** `CardBehaviorDefinition.FirstDoubleOperationClickDiscount`, the first Double operation each
  turn costing a click less (Synchrocyclotron), from the opt-in turn event `DoubleOperationPlayed`.
- **VP-AMOUNT:** `ChosenCardsPrintedRezCost` (realloc()) and `ProtectedRemotesWithRootCards` (Flood the Market); the
  advance instruction accepts a dynamic amount.
- **VP-RUN-FACTS:** the condition `SuccessfulRunOnEveryCentralThisTurn` from the Runner's turn facts, and the opt-in turn
  event `IceRezzed`, recorded by every ICE rez (on approach, by an effect, ignoring costs).
- **VP-UNINSTALL-INTERRUPT:** `CardBehaviorDefinition.ReturnsHostedBadPublicity`: when the card leaves an installed zone,
  the Corp takes the bad publicity it hosts before its counters are cleared (Luana Campos).

**Evidence.** 11 further probe tests (`VantagePointCostTests`, 32 Vantage Point tests in all); the Core, Cards, AI, Match
and CLI suites pass.

### VP.2c: the audit's misses (15 October 2026)

Binding VP.3's cards found primitives the audit had missed (the primitive audit's "VP.3 binding review" lists them). The
nine VP.3 needs landed here, before any card was registered, on probe cards 90024 to 90031:

- **VP-BAD-PUBLICITY**, its condition: `BehaviorCondition.BadPublicityAtLeast` (Luana Campos, Scapegoat).
- **VP-ENCOUNTER-SUBTYPE:** `BehaviorCondition.EncounteringSubtype`, the encountered ICE's printed or gained subtype
  (Corsair).
- **VP-ALLOTTED-CLICKS:** a negative `GainAllottedClicksNextTurn`, never below zero clicks (Caveat Emptor).
- **VP-RUN-EVENT-REMOVED** and **VP-RUN-PROTECTED-SERVER:** `RunEventBehaviorSpec.RemovedFromGameAfterRun` and
  `TargetMustBeProtected` (Take a Dive, Kompromat); an ended event's run-end abilities still resolve from removed-from-game.
- **VP-FIRST-EVENT:** `OccurrenceKind.FirstEventPlayedThisTurn`, from every play path (Touchstone).
- **VP-HOSTED-SERVER:** a RunSuccessful trigger may require its source in the attacked server, and a card hosted on ICE
  is in its host's server (Stowaway).
- **VP-INSTALL-FACEUP:** `AgendaBehaviorSpec.InstallsFaceUp`; such an agenda's triggers are active while it is installed
  (Sacrifice Zone Expansion, now in VP.5).
- **VP-REZ-SERVER-KIND:** a CardRezzed trigger may name the server kind (Grubber).

Five cards moved to later batches whose primitive slices own what they lack: Kompromat, Reanimation Protocol and
Sacrifice Zone Expansion to VP.5; Lotus Haze and Hype Machine to VP.7. VP.3 is 28 cards.

**Review (PR #152).** Codex's four findings, all fixed:
- an advance by a dynamic amount reported the static amount (zero) in its event;
- an unrestricted run payment could spend stealth credits before non-stealth ones, leaving a later stealth-only cost
  unpayable;
- hosted credits spendable on any cost weren't offered for run costs;
- a null card scored as an agenda was accepted when a state was read.

## VP.3 — Batch 1 (15 October 2026)

28 cards on `feat/vp3-batch1`:
- **Runner:** Take a Dive, Corsair, Virtual Intelligence P.I., Sell Out, Tailgate, Borrowed Goods, Underdome
  Irregulars, Methuselah, Touchstone, Stowaway and Word on the Street.
- **Corp:** Méliès City Luxury Line, Synchrocyclotron, Caveat Emptor, realloc(), Retirement Plan, Esca, Editorial
  Division, Witch Hunt, Magistrate Revontulet, Nihilo Agent, Grubber, Flood the Market, Scapegoat, Luana Campos,
  Myōshu, Vulture Fund and Let Them Dream.

- **Specs:** `src/Netrunner.Cards/VantagePoint/`, composed by `VantagePointBehaviorCatalog` (version `startup-vp.3`),
  which records the opt-in turn events the batch reads. Elevation's catalog and every Elevation card's behavior
  fingerprint are unchanged.
- **Tests:** 41 card tests (`VantagePointBatch1RunnerTests`, `VantagePointBatch1CorpTests`), at least one per card, on
  a text-free runtime catalog: Vantage Point's stats join `ElevationTestMetadata`, so the synthetic lane runs them.
- **Registry and coverage:** VP.3 complete; the report shows Vantage Point 28 of 66 registered, tested and complete.

### What binding found

- **Engine fixes:**
  - A mode option that installs from a zone was never offered: only an install of a searched card in custody is an
    "install option". This affected Retirement Plan, and Elevation's Mycoweb resolving Ansel 1.0's install subroutine.
  - The rig is a shuffle source (Scapegoat shuffles an installed Runner card into the stack).
- **VP-CHOOSE-AS-MANY-AS-ABLE:** an exact count that takes every candidate when there are fewer (realloc()'s "choose 2"
  with one rezzed piece of ICE). The one Core change a VP.3 card needed beyond VP.2.
- **Bots:**
  - The heuristic policies value taking and removing bad publicity and a card scored as an agenda.
  - The Runner policy and the contract agent take paid steals (Méliès City Luxury Line, Magistrate Revontulet).
  - The agent features carry the Corp's bad publicity, omitted while zero.
  - The misplay sweep's fixes, below.
  - These paths are reached only by Vantage Point cards, so the policy versions stay as they are, and bot-check's regret
    suites and every existing pool's fuzz agree.

### Ladder and the misplay sweep

`scripts/ai-ladder.sh --content vantage-point --entrants random,contract,easy,standard --seeds-per-pairing 3`: 288 games,
0 errors, 2 capped (both with the contract agent). Both sides order random < contract < easy < standard; three seeds don't
separate every neighbour. `ai-offers` over the saved games (the misplay sweep) found five plays the heuristic bots were
offered and never took:
- Flood the Market was never played: its amount was estimated at one counter. It now counts the protected remotes with
  a root card on the observed board.
- The standard Corp never rezzed Magistrate Revontulet, Synchrocyclotron or Nihilo Agent: assets that work just by being
  rezzed fell through to "no current payoff". They are now rezzed for:
  - a steal tax;
  - the Double discount while a Double operation is in HQ;
  - bad publicity that feeds the identity's first-bad-publicity search (Nihilo Agent with Editorial Division).
- Corsair's strength ability was never used: the encounter planner counted only abilities paid with counters. It now
  also plans those paid with credits from cards of a subtype.
- Esca is never rezzed by the standard Corp, which is right: the ambush works unrezzed.

After the fixes, at de6363c: 288 games, 0 errors, 2 capped, the same order. Every swept play is now taken. Corp easy −
contract +333 and standard − easy +146; Runner +299 and +48, all in Elo. `VantagePointBotTests` checks each fix at a
saved decision from the first ladder (`tests/Netrunner.Match.Tests/Fixtures/saved-games/vp3-*`).

### E6

`scripts/core-growth.sh ... --range slice-VP.3=8358757..<head>`: 4 Core files, no new vocabulary, so the script counts
28 of 28 (100%). By hand, realloc() needed the clamp: **27 of 28 (96%)**.

### Simulation decks and the batch proof

- `data/decks/vantage-point-simulation-decks.json`: three Corp decks (Editorial Division, Poétrï Luxury Brands, Weyland
  Consortium) and two Runner decks (Virtual Intelligence P.I., Tāo Salonga). They are ours, within Startup 2026's deck
  rules with Vantage Point counted legal (checked by `CardFormatTests`), and they hold every VP.3 card but the banned
  Let Them Dream. They load through `OfficialPresetCatalog.VantagePointSimulation()`, apart from the embedded catalog.
- `scripts/fuzz.sh vantage-point` and `--content vantage-point` play them on the Startup pool and catalog.
- **The first fuzz run found two defects**, both fixed before the batch closed:
  - the heuristic Runner rejected a paid steal;
  - Scapegoat's shuffle with nothing installed had no source zone.
- **Evidence:** per-slice fuzz at 28dfdfa, `--games-per-pairing 8 --verify-replays-every 8`, on every pool. Every game
  ended in a terminal state and every sampled replay verified:

| Pool | Games | Replays verified | Mean actions |
|---|---:|---:|---:|
| vantage-point | 144 | 18 | 474.7 |
| official | 24 | 3 | 390.9 |
| gateway | 288 | 36 | 411.7 |
| elevation | 1,152 | 144 | 433.4 |
| gateway-elevation | 288 | 36 | 405.5 |
| nsg-core-sets | 2,592 | 324 | 395.8 |

  Only nsg-core-sets moved (396.2 at VP.2): 22 games of NSG's Fashion Lab, whose Mycoweb now offers Ansel 1.0's
  install subroutine (the install-option fix above).

**Review (PR #153).** Codex found that narrowing install options let every zone-install mode through: Retirement Plan
with only ICE in Archives still offered its agenda and asset modes, and choosing one skipped the mandatory install. A
zone-install mode is now offered only while its zone holds a candidate, and the validator mirrors it. When no mode has
a candidate, the choice resolves to nothing, since the card is still playable. The re-run ladder (38d7d12) has 0
errors and 1 capped game; the Synchrocyclotron fixture comes from it, since the changed offers move the old saved game.

### Rulings and simplifications

| Card | Note |
|---|---|
| Esca | Revealed when accessed in R&D as Byte is (Elevation's reveal-on-access). |
| Editorial Division, Retirement Plan | One mode per subtype or kind searched or installed: the filter names one subtype or type. |
| Let Them Dream | One mode per zone searched; the found agenda then goes to HQ or R&D's bottom. Banned in Startup 2026, so in no deck. |
| Scapegoat | Removing bad publicity is offered only while the Corp has some. |

## VP.4 — Primitives B: runs and ICE (15 October 2026)

On probe cards 90033 to 90048 (`VantagePointRunIceTests`), each test citing the rule or the card it serves. Every
existing game, replay and pin keeps its bytes: each new field is omitted while unused, and each new occurrence is queued
only when a trigger waits on it.

- **VP-ICE-PASSED:** the occurrences `IcePassed` (after an encounter, a bypass, or unrezzed ICE; CR 26.03 6.9.4a),
  `IceBypassed` and `IceFullyBroken` (the break that leaves no subroutine unbroken). The trigger condition
  `SubjectOutermostFullyBroken` reads the encounter that just ended (Sipa). `ClicksRemainingAtMost` is Vertigo's "no
  clicks remaining".
- **VP-SUBROUTINE-BROKEN:** `SubroutineBroken`, from every break: a breaker, a bioroid's click, a hosted counter. The
  condition `SubjectStrengthAtMostZero` reads the ICE's current strength quote (The Tungsten Tailor).
- **VP-ENCOUNTER-ENDED:** `EncounterEnded`, before the pass (Knowledge Seeker).
- **VP-ALL-ICE-STRENGTH:** `CardBehaviorDefinition.AllIceStrength`, static while the card is active and installed, and
  `ModifyAllIceStrengthForRun` (`RunState.AllIceStrengthModifiers`, ezaM). Both are terms of every ICE's strength
  quote, so the engine and the bots' encounter planner read the same value.
- **VP-ICE-GAINS-SUBROUTINE:** `EncounteredIceGainsSubroutine` gives the encountered ICE the source's
  `GainedIceSubroutine`, a typed subroutine, listed before its others. Its encounter ordinal follows the printed ones.
  `RunState.GainedSubroutines` keeps it for the run, so the resolved-subroutine ledger stays readable, and
  `RunState.EncounterSubroutineSpec` resolves an ordinal, printed or gained, for the engine and the validator. The damage
  follow-up `RunnerDrawsOne` is Stick and Poke's subroutine.
- **VP-CANNOT-STEAL-OR-TRASH:** `RestrictRun`, kept in `RunState.BehaviorRestrictions` with its source, its ability's
  key and, with a target, the named definition (Vertigo, Perfect Recall). `RunState.ForbidsStealOrTrash` decides it for
  the engine, the validator and the run's public cue, with the ICE subroutines' restriction.
- **VP-UNLESS-JACK-OUT:** `JackOut` ends the run as jacked out (CR 26.03 6.1.5). Shred's run-end prevention doesn't stop it
  (Lionsmane, as the Runner's choice option).
- **VP-NO-SUCCESSFUL-RUN:** `CardBehaviorDefinition.RunsCannotSucceed`. At the success step the run ends, neither
  successful nor unsuccessful (`RunEndReason.NotDeclaredSuccessful`, Flagship).
- **VP-ACCESS-LIMIT:** declared (`RunAccessLimitBesidesSelf`), and allowed only beside the no-success clause. It can't
  apply: every breach follows a successful run, which that clause forbids while the card is rezzed, and the Corp can't
  rez between the success step and the breach. Persistence doesn't reach it either, since the engine arms persistent
  abilities only for a card trashed while accessed (CR 26.03 9.12.5).
- **VP-ACCESS-PAID-ABILITY:** `BehaviorCost.AccessedCardPrintedCost`, a credit cost of the accessed card's printed rez
  or play cost; a card with neither can't pay it (Lampades).
- **VP-ICE-VIRUS-COUNTERS:** existing vocabulary: virus counters may sit on Corp ICE, the purge instruction clears them,
  and `Derez` takes the source (Knowledge Seeker's probe).

**For VP.5's cards**, also here:
- `RunnerTags` as an amount for counts and damage.
- `InstallServerKinds` (The Red Room, Flagship).
- The conditions `RunAgainstSourceServer` and `RunAgainstAnotherServer`.
- `RemoveAdvancementCounters`; an ability may require its source's advancement counters.
- `AddToHand` from the rig (Lethe).

**Binding VP.5 ahead** found seven further gaps, beyond the audit's primitives. They land in **VP.5a**, before VP.5's
cards, as VP.2c did for VP.3:
- `VP-ENDED-RUN-SERVER` (Kompromat);
- `VP-INSTALL-AND-REZ-LESS` (Reanimation Protocol);
- a run made when a run ends (Aircheck);
- an installed program returned when the run ends (Beta Build);
- a paid change of the approached server (Baker);
- resolving a subroutine outside an encounter (Unleash);
- an optional cost of the source's advancement counters (Sacrifice Zone Expansion).

**Client.** A gained subroutine's ordinal follows the printed ones; VP.8's client shows its text from the source card.

**Review (PR #154).** Codex's four findings, all fixed:
- a saved run's all-ICE strength changes, and its card-ability restrictions, are now authenticated against the declaration
  their reason key names;
- a run never declared successful ends with neither outcome, so "if the run was unsuccessful" triggers don't fire;
- a run ended mid-encounter by an ability (end the run, jacking out, a typed subroutine's end) ends the encounter too.

The second review's three: an all-ICE strength change may not repeat beyond the resolutions that made it; a gained
subroutine records the key of the trigger that gave it; moving the Runner mid-encounter ends the encounter first.

The third review's four:
- a saved restriction's source must be installed, or in Archives if it left play after resolving, and a once-per-turn
  trigger or ability must show its use this turn;
- an install that trashes the encountered ICE (CR 8.5.10) ends the encounter, as an occurrence;
- the bots' run model reads jacking out as ending the run;
- the Runner bot values a server whose runs can't succeed (a rezzed Flagship) at nothing.

The fourth review's two:
- an ability whose cost is the accessed card's printed cost (Lampades) offers its resolved credit cost on the action,
  authenticated with it, so the label and the bots read what will be paid;
- the Runner's view names a gained subroutine's source, and the encounter planner reads it from that source, before the
  printed subroutines, as the encounter resolves them.

The fifth review's two: a printed subroutine's feature bytes omit the gained source, so saved games' digests hold; and a
behavior ending the run mid-encounter completes the encounter and keeps its ICE as the cursor while an "encounter ends"
trigger resolves, so an optional one can ask (probe 90060).
