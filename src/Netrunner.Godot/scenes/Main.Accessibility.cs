using Godot;

namespace Netrunner.GodotClient;

/// <summary>
/// Keeps the dynamic board's keyboard order deterministic as controls are
/// rebuilt from each Core publication. Text-only accessibility nodes are left
/// out of the Tab loop; actionable buttons, sliders, selectors, and cards use
/// the same explicit circular order in both directions.
/// </summary>
public partial class Main
{
	private static void ApplyAccessibilityFocusOrder(Node root)
	{
		List<Control> controls = FindDescendants<Control>(root)
			.Where(static control => control.Visible
				&& !control.IsQueuedForDeletion()
				&& control.FocusMode == Control.FocusModeEnum.All)
			.Where(static control => control is not BaseButton button || !button.Disabled)
			.Where(static control => control is not ScrollContainer)
			.ToList();
		for (int index = 0; index < controls.Count; index++)
		{
			Control current = controls[index];
			Control next = controls[(index + 1) % controls.Count];
			Control previous = controls[(index - 1 + controls.Count) % controls.Count];
			current.FocusNext = next.GetPath();
			current.FocusPrevious = previous.GetPath();
		}
	}

	/// <summary>Enumerates live descendants without stale queued subtrees.</summary>
	private static T[] FindDescendants<T>(Node root)
		where T : Node
	{
		var result = new List<T>();
		foreach (Node child in root.GetChildren())
		{
			if (child.IsQueuedForDeletion())
			{
				continue;
			}

			if (child is T matching)
			{
				result.Add(matching);
			}

			result.AddRange(FindDescendants<T>(child));
		}

		return result.ToArray();
	}
}
