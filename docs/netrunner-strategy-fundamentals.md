# Netrunner strategy fundamentals for bot policy design

Research date: 8 October 2026

This guide describes how to choose plays, allocate resources, and exploit mistakes in asymmetric Netrunner. It is intended for later translation into a bot policy, rather than as a rules reference. It covers general constructed play; archetype modules require the relevant cards to exist in the configured card pool. It is not a claim about the current Standard metagame or ban list.

The main principle is to spend resources to improve the chance of winning within the relevant time horizon. Credits, cards, accesses, installed ICE, and even agenda points are intermediate measures. Their value changes when either side is near a win or a temporary opportunity is about to disappear.

## How to interpret the recommendations

- **[B] Base:** a default consideration across decks. It still has exceptions.
- **[M] Matchup:** enable or adjust using evidence about the opponent's capabilities.
- **[A] Archetype:** depends on your own deck's plan.
- **[T] Tuning:** a proposed starting approximation, not a measured optimal threshold.

Rules below are preferences over legal candidate plans, not unconditional commands. A legal win takes priority over economy; a necessary attempt to avoid defeat can take priority over normal risk limits. Evaluate combinations of actions across a turn, not just isolated clicks.

**Research versus synthesis.** The source notes summarize published strategy. Rule IDs, computational definitions, examples, exception handling, and the suggested policy structure are my synthesis for this bot use case. These have not been validated through self-play. Numerical examples illustrate calculations; they are not universal thresholds.

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

Published grounding: *The value of a run* emphasizes opportunity cost, specific possible ICE, and the trade of a click for a rez and information. The Nordic finals analysis illustrates sequencing central attacks to force awkward rez choices. These ideas support probing before complete setup, without making every blind run good. [S1, S18]

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| R01 [B,M] | If a relevant server is open, or its affordable ICE possibilities are tolerable, consider running before installing a complete rig. | Buy an access, information, or a useful trigger while it is cheap. Compare against the best setup click; an empty or already-known server may offer little. |
| R02 [B,M] | Before approaching unknown ICE, enumerate plausible identities the Corp can actually rez, including discounts and non-credit costs. Run if the outcome distribution is acceptable for the objective. | “Corp below 3 credits” reduces some risks; it does not establish safety. Cheap ICE, rez modifiers, ambushes, and on-access costs still matter. |
| R03 [B] | If a probe can make the Corp spend money needed to defend a second target, sequence it before the second run. Recalculate after the Corp's rez decision. | The Corp can decline to rez. Evaluate both branches; do not credit the plan with guaranteed denial. |
| R04 [B] | If a run's only credible outcome is an already-known stop, and it earns no relevant trigger or resource exchange, prefer another action. | A blocked run can still be useful when it consumes a counter, earns money, or changes defenses. Mere activity has no value. |
| R05 [B,M] | When a non-winning run becomes worse than available alternatives, jack out at the next legal opportunity. | Earlier spending is sunk. Do not assume an escape window exists between revealing ICE and suffering its encounter; use the engine's actual timing. |

### Follow the agendas

Published grounding: *Flow and locks* describes interrupting agendas on their way from R&D through HQ and remotes. *Agenda density* explains why target value changes with the distribution of remaining agendas and the points required to win. This favors choosing a server from current evidence rather than always attacking R&D. [S3, S4]

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| R06 [B,M] | If a remote plausibly contains an agenda the Corp can score before your following turn, compare contesting it with your best alternative winning or disruptive line. Strongly favor contesting a likely winning agenda. | Include steal costs, defensive abilities, and trap probability. An early low-impact score can be conceded to gain a stronger position. |
| R07 [B] | If the Corp has drawn many cards without scoring, exposing agendas, or visibly moving them elsewhere, increase HQ's priority. | This is probabilistic. Hidden remotes, facedown Archives, filtering, and recycling can account for the missing agendas. |
| R08 [B] | Prefer R&D when its next accesses are fresh and its expected steal value per total cost beats other targets, especially when denying the next agenda draw matters. | If the top card is a known non-agenda and nothing changes it, another identical access has no new agenda chance. Multiaccess can reach beyond it. |
| R09 [B,M] | Check Archives when new discards, milling, or observed agenda movement make its contents valuable. | Unprotected Archives can still contain harmful access effects. If its relevant contents are already known and unchanged, do not repeatedly check without another payoff. |
| R10 [B,A] | If entry is expensive, bundle several useful accesses into that entry when the additional setup cost is justified. | Count distinct cards and their actual stealability. Multiaccess that repeats known cards, or increases damage exposure beyond survivability, may be poor. |

