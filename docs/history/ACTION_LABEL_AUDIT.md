# Action label audit

Reviewed the issued action families, the card-table dock, contextual board choices,
and target confirmations after the Cleaver install report.

| Area | Problem | Result |
| --- | --- | --- |
| Run events | Every Jailbreak destination said only “Play Jailbreak” | Card, run server, and credit cost appear together |
| Discard / memory | Generic “Discard card” or technical action prefix | Card name and discard/trash consequence |
| Identical copies | Same title produced identical choices | Visible zone and position distinguish copies |
| Pile targets | Known cards outside the rendered board became “face-down card” | Names resolve from the complete viewer-safe view |
| Hosted installs | Host shown only in the contextual panel | Dock and confirmation also name the host |
| Corp installs | Root and ICE destinations looked alike | Destination includes the server and installation zone |
| Advance / score / purge | Generic labels omitted target, reward, or click cost | Typed target, agenda points, and costs are visible |
| Paid abilities | Internal ability key; costs and target omitted | Readable effect, source, target, full costs, and once-per-turn limit |
| Additional costs | Card combinations were difficult to compare | Each chosen trash-cost card is named |
| Mode choices | “gain”, “draw”, or “gain-first” omitted amount and recipient | Catalog declarations supply recipient, amount, and effect order |
| Partial selections | Adding a card and committing a selection looked similar | “Add to selection”, “Finish selection”, and empty choices are distinct |
| Trigger ordering | Opaque trigger identifiers | Visible source card and readable trigger key |
| Draw replacement | Generic “Use replacement” | Visible source and replacement draw count |
| Breaker actions | Generic labels could lose source/payment distinctions | Breaker source and exact existing payment split are retained |

Access continuation remains neutral: the engine can automatically steal an agenda
after the paid window closes. The label must not promise “without stealing”. Run,
ICE-subroutine, bioroid, access-trash, tag-removal, prevention, and replacement
choices retain their issued quantities, targets, and payment distinctions.

`ActionLabelPresenter` supplies the same labels to the table, board-choice panel,
and confirmation prompt. Effect summaries describe supported typed catalog
instructions; unsupported declarations fall back to readable keys or the issued
description instead of guessing effects. Trigger/interrupt source metadata is
provided only to the decision owner and only when that source card is visible.
It is excluded from serialized observations, like existing decision metadata.
No issued IDs, authoritative state, replay hashes, or card assets change.

Regression coverage includes source visibility and observation serialization,
mode recipients and effect order, all additional costs, unknown targets, hosted
installs, selection completion, and exact breaker payment splits. Native UI checks
at 1920×1080 and 2560×1440 cover destination labels, confirmation, same-named
discard copies, and known/concealed pile targets. `--action-label-screenshots`
captures the run and discard examples through the interaction smoke; use an empty
artwork cache for repository-safe screenshots.
