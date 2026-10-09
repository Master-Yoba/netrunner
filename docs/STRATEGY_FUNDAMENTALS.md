# Netrunner strategy fundamentals for bot policy design

Research date: 8 October 2026

Revision 2: 9 October 2026 — actionable defaults, spending reserves, and graded mulligans.

This guide describes how to choose plays, allocate resources, and exploit mistakes in asymmetric Netrunner. It is intended for later translation into a bot policy, rather than as a rules reference. It covers general constructed play; archetype modules require the relevant cards to exist in the configured card pool. It is not a claim about the current Standard metagame or ban list.

The main principle is to spend resources to improve the chance of winning within the relevant time horizon. Credits, cards, accesses, installed ICE, and even agenda points are intermediate measures. Their value changes when either side is near a win or a temporary opportunity is about to disappear.

## How to interpret the recommendations

- **[B] Base:** a default consideration across decks. It still has exceptions.
- **[M] Matchup:** enable or adjust using evidence about the opponent's capabilities.
- **[A] Archetype:** depends on your own deck's plan.
- **[T] Tuning:** a proposed starting approximation, not a measured optimal threshold.

Rules below are preferences over legal candidate plans, not unconditional commands. A legal win takes priority over economy; a necessary attempt to avoid defeat can take priority over normal risk limits. Evaluate combinations of actions across a turn, not just isolated clicks.

**Policy stance.** My recommendation is to start with an active Runner and a Corp that converts openings into scores promptly. Prefer making a useful play now over preserving flexibility indefinitely. The defaults below commit to a choice when deeper search has no strong answer. Values marked [T] are my proposed starting settings, not tournament statistics or self-play results. Exact tactical wins, survival requirements, and card effects override them. Sources are retained at the end for traceability; they are not authorities for the proposed numerical settings.

### State the policy should use

| Quantity | Operational meaning |
|---|---|
| Spendable resources | Pool credits plus eligible hosted, recurring, temporary, or other payment sources. Track spending restrictions and refresh timing separately. |
| Completion cost of a run | Cheapest legal route to the intended payoff: initiation, ICE, abilities, upgrades, access and steal costs, and any necessary second run. A breach alone may not achieve the objective. |
| Recovery cost | Clicks, credits, cards, or lost permanents needed to restore a useful position after a play. Include tag removal and damage recovery where relevant. |
| Remote contest capacity | Whether the Runner can reach and steal the remote's agenda during the next opportunity, including preparation actions and plausible hidden tricks. |
| Corp defense budget | Credits and other costs needed on the Runner's turn to defend critical servers, followed by resources needed to finish scoring. Do not treat future income as already available. |
| Agenda belief | A probability distribution over hidden agenda locations and values, updated from accesses, draws, installs, discards, shuffles, and revealed cards. |
| Deadline | Earliest credible opposing win or strategic lock; also estimate your own earliest win. Use ranges when information is incomplete. |
| Useful access | An access with new information, steal potential, actionable trash potential, or another relevant payoff. Reaccessing a known non-agenda is not a fresh lottery ticket. |

The Runner must not inspect hidden Corp cards. The Corp may use its own HQ and installed cards, but not the Runner's grip or the unknown order of either deck. Knowing a registered decklist does not reveal shuffled card positions. Uncertain estimates should retain confidence and alternatives rather than become facts.

## Universal Runner fundamentals

### Early pressure and run selection

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| R01 [B,M] | If an open central offers a fresh access, run it before optional rig installation. Early, take one such access by default rather than a basic credit. | Do necessary economy sequencing first. Skip when a setup engine needs the click immediately, access punishment is credible, or another server is urgent; this is not a quota of one run every turn. |
| R02 [B,M] | With at least two clicks left, probe one unrezzed ICE on a relevant server when the plausible affordable outcomes are a stop or minor recoverable tax. Prefer probing to installing speculative coverage. | Do not use Corp credits below 3 as a safety certificate. Defer if affordable possibilities include lethal damage, losing an essential program, or tags you cannot manage. |
| R03 [B] | If a probe can make the Corp spend money needed to defend a second target, sequence it before the second run. Recalculate after the Corp's rez decision. | The Corp can decline to rez. Evaluate both branches; do not credit the plan with guaranteed denial. |
| R04 [B] | If a run's only credible outcome is an already-known stop, and it earns no relevant trigger or resource exchange, prefer another action. | A blocked run can still be useful when it consumes a counter, earns money, or changes defenses. Mere activity has no value. |
| R05 [B,M] | When a non-winning run becomes worse than available alternatives, jack out at the next legal opportunity. | Earlier spending is sunk. Do not assume an escape window exists between revealing ICE and suffering its encounter; use the engine's actual timing. |

### Follow the agendas

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| R06 [B,M] | Contest a likely scoring agenda by default; steal plus denied Corp progress usually beats a random central access or optional setup. Make stopping a likely winning score the highest non-winning priority. | Concede an early small agenda when the contest consumes most of the next turn as well, and the concession buys a credible stronger position. Trap evidence and scoring abilities can reverse this. |
| R07 [B] | Choose HQ over equally priced fresh R&D accesses when the Corp has drawn repeatedly without scoring and has no credible outlet for agendas. | Use probabilistic evidence, not a fixed agenda count. Hidden remotes, discards, and recycling can account for points; once HQ looks diluted, switch back. |
| R08 [B] | With no urgent remote, no HQ concentration evidence, and similar entry costs, choose a fresh R&D access as the central default. Do not pay for the same known non-agenda again. | Run HQ instead when materially cheaper or supported by abilities or multiaccess. If R&D is already covered beyond the next draw, develop or attack elsewhere. |
| R09 [B,M] | Check Archives when new discards, milling, or observed agenda movement make its contents valuable. | Unprotected Archives can still contain harmful access effects. If its relevant contents are already known and unchanged, do not repeatedly check without another payoff. |
| R10 [B,A] | For a non-urgent central costing 4 or more credits to enter, default to preparing multiaccess rather than buying one random access [T]. | A likely winning access, dense HQ, run rewards, or a disappearing opening can justify the single access. Do not wait for multiaccess that the deck cannot produce. |

