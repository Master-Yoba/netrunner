# Bot strategy 1 (4 October 2026)

After bot loops 1 and 2 ([BOT_LOOP_1.md](BOT_LOOP_1.md), [BOT_LOOP_2.md](BOT_LOOP_2.md)), the user asked for the problems
the loops had deferred as larger than a fix: the Runner's self-flatlines, the Corp's sequencing and IP Enforcement, how
the Corp exposes agendas, and how much the Runner fears unknown ICE. No LLM games were played. The evidence is the loops'
recorded games and the Core Battle Box ladder's saved games, probed decision by decision.

The results are **Runner v26** and **Corp v30**. The verification order these runs taught is now in
[BOT_LOOP_WORKFLOW.md](BOT_LOOP_WORKFLOW.md) (section 7).

## Runner self-flatlines

On the Core Battle Box, Standard Runner v25 lost 12% of its games to its own flatline against Corp v29 (v22: 41%). 50 of
v25's 58 were Measured Response: at threat 4, after a successful run, the Corp does 4 meat damage unless the Runner pays
8. The Runner's choice is forced by then, so the fixes are in the turn before:

- **Draw up after a successful run.** When the Corp may hold an operation that punishes a successful run, and the Runner
  can neither pay it off nor survive its damage with its grip, drawing comes first, weighed by the chance the Corp holds
  one: the operation's share of the Corp's unseen cards over a hand of them, and at least one in three once seen.
- **Run events leave the grip.** A run event's trap risk is priced against the grip without the event; Clean Getaway was
  played into Byte! as if the Runner still held it.
- **Match point with an empty grip draws first.** With the Corp one agenda from winning, an advanced remote is run
  whatever the trap risk; not while the Runner has a card or none and clicks to draw first.
- **A single-use breaker is spent by the run.** Mayfly is trashed when the run ends. The encounter planner counted that,
  the run's valuation didn't, so the Runner started runs through Mayfly and then wouldn't spend it (17 times in one game;
  the run model's validation fell to 80%).

Tried and dropped, by ladder against v25:

| Variant | Box Elo | Wins | Self-flatline | Runs/turn |
|---|---|---|---|---|
| A fixed penalty on runs that arm the punisher, a card of margin | −15 | 31% | 7% | 0.59 |
| The penalty weighed by the chance, no margin (Corp v29) | +12 | 35% | 9% | 0.64 |
| The same against Corp v30, with a draw to run an advanced remote safely | −22 | 28% | 9% | 0.60 |
| Without that draw | −13 | 29% | 8% | 0.62 |
| Without the run penalty or the draw (Corp v30) | +6 | 31% | 10% | 0.65 |
| **Kept: the draw after a successful run only** | **+3** | **31%** | **8%** | **0.64** |

The run penalty saved games against Corp v29, but cost about as many wins as it saved once Corp v30 kept its agendas in
HQ. The draw after a successful run costs no tempo: it spends clicks the turn has left.

The remaining access flatlines are judgment calls: a coin-flip ambush (Byte! is about 3 of the 90 root cards a Standard
Runner, which knows only the pool, can expect), and runs at match point. A Runner that knew the Corp's list (Hard) would
do better there; Standard and Easy know the pool by design.

## Corp sequencing and IP Enforcement

- **Now-or-never plays first.** Petty Cash can be played only before the turn's first action finishes. Installs made
  first forfeited it; from Archives it refunds its click, a free 2 credits. A positive play or ability with that
  condition gets a bonus over the turn's other plays, which stay available after it.
- **IP Enforcement.** Its install of an agenda from the Runner's score area was worth nothing, so the card was never
  played, including a missed win (loop 2, game 001 #837). It is now worth the points the Runner loses and the agenda the
  Corp holds again, and a win when the Corp can score it this turn.
- **Mercia's free install.** The only ICE in HQ isn't installed by hand while a rezzed Mercia B4LL4RD will install it at
  the end of the action phase, a credit cheaper and without a click.

## Agenda exposure (Corp)

Corp v23 pushed one agenda out of HQ into a remote the Runner could break whenever it held two and HQ was no safer. An
HQ access takes one random card, but an installed card in a breakable remote is taken outright. The agenda now waits in
HQ until agendas fill 60% of it (loop 2: Off the Books behind a lone Kessleroid against Principia; Embedded Reporting
behind Whitespace against Buzzsaw).

## Unknown-ICE fear (Runner)

- The loops' jack-outs past unrezzed ICE were fixed in v25 (the passed ICE counted as ahead).
- An advanced remote whose possible trap would flatline the grip is left alone while the grip is small. A draw to make
  such a remote safe to run, when the clicks allow both, was tried and dropped: it cost tempo on the ladder (above).

## Deferred

- **Drawing to run an advanced remote safely**, and **holding back runs that arm a punisher**: both cost more than they
  saved against Corp v30 (above).
- **Touch-ups** against basic advances, **Synapse Global**'s tagged gain, **Spin Doctor** before the install that
  trashes it: each needs a sequence comparison the heuristic doesn't make.
- **Knowing the Corp's deck.** Standard's prior is every Corp card in the pool, three copies each. A prior conditioned on
  the Corp's identity or faction would sharpen every risk above, but the AI doesn't see factions and the tiers' design
  keeps the list for Hard.

## Verification

- **Ladders** (240 deals each):

  | Change | Core Battle Box | Gateway |
  |---|---|---|
  | Corp v30 against v29 | **+50 Elo** (95% [10, 96]), separated | −8 ([−48, 38]) |
  | Runner v26 against v25 | +3 ([−35, 52]) | +3 ([−28, 40]) |

- **Runner diagnosis** on the box ladder (both against Corp v30): v26 wins 31% and loses 8% to its own flatline; v25 30%
  and 8%. Against Corp v29, v25 lost 12% to its own flatline; Corp v30's play leaves fewer of those positions.
- **Fuzz:** the engine and card specs are unchanged in this work; the per-slice fuzz on all four pools passed on 45c49b3.
- **Gates** (`make gates` on 17f83a3): verify, m5-gate, tactics and bot-check pass.
- **Regression tests:** `BotStrategy1Tests` (8 cases) at Core Battle Box ladder decisions where the versions differ.
