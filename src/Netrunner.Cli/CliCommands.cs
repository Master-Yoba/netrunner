using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static class CliCommands
{
    private static readonly JsonSerializerOptions DoctorJsonOptions = new() { WriteIndented = true };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args is ["doctor"] or ["doctor", "--json"])
        {
            return RunDoctor(output);
        }

        if (args is ["self-test", "--seed", var seedText]
            && ulong.TryParse(seedText, out ulong seed))
        {
            return RunSelfTest(seed, output);
        }

        if (args.Length > 0 && string.Equals(args[0], "card-data", StringComparison.Ordinal))
        {
            bool requestedJson = args.Skip(1).Any(static argument => argument == "--json");
            if (!TryParseCardDataCommand(
                    args,
                    out CardDataCommandOptions? cardDataOptions,
                    out string? cardDataParseError))
            {
                WriteCardDataFailure(
                    requestedJson,
                    2,
                    cardDataParseError ?? "The card-data command arguments are invalid.",
                    output,
                    error);
                return 2;
            }

            return RunCardData(cardDataOptions!, output, error);
        }

        if (TryParseArtifactCommand(args, "scenario", out string? scenarioPath, out bool scenarioJson))
        {
            return RunScenario(scenarioPath!, scenarioJson, output, error);
        }

        if (TryParseArtifactCommand(args, "replay", out string? replayPath, out bool replayJson))
        {
            return RunReplay(replayPath!, replayJson, output, error);
        }

        if (args.Length > 0 && string.Equals(args[0], "play", StringComparison.Ordinal))
        {
            bool requestedJson = args.Skip(1).Any(static argument => argument == "--json");
            if (!TryParsePlay(args, out PlayCommandOptions? options, out string? parseError))
            {
                WritePlayFailure(
                    requestedJson,
                    2,
                    parseError ?? "The play command arguments are invalid.",
                    output,
                    error);
                return 2;
            }

            return RunPlay(options!, output, error);
        }

        if (args.Length > 0 && string.Equals(args[0], "m2-proof", StringComparison.Ordinal))
        {
            bool requestedJson = args.Skip(1).Any(static argument => argument == "--json");
            if (!TryParseM2ProofCommand(args, out string? m2ScenarioPath, out bool m2Json, out string? m2ParseError))
            {
                WriteM2ProofFailure(
                    requestedJson,
                    2,
                    m2ParseError ?? "The m2-proof command arguments are invalid.",
                    output,
                    error);
                return 2;
            }

            return RunM2Proof(m2ScenarioPath!, m2Json, output, error);
        }

        error.WriteLine(
            "Usage: Netrunner.Cli doctor [--json] | self-test --seed <unsigned-integer> | "
            + "scenario <file> [--json] | replay <file> [--json] | "
            + "card-data normalize --upstream <file> --copyright <file> --commit <40-hex-sha> "
            + "--retrieval-date YYYY-MM-DD --output-dir <directory> [--json] | "
            + "card-data validate [--root <directory>] [--json] | "
            + "play --seed <unsigned-integer> --goal corp-score|runner-steal|deck-out "
            + "[--json] [--replay-out <file>] | "
            + "m2-proof [scenarioPath] [--json]");
        return 2;
    }

    private static int RunDoctor(TextWriter output)
    {
        string? godotPath = Environment.GetEnvironmentVariable("GODOT4");
        bool godotAvailable = godotPath is not null && File.Exists(godotPath);
        var report = new
        {
            status = godotAvailable ? "ok" : "incomplete",
            application = BuildMetadata.Current,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            godot = new
            {
                path = godotPath,
                available = godotAvailable,
            },
        };

        output.WriteLine(JsonSerializer.Serialize(report, DoctorJsonOptions));
        return godotAvailable ? 0 : 1;
    }

    private static int RunSelfTest(ulong seed, TextWriter output)
    {
        DeterministicRandom first = new(seed);
        ulong[] values = Enumerable.Range(0, 4).Select(_ => first.NextUInt64()).ToArray();
        DeterministicRandom restored = DeterministicRandom.Restore(first.State);

        output.WriteLine($"version={BuildMetadata.Current.ApplicationVersion}");
        output.WriteLine($"rules={BuildMetadata.Current.RulesVersion}");
        output.WriteLine($"seed={seed}");
        output.WriteLine($"sample={string.Join(',', values)}");
        output.WriteLine($"restored-next={restored.NextUInt64()}");
        return 0;
    }

    private static int RunCardData(
        CardDataCommandOptions options,
        TextWriter output,
        TextWriter error)
    {
        return options.Operation switch
        {
            CardDataOperation.Normalize => RunCardDataNormalize(options, output, error),
            CardDataOperation.Validate => RunCardDataValidate(options, output, error),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Operation, "Unknown card-data operation."),
        };
    }

    private static int RunCardDataNormalize(
        CardDataCommandOptions options,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            byte[] upstreamBytes = ReadUtf8File(options.UpstreamPath!, "upstream card data");
            IReadOnlyList<NormalizedCardDefinition> cards =
                CardDataNormalizer.Normalize(upstreamBytes, CardDataValidator.OfficialPackCode);
            if (cards.Count != CardDataValidator.OfficialCardCount)
            {
                throw new CardDataValidationException(
                    $"Expected {CardDataValidator.OfficialCardCount} System Gateway records, found {cards.Count}.");
            }

            byte[] copyrightBytes = ReadNormalizedCopyright(options.CopyrightPath!);
            byte[] cardsBytes = CardDataJson.SerializeCards(cards);
            var manifest = new CardDataManifest(
                CardDataValidator.CurrentManifestSchemaVersion,
                CardDataValidator.OfficialSourceUrl,
                options.CommitSha!,
                options.RetrievalDate!,
                "v1",
                CardDataValidator.CurrentNormalizedSchemaVersion,
                CardDataJson.ComputeSha256Hex(cardsBytes),
                "COPYRIGHT.md",
                "NOASSERTION",
                "pack/sg.json",
                CardDataValidator.OfficialPackCode,
                CardDataValidator.OfficialPackName,
                cards.Count);
            CardDataValidator.Validate(cards, manifest, requireOfficialGateway: true);
            byte[] manifestBytes = CardDataJson.SerializeManifest(manifest);
            string outputDirectory = Path.GetFullPath(options.OutputDirectory!);
            WriteCardDataDirectory(outputDirectory, cardsBytes, manifestBytes, copyrightBytes);

            CardDataSet reloaded = CardDataLoader.Load(
                Path.Combine(outputDirectory, "cards.json"),
                Path.Combine(outputDirectory, "manifest.json"),
                requireOfficialGateway: true);
            string copyrightSha = CardDataJson.ComputeSha256Hex(copyrightBytes);
            CardDataCommandOutput result = CreateCardDataOutput(
                "normalize",
                reloaded,
                outputDirectory,
                copyrightSha,
                null);
            WriteCardDataOutput(result, options.Json, output, error);
            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            WriteCardDataFailure(options.Json, 1, exception.Message, output, error);
            return 1;
        }
    }

    private static int RunCardDataValidate(
        CardDataCommandOptions options,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            CardDataSet data = CardDataLoader.LoadSystemGateway(options.RootDirectory);
            string dataDirectory = Path.Combine(
                Path.GetFullPath(options.RootDirectory ?? Directory.GetCurrentDirectory()),
                CardDataLoader.DefaultRelativeDirectory);
            string copyrightPath = Path.Combine(dataDirectory, "SOURCE_COPYRIGHT.md");
            CardDataCommandOutput result = CreateCardDataOutput(
                "validate",
                data,
                dataDirectory,
                CardDataJson.ComputeSha256Hex(ReadFileBytes(copyrightPath, "source copyright")),
                null);
            WriteCardDataOutput(result, options.Json, output, error);
            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            WriteCardDataFailure(options.Json, 1, exception.Message, output, error);
            return 1;
        }
    }

    private static CardDataCommandOutput CreateCardDataOutput(
        string operation,
        CardDataSet data,
        string outputDirectory,
        string? copyrightSha,
        string? message)
    {
        NormalizedCardDefinition first = data.Cards[0];
        NormalizedCardDefinition last = data.Cards[^1];
        return new CardDataCommandOutput(
            "ok",
            operation,
            data.Cards.Count,
            data.Manifest.OutputSha256,
            copyrightSha,
            data.Manifest.CommitSha,
            first.Code,
            first.Title,
            last.Code,
            last.Title,
            outputDirectory,
            message);
    }

    private static void WriteCardDataOutput(
        CardDataCommandOutput result,
        bool json,
        TextWriter output,
        TextWriter error)
    {
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(result));
            return;
        }

        output.WriteLine($"card-data={result.Operation}-ok");
        output.WriteLine($"count={result.CardCount}");
        output.WriteLine($"cards-sha256={result.OutputSha256}");
        if (result.CopyrightSha256 is not null)
        {
            output.WriteLine($"copyright-sha256={result.CopyrightSha256}");
        }

        output.WriteLine($"commit={result.CommitSha}");
        output.WriteLine($"first={result.FirstCode} {result.FirstTitle}");
        output.WriteLine($"last={result.LastCode} {result.LastTitle}");
        output.WriteLine($"directory={result.OutputDirectory}");
        _ = error;
    }

    private static void WriteCardDataFailure(
        bool json,
        int exitCode,
        string message,
        TextWriter output,
        TextWriter error)
    {
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new CardDataCommandOutput(
                "failed",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                message)));
        }
        else
        {
            error.WriteLine($"card-data failed (exit {exitCode}): {message}");
        }
    }

    private static byte[] ReadUtf8File(string path, string description)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetBytes(File.ReadAllText(path, new UTF8Encoding(false, true)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new CardDataValidationException($"Unable to read UTF-8 {description} at '{path}'.", exception);
        }
    }

    private static byte[] ReadNormalizedCopyright(string path)
    {
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(ReadFileBytes(path, "source copyright"));
        }
        catch (DecoderFallbackException exception)
        {
            throw new CardDataValidationException(
                $"Unable to read UTF-8 source copyright at '{path}'.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new CardDataValidationException("The source copyright file must not be empty.");
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (!text.EndsWith('\n'))
        {
            text += "\n";
        }

        return new UTF8Encoding(false).GetBytes(text);
    }

    private static byte[] ReadFileBytes(string path, string description)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CardDataValidationException($"Unable to read {description} at '{path}'.", exception);
        }
    }

    private static void WriteCardDataDirectory(
        string directory,
        byte[] cardsBytes,
        byte[] manifestBytes,
        byte[] copyrightBytes)
    {
        Directory.CreateDirectory(directory);
        string[] allowed = ["cards.json", "manifest.json", "SOURCE_COPYRIGHT.md"];
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (!allowed.Contains(Path.GetFileName(entry), StringComparer.Ordinal))
            {
                throw new CardDataValidationException(
                    $"Refusing to normalize into '{directory}': unexpected existing entry '{entry}'.");
            }

            throw new CardDataValidationException(
                $"Refusing to overwrite existing generated file '{entry}'; use a fresh staging directory.");
        }

        WriteNewFile(Path.Combine(directory, "cards.json"), cardsBytes);
        WriteNewFile(Path.Combine(directory, "manifest.json"), manifestBytes);
        WriteNewFile(Path.Combine(directory, "SOURCE_COPYRIGHT.md"), copyrightBytes);
    }

    private static void WriteNewFile(string path, byte[] bytes)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CardDataValidationException($"Unable to write generated card-data file '{path}'.", exception);
        }
    }

    private static bool TryParseCardDataCommand(
        string[] args,
        out CardDataCommandOptions? options,
        out string? error)
    {
        options = null;
        error = null;
        if (args.Length < 2 || !string.Equals(args[0], "card-data", StringComparison.Ordinal))
        {
            error = "The card-data command must specify normalize or validate.";
            return false;
        }

        CardDataOperation operation = args[1] switch
        {
            "normalize" => CardDataOperation.Normalize,
            "validate" => CardDataOperation.Validate,
            _ => 0,
        };
        if (operation == 0)
        {
            error = $"Unknown card-data operation '{args[1]}'; expected normalize or validate.";
            return false;
        }

        string? upstreamPath = null;
        string? copyrightPath = null;
        string? commitSha = null;
        string? retrievalDate = null;
        string? outputDirectory = null;
        string? rootDirectory = null;
        bool json = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 2; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument == "--json")
            {
                if (!seen.Add(argument))
                {
                    error = "The --json option may be specified only once.";
                    return false;
                }

                json = true;
                continue;
            }

            if (argument is not ("--upstream" or "--copyright" or "--commit" or "--retrieval-date" or "--output-dir" or "--root"))
            {
                error = $"Unknown card-data option '{argument}'.";
                return false;
            }

            if (!seen.Add(argument))
            {
                error = $"The {argument} option may be specified only once.";
                return false;
            }

            if (!TryReadOptionValue(args, ref index, argument, out string? value, out error))
            {
                return false;
            }

            switch (argument)
            {
                case "--upstream":
                    if (operation != CardDataOperation.Normalize)
                    {
                        error = "--upstream is valid only with card-data normalize.";
                        return false;
                    }

                    upstreamPath = value;
                    break;
                case "--copyright":
                    if (operation != CardDataOperation.Normalize)
                    {
                        error = "--copyright is valid only with card-data normalize.";
                        return false;
                    }

                    copyrightPath = value;
                    break;
                case "--commit":
                    if (operation != CardDataOperation.Normalize)
                    {
                        error = "--commit is valid only with card-data normalize.";
                        return false;
                    }

                    if (!IsLowerFullSha(value!))
                    {
                        error = "--commit must be an exact lowercase 40-hex commit SHA.";
                        return false;
                    }

                    commitSha = value;
                    break;
                case "--retrieval-date":
                    if (operation != CardDataOperation.Normalize)
                    {
                        error = "--retrieval-date is valid only with card-data normalize.";
                        return false;
                    }

                    if (!IsIsoDate(value!))
                    {
                        error = "--retrieval-date must be an ISO date in YYYY-MM-DD form.";
                        return false;
                    }

                    retrievalDate = value;
                    break;
                case "--output-dir":
                    if (operation != CardDataOperation.Normalize)
                    {
                        error = "--output-dir is valid only with card-data normalize.";
                        return false;
                    }

                    outputDirectory = value;
                    break;
                case "--root":
                    if (operation != CardDataOperation.Validate)
                    {
                        error = "--root is valid only with card-data validate.";
                        return false;
                    }

                    rootDirectory = value;
                    break;
            }
        }

        if (operation == CardDataOperation.Normalize)
        {
            if (upstreamPath is null || copyrightPath is null || commitSha is null
                || retrievalDate is null || outputDirectory is null)
            {
                error = "card-data normalize requires --upstream, --copyright, --commit, --retrieval-date, and --output-dir.";
                return false;
            }
        }

        options = new CardDataCommandOptions(
            operation,
            json,
            upstreamPath,
            copyrightPath,
            commitSha,
            retrievalDate,
            outputDirectory,
            rootDirectory);
        return true;
    }

    private static bool IsLowerFullSha(string value) =>
        value.Length == 40
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsIsoDate(string value) =>
        value.Length == 10
        && DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);

    private static int RunScenario(string path, bool json, TextWriter output, TextWriter error)
    {
        try
        {
            ScenarioArtifact artifact = EngineJson.Deserialize<ScenarioArtifact>(File.ReadAllText(path));
            GameState state = ScenarioBuilder.Build(artifact);
            string hash = DeterministicHash.ForState(state);
            if (json)
            {
                output.WriteLine(SerializeScenarioJson(state, hash));
            }
            else
            {
                output.WriteLine("scenario=ok");
                output.WriteLine($"state-hash={hash}");
                output.WriteLine($"players={state.Players.Count} cards={state.Cards.Count} zones={state.Zones.Count}");
            }

            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json)
            {
                output.WriteLine(EngineJson.Serialize(new ScenarioFailureCommandOutput("failed", exception.Message)));
            }
            else
            {
                error.WriteLine($"scenario failed: {exception.Message}");
            }

            return 1;
        }
    }

    private static int RunReplay(string path, bool json, TextWriter output, TextWriter error)
    {
        try
        {
            ReplayArtifact artifact = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(path));
            ReplayVerificationResult result = ReplayVerifier.Verify(artifact);
            if (json)
            {
                output.WriteLine(EngineJson.Serialize(new ReplayCommandOutput(
                    result.Success ? "ok" : "failed",
                    result.Message,
                    result.ActionSequence,
                    result.ExpectedHash,
                    result.ActualHash,
                    result.FinalStateHash)));
            }
            else if (result.Success)
            {
                output.WriteLine("replay=ok");
                output.WriteLine($"final-state-hash={result.FinalStateHash}");
            }
            else
            {
                error.WriteLine($"replay failed: {result.Message}");
            }

            return result.Success ? 0 : 1;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json)
            {
                output.WriteLine(EngineJson.Serialize(new ReplayCommandOutput(
                    "failed",
                    exception.Message,
                    null,
                    null,
                    null,
                    null)));
            }
            else
            {
                error.WriteLine($"replay failed: {exception.Message}");
            }

            return 1;
        }
    }

    private static int RunPlay(PlayCommandOptions options, TextWriter output, TextWriter error)
    {
        try
        {
            ToyMatchResult result = ToyMatchRunner.Run(options.Seed, options.Goal);
            string? replayPath = options.ReplayPath is null
                ? null
                : WriteReplayAtomically(result.Replay, options.ReplayPath);

            if (options.Json)
            {
                output.WriteLine(EngineJson.Serialize(new PlayCommandOutput(
                    "ok",
                    result.Seed,
                    FormatGoal(result.Goal),
                    result.Winner?.ToString(),
                    result.Reason?.ToString(),
                    result.ActionCount,
                    result.FinalStateHash,
                    result.EventStreamHash,
                    replayPath,
                    null)));
            }
            else
            {
                output.WriteLine("play=ok");
                output.WriteLine($"seed={result.Seed}");
                output.WriteLine($"goal={FormatGoal(result.Goal)}");
                output.WriteLine($"winner={result.Winner?.ToString() ?? "unknown"}");
                output.WriteLine($"reason={result.Reason?.ToString() ?? "unknown"}");
                output.WriteLine($"action-count={result.ActionCount}");
                output.WriteLine($"final-state-hash={result.FinalStateHash}");
                output.WriteLine($"event-stream-hash={result.EventStreamHash}");
                if (replayPath is not null)
                {
                    output.WriteLine($"replay-path={replayPath}");
                }
            }

            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            WritePlayFailure(options.Json, 1, exception.Message, output, error);
            return 1;
        }
    }

    private static bool TryParsePlay(
        string[] args,
        out PlayCommandOptions? options,
        out string? error)
    {
        options = null;
        error = null;
        if (args.Length == 0 || !string.Equals(args[0], "play", StringComparison.Ordinal))
        {
            error = "The play command must start with 'play'.";
            return false;
        }

        ulong seed = 0;
        ToyGoal goal = default;
        string? replayPath = null;
        bool seedSeen = false;
        bool goalSeen = false;
        bool jsonSeen = false;
        bool replayPathSeen = false;

        for (int index = 1; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--seed":
                    if (seedSeen)
                    {
                        error = "The --seed option may be specified only once.";
                        return false;
                    }

                    if (!TryReadOptionValue(args, ref index, "--seed", out string? seedText, out error))
                    {
                        return false;
                    }

                    if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                    {
                        error = $"Invalid --seed value '{seedText}'; expected an unsigned integer.";
                        return false;
                    }

                    seedSeen = true;
                    break;
                case "--goal":
                    if (goalSeen)
                    {
                        error = "The --goal option may be specified only once.";
                        return false;
                    }

                    if (!TryReadOptionValue(args, ref index, "--goal", out string? goalText, out error))
                    {
                        return false;
                    }

                    if (!TryParseGoal(goalText!, out goal))
                    {
                        error = $"Invalid --goal value '{goalText}'; expected corp-score, runner-steal, or deck-out.";
                        return false;
                    }

                    goalSeen = true;
                    break;
                case "--json":
                    if (jsonSeen)
                    {
                        error = "The --json option may be specified only once.";
                        return false;
                    }

                    jsonSeen = true;
                    break;
                case "--replay-out":
                    if (replayPathSeen)
                    {
                        error = "The --replay-out option may be specified only once.";
                        return false;
                    }

                    if (!TryReadOptionValue(args, ref index, "--replay-out", out replayPath, out error))
                    {
                        return false;
                    }

                    replayPathSeen = true;
                    break;
                default:
                    error = $"Unknown or misplaced play option '{argument}'.";
                    return false;
            }
        }

        if (!seedSeen)
        {
            error = "The play command requires --seed <unsigned-integer>.";
            return false;
        }

        if (!goalSeen)
        {
            error = "The play command requires --goal corp-score|runner-steal|deck-out.";
            return false;
        }

        options = new PlayCommandOptions(seed, goal, jsonSeen, replayPath);
        return true;
    }

    private static bool TryReadOptionValue(
        string[] args,
        ref int index,
        string option,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        if (index + 1 >= args.Length)
        {
            error = $"The {option} option requires a value.";
            return false;
        }

        string candidate = args[++index];
        if (candidate.Length == 0 || candidate.StartsWith("--", StringComparison.Ordinal))
        {
            error = $"The {option} option requires a value.";
            return false;
        }

        value = candidate;
        return true;
    }

    private static int RunM2Proof(string scenarioPath, bool json, TextWriter output, TextWriter error)
    {
        M2ScenarioDocument document;
        try
        {
            document = EngineJson.Deserialize<M2ScenarioDocument>(File.ReadAllText(scenarioPath));
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            WriteM2ProofFailure(json, 2, exception.Message, output, error);
            return 2;
        }

        M2ScenarioRunSpec? runSpec = document.Run;
        if (runSpec is null)
        {
            WriteM2ProofFailure(json, 2, $"scenario '{document.ScenarioId}' has no run contract.", output, error);
            return 2;
        }

        if (string.IsNullOrWhiteSpace(runSpec.Breaker))
        {
            WriteM2ProofFailure(
                json,
                2,
                $"scenario '{document.ScenarioId}' run contract does not name an icebreaker.",
                output,
                error);
            return 2;
        }

        var contract = new RunPlayContract(runSpec.Server, runSpec.Source, runSpec.Breaker, runSpec.MaxPumps);
        M2ProofResult scripted;
        M2ProofResult agent;
        try
        {
            scripted = M2ProofRunner.Run(document, new ScriptedHumanRunPolicy(contract), "scripted-human");
            agent = M2ProofRunner.Run(document, new RunContractAgent(contract), "contract-agent");
        }
        // InvalidOperationException covers M2ProofException, resolution and
        // state-invariant failures from the proof run, and scenario setup
        // validation errors.
        catch (Exception exception) when (exception is M2ProofException or M2ScenarioException or InvalidOperationException)
        {
            WriteM2ProofFailure(json, 1, exception.Message, output, error);
            return 1;
        }

        if (!string.Equals(scripted.FinalStateHash, agent.FinalStateHash, StringComparison.Ordinal)
            || !string.Equals(scripted.EventStreamHash, agent.EventStreamHash, StringComparison.Ordinal))
        {
            WriteM2ProofFailure(
                json,
                1,
                "the policies diverged: scripted-human reached final state " + scripted.FinalStateHash
                + " / event stream " + scripted.EventStreamHash
                + " but contract-agent reached " + agent.FinalStateHash
                + " / " + agent.EventStreamHash + ".",
                output,
                error);
            return 1;
        }

        bool replayVerified = ReplayVerifier.Verify(
            scripted.ReplayArtifact,
            GatewayBehaviorCatalog.Create()).Success;
        if (json)
        {
            var policies = new Dictionary<string, M2ProofPolicyOutput>(StringComparer.Ordinal)
            {
                [scripted.PolicyName] = new M2ProofPolicyOutput(scripted.FinalStateHash, scripted.EventStreamHash),
                [agent.PolicyName] = new M2ProofPolicyOutput(agent.FinalStateHash, agent.EventStreamHash),
            };
            output.WriteLine(EngineJson.Serialize(new M2ProofCommandOutput(
                document.ScenarioId,
                scripted.InitialStateHash,
                scripted.FinalStateHash,
                scripted.EventStreamHash,
                scripted.Checkpoints.Count,
                scripted.ReplayActions.Count,
                replayVerified,
                scripted.Trace.Problems,
                policies)));
        }
        else
        {
            output.WriteLine("m2-proof=ok");
            foreach (M2ProofResult result in (M2ProofResult[])[scripted, agent])
            {
                output.WriteLine(
                    $"policy={result.PolicyName} final-state-hash={result.FinalStateHash} event-stream-hash={result.EventStreamHash}");
            }

            output.WriteLine($"checkpoints={scripted.Checkpoints.Count}");
            output.WriteLine($"replay-actions={scripted.ReplayActions.Count}");
            output.WriteLine($"replay-verified={replayVerified.ToString().ToLowerInvariant()}");
            output.Write(scripted.Trace.RenderText());
        }

        return 0;
    }

    private static bool TryParseM2ProofCommand(
        string[] args,
        out string? path,
        out bool json,
        out string? error)
    {
        path = null;
        json = false;
        error = null;
        if (args.Length == 0 || !string.Equals(args[0], "m2-proof", StringComparison.Ordinal))
        {
            error = "The m2-proof command must start with 'm2-proof'.";
            return false;
        }

        string? scenarioPath = null;
        bool jsonSeen = false;
        for (int index = 1; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument == "--json")
            {
                if (jsonSeen)
                {
                    error = "The --json option may be specified only once.";
                    return false;
                }

                jsonSeen = true;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unknown m2-proof option '{argument}'.";
                return false;
            }

            if (scenarioPath is not null)
            {
                error = "The m2-proof command accepts at most one scenario path.";
                return false;
            }

            scenarioPath = argument;
        }

        path = scenarioPath ?? "data/m2/representative-run.json";
        json = jsonSeen;
        return true;
    }

    private static void WriteM2ProofFailure(
        bool json,
        int exitCode,
        string message,
        TextWriter output,
        TextWriter error)
    {
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new M2ProofFailureCommandOutput("failed", message)));
        }
        else
        {
            error.WriteLine($"m2-proof failed (exit {exitCode}): {message}");
        }
    }

    private static bool TryParseGoal(string value, out ToyGoal goal)
    {
        goal = value switch
        {
            "corp-score" => ToyGoal.CorpScore,
            "runner-steal" => ToyGoal.RunnerSteal,
            "deck-out" => ToyGoal.DeckOut,
            _ => default,
        };
        return value is "corp-score" or "runner-steal" or "deck-out";
    }

    private static string FormatGoal(ToyGoal goal) => goal switch
    {
        ToyGoal.CorpScore => "corp-score",
        ToyGoal.RunnerSteal => "runner-steal",
        ToyGoal.DeckOut => "deck-out",
        _ => throw new ArgumentOutOfRangeException(nameof(goal), goal, "Unknown toy goal."),
    };

    private static void WritePlayFailure(
        bool json,
        int exitCode,
        string message,
        TextWriter output,
        TextWriter error)
    {
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new PlayCommandOutput(
                "failed",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                message)));
        }
        else
        {
            error.WriteLine($"play failed (exit {exitCode}): {message}");
        }
    }

    private static string WriteReplayAtomically(ReplayArtifact replay, string path)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A replay output path is required.", nameof(path));
        }

        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination)
            ?? throw new IOException("The replay output path has no destination directory.");
        string fileName = Path.GetFileName(destination);
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new ArgumentException("The replay output path must name a file.", nameof(path));
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"The replay output directory '{directory}' does not exist.");
        }

        string temporaryPath = Path.Combine(directory, $".{fileName}.tmp");
        bool temporaryCreated = false;
        bool moved = false;
        try
        {
            byte[] bytes = CanonicalJson.SerializeUtf8(replay);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       options: FileOptions.WriteThrough))
            {
                temporaryCreated = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destination))
            {
                File.Move(temporaryPath, destination, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, destination);
            }

            moved = true;
            return destination;
        }
        finally
        {
            if (temporaryCreated && !moved)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Preserve the original write/move failure.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the original write/move failure.
                }
            }
        }
    }

    private static string SerializeScenarioJson(GameState state, string hash)
    {
        using JsonDocument stateDocument = JsonDocument.Parse(CanonicalJson.Serialize(state));
        return EngineJson.Serialize(new ScenarioCommandOutput(
            "ok",
            hash,
            stateDocument.RootElement.Clone()));
    }

    private static bool TryParseArtifactCommand(
        string[] args,
        string command,
        out string? path,
        out bool json)
    {
        path = null;
        json = false;
        if (args.Length < 2 || !string.Equals(args[0], command, StringComparison.Ordinal))
        {
            return false;
        }

        if (args.Length > 3 || (args.Length == 3 && args[2] != "--json"))
        {
            return false;
        }

        path = args[1];
        json = args.Length == 3;
        return true;
    }

    private static bool IsCommandFailure(Exception exception) =>
        exception is IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException
        or CardDataValidationException
        or JsonException
        or StateInvariantException
        or ArgumentException
        or InvalidOperationException
        or KeyNotFoundException
        or OverflowException;

    private sealed record ScenarioCommandOutput(
        string Status,
        string StateHash,
        JsonElement State);

    private sealed record ScenarioFailureCommandOutput(
        string Status,
        string Message);

    private sealed record ReplayCommandOutput(
        string Status,
        string Message,
        long? ActionSequence,
        string? ExpectedHash,
        string? ActualHash,
        string? FinalStateHash);

    private sealed record PlayCommandOptions(
        ulong Seed,
        ToyGoal Goal,
        bool Json,
        string? ReplayPath);

    private sealed record PlayCommandOutput(
        string Status,
        ulong? Seed,
        string? Goal,
        string? Winner,
        string? Reason,
        int? ActionCount,
        string? FinalStateHash,
        string? EventStreamHash,
        string? ReplayPath,
        string? Message);

    private sealed record M2ProofCommandOutput(
        string ScenarioId,
        string InitialStateHash,
        string FinalStateHash,
        string EventStreamHash,
        int Checkpoints,
        int ReplayActions,
        bool ReplayVerified,
        IReadOnlyList<string> TraceProblems,
        Dictionary<string, M2ProofPolicyOutput> Policies);

    private sealed record M2ProofPolicyOutput(
        string FinalStateHash,
        string EventStreamHash);

    private sealed record M2ProofFailureCommandOutput(
        string Status,
        string Message);

    private enum CardDataOperation
    {
        Normalize = 1,
        Validate = 2,
    }

    private sealed record CardDataCommandOptions(
        CardDataOperation Operation,
        bool Json,
        string? UpstreamPath,
        string? CopyrightPath,
        string? CommitSha,
        string? RetrievalDate,
        string? OutputDirectory,
        string? RootDirectory);

    private sealed record CardDataCommandOutput(
        string Status,
        string? Operation,
        int? CardCount,
        string? OutputSha256,
        string? CopyrightSha256,
        string? CommitSha,
        string? FirstCode,
        string? FirstTitle,
        string? LastCode,
        string? LastTitle,
        string? OutputDirectory,
        string? Message);
}
