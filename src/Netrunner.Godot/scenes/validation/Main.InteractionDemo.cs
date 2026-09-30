using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task RunInteractionDemoAsync()
	{
		try
		{
			OpenInteractionLab();
			foreach (int scenario in new[] { 3, 7 })
			{
				_labScenario = scenario;
				ResetInteractionLab();
				await TableFrames(25);
				Vector2 start = _gameTable.VisibleCards["lab-hand"].GetGlobalRect().GetCenter();
				await LabPointer(start);
				LabButton(start, true);
				await LabPointer(start + new Vector2(0, -40));
				BoardInteractionDestination destination = BoardInteractionProjector.Project(_currentSnapshot!.View).Sources[0].Destinations[0];
				Vector2 end = _gameTable.GetDropRect(destination.Kind.ToString(), destination.TargetId)!.Value.GetCenter();
				for (int step = 1; step <= 30; step++) await LabPointer(start.Lerp(end, step / 30f));
				await TableFrames(20);
				LabButton(end, false);
				await TableFrames(35);
				RequireInteraction(_labSubmissionCount == 1, "Demonstration drop did not execute.");
			}
			GD.Print("M411_DEMO PASS hosted_install=true new_remote=true");
			QuitCleanly();
		}
		catch (Exception error)
		{
			GD.PrintErr($"M411_DEMO FAIL {error}");
			QuitCleanly(1);
		}
	}
}
