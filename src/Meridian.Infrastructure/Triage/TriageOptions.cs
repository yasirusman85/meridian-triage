using Microsoft.Extensions.Configuration;

namespace Meridian.Infrastructure.Triage;

/// <summary>
/// The <c>Triage</c> configuration section (AD-1). The API key is not here: it is read from the
/// <c>OPENROUTER_API_KEY</c> environment variable only, never from appsettings.
/// </summary>
public sealed class TriageOptions
{
    public const string SectionName = "Triage";
    public const string ApiKeyVariable = "OPENROUTER_API_KEY";
    public const string StubProvider = "stub";
    public const string OpenRouterProvider = "openrouter";
    public const string DefaultModel = "anthropic/claude-haiku-5.5";
    public const string DefaultOpenRouterBaseUrl = "https://openrouter.ai/api/v1";
    public const string DefaultPromptVersion = "triage.v2";

    /// <summary>
    /// <c>stub</c>, <c>openrouter</c>, or empty. Empty means OpenRouter when the key is set, otherwise the stub.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>The OpenRouter model id sent with each request and recorded in events.</summary>
    public string Model { get; init; } = DefaultModel;

    public string OpenRouterBaseUrl { get; init; } = DefaultOpenRouterBaseUrl;

    /// <summary>The system prompt file, <c>Prompts/&lt;version&gt;.md</c>, recorded in events. The output contract stays <c>triage.v1</c>.</summary>
    public string PromptVersion { get; init; } = DefaultPromptVersion;

    public static TriageOptions From(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new TriageOptions
        {
            Provider = Blank(section["Provider"]),
            Model = Blank(section["Model"]) ?? DefaultModel,
            OpenRouterBaseUrl = Blank(section["OpenRouterBaseUrl"]) ?? DefaultOpenRouterBaseUrl,
            PromptVersion = Blank(section["PromptVersion"]) ?? DefaultPromptVersion,
        };
    }

    /// <summary>Reads the key from the environment. Null when unset or blank.</summary>
    public static string? ReadApiKey() => Blank(Environment.GetEnvironmentVariable(ApiKeyVariable));

    /// <summary>
    /// True when the OpenRouter adapter should be used. Throws for an unknown provider, or for
    /// <c>openrouter</c> without a key.
    /// </summary>
    public bool UsesOpenRouter(string? apiKey)
    {
        switch (Provider?.ToLowerInvariant())
        {
            case null:
                return apiKey is not null;
            case StubProvider:
                return false;
            case OpenRouterProvider when apiKey is null:
                throw new InvalidOperationException(
                    $"{SectionName}:Provider is '{OpenRouterProvider}' but the {ApiKeyVariable} environment variable is not set.");
            case OpenRouterProvider:
                return true;
            default:
                throw new InvalidOperationException(
                    $"Unknown {SectionName}:Provider '{Provider}'. Use '{StubProvider}' or '{OpenRouterProvider}'.");
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
