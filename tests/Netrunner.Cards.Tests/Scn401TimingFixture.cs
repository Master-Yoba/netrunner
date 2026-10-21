using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

using Netrunner.Core;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Test-owned schema for SCN-401.  The production ScenarioArtifact cannot
/// describe installed server roots or an injected executable behavior
/// catalog, so this deliberately remains a test fixture contract.  It is
/// strict and versioned like the M2 scenario document, but it never becomes a
/// runtime input to the engine.
/// </summary>
internal sealed class Scn401TimingFixtureDocument
{
    private static readonly string[] ExpectedCaseIds =
    [
        "approach-anoetic-void",
        "approach-manegarm-skunkworks",
        "encounter-funhouse",
        "encounter-ansel-bioroid-repeated",
        "encounter-ansel-installed-card-target",
        "encounter-ansel-normal-install",
        "encounter-ansel-restriction-agenda",
        "encounter-ansel-restriction-asset",
        "encounter-bran-inward-install",
        "encounter-mayfly-used-run-end",
        "encounter-mayfly-unused-run-end",
        "jailbreak-hq-success-draw-access",
        "jailbreak-rnd-success-draw-access",
    ];

    [JsonConstructor]
    public Scn401TimingFixtureDocument(
        int schemaVersion,
        string scenarioId,
        ulong seed,
        string rulesVersion,
        int engineStateSchemaVersion,
        string behaviorRegistryVersion,
        Scn401CatalogPin catalog,
        IReadOnlyList<Scn401TimingCase> cases,
        IReadOnlyList<string> citations,
        string crossCheckNote,
        IReadOnlyList<string>? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(scenarioId);
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(behaviorRegistryVersion);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(crossCheckNote);
        SchemaVersion = schemaVersion;
        ScenarioId = scenarioId;
        Seed = seed;
        RulesVersion = rulesVersion;
        EngineStateSchemaVersion = engineStateSchemaVersion;
        BehaviorRegistryVersion = behaviorRegistryVersion;
        Catalog = catalog;
        Cases = new ReadOnlyCollection<Scn401TimingCase>(cases.Select(CloneCase).ToArray());
        Citations = new ReadOnlyCollection<string>(citations.ToArray());
        CrossCheckNote = crossCheckNote;
        Excluded = new ReadOnlyCollection<string>((excluded ?? []).ToArray());
    }

    public int SchemaVersion { get; }

    public string ScenarioId { get; }

    public ulong Seed { get; }

    public string RulesVersion { get; }

    public int EngineStateSchemaVersion { get; }

    public string BehaviorRegistryVersion { get; }

    public Scn401CatalogPin Catalog { get; }

    public IReadOnlyList<Scn401TimingCase> Cases { get; }

    public IReadOnlyList<string> Citations { get; }

    public string CrossCheckNote { get; }

    public IReadOnlyList<string> Excluded { get; }

