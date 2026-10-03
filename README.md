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

## Reranking retrieved evidence

```csharp
using DecisionSharp.Reranking;

// Map the results your existing Weaviate retrieval already returned.
var evidence = retrievedRows.Select(row =>
    new Evidence(row.Id, row.Content, row.Metadata, row.RetrievalScore)).ToArray();
var reranker = new EvidenceReranker(engine, new RerankingOptions
{
    Concurrency = 4,
    Threshold = 0.75, // Example only: calibrate on your labeled retrieval data.
    TopK = 10
});
var ranked = await reranker.RerankAsync(query, evidence, cancellationToken);
```

Each candidate gets its own query/content state and relevance question. Results sort by relevance probability, then original position for ties. Threshold filtering is inclusive and occurs before top-K. With no threshold/top-K, all candidates are returned. Original retrieval scores and snapshotted source metadata remain separate and unchanged. Empty input makes no calls; malformed inputs fail before any evaluation.

A candidate failure cancels queued/in-flight siblings, waits for started workers and fails the whole rerank with the originating error. Caller cancellation propagates as cancellation. No failed item is silently removed or assigned a fabricated score. Relevance is not proof of truth, freshness, authorization or answerability; evaluate model quality and choose thresholds using the company's own labeled examples.

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
