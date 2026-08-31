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

            Console.WriteLine(
                $"gateway-coverage total={report.Counts.Total} registered={report.Counts.Registered} "
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

internal sealed record Options(
    string Root,
    string RegistryPath,
    string RulingsPath,
    string? CardsPath,
    string? CatalogPath,
    IReadOnlyList<string> TestAssemblyPaths,
    string MarkdownPath,
    string JsonPath)
{
    public const string Usage =
        "usage: dotnet run --project tools/Netrunner.CardCoverage -- "
        + "--root <repo> [--registry <path>] [--rulings <path>] [--cards <path>] "
        + "[--catalog <Cards.dll>] [--tests <tests.dll> ...] "
        + "[--markdown <path>] [--json <path>]";

    public static Options Parse(IReadOnlyList<string> args)
    {
        string root = Directory.GetCurrentDirectory();
        string? registry = null;
        string? rulings = null;
        string? cards = null;
        string? catalog = null;
        string? markdown = null;
        string? json = null;
        var tests = new List<string>();

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument is "--help" or "-h")
            {
                Console.WriteLine(Usage);
                throw new CoverageHelpException();
            }

            string value = argument switch
            {
                "--root" => ReadValue(args, ref index, argument),
                "--registry" => ReadValue(args, ref index, argument),
                "--rulings" => ReadValue(args, ref index, argument),
                "--cards" => ReadValue(args, ref index, argument),
                "--catalog" => ReadValue(args, ref index, argument),
                "--tests" => ReadValue(args, ref index, argument),
                "--markdown" => ReadValue(args, ref index, argument),
                "--json" => ReadValue(args, ref index, argument),
                _ => throw new CoverageUsageException($"Unknown argument '{argument}'."),
            };

            switch (argument)
            {
                case "--root":
                    root = value;
                    break;
                case "--registry":
                    registry = value;
                    break;
                case "--rulings":
                    rulings = value;
                    break;
                case "--cards":
                    cards = value;
                    break;
                case "--catalog":
                    catalog = value;
                    break;
                case "--tests":
                    tests.Add(value);
                    break;
                case "--markdown":
                    markdown = value;
                    break;
                case "--json":
                    json = value;
                    break;
            }
        }

        root = Path.GetFullPath(root);
        return new Options(
            root,
            Path.GetFullPath(registry ?? Path.Combine(root, "data/m4/batch-registry.json")),
            Path.GetFullPath(rulings ?? Path.Combine(root, "data/m4/rulings.json")),
            cards is null ? FindCards(root) : Path.GetFullPath(cards),
            catalog is null ? FindCatalog(root) : Path.GetFullPath(catalog),
            tests.Count == 0 ? FindTests(root) : tests.Select(Path.GetFullPath).ToArray(),
            Path.GetFullPath(markdown ?? Path.Combine(root, "docs/generated/gateway-coverage.md")),
            Path.GetFullPath(json ?? Path.Combine(root, "docs/generated/gateway-coverage.json")));
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string argument)
    {
        if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new CoverageUsageException($"Argument '{argument}' requires a value.");
        }

        return args[index];
    }

    private static string? FindCards(string root)
    {
        string path = Path.Combine(root, "data/vendor/system-gateway/cards.json");
        return File.Exists(path) ? path : null;
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

internal sealed class CoverageInput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private CoverageInput(
        RegistryDocument registry,
        RulingsDocument rulings,
        IReadOnlyDictionary<string, CardMetadata> metadata,
        IReadOnlySet<string> registeredCodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tests)
    {
        Registry = registry;
        Rulings = rulings;
        Metadata = metadata;
        RegisteredCodes = registeredCodes;
        Tests = tests;
    }

    public RegistryDocument Registry { get; }

    public RulingsDocument Rulings { get; }

    public IReadOnlyDictionary<string, CardMetadata> Metadata { get; }

    public IReadOnlySet<string> RegisteredCodes { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Tests { get; }

    public static CoverageInput Load(Options options)
    {
        RegistryDocument registry = ReadJson<RegistryDocument>(options.RegistryPath, "batch registry");
        RulingsDocument rulings = ReadJson<RulingsDocument>(options.RulingsPath, "rulings");
        RegistryValidator.Validate(registry, rulings);

        IReadOnlyDictionary<string, CardMetadata> metadata = options.CardsPath is null
            ? registry.Cards.ToDictionary(
                static card => card.Code,
                static card => new CardMetadata(card.Code, card.Title),
                StringComparer.Ordinal)
            : ReadCards(options.CardsPath, registry);

        if (options.CatalogPath is null)
        {
            throw new CoverageUsageException(
                "A compiled catalog assembly is required; pass --catalog <Netrunner.Cards.dll> after building the Cards project.");
        }

        IReadOnlySet<string> registeredCodes = CatalogReader.Read(options.CatalogPath);

        IReadOnlyDictionary<string, IReadOnlyList<string>> tests = TestMetadataReader.Read(options.TestAssemblyPaths);
        return new CoverageInput(registry, rulings, metadata, registeredCodes, tests);
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
    private static readonly IReadOnlySet<string> ExpectedCodes = new HashSet<string>(
        new[]
        {
            "30001", "30002", "30003", "30004", "30005", "30006", "30007", "30008", "30009", "30010",
            "30011", "30012", "30013", "30014", "30015", "30016", "30017", "30018", "30019", "30020",
            "30021", "30022", "30023", "30024", "30025", "30026", "30027", "30028", "30029", "30030",
            "30031", "30032", "30033", "30034", "30035", "30036", "30037", "30038", "30039", "30040",
            "30041", "30042", "30043", "30044", "30045", "30046", "30047", "30048", "30049", "30050",
            "30051", "30052", "30053", "30054", "30055", "30056", "30057", "30058", "30059", "30060",
            "30061", "30062", "30063", "30064", "30065", "30066", "30067", "30068", "30069", "30070",
            "30071", "30072", "30073", "30074", "30075", "30076", "30077",
        },
        StringComparer.Ordinal);

    public static void Validate(RegistryDocument registry, RulingsDocument rulings)
    {
        var errors = new List<string>();
        if (registry.SchemaVersion != 1)
        {
            errors.Add($"batch registry schemaVersion must be 1 (found {registry.SchemaVersion})");
        }

        if (!string.Equals(registry.CardSet, "system-gateway", StringComparison.Ordinal))
        {
            errors.Add("batch registry cardSet must be 'system-gateway'");
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

        if (!cards.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedCodes))
        {
            errors.Add(
                $"registry must contain exactly the 77 expected Gateway codes "
                + $"(missing: {string.Join(",", ExpectedCodes.Except(cards.Keys).Order())}; "
                + $"extra: {string.Join(",", cards.Keys.Except(ExpectedCodes).Order())})");
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

        if (!assigned.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedCodes))
        {
            errors.Add("batches must assign every expected Gateway code exactly once");
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
            throw new CoverageUsageException("Invalid coverage inputs:\n- " + string.Join("\n- ", errors));
        }
    }

    public static IReadOnlySet<string> Codes => ExpectedCodes;
}

internal static class CatalogReader
{
    public static IReadOnlySet<string> Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new CoverageUsageException($"Compiled catalog assembly does not exist: '{path}'.");
        }

        string fullPath = Path.GetFullPath(path);
        var context = new CatalogLoadContext(fullPath);
        Assembly assembly = context.LoadFromAssemblyPath(fullPath);
        Type catalogType = assembly.GetType("Netrunner.Cards.GatewayBehaviorCatalog", throwOnError: false)
            ?? throw new CoverageUsageException(
                "Compiled catalog assembly does not expose Netrunner.Cards.GatewayBehaviorCatalog.");
        MethodInfo create = catalogType.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
            ?? throw new CoverageUsageException(
                "Compiled catalog does not expose the immutable GatewayBehaviorCatalog.Create() surface.");

        object immutableCatalog;
        try
        {
            immutableCatalog = create.Invoke(null, null)
                ?? throw new CoverageUsageException("Immutable Gateway behavior catalog creation returned null.");
        }
        catch (TargetInvocationException exception)
        {
            throw new CoverageUsageException(
                $"Immutable Gateway behavior catalog creation failed: {exception.InnerException?.Message ?? exception.Message}");
        }

        PropertyInfo allDefinitionIds = immutableCatalog.GetType().GetProperty(
                "AllDefinitionIds",
                BindingFlags.Public | BindingFlags.Instance)
            ?? throw new CoverageUsageException(
                "Immutable Gateway behavior catalog does not expose AllDefinitionIds.");
        if (allDefinitionIds.GetValue(immutableCatalog) is not IEnumerable values)
        {
            throw new CoverageUsageException(
                "Immutable Gateway behavior catalog AllDefinitionIds is not enumerable.");
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
                isTrait && string.Equals(first, "GatewayCard", StringComparison.Ordinal)
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

    private CoverageReport(
        RegistryDocument registry,
        IReadOnlyList<CardReport> cards,
        IReadOnlyList<string> diagnostics)
    {
        Registry = registry;
        Cards = cards;
        Diagnostics = diagnostics;
        Counts = new CoverageCounts(
            cards.Count,
            cards.Count(static card => card.Registered),
            cards.Count(static card => card.Registered && card.Tests.Count > 0),
            cards.Count(static card => card.FullTextComplete),
            cards.Count(static card => card.Partial),
            cards.Count(static card => !card.Registered));
    }

    public RegistryDocument Registry { get; }

    public IReadOnlyList<CardReport> Cards { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public CoverageCounts Counts { get; }

    public static CoverageReport Create(CoverageInput input)
    {
        var diagnostics = new List<string>();
        var cards = new List<CardReport>();
        foreach (RegistryCard card in input.Registry.Cards.OrderBy(static card => card.Code, StringComparer.Ordinal))
        {
            string title = input.Metadata.TryGetValue(card.Code, out CardMetadata? metadata)
                ? metadata.Title
                : card.Title;
            IReadOnlyList<string> tests = input.Tests.TryGetValue(card.Code, out IReadOnlyList<string>? found)
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

            if (!registered && string.Equals(card.Status, "registered", StringComparison.Ordinal))
            {
                diagnostics.Add($"card {card.Code} is registry-registered but absent from the compiled catalog");
            }

            if (registered && tests.Count == 0)
            {
                diagnostics.Add($"card {card.Code} ({title}) is registered but has no GatewayCard test metadata");
            }

            if (fullTextComplete && !registered)
            {
                diagnostics.Add($"card {card.Code} ({title}) is marked full-text complete but is not registered");
            }

            RegistryBatch? batch = input.Registry.Batches.FirstOrDefault(
                candidate => string.Equals(candidate.Id, card.Batch, StringComparison.Ordinal));
            if (batch?.CompletionRequired == true
                && (!registered || !fullTextComplete || tests.Count == 0
                    || !string.Equals(card.ReviewStatus, "complete", StringComparison.Ordinal)
                    || card.Citations.Count == 0))
            {
                diagnostics.Add($"completed batch {card.Batch} card {card.Code} is missing a required CARD-401 field");
            }

            cards.Add(new CardReport(
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

        foreach (string code in input.RegisteredCodes.Except(RegistryValidator.Codes, StringComparer.Ordinal).Order())
        {
            diagnostics.Add($"compiled catalog contains unknown Gateway definition '{code}'");
        }

        if (cards.Count != RegistryValidator.Codes.Count)
        {
            diagnostics.Add($"coverage report has {cards.Count} cards; expected {RegistryValidator.Codes.Count}");
        }

        return new CoverageReport(input.Registry, cards, diagnostics);
    }

    public void Write(string markdownPath, string jsonPath)
    {
        CreateParentDirectory(markdownPath);
        CreateParentDirectory(jsonPath);
        File.WriteAllText(markdownPath, RenderMarkdown());
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            cardSet = Registry.CardSet,
            rulesVersion = Registry.RulesVersion,
            counts = Counts,
            diagnostics = Diagnostics,
            cards = Cards,
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
        builder.AppendLine("# System Gateway coverage");
        builder.AppendLine();
        builder.AppendLine("> Generated by `tools/Netrunner.CardCoverage`; test metadata is read from compiled assemblies without executing tests.");
        builder.AppendLine();
        builder.Append("- Rules: `").Append(Registry.RulesVersion).AppendLine("`");
        builder.Append("- Cards: `").Append(Counts.Total).AppendLine("` expected");
        builder.Append("- Registered: `").Append(Counts.Registered).AppendLine("`");
        builder.Append("- Registered with positive test metadata: `").Append(Counts.Tested).AppendLine("`");
        builder.Append("- Full-text complete: `").Append(Counts.FullTextComplete).AppendLine("`");
        builder.Append("- Partial registrations: `").Append(Counts.Partial).AppendLine("` (Nico Campaign and Offworld Office remain explicitly incomplete)");
        builder.Append("- Unregistered: `").Append(Counts.Unregistered).AppendLine("`");
        builder.AppendLine();
        builder.AppendLine("A partial registration is not full coverage. A completed batch requires full behavior registration, a positive test marker, review, citations, and an observation note.");
        builder.AppendLine();
        builder.AppendLine("| Code | Card | Batch | Slice | Registration | Text | Review | Reviewer | Positive tests | CR/ruling citations | Observation | Hidden-info impact | Serialization impact |");
        builder.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (CardReport card in Cards)
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

internal sealed record CoverageCounts(
    int Total,
    int Registered,
    int Tested,
    int FullTextComplete,
    int Partial,
    int Unregistered);

internal sealed record CardReport(
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