    public static Scn401TimingFixtureDocument Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scenarios", "scn-401-timing.json");
        return Parse(File.ReadAllText(path));
    }

    public static Scn401TimingFixtureDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize<Scn401TimingFixtureDocument>(json, EngineJson.Options)
                ?? throw new JsonException("SCN-401 fixture JSON was null.");
        }
        catch (JsonException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new JsonException($"SCN-401 fixture JSON is malformed: {exception.Message}", exception);
        }
    }

    public IReadOnlyList<string> CollectProblems()
    {
        var problems = new List<string>();
        if (SchemaVersion != 1)
        {
            problems.Add($"schemaVersion: expected 1, found {SchemaVersion}.");
        }

        if (ScenarioId != "SCN-401")
        {
            problems.Add($"scenarioId: expected 'SCN-401', found '{ScenarioId}'.");
        }

        if (Seed == 0)
        {
            problems.Add("seed: must be nonzero for deterministic evidence.");
        }

        if (RulesVersion != EngineCompatibility.RulesVersion)
        {
            problems.Add($"rulesVersion: '{RulesVersion}' is not supported.");
        }

        if (EngineStateSchemaVersion != EngineCompatibility.StateSchemaVersion)
        {
            problems.Add(
                $"engineStateSchemaVersion: expected {EngineCompatibility.StateSchemaVersion}, found {EngineStateSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(BehaviorRegistryVersion)
            || BehaviorRegistryVersion != Catalog.Version)
        {
            problems.Add("behaviorRegistryVersion: must match the pinned catalog version.");
        }

        if (Catalog.TestOnlyIceDefinitions.Count == 0)
        {
            problems.Add("catalog.testOnlyIceDefinitions: at least one focused fixture extension is required.");
        }

        if (Excluded.Count > 0)
        {
            problems.Add("excluded: SCN-401 Slice 4.5 scope is fully executable and must not retain exclusions.");
        }

        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Scn401TimingCase scenario in Cases)
        {
            string path = $"cases[{scenario.Id}]";
            if (!IsStableIdentifier(scenario.Id))
            {
                problems.Add($"{path}.id: must be a stable identifier.");
            }
            else if (!caseIds.Add(scenario.Id))
            {
                problems.Add($"{path}.id: duplicate case ID.");
            }

            if (!Enum.IsDefined(scenario.Kind))
            {
                problems.Add($"{path}.kind: '{scenario.Kind}' is not supported.");
            }

            if (!IsStableIdentifier(scenario.TargetServer)
                || (!scenario.TargetServer.StartsWith("remote:", StringComparison.Ordinal)
                    && scenario.TargetServer is not ("headquarters" or "hq" or "r&d" or "rnd" or "archives")))
            {
                problems.Add($"{path}.targetServer: must be a stable remote or central server reference.");
            }

            if (scenario.RunnerCredits < 0 || scenario.RunnerClicks < 0 || scenario.CorpCredits < 0)
            {
                problems.Add($"{path}: player resources must not be negative.");
            }

            if (!IsStableIdentifier(scenario.SourceCardId))
            {
                problems.Add($"{path}.sourceCardId: must be a stable card instance ID.");
            }

            ValidateCards(scenario, path, problems);
            ValidateReaction(scenario, path, problems);
            ValidateBoundaries(scenario, path, problems);
            ValidateExpectedEvents(scenario, path, problems);
        }

        if (Cases.Count != ExpectedCaseIds.Length)
        {
            problems.Add($"cases: expected {ExpectedCaseIds.Length} Slice 4.5 timing cases, found {Cases.Count}.");
        }

        foreach (string expectedCaseId in ExpectedCaseIds)
        {
            if (!caseIds.Contains(expectedCaseId))
            {
                problems.Add($"cases: required Slice 4.5 case '{expectedCaseId}' is missing.");
            }
        }

        foreach (string actualCaseId in caseIds)
        {
            if (!ExpectedCaseIds.Contains(actualCaseId, StringComparer.Ordinal))
            {
                problems.Add($"cases: unexpected case '{actualCaseId}' is outside the Slice 4.5 timing scope.");
            }
        }

        if (!Citations.Any(value => value.Contains("6.9.4g", StringComparison.Ordinal)))
        {
            problems.Add("citations: the 6.9.4g approach-server rule is missing.");
        }

        if (!Citations.Any(value => value.Contains("6.9.3a", StringComparison.Ordinal)))
        {
            problems.Add("citations: the 6.9.3a encounter-initialization rule is missing.");
        }

        if (Citations.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add("citations: citations must not be blank.");
        }

        if (!CrossCheckNote.Contains("Jinteki.net", StringComparison.Ordinal)
            || !CrossCheckNote.Contains("CR 26.03", StringComparison.Ordinal))
        {
            problems.Add("crossCheckNote: must name the Jinteki.net cross-check and CR 26.03 authority.");
        }

        return problems.AsReadOnly();
    }

    public void Validate()
    {
        IReadOnlyList<string> problems = CollectProblems();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "SCN-401 fixture is invalid:\n" + string.Join("\n", problems));
        }
    }

    private static void ValidateCards(
        Scn401TimingCase scenario,
        string path,
        List<string> problems)
    {
        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        Scn401CardPlacement? source = null;
        foreach (Scn401CardPlacement card in scenario.Cards)
        {
            string cardPath = $"{path}.cards[{card.Id}]";
            if (!IsStableIdentifier(card.Id))
            {
                problems.Add($"{cardPath}.id: must be a stable identifier.");
            }
            else if (!cardIds.Add(card.Id))
            {
                problems.Add($"{cardPath}.id: duplicate card instance ID.");
            }

            if (!IsStableIdentifier(card.DefinitionId))
            {
                problems.Add($"{cardPath}.definitionId: must be a stable definition ID.");
            }

            if (!Enum.IsDefined(card.Zone) || !Enum.IsDefined(card.OwnerSide))
            {
                problems.Add($"{cardPath}: zone and owner side must be valid enum values.");
            }

            bool serverZone = card.Zone is ZoneKind.ServerIce or ZoneKind.ServerRoot;
            if (serverZone != (card.Server is not null))
            {
                problems.Add($"{cardPath}.server: server zones require exactly one server reference.");
            }

            if (card.Server is not null
                && (!IsStableIdentifier(card.Server)
                    || !card.Server.StartsWith("remote:", StringComparison.Ordinal)))
            {
                problems.Add($"{cardPath}.server: only the pinned remote server is supported.");
            }

            if (card.Zone == ZoneKind.CorpHand && card.FaceUp)
            {
                problems.Add($"{cardPath}.faceUp: HQ cost cards must remain hidden.");
            }

            if (card.Id == scenario.SourceCardId)
            {
                source = card;
            }
        }

        if (source is null)
        {
            problems.Add($"{path}.sourceCardId: source card is not present in cards.");
            return;
        }

        bool runEventSource = scenario.Id.StartsWith("jailbreak-", StringComparison.Ordinal);
        if (source.OwnerSide != (runEventSource ? PlayerSide.Runner : PlayerSide.Corp)
            || (!runEventSource
                && scenario.Kind == Scn401TimingCaseKind.ApproachServer
                && (!source.FaceUp || !source.Rezzed)))
        {
            problems.Add(
                $"{path}.sourceCardId: reaction sources must have the owning side required by the case; approach Corp sources must already be face-up and rezzed.");
        }

        if (!runEventSource
            && scenario.Kind == Scn401TimingCaseKind.ApproachServer
            && source.Zone != ZoneKind.ServerRoot)
        {
            problems.Add($"{path}.sourceCardId: approach reactions require a server-root source.");
        }

        if (!runEventSource
            && scenario.Kind == Scn401TimingCaseKind.Encounter
            && source.Zone != ZoneKind.ServerIce)
        {
            problems.Add($"{path}.sourceCardId: encounter reactions require an ICE source.");
        }

        if (runEventSource
            && (source.Zone != ZoneKind.RunnerGrip
                || source.DefinitionId != GatewayBatch4CardCodes.Jailbreak.Value))
        {
            problems.Add($"{path}.sourceCardId: Jailbreak cases require definition 30028 in the Runner grip.");
        }

        if (scenario.Id == "approach-anoetic-void")
        {
            Scn401CardPlacement[] hq = scenario.Cards
                .Where(card => card.Zone == ZoneKind.CorpHand)
                .ToArray();
            if (hq.Length != 2)
            {
                problems.Add($"{path}.cards: Anoetic Void requires exactly two hidden HQ cost cards.");
            }
        }

        if (runEventSource)
        {
            Scn401TimingCaseKind expectedKind = Scn401TimingCaseKind.ApproachServer;
            if (scenario.Kind != expectedKind)
            {
                problems.Add($"{path}.kind: Jailbreak must use the approach-server run-event case kind.");
            }

            ZoneKind hiddenZone = scenario.TargetServer is "headquarters" or "hq"
                ? ZoneKind.CorpHand
                : ZoneKind.CorpDeck;
            Scn401CardPlacement[] hiddenCards = scenario.Cards
                .Where(card => card.Zone == hiddenZone && card.OwnerSide == PlayerSide.Corp)
                .ToArray();
            if (hiddenCards.Length != 2)
            {
                problems.Add($"{path}.cards: Jailbreak cases require exactly two hidden cards in the attacked central zone.");
            }
        }
    }

    private static void ValidateReaction(
        Scn401TimingCase scenario,
        string path,
        List<string> problems)
    {
        Scn401ReactionExpectation reaction = scenario.Reaction;
        if (!IsStableIdentifier(reaction.TriggerId))
        {
            problems.Add($"{path}.reaction.triggerId: must be a stable trigger key.");
        }

        if (!Enum.IsDefined(reaction.Occurrence)
            || !Enum.IsDefined(reaction.Mode)
            || !Enum.IsDefined(reaction.OwnerSide)
            || !Enum.IsDefined(reaction.DecisionKind)
            || !Enum.IsDefined(reaction.RunStep))
        {
            problems.Add($"{path}.reaction: typed occurrence, mode, owner, decision, and run step are required.");
        }

        if (scenario.Id.StartsWith("jailbreak-", StringComparison.Ordinal))
        {
            if (scenario.TargetServer is not ("headquarters" or "hq" or "r&d" or "rnd"))
            {
                problems.Add($"{path}.targetServer: Jailbreak may target only HQ or R&D.");
            }

            if (reaction.Occurrence != OccurrenceKind.RunSuccessful
                || reaction.RunStep != RunTimingStep.BreachCandidateSelection
                || !reaction.MustNotOpenPaidWindow
                || reaction.OwnerSide != PlayerSide.Runner
                || reaction.DecisionKind != DecisionKind.RunTiming)
            {
                problems.Add(
                    $"{path}.reaction: Jailbreak must trigger automatically at RunSuccessful before breach selection without a paid window.");
            }
        }
        else if (scenario.Kind == Scn401TimingCaseKind.ApproachServer)
        {
            if (reaction.Occurrence != OccurrenceKind.ServerApproached
                || reaction.RunStep != RunTimingStep.Success
                || !reaction.MustNotOpenPaidWindow)
            {
                problems.Add($"{path}.reaction: approach reactions must run at ServerApproached before Success without a paid window.");
            }
        }
        else if (reaction.Occurrence != OccurrenceKind.IceEncountered
            || reaction.RunStep != RunTimingStep.EncounterPaidWindow
            || reaction.MustNotOpenPaidWindow)
        {
            problems.Add($"{path}.reaction: Funhouse must run at IceEncountered before the existing encounter paid window.");
        }

        if (string.IsNullOrWhiteSpace(reaction.AcceptedAction)
            || string.IsNullOrWhiteSpace(reaction.Continuation))
        {
            problems.Add($"{path}.reaction: acceptedAction and continuation are required.");
        }
    }

    private static void ValidateBoundaries(
        Scn401TimingCase scenario,
        string path,
        List<string> problems)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Scn401YieldBoundary boundary in scenario.YieldedBoundaries)
        {
            string boundaryPath = $"{path}.yieldedBoundaries[{boundary.Name}]";
            if (!IsStableIdentifier(boundary.Name))
            {
                problems.Add($"{boundaryPath}.name: must be a stable identifier.");
            }
            else if (!names.Add(boundary.Name))
            {
                problems.Add($"{boundaryPath}.name: duplicate yielded boundary.");
            }

            if (!Enum.IsDefined(boundary.DecisionKind)
                || !Enum.IsDefined(boundary.RunStep)
                || !Enum.IsDefined(boundary.OwnerSide)
                || !boundary.SaveLoad)
            {
                problems.Add($"{boundaryPath}: typed decision, run step, owner, and saveLoad=true are required.");
            }
        }

        if (scenario.YieldedBoundaries.Count == 0)
        {
            problems.Add($"{path}.yieldedBoundaries: at least one boundary is required.");
        }
    }

    private static void ValidateExpectedEvents(
        Scn401TimingCase scenario,
        string path,
        List<string> problems)
    {
        if (scenario.ExpectedEventOrder.Count == 0)
        {
            problems.Add($"{path}.expectedEventOrder: at least one event marker is required.");
            return;
        }

        foreach (string marker in scenario.ExpectedEventOrder)
        {
            if (string.IsNullOrWhiteSpace(marker)
                || !IsKnownEventMarker(marker))
            {
                problems.Add($"{path}.expectedEventOrder: unknown event marker '{marker}'.");
            }
        }
    }

    private static bool IsKnownEventMarker(string marker) => marker switch
    {
        "server-approached" => true,
        "run-successful" => true,
        "encounter-initialized" => true,
        "encounter-completed" => true,
        "run-event-played" => true,
        "run-initiated" => true,
        "run-ended" => true,
        "breach-started" => true,
        "card-accessed" => true,
        "subroutines-broken" => true,
        "bioroid-ability-used" => true,
        "card-installed" => true,
        "mayfly-run-end-trash" => true,
        "run-timing-window-opened:breachCandidateSelection" => true,
        "run-timing-window-opened:afterMovePaidWindow" => true,
        "run-timing-window-opened:encounterPaidWindow" => true,
        _ => false,
    };

    private static bool IsStableIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(character => !char.IsWhiteSpace(character) && !char.IsControl(character));
    }

    private static Scn401TimingCase CloneCase(Scn401TimingCase scenario)
    {
        return new Scn401TimingCase(
            scenario.Id,
            scenario.Kind,
            scenario.TargetServer,
            scenario.SourceCardId,
            scenario.RunnerCredits,
            scenario.RunnerClicks,
            scenario.CorpCredits,
            scenario.Cards,
            scenario.Reaction,
            scenario.YieldedBoundaries,
            scenario.ExpectedEventOrder);
    }
}

