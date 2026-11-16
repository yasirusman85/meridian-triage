using System.Globalization;
using Meridian.Infrastructure.Triage;
using Microsoft.Extensions.Configuration;

namespace Meridian.Eval;

/// <summary>
/// The CLI: <c>dotnet run --project src/Meridian.Eval -- [--cases eval/cases] [--out eval/results]</c>.
/// Other <c>--Key=value</c> arguments and environment variables (<c>Triage__Model</c>) are configuration;
/// <c>Eval:InputPricePerMillion</c> and <c>Eval:OutputPricePerMillion</c> set the cost estimate's prices.
/// Exit codes: 0 thresholds pass, 1 they fail, 2 a case file is unusable or the arguments are wrong.
/// </summary>
public static class EvalCommand
{
    public const int Passed = 0;
    public const int Failed = 1;
    public const int BadInput = 2;

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string?>? openRouterApiKey = null,
        DateOnly? date = null,
        CancellationToken cancellationToken = default)
    {
        string? casesDirectory = null;
        string? outDirectory = null;
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cases" when i + 1 < args.Length:
                    casesDirectory = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outDirectory = args[++i];
                    break;
                case "--cases" or "--out":
                    await error.WriteLineAsync($"{args[i]} needs a directory.");
                    return BadInput;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }

        var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
        casesDirectory ??= Path.Combine(root ?? ".", "eval", "cases");
        outDirectory ??= Path.Combine(root ?? ".", "eval", "results");

        IConfiguration configuration;
        try
        {
            configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddCommandLine(rest.ToArray())
                .Build();
        }
        catch (FormatException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return BadInput;
        }

        EvalPrices prices;
        IReadOnlyList<EvalCase> cases;
        try
        {
            prices = new EvalPrices(
                Price(configuration, "Eval:InputPricePerMillion", EvalPrices.Default.InputPerMillion),
                Price(configuration, "Eval:OutputPricePerMillion", EvalPrices.Default.OutputPerMillion));
            cases = EvalCaseLoader.LoadDirectory(casesDirectory);
        }
        catch (Exception ex) when (ex is EvalCaseException or DirectoryNotFoundException or FormatException)
        {
            await error.WriteLineAsync(ex.Message);
            return BadInput;
        }

        var keyReader = openRouterApiKey ?? TriageOptions.ReadApiKey;
        await output.WriteLineAsync($"Running {cases.Count} cases from {casesDirectory}");
        var run = await EvalRunner.RunAsync(cases, configuration, keyReader, output, cancellationToken);
        var score = EvalScorer.Score(cases, run.Outcomes, prices);
        var (jsonPath, markdownPath) = EvalReport.Write(outDirectory, date ?? DateOnly.FromDateTime(DateTime.UtcNow), run, score);

        var s = score.Summary;
        await output.WriteLineAsync(
            $"Model {run.ModelId}: emergency recall {Rate(s.EmergencyRecall)}, category accuracy {Rate(s.CategoryAccuracy)}, "
            + $"{score.Failures.Count()} failures ({score.Failures.Count(f => f.Harm == Harm.Critical)} critical). {(s.Passed ? "PASS" : "FAIL")}");
        await output.WriteLineAsync($"Wrote {jsonPath}");
        await output.WriteLineAsync($"Wrote {markdownPath}");
        return s.Passed ? Passed : Failed;
    }

    /// <summary>The nearest directory at or above <paramref name="start"/> that holds <c>eval/cases</c>.</summary>
    public static string? FindRepositoryRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "eval", "cases")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    private static decimal Price(IConfiguration configuration, string key, decimal fallback)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
        {
            throw new FormatException($"{key} must be a non-negative number of USD per million tokens, not '{value}'.");
        }

        return price;
    }

    private static string Rate(Ratio ratio) =>
        ratio.Rate is null ? "n/a" : (ratio.Rate.Value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
