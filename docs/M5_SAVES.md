# Solo saves and policy recovery

M5.5 adds a Match-owned `SoloSaveDocument` with artifact kind `solo-save` and
schema 1. A save contains the validated composition request, complete engine
snapshot, exact installed opponent configuration and committed policy checkpoint,
and a self-contained accepted-action replay from setup. Engine, policy, and
whole-document integrity hashes are separate. UTC timestamps and build provenance
are metadata; changing them does not change the engine or policy hashes.

Saves contain private game state. They are local artifacts, not repository
fixtures or player-facing diagnostics. `SoloSavePreview` allowlists the name,
human seat, seed, preset IDs, difficulty, turn, phase, timestamp, and terminal
status. Browser error text excludes raw invariant failures and hidden card IDs.
No artwork, artwork paths, credentials, UI state, pending tasks, animation queues,
or hover/selection state is serialized.

## Coordinator boundary

The worker services commands between individual accepted actions. At most one
opponent evaluation is in flight. Its completion must match the session generation,
issued decision ID/revision/owner, and starting policy checkpoint before Apply.
Pausing invalidates and cancels tentative thinking. A late result is discarded;
continuing waits until that evaluation exits before starting another one.

`CaptureSaveAsync` returns a detached save and leaves the session paused at its
published action boundary. A client catches playback up to that boundary, writes
the document through `SoloSaveStore`, and confirms the saved turn/phase. Normal
Save can then continue; Save and quit closes only after a successful write. A
failed write leaves the coordinator usable. `CaptureAutoSaveAsync` captures the
same committed state without pausing thinking or presentation.

`GetObservationAsync` reads the current boundary immediately, including during
opponent thinking. Headless clients that need an actionable human decision use
`WaitForHumanDecisionAsync` or `GetReadyObservationAsync`. A paused publication has
`IsPaused = true` and is not actionable until `ContinueAsync`.

Restoring validates the pins, policy version, cursor agreement, replay endpoint,
setup request, full accepted-action replay, and state invariants. The actual
session resumes directly from the saved checkpoint without asking the bot to
rethink accepted actions. Replay reconstructs human-filtered history and stable
server names. Restore publishes no animation events and remains paused, including
at an opponent-owned decision. Continuing a terminal save performs no action.

## Storage and compatibility

The default directory is
`LocalApplicationData/NetrunnerSolo/saves`, shared by launch methods. Tests inject
their own temporary directory. Each `<save-id>.solo.json` may have one `.bak`
generation. The browser uses the files directly and offers a verified backup when
the primary is corrupt or incompatible; it never substitutes it silently.

Writes serialize off the caller thread under a store-level write lock. The shared
`AtomicFile` helper flushes a unique temporary file in the destination directory,
validates it, stages and flushes the previous valid generation, then publishes by
atomic rename. A corrupt primary does not overwrite a valid backup. Temporary
files left by process termination are not browser entries. The tests exercise
first write and overwrite with actual child-process termination; this is not a
claim of arbitrary hardware-failure durability or cross-process writer locking.

JSON above 64 MiB, malformed files, unknown schemas, invalid state, mismatched
rules/content/behavior/runtime pins, and unavailable policy versions fail closed.
Build-version differences alone are informational. There are no implicit schema
migrations, content downloads, or upgrades to a newer bot policy. Missing artwork
uses the existing presentation fallback and does not affect compatibility.

Validate a portable save without launching Godot:

```sh
source scripts/env.sh
dotnet run --project src/Netrunner.Cli -- save-validate /path/to/save.solo.json --json
```

The output contains only the preview and hashes, and the command returns a
nonzero status for corrupt or incompatible files.

## Crash traces

Stateless contract sessions retain the version 1 trace format. Stateful offline
sessions use version 2: `policy-checkpoint.json` atomically commits the replay and
one versioned policy boundary per accepted action. The journal still records
intent before Apply and completion after the durable engine/policy commit. The
legacy `replay.json` is a derived export for version 2; recovery uses the atomic
policy file if a crash interrupts that export.

Recovery checks policy observation cursors, own-action commit counts, last
accepted decisions, unchanged RNG on human actions, configuration identity, and
engine hashes. Earlier action boundaries recover their corresponding policy.
Resumed traces preserve the original self-contained replay prefix, so deleting
older diagnostic directories does not prevent later portable saves or recovery.
A version 1 trace cannot silently acquire a stateful heuristic policy.

## Verification scope

`SoloSaveTests` covers both seats, setup and subsequent continuation, exact
engine/policy/replay agreement, restored filtered history, manual and automatic
capture during thinking, late results, terminal restore, write failures, and
recovery after deleting prior trace folders. `SessionPolicyMatrixTests` adds
prepared Core positions for nested installs, memory, forced discard, trigger
ordering, paid targets, interrupt prevention/replacement, run/access decisions,
payment pools, discounts, and hosted virus counters. These prepared scenario
roots are not presented as ordinary player saves or lab fixtures.

`AtomicFileProcessTests` terminates a separate writer after primary/backup flush
and after publication. CLI tests cover verification output and failures. The
client save browser, playback catch-up, rolling auto-save policy, and native
save/restart/resume flow are M5.6 integration work.