### Spend and develop with a purpose

Published grounding: *Scoring windows* notes that unspent Runner money can discourage scoring. *Efficient play* recommends planning whole turns and getting useful information early. The tempo articles emphasize that development has to pay off in time to matter. [S5, S6, S7]

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| R11 [B] | Spend credits when a run, trash, or install improves winning prospects more than holding them. Preserve enough contest capacity if spending would give the Corp a stronger score next turn. | Avoid both hoarding during a winning opportunity and emptying the pool for weak accesses. A credible remote threat can make unused credits productive. |
| R12 [B,A] | When no immediate defense is needed and the hand lacks useful options, favor drawing if the remaining deck contains affordable economy, needed tools, or strong plays. | Draw is often better than a basic credit early, but a credit that unlocks a strong economy card or a necessary run can be better. Excess cards also risk discard. |
| R13 [B] | Play affordable burst economy before another planned spend takes you below its play threshold, unless the order sacrifices a more valuable trigger or opportunity. | Evaluate net gain, not the printed payout. Include the card and click used. Apply the same sequencing check to install discounts. |
| R14 [B,A] | Install the breaker or tutor access needed for your next important targets before redundant coverage. Add efficiency upgrades when expected future savings exceed their cost before the deadline. | An expensive complete rig can lose to rush. Conversely, a cheap emergency breaker can be correct despite poor long-run efficiency. |
| R15 [B,M] | If a planned draw or safe information run can change the rest of the turn, take it before irreversible commitments, after any necessary safety preparation. | Preserve clicks for reactions. Do not “run first” when drawing cards or installing protection is necessary to survive that run. |

### Assets, damage, and disruption

Published grounding: *Should you trash the PAD Campaign?* evaluates asset removal by the relative tempo swing, rather than a blanket requirement to trash economy. *Click Efficiency and Click Waste* stresses combining useful effects. The facechecking discussion highlights keeping recovery clicks and considering which cards or programs are exposed to punishment. [S8, S9, S19]

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| R16 [B,M] | Trash an economy asset when the future value denied, plus any disruption, exceeds entry, trash, recovery, and lost contest capacity. Favor assets about to pay out or that are the Corp's main recovery source. | Already-earned income is gone. A nearly exhausted asset has little future income to deny. Leaving an asset can be correct if clearing it opens a decisive score elsewhere. |
| R17 [B,M] | Prioritize an asset or upgrade enabling imminent scoring, lethal damage, recursion, or a lock over a modest income asset when removing it breaks the threatening line. | Rank by the enabled line, not by card category or trash cost alone. Re-evaluate if the Corp has redundant copies or immediate recursion. |
| R18 [B,M] | Before a run with damage risk, ensure the grip and usable prevention cover credible damage through the whole run; preserve recovery for the Corp's response. | Exactly as many cards as damage survives that damage but leaves zero cards. Account for multiple accesses, post-access triggers, core-damage effects, and losing crucial grip cards. |
| R19 [B,M] | If a run can give tags and the Corp can punish them, reserve the necessary removal resources or a verified alternative survival line before committing. | Late-click runs can remove the chance to recover. Tag-me decks may accept tags, but still need a plan against actual kill and resource destruction. |
| R20 [B,M] | Against program destruction, protect the irreplaceable breaker or recursion route before taking an encounter that can destroy it. | Losing a cheap redundant program can be an efficient payment; losing the only way through the scoring server can cost the game. |
| R21 [B,M] | If the Corp is spending heavily to score and the remote is too expensive to contest, attack the central or engine that the scoring commitment leaves vulnerable. | Do not choose compensation if the score itself ends the game. Account for agendas whose abilities restore Corp money or defense immediately. |
| R22 [B] | If a low-risk development turn permits an unavoidable Corp win, choose the available line with the best chance to win or prevent it, even if normally too risky. | Apply this to a credible deadline, not just a losing board position. Preserve a better long-term line when the loss is not actually imminent. |
| R23 [B,M] | Break only the subroutines required for the run's objective and an acceptable resulting position. Compare taking a penalty with paying to prevent it. | Letting the Corp gain credits can fund later ICE; accepting a tag or damage can change survival. Evaluate the sequence, not each subroutine in isolation. |
| R24 [B,A,M] | When deciding a mulligan, compare the opening hand's feasible first turns with a fresh hand from your deck. Favor usable economy and access to the tools needed before the opponent's likely deadline. | Do not require all three breaker types. A hand of expensive tools without money may be worse than economy plus a tutor; a slow engine hand may be acceptable only against a slow plan. |