### Spend and develop with a purpose

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| R11 [B] | Spend above the reserve defined below. Once safety and the next concrete contest are funded, prefer an affordable useful run, priority trash, or needed tool over more generic money. | No arbitrary 10- or 20-credit savings target. Spend below the reserve to win or prevent defeat; hold more only for a named sequence, not because larger balances always score better. |
| R12 [B,A] | During turns 1–3, if the hand lacks a useful play, favor drawing over a basic credit while above the next relevant affordability threshold [T]. | Take the credit first if it immediately enables strong economy or a necessary action. Installed high-yield economy, excess hand size, and a known deadline can reverse the preference. |
| R13 [B] | Play affordable burst economy before another planned spend takes you below its play threshold, unless the order sacrifices a more valuable trigger or opportunity. | Evaluate net gain, not the printed payout. Include the card and click used. Apply the same sequencing check to install discounts. |
| R14 [B,A] | Install the cheapest adequate answer to the next important server. Defer redundant breaker types and efficiency upgrades unless they should repay within about three relevant encounters or two turns [T]. | Long glacier games justify longer payback. Against rush, use an inefficient temporary answer now instead of losing points while finding the ideal rig. |
| R15 [B,M] | If a planned draw or safe information run can change the rest of the turn, take it before irreversible commitments, after any necessary safety preparation. | Preserve clicks for reactions. Do not “run first” when drawing cards or installing protection is necessary to survive that run. |

### Assets, damage, and disruption

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| R16 [B,M] | Default to trashing an accessible early economy asset costing at most 3 credits all-in if it should yield at least 4 more credits or is the Corp’s only recovery source [T]. | At 5 or more all-in, default to leaving ordinary drip economy unless denial is your plan or the asset is pivotal. Preserve required contest funds; urgent enabling assets use R17. |
| R17 [B,M] | Prioritize an asset or upgrade enabling imminent scoring, lethal damage, recursion, or a lock over a modest income asset when removing it breaks the threatening line. | Rank by the enabled line, not by card category or trash cost alone. Re-evaluate if the Corp has redundant copies or immediate recursion. |
| R18 [B,M] | Before routine unknown accesses against credible damage, draw toward a full grip and reserve a recovery click. For a bounded expected damage sequence D, prefer at least D+1 available grip cards after prevention [T]. | This is a cushion, not a safety guarantee: multiaccess and Corp-turn damage can require more. Exactly D cards survive D damage but leave no margin. A winning run may justify accepting more risk. |
| R19 [B,M] | If one removable tag is a plausible run outcome and punishment matters, retain at least one click and 2 eligible credits to clear it. Avoid the last-click run by default. | Scale to the actual number of tags and card-specific removal restrictions. Tag-me plans and immediate winning runs override the ordinary cleanup preference. |
| R20 [B,M] | Against program destruction, protect the irreplaceable breaker or recursion route before taking an encounter that can destroy it. | Losing a cheap redundant program can be an efficient payment; losing the only way through the scoring server can cost the game. |
| R21 [B,M] | If the Corp is spending heavily to score and the remote is too expensive to contest, attack the central or engine that the scoring commitment leaves vulnerable. | Do not choose compensation if the score itself ends the game. Account for agendas whose abilities restore Corp money or defense immediately. |
| R22 [B] | If a low-risk development turn permits an unavoidable Corp win, choose the available line with the best chance to win or prevent it, even if normally too risky. | Apply this to a credible deadline, not just a losing board position. Preserve a better long-term line when the loss is not actually imminent. |
| R23 [B,M] | Leave a harmless subroutine unbroken. Against non-kill decks, accept one net damage from a healthy, nonessential grip rather than pay 3 or more credits to prevent it [T]. | Pay instead when damage threatens survival, an irreplaceable card, or future recovery. Break Corp-credit gain when it would fund a later defense; do not minimize the current ICE cost in isolation. |
| R24 [B,A,M] | Keep strong usable economy even without a breaker or tutor. Mulligan weak economy plus an awkward breaker. Use the graded opening-hand policy below instead of requiring both card categories. | A rush matchup increases the value of timely interaction; a slow matchup increases the value of a durable engine. Printed card types alone do not establish a playable opening. |

## Runner defaults when search has no clear answer

These are my preferred starting biases [T]. They are deliberately opinionated so the bot acts instead of postponing every decision until it has a perfect model.

### R11 in practice — fund a purpose, then spend

**The default is to use money to get points, stop scores, or deny the Corp a productive turn.** Accumulating another five credits after the next useful sequence is funded usually loses to executing that sequence. Reserve money for an identifiable action, not for a generally comfortable balance.

Build the reserve in this order:

1. **Immediate safety:** fund known tag cleanup, necessary prevention, or other survival costs. A future income click cannot pay for an effect that happens before that click.
2. **Concrete next contest:** if the Corp has a credible remote score next turn, calculate the minimum pool needed at the end of this turn to contest it next turn. Subtract only reliable next-turn income that fits alongside preparation and running. Include restricted funds only where eligible. Include two traversals if defense may require them.
3. **Economic recovery:** in an otherwise safe state, use **3 pool credits** as a soft fallback floor. Use the actual higher play threshold instead when a strong economy card already in hand needs it. With a usable income engine, **0–2** can be enough; with an unfunded necessary install, reserve its actual cost instead. These are defaults, not hard floors.

Do not add the same money twice. A balance of 5 that both threatens the remote and enables a future economy event satisfies both requirements if their timing is compatible. Costs that must actually be paid sequentially must be funded sequentially. Implement the reserve as the minimum starting balance of a feasible recovery/contest sequence, rather than blindly summing labels.

**Once the reserve is met, spend available surplus before taking another basic credit** if there is a qualifying target:

| Available play | Default decision [T] |
|---|---|
| Fresh central access for 0–2 net credits, one run click, no material punishment, no urgent alternative | Run. Prefer it to a basic credit or a speculative spare breaker. |
| Fresh random single access for 3 credits | Run if it has a useful trigger, improves information for this turn, or the opening is closing. Otherwise develop. |
| Random single access for 4+ credits | Usually wait for multiaccess, a cheaper route, or better agenda evidence. |
| Likely agenda in a contestable remote | Spend aggressively to steal it; do not preserve the soft floor merely to stay comfortable. Preserve actual survival requirements. |
| Essential enabler that will generate a score or dangerous lock | Spend to remove it before buying optional rig improvements. |
| Ordinary asset with little future payout | Keep the money for a better target. Surplus money is not an instruction to waste it. |

“Net credits” means pool depletion after guaranteed run rewards and eligible temporary funding, not speculative gains from unknown cards. Charge additional setup clicks separately. These price bands assume roughly ordinary agenda density and a non-terminal midgame; a dense HQ or winning agenda changes them sharply.

**Worked choices:**

