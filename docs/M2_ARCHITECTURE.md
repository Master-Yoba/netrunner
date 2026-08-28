# M2 architecture and acceptance contract

Status: implementation contract
Owner: primary architecture/review agent
Milestone: representative run and timing engine
Rules baseline: Null Signal Games Comprehensive Rules 26.03

This document narrows the M2 section of `IMPLEMENTATION_PLAN.md` into
reviewable engine contracts. M1's stable IDs, atomic `Apply`, resolution stack,
canonical JSON, scenarios, replay, and agent action boundary remain mandatory.

## Rules sources and supported slice

The rules authority is the current [Null Signal Comprehensive Rules
26.03](https://rules.nullsignal.games/), effective 2 March 2026. Run scenarios
cite the exact applicable rules, principally sections 6.1–6.9 (runs), 7.1–7.5
(access/breach), 8.1 (rez), 9.2–9.10 (windows, abilities, subroutines, lingering
effects), and 10.1–10.6 (damage, tags, counters, memory, and costs). The
[Jinteki.net implementation](https://github.com/mtgred/netrunner) is a behavioral
cross-check only; its compressed UI stops are not copied and do not replace the
Comprehensive Rules.

The representative System Gateway interaction uses only explicitly registered
behavior for:

- Palisade (`30072`) and Whitespace (`30074`) as barrier/code-gate ICE;
- Cleaver (`30006`) and Buzzsaw (`30005`) as matching icebreakers;
- Overclock (`30029`) as the run event and run-scoped hosted-credit effect;
- Offworld Office (`30067`) as an accessed agenda;
- Nico Campaign (`30037`) as an accessed trashable asset.

Official metadata remains inert. Each supported behavior is a compiled,
versioned registration; no rules text is parsed or executed. The interaction
is a development micro-deck/scenario, not a user deck preset.

## Delivery order

M2 is implemented in four dependency-ordered slices:

1. run state and the CR 6.9 timing skeleton;
2. costs, abilities, checkpoints, triggers, prevention/replacement, lingering
   effects, damage/tags/memory/counters;
3. representative card behaviors, access rules, and player observations;
4. scenarios, scripted-human and contract-agent coordinators, CLI proof,
   replay/save coverage, and timing-trace review.

Each slice must build the whole solution and preserve every M1 test and CLI
golden. Broad Gateway card work remains outside M2.

## Authoritative run state

`GameState` gains an optional, deeply detached `RunState`. A null run is omitted
from JSON so M1 snapshots and hashes remain byte-compatible. A live run stores
only serializable values:

- attacked server and initiating side/source;
- exact CR phase and timing step (`6.9.1a` through `6.9.6d` where relevant);
- current ICE position and card ID, with the server ICE zone ordered
  innermost-to-outermost and runs traversing from the last element toward zero;
- successful/unsuccessful flags and end reason;
- current priority window, priority holder, consecutive passes, and allowed
  paid/rez capabilities;
- current encounter, ordered subroutine instances and broken/resolved status;
- breach candidates, random-access limit, chosen/accessed cards, and current
  access state;
- run-scoped credits and run/encounter-scoped effects with explicit expiry.

The state never stores delegates, callbacks, registry objects, or card text.
Run, encounter, subroutine, effect, and ability identities are stable typed
values or deterministic `(source card, registered behavior, ordinal)` keys.
Invariants reject a run/phase/position/window/encounter/breach combination that
cannot exist under the declared timing step.

## Run timing contract

A versioned `RunTimingFrame` owns progression through CR 6.9. It performs one
bounded automatic step, emits a typed timing event, then either advances, pushes
a child frame, or yields a materialized decision. The canonical progression is:

```text
6.9.1 initiation
  -> 6.9.2 approach ICE (when positioned at ICE)
  -> 6.9.3 encounter rezzed ICE
  -> 6.9.4 movement / jack out / move inward
  -> repeat approach, or approach server
  -> 6.9.5 success and breach
  -> 6.9.6 run ends and cleanup
```

The approach window is the normal opportunity to rez the approached ICE (CR
6.4.3/6.9.2b). Encounter initializes all subroutines unbroken, permits legal
interface abilities only at sufficient breaker strength, then resolves every
remaining subroutine in printed order (CR 6.5 and 9.8). Movement exposes the
paid window before the Runner's jack-out choice and the paid/non-ICE-rez window
after moving inward (CR 6.6/6.9.4). Success calls breach as a child resolution;
run-end cleanup occurs even after jack out or an end-the-run subroutine.

Priority windows alternate priority deterministically, beginning with the
active player unless the cited rule says otherwise. A pass transfers priority;
the window closes only after both players pass consecutively without an
intervening ability or rez. Legal actions are rebuilt on every priority change.
No client-side auto-pass may remove a legal timing window; a coordinator may
choose an issued pass action as policy.

The run paid windows are exactly 6.9.1e `(P)(R)`, 6.9.2b `(P)(R)` plus the
approached-ICE rez permission, 6.9.3b `(P)`, 6.9.4b `(P)`, and 6.9.4e `(P)(R)`.
There is no paid window in the Success Phase. Passing—not a special
"decline rez" action—is how the Corp declines a rez opportunity without
removing the other player's priority. The 6.9.4c jack-out/continue choice is a
Runner-only decision after the first Movement window closes; continuing causes
the 6.9.4d inward movement automatically.

## Effects, costs, and checkpoints

Reusable effect records describe credit/click costs, moves, counters, damage,
tags, strength changes, and run/access changes. Paying a cost and resolving its
effect occur inside the existing atomic transaction. An unaffordable or stale
action is never materialized; a resolution failure rolls back cost, effect,
RNG, allocators, run state, logs, and sequences together.

Each instruction becomes imminent before it resolves. A serializable interrupt
frame offers applicable prevention/replacement choices, applies at most the
legal combinations, then resolves the resulting instruction. Replacement
effects retain the replaced effect's source/context. Damage uses the serialized
RNG to choose grip cards and awards a Corp flatline when the Runner cannot
suffer the full amount. Core damage changes maximum hand size. Tags,
base/modified memory, installed program memory costs, named counters, and virus
purges are state/invariant-backed primitives. Installing a program may first
yield choices to trash installed programs so the post-install rig is legal; a
later memory-limit reduction opens the same mandatory cleanup at the next
checkpoint (CR 3.9.3), rather than rejecting an otherwise legal installation.

Game-level lingering effects are stored outside `RunState` so effects with a
turn duration survive run cleanup. Checkpoints collect conditional abilities
from committed occurrences. Mandatory
triggers resolve; optional triggers yield use/decline; simultaneous triggers
yield an ordering decision to the controlling player before resolution. A
triggered ability records its source and trigger snapshot so moving its source
after it becomes pending does not silently erase or retarget it. Lingering
effects have an explicit duration (`encounter`, `run`, or `turn`) and are removed
only by the corresponding cleanup checkpoint.

## Breach and access contract

The breach frame follows CR 7.5. Remote candidates are installed cards in the
server root. HQ chooses one eligible hand card using deterministic hidden-state
randomness; R&D chooses from the top in order; Archives uses eligible discard
cards and turns them faceup as required. Candidate and access ordering are
stored so save/load cannot reroll or reveal a different card.

Each access follows CR 7.2: access begins; exactly one legal mid-access ability
may be chosen (including pay-trash or no action); an agenda still present is
then mandatorily stolen; access ends. The Runner may not replace mandatory
stealing with no action. Trash costs are paid atomically. Accessed hidden cards
are visible to the Runner observation only for the rule-defined duration.

## Observation boundary

`ObservationBuilder.Build(state, viewer)` is the only production path from
authoritative state to an agent/client DTO. It exposes the current viewer's
public/known card views, their own private zones, public run/encounter data, and
only the pending decision's legal actions when that viewer owns it. It never
serializes `GameState` or exposes unknown Corp definitions, HQ/R&D contents,
the Runner grip/stack to Corp, hidden access candidates, RNG state, or debug
data. Exact JSON-shape negative tests cover both sides before, during, and after
access. Scripted and conventional agents consume this same observation.

## Compatibility and modularity

- M1 no-run states must retain their canonical state/event hashes and replays.
- New optional/default fields use explicit omission rules; live M2 state is
  fully explicit and strict once present.
- `GameEngine` may become a partial class so run/effect/access reducers live in
  focused files. The transaction owner and one `Apply` boundary remain single.
- Frame/action/event polymorphic registrations are exhaustive and fail closed.
- Every yielded M2 decision round-trips through `EngineJson`, can resume, and
  produces the same next decision/actions/hashes.
- Scenario JSON remains strict: unknown, missing, or contradictory run fields
  report the precise path.

## Required timing scenarios

Checked-in scenarios and tests cover at minimum:

1. initiation against unprotected and protected central/remote servers;
2. each initiation/approach/encounter/movement/success/run-end paid window;
3. Corp rez and decline-to-rez paths; unrezzed ICE is passed without encounter;
4. breaker strength below/equal/above ICE and partial/full break;
5. ordered unbroken subroutines, including end-the-run cleanup;
6. jack out at each legal movement decision and rejection elsewhere;
7. remote, HQ, R&D, and Archives breach candidate behavior;
8. trash, no-action, and mandatory agenda steal access paths;
9. conditional mandatory/optional/simultaneous triggers and explicit ordering;
10. prevention, replacement, encounter/run lingering expiry, damage/flatline,
    tags, mandatory memory cleanup, counters, and purge;
11. save/load at every yielded decision and representative nested interrupt;
12. exact Corp/Runner observations with no hidden-information leakage;
13. identical seed/actions yielding byte-identical state, events, timing trace,
    and replay hashes.

Every timing scenario records the applicable CR 26.03 rule numbers and a short
Jinteki.net behavioral cross-check note. Cross-check differences are documented,
not silently copied.

## M2 exit gate

M2 is complete only when:

- the curated run finishes headlessly from a scripted-human policy and a
  contract agent using issued legal action IDs through filtered observations;
- Palisade and Whitespace are legally rezzed, encountered, broken or fired in
  order, and the run reaches remote breach/access of Offworld Office and Nico;
- the Overclock run-scoped credit effect is spendable only during that run and
  expires at run end;
- save/load succeeds at every yielded decision in the representative trace;
- an automated trace validator and human-readable review find every CR-required
  window present and ordered;
- all M1 and M2 tests, CLI replay/determinism gates, format/analyzers, secret
  scan, and existing Godot headless smoke remain green.
