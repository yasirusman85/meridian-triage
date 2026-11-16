using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Application.Triage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Infrastructure.Triage;

/// <summary>
/// The OpenRouter model adapter (AD-1): one call to OpenRouter's OpenAI-compatible chat completions API with the
/// versioned system prompt and the message as quoted data (AD-10). Returns the raw content unvalidated; timeout,
/// retry, validation and fall-back belong to <see cref="PrepareJob"/>. Any provider problem (non-2xx, network
/// error, empty choice, refusal, moderation error) is <see cref="TriageFailureReasons.Unavailable"/>.
/// </summary>
public sealed class OpenRouterTriageModel : ITriageModel
{
    /// <summary>The named <see cref="HttpClient"/>; its base address and headers are set at registration.</summary>
    public const string HttpClientName = "openrouter";

    /// <summary>Where the system prompt is copied in the build output.</summary>
    public static readonly string DefaultPromptPath =
        Path.Combine(AppContext.BaseDirectory, "Prompts", $"{TriageContract.Version}.md");

    /// <summary>
    /// Output cap. A triage.v1 answer is a few hundred tokens; without a cap OpenRouter reserves credit for the
    /// model's full output length and answers 402 on a small balance.
    /// </summary>
    public const int MaxOutputTokens = 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _systemPrompt;
    private readonly ILogger _logger;

    public OpenRouterTriageModel(
        IHttpClientFactory httpClientFactory,
        string modelId,
        string systemPrompt,
        ILogger<OpenRouterTriageModel>? logger = null,
        string? promptVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        _httpClientFactory = httpClientFactory;
        ModelId = modelId;
        _systemPrompt = systemPrompt;
        _logger = logger ?? NullLogger<OpenRouterTriageModel>.Instance;
        PromptVersion = string.IsNullOrWhiteSpace(promptVersion) ? TriageContract.Version : promptVersion;
    }

    public string ModelId { get; }

    /// <summary>The system prompt version sent (e.g. <c>triage.v2</c>); the output contract is always <c>triage.v1</c>.</summary>
    public string PromptVersion { get; }

    /// <summary>Reads the <c>triage.v1</c> system prompt from <paramref name="path"/>.</summary>
    public static string LoadSystemPrompt(string? path = null) =>
        File.ReadAllText(path ?? DefaultPromptPath, Encoding.UTF8);

    /// <summary>Reads the system prompt <c>Prompts/&lt;version&gt;.md</c> from the build output.</summary>
    public static string LoadSystemPromptVersion(string version) =>
        LoadSystemPrompt(Path.Combine(AppContext.BaseDirectory, "Prompts", $"{version}.md"));

    /// <summary>
    /// The user turn: the received time and the message text between <c>&lt;message&gt;</c> tags, nothing about
    /// the sender (AD-10). A closing tag inside the text is neutralised so the text cannot end the quoted block.
    /// </summary>
    public static string BuildUserTurn(TriageRequest request)
    {
        var receivedUtc = DateTime.SpecifyKind(request.ReceivedAtUtc, DateTimeKind.Utc);
        var text = request.Text.Replace("</message", "&lt;/message", StringComparison.OrdinalIgnoreCase);
        return $"Received: {receivedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}\n"
            + $"<message>\n{text}\n</message>";
    }

    /// <summary>The chat completions request body.</summary>
    public JsonObject BuildRequestBody(TriageRequest request) => new()
    {
        ["model"] = ModelId,
        ["messages"] = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = _systemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = BuildUserTurn(request) },
        },
        ["response_format"] = new JsonObject { ["type"] = "json_object" },
        ["temperature"] = 0,
        ["max_tokens"] = MaxOutputTokens,
    };

    public async Task<TriageModelResult> PrepareAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync("chat/completions", BuildRequestBody(request), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Status only: a provider error body may echo the tenant's text.
                _logger.LogWarning("OpenRouter returned {StatusCode}", (int)response.StatusCode);
                return Unavailable(watch);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(body, watch);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout, not the caller's cancellation.
            _logger.LogWarning("OpenRouter request timed out in HttpClient");
            return Unavailable(watch);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("OpenRouter request failed: {ExceptionType}", ex.GetType().Name);
            return Unavailable(watch);
        }
    }

    private TriageModelResult Parse(string body, Stopwatch watch)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _))
            {
                // OpenRouter reports some provider and moderation errors inside a 200 body.
                _logger.LogWarning("OpenRouter returned an error body");
                return Unavailable(watch);
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                _logger.LogWarning("OpenRouter returned no choices");
                return Unavailable(watch);
            }

            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String
                && finish.GetString() is "content_filter" or "error")
            {
                _logger.LogWarning("OpenRouter finished with {FinishReason}", finish.GetString());
                return Unavailable(watch);
            }

            if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("OpenRouter choice has no message");
                return Unavailable(watch);
            }

            if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(refusal.GetString()))
            {
                _logger.LogWarning("OpenRouter model refused");
                return Unavailable(watch);
            }

            var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("OpenRouter returned empty content");
                return Unavailable(watch);
            }

            int? inputTokens = null;
            int? outputTokens = null;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = ReadInt(usage, "prompt_tokens");
                outputTokens = ReadInt(usage, "completion_tokens");
            }

            return TriageModelResult.Output(
                StripFences(content), ModelId, PromptVersion, watch.ElapsedMilliseconds, inputTokens, outputTokens);
        }
        catch (JsonException)
        {
            _logger.LogWarning("OpenRouter returned a body that is not JSON");
            return Unavailable(watch);
        }
    }

    /// <summary>Removes a surrounding Markdown code fence (<c>```json ... ```</c>) if present.</summary>
    public static string StripFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var inner = trimmed[(firstNewline + 1)..];
        var closing = inner.LastIndexOf("```", StringComparison.Ordinal);
        if (closing >= 0)
        {
            inner = inner[..closing];
        }

        return inner.Trim();
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private TriageModelResult Unavailable(Stopwatch watch) =>
        TriageModelResult.Failure(TriageFailureReasons.Unavailable, ModelId, PromptVersion, watch.ElapsedMilliseconds);
}
