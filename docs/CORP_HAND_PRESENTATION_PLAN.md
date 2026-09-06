# Corp hand presentation plan

The table exposes the Corp hand through two complementary projections: the
Corp player keeps a playable local hand, while the board exposes only the
public HQ identity and hand count to either viewer. HQ does not duplicate the
hand with decorative card backs.

## Visibility

| Viewer | HQ projection | Local hand |
| --- | --- | --- |
| Corp | Known Corp identity and `N cards in hand` beside it | Known card faces and normal selection/inspection |
| Runner | Public Corp identity and `N cards in hand` | Runner card faces and normal selection/inspection |

The count is public and contains no card identity, title, definition, or
ordering information. Installed root cards remain in their server lane and
retain their existing public or filtered presentation.

## Interaction

The HQ count is display-only; the public identity remains inspectable. Corp hand actions continue to use
the existing local-hand card handles, selection state, and legal-action
targets. Runner hand actions use the same path for the Runner's local hand.
The count does not create an action target, and a filtered hand
never gains an inspectable hidden-card handle.

## Acceptance

- HQ renders one identity projection plus the public `N cards in hand` count;
  no HQ hand-back controls are created.
- Corp and Runner local hands remain selectable and inspectable as before.
- Installed upgrades and other root cards remain rendered in their lane.
- Server lanes render in Archives, R&D, HQ, then stable first-observed remote
  order; an unknown server label lookup does not allocate a remote number.
- Hidden Runner cards use the Runner back; hidden Corp cards use
  the Corp back without changing filtered card contents.
- A future full Corp-hand view can add private card actions behind the same
  visibility boundary without changing the public HQ count contract.

## Later implementation

1. Add a dedicated hand component with a private face-up fan for the local Corp
   and a public face-down fan for the opposing Runner. Keep it visually separate
   from installed HQ upgrades and the identity; retire duplicate hand displays.
2. Support hover/focus lift, readable preview, keyboard traversal and horizontal
   overflow for large hands. Route play/install through issued action IDs and
   the existing confirmation flow. Keep the current playable hand until this
   replacement passes the same interaction checks.
3. Represent opponent cards by anonymous positions only. Drawing, shuffling and
   leaving HQ must not preserve trackable hidden identities. An HQ access reveals
   only the engine-projected accessed card in the access panel.
4. Animate draws and hand-count changes from filtered observations, including
   reduced motion. Retain the public count for empty and overflowing hands.
5. Verify both seats at 720p, 1080p and 1440p with 0, 1, 5 and 10+ cards;
   test draw, discard, install, shuffle, HQ access and session recovery. Require
   no hidden-title leaks, no clipped actions, and a manual usability review.

This later hand component is planned, not part of the current local UI change.
