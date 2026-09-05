# M4.9 — Card-table UI progress

Date: 5 September 2026

Status: UI-490 through UI-494 implemented; local M4 regression gate passed.
The card table is the default match UI. UI-495 awaits player acceptance. **The required player usability review is pending;
M5 is not cleared by automated games or these screenshots.**

Plan: [M4.9 UI plan](M4_9_UI_PLAN.md).
Implementation branch: `feat/m4-9-card-table`.
Implementation checkpoints: `9f946b7`, `7e527de`.
Draft PR: [#15](https://github.com/Master-Yoba/netrunner/pull/15).

## Implemented behavior

- Corp servers occupy permanent central lanes followed by stable numbered
  remotes. Roots sit above sideways ICE; the outermost ICE is nearest the
  Runner. Overflow scrolls within lanes and card strips.
- The lower field contains Runner identity, resources, rig, Stack/Heap and the
  local player's hand. Both seats keep the same board orientation. Opponent
  hands and decks expose counts and neutral backs.
- Cards have bounded faces, title/type/stat overlays, counters, status, focus,
  selection and legal-target cues. Local hand cards overlap and remain reachable
  by keyboard. Hosted Runner cards overlap their host, including Corp ICE.
- Optional local images use `user://card-art/{definition-code}.png`. No artwork
  is fetched during play. Native evidence uses the original geometric frames
  and metadata fallback; no third-party image set was provisioned or claimed
  as reviewed. The reference screenshot is not imported as game artwork.
- The contextual dock offers basic actions or actions for a selected card or
  server, staged target confirmation and Back. The complete issued-action list
  remains accessible. Run prompts and highlighted lanes identify the current
  run; encounter tooltips show strength and subroutine state.
- Archives, Heap, scores and decks open a keyboard-accessible pile browser.
  Public card faces open inspection; concealed piles remain count-only.
- A compact right-edge history rail expands to readable chronological history,
  with turn separators, details, frozen known-card inspection and copy/export.
  Pinning reserves a separate column on large windows; 720p keeps an overlay.
  Older-page reading survives new entries and shows an unread count. All
  in-session entries are retained; only 80 groups are rendered per page.
- Accepted publications are presented in order. Movement, reduced-motion
  highlighting, speed and skip do not own history retention. Skip cancels visual
  work; action controls wait for moving publications to settle. Submissions
  revalidate both current decision and current action membership.
- New match returns to setup. `make play` provisions no network data: it builds
  and launches against the existing authenticated local Gateway metadata.
  `--diagnostic-ui` retains the previous composition for development.

## Architecture and review

`MatchHistoryProjector` is a trusted Match-boundary adapter. It sees the accepted
transition and emits an allowlisted viewer-scoped summary. Godot never receives
raw opponent actions or authoritative state for its history rendering. Known
names begin with the previous observation and are admitted after explicit public
reveals. Private movement retires remembered identity. Historical inspection
uses frozen metadata with separate history handles and scrubbed physical links.

`MatchHistoryStore` deduplicates accepted action keys independently of rendering.
`GameTableControl` reconciles persistent card controls from filtered models;
`TableCardControl` resolves artwork only for known designs. `MatchHistoryPanel`
retains entries independently of its paged controls. `Main.CardTable` owns
composition, publication playback and routing of engine-issued IDs.

Architect review corrected compact-card sizing, hidden-card inspection reset,
server selection, run-fingerprint refresh, stale targets, publication settlement,
movement cancellation, transient-zone history handling, automatic rez attribution,
private-movement identity retirement, complete source/server action mapping,
and the ungrouped issued-action fallback. The full-game identity assertion was
adapted to the rendered table cards instead of obsolete diagnostic labels.

The Nico opening is unchanged: seed `47042`, basic decks, both hands kept,
human Runner passing priority. Corp installs/rezzes Nico, replaces/rezzes another
Nico, then replaces it with a concealed installation. History explains both
outgoing Nico cards going to Archives without naming the final hidden card.
Opponent policy remains M5 work.

The only Core change is additive public Runner `usedMemory`/`memoryLimit`
observation fields when RulesState exists, using the engine's existing static
modifier projection. Match maps them as nullable values and Godot displays them.
Legacy M1 observation shape, serialized state and gameplay remain unchanged.
Focused tests cover legacy omission, initial 0/4, and an active modifier.

## Verification record

- Focused Match history tests cover 2,000-entry retention and deduplication,
  thinking publications, accepted actions, the real Nico opening, hidden draws
  and discards, reveal/private movement, same-action knowledge retirement,
  automatic Corp rez attribution, missing transient zones, public shuffle and
  discard/trash details, and scrubbed historical handles.
- `--m4-9-ui-smoke` uses real mouse input for the official Nico opening and
  starts an actual run through server selection and its contextual Run button. Dense
  fixtures cover six remotes, five ICE, ten hand cards, twelve Runner cards,
  hosted cards, long history, both seats and both target resolutions. It checks
  card/dock bounds, stable nodes, keyboard overflow, inspection, history paging,
  motion cancellation, piles, run and outcome/restart presentation.
- Native OpenGL review was performed on Apple M5, Godot 4.7.2 .NET, at
  1280×720 and 2560×1440. Earlier rejected screenshots exposed clipped hand
  content; the strengthened smoke now asserts actual hand and rig visibility.
- A 120-frame native dense-board/history sample measured median 8.25 ms and
  p95 11.13 ms on Apple M5. This is an idle presentation sample, not a claim
  about every animation or a completed manual gameplay performance session.
- Final local `make m4-gate` passed on implementation checkpoint `7e527de`:
  1,362 solution tests (798 Core, 314 Cards, 59 AI, 68 CLI, 123 Match), official
  supplemental checks, formatting, proofs, native/headless UI checks, secret
  scanning and 77/77 coverage. Subsequent concealed-focus, compact-title and complete contextual-routing
  corrections are covered by the refreshed native smoke and PR CI.
- A Linux synthetic run caught a shutdown resource-binding failure after its
  functional assertions passed. Temporary card drawing/theme wrappers now have
  explicit disposal. Three repeated synthetic startup/shutdown checks and the
  official-content UI check exit cleanly locally; PR CI verifies Linux cleanup.
- The four official games all completed through table controls, restarted, and
  returned to fresh setup: Corp/basic 928 actions, Runner/basic 1,715,
  Corp/expanded 1,094, Runner/expanded 964; all use seed `47042`.
- [PR checks](https://github.com/Master-Yoba/netrunner/pull/15/checks) run the
  complete synthetic `make verify` and official `make m4-gate` lanes on Linux.
  Both lanes passed checkpoint `7e527de`; the live checks are the authoritative
  result for the PR's current revision.

## Visual evidence

- [Native check output](evidence/m4-9/native-ui-smoke.txt)
- [User reference](evidence/m4-9/reference/chiriboga.png)
- [Nico opening table](evidence/m4-9/nico-table.png)
- [Nico history](evidence/m4-9/nico-history.png)
- [Dense Runner table, 720p](evidence/m4-9/dense-runner-1280.png)
- [Dense Corp table, 720p](evidence/m4-9/dense-corp-1280.png)
- [Dense Runner table, 1440p](evidence/m4-9/dense-runner-2560.png)
- [Dense Corp table, 1440p](evidence/m4-9/dense-corp-2560.png)
- [Encounter presentation](evidence/m4-9/active-run-runner-1280.png)
- [Expanded history](evidence/m4-9/history-runner-1280.png)
- [Archives browser](evidence/m4-9/archives-browser-1280.png)
- [Outcome presentation](evidence/m4-9/outcome-runner-1280.png)

These are native screenshots of scripted real-game/fixture sessions. They are
not evidence that a human completed or accepted a manual game.

## Required player acceptance before M5

Launch `make play`. Use basic decks and seed `47042` as Runner, keep the hand,
and pass Corp priority windows to review the Nico opening. Then complete a
basic game in each seat and exercise expanded decks, run/break/access,
installation destinations, paid abilities, nested choices, cancellation and
history browsing while actions arrive. Use both 720p and 1440p; UI-scale options
are available in the toolbar for larger displays.

Record seed, deck pair, build (included in copied history), observed problems
and acceptance. Confirm all eight manual criteria in the plan, including
understanding the replacement without developer explanation. No named player
session or usability approval has yet been supplied. Remaining layout or
interaction defects found by that review must be fixed before declaring M4.9
complete or starting M5.
