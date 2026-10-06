# Elevation primitive audit

Status: planning audit, 2 October 2026, reviewed in EL.1 (see "EL.1 review" below and the registry, which is the source
of truth), from the Elevation card data at the pinned upstream commit
(`236140cf84491c23995b028bc791281837520b97`, pack `elev`, 82 records, local-only). Mechanics are summarized in our
own words; no card text is reproduced. The plan is [ELEVATION_PLAN.md](history/ELEVATION_PLAN.md); the machine-readable
assignment is `data/elevation/batch-registry.json`.

The primitives below are what the engine lacks today, judged against its vocabulary on 2 October (37 instruction kinds,
26 trigger occurrences, 13 condition kinds, 9 cost kinds, 2 static modifiers, plus the M4 primitive audit). Each is an
estimate to confirm in slice EL.1: some cards marked "existing" may need a parameter the engine lacks, and some primitives
may already be expressible.

## Primitive families

### Values and conditions (slice EL.2)

| Id | What | Cards |
|---|---|---|
| `EL-REMOVE-TAGS` | Removing tags as an effect ("remove up to N", "remove any number") and as a cost, including a variable X. Added by EL.1's review. | Lie Low, Synapse Global, Bigger Picture, IP Enforcement |
| `EL-OPERATION-TRASH-COST` | The basic trash ability on an accessed operation with a printed trash cost (CR 3.5.3). Added by EL.1's review. | IP Enforcement, Measured Response |
| `EL-AMOUNT` | Dynamic amounts: per click remaining, per tag removed, per counter removed, per credit the opponent lost, per hosted advancement counter, per matching card in a zone. | Ritual, Transfer of Wealth, Bigger Picture, Phat Gioan Baotixita, Idiosyncresis, Rising Tide |
| `EL-THREAT` | Threat level (the higher of the two scores) as a derived value; "if the threat level is X or greater" conditions and the Threat X keyword. | Public Access Plaza, Measured Response, N-Pot |
| `EL-TAG-COUNT` | A condition on the Runner's tag count (at least N). | Doomscroll |
| `EL-RUN-EVENT-ACTIVE` | A condition: a run event is active (the run was started by one). | Sang Kancil |
| `EL-ACTION-NOT-FINISHED` | A turn fact: whether the player has finished an action this turn. | Petty Cash |
| `EL-SUBROUTINE-RESOLVED-FACT` | A run fact: a subroutine resolved during this run. | Ryo "Phoenix" Ono |
| `EL-CONDITIONAL-STRENGTH` | ICE strength from the protecting-ICE count of its server. | Scatter Field |

### Counters, scoring and credits (slice EL.2)

| Id | What | Cards |
|---|---|---|
| `EL-DIVIDENDS` | Dividends: on score, agenda counters for each advancement counter beyond the requirement; abilities that spend hosted agenda counters at "your discard phase ends". | Project Ingatan, Sericulture Expansion, Embedded Reporting, Off the Books |
| `EL-SCORE-AREA-ABILITY` | A Corp ability usable from an agenda in either score area, including the Runner's. | Next Big Thing |
| `EL-FORFEIT` | Forfeiting an agenda: as a cost, as an alternative payment, and its "when you forfeit" occurrence; a Runner choice to spend a click instead of granting the Corp an extra allotted click. | Greenmail, Aggressive Trendsetting, Biawak, Plutus |
| `EL-RECURRING` | Recurring credits that refill before a turn, with spend restrictions (trash costs; rezzing in this server). | Azimat, Mahkota Langit Grid |
| `EL-REGION` | The region limit: one region per server. | Mahkota Langit Grid |
| `EL-PERSISTENT` | Persistent abilities that stay active through the access on which the card is trashed. | Mahkota Langit Grid |

### Timing occurrences (slice EL.2)