- **12 credits; a safe fresh central costs 2; next-turn contest reserve is 6:** run rather than click to 13. Ending at 10 still funds the reason for saving.
- **8 credits; remote contest next turn requires 7 after reliable income; an ordinary asset costs 4 to clear:** leave the asset. Ending at 4 creates the opening the Corp needs.
- **6 credits; a likely remote agenda costs 5 total to steal; Corp has no credible punishment and your income engine is active:** steal, even though you end at 1. A soft cash floor should not veto productive aggression.
- **14 credits; no credible scoring remote; safe fresh access costs 2; bot plans four basic credits:** reject that plan by default. Take the access and use remaining clicks on useful draw or development. Banking to 18 needs a named purpose.

My judgment: a beginner bot is more likely to lose by failing to cash in an opening than by ending a productive turn a little short of an arbitrary cash target. Give waiting a concrete justification. Against kill, fast scoring, or expensive repeated remote contests, that justification will often exist.

### R24 in practice — judge the opening, not the card categories

**Remove “must have a breaker or tutor” as a keep requirement.** Money plus draw can find tools; money alone can support early probes and later installation. A breaker that does not answer the likely early problem is much less valuable than it looks.

Use the following as a first implementation [T], for ordinary Runner decks. Evaluate a feasible two-turn opening from the actual starting credits and known identity/setup abilities. Do not count all cards' best effects if they cannot be paid for and played in the same opening. Unknown future draws do not count as already holding their rewards.

| Component | Score | Definition |
|---|---|---|
| Economy E | 0 | No meaningful usable economic improvement in the first two turns. |
| Economy E | 1 | Only weak, delayed, or highly conditional income; the opening remains cash-starved. |
| Economy E | 2 | A feasible economy package yields at least about 3 net credits by the end of turn two using no more than 3 clicks, with no severe drawback. |
| Economy E | 3 | At least about 6 net credits using at most 4 economy clicks, or a funded repeatable engine yielding at least 2 credits per activation click by turn two without stranding the rest of the opening. |
| Card access D | 0 | No useful draw or search in the opening. |
| Card access D | 1 | A draw effect sees at least 2 additional cards within at most 2 clicks and is affordable in the planned opening. |
| Card access D | 2 | Affordable draw sees at least 3 additional cards within at most 2 clicks, or a tutor can fetch and fund a relevant breaker by turn two. |
| Interaction I | 0 | No timely useful tool. Expensive unplayable breakers and support for absent cards count here. |
| Interaction I | 1 | One affordable tool provides useful expected early coverage or a credible pressure line. |
| Interaction I | 2 | The opening can fund a broad answer or a particularly strong response to the expected early scoring route. |
| Clutter W | 0 / 1 / 2 | Subtract 1 for two redundant or opening-dead cards; subtract 2 for three or more. One late-game card is acceptable. |

Use the highest satisfied tier within each component, not their sum. Basic credit clicks are never E-income; include acquisition/install/activation costs in net income. A card may support two components only if the same feasible opening genuinely produces both benefits. If the engine costs so much that no credible interaction or recovery remains, it does not qualify for E=3 merely because its printed activation is efficient.

**Decision order:**

1. **E=0–1: mulligan by default.** Override only for a deck-specific funded plan, such as an identity-backed opening, a particularly strong run economy line, or a combo whose setup genuinely works. Do not call a lucky future draw a plan.
2. **E=3 and W≤1: keep by default**, including hands with no breaker or tutor. Against a known rush strategy, require either D≥1 or I≥1; otherwise redraw unless the hand has a separate demonstrated way to meet that deadline.
3. **E=2: keep if `E + D + I - W >= 4`.** Otherwise mulligan. Economy plus one mediocre breaker is not enough to pass automatically.
4. **E=3 and W=2: keep only if the remaining opening still scores at least 4 and has D≥1 or I≥1.** The point is to reject hands whose apparent wealth is attached to a largely unusable opening.

Once a mulligan is taken, play the resulting hand under the game's rules; this is not a loop that keeps redrawing until the threshold passes. At mulligan time, use only information actually available then—normally identities and permitted deck information, not the Corp's future first turn.

| Representative hand profile | Components | Default |
|---|---|---|
| Two usable burst economy cards and useful early support; no breaker/tutor | E3 D0 I0 W0 | **Keep.** Strong money does not need a breaker badge. |
| One strong burst economy card plus affordable draw-three; no breaker | E2 D2 I0 W0 | **Keep.** Four points; draw gives a reasonable route to tools. |
| One usable economy card plus an affordable flexible tutor | E2 D2 I0 W0 | **Keep**, provided the tutor and fetched tool really fit the budget. |
| One usable economy card plus one narrow cheap breaker; no draw | E2 D0 I1 W0 | **Mulligan.** The old category gate would keep this too readily. |
| Weak slow economy plus an expensive breaker | E1 D0 I0 W0 | **Mulligan.** Neither category fixes the opening. |
| Three different breakers, console, multiaccess; no usable income | E0 D0 I1 W1 or worse | **Mulligan.** Coverage without funding is not setup. |
| Funded durable income engine plus affordable useful draw | E3 D1 I0 W0 | **Keep.** No installed breaker is required immediately. |
| Strong money but no draw or interaction against known fast rush | E3 D0 I0 W0 | **Mulligan** under the rush override. |
| Strong money plus three genuinely dead opening cards, no draw/tool | E3 D0 I0 W2 | **Mulligan.** The economy does not redeem the whole hand. |
| Useful early breaker and run event that reliably provide strong income together | E3 D0 I1 W0 | **Keep.** Classify by effects, not by “economy card” labels. |

These examples specify effect profiles rather than exact five-card lists; classify the actual five cards, including their joint affordability. The numeric score is an opinionated fallback, not a substitute for deck knowledge. For a deck with unusual starting resources, adjust the affordability model before changing thresholds.

Later, a better calibration is to score a sample of fresh legal starting hands from the bot's own known deck and compare their average opening value with the current hand. Do not demand a “perfect” combination that the deck rarely draws. Until then, the table above is preferable to a strict economy-plus-breaker gate.

## Universal Corporation fundamentals

