using System.Net;
using System.Text.Json;
using Netrunner.Cards;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class CardArtworkDownloaderTests
{
    [Fact]
    public async Task DownloadUsesPaginationAndPreferredImagesThenReusesValidCacheOffline()
    {
        using var fixture = new CardArtworkTestCache();
        using var handler = new ArtworkHandler();
        using var http = new HttpClient(handler);
        var downloader = new CardArtworkDownloader(http, static (_, _) => Task.CompletedTask);
        CardArtworkManifest manifest = await downloader.FetchAsync(fixture.Cache);
        Assert.Equal(77, manifest.Cards.Count);
        Assert.All(manifest.Cards, entry => Assert.Contains("/xlarge/", entry.SourceUrl, StringComparison.Ordinal));
        Assert.Equal(79, handler.Requests.Count); // Two listing pages and 77 images.
        Assert.All(handler.UserAgents, value => Assert.Contains("NetrunnerSolo-ArtworkCache", value, StringComparison.Ordinal));
        handler.Failure = static _ => HttpStatusCode.Forbidden;
        await downloader.FetchAsync(fixture.Cache);
        Assert.Equal(79, handler.Requests.Count);
        CardArtworkCache.Validate(fixture.Cache);
    }

    [Fact]
    public async Task PartialDownloadResumesAndFailedRefreshPreservesActiveManifest()
    {
        using var fixture = new CardArtworkTestCache();
        using var handler = new ArtworkHandler { Failure = uri => uri.AbsolutePath.Contains("30003", StringComparison.Ordinal) ? HttpStatusCode.Forbidden : null };
        using var http = new HttpClient(handler);
        var downloader = new CardArtworkDownloader(http, static (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.FetchAsync(fixture.Cache));
        Assert.False(File.Exists(Path.Combine(fixture.Cache, "manifest.json")));
        handler.Requests.Clear();
        handler.Failure = null;
        await downloader.FetchAsync(fixture.Cache);
        Assert.Equal(77, handler.Requests.Count); // Two listing pages, 75 remaining images.
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("30001", StringComparison.Ordinal));
        byte[] original = File.ReadAllBytes(Path.Combine(fixture.Cache, "manifest.json"));
        handler.Failure = uri => uri.Host == "card-images.netrunnerdb.com" ? HttpStatusCode.Forbidden : null;
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.FetchAsync(fixture.Cache, refresh: true));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Cache, "manifest.json")));
        CardArtworkCache.Validate(fixture.Cache);
    }

    [Fact]
    public async Task TransientResponsesRetryWithRetryAfterButForbiddenDoesNot()
    {
        using var fixture = new CardArtworkTestCache();
        int failures = 0;
        using var handler = new ArtworkHandler { Failure = _ => failures++ < 2 ? HttpStatusCode.TooManyRequests : null };
        using var http = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var downloader = new CardArtworkDownloader(http, (duration, _) => { delays.Add(duration); return Task.CompletedTask; });
        await downloader.FetchAsync(fixture.Cache);
        Assert.Equal(2, delays.Count(duration => duration == TimeSpan.FromSeconds(3)));
        handler.Requests.Clear();
        handler.Failure = static _ => HttpStatusCode.Forbidden;
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.FetchAsync(fixture.Cache, refresh: true));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("publisher")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("pagination")]
    [InlineData("html")]
    public async Task InvalidListingOrImageNeverPublishesCache(string defect)
    {
        using var fixture = new CardArtworkTestCache();
        using var handler = new ArtworkHandler { Defect = defect };
        using var http = new HttpClient(handler);
        var downloader = new CardArtworkDownloader(http, static (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.FetchAsync(fixture.Cache));
        Assert.False(File.Exists(Path.Combine(fixture.Cache, "manifest.json")));
    }

    [Fact]
    public async Task MissingHighResolutionUrlFallsBackToAdvertisedLargeImage()
    {
        using var fixture = new CardArtworkTestCache();
        using var handler = new ArtworkHandler { Defect = "no-xlarge" };
        using var http = new HttpClient(handler);
        var downloader = new CardArtworkDownloader(http, static (_, _) => Task.CompletedTask);
        CardArtworkManifest manifest = await downloader.FetchAsync(fixture.Cache);
        Assert.All(manifest.Cards, entry => Assert.Contains("/large/", entry.SourceUrl, StringComparison.Ordinal));
    }

    private sealed class ArtworkHandler : HttpMessageHandler
    {
        internal List<Uri> Requests { get; } = [];
        internal List<string> UserAgents { get; } = [];
        internal Func<Uri, HttpStatusCode?>? Failure { get; set; }
        internal string? Defect { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            if (Failure?.Invoke(uri) is { } status)
            {
                var failure = new HttpResponseMessage(status);
                failure.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
                return Task.FromResult(failure);
            }
            if (uri.Host == "card-images.netrunnerdb.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Defect == "html" ? "<html>not an image</html>"u8.ToArray() : CardArtworkTestCache.ImageBytes),
                });
            bool second = uri.Query.Contains("second", StringComparison.Ordinal);
            IEnumerable<string> ids = second ? CardArtworkCache.GatewayCodes.Skip(40) : CardArtworkCache.GatewayCodes.Take(40);
            if (Defect == "duplicate") ids = ids.Append("30001");
            if (Defect == "missing") ids = ids.Where(code => code != "30077");
            object[] rows = ids.Select(code => new
            {
                id = code,
                attributes = new
                {
                    card_set_id = "system_gateway",
                    released_by = Defect == "publisher" ? "other" : "null_signal_games",
                    display_illustrators = "Test illustrator",
                    images = new
                    {
                        nrdb_classic = new Dictionary<string, string?>
                        {
                            ["xlarge"] = Defect == "no-xlarge" ? null : $"https://card-images.netrunnerdb.com/xlarge/{code}.png",
                            ["large"] = $"https://card-images.netrunnerdb.com/large/{code}.png",
                        }
                    },
                },
            }).ToArray();
            string? next = Defect == "pagination" ? uri.AbsoluteUri : second ? null : "https://api.netrunnerdb.com/api/v3/public/printings?page=second";
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { data = rows, links = new { next } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(json) });
        }
    }
}
