using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// EL-BEHAVIOR-INSTALL: how a behavior-driven install from a zone pays.
/// </summary>
public enum BehaviorInstallCostRule
{
    /// <summary>The printed install cost (and, for ICE, the install tax), less any declared discount.</summary>
    Normal,

    /// <summary>"Ignoring all costs": no credits and no install tax.</summary>
    IgnoreAllCosts,
}

public enum BehaviorInstallChoiceStage
{
    AwaitingChoice,
    AwaitingInstall,
}

public static partial class ResolutionBehaviorIds
{
    public const string BehaviorInstallChoice = "rules.behavior-install-choice-26.03";
}

/// <summary>One frozen install alternative: a card and where it goes, at a cost.</summary>
public sealed record BehaviorInstallChoice
{
    [JsonConstructor]
    public BehaviorInstallChoice(
        CardInstanceId cardId,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote,
        int creditCost,
        CardInstanceId? hostCardId = null)
    {
        if (!Enum.IsDefined(destinationKind))
        {
            throw new ArgumentOutOfRangeException(nameof(destinationKind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(creditCost);
        CardId = cardId;
        DestinationKind = destinationKind;
        ServerId = serverId;
        CreateNewRemote = createNewRemote;
        CreditCost = creditCost;
        HostCardId = hostCardId;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public InstallDestinationKind DestinationKind { get; private set; }

    [JsonInclude]
    public ServerId? ServerId { get; private set; }

    [JsonInclude]
    public bool CreateNewRemote { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditCost { get; private set; }

    /// <summary>The ICE a card installed only on a host goes on (Botulus from Topan's install); null for any other card.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardInstanceId? HostCardId { get; private set; }
}

/// <summary>
/// EL-BEHAVIOR-INSTALL: the decision of a behavior-driven install from a
/// zone ("install 1 card from HQ", "install a program from your heap"). The
/// owner picks one frozen card and destination, or declines when optional.
/// The parent behavior frame's current InstallCard instruction is the
/// declaration; the chosen install runs through the ordinary install pipeline.
/// </summary>
public sealed record BehaviorInstallChoiceFrame : ResolutionFrame
{
    [JsonConstructor]
    public BehaviorInstallChoiceFrame(
        int version,
        string behaviorId,
        PlayerId owner,
        string frameIdentity,
        string parentFrameIdentity,
        BehaviorInstallCostRule costRule,
        int discount,
        bool optional,
        BehaviorInstallChoiceStage stage,
        IReadOnlyList<BehaviorInstallChoice> choices)
        : base(version, behaviorId)
    {
        if (!StableIdentifier.TryValidate(frameIdentity) || !StableIdentifier.TryValidate(parentFrameIdentity))
        {
            throw new ArgumentException("A behavior install choice needs stable frame identities.");
        }

        if (!Enum.IsDefined(costRule) || !Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(costRule));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(discount);
        ArgumentNullException.ThrowIfNull(choices);
        Owner = owner;
        FrameIdentity = frameIdentity;
        ParentFrameIdentity = parentFrameIdentity;
        CostRule = costRule;
        Discount = discount;
        Optional = optional;
        Stage = stage;
        Choices = new ReadOnlyCollection<BehaviorInstallChoice>(choices.ToArray());
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string FrameIdentity { get; private set; }

    /// <summary>Identity of the behavior frame whose InstallCard instruction opened this choice.</summary>
    [JsonInclude]
    [JsonRequired]
    public string ParentFrameIdentity { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorInstallCostRule CostRule { get; private set; }

    /// <summary>Credits the install costs less (Illumination, Topan).</summary>
    [JsonInclude]
    [JsonRequired]
    public int Discount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Optional { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorInstallChoiceStage Stage { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<BehaviorInstallChoice> Choices { get; private set; }

    internal BehaviorInstallChoiceFrame WithStage(BehaviorInstallChoiceStage stage) => new(
        Version,
        BehaviorId,
        Owner,
        FrameIdentity,
        ParentFrameIdentity,
        CostRule,
        Discount,
        Optional,
        stage,
        Choices);
}

/// <summary>The action that answers a behavior install choice: one frozen choice, or decline.</summary>
public sealed record BehaviorInstallChoiceParameters : ActionParameters
{
    [JsonConstructor]
    public BehaviorInstallChoiceParameters(string frameIdentity, BehaviorInstallChoice? choice)
    {
        if (!StableIdentifier.TryValidate(frameIdentity))
        {
            throw new ArgumentException("A behavior install action names its stable frame identity.", nameof(frameIdentity));
        }

        FrameIdentity = frameIdentity;
        Choice = choice;
    }

    [JsonInclude]
    [JsonRequired]
    public string FrameIdentity { get; private set; }

    /// <summary>The chosen install, or null to decline an optional install.</summary>
    [JsonInclude]
    public BehaviorInstallChoice? Choice { get; private set; }
}
