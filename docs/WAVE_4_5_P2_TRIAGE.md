# Wave 4.5 P2: LLM review triage

These are the reviewer's findings on Corp v9 games (`standard-w4a`, corpus `corpus-w4a-vs-now`,
1 October 2026). The verdicts are provisional: a first pass by Claude, checked against the decision logs,
the board lines and the card data. The user's triage, which settles them, is pending.

The rounds are configurations (see `WAVE_4_5_PROGRESS.md`):
- **A:** the decisions only.
- **B:** adds each turn's board, the opponent's actions and the primer.
- **C:** adds the `history` tool.

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
| 6 | B | DA–SG 3 | 621 | Offworld Office installed behind one unrezzed Palisade | unsure | The ICE'd alternative meant trashing Nico Campaign |
| 7 | B | QD–PH 5 | 9 | Turn-one Ballista on an empty remote, centrals bare | confirmed (PF3) | The Runner ran R&D and HQ that turn |
| 8 | B | QD–PH 5 | 126 | Malapert Data Vault installed in R&D | confirmed (PF2) | |
| 9 | B | QD–PH 5 | 638 | Three new remotes with one ICE each and nothing in them | confirmed (PF1) | |
| 10 | B | QD–SG 2 | 364 | Malapert Data Vault installed in R&D | confirmed (PF2) | |
| 11 | B | QD–SG 2 | 180 | ICE spread over empty remotes | duplicate of 5 | |
| 12 | B | AY–SG 2 | 1194 | At 6 points with no agenda installed, advances a trap nobody runs instead of drawing | confirmed (new) | The only other options were draw, gain or an install |
| 13 | B | DA–PH 1 | 350 | Manegarm Skunkworks left unrezzed while the Runner runs its server | wrong | No run on that server after the install |
| 14 | B | DA–PH 1 | 514 | ICE installed on new empty remotes over several turns | confirmed (PF1) | |
| 15 | B | DA–PH 2 | 320 | Offworld Office installed over an Urtica Cipher with 4 counters | unsure | The only other remote had no ICE |
| 16 | B | DA–SG 2 | 833 | Manegarm Skunkworks left unrezzed while the Runner runs its server | wrong | No run on that server after the install |
| 17 | B | DA–SG 4 | 513 | Advancing traps while Hedge Fund and Government Subsidy sit unplayed | wrong | Those operations weren't playable at the time |
| 18 | C | AY–SG 2 | 1436 | A second Anoetic Void beside the first | confirmed (new) | Unique; now found by `unique-replaces-own-copy` |
| 19 | C | DA–PH 1 | 350 | As 13 | duplicate of 13 | Also wrong |
| 20 | C | DA–PH 2 | 8 | Turn-one Palisade on an empty remote, centrals bare | confirmed (PF3) | The Runner ran R&D and HQ that turn |
| 21 | C | DA–PH 2 | 692 | Remotes and traps built where the Runner never runs | unsure | The run counts are wrong, but the Runner did run no remotes |
| 22 | C | DA–SG 4 | 12 | HQ left without ICE for seven turns | confirmed (PF3) | The Runner ran HQ 11 times |
| 23 | C | DA–SG 2 | 223 | Urtica Cipher never rezzed, so its damage never fires | wrong | Ambush works unrezzed (9.1.8a) |
| 24 | C | DA–SG 2 | 491 | Offworld Office installed over an Urtica Cipher with 2 counters | unsure | As 15 |
| 25 | C | QD–PH 1 | 247 | Four new remotes with ICE and nothing in them | confirmed (PF1) | |
| 26 | C | QD–PH 1 | 60 | Sprint shuffles Government Subsidy away | unsure | At 9 credits, close to castable; the agenda shuffle is standard |
| 27 | C | QD–PH 2 | 295 | A second Malapert Data Vault beside the first | confirmed (new) | Now found by `unique-replaces-own-copy` (fixture `unique-beside-copy`) |
| 28 | C | QD–PH 2 | 662 | Offworld Office scored with Malapert unrezzed | confirmed (new) | Rez offered at 1 credit with 6 in the pool; v13+ rezzes first |
| 29 | C | QD–PH 3 | 144 | A second Malapert Data Vault in HQ, which trashes the first | confirmed (PF2) | |
| 30 | C | QD–PH 3 | 744 | R&D left behind one Whitespace all game | confirmed (PF3) | Pharos and Ballista offered for R&D with 9 to 22 credits |
| 31 | C | QD–PA 1 | 49 | Sprint shuffles Government Subsidy and an agenda away | unsure | As 26 |
| 32 | C | QD–PA 3 | 48 | Malapert Data Vault installed in Archives, twice | confirmed (PF2) | |
| 33 | C | QD–PA 5 | 136 | Malapert Data Vault installed in HQ | confirmed (PF2) | Remote 1 was offered at 110 |
| 34 | C | QD–SG 1 | 709 | Scored without first rezzing Malapert | wrong | 1 credit paid for the rez or the advance, not both |

The second look (`--check`) ran in round C's first five games:

| # | Second look | Right? |
|---:|---|---|
| 18 | refuted: "the new copy enters rezzed" | no |
| 19 | refuted: no run on the server | yes |
| 20 | refuted: "a reasonable strategic choice" | no |
| 21 | refuted: the run counts are wrong | arguably |
| 22 | refuted: "HQ was run twice" (it read the history only to #300) | no |