### Build a scoring plan and budget it

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| C01 [B,A] | If an agenda can be scored before the Runner is likely to assemble an affordable contest, start scoring now. Prefer taking that window over a second income installation or a redundant ICE layer. | Use the Runner’s entire upcoming turn, including plausible burst money and tutors. Never treat the currently installed rig as the full answer set. |
| C02 [B] | Before installing and advancing, budget defense on the next Runner turn and completion on the following Corp turn. Require a feasible sequence for both. | “I can rez the ICE” is insufficient if the agenda then cannot be completed. Conversely, income arriving next turn may fund advancement but cannot fund defense now. |
| C03 [B] | If spending on an optional rez would prevent defending an imminent score or another critical server, prefer allowing the lower-value run. | Override for a likely game-ending steal, engine destruction, or a larger loss. Score-area abilities may alter the budget. |
| C04 [B,A] | When an expensive remote check leaves the Runner unable to contest again next turn, follow it with the strongest feasible scoring threat. A useful asset is the default bait before donating an agenda. | Use agenda bait only if the follow-up is credible even after its steal and the donated points do not create an easy Runner finish. |
| C05 [B] | Once an agenda can legally be scored, normally score it before offering an unnecessary further access opportunity. | Delay only for a concrete ability, scoring sequence, or strategic benefit supported by the exact card text. |

### Invest in the defenses that matter

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| C06 [B] | For ordinary scoring decks, prioritize affordable stopping ICE on an agenda-bearing HQ or threatened scoring remote. Otherwise protect the cheapest dangerous central attack before building an idle remote. | R&D multiaccess or HQ-triggered denial can outrank current agenda location. Do not count ICE you cannot afford to rez as reliable protection. |
| C07 [B,M] | Put repeat taxes on a repeatedly attacked central before adding them to an unused remote. Favor ICE that still taxes the Runner’s actual breakers. | Count bypass and accepted subroutines. An extra 1-credit tax rarely justifies a large new expenditure unless it closes a specific scoring window. |
| C08 [B] | Default to no second ICE on a remote that has neither a planned score nor an essential engine. On an active remote, add a layer only if it changes access feasibility or imposes a meaningful repeat tax. | Protecting a decisive non-agenda asset is a valid purpose. Do not buy layers merely to reach a preferred server size. |
| C09 [B,M] | Choose ICE order by simulating approaches: make unavoidable taxes occur before a final stop where useful, and preserve protection from known bypass tricks. | There is no universal “sentry outside” rule. Evaluate subtype coverage, on-encounter effects, movement, and the actual breaker suite. |
| C10 [B,M] | Rez affordable stopping ICE against an unprepared Runner when the server matters and the defense budget survives. Decline a costly rez on low-risk single access if it would break the scoring budget or block usable economy. | As a tie-break, paying 5 or more to tax the Runner only 1 is usually bad without a stop, useful trigger, or many expected reruns [T]. |
| C11 [B,M] | Save a limited defensive counter if the current breach is tolerable and a later score needs it more; spend it when it stops a decisive steal or wins a useful resource exchange. | It has no end-of-game value unused. Assess the Runner's ability to rerun, not just whether this run ends. |

### Manage money and agenda exposure

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| C12 [B] | Play usable burst economy early. Install a durable economy engine before optional defense when it should repay within roughly three Corp turns [T]. | Score first if the window is closing. Do not invest in three-turn payback when the likely game-ending threat is next turn. |
| C13 [B] | With no usable play and a defensible HQ, draw before clicking for a credit unless that credit unlocks an immediate action. With 3 or more agendas in a normal-sized HQ and no outlet, stop optional draw by default [T]. | Draw to seek a specific flood solution when that is the best escape. Hand-size modifiers, agenda values, and alternative win plans change the threshold. |
| C14 [B] | Resolve dangerous HQ flood in this default order: safe immediate score, safe scoring window, protected recycling, then better HQ defense. Avoid naked agenda installs as routine flood management. | An urgent attack may make defense come first. Only recycle through a line the Runner cannot interrupt profitably; reassess R&D density afterward. |
| C15 [B,M] | Reassess HQ, R&D, and Archives protection when agenda distribution, multiaccess, or attack routes change. | Defend against the Runner's actual payoff. A central with no agenda can still enable devastating economy denial or a combo prerequisite. |
| C16 [B] | Keep a spare non-agenda in HQ rather than installing it in a pointless location when doing so dilutes valuable agendas. Play productive economy or critical defense instead of preserving dilution. | Dilution is a small defensive benefit, not a reason to leave an engine unused. Evaluate actual HQ access effects. |
| C17 [B,M] | When the Runner inspects R&D faster than you draw, seek legal extra draw, shuffling, alternate agenda movement, or a change in defense that breaks the pattern. | A repeat top-card check is not automatically a complete lock. Identify which unseen cards the Runner can actually cover. |

### Exploit reactions and finish the game

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| C18 [B,M] | If the Runner cannot answer two independent threats within their next turn, present both when doing so improves the worst response you expect. | Two expensive cards that can both be ignored are not two threats. Simulate the Runner choosing the cheaper or strategically correct answer. |
| C19 [B,M] | If observable Runner behavior repeatedly reveals a weakness, exploit it: no remote checks permits hidden agendas; indiscriminate checks permit taxing decoys. | Use evidence across decisions and keep some uncertainty. An opponent can change behavior; do not infer hidden cards with certainty from one pass. |
| C20 [B,M] | Take a verified lethal line first. Otherwise prefer a sound score over incidental damage or tags; choose disruption when it disables the next contest or removes an essential tool. | Do not spend a full turn on damage the Runner repairs cheaply. Kill and grinder decks deliberately assign more value to accumulated damage. |
| C21 [B,A] | If the Runner's installed engine will outgrow your defense and you cannot disrupt it, shorten the game through the best available scoring or kill line. | Continuing to buy marginal defense can make eventual defeat more certain. Some glacier matchups require an early rush plan. |
| C22 [B] | If R&D is nearly empty, evaluate the draw-loss deadline and any legal recycling before optional draw or a multi-turn plan. | Emptying the Runner's stack does not by itself win; Runner attrition requires preventing safe progress or causing a flatline. |
| C23 [B,A,M] | For ordinary ICE-based scoring decks, keep playable economy plus 1–2 affordable relevant ICE; do not demand an agenda. Default to mulliganing 3+ agendas without a credible escape or a hand whose defenses are all unaffordable [T]. | Horizontal, kill, and combo decks need different opening requirements. An economy-free hand with a very strong affordable rush line can still be a keep. |
| C24 [B,M] | Accept bad publicity for an immediate strong scoring window more readily than for speculative setup. In a long taxing plan, decline an optional gain if repeated bad-publicity use should give back most of it. | For rough evaluation, one bad publicity used on five future runs gives up to five spendable credits to the Runner. Do not count runs with no eligible spending as full value. |

## Rough trade-offs for evaluation