## Universal Corporation fundamentals

### Build a scoring plan and budget it

Published grounding: the strategy literature treats scoring windows as temporary advantages in money, tools, time, or information. The older four-archetype model distinguishes plans that exploit early missing breakers, impose later taxes, score from hand, or threaten a kill; it also explicitly recognizes hybrids. [S2, S5, S10]

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| C01 [B,A] | When holding a scoreable agenda, test whether the Runner can contest it over the entire exposure period. If the window is good and likely to close, score rather than automatically building more infrastructure. | Compare the Runner's full next-turn preparation capacity, not only current credits and installed breakers. Include known tutors, run events, and eligible hosted money. |
| C02 [B] | Before installing and advancing, budget defense on the next Runner turn and completion on the following Corp turn. Require a feasible sequence for both. | “I can rez the ICE” is insufficient if the agenda then cannot be completed. Conversely, income arriving next turn may fund advancement but cannot fund defense now. |
| C03 [B] | If spending on an optional rez would prevent defending an imminent score or another critical server, prefer allowing the lower-value run. | Override for a likely game-ending steal, engine destruction, or a larger loss. Score-area abilities may alter the budget. |
| C04 [B,A] | If an asset, low-impact agenda, or defensive ability can drain the Runner enough to enable a more valuable score, evaluate that two-stage plan. | Calculate whether the Runner can afford both contests. Do not donate the points that let the Runner win, and model their option to ignore the bait. |
| C05 [B] | Once an agenda can legally be scored, normally score it before offering an unnecessary further access opportunity. | Delay only for a concrete ability, scoring sequence, or strategic benefit supported by the exact card text. |

### Invest in the defenses that matter

Published grounding: *Tactical Runs* distinguishes ICE that stops, taxes, and punishes. *The Primary Aspects of Netrunner* connects economy to what servers can actually be protected, while *What makes Netrunner special?* separates Corp investment in defense from creating threats. [S2, S11, S12]

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| C06 [B] | Place affordable stopping ICE on the server whose breach currently matters most. When the Runner lacks a relevant breaker, exploit that subtype on a scoring route. | Unrezzable protection may bluff but is not reliable defense. Damage or tags without a stop may allow a Runner to take the agenda anyway. |
| C07 [B,M] | Add taxing ICE where the Runner is likely to make repeated important runs and their available breakers actually pay a meaningful cost. | Printed strength or rez cost is not the tax. Include bypass, destruction, alternative breakers, and accepting subroutines. |
| C08 [B] | Before adding another ICE to a remote, identify the score, engine, or forcing threat the layer protects. Prefer another use of the click and money if the layer changes no relevant outcome. | A non-scoring remote can justify heavy protection when it contains a decisive engine or kill piece. “Never ICE an asset” is also wrong. |
| C09 [B,M] | Choose ICE order by simulating approaches: make unavoidable taxes occur before a final stop where useful, and preserve protection from known bypass tricks. | There is no universal “sentry outside” rule. Evaluate subtype coverage, on-encounter effects, movement, and the actual breaker suite. |
| C10 [B,M] | Rez when the prevented payoff or expected future tax justifies the immediate cost. Decline when the Runner is baiting spending that opens something more important. | Include information revealed, on-rez value, and economy thresholds. Rezzing merely because a run approaches is not a plan. |
| C11 [B,M] | Save a limited defensive counter if the current breach is tolerable and a later score needs it more; spend it when it stops a decisive steal or wins a useful resource exchange. | It has no end-of-game value unused. Assess the Runner's ability to rerun, not just whether this run ends. |

### Manage money and agenda exposure

