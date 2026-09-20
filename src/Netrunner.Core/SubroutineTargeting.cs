namespace Netrunner.Core;

/// <summary>Canonical target sets for one break ability activation, ordered by quantity then ordinal.</summary>
public static class SubroutineTargeting
{
    public static IEnumerable<IReadOnlyList<int>> Choices(IEnumerable<int> ordinals, int limit)
    {
        int[] available = ordinals.Distinct().Order().ToArray();
        for (int quantity = 1; quantity <= Math.Min(limit, available.Length); quantity++)
        {
            foreach (IReadOnlyList<int> targets in Choose(0, new int[quantity], 0)) yield return targets;
        }

        IEnumerable<IReadOnlyList<int>> Choose(int start, int[] selected, int depth)
        {
            if (depth == selected.Length)
            {
                yield return Array.AsReadOnly((int[])selected.Clone());
                yield break;
            }
            for (int index = start; index <= available.Length - (selected.Length - depth); index++)
            {
                selected[depth] = available[index];
                foreach (IReadOnlyList<int> targets in Choose(index + 1, selected, depth + 1)) yield return targets;
            }
        }
    }
}