| Id | What | Cards |
|---|---|---|
| `EL-ACTION-PHASE-END` | "When your action phase ends" as an occurrence for triggers. | Nebula Talent Management, Cacophony |
| `EL-TAG-REMOVED` | "The first time each turn a tag is removed" as an occurrence. | Synapse Global |
| `EL-HAND-SIZE-DISCARD` | The cards discarded to reach maximum hand size, as an occurrence with those cards. | Magdalene Keino-Chemutai |
| `EL-INSTALLED-THAT-CARD-TRIGGER` | A delayed trigger on the card an ability installed. | Topan |
| `EL-REZ-DURING-RUN` | "When you rez this ICE during a run against this server" triggers. | Bumi 1.0, Flyswatter |

### Identity state (slice EL.2)

| Id | What | Cards |
|---|---|---|
| `EL-FLIP` | Double-sided identities: a flipped state, its conditions and the flip instruction, with both sides' abilities. | Nebula Talent Management, Dewi Subrotoputri |

### Card sources and zones (slice EL.4)

| Id | What | Cards |
|---|---|---|
| `EL-INSTALL-FROM-ARCHIVES` | Install a card from Archives (including face-down ones the Corp may look at), ignoring costs where printed. Generalize install sources rather than adding one primitive per zone. | Project Ingatan, Mycoweb |
| `EL-PLAY-FROM-ARCHIVES` | Play an operation from Archives, then remove it from the game. | Petty Cash, Plutus |
| `EL-INSTALL-FROM-HEAP` | Install a program from the heap; move a heap card to the bottom of the stack. | Scrounge |
| `EL-INSTALL-FROM-SCORE-AREA` | Install an agenda from the Runner's score area. | IP Enforcement |
| `EL-NESTED-PLAY` | Play an operation from inside another card's ability. | Humanoid Resources |
| `EL-HOSTED-FACEUP` | Cards hosted face up on hardware without being installed, playable or installable as if from the grip, with end-of-turn cleanup. | Bling, Detente, Madani |
| `EL-ANY-PLAYER-ABILITY` | A paid ability on a card that any player can use. | Detente |
| `EL-ACCESS-OUTSIDE-RUN` | Accessing a random HQ card outside a run (moved with Detente to EL.6/EL.7 on 2 October 2026). | Detente |
| `EL-FACEUP-AGENDA` | Agendas installed face up (inactive), and an access trigger on them. | BANGUN |
| `EL-MOVE-UPGRADE` | Move an installed upgrade to another server's root. | Mercia B4LL4RD |

### Information (slice EL.4)

| Id | What | Cards |
|---|---|---|
| `EL-LOOK-TOP` | A private look at the top N cards of a deck, then choices among them. | Poetri Luxury Brands, AU Co., MuslihaT |
| `EL-REVEAL-GRIP` | Reveal the opponent's grip and choose cards from it. | Touch-ups |
| `EL-REVEAL-SUBSET` | Reveal all but N cards of HQ. | Peer Review |
| `EL-REVEAL-ON-ACCESS` | A card that must be revealed while accessed in R&D. | Byte! |
| `EL-REVEAL-TRASH-COST` | An additional cost to reveal and trash cards from HQ. | Plutus |
| `EL-RANDOM-HQ-TRASH` | Reveal and trash cards from HQ at random. | Shred |
| `EL-SABOTAGE` | Sabotage N: the Corp trashes N cards of its choice from HQ and/or the top of R&D. | Cacophony |
| `EL-COPY-OF-ACCESSED` | A target filter: a copy of a card accessed during this run. | Charm Offensive |

### Runs and ICE (slice EL.6)