Published grounding: *Agenda density* connects defense priorities to where points remain. *Flow and locks* describes moving agendas out of threatened zones and the danger of interrupted agenda flow. The efficiency articles distinguish productive draw from actions that only increase vulnerability or clutter. [S3, S4, S6, S9]

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| C12 [B] | Establish income sufficient for the next defense and scoring sequence. Install slower economy when it is likely to survive and pay back before the game's decisive turn. | Burst money can be superior during a temporary scoring window. Extra economy after the Runner has a likely immediate win is too late. |
| C13 [B] | If optional drawing finds needed ICE, economy, or scoring pieces with acceptable exposure, draw. Reduce optional draw when HQ is already agenda-heavy and there is no safe outlet. | Do not stop drawing mechanically just because HQ has an agenda: the solution to the flood may be in R&D. Evaluate draw, play, and discard together. |
| C14 [B] | When HQ becomes dangerous, compare scoring, protected installation, legal recycling, and stronger HQ defense. Choose a route that reduces total steal exposure. | Moving agendas to an open remote or Archives simply relocates the problem. Include the Runner's access to the destination and ability to interrupt recycling. |
| C15 [B,M] | Reassess HQ, R&D, and Archives protection when agenda distribution, multiaccess, or attack routes change. | Defend against the Runner's actual payoff. A central with no agenda can still enable devastating economy denial or a combo prerequisite. |
| C16 [B] | Preserve non-agenda cards in HQ when they meaningfully dilute random access and have future uses, but play them when the resulting board or economy is worth more. | Do not deliberately keep useless cards instead of solving a winning threat. The Corp knows HQ composition; the Runner does not. |
| C17 [B,M] | When the Runner inspects R&D faster than you draw, seek legal extra draw, shuffling, alternate agenda movement, or a change in defense that breaks the pattern. | A repeat top-card check is not automatically a complete lock. Identify which unseen cards the Runner can actually cover. |

### Exploit reactions and finish the game

Published grounding: *Near-Earth Tempo* presents threats that pull the Runner's effort in competing directions. *The Skills of Netrunner* separates efficient resource play from choosing strategically important actions and reading the opponent. [S13, S14]

| ID | Condition and preferred action | Trade-off and exceptions |
|---|---|---|
| C18 [B,M] | If the Runner cannot answer two independent threats within their next turn, present both when doing so improves the worst response you expect. | Two expensive cards that can both be ignored are not two threats. Simulate the Runner choosing the cheaper or strategically correct answer. |
| C19 [B,M] | If observable Runner behavior repeatedly reveals a weakness, exploit it: no remote checks permits hidden agendas; indiscriminate checks permit taxing decoys. | Use evidence across decisions and keep some uncertainty. An opponent can change behavior; do not infer hidden cards with certainty from one pass. |
| C20 [B,M] | If a tag, damage, or rig-destruction line is available, compare its full effect with scoring. Prefer it when it wins, disables the next contest, or removes more recovery capacity than it costs. | Damage is not automatically progress. Include prevention, replacement cards, recursion, and all costs of landing and exploiting tags. |
| C21 [B,A] | If the Runner's installed engine will outgrow your defense and you cannot disrupt it, shorten the game through the best available scoring or kill line. | Continuing to buy marginal defense can make eventual defeat more certain. Some glacier matchups require an early rush plan. |
| C22 [B] | If R&D is nearly empty, evaluate the draw-loss deadline and any legal recycling before optional draw or a multi-turn plan. | Emptying the Runner's stack does not by itself win; Runner attrition requires preventing safe progress or causing a flatline. |
| C23 [B,A,M] | When deciding a mulligan, favor a feasible opening plan: usable economy, affordable relevant protection, and a path toward your deck's threats. | Excess agendas or only unaffordable ICE often justify replacing the hand. Low-ICE combo and horizontal decks must use their own opening requirements rather than a mandatory ICE count. |
| C24 [B,M] | If a play gains money or points at the cost of bad publicity, compare its immediate window with the extra run funding it grants over the expected remaining game. | Bad publicity refreshes its benefit across runs; it can undermine a long taxing plan more than a short rush finish. Use exact payment eligibility from the engine. |

## Rough trade-offs for evaluation

These are proposed approximations [T]. Recompute their inputs from the game state. They are useful for ranking plans, not substitutes for resolving card effects.

