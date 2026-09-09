# Recoverable playtest sessions

Playtest diagnostics must be sufficient to restore an engine decision without a
player describing their moves. The action log shown on the board is a filtered
presentation and is not a recovery artifact.

## Recording contract

Each match gets a new local diagnostics directory. The authoritative recorder
lives in Match, next to the serialized coordinator loop. It records the complete
initial engine snapshot, including shuffled zones, deterministic random state,
allocators, resolution stack, and pending decision. It also records the match
request, human seat, content/catalog versions, and build provenance.

For every human or agent submission, an intent is flushed before engine Apply.
The accepted action and resulting state/event hashes are flushed before the
result can be published to the UI. Rejections are diagnostic records and never
advance the recovery state. Authoritative recording failure stops advancement
visibly rather than silently continuing without a recoverable trace.

Recovery verifies the recorded actions with the existing deterministic replay
verifier and the matching behavior/runtime catalogs. It rejects corrupt complete
records, incompatible content, and hash divergence. An incomplete final write
may be discarded; an interrupted intent is reported separately from a committed
result. Recovery promises the last durably completed engine decision, not the
instruction pointer of a native crash. Earlier accepted-action boundaries can
also be recovered.

The client separately records displayed snapshot/action sequences, user commands,
window focus, viewport, runtime information, and shutdown/error signals. These
records explain presentation timing without influencing engine randomness. A
native SIG11 does not need to invoke a managed exception handler for previously
flushed records to survive.

## Resuming

Opening a recovered session pauses at its verified decision, including when the
opponent owns it. Continuing explicitly restarts play. The continuation writes
to a fresh directory, preserving the original session for investigation.

These are local debugging artifacts containing the full game state, including
hidden cards. They are not routed into observations, opponent agents, or the
in-game public history. Send the diagnostics folder with a native crash report;
there is no need to recount the game actions. Matching code and local card data
are required for verified reconstruction; the trace does not bundle card assets.

Durable flushes protect against abrupt process termination. They do not promise
recovery after storage hardware failure or arbitrary filesystem corruption.

## Command-line investigation

With the same official content configuration used by `scripts/play.sh`, verify
and export a session without launching Godot:

```sh
./scripts/recover-session.sh /path/to/session \
  --replay-out /tmp/recovered-replay.json --snapshot-out /tmp/recovered-state.json
```

Add `--at-action 27` to stop at that accepted action sequence instead of the last
committed action. Add `--json` for machine-readable results. Reconstruction prints
the recovered action sequence and deterministic state hash. Snapshot export
contains the complete engine state; replay export remains compatible with the
existing `replay` command.


## Everyday use

Start normally with `make play`. Recording is automatic for official matches.
The terminal prints a launch folder under `.tools/godot-data/diagnostics/` and a
`SESSION_TRACE directory=...` line for each match. **Open logs** opens that launch
folder: it contains `play-output.log`, native `godot.log`, and a separate directory
for each session. Direct Godot launches use `user://diagnostics` for session files.

After a crash, send the launch folder. Each session contains `manifest.json`,
`replay.json`, `journal.jsonl`, and `ui-diagnostics.jsonl`. The replay file is
replaced atomically before its completion record is appended, so recovery can
also identify a committed result whose final journal write was interrupted.

To reopen the last recorded decision:

```sh
./scripts/play.sh --resume-session "/absolute/path/to/launch/session-directory"
```

Relative paths are resolved from the directory where the launcher was invoked.
The board remains paused until **Continue recovered match** is selected. The
original folder is unchanged; the continuation gets its own launch and session
folder. Use `--resume-at-action 31` to reopen an earlier accepted action in the game,
or inspect/export it with `recover-session.sh --at-action 31`.

## Verification

The official seed 47042 card-table smoke records and verifies action 32 with
state hash `fa59803437b93ca487a446cc31196dd5d9c6a00b3bd1896702a196e86b6d85fc`.
Godot reopens that boundary paused. A separate process was forcibly terminated
with SIGKILL during the R&D access decision, before recorder disposal or UI
shutdown. Recovery returned action 31 with hash
`9761e91492234ce80ffb877c90bd4ab70d8bf5502881b821c984031060ba82bf`.

Focused regressions cover durable publication, rejected actions, interrupted
records, journal corruption, hash/pin mismatches, source preservation, paused
opponent decisions, export validation, and stopping after recording failure.
Match tests: 138 passed. CLI tests: 77 passed. Official Godot builds, native
record/recovery smoke checks, shell syntax, and formatting checks pass.

This verifies recovery from abrupt termination; it does not fix the separate
macOS font-cache crash.
## Reproduced playtest failure: Jailbreak agenda access

The 2026-09-06 Corp-seat session with seed 47042 stopped at action 35 during
Jailbreak on HQ. Action 36 passed the access window and automatically stole
Offworld Office. The validator incorrectly reconstructed two remaining accesses
after the agenda left HQ; the engine correctly retained one.

The cursor invariant now accounts for chosen cards that leave their source
zone. Existing snapshots do not retain whether a moved card came from the
hidden pool or server root, so validation checks the feasible range in that
ambiguous case and remains exact when origins are known. No serialized state
fields or replay hashes changed. A future exact origin ledger would need an
explicit snapshot/replay migration.

Recovery of the original trace retained its action-35 hash. Retrying action 36
succeeded with two Runner agenda points and one remaining access; normal agent
continuation completed the run through action 39 without error. Snapshot export
also now supplies the recorded session's behavior/runtime catalogs, allowing
active official run events to be exported and validated.
