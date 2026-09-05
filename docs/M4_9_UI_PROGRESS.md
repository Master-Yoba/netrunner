# M4.9 UI progress

Status: implementation in progress. The first spatial table, viewer-scoped
history projection, retained history store and history panel are present in the
working tree. The manual usability gate has not been run or accepted, and no
M4.9 completion or successful full-gate claim is recorded here.

Date: 5 September 2026

The visual reference in `docs/evidence/m4-9/reference/chiriboga.png` was
inspected while shaping the compact table and right-edge history rail.

## Architecture records

| Area | Current implementation | Boundary and fallback |
|---|---|---|
| Table composition | `GameTableControl` lays out the board, stable remote names, hand and piles, identities, servers and ICE, rig, hosted cards, targets and selection. | It consumes presentation models and raises presentation handles; Core state and action submission remain outside Godot. |
| Card presentation | `TableCardControl` renders known cards with optional local artwork, neutral concealed backs, counters, status, focus and target cues. | Concealed cards do not receive metadata or artwork. The existing diagnostic composition remains the original fallback during migration. |
| History projection | `MatchHistoryProjector` emits viewer-scoped `MatchHistoryEntry` summaries, details and known cards at the Match boundary. | Godot receives public projections only; it does not read raw events, hidden IDs or authoritative snapshots. |
| History retention | `MatchHistoryStore` retains the full in-session entry list and deduplicates stable keys. | Rendering, animation and paging do not own retention. A new session clears the store. |
| History rendering | `MatchHistoryPanel` provides a 60-pixel rail and 320-pixel expanded panel, retains entries while rendering at most 80 groups per page, preserves older-page browsing with a new-entry count, and supports details, known-card inspection and copy/export. | The panel accepts `MatchHistoryEntry` projections only and does not expose raw state or event access. |
| Table action routing | `Main.CardTable` composes the table and history, maps presentation selection to the current issued action list, and stages and submits action IDs. | Core remains authoritative. Stale action membership and first-frame layout behavior still need review before acceptance. |

The original diagnostic composition remains available as a fallback for smoke and
gallery paths while the spatial table migration is in progress. It is not
completion evidence for the M4.9 manual gate.

## Slice status

| Slice | Status and remaining evidence |
|---|---|
| UI-490 visual baseline | Spatial table, cards, zones, compact rail and expanded history/dock are present. Native frames at 1280x720 and 2560x1440 for both seats remain to be reviewed. |
| UI-491 history and safe transitions | Entry, projector, store, panel and publication wiring are present. Replacement ordering, hidden-information, skip/reduced-motion and focused tests/manual review remain. |
| UI-492 live table and cards | Servers, ICE, rig, hosted cards, piles, identities, selection and inspection are present. Native overflow, focus reachability and artwork/fallback review remain. |
| UI-493 decisions and runs | Action filtering, target staging, confirmation/cancel, run prompts and issued IDs are present. Keyboard, nested choices and stale-action behavior remain to be reviewed. |
| UI-494 motion and comprehension | Publication queue/playback, reduced-motion handling, skip and history retention are present. Ordered replacement/reveal and Nico-session review remain. |
| UI-495 playtest gate | Not started. No manual acceptance is claimed. |

## Evidence recorded so far

- The Chiriboga reference frame was inspected.
- `tests/Netrunner.Match.Tests/MatchHistoryTests.cs` covers retention,
  accepted-action publication, hidden-information boundaries, Nico replacement
  and opponent-thinking behavior.
- No native screenshot session, performance measurement, M4.9 manual session,
  `m4-gate` result or complete verification result is claimed by this record.

## Review items before acceptance

- `Main.CardTable` currently relies on the `tableViewport.Resized` callback to
  position the history panel. Confirm the first native frame positions the rail
  before input is available.
- `GameTableControl.BuildBoardFingerprint` does not include active-run state.
  A run can start or end while cards are unchanged, leaving its lane marker
  stale until another fingerprinted board change occurs.
- Table action callbacks close over the build snapshot. Decision identity is
  checked, but same-decision action membership should be checked before staging
  and submitting an issued ID so a stale option is rejected immediately.
- Server selection filters actions but needs a visible selected-lane cue.
- `FriendlyText` only rewrites numeric server and card handles; every public
  presentation handle should have a player-facing label.
- Hovering or focusing a concealed card can leave the previous known card in
  the inspection action. Clear the action or make the concealed state explicit.
- At 1280x720, confirm that an expanded history overlay leaves the decision dock
  and the active run lane reachable.