| Id | What | Cards |
|---|---|---|
| `EL-MOVE-RUNNER` | Move the Runner to the outermost position of another server during a run. | Proprionegation |
| `EL-CHANGE-ATTACKED-SERVER` | Change the attacked server when approaching it. | Maintenance Access |
| `EL-PREVENT-END-RUN` | Prevent the run from ending unless the Corp pays a cost. | Shred |
| `EL-BYPASS` | Bypass the encountered ICE. | Fransofia Ward |
| `EL-DEREZ` | Derez an installed Corp card. | Maglectric Rapid |
| `EL-SWAP-APPROACHED-ICE` | Swap the approached ICE with ICE from Archives or HQ. | Mitra Aman |
| `EL-BREAK-RESTRICTION` | A subroutine only a given breaker type may break. | Semak-samun |
| `EL-RESOLVE-OTHER-SUBROUTINE` | Resolve a subroutine of another rezzed ICE. | Mycoweb |
| `EL-ICE-GAINS-SUBTYPE` | A static effect giving the host ICE further subtypes. | Chromatophores |
| `EL-CANNOT-TRASH` | ICE the Runner cannot trash while rezzed. | Kessleroid |
| `EL-MOD-HOST` | Hardware hosted on an icebreaker, moving at turn start. | GAMEDRAGON Pro |
| `EL-STRENGTH-PERSISTENCE` | Strength increases that last the rest of the run. | GAMEDRAGON Pro |
| `EL-REZ-COST-PAYMENT` | An alternative payment for part of a rez cost (forfeiting an agenda). | Biawak |
| `EL-REZ-COST-MODIFIER` | A global modifier of every ICE's rez cost. | Fransofia Ward |
| `EL-ATTACKED-SERVER-TRASH-COST` | A paid ability whose cost trashes a rezzed card of a kind in the attacked server. | LEO Construction |

Every primitive lands with positive and negative engine tests, an observation test when hidden information is
involved, and a serializable, replayable frame wherever it can yield, as in M4's audit contract.

## Cards

