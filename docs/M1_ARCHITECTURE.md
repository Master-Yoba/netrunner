# M1 architecture and acceptance contract

Status: implemented and accepted — see `M1_COMPLETION.md`  
Owner: primary architecture/review agent  
Milestone: deterministic headless toy game

This document narrows the M1 section of `IMPLEMENTATION_PLAN.md` into contracts
that every implementation slice must preserve. The implementation may improve
names and internal layout, but changing a contract requires an explicit review.

## Boundaries and dependency order

M1 is delivered in four dependency-ordered slices:

1. state foundation: typed IDs, state, snapshots, invariants, RNG and JSON;
2. engine boundary: decisions, legal actions, atomic apply and resolution frames;
3. evidence boundary: action/event logs, canonical hashes, replay, scenarios and
   pinned card-data normalization;
4. playable proof: toy rules, two fixture decks, CLI agents and every toy win
   condition.

`Netrunner.Core` owns deterministic game truth. `Netrunner.Cards` owns static
card metadata and behavior registration. `Netrunner.AI`, `Netrunner.Cli`, and
Godot are consumers; none may mutate authoritative state except by submitting a
materialized legal action to Core.

## State contract

- Every engine identity is a validated typed value serialized as a stable string.
  Engine-issued identities come from state-owned monotonic counters, never a
  GUID, wall clock, runtime hash, or global process state.
- `GameState` is a versioned data aggregate. It records the rules and card-data
  versions, complete authoritative card/zone/server/player/turn state, outcome,
  allocator state, and the exact deterministic RNG state. Every `ulong` stream
  state is valid, including zero; serialization preserves the current state,
  not merely the original seed.
- Authoritative payloads are strongly typed. `object`, callbacks, delegates,
  tasks, Godot objects, service handles, and executable code pointers are not
  permitted in state, decisions, frames, actions, or replay records.
- Collection iteration is never a rules tie-breaker. Rules that require order
  store an ordered list; unordered maps are sorted before canonical output.
- A snapshot is deeply detached and immutable at its public boundary. It cannot
  be used to mutate the live state. Agent/client observations remain a separate,
  filtered DTO and are not part of M1's state-foundation slice.
- Loading is strict: unknown schema/frame/behavior versions, invalid typed IDs,
  and invariant failures return actionable compatibility or validation errors.

## Action transaction contract

The only rules mutation boundary is equivalent to:

```text
Apply(state, decision-id, decision-revision, action-id) -> ApplyResult
```

An action is selected exclusively from the current decision's materialized
legal-action list. It carries no caller-invented parameters. Apply first checks
terminal state, owner, decision identity and revision, then action membership.
It resolves on an isolated working state and event buffer. State, RNG, allocator,
action log, event sequence, and next decision commit together only after
invariant validation. Rejection or exception leaves the input byte-equivalent
to its pre-call canonical form and emits no accepted action or committed event.

`ApplyResult` distinguishes acceptance from typed rejection (including stale,
wrong owner, unknown action, invalid state and resolution failure). Rejections
are diagnostics, not replay inputs.

## Resolution contract

Resolution is a LIFO stack of versioned, JSON-serializable discriminated frames.
A frame executes one bounded step and then completes, pushes child frames, or
yields one typed decision. Nested input is a sequence of decisions, not a
partially filled generic action.

Frames refer to registered rule/card behavior IDs plus serializable arguments.
Save/load during a pending nested prompt must restore the exact stack, decision
ID, owner, revision, legal actions, RNG, and allocator. Unknown frame or behavior
IDs fail closed with a useful message.

## Logs, canonical form and replay

- Accepted actions are the canonical replay input. Structured events are the
  presentation and diagnostic stream; replay never reapplies events as rules.
- Action and event envelopes have monotonic sequence numbers and stable IDs.
- Canonical JSON uses UTF-8, stable field names, sorted object/map keys, preserved
  semantic array order, and no insignificant whitespace. JSON numbers are parsed
  as exact base-10 components and emitted as one normalized plain-decimal form:
  fractional trailing zeroes and exponent notation are removed, and every
  spelling of zero becomes `0`. Non-finite, ambiguous, duplicate-key, or
  unrepresentably large numeric input is rejected; typed engine integers remain
  exact through their JSON converters. Metadata timestamps are excluded from
  deterministic hashes.
- State and event hashes are lowercase SHA-256 of their canonical bytes.
- A replay contains its initial state or scenario identity and seed, accepted
  actions, and optional expected state/event hashes. Verification stops at the
  first divergence and identifies the action sequence and expected/actual hash.

## Toy rules boundary

The M1 game is intentionally smaller than full Netrunner but uses real engine
boundaries. It supports deterministic setup, Corp and Runner turns, clicks,
basic credit and draw actions, install, Corp agenda scoring, Runner access and
agenda stealing, agenda-point victory, and deck-out victory. Unsupported card
text is data only and cannot silently execute.

The two tiny decks are deterministic test fixtures, not advertised user presets.
Each toy-game path must be finishable by a contract agent through legal action
IDs alone.

## Required review matrix

Each slice must build the complete solution and keep earlier tests green. M1 is
not complete until automated tests demonstrate:

- invalid IDs and malformed state fail with actionable errors;
- snapshot collection data is detached from authoritative mutable sources;
- fixed-seed shuffle matches a checked-in golden sequence and RNG restores;
- stale, out-of-turn and unknown actions reject without changing state, RNG,
  logs, sequences, or hashes;
- an injected resolution failure rolls back the entire transaction;
- nested decisions and a save taken during a prompt round-trip exactly;
- scenario JSON rejects missing/unknown/invalid fields at the correct boundary;
- replaying identical seed/actions twice yields byte-identical canonical state
  and event streams and equal hashes;
- contract agents finish by Corp score, Runner steal/score, and deck-out, with
  no illegal choice, crash, stall, or unbounded loop;
- CLI `scenario`, `play`, and `replay` return nonzero with useful diagnostics for
  invalid input or divergence.

The final gate is `scripts/verify.sh` plus an explicit M1 CLI gate covering all
win reasons. M2 work does not begin while an M1 contract above is waived.
