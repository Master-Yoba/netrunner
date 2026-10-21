using System.Text.Json;
using Netrunner.Cards;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    private static int RunCardArtwork(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        try
        {
            if (args.Length < 2 || args[1] is not ("fetch" or "validate" or "pack" or "unpack"))
                throw new ArgumentException("Usage: card-art fetch|validate|pack|unpack [--cache-dir <directory>] [--json]");
            string operation = args[1];
            var options = new Dictionary<string, string?>(StringComparer.Ordinal);
            string[] allowed = operation switch
            {
                "fetch" => ["--refresh"],
                "pack" => ["--output", "--include-metadata"],
                "unpack" => ["--input", "--metadata-output", "--replace"],
                _ => [],
            };
            for (int i = 2; i < args.Length; i++)
            {
                string option = args[i];
                if (option is not ("--cache-dir" or "--json") && !allowed.Contains(option, StringComparer.Ordinal))
                    throw new ArgumentException($"Unknown option for card-art {operation}: {option}");
                string? value = null;
                if (option is not ("--json" or "--refresh" or "--replace"))
                {
                    if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i]))
                        throw new ArgumentException($"Missing value for {option}.");
                    value = args[i];
                }
                if (!options.TryAdd(option, value)) throw new ArgumentException($"Duplicate option: {option}");
            }
            string cache = CardArtworkCache.ResolveDirectory(options.GetValueOrDefault("--cache-dir"));
            switch (operation)
            {
                case "fetch":
                    using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                    using (var http = new HttpClient(handler))
                        new CardArtworkDownloader(http).FetchAsync(cache, options.ContainsKey("--refresh"),
                            progress: message => error.WriteLine(message)).GetAwaiter().GetResult();
                    break;
                case "validate":
                    CardArtworkCache.Validate(cache);
                    break;
                case "pack":
                    CardArtworkArchive.Pack(cache, RequiredOption(options, "--output"), options.GetValueOrDefault("--include-metadata"));
                    break;
                case "unpack":
                    CardArtworkArchive.Unpack(RequiredOption(options, "--input"), cache,
                        options.GetValueOrDefault("--metadata-output"), options.ContainsKey("--replace"));
                    break;
            }
            output.WriteLine(json
                ? JsonSerializer.Serialize(new { status = "ok", operation, cacheDirectory = cache, cards = 77 })
                : $"card-art {operation}=PASS cards=77 cache={cache}");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or JsonException or HttpRequestException or OperationCanceledException or InvalidOperationException
            or KeyNotFoundException or CardDataValidationException)
        {
            int code = exception is ArgumentException ? 2 : 1;
            if (json) output.WriteLine(JsonSerializer.Serialize(new { status = "error", error = exception.Message, exitCode = code }));
            else error.WriteLine($"card-art: {exception.Message}");
            return code;
        }
    }

    private static string RequiredOption(IReadOnlyDictionary<string, string?> options, string name) =>
        options.GetValueOrDefault(name) ?? throw new ArgumentException($"Missing required option {name}.");
}
