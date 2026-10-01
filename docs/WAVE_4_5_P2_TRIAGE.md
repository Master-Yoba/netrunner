# Wave 4.5 P2: LLM review triage

These are the reviewer's findings on Corp v9 games (`standard-w4a`, corpus `corpus-w4a-vs-now`,
1 October 2026). The verdicts are the user's triage. Claude's first pass checked each finding against the
decision logs, the board lines and the card data; the user changed findings 6, 15, 17, 21, 24, 26 and 31
and added the notes on 13 and 34.

The rounds are configurations (see `WAVE_4_5_PROGRESS.md`):
- **A:** the decisions only.
- **B:** adds each turn's board, the opponent's actions and the primer.
- **C:** adds the `history` tool.

Verdicts:
- **confirmed:** the bot's play was a mistake.
- **wrong:** a fact in the claim is false, or the play was fine.
- **duplicate:** the same fault was already reported in that game.

"PF" marks one of the 28 September playtest's faults:
1. ICE on empty remotes.
2. Malapert Data Vault in a central.
3. Centrals left open.

The game names are the Corp deck and the Runner deck, with the game number:
- **QD:** Quick and Dirty.
- **DA:** Discretion Advised.
- **AY:** Advanced Yomi.
- **PH:** Party Hard.
- **SG:** Stolen Goods.
- **PA:** Planning Ahead.

