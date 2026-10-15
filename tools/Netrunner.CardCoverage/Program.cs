using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Netrunner.CardCoverage;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            CoverageInput input = CoverageInput.Load(options);
            CoverageReport report = CoverageReport.Create(input);
            report.Write(options.MarkdownPath, options.JsonPath);

            foreach (string diagnostic in report.Diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }

            foreach (SetReport set in report.Sets)
            {
                Console.WriteLine(
                    $"card-coverage set={set.CardSet} total={set.Counts.Total} registered={set.Counts.Registered} "
                    + $"tested={set.Counts.Tested} complete={set.Counts.FullTextComplete} "
                    + $"partial={set.Counts.Partial} unregistered={set.Counts.Unregistered}");
            }

            Console.WriteLine(
                $"card-coverage total={report.Counts.Total} registered={report.Counts.Registered} "
                + $"tested={report.Counts.Tested} complete={report.Counts.FullTextComplete} "
                + $"partial={report.Counts.Partial} unregistered={report.Counts.Unregistered}");

            return report.Diagnostics.Count == 0 ? 0 : 1;
        }
        catch (CoverageHelpException)
        {
            return 0;
        }
        catch (CoverageUsageException exception)
        {
            Console.Error.WriteLine($"card-coverage: {exception.Message}");
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"card-coverage failed: {exception.Message}");
            return 1;
        }
    }
}

internal sealed class CoverageUsageException : Exception
{
    public CoverageUsageException(string message)
        : base(message)
    {
    }
}

internal sealed class CoverageHelpException : Exception
{
}

/// <summary>
/// One card set the report covers: its registry, rulings, local-only card
/// metadata, the compiled catalog that registers it and the codes it must
/// hold.
/// </summary>
internal sealed record CardSetOptions(
    CardSetDefinition Definition,
    string RegistryPath,
    string RulingsPath,
    string? CardsPath);

