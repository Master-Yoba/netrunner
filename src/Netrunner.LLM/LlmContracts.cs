using Netrunner.Core;

namespace Netrunner.LLM;

public sealed record OpenAiCompatibleOptions(
    Uri BaseUri,
    string Model,
    string? ApiKey,
    TimeSpan Timeout);

public sealed record LlmActionRequest(
    Observation Observation,
    IReadOnlyList<ActionId> AllowedActionIds);

public sealed record LlmActionResponse(ActionId ActionId, string? Intent);

public interface ILlmActionTransport
{
    ValueTask<LlmActionResponse> ChooseActionAsync(
        LlmActionRequest request,
        CancellationToken cancellationToken = default);
}

