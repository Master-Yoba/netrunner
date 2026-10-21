# Core Battle Box: Runner matchup guides

## 01 — PT Untaian Shell Game

**Scope:** Runner counterplay against the 49-card PT Untaian list pictured in Card Gamer’s *Introducing: The Netrunner Core Battle Box*. This is the first entry in a separate matchup document, intended to become a bot-policy supplement to the general strategy guide. Research checked 9 October 2026.

**My default plan:** build draw and economy, check inexpensive remotes while their advancement counts are low, and remove Phật Gioan Baotixita before farming agendas. Make dangerous runs early in the turn. Once Measured Response can be played, treat a successful run as a commitment to surviving the next Corp turn; sometimes the correct turn contains no successful run at all.

This is a damage-and-tempo deck with several different kill conditions. “Keep five cards” and “clear tags” are useful habits, but neither is a complete defense. Waiting indefinitely is also bad: PT Untaian and scored Sericulture Expansions can make unchecked remotes more dangerous without spending advancement clicks.

The numerical preferences below are proposed policy defaults, not measured win-rate claims. Exact card effects and damage arithmetic are distinguished from those preferences. Assume ordinary five-card maximum hand size, no damage prevention, and no special draw triggers unless stated otherwise. Adapt those assumptions to the actual Runner.

### The list this guide targets

