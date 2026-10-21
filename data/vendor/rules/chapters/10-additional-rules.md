# 10. Additional Rules

_Netrunner Comprehensive Rules v26.03 · [source](https://rules.nullsignal.games/#chpt_additional_rules) · retrieved 2026-09-01_


## 10.1. General

**10.1.1.** Some cards are unique, and have a unique symbol (◆) before their name to designate this. There can be only one unique card of the same name active at a time. If a unique card becomes active, any other card that shares its name is trashed during the next checkpoint. Trashing a card this way cannot be prevented. See section 10.3.

**10.1.2.** To purge virus counters is to remove all virus counters hosted on cards and return them to the bank. The Corp can purge virus counters as a basic action or with a card ability.

**10.1.3.** Some abilities add a card to a player's score area "as an agenda". When this happens, the card loses all its previous properties and gains only those properties specified in the effect converting it. This conversion lasts until the card moves to a zone that is not a score area, at which point it returns to being its original printed card. If this happens in any way other than by agenda forfeit, the card is immediately trashed. See rule 8.2.5.

**10.1.4.** Some abilities can convert a card into a counter. When this happens, the card loses all its previous properties and gains only those properties specified in the effect converting it. This conversion lasts until the counter moves to another zone, at which point it reverts to being a card, regains its original printed characteristics, and is trashed.

**10.1.5.** Some cards are written with self-referential language. If a card references its own name without using the word "copy", treat that name as if it said "this object".

  - Example: The subroutine on the original printing of Kitsune includes the phrase "trash Kitsune." This phrase means "trash this ice.".
  - Example: Boomerang's text refers to "a copy of Boomerang". This is not self-referential language and refers to any card named Boomerang.

**10.1.6.** Infinite Loops


- a. If a mandatory infinite loop is created (a player cannot choose to stop resolving the loop) then the player who is resolving the loop chooses a number. The loop instantaneously resolves that many times, and then ends.

  - Example: The Runner runs into a rezzed Wormhole. The only other piece of ice that is rezzed is a Wormhole, and so a mandatory infinite loop is created where each of the Wormholes' subroutines resolves the other. The Corp chooses how many times this loop occurs, say 2,157 times, and then the Runner continues the run.

- b. If an optional infinite loop is created (a player can choose to stop resolving the loop) during a run, then the Runner must jack out unless another card ability prevents them from doing so. If the Runner cannot jack out, then it is the Corp's responsibility to end the loop by letting the Runner continue the run.



## 10.2. Information


The game, at its core, is about information. Much of the game revolves around deducing information about the opponent's cards and strategy.

**10.2.1.** Information in the game is classified as hidden information or open information.

**10.2.2.** Hidden Information


- a. hidden information is any information about the game, game state, or cards unavailable to one or more players. This includes facedown cards in play or in Archives, cards in HQ or R&D, cards in the Runner's grip or stack, etc.

- b. A player cannot learn hidden information without the aid of a game effect, rule, or another player verbally communicating the information. However, if a player that has access to information about the game or a card chooses to verbally share it with their opponent, that player is not required to tell the truth. Bluffing is allowed.

  - Example: The Runner uses Indexing. While rearranging cards, the Runner places Braintrust on top of R&D, followed by Snare!. The Corp draws at the beginning of their next turn, then spends their first click to draw a card. For their second click, the Corp installs a card in an empty remote server, telling the Runner that they should run it because it is a Braintrust.


**10.2.3.** Open Information


- a. open information is any information about the game, game state, cards, or abilities that is available to both players. This includes faceup cards in Archives and the heap, the number of cards in HQ, R&D, the stack, and the grip, the number of credits in a credit pool, and any other information continuously available to both players.

- b. Open information cannot be hidden from an opponent. A player must allow their opponent to discover the information themselves if they attempt to do so.

  - Example: The Runner installs Femme Fatale, choosing a piece of ice protecting HQ with Femme's ability. The Runner must explicitly state to the Corp which ice has been chosen and must continue to do so if the Corp asks for Femme Fatale's target during a later turn.



## 10.3. Checkpoints

**10.3.1.** A checkpoint is a process wherein objects that have entered an illegal state are corrected, expired effects are removed, and other important conditions are checked. This procedure is automatically performed at several timing points during the game, and entails the following steps, which are performed in order:


- a. Each active conditional ability looks at the changes to the game state since the beginning of the last checkpoint to see if its condition has been met. Any ability that has met its condition creates the appropriate instances of itself and marks them as pending, as described in section 9.6.

- b. Any ability with a duration that has passed is removed from the game state.

  - Example: During the checkpoint that follows the end of an encounter, abilities that increased the strength of an icebreaker during that encounter expire and the icebreaker's strength is correspondingly lowered.
  - Example: The Corp has Targeted Marketing in the play area when the Runner steals an agenda. In the next checkpoint, the game recognizes that the ability on Targeted Marketing that was preventing the operation from being trashed no longer applies, and so the Corp trashes Targeted Marketing as if it were completing its resolution.

- c. If the agendas in either player's score area total 7 or more agenda points, that player wins the game. If both players would win this way simultaneously, the game ends in a draw.

- d. If 2 or more unique (◆) cards with the same name are active, for each such name, all of those cards except the one that became active most recently are trashed. If 2 or more console cards are installed under the control of the same player, for each such player, all of those cards except the one that became active most recently are trashed.

- e. If any objects break any restrictions of card abilities or the game rules (such as the Runner's memory limit) or are installed or hosted in an illegal location, an appropriate set of those objects are trashed. A set is appropriate for this purpose if trashing the objects in the set would leave all remaining installed or hosted objects in legal locations and if no object can be removed from the set while maintaining that property. If there are multiple distinct appropriate sets, and one player controls all the objects in each of those sets, that player chooses which set to trash. If the sets contain objects controlled by both players, the active player chooses which set is trashed.

  - Example: The Runner controls a Chisel hosted on an unrezzed piece of ice. The Corp rezzes that ice, revealing it to be Tithonium. Since Tithonium has an ability prohibiting cards from being hosted on it, Chisel is now installed in an illegal location and must be trashed.
  - Example: The Runner has installed programs with total memory cost equal to their memory limit when the Corp plays Bad Times, reducing the memory limit by 2{MU}. The Runner can trash any two programs with a memory cost of 1{MU} each, or any one program with a memory cost of 2{MU} or greater. They cannot trash any 0{MU} programs, nor can they trash any set of more than one program with a greater total memory cost than 2{MU}, since those objects can be removed from the trashed set while maintaining that the result will be a legal game state.

- f. Any objects that were hosted on an agenda that moved from a score area to any zone other than a score area are trashed.

- g. Any objects that were hosted on an installed card that was uninstalled are trashed, except for cards or counters that are temporarily in the set-aside zone because of rule 9.5.5. This step is repeated until no more cards or counters are trashed.

- h. Any remote server with no cards protecting it, in its root, or in the process of being installed with a destination protecting it or in its root ceases to exist.

- i. Any position protecting a server that is not occupied by a piece of ice ceases to exist, except if it is the Runner's current position or if there is a piece of ice in the process of being installed protecting that server. See also rule 6.2.4.

- j. If a server is being breached and 1 or more cards entered the root of that server since the previous checkpoint, for each such card, the Runner declares whether or not it becomes a candidate for access in this breach. See also section 7.4.

  - Example: The Runner accesses Ganked! and encounters Drafter in the middle of breaching a server. The second subroutine on Drafter resolves, and the Corp uses its effect to install a card in the root of the server being breached. When the installation is completed, the Runner declares whether or not the installed card becomes a candidate. If it does become a candidate, the Runner will select it for access later in the breach.

- k. Any cards in discard piles that had been converted into counters or agendas return to their printed characteristics.

- l. Any counters in a discard pile are returned to the bank.


**10.3.2.** After a checkpoint is completed, if it marked any instances of conditional abilities as pending, a new reaction window immediately opens to handle those abilities, even if this takes place during another reaction window. After a reaction window closes, the game proceeds to whatever was due to occur after the checkpoint that caused it to open.

**10.3.3.** Whenever a player would receive priority, first a checkpoint occurs. See also section 9.2.4.

**10.3.4.** Whenever a player pays a cost, a checkpoint occurs immediately afterward, before continuing with the effect or timing structure in which the cost was paid. See also section 1.16.

**10.3.5.** Whenever an instruction finishes resolving, a checkpoint occurs immediately afterward, before the next instruction becomes imminent. See also section 9.11.

**10.3.6.** The checkpoint following the last step of a timing structure takes place outside of that timing structure, as does any associated reaction window.

  - Example: The Runner, playing as Jesminder Sareen: Girl Behind the Curtain, steals an agenda during a run on a server with AMAZE Amusements rezzed in its root. Since AMAZE Amusements's ability meets its trigger condition when the run ends, it will become pending in the reaction window following step 6.9.6d and attempt to give the Runner 2 tags. During this window, the run timing structure has closed, and Jesminder's interrupt ability cannot apply to those tags.



## 10.4. Damage

**10.4.1.** Many cards and ice subroutines inflict damage on the Runner. The Runner suffers (sometimes referred to as "takes") damage according to the rules for the type of damage specified. If a Corp card "does" damage to the Runner, they suffer that much damage, with the additional provision that the Corp and the source card are responsible for that damage. If a card directs or allows the Runner to "suffer" damage, then the Runner and the source card are responsible for that damage.

  - Example: The Corp, playing as Argus Security: Protection Guaranteed, has scored The Cleaners. The Runner makes a run on R&D and steals Hostile Takeover. Argus Security's ability triggers and the Runner chooses to suffer 2 meat damage. As The Cleaners only adds additional damage to damage done by the Corp, its ability does not apply. When the Corp plays Punitive Counterstrike on their next turn, The Cleaners does increase the amount of damage done by Punitive Counterstrike.

**10.4.2.** The three types of damage the Runner can suffer are meat, net, and core damage


- a. meat damage and net damage are resolved the same way. For each point of damage suffered, the player responsible for the damage trashes 1 randomly-chosen card from the grip.

- b. For each point of core damage suffered, the player responsible for the damage trashes 1 randomly-chosen card from the grip, and the Runner's maximum hand size is permanently reduced by 1. The Runner takes a core damage counter for each core damage suffered to track the reduction in hand size.

- c. Some cards refer to "brain damage". This is an older term for core damage. The two terms are interchangeable.


**10.4.3.** If the Runner suffers more than 1 damage of any type, the cards are randomly trashed simultaneously.


- Example: The Runner has 2 tags and four cards in their grip, one of which is I've Had Worse. The Corp plays BOOM!, dealing 7 meat damage to the Runner. If the Runner cannot or does not prevent at least 3 of the incoming damage, they immediately flatline as they have suffered more damage than they have cards in grip, and I've Had Worse does not have the opportunity to resolve. If the Runner does prevent enough of the damage to survive, the Runner randomly trashes as many cards as the remaining amount of damage. After all the cards have been randomly selected and placed in the heap, I've Had Worse meets its trigger condition if it is among those cards trashed by the damage.

- a. If an effect modifies the procedure for dealing damage such that the cards trashed cannot be selected simultaneously, then the cards are selected sequentially but are still trashed simultaneously.

  - Example: The Corp is playing as Chronos Protocol: Selective Mind-mapping when the Runner accesses a Snare! before taking any other damage during this turn. The Corp looks at the grip and selects a card to be trashed as the first point of net damage for the turn, then two other cards are randomly selected. All 3 cards are trashed simultaneously.


**10.4.4.** If the Runner takes more damage than the number of cards in their grip, they are flatlined. See rule 1.7.2b.



## 10.5. Tags


Tags represent the Corp's possession of valuable information about the Runner, such as a data trail they've left behind or the physical location they are jacked in from.

**10.5.1.** A tag refers to a tag counter placed on the Runner. Tags are used in card abilities and basic actions.

**10.5.2.** The Runner is tagged if there are one or more tags on them.

**10.5.3.** While the Runner is tagged, the Corp can, as an action, spend {click} and 2{c} to trash one of the Runner's resource cards.

**10.5.4.** While the Runner is tagged, they can, as an action, spend {click} and 2{c} to remove one tag, returning it to the bank.



## 10.6. Bad Publicity


Nobody likes corrupt Corps, or at least, most people say they don't. If a Corp has a bad reputation, the Runner has an easier time finding help to attack that Corp's servers. This extra help is abstracted as extra credits the Runner gets to use during runs.

**10.6.1.** bad publicity refers to a bad publicity counter placed on the Corp. Bad publicity is used by card abilities and during runs.

**10.6.2.** The bad publicity fund is a location in the play area that can contain credits. Credits in the bad publicity fund are controlled by the Runner, but are not part of the Runner's credit pool. The Runner can spend credits in the bad publicity fund during runs.

**10.6.3.** Credits are added to and removed from the bad publicity fund during each run.


- a. During step 6.9.1b, the Runner adds 1{c} from the bank to the bad publicity fund for each bad publicity the Corp has.

- b. During step 6.9.6b, the Runner returns any remaining credits from the bad publicity fund to the bank.

- c. If the Corp takes bad publicity or removes bad publicity after step 6.9.1b of a run, this has no effect on the credits in the bad publicity fund for that run.



## 10.7. Link


The Runner's link is the number of points their path through the Net traverses between their rig and the Corp's server. The more proxies, redirects, and other intermediary connecting points between the Corp and the Runner, the harder it is for sysops to track down the Runner's virtual location.

**10.7.1.** The Runner's link value is calculated by adding the base link on their identity card to the link ({link}) produced by their installed cards.

**10.7.2.** Link is primarily used to contest traces, as described in section 10.8, but other abilities can also refer to the Runner's link value.



## 10.8. Traces


A trace is the Corp's attempt to connect from their servers back to the Runner. Many traces can allow the Corp to gain information about the Runner, tagging them, but traces can also produce a wide variety of other effects. Traces most commonly originate from ice subroutines, but Corporate operations can produce some of the nastiest trace effects.

**10.8.1.** Some card abilities initiate a trace attempt (sometimes simply called a trace) on the Runner. Traces are marked by the language "Trace [N]" on a card, where the given value N is the base trace strength of the trace. A trace pits the Corp's trace strength against the Runner's link strength, both of which can be increased by spending credits.

**10.8.2.** The Corp acts first during a trace attempt, openly spending any number of credits. This sets the trace strength, which is the base trace strength plus the number of credits spent.

**10.8.3.** After the Corp spends their credits, the Runner has the opportunity to spend credits to establish their link strength. The Runner's link strength is equal to their link value plus the number of credits spent.

**10.8.4.** After the Runner's link strength is established, it is compared to the Corp's trace strength. If the trace strength exceeds the link strength, the trace attempt is successful and any "if successful" abilities associated with the trace meet their trigger conditions. If the link strength is equal to or greater than the trace strength, then the trace attempt is unsuccessful, and any "if unsuccessful" abilities associated with the trace meet their trigger conditions.

**10.8.5.** "If successful" and "if unsuccessful" effects following the "Trace [N]" indicator are conditional abilities tied to the results of the trace. Any instructions in the body of the trace without an "if successful" or "if unsuccessful" are conditional abilities with the implicit trigger condition, "when the trace is determined to be successful or unsuccessful."

  - Example: Gemini's subroutine initiates a trace attempt with two associated conditional abilities. The first is "If successful, do 1 net damage." and the second is "When the trace is determined to be successful or unsuccessful, if your trace strength is 5 or greater, do 1 net damage." Both of these will become pending (if appropriate) after step 10.8.6e below.

**10.8.6.** Steps of Resolving a Trace Attempt


- a. The trace initiates. At this time, any "when a trace is initiated" abilities meet their trigger conditions.

- b. A checkpoint occurs.

- c. The Corp may spend credits to increase the trace strength.

- d. The Runner may spend credits to increase their link strength.

- e. The trace is determined to be successful or unsuccessful.

- f. The trace is complete.



## 10.9. Load and Empty

**10.9.1.** To load a card with counters is to place those counters on that card. A card is empty when it is no longer hosting any counters of a type that were previously loaded onto it.

**10.9.2.** An ability on a card that refers to that card becoming empty is always linked to a preceding ability on that same card that loaded it with counters.

  - Example: The Runner installs Crowdfunding, and its first ability becomes pending. Even though there are no credits hosted on Crowdfunding until that ability resolves, the following "When it is empty, ..." ability does not meet its trigger condition because the card has not yet been loaded.

**10.9.3.** If any other types of counters are placed on that card without having been loaded, and those counters are subsequently removed, then empty abilities on that card do not meet their condition.

**10.9.4.** Loading a card with counters does not impose any further restrictions upon those counters for that card. Other abilities can still issue instructions to act upon counters of that type, including but not limited to placing more of them or removing some of them.



## 10.10. Charge


A Runner's life is one of fluidity and adaptation; their rig is in a constant state of repair of upgrade, hideouts change, and belongings come and go. With creativity and skill, a Runner can apply their personal touch to get more out of their tools and resources than anyone else would think possible.

**10.10.1.** To charge is to place 1 power counter on a card that already has at least 1 hosted power counter. Cards with no hosted power counters cannot be charged.

**10.10.2.** If an instruction directs a player to charge 1 or more cards from among a set of cards, the particular cards to be charged are chosen as targets of that instruction. Only cards in the indicated set with at least 1 hosted power counter are valid targets. See section 1.15.

**10.10.3.** If an instruction directs you to charge a particular card, that part of the instruction can only be resolved if the card has at least 1 hosted power counter at the time the instruction becomes imminent.



## 10.11. Mark


A savvy Runner is always ready to take advantage of an unexpected windfall or moment of vulnerability: an unguarded shipment, sudden tip-off from a trusted friend, an inattentive guard or merely an unsecured PAD. Many Runners refer to these opportunities as "marks", and pursue them for access and profit.

**10.11.1.** The mark is a designation that can be applied to a particular server. It has no inherent effect, but abilities can refer to it.


- a. There is only ever a single server designated as the mark, and all cards that refer to a player's mark share the same designated server.


**10.11.2.** If a player is instructed to "identify your mark", determine if any server is currently designated as the mark. If there is no mark, a random central server becomes the mark for the remainder of the turn.


- a. The random central server can be selected by any method, as long as there is an equal probability to select each of the 3 central servers (HQ, R&D, and Archives). Recommended methods include designating 1 card from outside the game to represent each server, shuffling those 3 cards, and drawing 1, or designating an equal number of faces of a die to represent each server and rolling that die.


**10.11.3.** If a server is already designated as the mark, an instruction to "identify your mark" does nothing. The mark is immutable for the remainder of the turn.

**10.11.4.** The designation of a server as the mark is treated as a lingering effect that expires at the end of the turn. When the effect expires, there is no mark until another instruction to "identify your mark" resolves.

**10.11.5.** When a condition checks a game property related to a player's mark, it only checks from the moment that server was designated as the mark.

  - Example: The Runner has no mark and plays Jailbreak to make a run on HQ. When the run is successful, they draw Virtuoso with Jailbreak's ability. The Runner continues their turn by installing Virtuoso and playing Carpe Diem. If Carpe Diem's instruction to "identify your mark" results in HQ becoming the mark, the subsequent run it allows will meet the trigger condition for Virtuoso's third ability if it is successful. Even though there was a successful run on HQ earlier in the turn, HQ was not designated as the mark at that time, so the run made with Carpe Diem is the first time the Runner makes a successful run on their mark this turn. The Runner will access 1 additional card when they breach HQ.



## 10.12. Sabotage


Sabotage can be quietly working to undermine a Corp from the inside, organizing protest action, sinking a transport ship, or simply inspiring others to spread the actions further. An act of sabotage at just the right moment can severely limit the Corp's options and force them into making difficult decisions.

**10.12.1.** Some Runner card abilities sabotage the Corp. An instruction to "sabotage N" directs the Corp to trash N cards collectively from HQ and the top of R&D.

**10.12.2.** To resolve a sabotage effect, the Corp chooses which cards to trash from HQ and determines the remaining number of cards to be trashed from the top of R&D. Then, all of those cards are trashed simultaneously


- a. Cards trashed by a sabotage effect enter Archives facedown.

- b. The Corp cannot look at cards trashed from R&D by a sabotage effect until after making all decisions for the resolution of that effect.


**10.12.3.** In resolving a "sabotage N" effect, the Corp may be required to trash cards from a certain zone if there are not enough cards in the other zone.


- a. If there are fewer than N cards in R&D, but at least N cards total in HQ and R&D combined, the Corp must choose enough cards to trash from HQ such that the remaining number of cards to be trashed from the top of R&D is equal to or less than the actual number of cards in R&D.

- b. If there are fewer than N cards in HQ and R&D combined, the Corp trashes all cards from HQ and all cards from R&D.

  - Example: The Runner uses Esâ Afontov: Eco-Insurrectionist's ability to sabotage 2. The Corp has 2 cards in HQ and 30 cards in R&D. The Corp can choose not to trash any cards from HQ and trash 2 cards from the top of R&D; choose 1 of the 2 cards in HQ and trash that card and the top card of R&D; or choose and trash both cards from HQ.
  - Example: The Runner uses Chastushka's ability to sabotage 4. The Corp has 5 cards in HQ and 2 cards in R&D. The Corp must choose at least 2 cards in HQ to be trashed, so that the total number of cards trashed is 4.
  - Example: The Runner uses Chastushka's ability to sabotage 4. The Corp has 2 cards in HQ and 1 card in R&D. The Corp must trash all cards from HQ and R&D.



## 10.13. Dividends


Dividends are rewards the Corp receives for over-investing in their agendas. Dividends might represent a bumper harvest of construction biomaterials, investigative journalists uncovering (or fabricating) a particularly effective story, or simply reaping record-setting profits after an especially risky investment.

**10.13.1.** Dividends is a keyword that represents a conditional ability. "Dividends N" means "When you score this agenda, place N agenda counters on it for each hosted advancement counter past its advancement requirement.".

  - Example: The Corp scores Embedded Reporting with 5 advancement counters on it. The Corp will put 4 agenda counters on the agenda in their score area, 2 agenda counters for each of the 2 excess advancement counters.

**10.13.2.** A dividends ability checks its source agenda's advancement requirement and number of hosted advancement counters at the time that agenda began to be scored, before it moved to the Corp's score area.

  - Example: The Corp has Project Ingatan and a rezzed SanSan City Grid in the root of a server. The Corp advances Project Ingatan 3 times and scores it. Once Project Ingatan moves to the Corp's score area, its hosted advancement counters are all removed, and the modifier from SanSan City Grid no longer applies. But it did have 3 hosted advancement counters and an advancement requirement of 2 at the time it was scored, so the Corp places 1 agenda counter on it with its dividends 1 ability.



## 10.14. Bidding

**10.14.1.** A bid is a number of credits that a player secretly chooses to spend as part of resolving an ability. Some effects instruct only a single player to bid, while other effects require both players to bid simultaneously.

**10.14.2.** Players select their bids in secret. This can be accomplished by writing down a number, by hiding a number of credit counters in a closed fist, or by any other unambiguous method.

**10.14.3.** A player cannot bid a number of credits they are unable to spend. Players can always bid 0{c}.

  - Example: The Runner has no credits in their credit pool and 1 credit hosted on Fencer Fueno when they make a successful run on a server with Adrian Seis rezzed in its root. When the ability on Adrian Seis resolves, the players play a Psi Game, which requires them both to bid 0, 1, or 2{c}. In this case, the Runner cannot bid 2{c}, as they are not able to spend that number of credits. They can, however, bid 1{c}, as they will be able to spend the credit from Fencer Fueno to pay for that bid.
  - Example: The Runner encounters RSVP and its subroutine resolves. Later in that run, they are required to bid credits by another ability. Since the effect of RSVP's subroutine prohibits the Runner from spending credits, they must bid 0{c}.

**10.14.4.** After all bids are selected, the ability that called for the bids continues resolving. Players will later be instructed to reveal their bids. Each player must spend a number of credits equal to their bid as soon as the bid is revealed.


- a. Spending credits for a revealed bid is not a conditional ability and does not wait for a checkpoint or priority window after revealing the number of credits bid.

- b. Spending credits for a bid counts as paying a nested cost. See section 1.16.

- c. If both players are bidding and a player has multiple sources of credits available from which to pay for their bid (see rule 1.10.3c), that player chooses how to pay after learning the number of credits their opponent bid. If both players have multiple ways to pay for their bids, first the active player chooses how to pay, then the inactive player chooses how to pay. In either circumstance, the credits are spent simultaneously.


**10.14.5.** Some older cards direct players to "secretly spend credits" without reference to bidding. These abilities still follow the rules for bidding. Trigger conditions based on players "secretly spending credits" or "revealing secretly spent credits" are met at the next checkpoint after bids are revealed and paid for.

**10.14.6.** Psi Games


- a. A Psi Game is a bidding contest in which the outcome depends on whether the number of credits each player bid was the same or different. The instruction "Play a Psi Game." encompasses both bidding and revealing bids.

- b. When playing a Psi Game, each player can bid 0{c}, 1{c}, or 2{c}.

- c. Playing a Psi Game is handled as a single instruction. After both players have selected a bid for a Psi Game, they immediately proceed to reveal and spend their bids. There are no checkpoints during this process until after both players have paid for their bids.

- d. In an ability that calls for a Psi Game, subsequent instructions will depend on the outcome of the Psi Game. The condition "if the bids match" is satisfied if the Corp and Runner bid the same number of credits. The condition "if the bids differ" is satisfied if the Corp and Runner did not bid the same number of credits.
