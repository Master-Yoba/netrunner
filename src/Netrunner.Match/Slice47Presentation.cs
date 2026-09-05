using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Exact access count copied from Core's run-end observation envelope.</summary>
public sealed record RunEndAccessViewModel(
    string RunId,
    string ServerId,
    int AccessOccurrenceCount)
{
    public int Count => AccessOccurrenceCount;

    public bool IsKnown => AccessOccurrenceCount >= 0;

    public string Summary => $"ACCESS OCCURRENCES · {AccessOccurrenceCount.ToString(CultureInfo.InvariantCulture)}";

    public ZahyaAccessCountViewModel Zahya => new(AccessOccurrenceCount, true);
}

/// <summary>
/// Public physical ICE topology. Core may send this alongside a null card
/// view when a position or host relationship must remain inspectable without
/// revealing the card definition.
/// </summary>
public sealed record IcePositionViewModel(
    string CardId,
    string ServerId,
    int Index)
{
    public string Summary => $"{CardId} · {ServerId}[{Index.ToString(CultureInfo.InvariantCulture)}]";
}

/// <summary>
/// Viewer-safe count used by the Zahya identity panel. The number is supplied
/// by Core and remains unavailable until Core sends it.
/// </summary>
public sealed record ZahyaAccessCountViewModel(int Count, bool IsKnown)
{
    public static ZahyaAccessCountViewModel Unknown { get; } = new(0, false);

    public string Summary => IsKnown
        ? $"ZAHYA ACCESS COUNT · {Count.ToString(CultureInfo.InvariantCulture)}"
        : "ZAHYA ACCESS COUNT · unavailable";

    public int? Value => IsKnown ? Count : null;
}

/// <summary>One side's identity card and the facts safe to place in an identity panel.</summary>
public sealed record IdentityPanelViewModel(PlayerSide Side, CardViewModel? Card)
{
    public bool IsAvailable => Card is not null;

    public string Title => Card?.Title ?? "IDENTITY UNAVAILABLE";

    public string Type => Card?.Type ?? "Unknown identity";

    public string Faction => Card?.Faction ?? string.Empty;

    public string RulesText => Card?.RulesText ?? string.Empty;

    public int? AgendaPoints => Card?.AgendaPoints;

    public string Summary => Card is null
        ? $"{Side.ToString().ToUpperInvariant()} IDENTITY · unavailable"
        : $"{Side.ToString().ToUpperInvariant()} IDENTITY · {Title}"
            + (Faction.Length == 0 ? string.Empty : $" · {Faction}");
}

/// <summary>Both identity panels projected from one filtered Match view.</summary>
public sealed record IdentityPanelState(
    IdentityPanelViewModel Corp,
    IdentityPanelViewModel Runner)
{
    public IdentityPanelViewModel For(PlayerSide side) => side switch
    {
        PlayerSide.Corp => Corp,
        PlayerSide.Runner => Runner,
        _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };
}

/// <summary>Builds the identity panel without consulting authoritative state.</summary>
public static class IdentityPanelPresenter
{
    public static IdentityPanelState Present(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new IdentityPanelState(
            new IdentityPanelViewModel(PlayerSide.Corp, view.CorpIdentity),
            new IdentityPanelViewModel(PlayerSide.Runner, view.RunnerIdentity));
    }

    public static IdentityPanelState Present(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Present(snapshot.View);
    }
}

/// <summary>One physical card's old or resulting ICE position.</summary>
public sealed record IceSwapPositionViewModel(
    string ZoneId,
    int Index,
    string CardId,
    string? Title = null)
{
    public string Summary => $"{CardId} · {ZoneId}[{Index.ToString(CultureInfo.InvariantCulture)}]";
}

