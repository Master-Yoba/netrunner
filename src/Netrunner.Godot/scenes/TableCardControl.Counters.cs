using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Counters as shapes (roadmap U5): each kind keeps one shape whatever the
/// faction colour, so the shape carries the meaning. Advancement is an amber
/// diamond, a virus counter an acid-green hexagon, a hosted credit a gold coin,
/// and any other counter a steel disc. The count sits inside the shape.
/// </summary>
public partial class TableCardControl
{
	private enum CounterShape { Diamond, Hexagon, Coin, Disc }

	private static readonly Color CoinColor = new("f4c542");
	private static readonly Color DiscColor = new("9fb4c7");
	private static readonly Color BadgeInk = new(0.04f, 0.05f, 0.06f);

	/// <summary>The count shown in the advancement diamond, or 0 when there is none.</summary>
	public int AdvancementBadgeCount => CurrentBadges().Where(badge => badge.Shape == CounterShape.Diamond).Sum(badge => badge.Count);

	/// <summary>The count shown in the virus hexagon, or 0 when there is none.</summary>
	public int VirusBadgeCount => CurrentBadges().Where(badge => badge.Shape == CounterShape.Hexagon).Sum(badge => badge.Count);

	private List<(CounterShape Shape, int Count, Color Color)> CurrentBadges() => _card is { } card
		? CounterBadges(card, _hiddenAdvancement)
		: _hiddenAdvancement > 0 ? [(CounterShape.Diamond, _hiddenAdvancement, FactionPalette.Advancement)] : [];

	private static List<(CounterShape Shape, int Count, Color Color)> CounterBadges(CardViewModel card, int hiddenAdvancement)
	{
		var badges = new List<(CounterShape, int, Color)>();
		int advancement = card.IsKnown ? card.AdvancementCounters : hiddenAdvancement;
		if (advancement > 0) badges.Add((CounterShape.Diamond, advancement, FactionPalette.Advancement));
		if (!card.IsKnown) return badges;
		foreach (CardCounterViewModel counter in card.TypedCounters.Where(counter => counter.Count > 0))
		{
			badges.Add(counter.Purpose switch
			{
				CardCounterPurpose.Virus => (CounterShape.Hexagon, counter.Count, FactionPalette.Virus),
				CardCounterPurpose.HostedCredit => (CounterShape.Coin, counter.Count, CoinColor),
				_ => (CounterShape.Disc, counter.Count, DiscColor),
			});
		}
		return badges;
	}

	/// <summary>Draws the counter badges down the card's right edge, from its middle.</summary>
	private void DrawCounterBadges(Rect2 cardRect)
	{
		List<(CounterShape Shape, int Count, Color Color)> badges = CurrentBadges();
		if (badges.Count == 0) return;
		float size = IsCompactPortrait || _isIce ? 20 : 24;
		Font? font = TableFonts.MonoMedium ?? ThemeDB.FallbackFont;
		int fontSize = Mathf.RoundToInt(size * 0.55f);
		float top = cardRect.GetCenter().Y - (badges.Count * (size + 3) - 3) / 2;
		for (int index = 0; index < badges.Count; index++)
		{
			(CounterShape shape, int count, Color color) = badges[index];
			Vector2 centre = new(cardRect.End.X - size / 2 - 4, top + index * (size + 3) + size / 2);
			DrawCounterShape(shape, centre, size / 2, color);
			string text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
			Vector2 textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
			Vector2 baseline = centre + new Vector2(-textSize.X / 2, font.GetAscent(fontSize) / 2 - 1);
			DrawString(font, baseline, text, HorizontalAlignment.Left, -1, fontSize, BadgeInk);
		}
	}

	private void DrawCounterShape(CounterShape shape, Vector2 centre, float radius, Color color)
	{
		Vector2[] points = shape switch
		{
			CounterShape.Diamond => [centre + new Vector2(0, -radius), centre + new Vector2(radius, 0), centre + new Vector2(0, radius), centre + new Vector2(-radius, 0)],
			CounterShape.Hexagon => [.. Enumerable.Range(0, 6).Select(corner => centre + Vector2.FromAngle(Mathf.Pi / 3 * corner) * radius)],
			_ => [],
		};
		if (points.Length == 0)
		{
			DrawCircle(centre, radius, color);
			DrawArc(centre, radius, 0, Mathf.Tau, 24, BadgeInk with { A = 0.6f }, 1.5f, true);
			if (shape == CounterShape.Coin) DrawArc(centre, radius * 0.72f, 0, Mathf.Tau, 20, BadgeInk with { A = 0.35f }, 1, true);
			return;
		}
		DrawColoredPolygon(points, color);
		DrawPolyline([.. points, points[0]], BadgeInk with { A = 0.6f }, 1.5f, true);
	}
}
