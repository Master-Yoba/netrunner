namespace Netrunner.Core;

/// <summary>
/// What a search agent may do with the hidden part of the game (AI plan A3):
/// deal simulation states consistent with its own observation. A host that
/// holds the authoritative state attaches one to the observation it hands the
/// agent (<see cref="ObservationBuilder.Build(GameState, PlayerSide, IBehaviorCatalog, IRuntimeCardCatalog)"/>).
/// The state never leaves this object: the only way to it is
/// <see cref="ObservationSampler"/>, which never reads a hidden identity, so
/// hidden-state twins get identical samples for the same seed. Samples are
/// simulation states and can only be applied in <see cref="ApplyMode.Simulation"/>.
/// </summary>
public sealed class ObservationDeterminizer
{
    private readonly GameState _state;

    internal ObservationDeterminizer(GameState state, PlayerSide viewer, IBehaviorCatalog behavior, IRuntimeCardCatalog runtime)
    {
        _state = state;
        Viewer = viewer;
        Behavior = behavior;
        Runtime = runtime;
    }

    public PlayerSide Viewer { get; }
    public IBehaviorCatalog Behavior { get; }
    public IRuntimeCardCatalog Runtime { get; }

    /// <summary>A simulation state consistent with the viewer's observation, dealt from <paramref name="random"/>.</summary>
    public GameState Sample(SamplerKnowledge knowledge, OpponentCardPool pool, DeterministicRandom random) =>
        ObservationSampler.Sample(_state, Viewer, knowledge, pool, Runtime, random);
}
