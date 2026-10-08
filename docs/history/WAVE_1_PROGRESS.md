# Wave 1 progress

Roadmap: [improvement roadmap](IMPROVEMENT_ROADMAP.md), Wave 1 (first visible
wins; the engine gets fast; distinct difficulty).
Evidence date: 26 September 2026, on the Linux x86_64 development box (8
cores). Strength numbers come from `ai-ladder` on official content.

## Branches

Stacked in order on `dev`:

| Item | Branch |
|---|---|
| E6 domain names | `feat/e6-domain-names` |
| A2 heuristic fixes, A6a distinct Easy | `feat/a2-heuristic-fixes` |
| E1 Simulation mode | `feat/e1-simulation-apply` |
| E3 nightly fuzzing and repro | `feat/e3-nightly-and-repro` |
| U1 client quick wins | `feat/u1-quick-wins` |

## E6: domain names

The slice-numbered presentation files, types and tests in `Netrunner.Match`
are renamed after what they present: `Slice45*` became `EncounterChoice*` (plus
`ChoiceSurface`, `RunCueViewModel`, `BehaviorCostViewModel`,
`BehaviorChoiceOptionViewModel`), `Slice46*` became `UnlessChoice*` and
`DamageTagObservationProjector`, `Slice47*` became `IdentityChoice*` and
`IdentityObservationProjector`, and the `Slice4x` test classes got domain
names. Serialized names are unchanged: Core's `slice45Cues` observation key
and `AgentFeatures.Slice45Cues` feed observation digests, and the M2 scenario
format keeps its name.

## A2 and A6a: stronger Runner, distinct Easy

**Finding:** hidden installed cards were `null` in the observation, so no
Runner bot could see public advancement counters. Core observations now add a
`hiddenAdvancements` entry (server, zone, index, counters; never a card ID),
present only when a hidden card has counters. Card memory for hidden cards
was not added: they have no ID to attach a memory to.

**Versioning:** policies are versioned end to end (`OfflinePolicies`). Frozen
manifests and older saves keep Runner v1 and Corp v2 exactly; new games use
Runner v2 and Corp v3. Agent features v3 read the new entry; v2 omits it, so
v2 digests are unchanged. `make m5-quick` produced identical smoke and tactical
digests to the Wave 0 run.

- **Runner v2:** face-down roots gain 200 per public advancement counter; HQ and
  R&D scale with estimated agenda density; contested servers rise when the
  Corp reaches 5 points.
- **Corp v3:** prefers ICE the Runner can't break yet; weighs agenda protection
  by breaker coverage; avoids unprotected agenda installs it can't score this
  turn; central ICE matters more near the Runner's 7 points.
- **Easy (A6a):** a 150-point near-tie band (was 15) and the new cues off.

Head to head (`standard-m5` is the frozen M5 policy):

| Ladder | Games | Corp | Runner |
|---|---:|---:|---:|
| Standard − Standard-M5 | 960 | −6 [−43, 36] | **+102 [66, 145]** |
| Standard − Standard-M5 (with the agenda-safety term) | 480 | −6 [−61, 48] | +41 [−16, 102] |
| Standard − Easy | 960 | **+115 [75, 158]** | **+135 [92, 178]** |

- The Runner gain is clear. Against the M5 Corp, the Runner's score rate rose
  from 28% to 42%, which also brings self-play closer to balance.
- Neither Corp change moved Corp strength. Corp v3 stays the default because
  it is no weaker and carries the distinct Corp Easy; larger Corp gains are
  expected from the A3 turn planner, which can see sequences such as
  install-advance-advance.
- Easy is now clearly weaker than Standard in both seats (Wave 0 found them
  indistinguishable).

## E1: Simulation mode

`GameEngine.Apply(..., ApplyMode.Simulation)` skips invariant validation and
hashing. The result has no accepted-action record and its state is marked
simulated: serializing, snapshotting or hashing it throws, and authoritative
`Apply` rejects it.

- `engine-bench` (official, 6 games, 2,089 positions, measured under load):
  authoritative 2,263 µs, simulation 158 µs per action, **14.4× faster**.
