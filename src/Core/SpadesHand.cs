using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace Spades.Core;

public enum SpadesPhase { Bidding, Playing, Over }

/// <summary>
/// Which cards rank above the ace of spades (house rules): none; the two of spades ("deuce high");
/// the two of diamonds, then the two of spades ("big deuce", the two of diamonds counting as a
/// spade); or two jokers above those (the two of clubs and two of hearts leave the deck, the big
/// and little jokers come in; big joker, little joker, two of diamonds, two of spades, then the
/// ace, all of them spades).
/// </summary>
public enum HighCards { Standard, DeuceHigh, BigDeuce, Jokers }

/// <summary>A trick that's been won: who took it and the cards.</summary>
public sealed record TrickWon(int Winner, IReadOnlyList<Play> Cards);

/// <summary>What a partnership scored in one hand.</summary>
public sealed record TeamScore(int Bid, int Tricks, int Points, int Bags, bool Made, IReadOnlyList<(int Seat, bool Made)> Nils);

/// <summary>
/// One hand of partnership Spades (the usual rules): seats 0 and 2 against 1 and 3, thirteen cards
/// each. Starting left of the dealer, everyone bids how many tricks they'll take (0 is nil). The
/// player left of the dealer leads. Follow suit if you can; spades are always trump, and can't be
/// led until one has been played (unless you hold only spades). A side making its bid scores ten
/// a trick bid, plus one a trick over (a "bag"); falling short loses ten a trick bid. Nil scores
/// 100 if that player takes no tricks, and loses 100 if they take any (their tricks then count
/// as bags, not towards the partner's bid).
/// </summary>
public sealed class SpadesHand
{
	public const int Seats = 4;
	private readonly List<Card>[] _hands;
	private readonly int?[] _bids = new int?[Seats];
	private readonly List<Play> _trick = new();
	private readonly List<Card> _played = new();
	private readonly int[] _tricks = new int[Seats];
	private readonly HashSet<Suit>[] _void = Enumerable.Range(0, Seats).Select(_ => new HashSet<Suit>()).ToArray();