/// <summary>
/// Presentation-only before/after view for an engine-issued simultaneous ICE
/// swap. Both selected positions are retained, including when the pieces are
/// in different server ICE zones.
/// </summary>
public sealed record IceSwapPreviewViewModel(
    string FirstCardId,
    string SecondCardId,
    IceSwapPositionViewModel FirstBefore,
    IceSwapPositionViewModel SecondBefore,
    IceSwapPositionViewModel FirstAfter,
    IceSwapPositionViewModel SecondAfter)
{
    public IReadOnlyList<IceSwapPositionViewModel> BeforePositions =>
        new ReadOnlyCollection<IceSwapPositionViewModel>([FirstBefore, SecondBefore]);

    public IReadOnlyList<IceSwapPositionViewModel> AfterPositions =>
        new ReadOnlyCollection<IceSwapPositionViewModel>([FirstAfter, SecondAfter]);

    public IReadOnlyList<IceSwapPositionViewModel> Before => BeforePositions;

    public IReadOnlyList<IceSwapPositionViewModel> After => AfterPositions;

    public string Summary =>
        $"ICE SWAP PREVIEW · BEFORE {FirstBefore.Summary} ↔ {SecondBefore.Summary}"
        + $" · AFTER {FirstAfter.Summary} ↔ {SecondAfter.Summary}";

    public static IceSwapPreviewViewModel FromEvent(IceSwappedEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var firstBefore = new IceSwapPositionViewModel(
            payload.FirstZoneId.Value,
            payload.FirstIndex,
            payload.FirstCardId.Value);
        var secondBefore = new IceSwapPositionViewModel(
            payload.SecondZoneId.Value,
            payload.SecondIndex,
            payload.SecondCardId.Value);
        return new IceSwapPreviewViewModel(
            payload.FirstCardId.Value,
            payload.SecondCardId.Value,
            firstBefore,
            secondBefore,
            firstBefore with { CardId = payload.SecondCardId.Value },
            secondBefore with { CardId = payload.FirstCardId.Value });
    }

    public static IceSwapPreviewViewModel FromChoice(
        MatchViewModel view,
        BehaviorChoiceParameters choice)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.CardIds.Count != 2)
        {
            throw new ArgumentException("An ICE swap choice must carry exactly two card IDs.", nameof(choice));
        }

        string firstId = choice.CardIds[0].Value;
        string secondId = choice.CardIds[1].Value;
        (string Zone, int Index, string? Title) first = FindIcePosition(view, firstId);
        (string Zone, int Index, string? Title) second = FindIcePosition(view, secondId);
        var firstBefore = new IceSwapPositionViewModel(first.Zone, first.Index, firstId, first.Title);
        var secondBefore = new IceSwapPositionViewModel(second.Zone, second.Index, secondId, second.Title);
        return new IceSwapPreviewViewModel(
            firstId,
            secondId,
            firstBefore,
            secondBefore,
            firstBefore with { CardId = secondId, Title = second.Title },
            secondBefore with { CardId = firstId, Title = first.Title });
    }

    private static (string Zone, int Index, string? Title) FindIcePosition(MatchViewModel view, string cardId)
    {
        foreach (ServerViewModel server in view.Servers)
        {
            IcePositionViewModel? publicPosition = server.IcePositions
                .FirstOrDefault(position => string.Equals(position.CardId, cardId, StringComparison.Ordinal));
            if (publicPosition is not null)
            {
                string? title = server.OrderedIce
                    .FirstOrDefault(card => card is not null
                        && string.Equals(card.Id, cardId, StringComparison.Ordinal))?.Title;
                return (publicPosition.ServerId, publicPosition.Index, title);
            }

            for (int index = 0; index < server.OrderedIce.Count; index++)
            {
                if (server.OrderedIce[index] is { } card
                    && string.Equals(card.Id, cardId, StringComparison.Ordinal))
                {
                    return (server.Id, server.OrderedIce.Count - index - 1, card.Title);
                }
            }
        }

        throw new InvalidOperationException($"ICE swap preview target '{cardId}' is not visible in the observation.");
    }
}

