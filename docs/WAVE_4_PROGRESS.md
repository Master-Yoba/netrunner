# Wave 4 progress

Roadmap: [improvement roadmap](IMPROVEMENT_ROADMAP.md), Wave 4 (real decks;
polish, and the bot plans turns and searches runs).
Evidence date: 28 September 2026, on the Linux x86_64 development box.

## Branches

| Item | Branch |
|---|---|
| C1 Gateway sample decks | `feat/c1-gateway-decks` |

## C1: Gateway sample decks

### Data

The seven System Gateway-only decks from Null Signal Games'
[sample decklists](https://nullsignal.games/players/getting-started-sample-decklists/)
are in `data/decks/gateway-sample-decks.json`, as card codes and
quantities copied from each deck's NetrunnerDB API record (retrieved
28 September 2026), with the published deck names and our own one-line
summaries. The file is separate from `data/m4/presets.json` on purpose: the
frozen M5 manifests pin that file's SHA-256, so changing it would invalidate
every frozen baseline.

`OfficialPresetCatalog` loads both. The teaching decks keep their existing
validation (`Teaching`), and the sample decks (`Gateway`, kind `gateway`)
are checked for structure without metadata, and against the authenticated
metadata for identity, sides, product quantities, minimum deck size,
influence, out-of-faction agendas and agenda points. `DeckValidator` accepts
all seven.

| Side | Deck | Identity | Influence |
|---|---|---|---:|
| Runner | Party Hard | René "Loup" Arcemont: Party Animal | 14 / 15 |
| Runner | Stolen Goods | Zahya Sadeghi: Versatile Smuggler | 14 / 15 |
| Runner | Planning Ahead | Tāo Salonga: Telepresence Magician | 15 / 15 |
| Corp | Discretion Advised | Haas-Bioroid: Precision Design | 15 / 15 |
| Corp | Advanced Yomi | Jinteki: Restoring Humanity | 15 / 15 |
| Corp | Hyper Velocity | NBN: Reality Plus | 15 / 15 |
| Corp | Quick and Dirty | Weyland Consortium: Built to Last | 15 / 15 |

Every Corp deck has 44 cards and 18 agenda points.

### Content IDs

A pairing of named presets is the content ID `corpPresetId~runnerPresetId`.
`RulesFuzzRunner.GatewayContentIds` lists the twelve Gateway pairings, and
`fuzz` and `ai-ladder` take `--content gateway`.

### Rules hardening

The first fuzz over the twelve pairings (360 games) failed 20 times, and a
second round (1,200 games, after the first fixes) 4 times. Six defects,
none reachable with the tutorial decks:

| Defect | Cards | Fix |
|---|---|---|
| A Corp operation's effect raising a trigger that asks for a choice broke the card-play frame rule | Public Trail's tag firing NBN: Reality Plus | The validator lets an executing card-play behavior suspend beneath the effect pipeline its own instruction opened, controlled by the same player; memory-cleanup suspension keeps its stricter check |
| An access trash with a waiting trigger yielded the next breach prompt first, stranding the trigger | René "Loup" Arcemont trashing a card with more to access | As with a steal: the next breach decision waits until the trigger resolves |
| A trigger's own install replacing its source aborted the trigger | Pantograph installing a second Pantograph (a console) | An ability that has reached its own install may find its source in the trash, in the engine and the validator |
| The validator offered a Trojan as a behavior install with no ICE to host it | Pantograph with Tranquilizer in the grip | The validator's install check requires an installed ICE host, as the engine's does |
| An "up to" card selection kept offering more cards past its limit | Longevity Serum, Spin Doctor | Both exact and "up to" limits cap further picks |
| A breaker trashed mid-encounter left its strength boost in the run | Ansel 1.0 trashing a pumped Unity | Run strength adjustments on a card end when it leaves play, and the ICE subroutine cursor no longer writes back the stale run |

Each failing game is a regression test (`GatewayFuzzRegressionTests`).
After the fixes:

| Run | Games | Result |
|---|---:|---|
| Gateway, seed 5 (the seed that failed) | 1,200 | all terminal |
| Gateway, seed 6 | 2,400 | all terminal |
| Gateway, seed 8, differential (Simulation checked against every action) | 600 | all terminal |