internal sealed record Scn401CatalogPin
{
    [JsonConstructor]
    public Scn401CatalogPin(string version, IReadOnlyList<string> testOnlyIceDefinitions)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(testOnlyIceDefinitions);
        Version = version;
        TestOnlyIceDefinitions = new ReadOnlyCollection<string>(testOnlyIceDefinitions.ToArray());
    }

    public string Version { get; }

    public IReadOnlyList<string> TestOnlyIceDefinitions { get; }
}

internal enum Scn401TimingCaseKind
{
    ApproachServer,
    Encounter,
}

internal sealed record Scn401TimingCase
{
    [JsonConstructor]
    public Scn401TimingCase(
        string id,
        Scn401TimingCaseKind kind,
        string targetServer,
        string sourceCardId,
        int runnerCredits,
        int runnerClicks,
        int corpCredits,
        IReadOnlyList<Scn401CardPlacement> cards,
        Scn401ReactionExpectation reaction,
        IReadOnlyList<Scn401YieldBoundary> yieldedBoundaries,
        IReadOnlyList<string> expectedEventOrder)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(targetServer);
        ArgumentNullException.ThrowIfNull(sourceCardId);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(reaction);
        ArgumentNullException.ThrowIfNull(yieldedBoundaries);
        ArgumentNullException.ThrowIfNull(expectedEventOrder);
        Id = id;
        Kind = kind;
        TargetServer = targetServer;
        SourceCardId = sourceCardId;
        RunnerCredits = runnerCredits;
        RunnerClicks = runnerClicks;
        CorpCredits = corpCredits;
        Cards = new ReadOnlyCollection<Scn401CardPlacement>(cards.ToArray());
        Reaction = reaction;
        YieldedBoundaries = new ReadOnlyCollection<Scn401YieldBoundary>(yieldedBoundaries.ToArray());
        ExpectedEventOrder = new ReadOnlyCollection<string>(expectedEventOrder.ToArray());
    }

    public string Id { get; }

    public Scn401TimingCaseKind Kind { get; }

    public string TargetServer { get; }

    public string SourceCardId { get; }

    public int RunnerCredits { get; }

    public int RunnerClicks { get; }

    public int CorpCredits { get; }

    public IReadOnlyList<Scn401CardPlacement> Cards { get; }

    public Scn401ReactionExpectation Reaction { get; }

    public IReadOnlyList<Scn401YieldBoundary> YieldedBoundaries { get; }

    public IReadOnlyList<string> ExpectedEventOrder { get; }
}