internal sealed record CardSetDefinition(
    string CardSet,
    string Title,
    string CatalogType,
    IReadOnlySet<string> ExpectedCodes,
    IReadOnlySet<string> ExcludedCatalogCodes)
{
    public static CardSetDefinition SystemGateway { get; } = new(
        "system-gateway",
        "System Gateway",
        "Netrunner.Cards.GatewayBehaviorCatalog",
        Codes(30001, 77),
        new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// Elevation is registered in the Gateway + Elevation catalog, which also
    /// holds every Gateway card; those are excluded from Elevation's set.
    /// </summary>
    public static CardSetDefinition Elevation { get; } = new(
        "elevation",
        "Elevation",
        "Netrunner.Cards.ElevationBehaviorCatalog",
        Codes(35001, 82),
        Codes(30001, 77));

    /// <summary>
    /// Vantage Point is registered in the Startup catalog, which also holds
    /// every Gateway and Elevation card; those are excluded from its set.
    /// </summary>
    public static CardSetDefinition VantagePoint { get; } = new(
        "vantage-point",
        "Vantage Point",
        "Netrunner.Cards.VantagePointBehaviorCatalog",
        Codes(36001, 66),
        Codes(30001, 77).Concat(Codes(35001, 82)).ToHashSet(StringComparer.Ordinal));

    private static HashSet<string> Codes(int first, int count) =>
        Enumerable.Range(first, count)
            .Select(static code => code.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
}

internal sealed record Options(
    string Root,
    IReadOnlyList<CardSetOptions> Sets,
    string? CatalogPath,
    IReadOnlyList<string> TestAssemblyPaths,
    string MarkdownPath,
    string JsonPath)
{
    public const string Usage =
        "usage: dotnet run --project tools/Netrunner.CardCoverage -- "
        + "--root <repo> [--registry <path>] [--rulings <path>] [--cards <path>] "
        + "[--elevation-registry <path>] [--elevation-rulings <path>] [--elevation-cards <path>] "
        + "[--vantage-point-registry <path>] [--vantage-point-rulings <path>] [--vantage-point-cards <path>] "
        + "[--catalog <Cards.dll>] [--tests <tests.dll> ...] "
        + "[--markdown <path>] [--json <path>]";

    private static readonly string[] ValueOptions =
    [
        "--root", "--registry", "--rulings", "--cards", "--elevation-registry", "--elevation-rulings",
        "--elevation-cards", "--vantage-point-registry", "--vantage-point-rulings", "--vantage-point-cards",
        "--catalog", "--tests", "--markdown", "--json",
    ];

    public static Options Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var tests = new List<string>();

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument is "--help" or "-h")
            {
                Console.WriteLine(Usage);
                throw new CoverageHelpException();
            }

            if (!ValueOptions.Contains(argument, StringComparer.Ordinal))
            {
                throw new CoverageUsageException($"Unknown argument '{argument}'.");
            }

            string value = ReadValue(args, ref index, argument);
            if (argument == "--tests")
            {
                tests.Add(value);
            }
            else
            {
                values[argument] = value;
            }
        }

        string root = Path.GetFullPath(values.GetValueOrDefault("--root") ?? Directory.GetCurrentDirectory());
        string Value(string option, string fallback) => Path.GetFullPath(values.GetValueOrDefault(option) ?? Path.Combine(root, fallback));
        string? Cards(string option, string fallback)
        {
            if (values.TryGetValue(option, out string? explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            string path = Path.Combine(root, fallback);
            return File.Exists(path) ? path : null;
        }

        CardSetOptions[] sets =
        [
            new(
                CardSetDefinition.SystemGateway,
                Value("--registry", "data/m4/batch-registry.json"),
                Value("--rulings", "data/m4/rulings.json"),
                Cards("--cards", "data/vendor/system-gateway/cards.json")),
            new(
                CardSetDefinition.Elevation,
                Value("--elevation-registry", "data/elevation/batch-registry.json"),
                Value("--elevation-rulings", "data/elevation/rulings.json"),
                Cards("--elevation-cards", "data/vendor/elevation/cards.json")),
            new(
                CardSetDefinition.VantagePoint,
                Value("--vantage-point-registry", "data/vantage-point/batch-registry.json"),
                Value("--vantage-point-rulings", "data/vantage-point/rulings.json"),
                Cards("--vantage-point-cards", "data/vendor/vantage-point/cards.json")),
        ];

        return new Options(
            root,
            sets,
            values.TryGetValue("--catalog", out string? catalog) ? Path.GetFullPath(catalog) : FindCatalog(root),
            tests.Count == 0 ? FindTests(root) : tests.Select(Path.GetFullPath).ToArray(),
            Value("--markdown", "docs/generated/card-coverage.md"),
            Value("--json", "docs/generated/card-coverage.json"));
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string argument)
    {
        if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new CoverageUsageException($"Argument '{argument}' requires a value.");
        }

        return args[index];
    }

    private static string? FindCatalog(string root)
    {
        string[] candidates =
        {
            Path.Combine(root, "src/Netrunner.Cards/bin/Debug/net8.0/Netrunner.Cards.dll"),
            Path.Combine(root, "src/Netrunner.Cards/bin/Release/net8.0/Netrunner.Cards.dll"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string[] FindTests(string root)
    {
        string[] candidates =
        {
            Path.Combine(root, "tests/Netrunner.Cards.Tests/bin/Debug/net8.0/Netrunner.Cards.Tests.dll"),
            Path.Combine(root, "tests/Netrunner.Cards.Tests/bin/Release/net8.0/Netrunner.Cards.Tests.dll"),
        };
        return candidates.Where(File.Exists).ToArray();
    }
}

internal sealed class CardSetInput
{
    public CardSetInput(
        CardSetDefinition definition,
        RegistryDocument registry,
        RulingsDocument rulings,
        IReadOnlyDictionary<string, CardMetadata> metadata,
        IReadOnlySet<string> registeredCodes)
    {
        Definition = definition;
        Registry = registry;
        Rulings = rulings;
        Metadata = metadata;
        RegisteredCodes = registeredCodes;
    }

    public CardSetDefinition Definition { get; }

    public RegistryDocument Registry { get; }

    public RulingsDocument Rulings { get; }

    public IReadOnlyDictionary<string, CardMetadata> Metadata { get; }

    public IReadOnlySet<string> RegisteredCodes { get; }
}

internal sealed class CoverageInput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private CoverageInput(
        IReadOnlyList<CardSetInput> sets,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tests)
    {
        Sets = sets;
        Tests = tests;
    }

    public IReadOnlyList<CardSetInput> Sets { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Tests { get; }

    public static CoverageInput Load(Options options)
    {
        if (options.CatalogPath is null)
        {
            throw new CoverageUsageException(
                "A compiled catalog assembly is required; pass --catalog <Netrunner.Cards.dll> after building the Cards project.");
        }

        var sets = new List<CardSetInput>();
        foreach (CardSetOptions set in options.Sets)
        {
            RegistryDocument registry = ReadJson<RegistryDocument>(set.RegistryPath, $"{set.Definition.Title} batch registry");
            RulingsDocument rulings = ReadJson<RulingsDocument>(set.RulingsPath, $"{set.Definition.Title} rulings");
            RegistryValidator.Validate(registry, rulings, set.Definition);

            IReadOnlyDictionary<string, CardMetadata> metadata = set.CardsPath is null
                ? registry.Cards.ToDictionary(
                    static card => card.Code,
                    static card => new CardMetadata(card.Code, card.Title),
                    StringComparer.Ordinal)
                : ReadCards(set.CardsPath, registry);

            IReadOnlySet<string> registered = CatalogReader.Read(options.CatalogPath, set.Definition.CatalogType)
                .Except(set.Definition.ExcludedCatalogCodes, StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            sets.Add(new CardSetInput(set.Definition, registry, rulings, metadata, registered));
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> tests = TestMetadataReader.Read(options.TestAssemblyPaths);
        return new CoverageInput(sets, tests);
    }

    private static T ReadJson<T>(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new CoverageUsageException($"{description} file does not exist: '{path}'.");
        }

        try
        {
            T? value = JsonSerializer.Deserialize<T>(
                File.ReadAllBytes(path),
                JsonOptions);
            return value ?? throw new CoverageUsageException($"{description} file is empty: '{path}'.");
        }
        catch (JsonException exception)
        {
            throw new CoverageUsageException($"Invalid {description} JSON at '{path}': {exception.Message}");
        }
    }

    private static Dictionary<string, CardMetadata> ReadCards(
        string path,
        RegistryDocument registry)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new CoverageUsageException($"Card metadata at '{path}' must be a JSON array.");
        }

        var cards = new Dictionary<string, CardMetadata>(StringComparer.Ordinal);
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("code", out JsonElement codeElement)
                || !element.TryGetProperty("title", out JsonElement titleElement)
                || codeElement.ValueKind != JsonValueKind.String
                || titleElement.ValueKind != JsonValueKind.String)
            {
                throw new CoverageUsageException($"Card metadata at '{path}' contains a record without code/title.");
            }

            string code = codeElement.GetString()!;
            string title = titleElement.GetString()!;
            if (!cards.TryAdd(code, new CardMetadata(code, title)))
            {
                throw new CoverageUsageException($"Card metadata at '{path}' contains duplicate code '{code}'.");
            }
        }

        var expected = registry.Cards.Select(static card => card.Code).ToHashSet(StringComparer.Ordinal);
        if (!cards.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
        {
            string missing = string.Join(", ", expected.Except(cards.Keys, StringComparer.Ordinal).Order());
            string extra = string.Join(", ", cards.Keys.Except(expected, StringComparer.Ordinal).Order());
            throw new CoverageUsageException(
                $"Card metadata code set differs from checked-in registry (missing [{missing}], extra [{extra}]).");
        }

        return cards;
    }
}

internal sealed record CardMetadata(string Code, string Title);

internal sealed class RegistryDocument
{
    public int SchemaVersion { get; set; }

    public string CardSet { get; set; } = string.Empty;

    public string RulesVersion { get; set; } = string.Empty;

    public List<RegistryBatch> Batches { get; set; } = new();

    public List<RegistryCard> Cards { get; set; } = new();
}

internal sealed class RegistryBatch
{
    public string Id { get; set; } = string.Empty;

    public string Slice { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool CompletionRequired { get; set; }

    public List<string> Codes { get; set; } = new();

    /// <summary>
    /// A baseline/reporting batch can list cards that were already registered
    /// by M2 while their actual M4 completion ownership remains in a later
    /// batch.  In that case this list is the authoritative ownership list;
    /// otherwise <see cref="Codes"/> is used.
    /// </summary>
    public List<string> OwnedCodes { get; set; } = new();

    public IReadOnlyList<string> OwnershipCodes => OwnedCodes.Count == 0 ? Codes : OwnedCodes;
}

internal sealed class RegistryCard
{
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Batch { get; set; } = string.Empty;

    public string Slice { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string TextStatus { get; set; } = string.Empty;

    public string ReviewStatus { get; set; } = string.Empty;

    public string Reviewer { get; set; } = string.Empty;

    public List<string> Citations { get; set; } = new();

    public string Observation { get; set; } = string.Empty;

    public string HiddenInformationImpact { get; set; } = string.Empty;

    public string SerializationImpact { get; set; } = string.Empty;
}

internal sealed class RulingsDocument
{
    public int SchemaVersion { get; set; }

    public string RulesVersion { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public List<Ruling> Rulings { get; set; } = new();
}

internal sealed class Ruling
{
    public string Id { get; set; } = string.Empty;

    public string Cr { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string JnetCrossCheck { get; set; } = string.Empty;
}

internal static class RegistryValidator
{
    public static void Validate(RegistryDocument registry, RulingsDocument rulings, CardSetDefinition set)
    {
        var errors = new List<string>();
        IReadOnlySet<string> expectedCodes = set.ExpectedCodes;
        if (registry.SchemaVersion != 1)
        {
            errors.Add($"batch registry schemaVersion must be 1 (found {registry.SchemaVersion})");
        }

        if (!string.Equals(registry.CardSet, set.CardSet, StringComparison.Ordinal))
        {
            errors.Add($"batch registry cardSet must be '{set.CardSet}'");
        }

        if (!string.Equals(registry.RulesVersion, "CR-26.03", StringComparison.Ordinal))
        {
            errors.Add("batch registry rulesVersion must be 'CR-26.03'");
        }

        var cards = new Dictionary<string, RegistryCard>(StringComparer.Ordinal);
        foreach (RegistryCard card in registry.Cards)
        {
            if (string.IsNullOrWhiteSpace(card.Code))
            {
                errors.Add("registry contains a card with a blank code");
                continue;
            }

            if (!cards.TryAdd(card.Code, card))
            {
                errors.Add($"registry contains duplicate card code '{card.Code}'");
            }

            if (string.IsNullOrWhiteSpace(card.Batch) || string.IsNullOrWhiteSpace(card.Slice))
            {
                errors.Add($"card {card.Code} must name batch and slice");
            }

            if (string.IsNullOrWhiteSpace(card.Status)
                || string.IsNullOrWhiteSpace(card.TextStatus)
                || card.Citations.Count == 0
                || string.IsNullOrWhiteSpace(card.Observation)
                || string.IsNullOrWhiteSpace(card.HiddenInformationImpact)
                || string.IsNullOrWhiteSpace(card.SerializationImpact))
            {
                errors.Add($"card {card.Code} is missing review/audit fields");
            }
        }

        if (!cards.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedCodes))
        {
            errors.Add(
                $"registry must contain exactly the {expectedCodes.Count} expected {set.Title} codes "
                + $"(missing: {string.Join(",", expectedCodes.Except(cards.Keys).Order())}; "
                + $"extra: {string.Join(",", cards.Keys.Except(expectedCodes).Order())})");
        }

        var batches = new Dictionary<string, RegistryBatch>(StringComparer.Ordinal);
        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (RegistryBatch batch in registry.Batches)
        {
            if (!batches.TryAdd(batch.Id, batch))
            {
                errors.Add($"registry contains duplicate batch '{batch.Id}'");
            }

            foreach (string code in batch.OwnershipCodes)
            {
                if (!assigned.TryAdd(code, batch.Id))
                {
                    errors.Add($"card {code} is assigned to both {assigned[code]} and {batch.Id}");
                }

                if (!cards.ContainsKey(code))
                {
                    errors.Add($"batch {batch.Id} names unknown card {code}");
                }
            }
        }

        if (!assigned.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedCodes))
        {
            errors.Add($"batches must assign every expected {set.Title} code exactly once");
        }

        foreach ((string code, RegistryCard card) in cards)
        {
            if (!batches.TryGetValue(card.Batch, out RegistryBatch? batch))
            {
                errors.Add($"card {code} names missing batch '{card.Batch}'");
            }
            else if (!batch.OwnershipCodes.Contains(code, StringComparer.Ordinal))
            {
                errors.Add($"card {code} is not listed in its batch '{card.Batch}'");
            }

            if (batch?.CompletionRequired == true
                && string.Equals(batch.Status, "complete", StringComparison.Ordinal)
                && (!string.Equals(card.ReviewStatus, "complete", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(card.Reviewer)))
            {
                errors.Add($"completed batch card {code} must include a completed review and reviewer");
            }

            foreach (string citation in card.Citations)
            {
                if (!rulings.Rulings.Any(ruling => string.Equals(ruling.Id, citation, StringComparison.Ordinal)))
                {
                    errors.Add($"card {code} cites unknown ruling '{citation}'");
                }
            }
        }

        var rulingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Ruling ruling in rulings.Rulings)
        {
            if (!rulingIds.Add(ruling.Id))
            {
                errors.Add($"rulings contains duplicate id '{ruling.Id}'");
            }

            if (string.IsNullOrWhiteSpace(ruling.Cr)
                || string.IsNullOrWhiteSpace(ruling.Summary)
                || string.IsNullOrWhiteSpace(ruling.JnetCrossCheck))
            {
                errors.Add($"ruling {ruling.Id} must include CR text, summary, and jnet cross-check");
            }
        }

        if (rulings.SchemaVersion != 1 || !string.Equals(rulings.RulesVersion, "CR-26.03", StringComparison.Ordinal))
        {
            errors.Add("rulings must declare schemaVersion 1 and rulesVersion CR-26.03");
        }

        if (errors.Count > 0)
        {
            throw new CoverageUsageException($"Invalid {set.Title} coverage inputs:\n- " + string.Join("\n- ", errors));
        }
    }
}

internal static class CatalogReader
{
    public static IReadOnlySet<string> Read(string path, string catalogTypeName)
    {
        if (!File.Exists(path))
        {
            throw new CoverageUsageException($"Compiled catalog assembly does not exist: '{path}'.");
        }

        string fullPath = Path.GetFullPath(path);
        var context = new CatalogLoadContext(fullPath);
        Assembly assembly = context.LoadFromAssemblyPath(fullPath);
        Type catalogType = assembly.GetType(catalogTypeName, throwOnError: false)
            ?? throw new CoverageUsageException(
                $"Compiled catalog assembly does not expose {catalogTypeName}.");
        MethodInfo create = catalogType.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
            ?? throw new CoverageUsageException(
                $"Compiled catalog does not expose the immutable {catalogTypeName}.Create() surface.");

        object immutableCatalog;
        try
        {
            immutableCatalog = create.Invoke(null, null)
                ?? throw new CoverageUsageException($"Immutable {catalogTypeName} creation returned null.");
        }
        catch (TargetInvocationException exception)
        {
            throw new CoverageUsageException(
                $"Immutable {catalogTypeName} creation failed: {exception.InnerException?.Message ?? exception.Message}");
        }

        PropertyInfo allDefinitionIds = immutableCatalog.GetType().GetProperty(
                "AllDefinitionIds",
                BindingFlags.Public | BindingFlags.Instance)
            ?? throw new CoverageUsageException(
                $"Immutable {catalogTypeName} does not expose AllDefinitionIds.");
        if (allDefinitionIds.GetValue(immutableCatalog) is not IEnumerable values)
        {
            throw new CoverageUsageException(
                $"Immutable {catalogTypeName} AllDefinitionIds is not enumerable.");
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (object? id in values)
        {
            string? text = id?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                codes.Add(text);
            }
        }

        return codes;
    }

    private sealed class CatalogLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;

        public CatalogLoadContext(string mainAssemblyPath)
            : base(isCollectible: true)
        {
            resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? path = resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}

internal static class TestMetadataReader
{
    private const string TraitName = "TraitAttribute";
    private const string CardMarkerName = "GatewayCardTestAttribute";

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Read(IReadOnlyList<string> paths)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                throw new CoverageUsageException($"Test assembly does not exist: '{path}'.");
            }

            ReadAssembly(path, result);
        }

        return result.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.Distinct(StringComparer.Ordinal).Order().ToArray(),
            StringComparer.Ordinal);
    }

    private static void ReadAssembly(string path, Dictionary<string, List<string>> result)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new CoverageUsageException($"Test assembly has no CLI metadata: '{path}'.");
        }

        MetadataReader reader = peReader.GetMetadataReader();
        string assemblyName = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : Path.GetFileNameWithoutExtension(path);

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            string typeName = FormatTypeName(reader, typeHandle);
            IReadOnlyList<Marker> typeMarkers = ReadMarkers(reader, type.GetCustomAttributes());
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                IReadOnlyList<Marker> methodMarkers = ReadMarkers(reader, method.GetCustomAttributes());
                if (!methodMarkers.Any(static marker => marker.IsTest))
                {
                    continue;
                }

                var cardCodes = methodMarkers
                    .Concat(typeMarkers)
                    .Where(static marker => marker.CardCode is not null)
                    .Select(static marker => marker.CardCode!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (cardCodes.Length == 0)
                {
                    continue;
                }

                string testId = $"{assemblyName}:{typeName}.{reader.GetString(method.Name)}";
                foreach (string code in cardCodes)
                {
                    if (!result.TryGetValue(code, out List<string>? tests))
                    {
                        tests = new List<string>();
                        result.Add(code, tests);
                    }

                    tests.Add(testId);
                }
            }
        }
    }

    private static List<Marker> ReadMarkers(
        MetadataReader reader,
        CustomAttributeHandleCollection handles)
    {
        var markers = new List<Marker>();
        foreach (CustomAttributeHandle handle in handles)
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            string attributeName = GetAttributeTypeName(reader, attribute.Constructor);
            bool isFact = attributeName.EndsWith(".FactAttribute", StringComparison.Ordinal)
                || attributeName.EndsWith(".TheoryAttribute", StringComparison.Ordinal)
                || attributeName is "FactAttribute" or "TheoryAttribute";
            bool isTrait = attributeName.EndsWith($".{TraitName}", StringComparison.Ordinal)
                || attributeName == TraitName;
            bool isCardMarker = attributeName.EndsWith($".{CardMarkerName}", StringComparison.Ordinal)
                || attributeName == CardMarkerName;
            if (!isFact && !isTrait && !isCardMarker)
            {
                continue;
            }

            string? first = null;
            string? second = null;
            try
            {
                BlobReader blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() == 1)
                {
                    first = blob.ReadSerializedString();
                    if (isTrait)
                    {
                        second = blob.ReadSerializedString();
                    }
                }
            }
            catch (BadImageFormatException)
            {
                // A malformed unrelated test attribute must not make the
                // metadata walk execute user code; it simply contributes no
                // card marker.
            }

            markers.Add(new Marker(
                isFact,
                isTrait && first is "GatewayCard" or "ElevationCard" or "VantagePointCard"
                    ? second
                    : isCardMarker ? first : null));
        }

        return markers;
    }

    private static string GetAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        EntityHandle parent = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default,
        };

        return parent.IsNil ? string.Empty : GetTypeName(reader, parent);
    }

    private static string GetTypeName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => FormatTypeName(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => FormatTypeReference(reader, (TypeReferenceHandle)handle),
            _ => string.Empty,
        };
    }

    private static string FormatTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        string name = reader.GetString(definition.Name);
        string @namespace = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
    }

    private static string FormatTypeReference(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference reference = reader.GetTypeReference(handle);
        string name = reader.GetString(reference.Name);
        string @namespace = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
    }

    private sealed record Marker(bool IsTest, string? CardCode);
}


