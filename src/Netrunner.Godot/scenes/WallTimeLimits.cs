using System.Globalization;

namespace Netrunner.GodotClient;

/// <summary>
/// Wall-time limits for smokes and gates. NETRUNNER_TIMEOUT_SCALE (1 to 20,
/// default 1) stretches them on slower machines, so the defaults stay tuned
/// to the reference hardware. Use it only for "give up after" limits, never
/// for durations the game is expected to take or for performance budgets.
/// </summary>
internal static class WallTimeLimits
{
	public const string ScaleVariable = "NETRUNNER_TIMEOUT_SCALE";

	public static double Scale { get; } = ReadScale(Environment.GetEnvironmentVariable(ScaleVariable));

	public static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds * Scale);

	public static int Count(int count) => (int)Math.Ceiling(count * Scale);

	internal static double ReadScale(string? text) =>
		double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value is >= 1 and <= 20
			? value
			: 1;
}
