using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;
using Renegade.Core;

namespace Spades.Core;

/// <summary>
/// A computer Spades player. Bidding counts likely tricks: aces, guarded kings, long and high
/// spades; a very weak hand bids nil (from Medium up). In play, Easy picks any legal card.
/// Medium plays as a partner should: it doesn't overtake a partner who's winning, wins as
/// cheaply as it can while the side still needs tricks, ducks once the bid is made (no bags),
/// guards its own nil and covers a partner's. Hard looks ahead, playing each choice through over
/// many guessed deals with Medium's style and scoring the hand for its side.
/// </summary>
public sealed class SpadesAi
{
	private readonly Random _random;

	public SpadesAi(int seat, Difficulty difficulty, Random random)
	{
		Seat = seat;
		Difficulty = difficulty;
		_random = random;
	}

	public int Seat { get; }
	public Difficulty Difficulty { get; }

	public int ChooseBid(SpadesHand hand)
	{
		var mine = hand.Hand(Seat);
		double tricks = 0;
		foreach (var suit in mine.GroupBy(hand.SuitOf))
		{
			var cards = suit.OrderByDescending(hand.Power).ToList();
			int length = cards.Count;
			if (suit.Key == Suit.Spades)
			{
				// A top spade is a trick; the next one down nearly so if guarded, the third if guarded twice.
				// (With house rules the ace may be only the fifth best spade.)
				var all = Deck.Standard().Concat(new[] { Card.Joker(true), Card.Joker(false) }).Where(c => hand.SuitOf(c) == Suit.Spades && (!c.IsJoker || hand.High == HighCards.Jokers)).ToList();
				if (hand.High == HighCards.Jokers)
					all.RemoveAll(c => c == new Card(2, Suit.Clubs) || c == new Card(2, Suit.Hearts));
				foreach (var c in cards)
				{
					int higher = all.Count(o => hand.Power(o) > hand.Power(c) && !mine.Contains(o));
					tricks += higher switch { 0 => 1, 1 => length >= 2 ? 0.85 : 0.3, 2 => length >= 3 ? 0.6 : 0.15, _ => 0 };
				}
				// Long trumps win tricks too, past a fair share of the spades.
				int share = (int)Math.Round(all.Count / 4.0);
				tricks += Math.Max(0, length - share) * 0.8;
			}
			else
			{
				if (cards.Any(c => c.Rank == Card.Ace))
					tricks += length <= 5 ? 1 : 0.7;
				if (cards.Any(c => c.Rank == Card.King) && length >= 2)
					tricks += length <= 4 ? 0.8 : 0.5;
				if (cards.Any(c => c.Rank == Card.Queen) && length >= 3 && length <= 4)
					tricks += 0.3;
			}
		}
		// Short side suits let spades ruff.
		int spades = mine.Count(c => hand.SuitOf(c) == Suit.Spades);
		foreach (Suit s in new[] { Suit.Hearts, Suit.Clubs, Suit.Diamonds })
		{
			int n = mine.Count(c => hand.SuitOf(c) == s);
			if (n <= 1 && spades >= 3)
				tricks += n == 0 ? 0.8 : 0.4;
		}
		if (Difficulty != Difficulty.Easy && tricks < 1.0 && spades <= 3 && mine.Where(c => hand.SuitOf(c) == Suit.Spades).All(c => hand.Power(c) is >= 2 and <= 9)
			&& !mine.Any(c => c.Rank == Card.Ace) && hand.Bid(SpadesHand.Partner(Seat)) != 0)
			return 0;
		int bid = (int)Math.Round(tricks);
		if (Difficulty == Difficulty.Easy)
			bid += _random.Next(-1, 2);
		// A side can't take more than thirteen.
		int partnerBid = hand.Bid(SpadesHand.Partner(Seat)) ?? 0;
		return Math.Clamp(bid, 1, Math.Max(1, 13 - partnerBid));
	}

	public Card ChoosePlay(SpadesHand hand)
	{
		var legal = hand.Legal();
		if (legal.Count == 1)
			return legal[0];
		return Difficulty switch
		{
			Difficulty.Easy => legal[_random.Next(legal.Count)],
			Difficulty.Medium => Heuristic(hand, hand.Current, legal),
			_ => LookAhead(hand, legal),
		};
	}

	public static Card Heuristic(SpadesHand hand, int seat, IReadOnlyList<Card> legal)
	{
		var trick = hand.Trick;
		int team = SpadesHand.Team(seat);
		int partner = SpadesHand.Partner(seat);
		bool myNil = hand.Bid(seat) == 0;
		bool partnerNil = hand.Bid(partner) == 0;
		bool needTricks = hand.TeamTricks(team) < hand.TeamBid(team);
		bool Spade(Card c) => hand.SuitOf(c) == Suit.Spades;
		Card Lowest(IEnumerable<Card> cards) => cards.OrderBy(c => Spade(c) ? 1 : 0).ThenBy(hand.Power).First();
		Card Highest(IEnumerable<Card> cards) => cards.OrderByDescending(c => Spade(c) ? 1 : 0).ThenByDescending(hand.Power).First();

		if (trick.Count == 0)
		{
			if (myNil)
				return Lowest(legal);
			// Lead a winner (an ace) while we need tricks; otherwise lead low.
			var aces = legal.Where(c => c.Rank == Card.Ace && !Spade(c)).ToList();
			if (needTricks && aces.Count > 0)
				return aces[0];
			return Lowest(legal);
		}
		var winning = hand.Winner(trick);
		bool partnerWinning = winning.Seat == partner;
		var winners = legal.Where(c => hand.Winner(trick.Append(new Play(seat, c)).ToList()).Seat == seat).ToList();
		var losers = legal.Except(winners).ToList();

		if (myNil)
			return losers.Count > 0 ? Highest(losers) : Lowest(legal);
		if (partnerNil && winning.Seat == partner && winners.Count > 0)
			return winners.OrderBy(hand.Power).First(); // cover the partner's nil
		if (partnerWinning && (!partnerNil))
			return losers.Count > 0 ? Lowest(losers) : Lowest(legal);
		if (needTricks && winners.Count > 0)
			return winners.OrderBy(c => Spade(c) ? 1 : 0).ThenBy(hand.Power).First();
		return losers.Count > 0 ? Highest(losers.Where(c => !Spade(c)).DefaultIfEmpty(losers[0])) : Lowest(legal);
	}

	private Card LookAhead(SpadesHand hand, IReadOnlyList<Card> legal)
	{
		const int samples = 24;
		int team = SpadesHand.Team(Seat);
		var value = legal.ToDictionary(c => c, _ => 0.0);
		for (int n = 0; n < samples; n++)
		{
			var world = hand.Guess(Seat, _random);
			foreach (var card in legal)
			{
				var sim = world.Clone();
				sim.PlayCard(card);
				while (sim.Phase == SpadesPhase.Playing)
					sim.PlayCard(Heuristic(sim, sim.Current, sim.Legal()));
				var us = sim.Score(team);
				var them = sim.Score(1 - team);
				// Our points less theirs, with bags counted as the cost they'll become.
				value[card] += us.Points - them.Points - 8 * us.Bags + 4 * them.Bags;
			}
		}
		return value.OrderByDescending(kv => kv.Value).ThenBy(kv => hand.Power(kv.Key)).First().Key;
	}
}
