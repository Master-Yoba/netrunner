# M5.0 contract-agent baseline

Captured 22 September 2026 against the production code at `8f27bac`.
The checkout HEAD was `8a94c30` (the subsequent documentation-only plan commit).
The new capture utility was being added locally; the report records its exact
assembly hash as well as Core/Cards/AI/Match hashes. No production policy,
engine, card or UI code changed to obtain these results.

Inputs: [contract-baseline-v1](../../data/m5/benchmarks/contract-baseline-v1.json).
Evidence: [complete per-attempt JSON report](../evidence/m5-0/contract-baseline-v1.json).
Full private replays remain in ignored `artifacts/m5-0/capture-final/` and can
be regenerated with [the capture instructions](../../data/m5/benchmarks/README.md).

## Measured result

All **20 unique games reached a normal terminal outcome** within the declared
4,000-action limit. All 40 attempts passed replay verification. Each second
attempt matched the first attempt's complete replay SHA-256 and selection
counters. The final capture also matched the initial exploratory capture's
replay hashes after adding the embedded-preset checksum to the manifest.

There were no rejected-agent-action errors, caps, stalls or exceptions in this
sample. This is a small characterization suite, not the M5 1,000-game gate and
not a tactical-strength score.

| Corp preset | Runner preset | Games | Corp agenda wins | Corp flatlines | Runner agenda wins | Runner deck-out wins | Accepted actions |
|---|---|---:|---:|---:|---:|---:|---:|
| Basic | Basic | 5 | 1 | 1 | 0 | 3 | 3,362 |
| Basic | Expanded | 5 | 1 | 1 | 1 | 2 | 3,241 |
| Expanded | Basic | 5 | 1 | 0 | 4 | 0 | 4,160 |
| Expanded | Expanded | 5 | 0 | 1 | 4 | 0 | 3,845 |
| Total | | 20 | 3 | 3 | 9 | 5 | 14,608 |

Game lengths ranged from 77 to 2,027 accepted actions (median 683.5), including
paid-window passes and nested choices. Winner totals are Corp 6 / Runner 14;
they do not establish balance because both sides use the same limited policy
and teaching presets. The report's `turn` field is Core's turn number, not a
manually inferred count of full Corp/Runner turn pairs.

Notable selection counts, excluding duplicate runs:

| Selection | Corp | Runner |
|---|---:|---:|
| Basic draw action | 0 | 0 |
| Gain-credit action | 140 | 0 |
| Install finalized | 339 | 42 |
| Install finalized with replacement selected | 103 | 0 |
| Score-agenda action | 30 | 0 |
| Start-run action | 0 | 194 |
| Pump-breaker action | 0 | 38 |
| Break-subroutines action | 0 | 2 |
| Bioroid ability action | 0 | 79 |
| Access-trash action | 0 | 1 |

These are choices, not a count of all effects. Cards can draw or gain credits;
agenda steals can be resolved automatically by Core. The scarcity of ordinary
breaks is a lead to investigate, not proof that all 38 pumps were wasted. The
103 replacement selections are not all mistakes and are not all Nico Campaigns.
Timing in the report includes per-action serialization, replay verification and
capture overhead; it is not an isolated AI latency benchmark.

## Named findings and tactical follow-up

