using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Cards;
using Renegade.Core;
using Renegade.Play;
using Renegade.Ui;
using Spades.Core;

namespace Spades.Game;

/// <summary>
/// Partnership Spades: you and Max (across) against Rosie and Lena, a game to 500. Bid how many
/// tricks you'll take (nil for none) with the − and + buttons, then play: tap a card (greyed
/// cards can't be played). Spades are trump; each trick's winner sweeps it to their pile.
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private SpadesGame _game = new();
	private SpadesHand? _hand;
	private readonly SpadesAi[] _ais = new SpadesAi[4];
	private readonly List<Card3D> _trick = new();
	private Control _bidBox = null!;
	private Label _bidLabel = null!;
	private int _bid = 3;

	protected override string Title => "Spades";
	protected override string Hint => "Tap a card to play it";
	protected override int MinPlayers => 4;
	protected override int MaxPlayers => 4;
	protected override bool SortYourHand => false;
	protected override string NewGameText => _game.IsOver ? "New game" : "Next hand";
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => !_game.IsOver && (_game.Hands > 0 || _hand is { Phase: not SpadesPhase.Over });

	protected override void LevelChanged(Difficulty difficulty) => MakeAis(difficulty);

	private void MakeAis(Difficulty difficulty)
	{
		for (int s = 0; s < 4; s++)
			_ais[s] = new SpadesAi(s, s == 0 ? Difficulty.Medium : difficulty, new Random(Random.Next()));
	}

	protected override void AddSettings()
	{
		// The bid picker: − [3] + and Bid.
		_bidBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_bidBox.AddThemeConstantOverride("separation", 14);
		var minus = Widgets.Button("−", 34, Hud.Theme.ButtonDark, Hud.Theme);
		var plus = Widgets.Button("+", 34, Hud.Theme.ButtonDark, Hud.Theme);
		minus.CustomMinimumSize = plus.CustomMinimumSize = new Vector2(84, 64);
		_bidLabel = Widgets.Label("3", 34, Hud.Theme.Text);
		_bidLabel.CustomMinimumSize = new Vector2(110, 64);
		var bid = Widgets.Button("Bid", 28, Hud.Theme.Accent, Hud.Theme);
		bid.CustomMinimumSize = new Vector2(150, 64);
		minus.Pressed += () => SetBid(_bid - 1);
		plus.Pressed += () => SetBid(_bid + 1);
		bid.Pressed += () =>
		{
			if (!Busy && _hand is { Phase: SpadesPhase.Bidding, Current: 0 })
				Run(PlaceBid(0, _bid));
		};
		_bidBox.AddChild(minus);
		_bidBox.AddChild(_bidLabel);
		_bidBox.AddChild(plus);
		_bidBox.AddChild(bid);
		Widgets.Anchor(_bidBox, Control.LayoutPreset.CenterBottom, left: -300, right: 300, top: -192, bottom: -128);
		_bidBox.Visible = false;
		Hud.AddChild(_bidBox);
	}

	private void SetBid(int bid)
	{
		_bid = Math.Clamp(bid, 0, 13);
		_bidLabel.Text = _bid == 0 ? "Nil" : _bid.ToString();
	}

	protected override async Task NewRound()
	{
		int round = Round;
		_bidBox.Visible = false;
		if (_game.IsOver)
			_game = new SpadesGame();
		MakeAis(Settings.Difficulty);
		_nodes.Clear();
		_trick.Clear();
		_hand = null;
		SetScore();
		var order = Deck.Shuffled(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		var hand = new SpadesHand(order, _game.NextDealer);
		_hand = hand;
		await DealRound(nodes, Enumerable.Range(0, 52).Select(i => (hand.Dealer + 1 + i) % 4).ToList(), spacing: 0.045f);
		if (round != Round)
			return;
		await PickUpAll(s => hand.Hand(s).Select(c => _nodes[c]));
		ArrangeBySuit();
		Hud.SetRestartVisible(true);
		await NextBid();
	}

	private async Task NextBid()
	{
		var hand = _hand!;
		int round = Round;
		SetScore();
		if (hand.Phase != SpadesPhase.Bidding)
		{
			SayNone();
			await NextTurn();
			return;
		}
		if (hand.Current == 0 && !Demo)
		{
			// Start the picker at what a sensible player would bid.
			SetBid(new SpadesAi(0, Difficulty.Medium, Random).ChooseBid(hand));
			_bidBox.Visible = true;
			Busy = false;
			int partner = hand.Bid(2) is { } b ? b : -1;
			Hud.SetStatus(partner >= 0 ? $"Your bid. Max bid {(partner == 0 ? "nil" : partner.ToString())}." : "Your bid: how many tricks will you take?", Bright);
			return;
		}
		Busy = true;
		await Pause(0.7f);
		if (round != Round)
			return;
		await PlaceBid(hand.Current, _ais[hand.Current].ChooseBid(hand));
	}

	private async Task PlaceBid(int seat, int bid)
	{
		var hand = _hand!;
		Busy = true;
		_bidBox.Visible = false;
		hand.PlaceBid(bid);
		Say(seat, bid == 0 ? "Nil!" : $"{bid}");
		Hud.SetStatus(seat == 0 ? $"You bid {(bid == 0 ? "nil" : bid.ToString())}." : $"{Names[seat]} bids {(bid == 0 ? "nil" : bid.ToString())}.", Muted);
		await Pause(0.6f);
		await NextBid();
	}

	private async Task NextTurn()
	{
		var hand = _hand!;
		int round = Round;
		SetScore();
		if (hand.Phase == SpadesPhase.Over)
		{
			EndHand();
			return;
		}
		int seat = hand.Current;
		if (seat == 0 && !Demo)
		{
			DimUnplayable(hand.Legal());
			Busy = false;
			Hud.SetStatus(hand.Trick.Count == 0 ? "Your lead: tap a card." : "Your turn: tap a card to play.", Bright);
			return;
		}
		Busy = true;
		var copy = hand.Clone();
		var ai = _ais[seat];
		var think = Task.Run(() => ai.ChoosePlay(copy));
		await Pause(0.45f);
		var card = await think;
		if (round != Round)
			return;
		await Play(seat, card);
	}

	private async Task Play(int seat, Card card)
	{
		var hand = _hand!;
		Busy = true;
		DimUnplayable(null);
		bool breaks = card.Suit == Suit.Spades && !hand.SpadesBroken;
		var won = hand.PlayCard(card);
		var node = _nodes[card];
		_trick.Add(node);
		await PlayToTrick(seat, node);
		if (seat == 0)
			ArrangeBySuit();
		if (breaks)
			Hud.SetStatus("Spades are broken!", Bright);
		if (won != null)
		{
			await Pause(0.65f);
			bool ours = SpadesHand.Team(won.Winner) == 0;
			Hud.SetStatus(won.Winner == 0 ? "You take the trick." : $"{Names[won.Winner]} takes the trick.", ours ? Bright : Muted);
			await SweepTrick(won.Winner, _trick.ToList(), hand.TricksWon(won.Winner) - 1);
			_trick.Clear();
		}
		await NextTurn();
	}

	private void EndHand()
	{
		var hand = _hand!;
		var (us, them) = _game.Record(hand);
		SetScore();
		string Side(TeamScore s, string who) =>
			s.Bid == 0 ? "" : s.Made ? $"{who} made {s.Bid} ({s.Tricks} tricks): {s.Points:+#;-#;0}." : $"{who} set! {s.Tricks} of {s.Bid}: {s.Points:+#;-#;0}.";
		string nils = string.Join(" ", us.Nils.Concat(them.Nils).Select(n => $"{(n.Seat == 0 ? "Your" : Names[n.Seat] + "'s")} nil {(n.Made ? "made" : "failed")}."));
		string summary = $"{Side(us, "You and Max")} {Side(them, "Rosie and Lena")} {nils}".Trim();
		if (_game.IsOver)
		{
			int w = _game.Winner;
			Sparkle.Burst(this, Table.Winnings[w == 0 ? 0 : 1] + Vector3.Up * 0.3f);
			Finish($"{summary} {(w == 0 ? "You and Max win" : "Rosie and Lena win")}, {_game.Scores[w]} to {_game.Scores[1 - w]}!",
				w == 0 ? 0 : 1, null);
		}
		else
		{
			Finish(summary, -1);
		}
	}

	private void SetScore()
	{
		var hand = _hand;
		string tricks = hand is { Phase: SpadesPhase.Playing or SpadesPhase.Over } ? $" · Tricks {hand.TeamTricks(0)}/{hand.TeamBid(0)}–{hand.TeamTricks(1)}/{hand.TeamBid(1)}" : "";
		Hud.SetScore($"Us {_game.Scores[0]} · Them {_game.Scores[1]} · Bags {_game.Bags[0]}–{_game.Bags[1]}{tricks}");
	}

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_hand is not { Phase: SpadesPhase.Playing, Current: 0 } hand || Fans[0].At(origin, direction) is not { } card)
			return;
		if (!hand.Legal().Contains(card.Card))
		{
			Hud.SetStatus(hand.Trick.Count > 0 ? "You must follow suit." : "Spades aren't broken yet: lead another suit.", Bright);
			return;
		}
		Hud.HideHint();
		Run(Play(0, card.Card));
	}
}
