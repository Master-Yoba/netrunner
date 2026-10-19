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

The sixth review's one: a move ending the encounter (Proprionegation) expires the encounter's effects before its
reactions resolve, as the other ways an encounter ends do.

The seventh review's one: the encounter planner checks survival hit by hit, in order, so a gained subroutine's draw
refills the grip before the next hit. Without draws this is the old total check, so no existing game moves.

The eighth review's one: the bots' whole-run forecast reads changes to every ICE's strength, from active cards and, through
the observation (`allIceStrength`, absent while none), from the run itself.

### VP.5a: VP.5's binding gaps (15 October 2026)

On probe cards 90049 to 90059 (`VantagePointRunGapTests`):
- **VP-ENDED-RUN-SERVER:** with no live run, the attacked-server selector reads the server of the run-ended occurrence
  resolving (Kompromat's derez choice). The engine and the validator share the lookup.
- **VP-INSTALL-AND-REZ-LESS:** a rez paying less takes `DiscountLeftAfterIceInstallTax`, the shared discount left after
  the install tax the ICE paid. It may record whether it rezzed a card matching its filter, and bad publicity may take that
  recorded amount (Reanimation Protocol). A card filter may name a subtype the card must lack (`ExcludedSubtype`:
  "non-liability", Beta Build's "non-virus").
- **VP-RETURN-AT-RUN-END:** `RememberForRun` keeps a card in the run's persistent context, which travels into the
  run-ended occurrence. A `RememberedCard` target names it while it is still installed. A run event's own abilities are
  active as its run begins (Beta Build: search, install, and the program back on top of the stack when the run ends).
- **VP-RUN-AFTER-RUN:** `RunAfterRun` marks the turn frame. Once the ended run's reactions finish, the Runner runs the
  chosen card's remote server as part of the same action. A remote exists only while it holds a card, so the
  remote-server selector picks a server by one of its cards (Aircheck).
- **VP-PAID-REDIRECT:** the occurrence `ServerWouldBeApproached`, raised once at the approach step (6.9.4g). It fires
  before the approach, and `ChangeAttackedServer` makes the Runner approach a central server instead. The trigger condition
  `RunStartedBySource` limits it to the source's own run (Baker).
- **VP-RESOLVE-SUBROUTINE:** `ResolveIceSubroutine` resolves a subroutine of a piece of ICE outside an encounter. A
  behavior subroutine runs as the resolving card's child frame. Credit, tag and plain damage subroutines resolve
  directly, and end-the-run subroutines do nothing with no run. A mode offering one is available only while it can
  resolve; subroutines that need an encounter's choice frame aren't offered (Unleash, a ruling for VP.5's binding to
  revisit against the decks' ICE). Elevation's Mycoweb resolves another ICE's subroutine differently: each pool ICE's
  subroutines become inline instructions in the source's own frame. That covers pay-or-tag, trash and install
  subroutines, but the source, not the ICE, is then "this ICE". VP.5 picks per card.
- **VP-ADVANCEMENT-COST:** existing vocabulary plus the trigger condition `RunOnAnotherServer`: an optional trigger
  requiring its source's advancement counters removes one (Sacrifice Zone Expansion).

**Review (PR #155).** The first round's two:
- moving the run (Proprionegation) or redirecting it now moves its persistent context with it, so a program Beta Build
  remembered survives the move;
- a remembered card that leaves play is forgotten: a copy installed again is a new object, and the
  validator rejects a live run that remembers an uninstalled card. Once the run has ended, its snapshot is fixed. Only
  a run-end reaction could uninstall and then reinstall the card before the return resolves, and no card in the pool does.

The second round's one: a behavior subroutine resolved outside a run can still end the run (Grubber's and Semak-Samun's
options, or N-Pot's threat), and that now does nothing, as a printed end-the-run subroutine already did. A behavior
subroutine that changes the run or its encounter (ezaM's strength) isn't offered with no run.

The third round's one: the turn frame's clone kept no follow-up run, so Aircheck's run was lost whenever another run-end
reaction asked for a decision after it (Zahya's optional gain). The clone and the one rebuild that keeps the frame's
stage now carry it. The other VP.5a fields survive every copy: the run's state and persistent context pass them on, and
a card filter's excluded subtype is part of its equality.

### VP.5b: the gaps writing VP.5's specs found (19 October 2026)

Writing the specs found six primitives that VP.5a's pass had judged expressible. They are proven on probe cards 90061
to 90067 (`VantagePointBindingGapTests`):
- **VP-TRAP-ICE:** `IceType.Trap`, for ICE with none of the three breaker types (Vicsek: "Trap - AP - Observer"). Only a
  breaker that interfaces with every piece of ICE (Mayfly) breaks it, or a typed one while the ICE gains every type
  (Chromatophores). No icebreaker interfaces with traps alone.
- **VP-BIOROID-MULTI-BREAK:** an encountered-ICE break ability may cost two lost clicks and break up to two subroutines
  (Ansel 2.0). Each use names its subroutine, or two in printed order (`SecondSubroutineId`, omitted while unused). The
  engine and the validator share the offers, which are each unbroken subroutine and each pair. The bots' encounter planner
  counts a pair as one use.
- **VP-OPPONENT-ABILITY:** `BehaviorPaidAbility.UsedByOpponent`: only the card's opponent uses the ability, and they pay
  for and resolve it (Rotary's "Only the Corp can use this ability"). The engine and the validator share one rule for
  the side that uses an ability.
- **VP-SWAP-WITH:** `SwapInstalledIce` may name its first piece of ICE in its argument: `self`, the source (ezaM), or a
  binding (`trigger-subject`, the ICE Sipa's Runner just passed). Its target is the chosen ICE. With none chosen,
  nothing is swapped.
- **VP-TAG-COST:** a card's play cost of tags (`BehaviorCostKind.Tags`) removes the Runner's tags, whoever plays the card,
  and the card can't be played while the Runner has too few (Unleash). Before, plays took only credit, click and trash
  costs.
- **VP-DECK-BOTTOM:** a card a move takes from a deck into that same deck goes to its bottom (ezaM's look at R&D's top).
  Before, a move within one zone did nothing.

## VP.5 — Batch 2 (19 October 2026)

28 cards on `feat/vp5-batch2`, in one PR with VP.5b's primitives:
- **Runner:** Chain Reaction, The Tungsten Tailor, Lampades, Stick and Poke, Kompromat, Rotary, Baker, Aircheck, Beta
  Build and Sipa.
- **Corp:** Ansel 2.0, Reverb, Sleipnir, Vertigo, Perfect Recall, ezaM, Lionsmane, Vicsek, Unleash, The Red Room,
  Lethe, Paywall, Sacrifice Zone Expansion, Event Horizon, Flywheel, Reanimation Protocol, Flagship and Shackleton Grid.

- **Specs:** `VantagePointBatch2CorpSpecs` and `VantagePointBatch2RunnerSpecs`, composed by `VantagePointBehaviorCatalog`
  (version `startup-vp.5`). Elevation's catalog is unchanged.
- **Tests:** 49 card tests (`VantagePointBatch2CorpTests`, `VantagePointBatch2RunnerTests`), at least one per card.
- **Registry and coverage:** VP.5 complete. The report shows Vantage Point 56 of 66 registered, tested and complete.

### What binding found

Beyond VP.5b's six primitives, the batch needed three Core changes:
- **VP-ICE-TRASHES-ITSELF (Vicsek):** found by the fuzz. A subroutine that trashes the encountered ICE ends its
  encounter, and the Runner moves on from its position, as when an install trashes it. The run's cursor may then sit
  just outside the remaining ICE.
- **Sipa:** swapping the ICE just passed leaves the cursor empty while its completed encounter stays on record.
- **Unleash:** the validator's card-play frame accepts VP.5b's tag cost.

Reanimation Protocol with no ICE in Archives rezzes nothing (also a fuzz find). Lethe's bounce and Event Horizon's
program trash choose as many as able: a zone-count condition can't read the rig.

### Simulation decks

- `data/decks/vantage-point-simulation-decks.json` now has eight decks, all legal under Startup 2026 with Vantage Point
  counted legal (`CardFormatTests`). Between them they hold every VP.5 card.
- **Four of VP.3's decks** take VP.5's cards in like-for-like swaps within the deck's faction:
  - Editorial Division: Lethe and Paywall;
  - Weyland: Sacrifice Zone Expansion, Event Horizon, Flywheel, Reanimation Protocol, Flagship and Shackleton Grid;
  - Vic: Kompromat and Rotary;
  - Tāo: Aircheck, Beta Build, Sipa and Baker (with the deck's stealth credits).
- **Three new decks** come from the Core Battle Box's lists in the same way:
  - LEO Construction: Ansel 2.0, Reverb, Sleipnir, Vertigo and Perfect Recall;
  - PT Untaian: ezaM, Lionsmane, Vicsek, Unleash and The Red Room;
  - Topan: Chain Reaction, The Tungsten Tailor, Lampades and Stick and Poke.
- **The first vantage-point fuzz** rejected 46 of 360 games, on Vicsek's self-trash and Reanimation Protocol (both
  above). After the fixes, all 360 games ended in a terminal state and 45 sampled replays verified (mean 456.4 actions).

### Ladder and the misplay sweep

- **Ladder:** `scripts/ai-ladder.sh --content vantage-point --entrants random,contract,easy,standard --seeds-per-pairing 3`
  over the eight decks played 720 games, with 0 errors and 3 capped. Both sides order random < contract < easy <
  standard.
- **The sweep:** `ai-offers` over the saved games found the standard bots never took these plays:
  - **Kompromat and Beta Build** were never played, and **Aircheck** was played 3 times in 789 offers. A run event's
    value now counts its run-end bad publicity, its run after the run, and its program installed from the stack.
  - **Baker** was never installed, nor its run used. A redirect to HQ or R&D is now worth what that server holds over the
    one attacked, while stealth credits are to hand or a rig card places them as the run begins. Baker moved to the Tāo
    deck, whose stealth cards can pay for the redirect.
  - **Flagship** was never rezzed: it read as a scoring upgrade. A no-success upgrade, or one reacting to credits from
    outside the pool, is now run-relevant.
  - The Corp values Rotary's trash ability as trashing a Runner card. It is still rarely worth a click in these games.
- **After the fixes:** every card above is played, installed or rezzed (Baker's run 33 times). The ladder's order holds,
  with 0 errors.
- **Scope:** these paths read vocabulary only Vantage Point cards use, so the policy versions are unchanged.
- **Fixtures:** `VantagePointBotTests` checks each fix at a saved decision of this ladder
  (`tests/Netrunner.Match.Tests/Fixtures/saved-games/vp5-*`). VP.3's fixtures were recorded on the VP.3 catalog, which the
  Startup pool no longer plays, so its cases move to decisions of this ladder too.

### E6

`scripts/core-growth.sh ... --range slice-VP.5=561bb46..<head>` counts VP.5b's primitives and the cards together: 13 Core
files, two new terms of vocabulary, 27 of 28 cards with no Core change. By hand, beyond VP.5b, three cards needed a Core
change (Vicsek, Sipa and Unleash): **25 of 28 (89%)**.

**Review (PR #156).** The first round's two findings:
- **Valid:** Ansel 2.0's pairs now follow printed order, so a subroutine Stick and Poke adds (listed first, numbered last)
  pairs with each printed one.
- **Not a change:** Unleash's resolve stays optional. The card says "You may resolve 1 subroutine on that ice".

The second round's two, both Beta Build's: it now shuffles the stack after its search, and with no program to install
it remembers nothing and the run goes on.

### Rulings and deviations

| Card | Note |
|---|---|
| Unleash | One mode per subroutine position, offered while that subroutine can resolve outside an encounter. A subroutine that needs an encounter's choice frame, or changes the run, isn't offered. |
| Sipa | "Another installed piece of ICE": choosing the passed ICE itself swaps nothing, the same as declining. |
| Mycoweb (Elevation) | The Startup catalog keeps Elevation's catalog unchanged, so Mycoweb offers only the Elevation pool's subroutines, none of VP.5's sentries or code gates. VP.8 rebuilds it over the Startup pool. |


## VP.6 — Primitives C: zones, information and identities (19 October 2026)

VP.6's primitives land with VP.7's cards in one PR, since each has only one or two cards. Each has engine tests through
its card, and an observation test where it hides something:
- **VP-ABILITY-FROM-HQ:** a paid ability may be usable from HQ (`usableFromHeadquarters`): Tocsin's Expendable, whose
  costs reveal and trash the card.
- **VP-ARCHIVES-TURNED-FACEUP:** an occurrence, deferred only when an Archives breach turns two or more face-down cards
  face up at once (Nurse Hạnh).
- **VP-CARD-TRASHED:** an occurrence for any card trashed, with the card as its subject (Hiram's hardware). It is queued
  only while a catalog trigger listens for it, so other games' replays are unchanged.
- **VP-HOST-RESOURCES:** a resource may host resources of named subtypes (`hostsResources`), installed onto it at a
  discount, with a hand-size bonus while it hosts one of each (Hackerspace). The engine and the validator share the
  host rule. The discount is in the install's plan, its frame and its payment.
- **VP-HOST-FACEDOWN:** a program may host cards from the grip face down, up to a limit (`hostsFaceDownLimit`, Read-Write
  Share). Its public view shows only how many, under a separate key (`runnerHostedFaceDown`). The Runner's own view names
  them.
- **VP-SECRET-IDENTITY:** the rules state carries the Corp's secret choice and whether the identity is flipped
  (`secretIdentity`, omitted when unused). The choice is in the Corp's private view only. The public view shows the side
  only while flipped. New vocabulary:
  - instructions: `SetSecretIdentity`, `FlipIdentity`;
  - conditions: `IdentityFlipped`, `IdentityNotFlipped`, `SecretIdentityMatchesAttackedServer`.
- **VP-ARRANGE** needed no new vocabulary: arranging the top N cards is N rounds of choosing one of them and putting it on
  top (the last chosen ends on top), so every order can be made.

Binding VP.7 found three more, beside VP-CARD-TRASHED above:
- **VP-SOURCE-SERVER-ROOT:** a selector for cards in the root of the source's server (Hype Machine). The engine, the
  validator's candidates and the advance re-check share it.
- **VP-MOVE-UPGRADE:** `MoveSourceToServerOf` may name a bound card to move instead of its source (Lotus Haze's rezzed
  upgrade).
- **VP-LOOK-OPPONENT-DECK:** a choice among the opponent's deck cards shows them to the chooser only (Hiram).

## VP.7 — Batch 3 (19 October 2026)

The last 10 cards, on `feat/vp7-batch3`:
- **Runner:** Hackerspace, Nurse Hạnh, Hiram "0mission" Svensson and Read-Write Share.
- **Corp:** Méliès U: Only the Brightest (front and back), Lotus Haze, Knowledge Seeker, Cultivate, Hype Machine and Tocsin.

- **Specs:** `VantagePointBatch3Specs`, composed by `VantagePointBehaviorCatalog` (version `startup-vp.7`).
- **Tests:** `VantagePointBatch3Tests`, at least one per card.
- **Registry and coverage:** VP.7 complete. The report shows every Vantage Point card registered, tested and complete
  (225 of 225 across the three sets).
- **Méliès U** is implemented in full (D-VP-8):
  - the Corp sets the copy secretly as its discard phase ends;
  - a successful run on a central server flips it, and the copy naming that server resolves;
  - the front gains a credit as the Runner's action phase ends;
  - the identity flips back as the Runner's discard phase ends.

### Simulation decks

`data/decks/vantage-point-simulation-decks.json` now has ten decks, all legal (`CardFormatTests`):
- like-for-like swaps:
  - Editorial Division: Hype Machine;
  - Weyland: Tocsin;
  - Topan: Hackerspace and Nurse Hạnh;
  - Tāo: Read-Write Share;
- two new decks:
  - Méliès U, from the PT Untaian list, with Knowledge Seeker, Cultivate and Lotus Haze;
  - Hiram, the Tāo list under Hiram's identity.

### The fuzz, the ladder and the sweep

- **Fuzz:** 576 games, all terminal, and 72 sampled replays verified.
- **Ladder:** the first ladder found one rejected game. Scapegoat (VP.3) shuffled Methuselah, +1 memory, out of a full
  rig, and the Runner's memory cleanup never started. The chosen card was set aside while the Corp's choice resolved, so
  the transfer didn't see it leave the rig. It now checks the card's original zone
  (`ShufflingAwayAMemoryCardMakesTheRunnerTrashAProgram`).
- **The sweep:** the standard Corp never played Cultivate (0 in 863 offers). Its trash among R&D's top cards read as
  trashing an installed Corp card, and its card to HQ had no value. An operation's choice among R&D's top cards is now
  the Corp's pick of them. Triggers (Au Co) keep their values. After the fix, the Corp played Cultivate 36 times in 79
  offers, in 31 of 32 games.
- **The second sweep:** the standard Runner never installed Nurse Hạnh (0 in 83 offers) or Hackerspace (0 in 72) with a
  click, only when its identity's install offered them free. Both were valued as a card with no income. Nurse Hạnh is now
  worth its draws, and Hackerspace its discount on the hostable resources in the grip, with its hand size. After the fix,
  the Runner installed Nurse Hạnh 25 times in 36 offers. Hackerspace stays uninstalled with a click (0 in 38): the Topan
  deck holds one card it can host of each kind, Nurse Hạnh and Stick and Poke, so it rarely has anything to discount. The
  identity's free install takes it every time (12 of 12).
- **The ladder after both fixes:** 1,152 games, 0 errors, 12 capped. Both sides order random < contract < easy <
  standard; random and contract don't separate, as before.
- Hype Machine's rez and ability, Tocsin's Expendable and Read-Write Share's trash are rarely used, which suits their
  costs.
- **Scope:** these paths read vocabulary only Vantage Point cards use, so the policy versions are unchanged.
- **Fixtures:** `VantagePointBotTests` checks VP.3's, VP.5's and VP.7's fixes at saved decisions of this ladder
  (`Fixtures/saved-games/vp7-*`). VP.5's Corsair case is dropped. This ladder offered Corsair's ability twice, both times
  against Palisade with no fracter installed, where declining is right.

### E6

`scripts/core-growth.sh ... --range slice-VP.7=b2474c0..4063bab` counts VP.6's primitives and the cards together:
- 20 Core files and 10 new terms of vocabulary;
- 5 of 10 cards with no Core change.

By hand, beyond VP.6's planned primitives, three cards needed a Core change: Hiram, Hype Machine and Lotus Haze. That
makes **7 of 10 (70%)**. As the Startup pool's last batch, it carries most of the set's one-of-a-kind effects: an
identity, a host and a hidden zone.

**Review (PR #157).** The first round's three findings, all valid:
- **The Runner's samples** (`ObservationSampler`) copied the rules state, so the Runner's planner simulated against the
  copy of Méliès U the Corp had set. A Runner's sample now draws an unflipped side again among the three. Nothing is
  drawn without a secret identity, so other games' samples are unchanged.
- **Tocsin's Expendable** was also offered by an installed Tocsin. An ability usable from HQ is now usable only there.
- **Lotus Haze** couldn't move an upgrade to a central server with nothing installed, since it chose the server by a card
  in it. `MoveSourceToServerOf` may now name a central server by its zone. Lotus Haze chooses HQ, R&D, Archives, or a
  remote by one of its cards.

The second round's one finding, valid: Lotus Haze could put a region in a root that already held one, which CR 3.6.5e
forbids (Shackleton Grid to Mahkota Langit Grid's server). That move now leaves the region where it is.

The third round's one finding, valid: an effect's install onto Hackerspace (Baz Wong's, as the Corp rezzes ICE) was offered
at full price but checked with the discount, so it was rejected. The effect's install plan now passes on its host.

The fourth round's finding is deferred to VP.8's tracker work (A9 over the new hidden zones). The Runner's samples and card
tracker don't keep the R&D card Hiram's look shows, so the bot forgets it once the look ends. The engine and the
observation are right; only the bot's knowledge is lost.

## VP.8 — Integration: decks, formats, bots, client and the milestone gate (19 October 2026)

### Decks (D-VP-6)

No Startup battle box has been published, so Startup 2026's decks are real players' tournament lists. The user approved
the set on 19 October 2026.
- **Source:** every Startup event on Always Be Running since Vantage Point joined Startup (17 April 2026): 31 events,
  137 linked NetrunnerDB lists. 132 use only the Startup pool and are legal under Balance Update 26.09; one breaks the
  limit on agendas worth 3 or more points.
- **Selection:** each faction's two best lists, by event size over placement, one per identity. That makes eight Corp
  and six Runner decks, with fourteen identities. Half come from the 2026 Worlds Startup side event (29 players): its
  first, second, third and fourth places.
- `data/decks/startup-2026-decks.json` records each list's NetrunnerDB link, author and placement. The names,
  summaries and archetypes are ours. Between them the lists hold 37 of the 66 Vantage Point cards.
- `CardFormatTests.Startup2026DecksAreTournamentListsLegalAndFullyBound` checks that each is legal and fully bound.

### Formats and the menu (D-VP-5, D-VP-7)

- **Startup 2026** is complete: the Startup pool, all three packs legal, and Balance Update 26.09's bans and agenda limit.
  The format snapshot names the `startup` pool, which the schema now lists.
- **`PlayFormats`:**
  - offers it with its fourteen decks;
  - maps its presets and the Startup behavior catalog to the format;
  - records it in saves, which reload (`PlayFormatsTests`).
- **The menu** offers "Startup 2026" beside the other formats, with random opponents drawn from its decks. The menu
  smoke checks its eight Corp and six Runner decks.
- **Ladders and fuzz:** `startup-2026` is a `--content` pool (48 pairings, pool version `startup-2026-v1`), and
  `affected-pools.sh` and `card-fingerprints` list it.

### Mycoweb over the Startup pool

VP.5's deviation is closed. The Startup catalog (`startup-vp.8`) rebuilds Mycoweb over its own ICE, so Mycoweb resolves
Vantage Point's sentries and code gates too. Two kinds of subroutine are never offered:
- Mycoweb's own;
- one that acts on its own ICE, such as Knowledge Seeker's virus counter, which would land on Mycoweb.

The Elevation pool's Mycoweb is unchanged, and so is every other Elevation card's fingerprint. `BehaviorCatalogBuilder.Replace`
swaps the one definition.