| ID | Evidence level | Finding | Follow-up |
|---|---|---|---|
| M5-B01 | Measured plus source-confirmed limitation | Neither side chose basic draw. The fixed turn priority puts gain-credit ahead of draw; when both are offered it always favors credits. There is no hand-need evaluator. | `corp-opening-05`, `runner-opening-04`; AI-502/503 economy and draw value |
| M5-B02 | Existing reproduced playtest, retained as a regression | The basic `47042` opening installs/rezzes and replaces two Nico Campaigns. The M4 history proof documents it; current install utility has no remaining-payout loss term. | `corp-replacement-01/02`; explicit active-versus-exhausted asset cases |
| M5-B03 | Source-confirmed limitation | `IsBoundedAbility` allows click/lose-click or unused once-per-turn abilities, excluding useful unlimited counter-paid encounter abilities such as Leech. Existing contract tests intentionally preserve that limitation. | `runner-payments-04`; support resource-bounded useful activations in the new agent |
| M5-B04 | Source-confirmed preference gap | Ordinary pump/break/access payment semantic keys sort hosted spend ascending. This can preserve expiring run credits while spending the wallet. ICE-subroutine payment branches already use a separate wallet-cost ordering. | `runner-payments-01/02`; compare only equivalent legal payments and retain source restrictions |
| M5-B05 | Source-confirmed limitation | Setup always keeps. There is no comparison of functional and economy-poor opening hands. | Both `opening-01/02` pairs; pinned keep/mulligan positions |
| M5-B06 | Measured lead, tactical failure not yet demonstrated | 38 pumps, two ordinary breaks and 79 bioroid activations across 20 games. Fixed action priorities do not reserve the future cost of breaks or compare complete encounter plans. | `runner-encounter-02/03/04`; inspect bounded pump/break sequences and opportunity cost |
| M5-B07 | Source-confirmed limitation | Optional triggers/replacements are generally used whenever available; fixed preferences do not reason about a harmful optional draw or ICE swap. | `corp-choices-03`, `runner-choices-05`; explicit legal decline positions |

Source evidence:

- [Contract selection and payment keys](../../src/Netrunner.AI/FullGameContractAgent.cs):
  `DefaultTurnActionOrder`, `SelectTurnAction`, `IsBoundedAbility`, `SemanticKey`,
  `SelectTriggerChoice`, `SelectInterruptChoice`, and Setup dispatch.
- [Leech's typed counter-paid ability](../../src/Netrunner.Cards/GatewayBatch3BehaviorSpecs.cs).
- [Nico history proof](../../tests/Netrunner.Match.Tests/MatchHistoryTests.cs):
  `OfficialNicoOpeningExplainsTwoReplacementsWithoutNamingHiddenInstall`.
- [Contract-agent tests](../../tests/Netrunner.AI.Tests/FullGameContractAgentTests.cs):
  `UnlimitedNonclickAbilityIsIgnoredInFavorOfSupportedFallback`,
  `TurnActionDispatchUsesTypedGainAndDrawPriorityAndNeverEndsTurn`, and
  `TriggerChoicePrefersUseAndIsNeutralToActionOrderAndJson`.

M5.0 does not change these policies or re-label contractual tests as tactical
successes. The new heuristic will be measured separately against the unchanged
baseline. The [80-position specification](../../data/m5/benchmarks/tactical-v1.json)
also covers known wins/losses, information twins, compound choices, conditional
cost expiry and absent decision families. Executable builders and exact bounded
expectations remain M5.2 work.

## Reproduction and limits

`scripts/capture-m5-baseline.sh` compiles the developer-only utility with the
authenticated local content. It rejects mismatched catalog/preset pins and an
existing destination, validates all deck pairs before running, and writes a
report after each attempt so an incomplete sample is explicitly marked.

Both policies are stateless v6; they have no policy seeds or checkpoints.
The manifest records that fact rather than pretending the game's seed initializes
a policy RNG. Each repeated run creates fresh agents. The metering wrapper sees
only their ordinary observations and cannot influence selection.

The utility preserves a replay/snapshot on the current runner's diagnosed cap
and records other exceptions. The existing runner does not export a recoverable
prefix for every arbitrary exception/timeout; robust per-action failure bundles,
turn caps, cycle detection and parallel scheduling belong to M5.2. No hard
watchdog or 1,000-game stability claim is made here.

The manifest/report/specification checker is content-independent and part of
`make verify`/`make m4-gate`. Actual baseline capture requires official content.
Committed evidence contains only counts, results and hashes, never card faces,
card source data, full observations or authoritative snapshots.
