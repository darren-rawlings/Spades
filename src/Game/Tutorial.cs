using System.Collections.Generic;
using Renegade.Ui;

namespace Spades.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Partners",
			"You play with Max, across the table, against Rosie and Lena. Your side tries to take at least as many tricks as you both bid."),
		new TutorialPage("Bid",
			"Use − and + to choose how many tricks you think you'll take (the picker starts at a sensible bid), then press Bid. Nil means you'll take none: 100 points if you manage it, minus 100 if not."),
		new TutorialPage("Play",
			"Follow suit if you can. Spades are trump: any spade beats any other suit. Spades can't be led until one has been played. Settings › High cards adds house rules: the deuce of spades above the ace, the two of diamonds above that (it counts as a spade), or two jokers on top."),
		new TutorialPage("Scoring",
			"Make your bid for ten points a trick, plus one for each extra trick (a bag). Fall short and you lose ten a trick. Every ten bags costs 100 (Settings can turn that off), so don't take too many! First side to 500 wins (or the score chosen in Settings). Every trick is listed in the log at the top."),
	};
}
