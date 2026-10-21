using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Synthetic, already-filtered presentation data for editor previews.
/// Samples never construct a coordinator, resolve hidden metadata or write a
/// trace. Runtime callers must supply an actual MatchViewModel.
/// </summary>
public static class UiPreviewSamples
{
	public enum Density { Empty, Normal, Dense }

	public static CardViewModel Known(string id, string type = "Event", string? title = null)
		=> new(id, null, title ?? "Sample " + type, type, true, type is "ICE" or "Asset", 0,
			3, type == "ICE" ? 4 : null, type == "Agenda" ? 2 : null, type == "Asset" ? 3 : null,
			[], "Synthetic preview text. Gain 2 credits. A long sentence tests wrapping when the card or panel is made narrower.",
			type == "ICE" ? "Weyland" : "Shaper", type == "Program" ? 1 : null);

	public static MatchViewModel Board(PlayerSide seat, Density density)
	{
		bool empty = density == Density.Empty;
		bool dense = density == Density.Dense;
		int handCount = empty ? 0 : dense ? 12 : 5;
		CardViewModel[] hand = Enumerable.Range(0, handCount)
			.Select(i => Known($"preview-hand-{i}", seat == PlayerSide.Corp ? "Operation" : "Event", $"Sample hand card {i + 1}")).ToArray();
		var servers = new List<ServerViewModel>();
		foreach (ServerKind kind in new[] { ServerKind.Archives, ServerKind.RAndD, ServerKind.Headquarters })
			servers.Add(new($"preview-{kind}", kind, empty ? [] : [Known($"preview-ice-{kind}", "ICE"), null], []));
		for (int i = 0; i < (empty ? 0 : dense ? 5 : 2); i++)
			servers.Add(new($"preview-remote-{i}", ServerKind.Remote,
				Enumerable.Range(0, dense ? 5 : 2).Select(j => j == 1 ? null : Known($"preview-ice-{i}-{j}", "ICE")).ToArray(),
				[i == 0 ? Known($"preview-root-{i}", "Asset") with { AdvancementCounters = 2 } : null]));
		CardViewModel[] rig = Enumerable.Range(0, empty ? 0 : dense ? 12 : 3)
			.Select(i => Known($"preview-rig-{i}", "Program")).ToArray();
		if (dense) rig[0] = rig[0] with { HostCardId = "preview-ice-0-0" };
		return new(seat,
			new("preview-corp", PlayerSide.Corp, 12, 3, 2),
			new("preview-runner", PlayerSide.Runner, 9, 4, 2) { Tags = 1, UsedMemory = 3, MemoryLimit = 4 },
			servers,
			new(ZoneKind.CorpHand, seat == PlayerSide.Corp ? handCount : 5, seat == PlayerSide.Corp ? hand : []),
			new(ZoneKind.CorpDeck, 21, []),
			new(ZoneKind.RunnerGrip, seat == PlayerSide.Runner ? handCount : 5, seat == PlayerSide.Runner ? hand : []),
			new(ZoneKind.RunnerStack, 24, []), rig,
			empty ? [] : [Known("preview-corp-score", "Agenda")],
			empty ? [] : [Known("preview-runner-score", "Agenda")],
			null, null, new(GameOutcomeKind.Ongoing, null, null), [])
		{
			CorpIdentity = Known("preview-corp-id", "Identity", "Sample Corporation"),
			RunnerIdentity = Known("preview-runner-id", "Identity", "Sample Runner"),
			CorpDiscard = empty ? [] : [Known("preview-archives", "Operation")],
			RunnerHeap = empty ? [] : [Known("preview-heap", "Event")],
			Turn = new(true, seat, seat == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner,
				seat == PlayerSide.Corp ? TurnStep.CorpAction : TurnStep.RunnerAction, 3, null),
			CorpHandSize = new(PlayerSide.Corp, seat == PlayerSide.Corp ? handCount : 5, 5, null),
			RunnerHandSize = new(PlayerSide.Runner, seat == PlayerSide.Runner ? handCount : 5, 5, null),
		};
	}
}
