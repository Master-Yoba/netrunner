using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

/// <summary>Owns event-feed animation and bounded presentation history.</summary>
public partial class Main
{
	private async Task AnimateEventsAsync()
	{
		_animationRunning = true;
		while (_animationBatches.Count > 0)
		{
			IReadOnlyList<EngineEvent> batch = _animationBatches.Dequeue();
			foreach (EngineEvent @event in batch)
			{
				_eventTitle.Text = $"{@event.Kind} · sequence {@event.Sequence}";
				AppendEventFeed(DescribeEvent(@event));
				double seconds = _reducedMotion.ButtonPressed || _skipAnimation
					? 0.01
					: 0.18 / Math.Max(0.25, _speed.Value);
				await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
			}

			_skipAnimation = false;
		}

		_animationRunning = false;
	}

	private void SkipAnimations()
	{
		_skipAnimation = true;
		_animationBatches.Clear();
	}

	private void AppendEventFeed(string line)
	{
		const int MaxVisibleEventLines = 8;
		_eventFeedLines.Enqueue(line);
		while (_eventFeedLines.Count > MaxVisibleEventLines)
		{
			_eventFeedLines.Dequeue();
		}

		_eventFeed.Text = string.Join('\n', _eventFeedLines.Reverse());
	}
}
