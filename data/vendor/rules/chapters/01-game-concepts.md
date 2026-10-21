# 1. Game Concepts

_Netrunner Comprehensive Rules v26.03 · [source](https://rules.nullsignal.games/#chpt_game_concepts) · retrieved 2026-09-01_


## 1.1. General


These rules are compatible with cards from the game android: netrunner by Fantasy Flight Games. android: netrunner is a game about the cyber-struggle between massive Corporations and subversive hackers known as Runners.

**1.1.1.** The game is played between two players. One player takes the role of the Corp (Corporation) and the other takes the role of the Runner. This rules document will frequently refer to a player interchangeably with their game role.

**1.1.2.** Each player needs a legal deck, an identity card for their role, and any extra cards used from outside their deck. They also need a supply of tokens as described in section 1.9. The constraints that define the legality of a deck are defined in section 1.4, and the cases where cards outside the deck and identity can be used are defined in section 1.5.

**1.1.3.** All numbers used in the game are integers. Unless otherwise stated, a given value can be positive, negative, or zero.



## 1.2. Golden Rules

**1.2.1.** If the text of a card directly contradicts these rules, the text of the card takes precedence.

**1.2.2.** If a rule or ability directs something to happen, but another effect states that it cannot happen, the "cannot" ability takes precedence.

**1.2.3.** If an instruction includes the words "if able," it can only be carried out fully or not at all. If any part of the instruction is not possible to carry out, the entire instruction is ignored.

**1.2.4.** If an instruction does not include the words "if able," as much of that instruction as possible is carried out. Any parts of the instruction that are not possible to carry out are ignored.



## 1.3. Symbols

**1.3.1.** Several non-English symbols appear on cards and in this rules document. This section serves as a basic guide to those symbols.

**1.3.2.** The symbol {c} (plaintext: {c}) stands for "credit". It always appears with a quantity, such as 1{c}, which means "one credit," or 3{c}, which means "three credits." See section 1.10 for rules about credits.

**1.3.3.** The symbol {click} (plaintext: {click}) stands for "click". Multiple clicks can be represented either by multiple symbols, such as {click}{click}, or by a numeral and symbol, such as 2{click}, both meaning "two clicks." See section 1.11 for rules about clicks.

**1.3.4.** The symbol {recurring} (plaintext: {recurring}) stands for recurring credit. It always appears with a quantity, such as 1{recurring}, which means "one recurring credit," or 3{recurring}, which means "three recurring credits." See section 1.10.5 for rules about recurring credits.

**1.3.5.** The symbol {link} (plaintext: {link}) stands for "link". It is always used with a quantity, such as 1{link}, which means "1 link." See section 10.7 for rules about link.

**1.3.6.** The symbol {MU} (plaintext: {MU}) stands for "memory" and also represents the unit used to measure memory. When used with a quantity, it should be read as "memory units". For example, 2{MU} means "2 memory units." See section 1.20 for rules about memory.

**1.3.7.** The symbol {sub} (plaintext: {sub}) stands for "subroutine". Each symbol marks a single subroutine on a piece of ice. See section 9.8 for information about subroutines.

**1.3.8.** The symbol {trash} (plaintext: {trash}) stands for "trash this card". It is used as a self-referential cost in card text, such as "{trash}: Draw 2 cards," which means "Trash this card to draw 2 cards." See section 1.16 for rules about costs, and section 1.19 for rules about trashing cards.

**1.3.9.** The symbol {interrupt} (plaintext: {interrupt}) stands for "interrupt timing". It is used to designate abilities that resolve immediately before other abilities. See section 9.9 for rules about interrupt abilities.



## 1.4. Deck Construction

**1.4.1.** Each player's deck is associated with a single identity card that determines the faction, minimum deck size, and influence limit of that deck. The identity card may also stipulate other variances from the standard deckbuilding rules.


- a. The identities The Catalyst and The Syndicate from the System Gateway Starter Pack are intended for use only with the decks included in that pack. They are not legal for play under the full deck construction rules.


**1.4.2.** Each deck must meet all requirements in this section to be legal for play.

**1.4.3.** The deck must contain at least as many cards as the minimum deck size indicated on the corresponding identity.


- a. Identity cards, extra cards that begin the game outside the deck, and player aid cards are not part of the deck and are not counted towards the size of the deck.

- b. There is no maximum deck size.


**1.4.4.** Decks cannot contain identity cards, player aid cards, cards from the wrong side (Corp cards in a Runner deck or vice-versa), or out-of-faction cards that lack influence costs.

**1.4.5.** Neutral cards and cards that belong to a faction other than the corresponding identity's faction are all considered out-of-faction. The total influence cost of out-of-faction cards in the deck must not exceed the influence limit of that identity.


- a. The total influence cost of out-of-faction cards is counted by copy and not by name.

  - Example: Including a single copy of Diesel in a non-Shaper deck adds 2 to the total influence in that deck, while including two copies of Diesel in a non-Shaper deck adds 4 to the total influence in that deck.


**1.4.6.** A Corp deck must contain agendas totalling a certain number of agenda points, as determined by the total number of cards in the deck.


- a. A deck with 40 to 44 cards must contain 18 or 19 agenda points.

- b. A deck with 45 to 49 cards must contain 20 or 21 agenda points.

- c. A deck with 50 to 54 cards must contain 22 or 23 agenda points.

- d. A deck with more than 54 cards must contain 22 or 23 agenda points, plus an additional 2 agenda points for every full 5 cards in the deck over 50.

  - Example: A 66 card deck requires 6 additional agenda points, since it includes 3 sets of 5 cards beyond 50. This gives a final requirement of either 28 or 29 agenda points.


**1.4.7.** The deck cannot contain more than 3 copies of any single card, by name. Some cards stipulate alternative copy limits in their card text.

**1.4.8.** Tournament play may impose other restrictions or requirements on the legal configurations of identities and decks.



## 1.5. Extra Cards

**1.5.1.** Some abilities allow for the use of additional cards from outside the deck.

**1.5.2.** Jinteki Biotech: Life Imagined and Méliès U: Only the Brightest each have an ability which references different copies of the identity. There are different versions of those identities which have different abilities on their reverse side.


- a. A player playing Jinteki Biotech as their identity may bring any number of copies of that identity along with their deck. After completing game setup, that player may choose any copy they own to be their active identity for the duration of the game. All other copies are placed outside the game.

- b. A player playing Méliès U as their identity may bring any number of copies of that identity along with their deck. At the start of the game, 1 copy will be their active identity, and all other copies are placed outside the game. Whenever the first ability on Méliès U: Only the Brightest resolves, that player is instructed to "secretly set" their identity. To complete this instruction, the player secretly chooses either their current identity card or 1 of the Méliès U identity cards outside the game, and the chosen copy becomes their active identity. The player can shuffle their current identity card into the set of copies outside the game in order to obscure whether they choose to keep the same copy or switch to another copy. The chosen identity card always enters the play area with its Méliès U: Only the Brightest side faceup. All other copies are placed outside the game until the ability resolves again.


**1.5.3.** One Runner identity, Adam: Compulsive Hacker, has the ability, "You start the game with 3 different directive cards installed (these cards are not considered part of your deck)."


- a. A player playing Adam as their identity must bring at least 3 differently named cards with the directive subtype along with their deck. The player may bring any number of directive cards over the required 3.

- b. After players reveal their identities, the player playing Adam selects exactly 3 of their provided directive cards. Those cards begin the game installed in the play area. All other directives the player brought this way remain outside the game.

- c. A player playing Adam can also include directive cards in their deck. The cards chosen to be installed at the beginning of the game do not impact the deck's influence requirements or the maximum allowed number of copies of those cards.

- d. Once the game has begun, the cards installed this way are treated exactly as any other installed cards. Abilities that move these cards to other zones function normally, including shuffling them into the stack.


**1.5.4.** Two cards, Rebirth and DJ Fenris, make use of identity cards other than the one selected by the player during deck construction.


- a. A player may bring any number of additional Runner identity cards along with their deck. These cards are kept in a pile outside the game. The Runner may look at these cards at any time.

- b. When an ability refers to an identity other than the Runner's current identity, it refers to the cards provided this way. If an identity card leaves the play area, it must be returned to the pile outside the game.

- c. In a tournament, all identities brought to the game this way must be legal for players to use as their actual identity in that tournament.

- d. If the Runner switches their identity with a double-sided identity, it enters play with the front side faceup. If the Runner switches a double-sided identity with another identity, the new identity enters play faceup regardless of the previous identity's current status.



## 1.6. Starting the Game

**1.6.1.** The players decide who will play as the Corp and who will play as the Runner. Each player places an appropriate identity card for the side they are to play faceup in the play area, then supplies a corresponding deck, placing it facedown in the play area, and any appropriate extra cards.


- a. Some identity cards have abilities that affect setup. Even though cards are not active until the game begins, these identities still alter setup at the appropriate step as indicated by their text.

- b. Some identities are double-sided (see section 3.1). If a player is playing a double-sided identity, it begins the game with the front side faceup.


**1.6.2.** If a player's identity has an ability that affects setup or the start of the game, and that ability does not directly correspond to a setup step outlined in this section of the rules, that player makes any necessary decisions and changes for their identity's special setup or start of game abilities at this time. If both players have setup or start of game abilities at this time, the Corp resolves theirs first.

**1.6.3.** The players create the bank by gathering all types of tokens described in section 1.9.

**1.6.4.** Each player takes five credits from the bank, placing the credits in their credit pool.

**1.6.5.** Each player shuffles their deck.

**1.6.6.** Each player draws 5 cards from the top of their deck to form their starting hand.


- a. After drawing starting hands, the Corp may choose to take a mulligan; then, the Runner may choose to take a mulligan. To take a mulligan, the player shuffles their starting hand back into their deck, then draws a new starting hand. They must keep the second hand as their starting hand.


**1.6.7.** The game begins and the Corp takes their first turn.


- a. If the Corp's identity has an ability with instructions for "before taking your first turn," the Corp resolves that ability immediately before taking their first turn, and thus before the game starts.



## 1.7. Ending the Game

**1.7.1.** The game ends when a player meets one of their win conditions.


- a. If both players would simultaneously satisfy their win conditions, the game ends in a draw.


**1.7.2.** Each player has two possible win conditions available.


- a. Either player can win by collecting agenda points in their score area (usually done by scoring or stealing agendas; see section 1.17). A player with a score of 7 or more wins the game.

- b. The Corp wins if the Runner is flatlined. The Runner is flatlined immediately if they suffer more damage than they have cards in their grip. See section 10.4 for information about damage. The Runner is also flatlined if, at the beginning of their discard step, their maximum hand size is less than 0. See section 5.5 for information about maximum hand size and the discard step.

- c. The Runner wins if the Corp is required to draw a card from R&D but cannot because R&D is empty.



## 1.8. Cards

**1.8.1.** There are six types of Corp cards: agendas, assets, ice, identities, operations, and upgrades. All cards except the identity card are shuffled into the Corp's deck at the beginning of the game.

**1.8.2.** There are five types of Runner cards: events, hardware, identities, programs, and resources. All cards except the identity card are shuffled into the Runner's deck at the beginning of the game.

**1.8.3.** Cards that are active are able to affect the game through their abilities.


- a. Runner cards that are installed and faceup in the play area, Corp cards that are installed and rezzed, events and operations in the play area, agendas in the Corp's score area, and both players' identities are active.

- b. Unless otherwise stated, Runner cards become active as part of the process of playing or installing them.

- c. Unless otherwise stated, Corp cards other than operations are installed unrezzed, and thus inactive, into the play area. Assets, ice, and upgrades become active when they are rezzed; agendas become active when they are scored. Operations become active as part of the process of playing them.

- d. Identity cards become active when the game begins.


**1.8.4.** Cards that are inactive are unable to affect the game or have most of their abilities used.


- a. Cards are inactive in R&D, HQ, Archives, the heap, the grip, the stack, the Runner's score area, while set aside, and while removed from the game. Facedown cards in the play area are also inactive.

- b. Inactive cards, except facedown installed Runner cards, retain their printed characteristics (name, card type, faction, cost, subtypes, influence, etc). See section 8.1.4 for information about facedown installed Runner cards.


**1.8.5.** For a player to draw 1 or more cards is to take that many cards from the top of their deck and put them into their hand. Section 8.4 provides more details about drawing cards.

**1.8.6.** Most abilities are active if and only if the card they appear on is active. Section 9.1.8 details the cases where abilities on inactive cards are still active.

**1.8.7.** Abilities can convert cards from one type into another type, or even from a card into a counter. Rules 10.1.3 and 10.1.4 explain the details of card conversion.



## 1.9. Counters and Tokens

**1.9.1.** Counters and tokens are game pieces (or equivalent) that track various resources, effects, and statuses of players and their cards.


- a. The terms "counter" and "token" are interchangeable.


**1.9.2.** The bank is the supply of counters not yet in play. Counters in the bank are available to both players to take and use as dictated by the game rules and card abilities. Players do not control counters in the bank and cannot spend them. The bank is an unlimited supply; running out of game pieces to track a type of counter does not prohibit a player from gaining counters of that type or placing them on cards.


- a. If an instruction directs players to place counters on a card without designating where those counters come from, take those counters from the bank.

- b. A condition or restriction that looks for counters being placed on a card can be met by either counters from the bank or counters moved from another location.


**1.9.3.** Some abilities can convert a card into a counter. A counter put into play in this way is tracked with the card acting as a game piece. See rule 10.1.4.

**1.9.4.** Some abilities can cause a player or card to have or be "considered to have" 1 or more counters or additional counters without placing or giving those counters. A counter considered to exist in this way conveys all the same information and effects as a counter of the specified type would, except that it cannot be moved or removed from the card or player it belongs to, nor can it be spent to pay a cost.

**1.9.5.** There are ten types of counters: credit counters, click counters, tag counters, bad publicity counters, core damage counters, advancement counters, virus counters, power counters, agenda counters, and condition counters.


- a. Credit counters are used to track the number of credits each player has in their credit pool; they can also be placed on cards. Rules for credits, the credit pool, and other related concepts are in section 1.10.

- b. Click counters are a gameplay aid used to track the clicks the active player has spent or has left to spend during their turn. Rules for clicks are in section 1.11.

- c. Tag counters are used to represent tags on the Runner. Rules for tags are given in section 10.5.

- d. Bad publicity counters are used to represent bad publicity on the Corp. Rules for bad publicity are in section 10.6.

- e. Core damage counters are a gameplay aid used to track core damage the Runner has suffered. Each point of core damage the Runner suffers forces them to take a core damage counter. See section 10.4.

- f. Advancement counters are a counter used primarily on installed agendas to track the Corp's progress toward being able to score them.

- g. Virus counters are a generic counter, used primarily by virus programs, that can be removed by the Corp purging them. See rule 10.1.2.

- h. Power counters are a generic counter used by a variety of cards. They have no special rules.

- i. Agenda counters are a generic counter used primarily on scored agendas. They have no special rules.

- j. Condition counters are counters that have rules text. Their abilities are active as long as they are hosted on a card.



## 1.10. Credits


Each of the Runner's credits represents enough money to upgrade some basic parts for their console, have a meal at a decent restaurant, or buy a ticket and some concessions for a night at the sensies. Each of the Corp's credits represents enough money to manufacture a run of computer parts, buy out a decent restaurant, or film a low-budget sensie.

**1.10.1.** A credit ({c}) is the basic unit of currency. Players spend their credits to pay for various costs, card abilities, traces, etc. Credit counters most commonly represent 1{c} each, but can represent larger denominations if clearly marked.

**1.10.2.** Each player has a credit pool where they keep a supply of credit counters matching the credits they have available to spend. The number of credits in a player's credit pool is open information.

**1.10.3.** Credits enter and leave a player's credit pool as that player gains, loses, or spends credits.


- a. A player gains credits whenever credits enter their credit pool from any location.

- b. If a player is required to lose credits, that player is forced to move the specified number of credits from their credit pool to the bank. Players cannot lose credits from cards.

  - Example: If a subroutine on DNA Tracker resolves, the Runner must lose 2{c} from their credit pool, or 1{c} if that is all they have. If the Runner's credit pool is empty, the effect does nothing.

- c. To spend or pay credits, a player puts the specified number of credits back into the bank. By default, the credits must come from the player's credit pool. Abilities can also allow players to spend credits hosted on their cards under certain conditions. If any such abilities apply, the player chooses how to divide the credits they are spending from among the allowed locations. The terms "spend" and "pay" are synonymous.

  - Example: The Runner may use the credit from Cyberfeeder to pay for Atman's first ability.
  - Example: The Runner may use credits from Ghost Runner when secretly spending credits to resolve a Psi ability (such as The Future Perfect).
  - Example: The Runner must use credits from Ghost Runner if this is the only way for them to pay 3{c} when resolving the "when encountered" ability on Tollbooth.


**1.10.4.** Abilities can place credits on cards, as discussed in section 1.13. Credits on a player's card are not in that player's credit pool; the player can only interact with those credits as instructed by card abilities.


- a. If a player is instructed to take credits from a card, they remove that many credits from that card and gain those credits.

- b. If an ability specifies how a player is allowed to spend credits from a card, they can be spent from that card as if they were in the player's credit pool for the specified actions or abilities.

- c. If an ability specifies a time period during which a player is allowed to spend credits from a card, they can be spent from that card as if they were in the player's credit pool for any purpose (other than losing credits) as long as the specified time period is active or the specified duration has not expired.

- d. Spending credits from a card is considered "using" the card that allowed those credits to be spent. See section 9.1.6.


**1.10.5.** Recurring Credits


- a. Recurring Credits ({recurring}) place credits on a card repeatedly. The text "N{recurring}" means "When this card becomes active, place N credits on it. Before abilities meet their trigger conditions for your turn beginning, if there are fewer than N credits on this card, place credits on it until there are N credits on it."

- b. Recurring credits are first placed on a card as soon as it becomes active. This occurs at step 8.5.16e of installing the card if it is installed active, step 8.6.7c of playing an event or operation, or as soon as the card is turned faceup or scored, as applicable.

- c. Recurring credits are refilled during step 5.6.1c of the Corp's turn and step 5.7.1c of the Runner's turn, before other abilities that apply at the start of the turn resolve.

- d. Recurring credits do not accumulate. They are refilled only up to the indicated number.

  - Example: The Runner installs Spinal Modem, which has 2{recurring}. 2 credits are placed on Spinal Modem now. The Runner spends 1 of those credits later in their turn. At the beginning of the Runner's next turn, the credits on Spinal Modem should be replenished up to 2{c}, so 1 more credit is placed on it.



## 1.11. Clicks


Working a job, making connections, and especially jacking in—everything you do takes time, and it always goes by faster than you think. A click represents an abstract amount of time spent on a particular activity, either several hours all at once or scattered across the day.

**1.11.1.** A click ({click}) is the basic unit of activity. Players spend their clicks to perform actions and trigger abilities. Each click counter represents 1 click.

**1.11.2.** As the first step of a player's turn, they gain an allotted number of clicks to spend during the action phase of that turn. See section 5 for details about the procedures of player turns.


- a. The Corp receives 3{click} on each of their turns.

- b. The Runner receives 4{click} on each of their turns.


**1.11.3.** Players can gain, lose, or spend clicks.


- a. A player gains clicks whenever the number of clicks they have is increased.

- b. If a player loses or spends clicks, the number of clicks they have is reduced by that amount. The terms "lose" and "spend" are not synonymous for the purposes of meeting conditions and restrictions of card abilities.

- c. Paid abilities that begin with {click} are actions and have special timing rules (see section 5.2). Aside from taking actions, there are no special restrictions to how or when a player can spend their clicks during their turn.


**1.11.4.** Some cards have the subtype priority and the text, "Play only as your first {click}." A player can only play a priority card using the basic action to play an event or operation, and only if they have not spent any other clicks that turn. Losing clicks does not affect a player's ability to play a priority card.



## 1.12. Objects

**1.12.1.** An object is an occurrence of a card or counter existing in a particular zone. When a card or counter moves from one zone to another, it becomes a new object.

**1.12.2.** Restrictions and effects on a card or counter only apply for as long as that card or counter remains the same object. Cards and counters have no memory of the objects they were in the past.


- Example: The Corp uses the first ability on Vaporframe Fabricator, which specifies that it can only be used once per turn, and trashes Vaporframe Fabricator during the installation process. Then the Corp plays Restore, reinstalling the same copy of Vaporframe Fabricator from Archives. Even though it is the same physical card, the reinstalled Vaporframe Fabricator is a new object, and its "once per turn" ability is available to be used this turn.

- a. An ability that moves cards can identify the new objects that those cards become and continue to act on them.

  - Example: The Corp plays Priority Construction, installing a piece of ice from HQ. That ice is now a new object in the play area, but Priority Construction's second instruction can still find it and place advancement counters on it.


**1.12.3.** A card that moves from its location to a hidden or secret location becomes a new object, even if the new location is in the same zone.

  - Example: The Corp scores Accelerated Beta Test, looks at the top 3 cards of R&D, and installs and rezzes a piece of ice from among them. As a chain reaction, the Corp then uses The Foundry: Refining the Process to search R&D, with the result that the other cards currently being looked at by Accelerated Beta Test are shuffled along with the rest of R&D. Since the cards were moved to an unknown location in R&D, Accelerated Beta Test can no longer act on them, so those cards can no longer be looked at and will not be installed or trashed.
  - Example: The top card of R&D in the current game state is an object. So is the 2nd card from the top of R&D, the 3rd card, and so on. If the Corp plays Precognition and rearranges the top 5 cards of R&D, the Runner will not know which cards correspond to which objects in the previous order, so the cards become new objects under their new order.

**1.12.4.** A card or counter that moves to a known location in the same zone is still the same object.

  - Example: The Runner has chosen a piece of ice with Femme Fatale, and the Corp uses Thimblerig's ability to move that ice to another location. But the ice has not left the play area, so it is still the same object and the Runner can still use Femme Fatale's ability to bypass it.

**1.12.5.** A card that is turned faceup or facedown is still the same object.

  - Example: The Corp uses Vaporframe Fabricator, installing a piece of ice at no cost, and then plays Divert Power to derez Vaporframe Fabricator and rez their newly-installed ice at a discount. The Corp can rez Vaporframe Fabricator again, but since it is still the same object, its "once per turn" ability cannot be used again this turn.

**1.12.6.** When a card or counter becomes a new object, its previous object ceases to exist, but can still be remembered by rules or abilities that refer to past game states.


- Example: The Runner plays Bravado. During the run, a piece of ice that the Runner has already passed is trashed. When the run ends, Bravado's ability must determine how many pieces of ice the Runner passed during that run. It does this by reviewing the game history and counting each distinct ice object that the Runner passed. Even though the object corresponding to the trashed ice no longer exists in the present game state, Bravado can still count it.
- Example: The Runner trashes the top card of R&D while accessing it. The object corresponding to that card in its location on top of R&D ceases to exist, but still counts against the number of cards the Runner can access from R&D during this breach.

- a. An object that has ceased to exist still counts as the source for any active lingering effects created by that object. See section 9.10 for information about lingering effects.

  - Example: The Runner runs a server, steals an agenda, and accesses a rezzed copy of AMAZE Amusements. The Runner trashes AMAZE Amusements, and the object they had accessed ceases to exist, while the trashed card becomes a new object in Archives. The source of the persistent ability created by the trashing is the original object in the root of the server, so the reference to "this server" in the persistent ability still refers to the attacked server, and the Runner will take 2 tags when the run ends.



## 1.13. Host, Hosted, and Hosting

**1.13.1.** Whenever an object is "placed" or "loaded" onto a card, a host relationship is created between those two objects. The object placed on top of a card is hosted on that card. The card onto which the other object is placed is the host.


- a. Only cards in score areas and the play area can host objects.


**1.13.2.** The state of being hosted is distinct, but not exclusive from, the state of being installed.


- a. If an ability instructs a player to host a card that is not installed without reference to installing it, the hosted card does not become installed.

- b. If an installed Corp card becomes hosted on a Runner card, the Corp card becomes uninstalled.

- c. If a card is installed from a hosted state, it ceases to be hosted. The hosting relationship ends at step step 8.5.16a of the installation.


**1.13.3.** Hosted counters do not count as "on" a player or as objects a player "has". They are not counted by abilities that check the number of counters on a player and cannot be affected by abilities that remove counters from a player.

  - Example: The Corp resolves the first subroutine on Whitespace, forcing the Runner to lose 3{c}. The Runner cannot lose credits hosted on their cards, even if they do not have 3{c} in their credit pool.
  - Example: The Corp has a rezzed Superdeep Borehole with hosted bad publicity counters. When the Runner fills their bad publicity fund in step 6.9.1b, they do not receive credits for the hosted bad publicity.
  - Example: The Corp plays Scapegoat to remove 2 bad publicity. They cannot remove bad publicity counters hosted on Superdeep Borehole.

**1.13.4.** An object can only be hosted on a single card at any given time.

**1.13.5.** A card can host any number of other objects, except where explicitly restricted by a card ability.

**1.13.6.** Any card in an eligible location can act as a host, but a host relationship can only be created or permitted by a card ability.


- a. If a card has an ability describing the types and/or numbers of cards it can host, but it does not have an ability that directly hosts cards onto itself, then the card is permitted as an eligible installation destination for the types of cards listed, up to the number specified. A host card is chosen as an installation destination during step 8.5.16b of the installation process.

  - Example: Off-Campus Apartment has the ability, "Off-Campus Apartment can host any number of connections." This means that whenever the Runner installs a connection card, they can choose to install that card into the play area hosted on Off-Campus Apartment or as normal directly into the play area.

- b. If a card has an ability describing cards it can host, but also one or more abilities that can create host relationships onto itself, then it only allows cards to become hosted on it through those abilities. A card cannot be installed directly onto the card through an unrelated install effect.

  - Example: Glenn Station reads, "Glenn Station can host a single card", but also has a paid ability that hosts a card on itself. Therefore, Glenn Station only lets the Corp host cards through that paid ability, not through an install action.

- c. If a card has an ability stipulating that it can only be installed hosted onto another card, then during the installation process a player that wishes to install that card must choose a valid destination matching the description provided. If no such destination exists before the installation process begins, the card cannot be installed.

  - Example: Egret states, "Install only on a rezzed piece of ice." The Runner can only install Egret if there is an installed, rezzed piece of ice available before the installation process begins; otherwise, it is illegal for the Runner to install Egret.

- d. Some operations and events convert themselves into condition counters and install themselves hosted onto other cards. An operation or event can never be installed through an install effect; it can only become an installed counter through its conversion ability. See rule 10.1.4.


**1.13.7.** Cards can be hosted faceup or facedown.


- a. Unless otherwise specified, a card that is hosted while it is being installed is hosted onto the destination card in the same faceup or facedown status it would normally be installed in.

- b. Unless otherwise specified, a card that is hosted without being installed is hosted faceup.

- c. Like other facedown installed cards, installed cards hosted facedown are distinct. The order in which they were installed (or turned facedown) is open information.

- d. Cards hosted facedown without being installed must be kept in distinct groups according to their host. Cards within such a group are not ordered and can be freely arranged by their controller.


**1.13.8.** Once a card is installed, hosted or not, a player cannot change that card's hosted status unless a card ability explicitly directs it to be hosted onto a new card.

**1.13.9.** Host relationships are not transitive. If card A is hosted on card B, and card B is hosted on card C, card A is not considered to be hosted on card C.

  - Example: If the Runner installs a Leprechaun hosted on a Dhegdheer, programs hosted on Leprechaun do not have their install costs reduced because they are hosted on Leprechaun and not on Dhegdheer.

**1.13.10.** If an ability on a card refers to a "hosted" card or counter, that ability only references objects hosted on that card.

**1.13.11.** Hosted objects can be removed or spent from their host without affecting the host.

**1.13.12.** Creating a hosting relationship moves the hosted object to the same zone as the host. If the hosted object is subsequently moved to another zone or hosted on a different card, the hosting relationship ends.

  - Example: The Runner uses the action on Madani, choosing 2 programs in their grip to host on Madani. The chosen cards change zones, moving from the Runner's grip to the play area.

**1.13.13.** If a host card changes zones, except by moving from a player's score area to another player's score area, all objects hosted on that card (and all objects hosted on those objects, and so on) are trashed during the next checkpoint. This cannot be prevented. See section 10.3 for details on checkpoints.

  - Example: The Runner has an installed copy of Detente with Corp cards hosted on it. They play Rejig, adding Detente to their grip. The hosted Corp cards are trashed.
  - Example: The Runner has Next Big Thing in their score area with a hosted agenda counter. The Corp plays IP Enforcement to install that agenda. The hosted agenda counter will be trashed after step 8.5.16d of the installation.
  - Example: The Runner has Next Big Thing in their score area with a hosted agenda counter. The Corp plays Exchange of Information to swap that agenda to their own score area. The agenda counter remains hosted on Next Big Thing.

**1.13.14.** Condition counters are the only type of counter that can host other objects. Hosting on condition counters follows the same rules as hosting on cards.



## 1.14. Ownership and Control

**1.14.1.** The owner of a card is the player who provided that card at the start of the game as their identity, part of their deck, or an extra card along with their deck.


- a. A card that is converted into a counter retains its owner.

- b. No player owns counters that are not converted cards.


**1.14.2.** The controller of an object is the player responsible for that object.


- a. The controller of a card in the play area is the player who installed or placed it there.

- b. The Corp controls each agenda in the Corp's score area. The Runner controls each agenda in the Runner's score area.

- c. Cards in other zones are controlled by their owner.

- d. Each player controls the credits in their credit pool.

- e. The Corp controls each bad publicity counter. The Runner controls each tag counter.

- f. The controller of a counter hosted on a card is the player who controls the host card.

- g. Click counters and core damage counters are gameplay aids only and have no controller.


**1.14.3.** A player can only pay costs using objects they control, unless specifically indicated.


- a. Either player can remove tags or bad publicity from themself or their opponent to pay costs.


**1.14.4.** The controller of an ability is the player responsible for that ability. By default, this is the player who controls the ability's source. See also section 9.1.3. A player can only use abilities they control.


- a. Unless specifically stated otherwise, abilities on agendas are controlled by the Corp, even for agendas in the Runner's score area.

- b. Some abilities state that they can only be used by a specific player. The specified player controls each such ability, even if they do not control its source.


**1.14.5.** Unless otherwise noted, the controller of an ability carries out its effects and makes any choices required. If a player is specified to perform all or part of an effect, the specified player carries out that part of the effect and makes any choices required instead.


- Example: Rototurret's first subroutine reads, "Trash 1 installed program." Since Rototurret is a Corp card, the Corp chooses which installed program to trash. Conversely, Bulwark's first subroutine reads, "The Runner trashes 1 installed program." Since the Runner is specified to be carrying out this effect, they choose which of their installed programs to trash.

- a. Some trigger conditions care about effects performed by a particular player. These conditions are only met when that player is the one to carry out the relevant effect.

  - Example: The Corp has a rezzed Hostile Infrastructure. Apocalypse does not specify who trashes the cards, so the Runner is responsible for the trashing and Hostile Infrastructure meets its trigger condition.
  - Example: Alice Merchant states that "the Corp must trash 1 card from HQ", so the Corp carries out this effect even though Alice Merchant is a Runner card, and therefore Hostile Infrastructure does not meet its trigger condition.



## 1.15. Targets

**1.15.1.** Many abilities require players to choose 1 or more objects or subroutines to affect. Each object and subroutine chosen for an ability is a target of that ability.


- Example: Top Hat reads in part, "you may choose 1 of the top 5 cards of R&D and access it." The target of this instruction is the card in R&D the Runner chooses.
- Example: One of the subroutines on Colossus reads in part, "Trash 1 installed program and 1 installed resource." The targets of this subroutine are the two cards that will be trashed.
- Example: Trick of Light reads, "Choose 1 installed card you can advance. Move up to 2 advancement counters from 1 other card to the chosen card." The targets of this operation are the advancement counters to be moved and the destination card. If 2 tokens are chosen, they must be hosted on the same card.
- Example: The Runner is encountering a barrier and uses Cleaver to break subroutines. The targets of Cleaver's' interface ability are the 1 or 2 subroutines that it will break.

- a. Cards found during a search are not considered targets of the instruction that performs the search. Players do not need to announce what card(s) they expect to find before the search actually takes place. See section 8.7 for rules about searching.

- b. Only objects and subroutines are announced as targets. If an instruction directs a player to choose (or "name") a number, a card type, a subtype, a card name, a server, or one of a specified set of effects, that choice is not made until the instruction resolves.


**1.15.2.** Immediately before an instruction becomes imminent, the player resolving that instruction must first choose the targets required by that instruction. For each time the instruction requires a player to choose 1 or more objects and/or subroutines, that player announces appropriate targets. Once players are finished announcing targets for an instruction, the instruction becomes imminent, and players have the opportunity to modify the effects of that instruction with interrupt abilities before it resolves. See section 9.9.


- a. When a player announces a card as a target, they indicate clearly which card is being chosen without changing its facedown/faceup status or location.

- b. The player resolving the instruction can only choose targets that are valid for that instruction. A target is valid for an instruction if it meets any specified requirements of that instruction or can otherwise be acted upon by that instruction.

- c. Unless an instruction explicitly specifies the zone from which an object must be selected as a target, only counters in the play area and installed cards are valid targets for that instruction.

  - Example: The Corp resolves a subroutine that says "{sub} The Runner trashes 1 program." The Runner must trash 1 of their installed programs and may not trash a program from their grip or stack.

- d. Some effects allow a player to choose multiple cards as targets for a single announcement. Section 9.12.2 contains rules about resolving effects involving sets of cards.

- e. A player may announce the targets for an instruction in any order, but each object or subroutine can only be chosen as a target once for each announcement. If there are not enough valid targets available, then that player chooses as many distinct targets as possible and the remaining targets are not announced.

  - Example: The Runner accesses an Aggressive Secretary with three advancement counters on it. The Runner only has two installed programs, so the Corp chooses both of those programs for Aggressive Secretary. After the programs have been chosen, they are both trashed simultaneously.

- f. Targets cannot be chosen for an instruction at any other time. Even if the game state changes due to interrupt abilities while the instruction is imminent, unannounced targets remain unchosen.


**1.15.3.** At the time an instruction resolves, if any of its targets either have become invalid or were not announced, as much of that ability resolves as possible without acting on the unannounced or invalid targets, following rules 1.2.3 and 1.2.4 of the Golden Rules. A target can be invalid because the chosen object or subroutine no longer exists or because the target no longer meets other requirements specified by the targeting ability.

**1.15.4.** After the resolution of an instruction that chose a particular target, subsequent instructions of the same ability can continue to act on that target without needing to select it again, even if the ability moves the target or changes its properties. See also section 1.12.2.

  - Example: Howler's ability targets a card in HQ. Its first instruction installs that card, and its second instruction creates a delayed conditional ability that refers to that card in the play area. The latter ability can find and act on the card as long as it is still installed.



## 1.16. Costs

**1.16.1.** In order to use an ability or apply an effect, a player may be required to pay a cost. A cost can take the form of any card, counter, or other item a player must spend; an effect a player must resolve; or other type of requirement a player must meet. If a player cannot pay the full cost of an action or ability all at once with cards and counters they control, they cannot use the effect associated with that cost.


- a. The act of paying a cost cannot be modified or cancelled by optional interrupt abilities.

  - Example: The Runner can trash Clone Chip to trigger its paid ability, but cannot use LLDS Energy Regulator to prevent Clone Chip from being trashed this way.

- b. If a static ability or a mandatory conditional ability interrupt would prevent the steps of paying a cost from being carried out if they were performed as an effect, that cost cannot be paid.

  - Example: The Runner cannot pay the additional cost to steal Obokata Protocol while Guru Davinder is installed, because the Runner could not currently take 4 damage if instructed to: Guru Davinder's first ability would prevent the damage.
  - Example: The Runner is playing as Jesminder Sareen and encounters Funhouse, without any other tag-related effects resolving earlier in the run. Since Jesminder Sareen's ability would prevent taking 1 tag, the Runner cannot take 1 tag to pay the nested cost in Funhouse's ability. Funhouse will end the run.

- c. If triggering an ability or resolving an effect is subject to both costs and other restrictions, the cost cannot be paid unless all such restrictions are met, and cannot be paid in a way that would result in any restriction no longer being met.

  - Example: The Runner controls Zer0 and Clan Vengeance, and wants to accumulate more power counters on Clan Vengeance. Zer0's ability has the restriction "Use this ability only once per turn." If the Runner has already used the ability this turn, they cannot suffer additional net damage by attempting (even unsuccessfully) to use the ability again.
  - Example: The Corp installs Azef Protocol in the root of a server with a rezzed SanSan City Grid and advances Azef Protocol twice. Since SanSan City Grid reduces Azef Protocol's advancement requirement to 2, the Corp now meets the restriction necessary to score Azef Protocol, but must also pay its additional cost to score. The Corp cannot trash the SanSan City Grid to pay this cost, because paying the cost this way would return Azef Protocol's advancement requirement to 3, and the restriction to score it would no longer be met.

- d. A cost of 0 can still be paid by a player, even though no items are paid. Costs of 0 are not paid automatically. A player pays a cost of 0 by explicitly announcing they are paying it.

  - Example: The Runner accesses a Beanstalk Royalties from R&D and announces that they are using the ability of Freedom Khumalo: Crypto-Anarchist to trash the Beanstalk Royalties. Even though the Runner does not remove any virus counters from any cards, they have still paid the cost.


**1.16.2.** The actual contents of a cost can be dependent on the game state. Abilities can modify their own costs, modify costs of a certain type or that meet certain criteria, or include costs that need to be calculated.


- a. If a quantity in a cost is subject to effects modifying its value, determine the final value of that quantity by taking its default value, applying each effect that increases the cost, and then applying each effect that lowers the cost. Finally, if the value determined by this process is less than 0, set the value to 0.

- b. Some abilities calculate a quantity using phrases like "for each", "for every", or "plus". Any time such a calculation appears in a cost, the result of that calculation is determined at the time the cost is to be paid. The result is taken as an aggregate, so that paying the cost is a single instance of whatever was paid. Note that additional costs (section 1.16.11) are not aggregated with other costs. See also Section 9.12.2 for rules about calculated quantities that appear in other contexts.

  - Example: The Runner approaches a server protected by 3 advanced pieces of ice and with a rezzed Cayambe Grid in its root. They pay the nested cost in Cayambe Grid's ability. This cost is 1 payment of 6{c}, not 3 payments of 2{c}, so this only meets the trigger condition for 1 instance of GameNET's ability.

- c. Some costs contain the variable X. Before a player pays such a cost, they choose and announce a positive integer or 0 to be the value for X. The chosen value must follow any applicable restrictions.

  - Example: Psychographics is an operation with a play cost of X. It also has the ability, "X must be equal to or less than the number of tags the Runner has." If the Runner has 3 tags, the Corp can choose 0, 1, 2, or 3 as the value for X when they play Psychographics.

- d. If an ability needs to know the value of a cost in a context where that cost is not being paid, treat any X that appears in that cost as 0.

- e. Some abilities modify a cost by allowing a player to pay part or all of that cost in an alternate way. This does not change the value of the cost, but gives the player an additional option when deciding how to pay that cost.

  - Example: The Corp rezzes Biawak, which has the ability "You can forfeit 1 agenda as you rez this ice to pay for 10{c} of its rez cost." and a rez cost of 14{c}. The Corp chooses to pay Biawak's rez cost by spending 2{c} from their credit pool, spending 2{c} from a Mahkota Langit Grid, and forfeiting 1 agenda.

- f. An ability that instructs the Corp to install and rez a card can also reduce the "total" cost to do so. This use of "total" indicates that the Corp can divide the modifier between the install cost and the rez cost of the relevant card. When such a modifier applies, the Corp declares a number of credits by which to reduce the install cost and a number of credits by which to reduce the rez cost. This declaration is made at the beginning of step 8.5.16d, before calculating the value of the install cost, and the numbers declared this way must be nonnegative numbers whose sum is equal to the number of credits specified by the original "total" modifier. The respective costs are then lowered by the declared amounts, following the normal procedure described in rule 1.16.2a.

  - Example: The Corp uses the ability on Tucana to install and rez a copy of Logjam from R&D, paying a total of 3{c} less. They install the Logjam protecting a server that already has 1 piece of ice protecting it, meaning that without the "total of 3{c} less" modifier, the install cost would be 1{c} and the rez cost would be 6{c}. The Corp applies 1{c} of the discount to the install cost and 2{c} to the rez cost, leaving a final install cost of 0{c} and a final rez cost of 4{c}.


**1.16.3.** After a player pays a cost, a checkpoint occurs. See section 10.3.


- a. This checkpoint occurs even if the cost being paid is 0.


**1.16.4.** There are six main types of costs: install costs (found mainly on hardware, programs, and resources, and incurred when installing ice), play costs (found on operations and events), rez costs (found on assets, upgrades, and ice), paid ability trigger costs, additional costs, and nested costs.


- a. Install costs, rez costs, and play costs are all costs that are inherent properties of cards. These costs are automatically applied to any ability that performs the corresponding effects (installing, rezzing, or playing) unless that ability stipulates otherwise. See also section 2.3 (for more about these costs as they appear on cards) and section 8.5.11, rule 8.1.2d and section 8.6.2 (for more about paying costs to install, rez, and play cards).

- b. The presence of an install cost, rez cost, or play cost does not change the optional or mandatory status of an ability. If an effect directs a player to install, rez, or play a card, they must pay the associated cost if able. If they cannot, the card is not installed, rezzed, or played.

- c. If an effect directs a player to install, rez, or play a card, but doing so has an additional cost, the player is not forced to pay the additional cost. If they decline to do so, they do not pay the install, rez, or play cost either, and the card is not installed, rezzed, or played.

- d. When a player takes an action that plays or installs one or more cards and has no other effects, the play cost or install cost of each of those cards is considered to have been spent to take that action, even though other steps take place between initiating the action and paying that cost.

  - Example: The Corp has Jeeves Model Bioroids rezzed, and uses the basic action "{click}: Play an operation." to play Blue Level Clearance. Both the click spent for the cost of the basic action and the click spent for Blue Level Clearance's additional cost count as clicks spent to take the "{click}: Play an operation." action.


**1.16.5.** Some abilities direct a player to ignore certain costs when performing particular effects.


- a. If a player is directed to ignore 1 or more of the 6 main types of costs, the specified costs are removed from the total cost that must be paid. Any other costs (usually additional costs) that are not specified to be ignored still apply normally.

- b. If a player is directed to ignore "credit costs", any credits to be paid from any type of cost are removed from the total cost. Any costs that do not consist of credits still apply normally.

- c. If a player is directed to ignore "all costs", all elements of the relevant cost are removed, including additional costs, so that the total cost becomes 0.


**1.16.6.** The install cost of a card must be paid to install that card.


- a. The install cost of a program, resource, or piece of hardware is listed on the card itself.

- b. The install cost of a piece of ice is a number of credits equal to the number of pieces of ice already protecting the server that ice will be installed protecting.

- c. There is no cost to install an asset, upgrade, or agenda.


**1.16.7.** The play cost of an operation or event must be paid to play that card. It is listed on the card itself.

**1.16.8.** The rez cost of an asset, upgrade, or piece of ice must be paid in order to rez that card. It is listed on the card itself.

**1.16.9.** The trigger cost of each paid ability is the first part of that ability's text. It is followed by a colon (:) and then the ability's effect. See section 9.5 for more details on paid abilities.

**1.16.10.** An additional cost adds something to the regular cost of initiating a particular game effect. A player must pay all additional costs along with any regular costs in order to initiate an effect.


- a. If a player would be forced to carry out a game effect, but doing so has an additional cost, that player may decline to pay the additional cost, even if they are able to pay it, thus preventing that effect from occurring.

  - Example: The Runner accesses Obokata Protocol. Normally the Runner must steal agendas that they access, but because Obokata Protocol has an additional cost to steal, the Runner can decline to suffer the net damage and not steal the agenda.

- b. A player must pay all additional costs simultaneously with the cost that is being added to, even if multiple cards or abilities are adding separate additional costs. A player cannot pay the original cost or any of the additional costs individually. If they cannot pay for all of the costs at once, then they do not pay any of the costs and the corresponding effects cannot occur.

  - Example: The Runner accesses Obokata Protocol while Ben Musashi and Predictive Algorithm are both active. The Runner must be able to pay 2 credits and suffer 6 net damage all at once in order to steal the Obokata Protocol, even though each of the three costs are from different card abilities. After all costs have been paid, abilities that meet their trigger conditions from the paying of any of those costs, such as I've Had Worse or Order of Sol, can then resolve as applicable.

- c. If an additional cost is required for an effect that does not normally require paying any cost, the additional cost is paid and a checkpoint is resolved before performing the usual procedure to carry out that effect. See also rule 10.3.4.

  - Example: The Corp is playing Ob Superheavy Logistics and initiates scoring Azef Protocol, which has an additional cost to score of "trash one of your other installed cards." If the Corp trashes a rezzed card, meeting the trigger condition for Ob Superheavy Logistics's ability, this ability will resolve following the checkpoint associated with paying the cost. This happens before Azef Protocol is added to the Corp's score area or any trigger conditions related to scoring an agenda are met.


**1.16.11.** A nested cost is a cost appearing within an ability's instructions that must be paid while the ability is resolving in order for some or all of the rest of the effects of that ability to resolve.


- a. Nested costs are usually written in the format "[player] may [cost] to [instructions]" or "[player] may [cost]. If [you/they] do, [instructions]." If the indicated player pays the indicated cost, the indicated instructions are resolved. Otherwise, that part of the ability is not resolved.

- b. Nested costs can also be written in the format "[instructions] unless [player] [cost]" or "[player] may [cost]. If [you/they] do not, [instructions]." If the indicated player pays the indicated cost, the indicated instructions are not resolved. Otherwise, that instruction is resolved.

  - Example: A subroutine reading "End the run unless the Runner pays 1{c}." contains a nested cost. If the Runner chooses to pay 1{c}, the subroutine will not end the run.

- c. An ability that reads "If [you/they] do" or "If [you/they] do not" without a preceding "may" does not indicate a nested cost.

- d. Some abilities use the word "otherwise" to indicate that different instructions should be resolved depending on whether a nested cost was paid or not. In an ability with a nested cost of a form described in rule 1.16.11a, treat "otherwise" as an "if [you/they] do not" clause attached to the same cost. Conversely, in an ability with a nested cost of a form described in rule 1.16.11b, treat "otherwise" as an "if [you/they] do" clause attached to the same cost. Note that "otherwise" can also appear in the context of other types of conditions, and does not by itself indicate a nested cost.



## 1.17. Score, Scoring and Stealing

**1.17.1.** The sum of all agenda points on agendas in a player's score area is that player's score.


- a. The threat level is equal to the greatest score of any player. The "Threat N" ability flag makes use of the threat level (rule 9.3.6f).

  - Example: If the Runner's score is 4 agenda points, and the Corp's score is 3 agenda points, the threat level is 4.


**1.17.2.** If at any time a player's score is greater than or equal to 7, they win the game at the next checkpoint. See section 1.7 regarding winning the game, and section 10.3 for details about checkpoints.

**1.17.3.** Players add agendas to their score areas by scoring or stealing them. The Corp can score an agenda to their score area as an option during certain paid ability windows on their turn. The Runner can steal an agenda to their score area while accessing it.


- a. The advancement requirement of an agenda restricts when the Corp can score it. The Corp can only score an agenda that has advancement counters on it greater than or equal to its advancement requirement. See section 1.18.

- b. The Corp is not required to score an agenda immediately upon satisfying its advancement requirement.

- c. Scoring an agenda does not cost {click} and is not an action.

- d. The Runner cannot decline to steal an agenda they access, unless there is an additional cost to steal that agenda and the Runner cannot or does not wish to pay it. Section 7 details accessing, and section 1.16.10 details additional costs.

- e. If an effect directly adds an agenda to a score area or otherwise moves an agenda from any zone to a score area, that agenda is not considered scored or stolen.

- f. If an effect causes another card to be added to a score area "as an agenda", the newly converted agenda is not considered scored or stolen. See rule 10.1.3.


**1.17.4.** Agendas are always added to the score area faceup, regardless of their previous faceup/facedown status.

**1.17.5.** If an installed agenda is scored or stolen, it becomes uninstalled. Any advancement counters on the scored or stolen agenda are returned to the bank. See rule 1.13.13.

**1.17.6.** Some agendas and other cards have abilities with a trigger condition related to an agenda being scored. These abilities become pending after the Corp moves the agenda from its current zone to their score area.

**1.17.7.** Some agendas and other cards have abilities with a trigger condition related to an agenda being stolen. These abilities become pending after the Runner moves the agenda from its current zone to their score area.

**1.17.8.** If an ability meets its trigger condition from an agenda being scored or stolen, the agenda's last known number of advancement counters before being scored or stolen is used for any references to the number of advancement counters in the effects of that ability.



## 1.18. Advancing Cards

**1.18.1.** To advance a card is to place an advancement counter from the bank on it. The Corp typically advances cards with a basic action during their action phase, but card abilities can also advance cards.

**1.18.2.** Resolving an instruction that directly places an advancement counter onto a card is not the same as advancing a card. Likewise, resolving an instruction that moves an advancement counter from one card to another is not the same as advancing a card.

  - Example: The Corp plays Mushin No Shin, choosing to install an Oaktown Renovation. Because the three advancement counters were placed on the card directly, the Corp does not gain any credits from the ability on Oaktown Renovation.

**1.18.3.** The Corp can only advance certain installed cards. Agendas can always be advanced. If a card other than an agenda says that it "can be advanced" or that "you can advance" that card, the card can be advanced even while it is unrezzed. There is no limit to the number of times a card can be advanced.

**1.18.4.** An "advanced card" is a card with 1 or more advancement counters hosted on it.



## 1.19. Trashing

**1.19.1.** Trashing is the act of moving an object to its owner's discard pile.

**1.19.2.** During access, the Runner has the opportunity to pay the trash cost of the accessed card to trash it. See section 7.1.5.

**1.19.3.** A trashed card is not considered to have been discarded, and vice versa. Cards that prevent a card from being trashed cannot prevent a card from being discarded.

**1.19.4.** The symbol {trash} represents trashing the object it appears on. It is used as a cost for certain abilities.


- Example: Fall Guy's last ability reads "{trash}: Gain 2{c}." The cost to trigger this ability is to trash Fall Guy.

- a. "A {trash} ability" refers to a paid ability that includes {trash} in its trigger cost.

- b. Some paid abilities have multiple options for their trigger cost, separated by "or". A player triggering such an ability only "used a {trash} ability" if the actual cost that was paid includes {trash}.



## 1.20. Memory

**1.20.1.** A memory unit ({MU}) is a space available to the Runner to install programs.

**1.20.2.** The Runner's memory limit is the total amount of {MU} the Runner has. If the Runner's identity card does not specify a starting memory limit, their starting memory limit is 4{MU}. An ability of the form "+N{MU}" increases the Runner's memory limit by N{MU}.

  - Example: The Runner is playing an identity with a starting memory limit of 4{MU}. They have an installed copy of T400 Memory Diamond, which has the ability "+1{MU}". The Runner can therefore have up to 5{MU} worth of programs installed.

**1.20.3.** Programs have a memory cost that is continually applied against the memory limit. Section 3.9.3 details how the Runner's installed programs are constrained by the memory limit.

**1.20.4.** Some abilities give the Runner {MU} that can only be used for programs that meet a given requirement.


- a. If a calculated value refers directly to the Runner's memory limit or to their unused {MU}, that calculation ignores requirements about how that {MU} can be filled.

- b. To determine if the Runner's installed programs satisfy the restrictions on their {MU}, create a mapping that assigns each 1{MU} in the memory cost of each installed program to a specific source (either the Runner's starting memory limit or an ability providing additional {MU}). The total {MU} assigned to each source must not exceed the amount of {MU} it provides, and all programs with {MU} assigned to a given source must meet all restrictions imposed on that {MU}. If it is not possible to create such a mapping, the Runner is considered to have exceeded their memory limit, and they must trash installed programs as described in section 3.9.3.



