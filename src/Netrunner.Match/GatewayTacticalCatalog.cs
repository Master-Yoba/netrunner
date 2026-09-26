using System.Collections.Immutable;
using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Executable counterparts of the frozen M5.0 case intentions.</summary>
public sealed partial class GatewayTacticalCatalog
{
    public const string BuilderVersion = "gateway-tactics-v1";
    private readonly CardDataSet _data;
    private readonly BehaviorCatalog _behavior;
    private readonly RuntimeCardCatalog _runtime;

    public GatewayTacticalCatalog(CardDataSet data)
    {
        if (data.Manifest.PackCode != CardDataValidator.OfficialPackCode
            || data.Manifest.CommitSha != CardDataValidator.OfficialCommitSha || data.Cards.Count != 77)
            throw new ArgumentException("Gateway tactics require the pinned, locally provisioned 77-card official metadata.", nameof(data));
        _data = data;
        _behavior = GatewayBehaviorCatalog.Create();
        _runtime = GatewayRuntimeCardCatalog.Create(data);
    }

    public BenchmarkPins Pins => BenchmarkPins.Capture(_data, _behavior, _runtime);
    public static ImmutableArray<TacticalCaseMetadata> Specifications { get; } = LoadSpecifications();

    public static ImmutableArray<string> ImplementedIds { get; } = Specifications
        .Select(specification => specification.Id).ToImmutableArray();

    internal TacticalFixture Build(string id)
    {
        TacticalCaseMetadata metadata = Specifications.Single(value => value.Id == id);
        return metadata.Category switch
        {
            "opening" => BuildOpening(metadata),
            "rig" when metadata.Side == PlayerSide.Runner => BuildRunnerRig(metadata),
            "encounter" when metadata.Side == PlayerSide.Runner => BuildRunnerEncounter(metadata),
            "targets" when metadata.Side == PlayerSide.Runner => BuildRunnerTargets(metadata),
            "access" when metadata.Side == PlayerSide.Runner => BuildRunnerAccess(metadata),
            "defense" when metadata.Side == PlayerSide.Runner => BuildRunnerDefense(metadata),
            "choices" when metadata.Side == PlayerSide.Runner => BuildRunnerChoices(metadata),
            "payments" when metadata.Side == PlayerSide.Runner => BuildRunnerPayments(metadata),
            "development" when metadata.Side == PlayerSide.Corp => BuildCorpDevelopment(metadata),
            "replacement" when metadata.Side == PlayerSide.Corp => BuildCorpReplacement(metadata),
            "scoring" when metadata.Side == PlayerSide.Corp => BuildCorpScoring(metadata),
            "rez" when metadata.Side == PlayerSide.Corp => BuildCorpRez(metadata),
            "pressure" when metadata.Side == PlayerSide.Corp => BuildCorpPressure(metadata),
            "choices" when metadata.Side == PlayerSide.Corp => BuildCorpChoices(metadata),
            "information" when metadata.Side == PlayerSide.Corp => BuildCorpInformation(metadata),
            _ => throw new NotSupportedException($"Tactical builder '{id}' is not implemented yet."),
        };
    }

    private TacticalBoard Board() => new(_behavior, _runtime, _data.Manifest.CommitSha);

    private static ImmutableArray<TacticalCaseMetadata> LoadSpecifications()
    {
        using Stream stream = typeof(GatewayTacticalCatalog).Assembly.GetManifestResourceStream("Netrunner.Match.Data.m5.tactical-v1.json")
            ?? throw new InvalidOperationException("M5 tactical specifications are missing.");
        using JsonDocument document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("positions").EnumerateArray().Select(position =>
        {
            string Text(string key) => position.GetProperty(key).GetString()!;
            return new TacticalCaseMetadata(Text("id"), Text("side") == "corp" ? PlayerSide.Corp : PlayerSide.Runner,
                Text("category"), Text("split"), position.GetProperty("critical").GetBoolean(), Text("title"), Text("preconditions"), Text("acceptance"));
        }).ToImmutableArray();
    }
}