internal sealed class CoverageReport
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private CoverageReport(IReadOnlyList<SetReport> sets, IReadOnlyList<string> diagnostics)
    {
        Sets = sets;
        Diagnostics = diagnostics;
        Counts = CoverageCounts.From(sets.SelectMany(static set => set.Cards).ToArray());
    }

    public IReadOnlyList<SetReport> Sets { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public CoverageCounts Counts { get; }

    public static CoverageReport Create(CoverageInput input)
    {
        var diagnostics = new List<string>();
        var sets = new List<SetReport>();
        foreach (CardSetInput set in input.Sets)
        {
            sets.Add(CreateSet(set, input.Tests, diagnostics));
        }

        return new CoverageReport(sets, diagnostics);
    }

    private static SetReport CreateSet(
        CardSetInput input,
        IReadOnlyDictionary<string, IReadOnlyList<string>> allTests,
        List<string> diagnostics)
    {
        var cards = new List<CardReport>();
        foreach (RegistryCard card in input.Registry.Cards.OrderBy(static card => card.Code, StringComparer.Ordinal))
        {
            string title = input.Metadata.TryGetValue(card.Code, out CardMetadata? metadata)
                ? metadata.Title
                : card.Title;
            IReadOnlyList<string> tests = allTests.TryGetValue(card.Code, out IReadOnlyList<string>? found)
                ? found
                : Array.Empty<string>();
            bool registered = input.RegisteredCodes.Contains(card.Code);
            bool fullTextComplete = string.Equals(card.TextStatus, "complete", StringComparison.Ordinal);
            bool partial = string.Equals(card.Status, "partial", StringComparison.Ordinal)
                || string.Equals(card.TextStatus, "partial", StringComparison.Ordinal);

            if (registered && string.Equals(card.Status, "unregistered", StringComparison.Ordinal))
            {
                diagnostics.Add($"card {card.Code} is compiled/registered but registry status is unregistered");
            }

            if (registered && string.Equals(card.Status, "planned", StringComparison.Ordinal))
            {
                diagnostics.Add($"card {card.Code} is compiled/registered but registry status is planned");
            }

            if (!registered && string.Equals(card.Status, "registered", StringComparison.Ordinal))
            {
                diagnostics.Add($"card {card.Code} is registry-registered but absent from the compiled catalog");
            }

            if (registered && tests.Count == 0)
            {
                diagnostics.Add($"card {card.Code} ({title}) is registered but has no card test metadata");
            }

            if (fullTextComplete && !registered)
            {
                diagnostics.Add($"card {card.Code} ({title}) is marked full-text complete but is not registered");
            }

            RegistryBatch? batch = input.Registry.Batches.FirstOrDefault(
                candidate => string.Equals(candidate.Id, card.Batch, StringComparison.Ordinal));
            if (batch?.CompletionRequired == true
                && string.Equals(batch.Status, "complete", StringComparison.Ordinal)
                && (!registered || !fullTextComplete || tests.Count == 0
                    || !string.Equals(card.ReviewStatus, "complete", StringComparison.Ordinal)
                    || card.Citations.Count == 0))
            {
                diagnostics.Add($"completed batch {card.Batch} card {card.Code} is missing a required CARD-401 field");
            }

            cards.Add(new CardReport(
                input.Registry.CardSet,
                card.Code,
                title,
                card.Batch,
                card.Slice,
                card.Status,
                card.TextStatus,
                card.ReviewStatus,
                card.Reviewer,
                registered,
                tests,
                fullTextComplete,
                partial,
                card.Citations,
                card.Observation,
                card.HiddenInformationImpact,
                card.SerializationImpact));
        }

        foreach (string code in input.RegisteredCodes.Except(input.Definition.ExpectedCodes, StringComparer.Ordinal).Order())
        {
            diagnostics.Add($"compiled catalog contains unknown {input.Definition.Title} definition '{code}'");
        }

        if (cards.Count != input.Definition.ExpectedCodes.Count)
        {
            diagnostics.Add(
                $"{input.Definition.Title} coverage has {cards.Count} cards; expected {input.Definition.ExpectedCodes.Count}");
        }

        return new SetReport(
            input.Registry.CardSet,
            input.Definition.Title,
            input.Registry.Batches.Select(static batch => new BatchReport(batch.Id, batch.Slice, batch.Status, batch.OwnershipCodes.Count)).ToArray(),
            CoverageCounts.From(cards),
            cards);
    }

    public void Write(string markdownPath, string jsonPath)
    {
        CreateParentDirectory(markdownPath);
        CreateParentDirectory(jsonPath);
        File.WriteAllText(markdownPath, RenderMarkdown());
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            rulesVersion = "CR-26.03",
            counts = Counts,
            sets = Sets.Select(static set => new { cardSet = set.CardSet, title = set.Title, batches = set.Batches, counts = set.Counts }),
            diagnostics = Diagnostics,
            cards = Sets.SelectMany(static set => set.Cards),
        }, ReportJsonOptions) + Environment.NewLine);
    }

    private static void CreateParentDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private string RenderMarkdown()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Card coverage");
        builder.AppendLine();
        builder.AppendLine("> Generated by `tools/Netrunner.CardCoverage`; test metadata is read from compiled assemblies without executing tests.");
        builder.AppendLine();
        builder.AppendLine("- Rules: `CR-26.03`");
        builder.Append("- Cards: `").Append(Counts.Total).AppendLine("` expected");
        builder.Append("- Registered: `").Append(Counts.Registered).AppendLine("`");
        builder.Append("- Registered with positive test metadata: `").Append(Counts.Tested).AppendLine("`");
        builder.Append("- Full-text complete: `").Append(Counts.FullTextComplete).AppendLine("`");
        builder.Append("- Partial registrations: `").Append(Counts.Partial).AppendLine("`");
        builder.Append("- Unregistered: `").Append(Counts.Unregistered).AppendLine("`");
        builder.AppendLine();
        builder.AppendLine("A partial registration is not full coverage. A completed batch requires full behavior registration, a positive test marker, review, citations, and an observation note.");
        builder.AppendLine();
        builder.AppendLine("| Set | Cards | Registered | Tested | Complete | Partial | Unregistered |");
        builder.AppendLine("|---|---|---|---|---|---|---|");
        foreach (SetReport set in Sets)
        {
            builder.Append('|').Append(set.Title).Append('|').Append(set.Counts.Total).Append('|')
                .Append(set.Counts.Registered).Append('|').Append(set.Counts.Tested).Append('|')
                .Append(set.Counts.FullTextComplete).Append('|').Append(set.Counts.Partial).Append('|')
                .Append(set.Counts.Unregistered).Append('|').AppendLine();
        }

        foreach (SetReport set in Sets)
        {
            builder.AppendLine();
            builder.Append("## ").AppendLine(set.Title);
            builder.AppendLine();
            builder.AppendLine("| Batch | Slice | Status | Cards |");
            builder.AppendLine("|---|---|---|---|");
            foreach (BatchReport batch in set.Batches)
            {
                builder.Append('|').Append(batch.Id).Append('|').Append(batch.Slice).Append('|')
                    .Append(batch.Status).Append('|').Append(batch.Cards).Append('|').AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("| Code | Card | Batch | Slice | Registration | Text | Review | Reviewer | Positive tests | CR/ruling citations | Observation | Hidden-info impact | Serialization impact |");
            builder.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (CardReport card in set.Cards)
            {
                builder.Append('|').Append(card.Code).Append('|').Append(Escape(card.Title)).Append('|')
                    .Append(card.Batch).Append('|').Append(card.Slice).Append('|')
                    .Append(card.Registered ? "registered" : "unregistered").Append('|')
                    .Append(card.TextStatus).Append('|').Append(card.ReviewStatus).Append('|')
                    .Append(Escape(card.Reviewer)).Append('|')
                    .Append(card.Tests.Count == 0 ? "—" : string.Join("<br>", card.Tests.Select(Escape))).Append('|')
                    .Append(card.Citations.Count == 0 ? "—" : string.Join(", ", card.Citations.Select(Escape))).Append('|')
                    .Append(Escape(card.Observation)).Append('|')
                    .Append(Escape(card.HiddenInformationImpact)).Append('|')
                    .Append(Escape(card.SerializationImpact)).Append('|').AppendLine();
            }
        }

        if (Diagnostics.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Diagnostics");
            builder.AppendLine();
            foreach (string diagnostic in Diagnostics)
            {
                builder.Append("- ").AppendLine(diagnostic);
            }
        }

        return builder.ToString();
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');
}

internal sealed record SetReport(
    string CardSet,
    string Title,
    IReadOnlyList<BatchReport> Batches,
    CoverageCounts Counts,
    IReadOnlyList<CardReport> Cards);

internal sealed record BatchReport(string Id, string Slice, string Status, int Cards);

internal sealed record CoverageCounts(
    int Total,
    int Registered,
    int Tested,
    int FullTextComplete,
    int Partial,
    int Unregistered)
{
    public static CoverageCounts From(IReadOnlyCollection<CardReport> cards) => new(
        cards.Count,
        cards.Count(static card => card.Registered),
        cards.Count(static card => card.Registered && card.Tests.Count > 0),
        cards.Count(static card => card.FullTextComplete),
        cards.Count(static card => card.Partial),
        cards.Count(static card => !card.Registered));
}

internal sealed record CardReport(
    string CardSet,
    string Code,
    string Title,
    string Batch,
    string Slice,
    string Status,
    string TextStatus,
    string ReviewStatus,
    string Reviewer,
    bool Registered,
    IReadOnlyList<string> Tests,
    bool FullTextComplete,
    bool Partial,
    IReadOnlyList<string> Citations,
    string Observation,
    string HiddenInformationImpact,
    string SerializationImpact);
