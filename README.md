# DecisionSharp

A net8.0 library for typed decisions through hosted Jev or a configured jevos endpoint, plus reranking of evidence retrieved by the caller. Inference runs in that service; the application does not need Python or a Weaviate client dependency.

| Package | Responsibility |
| --- | --- |
| DecisionSharp.Core | Immutable request, question, answer and result contracts; IDecisionEngine |
| DecisionSharp.Jev | One-attempt HTTP evaluation and strict response validation |
| DecisionSharp.Extensions.DependencyInjection | Typed HttpClient registration, optional Microsoft resilience and memory caching |
| DecisionSharp.Reranking | Bounded, stable, atomic evidence reranking |

Local packages are produced in `artifacts/packages` at version `0.1.0`. Add that directory as a NuGet source and reference the packages your application needs. These packages have not been publicly published.

## Direct client

```csharp
using DecisionSharp.Core;
using DecisionSharp.Jev;

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
{
    Timeout = Timeout.InfiniteTimeSpan
};
IDecisionEngine engine = new JevDecisionEngine(http, new JevOptions
{
    ApiKey = Environment.GetEnvironmentVariable("DECISIONSHARP_API_KEY"),
    DefaultModel = "jev-latest"
});
var result = await engine.EvaluateAsync(new { message = "The invoice failed." },
    new Dictionary<string, DecisionQuestion>
    {
        ["urgent"] = new YesNoQuestion("Does the message express urgency?"),
        ["team"] = new ChoiceQuestion("Which team should handle this?",
            new Dictionary<string, string?> { ["billing"] = "Invoices", ["technical"] = null }),
        ["severity"] = new ScoreQuestion("How severe is the problem?", new[] { "Low", "High" })
    }, cancellationToken: cancellationToken);
double probability = result.GetAnswer<YesNoAnswer>("urgent").ProbabilityOfYes;
```

The generic convenience overload is available through `IDecisionEngine`. Use `DecisionRequest` for a prebuilt JsonElement or a per-request model override. JSON and input collections are copied; callers may dispose their source documents afterward. Instructions and criteria also accept structured JSON. Choice descriptions may be null. `YesNoQuestion.WithCriteria` is a text helper for optional yes/no descriptions; portable policies belong in instructions because jevos may ignore yes/no criteria semantics.

Results preserve the provider's resolved model, usage, probability distributions and confidence. Yes/no probability has no implicit conversion to bool. Typed accessors throw for an absent ID or a wrong answer type.

## Dependency injection

```csharp
using DecisionSharp.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

services.AddDecisionSharp(options =>
{
    options.Jev.ApiKey = Environment.GetEnvironmentVariable("DECISIONSHARP_API_KEY");
    options.Jev.DefaultModel = "jev-latest";
    // Optional; each repeated POST can incur another charge.
    options.EnableRetries = true;
    // Optional; off by default.
    options.Cache.Enabled = true;
    options.Cache.Namespace = "provider/tenant/policy-v1";
});
```

Registration validates and snapshots options immediately. Register one configured provider per service container. Separate tenants/credential contexts must use separate engines/containers and cache namespaces. The default handler disables redirects and refreshes pooled connections after two minutes; the cache and its engine live for the container's lifetime when enabled. The container disposes the dedicated cache. Do not add a second retry handler. Caller-supplied handlers must preserve redirect and credential protections.

For a local endpoint:

```csharp
options.Jev.BaseUri = new Uri("http://127.0.0.1:8017/");
options.Jev.AllowInsecureLocalEndpoint = true;
options.Jev.ProviderName = "jevos";
options.Jev.ApiKey = null; // Set one if your local service requires authentication.
```

Base URIs may contain a path prefix; `https://host/service` sends to `https://host/service/v1/systemone`. Query strings, fragments and embedded credentials are rejected. Hosted configuration requires HTTPS and a bearer key. HTTP is permitted only with explicit local opt-in. The key is added per request, never to shared default headers.

