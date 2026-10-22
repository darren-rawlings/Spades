using System;
using System.Linq;
using Renegade.Cards;
using Renegade.Core;
using Spades.Core;
using Xunit;

namespace Spades.Tests;

public class SpadesTests
{
	private static SpadesAi[] Players(Random random, params Difficulty[] levels) =>
		Enumerable.Range(0, 4).Select(s => new SpadesAi(s, levels[s % levels.Length], random)).ToArray();

	private static SpadesHand PlayOut(SpadesHand hand, SpadesAi[] ais)
	{
		while (hand.Phase == SpadesPhase.Bidding)
			hand.PlaceBid(ais[hand.Current].ChooseBid(hand));
		while (hand.Phase == SpadesPhase.Playing)
			hand.PlayCard(ais[hand.Current].ChoosePlay(hand));
		return hand;
	}

	[Fact]
	public void BiddingGoesRoundFromTheDealersLeftAndThenTheyLead()
	{
		var hand = new SpadesHand(Deck.Shuffled(new Random(1)), dealer: 2);
		Assert.Equal(3, hand.Current);
		for (int i = 0; i < 4; i++)
			hand.PlaceBid(3);
		Assert.Equal(SpadesPhase.Playing, hand.Phase);
		Assert.Equal(3, hand.Current);
		Assert.Equal(6, hand.TeamBid(0));
	}

	[Fact]
	public void SpadesCantBeLedUntilBroken()
	{
		var random = new Random(2);
		for (int seed = 0; seed < 25; seed++)
		{
			var hand = new SpadesHand(Deck.Shuffled(new Random(seed)), seed % 4);
			var ais = Players(random, Difficulty.Medium);
			while (hand.Phase == SpadesPhase.Bidding)
				hand.PlaceBid(ais[hand.Current].ChooseBid(hand));
			while (hand.Phase == SpadesPhase.Playing)
			{
				var mine = hand.Hand(hand.Current);
				if (hand.Trick.Count == 0 && !hand.SpadesBroken && mine.Any(c => c.Suit != Suit.Spades))
					Assert.DoesNotContain(hand.Legal(), c => c.Suit == Suit.Spades);
				hand.PlayCard(ais[hand.Current].ChoosePlay(hand));
			}
			Assert.Equal(13, Enumerable.Range(0, 4).Sum(hand.TricksWon));
		}
	}

	[Fact]
	public void MakingABidScoresTenATrickPlusBagsAndFailingLosesIt()
	{
		var hand = new SpadesHand(Deck.Shuffled(new Random(3)), 0);
		var ais = Players(new Random(3), Difficulty.Medium);
		PlayOut(hand, ais);
		for (int team = 0; team < 2; team++)
		{
			var score = hand.Score(team);
			int nil = score.Nils.Sum(n => n.Made ? 100 : -100);
			int expected = score.Bid == 0 ? 0 : score.Made ? 10 * score.Bid + (score.Tricks - score.Bid) : -10 * score.Bid;
			Assert.Equal(expected + nil, score.Points);
		}
	}

	[Fact]
	public void TenBagsCostAHundred()
	{
		var game = new SpadesGame();
		game.Bags[0] = 9;
		var random = new Random(4);
		int before = game.Scores[0];
		var ais = Players(random, Difficulty.Easy);
		// Play hands until side 0 collects another bag, then the penalty must have been applied.
		for (int n = 0; n < 40 && game.Bags[0] >= 9; n++)
		{
			var hand = PlayOut(new SpadesHand(Deck.Shuffled(random), game.NextDealer), ais);
			var (us, _) = game.Record(hand);
			if (us.Bags > 0)
			{
				Assert.True(game.Bags[0] < 10);
				break;
			}
		}
	}

	[Fact]
	public void ComputersAlwaysFollowSuitWhenTheyCan()
	{
		// As the table does it: each computer chooses on a copy of the hand.
		var random = new Random(9);
		for (int n = 0; n < 60; n++)
		{
			var ais = Players(random, Difficulty.Hard, Difficulty.Medium, Difficulty.Easy, Difficulty.Hard);
			var hand = new SpadesHand(Deck.Shuffled(random), n % 4);
			while (hand.Phase == SpadesPhase.Bidding)
				hand.PlaceBid(ais[hand.Current].ChooseBid(hand.Clone()));
			while (hand.Phase == SpadesPhase.Playing)
			{
				int seat = hand.Current;
				var card = ais[seat].ChoosePlay(hand.Clone());
				if (hand.Trick.Count > 0 && hand.Hand(seat).Any(c => c.Suit == hand.Trick[0].Card.Suit))
					Assert.Equal(hand.Trick[0].Card.Suit, card.Suit);
				hand.PlayCard(card);
			}
		}
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public void WholeGamesReachFiveHundred(int seed)
	{
		var random = new Random(seed);
		var game = new SpadesGame();
		var ais = Players(random, Difficulty.Medium, Difficulty.Easy);
		while (!game.IsOver && game.Hands < 80)
			game.Record(PlayOut(new SpadesHand(Deck.Shuffled(random), game.NextDealer), ais));
		Assert.True(game.IsOver);
	}

	[Fact]
	public void HardOutscoresEasy()
	{
		var random = new Random(8);
		int hard = 0, easy = 0;
		for (int n = 0; n < 16; n++)
		{
			// Side 0 (seats 0 and 2) plays Hard; side 1 Easy.
			var ais = Players(random, Difficulty.Hard, Difficulty.Easy);
			var hand = PlayOut(new SpadesHand(Deck.Shuffled(random), n % 4), ais);
			hard += hand.Score(0).Points;
			easy += hand.Score(1).Points;
		}
		Assert.True(hard > easy, $"Hard {hard}, Easy {easy}");
	}
}

public class HouseRuleTests
{
	private static Card C(int rank, Suit suit) => new(rank, suit);

