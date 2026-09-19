using System.Net;
using System.Text.Json;
using Netrunner.Cards;

namespace Netrunner.Cli;

/// <summary>Explicit, resumable acquisition; never used by the game client.</summary>
public sealed class CardArtworkDownloader
{
    private const string PrintingUrl = "https://api.netrunnerdb.com/api/v3/public/printings?filter[card_set_id]=system_gateway&page[size]=100";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public CardArtworkDownloader(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _delay = delay ?? Task.Delay;
    }

    public async Task<CardArtworkManifest> FetchAsync(string directory, bool refresh = false,
        Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        CardArtworkCache.RejectLinks(directory);
        if (!refresh && File.Exists(CardArtworkCache.SafePath(directory, CardArtworkCache.ManifestName)))
        {
            try { return CardArtworkCache.Validate(directory); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or ArgumentException)
            {
                progress?.Invoke("Repairing incomplete or invalid artwork cache.");
            }
        }

        var printings = new Dictionary<string, Printing>(StringComparer.Ordinal);
        var pages = new HashSet<string>(StringComparer.Ordinal);
        string? url = PrintingUrl;
        while (url is not null)
        {
            if (!IsSource(url, "api.netrunnerdb.com") || !pages.Add(url) || pages.Count > 20)
                throw new InvalidDataException("Invalid or repeated artwork API pagination link.");
            byte[] json = await GetAsync(url, CardArtworkCache.MaxManifestBytes * 8, cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            foreach (JsonElement item in document.RootElement.GetProperty("data").EnumerateArray())
            {
                string id = item.GetProperty("id").GetString()!;
                JsonElement attributes = item.GetProperty("attributes");
                if (!CardArtworkCache.GatewayCodes.Contains(id, StringComparer.Ordinal)
                    || attributes.GetProperty("card_set_id").GetString() != "system_gateway"
                    || attributes.GetProperty("released_by").GetString() != "null_signal_games")
                    throw new InvalidDataException("The artwork listing contains an unexpected card or publisher.");
                JsonElement images = attributes.GetProperty("images").GetProperty("nrdb_classic");
                string? image = images.TryGetProperty("xlarge", out JsonElement large) ? large.GetString() : null;
                image ??= images.GetProperty("large").GetString();
                if (!IsSource(image, "card-images.netrunnerdb.com"))
                    throw new InvalidDataException($"No supported official image URL for {id}.");
                string artist = attributes.GetProperty("display_illustrators").GetString() ?? string.Empty;
                if (!printings.TryAdd(id, new Printing(id, image!, artist)))
                    throw new InvalidDataException($"Duplicate printing {id} in artwork listing.");
            }
            url = document.RootElement.TryGetProperty("links", out JsonElement links)
                && links.TryGetProperty("next", out JsonElement next) && next.ValueKind == JsonValueKind.String
                ? next.GetString() : null;
        }
        if (printings.Count != CardArtworkCache.GatewayCodes.Count)
            throw new InvalidDataException($"Expected 77 Gateway images, found {printings.Count}.");

        var entries = new List<CardArtworkEntry>();
        foreach (string code in CardArtworkCache.GatewayCodes)
        {
            Printing printing = printings[code];
            string receiptPath = CardArtworkCache.SafePath(directory, $".download/{code}.json");
            CardArtworkEntry? entry = !refresh ? ReadReceipt(directory, receiptPath, printing) : null;
            if (entry is null)
            {
                byte[] bytes = await GetAsync(printing.Url, CardArtworkCache.MaxImageBytes, cancellationToken).ConfigureAwait(false);
                string hash = CardArtworkCache.Hash(bytes);
                string extension = CardArtworkCache.ImageExtension(bytes);
                entry = new CardArtworkEntry(code, code, $"images/{hash}{extension}", printing.Url,
                    printing.Artist, DateTimeOffset.UtcNow, bytes.LongLength, hash);
                CardArtworkCache.WriteAtomically(CardArtworkCache.SafePath(directory, entry.Path), bytes);
                CardArtworkCache.WriteAtomically(receiptPath, JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions));
            }
            entries.Add(entry);
            progress?.Invoke($"Artwork {entries.Count}/77: {code}");
        }
        var manifest = new CardArtworkManifest(1, "system_gateway", "en", entries);
        foreach (CardArtworkEntry entry in entries) CardArtworkCache.ReadImage(directory, entry);
        CardArtworkCache.WriteAtomically(CardArtworkCache.SafePath(directory, CardArtworkCache.ManifestName),
            CardArtworkCache.Serialize(manifest));
        return manifest;
    }

    private static CardArtworkEntry? ReadReceipt(string directory, string path, Printing printing)
    {
        try
        {
            CardArtworkEntry? entry = JsonSerializer.Deserialize<CardArtworkEntry>(
                CardArtworkCache.ReadBounded(path, CardArtworkCache.MaxManifestBytes), JsonOptions);
            if (entry is null || entry.DefinitionId != printing.Id || entry.PrintingId != printing.Id
                || entry.SourceUrl != printing.Url || !CardArtworkCache.IsSha256(entry.Sha256)
                || entry.Path != $"images/{entry.Sha256}{Path.GetExtension(entry.Path)}") return null;
            CardArtworkCache.ReadImage(directory, entry);
            return entry;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private async Task<byte[]> GetAsync(string url, int limit, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await _delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            TimeSpan retryDelay = TimeSpan.FromSeconds(attempt + 1);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("NetrunnerSolo-ArtworkCache/1.0 (+https://github.com/Master-Yoba/netrunner)");
                using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Artwork response exceeds the size limit.");
                    await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    using var output = new MemoryStream();
                    byte[] buffer = new byte[8192];
                    int count;
                    while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                    {
                        if (output.Length + count > limit) throw new InvalidDataException("Artwork response exceeds the size limit.");
                        output.Write(buffer, 0, count);
                    }
                    return output.ToArray();
                }
                bool transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!transient || attempt == 2)
                    throw new HttpRequestException($"Artwork download failed: HTTP {(int)response.StatusCode} at {url}.", null, response.StatusCode);
                if (response.Headers.RetryAfter is { } retry)
                {
                    retryDelay = retry.Delta ?? (retry.Date - DateTimeOffset.UtcNow) ?? retryDelay;
                    if (retryDelay > TimeSpan.FromMinutes(1))
                        throw new InvalidDataException($"Image source requests a longer wait; retry after {retry}.");
                    if (retryDelay < TimeSpan.Zero) retryDelay = TimeSpan.Zero;
                }
            }
            catch (HttpRequestException exception) when (exception.StatusCode is null && attempt < 2) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2) { }
            await _delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }
        throw new HttpRequestException($"Artwork download failed at {url}.");
    }

    private static bool IsSource(string? url, string host) => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == host && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);

    private sealed record Printing(string Id, string Url, string Artist);
}
