using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    private static int RunSaveValidate(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        try
        {
            if (args.Length is < 2 or > 3 || args[1].StartsWith("--", StringComparison.Ordinal)
                || args.Length == 3 && args[2] != "--json")
                throw new ArgumentException("save-validate requires a solo save file and optional --json.");
            SoloSaveDocument save = SoloSaveStore.Read(args[1]);
            save.ValidateIntegrity();
            FullMatchDefinition definition = save.Payload.Snapshot.BehaviorRegistryVersion == DeveloperMatchCards.BehaviorVersion
                ? FullMatchDefinition.Developer(save.Payload.Request.Seed)
                : FullMatchDefinition.FromRequest(save.Payload.Request, CardDataResource.LoadEmbedded(), GatewayBehaviorCatalog.Create());
            if (MatchRequestJson.Serialize(definition.Request) != MatchRequestJson.Serialize(save.Payload.Request))
                throw new InvalidOperationException("The saved request does not match the installed match definition.");
            GameState state = save.Validate(definition.Behavior, definition.Runtime);
            var result = new { Status = "ok", save.Preview, state.ActionSequence, save.EngineStateHash, save.PolicyStateHash };
            if (json) output.WriteLine(EngineJson.Serialize(result));
            else output.WriteLine($"save-validate=ok name={save.Metadata.Name} action-sequence={state.ActionSequence} engine-state={save.EngineStateHash} policy-state={save.PolicyStateHash}");
            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { Status = "failed", exception.Message }));
            else error.WriteLine($"save-validate failed: {exception.Message}");
            return 1;
        }
    }
}