	public SpadesHand(IReadOnlyList<Card> deck, int dealer, HighCards high = HighCards.Standard)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("Spades needs a full deck.", nameof(deck));
		Dealer = dealer;
		High = high;
		_hands = Tricks.Deal(deck, Seats, first: (dealer + 1) % Seats);
		Current = (dealer + 1) % Seats;
	}

	private SpadesHand(SpadesHand other, List<Card>[] hands)
	{
		Dealer = other.Dealer;
		High = other.High;
		_hands = hands;
		Phase = other.Phase;
		Current = other.Current;
		SpadesBroken = other.SpadesBroken;
		TricksPlayed = other.TricksPlayed;
		other._bids.CopyTo(_bids, 0);
		_trick.AddRange(other._trick);
		_played.AddRange(other._played);
		other._tricks.CopyTo(_tricks, 0);
		for (int s = 0; s < Seats; s++)
			_void[s].UnionWith(other._void[s]);
	}

	public int Dealer { get; }
	public HighCards High { get; }
	public SpadesPhase Phase { get; private set; } = SpadesPhase.Bidding;
	public int Current { get; private set; }
	public bool SpadesBroken { get; private set; }
	public int TricksPlayed { get; private set; }
	public IReadOnlyList<Card> Hand(int seat) => _hands[seat];
	public IReadOnlyList<Play> Trick => _trick;
	public IReadOnlyList<Card> Played => _played;
	public int TricksWon(int seat) => _tricks[seat];
	public int? Bid(int seat) => _bids[seat];
	public IReadOnlySet<Suit> VoidIn(int seat) => _void[seat];

	public static int Partner(int seat) => (seat + 2) % Seats;
	public static int Team(int seat) => seat % 2;

	/// <summary>A side's combined bid (nil bids add nothing).</summary>
	public int TeamBid(int team) => (_bids[team] ?? 0) + (_bids[team + 2] ?? 0);

	/// <summary>Tricks a side has taken towards its bid (a nil bidder's tricks don't count).</summary>
	public int TeamTricks(int team) => Enumerable.Range(0, Seats).Where(s => Team(s) == team && _bids[s] != 0).Sum(s => _tricks[s]);

	public SpadesHand Clone() => new(this, _hands.Select(h => h.ToList()).ToArray());

	public void PlaceBid(int bid)
	{
		if (Phase != SpadesPhase.Bidding)
			throw new InvalidOperationException("Not bidding now.");
		if (bid is < 0 or > 13)
			throw new ArgumentOutOfRangeException(nameof(bid));
		_bids[Current] = bid;
		Current = (Current + 1) % Seats;
		if (_bids.All(b => b != null))
		{
			Phase = SpadesPhase.Playing;
			Current = (Dealer + 1) % Seats;
		}
	}

	/// <summary>The suit a card plays as: jokers, and the two of diamonds under the big-deuce rules, are spades.</summary>
	public Suit SuitOf(Card card) => card.IsJoker || (High is HighCards.BigDeuce or HighCards.Jokers && card == Two(Suit.Diamonds)) ? Suit.Spades : card.Suit;

	/// <summary>How strong a card is within its suit: two low, ace 14, then any house-rule cards above the ace of spades.</summary>
	public int Power(Card card)
	{
		if (card.Rank == Card.BigJoker)
			return 18;
		if (card.Rank == Card.LittleJoker)
			return 17;
		if (High is HighCards.BigDeuce or HighCards.Jokers && card == Two(Suit.Diamonds))
			return 16;
		if (High != HighCards.Standard && card == Two(Suit.Spades))
			return 15;
		return Tricks.Value(card);
	}

	/// <summary>Who wins a trick: the strongest spade played, or else the strongest card of the suit led.</summary>
	public Play Winner(IReadOnlyList<Play> trick)
	{
		var best = trick[0];
		foreach (var play in trick.Skip(1))
		{
			bool beats = SuitOf(play.Card) == SuitOf(best.Card) ? Power(play.Card) > Power(best.Card) : SuitOf(play.Card) == Suit.Spades;
			if (beats)
				best = play;
		}
		return best;
	}

	private static Card Two(Suit suit) => new(2, suit);

	/// <summary>The cards for a deal under these rules, shuffled: the jokers rules swap the two of clubs and the two of hearts for the jokers.</summary>
	public static List<Card> Pack(HighCards high, Random random)
	{
		var cards = Deck.Standard();
		if (high == HighCards.Jokers)
		{
			cards.Remove(Two(Suit.Clubs));
			cards.Remove(Two(Suit.Hearts));
			cards.Add(Card.Joker(big: true));
			cards.Add(Card.Joker(big: false));
		}
		Deck.Shuffle(cards, random);
		return cards;
	}

	public List<Card> Legal() => Legal(Current);

	public List<Card> Legal(int seat)
	{
		var hand = _hands[seat];
		if (_trick.Count == 0)
		{
			if (!SpadesBroken && hand.Any(c => SuitOf(c) != Suit.Spades))
				return hand.Where(c => SuitOf(c) != Suit.Spades).ToList();
			return hand.ToList();
		}
		var led = SuitOf(_trick[0].Card);
		return hand.Any(c => SuitOf(c) == led) ? hand.Where(c => SuitOf(c) == led).ToList() : hand.ToList();
	}

	public TrickWon? PlayCard(Card card)
	{
		if (Phase != SpadesPhase.Playing)
			throw new InvalidOperationException("Not playing now.");
		if (!Legal().Contains(card))
			throw new InvalidOperationException($"You can't play {card} now.");
		if (_trick.Count > 0 && SuitOf(card) != SuitOf(_trick[0].Card))
			_void[Current].Add(SuitOf(_trick[0].Card));
		_hands[Current].Remove(card);
		_trick.Add(new Play(Current, card));
		_played.Add(card);
		if (SuitOf(card) == Suit.Spades)
			SpadesBroken = true;
		if (_trick.Count < Seats)
		{
			Current = (Current + 1) % Seats;
			return null;
		}
		int winner = Winner(_trick).Seat;
		var won = new TrickWon(winner, _trick.ToList());
		_tricks[winner]++;
		_trick.Clear();
		TricksPlayed++;
		Current = winner;
		if (TricksPlayed == 13)
			Phase = SpadesPhase.Over;
		return won;
	}

	/// <summary>What a side scored this hand (when it's over).</summary>
	public TeamScore Score(int team)
	{
		int bid = TeamBid(team), tricks = TeamTricks(team);
		var nils = new List<(int, bool)>();
		int points = 0, bags = 0;
		foreach (int s in new[] { team, team + 2 })
			if (_bids[s] == 0)
			{
				bool made = _tricks[s] == 0;
				nils.Add((s, made));
				points += made ? 100 : -100;
				bags += _tricks[s];
			}
		bool madeBid = tricks >= bid;
		if (bid > 0)
		{
			if (madeBid)
			{
				points += 10 * bid + (tricks - bid);
				bags += tricks - bid;
			}
			else
			{
				points -= 10 * bid;
			}
		}
		return new TeamScore(bid, tricks, points, bags, madeBid, nils);
	}

	/// <summary>A copy with the unseen cards dealt out again among the others, keeping their known voids.</summary>
	public SpadesHand Guess(int seat, Random random)
	{
		var hidden = Enumerable.Range(0, Seats).Where(s => s != seat).SelectMany(s => _hands[s]).ToList();
		for (int attempt = 0; attempt < 40; attempt++)
		{
			Deck.Shuffle(hidden, random);
			var hands = new List<Card>[Seats];
			hands[seat] = _hands[seat].ToList();
			var pool = hidden.ToList();
			bool ok = true;
			foreach (int s in Enumerable.Range(0, Seats).Where(s => s != seat).OrderByDescending(s => _void[s].Count))
			{
				var fits = pool.Where(c => !_void[s].Contains(SuitOf(c))).Take(_hands[s].Count).ToList();
				if (fits.Count < _hands[s].Count)
				{
					ok = false;
					break;
				}
				hands[s] = fits;
				foreach (var c in fits)
					pool.Remove(c);
			}
			if (ok)
				return new SpadesHand(this, hands);
		}
		return Clone();
	}
}