- `fuzz --differential` checks every action in both modes through
  `GameEngine.VerifySimulationEquivalence`: 200 official games (about 76,000
  actions) matched exactly.

## E3: nightly fuzzing and repro

`make fuzz-nightly` runs 1,000 official games with the differential check,
seeded from the date. It runs locally rather than in CI to save Actions
minutes; schedule it with cron on a development machine. `fuzz-repro
<failure-dir>` re-applies a failure's single action from its recorded prior
state and reports whether it still reproduces.

## U1: client quick wins

Checked in the live client on seed 42 and with `make ui-capture`:

- The expanded history panel draws above the hand fan.
- Both score areas are card-sized piles showing the latest agenda face-up with
  a count; the Corp's turn-2 score now appears in its score pile instead of
  vanishing.
- A hand card's hover preview opens above the hand instead of over the other
  hand cards.

The history labels and player-facing prompts were already fixed in Wave 0.

## Gate status

| Criterion | Result |
|---|---|
| U1 before/after captures reviewed | Pass |
| Simulation `Apply` at least 5× faster, differential green on official content | Pass: 14.4×; 200 games matched |
| A2 improves each side's rating without lowering the other's | Runner yes (+102); Corp no change (−6, not lower) |
| Standard − Easy separated head to head in both seats | Pass |
| Nightly run of at least 1,000 fuzz games clean | Pass: 1,000 official games, differential, seed 20260926, all terminal |
| `make m5-gate` | Pass; Standard digest unchanged (`202291b9…`) |
| `make m5-client-smoke` | Pass |
| `make m4-gate` | Pass: official coverage 77/77 |
| Policy performance run (policy code changed) | Pass: p95 below 0.9 ms in every group (budgets 50 and 200 ms) |

Gates ran with `NETRUNNER_TIMEOUT_SCALE=4` on the slower development box.

The frozen M4/M5 tactical suites grade exact choices of the policies they were
written for, so `CorpPolicyTacticalTests` and `RunnerPolicyTacticalTests` pin
Corp v2 and Runner v1. The new defaults are measured by the ladders above
rather than by those suites.

## Playtest follow-up

A human playtest of the merged Wave 1 (Runner against Standard, official
content) found six problems, fixed on `fix/playtest-1`:

| Report | Cause | Fix |
|---|---|---|
| Corp played Seamless Launch onto Diviner | Advancement effects scored a flat value whatever the targets; the only legal target was ICE | Corp v4 values the effect by its best legal target |
| History read "plays a face-down card", "makes a choice" | The view model dropped the public play area; behavior advancement had no history wording | Project the play area; history names the card, server and counter total |
| A stolen agenda showed full size outside the summary pane | The score pile scaled a stale hidden face instead of the rendered one | Scale the rendered face; flight ghosts shrink to the destination size |
| Small cards flew into the score areas on every update | An empty score pile's hidden face counted as a new arrival | Skip hidden and score-pile faces as arrivals |
| The hand preview cut off the hovered card's top | The preview was anchored to the fan, below the lifted card | Anchor to the lifted card's top; the card-art smoke checks it |
| Standard felt easy: agendas sat in remotes, HQ stayed at 1–2 cards | v3 advanced only agendas it could finish this turn, so 4-counter agendas never started; drawing was valued low | Corp v4 advances a protected agenda a full next turn can finish, and draws more readily with 1–3 cards |

Ladder, Corp v4 against the Wave 1 policies (`standard` against
`standard-w1`, 1,920 official games): Corp +6 [−22, 33], Runner 0 [−31, 29]
(same Runner policy). The fixes remove visible blunders, but don't
measurably strengthen the Corp against the bot Runner; a stronger Corp still
waits for the A3 turn planner.

Gates on `fix/playtest-1` (timeout scale 4): `make test`, `make m4-gate`
(coverage 77/77), `make m5-gate` (frozen digests unchanged), `make
m5-client-smoke` and the policy performance run (p95 below 1 ms in every
group) all pass.
