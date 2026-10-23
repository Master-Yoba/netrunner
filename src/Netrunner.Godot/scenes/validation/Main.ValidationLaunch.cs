using System.Globalization;
using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

/// <summary>
/// Command-line validation modes: smoke tests, gates, developer demos and the
/// interaction lab launch flag. Everything here is selected only by explicit
/// user arguments; an ordinary launch reaches none of these paths. The flag
/// names are stable because scripts and CI depend on them.
/// </summary>
public partial class Main
{
	private bool _smokeTest;
	private bool _responsivenessSmoke;
	private bool _gallerySmoke;
	private bool _slice43UiSmoke;
	private bool _slice44UiSmoke;
	private bool _slice45UiSmoke;
	private bool _slice46UiSmoke;
	private bool _slice47UiSmoke;
	private bool _slice48UxSmoke;
	private bool _fullMatchSmoke;
	private bool _m4FullMatchSmoke;
	private PlayerSide _fullMatchSmokeSide = PlayerSide.Runner;
	private string _fullMatchSmokePreset = "expanded";
	private ulong _fullMatchSmokeSeed = 47042;
	private bool _gallerySmokePassed;
	private int _smokeStarted;
	private bool _smokeEncounterFocusObserved;
	private bool _smokeAccessFocusObserved;
	private bool _smokeRunEndedObserved;
	private bool _smokeOutcomePresentationObserved;
	private bool _smokeTargetCueObserved;
	private bool _smokeTargetControlsObserved;
	private bool _smokeBodyColumnsInViewport = true;
	private bool _smokeLabelsLegible = true;

	/// <summary>Reads validation flags. Returns false when the launch must stop.</summary>
	private bool ReadValidationArguments(IReadOnlyList<string> arguments)
	{
		_gallerySmoke = arguments.Contains("--m4-card-gallery-smoke", StringComparer.Ordinal);
		_slice43UiSmoke = arguments.Contains("--m4-slice43-ui-smoke", StringComparer.Ordinal);
		_slice44UiSmoke = arguments.Contains("--m4-slice44-ui-smoke", StringComparer.Ordinal);
		_slice45UiSmoke = arguments.Contains("--m4-slice45-ui-smoke", StringComparer.Ordinal);
		_slice46UiSmoke = arguments.Contains("--m4-slice46-ui-smoke", StringComparer.Ordinal);
		_slice47UiSmoke = arguments.Contains("--m4-slice47-ui-smoke", StringComparer.Ordinal);
		_slice48UxSmoke = arguments.Contains("--m4-slice48-ux-smoke", StringComparer.Ordinal);
		_fullMatchSmoke = arguments.Contains("--full-match-smoke-corp", StringComparer.Ordinal)
			|| arguments.Contains("--full-match-smoke-runner", StringComparer.Ordinal);
		_m4FullMatchSmoke = arguments.Contains("--m4-full-smoke-corp", StringComparer.Ordinal)
			|| arguments.Contains("--m4-full-smoke-runner", StringComparer.Ordinal);
		if (_fullMatchSmoke || _m4FullMatchSmoke)
		{
			_fullMatchSmokeSide = arguments.Contains("--m4-full-smoke-corp", StringComparer.Ordinal)
				|| arguments.Contains("--full-match-smoke-corp", StringComparer.Ordinal)
				? PlayerSide.Corp
				: PlayerSide.Runner;
			if (_m4FullMatchSmoke
				&& !TryReadFullMatchSmokeOptions(arguments, out _fullMatchSmokePreset, out _fullMatchSmokeSeed, out string? smokeError))
			{
				GD.PrintErr($"M4_FULL_SMOKE FAIL {smokeError}");
				QuitCleanly(1);
				return false;
			}
		}
		_smokeTest = arguments.Contains("--smoke-test", StringComparer.Ordinal)
			|| arguments.Contains("--m3-responsiveness-smoke", StringComparer.Ordinal);
		_responsivenessSmoke = arguments.Contains("--m3-responsiveness-smoke", StringComparer.Ordinal);
		_useCardTable = !_smokeTest && !_gallerySmoke && !_slice43UiSmoke && !_slice44UiSmoke
			&& !_slice45UiSmoke && !_slice46UiSmoke && !_slice47UiSmoke && !_slice48UxSmoke && !_fullMatchSmoke
			&& !arguments.Contains("--diagnostic-ui", StringComparer.Ordinal);
		return true;
	}

