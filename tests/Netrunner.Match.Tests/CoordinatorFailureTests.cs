using Xunit;

namespace Netrunner.Match.Tests;

public sealed class CoordinatorFailureTests
{
    [Fact]
    public async Task UnexpectedFailureFaultsActiveCommandAndClosesQueue()
    {
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions { MetadataResolver = new BrokenResolver() });
        Task<MatchSnapshot> start = coordinator.StartAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => start.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.GetObservationAsync(Core.PlayerSide.Runner));
    }

    private sealed class BrokenResolver : ICardMetadataResolver
    {
        public IReadOnlyList<CardPresentationMetadata> Cards => [];

        public bool TryResolve(string definitionId, out CardPresentationMetadata metadata) =>
            throw new ArgumentException("Invalid fixture metadata.", nameof(definitionId));
    }
}