These are proposed approximations [T]. Recompute their inputs from the game state. They are useful for ranking plans, not substitutes for resolving card effects.

| Comparison | Starting calculation | Important correction |
|---|---|---|
| Basic credit versus draw | Early with few options and adequate cash, draw wins. Start a credit-equivalent evaluator at 1.5 for a useful unknown draw versus 1 for a basic credit [T]. | This is a tie-break, not an extra reward on top of the simulated cards. Reduce it with clutter; increase the value of a credit that crosses an immediate threshold. |
| Long-term economy | Prefer an early engine that should repay in 2–3 turns; default against new engines needing 4+ turns unless the matchup is demonstrably slow [T]. | Payback includes install and activation clicks. At an immediate scoring deadline, even two-turn payback may be too slow. |
| Breaker upgrade | Prefer the existing adequate breaker unless the replacement should repay in roughly 3 encounters or 2 turns [T]. | A 6-credit upgrade saving 2 per encounter needs three encounters just for credit break-even, so normally defer it if only three uses remain. Opening a previously inaccessible server is different. |
| Asset removal | Early, lean toward spending up to 3 all-in to deny 4+ expected future credits; lean away at 5+ all-in for ordinary drip [T]. | R16 and R17 override for reserve violations, immediate payouts, sole recovery sources, and enabling assets. Do not give this bonus twice. |
| Run investment | Default yes at 0–2 net credits for one fresh ordinary central access, conditional at 3, usually no at 4+ without extra payoff [T]. | Apply R11’s safety and opportunity filters. Near a win, relevant agenda probability matters more than the ordinary price band. |
| Repeated remote contest | Count every required traversal and every activation/access cost. | If an ability forces a second run, an apparent 7-credit route may require 14 credits and two run clicks, before other costs. |
| Damage versus breaking | Against non-kill, prefer paying 1 to avoid a random net damage; accept it rather than pay 3+ when the grip is healthy and replaceable [T]. | At 2, let current draw efficiency decide. Do not extend this to core damage, essential grip cards, chained damage, or lethal exposure. |
| Extra ICE | Reject an optional layer costing about 6+ to establish for only a 1-credit pass tax unless repeated use or a concrete stop justifies it [T]. | Affordable gearchecks in a scoring window get priority even if their later tax is tiny. |

For a uniformly random HQ sample, if HQ has `N` cards including exactly `A` agendas, accessing `k` distinct cards has probability `1 - C(N-A,k)/C(N,k)` of finding at least one agenda. A Runner normally does not know `A`, so average over its belief distribution. Across separate HQ breaches, repeats are possible. Also distinguish “an agenda” from “enough stealable points to win.” This formula and the numeric examples are analytical tools, not sourced empirical performance claims.

## Opponent mistakes to punish

Use these as candidate bad-decision labels. Penalize the avoidable strategic error using information available at the time, not the unlucky outcome afterward. **Critical** means the error allows a foreseeable immediate loss; **major** means it creates a large exploitable opportunity; **moderate** means repeatable inefficiency. Severity is state-dependent.

### Corp mistakes the Runner should punish

| Mistake | Observable condition | Runner exploitation | Default severity |
|---|---|---|---|
| Unfunded defense | Corp spending leaves the relevant defense unaffordable under current modifiers. | Attack the exposed target before income recovers. | Major; critical if winning steal is available. |
| Decorative remote | Several ICE protect a remote with no evident forcing threat while a central remains cheap. | Attack that central or develop; do not pay to inspect an irrelevant fortress repeatedly. | Moderate to major. |
| Premature agenda commitment | A likely scoring card is installed while your full turn can afford the complete steal line. | Assemble only the required answer and contest before scoring. | Major. |
| Agenda accumulation | Draws accumulate with no corresponding scores or credible agenda movement. | Increase HQ access or multiaccess priority, retaining uncertainty. | Major. |
| No stopping effect | Remote ICE's penalties are affordable and do not prevent reaching the agenda. | Accept tolerable subroutines and steal. | Major. |
| Unprotected enabling asset | An accessible card enables the next score, kill, or recovery sequence. | Remove it before activation when the cost is favorable. | Major to critical. |
| Wasteful rez | Corp rezzes a low-impact defense and cannot also cover your main target. | Switch targets with remaining clicks. | Major. |
| Predictable installs | Repeated evidence separates agendas and decoys by the Corp's actions. | Reweight remote beliefs and check the likely agendas more selectively. | Moderate to major. |

### Additional decision penalties for the bot [T]

- **Hoarding:** moderate penalty for a basic credit when the R11 reserve is already met and a qualifying affordable target exists; major if it lets a clear opening expire. Do not penalize waiting without a useful target.
- **False mulligan rejection:** major opening-policy error for rejecting an E3/W0 non-rush hand solely because it has no breaker or tutor.
- **False mulligan acceptance:** major opening-policy error for keeping weak income and an unaffordable breaker solely because both card categories are present.
- **Idle defense:** moderate penalty for adding a second layer to an irrelevant remote; major if doing so leaves the active score or central unfunded.

Apply each penalty to the underlying mistake once. Do not also count its mirrored opponent-exploitation label as a second independent loss.

### Runner mistakes the Corp should punish

| Mistake | Observable condition | Corp exploitation | Default severity |
|---|---|---|---|
| Unnecessary expenditure | Runner drains contest capacity on a weak run or low-priority trash. | Score during recovery, accounting for hidden burst money. | Major. |
| Excess setup | Runner spends turns on redundant rig pieces while missing one crucial answer. | Score behind the actual missing coverage immediately. | Major. |
| Empty grip or unmanaged tags | Runner ends the turn within a legal kill or severe disruption line. | Execute it after checking prevention and all costs. | Critical if lethal. |
| Reckless encounter | Runner faces affordable punishing ICE with irreplaceable exposed programs or insufficient cards. | Rez when the punishment is better than conserving the money. | Major to critical. |
| Repeated expensive single accesses | Runner pays heavily without meaningful new information or disruption. | Let the taxes work; develop or score during their recovery. | Moderate to major. |
| Trash everything | Runner answers every asset despite insufficient money or clicks to cover a score. | Present competing threats and exploit the resulting gap. | Major. |
| Ignore the engine | Runner leaves an accessible enabling card despite having a favorable removal line. | Use the engine before they reconsider; convert its value into a win threat. | Major. |
| Fails to exploit superiority | Runner can contest but keeps taking economy actions while your route to a win remains active. | Execute that route; force decisions instead of waiting for more Runner money. | Major to critical. |

## Runner archetype modules

