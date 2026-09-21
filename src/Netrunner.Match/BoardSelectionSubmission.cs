using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Advances a confirmed local selection using fresh issued incremental actions.
/// A changed continuation or unexpected accepted prefix stops the batch; it never rolls Core back.
/// </summary>
public sealed class BoardSelectionSubmission
{
    private readonly ObservationCardSelection _origin;
    private readonly string[] _desired;
    private string[] _expected;
    private (string Id, long Revision)? _lastDecision;
    public bool Finished { get; private set; }
    public string? Error { get; private set; }

    public BoardSelectionSubmission(ObservationCardSelection origin, IEnumerable<string> desired)
    {
        _origin = origin;
        _desired = desired.ToArray();
        _expected = origin.Selected.ToArray();
        if (!CanConfirm(origin, _desired)) throw new ArgumentException("The staged selection is incomplete or contains ineligible cards.", nameof(desired));
    }

    public static bool CanConfirm(ObservationCardSelection context, IReadOnlyList<string> selected) =>
        selected.Distinct(StringComparer.Ordinal).Count() == selected.Count
        && selected.All(context.Candidates.Contains)
        && selected.Take(context.Selected.Count).SequenceEqual(context.Selected)
        && (context.Cardinality switch
        {
            BehaviorChoiceCardinalityKind.Exactly => selected.Count == context.Count,
            BehaviorChoiceCardinalityKind.UpTo => selected.Count <= context.Count,
            BehaviorChoiceCardinalityKind.AnyNumber => true,
            _ => false,
        });

    public LegalActionViewModel? Next(MatchViewModel view)
    {
        if (Finished || Error is not null) return null;
        if (view.Decision is not { } decision || decision.OwnerSide != view.Viewer)
            return Stop("Selection interrupted by another decision.");
        if (_lastDecision == (decision.Id, decision.Revision)) return null;
        if (view.CardSelection is not { } context || context.ContinuationId != _origin.ContinuationId
            || context.ChoiceKey != _origin.ChoiceKey || context.Cardinality != _origin.Cardinality || context.Count != _origin.Count
            || !context.Candidates.SequenceEqual(_origin.Candidates) || !context.Selected.SequenceEqual(_expected))
            return Stop("Selection changed. Review the remaining choice.");
        bool final = _expected.Length == _desired.Length;
        LegalActionViewModel? issued = view.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ChooseBehavior
            && action.Parameters is BehaviorChoiceParameters p && p.ChoiceKey == context.ChoiceKey && p.OptionKeys.Count == 0
            && p.FinalizeSelection == final
            && (final ? p.CardIds.Select(id => id.Value).SequenceEqual(_desired)
                : p.CardIds.Count == 1 && p.CardIds[0].Value == _desired[_expected.Length]));
        if (issued is null) return Stop("The next selection is no longer issued. Review the remaining choice.");
        _lastDecision = (decision.Id, decision.Revision);
        if (final) Finished = true;
        else _expected = [.. _expected, _desired[_expected.Length]];
        return issued;
    }

    private LegalActionViewModel? Stop(string error) { Error = error; return null; }
}