| Comparison | Starting calculation | Important correction |
|---|---|---|
| Basic credit versus draw | Compare one immediate credit with the expected value of cards that can become useful before the deadline. | Draw becomes less attractive with unusable cards or forced discard; a single credit can cross a crucial affordability threshold. |
| Long-term economy | Expected payouts before disruption or game end minus install and activation costs, compared with spending those clicks elsewhere. | A nominal profit after six turns is irrelevant if a likely winning score arrives in two. |
| Breaker upgrade | Expected number of relevant future encounters × savings per encounter, versus installation, acquisition, and memory costs. | Example: a 6-credit upgrade saving 2 per encounter needs three encounters just to repay its credit cost; clicks make true break-even later. |
| Asset removal | Future income and strategic effects denied versus entry, trash, recovery, and sacrificed opportunities. | Example: denying four likely 1-credit payouts for a 3-credit trash and a click is not automatically favorable; the sides' money has different urgency. |
| Run investment | Expected win or denial value + information + useful triggers, minus all resource and risk costs. | A 5-credit run with three new accesses amortizes entry better than three 5-credit runs with one each, but compare preparation cost and survival too. |
| Repeated remote contest | Count every required traversal and every activation/access cost. | If an ability forces a second run, an apparent 7-credit route may require 14 credits and two run clicks, before other costs. |
| Damage versus breaking | Compare credits saved with replacement draws, lost valuable cards, prevention use, and increased kill risk. | One net damage is not always “one click”: it can trash a unique answer or consume the only remaining safety margin. |
| Extra ICE | Expected reduction in breach probability or repeated tax relative to install, rez, card, and opportunity costs. | A layer that costs the Corp 7 to establish but the Runner 1 to pass needs a concrete defensive purpose. |

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

Archetypes overlap. A deck can be a regular toolbox with a denial package or a big rig with a central burst finish. Historical archetype catalogs document these different engines and payoff patterns; they do not establish current card legality. [S15] The condition/action policies below are proposed adaptations.

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

## Corporation archetype modules

Published archetype discussions distinguish rush, glacier, fast advance, kill, horizontal assets, shell games, grinders, prison, and combo, while emphasizing hybrids. [S10, S16, S17] The following modules translate those categories into conditional priorities; use only the modules your card pool supports.

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

## Translating this into a policy

This is a proposed implementation outline, not a result established by the strategy sources.

1. **Generate legal turn plans and contingent responses.** Let the rules engine determine legality, costs, triggers, and terminal outcomes. Include economy, draw, installation, runs, scoring, and disruption.
2. **Check tactical wins and avoidable immediate losses.** Search complete lines. A high-value credit action should not outrank a certain win, nor hide an obvious lethal reply.
3. **Evaluate uncertain plans over plausible hidden states.** Use observable history and card-pool knowledge. Preserve low-probability dangerous outcomes without acting as though every unknown card is the worst possible card.
4. **Apply the base considerations.** Reward improved win prospects, denied opposing progress, useful information, and reusable capacity. Charge actual resources, exposure, and recovery.
5. **Adjust for archetype and matchup.** Add urgency for rush windows, reserves against tag punishment, engine priorities against horizontal decks, and so on. Evidence should update matchup beliefs during play.
6. **Replan after information changes.** A draw, rez, access, trace result, or unexpected defense can invalidate the rest of a turn.

A rule record could contain `id`, `applies_when`, `candidate_action`, `expected_benefit`, `resource_cost`, `risk`, `exceptions`, `archetype_weights`, `matchup_weights`, `confidence`, and `source_ids`. Keep hard legality outside heuristic weights. Prefer probabilistic action selection among similarly valued bluffing lines to a deterministic install pattern the opponent can learn perfectly.

For initial evaluation, create paired positions that differ in one strategically meaningful variable: the Corp can versus cannot afford a rez; a run leaves versus removes remote contest capacity; R&D's next access is fresh versus known; an asset has one versus several payouts left; the Runner has versus lacks tag-removal time. The desired action should change only when the altered fact changes the relevant trade-off. These are suggested policy checks, not tests already performed.

## Sources and research notes

Sources were consulted for strategic concepts, not for a current tier list. Older lists and card examples may be rotated, banned, or incompatible with the selected format. Some articles make deliberately strong teaching claims; this guide does not adopt claims such as “always force rezzes” or a fixed number of accesses guaranteeing victory.

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

Rules-sensitive statements were also cross-checked against the [Null Signal Games comprehensive rules](https://rules.nullsignal.games/). This is a supporting reference for timing and survival, not the organizing framework of the guide. The configured engine and card text should determine each concrete interaction.
