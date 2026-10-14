# Vantage Point primitive audit

Status: planning audit, 14 October 2026, reviewed in VP.1 (see "VP.1 review" below and the registry, which is the source
of truth), from the Vantage Point card
data at the pinned upstream commit (`236140cf84491c23995b028bc791281837520b97`, pack `vp`, 66 records, local-only).
Mechanics are summarized in our own words; no card text is reproduced. The plan is
[VANTAGE_POINT_PLAN.md](VANTAGE_POINT_PLAN.md).

The primitives below are what the engine lacks today, judged against its vocabulary after Elevation (59 instruction kinds
and 37 trigger occurrences, with the conditions, costs and static modifiers both card sets added) and the
[Elevation audit](ELEVATION_PRIMITIVES.md). Rows marked
"existing (to confirm)" look expressible today but may need a parameter the engine lacks; VP.1 settles each.

## Primitive families

### Bad publicity (slice VP.2)

| Id | What | Cards |
|---|---|---|
| `VP-BAD-PUBLICITY` | Bad publicity counters on the Corp (CR 10.6): the bad publicity fund filled when a run's server is announced and emptied when it ends (steps 6.9.1b and 6.9.6b), its credits spendable during the run; taking and removing bad publicity as effects and as costs; a "the first time each turn you take bad publicity" occurrence; bad publicity counters hosted on a card, where they have no effect. New to the engine: neither System Gateway nor Elevation has it. | Take a Dive, Kompromat, Editorial Division, Witch Hunt, Nihilo Agent, Grubber, Scapegoat, Luana Campos, Reanimation Protocol, Vulture Fund |

### Credit sources (slice VP.2)

| Id | What | Cards |
|---|---|---|
| `VP-STEALTH-CREDITS` | Credits tagged by their source: a cost payable only with credits from stealth cards, and stealth cards' hosted credits spendable during runs. Every credit payment learns where its credits come from. | Corsair, Lampades, Baker, Aircheck, Methuselah, Touchstone |
| `VP-POOL-LOCK` | While a card is active, the Runner can neither spend nor lose credits from their pool, only the card's hosted credits. | Aircheck |
| `VP-CREDITS-OUTSIDE-POOL` | An occurrence: the Runner spends credits from outside their pool during a run against a server. | Shackleton Grid |

### Scoring and agendas (slice VP.2)