The article warns that linked decklists can change. This guide is pinned to its [published deck image](https://cardgamer.com/wp-content/uploads/2026/07/PTU.png), which I inspected, rather than assuming the linked NetrunnerDB list is unchanged. NetrunnerDB’s live deck page was unavailable to the research fetch.

| Type | Cards |
|---|---|
| Identity | PT Untaian: Life’s Building Blocks |
| Agendas — 10 | 1 Longevity Serum; 3 Orbital Superiority; 3 Proprionegation; 3 Sericulture Expansion |
| Assets — 15 | 3 Byte!; 1 Clearinghouse; 3 Phật Gioan Baotixita; 2 Regolith Mining License; 3 Spin Doctor; 3 Urtica Cipher |
| Operations — 12 | 3 Hansei Review; 3 Hedge Fund; 2 Measured Response; 1 Neurospike; 3 Petty Cash |
| ICE — 12 | 3 Semak-samun; 1 Flyswatter; 3 Whitespace; 3 Empiricist; 2 Karunā |

All ten agendas are worth two points. There is no automatic identity damage on stealing: PT Untaian is not Personal Evolution. There is no Anoetic Void, Sting!, False Lead, or installed-card swapping effect in this snapshot. Do not import those threats from other Jinteki shell-game decks.

### What actually kills you

Read the following as separate threat channels. Removing one channel does not remove the others.

| Threat | What matters | Default Runner response |
|---|---|---|
| **Byte!** | On access outside Archives, the Corp can pay 4 to do 3 net damage and give a tag. It works from HQ and R&D as well as remotes. Trash cost 0. | Arrive with at least 3 cards for this packet alone; normally 4–5. Keep a click and 2 credits for its tag. Trash it after surviving. |
| **Urtica Cipher** | Accessing it **while installed** does `2 + advancement counters` net damage. Its access damage needs no Corp credits. Trash cost 2. | Check low-counter remotes early; stop treating an old advanced remote as an ordinary five-card run. Central accesses of Urtica do not cause its damage. |
| **Phật Gioan Baotixita** | First agenda scored or stolen each turn causes 1–3 net damage, depending on counters spent. Rez 1, trash 3. | Usually the first non-immediate threat to remove. Its presence changes the cost of every first agenda steal and of the next Corp score. |
| **Clearinghouse** | At the beginning of the Corp turn, it can trash itself to do meat damage equal to its advancement counters. Rez 0, trash 3; no tag needed. | A sufficiently advanced suspected Clearinghouse is urgent even if the Corp is broke. Ending the turn can be the losing action. |
| **Measured Response** | Cost 5. At threat 4+, after any successful run on your preceding turn: take 4 meat damage or pay 8 credits. Two copies; accessible copies have trash cost 3. | Budget cards **and** unrestricted credits after running. If you cannot survive the plausible sequence, take a recovery turn without a successful run. |
| **Orbital Superiority** | A 4/2 agenda. On scoring, it does 4 meat damage if you are already tagged; otherwise it gives a tag. | Remove access/ICE tags before ending your turn, especially with a scoreable remote. Stealing Orbital does not trigger this scoring ability. |
| **Neurospike** | Cost 3. Net damage equals the printed points the Corp scored **that turn**. One copy. | When the Corp can score and still play an operation, include another 2 damage for one agenda. Your preceding steal does not fuel it. |

Card text checked against the [Elevation](https://github.com/NetrunnerDB/netrunner-cards-json/blob/master/pack/elev.json) and [System Gateway](https://github.com/NetrunnerDB/netrunner-cards-json/blob/master/pack/sg.json) card-data files, supplemented by Null Signal’s [Measured Response article](https://nullsignal.games/blog/measured-response-a-tale-from-elevation-development/) and [Neurospike preview](https://nullsignal.games/blog/without-trace/). The recommendations are my matchup analysis.

### Grip targets: count at the damage window

**PT01 — Use the number of cards present when damage happens.** Playing a run event removes a card from grip. Installing a breaker before running also removes one. ICE damage happens before access damage. A damage-triggered draw cannot rescue you from a packet that already flatlines you.

Taking damage equal to your grip size survives with zero cards; taking more flatlines you. The “working target” column below adds one spare card, not a guarantee of surviving the whole turn.

| Isolated access threat | Exact minimum to survive that threat | Working target at access |
|---|---:|---:|
| Byte! | 3 | 4–5, plus tag recovery |
| Urtica, 0 advancements | 2 | 3 |
| Urtica, 1 advancement | 3 | 4 |
| Urtica, 2 advancements | 4 | 5 |
| Urtica, 3 advancements | 5 | 6 |
| Urtica, 4 advancements | 6 | 7 |
| First non-winning steal with Phật at 0 / 1 / 2+ counters | 1 / 2 / 3 | 2 / 3 / 4 |

**PT02 — Default to five cards for a blind run, but calculate the actual worst relevant branch.** Against an unadvanced remote, Byte! can cost three cards; against a two-advanced remote, Urtica can cost four. Those are alternative identities of one card, so do not add them together. In a central multiaccess, Byte! and an agenda can be different cards, so those damage packets can accumulate.

**PT03 — Prefer one dangerous run and recovery over two dangerous runs.** Start the run on click 1, or click 2 if drawing first is necessary. An unknown remote on click 4 is normally a bad choice: even a survivable hit leaves no ordinary draw or tag-removal actions. Override this for a winning run or an otherwise unavoidable loss, not merely for an extra access.

**PT04 — With basic draws, budget roughly one recovery click per card of damage.** A three-damage access consumes three basic draws to rebuild the hand. Byte! adds a tag-removal click and 2 credits. This is why a nominally free access may consume more than a whole turn’s resources. Reliable multi-card draw materially improves this matchup.

**PT05 — End near five cards after a damaging run, unless the calculated Corp turn requires more protection.** “I lived through access” is not a stopping criterion. If the remaining clicks cannot both clear a dangerous tag and restore the needed grip, the run was too late or began with too few cards. Drawing above five can protect an access during your turn, but ordinary end-of-turn discarding removes that surplus before the Corp acts.

**PT06 — Keep a functioning deck, not just a large grip.** Install the only affordable breaker needed for the imminent run before exposing it to random damage, then refill. Do not routinely install optional hardware or extra breakers at the cost of crossing a survival threshold. Damage discards are random: five cards containing your only killer and only draw engine are not five freely chosen “fodder” cards.

For a discard decision, normally retain a playable multi-card draw effect, useful economy, and any essential missing breaker/tutor. Discard redundant expensive setup pieces first. During your turn, however, even a redundant card is useful health; do not spend a click installing it merely to make the grip look efficient.

### When to run: firm defaults

**PT07 — Check accessible remotes at the first affordable, survivable opportunity.** For an unprotected unknown remote with 0–1 advancements, prefer checking on click 1 with five cards and money to trash an important asset. This denies future value and prevents the choice from becoming “six-damage Urtica or lethal Clearinghouse.” Do not require a full three-breaker rig for an unprotected server.

At two advancements, still lean toward an early check with five cards if access costs are low and the post-run plan survives. At three or more, leave the routine-check policy: calculate Urtica damage, draw above five if useful, and examine whether ignoring it loses to scoring or Clearinghouse. “Never run a highly advanced remote” is exploitable.

**PT08 — Spend 3 credits to trash an accessible Phật in preference to a speculative central access.** Allowing it two counter-building turns makes an otherwise ordinary agenda access cost three cards. Paying 3 now commonly avoids several future draw clicks. I would usually accept another 1–2 credits of ICE toll for this removal, provided the run and following Corp turn remain survivable. This 4–5-credit total is a starting preference, not a hard ceiling for a critical target.

Exceptions: take a winning agenda first; stop an imminent lethal Clearinghouse first; do not consume the credits required to survive an already-enabled Measured Response sequence. If Phật is unreachable, adjust every agenda-access budget instead of pretending the central servers are harmless.

**PT09 — Prefer safe young remotes to blind central farming when Phật is active.** Young remotes can contain the engine you need to remove. Central agenda accesses let that engine damage you. Once Phật is removed, cheap HQ/R&D pressure becomes much more attractive, especially while the Corp cannot fund Byte!.

**PT10 — Use low Corp credits to pressure; do not equate them with safety.** In this exact list, these are the relevant rez/payment thresholds:

| Corp credits available at the relevant window | What becomes affordable |
|---|---|
| 0 | Urtica access damage and rez of Clearinghouse; neither needs paid damage activation |
| 1 | Phật rez, producing even an uncharged one-damage agenda trigger |
| 2 | Whitespace or Flyswatter rez |
| 3 | Semak-samun rez |
| 4 | Karunā rez, or a Byte! payment |
| 7 | Empiricist rez |

These thresholds compete for the same credits. If the Corp spends down below 4 rezzing ICE, Byte! may cease to be live on that run. Recalculate after each spend. A Corp with 2 credits cannot rez the damaging sentries here, but can still stop the run with a code gate or leave you accessing Urtica. This makes an early probe more attractive, not automatically successful.

**PT11 — On a damaging sentry reveal, protect access capacity before protecting the credit pool.** Pay to break damage that would leave the intended access unsafe. If you cannot, abandon the run at the next legal opportunity. Do not assume you can jack out after seeing ICE but before its encounter effects; use the actual run timing.

**PT12 — Do not repeatedly attack a known Urtica.** If you accessed it and left it installed, remember that card’s identity while it remains the same object. This list cannot secretly turn it into an agenda. Paying its 2-credit trash cost during the original access is often convenient, but a second damaging run merely to tidy up that known trap is normally wasteful.

**PT13 — Multiaccess needs a combined damage budget.** With five cards, a live Byte! plus a charged Phật agenda trigger totals six damage and kills you, regardless of their order if neither access wins the game. Two funded Byte! accesses also total six. Do not make routine blind multiaccess into those live combinations. Remove Phật, exhaust the Corp’s Byte! funding, add grip/prevention, or select a smaller access plan. Do not assume you can stop a mandatory breach between accesses just because the first one hurt.

**PT14 — Use Archives when it has a reason to be valuable.** Byte! does not trigger there, and Urtica is not installed there. That makes Archives safer from access ambushes, but an agenda can still trigger Phật, and a successful Archives run still enables Measured Response. Prefer a cheap Archives run when unseen discards plausibly include an agenda; do not run an empty, known Archives merely because it is damage-free at access.

**PT15 — Take the winning steal if you can reach it alive.** Reaching at least seven agenda points wins at the checkpoint before Phật’s pending on-steal damage resolves. This deck normally takes the Runner from six to eight. Survive all earlier ICE/access damage; do not reject a winning agenda because of damage that would occur after victory. Conversely, on a non-winning access, you cannot normally decline an agenda just to avoid Phật.

### Measured Response changes the middle game

**PT16 — Recompute the end-turn budget whenever either player reaches four points, or the Corp can reach four before playing an operation.** Threat is the higher player score, not the sum. At 2–2 it is two, but a Corp score next turn can raise it to four before Measured Response is played.

This card does not require a tag or a stolen agenda. Trashing Phật, collecting a run reward, or successfully running Archives is sufficient to meet its previous-turn run condition. Failing a run does not meet it. If you have already made one successful run this turn, making no more runs does not switch it off.

**PT17 — Against a plausible single Response, four end-turn cards is the exact no-payment minimum; prefer five.** If you have five cards and there is no other damage in the sequence, usually take the four damage rather than pay 8: eight credits is a large price for avoiding roughly four basic draw clicks. Reverse that preference if more damage can follow, random loss would destroy the only practical winning line, or the preserved tempo is decisive.

The payment decision must consider the rest of the Corp turn. “Always take the first Response at five cards” is a losing policy against two copies.

| Response sequence, with **no other damage or draw** | Survival requirement |
|---|---|
| One Response; take damage | At least 4 cards |
| One Response; pay | At least 8 spendable credits |
| Two Responses; pay neither | At least 8 cards — ordinarily unavailable after discarding |
| Two Responses; pay one | At least 4 cards and 8 spendable credits |
| Two Responses; pay both | At least 16 spendable credits |

**PT18 — When two Responses are plausible, five cards plus eight credits is a useful working reserve, not a universal shield.** Pay one, absorb the other, retain one card. Additional Phật, Clearinghouse, Orbital, or Neurospike damage can invalidate this reserve. Restricted run/breaker credits do not pay an operation’s out-of-run demand unless their restriction actually permits it.

Do not demand this reserve from turn one or when both copies are accounted for outside possible access. It applies when the threat condition and likely Corp resources make the sequence credible. The bot should distinguish “possible somewhere in the deck” from “known in HQ,” while assigning a serious cost to betting its life against an affordable, unaccounted-for copy.

**PT19 — Project the Corp’s next-turn money and clicks, not just its current pool.** Two Responses directly cost 10 credits and two actions. At 5 credits, Hansei Review can supply the remaining money with the third action, provided its mandatory HQ trash can leave both Responses in hand. A loaded Regolith or replayable Petty Cash can also alter affordability. Conversely, three manual advancement actions leave no action for Response or Neurospike. Count actual legal sequences; do not add every damage card in the deck to one impossible turn.

**PT20 — If you cannot survive a credible Response line, prefer a recovery turn with no successful run.** Draw, gain money, install essential tools, then attack next turn with a viable ending position. This turns off Response’s run condition for that next Corp turn. It does not stop Clearinghouse or scoring damage: a recovery turn is available only if those threats also permit waiting.

### Phật, advances, and the next Corp turn

**PT21 — Track Phật’s counters precisely.** It gains one at the end of each **Corp** discard phase. Its damage is `1 + counters spent`, with at most two spent per trigger. It is unique, so three deck copies do not mean three simultaneous active damage engines.

After your steal consumes two counters, a Phật that started with two has zero: its next first-score trigger is then one damage, not three. If it started with four, two remain and the next trigger can still deal three. The first score/steal trigger is once per turn, so a Runner-turn steal and a Corp-turn score can both trigger it. Do not assume either permanent exhaustion or automatic full recharge.

**PT22 — Treat counter-rich Sericulture as an acceleration engine.** Sericulture is a 3/2 agenda. Overadvancing it gives agenda counters; at the end of a Corp discard phase it can spend one to place two advancements on an installed card. PT Untaian can separately add one to an unrezzed advanceable card when HQ has at most three cards, for 1 credit. Neither effect allows a score that turn.

A remote can therefore receive three advancements at the end of a Corp turn from these two abilities without three advancement actions. With multiple scored Sericultures, inspect all available triggers. This makes “I will wait one turn and see” costly. Use the counters actually present when your turn begins; do not add hypothetical manual advances to Clearinghouse’s beginning-of-turn damage.

**PT23 — Clear tags before a plausible Orbital score.** Paying one click and 2 credits removes a tag and can remove four damage from that score. This usually beats an optional economy or setup click. Still check the remaining kill line: an untagged Orbital gives a tag, and Phật/Neurospike/Response can damage you independently.

**PT24 — Clearinghouse is a deadline, not a reason to run recklessly.** If a remote could be Clearinghouse and its counters exceed your available end-turn grip, doing nothing can lose immediately next turn. If accessing it could instead be a lethal Urtica, draw enough before accessing, use relevant protection/information tools, or take a winning line elsewhere. Do not solve the fork by declaring the hidden card “probably an agenda.”

The one Clearinghouse copy limits this threat only while its location is genuinely accounted for. Spin Doctor and Longevity Serum can return discarded kill pieces to R&D. A card in Archives is not permanently gone.

### Which ICE to fear, and what to break

| ICE | Specific danger | Default |
|---|---|---|
| **Semak-samun** — rez 3, strength 3 barrier | Its printed subroutine can only be broken using a fracter. Otherwise pass by suffering 3 net damage, or let the run end. | Without a fracter, usually end the run. Taking three to reach an unknown card is a poor ordinary exchange. Generic AI breaking does not bypass its restriction. |
| **Karunā** — rez 4, strength 3 sentry | Two damage, then an option to jack out; another two damage if you continue into the second unbroken subroutine. | Break the damage. If forced to take the first two, usually use the offered exit unless the remaining line is both valuable and survivable. The exit comes after the first damage. |
| **Empiricist** — rez 7, strength 5 sentry | First sub draws a card and can put an HQ card atop R&D. Next does 1 net plus a tag; last does 2 net. | Prioritize stopping damage/tag for survival, but on an R&D run do not casually allow the first sub: it can arrange a Byte! access. Its high rez cost can exhaust Byte! funding; check the remaining credits. |
| **Whitespace** — rez 2, strength 0 code gate | Lose 3 credits, then end the run if you have at most 6. | Usually break rather than donate 3 and still bounce. Passing both unbroken requires starting with at least 10 credits and accepting the loss; that loss can destroy a Response reserve. |
| **Flyswatter** — rez 2, strength 0 code gate | Rezzing it during a run on its server purges virus counters; its sub ends the run. | Do not base the run’s affordability on virus counters surviving a possible rez. Breaking the subroutine does not stop the purge. |

**PT25 — A killer is valuable early, but money plus draw can be a better opening than a breaker-heavy hand.** This deck has five damaging sentries and three damage-taxing barriers, yet many early targets are unprotected. Keep a hand with usable economy and reliable draw even if it lacks a breaker, provided it can search/draw toward access tools. Do not keep three expensive breakers plus weak support just because the mulligan test sees “economy + breaker.”

My starting keep preference is **playable economy + efficient draw**, with a cheap killer/tutor as a strong bonus and a fracter next when barriers become relevant. Mulligan a hand whose first two turns spend nearly all credits installing tools while leaving no draw plan and a depleted grip. Exact card choices depend on the Runner list.

### Target priority and trade-offs

Use this ordering when several affordable actions are available; imminent victory or death overrides ordinary value.

1. **Win now, or stop the immediate loss.** A lethal Clearinghouse or the Corp’s winning score takes precedence over normal trash efficiency.
2. **Remove accessible Phật.** Its 3-credit trash cost is normally a better investment than another speculative access.
3. **Check a young unknown remote.** Especially one that can score or receive another round of advancement before your next opportunity.
4. **Trash an accessed Measured Response for 3 when its kill condition is relevant.** This is an unusually important trashable operation. At low threat and with scarce credits, preserving access/recovery money can be better.
5. **Trash a productive Regolith for 3.** Prefer this while several 3-credit withdrawals remain. Against one last withdrawal, spending a run and 3 to deny that withdrawal is usually poor unless it breaks a concrete kill/scoring budget.
6. **Attack productive centrals or force a valuable defensive expenditure.** Choose HQ when observed agenda retention makes it attractive; choose R&D for fresh cards and future agenda denial. Neither is automatically better in every board state.
7. **Trash Spin Doctor when you can actually deny value.** Its rez draw happens before trashing it, and an active Doctor can remove itself and recycle cards before you reach it. Do not spend an expensive run assuming both effects will be prevented.

**PT26 — Do not repeatedly buy the same low-value information.** Avoid reaccessing a known harmless R&D top card without another reward or a relevant state change. Avoid treating low HQ size as proof of agenda density: the identity encourages unloading cards, but the remaining hand may be operations and ambushes. Use observed draws, installs, discards, and prior accesses to update the belief.

**PT27 — Make Proprionegation spend its token before your critical run.** A scored copy can spend its agenda counter during a run to move you to the outermost position of Archives. A single “must hit this remote now” run may be redirected. With a token live, prefer an early meaningful attack that forces the Corp to spend it, leaving time and money for the real follow-up. If it preserves the token, you should still get value from the first target. Account for Archives ICE and for a redirected run becoming successful and enabling Response.

### Worked decisions

**A. Unprotected, one-advanced remote; five cards; Corp on 3 credits; your first click.**

Default: check it if you can afford a relevant trash and the following turn is survivable. Byte! cannot be funded at the access window without additional credit effects; Urtica would do three, leaving two. Three ordinary draws could refill to five. This is usually better than allowing another advancement cycle. Do not reuse the same conclusion on click 4.

**B. Byte! on a first-click run; five cards; one tag received.**

You have two cards afterward. Removing the tag and drawing twice ends with four cards, not five. That survives one standalone Response exactly, but fails against further damage. Decide before the run whether four at turn end is enough, or arrange more initial grip / stronger draw. “Run first” provides recovery time; it does not conjure enough recovery for every outcome.

**C. Two-advanced unknown remote; five cards; first click.**

Urtica would leave one card. Three basic draws finish at four. Default: a reasonable check when it denies an important score or escalating threat and four cards survive the next turn; a poor check if a live damage combination makes that ending position lethal. Six at access would let three draws rebuild to five.

**D. Five cards and 8 credits after a successful run; two Responses plausible.**

With no other damage, pay one and take one, ending at one card and zero credits. Taking both kills you. If a one-counter Clearinghouse fires first, the same line ends at zero and still survives; at two counters it kills you. The 8-credit reserve is therefore conditional on the rest of the board.

**E. Untagged, five cards; Corp can score one agenda; charged Phật and Neurospike available.**

Phật at two or more remaining counters can do three, and Neurospike adds two: five total, surviving at zero. Add even one Clearinghouse damage first and the line becomes lethal. If the agenda is Orbital and you start tagged, its four additional damage makes the position much worse. Count the advancement actions and operation action before declaring the sequence available.

**F. A suspected Clearinghouse has four advancements; you have five cards and no prevention.**

Leaving it costs at most four from that card alone, but follow-up damage may kill you. Accessing an Urtica at the same counter count costs six and immediately kills you. Default: do not run from five. Draw to at least six at access, preferably seven, and check whether the remaining clicks support recovery; otherwise remove another component of the kill or pursue a winning line. This is a concrete fork to solve, not an instruction to always run or always ignore.

### Adjustments for the Battle Box Runners

These are matchup knobs, not new base rules.

| Runner plan | Adjustment against PT Untaian |
|---|---|
| **Topan — self-damage for efficient installs** | An identity install removes the installed card from grip and deals one meat damage: ordinarily two fewer cards before any draw triggers. Do the full sequence before approving a run. If the 2-credit discount forces a recovery click or creates a lethal window, skip the discount. Prioritize a functioning draw engine; account for triggered draw only at its real timing. |
| **MuslihaT — frequent runs and efficient pressure** | Keep the cheap-remote pressure, but drop the assumption that every turn must contain a successful run. At threat 4+, a small run reward may create a large Response liability. Value access to the right breaker; do not rely on virus support surviving Flyswatter’s rez. |
| **Magdalene — building a large, efficient rig** | Keep economy/draw hands and build the tools that address the actual ICE. Interrupt setup to trash accessible Phật and check young remotes. Waiting for the complete rig can let advancement engines create an already-losing board. Cheap unprotected attacks do not require the late-game rig. |

### Mistakes to penalize in the Runner policy

Severity assumes no forced-loss situation and no immediately winning alternative justification.

| Severity | Detectable mistake |
|---|---|
| **Critical** | Entering a known damage window with less grip/prevention than its unavoidable damage. |
| **Critical** | Ending a successful-run turn where a known affordable Response sequence kills you, while a safe alternative existed. |
| **Critical** | Ending tagged into an affordable Orbital kill when removing the tag would prevent it. |
| **Critical** | Ignoring a known lethal Clearinghouse while a survivable removal or winning line existed. |
| **High** | Blind multiaccess with five cards into a live six-damage combination, without a deliberate necessary gamble. |
| **High** | Paying for an optional install or trash that crosses below a required Response-payment threshold. |
| **High** | Taking three from Semak-samun, or four from Karunā, then entering an unknown access with insufficient grip. |
| **High** | Repeatedly leaving an affordable Phật alive while paying its damage on non-winning agenda steals. |
| **High** | Planning a single critical remote run while an unused Proprionegation counter can redirect it. |
| **Medium** | Late-click blind runs whose common survivable outcomes leave an unsafe end-turn grip/tag state. |
| **Medium** | Spending several turns completing an unnecessary rig while safe young remotes become dangerous. |
| **Medium** | Spending clicks on optional setup while below the next run’s grip threshold. |
| **Medium** | Re-running an identified Urtica, or the same known R&D non-agenda, without a concrete benefit. |

### Corp mistakes the Runner should punish

| Observed Corp mistake | Runner’s default exploitation |
|---|---|
| Phật left accessible for a cheap run | Trash it before starting agenda pressure. |
| Important asset or plausible agenda left in a low-counter, affordable remote | Check promptly with a recovery budget. |
| Corp spends below Byte!’s 4-credit payment threshold | Increase cheap central pressure while still respecting Phật and ICE. |
| Corp overinvests in a remote and leaves a productive central cheap | Take fresh central accesses rather than paying the remote’s entire toll. |
| Regolith is exposed with several withdrawals remaining | Pay 3 to remove it when doing so preserves survival and blocks future funding. |
| Corp gives away an agenda while the Runner is on six points | Take the winning steal; Phật is not post-victory protection. |
| Corp spends its last Proprionegation token on a modest first attack | Use remaining clicks for the target it can no longer redirect. |
| Corp invests more counters in a Urtica you already identified | Leave it alone and attack elsewhere; those counters do not create a new mystery. |
| Corp relies on Response without a competing score/start-turn threat | Recover without a successful run, denying its play condition while improving your next attack. |

### Policy integration

Track at least: grip at each window; remaining recovery clicks; unrestricted end-turn credits; tag count; successful-run flag; both scores; each remote’s counters and remembered identity; Phật counters and first-agenda-event status; Proprionegation tokens; Sericulture counters; affordable Corp scoring/operation sequences; and observed locations of the single Clearinghouse/Neurospike and two Responses.

Use a two-stage decision:

1. **Check survival through the run and the following Corp turn.** Enumerate feasible sequences rather than summing incompatible threats. Preserve uncertainty about hidden cards; use this known decklist for possibilities, not the game engine’s hidden identities.
2. **Among viable actions, prefer early cheap checks, engine removal, and fresh agenda pressure.** Price ordinary damage at roughly one basic draw click per card, then adjust for actual draw engines and loss of essential cards. Carry an explicit Response cash reserve only when the board makes it relevant.

Do not hard-ban every action with any hidden-card risk. Distinguish a forced lethal line, an affordable plausible kill, and a remote low-probability combination. The point of these defaults is to avoid routine preventable deaths while still attacking before the shell game grows out of control.

### Research notes

- [Kenny Suzuki, *Introducing: The Netrunner Core Battle Box*](https://cardgamer.com/features/introducing-the-netrunner-core-battle-box/) — deck selection and published list image. The guide targets this snapshot, not every PT Untaian build.
- [NetrunnerDB card-data repository: Elevation](https://github.com/NetrunnerDB/netrunner-cards-json/blob/master/pack/elev.json) and [System Gateway](https://github.com/NetrunnerDB/netrunner-cards-json/blob/master/pack/sg.json) — costs, counters, ICE and damage wording, accessed as raw JSON.
- [Null Signal Games, *Measured Response: A Tale from Elevation Development*](https://nullsignal.games/blog/measured-response-a-tale-from-elevation-development/) — final printed Response text. The article also contains earlier designs; those are not used here.
- [Null Signal Games, *Without a Trace*](https://nullsignal.games/blog/without-trace/) — Neurospike’s printed-points restriction and scoring-turn dependence.
- [SanSan South: Byte!](https://sansansouth.com/en/card/35050) and [Phật Gioan Baotixita](https://sansansouth.com/en/card/35051) — independent text checks for the central access and agenda-trigger threats.
- [Stimhack, *Deconstructing Shellgame*, by tehepicwin](https://stimhack.com/deconstructing-shellgame-by-tehepicwin/) — useful historical discussion of early checks and recovery. Its older Personal Evolution list is not this deck; its card-specific advice and matchup results are not transferred.
- [Null Signal Comprehensive Rules](https://rules.nullsignal.games/) — consulted only for strategy-changing timing: threat level, damage survival, mandatory stealing, victory checkpoints, tags and hand-size timing. This document is a strategy guide, not a replacement rules reference.

Thresholds labeled as defaults, trash priorities, mulligan preferences and action penalties are recommendations to test in bot play. Damage totals and affordability constraints should instead be validated directly against the game state and card implementations.