| # | Round | Game | Seq | Claim, in short | Verdict | Evidence |
|---|---|---|---:|---|---|---|
| 1 | A | DA–SG 3 | 59 | Ansel installed into a new remote while Nico Campaign sat in its own remote without ICE | confirmed (PF1) | The Runner trashed the Nico that turn |
| 2 | A | DA–SG 3 | 54 | Nico Campaign left unrezzed until turn 11 | wrong | That Nico was trashed on turn 2; the rez was held for Brân |
| 3 | A | QD–PH 5 | 332 | Gain 1 credit chosen over rezzing Ballista | wrong | ICE is rezzed when approached; nobody approached it |
| 4 | A | QD–SG 2 | 367 | A second Ballista breaks uniqueness | wrong | Ballista isn't unique |
| 5 | A | QD–SG 2 | 370 | Tithe installed into a new empty remote | confirmed (PF1) | The turn lines show the sprawl |
| 6 | B | DA–SG 3 | 621 | Offworld Office installed behind one unrezzed Palisade | confirmed (new) | The Runner could break in at once (breaker and credits). Remote 2 wasn't the play either: it was breakable with a click for a credit. Better: hold the agenda and draw for strong ICE or for a card to double-install as a bluff |
| 7 | B | QD–PH 5 | 9 | Turn-one Ballista on an empty remote, centrals bare | confirmed (PF3) | The Runner ran R&D and HQ that turn |
| 8 | B | QD–PH 5 | 126 | Malapert Data Vault installed in R&D | confirmed (PF2) | |
| 9 | B | QD–PH 5 | 638 | Three new remotes with one ICE each and nothing in them | confirmed (PF1) | |
| 10 | B | QD–SG 2 | 364 | Malapert Data Vault installed in R&D | confirmed (PF2) | |
| 11 | B | QD–SG 2 | 180 | ICE spread over empty remotes | duplicate of 5 | |
| 12 | B | AY–SG 2 | 1194 | At 6 points with no agenda installed, advances a trap nobody runs instead of drawing | confirmed (new) | The only other options were draw, gain or an install |
| 13 | B | DA–PH 1 | 350 | Manegarm Skunkworks left unrezzed while the Runner runs its server | wrong | No run on that server after the install, and Manegarm needn't be rezzed before one. The real fault is different: that strong server should have been used for scoring |
| 14 | B | DA–PH 1 | 514 | ICE installed on new empty remotes over several turns | confirmed (PF1) | |
| 15 | B | DA–PH 2 | 320 | Offworld Office installed over an Urtica Cipher with 4 counters | wrong | The play was fine: an Urtica Cipher with 4 counters is an obvious trap that would never trigger, and the server is behind a Palisade the Runner can't break. Separate faults: obvious Urtica plays, and over-investment in Urtica |
| 16 | B | DA–SG 2 | 833 | Manegarm Skunkworks left unrezzed while the Runner runs its server | wrong | No run on that server after the install |
| 17 | B | DA–SG 4 | 513 | Advancing traps while Hedge Fund and Government Subsidy sit unplayed | confirmed (new) | A poverty loop: from turn 6 it gains 1 credit and spends it on Urtica advancements, so the Hedge Fund it holds from turn 4 is never affordable. First marked wrong; changed in the user's triage |
| 18 | C | AY–SG 2 | 1436 | A second Anoetic Void beside the first | confirmed (new) | Unique; now found by `unique-replaces-own-copy` |
| 19 | C | DA–PH 1 | 350 | As 13 | duplicate of 13 | Also wrong |
| 20 | C | DA–PH 2 | 8 | Turn-one Palisade on an empty remote, centrals bare | confirmed (PF3) | The Runner ran R&D and HQ that turn |
| 21 | C | DA–PH 2 | 692 | Remotes and traps built where the Runner never runs | confirmed (PF3) | Tithe is a tax, not protection: R&D was effectively open all game, and the Corp should have protected it. The run counts in the claim are wrong |
| 22 | C | DA–SG 4 | 12 | HQ left without ICE for seven turns | confirmed (PF3) | The Runner ran HQ 11 times |
| 23 | C | DA–SG 2 | 223 | Urtica Cipher never rezzed, so its damage never fires | wrong | Ambush works unrezzed (9.1.8a) |
| 24 | C | DA–SG 2 | 491 | Offworld Office installed over an Urtica Cipher with 2 counters | wrong | As 15 |
| 25 | C | QD–PH 1 | 247 | Four new remotes with ICE and nothing in them | confirmed (PF1) | |
| 26 | C | QD–PH 1 | 60 | Sprint shuffles Government Subsidy away | confirmed (new) | Shuffling Government Subsidy away one credit short of casting it makes no sense. Keeping the agenda was fine: HQ behind Palisade and Remote 1 behind Ballista both end the run, the Runner has no breakers and few credits, and Remote 1 was set to score next turn |
| 27 | C | QD–PH 2 | 295 | A second Malapert Data Vault beside the first | confirmed (new) | Now found by `unique-replaces-own-copy` (fixture `unique-beside-copy`) |
| 28 | C | QD–PH 2 | 662 | Offworld Office scored with Malapert unrezzed | confirmed (new) | Rez offered at 1 credit with 6 in the pool; v13+ rezzes first |
| 29 | C | QD–PH 3 | 144 | A second Malapert Data Vault in HQ, which trashes the first | confirmed (PF2) | |
| 30 | C | QD–PH 3 | 744 | R&D left behind one Whitespace all game | confirmed (PF3) | Pharos and Ballista offered for R&D with 9 to 22 credits |
| 31 | C | QD–PA 1 | 49 | Sprint shuffles Government Subsidy and an agenda away | confirmed (new) | As 26 |
| 32 | C | QD–PA 3 | 48 | Malapert Data Vault installed in Archives, twice | confirmed (PF2) | |
| 33 | C | QD–PA 5 | 136 | Malapert Data Vault installed in HQ | confirmed (PF2) | Remote 1 was offered at 110 |
| 34 | C | QD–SG 1 | 709 | Scored without first rezzing Malapert | wrong | 1 credit couldn't pay for both. The underlying fault is the budget: the Corp should have planned to have the rez ready |

The second look (`--check`) ran in round C's first five games:

| # | Second look | Right? |
|---:|---|---|
| 18 | refuted: "the new copy enters rezzed" | no |
| 19 | refuted: no run on the server | yes |
| 20 | refuted: "a reasonable strategic choice" | no |
| 21 | refuted: the run counts are wrong | no: the counts were wrong, but R&D was effectively open |
| 22 | refuted: "HQ was run twice" (it read the history only to #300) | no |

## Faults the triage named

These go beyond the findings themselves, and are candidates to check against Corp v19:
- **Agenda placement (6):** an agenda isn't installed in a remote the Runner can break into at once. Hold
  it and draw for ICE, or bluff with a double install.
- **A strong remote goes unused for scoring (13).**
- **Urtica Cipher (15, 17, 24):**
  - an obvious advanced trap is worth nothing;
  - the bot over-invests in it;
  - in a poor Corp, the advancements starve its economy.
- **R&D left behind only Tithe, which taxes the Runner but doesn't stop it (21).**
- **Sprint (26, 31):** it shuffles away an operation the Corp is about to afford. An agenda can stay in
  hand when the scoring remote is ready.
- **Budget (34):** credits aren't kept for the rez the Corp will need, such as Malapert before scoring.