| Id | What | Cards |
|---|---|---|
| `VP-SCORE-STEAL-COST` | Additional costs to steal an agenda (a click, credits) and to score one (a card moved to the score area). | Méliès City Luxury Line, Magistrate Revontulet, Word on the Street |
| `VP-CARD-AS-AGENDA` | A non-agenda card added to a score area as an agenda worth a printed number of points, with its own restrictions (can't be forfeited). | Word on the Street, Myōshu |
| `VP-AGENDA-POINTS-MODIFIER` | An agenda worth a different number of points in the opponent's score area. | Let Them Dream |
| `VP-SCORING-FACTS` | Turn facts: an agenda scored or stolen this turn, scored by this player this turn, installed this turn, and scored without being installed this turn. | Word on the Street, Witch Hunt, Hype Machine, Myōshu |
| `VP-ADVANCED` | An occurrence: a card is advanced (each time, with "the first time each turn"), not only from zero. | Sacrifice Zone Expansion |

### Costs and values (slice VP.2)

| Id | What | Cards |
|---|---|---|
| `VP-TRASH-AS-COST` | Trashing a card as a cost: an installed card of a kind, or a card of a kind from the grip. | Sell Out, Methuselah |
| `VP-PLAY-COST-MODIFIER` | A play cost lowered by a count (ICE protecting a server). | Tailgate |
| `VP-CLICK-COST-MODIFIER` | A click cost lowered: the first operation of a subtype each turn costs one click less. | Synchrocyclotron |
| `VP-DYNAMIC-REZ-COST` | A card's own rez cost lowered by a count (other unrezzed ICE) or by a condition (an agenda scored or stolen this turn). | Reverb, Hype Machine |
| `VP-AMOUNT` | Further dynamic amounts: a chosen card's printed rez cost; the remote servers with a card in their root and ICE protecting them. | realloc(), Flood the Market |
| `VP-RUN-FACTS` | Turn facts: successful runs on each central server this turn; ICE rezzed this turn. | Chain Reaction, Underdome Irregulars |
| `VP-UNINSTALL-INTERRUPT` | An interrupt when a card would be uninstalled, resolving before it leaves play. | Luana Campos |

### Runs and ICE (slice VP.4)

| Id | What | Cards |
|---|---|---|
| `VP-ICE-PASSED` | Occurrences: the Runner passes a piece of ICE, fully breaks it, or bypasses it; with facts for "the outermost ICE" and "fully broken". | Sipa, Vertigo, Lethe |
| `VP-ALL-ICE-STRENGTH` | A strength modifier on every piece of ICE: static while a card is installed, or lingering for the rest of the run. | The Tungsten Tailor, ezaM |
| `VP-SUBROUTINE-BROKEN` | An occurrence: the Runner breaks a subroutine, with the ICE's strength at the time. | The Tungsten Tailor |
| `VP-ICE-GAINS-SUBROUTINE` | ICE gains a subroutine, before its own, for the rest of an encounter. | Stick and Poke |
| `VP-CANNOT-STEAL-OR-TRASH` | For the rest of a run, the Runner can't steal or trash Corp cards: all of them, or copies of a named card. | Vertigo, Perfect Recall |
| `VP-UNLESS-JACK-OUT` | A subroutine the Runner prevents by jacking out. | Lionsmane |
| `VP-NO-SUCCESSFUL-RUN` | Runs against a server can't be declared successful (without becoming unsuccessful). | Flagship |
| `VP-ACCESS-LIMIT` | A cap on the cards the Runner may access during a run on a server, beyond one named card. | Flagship |
| `VP-ACCESS-PAID-ABILITY` | A Runner ability usable while accessing a card, paying the accessed card's printed rez or play cost. | Lampades |
| `VP-ENCOUNTER-ENDED` | An occurrence: an encounter with a piece of ICE ends. | Knowledge Seeker |
| `VP-ICE-VIRUS-COUNTERS` | Virus counters hosted on Corp ICE, removed by a purge it triggers itself. | Knowledge Seeker |

### Zones, information and identities (slice VP.6)

| Id | What | Cards |
|---|---|---|
| `VP-ARRANGE` | Arranging the top cards of a deck in any order (CR 8.3.3), after a private look. | Cultivate, Knowledge Seeker |
| `VP-HOST-FACEDOWN` | Cards from the grip hosted face down on a program, up to a limit, and shuffled back into the stack. | Read-Write Share |
| `VP-HOST-RESOURCES` | Resources of given subtypes installed onto a host resource, at a discount, with a static effect while both kinds are hosted. | Hackerspace |
| `VP-ARCHIVES-TURNED-FACEUP` | An occurrence: two or more face-down cards in Archives are turned face up at once. | Nurse Hạnh |
| `VP-ABILITY-FROM-HQ` | A paid ability usable from HQ, with revealing and trashing the card as its cost (Expendable). | Tocsin |
| `VP-SECRET-IDENTITY` | Méliès U: several copies of the identity brought to the game; at the end of each discard phase the Corp secretly sets which one is active; a successful central run flips it and resolves the side matching that server. The hidden choice is a new information channel. | Méliès U: Only the Brightest |

Every primitive lands with positive and negative engine tests, an observation test when hidden information is involved
(the bad publicity fund is public; stealth credit sources, a secret identity and face-down hosted cards are not all
public), and a serializable, replayable frame wherever it can yield, as in the M4 and Elevation audits.

## Cards

| Code | Title | Type | Batch | New primitives |
|---|---|---|---|---|
| 36001 | Chain Reaction | runner event | VP.5 | `VP-RUN-FACTS` |
| 36002 | Take a Dive | runner event | VP.3 | `VP-BAD-PUBLICITY` |
| 36003 | The Tungsten Tailor | runner hardware | VP.5 | `VP-ALL-ICE-STRENGTH`, `VP-SUBROUTINE-BROKEN` |
| 36004 | Corsair | runner program | VP.3 | `VP-STEALTH-CREDITS` |
| 36005 | Lampades | runner program | VP.5 | `VP-STEALTH-CREDITS`, `VP-ACCESS-PAID-ABILITY` |
| 36006 | Hackerspace | runner resource | VP.7 | `VP-HOST-RESOURCES` |
| 36007 | Nurse Hạnh | runner resource | VP.7 | `VP-ARCHIVES-TURNED-FACEUP` |
| 36008 | Stick and Poke | runner resource | VP.5 | `VP-ICE-GAINS-SUBROUTINE` |
| 36009 | Virtual Intelligence, P.I.: “You Can Call Me Vic” | runner identity | VP.3 | existing |
| 36010 | Kompromat | runner event | VP.3 | `VP-BAD-PUBLICITY` |
| 36011 | Sell Out | runner event | VP.3 | `VP-TRASH-AS-COST` |
| 36012 | Tailgate | runner event | VP.3 | `VP-PLAY-COST-MODIFIER` |
| 36013 | Borrowed Goods | runner hardware | VP.3 | existing |
| 36014 | Rotary | runner hardware | VP.5 | existing |
| 36015 | Baker | runner program | VP.5 | `VP-STEALTH-CREDITS` |
| 36016 | Underdome Irregulars | runner resource | VP.3 | `VP-RUN-FACTS` |
| 36017 | Hiram “0mission” Svensson: Shadow of the Past | runner identity | VP.7 | existing |
| 36018 | Aircheck | runner event | VP.5 | `VP-STEALTH-CREDITS`, `VP-POOL-LOCK` |
| 36019 | Beta Build | runner event | VP.5 | existing |
| 36020 | Methuselah | runner hardware | VP.3 | `VP-STEALTH-CREDITS`, `VP-TRASH-AS-COST` |
| 36021 | Touchstone | runner hardware | VP.3 | `VP-STEALTH-CREDITS` |
| 36022 | Read-Write Share | runner program | VP.7 | `VP-HOST-FACEDOWN` |
| 36023 | Sipa | runner program | VP.5 | `VP-ICE-PASSED` |
| 36024 | Stowaway | runner program | VP.3 | existing |
| 36025 | Word on the Street | runner resource | VP.3 | `VP-SCORE-STEAL-COST`, `VP-CARD-AS-AGENDA`, `VP-SCORING-FACTS` |
| 36026 | Méliès City Luxury Line | corp agenda | VP.3 | `VP-SCORE-STEAL-COST` |
| 36027 | Synchrocyclotron | corp asset | VP.3 | `VP-CLICK-COST-MODIFIER` |
| 36028 | Ansel 2.0 | corp ice | VP.5 | existing |
| 36029 | Reverb | corp ice | VP.5 | `VP-DYNAMIC-REZ-COST` |
| 36030 | Sleipnir | corp ice | VP.5 | existing |
| 36031 | Vertigo | corp ice | VP.5 | `VP-ICE-PASSED`, `VP-CANNOT-STEAL-OR-TRASH` |
| 36032 | Caveat Emptor | corp operation | VP.3 | existing |
| 36033 | realloc() | corp operation | VP.3 | `VP-AMOUNT` |
| 36034 | Retirement Plan | corp operation | VP.3 | existing |
| 36035 | Perfect Recall | corp upgrade | VP.5 | `VP-CANNOT-STEAL-OR-TRASH` |
| 36036 | Méliès U: Only the Brightest | corp identity | VP.7 | `VP-SECRET-IDENTITY` |
| 36037 | Lotus Haze | corp agenda | VP.3 | existing |
| 36038 | Esca | corp asset | VP.3 | existing |
| 36039 | ezaM | corp ice | VP.5 | `VP-ALL-ICE-STRENGTH` |
| 36040 | Knowledge Seeker | corp ice | VP.7 | `VP-ICE-VIRUS-COUNTERS`, `VP-ENCOUNTER-ENDED`, `VP-ARRANGE` |
| 36041 | Lionsmane | corp ice | VP.5 | `VP-UNLESS-JACK-OUT` |
| 36042 | Vicsek | corp ice | VP.5 | existing |
| 36043 | Cultivate | corp operation | VP.7 | `VP-ARRANGE` |
| 36044 | Unleash | corp operation | VP.5 | existing |
| 36045 | The Red Room | corp upgrade | VP.5 | existing |
| 36046 | Editorial Division: Ad Nihilum | corp identity | VP.3 | `VP-BAD-PUBLICITY` |
| 36047 | Witch Hunt | corp agenda | VP.3 | `VP-BAD-PUBLICITY`, `VP-SCORING-FACTS` |
| 36048 | Magistrate Revontulet | corp asset | VP.3 | `VP-SCORE-STEAL-COST` |
| 36049 | Nihilo Agent | corp asset | VP.3 | `VP-BAD-PUBLICITY` |
| 36050 | Grubber | corp ice | VP.3 | `VP-BAD-PUBLICITY` |
| 36051 | Lethe | corp ice | VP.5 | `VP-ICE-PASSED` |
| 36052 | Paywall | corp ice | VP.5 | existing |
| 36053 | Flood the Market | corp operation | VP.3 | `VP-AMOUNT` |
| 36054 | Scapegoat | corp operation | VP.3 | `VP-BAD-PUBLICITY` |
| 36055 | Hype Machine | corp upgrade | VP.3 | `VP-DYNAMIC-REZ-COST`, `VP-SCORING-FACTS` |
| 36056 | Sacrifice Zone Expansion | corp agenda | VP.3 | `VP-ADVANCED` |
| 36057 | Luana Campos | corp asset | VP.3 | `VP-BAD-PUBLICITY`, `VP-UNINSTALL-INTERRUPT` |
| 36058 | Event Horizon | corp ice | VP.5 | existing |
| 36059 | Flywheel | corp ice | VP.5 | existing |
| 36060 | Tocsin | corp ice | VP.7 | `VP-ABILITY-FROM-HQ` |
| 36061 | Myōshu | corp operation | VP.3 | `VP-CARD-AS-AGENDA`, `VP-SCORING-FACTS` |
| 36062 | Reanimation Protocol | corp operation | VP.3 | `VP-BAD-PUBLICITY` |
| 36063 | Vulture Fund | corp operation | VP.3 | `VP-BAD-PUBLICITY` |
| 36064 | Flagship | corp upgrade | VP.5 | `VP-NO-SUCCESSFUL-RUN`, `VP-ACCESS-LIMIT` |
| 36065 | Shackleton Grid | corp upgrade | VP.5 | `VP-CREDITS-OUTSIDE-POOL` |
| 36066 | Let Them Dream | corp agenda | VP.3 | `VP-AGENDA-POINTS-MODIFIER` |

**18 of 66 cards (27%) use only existing vocabulary**, against Elevation's 26% after its review. 33 primitives, 21 of
them used by one card: each is a design review for a more general form. Bad publicity (10 cards) and stealth credits (6)
are the two large families, and both reach into the run structure and the credit payment code every card uses.

| Batch | Cards | Using only existing vocabulary |
|---|---|---|
| VP.3 | 33 | 7 |
| VP.5 | 25 | 10 |
| VP.7 | 8 | 1 |

## VP.1 review

Every row was checked against CR 26.03 and the engine's vocabulary, and each card's citations, observation, hidden-
information and serialization notes are in `data/vantage-point/batch-registry.json`, with 31 rulings in
`data/vantage-point/rulings.json`, each cross-checked against jinteki.net's implementation of the card. The outcome:

- **The eight rows marked "to confirm" use existing vocabulary:** Rotary (Detente's any-player ability, Elevation's breach
  access), Hiram (install and card-move occurrences, a private look), Beta Build (search, and Topan's trigger on the card
  it installed), Ansel 2.0 (the bioroid break, installs from HQ or Archives), Sleipnir, Caveat Emptor (the next-turn
  allotment, to check a negative amount for the Runner in VP.3), Vicsek and The Red Room. The audit stands at 18 of 66.
- **Shackleton Grid** counts the bad publicity fund as credits from outside the Runner's pool, as jinteki.net does: its
  primitive needs `VP-BAD-PUBLICITY`'s fund to report its spending (VP.2 lands both).
- **Méliès U:** the card sets the active copy when the Corp's discard phase ends; jinteki.net chooses at the end of the
  Corp's turn. The card text is followed.