internal sealed record Scn401CardPlacement
{
    [JsonConstructor]
    public Scn401CardPlacement(
        string id,
        string definitionId,
        ZoneKind zone,
        PlayerSide ownerSide,
        string? server = null,
        bool faceUp = false,
        bool rezzed = false)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(definitionId);
        Id = id;
        DefinitionId = definitionId;
        Zone = zone;
        OwnerSide = ownerSide;
        Server = server;
        FaceUp = faceUp;
        Rezzed = rezzed;
    }

    public string Id { get; }

    public string DefinitionId { get; }

    public ZoneKind Zone { get; }

    public PlayerSide OwnerSide { get; }

    public string? Server { get; }

    public bool FaceUp { get; }

    public bool Rezzed { get; }
}

internal sealed record Scn401ReactionExpectation
{
    [JsonConstructor]
    public Scn401ReactionExpectation(
        OccurrenceKind occurrence,
        string triggerId,
        TriggerMode mode,
        PlayerSide ownerSide,
        DecisionKind decisionKind,
        RunTimingStep runStep,
        string acceptedAction,
        string continuation,
        bool mustNotOpenPaidWindow)
    {
        ArgumentNullException.ThrowIfNull(triggerId);
        ArgumentNullException.ThrowIfNull(acceptedAction);
        ArgumentNullException.ThrowIfNull(continuation);
        Occurrence = occurrence;
        TriggerId = triggerId;
        Mode = mode;
        OwnerSide = ownerSide;
        DecisionKind = decisionKind;
        RunStep = runStep;
        AcceptedAction = acceptedAction;
        Continuation = continuation;
        MustNotOpenPaidWindow = mustNotOpenPaidWindow;
    }