Archetypes overlap. A deck can be a regular toolbox with a denial package or a big rig with a central burst finish. Apply the module to change the base defaults, not to suspend interaction while completing a preferred setup.

### Big rig and late-game remote control [A,M]

- **When** the Corp cannot win before your economy and relevant coverage become active, **prioritize** those installations over low-value runs.
- **When** the Corp presents an earlier credible score, **interrupt setup** for the cheapest effective contest; concede only after comparing the lost points and agenda abilities with time gained.
- **Once** you can repeatedly contest the remote, retain that capacity and add central pressure to convert the stalled agenda flow into steals.
- **Punish:** Corp investment in taxes your completed rig breaks cheaply. **Avoid:** building an impressive rig after the deciding score. **Knobs:** setup horizon, concession tolerance, remote reserve, upgrade payback.

### Aggro and run-based tempo [A,M]

- **When** a cheap run produces money, draw, a useful counter, information, or a strong access, prefer combining those rewards in one action.
- **While** the Corp's defenses are sparse, attack the target that forces the most awkward affordable response; install only what maintains pressure.
- **When** all profitable routes close, switch to economy and tools instead of repeating losing runs.
- **Punish:** slow, expensive setup and exposed centrals. **Avoid:** treating number of runs as the objective. **Knobs:** early-run bonus, trigger value, tolerance for recoverable facechecks, minimum fallback economy.

### Economy denial and ICE destruction [A,M]

- **When** draining money, derezzing, or destroying ICE makes a follow-up attack materially cheaper, plan disruption and exploitation together.
- **Target** recovery sources when they would let the Corp escape the denial cycle; prioritize HQ if agendas accumulate there.
- **Switch plans** if cheap recovery, recursion, or denial-resistant income costs less than your repeated attacks.
- **Punish:** Corp dependence on one costly rez or income source. **Avoid:** spending more to keep the Corp poor than you gain from that poverty. **Knobs:** recovery estimate, denial duration, follow-up value.

### Regular toolbox and flexible midrange [A,M]

- **When** the next Corp threat needs one answer, obtain that answer rather than all possible answers.
- **Hold** flexible tutors or recursion until their target is known, unless delayed installation creates a dangerous opening.
- **After** establishing contest capacity, choose remote, HQ, or R&D from current payoff estimates.
- **Punish:** a Corp assuming that an uninstalled breaker is unavailable. **Avoid:** unnecessary tutoring that spends the only flexible answer. **Knobs:** tutor timing, emergency reserves, central-versus-remote weighting.

### Central multiaccess and burst access [A,M]

- **When** a multiaccess effect reaches enough new cards to justify its setup, concentrate spending on that run.
- **Prefer** HQ bursts when agenda accumulation is likely and R&D bursts when fresh depth is valuable.
- **If** the Corp has a faster remote win, contest or accelerate the burst rather than finishing optional setup.
- **Punish:** light central defense during remote investment. **Avoid:** multiaccess that cannot pay steal costs or survive access effects. **Knobs:** useful-card count, target density, burst deadline, survival reserve.

### Combo and board reset [A,M]

- **When** the assembled line can win or produce a clearly favorable recovery race before the Corp's deadline, preserve its prerequisites and execute it.
- **Before a reset**, count both players' rebuild resources and your own destroyed investment.
- **If** a prerequisite is blocked, compare finding an answer with switching to ordinary steals; do not endlessly assemble an unusable combo.
- **Punish:** Corp overcommitment to a reset-vulnerable board. **Avoid:** a spectacular reset from which the Corp recovers first. **Knobs:** combo readiness, disruption risk, rebuild advantage.

### Mill and sabotage [A,M]

- **When** milling changes Archives or creates a realistic draw-loss deadline, update both the access plan and the clock.
- **Pressure** recycling when removing it shortens the clock more than additional milling.
- **Contest** scoring when the Corp can win before the mill plan finishes; milling is not automatic prevention.
- **Punish:** a thin R&D with no adequate recycling. **Avoid:** treating random milling as a guaranteed agenda discard. **Knobs:** deck-exhaustion clock, Archives value, recycling capacity.

### Stealth, recurring-credit engines, and tag-me overlays [A,M]

- **Stealth/recurring:** schedule runs around refreshable funds and spending restrictions. Buy extra sources when they remove a repeated bottleneck. Avoid spending the only eligible stealth credit before a later required activation.
- **Tag-me:** accept tags only when your deck and current position can survive the opponent's available punishment and lost resources. Stop installing vulnerable resources without a near-term return.
- These are resource and risk overlays, not necessarily independent win plans. **Knobs:** restricted-credit allocation, refresh timing, resource exposure, punishment tolerance.

### Runner archetype overrides to the numeric defaults [T]

| Plan | Default adjustment |
|---|---|
| Big rig | Allow 3–4-turn engine payback in a slow matchup; keep the early remote-contest override. Do not save merely to complete every rig slot. |
| Aggro/run economy | Allow one higher run-cost band when guaranteed triggers make the turn productive; compute net cost first and do not count the same reward twice. |
| Denial | Spend below the soft floor when the disruption demonstrably leaves the Corp unable to defend the follow-up. Require that follow-up to be available. |
| Toolbox | Preserve the flexible tutor over installing speculative coverage. With funded economy, treat an affordable tutor as strong mulligan card access. |
| Central burst | Default to assembling a burst of at least 3 useful accesses before paying a 4+ credit entry fee. An urgent remote still outranks that setup. |
| Combo | Reserve the actual combo cost only when the key pieces and a plausible execution turn exist. Missing several pieces is not a reason to bank indefinitely. |
| Mill/sabotage | Inspect changed Archives when credible new value appears; race only when the projected deck clock actually beats the scoring clock. |
| Stealth/recurring | Prefer running before refresh when eligible credits would otherwise expire and the target qualifies. Unused recurring credits alone do not justify a pointless run. |

## Corporation archetype modules

Choose a primary plan and a fallback. Use only the modules supported by the deck and configured card pool.

### Rush [A,M]

- **When** affordable stopping ICE exploits a missing Runner answer, install and score promptly; choose burst economy if it funds that window.
- **After** coverage appears, seek another gap, a timing trick, or a supported fast-advance/kill finish.
- **Punish:** slow rigs and unnecessary early Runner installs. **Avoid:** adding redundant layers while the Runner finds the missing breaker. **Knobs:** window urgency, accepted agenda risk, transition point.

### Glacier [A,M]

