# Wave 2 progress

Roadmap: [improvement roadmap](IMPROVEMENT_ROADMAP.md), Wave 2 (the board comes
alive for one run and one score).

## Branches

Stacked in order on `dev`:

| Item | Branch |
|---|---|
| E4b event completeness | `feat/e4b-event-completeness` |

## E4b: event completeness

**Decision: presentation events are derived in `Netrunner.Match`, not added
to Core.** `GameState.EventSequence` is part of the state hash, so a new Core
event would change every state hash and break replay of existing saves,
session traces and fuzz corpora. Core already carries enough to derive the
missing facts, so `PresentationEventProjector` builds them on the match
worker from the raw events and the authoritative states before and after
each accepted action. `MatchSnapshot.PresentationEvents` publishes them.

**Card references.** `CardKeyTracker` gives each card the viewer can see or
follow a presentation key: a visible card's ID, or an opaque `tracked:N`
handle for a face-down installed card. A handle stays stable while its card
stays installed and hidden, and is retired when the card leaves play or is
revealed, so it never links two separate hidden periods. A reveal publishes
both keys (`PreviousCardKey` → `CardKey`) so the board can flip the card in
place.

**Explicit rules.** `PresentationEventProjector.Rules` classifies every Core
payload type as Projected, ViewOnly (shown by another event or the settled
view) or Bookkeeping. A test fails when Core adds a payload type without a
rule. Raw event filtering for publication is unchanged
(`RunnerEventVisibility` and `CorpEventVisibility` stay fail-closed).

Audit of the Wave 2 slice (advance, score, run start, encounter, subroutine
fires, run end) plus the everyday board changes:

| Board change | Core source | Before E4b | Presentation event |
|---|---|---|---|
| Turn starts | `TurnStarted` | Present | `TurnStarted` (side, turn, clicks) |
| Click spent | `ClickSpent` | Present | `ClickSpent` (clicks left) |
| Credits gained or lost | `CreditsAdjusted` | Present; no source | `CreditsChanged` (delta, total); source not yet attributed |
| Draw | `CardDrawn` | Present | `CardDrawn` (key only when visible) |
| Install | `CardInstalled` | Present, but a Corp install hid the slot from the Runner | `CardInstalled` with a tracked key and server |
| Rez | `CardRezzed`, `IceRezzed` | Present | `CardRezzed`, with the tracked key it had before |
| Advance a face-down card | `AgendaAdvanced`, `BehaviorCardAdvanced` | **Missing for the Runner**: both were suppressed because the card is hidden | `CardAdvanced` with a tracked key, counters placed and total |
| Score, steal | `AgendaScored`, `AgendaStolen` | Present | `AgendaScored`, `AgendaStolen` (points; previous key when it was tracked) |
| Run starts | `RunInitiated` | Present | `RunStarted` (server) |
| Approach and encounter ICE | `IceApproached`, `EncounterInitialized` | Present; hidden ICE suppressed for the Runner | `IceApproached`, `EncounterStarted` with the ICE's tracked key |
| Subroutines broken | `SubroutinesBroken` | Present; no ICE reference | `SubroutinesBroken` with the encountered ICE |
| Subroutine fires | none | **Missing** | `SubroutineFired` (ICE, printed position), derived from the run's resolved-subroutine ledger; when a subroutine ends the run, the unresolved unbroken subroutines up to the first end-the-run one |
| Encounter ends | `EncounterCompleted` | Present | `EncounterEnded` |
| Server approached, run successful | `ServerApproached`, `RunSuccessful` | Present | same names |
| Access | `CardAccessed` | Present | `CardAccessed` |
| Run ends | `RunEnded` | Present | `RunEnded` (reason, success) |

Remaining gaps, outside the slice: credit changes don't say what caused them,
and damage doesn't list the trashed cards per card. Both can be derived the
same way when U3 extends past the slice.

Verification: random-legal games on developer and official content project
every slice kind; no event names a card the Runner can't see; tracked keys
are stable while a card stays hidden; and a face-down advance reaches the
Runner as a tracked card (official).