	/// <summary>Starts the flag-selected mode, in the historical precedence order.</summary>
	private void StartLaunchMode(IReadOnlyList<string> arguments, bool fullSmokeReload)
	{
		if (TryReadCaptureOptions(arguments, out CaptureOptions? capture, out string? captureError))
		{
			if (captureError is not null || capture is null)
			{
				GD.PrintErr($"UI_CAPTURE FAIL {captureError}");
				QuitCleanly(2);
			}
			else
			{
				_ = RunCaptureAsync(capture);
			}
		}
		else if (arguments.Contains("--m5-developer-smoke", StringComparer.Ordinal))
		{
			_ = RunDeveloperSessionSmokeAsync(arguments);
		}
		else if (arguments.Contains("--m5-client-smoke", StringComparer.Ordinal))
		{
			_ = RunSoloClientSmokeAsync(arguments);
		}
		else if (arguments.Contains("--llm-opponent-smoke", StringComparer.Ordinal))
		{
			_ = RunLlmOpponentSmokeAsync();
		}
		else if (arguments.Contains("--card-art-smoke", StringComparer.Ordinal) || arguments.Contains("--card-art-cache-smoke", StringComparer.Ordinal))
		{
			_ = RunCardArtworkSmokeAsync(arguments.Contains("--card-art-cache-smoke", StringComparer.Ordinal));
		}
		else if (arguments.Contains("--m4-11-demo", StringComparer.Ordinal))
		{
			_ = RunInteractionDemoAsync();
		}
		else if (arguments.Contains("--m4-11-ui-smoke", StringComparer.Ordinal)
			|| arguments.Contains("--m4-12-ui-smoke", StringComparer.Ordinal))
		{
			_ = RunInteractionSmokeAsync();
		}
		else if (arguments.Contains("--run-hud-soak", StringComparer.Ordinal))
		{
			_ = RunRunHudSoakAsync(arguments);
		}
		else if (arguments.Contains("--run-hud-smoke", StringComparer.Ordinal))
		{
			_ = RunRunHudSmokeAsync();
		}
		else if (arguments.Contains("--interaction-lab", StringComparer.Ordinal))
		{
			OpenInteractionLab();
		}
		else if (arguments.Contains("--main-menu-smoke", StringComparer.Ordinal))
		{
			_ = RunMainMenuSmokeAsync();
		}
		else if (arguments.Contains("--onboarding-smoke", StringComparer.Ordinal))
		{
			_ = RunOnboardingSmokeAsync();
		}
		else if (arguments.Contains("--invisible-window-smoke", StringComparer.Ordinal))
		{
			_ = RunInvisibleWindowSmokeAsync();
		}
		else if (arguments.Contains("--m4-9-ui-smoke", StringComparer.Ordinal))
		{
			_ = RunCardTableSmokeAsync();
		}
		else if (_fullMatchSmoke || _m4FullMatchSmoke)
		{
			_seedInput.Text = _fullMatchSmokeSeed.ToString(CultureInfo.InvariantCulture);
			_humanSide.Select(_fullMatchSmokeSide == PlayerSide.Runner ? 0 : 1);
			if (!ConfigureFullMatchSmokePresets())
			{
				return;
			}
			if (_m4FullMatchSmoke && fullSmokeReload)
			{
				_ = RunFullMatchReloadSmokeAsync();
			}
			else
			{
				BeginMatch();
				_ = RunFullMatchSmokeAsync(_fullMatchSmokeSide);
			}
		}
		else if (_slice48UxSmoke)
		{
			_ = RunSlice48UxSmokeAsync();
		}
		else if (_slice47UiSmoke)
		{
			_ = RunSlice47UiSmokeAsync();
		}
		else if (_slice46UiSmoke)
		{
			_ = RunSlice46UiSmokeAsync();
		}
		else if (_slice45UiSmoke)
		{
			_ = RunSlice45UiSmokeAsync();
		}
		else if (_slice44UiSmoke)
		{
			_ = RunSlice44UiSmokeAsync();
		}
		else if (_slice43UiSmoke)
		{
			_ = RunSlice43UiSmokeAsync();
		}
		else if (_gallerySmoke)
		{
			RunGallerySmoke();
		}
		else if (_smokeTest)
		{
			BeginMatch();
		}
	}
}
