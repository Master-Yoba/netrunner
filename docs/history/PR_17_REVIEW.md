# Consolidated review of PRs 14–17

PR 17 contains the complete branches from PRs 14, 15 and 16. The user requested
committing the local editor settings, applying valid feedback on this branch,
closing the older PRs and merging PR 17 into `dev`.

| Feedback | Assessment and disposition |
| --- | --- |
| PR 14 | No review findings or discussion comments. |
| PR 15 / PR 17: Archives total | Valid. Visible card projections cannot supply the total when Runner cannot see facedown cards; carry a separate public count and render concealed remainder without identities. |
| PR 16: release card backs | Valid against the existing source notice. Both release export presets exclude `card-backs/*`; missing packaged backs fall back without resource-load errors. Development/CI assets remain available. |
| PR 16: reference screenshots | Valid against the repository's local-only artwork rule. Both reference screenshots are removed from tracking, preserved locally and ignored; documentation retains source links and the written visual analysis. No history rewrite. |
| PR 16: recovery policy version | Valid: recorded policy compatibility must be checked. The separate claim that `FullGameContractAgent.AgentVersion` does not exist is incorrect on this branch; its working lookup is retained. |
| PR 16 / PR 17: unknown actor rejection | Valid. Presentation/history context must not throw before the engine can reject an unknown actor. Rejection must leave the coordinator and trace usable. |
| PR 17: encounter fingerprint | Valid. A subroutine break can change the rendered encounter marker without changing the old fingerprint. Include the encounter fields used by the UI. |

The previous PR 16 official CI failure was a full-match smoke wall-time timeout
at 1,495 actions, not a failing rules assertion. Both PR 17 lanes had subsequently
passed; updated checks must pass on the final commit before merging.

Verification: six focused Match regressions passed, including both Archives
viewer projections and M1 shape preservation. Native UI checks passed for both
seats at 1920×1080 and 2560×1440, with new assertions for concealed Archives
placeholders and encounter-only marker refresh. Both exported resource archives
were inspected and contain neither card-back sources nor imported textures.
The complete local gate and final-commit CI are required before merge.