| Code | Title | Type | Batch | New primitives |
|---|---|---|---|---|
| 35001 | Ryo "Phoenix" Ono: Out of the Ashes | runner identity | EL.7 | `EL-SUBROUTINE-RESOLVED-FACT` |
| 35002 | Topan: Ormas Leader | runner identity | EL.7 | `EL-INSTALLED-THAT-CARD-TRIGGER` |
| 35003 | Charm Offensive | runner event | EL.7 | `EL-COPY-OF-ACCESSED` |
| 35004 | Scrounge | runner event | EL.5 | `EL-INSTALL-FROM-HEAP` |
| 35005 | Shred | runner event | EL.7 | `EL-PREVENT-END-RUN`, `EL-RANDOM-HQ-TRASH` |
| 35006 | Bling | runner hardware | EL.5 | `EL-HOSTED-FACEUP` |
| 35007 | Gourmand | runner program | EL.3 | existing |
| 35008 | Hantu | runner program | EL.3 | existing |
| 35009 | Rising Tide | runner program | EL.3 | existing |
| 35010 | Cacophony | runner resource | EL.3 | `EL-SABOTAGE`, `EL-ACTION-PHASE-END` |
| 35011 | Rent Rioters | runner resource | EL.3 | existing |
| 35012 | Barry "Baz" Wong: Tri-Maf Veteran | runner identity | EL.7 | existing |
| 35013 | MuslihaT: Multifarious Marketeer | runner identity | EL.5 | `EL-LOOK-TOP` |
| 35014 | Clean Getaway | runner event | EL.3 | existing |
| 35015 | Lie Low | runner event | EL.3 | existing |
| 35016 | Maintenance Access | runner event | EL.7 | `EL-CHANGE-ATTACKED-SERVER` |
| 35017 | Transfer of Wealth | runner event | EL.3 | `EL-AMOUNT` |
| 35018 | Detente | runner hardware | EL.7 | `EL-HOSTED-FACEUP`, `EL-ANY-PLAYER-ABILITY`, `EL-ACCESS-OUTSIDE-RUN` |
| 35019 | Maglectric Rapid (748 Mod) | runner hardware | EL.7 | `EL-DEREZ` |
| 35020 | Sang Kancil | runner program | EL.3 | `EL-RUN-EVENT-ACTIVE` |
| 35021 | Fransofia Ward | runner resource | EL.7 | `EL-BYPASS`, `EL-REZ-COST-MODIFIER` |
| 35022 | Open Market | runner resource | EL.3 | existing |
| 35023 | Dewi Subrotoputri: Pedagogical Dhalang | runner identity | EL.3 | `EL-FLIP` |
| 35024 | Magdalene Keino-Chemutai: Cryptarchitect | runner identity | EL.5 | `EL-HAND-SIZE-DISCARD` |
| 35025 | Illumination | runner event | EL.3 | existing |
| 35026 | Ritual | runner event | EL.3 | `EL-AMOUNT` |
| 35027 | GAMEDRAGON Pro | runner hardware | EL.7 | `EL-MOD-HOST`, `EL-STRENGTH-PERSISTENCE` |
| 35028 | Madani | runner hardware | EL.5 | `EL-HOSTED-FACEUP` |
| 35029 | Azimat | runner program | EL.3 | `EL-RECURRING` |
| 35030 | Chromatophores | runner program | EL.7 | `EL-ICE-GAINS-SUBTYPE` |
| 35031 | Devadatta Drone | runner program | EL.3 | existing |
| 35032 | Principia | runner program | EL.3 | existing |
| 35033 | "Knickknack" O'Brian | runner resource | EL.3 | existing |
| 35034 | Side Hustle | runner resource | EL.3 | existing |
| 35035 | LEO Construction: Labor Solutions | corp identity | EL.7 | `EL-ATTACKED-SERVER-TRASH-COST` |
| 35036 | Poetri Luxury Brands: All the Rage | corp identity | EL.5 | `EL-LOOK-TOP` |
| 35037 | Aggressive Trendsetting | corp agenda | EL.3 | `EL-FORFEIT` |
| 35038 | Project Ingatan | corp agenda | EL.5 | `EL-DIVIDENDS`, `EL-INSTALL-FROM-ARCHIVES` |
| 35039 | Humanoid Resources | corp asset | EL.3 | `EL-NESTED-PLAY` |
| 35040 | Otto Campaign | corp asset | EL.3 | existing |
| 35041 | Bumi 1.0 | corp ice | EL.7 | `EL-REZ-DURING-RUN` |
| 35042 | Scatter Field | corp ice | EL.7 | `EL-CONDITIONAL-STRENGTH` |
| 35043 | Nanomanagement | corp operation | EL.3 | existing |
| 35044 | Top-Down Solutions | corp operation | EL.3 | existing |
| 35045 | Mercia B4LL4RD | corp upgrade | EL.7 | `EL-MOVE-UPGRADE` |
| 35046 | AU Co.: The Gold Standard in Clones | corp identity | EL.5 | `EL-LOOK-TOP` |
| 35047 | PT Untaian: Life's Building Blocks | corp identity | EL.3 | existing |
| 35048 | Proprionegation | corp agenda | EL.7 | `EL-MOVE-RUNNER` |
| 35049 | Sericulture Expansion | corp agenda | EL.3 | `EL-DIVIDENDS` |
| 35050 | Byte! | corp asset | EL.5 | `EL-REVEAL-ON-ACCESS` |
| 35051 | Phat Gioan Baotixita | corp asset | EL.3 | `EL-AMOUNT` |
| 35052 | Empiricist | corp ice | EL.7 | existing |
| 35053 | Mycoweb | corp ice | EL.7 | `EL-INSTALL-FROM-ARCHIVES`, `EL-RESOLVE-OTHER-SUBROUTINE` |
| 35054 | Semak-samun | corp ice | EL.7 | `EL-BREAK-RESTRICTION` |
| 35055 | Peer Review | corp operation | EL.5 | `EL-REVEAL-SUBSET` |
| 35056 | Mitra Aman | corp upgrade | EL.7 | `EL-SWAP-APPROACHED-ICE` |
| 35057 | Nebula Talent Management: Making Stars | corp identity | EL.3 | `EL-FLIP`, `EL-ACTION-PHASE-END` |
| 35058 | Synapse Global: Faster than Thought | corp identity | EL.3 | `EL-TAG-REMOVED` |
| 35059 | Embedded Reporting | corp agenda | EL.3 | `EL-DIVIDENDS` |
| 35060 | Next Big Thing | corp agenda | EL.3 | `EL-SCORE-AREA-ABILITY` |
| 35061 | Idiosyncresis | corp asset | EL.3 | `EL-AMOUNT` |
| 35062 | Public Access Plaza | corp asset | EL.3 | `EL-THREAT` |
| 35063 | Doomscroll | corp ice | EL.3 | `EL-TAG-COUNT` |
| 35064 | N-Pot | corp ice | EL.7 | `EL-THREAT` |
| 35065 | Bigger Picture | corp operation | EL.3 | `EL-AMOUNT` |
| 35066 | IP Enforcement | corp operation | EL.5 | `EL-INSTALL-FROM-SCORE-AREA` |
| 35067 | Touch-ups | corp operation | EL.5 | `EL-REVEAL-GRIP` |
| 35068 | BANGUN: When Disaster Strikes | corp identity | EL.5 | `EL-FACEUP-AGENDA` |
| 35069 | The Zwicky Group: Invisible Hands | corp identity | EL.3 | existing |
| 35070 | Greenmail | corp agenda | EL.3 | `EL-FORFEIT` |
| 35071 | Off the Books | corp agenda | EL.3 | `EL-DIVIDENDS` |
| 35072 | Anthill Excavation Contract | corp asset | EL.3 | existing |
| 35073 | Plutus | corp asset | EL.5 | `EL-PLAY-FROM-ARCHIVES`, `EL-FORFEIT`, `EL-REVEAL-TRASH-COST` |
| 35074 | Biawak | corp ice | EL.7 | `EL-FORFEIT`, `EL-REZ-COST-PAYMENT` |
| 35075 | Kessleroid | corp ice | EL.3 | `EL-CANNOT-TRASH` |
| 35076 | Syailendra | corp ice | EL.3 | existing |
| 35077 | Key Performance Indicators | corp operation | EL.3 | existing |
| 35078 | Measured Response | corp operation | EL.3 | `EL-THREAT` |
| 35079 | Flyswatter | corp ice | EL.7 | `EL-REZ-DURING-RUN` |
| 35080 | Lamplighter | corp ice | EL.3 | existing |
| 35081 | Petty Cash | corp operation | EL.5 | `EL-PLAY-FROM-ARCHIVES`, `EL-ACTION-NOT-FINISHED` |
| 35082 | Mahkota Langit Grid | corp upgrade | EL.3 | `EL-RECURRING`, `EL-REGION`, `EL-PERSISTENT` |

**23 of 82 cards (28%) use only existing vocabulary**, against 17% of Gateway's registered cards over its batches
(the E6 baseline in the plan). 51 primitives, most used by one card: each is a design review for a more general form.

## EL.1 review

Every row was checked against CR 26.03 and the engine's vocabulary (`card-vocabulary --json`, 200 items on Gateway's
77 cards). Each card's citations, observation and hidden-information and serialization notes are in the registry. The
corrections:

- **Rising Tide** counts fracters in the heap for its strength: `EL-AMOUNT` (a count of matching cards in a zone).
- **Lie Low**, **Synapse Global**, **Bigger Picture** and **IP Enforcement** remove tags as an effect or a cost:
  `EL-REMOVE-TAGS`.
- **IP Enforcement** and **Measured Response** carry trash costs, which the Runner may pay while accessing them
  (CR 3.5.3): `EL-OPERATION-TRASH-COST`. IP Enforcement's play cost is X.

That leaves 21 of 82 cards (26%) on existing vocabulary. The table above keeps the planning batches; the registry's
`newPrimitives` field is the reviewed list.

