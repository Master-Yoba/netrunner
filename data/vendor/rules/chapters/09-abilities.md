# 9. Abilities

_Netrunner Comprehensive Rules v26.03 · [source](https://rules.nullsignal.games/#chpt_abilities) · retrieved 2026-09-01_


## 9.1. General

**9.1.1.** An ability is an independent unit of text on a card or counter, a basic action, or the basic trash ability.


- a. All rules text on a card or counter is part of an ability.

- b. The basic actions are defined in section 5.2.6 and section 5.2.7.

- c. The basic trash ability is defined in section 7.1.5.

- d. An ability's text can include other abilities that it could grant to cards or counters or introduce directly to the game state. See also section 9.10.

- e. Each ability is categorized as either a static ability (section 9.4), a paid ability (section 9.5), a conditional ability (section 9.6), a play ability (section 9.7), or a subroutine (section 9.8).

- f. Abilities marked with {interrupt} or that include the word "would" in their instructions have special timing rules that differ from other abilities. See section 9.9.

- g. Each non-static ability contains one or more instructions that make up the ability's component steps. Section 9.3.4 describes the nature and function of instructions in an ability's text. An instruction that the game is presently concerned with can be either imminent or resolving.


**9.1.2.** To resolve an ability means to resolve each of that ability's instructions in the order they appear. If an ability contains more than one instruction, a checkpoint occurs between each consecutive instruction. To resolve an instruction means to carry out that instruction.


- a. While resolving an ability, other abilities can meet their conditions. When this happens, a "chain reaction" is created. The current instruction finishes resolving, then more recent abilities fully resolve before the next instruction of the original ability. If any other abilities meet their conditions while resolving the "chained" abilities, then another "chain" is created before continuing to resolve the previous chained abilities. Resolve each set of chained abilities one at a time, from the most recently met condition to the oldest, before continuing with the original ability that started the chain reaction.

  - Example: The Runner's identity is Armand "Geist" Walker. They access a Snare! from R&D with only 2 cards in their grip. Before taking a tag or suffering any net damage, the Runner triggers Decoy's ability in order to avoid the tag. Using the Decoy meets the trigger condition of Armand "Geist" Walker's ability. As this is the most recent ability to meet its trigger condition, Armand "Geist" Walker's ability must resolve first, before Decoy avoids the tag and before Snare! finishes resolving. The Runner draws a card from Armand "Geist" Walker, avoids the tag from Snare! with Decoy, and then finally suffers (and survives) the 3 net damage from Snare!.

- b. An ability "is resolving" from when its first instruction becomes imminent until its last instruction has finished resolving. See also section 9.9. An event or operation "is resolving" throughout step 8.6.7f of playing that event or operation.

  - Example: The Corp resolves a subroutine on Attini while its first ability is active. The subroutine is resolving during the interrupt window for its instruction, so the Runner cannot spend credits during that window and will not be able to use Caldera's ability to prevent net damage.
  - Example: The Runner is playing Zahya Sadeghi: Versatile Smuggler and plays Direct Access to run HQ. The static ability on Direct Access removes Zahya Sadeghi's ability while it is resolving. Normally, that ability would meet its trigger condition and resolve immediately after the "Run any server." instruction. This reaction window is outside of the run, but it is still part of Direct Access resolving, so the ability is not present at the needed time and cannot be triggered or resolved.


**9.1.3.** The source of an ability is the card, counter, or game rule that originated it.


- a. Each card is the source of its own printed abilities.

- b. If something grants an ability to an object (see section 9.1.9), the source of the granted ability is that object, not whatever granted the ability.

- c. The source of an ability that is being maintained by a lingering effect is the object that created that lingering effect. See section 9.10.


**9.1.4.** Each paid ability, conditional ability, and subroutine becomes independent of its source at a certain point before it resolves, as described in rules 9.5.4, 9.6.12 and 9.8.8, respectively. If the source changes zones after that point, the ability cannot act on the source.

  - Example: The runner plays Compile, uses it to install Mayfly during the resulting run, and breaks subroutines with Mayfly during that run. When the run ends, delayed conditional abilities from both Mayfly and Compile become pending. The Runner decides to resolve Compile first, adding Mayfly to the bottom of their stack. The Runner then resolves the ability from Mayfly, which instructs them to trash its source program. Because Mayfly has changed zones, the copy of it on the bottom of the stack is a new object. With nothing to trash, the ability from Mayfly does nothing.

**9.1.5.** Whereas an ability, instruction, or declaration is made up of text, an effect is what happens in the game because of that text.


- a. Any of a non-static ability's effects that apply beyond the duration of that ability's resolution become independent of that ability and its source. The game engine takes responsibility for managing these lingering effects directly, and deletes them from the game state as their durations expire. See section 9.10.


**9.1.6.** Any time a player chooses to resolve an optional ability or an optional part of an ability, the player is considered to be using that ability and the source card of that ability. Players do not "use" abilities that are entirely mandatory.


- a. A paid ability is considered used once the trigger cost has been paid. See step 9.5.7 for the steps of using a paid ability.

- b. A conditional ability is considered used once the relevant optional effects of the ability have been resolved by the controller of the ability. See step 9.6.15 for the steps of triggering and resolving conditional abilities.

- c. If an ability allows a player to spend or move counters, that ability's source card is considered used at the time the player pays a cost by spending or moving those counters, even if the counters are spent or moved from cards other than the card that is the source of that ability.

  - Example: The Runner spends the credit hosted on Cyberfeeder to pay the trigger cost of Mimic's ability. Because the Runner triggered Mimic's paid ability and Cyberfeeder's ability allowed the credit to be spent, both Mimic and Cyberfeeder have been used.


**9.1.7.** Abilities are active if they are eligible to be resolved or apply to the current game state, and inactive otherwise. By default, a card's abilities are active if and only if that card is active (see section 1.8.3). Abilities of counters are active for as long as the counter exists.

**9.1.8.** Some types of abilities are active even while their source card is inactive.


- a. Conditional abilities that meet their conditions when their source card is accessed are active even while that card is inactive.

- b. Abilities stating that they are active in a particular zone are active in that zone. Abilities that can only ever meet their conditions in a particular zone are active in that zone. Abilities that can only affect the game state from a particular zone are active in that zone. When determining whether these stipulations apply, refer only to the game rules, not to any other effects that may be changing how cards move between zones.

  - Example: I've Had Worse has an ability that meets its trigger condition when it is trashed due to damage. Normally, this can only occur by moving I've Had Worse from the grip to the heap. Therefore, this ability is active in the heap. However, if I've Had Worse is trashed, but the Corp uses Skorpios Defense Systems to remove it from the game instead of adding it to the heap, the ability is not active.

- c. Abilities that modify when or if their source card can be played, installed, or rezzed are active even while that card is inactive.

- d. Abilities that modify the cost to install, rez, or play their source card are active even while that card is inactive.

- e. Abilities that define or modify the advancement requirement of their source card or create an additional cost to steal their source card are active even while that card is inactive.

- f. Abilities allowing a card to be advanced are active even while that card is unrezzed.

- g. If an active card moves to a zone where it is inactive, an ability of that card with a trigger condition that is met by this zone change remains active in the card's new location until any corresponding instances of the ability resolve. See rule 9.6.2 for information about instances of conditional abilities.

  - Example: The Runner uses Singularity to simultaneously trash a rezzed Hostile Infrastructure and 2 upgrades. Those cards are moved to Archives and become inactive, but Hostile Infrastructure's ability remains active, and 3 instances will become pending, each doing 1 net damage when it resolves. Once all those instances are resolved, Hostile Infrastructure's ability stops being active.
  - Example: The Runner plays Test Run to install Nanuq. When their turn ends, Test Run's delayed conditional ability adds Nanuq to the top of the Runner's stack. This meets the trigger condition of Nanuq's first ability, which remains active until it resolves even though Nanuq itself is inactive in the stack. The ability will move Nanuq from the top of the stack to the removed-from-game zone.

- h. If a piece of ice is encountered while it is not installed, its subroutines are active during that encounter.

  - Example: The Runner accesses Archangel from HQ, and the Corp uses its ability to force the Runner to encounter it. Archangel's subroutine is active and can resolve during that encounter.

- i. Persistent abilities can sometimes remain active after their source card is trashed. See section 9.12.5.


**9.1.9.** Static abilities and lingering effects can make objects gain or lose abilities.


- a. If an object loses an ability, that ability is completely ignored. If the lost ability is a subroutine, it is not considered by any effect that counts subroutines on that object.

- b. To determine the actual abilities present on an object (as well as its other characteristics), follow the procedure described in rule 9.12.1d and rule 9.12.1e.

- c. Abilities on an object have no particular order, except for play abilities and subroutines. Play abilities cannot be added to or removed from an event or operation, and their order is the order they appear on their source card. To determine the order of subroutines on an object, refer to section 9.8.2 and section 9.8.3.



## 9.2. Timing and Priority

**9.2.1.** The active player is the player whose turn it is. The other player is the inactive player.

**9.2.2.** A timing structure is a unit of the game in which a prescribed sequence of steps progress the game forward.


- a. The Corp's turn and the Runner's turn are timing structures, as are each of the 3 phases of the turns. See section 5.

- b. A run is a timing structure, as are each of the 6 phases of a run. See section 6.

- c. Breaching a server is a timing structure. See sections 7.3 and 7.5.

- d. Accessing a card is a timing structure. See sections 7.1 and 7.2.

- e. Other procedures with prescribed steps, such as installing a card or resolving a trace attempt, are not timing structures.


**9.2.3.** Priority is a player's opportunity to act and make certain game choices. No more than one player can have priority at any given time.

**9.2.4.** A priority window is a general term for a timing step in which one or both players receive priority. Priority windows open for different purposes throughout the game. When a priority window closes, the game continues to the next timing step.


- a. All priority windows give the player with priority the option to choose a relevant ability they control (if there are any) as the next ability for the game to resolve. This is often referred to as triggering the chosen ability. Some priority windows also give players other options.

- b. Except during action windows, the player with priority has the option to pass in addition to the available options defined by that priority window. When a player passes, the game progresses to the next step, either by giving priority to the other player or by closing the priority window. Each type of priority window defines the method and resolution of passing.

- c. Unless otherwise noted, the player with priority receives priority again after resolving one of their available options. That player will continue to receive priority until they pass.

- d. While a priority window is open, another nested priority window can open as well. This nesting allows the game to resolve "chain reactions," as discussed in rule 9.1.2a. The most recently opened priority window is always resolved before returning to an earlier priority window.

- e. Whenever a player receives priority during a priority window, a checkpoint occurs immediately before that player may act. See section 10.3.


**9.2.5.** The types of priority windows are action windows, paid ability windows, reaction windows, interrupt windows, and mid-access ability windows.

**9.2.6.** An action window is a priority window that opens during a player's action phase if they have unspent clicks.


- a. An action window gives only the active player priority.

- b. During an action window, the active player must take an action. Section 5.2 discusses actions. This type of window does not give the option to pass.

- c. After the active player takes an action, the action window closes and the game moves to the next timing step. The player does not receive priority again.

- d. There are two timing steps during which an action window may open. The Corp has an action window in step 5.6.2b of their action phase. The Runner has an action window in step 5.7.1f of their action phase.


**9.2.7.** A paid ability window is a priority window that opens throughout the game's timing structures to allow players to trigger paid abilities, rez cards, or score agendas.


- a. Paid ability windows give both players priority, starting with the active player. When a player passes, the other player receives priority. Players continue to exchange priority until a player who receives priority from their opponent passes without resolving any other option available to them. Once this happens, the paid ability window closes.

- b. During every paid ability window, the player with priority has the option to trigger an active paid ability they control. Players cannot trigger actions, interrupts, or mid-access abilities in a paid ability window. See section 9.5. Within this document, paid ability windows are marked with the symbol (P) to denote the option to trigger a regular paid ability.

- c. During some paid ability windows, the Corp has the option to rez an asset or upgrade while they have priority. Within this document, the symbol (R) denotes a paid ability window in which cards can be rezzed. See section 8.1.

- d. During some paid ability windows, the Corp has the option to score an agenda while they have priority. Within this document, the symbol (S) denotes a paid ability window in which agendas can be scored. See section 1.17.

- e. During the paid ability window at step 6.9.2b of the Approach Ice Phase of a run, the Corp has the option to rez the piece of ice the Runner is approaching while they have priority.

- f. The player with priority during a paid ability window may use any of the options available to them any number of times in any combination and order until they decide to pass, so long as they are allowed and the player pays any and all costs to use each option. Each option must be fully resolved before another is chosen. A player is not obligated to resolve any of the options available to them, except they must pass.

- g. Paid ability windows occur throughout the timing steps of turns and runs. Sections 5.6, 5.7 and 6.9 detail those steps and indicate which options are available in which windows. Section 11 also contains a summarized version of these steps.


**9.2.8.** A reaction window is a priority window that opens whenever one or more active conditional abilities become pending by meeting their conditions. Section 9.6 discusses conditional abilities.


- a. Each reaction window is associated with the fixed set of conditional abilities that met their conditions just before the window opened. If other abilities become pending during a reaction window, a separate reaction window opens to handle the new abilities. See rule 9.1.2a.

- b. Reaction windows give both players priority, starting with the active player. When the active player passes, the inactive player receives priority. When the inactive player passes, the reaction window closes.

- c. During a reaction window, the player with priority has the option to trigger a pending conditional ability they control that is associated with that window.

- d. The player with priority during a reaction window may trigger their pending abilities in any order until they decide to pass; they do not need to trigger mandatory abilities before optional ones. Each ability they trigger must be fully resolved before another is chosen.

- e. The player with priority cannot pass if they control any pending mandatory abilities. They may pass with optional conditional abilities still pending, in which case those abilities lose their pending status without being triggered. Section 9.6.9 describes the differences between mandatory and optional conditional abilities.

- f. If a reaction window opens due to a timing structure beginning, and during that reaction window the timing structure ends (e.g. by an effect moving the game to another timing point past the end of the structure), then the reaction window immediately closes. All remaining abilities associated with the window lose their pending status without being triggered, even if they are mandatory abilities.

  - Example: The Runner has a Femme Fatale installed and chose a Tollbooth with its "when installed" ability. When the Runner encounters the Tollbooth, they pay 1{c} to bypass the Tollbooth with Femme Fatale. Because the resolution of Femme Fatale's ability causes the encounter to end, the pending ability from Tollbooth cannot be triggered. The Runner does not pay 3{c}, and the run does not end.


**9.2.9.** An interrupt window is a priority window that opens just before an instruction would resolve when one or more players have abilities that could modify that imminent instruction. See section 9.9 for rules about interrupt abilities.


- a. Each interrupt window is associated with the single imminent instruction being modified by the abilities triggered during the window, and with a fixed set of conditional ability interrupts determined as the window opens.

- b. As an interrupt window opens, before players receive priority, the expected effects of the imminent instruction are determined, any applicable replacement effects are applied, and then relevant conditional abilities become pending. See section 9.9.4.

- c. Interrupt windows give both players priority, starting with the active player. When a player passes, the other player receives priority. Players continue to exchange priority until a player who receives priority from their opponent passes without resolving any other option available to them. Once this happens, the interrupt window closes.

- d. During an interrupt window, the player with priority has the option to trigger an interrupt ability they control that is relevant to the imminent effect. See section 9.9.3.

- e. The player with priority during an interrupt window may trigger their abilities in any order until they decide to pass. They do not need to trigger mandatory abilities before optional abilities or trigger conditional abilities before paid abilities. Each ability they trigger must be fully resolved before another is chosen.

- f. The player with priority cannot pass if they control any pending mandatory abilities that are still relevant to the imminent effect. They may pass with optional conditional abilities still pending. (Section 9.6.9 describes the differences between mandatory and optional conditional abilities.) Once a player has passed, if they receive priority again, they may continue to trigger paid abilities and pending conditional abilities that are relevant. As the interrupt window closes, any remaining pending conditional abilities lose their pending status.


**9.2.10.** A mid-access ability window is a priority window that opens while the Runner is accessing a card.


- a. A mid-access ability window gives only the Runner priority.

- b. During a mid-access ability window, the Runner may use a mid-access ability or may pass. In addition to any mid-access abilities available from cards, the Runner may use the basic trash ability (see section 7.1.5).

- c. After the Runner uses a mid-access ability or passes, the mid-access ability window closes and the game moves to the next timing step. The Runner does not receive priority again.

- d. The only point at which a mid-access ability window occurs is at step 7.2.2 of accessing a card.



## 9.3. Interpreting Card Text

**9.3.1.** Text is classified into conditions, restrictions, instructions, declarations, and ability flags. The rules text of an ability can contain text in any combination of these classes, except that no ability contains both instructions and declarations.

**9.3.2.** A condition is a unit of text that stipulates a requirement for when an ability's effects are allowed to apply.


- a. A cost condition describes a cost (either a nested cost or a trigger cost) that a player must pay to apply an effect. Costs are discussed in detail in section 1.16.

- b. A trigger condition describes a change in game state that must occur for an effect to apply. Trigger conditions often begin with words such as "if", "when", "whenever", or "after", or with ordinal phrases such as "the first time".

- c. A static condition describes a property of the game state that must be true for an effect to apply. Static conditions often begin with words such as "if", "during", or "while".


**9.3.3.** A restriction is a unit of text that applies one of a specific set of constraints for a card to be played or an ability to be used. If a restriction appears in an ability, it applies to the entire ability regardless of whether it is written before, after, or in the middle of the ability's other text. Many restrictions fall under section 9.1.8, meaning they are active even while their source is inactive.


- a. Text modifying the cost to play, install, rez, or steal the card it appears on is a restriction, including text that adds an additional cost.

- b. Constraints on when or where a card can be installed, rezzed, played, or scored are restrictions.

- c. Limits on when, where, or how often an ability can be used are restrictions.

- d. Constraints on how a cost can be paid are restrictions.

- e. If a card's text limits what cards it can host, the text describing that limit is a restriction. See also rule 1.13.6b.

- f. Definitions for or constraints on a variable are restrictions.

  - Example: Some abilities dictate a value for X, such as "X is the number of rezzed NEXT ice." Some abilities with X in their cost constrain the value that can be chosen for X, such as "X must be equal to or less than the number of tags the Runner has." These statements are restrictions.

- g. Stipulations that an effect or part of an effect cannot be prevented are restrictions.

- h. Any text not falling under one of the above categories is not a restriction.


**9.3.4.** An instruction is a statement or command that is resolved at a specific time and applies immediate effects to the game state. (This can include creating new lingering abilities or effects that will continue to apply over time; see section 9.10.)


- a. Instructions can originate from a game rule or from the text of an ability. An instruction in an ability resolves when that ability resolves (see section 9.1.2). An instruction in the game rules resolves during the timing step(s) when it appears. Section 9.11 explains how to determine the boundaries between consecutive instructions.

- b. If an instruction requires any targets, players announce those targets before that instruction becomes imminent. See section 1.15.

- c. Each instruction is carried out as an atomic unit and cannot be interrupted once it begins to resolve. The procedure for carrying out an instruction can be altered by other effects such as interrupts, but only before the instruction begins to resolve. See section 9.9.

- d. Other than choosing targets, carry out the steps of a single instruction in the order they are written.

- e. Instructions in an ability can create effects that last beyond the resolution of the ability. See section 9.10.


**9.3.5.** A declaration is a statement describing an effect on components or game rules that is applied continuously. A declaration applies its effects as long as it is active.

**9.3.6.** An ability flag is a keyword or symbol that appears at the beginning of an ability. Ability flags are separated from the main text of the ability by an arrow (→). Each ability flag changes the rules for how the flagged ability functions.


- a. There are six ability flags: access, interface, {interrupt}, persistent, threat N, and once per turn.

- b. The "access" flag appears on Runner card paid abilities and affects the timing for triggering those abilities. The Runner can use abilities with this flag only during the mid-access ability window at step 7.2.2 of accessing a card. See section 9.2.10.

- c. The "interface" flag appears on icebreaker paid abilities and affects the timing for triggering those abilities. The Runner can use abilities with this flag only during an encounter, and only if the ability's source is an icebreaker with strength that equals or exceeds the strength of the encountered ice. See section 3.9.5.

- d. The interrupt flag ({interrupt}) appears on paid and conditional abilities and affects the timing for triggering those abilities. Players can use abilities with this flag only during interrupt windows, and only if the ability is relevant to the imminent instruction. See section 9.9.

- e. The "persistent" flag appears on Corp card abilities and changes the rules about when those abilities are active. An ability with this flag can persist until the end of the run after its source card is trashed. See section 9.12.5.

- f. The "threat N" flag can appear on any type of ability. Abilities with this flag are only active if the threat level is greater than or equal to N, regardless of section 9.1.8. See rule 1.17.1a.

- g. The "once per turn" flag appears on paid and conditional abilities. An ability with this flag can only be used once per turn. section 9.1.6 explains what it means to "use" an ability.

  - Example: The Runner is playing as Zahya Sadeghi and finishes a run on HQ, having accessed 1 card. They choose not to gain 1{c} with Zahya's ability. The ability was not "used", so its "once per turn" restriction has not been expended, even though the ability was able to resolve. The Runner can make another run later in the turn and gain credits with the ability.


**9.3.7.** The five types of abilities can be identified by the types of text they are made of.


- a. Static abilities (section 9.4) can contain declarations, restrictions, and conditions, but never instructions. Static abilities are the only ability type that can contain declarations.

- b. Paid abilities (section 9.5) can always be identified by their formatting: a cost condition in bold text, a colon (:), then the remainder of the ability. The text after the colon can contain conditions, restrictions, and/or instructions.

- c. Conditional abilities (section 9.6) always contain a trigger condition or static condition and at least one instruction. They can also contain restrictions, other instructions, or nested conditions. The primary condition is often written with a timing-related word or phrase like "when", "after", "the first time", and so on, and is usually the first part of the ability's text. Some older cards write the condition at the end of the ability or use "if" to indicate the condition.

- d. Play abilities (section 9.7) are the abilities on events and operations that are not paid, conditional, or static abilities. They can contain conditions, restrictions, and/or instructions.

- e. Subroutines (section 9.8) always begin with the {sub} symbol, and can contain conditions, restrictions, and/or instructions.



## 9.4. Static Abilities

**9.4.1.** A static ability is an ability that continuously affects the game as long as it is active. Static abilities are the only type of ability that can contain declarations, and they do not resolve or have associated priority windows.

**9.4.2.** If a static ability contains a static condition, then the constrained parts of the ability apply to the game state only if that condition is met.

**9.4.3.** Static abilities can include restrictions that apply to their source card. These restrictions are often active even while the card is inactive. See section 9.1.8.

**9.4.4.** The effects of static abilities do not have durations and cannot directly create lingering effects (see section 9.10).

  - Example: The runner controls Puffer with a hosted Gebrselassie. Gebrselassie changes the durations of abilities affecting its host's strength, so if the Runner uses Puffer's paid ability to give it +1 strength, that increase will last for the remainder of the turn. Gebrselassie does not affect Puffer's static ability that increases its strength for each hosted power counter, so if the Runner spends {click} to remove a power counter, then the increase in strength from Puffer's static ability will immediately be lost.

**9.4.5.** Static abilities that modify a value maintain the same restrictions and specifications that were present on the original value.

  - Example: The Cleaners has a static ability that gives every instance of meat damage done by the Corp +1 to the amount of the damage. Part of Flare's effect does 2 meat damage that can't be prevented. If this effect resolves while The Cleaners is active, then all 3 points of meat damage are unpreventable.



## 9.5. Paid Abilities

**9.5.1.** A paid ability is an ability players trigger at-will, during appropriate priority windows. In order to use a paid ability, the controller of that ability must pay its trigger cost. Paid abilities are always written with the trigger cost first, followed by a colon (:), followed by the remainder of the ability's text.

**9.5.2.** A player can trigger paid abilities they control while they have priority in an appropriate priority window.


- a. If the trigger cost of a paid ability begins with a {click} symbol, the ability is an action. A player can use one action paid ability during each action window on their turn. See section 9.2.6 and section 5.2.

- b. If the {interrupt} flag appears before a paid ability's trigger cost, that ability is an interrupt. A player can use interrupt paid abilities during appropriate interrupt windows. Each such ability can be used as many times as its effects could apply to the imminent instruction. See section 9.9.

- c. If the "access" flag appears before a paid ability's trigger cost, that ability is a mid-access ability. The Runner can use up to 1 mid-access ability during the mid-access ability window at step 7.2.2 of accessing a card.

- d. If the "interface" flag appears before a paid ability's trigger cost, that ability is an Interface ability and is subject to the restrictions of icebreakers discussed in section 3.9.5. Interface abilities do not have a special priority window type.

- e. Certain paid abilities can only be used at times appropriate to their effects, as detailed in section 9.5.6.

- f. A paid ability that is not an action, interrupt, or mid-access ability can be used during a paid ability window. Each such ability can be used an unlimited number of times as long as its cost is paid each time and any restrictions specified in its text or by other abilities are observed.


**9.5.3.** Paid abilities are always optional. A paid ability and its source are considered used when the ability's trigger cost is paid.


- a. Mid-access abilities the Runner is forced to trigger still count as optional abilities. See also rule 7.1.5c and rule 7.1.5d.

  - Example: The Corp resolves the subroutine on Wendigo, prohibiting the Runner from using their installed Imp for the remainder of the run. Later, the Runner accesses an installed Mumbad Virtual Tour. Even though Mumbad Virtual Tour forces the Runner to trash it if able, Imp's ability is still optional for purposes of Wendigo's prohibition. Since Imp's ability cannot be used at all this run, Mumbad Virtual Tour cannot compel the Runner to spend one of its hosted virus counters. If the Runner can pay the cost of another mid-access ability, such as the basic trash ability, the Runner must use that ability. If not, the Runner will not be able to trash Mumbad Virtual Tour at all.


**9.5.4.** Once a player pays the trigger cost of a paid ability, that ability becomes independent of its source and rule 9.1.4 applies to it.

**9.5.5.** If the trigger cost of a paid ability uninstalls or forfeits the source of that paid ability, and the effects of that paid ability refer to or act on cards or counters hosted on that source, set aside any hosted cards and counters as the trigger cost is paid. The set-aside cards or counters are still considered "hosted" for purposes of this ability, but they are not trashed due to not having a host for as long as the ability is resolving. When the ability finishes resolving, if any of those cards or counters are still set aside, they are trashed during step 10.3.1f or step 10.3.1g of the next checkpoint, as appropriate. Other abilities cannot interact with these cards or counters while they are set aside (see rule 4.8.3).

  - Example: The Runner has Fermenter installed with 4 hosted virus counters. They use its paid ability, which has a cost that includes trashing Fermenter. As the Runner pays this cost, they set aside the 4 counters. When the ability resolves and refers to the number of hosted virus counters, it includes the counters that were set aside, so the Runner gains 8 credits. When the ability is finished resolving, the counters are returned to the bank.
  - Example: The Runner uses the {trash} ability on Street Peddler. They pay the trigger cost and set the 3 hosted cards aside. When the ability resolves, the Runner installs one of the set-aside cards. Since the other two cards are still set aside, the next checkpoint trashes them just as in any other case of cards that were hosted on a card that is no longer installed. No other abilities are able to tell that the cards were set aside: they are treated as having been installed or trashed from their previous location in the play area.
  - Example: This example describes a situation similar to the previous examples, but covers the sequence of steps the game carries out in a higher level of detail. The Corp has priority in a paid ability window, and chooses to trigger the {trash} ability on Reconstruction Contract. As they pay the trigger cost, moving Reconstruction Contract to Archives, they set aside the advancement counters hosted on Reconstruction Contract. Next, a checkpoint occurs following the cost having been paid. After that checkpoint and any corresponding reaction window, the {trash} ability's instruction is ready to become imminent. The Corp chooses a card they can advance to be the instruction's target, and an interrupt window opens. After the interrupt window (assuming the effects of the instruction are not prevented), the Corp moves the set-aside counters to the target card.

**9.5.6.** Paid abilities with certain effects can only be triggered in specific situations.


- a. A paid ability that contains an instruction that could break 1 or more subroutines can only be used during an encounter.

- b. A paid ability that refers to ice the Runner is approaching can only be used during the Approach Ice Phase of a run, and only if the ice being approached meets all stipulations used in referring to it.

  - Example: The Corp can only use the paid ability on Project Wotan while the Runner is approaching a rezzed piece of bioroid ice.

- c. A paid ability that refers to ice the Runner is encountering can only be used during an encounter with a piece of ice that meets all stipulations used in referring to it.

  - Example: The Runner cannot trigger the first ability on Arruaceiras Crew at an arbitrary time to give themself a tag. They can only use this ability during an encounter.
  - Example: The Runner can use the {trash} ability on Abagnale during an encounter with a code gate. They can only use the interface ability on Abagnale during an encounter with a code gate that Abagnale has sufficient strength to interface with.


**9.5.7.** Steps of Using a Paid Ability


- a. Announce the intent to trigger the paid ability.

- b. Pay the trigger cost. The ability and its source are considered used. "When used" abilities meet their trigger conditions. (The cost-paid checkpoint then occurs.)

- c. Announce any targets for the ability's first instruction. That instruction then becomes imminent.

- d. An interrupt window occurs, during which abilities can modify, prevent, or avoid the imminent effects.

- e. Resolve the instruction, applying any changes to its effects from interrupts that were resolved.

- f. A checkpoint occurs.

- g. If there are more instructions to resolve, announce any targets for the next instruction. That instruction then becomes imminent. Return to (d).

- h. Resolution of the paid ability is complete.



## 9.6. Conditional Abilities

**9.6.1.** A conditional ability is an ability that a player can or must trigger at a specific point in the game. Conditional abilities always include a primary condition and one or more instructions, but they have no special syntax requirements.


- a. The primary condition of a conditional ability is usually a trigger condition, but can also be a static condition.

- b. The primary condition of a conditional ability is often, but not always, written at the front of the ability. See rule 9.3.7c for details about conditional ability syntax.


**9.6.2.** When a conditional ability has met its condition, one or more instances of that conditional ability is created pending in the next reaction window that opens. Each instance of a conditional ability is a separate copy of that ability that resolves independently of the others. An instance of an ability with the pending status is waiting for its controller to trigger it.

**9.6.3.** A conditional ability with a static condition can only have one instance at a time.

**9.6.4.** A conditional ability with a trigger condition can have multiple instances.


- a. If a trigger condition is met again while an instance of the corresponding ability is already pending, imminent, or resolving, a new instance can still become pending in the next reaction window.

- b. If the trigger condition of an ability is met more than once between consecutive checkpoints, multiple instances of that ability become pending in the next reaction window.

  - Example: The Runner plays Singularity, trashing 3 Corp cards simultaneously. Since Hostile Infrastructure's trigger condition is met separately for each card trashed, the next checkpoint handles all of those occurrences, and 3 instances of the ability become pending in the same reaction window.
  - Example: The Runner controls Blackguard and plays Satellite Uplink, exposing 2 cards in a single instruction. In the next checkpoint, two instances of Blackguard's ability become pending, one corresponding to each card that was exposed.


**9.6.5.** The trigger condition of most conditional abilities describes an occurrence that allows it to become pending. These rules about trigger conditions also apply to conditional abilities with static conditions, except as described in section 9.6.7.


- a. During each checkpoint, the game checks whether any conditional abilities have met their trigger conditions. If any have, a reaction window opens to resolve those abilities (See section 10.3). This reaction window is fully resolved and closed before the game proceeds to the next instruction of the original effect or game rule.

- b. A conditional ability can only recognize its trigger condition occurring if the ability is active at the time the effect or game state that meets its trigger condition is processed by step 10.3.1a of a checkpoint. If a conditional ability becomes active after its trigger condition was met and a checkpoint has occurred, that ability does not become pending, even if a reaction window associated with that condition is open.

  - Example: The Corp is resolving their "turn begins" abilities, and they trash Reaper Function to do 2 net damage. This trashing allows the Corp to use the ability on Ob Superheavy Logistics to install and rez Nico Campaign. The Corp cannot take credits from Nico Campaign this turn because the "turn begins" trigger condition has already been processed.
  - Example: The Corp uses Architect Deployment Test to install and rez a copy of Tranquility Home Grid, which is the first card installed this turn. After completing the installation from this effect, the Corp pays the rez cost of Tranquility Home Grid. Since Architect Deployment Test specified that all costs are ignored, the rez cost is reduced to 0 (see rule 1.16.5c), but this step still happens and is still followed by a checkpoint (see rule 1.16.3a). This is the checkpoint that processes that a card was installed, but Tranquility Home Grid is not yet active and an instance of its ability cannot be created. After the checkpoint and any corresponding reaction window, the Corp finishes rezzing Tranquility Home Grid and it becomes active.

- c. A conditional ability can only recognize its trigger condition occurring if all the stipulations and requirements listed in the trigger condition are met at the moment that the trigger condition would occur.

  - Example: The entire trigger condition on Quantum Predictive Model is "If the Runner is tagged when Quantum Predictive Model is accessed". Quantum Predictive Model can only meet its trigger condition if the Runner is tagged at the time the access occurs. Even if Casting Call is hosted on Quantum Predictive Model, the tag will be given after the access begins, and no ordering of the abilities will allow Quantum Predictive Model to meet its trigger condition.

- d. Additional stipulations and requirements listed in the instructions of a conditional ability are checked as part of the ability's effects and are not part of the ability's trigger condition. For the effects under the scope of this kind of condition to resolve, the condition only needs to be met when the relevant instructions resolve. This can occur even if some or all of those conditions were not met at the time the ability met its trigger condition.

  - Example: If a Runner with 1 link has both Underworld Contact installed and The Supplier installed hosting a Dyson Mem Chip, both Underworld Contact and The Supplier meet their trigger conditions at the same time. The Runner can trigger The Supplier first, installing the Dyson Mem Chip, thus allowing Underworld Contact to recognize that the Runner has 2 link and give them 1 credit, even though the Runner did not have 2 link at the time the Underworld Contact became pending.

- e. The phrase "If successful" in reference to a run is a trigger condition with specific rules. See section 6.7.4. The phrases "If successful" and "If unsuccessful" are also frequently used as trigger conditions relating to the results of a trace. See section 10.8.


**9.6.6.** Trigger conditions look for an instantaneous change in the game state. The next checkpoint after that change takes place marks an instance of the ability as pending for each occurrence of the change that was looked for.


- a. Some conditional abilities refer to the game state before the trigger condition was met, using the word "had". The previous game state referred to is the game state at the time of step 10.3.1a of the checkpoint immediately preceding the checkpoint in which the ability became pending.

  - Example: The ability of Weyland Consortium: Built to Last only causes the Corp to gain 2{c} when they advance a card that "had no advancement counters". This ability becomes pending immediately after an instruction resolves in which the Corp advances a card, and the requirement is checked against the state of the game at the previous checkpoint, before that advancement took place.


**9.6.7.** Conditional abilities with a static condition instead of a trigger condition describe an effect that must be performed repeatedly, if possible, while the condition is true. Such abilities usually attempt to end their own repetition by uninstalling their source.


- a. During each checkpoint, the game checks whether any conditional abilities with static conditions should become pending, alongside conditional abilities with trigger conditions. However, there are further requirements for this kind of ability to be marked pending.

- b. A conditional ability with a static condition can only be marked pending during a checkpoint if the condition is true at the beginning of that checkpoint.

- c. A conditional ability with a static condition can only be marked pending if no other instance of the same ability from the same source is already pending, imminent, or resolving.

- d. If a conditional ability with a static condition resolves and no instruction in that ability had any expected effects at the time that instruction's interrupt window opened, that ability cannot be marked pending again until a timing structure step is completed.

  - Example: Parasite is hosted on a piece of ice with 0 strength. Parasite's ability tries to trash that ice. If the effect of an interrupt ability prevents the ice from being trashed without increasing its strength, the condition is still true, so the ability will become pending again. But this does not occur while the previous instance of the ability is still pending or resolving. Conversely, if a static ability (such as the one on Architect) prohibits Parasite from trashing the ice, there are no effects Parasite's ability can perform, and therefore the ability resolves only once per timing structure step.


**9.6.8.** A player can trigger a conditional ability while they have priority in a reaction window in which the ability is pending.


- a. Once a player triggers a conditional ability, that instance of the ability loses its pending status. Other pending instances of the ability are unaffected and can still be triggered later, regardless of whether those instances are associated with the same reaction window.

- b. If the {interrupt} symbol appears before a conditional ability's text, that ability is an interrupt. These abilities will become pending in interrupt windows rather than reaction windows, but are otherwise resolved in the same way as non-interrupt conditional abilities. See section 9.9.


**9.6.9.** If a conditional ability gives its controller a choice of whether to apply its effects, such that the ability could potentially have no effects at all, it is considered an optional conditional ability. These abilities can usually be recognized by the presence of permissive words such as "may" or "allows", or by a restriction such as "Use this ability only once per turn." If the ability is not optional, then it is a mandatory conditional ability.


- a. Instances of optional conditional abilities can still be pending when their controller passes in the corresponding priority window. Players are not required to trigger optional conditional abilities.

- b. Players must trigger all instances of pending mandatory conditional abilities they control in a given priority window before they can pass in that window. See section 9.2.8.

- c. Both optional and mandatory conditional abilities may have optional parts to their effects. Even if triggering the ability is mandatory, its controller may still decline any optional constituent effects that arise during its resolution.

- d. A conditional ability and its source are considered used when any optional component of the ability's effects is carried out.


**9.6.10.** If any instances of a conditional ability are pending and the conditional ability itself becomes inactive, those instances lose their pending status and will not resolve.

  - Example: The Runner has both Aesop's Pawnshop and Drug Dealer installed. Both cards have abilities that meet their trigger conditions when the Runner's turn begins. If the Runner chooses to trigger Aesop's Pawnshop first, and then uses it to trash Drug Dealer, Drug Dealer's pending ability can never be triggered, so the Runner does not lose a credit.

**9.6.11.** If a priority window closes while any abilities still have pending instances associated with that window, the remaining instances lose their pending status and cannot be triggered or resolved. See rule 9.2.8f.

**9.6.12.** Once a player triggers a conditional ability and its instructions become imminent, the ability becomes independent of its source and rule 9.1.4 applies to it.

**9.6.13.** A delayed conditional ability is a conditional ability maintained by a lingering effect (see section 9.10). Unlike other lingering effects, some delayed conditional abilities are created without an explicitly stated duration.


- a. If an "if successful" ability contains a nested delayed conditional ability referring to breaching the attacked server, that ability applies to the breach at step 6.9.5b of the run the "if successful" ability is associated with. Its duration expires at the end of the run. See rule 6.7.4b.

- b. If an instruction creates a delayed conditional ability and specifies a duration, the delayed conditional ability is active until that duration expires.

  - Example: In the Groove creates a delayed conditional ability that specifies a duration of "this turn". Since a duration is specified, it is not limited to only being triggered once. The ability becomes pending and can resolve every time its trigger condition is met, and its existence only expires at the end of the turn.

- c. Any other delayed conditional ability exists until the next time it resolves, after which it expires.

  - Example: Joshua B.'s ability allows the Runner to gain {click}. If they do, it creates a delayed conditional ability that is independent of Joshua B. itself, which will meet its trigger condition only once, when that turn ends. After that ability resolves, the game no longer maintains the lingering effect creating the ability.

- d. If an effect tries to create a delayed conditional ability with the trigger condition "When this run ends", but there is no run in progress, then the lingering effect is not created.

  - Example: The Runner encounters a piece of ice outside of a run, and uses Mayfly to break its subroutines. Since there is no run in progress, the delayed conditional ability inside Mayfly's interface ability cannot be created, and Mayfly will not be trashed.


**9.6.14.** Some cards refer to a class of conditional abilities according to their trigger condition.


- a. A "when encountered" ability is any ability that could meet its trigger condition at step 6.9.3a of an encounter with its source.

- b. A "when installed" ability is any ability that could meet its trigger condition at step 8.5.16f of the process of installing its source.

- c. A "when scored" ability is any ability on an agenda that could meet its trigger condition as a result of the Corp choosing the option to score that agenda during a paid ability window (see rule 9.2.7d).

- d. If an effect attempts to resolve a conditional ability according to one of the classes identified in section 9.6.14, the ability is marked pending as though the stipulation of that class had occurred. Any additional requirements of the trigger condition in question must still be met by the game state.

  - Example: The Corp has scored AstroScript Pilot Program and Market Research. They forfeit one of those agendas to play 24/7 News Cycle and resolve the "when scored" ability of the other agenda, even though neither agenda was actually scored at this time. The Corp can forfeit Market Research to resolve AstroScript Pilot Program's ability and put an agenda counter on it, with no other stipulations. If the Runner is tagged, the Corp could instead forfeit AstroScript Pilot Program to put an agenda counter on Market Research. However, if the Runner is not tagged, the additional requirement of Market Research's trigger condition will not be met, so its "when scored" ability cannot become pending or resolve.


**9.6.15.** Steps of Triggering and Resolving a Conditional Ability


- a. Announce that you will trigger one of the pending abilities associated with the reaction window that gave you priority.

- b. Announce any targets for the ability's first instruction. That instruction then becomes imminent.

- c. An interrupt window occurs, during which abilities can modify, prevent, or avoid the imminent effects.

- d. Resolve the instruction, applying any changes to its effects from interrupts that were resolved. If the controller of the ability chooses to resolve an optional effect contained in this instruction, and this is the first instruction for which that player has done so, "when used" conditionals meet their trigger conditions.

- e. A checkpoint occurs.

- f. If there are more instructions to resolve, announce any targets for the next instruction. That instruction then becomes imminent. Return to (c).

- g. Resolution of the conditional ability is complete.



## 9.7. Play Abilities

**9.7.1.** Any ability on an event or operation that is not a paid, conditional, or static ability is a play ability. Play abilities are the abilities that resolve as part of playing that event or operation during step 8.6.7f.

  - Example: The operation card Oppo Research has several abilities of different types. The first ability reads "Play only if the Runner stole or trashed a Corp card during their last turn." This is a static ability that contains a restriction and no declarations. The second ability ("After you resolve this operation, your action phase ends.") is a conditional ability that meets its trigger condition and resolves after the Corp finishes playing and resolving Oppo Research. The third and fourth abilities contain instructions with no trigger conditions or paid ability trigger costs, so they are both play abilities. They resolve in sequence while Oppo Research is being played.

**9.7.2.** Steps of Resolving a Play Ability


- a. Announce any targets for the ability's first instruction. That instruction then becomes imminent.

- b. An interrupt window occurs, during which abilities can modify, prevent, or avoid the imminent effects.

- c. Resolve the instruction, applying any changes to its effects from interrupts that were resolved.

- d. A checkpoint occurs.

- e. If there are more instructions to resolve, announce any targets for the next instruction. That instruction then becomes imminent. Return to (b).

- f. Resolution of the play ability is complete.



## 9.8. Subroutines

**9.8.1.** A subroutine is an ability beginning with the {sub} symbol. Only ice can have subroutines.

**9.8.2.** Subroutines always have a specified order. Subroutines on a piece of ice are categorized based on how the ice gained them, as described in section 9.8.3. All subroutines in the first category presented (rule 9.8.3a) are ordered first, followed by all subroutines in the second category (rule 9.8.3b), and so on. Each category's description includes the rule for how multiple subroutines within that category are ordered.


- a. One card, TL;DR, has an ability which can grant a second copy of each subroutine on a piece of ice. Each subroutine created this way is ordered immediately after the subroutine it copied, regardless of categories. If any subroutines are removed while the copies are still active, the copies remain in their relative order as if the removed subroutines were still present.

- b. One card, Hive, has a static ability that removes its own printed subroutines. Subroutines are lost to this ability beginning with the last printed subroutine and working backwards.

- c. Some Corp abilities grant 1 or more subroutines "in the order of your choice". When such an ability resolves, the Corp declares the order of each subroutine added by the ability relative to each other subroutine the ice has at that time, regardless of categories.

  - Example: The Runner encounters a copy of Lancelot hosting a condition counter from Sub Boost. The Corp reveals a copy of Merlin in HQ to add a "{sub} Do 2 net damage." subroutine to Lancelot. The Corp declares whether the subroutine from Merlin will be added before Lancelot's printed "{sub} Trash 1 installed program." subroutine, between the printed subroutine and the "{sub} End the run." subroutine granted by the condition counter, or after the "{sub} End the run." subroutine.

- d. If multiple subroutines are added to a piece of ice by the same category of effect at the same time in an unspecified order, the Corp declares the relative order of those subroutines at the time the ice gains them.


**9.8.3.** The categories of subroutines used to determine their order, as described in section 9.8.2, are as follows:


- a. Subroutines gained from an ability, except a static ability on the ice itself, where the ability granting the subroutines states they are added before other subroutines. Subroutines within this category are ordered based on when the effect granting them began to apply, with the most recently added subroutines first.

  - Example: The Runner encounters a copy of Loki while Stick and Poke is installed and Warden Fatuma is rezzed. Since Warden Fatuma's ability is a static ability, the subroutine it grants is already present when the encounter begins. The Runner resolves the ability from Stick and Poke, then the Corp resolves the ability from Loki, choosing a copy of Tithe. The subroutines on Loki are, in order: "{sub} Do 1 net damage.", "{sub} Gain 1{c}.", "{sub} Do 1 net damage. The Runner draws 1 card.", "{sub} The Runner loses {click}.", and "{sub} End the run unless the Runner shuffles all cards from the grip into the stack."

- b. Subroutines gained from a static ability on the ice itself, where the ability states the subroutines are added before other subroutines. Subroutines in this category on a single piece of ice are always identical. If the number of such subroutines increases, the new copies are added after any previously-existing subroutines in this category. If the number of such subroutines decreases, subroutines are lost beginning with the last subroutine the ice previously had from the same ability and working backwards.

- c. Subroutines printed on the ice. Subroutines this category are ordered in the same way as they are written in the ice's card text.

- d. Subroutines gained from a static ability on the ice itself, where the ability either states the subroutines are added after other subroutines or does not specify an order. Subroutines in this category on a single piece of ice are always identical. If the number of such subroutines increases, the new copies are added after any previously-existing subroutines in this category. If the number of such subroutines decreases, subroutines are lost beginning with the last subroutine the ice previously had from the same ability and working backwards.

  - Example: The Corp has 3 cards in HQ while the Runner is encountering Ashigaru. After the Runner breaks Ashigaru's 3 subroutines, the Corp uses Panic Button to draw another card. The new subroutine is added after the previous 3, so the Runner can use Gbahali to break it.
  - Example: The Corp has 3 cards in HQ while the Runner is encountering Ashigaru. The Runner breaks the first subroutine, then uses Utopia Shard to force the Corp to discard 2 cards. Ashigaru correspondingly loses its last 2 subroutines, leaving behind the subroutine that was already broken.

- e. Subroutines gained from an ability, except a static ability on the ice itself, where the ability either states the subroutines are added after other subroutines or does not specify an order. Subroutines within this category are ordered based on when the effect granting them began to apply, with the oldest subroutines first.

  - Example: The Runner encounters Marker, and its subroutine resolves. The next ice the Runner encounters is Brainstorm. When the encounter begins, the lingering effect from Marker gives Brainstorm an "{sub} End the run." subroutine. Then, Brainstorm's conditional ability resolves, giving it the appropriate number of "{sub} Do 1 core damage." subroutines. The "{sub} Do 1 core damage." subroutines are all ordered after the "{sub} End the run." subroutine.


**9.8.4.** For each encounter, the subroutines on the encountered ice have a status of broken or unbroken with respect to that encounter.


- a. When an encounter begins, each of the encountered ice's subroutines becomes unbroken with respect to that encounter.

- b. When a piece of ice gains 1 or more new subroutines during an encounter with it, those subroutines come into existence unbroken with respect to that encounter.

- c. When an encounter ends, card abilities can still check whether subroutines were broken or unbroken during that encounter.


**9.8.5.** To break a subroutine is to change its status with respect to the active encounter from unbroken to broken. Many cards that can allow a player to break subroutines are icebreakers (see section 3.9.5). If the Runner breaks all the subroutines on the encountered ice, they fully break that ice (see section 6.5.7).

**9.8.6.** Only unbroken subroutines can be chosen as targets for abilities that would break them.


- a. An ability that breaks "all subroutines" does not target any subroutines, and can be used as long as the encountered ice has at least 1 unbroken subroutine.

- b. One card, Grappling Hook, has the ability to break "all but 1 subroutine" on the encountered ice. To trigger this ability, its controller chooses as its target the 1 subroutine that will not be broken. When the ability resolves, it breaks all unbroken subroutines except the chosen subroutine.

  - Example: The Runner uses two copies of Grappling Hook to break all of the subroutines on Heimdall 1.0. For the first Grappling Hook, the Runner chooses the unbroken "{sub} Do 1 core damage." subroutine as the target, and Grappling Hook breaks both "{sub} End the run." subroutines. For the second Grappling Hook, the Runner chooses one of the already-broken "{sub} End the run." subroutines as the target. They can choose the broken subroutine as Grappling Hook's target because Grappling Hook will not attempt to break the chosen subroutine. When the ability resolves, Grappling Hook breaks the "{sub} Do 1 core damage." subroutine, but does nothing to the other "{sub} End the run." subroutine, because it is already broken.


**9.8.7.** At step 6.9.3c of an encounter, the Corp resolves subroutines that are unbroken with respect to that encounter.


- a. Resolving subroutines is mandatory and does not open a priority window.

- b. The Corp resolves the unbroken subroutines 1 at a time in order. The order of subroutines is discussed in section 9.8.2.

- c. If the encounter ends during this process, no more subroutines are resolved.

  - Example: The Runner encounters Little Engine and does not break any subroutines. Little Engine's first subroutine resolves and ends the run. Since the run and therefore the encounter have ended, no more subroutines resolve. The Runner will not gain 5{c}.

- d. One card, Street Magic, allows the Runner to set the order in which subroutines are resolved. If this ability is active when step 6.9.3c is reached for the first time in an encounter, the Runner declares an order for the unbroken subroutines, and the Corp must resolve them in this order instead of their normal order. The new order is determined before any subroutines are resolved.


**9.8.8.** Once a subroutine's first instruction becomes imminent, the subroutine becomes independent of its source and rule 9.1.4 applies to it.

**9.8.9.** One card, Tsakhia "Bankhar" Gantulga, can apply a replacement effect while a subroutine is imminent in order to resolve a different subroutine instead. The replaced subroutine is treated as having the same source as the original imminent subroutine.

  - Example: The Runner encounters Bloop while Tsakhia "Bankhar" Gantulga's replacement effect applies. All 3 of Bloop's subroutines would resolve, but are replaced by "{sub} Do 1 net damage." by the replacement effect. These subroutines count as resolving from Bloop, so when the Runner passes Bloop after the encounter, the Runner can use Persephone to trash the top card of their stack and the top 3 cards of R&D.

**9.8.10.** Steps of Resolving a Subroutine


- a. The subroutine itself becomes imminent.

- b. An interrupt window occurs, during which abilities can prevent the subroutine from resolving. If this happens, the remaining steps are not carried out.

- c. Announce any targets for the subroutine's first instruction. That instruction then becomes imminent.

- d. An interrupt window occurs, during which abilities can modify, prevent, or avoid the imminent effects.

- e. Resolve the instruction, applying any changes to its effects from interrupts that were resolved.

- f. A checkpoint occurs.

- g. If there are more instructions to resolve, announce any targets for the next instruction. That instruction then becomes imminent. Return to (d).

- h. Resolution of the subroutine is complete.



## 9.9. Interrupts and Replacement Effects

**9.9.1.** An interrupt is an ability that modifies either the effects of another instruction or the context in which that instruction will be resolved. Interrupts can be written in the form of a paid ability or a conditional ability.


- a. Interrupt abilities are denoted by the {interrupt} symbol appearing at the beginning of the ability, or by certain keywords in the ability's text.

- b. Any paid or conditional ability that uses the word "prevent", "avoid", or "would" is an interrupt ability.


**9.9.2.** The expected effects of an imminent instruction are the effects described by the instruction's text, modified by any static abilities that affect it and any replacement effects or interrupt effects that have been applied. The expected effects of an instruction are continually updated throughout the interrupt window.


- Example: The Runner plays Process Automation and the instruction "Gain 2{c} and draw 1 card." becomes imminent. The expected effects of this instruction are that the Runner will gain 2{c} and draw 1 card.
- Example: The Runner plays Process Automation after Lockdown's subroutine resolves. The instruction "Gain 2{c} and draw 1 card." becomes imminent, but the Runner cannot draw cards. The expected effect of this instruction is that the Runner will gain 2{c}.

- a. When an instruction resolves, its expected effects correspond to the effects that occur, except for situations where rule 9.9.7d applies.


**9.9.3.** In order to trigger an interrupt, it must be relevant to an imminent instruction. An interrupt is relevant to an instruction if at least one of the following is true:


- a. Its effects could prevent or avoid part or all of the imminent instruction's expected effects. See section 9.9.5.

- b. Its effects could modify a value associated with the imminent instruction's expected effects. See section 9.9.6 and section 9.9.7.

- c. Its effects could create a replacement effect that applies to the imminent instruction's expected effects. See rule 9.9.10.

- d. It has a trigger condition using the word "would" that is met by the imminent instruction's expected effects.

  - Example: The trigger condition on The Class Act's second ability is "The first time each turn you would draw any number of cards". Even though this ability's effects do not directly modify the imminent instruction's effects, the ability is relevant to an instruction that is expected to draw cards.


**9.9.4.** Whenever an instruction becomes imminent, an interrupt window opens to allow players to modify the effects of that instruction. Section 9.2.9 contains the rules for handling priority during interrupt windows.


- a. As the window opens, the initial expected effects of the instruction are determined, then any active replacement effects that replace all or part of the expected effects are applied. If multiple replacement effects could apply, the order in which they are applied follows the rules in section 9.9.11.

- b. After replacement effects have been applied, but before players receive priority, active conditional ability interrupts that are relevant to the imminent instruction are marked pending and are associated with the window. If a conditional ability interrupt becomes active after the interrupt window opens, it does not become pending.

- c. While a player has priority in an interrupt window, they can trigger a conditional ability interrupt they control only if it is pending in that window and still relevant to the imminent instruction.

  - Example: An instruction is imminent that would trash an installed copy of Harbinger. The Runner uses Sacrificial Construct to prevent Harbinger from being trashed. The Runner can no longer trigger Harbinger's interrupt ability, even though it is still pending, because the expected effects of the instruction no longer include Harbinger being trashed and therefore the ability is no longer relevant.
  - Example: An instruction is imminent that would give the Runner 2 tags. The Runner trashes Decoy to use its ability to avoid 1 of the tags, which meets the trigger condition for Thunder Art Gallery's ability. The latter resolves as a chain reaction (see rule 9.1.2a) while the interrupt window is still open, allowing the Runner to install No One Home, which has a conditional ability interrupt that is relevant since the instruction is still expected to give the Runner 1 tag. However, No One Home was not active when the interrupt window opened, so its ability is not pending and cannot be triggered.

- d. While a player has priority in an interrupt window, they can use a paid ability interrupt that is relevant to the imminent instruction. The ability does not have to have been active when the interrupt window opened, nor does a paid ability window need to be open at this time.

  - Example: An instruction is imminent that would give the Runner 2 tags. The Runner trashes Decoy to use its ability to avoid 1 of the tags, which meets the trigger condition for Thunder Art Gallery's ability. The latter resolves as a chain reaction (see rule 9.1.2a) while the interrupt window is still open, allowing the Runner to install another copy of Decoy. The Runner receives priority again in the original interrupt window and uses the second Decoy's ability to avoid the other imminent tag.


**9.9.5.** To prevent or avoid an effect is to stop that effect from happening. The terms "prevent" and "avoid" are synonymous.


- a. Some abilities look for an ordinal instance of an effect that "would" take place within a specified time period. These abilities look for the appropriate number of times that effect becomes imminent, not whether the effect actually resolves. Preventing or replacing an effect does not allow these abilities to meet their trigger conditions again in the same time period.

  - Example: Tori Hanzō's interrupt can only be used "the first time you would do net damage" during a run. If the first net damage that becomes imminent is prevented by the Runner using Feedback Filter, Tori Hanzō cannot be used on any other instances of net damage for the remainder of the run, as the next imminent net damage is the second time the Corp "would" deal net damage, not the first time.

- b. Abilities that do not use "would" do not see effects that are prevented, as those effects did not occur.


**9.9.6.** Some instructions make use of an associated value. These values can be changed by interrupts and replacement effects.


- a. A number of tags the Runner would take from an effect is a value. Avoiding a number of those tags decreases that value. The value for a number of tags the Runner takes must be greater than 0 for the effect giving tags to occur.

- b. An amount of damage the Runner would take from an effect is a value. Preventing an amount of that damage decreases that value. The value for an amount of damage the Runner takes must be greater than 0 for the damage effect to occur.

- c. A cost that would be paid while resolving an effect is a value. Rule 1.16.2a applies to the final value at the time the cost is paid. See also rule 1.16.1d.

  - Example: Patchwork's interrupt modifies a play cost or install cost, so it is relevant to any instruction where a card will be played or installed and the corresponding cost paid.

- d. The base trace strength of a trace attempt is a value. See section 10.8 for rules about trace attempts. A base trace strength value does not need to be greater than 0 for the trace attempt to occur.


**9.9.7.** Values associated with an imminent instruction are calculated continuously as part of the instruction's expected effects.


- a. While an instruction is imminent, values associated with that instruction can be reduced below 0. Values 0 or lower can still be modified by other interrupt abilities while the instruction remains imminent.

  - Example: The Runner takes a tag on their turn, allowing the Corp to trigger Mr. Stone. While the 1 meat damage from Mr. Stone's ability is imminent, both players can trigger interrupts to modify the damage effect. Since it is the Runner's turn, they receive priority first and use Biometric Spoofing to prevent 2 damage. The imminent effect is now expected to do -1 damage. When the Corp receives priority, they trigger two copies of The Cleaners that each add 1 to the expected damage, increasing the total to 0 and then to 1. The ability will resolve with 1 damage even though the amount of damage was temporarily 0 or less while the effect was imminent.

- b. If an effect prevents "all damage" or "any amount of damage" while an instruction is imminent, the damage is removed from the expected effects entirely, and there is no longer a value to be modified. If an effect avoids "all tags" or "any number of tags" while an instruction is imminent, taking tags is removed from the expected effects entirely, and there is no longer a value to be modified.

  - Example: The Runner installs Net-Ready Eyes and uses Chrome Parlor to prevent all of the damage from its first ability. There is no longer an imminent damage effect, so the Corp cannot use The Cleaners.

- c. One card, Bio-Modeled Network, has an instruction that prevents "all but 1 net damage." This ability is relevant when the imminent instruction's expected effects include doing net damage and the amount of the damage is 2 or more. The effect of this ability is to set the value for the amount of damage to 1. Previously-applied modifiers to the value are ignored. The value can still be modified by other interrupts after being set to 1 this way.

- d. Some types of values must be greater than 0 for the associated effect to occur. See section 9.9.6. At the time an instruction resolves, if any value of one of those types is 0 or less, as much of that ability resolves as possible without applying the part of the effect making use of that value, following rules 1.2.3 and 1.2.4 of the Golden Rules.

  - Example: The Runner accesses Breached Dome. While the instruction from its "when accessed" ability is imminent, the Runner uses Biometric Spoofing to prevent 2 damage. When the ability resolves, the value for the amount of damage is -1, which is less than or equal to 0, so the damage effect cannot occur and the Corp only trashes the top card of the stack.

- e. Any effects making use of modified values maintain their restrictions and specifications with the new values.

  - Example: While the result of a successful trace from Flare is imminent, the Corp triggers The Cleaners to increase the imminent meat damage by 1. Even though the damage from Flare has been increased from 2 to 3, none of that meat damage can be prevented.

- f. One card, Guru Davinder, has a non-interrupt conditional ability with the trigger condition "Whenever this resource prevents 1 or more damage". The trigger condition is met when the following occurs: the expected effects of an imminent instruction include a damage effect with a value greater than 0 for the amount of the damage; an interrupt ability resolves from the same source object as the conditional ability; and the interrupt decreases the value for the damage amount or removes the damage effect entirely.

  - Example: The Runner accesses Angelique Garza Correa and the Corp pays 2{c}, making the "Do 2 meat damage" instruction imminent. The Runner uses Plascrete Carapace to prevent 2 meat damage, leaving the imminent instruction with the expected effect "Do 0 meat damage.". At this point, the 0 value for the damage can still be modified by abilities like The Cleaners. But then the Runner resolves the interrupt ability on Guru Davinder, which removes the damage effect altogether. Since the value for the damage was 0, Guru Davinder's second ability does not meet its trigger condition and the Runner does not have to trash Guru Davinder or pay 4{c}. There is no longer an imminent damage effect, so the Corp cannot use The Cleaners.


**9.9.8.** A replacement effect modifies another effect by replacing some or all of the original effect's behavior with different behavior. Replacement effects are indicated by the word "instead" in their text. Once created, replacement effects apply automatically and do not require players to explicitly trigger them.


- a. Interrupts can introduce replacement effects that apply to the currently imminent instruction. See rule 9.9.10.

- b. Static abilities can stipulate replacement effects that apply whenever appropriate while the static ability is active.

- c. Other abilities can create replacement effects ahead of time, which apply to effects that could happen later. This is a type of lingering effect (see section 9.10).


**9.9.9.** Replacement effects most commonly apply to the effects of an instruction, but some apply to static abilities or alter the durations of lingering effects.


- a. If a replacement effect modifies the effects of a static ability or the duration of a lingering effect, this modification applies continuously for as long as the replacement effect itself is active, and no longer applies once the replacement effect or the original effect being modified becomes inactive.

  - Example: Gebrselassie's static ability replaces the durations of certain lingering effects affecting its host with a duration of "for the remainder of the turn." If Gebrselassie is uninstalled or moved to a different host, its replacement effect no longer applies to those lingering effects, so they revert to their normal durations. If any of those durations have already expired, the corresponding effects will end at the next checkpoint.

- b. If an active replacement effect can apply to the expected effects of an imminent instruction, it is applied as the interrupt window opens, and the expected effects of the instruction are updated accordingly. This happens before the expected effects are checked for relevant active abilities that could become pending. Replacement effects created by interrupts will not yet be active at this time and are applied according to rule 9.9.10 instead.

- c. Each replacement effect can only apply once to any single effect. Once a replacement effect has been applied, even if the new effect it creates (or a new effect created by a later replacement effect) still includes an occurrence of the effect that gets replaced, the same replacement effect cannot be applied to that effect again.

  - Example: The Runner accesses Project Vacheron and its interrupt ability resolves. The interrupt creates a replacement effect that will override adding the agenda to the Runner's score area. The Runner will still steal Project Vacheron, but they will do so by adding it to their score area with hosted agenda counters. Even though the modified effect still includes adding Project Vacheron to the Runner's score area, there is no way for the replacement effect to apply again to its own results.


**9.9.10.** Some interrupts create replacement effects to modify an instruction that is already imminent. When the interrupt resolves, if the replacement effect can apply to the expected effects of the imminent instruction, it is applied immediately.

  - Example: An instruction is imminent with an expected effect of the Corp doing net damage to the Runner. The Corp uses Tori Hanzō's ability, creating a replacement effect replacing net damage with core damage. The replacement effect applies immediately, so after Tori Hanzō's ability resolves, the expected effect is now to do 1 core damage. When players get priority again, their interrupts are relevant or not based on the new expected effects.

**9.9.11.** If multiple replacement effects would apply to an effect acting on a targeted card, that card's controller chooses the order in which the replacement effects apply. If multiple replacement effects would apply to any other effect, the controller of the base effect chooses the order in which the replacement effects apply.


- a. After applying one replacement effect, a subsequent replacement effect can only be applied if the effect it was to replace is still expected. A replacement effect cannot apply without something to replace.

  - Example: The Runner chooses HQ with Security Testing when their turn begins, then plays Account Siphon. When the run becomes successful, both Security Testing and Account Siphon create replacement effects. When step 6.9.5b of the run becomes imminent, the Runner must choose to apply either the replacement effect from Security Testing or the one from Account Siphon. Since neither effect creates a new instance of breaching the server, the effect not chosen to apply first has nothing to replace and does not apply.
  - Example: The Runner chooses R&D with Security Testing when their turn begins, then plays Showing Off. When a run on R&D becomes successful, both Security Testing and Showing Off create replacement effects. When step 6.9.5b of the run becomes imminent, the Runner must choose to apply either the replacement effect from Security Testing or the one from Showing Off. If they choose to apply Security Testing first, Showing Off's effect has nothing to replace and does not apply. If they choose to apply Showing Off first, the expected effects still include breaching R&D, albeit not in the usual way. Security Testing can still replace this breach, so it applies next. Either way, the Runner will gain 2{c} and will not breach the server.



## 9.10. Lingering Effects

**9.10.1.** Instructions can create lingering effects that exist beyond their resolution. Once a lingering effect is created, it exists independently of its source even if the source becomes inactive. Lingering effects remain active only for the duration specified in the instruction that created them. Lingering effects with durations that have expired are removed from the game state at step 10.3.1b of each checkpoint.

**9.10.2.** Some lingering effects exist only to maintain the existence of another ability. Delayed conditional abilities are maintained this way and may have an implicit duration. See section 9.6.13.

**9.10.3.** Some lingering effects maintain a choice that a player made while resolving an ability in order for another ability or effect with the same source object to make use of that choice.


- a. If the choice is only referred to by another lingering effect, the lingering effect maintaining the choice has the same duration as the other lingering effect.

  - Example: The Runner resolves Pelangi's paid ability and chooses an ice subtype. The ability then creates a lingering effect adding the chosen subtype to the encountered ice. Both the choice of subtype and the effect adding it to the ice expire at the end of the encounter.

- b. If a conditional ability has a "when your turn begins" trigger condition and no effects other than making a choice, the duration of the lingering effect maintaining the choice expires when that turn ends or the source object becomes inactive.

  - Example: Security Testing's first ability reads, "When your turn begins, you may choose a server." The choice made when resolving this ability on a particular turn should be maintained only until that turn ends. The trigger condition of Security Testing's second ability refers to the chosen server, and it will always look for the server chosen this turn. A successful run on a server that was not chosen this turn will never meet that trigger condition, even if no server was chosen this turn.

- c. If the previous cases do not apply, the duration of the lingering effect expires only when the source object becomes inactive.

  - Example: Femme Fatale's first conditional ability directs the Runner to choose an installed piece of ice. Which ice was chosen is maintained as a lingering effect for as long as Femme Fatale remains active, and its last ability refers to the object that is being remembered by that lingering effect.


**9.10.4.** If a lingering effect is created with a duration based on a timing structure that is not in progress at the time the lingering effect is created, the duration expires immediately, and the lingering effect persists only until the next checkpoint after it is created.


- a. Abilities on an icebreaker that modify their source's strength have an implicit duration of "for the remainder of the current encounter". See rule 3.9.5b.


**9.10.5.** Only lingering effects have durations. An ability that modifies the durations of other abilities functions by keeping their corresponding lingering effects active until an additional duration expires. This type of ability does not interact with effects from static abilities.

  - Example: The Runner controls Gebrselassie hosted on Na'Not'K. They run a server protected by 3 pieces of ice, and use Na'Not'K's last ability to raise its strength to a total of 6. When the run ends, Na'Not'K's static ability will no longer provide +3 strength, but the lingering effect of +2 strength from its paid ability will be maintained by Gebrselassie until the turn ends or Gebrselassie stops being hosted on Na'Not'K. If the Runner makes another run on a server protected by only 1 piece of ice, Na'Not'K will have 4 strength when that run begins.



## 9.11. Identifying Instructions


To correctly resolve conditional abilities and interrupts that could meet their trigger conditions during the resolution of another ability (see rule 9.1.2a), players need to determine where checkpoints occur during the resolution of each ability. This section aids players in making this determination. For clarifications on particular cards, refer to the Rulings section of that card's page on netrunnerdb.com.

**9.11.1.** An instruction normally cannot be paused once it begins to resolve. An instruction can only be paused if a checkpoint opens in the middle of its resolution, which only occurs in the following cases:


- a. Any time a cost is paid, a checkpoint must occur immediately afterward.

- b. When a timing structure is opened, the checkpoints in that timing structure are carried out. See section 9.2.2 for a list of timing structures.

- c. When a player draws cards, a checkpoint occurs at step 8.4.5b of that procedure.

- d. When an event or operation is played, a checkpoint occurs at step 8.6.7e of that procedure.

- e. When a trace is initiated, a checkpoint occurs at step 10.8.6b of that procedure.


**9.11.2.** Each step in a timing structure forms a single instruction, and is accordingly preceded by an interrupt window and followed by a checkpoint.


- a. Some game procedures that are not timing structures are presented as a sequence of steps. These steps do not indicate instruction boundaries, and checkpoints only occur in these procedures when explicitly called for.

  - Example: The steps of installing a card are not separate instructions. Installing a card usually happens in its entirety as part of a single instruction. The only checkpoint that occurs during the procedure of installing a card is at step 8.5.16d, immediately after the install cost is paid.
  - Example: The steps of a checkpoint are not instructions. No checkpoints take place in the middle of another checkpoint.


**9.11.3.** Usually, each sentence in the text of an ability forms a single instruction. After each instruction, an ability pauses its resolution to allow priority windows to open and other abilities to resolve. First, a checkpoint occurs, allowing any appropriate conditional abilities to be marked as pending in a reaction window, then targets are announced for the next instruction. Finally, the next instruction becomes imminent, allowing interrupts relevant to that instruction to resolve in an interrupt window.

**9.11.4.** Some sentences are not instructions, some sentences encompass multiple instructions, and some instructions encompass multiple sentences. These are the cases in which sentences do not correspond one-to-one with instructions:


- a. A sentence is not part of any instruction if it only provides clarification, restrictions or conditions on when or how the ability it appears in can be triggered, or otherwise does not give directions to the players or the game state that would be carried out as the ability resolves.

  - Example: A sentence reading "Use this ability only by spending credits from a stealth card." is not an instruction or part of any instruction. It is a restriction that applies to the entire ability.

- b. If a sentence directs a player to play, install, or access more than one card, each such card is handled as a separate instruction. The instruction ends and a new one begins immediately before each play, install, or access after the first.

  - Example: The Corp plays Shipment from MirrorMorph, which has a single sentence directing them to install up to 3 cards from HQ. This is treated as though it said, "You may install a card from HQ. You may install a card from HQ. You may install a card from HQ.", or equivalently, "You may install a card from HQ. Repeat this process 2 more times."

- c. If a sentence directs a player to choose 1 or more targets, but does not act on that choice or describe any other effects, that sentence and the following sentence form a single instruction.

  - Example: Tinkering's play ability reads, "Choose a piece of ice. That ice gains sentry, code gate, and barrier until the end of the turn." The first sentence does nothing except direct the Runner to select a target, so it forms a single instruction with the second sentence. The target is chosen as the instruction becomes imminent, and the card gains the subtypes when the instruction resolves.

- d. Some older cards have search effects written in the same sentence as the effects that will be performed upon any found cards. Treat these sentences as if ending the search and performing any necessary shuffling is the end of an instruction. A checkpoint occurs while cards found by the search are in the set-aside zone, then the remainder of the sentence is treated as the next instruction.

  - Example: The Runner uses Djinn to search for a program while the Corp has Personality Profiles in their score area. The Runner finds and sets aside a copy of Datasucker and shuffles the stack. This ends an instruction, and since the search has occurred, Personality Profiles's ability meets its trigger condition. The Corp resolves the ability during a reaction window before the next instruction becomes imminent, forcing the Runner to trash a random card from their grip. Finally, the second instruction resolves and the Runner adds the set-aside Datasucker to their grip.

- e. Some older cards direct a player to look at or reveal a set of cards in the same sentence as the effects that will be performed upon those cards. Treat these sentences as if making the cards visible to the relevant player(s) is the end of an instruction. A checkpoint occurs once the cards are visible to the relevant player(s), then the remainder of the sentence is treated as the next instruction.

  - Example: The Corp is resolving the first subroutine on Architect, which, in one sentence, allows the Corp to look at cards in R&D and install one of them. Looking at the top 5 cards of R&D ends the ability's first instruction. Once the Corp is able to see those cards, they can choose a target for the second instruction, which will install it. Since the install is optional, the Corp can also decline to choose a target, and the second instruction will have no effect. After the second instruction resolves, the ability is complete, and the Corp's permission to look at cards in R&D ends.

- f. If an ability contains a nested cost, the choice of whether or not to pay that cost ends an instruction. If the cost is paid (or not paid) such that the corresponding effect will be carried out, that part of the ability becomes the next instruction. See also section 1.16.11.

- g. If an effect directs a player to choose between a set of options that would create different effects, that choice ends an instruction. Each option begins its own instruction or set of instructions, and the one chosen will resolve next.

  - Example: The Runner encounters Data Raven and is forced by its first ability to resolve either "take 1 tag" or "end the run." Making this choice ends the first instruction, and the option the Runner chooses will become imminent after a checkpoint.


**9.11.5.** If an ability initiates a timing structure, its source may have linked abilities that apply during that timing structure. These are frequently written directly after the instruction that creates the timing structure, but they are not instructions in the same ability.



## 9.12. Other Rules and Terminology

**9.12.1.** Simultaneous Effects


- a. If more than one effect attempts to modify the same value, determine its final value from its default value by first applying any effect that sets it to a specific value, then applying each effect that increases the value, and finally applying each effect that lowers the value.

- b. If more than one effect attempts to add or remove the same subtype from a card, count the number of times the subtype is added (including from the card's printed text) and the number of times the subtype is removed. As long as the number of times added is greater, the card has that subtype. As long as the number of times removed is greater or the numbers are equal, the card does not have that subtype.

- c. If more than one effect gives a player a choice of how to resolve an ability or game rule such that both players are instructed to make a choice that can only be made once, the active player makes that choice. The effects that granted the players the choice otherwise resolve as normal.

  - Example: If Chronos Protocol and Titanium Ribs are both active, both players are instructed to choose the card trashed by the first net damage each turn. Since this is impossible, only the active player chooses that card. Since Chronos Protocol also instructs the Corp to look at the Runner's grip and this is not prevented or contradicted by any other ability, this part of the effect happens on either player's turn.

- d. If a static or lingering effect changes whether a second effect is active, what objects it applies to, or what it does to those objects, the second effect depends on the first effect. To determine the characteristics of each object in a game state where dependencies exist, use the following process: begin with each object's printed characteristics. Then, apply effects that modify objects to the game state in the order specified by rule 9.12.1e. Do not apply an effect if it originates from a static ability that is no longer present or no longer active due to a previously-applied effect. When all active effects are applied, this process is complete and each object has its correct characteristics.

- e. When applying effects as indicated by rule 9.12.1d, an effect that is waiting to be applied is independent if it does not depend on any other effects waiting to be applied. Always choose an independent effect as the next effect to apply, if possible. If there are multiple independent effects at once, those effects can be applied in any order relative to each other. (The order chosen should have no impact on the ultimate result.) If there are no independent effects (because each remaining effect depends on another, forming a "loop"), check the source of each of those effects, and treat effects from hosted objects as if they did not depend on effects from the objects they are hosted on.

  - Example: Mother Goddess, Ansel 1.0, and Warden Fatuma are rezzed, and Hush is hosted on Mother Goddess. Mother Goddess, Warden Fatuma, and Hush all produce effects that could modify the characteristics of Mother Goddess, so in order to determine Mother Goddess's true characteristics, we must examine how those effects depend on each other. Mother Goddess's effect grants it the subtypes of Ansel 1.0, including the bioroid subtype, which affects whether Warden Fatuma's ability applies to Mother Goddess, so Warden Fatuma's effect depends on Mother Goddess's effect. Hush's effect removes Mother Goddess's ability, so Mother Goddess's effect depends on Hush's effect. No other effects change how Hush's effect behaves. Since Hush's effect is independent, it must be applied first. When it is applied, Mother Goddess's ability no longer exists, so Warden Fatuma's effect is now independent. It is applied next and grants a subroutine to Ansel 1.0, but not to Mother Goddess, which does not have the bioroid subtype.
  - Example: Hush is hosted on Magnet. Hush's ability creates an effect that removes abilities from Magnet, and Magnet's ability creates an effect that removes an ability from Hush. The dependencies of these effects form a loop, so the hosting relationship of their source objects is considered, and the effect from Hush's ability ignores its dependence on the effect from Magnet's ability. Hush's effect is therefore treated as independent and applied first. Magnet's ability and the resulting effect no longer exist, so they are never applied.


**9.12.2.** Quantities and Sets


- a. If an effect acts on a set of cards for a single purpose, then the effect acts on all of those cards together simultaneously rather than one at a time.

  - Example: The Runner uses Singularity to simultaneously trash a rezzed Warroid Tracker and a rezzed Hostile Infrastructure. Hostile Infrastructure has a trigger condition of "Whenever the Runner trashes a Corp card", so it sees both trashed cards and becomes pending twice. By contrast, Warroid Tracker has a trigger condition of "Whenever the Runner trashes at least 1 card…", so it sees only a single event that trashed multiple cards. Warroid Tracker's ability becomes pending only once.

- b. Some abilities perform effects based on a calculated quantity, usually indicated by a phrase like "for each", "for every", or "plus". If all of the effects tied to a quantity in this way are listed in rule 9.12.2c, then the effects are performed only once, with the values included in the effect aggregated according to the calculated quantity. If a value aggregated in this way is less than or equal to 0, instead the part of the effect associated with that value does not take place at all. If an ability indicates that effects should be performed a number of times or across a number of targets and any part of those effects is not listed in rule 9.12.2c, then the effects are not aggregated.

  - Example: The Runner plays Ritual while they have 3 {click} remaining and an installed copy of The Class Act. Ritual's instruction will have the Runner perform 1 instance of drawing 3 cards, so The Class Act's interrupt ability will first have the Runner look at the top 4 cards of their stack and add 1 to the bottom.
  - Example: The Runner accesses Urtica Cipher with 3 hosted advancement counters. The Corp resolves the instruction "do 2 net damage plus 1 net damage for each hosted advancement counter", doing a single instance of 5 net damage. They can only apply the interrupt ability from Prāna Condenser to the damage once.
  - Example: The Corp has a rezzed copy of NASX and plays realloc(). Its ability has the Corp gain credits based on a "for each", but it also has the Corp derez cards based on the same "for each". Since derezzing cards is not an aggregated effect, none of the effects are aggregated, and the Corp will be able to resolve 2 separate instances of the second ability on NASX.

- c. The following effects are aggregated when performed in a single instruction, as described in rule 9.12.2b: gaining, losing, or spending a number of credits; gaining, losing, or spending a number of clicks; taking, removing, or preventing a number of tags; taking, removing, or preventing a number of bad publicity; looking at or revealing a number of cards from a specified location; drawing a number of cards; trashing a number of cards from specified locations (including by damage); and shuffling a number of cards from a player's discard pile into their deck.

- d. If a condition refers to "all" items in a set and that set contains zero items, the condition is automatically satisfied as soon as it could be satisfied for 1 or more of that item.

  - Example: The Runner plays Forked, initiating a run on a server. The first piece of ice that the Runner encounters is Troll. If Troll's "when encountered" ability does not end the run, the Runner is automatically considered to have broken all of the zero subroutines on Troll as soon as step 6.9.3b of the encounter begins, and Troll is trashed by Forked.

- e. Some values are defined with a variable "X". If the ability defining X is inactive or if the value of X cannot be evaluated, treat X as equal to 0.

  - Example: The Corp uses the ability granted by ZATO City Grid to trash Surveyor and resolve one of its subroutines. Since Surveyor is in Archives when the trace on the subroutine initiates, the ability defining X is inactive, so the base trace strength is 0.
  - Example: Hush is hosted on Surveyor. The ability defining X is lost, so Surveyor's strength is 0.


**9.12.3.** Must


- a. If a card ability states that a player "must" do something without stipulating how that player must do so, then that player is forced to make any decisions necessary to satisfy the requirement, even if it requires the use of other card abilities.

  - Example: The Runner has an Imp and a Scrubber installed and accesses Mumbad Virtual Tour. If the Runner only has 4{c}, they must still choose to either spend a counter from Imp to trash Mumbad Virtual Tour if they are able to do so or spend recurring credits on Scrubber to help pay the trash cost of the Mumbad Virtual Tour.

- b. If a card ability states that a player "must" do something and specifies the means by which that player must do so, then that player is not required to carry out the effect except by the means specified, even if it would be possible through the use of other card abilities.

  - Example: The Runner has Imp and Neutralize All Threats installed when they access a card with a trash cost greater than the number of credits they have available to spend. Because they are unable to trash the card by paying its trash cost, they are not required to spend a counter from Imp in order to trash it.

- c. If the "must" ability presents a player with a choice between two or more effects, that player must choose an effect that can be fully resolved. If none of the choices can be fully resolved, then the "must" ability does nothing.

  - Example: If the first subroutine on Fairchild 2.0 resolves while the Runner has no installed cards but 3{c}, then the Runner must choose to pay 2{c}. When the second subroutine on Fairchild 2.0 resolves, the Runner can neither pay 2{c} nor trash an installed card, so nothing happens.

- d. Making a choice between effects is always a separate instruction from resolving the chosen effect (see rule 9.11.4g). The previous rule only requires the player to choose an effect that is possible to resolve; it does not require the chosen effect to actually resolve as expected. Once an effect has been chosen, the player can apply interrupts to modify, prevent, or replace that effect as normal.

  - Example: If the Runner encounters Data Raven, they must choose to either take a tag or end the run. The Runner can choose to take the tag, then avoid the tag with Decoy. This does not cause the run to end, as the decision to take the tag has already been made.

- e. A singular "must" ability cannot force a player to pay an additional cost they wish to decline, but a player cannot avoid choosing a fully resolvable effect from among multiple options in a "must" ability by declining to pay the additional cost for one of those choices.

  - Example: If Service Outage is active and the Runner has Always Be Running installed, the Runner can decline to pay the additional 1{c} to make a run with their first {click}, thus satisfying the "must" of Always Be Running and allowing them to spend their first click on a different action. However, if a Runner has a False Echo installed and passes an unrezzed Archer, the Corporation cannot choose to rez the Archer but decline to forfeit an agenda; the Corp must either pay 4{c} and forfeit an agenda to rez the Archer or add the Archer to HQ.


**9.12.4.** Repeat this process


- a. If an ability instructs a player to "repeat this process" without specifying how many times to do so, then that player resolves the ability again, including the instruction to "repeat this process" if it is still applicable.

- b. If an ability instructs a player to "repeat this process" a specified number of times, then that player resolves the entire ability, except for the repetition instruction, that many times in a row. The number of repetitions is determined after the first full resolution but before the first repetition, and once the number of repetitions is determined it cannot be changed.

- c. Regardless of the number of times an ability is repeated, a checkpoint occurs after each full resolution of the ability's instructions, before another repetition begins.

- d. Each repetition of the ability is resolved independently of the others. If the ability provides the player with any choices, they may choose differently each time.


**9.12.5.** Persistent


- a. A persistent ability is an ability on a Corp card marked with the "persistent" ability flag before its other text. When the Runner trashes a rezzed card they are accessing, its persistent abilities persist through a lingering effect that is created as the source card is trashed. See also section 9.10.

- b. A persistent ability begins to persist simultaneously with the trashing of its source card. The ability never becomes inactive, nor is it considered a new ability.

- c. An ability only persists until just after the end of the run during which it began to persist. When the reaction window immediately following step 6.9.6d of that run closes, the lingering effect allowing the ability to persist expires, and the ability ceases to persist.

- d. A persisting ability is only applicable to the run during which it began to persist. After the checkpoint following step 6.9.6d of the run, no more instances of the ability can be created. Even if another run initiates during the reaction window following that checkpoint, new instances of any abilities persisting from the first run cannot become pending during the second run, even if their trigger conditions would normally be met during that second run.

  - Example: The Runner trashes AMAZE Amusements, causing its "Whenever a run on this server ends" ability to persist. When the run ends, the Runner uses Doppelgänger to make another run while the persisting ability is still pending. The ability remains active so that its existing pending instance can resolve, but another instance cannot be created even if the Runner steals an agenda during the second run.


**9.12.6.** Modal Abilities


- a. Some abilities contain text organized into bulleted lists. These are called modal abilities, and each list item is called a mode.

- b. Each modal ability contains an instruction directing its controller to resolve 1 or more of its modes. The effect of this instruction is for the relevant player to choose the next mode to resolve. The first instruction of the chosen mode will be the next instruction to become imminent. The player does not need to choose modes in the order they are written. If the description of the number of modes to resolve contains "up to", the player may also choose not to resolve any more modes.

- c. After the instructions of a mode are complete, the game returns to the instruction to resolve 1 or more modes. If the maximum allowed number of modes have not yet been resolved, the player will choose another mode to resolve, as if resolving that initial instruction again. If the maximum number of allowed modes have been resolved, the game proceeds to the next instruction or ability following the list of modes.

- d. In a single instance of resolving a modal ability, each mode can be resolved no more than 1 time, unless the ability specifically states that its modes can be chosen more than once.
