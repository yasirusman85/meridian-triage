using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Triage;

namespace Meridian.Infrastructure.Triage;

/// <summary>
/// The default model adapter (AD-1): answers from recorded responses instead of calling a provider.
/// Each file in <c>Triage/Recorded/*.json</c> holds <c>text</c> and <c>response</c>; a request is matched by the
/// SHA-256 of its whitespace-trimmed text. The recorded <c>response</c> is returned as raw JSON, unvalidated, so it
/// goes through the same validator as a real model's output.
/// </summary>
public sealed class StubTriageModel : ITriageModel
{
    public const string StubModelId = "stub";

    /// <summary>Where the recorded responses are copied in the build output.</summary>
    public static readonly string DefaultDirectory = Path.Combine(AppContext.BaseDirectory, "Triage", "Recorded");

    private readonly IReadOnlyDictionary<string, string> _responsesByHash;

    public StubTriageModel(IReadOnlyDictionary<string, string> responsesByTextHash)
    {
        _responsesByHash = responsesByTextHash;
    }

    public string ModelId => StubModelId;

    public string PromptVersion => TriageContract.Version;

    /// <summary>Number of recorded responses loaded.</summary>
    public int Count => _responsesByHash.Count;

    /// <summary>Loads every <c>*.json</c> file in <paramref name="directory"/>. A missing directory gives an empty stub.</summary>
    public static StubTriageModel FromDirectory(string directory)
    {
        var responses = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
            {
                var (text, response) = ReadRecording(path);
                if (!responses.TryAdd(Hash(text), response))
                {
                    throw new InvalidDataException($"Recorded response {Path.GetFileName(path)} duplicates the text of another recording.");
                }
            }
        }

        return new StubTriageModel(responses);
    }

    /// <summary>Lowercase hex SHA-256 of the UTF-8 bytes of the trimmed text.</summary>
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim()))).ToLowerInvariant();

    public Task<TriageModelResult> PrepareAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var result = _responsesByHash.TryGetValue(Hash(request.Text), out var raw)
            ? TriageModelResult.Output(raw, ModelId, PromptVersion, watch.ElapsedMilliseconds)
            : TriageModelResult.Failure(TriageFailureReasons.NoRecordedResponse, ModelId, PromptVersion, watch.ElapsedMilliseconds);
        return Task.FromResult(result);
    }

    private static (string Text, string Response) ReadRecording(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("response", out var response))
        {
            throw new InvalidDataException($"Recorded response {Path.GetFileName(path)} needs a string 'text' and a 'response'.");
        }

        // A string response is returned as is (lets a recording hold deliberately malformed output).
        var raw = response.ValueKind == JsonValueKind.String ? response.GetString()! : response.GetRawText();
        return (text.GetString()!, raw);
    }
}
