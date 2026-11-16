using System.Text.Json;
using Meridian.Application.Abstractions;
using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;
using Meridian.Domain.Rules;
using Meridian.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Eval;

/// <summary>The outcome of running every case: what the pipeline did with each, and which model did it.</summary>
public sealed record EvalRun(string ModelId, string PromptVersion, IReadOnlyList<CaseOutcome> Outcomes);

/// <summary>
/// Runs the cases through the product's own intake pipeline (AD-12): <see cref="SubmitMessage"/> with the
/// Infrastructure DI (EF Core + SQLite, the configured <see cref="ITriageModel"/>, the outbox), against a throwaway
/// SQLite file that is migrated and seeded like the app's. Only the clock is replaced, so each message is received at
/// its case's <c>received_at</c>; the model is wrapped to record its results. Cases run one after another in the given
/// order, all in the one database, so a follow-up finds the job its earlier case made.
/// </summary>
public static class EvalRunner
{
    /// <param name="configuration">Triage settings (<c>Triage:Model</c>, <c>Triage:Provider</c>); the connection string is overridden.</param>
    /// <param name="openRouterApiKey">Supplies the OpenRouter key; null from it means the stub. Tests pass <c>() =&gt; null</c>.</param>
    public static async Task<EvalRun> RunAsync(
        IReadOnlyList<EvalCase> cases,
        IConfiguration configuration,
        Func<string?>? openRouterApiKey = null,
        TextWriter? progress = null,
        CancellationToken cancellationToken = default)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"meridian-eval-{Guid.NewGuid():N}.db");
        try
        {
            var provider = BuildServices(configuration, databasePath, openRouterApiKey, out var clock, out var recorder);
            await using (provider)
            {
                await provider.MigrateMeridianDatabaseAsync(cancellationToken);
                var outcomes = new List<CaseOutcome>();
                foreach (var evalCase in cases)
                {
                    var outcome = await RunCaseAsync(provider, clock, recorder, evalCase, cancellationToken);
                    outcomes.Add(outcome);
                    progress?.WriteLine($"{evalCase.Id}: {Describe(outcome)}");
                }

                return new EvalRun(recorder.ModelId, recorder.PromptVersion, outcomes);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(databasePath);
        }
    }

    private static ServiceProvider BuildServices(
        IConfiguration configuration,
        string databasePath,
        Func<string?>? openRouterApiKey,
        out EvalClock clock,
        out RecordingTriageModel recorder)
    {
        var settings = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = $"Data Source={databasePath};Pooling=False",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(settings);
        services.AddLogging();
        services.AddMeridianInfrastructure(openRouterApiKey);

        // The same use cases the web app registers (Meridian.Web/Program.cs).
        services.AddScoped<PrepareJob>();
        services.AddScoped<SubmitMessage>();
        services.AddScoped<AcknowledgeTenant>();

        clock = new EvalClock();
        services.Replace(ServiceDescriptor.Singleton<IClock>(clock));

        var modelDescriptor = services.Last(d => d.ServiceType == typeof(ITriageModel));
        services.Remove(modelDescriptor);
        services.AddSingleton(sp => new RecordingTriageModel(CreateInner(sp, modelDescriptor)));
        services.AddSingleton<ITriageModel>(sp => sp.GetRequiredService<RecordingTriageModel>());

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        recorder = provider.GetRequiredService<RecordingTriageModel>();
        return provider;
    }

    private static ITriageModel CreateInner(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is ITriageModel instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (ITriageModel)descriptor.ImplementationFactory(sp);
        }

        return (ITriageModel)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }

    private static async Task<CaseOutcome> RunCaseAsync(
        IServiceProvider provider,
        EvalClock clock,
        RecordingTriageModel recorder,
        EvalCase evalCase,
        CancellationToken cancellationToken)
    {
        clock.UtcNow = evalCase.ReceivedAtUtc;
        recorder.TakeResults();

        SubmitMessageResult submitted;
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var submit = scope.ServiceProvider.GetRequiredService<SubmitMessage>();
            submitted = await submit.ExecuteAsync(new SubmitMessageCommand(evalCase.Sender, evalCase.Text), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Empty(evalCase, recorder.TakeResults(), $"{ex.GetType().Name}: {ex.Message}");
        }

        var calls = recorder.TakeResults();
        if (!submitted.Succeeded)
        {
            var errors = string.Join("; ", submitted.Errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}"));
            return Empty(evalCase, calls, $"rejected: {errors}");
        }

        await using var readScope = provider.CreateAsyncScope();
        var jobs = readScope.ServiceProvider.GetRequiredService<IJobRepository>();
        var loaded = new List<(SubmittedJob Submitted, Job Job)>();
        foreach (var item in submitted.Jobs)
        {
            var job = await jobs.GetAsync(item.JobId, cancellationToken)
                ?? throw new InvalidOperationException($"Job {item.Reference} was reported but not stored.");
            loaded.Add((item, job));
        }

        var newJobs = loaded.Where(j => !j.Submitted.IsFollowUp).Select(j => j.Job).ToList();
        var followUpJobs = loaded.Where(j => j.Submitted.IsFollowUp).Select(j => j.Job).ToList();
        var failureReason = newJobs.Select(FailureReason).FirstOrDefault(r => r is not null);
        var response = failureReason is null ? LastValidResponse(calls) : null;

        var issues = new List<ActualIssue>();
        if (response is null || response.Issues.Count == 0)
        {
            // Model failure, or a valid answer with no issues: the pipeline's one Unsure job stands for the message.
            issues.AddRange(newJobs.Select(j => new ActualIssue(j.Category, j.Urgency, false, null, j.Reference, false)));
        }
        else
        {
            var properties = await readScope.ServiceProvider.GetRequiredService<IPropertyRepository>().ListAsync(cancellationToken);
            var resolver = new PropertyResolver(properties);
            var ruleUrgency = EmergencyRules.Evaluate(evalCase.Text, evalCase.ReceivedAtUtc).Count > 0 ? Urgency.Emergency : Urgency.Routine;
            var byIndex = newJobs
                .Select(j => (Job: j, Index: IssueIndex(j)))
                .Where(x => x.Index is not null)
                .ToDictionary(x => x.Index!.Value, x => x.Job);
            var nextFollowUp = 0;
            for (var index = 0; index < response.Issues.Count; index++)
            {
                var issue = response.Issues[index];
                var propertyText = string.IsNullOrWhiteSpace(issue.PropertyText) ? null : issue.PropertyText;
                if (byIndex.TryGetValue(index, out var job))
                {
                    issues.Add(new ActualIssue(job.Category, job.Urgency, issue.VulnerabilityMentioned, propertyText, job.Reference, false));
                    continue;
                }

                // Attached to an earlier job. Several issues on one job collapse into one entry in the result,
                // so issues are given to the follow-up jobs in order, the last one taking any extra.
                var target = followUpJobs.Count == 0 ? null : followUpJobs[Math.Min(nextFollowUp++, followUpJobs.Count - 1)];
                var category = PrepareJob.CategoryFor(issue, resolver.Resolve(issue.PropertyText));
                var urgency = issue.Urgency > ruleUrgency ? issue.Urgency : ruleUrgency;
                issues.Add(new ActualIssue(category, urgency, issue.VulnerabilityMentioned, propertyText, target?.Reference ?? "?", true));
            }
        }

        var (latency, input, output) = Totals(calls);
        return new CaseOutcome(
            evalCase.Id,
            issues,
            newJobs.Select(j => j.Reference).ToList(),
            followUpJobs.Select(j => j.Reference).ToList(),
            response is not null,
            response is null ? failureReason : null,
            calls.Count,
            latency,
            input,
            output);
    }

    private static CaseOutcome Empty(EvalCase evalCase, IReadOnlyList<TriageModelResult> calls, string error)
    {
        var (latency, input, output) = Totals(calls);
        return new CaseOutcome(evalCase.Id, Array.Empty<ActualIssue>(), Array.Empty<string>(), Array.Empty<string>(), false, null,
            calls.Count, latency, input, output, error);
    }

    private static (long? LatencyMs, int? InputTokens, int? OutputTokens) Totals(IReadOnlyList<TriageModelResult> calls)
    {
        if (calls.Count == 0)
        {
            return (null, null, null);
        }

        int? Sum(Func<TriageModelResult, int?> pick) =>
            calls.Any(c => pick(c) is not null) ? calls.Sum(c => pick(c) ?? 0) : null;

        return (calls.Sum(c => c.LatencyMs), Sum(c => c.InputTokens), Sum(c => c.OutputTokens));
    }

    /// <summary>The validated answer the pipeline used: the last call whose output passes the one validator.</summary>
    private static TriageResponse? LastValidResponse(IReadOnlyList<TriageModelResult> calls)
    {
        for (var i = calls.Count - 1; i >= 0; i--)
        {
            if (calls[i].Succeeded)
            {
                var validation = TriageValidator.Validate(calls[i].RawOutput);
                if (validation.IsValid)
                {
                    return validation.Response;
                }
            }
        }

        return null;
    }

    private static JsonElement? ModelEventPayload(Job job, EventType type)
    {
        var modelEvent = job.Events.LastOrDefault(e => e.Type == type);
        if (modelEvent is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(modelEvent.PayloadJson);
        return document.RootElement.Clone();
    }

    /// <summary>The model issue a job was made for, from its <c>ModelPrepared</c> event; null for a fallback job.</summary>
    private static int? IssueIndex(Job job)
    {
        var payload = ModelEventPayload(job, EventType.ModelPrepared);
        if (payload is not { } root
            || !root.TryGetProperty("applied", out var applied) || applied.ValueKind != JsonValueKind.Object
            || !applied.TryGetProperty("issueIndex", out var index) || index.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return index.GetInt32();
    }

    private static string? FailureReason(Job job)
    {
        var payload = ModelEventPayload(job, EventType.ModelPreparationFailed);
        return payload is { } root && root.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
            ? reason.GetString()
            : null;
    }

    private static string Describe(CaseOutcome outcome)
    {
        if (outcome.Error is not null)
        {
            return $"error ({outcome.Error})";
        }

        var issues = string.Join(", ", outcome.Issues.Select(i => $"{i.Category}/{i.Urgency}{(i.IsFollowUp ? " (follow-up)" : string.Empty)}"));
        var model = outcome.ModelPrepared ? "model ok" : $"model failed: {outcome.ModelFailureReason ?? "unknown"}";
        return $"{issues} [{model}]";
    }

    private static void TryDelete(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