- **When** sustainable money and layered defense can exceed the Runner's next-turn contest capacity, develop a scoring remote with a complete defense budget.
- **Use** assets and upgrades to make repeated checks costly; score after a check meaningfully reduces recovery capacity.
- **Against** superior late-game engines, take earlier scores or disrupt the engine.
- **Punish:** expensive central runs that leave insufficient remote funds. **Avoid:** judging defense by ICE count. **Knobs:** remote cost target, upgrade reserve, expected repeated runs.

### Fast advance [A,M]

- **When** an agenda and enabling effects form a legal install-to-score sequence this turn, compare scoring now with preserving the tools for a more valuable sequence.
- **Protect** HQ, R&D, and exposed enablers according to the route by which the Runner can disrupt you.
- **Budget** clicks, counters, advancement, and operation costs for the complete line before starting it.
- **Punish:** Runner investment solely in remote breaking. **Avoid:** building a costly scoring fortress for agendas that do not need exposure. **Knobs:** enabler conservation, central defense, chain value.

### Never advance and hidden agendas [A,M]

- **When** an agenda can be left unadvanced and completed next turn, use install patterns also available to useful assets or traps.
- **If** checks are costly, offer cards that gain value whether checked or left alone.
- **If** the Runner checks cheaply and consistently, reduce reliance on bluffing.
- **Punish:** ignored old remotes or automatic expensive checks. **Avoid:** treating every unadvanced card as harmless. This is a scoring technique that can overlay other archetypes. **Knobs:** inferred check rate, bluff mix, check tax.

### Asset spam and horizontal economy [A,M]

- **When** deployment exceeds the Runner's profitable clearing capacity, install complementary income, draw, recursion, and win-enabling threats.
- **If** the Runner clears assets, exploit the time and money spent; if they ignore them, activate the engine and convert it into scoring or a kill.
- **Against** efficient mass trash or resets, protect essential pieces and avoid needless overextension.
- **Punish:** attempts to clear everything. **Avoid:** generating a large board with no finishing line. **Knobs:** deployment rate, asset priority, redundancy, reset exposure.

### Tag pressure and tag-and-bag [A,M]

- **Before** attempting tags, determine whether the Runner can avoid or clear them and whether punishment remains affordable afterward.
- **When** tag removal drains a turn, score during the resulting gap even if the kill fails.
- **Against** a tag-me Runner, use real punishment; merely adding more tags may have little value.
- **Punish:** ending a turn tagged, broke, or without a viable recovery line. **Avoid:** spending the kill budget to land tags. **Knobs:** trace budget, cleanup capacity, punishment mix.

### Damage kill, shell game, and grinder [A,M]

- **Kill:** when damage can exceed the Runner's effective survival capacity in a legal sequence, preserve the necessary costs and timing; otherwise use damage for an identified tempo benefit.
- **Shell game:** when checks cost cards and missed checks permit scores or dangerous assets, mix real threats with traps rather than deploying only traps.
- **Grinder:** when total remaining cards and prevention cannot support repeated contests, extend that attrition pressure while respecting the Corp's own deck clock.
- **Punish:** low grip and indiscriminate multiaccess. **Avoid:** assuming a full grip guarantees safety, or that any damage is productive. **Knobs:** burst ceiling, remaining safe accesses, trap mix, recovery rate.

### Prison and rig control [A,M]

- **When** disabling a breaker or sustaining a particular asset removes the Runner's practical winning routes, protect that bottleneck and identify the eventual finish.
- **Account** for tutors, recursion, alternate breakers, and direct disruption before declaring a lock.
- **If** the Runner is about to recover, exploit the current window instead of buying marginal lock strength.
- **Punish:** dependence on one irreplaceable tool. **Avoid:** a costly lock that never wins before deck exhaustion. **Knobs:** recovery estimate, lock maintenance, finish deadline.

### Combo and hybrid plans [A,M]

- **Combo:** assemble and protect prerequisites when the completed sequence wins before opposing pressure does. Verify the entire line, including draws and legal interruptions.
- **Hybrid:** switch between scoring, fast advance, and kill according to the actual opening; do not switch merely because the last attempt failed.
- **Punish:** defense focused on only one of your live winning routes. **Avoid:** consuming shared money or counters on a secondary plan that disables a stronger finish. **Knobs:** readiness, fallback strength, shared-resource conflict.

### Corporation archetype overrides to the defaults [T]

| Plan | Default adjustment |
|---|---|
| Rush | A valid current scoring window outranks extra economy or optional ICE. Reserve for the next score, not an eventual perfect server. |
| Glacier | Default to one developed scoring remote. Accept roughly 3-turn defense/economy payback, shortening the horizon against a stronger Runner late game. |
| Fast advance | Execute an available safe score rather than wait for a remote. Conserve the enabler only when a specific stronger chain or required final score needs it. |
| Never advance | Favor useful assets as decoys; do not pay heavily for a trap the Runner has no reason to check. |
| Asset spam | In a quiet turn, favor deployment of a productive asset over an optional ICE layer. Add protection when it preserves an essential enabler, not each asset individually. |
| Tag-and-bag | Do not initiate an expensive tag line without either affordable punishment or a scoring follow-up enabled by cleanup. |
| Damage/grinder | Raise the value of damage only as the Runner’s remaining safe contests decline; ordinary damage against abundant draw should not beat a secure score. |
| Prison/rig control | Once the lock is sufficient, fund the finish before strengthening it further. |
| Combo/hybrid | Keep shared money and counters for the strongest currently feasible win line; do not split the budget across two unfinished plans. |

## Translating this into a policy

Use these defaults as priors and tie-breaks within the existing evaluator. They are useful precisely where its estimates are weak. Avoid awarding a run both its simulated benefit and a second full heuristic estimate of the same benefit.

1. **Generate legal turn plans and contingent responses.** Let the rules engine determine legality, costs, triggers, and terminal outcomes. Include economy, draw, installation, runs, scoring, and disruption.
2. **Check tactical wins and avoidable immediate losses.** Search complete lines. A high-value credit action should not outrank a certain win, nor hide an obvious lethal reply.
3. **Evaluate uncertain plans over plausible hidden states.** Use observable history and card-pool knowledge. Preserve low-probability dangerous outcomes without acting as though every unknown card is the worst possible card.
4. **Apply committed defaults when values are close or uncertain.** Use R11 spending bands, the mulligan decision order, and the archetype overrides. In a credit-equivalent evaluator, a modest default preference can be about 0.5–1 credit [T]; use a 2–3 credit penalty for a clear repeatable inefficiency. Proven tactical losses bypass these small modifiers.
5. **Adjust for archetype and matchup.** Add urgency for rush windows, reserves against tag punishment, engine priorities against horizontal decks, and so on. Evidence should update matchup beliefs during play.
6. **Replan after information changes.** A draw, rez, access, trace result, or unexpected defense can invalidate the rest of a turn.

