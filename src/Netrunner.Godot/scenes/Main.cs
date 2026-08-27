using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

public partial class Main : Control
{
	private Label _status = null!;
	private Label _eventTitle = null!;
	private Label _eventDetail = null!;
	private Label _eventFeed = null!;
	private Label _cardDetails = null!;
	private ProgressBar _progress = null!;
	private PanelContainer _eventPanel = null!;
	private int _framesWhileAnimating;
	private int _eventsPresented;
	private bool _animationRunning;

	public override void _Ready()
	{
		_status = GetNode<Label>("Margin/Layout/Header/Status");
		_eventTitle = GetNode<Label>("Margin/Layout/Body/EventPanel/EventLayout/EventTitle");
		_eventDetail = GetNode<Label>("Margin/Layout/Body/EventPanel/EventLayout/EventDetail");
		_eventFeed = GetNode<Label>("Margin/Layout/Body/EventFeed");
		_cardDetails = GetNode<Label>("Margin/Layout/CardDetails");
		_progress = GetNode<ProgressBar>("Margin/Layout/Body/EventPanel/EventLayout/Progress");
		_eventPanel = GetNode<PanelContainer>("Margin/Layout/Body/EventPanel");

		Button inspectButton = GetNode<Button>("Margin/Layout/InspectButton");
		inspectButton.Pressed += ToggleCardDetails;

		BuildInfo build = BuildMetadata.Current;
		_status.Text = $"Foundation bridge · {build.ApplicationVersion} · Rules {build.RulesVersion}";
		_ = RunEventBridgeAsync();
	}

	public override void _Process(double delta)
	{
		if (_animationRunning)
		{
			_framesWhileAnimating++;
		}
	}

	private async Task RunEventBridgeAsync()
	{
		bool smokeTest = OS.GetCmdlineUserArgs().Contains("--smoke-test", StringComparer.Ordinal);
		double duration = smokeTest ? 0.01 : 0.32;
		IReadOnlyList<GameEvent> events = FoundationEventBatch.Create();
		_progress.MaxValue = events.Count;
		_animationRunning = true;

		try
		{
			foreach (GameEvent gameEvent in events)
			{
				string title = ReadData(gameEvent, "title", gameEvent.Kind);
				string detail = ReadData(gameEvent, "detail", string.Empty);
				_eventTitle.Text = title;
				_eventDetail.Text = detail;
				_eventFeed.Text += $"{gameEvent.Sequence}. {title}\n";
				_progress.Value = gameEvent.Sequence;

				Tween tween = CreateTween();
				tween.TweenProperty(_eventPanel, "modulate:a", 1.0, duration).From(0.35);
				await ToSignal(tween, Tween.SignalName.Finished);
				_eventsPresented++;
			}

			_eventTitle.Text = "Event bridge ready";
			_eventDetail.Text = "Animations consumed immutable Core events without advancing game state.";
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			if (smokeTest)
			{
				GetTree().Quit(1);
			}

			return;
		}
		finally
		{
			_animationRunning = false;
		}

		if (smokeTest)
		{
			bool passed = _eventsPresented == 5 && _framesWhileAnimating > 0;
			GD.Print(
				$"M0_EVENT_BRIDGE {(passed ? "PASS" : "FAIL")} " +
				$"events={_eventsPresented} responsive_frames={_framesWhileAnimating}");
			GetTree().Quit(passed ? 0 : 1);
		}
	}

	private void ToggleCardDetails()
	{
		_cardDetails.Visible = !_cardDetails.Visible;
	}

	private static string ReadData(GameEvent gameEvent, string key, string fallback) =>
		gameEvent.Data is not null && gameEvent.Data.TryGetValue(key, out string? value)
			? value
			: fallback;
}
