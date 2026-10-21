using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Netrunner.LLM;

/// <summary>A tool the model may call: its name, what it does, and its arguments as a JSON schema.</summary>
public sealed record LlmTool(string Name, string Description, JsonObject Parameters);

/// <summary>A call the model made to a tool, with its arguments as JSON text.</summary>
public sealed record LlmToolCall(string Id, string Name, string Arguments);

/// <summary>One chat message: system, user, assistant (possibly with tool calls) or a tool's result.</summary>
public sealed record LlmChatMessage(string Role, string? Content, IReadOnlyList<LlmToolCall>? ToolCalls = null, string? ToolCallId = null)
{
    public static LlmChatMessage System(string content) => new("system", content);

    public static LlmChatMessage User(string content) => new("user", content);

    public static LlmChatMessage Tool(string callId, string content) => new("tool", content, ToolCallId: callId);
}

/// <summary>
/// Sampling for one request; null leaves the server's default. <see cref="ThinkingBudget"/> caps a reasoning
/// model's thinking in tokens (llama.cpp's <c>thinking_budget_tokens</c>).
/// </summary>
public sealed record LlmSampling(double? Temperature = null, double? TopP = null, int? TopK = null, int? Seed = null,
    string? ReasoningEffort = null, int? MaxTokens = null, int? ThinkingBudget = null);

/// <summary>
/// One chat request. A <see cref="AnswerSchema"/> (a JSON schema) constrains the answer to that shape; after a
/// thinking budget runs out, a model without one tends to keep thinking in its answer.
/// </summary>
public sealed record LlmChatRequest(IReadOnlyList<LlmChatMessage> Messages, IReadOnlyList<LlmTool> Tools, LlmSampling Sampling,
    JsonObject? AnswerSchema = null);

public sealed record LlmUsage(int PromptTokens, int CompletionTokens);

public sealed record LlmChatResponse(LlmChatMessage Message, string? FinishReason, LlmUsage? Usage);

/// <summary>A chat model with tool calling, whatever serves it.</summary>
public interface ILlmChatClient
{
    Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// A chat client for any server with an OpenAI-compatible <c>/chat/completions</c> API (llama.cpp,
/// Ollama, vLLM, LM Studio). Sampling fields are sent only when set; <c>top_k</c>, <c>reasoning_effort</c> and
/// <c>thinking_budget_tokens</c> are extensions some servers read and others ignore.
/// </summary>
public sealed class OpenAiCompatibleChatClient(OpenAiCompatibleOptions options, HttpClient? client = null) : ILlmChatClient, IDisposable
{
    private readonly HttpClient _client = client ?? new HttpClient { Timeout = options.Timeout };
    private readonly bool _owns = client is null;

    public async Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = new JsonArray([.. request.Messages.Select(ToJson)]),
        };
        if (request.Tools.Count > 0)
            body["tools"] = new JsonArray([.. request.Tools.Select(tool => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.Parameters.DeepClone(),
                },
            })]);
        LlmSampling sampling = request.Sampling;
        if (sampling.Temperature is { } temperature) body["temperature"] = temperature;
        if (sampling.TopP is { } topP) body["top_p"] = topP;
        if (sampling.TopK is { } topK) body["top_k"] = topK;
        if (sampling.Seed is { } seed) body["seed"] = seed;
        if (sampling.ReasoningEffort is { } effort) body["reasoning_effort"] = effort;
        if (sampling.MaxTokens is { } maxTokens) body["max_tokens"] = maxTokens;
        if (sampling.ThinkingBudget is { } budget) body["thinking_budget_tokens"] = budget;
        if (request.AnswerSchema is { } schema)
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "answer", ["strict"] = true, ["schema"] = schema.DeepClone() },
            };

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(options.BaseUri.ToString().TrimEnd('/') + "/chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(options.ApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        using HttpResponseMessage response = await _client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The model server answered {(int)response.StatusCode}: {text[..Math.Min(400, text.Length)]}");
        return Parse(text);
    }

    /// <summary>Reads a <c>/chat/completions</c> response: the first choice's message, finish reason and usage.</summary>
    public static LlmChatResponse Parse(string json)
    {
        JsonNode root = JsonNode.Parse(json) ?? throw new InvalidDataException("The model server returned an empty response.");
        JsonNode choice = root["choices"]?[0] ?? throw new InvalidDataException("The model server returned no choices.");
        JsonNode message = choice["message"] ?? throw new InvalidDataException("The model server returned no message.");
        List<LlmToolCall>? calls = message["tool_calls"] is JsonArray array
            ? [.. array.OfType<JsonNode>().Select((call, index) => new LlmToolCall(
                (string?)call["id"] ?? $"call-{index}",
                (string?)call["function"]?["name"] ?? "",
                call["function"]?["arguments"] is JsonValue value && value.TryGetValue(out string? arguments) ? arguments
                    : call["function"]?["arguments"]?.ToJsonString() ?? "{}"))]
            : null;
        LlmUsage? usage = root["usage"] is JsonObject used
            ? new LlmUsage((int?)used["prompt_tokens"] ?? 0, (int?)used["completion_tokens"] ?? 0)
            : null;
        return new LlmChatResponse(new LlmChatMessage("assistant", (string?)message["content"], calls is { Count: > 0 } ? calls : null),
            (string?)choice["finish_reason"], usage);
    }

    private static JsonNode ToJson(LlmChatMessage message)
    {
        var node = new JsonObject { ["role"] = message.Role, ["content"] = message.Content };
        if (message.ToolCalls is { Count: > 0 } calls)
            node["tool_calls"] = new JsonArray([.. calls.Select(call => (JsonNode)new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments },
            })]);
        if (message.ToolCallId is not null) node["tool_call_id"] = message.ToolCallId;
        return node;
    }

    public void Dispose()
    {
        if (_owns) _client.Dispose();
    }

    /// <summary>
    /// Options from the environment, as <c>.env</c> sets them: NETRUNNER_LLM_BASE_URL, NETRUNNER_LLM_MODEL,
    /// NETRUNNER_LLM_API_KEY and NETRUNNER_LLM_TIMEOUT_SECONDS. Null when no base URL or model is set.
    /// </summary>
    public static OpenAiCompatibleOptions? FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        string? baseUrl = read("NETRUNNER_LLM_BASE_URL");
        string? model = read("NETRUNNER_LLM_MODEL");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model)) return null;
        int seconds = int.TryParse(read("NETRUNNER_LLM_TIMEOUT_SECONDS"), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 600;
        return new OpenAiCompatibleOptions(new Uri(baseUrl), model, read("NETRUNNER_LLM_API_KEY"), TimeSpan.FromSeconds(seconds));
    }
}
