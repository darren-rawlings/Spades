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

## House rules and options (Darren's notes, 2026-10-22)
- **Play to:** 250, 300 or 500 (the default).
- **High cards:** the ace of spades is highest (standard); the deuce of spades above the ace;
  "big deuce", where the two of diamonds counts as a spade and ranks above the two of spades; or
  jokers, where the two of clubs and the two of hearts leave the deck and the big and little
  jokers come in (big joker, little joker, two of diamonds, two of spades, then the ace, all
  spades). The jokers are drawn by the card kit.
- **Ten bags cost 100:** on by default; it can be turned off.
- Each side has one trick pile: Max collects yours, Rosie (on your left) theirs.
- The score line: each side's total, then this hand's bid and tricks taken.
- Every finished trick is written to the log at the top, card by card, so any hand can be
  checked afterwards (and the rules code is tested to make every player follow suit).