/// <summary>One action row carrying typed Slice 4.7 semantics.</summary>
public sealed record Slice47ActionPresentationViewModel(
    ActionId ActionId,
    ActionKind ActionKind,
    PlayerSide ActingPlayer,
    string? ChoiceKey,
    string? OptionKey,
    IReadOnlyList<string> TargetCardIds,
    string Description,
    string PresentationLabel,
    bool IsTyped)
{
    public LegalActionViewModel? Action { get; init; }

    public string? TargetCardId => TargetCardIds.Count == 1 ? TargetCardIds[0] : null;

    public string? TargetServerId { get; init; }

    public bool IsOptional => OptionKey is null && ActionKind == ActionKind.ChooseBehavior;

    public bool IsControllerChoice { get; init; }

    public bool IsNbnChoice { get; init; }

    public bool IsGainTwoChoice { get; init; }

    public bool IsDrawTwoChoice { get; init; }

    public IceSwapPreviewViewModel? SwapPreview { get; init; }

    public string? SemanticLabel => IsControllerChoice
        ? "CONTROLLER CHOICE"
        : IsNbnChoice
            ? "NBN CHOICE"
            : null;
}

/// <summary>Typed Slice 4.7 decision surface used by Match and Godot.</summary>
public sealed record Slice47DecisionViewModel(
    bool IsAvailable,
    string? Diagnostic,
    DecisionKind? DecisionKind,
    PlayerSide? ActingPlayer,
    string? Prompt,
    IReadOnlyList<Slice47ActionPresentationViewModel> Options,
    IReadOnlyList<IceSwapPreviewViewModel> SwapPreviews,
    ZahyaAccessCountViewModel ZahyaAccess)
{
    public bool IsProminent => IsAvailable && Options.Count > 0;

    public bool HasControllerChoice => Options.Any(static option => option.IsControllerChoice);

    public bool HasNbnChoice => Options.Any(static option => option.IsNbnChoice);

    public static Slice47DecisionViewModel Unavailable(string? diagnostic = null) => new(
        false,
        diagnostic ?? "Slice 4.7 decision projection unavailable.",
        null,
        null,
        null,
        Array.Empty<Slice47ActionPresentationViewModel>(),
        Array.Empty<IceSwapPreviewViewModel>(),
        ZahyaAccessCountViewModel.Unknown);
}

/// <summary>Reads additive Slice 4.7 observation envelopes.</summary>
public static class Slice47ObservationProjector
{
    public static RunEndAccessViewModel? ReadRunEnd(IReadOnlyDictionary<string, object?> publicState)
    {
        ArgumentNullException.ThrowIfNull(publicState);
        if (!publicState.TryGetValue("runEnd", out object? raw)
            || !TryReadObject(raw, out IReadOnlyDictionary<string, object?>? map)
            || map is null
            || !TryReadString(map, "runId", out string runId)
            || !TryReadString(map, "serverId", out string serverId)
            || !TryReadInt(map, "accessOccurrenceCount", out int count)
            || count < 0)
        {
            return null;
        }

        return new RunEndAccessViewModel(runId, serverId, count);
    }

    private static bool TryReadObject(
        object? raw,
        out IReadOnlyDictionary<string, object?>? map)
    {
        if (raw is IReadOnlyDictionary<string, object?> dictionary)
        {
            map = dictionary;
            return true;
        }

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            map = element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone(),
                StringComparer.Ordinal);
            return true;
        }

        map = null;
        return false;
    }

    private static bool TryReadString(
        IReadOnlyDictionary<string, object?> map,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!map.TryGetValue(key, out object? raw)
            || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return false;
        }

        value = raw switch
        {
            string text => text,
            CardInstanceId card => card.Value,
            ServerId server => server.Value,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => string.Empty,
        };
        return value.Length > 0;
    }

    private static bool TryReadInt(
        IReadOnlyDictionary<string, object?> map,
        string key,
        out int value)
    {
        value = 0;
        if (!map.TryGetValue(key, out object? raw) || raw is null)
        {
            return false;
        }

        if (raw is int integer)
        {
            value = integer;
            return true;
        }

        if (raw is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)longValue;
            return true;
        }

        return raw is JsonElement element
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }
}