## 1.21. Card Visibility

**1.21.1.** Cards in the play area and other public zones are either faceup or facedown. Faceup cards are freely visible to all players. Facedown cards are oriented so that the face containing the card's information is not visible.


- a. Cards in hidden or secret zones (see section 4.1) are neither faceup nor facedown.

- b. Some effects host multiple cards on an object at the same time or set aside multiple cards at the same time. Facedown cards that enter a zone at the same time by the same effect are treated as a group. See section 1.13.7 and rule 4.8.7, respectively.


**1.21.2.** When a player looks at a card, they are allowed to see the front face of the card even if the front face would not normally be visible to them. A player should look at that card without showing it to the other player if it is not normally visible to that player.


- a. A player may look at facedown cards they control at any time.


**1.21.3.** To reveal a card is to show its front face to all players, then return it to its previous state.


- a. Revealing a facedown card does not count as turning it faceup.


**1.21.4.** Some abilities instruct the Runner to expose one or more cards. To expose a card is to reveal it, except that only installed, unrezzed cards can be exposed.

**1.21.5.** Effects that instruct a player to look at, reveal, expose, or access a card are not the same, even if they would be performed similarly at times. See section 7 for more information on accessing.

**1.21.6.** If a resolving ability directs one or both players to look at or reveal a card or set of cards, each such card remains visible to the relevant player(s) until the entire ability is finished resolving or the card moves to a different location. See also rule 7.3.1a.

**1.21.7.** The ability "While the Runner is accessing this [type] in R&D, they must reveal it." is a static ability that applies for the entire duration of the access. This is also the case for cards with the older wording, "If [this card] is accessed from R&D, the Runner must reveal it."
