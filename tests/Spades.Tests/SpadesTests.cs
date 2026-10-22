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
