# 6. Runs

_Netrunner Comprehensive Rules v26.03 · [source](https://rules.nullsignal.games/#chpt_runs) · retrieved 2026-09-01_


## 6.1. General

**6.1.1.** A run is a Runner's attack on a server. There are 6 phases used in the procedure of carrying out a run, described in sections 6.3 through 6.8. A run always begins with the Initiation Phase. Section 6.9 contains the full set of steps carried out during each phase, and should be referred to in conjunction with the sections discussing the phases.

**6.1.2.** The attacked server is the server the Runner is attempting to reach, usually to breach that server.


- a. The Runner announces the attacked server in the initiation phase of the run.

- b. Card abilities can sometimes change the attacked server during the run. This does not end the run, but it can affect whether or not certain card abilities or effects apply.

- c. If an ability changes the Runner's position, the attacked server and the current timing step of the run could also change. See section 6.2.8.

- d. A few abilities change the attacked server directly, without referring to the Runner's position. These abilities do not change the current timing step of the run.

  - Example: Sneakdoor Beta's ability changes the attacked server from Archives to HQ. The Runner does not need to encounter ice protecting HQ after resolving this ability.


**6.1.3.** Many abilities modify the sequence of steps taken to resolve a run. Such abilities can direct players to skip steps or phases, replace steps with other effects, move directly from the current timing point to a new phase, or make other changes to the procedures explained in this section. Heed the Golden Rules in section 1.2.


- a. Some abilities direct the Runner to approach or encounter a piece of ice as part of moving them to that ice's position. See section 6.2.8.

- b. One card, Code Replicator, directs the Runner to approach the piece of ice in their position without first moving them to a new position. This effect changes the current timing step of the run to step 6.9.2, the Approach Ice Phase.

- c. An ability that directs the Runner to encounter a piece of ice without first moving the Runner's position does not change the current timing point of the run, the attacked server, or the Runner's position. The effect of such an ability is to resolve an Encounter Ice Phase independent of the current state of the run. See section 6.5.9.

- d. Any time the current timing step of the run is moved to a different phase of the run, except for an instruction to end the run (See section 6.1.4, below), no more steps in the previously-active phase are carried out. If a paid ability window is open, players complete that window normally. Then the active phase is complete and a checkpoint occurs. Finally, the game proceeds to the new phase.

- e. If a condition refers to encountering a piece of ice "after" an approach or passing a piece of ice "after" an encounter, then that condition is only met by the indicated phases or steps occurring in direct sequence according to the standard progression of the run.

  - Example: The Runner uses Inversificator to break the subroutine on a Mirāju that is protecting Archives. When the encounter ends, Mirāju's ability creates a replacement effect so that the Runner moves to the outermost position of Archives instead of passing Mirāju. Because the ice is not passed, Inversificator's ability does not meet its trigger condition. Later, the Runner encounters the same Mirāju again and does not break its subroutine. When this encounter ends, the Runner passes Mirāju. Even though the Runner has used Inversificator to fully break Mirāju during an encounter earlier in the run, Inversificator's trigger condition is not met when the Runner passes Mirāju because the Runner is not passing it after the relevant encounter.

- f. If a condition refers to passing a piece of ice "after" fully breaking it, then that condition is only met when the Runner passes that piece of ice after an encounter (as defined by rule 6.1.3e), and only if the Runner fully broke that ice during that encounter. See section 6.5.7.


**6.1.4.** To end the run is to halt the Runner's progress toward the server. When an effect ends the run, the current phase of the run ends without following any of its remaining steps, and before completing open priority windows. The game immediately moves from the current timing point to step 6.9.6, the Run Ends Phase.


- a. If a run is in progress, an instruction to end the run also ends any breach and/or access taking place.

- b. If an instruction to end the run resolves while there is no run in progress, but there is an encounter in progress, that encounter ends without resolving any more of its steps. Process any open priority windows in the same way as described in section 6.8.2, but do not resolve any other steps of the Run Ends Phase. Any breach and/or access initiated during the encounter also ends, but a breach and/or access that was already in progress when the encounter began is not ended.

  - Example: The Runner uses Detente to access a card in HQ while there is no run in progress. They access Ganked! and are forced to encounter Loot Box. The Runner chooses to neither break any subroutines nor spend credits for Loot Box's first subroutine, so that subroutine tries to end the run. There is no run to end, but this effect still ends the encounter. The second subroutine on Loot Box will not resolve.

- c. If an instruction to end the run resolves while there is neither a run nor an encounter in progress, that effect does nothing.

  - Example: The Corp plays Unleash and resolves the last subroutine on Lycian Multi-Munition. The Corp will gain 1{c}, but there is no further effect.


**6.1.5.** Jacking out is the process by which a Runner voluntarily ends a run. Jacking out follows the usual process for ending the run as described in section 6.1.4, but some card abilities function differently depending on whether or not the Runner chose to end the run by jacking out.


- a. The Runner has the opportunity to jack out after passing a piece of ice.

- b. If there is no ice protecting the server, the Runner has the opportunity to jack out before approaching the server.



## 6.2. Position

**6.2.1.** Each server has a sequence of positions that assign an order to ice protecting that server. A piece of ice protecting a server always occupies exactly 1 position in that server's sequence at a time.


- a. A piece of ice that is hosted on another card is not protecting a server and does not occupy a position, regardless of whether the ice is installed.


**6.2.2.** When a piece of ice is installed protecting a server or an installed piece of ice is moved, a new position is created for that ice. For ice being installed, the position is created when the Corp declares an install destination for the ice (step 8.5.16b), and the ice occupies that position once the ice becomes installed (step 8.5.16e). For installed ice being moved, the position is created when the movement happens, and the ice occupies that position immediately.


- a. If a piece of ice is being installed in or moved to the outermost position protecting a server, the new position is outward from the outermost already-existing position. If an instruction to install a piece of ice does not explicitly specify otherwise, the ice is installed in the outermost position.

- b. If a piece of ice is being installed in or moved to the innermost position protecting a server, the new position is inward from the innermost already-existing position.

- c. If a piece of ice is being installed "directly inward" from another piece of ice, the new position is inward from that ice's position, and outward from the next already-existing position inward, if there is one.

- d. If a piece of ice is being installed "in any position", the new position can be created outward from all existing positions, inward from all existing positions, or between any two existing consecutive positions, as the Corp chooses.

- e. One card, Mutate, has an ability that installs a piece of ice in the "same position" as a particular piece of ice that was previously trashed. This is a different usage of "same position" from that described in rule 6.2.3. For this installation, the ice is installed protecting the same server the trashed ice was protecting, and the new position is created at the same place in that server's sequence of positions as the position the trashed ice had.

- f. If a piece of ice is swapped with a piece of ice protecting a server, no new position is created. The first ice will occupy the position previously held by the second ice (and vice-versa, if the first piece of ice was also protecting a server).


**6.2.3.** Some abilities refer to ice in the "same position" protecting different servers. Two pieces of ice occupy the "same position" if their respective servers have the same number of positions inward from their respective positions.

  - Example: A server has 2 pieces of ice protecting it, and a copy of Rook is hosted on the outermost of those 2 pieces of ice. Rook's ability can move it to the 2nd piece of ice protecting any server, counted starting from the innermost piece of ice and moving outward. The Rook cannot be moved to a server protected by only 1 piece of ice.
  - Example: The Runner passes a piece of ice in the innermost position protecting a server, and uses Slipstream to move to another server. Since positions are compared working from the innermost position, the Runner will be able to approach the innermost ice protecting any server, regardless of how many other pieces of ice are outward from that ice.

**6.2.4.** If a piece of ice leaves its position, except by being swapped with another piece of ice, its former position ceases to exist. This happens during step 10.3.1i of the next checkpoint, unless the Runner is in that position, in which case the position continues to exist until the Runner moves to another position or ceases to have a position.

**6.2.5.** During a run, the Runner occupies the attacked server's positions.


- a. The Runner enters the outermost position protecting the attacked server at step 6.9.1d of the Initiation Phase.

- b. The Runner progresses through the positions of the attacked server in order. While the Runner occupies a position, they approach, encounter (if the ice is rezzed), and pass the ice in that position, then they proceed to the next position moving inward, until they complete this process for the innermost position or the run is ended.

- c. Once the Runner leaves the innermost position protecting a server, they no longer have a position. This occurs at step 6.9.4d of the Movement Phase. However, abilities can still move the Runner to a position later in that phase.

- d. During the Success Phase and the Run Ends Phase, the Runner has no position and can no longer move to a position.

  - Example: The Corp uses Ganked! to force the Runner to encounter Cell Portal in the middle of breaching a server. Since the run is already successful, resolving Cell Portal's subroutine will have no meaningful effect except to derez Cell Portal.

- e. The Runner does not have a position while they are not engaged in a run, nor do they maintain their position from one run to the next.


**6.2.6.** The Runner's current position is a specific element of the sequence of positions, not an index into that sequence. Adding or removing other positions protecting the attacked server does not affect the Runner's position.


- a. If a position outward from the Runner's current position is added to the attacked server, the Runner will normally not need to approach or encounter the new piece of ice to reach the server.

  - Example: The Runner runs a server protected by 2 pieces of ice. The Runner approaches the outermost ice, and the Corp rezzes Drafter. During the subsequent encounter, the Corp resolves Drafter's second subroutine, installing a new piece of ice protecting the attacked server and trashing both of the other pieces of ice in the process. The new ice is installed to the outermost position protecting the server, which is outward from the Runner's current position, so the Runner will not approach the new piece of ice. This is true even though the number of positions between the new ice and the server is less than the number of positions that were previously between the Runner and the server.

- b. If a position inward from the Runner's current position is added to or removed from the attacked server, the ice the Runner will need to approach and/or encounter changes accordingly.

  - Example: The Runner encounters Brân 1.0 and breaks its last two subroutines. Brân 1.0's first subroutine resolves and the Corp installs a new piece of ice in the next inward position protecting the server. The Runner will have to approach that new piece of ice later in this run because its position is inward from their current position.


**6.2.7.** Changes to the piece of ice in the Runner's current position can affect the progression of the run, but they do so differently during an encounter than outside of an encounter.


- a. If a piece of ice is uninstalled or moved to another position (protecting this or another server) while it is being approached, the approach ends and the run continues to the Movement Phase.

- b. If a piece of ice currently being approached is swapped with another piece of ice, the Runner remains in their current position and the run remains in the Approach Ice Phase. The Runner is now approaching the new piece of ice occupying that position. Trigger conditions related to a piece of ice being approached are not met for the new ice.

- c. If a piece of ice is uninstalled or derezzed while it is being encountered, the Encounter Phase ends and the run continues to the Movement Phase.

- d. If a piece of ice currently being encountered moves to another position or is swapped with installed ice in another position, the Runner remains with the encountered ice and continues the run from that ice's new position.

  - Example: The Runner encounters Bullfrog as the innermost piece of ice protecting a remote server. Bullfrog's subroutine resolves and it is moved to be the outermost piece of ice protecting Archives. The Runner is now running on Archives and continues the run from Bullfrog's new position.

- e. If the piece of ice in the Runner's position is uninstalled, derezzed, moved, or swapped during the Initiation Phase or the Movement Phase, the Runner is not moved and the current timing step of the run does not change.

  - Example: A run begins, and before the Runner approaches the outermost piece of ice protecting the attacked server, the Corp uses Project Yagi-Uda to swap that ice with another piece of ice. The Runner will approach the new ice.
  - Example: The Runner passes a piece of ice, and in step 6.9.4b, the Corp uses Project Yagi-Uda to swap that ice with another piece of ice. The Runner has still passed their position (but has not passed the new ice occupying that position) and will proceed to the next position in step 6.9.4d. They will not approach or encounter the new ice.


**6.2.8.** During a run, abilities can move the Runner directly to a position.


- a. If an ability instructs the Runner to move to a piece of ice in a different position, the Runner's position becomes the position corresponding to that ice, and the server that ice protects becomes the attacked server. The current timing step becomes step 6.9.2, the Approach Ice Phase, or step 6.9.3, the Encounter Ice Phase, according to whether the Runner was directed to approach or encounter the ice.

- b. If an ability instructs the Runner to move to "the outermost position" of a server, that server becomes the attacked server. If there is any ice protecting that server, the Runner's position becomes the position of the outermost such piece of ice, and the current timing step becomes step 6.9.2, the Approach Ice Phase. If there is no ice, the Runner ceases to have a position, and the current timing step becomes step 6.9.4, the Movement Phase.

- c. If an ability instructs the Runner to move to a position during the Success Phase, during the Run Ends Phase, or while there is no run in progress, instead the Runner does nothing. See rule 6.2.5d.

- d. If an ability instructs the Runner to move to and approach a position they are already approaching, instead the Runner does nothing.



## 6.3. Initiation

**6.3.1.** The initiation phase sets the parameters for the run and begins the run.

**6.3.2.** At the start of the Initiation Phase, the Runner announces a server to be the attacked server.


- a. Some abilities state that the Runner "cannot run on" or "cannot initiate a run on" certain servers under certain conditions. These effects refer to the announcement of the attacked server in the Initiation Phase.

  - Example: Off the Grid states in part, "The Runner cannot initiate a run on this server." This only prohibits the Runner from announcing this particular remote server as the attacked server at the beginning of the run. If an ability changes the attacked server while a run is in progress, Off the Grid does not interfere with that ability.

- b. Some abilities impose an additional cost to run, either generally or on a particular server. Any costs to run a server must be paid at the time that server is announced as the attacked server.


**6.3.3.** After the attacked server is announced, credits are added to the Runner's bad publicity fund. See section 10.6 for an explanation of bad publicity.

**6.3.4.** Some abilities look for particular conditions occurring "during a run" or are restricted to being used "during a run." For purposes of abilities, the run formally begins only after the attacked server is announced and any costs are paid.

  - Example: The Runner initiates a run against a server with a rezzed Heinlein Grid while the Corp has Enhanced Login Protocol in play. They do not lose any credits to Heinlein Grid's ability, as the additional click Enhanced Login Protocol requires them to spend is spent to initiate the run, and is not spent during the run.

**6.3.5.** When the Initiation Phase completes, the run moves either to the Approach Ice Phase (section 6.4) or the Movement Phase (section 6.6). If there is ice protecting the attacked server, the Runner's position becomes the outermost piece of ice, and the run proceeds to the Approach Ice Phase. If there is no ice protecting the server, then the run moves directly to the Movement Phase.



## 6.4. Approach Ice

**6.4.1.** The approach ice phase gives the Corp the opportunity to rez the ice in the Runner's current position.

**6.4.2.** For purposes of meeting trigger conditions, the Runner approaching a piece of ice is equivalent to the Approach Ice Phase beginning at that ice's position. Abilities with this trigger condition are subject to rule 9.2.8f.

**6.4.3.** The paid ability window in step 6.9.2b of the Approach Ice Phase is the only time the Corp can normally rez ice. In this window, the Corp only gains the ability to rez the piece of ice being approached, not any other ice.

**6.4.4.** When the Approach Ice Phase completes, the run moves either to the Encounter Ice Phase (section 6.5) or the Movement Phase (section 6.6). If the approached piece of ice is rezzed, the Runner encounters it next. If the approached piece of ice is unrezzed or if there is no longer ice installed in that position, then the run moves directly to the Movement Phase.



## 6.5. Encounter Ice

**6.5.1.** The encounter ice phase gives the Runner the opportunity to interact with the ice in their current position, then requires the Corp to resolve unbroken subroutines on that ice.

**6.5.2.** For purposes of meeting trigger conditions, the Runner encountering a piece of ice is equivalent to the Encounter Ice Phase beginning at that ice's position. Abilities with this trigger condition are subject to rule 9.2.8f.

**6.5.3.** As each encounter begins, each subroutine on the encountered ice gains an "unbroken" status associated with that encounter. See section 9.8 for details.

**6.5.4.** During the paid ability window in the Encounter Ice Phase (step 6.9.3b), the Runner can break subroutines on the ice. See rule 9.8.5.

**6.5.5.** After the paid ability window in the Encounter Ice Phase closes, unbroken subroutines on the encountered ice resolve. See section 9.8.7.

**6.5.6.** At the end of the Encounter Ice Phase, the run continues to the Movement Phase, section 6.6.

**6.5.7.** Fully Break


- a. During each encounter, the Runner fully breaks the encountered ice the first time all subroutines on that ice are broken.

- b. Whenever the Runner fully breaks a piece of ice, if all its subroutines were broken using abilities on a single object, that object also fully breaks the ice.

- c. If an encountered piece of ice has no subroutines, the Runner fully breaks it when step 6.9.3b of the encounter begins. No objects fully break the ice in this case.

- d. If the encountered piece of ice gains subroutines after the Runner fully breaks it, the Runner's status of having fully broken the ice remains in place. Conditions that check whether the Runner fully broke the ice will still be met as normal. Breaking the new subroutines will not fully break the ice or meet related conditions again.


**6.5.8.** Bypass


- a. When an effect allows the Runner to bypass a piece of ice, the Encounter Ice Phase is aborted and the Runner immediately proceeds to pass that ice.

- b. Most bypass effects occur at the beginning of an encounter and result in steps 6.9.3b and 6.9.3c never occurring, meaning that subroutines on the encountered ice are neither broken nor resolved.

- c. A few bypass effects can be triggered later. If ice is bypassed in step 6.9.3b, subroutines may or may not have been broken, but step 6.9.3c never occurs so no subroutines resolve. Note that for ice with zero subroutines, the Runner has "broken all subroutines" if and only if step 6.9.3b was reached. See rule 9.12.2d.

  - Example: The Runner plays Forked and encounters a Troll that they can bypass with Femme Fatale. The Runner pays 0{c} to bypass the Troll with Femme Fatale. Forked does not meet its trigger condition, and Troll is not trashed, because the opportunity to break subroutines was never reached.


**6.5.9.** Forced Encounters


- a. If an ability directs the Runner to encounter a piece of ice without first changing their position, resolve an Encounter Ice Phase but do not change the Runner's position, if any. After the encounter, return to the effect that caused the encounter and proceed from there. This type of encounter outside the normal progression of a run is referred to as a forced encounter.

  - Example: If Shiro causes Chrysalis to be accessed, resolve the encounter with Chrysalis and then return to resolving subroutines on Shiro.

- b. During a forced encounter, an instruction to end the run is applied to both the Encounter Ice Phase being resolved and the phase from which the additional encounter was initiated. See also rule 6.1.4b.

  - Example: The Corp uses The Twins to force the Runner to encounter a piece of ice again when they pass it. If a subroutine directs the Corp to end the run during the extra encounter, both that encounter and the original Movement Phase are aborted, and the game proceeds to the Run Ends phase.

- c. An instruction that creates a forced encounter is not finished resolving until the encounter is complete.

- d. During a forced encounter, if an ability moves the Runner or directs the Runner to approach a piece of ice, that movement resolves and changes the parameters of the underlying run normally, but does not end or interfere with the forced encounter.



## 6.6. Movement

**6.6.1.** The movement phase handles the progression of the run between pieces of ice and after the innermost piece of ice, up to the point when the Runner approaches the attacked server.

**6.6.2.** If the Movement Phase was reached from the Approach Ice Phase or the Encounter Ice Phase, the Runner passes the ice in their position.

**6.6.3.** During each Movement Phase, the Runner has the opportunity to jack out. See section 6.1.5.

**6.6.4.** If the Runner declines to jack out, their position moves to the next inward position if there is one.

**6.6.5.** If the Runner moves to a new position, the run continues from the Movement Phase to the Approach Ice Phase, section 6.4. The Runner will approach the piece of ice in their new position.

**6.6.6.** If the Runner does not jack out or return to the Approach Ice Phase, they approach the server. This has no inherent effects, but is referred to by some card abilities. After the Runner approaches the server, the run continues to the Success Phase, section 6.7.



## 6.7. Success

**6.7.1.** The success phase declares the run successful and allows the Runner to breach the attacked server.

**6.7.2.** A run during which the runner breaks through the Corp's defenses and reaches the server itself is declared a successful run. This label is frequently referred to by card abilities.

**6.7.3.** Declaring a run to be successful is not the same as the Success Phase beginning. Accordingly, abilities that have trigger conditions related to a successful run are not subject to rule 9.2.8f.

**6.7.4.** Many abilities that initiate a run contain a conditional ability with the trigger condition "If successful". This means, "After the run created this way becomes successful".


- a. If an effect that initiates a run specifies that that run must be made against a specific server or servers, any "If successful" abilities associated with that effect are tied to that server or servers. If the attacked server has changed since the run was initiated, and no longer is a server that could have been chosen for this run, the "If successful" abilities do not meet their trigger conditions.

  - Example: The Runner plays Because I Can, which directs them to make a run on a remote server. If an effect moves the run to a central server, the "If successful" effect on Because I Can will not apply. However, if the run is moved instead to another remote server, the "If successful" effect is still applied, since Because I Can would have allowed a run on the second remote server to begin with.

- b. "If successful" abilities frequently create lingering effects (see section 9.10) that modify the procedure for accessing cards or replace breaching the attacked server with another effect entirely. These effects apply to the breach permitted by the rules for this run (see rule 6.7.5), and they expire when the run is complete.

- c. If an "If successful" effect gives the Runner an effect they can optionally carry out instead of breaching, that decision is made at the time the breach would begin, in step 6.9.5b.

  - Example: The Runner plays Account Siphon while Ash 2X3ZB9CY is installed in the root of HQ, and the run is successful. The Runner can wait until after the trace from Ash 2X3ZB9CY completes before deciding to access cards normally or force the Corp to lose credits.


**6.7.5.** When the Runner breaches the server, they follow the procedures detailed in section 7.



## 6.8. Run Ends Phase

**6.8.1.** The run ends phase formalizes the end of the run and prepares the game to return to the timing structure from which the run was initiated.

**6.8.2.** At the beginning of the Run Ends Phase, the game processes each priority window that was open when the run was ended. This is done according to the following procedure:


- a. If a paid ability window was open when the run was ended, the window closes. No player can trigger further paid abilities or resolve other options the window would normally allow (such as rezzing cards).

  - Example: The Corp spends a counter from a scored Nisei MK II to end the run. Once this happens, the Runner does not have an opportunity to use their bad publicity credits from this run to pay for using Self-modifying Code.

- b. If the run was ended during a reaction window opened due to a phase beginning, the reaction window closes, as per rule 9.2.8f. No other pending abilities are resolved.

  - Example: The Runner uses the ability on Security Nexus during a reaction window at the beginning of the Encounter Ice Phase. The trace is successful, so the ability ends the run. If Tollbooth's ability is also pending in this reaction window, it will not be resolved, since the encounter has ended.

- c. Any other open priority window is completed normally, except that new timing structures (including a breach that was delayed according to rule 7.3.8) cannot be initiated. If more than one priority window needs to be completed this way, they are resolved in the usual order described in section 9.1.2.

  - Example: The run is ended during the reaction window following step 6.9.4g, while Formicary's ability is still pending. The Corp can rez Formicary and move it to the approached server, but the Runner's position cannot be moved and an encounter cannot be initiated.


**6.8.3.** During the Run Ends Phase, if there are any credits remaining in the Runner's bad publicity fund, those credits are returned to the bank.

**6.8.4.** As part of the Run Ends Phase, the game determines whether the run should be declared an unsuccessful run. This is a label applied to runs in which the Runner failed to reach the server they attacked, and is referred to by card abilities. The run is declared unsuccessful if neither of the following conditions have been met:


- a. If the run reached step 6.9.5, the Success Phase, it is not declared unsuccessful. This is the case regardless of whether the run was actually declared successful.

  - Example: The Runner reaches a server with Crisium Grid rezzed. Crisium Grid's ability stops the run from being declared successful, but the run is not declared unsuccessful either.

- b. If the attacked server has ceased to exist, the run is not declared unsuccessful. See rule 8.5.8.


**6.8.5.** Conditions related to a run ending are not met as soon as an "end the run" effect resolves or at the beginning of the Run Ends Phase. Instead, these conditions are met at step 6.9.6d, when the entire run timing structure is complete. Similarly, static abilities that apply during a run and durations that apply until the end of a run expire at step 6.9.6d.

  - Example: The Runner plays The Noble Path to make a run in which they prevent all damage. During the run, the subroutine on Chum resolves, and then the Runner encounters Wall of Static and does not break its subroutine. When the "end the run" ability resolves, the encounter also ends, so the trigger condition on Chum's delayed conditional ability is met. The ability will resolve and fail to damage the Runner during the Run Ends Phase, because The Noble Path's effect still applies. Similarly, Georgia Emelyov's first ability meets its trigger condition at step 6.9.6c, which is still before the run is over, so the damage will be prevented.
  - Example: The Runner plays The Noble Path to make a run in which they prevent all damage. The run is successful but the Runner takes a tag during the run. When the run ends, Dedicated Response Team's ability meets its trigger condition at the same time that The Noble Path's effect expires, so the Runner will suffer 2 meat damage.



## 6.9. Steps of a Run

**6.9.1.** Initiation Phase


- a. The Runner announces the attacked server.

- b. The Runner adds 1{c} from the bank to their bad publicity fund for each bad publicity the Corp has.

- c. The run formally begins. Conditions related to the run beginning or initiating are met.

- d. The Runner's position is set corresponding to the outermost ice protecting the attacked server, if any.

- e. A paid ability window occurs, in which players may use paid abilities and the Corp may rez non-ice cards. (P) (R)

- f. The Initiation Phase is complete. If the Runner has a position corresponding to a piece of ice, proceed to step 6.9.2, Approach Ice Phase, approaching the piece of ice corresponding to their position. If the Runner has no position or if there is no ice in their position, proceed to step 6.9.4, Movement Phase.


**6.9.2.** Approach Ice Phase


- a. The approach begins. Conditions related to the Runner approaching this ice are met.

- b. A paid ability window occurs, in which players may use paid abilities and the Corp may rez non-ice cards. Additionally, the Corp can rez the approached piece of ice. (P) (R)

- c. The Approach Ice Phase is complete. If the approached piece of ice is rezzed, continue to step 6.9.3, Encounter Ice Phase. Otherwise, proceed to step 6.9.4, Movement Phase.


**6.9.3.** Encounter Ice Phase


- a. The encounter begins. Conditions related to the Runner encountering this ice are met.

- b. A paid ability window occurs, in which players may only use paid abilities. Subroutines on the encountered ice can be broken during this window. (P)

- c. If there are unbroken subroutines that have not yet resolved this encounter, the Corp resolves the next such subroutine in order. Otherwise, skip to (e).

- d. Return to (c).

- e. The Encounter Ice Phase is complete. Continue to step 6.9.4, Movement Phase.


**6.9.4.** Movement Phase


- a. If the Runner's position corresponds to a piece of ice, the Runner passes that ice. Conditions related to passing ice are met. If there are no positions more inward than the ice just passed, conditions related to passing all of the ice protecting a server are also met.

- b. A paid ability window occurs, in which players may only use paid abilities. (P)

- c. The Runner may choose to jack out, proceeding to step 6.9.6, Run Ends Phase. If the Runner does not, continue to (d).

- d. The Runner's position moves to the next position inward, if any.

- e. A paid ability window occurs, in which players may use paid abilities and the Corp may rez non-ice cards. (P) (R)

- f. If the Runner moved to a new position, return to step 6.9.2, Approach Ice Phase, approaching the piece of ice in their new position. If not, continue to (g).

- g. The Runner approaches the attacked server. Conditions related to the Runner approaching a server are met.

- h. The Movement Phase is complete. Continue to step 6.9.5, Success Phase.


**6.9.5.** Success Phase


- a. The run is declared successful. Conditions related to a successful run are met.

- b. The Runner breaches the attacked server.

- c. The Success Phase is complete. Continue to step 6.9.6, Run Ends Phase.


**6.9.6.** Run Ends Phase


- a. Any priority windows that were open when the run moved to this phase are completed or closed as described in section 6.8.2.

- b. The Runner returns any remaining credits in the bad publicity fund to the bank.

- c. If the Success Phase was not reached during this run and the attacked server still exists, the run becomes unsuccessful.

- d. The Run Ends Phase and the run as a whole are both complete.
