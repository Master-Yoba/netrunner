# 5. Turns

_Netrunner Comprehensive Rules v26.03 · [source](https://rules.nullsignal.games/#chpt_turns) · retrieved 2026-09-01_


## 5.1. General

**5.1.1.** A turn is a duration of time during which a player may take actions and is the first to receive priority to act during priority windows.

**5.1.2.** The Corp's turn consists of three phases. During the draw phase, the Corp draws the top card of R&D into HQ; during the action phase, the Corp spends {click}s to perform actions; during the discard phase, the Corp discards until the number of cards in HQ does not exceed their maximum hand size. Once the Corp's turn ends, the Runner's turn begins.

**5.1.3.** The Runner's turn consists of two phases. During the action phase, the Runner spends {click}s to perform actions; during the discard phase, the Runner discards until the number of cards in their grip does not exceed their maximum hand size. Once the Runner's turn ends, the Corp's turn begins.

**5.1.4.** The beginning and end of each player's turn are formalized as particular steps of the turn timing structures.


- a. Trigger conditions related to a turn beginning are not met as the turn timing structure opens. They are met at the timing step that indicates the formal beginning of the turn.

- b. Trigger conditions related to a turn or discard phase ending are met at the timing step that indicates the formal end of the turn. They are not met when the turn timing structure later closes. The end of the turn and the end of the discard phase are the same.

- c. Some effects can occur before the "turn begins" step or after the "turn ends" step of a turn. The player whose turn timing structure is in progress is still the active player, and effects that apply during their turn are still applicable, even though their turn has not yet formally begun or has already formally ended.

- d. Once the game begins, it is always one or the other player's turn. There is no time "between turns".



## 5.2. Actions

**5.2.1.** An action is any paid ability where the cost begins with a {click} symbol.


- a. Other costs can contain {click} symbols without denoting an action.

  - Example: The ability "{click}: Gain 1{c} and draw 1 card." on Professional Contacts is an action. The ability "Lose {click}: Break 1 subroutine on this ice. Only the Runner can use this ability." on Eli 1.0 is not an action and is used during a paid ability window.


**5.2.2.** A player initiates an action by paying its cost in {click}, as well as any other costs associated with that action, during their action window.


- a. Once an action is initiated, it must be completed before the game can advance to the next step or open another action window. If the effects of an action initiate a timing structure, such as a run, players may use paid abilities, rez cards, and score agendas as dictated by the priority windows of that nested timing structure, but otherwise players cannot perform any of those voluntary effects while the action is resolving. See section 9.2 for more details about priority.

- b. If a timing structure is initiated during the resolution of an action, that action is not complete until the new timing structure is complete and any further effects of the initiated action following the completion of the new timing structure are resolved.

  - Example: The Runner takes the action "play an event", playing Stimhack. The action is not complete until the run initiated by Stimhack ends, the Runner suffers core damage, and Stimhack is trashed following its resolution.

- c. A conditional ability that meets its trigger condition due to a player taking an action resolves after the action's trigger cost is paid, before the effects of the action itself. See rule 9.1.2a.

- d. Some conditional abilities meet their trigger conditions due to an action "ending", "finishing", or "completing". These abilities resolve in the reaction window following step 5.6.2b or step 5.7.1f, for actions taken by the Corp and Runner, respectively.


**5.2.3.** Each player has several basic actions they can always perform. The Corp's basic actions are listed in section 5.2.6, and the Runner's basic actions are listed in section 5.2.7.


- a. If an ability has a trigger condition related to an effect being performed “with the basic action”, that trigger condition is met by any basic action that performs the indicated effect. If an ability imposes a cost on performing an effect “with the basic action”, that cost applies to any instance of the indicated effect that would occur as part of resolving a basic action.


**5.2.4.** A player can only take actions during the action phase of their turn. If an effect directs a player to take an action outside of that player's action phase, instead that player does nothing.

**5.2.5.** Some cards refer to players taking "the same action" or "different actions".


- a. Actions a player performs are the same if they are all the same basic action or if all of those actions were initiated by the same ability from the same card.

- b. Actions a player performs are different if each of them is initiated from a different basic action or card ability. Instances of equivalent abilities on different cards are still different actions.

  - Example: The Corp, playing MirrorMorph: Endless Iteration, plays Hedge Fund as the first action of their turn, then plays Lateral Growth. Even though two different cards were played, both times the Corp used the basic action to "Play 1 operation from HQ". Since their first two actions were the same, the Corp will not be able to trigger the ability on MirrorMorph this turn.


**5.2.6.** Corp Actions


- a. The following basic actions are always available to the Corp during their action phase, in addition to any actions provided by card abilities.

- b. "{click}: Gain 1{c}."

- c. "{click}: Draw 1 card."

- d. "{click}: Install 1 agenda, asset, upgrade, or piece of ice from HQ." See section 8.5.

- e. "{click}: Play 1 operation from HQ." See section 8.6.

- f. "{click}, 1{c}: Advance 1 installed card." See section 1.18.

- g. "{click}, 2{c}: Trash 1 resource. Take this action only if the Runner is tagged." See section 10.5.

- h. "{click}{click}{click}: Purge virus counters." See rule 10.1.2.


**5.2.7.** Runner Actions


- a. The following basic actions are always available to the Runner during their action phase, in addition to any actions provided by card abilities.

- b. "{click}: Gain 1{c}."

- c. "{click}: Draw 1 card."

- d. "{click}: Install 1 program, resource, or piece of hardware from the grip." See section 8.5.

- e. "{click}: Play 1 event from the grip." See section 8.6.

- f. "{click}: Run any server." See section 6.

- g. "{click}, 2{c}: Remove 1 tag." See section 10.5.



## 5.3. Draw Phase

**5.3.1.** The draw phase is the first phase of the Corp's turn. It includes the formal beginning of the Corp's turn and the Corp's mandatory draw.

**5.3.2.** The mandatory draw is a card draw performed by the Corp. It is not an action and does not cost {click}.

**5.3.3.** The Runner's turn does not have a draw phase.



## 5.4. Action Phase

**5.4.1.** The action phase is the first phase of the Runner's turn and the second phase of the Corp's turn. It is the only timing structure in which players can take actions, as discussed in section 5.2.

**5.4.2.** During a player's action phase, they must continue to take actions until they have no more clicks or the phase is ended directly by a card ability. The player may take actions in any combination and order as long as they pay the appropriate costs. Once they have no remaining clicks, the action phase ends.

**5.4.3.** A player's action phase can be forced to end by certain card abilities (e.g. via the ability on a terminal operation or event). When a player's action phase is forced to end, the following occurs:


- a. The current step of the action phase ends, and all remaining steps are skipped.

- b. Any currently open priority windows close, their associated abilities lose their pending status, and the player with priority loses priority.

- c. Any currently resolving actions or abilities cease resolving and are considered complete.

- d. Conditions related to the action phase ending are met, and a checkpoint occurs. A reaction window created by this checkpoint is still considered to be taking place in the action phase, but players cannot take actions during this window.

- e. After completing any reaction windows opened by the checkpoint from rule 5.4.3d, the game proceeds to the active player's discard phase.


**5.4.4.** If an effect attempts to end a player's action phase during their draw phase, their discard phase, or another player's turn, this effect does nothing.



## 5.5. Discard Phase

**5.5.1.** The discard phase is the last phase of each player's turn. It contains the discard step and the formal end of that player's turn.

**5.5.2.** discarding is the act by which a player moves a card to their discard pile during their discard phase if they have exceeded their maximum hand size.


- a. Cards discarded from HQ are always sent to Archives facedown, regardless of whether they have been previously accessed by the Runner.

- b. A discarded card is not considered to have been trashed, and vice versa.


**5.5.3.** A player's maximum hand size is the maximum number of cards that player can keep in their hand during their discard phase.


- a. Players begin the game with a maximum hand size of five cards.

- b. Each point of core damage the Runner suffers permanently reduces their maximum hand size by 1 card. See section 10.4. Other abilities can modify a player's maximum hand size directly.


**5.5.4.** The discard step is step 5.6.3a for the Corp, and step 5.7.2a for the Runner. In that step, the active player discards cards to reach their maximum hand size.


- a. Before the Runner discards cards in their discard step, they must check for a flatline. See rule 1.7.2b.

- b. If the number of cards in the active player's hand is less than or equal to their maximum hand size, nothing else happens in this step.

- c. If the number of cards in the active player's hand is greater than their maximum hand size, they discard a number of cards from their hand equal to the difference. The discarding player chooses which of their cards to discard, and the chosen cards are all discarded simultaneously.

- d. If an effect instructs a player to skip their discard step, the game proceeds directly from the start of their discard phase to the step following the discard step. They will not check their hand size or discard cards.



## 5.6. Steps of the Corp's Turn

**5.6.1.** During the draw phase, the following steps occur in order:


- a. The Corp gains their allotted clicks (default {click}{click}{click}).

- b. A paid ability window occurs, in which players may use paid abilities, the Corp may rez non-ice cards, and the Corp may score agendas. (P) (R) (S)

- c. The Corp's recurring credits ({recurring}) refill.

- d. The Corp's turn formally begins. Conditions related to the turn beginning are met.

- e. The Corp performs their mandatory draw.

- f. The Corp's draw phase is complete. Play proceeds to the Corp's action phase.


**5.6.2.** During the Corp's action phase, the following steps occur in order:


- a. A paid ability window occurs, in which players may use paid abilities, the Corp may rez non-ice cards, and the Corp may score agendas. (P) (R) (S)

- b. If the Corp has any unspent {click}, the Corp takes an action. Otherwise, skip to (d).

- c. Return to (a).

- d. The Corp's action phase formally ends. Conditions related to the action phase ending are met.

- e. The Corp's action phase is complete. Play proceeds to the Corp's discard phase.


**5.6.3.** During the Corp's discard phase, the following steps occur in order:


- a. The Corp discards cards to reach their maximum hand size.

- b. A paid ability window occurs, in which players may use paid abilities and the Corp may rez non-ice cards. (P) (R)

- c. If the Corp has any unspent {click}, the Corp loses those {click}.

- d. The Corp's turn formally ends. Conditions related to the turn or discard phase ending are met.

- e. The Corp's discard phase and turn are complete. Play proceeds to the Runner's turn.



## 5.7. Steps of the Runner's Turn

**5.7.1.** During the Runner's action phase, the following steps occur in order:


- a. The Runner gains allotted clicks (default {click}{click}{click}{click}).

- b. A paid ability window occurs, in which players may use paid abilities and the Corp may rez non-ice cards. (P) (R)

- c. The Runner's recurring credits ({recurring}) refill.

- d. The Runner's turn formally begins. Conditions related to the turn beginning are met.

- e. A paid ability window occurs, in which players may use paid abilities and the Corp may rez non-ice cards. (P) (R)

- f. If the Runner has any unspent {click}, the Runner takes an action. Otherwise, skip to (h).

- g. Return to (e).

- h. The Runner's action phase formally ends. Conditions related to the action phase ending are met.

- i. The Runner's action phase is complete. Play proceeds to the Runner's discard phase.


**5.7.2.** During the Runner's discard phase, the following steps occur in order:


- a. The Runner discards cards to reach their maximum hand size.

- b. A paid ability window occurs, in which players may use paid abilities and the Corp may rez non-ice cards. (P) (R)

- c. If the Runner has any unspent {click}, the Runner loses those {click}.

- d. The Runner's turn formally ends. Conditions related to the turn or discard phase ending are met.

- e. The Runner's discard phase and turn are complete. Play proceeds to the Corp's turn.