	private static Play P(int seat, Card card) => new(seat, card);

	private static SpadesHand Hand(HighCards high) => new(SpadesHand.Pack(high, new Random(1)), 0, high);

	[Fact]
	public void DeuceHighPutsTheTwoOfSpadesAboveTheAce()
	{
		var hand = Hand(HighCards.DeuceHigh);
		var trick = new[] { P(0, C(Card.Ace, Suit.Spades)), P(1, C(2, Suit.Spades)), P(2, C(Card.King, Suit.Spades)), P(3, C(3, Suit.Spades)) };
		Assert.Equal(1, hand.Winner(trick).Seat);
		Assert.Equal(0, Hand(HighCards.Standard).Winner(trick).Seat);
	}

	[Fact]
	public void BigDeuceMakesTheTwoOfDiamondsTheTopSpade()
	{
		var hand = Hand(HighCards.BigDeuce);
		Assert.Equal(Suit.Spades, hand.SuitOf(C(2, Suit.Diamonds)));
		var trick = new[] { P(0, C(Card.Ace, Suit.Spades)), P(1, C(2, Suit.Spades)), P(2, C(2, Suit.Diamonds)), P(3, C(Card.King, Suit.Spades)) };
		Assert.Equal(2, hand.Winner(trick).Seat);
		// Diamonds led: the two of diamonds is a spade, so it trumps rather than follows.
		var led = new[] { P(0, C(5, Suit.Diamonds)), P(1, C(2, Suit.Diamonds)) };
		Assert.Equal(1, hand.Winner(led).Seat);
	}

	[Fact]
	public void JokersReplaceTheTwoOfClubsAndTheTwoOfHeartsAndRankHighest()
	{
		var pack = SpadesHand.Pack(HighCards.Jokers, new Random(2));
		Assert.Equal(52, pack.Count);
		Assert.DoesNotContain(C(2, Suit.Clubs), pack);
		Assert.DoesNotContain(C(2, Suit.Hearts), pack);
		Assert.Contains(Card.Joker(big: true), pack);
		Assert.Contains(Card.Joker(big: false), pack);
		var hand = new SpadesHand(pack, 0, HighCards.Jokers);
		var order = new[] { Card.Joker(big: true), Card.Joker(big: false), C(2, Suit.Diamonds), C(2, Suit.Spades), C(Card.Ace, Suit.Spades), C(Card.King, Suit.Spades) };
		for (int i = 1; i < order.Length; i++)
			Assert.True(hand.Power(order[i - 1]) > hand.Power(order[i]));
		Assert.All(order, c => Assert.Equal(Suit.Spades, hand.SuitOf(c)));
	}

	[Theory]
	[InlineData(HighCards.DeuceHigh)]
	[InlineData(HighCards.BigDeuce)]
	[InlineData(HighCards.Jokers)]
	public void WholeHandsPlayOutUnderEachHouseRule(HighCards high)
	{
		var random = new Random(5);
		for (int n = 0; n < 12; n++)
		{
			var ais = Enumerable.Range(0, 4).Select(s => new SpadesAi(s, s % 2 == 0 ? Difficulty.Hard : Difficulty.Medium, random)).ToArray();
			var hand = new SpadesHand(SpadesHand.Pack(high, random), n % 4, high);
			while (hand.Phase == SpadesPhase.Bidding)
				hand.PlaceBid(ais[hand.Current].ChooseBid(hand));
			while (hand.Phase == SpadesPhase.Playing)
			{
				int seat = hand.Current;
				var card = ais[seat].ChoosePlay(hand.Clone());
				if (hand.Trick.Count > 0 && hand.Hand(seat).Any(c => hand.SuitOf(c) == hand.SuitOf(hand.Trick[0].Card)))
					Assert.Equal(hand.SuitOf(hand.Trick[0].Card), hand.SuitOf(card));
				hand.PlayCard(card);
			}
			Assert.Equal(13, Enumerable.Range(0, 4).Sum(hand.TricksWon));
		}
	}

	[Fact]
	public void TheTenBagRuleCanBeTurnedOff()
	{
		var random = new Random(7);
		var game = new SpadesGame(target: 10000, bagPenalty: false);
		var ais = Enumerable.Range(0, 4).Select(s => new SpadesAi(s, Difficulty.Easy, random)).ToArray();
		int expected0 = 0;
		for (int n = 0; n < 15; n++)
		{
			var hand = new SpadesHand(Deck.Shuffled(random), game.NextDealer);
			while (hand.Phase == SpadesPhase.Bidding)
				hand.PlaceBid(ais[hand.Current].ChooseBid(hand));
			while (hand.Phase == SpadesPhase.Playing)
				hand.PlayCard(ais[hand.Current].ChoosePlay(hand));
			var (us, _) = game.Record(hand);
			expected0 += us.Points;
		}
		Assert.Equal(expected0, game.Scores[0]);
	}
}
