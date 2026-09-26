# Offline play and saved games

The normal setup uses the offline heuristic opponent, with Standard selected by
default and Easy available for either seat. Both use the same legal actions and
information boundary. The two official preset families and explicit game seed
remain selectable; Generate seed displays a fresh seed before starting.

Choose **Resume saved game** at setup, or **Load game** from the settings cog.
The browser shows the name, human seat, difficulty, decks, seed, turn/phase,
saved time and compatibility. It handles corrupt entries independently and
explicitly offers a verified previous backup when one is available. Loading
validates the complete save and reconstructs filtered history on a worker before
replacing the active match. The restored board starts paused with **Continue
match**; completed games stay completed. Loading from practice leaves the lab.

Use **Save game** or **Save and quit** in the cog. Each manual save creates a
named entry, preserving earlier saves. Capture pauses at a committed decision;
the displayed board catches up to that boundary before confirmation. Normal
saving continues an active game, while an already paused game stays paused. A
brief status confirmation names the saved turn and phase while play continues.
Save and quit closes only after durable storage succeeds. A failure keeps the
match available and explains how to retry. Escape closes the topmost dialog and
restores focus; closing a pending load cancels its effect on the active match.

A rolling **Automatic save** is captured after setup, at turn boundaries, at
outcome and after successful manual saves. It retains a previous valid backup.
Automatic capture does not interrupt opponent thinking or animation. Writes
are coalesced and serialized by one Match-owned service for the application,
including across New game scene reloads. Session generations reject stale
captures and callbacks, and an older capture cannot overwrite a newer boundary
within the same session. Automatic-write failures appear in the board status.

Files live in `LocalApplicationData/NetrunnerSolo/saves`, independent of launch
scripts and diagnostics. **Open save folder** opens that location. The optional
`NETRUNNER_SAVE_DIRECTORY` override is useful for isolated tests. No artwork is
stored in saves; an absent local artwork cache uses the existing fallback cards.
See [save contract and storage guarantees](../M5_SAVES.md).

## Implementation boundaries

`OfflineMatchFactory` chooses the opponent configuration. Historical full-game
proofs still explicitly select the contract agent. Trace recovery restores the
saved stateful policy/profile or the supported legacy contract format.
`SoloSessionService` owns private save documents and disk work; Godot receives
only previews, receipts and a loaded coordinator. The save modal is a reusable
scene, `scenes/components/SoloSaveDialog.tscn`.

Every coordinator publication carries the originating client generation. Loads
clear queued presentation, motion, hover, inspection, selections and staged
choices. Async submission, start, continue, save and load callbacks check their
origin before touching the current board. Transient UI state is never serialized.

## Reproducible integration checks

With official content already provisioned:

```sh
make m5-client-smoke
# Two representative seat/preset cases during development:
bash scripts/m5-client-smoke.sh --quick
```

The full matrix uses Standard in both seats and preset families plus Easy in
Runner/expanded. Each case launches a save-and-quit process and a fresh resume
process which finishes the game, reloads the terminal save and returns to setup.
The smoke additionally exercises animation-boundary saving, corrupt entries,
failed Save and quit, verified backup loading, Escape/focus, canceled loads,
stale publications and saving an already paused game. Corp cases use 1440p and
Runner cases 1080p. Both normal and reduced motion are exercised. Output goes to
ignored `artifacts/m5/client-*`; it includes private saves/traces and must remain
local. These scripted games are integration checks, not human playtesting.