Retries are disabled by default. Enabling them permits at most two retries for transport failures and HTTP 408, 429, 500, 502, 503, 504 and 529, with exponential backoff/jitter starting at 200 ms and Retry-After support. Caller cancellation, authentication, validation and protocol failures are not retried. The default 30-second total budget covers attempts, waiting and response reads. `Jev.TotalTimeout` changes that budget (supported range: 10 ms through 24 hours, matching the Microsoft pipeline). Circuit breaking is independently disabled by default; enable it and configure `CircuitFailureRatio`, `CircuitMinimumThroughput`, `CircuitSamplingDuration` and `CircuitBreakDuration` explicitly when needed. Microsoft resilience supplies the pipeline.

Caching defaults to 1024 entries with five-minute absolute expiration when enabled; configure `Cache.Capacity` and `Cache.AbsoluteExpiration`. Cache keys hash the full effective serialized request, namespace, endpoint and provider. Only successful complete results enter the cache. Concurrent misses run separately so one caller's cancellation cannot affect another. Use explicit model versions for reproducible caching: aliases can change during a TTL. Change the namespace when tenant, credentials or policy context changes. Cache keys are never emitted in telemetry.

## How to use DecisionSharp in your solution

Use DecisionSharp after your existing search retrieves candidates: retrieve → map to `Evidence` → rerank with Jev → use the returned content in your answer or search results. DecisionSharp does not retrieve documents or generate an answer. The reranker sends each candidate's query and content to Jev; IDs, metadata and original retrieval scores stay in your application.

### 1. Add the packages

Build the local packages from the DecisionSharp repository:

```bash
dotnet pack DecisionSharp.sln -c Release -o artifacts/packages
```

In your consuming .NET 8 or later project, add that folder as a NuGet source alongside nuget.org. Replace the path below with the absolute path to your checkout:

```bash
dotnet nuget add source /absolute/path/DecisionSharp/artifacts/packages --name DecisionSharpLocal
dotnet add package DecisionSharp.Jev --version 0.1.0
dotnet add package DecisionSharp.Reranking --version 0.1.0
```

`DecisionSharp.Core` is included transitively. For the dependency-injection example below, also add:

```bash
dotnet add package DecisionSharp.Extensions.DependencyInjection --version 0.1.0
```

The packages are currently local artifacts, so installing only from nuget.org is insufficient. If you use project references instead, reference the corresponding library projects in this repository.

### 2. Configure the hosted Jev provider

Create an `appsettings.json` in your application directory. The console walkthrough also reads the `Weaviate` section:

```json
{
  "DecisionSharp": {
    "BaseUri": "https://api.typesafe.ai/",
    "Model": "jev-latest",
    "ApiKey": ""
  },
  "Weaviate": {
    "Hostname": "YOUR_CLUSTER_HOSTNAME.weaviate.cloud",
    "ApiKey": "",
    "Collection": "KnowledgeChunk"
  }
}
```

For local development, put your Jev key in `DecisionSharp.ApiKey` in an ignored local settings file; commit only a blank example. Add `/appsettings.json` to your application's `.gitignore` if that file contains the key. If your application tracks non-secret `appsettings.json`, keep its key blank and supply the key through environment configuration instead. The console example uses `DECISIONSHARP_API_KEY`; ASP.NET Core uses `DecisionSharp__ApiKey`. Put the Weaviate key in the ignored file too, or override it with `WEAVIATE_API_KEY` in the console example. Use the bare Weaviate hostname without `https://`. The library itself does not load configuration files: your application maps settings into `JevOptions`.

### 3. Retrieve from Weaviate, then rerank with Jev in a console application

Create a console project with `dotnet new console -n SearchDemo --framework net8.0`, install the two DecisionSharp packages above, and add the official Weaviate client to this consuming application:

```bash
dotnet add package Weaviate.Client --version 1.2.0
```