/// <summary>
/// A game of Spades to 500 (or another target): every ten bags a side collects costs it 100,
/// unless that rule is turned off.
/// </summary>
public sealed class SpadesGame
{
	public const int BagLimit = 10, BagPenalty = 100;

	public SpadesGame(int target = 500, bool bagPenalty = true)
	{
		Target = target;
		BagRule = bagPenalty;
	}

	public int Target { get; }

	/// <summary>Whether ten bags cost a hundred points.</summary>
	public bool BagRule { get; }

	public int[] Scores { get; } = new int[2];
	public int[] Bags { get; } = new int[2];
	public int Hands { get; private set; }
	public int NextDealer => Hands % SpadesHand.Seats;

	public bool IsOver => Scores.Max() >= Target && Scores[0] != Scores[1];
	public int Winner => !IsOver ? -1 : Scores[0] > Scores[1] ? 0 : 1;

	public (TeamScore Us, TeamScore Them) Record(SpadesHand hand)
	{
		var result = (hand.Score(0), hand.Score(1));
		foreach (var (team, score) in new[] { (0, result.Item1), (1, result.Item2) })
		{
			Scores[team] += score.Points;
			Bags[team] += score.Bags;
			while (BagRule && Bags[team] >= BagLimit)
			{
				Bags[team] -= BagLimit;
				Scores[team] -= BagPenalty;
			}
		}
		Hands++;
		return result;
	}
}
