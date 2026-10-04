using System.Globalization;
using System.Text.Json;
using DecisionSharp.Core;
using DecisionSharp.Core.Answers;
using DecisionSharp.Core.Questions;
using DecisionSharp.Extensions.DependencyInjection;
using DecisionSharp.Jev;
using DecisionSharp.Reranking;
using Microsoft.Extensions.DependencyInjection;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    using var settings = JsonDocument.Parse(File.Exists(settingsPath) ? await File.ReadAllTextAsync(settingsPath, cancellation.Token) : "{}");
    var section = settings.RootElement.TryGetProperty("DecisionSharp", out var configured) ? configured : default;
    string? Setting(string environmentVariable, string property) => Environment.GetEnvironmentVariable(environmentVariable)
        ?? (section.ValueKind == JsonValueKind.Object && section.TryGetProperty(property, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean().ToString() : null
            : null);
    var baseUri = Setting("DECISIONSHARP_BASE_URI", "BaseUri");
    var model = Setting("DECISIONSHARP_MODEL", "Model");
    if (string.IsNullOrWhiteSpace(baseUri) || string.IsNullOrWhiteSpace(model))
    {
        Console.Error.WriteLine("Set DecisionSharp.BaseUri and DecisionSharp.Model in appsettings.json, or DECISIONSHARP_BASE_URI and DECISIONSHARP_MODEL.");
        return 1;
    }
    var services = new ServiceCollection();
    services.AddDecisionSharp(options =>
    {
        options.Jev.BaseUri = new Uri(baseUri, UriKind.Absolute);
        options.Jev.DefaultModel = model;
        var apiKey = Setting("DECISIONSHARP_API_KEY", "ApiKey");
        options.Jev.ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        options.Jev.AllowInsecureLocalEndpoint = string.Equals(Setting("DECISIONSHARP_LOCAL", "AllowInsecureLocalEndpoint"), "true", StringComparison.OrdinalIgnoreCase);
        options.Jev.ProviderName = options.Jev.AllowInsecureLocalEndpoint ? "jevos" : "jev";
    });
    using var provider = services.BuildServiceProvider();
    var engine = provider.GetRequiredService<IDecisionEngine>();
    if (args.Contains("--smoke", StringComparer.Ordinal))
    {
        var result = await engine.EvaluateAsync(new { message = "An invoice failed." }, new Dictionary<string, DecisionQuestion>
        {
            ["urgent"] = YesNoQuestion.WithCriteria("Is it urgent?", "Time sensitive", "Routine"),
            ["route"] = new ChoiceQuestion("Which team?", new Dictionary<string, string?> { ["billing"] = "Invoices", ["tech"] = null }),
            ["severity"] = new ScoreQuestion("How severe?", new[] { "Low", "High" })
        }, cancellationToken: cancellation.Token);
        Console.WriteLine(FormattableString.Invariant($"model={result.Model} yes={result.GetAnswer<YesNoAnswer>("urgent").ProbabilityOfYes:F3} choice={result.GetAnswer<ChoiceAnswer>("route").Choice} score={result.GetAnswer<ScoreAnswer>("severity").Score:F3} input_tokens={result.Usage.InputTokens} output_tokens={result.Usage.OutputTokens}"));
    }
    else
    {
        var evidencePath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "evidence.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(evidencePath, cancellation.Token));
        // Map already-retrieved Weaviate rows here; retrieval and its scores stay with the caller.
        var evidence = document.RootElement.EnumerateArray().Select(row => new Evidence(
            row.GetProperty("id").GetString()!, row.GetProperty("content").GetString()!,
            row.TryGetProperty("metadata", out var metadata) ? metadata.EnumerateObject().ToDictionary(p => p.Name, p => p.Value) : null,
            row.TryGetProperty("retrievalScore", out var retrievalScore) ? retrievalScore.GetDouble() : null)).ToArray();
        var scored = await new EvidenceReranker(engine).RerankAsync("How are failed invoices handled?", evidence, cancellation.Token);
        foreach (var item in scored)
        {
            Console.WriteLine(FormattableString.Invariant($"{item.Evidence.Id}\trelevance={item.RelevanceProbability:F3}\tretrieval={item.Evidence.RetrievalScore}\tmodel={item.ResolvedModel}"));
        }
    }
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Canceled."); return 2; }
catch (DecisionTimeoutException) { Console.Error.WriteLine("Decision evaluation timed out."); return 1; }
catch (DecisionServiceException ex) { Console.Error.WriteLine($"Decision service returned HTTP {(int)ex.StatusCode}."); return 1; }
catch (DecisionProtocolException) { Console.Error.WriteLine("Decision service returned an invalid response."); return 1; }
catch (Exception) { Console.Error.WriteLine("Check endpoint configuration and the evidence file. Evaluation failed."); return 1; }
