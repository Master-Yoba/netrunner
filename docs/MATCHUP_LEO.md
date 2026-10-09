# Core Battle Box: Runner matchup guides

## 03 — LEO Construction: Glacier

**Default plan:** attack before the defensive upgrades accumulate, develop efficient breakers and economy, and make important attacks with enough resources for more than one run in the same turn. LEO’s first defense is often a sacrificed card rather than an ICE subroutine. A single expensive run on your last click is exactly the attack this deck wants you to make.

This guide targets the 49-card list in the [published LEO deck image](https://cardgamer.com/wp-content/uploads/2026/07/LEO.png) from Card Gamer’s Core Battle Box article, inspected on 9 October 2026. The live linked NetrunnerDB page could not be fetched. Treat this as a policy for that snapshot. Numerical priorities below are my suggested defaults, not empirical win rates; card effects and worked arithmetic are factual constraints.

### Deck snapshot

| Type | Published list |
|---|---|
| Identity | LEO Construction: Labor Solutions |
| Agendas — 9 | 1 Luminal Transubstantiation; 3 Offworld Office; 3 Project Ingatan; 2 Send a Message |
| Assets — 6 | 3 Humanoid Resources; 3 Spin Doctor |
| Operations — 12 | 2 Hansei Review; 3 Hedge Fund; 2 Petty Cash; 2 Predictive Planogram; 3 Seamless Launch |
| Upgrades — 6 | 1 Anoetic Void; 2 Manegarm Skunkworks; 3 Mercia B4LL4RD |
| ICE — 16 | 3 Brân 1.0; 3 Palisade; 2 Flyswatter; 2 N-Pot; 1 Ansel 1.0; 3 Bumi 1.0; 2 Tithe |

There is no Measured Response, tag punishment, or access ambush in this list. Do not hold an automatic eight-credit damage reserve against it. Your resources are primarily needed to **reach the server repeatedly, preserve installed cards, and defeat upgrades**. Damage comes from Tithe and Bumi; Bumi’s core damage also permanently reduces maximum hand size.

### The defensive cards that change the plan

| Threat | Exact strategic constraint | Default response |
|---|---|---|
| **LEO identity** | Once per turn, trash a rezzed bioroid at the attacked server to end the run. Breaking its subs does not stop this. | Plan a valuable first attack and an affordable follow-up in the same turn. |
| **Mercia B4LL4RD ×3** | Rez 2, trash 2; bioroid upgrade. Installs ICE at action-phase end with a 1-credit install discount, then moves to that server. | Remove it early when affordable. It both grows servers and supplies cheap LEO sacrifice material. |
| **Anoetic Void ×1** | Rez 0, trash 1. At approach to the server, paying 2 credits and trashing two HQ cards ends the run. Repeatable. | Count HQ cards and credits. A breaker-complete rig is insufficient while the Corp can keep paying. |
| **Manegarm Skunkworks ×2** | Rez 2, trash 3. Each approach requires two clicks or 5 credits, otherwise the run ends. | Budget the tax for each attempt, plus a follow-up. Do not spend the last useful clicks on ICE first. |
| **Seamless Launch ×3** | Cost 1, places two advancements on a card installed before this turn. | Treat a card left unadvanced in the scoring server as a potential next-turn agenda. |
| **Ansel 1.0 ×1** | Can trash an installed Runner card, install a Corp card, and prohibit stealing/trashing for the rest of the run. | Stop the no-steal/no-trash sub on a scoring or removal run, even if no sub explicitly ends the run. |
| **Bumi 1.0 ×3** | Program trash plus core damage; rez can trash an installed trojan. | Preserve clicks or a killer. Do not casually accept permanent damage to save a few credits. |

The identity’s sacrifice mechanic is documented in [Null Signal’s LEO playtesting article](https://nullsignal.games/blog/bounty-hunters-and-bioroids-in-elevation-playtesting/). Here the cheap bioroid upgrade is particularly important: the Corp need not sacrifice its best ICE to stop you.

### Opening and mulligan defaults

**LE01 — Keep economy plus draw or an efficient setup engine; do not require a breaker in every opening hand.** The long-term task is repeated affordable access. A hand with playable income, draw and a route to breakers is usually better than three expensive breakers and no way to fund them. A cheap early fracter or killer is a useful bonus.

**LE02 — Run exposed valuable targets before spending multiple turns on setup.** Prefer a cheap early Mercia/Humanoid trash or fresh central access over an optional install. This is a setup matchup, but an uncontested Mercia makes future setup races worse. Your aim is to develop while imposing useful costs, not to wait until every ICE type is covered.

**LE03 — Start serious remote attacks on click 1.** This leaves three clicks after initiating the run. Those clicks can pay bioroid subs, Manegarm, or another run—but cannot pay all of them at once. If you have already spent two setup clicks, a newly attractive remote may no longer be a viable contest.

**LE04 — Prefer permanent breaker coverage when click-breaking would consume the whole turn.** Early click-breaking is useful to steal a timely agenda or force a meaningful sacrifice. Repeating it every turn against a growing server usually gives the Corp time to score. An efficient fracter and killer deserve early investment; a decoder becomes important as N-Pot gains live subroutines.

### Getting through LEO rather than merely through ICE

**LE05 — Before declaring a run decisive, ask what LEO can sacrifice.** Eligible cards in this snapshot are Brân, Bumi, Ansel and Mercia, provided they are rezzed and in/protecting the attacked server. Humanoid Resources is **not** a bioroid despite its name. Palisade, Tithe, N-Pot and Flyswatter are not bioroids either.

A bioroid you already passed still protects the same server and can be sacrificed. A facedown Mercia can be rezzed for 2 and then sacrificed at a legal opportunity. Do not mark an unprotected remote “guaranteed access” merely because it has no ICE.

**LE06 — Prefer two meaningful attacks in one turn to one meaningless probe plus one real attack.** The Corp can decline to spend LEO on a low-value run. Attack a central with useful multiaccess, an important upgrade, or the scoring remote itself. If it lets you in, take value; if it sacrifices, reassess and use the exhausted identity on the follow-up.

LEO’s once-per-turn limit is global, not per server. Spending it on HQ opens a window elsewhere that turn. It resets next turn: one run every turn may repeatedly meet a fresh denial ability.

**LE07 — Price the second run before paying for the first.** Example: a remote costs 4 credits to break and contains only Mercia as LEO fuel. If the first attempt is stopped by sacrificing Mercia, a second attempt still costs the ICE toll. Plan roughly 8 credits and two run actions, not 4 credits and one action. If the Corp sacrifices an ICE instead, recalculate the now-cheaper second route rather than charging the old toll twice.

Do not assume the cheapest sacrifice: the Corp may preserve ICE and spend Mercia. Also do not assume two runs suffice when Anoetic Void is present.

**LE08 — Take the value when the Corp refuses the bait.** If a productive HQ attack succeeds and LEO remains unused, you have not earned a guaranteed remote run. Decide again. Do not force a doomed remote attempt merely to complete a prewritten sequence.

### Anoetic Void and Manegarm budgets

**LE09 — Count possible Void activations from HQ and money.** With no intervening draw/recursion, the upper bound is `min(floor(HQ cards / 2), floor(available credits / 2))`. Five HQ cards and six credits support at most two activations. Each costs both resources. If the Corp wants to retain particular cards, actual willingness may be lower; the maximum is still the relevant hard denial bound.

The bound must include other spending and effects. Ansel can install cards, Spin Doctor can supply cards, and a new rez may change the calculation. Do not apply the simple formula through a sequence that changes its inputs.

**LE10 — Never promise access because Void is “only once.”** It is not once per turn. With two payable Void activations plus unused LEO fuel, the Corp can potentially stop three attempts. A fourth attempt is the optimistic action-count requirement, before paying bioroid or Manegarm clicks. If that is not affordable, pressure a different server or rebuild; do not spend the whole pool to learn that the third denial also exists.

**LE11 — Trash an accessed Void for 1 almost automatically.** It is an exceptionally cheap removal of repeatable access denial. Reaching it usually means the Corp declined or could not activate, or your card effects bypassed the obstacle. Trash it while that opportunity exists, unless doing so directly prevents the winning access you can otherwise take. Ansel and Ingatan may recur it later; track its location.

**LE12 — Against one Manegarm, preserve either two clicks at approach or 5 credits beyond the ICE cost.** Without other click spending, a click-1 run arrives with three clicks: pay two and one remains. A click-2 run arrives with two: pay two and no follow-up remains. Click-3/4 attempts normally need the 5-credit option.

When LEO or Void can still stop you, prefer paying 5 if the two-click choice would eliminate the necessary next attempt and you can afford that attempt. When this is the final genuinely sufficient attempt, paying two clicks is often preferable to paying 5. The default changes with the remaining denial, not merely your current bank.

**LE13 — Count taxes per approach and per copy.** Both Manegarm copies can be installed at the same server; each demands its own payment. Two copies can cost 10 credits, or a legal mixture of credits and clicks. Trashing one after access does not refund earlier taxes. The Corp can also arrange its approach-trigger resolutions so you pay a tax before Void ends the run; budget for that unfavorable order.

**LE14 — Do not spend bioroid clicks that were reserved for Manegarm or a rerun.** A click-1 run has three clicks after initiation. Spending two to break Brân’s end-run subs leaves only one; you can no longer pay Manegarm’s two-click option. Either pay some ICE costs with a breaker or have 5 credits available at the server. Reject plans that spend the same clicks twice.

### Scoring windows: unadvanced does not mean harmless

**LE15 — Check a newly installed scoring-server card when the route is economically viable.** One Seamless plus one manual advance scores a previously installed 3/2 for two actions and 2 credits. A 4/2 at zero can use Seamless plus two advances, or two Seamless. Two Seamless plus one advance score a previously installed 5/3 for three actions and 3 credits. There need not be advancement counters telegraphing the threat.

Seamless cannot target a card installed that same turn. Installing an agenda from HQ and then using Seamless on it is not a legal fast-advance line. This distinction makes the Runner’s intervening turn the crucial contest window.

**LE16 — Prefer denying the first Offworld Office over an equal-cost speculative central access.** The score gains 7 credits, funding defensive rezzes and Void activations. A steal denies both points and the refill. Accept spending a substantial fraction of the pool if the whole run sequence genuinely reaches the agenda; an unaffordable second attempt is not a successful contest.

**LE17 — Track Ingatan’s counters and Archives.** Each excess advancement gives a counter; at discard end a counter can install a card from Archives without install costs. It can rebuild sacrificed bioroids or upgrades. When the Corp has an overadvanced Ingatan, early trashing still has value, but no longer guarantees permanent removal. Record known rebuilt cards instead of assigning them a generic hidden-card identity.

**LE18 — Expect Luminal and Send a Message to rebuild the defense.** Luminal is a 3/2 whose score grants three clicks but prohibits further scores that turn: it can fund/install, not immediately score another agenda. Send a Message’s score or steal can rez an installed ICE free. After a non-winning steal, update future access costs and LEO fuel. A credit-starved Corp may acquire a rezzed bioroid without paying its printed rez cost.

### Grip and ICE subroutines

**LE19 — Keep only the damage buffer the route needs, plus a practical margin.** A known Tithe packet needs one card; two unbroken Tithes need two. Bumi’s core damage deals a damage card and lowers future maximum hand size. On an uncertain early route, three to four cards is a reasonable working target; it is not immunity to arbitrary multiple encounters. With complete safe breaking, there is no reason to copy PT Untaian’s five-card-access requirement.

**LE20 — Pay modest costs to avoid the first core damage.** Prefer a spare click or an affordable break to taking it for a routine access. It permanently reduces future grip capacity and can discard a key card now. Accept it for a calculated winning/necessary line, not as the default cheap way through Bumi.

**LE21 — Protect your installed engine from Bumi and Ansel.** The Corp chooses the relevant trash target. Do not model a spare program as a shield for your only useful breaker. Bumi’s on-rez trojan trash is separate from its subs; breaking the program-trash sub does not undo that rez effect. Flyswatter likewise purges on rez, independently of its end-run sub.

| ICE | Numbers and special concern | Default |
|---|---|---|
| Brân 1.0 | Rez 6, strength 6; install inward plus two end-run subs. | Break the install sub when a new inner defender ruins the plan. Budget clicks across the whole route. |
| Palisade | Rez 3; strength 4 on remotes, 2 on centrals. | Good early fracter target; use the correct strength. |
| N-Pot | Rez 4, strength 4; live end-run subs at all threats, threat 2+, and threat 4+. Can pay 3 per sub without a decoder. | Native payment costs 3 / 6 / 9 at threat below 2 / 2–3 / 4+. Use it for a timely early access, not as the permanent plan at nine per encounter. |
| Ansel 1.0 | Rez 6, strength 4; three subs, each can be click-broken. | On an agenda/asset run, stop the last sub or the run cannot achieve its ordinary steal/trash purpose. |
| Bumi 1.0 | Rez 3, strength 3; program trash and core damage; click-breaking available. | Reserve two clicks or an effective killer. Consider the rez-triggered trojan loss separately. |
| Tithe | Rez 1, strength 1; one net damage and Corp gains 1. | Let the credit gain happen if breaking it costs more than the denial is worth and it does not fund a critical defense. Preserve enough grip for damage you allow. |
| Flyswatter | Rez 2, strength 0; on-rez purge and end-run sub. | Recompute virus-assisted strength/cost before spending on later ICE. |

**LE22 — Do not finish an Ansel-disabled run just because access is possible.** If stealing/trashing is prohibited, a further expensive approach usually has little value. Continue only for concrete information, rewards, or another allowed effect. If the remaining path costs nothing, information may still justify success; the key is not to value the inaccessible agenda as stolen.

### Trash priorities and useful spending defaults

**LE23 — Accessible defense removal normally beats one more single central access.** Default ordering, after urgent wins/scores: Void for 1; Mercia for 2; Manegarm for 3; Humanoid for 1; Spin Doctor when its remaining effect is actually deniable. Reorder for the current server: Manegarm blocking every relevant route may outrank Mercia elsewhere.

For a fresh exposed Mercia, I would normally pay its 2-credit trash plus another 1–2 in route toll. It denies repeated installations and a cheap LEO stop. An exposed Humanoid for a 1-credit trash is also attractive. Do not extrapolate these preferences into spending ten credits on a secondary asset while a winning agenda sits elsewhere.

**LE24 — Build toward a whole-turn access budget, not a fixed bank target.** If the route requires 4 to break, 5 for Manegarm, and may be stopped once without reducing the route cost, budget near 18 for two paid attempts. If you can pay the second Manegarm with spare clicks, the need is lower. If LEO sacrifices an expensive ICE, it may fall sharply. “I have ten credits” says little without this sequence.

### Worked choices

**Mercia in an otherwise unprotected scoring remote; LEO unused.** Do not count one run as guaranteed. The Corp can rez Mercia for 2 and sacrifice it. Run early with a second action available. Either Mercia is removed or you may access; if the agenda remains, follow up after recalculating.

**Brân plus Manegarm; no fracter; click 1.** Clicking both end-run subs consumes two of the three remaining clicks. Manegarm then requires 5 credits because only one click remains. Brân’s install sub can make the route worse. This is not a “free bioroid click-through.”

**Five HQ cards, 6 credits, live Void and LEO.** In the simplified state, two Void stops and one LEO stop can require four attempts. If your resources cover only two, choose another pressure line or prepare; do not launch a supposedly decisive two-run plan. Any additional draws/installs require a new calculation.

**Unadvanced card survives in the remote; Corp has 3 credits.** It can be a 5/3 scored through double Seamless plus one advance next turn. Treat it as potentially urgent despite zero counters. Conversely, an agenda newly installed on that next turn is ineligible for Seamless that turn.

### Runner-specific knobs

| Runner | Adjustment |
|---|---|
| Topan | Efficient installs help build the repeat-run rig. Avoid weakening grip before Bumi/Tithe, but do not keep an unnecessary Response reserve. Flyswatter can undermine virus-based break quotes. |
| MuslihaT | Use productive central runs to force choices about the global LEO ability. A cheap run that the Corp happily allows does not consume the identity. Prefer first attacks with a real payoff. |
| Magdalene | This is the strongest case for investing in an efficient mature rig, but still remove cheap Mercia and contest old remote cards. Plan both resource growth and the full sequence needed to breach. |

### Mistakes to penalize; mistakes to punish

| Runner error | Severity |
|---|---|
| Spending everything on one final-click run while a known LEO sacrifice stops it | High; critical if it concedes an avoidable winning score |
| Counting the same clicks for bioroid breaks, Manegarm and a second run | High |
| Treating Void as once per turn or ignoring its HQ-card cost | High |
| Letting Ansel’s last sub resolve while valuing the run as an agenda steal | High |
| Ignoring an old unadvanced remote despite a viable Seamless scoring line | High |
| Taking routine core damage to save a small affordable break cost | Medium–high |
| Paying PT-style damage reserves against a list with no operation kill | Medium |
| Treating Humanoid as bioroid fuel, or passed ICE as ineligible LEO fuel | Model error to correct, not merely a small preference penalty |

| Corp mistake | Runner punishment |
|---|---|
| Uses LEO early and leaves a valuable second target affordable | Attack again before the turn resets. |
| Leaves Mercia or Void accessible | Trash during that access window. |
| Overprotects a remote and leaves a central efficient | Take productive central accesses rather than endlessly paying the remote tax. |
| Spends below 2 credits or exhausts HQ so Void cannot activate | Recompute and exploit the opening before a refill. |
| Sacrifices an ICE that greatly reduces the route toll | Use the cheaper same-turn rerun. |
| Leaves a newly installed scoring card outside adequate defense | Steal before Seamless makes it a score next turn. |
| Lets its expensive bioroid be approached early while you have spare clicks | Use click-breaking where it genuinely produces access or a valuable sacrifice. |

### Policy state and research

Track LEO’s global once-per-turn state; eligible rezzed/potentially rezzable bioroids by server; remaining clicks at each encounter and approach; Void money/HQ capacity; each Manegarm copy; card installation turn for Seamless; Ingatan counters and known Archives contents; and on-rez versus subroutine effects. Calculate the price of the complete attempt sequence. Never label “all ICE breakable” as equivalent to “agenda reachable.”

Sources: [Card Gamer’s article](https://cardgamer.com/features/introducing-the-netrunner-core-battle-box/) and published list image; [Null Signal’s LEO design/playtesting account](https://nullsignal.games/blog/bounty-hunters-and-bioroids-in-elevation-playtesting/); directly inspected [Elevation card data](https://github.com/NetrunnerDB/netrunner-cards-json/blob/master/pack/elev.json) and [System Gateway card data](https://github.com/NetrunnerDB/netrunner-cards-json/blob/master/pack/sg.json). Recommendations, budgets, ordering and penalties are my analysis of this specific list. They should be tuned through bot games; the underlying payment/timing constraints should be enforced by the rules engine.