A rule record could contain `id`, `applies_when`, `default_action`, `preference_strength`, `thresholds`, `override_conditions`, `archetype_weights`, `matchup_weights`, `confidence`, and `source_ids`. Log the default selected and the specific evidence that overrode it. Keep hard legality outside heuristic weights. Prefer probabilistic action selection among similarly valued bluffing lines to a deterministic install pattern the opponent can learn perfectly.

For initial evaluation, create paired positions that differ in one strategically meaningful variable: the Corp can versus cannot afford a rez; a run leaves versus removes remote contest capacity; R&D's next access is fresh versus known; an asset has one versus several payouts left; the Runner has versus lacks tag-removal time. The desired action should change only when the altered fact changes the relevant trade-off. These are suggested policy checks, not tests already performed.

## Sources and research notes

The recommendations in the main text are my policy judgments, informed by these sources. The numerical defaults, mulligan score, and illustrative decision penalties are not claims made by those authors. Sources were consulted for strategic concepts, not for a current tier list. Older lists and card examples may be rotated, banned, or incompatible with the selected format. Some articles make deliberately strong teaching claims; this guide does not adopt claims such as “always force rezzes” or a fixed number of accesses guaranteeing victory.

- **S1 — [spike biased: The value of a run](https://qtm-netrunner.gitlab.io/spike/docs/part1/04-run/).** Opportunity costs, facechecking as an informed risk, additional defensive effects.
- **S2 — [StimHack: Skill Deepdive Tactical 2 — Runs](https://stimhack.com/skill-deepdive-tactical-2-runs/), Elusive with Spencer Dub, 2021.** Rez pressure, ICE roles, defensive budgets, scoring-window considerations.
- **S3 — [spike biased: Flow and locks](https://qtm-netrunner.gitlab.io/spike/docs/part2/01-locks/).** Agenda movement and the relationships between central pressure and remote control.
- **S4 — [spike biased: Agenda density](https://qtm-netrunner.gitlab.io/spike/docs/part2/02-density/).** Changing agenda distributions, uncertainty, and relevant agenda values.
- **S5 — [spike biased: Scoring windows](https://qtm-netrunner.gitlab.io/spike/docs/part1/03-windows/).** Temporary opportunities and the deterrent value of Runner resources.
- **S6 — [spike biased: Efficient play](https://qtm-netrunner.gitlab.io/spike/docs/part1/01-efficient/).** Whole-turn planning, action order, information, and drawing.
- **S7 — [Sneakdoor Melbourne: Tempo in Netrunner — Every click matters](https://sneakdoor.wordpress.com/2015/05/21/tempo-in-netrunner-every-click-matters/), 2015.** Tempo and different schedules for converting setup into pressure.
- **S8 — [Sneakdoor Melbourne: Should you trash the PAD Campaign?](https://sneakdoor.wordpress.com/2015/05/22/tempo-in-netrunner-part-2-should-you-trash-the-pad-campaign/), 2015.** Relative tempo, asset removal, and denial.
- **S9 — [Android Netrunner Portugal: Click Efficiency and Click Waste](https://anrportugal.wordpress.com/2017/07/11/click-efficiency-and-click-waste-a-study-on-android-netrunner-strategy/), 2017.** Context-sensitive efficiency and combining effects.
- **S10 — [StimHack: The Four Corps You Meet in Netrunner](https://stimhack.com/the-four-corps-you-meet-in-netrunner-by-erinrockabitch/), 2016.** Rush, glacier, fast advance, kill, and transitions between plans. Historical taxonomy, not an exhaustive modern classification.
- **S11 — [Strange Assembly: The Primary Aspects of Netrunner](https://www.strangeassembly.com/2013/hack-the-planet-the-primary-aspects-of-netrunner), 2013.** Economy, bluffing, and run dynamics.
- **S12 — [StimHack: What makes Netrunner special?](https://stimhack.com/what-makes-netrunner-special/), 2021.** Economy, defense, and threats as different investments.
- **S13 — [The Winning Agenda: Near-Earth Tempo](https://thewinningagenda.com/2015/11/05/near-earth-tempo-an-example-of-deck-pruning-and-design/), 2015.** Creating competing demands on Runner resources.
- **S14 — [StimHack: The Skills of Netrunner](https://stimhack.com/the-skills-of-netrunner/), 2016.** Efficiency, strategy, tactics, and opponent reading.
- **S15 — [Sneakdoor Melbourne: Runner Archetypes](https://sneakdoor.wordpress.com/runner-archetypes/).** Historical examples of big rig, toolbox, burst access, reset, and specialized engines.
- **S16 — [spike biased: Deck archetypes 101](https://qtm-netrunner.gitlab.io/spike/docs/part1/06-archetypes-101/).** Broader Corp archetypes, flexible Runners, linear plans, and hybrid caveats.
- **S17 — [Sneakdoor Melbourne: Corp Archetypes](https://sneakdoor.wordpress.com/corp-archetypes/).** Historical examples of asset tempo, scoring plans, and combo finishes.
- **S18 — [StimHack: Netrunner Nordic Championship Finals Game 1 — A Detailed High-Level Analysis](https://stimhack.com/netrunner-nordic-championship-finals-game-1-a-detailed-high-level-analysis/).** Worked examples of sequencing pressure and reading available defenses.
- **S19 — [Board and Card Games Stack Exchange: Tactics against unrezzed ICE](https://boardgames.stackexchange.com/questions/15589/what-are-some-common-tactics-to-make-runs-against-servers-with-unrezzed-ice-in-n).** Community discussion of facechecking, recovery clicks, and exposure to damage or program destruction; historical faction assumptions should not become hard rules.

- **S20 — [Board and Card Games Stack Exchange: When should I mulligan?](https://boardgames.stackexchange.com/questions/12224/when-should-i-mulligan-in-android-netrunner).** Affordability and opening plans; the discussion specifically challenges treating a missing breaker as an automatic mulligan. Historical blanket advice is not adopted.

Rules-sensitive statements were also cross-checked against the [Null Signal Games comprehensive rules](https://rules.nullsignal.games/). This is a supporting reference for timing and survival, not the organizing framework of the guide. The configured engine and card text should determine each concrete interaction.
