# Spades: design

A Renegade card game on the card kit ([renegade-cards](https://github.com/darren-rawlings/renegade-cards)).

## The game
Partnership Spades: you and Max (across) against Rosie and Lena, a game to 500. Thirteen cards
each; everyone bids tricks (nil for none), starting left of the dealer, who leads. Follow suit;
spades are always trump and can't be led until broken. Making the side's bid scores 10 a trick
plus 1 a bag (overtrick); failing loses 10 a trick. Nil: +100 if made, −100 if not (its tricks
count as bags). Every 10 bags: −100. Rules in `src/Core` (tested, including whole games).

## Feel
The bid picker (− [n] +, Bid) starts at a sensible bid for your hand; bids show over each
player. Cards you can't play are greyed; tricks are swept to the winner's pile.

## Computer levels
Easy (rough bids, any legal card), Medium (counts tricks, bids nil on weak hands, plays as a
partner: doesn't overtake, ducks once the bid is made, guards nils), Hard (looks ahead over many
guessed deals; runs on a background thread).

## Scope for version 1
Everything above. Later: blind nil, tutorial clips, choosing your partner's seat.