This client requires Weaviate 1.32 or later and a reachable gRPC endpoint. The walkthrough connects to Weaviate Cloud. See the [Weaviate C# client documentation](https://docs.weaviate.io/weaviate/client-libraries/csharp) for connection requirements.

Use an existing populated `KnowledgeChunk` collection with text properties `content` and `source`, and a single Weaviate Embeddings text vectorizer configured to embed `content`. For example, store a chunk about checking payment methods and retrying failed invoices, with `source` set to `runbooks/billing.md`. `NearText` uses the collection's vectorizer to embed the query and retrieve nearby vectors. See [Weaviate vector search](https://docs.weaviate.io/weaviate/search/similarity) for query behavior and vector configuration.

Replace `Program.cs` with this code. Run `dotnet run -- "How should we handle a failed invoice?"` from the project's directory so it can read your `appsettings.json`.

```csharp
using System.Text.Json;
using DecisionSharp.Core;
using DecisionSharp.Jev;
using DecisionSharp.Reranking;
using Weaviate.Client;
using Weaviate.Client.Models;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var settings = JsonDocument.Parse(await File.ReadAllTextAsync("appsettings.json"));
var jevConfig = settings.RootElement.GetProperty("DecisionSharp");
var vectorConfig = settings.RootElement.GetProperty("Weaviate");
var query = args.Length > 0 ? string.Join(" ", args) : "How should we handle a failed invoice?";

// Step 1: Weaviate performs vector retrieval and returns up to 20 chunks.
using var weaviate = await Connect.Cloud(
    vectorConfig.GetProperty("Hostname").GetString()!,
    Environment.GetEnvironmentVariable("WEAVIATE_API_KEY")
        ?? vectorConfig.GetProperty("ApiKey").GetString()!);
var collection = weaviate.Collections.Use(vectorConfig.GetProperty("Collection").GetString()!);
var retrieved = await collection.Query.NearText(query,
    limit: 20,
    returnProperties: ["content", "source"],
    returnMetadata: MetadataOptions.Distance,
    cancellationToken: cancellation.Token);

// Step 2: Map actual Weaviate results into DecisionSharp's immutable evidence.
var candidates = retrieved.Objects.Select(hit => new Evidence(
    id: hit.UUID.ToString()!,
    content: hit.Properties["content"]?.ToString()!,
    metadata: new Dictionary<string, JsonElement>
    {
        ["source"] = JsonSerializer.SerializeToElement(hit.Properties["source"])
    },
    retrievalScore: hit.Metadata.Distance)).ToArray();

// Step 3: Jev scores each chunk's relevance to the same query.
using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
{
    Timeout = Timeout.InfiniteTimeSpan
};
IDecisionEngine engine = new JevDecisionEngine(http, new JevOptions
{
    BaseUri = new Uri(jevConfig.GetProperty("BaseUri").GetString()!),
    DefaultModel = jevConfig.GetProperty("Model").GetString()!,
    ApiKey = Environment.GetEnvironmentVariable("DECISIONSHARP_API_KEY")
        ?? jevConfig.GetProperty("ApiKey").GetString(),
    TotalTimeout = TimeSpan.FromSeconds(30)
});
var reranker = new EvidenceReranker(engine, new RerankingOptions
{
    Concurrency = 4,
    TopK = 5
});
var ranked = await reranker.RerankAsync(query, candidates, cancellation.Token);

foreach (var hit in ranked)
{
    Console.WriteLine($"{hit.Evidence.Id}: relevance={hit.RelevanceProbability:F3}, " +
        $"vectorDistance={hit.Evidence.RetrievalScore}, model={hit.ResolvedModel}");
}
// Step 4: Use the five best chunks, in Jev's order, as answer context.
var context = string.Join("\n\n", ranked.Select(hit => hit.Evidence.Content));
Console.WriteLine(context);
```

Weaviate supplies the candidate pool; Jev can change its ordering based on relevance to the query. The example preserves Weaviate's raw vector distance in `Evidence.RetrievalScore`: lower distance means closer vectors, while higher `RelevanceProbability` means greater Jev relevance. These are separate measurements, and no conversion or combination is applied. `TopK = 5` selects from the 20 retrieved chunks after scoring them all; increase the retrieval limit when relevant chunks are missing from the candidate pool. Each returned `ScoredEvidence` also carries source metadata and the provider's `ResolvedModel`.

### 4. Use the Jev reranker in an ASP.NET Core application

In a .NET 8 or later ASP.NET Core project, install the packages above and use the same JSON configuration. ASP.NET Core loads `appsettings.json` and environment overrides automatically. This complete `Program.cs` registers the provider and reranker, then exposes an endpoint accepting candidates from your existing search:

```csharp
using System.Text.Json;
using DecisionSharp.Core;
using DecisionSharp.Extensions.DependencyInjection;
using DecisionSharp.Reranking;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDecisionSharp(options =>
{
    options.Jev.BaseUri = new Uri(builder.Configuration["DecisionSharp:BaseUri"]!);
    options.Jev.DefaultModel = builder.Configuration["DecisionSharp:Model"]!;
    options.Jev.ApiKey = builder.Configuration["DecisionSharp:ApiKey"];
    options.Jev.TotalTimeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddTransient<EvidenceReranker>(services => new EvidenceReranker(
    services.GetRequiredService<IDecisionEngine>(), new RerankingOptions
    {
        Concurrency = 4,
        Threshold = 0.70, // Illustrative; calibrate using your own labeled queries.
        TopK = 5
    }));

var app = builder.Build();
app.MapPost("/search/rerank", async (
    RerankRequest request, EvidenceReranker reranker, CancellationToken cancellationToken) =>
{
    var candidates = request.Hits.Select(hit => new Evidence(
        hit.Id, hit.Content,
        new Dictionary<string, JsonElement>
        {
            ["source"] = JsonSerializer.SerializeToElement(hit.Source)
        },
        hit.RetrievalScore)).ToArray();
    var ranked = await reranker.RerankAsync(request.Query, candidates, cancellationToken);
    return Results.Ok(ranked.Select(hit => new
    {
        id = hit.Evidence.Id,
        content = hit.Evidence.Content,
        source = hit.Evidence.Metadata!["source"].GetString(),
        retrievalScore = hit.Evidence.RetrievalScore,
        relevanceProbability = hit.RelevanceProbability,
        resolvedModel = hit.ResolvedModel
    }));
});
app.Run();

public sealed record SearchHit(string Id, string Content, string Source, double? RetrievalScore);
public sealed record RerankRequest(string Query, SearchHit[] Hits);
```

For example, POST this JSON to `/search/rerank` with `Content-Type: application/json`:

```json
{
  "query": "How should we handle a failed invoice?",
  "hits": [
    {
      "id": "weather",
      "content": "Tomorrow will be sunny with light winds.",
      "source": "weather/tomorrow.md",
      "retrievalScore": 0.92
    },
    {
      "id": "billing-runbook",
      "content": "Check the payment method, retry collection, and escalate repeated invoice failures to billing.",
      "source": "runbooks/billing.md",
      "retrievalScore": 0.81
    }
  ]
}
```

The response is an array in descending relevance order; candidates below `0.70` are omitted. In an existing search endpoint, use the same `Select` mapping on the rows returned by your search client, call the injected reranker, and return its results or build answer context from `hit.Evidence.Content`. Map a document ID, text chunk, optional metadata and optional retrieval score; use a unique ID for each chunk. The Weaviate client is a dependency of the consuming console application; DecisionSharp itself has no vector-store dependency.

### 5. Choose limits and handle failures

| Setting | Effect |
| --- | --- |
| `Concurrency = 4` | At most four candidate evaluations at once **per rerank call**; concurrent requests can make more calls in aggregate. |
| `TopK = 5` | Return at most five results after scoring and threshold filtering; every candidate still needs evaluation. |
| `Threshold = 0.70` | Include relevance probabilities greater than or equal to 0.70; choose the cutoff using labeled data. |
| `Jev.TotalTimeout` | Budget for each candidate evaluation, including retries and response reads; it is not a deadline for the whole batch. |

Omit `Threshold` to keep low-scoring results, and omit `TopK` to return all qualifying candidates. Equal relevance scores retain input order. Original retrieval scores and snapshotted metadata remain unchanged. Empty input makes no calls; malformed inputs fail before evaluation. For a whole-search deadline, pass a cancellation token with your application's deadline to `RerankAsync`; the ASP.NET Core example passes the request cancellation token.

A candidate failure cancels sibling work and fails the whole rerank; it does not return partial results or fabricate a relevance score. Handle the exceptions described below at your application's boundary. Retries and caching are off by default; configure them through `AddDecisionSharp` as described above when needed. Each candidate normally causes one Jev request, and retries can add billable requests. Relevance does not establish truth, freshness, access permission or answerability; check those before using retrieved content.

## Failures and telemetry

- `DecisionServiceException`: HTTP status and allowlisted metadata; no raw response body.
- `DecisionProtocolException`: malformed/missing/duplicate fields, invalid answer types/distributions or a response beyond `Jev.ResponseByteLimit` (default 1 MiB).
- `DecisionTimeoutException`: the evaluation budget expired, distinct from caller cancellation.
- `HttpRequestException`: transport failure; `OperationCanceledException`: caller cancellation.
- Circuit-open errors from the opt-in Microsoft pipeline remain Polly circuit exceptions.

ActivitySource and Meter are named `DecisionSharp`. Instruments are `decision.evaluations`, `decision.failures`, `decision.duration` (seconds) and `decision.cache.hits`. Tags are bounded provider (`jev`/`jevos`), outcome and question type; resolved model is trace-only. No state, instructions, keys, raw bodies, document IDs or arbitrary question IDs are recorded. Applications may attach OpenTelemetry exporters; the library requires no OpenTelemetry SDK.

## Console example and verification

Open `samples/DecisionSharp.Console/appsettings.json` and fill in `DecisionSharp.ApiKey`. The hosted base URI and model are already supplied. The local file is ignored by Git, copied to the console output directory, and excluded from source archives. From a fresh checkout/source archive, copy `appsettings.example.json` to `appsettings.json` first.

```json
{
  "DecisionSharp": {
    "BaseUri": "https://api.typesafe.ai/",
    "Model": "jev-latest",
    "ApiKey": "YOUR_KEY_HERE",
    "AllowInsecureLocalEndpoint": false
  }
}
```

Run `dotnet run --project samples/DecisionSharp.Console -- --smoke` after saving it. Keep the real key only in the ignored local file; build outputs containing copied settings are also ignored and excluded from archives.

Environment variables override JSON settings. `DECISIONSHARP_BASE_URI`, `DECISIONSHARP_MODEL`, `DECISIONSHARP_API_KEY` and `DECISIONSHARP_LOCAL` map to the four JSON properties. An explicitly empty key override clears the configured key. For a local service, set `AllowInsecureLocalEndpoint` to true (or `DECISIONSHARP_LOCAL=true`) and use its HTTP base URI. Hosted endpoints require an API key. Keep keys out of command histories.

```bash
export DECISIONSHARP_BASE_URI=http://127.0.0.1:8017/
export DECISIONSHARP_MODEL=jev-latest
export DECISIONSHARP_LOCAL=true
dotnet run --project samples/DecisionSharp.Console --no-launch-profile -- --smoke
dotnet run --project samples/DecisionSharp.Console --no-launch-profile
```

`--smoke` exercises yes/no, choice and score. The default run maps `samples/DecisionSharp.Console/evidence.json` as already-retrieved rows, then reranks them. Pass a JSON file path as the first argument to use other retrieved rows; the sample query is “How are failed invoices handled?”. Ctrl+C cancels work. Console output contains result scores/model and caller-provided evidence IDs, not request bodies or credentials.

```bash
dotnet restore DecisionSharp.sln
dotnet build DecisionSharp.sln -c Release --no-restore
dotnet test DecisionSharp.sln -c Release --no-build
dotnet pack DecisionSharp.sln -c Release --no-build -o artifacts/packages
```

Tests use independent synthetic HTTP fixtures and a loopback fixture server for the console. They verify contracts and integration behavior, not model accuracy or live Jev/jevos compatibility. Actual checks and remaining acceptance gaps are recorded in `artifacts/verification.md`. No CI configuration was supplied; SDK checks run locally. Do not monitor TeamCity after a handoff unless Steve requests it.

The authoritative project requirements are in [the spec](docs/DecisionSharp-design.md). Wire shapes follow the [TypeSafe API reference](https://docs.typesafe.ai/api) and [quick start](https://docs.typesafe.ai/introduction/quickstart); the self-hosted service is described in the [jevos repository](https://github.com/feder-cr/jev). Provider interchangeability concerns wire contracts, not equivalent accuracy, calibration, capacity or speed.