    public OccurrenceKind Occurrence { get; }

    public string TriggerId { get; }

    public TriggerMode Mode { get; }

    public PlayerSide OwnerSide { get; }

    public DecisionKind DecisionKind { get; }

    public RunTimingStep RunStep { get; }

    public string AcceptedAction { get; }

    public string Continuation { get; }

    public bool MustNotOpenPaidWindow { get; }
}

internal sealed record Scn401YieldBoundary
{
    [JsonConstructor]
    public Scn401YieldBoundary(
        string name,
        DecisionKind decisionKind,
        RunTimingStep runStep,
        PlayerSide ownerSide,
        bool saveLoad)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        DecisionKind = decisionKind;
        RunStep = runStep;
        OwnerSide = ownerSide;
        SaveLoad = saveLoad;
    }

    public string Name { get; }

    public DecisionKind DecisionKind { get; }

    public RunTimingStep RunStep { get; }

    public PlayerSide OwnerSide { get; }

    public bool SaveLoad { get; }
}

/// <summary>
/// Builds an authoritative state from a SCN-401 case while keeping server
/// topology and any fixture-only ICE declarations in the Cards test assembly.
/// No production scenario loader is broadened for this evidence.
/// </summary>
internal static class Scn401TimingFixtureBuilder
{
    private static readonly CardDefinitionId PlainIce = new("fixture:scn401-plain-ice");
    private static readonly CardDefinitionId HqCostA = new("fixture:scn401-hq-a");
    private static readonly CardDefinitionId HqCostB = new("fixture:scn401-hq-b");
    private static readonly CardDefinitionId RestrictionAgenda = new("fixture:scn401-agenda");
    private static readonly CardDefinitionId RestrictionAsset = new("fixture:scn401-asset");

