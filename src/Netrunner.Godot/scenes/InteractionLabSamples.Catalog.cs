using Netrunner.Core;

namespace Netrunner.GodotClient;

public static partial class InteractionLabSamples
{
	public sealed record Scenario(int Id, PlayerSide Side, string Name);

	// Picker order is independent of fixture IDs. Duplicate gestures and layout
	// stress fixtures remain available to the smoke suite, not the practice menu.
	public static IReadOnlyList<Scenario> Scenarios { get; } = [
		new(15, PlayerSide.Runner, "Hand & board basics"),
		new(17, PlayerSide.Runner, "Discard from hand"),
		new(2, PlayerSide.Runner, "Install a program"),
		new(9, PlayerSide.Runner, "Install with memory cleanup"),
		new(3, PlayerSide.Runner, "Install on ICE"),
		new(1, PlayerSide.Runner, "Play a run event"),
		new(19, PlayerSide.Runner, "Boost & break with an icebreaker"),
		new(25, PlayerSide.Runner, "Break a bioroid with clicks"),
		new(22, PlayerSide.Runner, "Choose a card to trash for ICE"),
		new(23, PlayerSide.Runner, "Choose a breach target"),
		new(24, PlayerSide.Runner, "Trash an accessed card"),
		new(21, PlayerSide.Runner, "Pay for a targeted ability"),
		new(11, PlayerSide.Runner, "Choose an event effect"),
		new(14, PlayerSide.Runner, "Choose two targets"),
		new(27, PlayerSide.Runner, "Select any number of cards"),
		new(31, PlayerSide.Runner, "Order two targets"),
		new(28, PlayerSide.Runner, "Search for a card"),
		new(30, PlayerSide.Runner, "Use an ability from Heap"),
		new(16, PlayerSide.Corp, "Hand & board basics"),
		new(18, PlayerSide.Corp, "Discard from hand"),
		new(5, PlayerSide.Corp, "Install in a server or new remote"),
		new(6, PlayerSide.Corp, "Install ICE"),
		new(8, PlayerSide.Corp, "Replace an installed asset"),
		new(20, PlayerSide.Corp, "Rez, advance & score"),
		new(26, PlayerSide.Corp, "Install ICE during a run"),
		new(29, PlayerSide.Corp, "Trash a tagged Runner's resource"),
	];
}