/// <summary>Projects typed Slice 4.7 choices and event previews for clients.</summary>
public static class Slice47PresentationProjector
{
    public static Slice47DecisionViewModel Present(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Decision is not { } decision)
        {
            return Slice47DecisionViewModel.Unavailable("There is no viewer-owned decision in this observation.") with
            {
                ZahyaAccess = ZahyaCount(view),
            };
        }

        var projected = new List<Slice47ActionPresentationViewModel>(view.LegalActions.Count);
        foreach (LegalActionViewModel action in view.LegalActions)
        {
            projected.Add(ProjectAction(view, decision.OwnerSide, action));
        }

        var previews = projected
            .Where(static action => action.SwapPreview is not null)
            .Select(static action => action.SwapPreview!)
            .ToArray();
        return new Slice47DecisionViewModel(
            true,
            null,
            decision.Kind,
            decision.OwnerSide,
            decision.Prompt,
            new ReadOnlyCollection<Slice47ActionPresentationViewModel>(projected),
            new ReadOnlyCollection<IceSwapPreviewViewModel>(previews),
            ZahyaCount(view));
    }

    public static Slice47DecisionViewModel Present(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Slice47DecisionViewModel decision = Present(snapshot.View);
        IceSwapPreviewViewModel[] eventPreviews = snapshot.Events
            .Where(static @event => @event.Payload is IceSwappedEventPayload)
            .Select(static @event => IceSwapPreviewViewModel.FromEvent((IceSwappedEventPayload)@event.Payload))
            .ToArray();
        if (eventPreviews.Length == 0)
        {
            return decision;
        }

        return decision with
        {
            SwapPreviews = new ReadOnlyCollection<IceSwapPreviewViewModel>(
                decision.SwapPreviews.Concat(eventPreviews).ToArray()),
        };
    }

    public static IdentityPanelState PresentIdentities(MatchViewModel view) =>
        IdentityPanelPresenter.Present(view);

    public static ZahyaAccessCountViewModel PresentZahyaAccess(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return ZahyaCount(view);
    }

    public static IceSwapPreviewViewModel Preview(IceSwappedEventPayload payload) =>
        IceSwapPreviewViewModel.FromEvent(payload);

    public static IceSwapPreviewViewModel? Preview(MatchViewModel view, BehaviorChoiceParameters choice)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(choice);
        if (!IsSwapChoice(choice) || choice.CardIds.Count != 2)
        {
            return null;
        }

        // Core emits the public ICE topology whenever a legal swap choice can
        // name a hidden ICE. A missing position is therefore a malformed
        // observation and must remain visible to the host rather than being
        // silently dropped from the decision presentation.
        return IceSwapPreviewViewModel.FromChoice(view, choice);
    }

    public static string DescribeIceSwap(IceSwappedEventPayload payload) =>
        IceSwapPreviewViewModel.FromEvent(payload).Summary;

    public static string DescribeDerez(CardDerezzedEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return $"DEREZZED · {payload.CardId.Value} · state changed by Core.";
    }

    private static Slice47ActionPresentationViewModel ProjectAction(
        MatchViewModel view,
        PlayerSide actingPlayer,
        LegalActionViewModel action)
    {
        string? choiceKey = null;
        string? optionKey = null;
        IReadOnlyList<string> targets = Array.Empty<string>();
        string label;
        bool typed = false;
        bool controller = false;
        bool nbn = false;
        bool gainTwo = false;
        bool drawTwo = false;
        IceSwapPreviewViewModel? swap = null;
        string? targetServer = null;

        if (action.Parameters is BehaviorChoiceParameters behavior)
        {
            choiceKey = behavior.ChoiceKey;
            optionKey = behavior.OptionKeys.Count == 1 ? behavior.OptionKeys[0] : null;
            targets = new ReadOnlyCollection<string>(behavior.CardIds.Select(static id => id.Value).ToArray());
            typed = true;
            (label, controller, nbn, gainTwo, drawTwo) = LabelBehaviorChoice(behavior);
            if (IsSwapChoice(behavior))
            {
                swap = Preview(view, behavior);
            }
        }
        else
        {
            label = ActionsRegionPresenter.Describe(action.Kind, action.Description, action.Parameters);
            targetServer = action.Parameters switch
            {
                RunServerParameters run => run.ServerId.Value,
                PlayCardParameters play when play.TargetServerId is { } server => server.Value,
                _ => null,
            };
            string? card = action.Parameters switch
            {
                CardChoiceParameters choice => choice.CardId.Value,
                InstallCardParameters install => install.CardId.Value,
                PlayCardParameters play => play.CardId.Value,
                AdvanceCardParameters advance => advance.CardId.Value,
                ScoreAgendaParameters score => score.CardId.Value,
                TrashTaggedResourceParameters trash => trash.CardId.Value,
                RezCardParameters rez => rez.CardId.Value,
                RunIceParameters ice => ice.IceId.Value,
                _ => null,
            };
            targets = card is null ? Array.Empty<string>() : [card];
            typed = action.Parameters is not LegacyActionParameters;
        }

        return new Slice47ActionPresentationViewModel(
            action.Id,
            action.Kind,
            actingPlayer,
            choiceKey,
            optionKey,
            targets,
            action.Description,
            label,
            typed)
        {
            Action = action,
            TargetServerId = targetServer,
            IsControllerChoice = controller,
            IsNbnChoice = nbn,
            IsGainTwoChoice = gainTwo,
            IsDrawTwoChoice = drawTwo,
            SwapPreview = swap,
        };
    }

    private static (string Label, bool Controller, bool Nbn, bool GainTwo, bool DrawTwo) LabelBehaviorChoice(
        BehaviorChoiceParameters choice)
    {
        string option = choice.OptionKeys.Count == 1 ? choice.OptionKeys[0] : string.Empty;
        string normalizedChoice = Normalize(choice.ChoiceKey);
        string normalizedOption = Normalize(option);
        bool gainTwo = string.Equals(normalizedOption, "gain-two", StringComparison.Ordinal);
        bool drawTwo = string.Equals(normalizedOption, "draw-two", StringComparison.Ordinal);
        bool nbn = gainTwo || drawTwo
            || normalizedChoice.Contains("nbn", StringComparison.Ordinal);
        bool controller = normalizedChoice.Contains("controller", StringComparison.Ordinal)
            || normalizedOption is "corp" or "runner"
            || normalizedOption.Contains("controller-corp", StringComparison.Ordinal)
            || normalizedOption.Contains("controller-runner", StringComparison.Ordinal);
        string label = gainTwo
            ? "GAIN 2 CREDITS"
            : drawTwo
                ? "DRAW 2 CARDS"
                : choice.OptionKeys.Count == 0
                    ? "SELECT TARGETS"
                    : choice.OptionKeys.Count > 1
                        ? "CHOOSE AN OPTION"
                        : option.ToUpperInvariant().Replace('_', ' ').Replace('-', ' ');
        if (controller)
        {
            string controllerLabel = option.Length == 0
                ? "CHOOSE CONTROLLER"
                : $"CHOOSE {option.ToUpperInvariant().Replace('_', ' ').Replace('-', ' ')} CONTROLLER";
            label = controllerLabel;
        }

        return (label, controller, nbn, gainTwo, drawTwo);
    }

    private static bool IsSwapChoice(BehaviorChoiceParameters choice) =>
        choice.CardIds.Count == 2
        && (string.Equals(Normalize(choice.ChoiceKey), "tao-ice", StringComparison.Ordinal)
            || Normalize(choice.ChoiceKey).Contains("swap", StringComparison.Ordinal)
            || choice.OptionKeys.Any(option => Normalize(option).Contains("swap", StringComparison.Ordinal)));

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static ZahyaAccessCountViewModel ZahyaCount(MatchViewModel view)
    {
        if (view.Run?.Access.AccessOccurrenceCount is { } liveCount && liveCount >= 0)
        {
            return new ZahyaAccessCountViewModel(liveCount, true);
        }

        if (view.RunEnd?.AccessOccurrenceCount is { } endCount && endCount >= 0)
        {
            return new ZahyaAccessCountViewModel(endCount, true);
        }

        return ZahyaAccessCountViewModel.Unknown;
    }
}