    public static BehaviorCatalog CreateBehaviorCatalog(Scn401TimingCase scenario)
    {
        BehaviorCatalog gateway = GatewayBehaviorCatalog.Create();
        BehaviorCatalogBuilder builder = new(gateway.Version, gateway);
        if (scenario.Kind == Scn401TimingCaseKind.ApproachServer)
        {
            AddIceIfMissing(
                builder,
                gateway,
                new IceBehaviorSpec(
                    PlainIce,
                    strength: 0,
                    iceType: IceType.Barrier,
                    rezCost: 0,
                    subroutines: [],
                    strengthModifiers: []));
        }
        else if (scenario.Cards.Any(card => card.DefinitionId == GatewayBatch4CardCodes.Ansel.Value)
            && !gateway.TryGetIce(GatewayBatch4CardCodes.Ansel, out _))
        {
            // Keep the D1 fixture executable while the production ICE binding
            // is being assembled.  Once the Cards-owned Ansel declaration is
            // present this fallback is not added, so the fixture exercises the
            // injected production declaration rather than a title branch.
            builder.AddIce(new IceBehaviorSpec(
                GatewayBatch4CardCodes.Ansel,
                strength: 0,
                iceType: IceType.Sentry,
                rezCost: 0,
                subroutines:
                [
                    new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1),
                    new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1),
                    new EndRunSubroutineSpec(),
                ],
                strengthModifiers: [],
                encounteredBioroidAbilities: GatewayBatch4BioroidBehaviorSpecs.BioroidAbilities));
        }

        foreach (Scn401CardPlacement placement in scenario.Cards)
        {
            if (placement.Zone != ZoneKind.ServerIce)
            {
                continue;
            }

            CardDefinitionId definitionId = new(placement.DefinitionId);
            if (gateway.TryGetIce(definitionId, out _)
                || definitionId == GatewayBatch4CardCodes.Ansel
                || (scenario.Kind == Scn401TimingCaseKind.ApproachServer
                    && definitionId == PlainIce))
            {
                continue;
            }

            if (definitionId == GatewayBatch4CardCodes.Funhouse)
            {
                // D1's encounter-trigger case predates the printed
                // subroutine binding.  Preserve its empty shell until the
                // production Funhouse declaration is available.
                builder.AddIce(new IceBehaviorSpec(
                    definitionId,
                    strength: 0,
                    iceType: IceType.CodeGate,
                    rezCost: 0,
                    subroutines: [],
                    strengthModifiers: []));
                continue;
            }

            // A one-subroutine barrier gives the Mayfly cases a deterministic
            // encounter to break without introducing a second production card
            // behavior registration.
            builder.AddIce(new IceBehaviorSpec(
                definitionId,
                strength: 0,
                iceType: IceType.Barrier,
                rezCost: 0,
                subroutines: [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)],
                strengthModifiers: []));
        }

        if (scenario.Cards.Any(card => card.DefinitionId == RestrictionAgenda.Value)
            && !gateway.TryGetAgenda(RestrictionAgenda, out _))
        {
            builder.AddAgenda(new AgendaBehaviorSpec(RestrictionAgenda, agendaPoints: 1));
        }

        if (scenario.Cards.Any(card => card.DefinitionId == RestrictionAsset.Value)
            && !gateway.TryGetAsset(RestrictionAsset, out _))
        {
            builder.AddAsset(new AssetBehaviorSpec(RestrictionAsset, trashCost: 0));
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds the small authenticated runtime metadata catalog needed by the
    /// fixture's real-card boundaries.  SCN-401 deliberately uses a detached
    /// catalog rather than the process-wide Gateway content build: hidden-zone
    /// cards and the fixture ICE shell are test-owned, while the engine still
    /// receives the same typed class/cost information it would receive from
    /// production card data.
    /// </summary>
    public static RuntimeCardCatalog CreateRuntimeCatalog(Scn401TimingCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var definitions = new Dictionary<CardDefinitionId, CardRuntimeSpec>();
        foreach (Scn401CardPlacement placement in scenario.Cards)
        {
            CardDefinitionId definitionId = new(placement.DefinitionId);
            if (!definitions.ContainsKey(definitionId))
            {
                definitions.Add(definitionId, RuntimeSpec(definitionId, placement));
            }
        }

        return new RuntimeCardCatalog(
            $"scn401-runtime-{scenario.Id}",
            definitions.Values
                .OrderBy(spec => spec.DefinitionId.Value, StringComparer.Ordinal)
                .ToArray());
    }

    public static GameState BuildState(
        Scn401TimingFixtureDocument fixture,
        Scn401TimingCase scenario,
        BehaviorCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(catalog);

        GameState baseline = GameState.CreateNew(
            fixture.Seed,
            fixture.RulesVersion,
            EngineCompatibility.GatewayCardDataVersion,
            catalog.Version,
            catalog.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        ServerId remoteId = allocator.NextServerId();
        ZoneId remoteIceId = allocator.NextZoneId();
        ZoneId remoteRootId = allocator.NextZoneId();
        ServerState remote = new(remoteId, ServerKind.Remote, remoteIceId, remoteRootId);
        ServerState[] servers = baseline.Servers.Append(remote).ToArray();
        ZoneState[] baseZones = baseline.Zones
            .Append(new ZoneState(remoteIceId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId))
            .Append(new ZoneState(remoteRootId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId))
            .ToArray();

        var zoneCards = baseZones.ToDictionary(zone => zone.Id, zone => zone.CardIds.ToList());
        var cards = new List<CardInstanceState>(scenario.Cards.Count);
        foreach (Scn401CardPlacement placement in scenario.Cards)
        {
            ZoneState zone = ResolveZone(baseZones, placement, remoteId);
            CardInstanceState card = new(
                new CardInstanceId(placement.Id),
                new CardDefinitionId(placement.DefinitionId),
                zone.Id,
                placement.OwnerSide,
                placement.FaceUp,
                placement.Rezzed);
            cards.Add(card);
            zoneCards[zone.Id].Add(card.Id);
        }

        ZoneState[] zones = baseZones
            .Select(zone => new ZoneState(
                zone.Id,
                zone.Kind,
                zone.OwnerSide,
                zone.ServerId,
                zoneCards[zone.Id]))
            .ToArray();
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState[] players =
        [
            new PlayerState(corp.Id, PlayerSide.Corp, scenario.CorpCredits, clicks: 3, corp.AgendaPoints),
            new PlayerState(runner.Id, PlayerSide.Runner, scenario.RunnerCredits, scenario.RunnerClicks, runner.AgendaPoints),
        ];
        TurnFactsState facts = new(
            version: 1,
            side: PlayerSide.Runner,
            turnNumber: baseline.Turn.TurnNumber,
            clicksAllotted: scenario.RunnerClicks,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnState turn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            baseline.Turn.TurnNumber,
            TurnStep.RunnerAction,
            new TurnHistoryState(1, null, null, facts, null));

        RuntimeCardCatalog runtime = CreateRuntimeCatalog(scenario);
        RulesState defaults = RulesState.CreateDefault(players);
        PlayerId runnerId = runner.Id;
        ProgramMemoryState[] programs = cards
            .Where(card => card.OwnerSide == PlayerSide.Runner
                && zones.Single(zone => zone.Id == card.ZoneId).Kind == ZoneKind.RunnerRig
                && runtime.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                && spec.CardClass == RuntimeCardClass.Program)
            .Select(card => new ProgramMemoryState(
                card.Id,
                runnerId,
                runtime.GetSpec(card.DefinitionId).MemoryUnits))
            .OrderBy(program => program.CardId.Value, StringComparer.Ordinal)
            .ToArray();
        PlayerRulesState[] rulePlayers = defaults.Players
            .Select(player => player.PlayerId == runnerId
                ? new PlayerRulesState(
                    player.PlayerId,
                    player.BaseHandSize,
                    player.CoreDamage,
                    player.Tags,
                    player.BaseMemory,
                    programs.Sum(program => program.MemoryCost))
                : player)
            .ToArray();
        RulesState rules = new(
            defaults.Version,
            rulePlayers,
            programs,
            defaults.Counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            baseline.DecisionRevision,
            baseline.EventSequence,
            [],
            null,
            baseline.ActionSequence,
            baseline.InitialSeed,
            runState: null,
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        StateInvariantValidator.Validate(state, catalog, runtime);
        return state;
    }

    public static ServerId RemoteId(GameState state) =>
        state.Servers.Single(server => server.Kind == ServerKind.Remote).Id;

    public static ServerId TargetServerId(GameState state, string targetServer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(targetServer);
        if (targetServer.StartsWith("remote:", StringComparison.Ordinal))
        {
            return RemoteId(state);
        }

        ServerKind kind = targetServer switch
        {
            "hq" or "headquarters" => ServerKind.Headquarters,
            "rnd" or "r&d" or "r-and-d" => ServerKind.RAndD,
            "archives" => ServerKind.Archives,
            _ => throw new InvalidOperationException($"Unknown SCN-401 target server '{targetServer}'."),
        };
        return state.Servers.Single(server => server.Kind == kind).Id;
    }

    public static CardInstanceId? TryInitiatingSource(Scn401TimingCase scenario) =>
        scenario.Cards
            .FirstOrDefault(card => card.Zone == ZoneKind.RunnerGrip
                && card.OwnerSide == PlayerSide.Runner
                && card.DefinitionId == GatewayBatch4CardCodes.Jailbreak.Value)
            is { } source
            ? new CardInstanceId(source.Id)
            : null;

    private static ZoneState ResolveZone(
        IReadOnlyList<ZoneState> zones,
        Scn401CardPlacement placement,
        ServerId remoteId)
    {
        if (placement.Zone == ZoneKind.ServerIce)
        {
            return zones.Single(zone => zone.Kind == ZoneKind.ServerIce && zone.ServerId == remoteId);
        }

        if (placement.Zone == ZoneKind.ServerRoot)
        {
            return zones.Single(zone => zone.Kind == ZoneKind.ServerRoot && zone.ServerId == remoteId);
        }

        return zones.Single(zone => zone.Kind == placement.Zone && zone.ServerId is null);
    }

    public static CardDefinitionId HqDefinition(int index) => index % 2 == 0 ? HqCostA : HqCostB;

    private static void AddIceIfMissing(
        BehaviorCatalogBuilder builder,
        BehaviorCatalog catalog,
        IceBehaviorSpec spec)
    {
        if (!catalog.TryGetIce(spec.DefinitionId, out _))
        {
            builder.AddIce(spec);
        }
    }

    private static CardRuntimeSpec RuntimeSpec(
        CardDefinitionId definitionId,
        Scn401CardPlacement placement)
    {
        if (placement.OwnerSide == PlayerSide.Runner)
        {
            if (definitionId.Value == GatewayBatch4CardCodes.Jailbreak.Value)
            {
                return new CardRuntimeSpec(
                    definitionId,
                    PlayerSide.Runner,
                    RuntimeCardClass.Event,
                    playCost: 0);
            }

            if (definitionId.Value == GatewayBatch4CardCodes.Mayfly.Value)
            {
                return new CardRuntimeSpec(
                    definitionId,
                    PlayerSide.Runner,
                    RuntimeCardClass.Program,
                    installCost: 1,
                    memoryUnits: 1,
                    subtypes: ["icebreaker", "ai"]);
            }

            return new CardRuntimeSpec(
                definitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Resource,
                installCost: 0);
        }

        if (definitionId == RestrictionAgenda)
        {
            return new CardRuntimeSpec(
                definitionId,
                PlayerSide.Corp,
                RuntimeCardClass.Agenda,
                agendaPoints: 1,
                advancementRequirement: 1);
        }

        if (definitionId == RestrictionAsset)
        {
            return new CardRuntimeSpec(
                definitionId,
                PlayerSide.Corp,
                RuntimeCardClass.Asset,
                rezCost: 0,
                trashCost: 0);
        }

        if (placement.Zone == ZoneKind.ServerIce
            || definitionId.Value is "30038" or "30039" or "30054"
            || definitionId.Value == PlainIce.Value)
        {
            int rezCost = definitionId.Value switch
            {
                "30038" or "30039" => 6,
                "30054" => 5,
                _ => 0,
            };
            return new CardRuntimeSpec(
                definitionId,
                PlayerSide.Corp,
                RuntimeCardClass.Ice,
                rezCost: rezCost,
                subtypes: definitionId.Value switch
                {
                    "30038" => ["sentry", "bioroid", "destroyer"],
                    "30039" => ["barrier", "bioroid"],
                    "30054" => ["code-gate"],
                    _ => ["barrier"],
                });
        }

        return new CardRuntimeSpec(
            definitionId,
            PlayerSide.Corp,
            RuntimeCardClass.Asset,
            rezCost: 0,
            trashCost: 0);
    }
}
