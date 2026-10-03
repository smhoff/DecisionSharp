# DecisionSharp Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` for inline execution. Use `superpowers:subagent-driven-development` only if Steve explicitly selects delegation. Steps use checkboxes for tracking.

**Goal:** Deliver the specified typed .NET decision client and evidence reranker, with verified local packages and a configurable console example.

**Architecture:** Core owns immutable contracts; Jev implements HTTP evaluation; DI supplies optional resilience and caching; Reranking consumes only the engine contract. Use System.Text.Json, HttpClient, Microsoft extensions, and one test project. No extra provider abstraction, retrieval client, or inference runtime.

**Tech Stack:** C#, net8.0 libraries, nullable reference types, System.Text.Json; Microsoft.Extensions.Http.Resilience and MemoryCache in DI; xUnit for the requested automated coverage. Pin package versions after verifying restore compatibility.

**Spec:** [DecisionSharp-design.md](../../DecisionSharp-design.md).

## Global Constraints

- Target net8.0 as the compatibility baseline, with CI validation on supported .NET SDKs including .NET 10.
- Core: BCL only. Jev: Core. DI: Core, Jev, Microsoft extensions. Reranking: Core.
- Choice contains 2–255 named options. Score contains 2–10 ordered levels.
- Probabilities and confidence must be finite and within [0,1]; distributions must sum to 1 within 0.001.
- Enforce a configurable response-byte limit before parsing, default 1 MiB.
- Core transport performs one attempt. Default total timeout: 30 seconds; optional retry preset: at most two retries within that budget.
- Optional memory caching is off by default, bounded by entry count (default 1024 when enabled) with absolute expiration (default five minutes).
- Default concurrency is four, configurable. Default reranking failure behavior is atomic.
- No public GitHub repository creation, NuGet publication, native inference, Python application dependency, Weaviate replacement, distributed cache, fallback, or cross-request coalescing.
- Run local checks and report the handoff. Do not monitor TeamCity or start a build-monitoring agent without Steve's explicit request.
- Ponytail full: satisfy every explicit requirement; skip speculative abstractions and dependencies.

## Review Focus

1. A base URI with a path prefix must preserve that prefix; redirects must not send credentials to another origin (Task 2).
2. Nested duplicate properties, including duplicates in unknown response fields, must fail rather than silently overwrite data (Task 3).
3. Mutation or disposal of JSON and collections must not change requests, results, metadata, or cache identity (Tasks 1, 5, 6).
4. A failure racing with sibling cancellation must retain the originating failure; caller cancellation must remain cancellation (Task 6).
5. Header-only responses with stalled or oversized bodies must remain subject to the timeout and byte budget (Tasks 3, 4).

## Starting State and File Map

The folder is not a Git checkout. SDKs installed: 9.0.115, 10.0.203, 10.0.302; runtime 8.0.22 is installed, so net8.0 tests can execute locally. Existing `DecisionSharp/DecisionSharp.csproj` targets net10.0; `Class1.cs` contains invalid placeholder code. No existing implementation or tests can be reused. Do not initialize Git as part of this plan; commit steps apply only if Steve supplies a repository.

Retain `DecisionSharp.sln`; replace the placeholder project with sibling package directories:

| Files | Responsibility |
| --- | --- |
| `Directory.Build.props`, `global.json`, `.gitignore` | Shared library settings, installed SDK selection, build-output exclusions |
| `DecisionSharp.Core/{DecisionSharp.Core.csproj,IDecisionEngine.cs,DecisionRequest.cs,DecisionQuestion.cs,DecisionAnswer.cs,DecisionResult.cs}` | Public contracts, snapshotting and validation |
| `DecisionSharp.Jev/{DecisionSharp.Jev.csproj,JevOptions.cs,JevDecisionEngine.cs,JevWire.cs,DecisionExceptions.cs,DecisionTelemetry.cs,AssemblyInfo.cs}` | Endpoint rules, HTTP, internal wire codec, errors, instruments; friend access for DI and tests to reuse effective request serialization |
| `DecisionSharp.Extensions.DependencyInjection/{DecisionSharp.Extensions.DependencyInjection.csproj,DecisionSharpServiceCollectionExtensions.cs,DecisionSharpOptions.cs,CachedDecisionEngine.cs,ResilienceTimeoutHandler.cs}` | Registration, Microsoft pipeline, bounded private cache, timeout normalization |
| `DecisionSharp.Reranking/{DecisionSharp.Reranking.csproj,Evidence.cs,RerankingOptions.cs,EvidenceReranker.cs,ScoredEvidence.cs}` | Evidence snapshots, option validation, bounded scoring and sorting |
| `tests/DecisionSharp.Tests/{DecisionSharp.Tests.csproj,CoreTests.cs,JevRequestTests.cs,JevResponseTests.cs,ResilienceTests.cs,CacheTests.cs,TelemetryTests.cs,RerankingTests.cs,StubHttpHandler.cs,Fixtures/*}` | One test runner and independent wire fixtures |
| `samples/DecisionSharp.Console/{DecisionSharp.Console.csproj,Program.cs,evidence.json}` | Configuration and adapter for caller-retrieved evidence |
| `README.md`, `artifacts/verification.md`, `artifacts/packages/*`, `artifacts/DecisionSharp-source.zip` | Usage, actual validation results, deliverables |

Use combined files for related immutable variants; split only if implementation becomes hard to read. Do not introduce a second serializer just for cache keys.

## Task 1: Immutable Core Contract

**Files:** Core files, shared build files, solution, `CoreTests.cs`, test project. Remove `DecisionSharp/Class1.cs` and replace the placeholder project entry.

**Interfaces:**

- `IDecisionEngine.EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default) : Task<DecisionResult>`.
- Default interface convenience overload: `EvaluateAsync<TState>(TState state, IReadOnlyDictionary<string, DecisionQuestion> questions, string? model = null, CancellationToken cancellationToken = default) : Task<DecisionResult>`, using `JsonSerializer.SerializeToElement`.
- `DecisionRequest(JsonElement state, IReadOnlyDictionary<string, DecisionQuestion> questions, string? model = null)`; read-only `State`, `Questions`, `Model`.
- `DecisionQuestion` variants: `YesNoQuestion(JsonElement instructions, JsonElement? criteria = null)`, `ChoiceQuestion(JsonElement instructions, IReadOnlyDictionary<string, JsonElement?> criteria)`, `ScoreQuestion(JsonElement instructions, IReadOnlyList<JsonElement> criteria)`; text overloads for each and text criteria helpers.
- `DecisionAnswer` variants: `YesNoAnswer(double probabilityOfYes)`, `ChoiceAnswer(string choice, IReadOnlyDictionary<string,double> probabilities, double confidence)`, `ScoreAnswer(double score, IReadOnlyDictionary<string,string> legend, IReadOnlyDictionary<string,double> probabilities, double confidence)`.
- `DecisionResult(string model, IReadOnlyDictionary<string,DecisionAnswer> answers, TokenUsage usage)`; `TokenUsage(long inputTokens, long outputTokens)`; `GetAnswer<TAnswer>(string id)` with clear missing-ID and wrong-type exceptions.

- [x] Write tests for owned JSON and read-only collection snapshots; state/instructions string, object and array; optional yes/no criteria with only `true`/`false` keys; structured criteria; nullable choice descriptions; bounds 2/255 and 2/10; blank keys/text/model and unsupported JSON kinds. Reject duplicate properties in structured input. Assert explicit probability access and no bool conversion.
- [x] Create only the projects needed for these tests, select installed SDK 10.0.302, target libraries/tests/sample at net8.0, and pin restored test dependencies. Run `dotnet test tests/DecisionSharp.Tests --filter FullyQualifiedName~CoreTests`; confirm failures concern the missing behavior.
- [x] Implement contracts with defensive copies and `JsonElement.Clone()`. Use standard read-only collections; positional records alone do not make dictionaries immutable. Validate malformed inputs before an engine call.
- [x] Run the Core tests; verify disposed source documents and later collection mutations cannot affect any exposed property.
- [x] If Git is available, commit this tested unit as `feat: add immutable decision contracts`.

## Task 2: One-Attempt HTTP Requests

**Files:** Jev project, options, engine, internal wire codec, exception types, assembly friend access; request tests, stub handler and request fixtures.

**Interfaces:**

- `JevDecisionEngine(HttpClient httpClient, JevOptions options) : IDecisionEngine`.
- `JevOptions`: required absolute `BaseUri`, `DefaultModel`, optional `ApiKey`; `AllowInsecureLocalEndpoint=false`, `ResponseByteLimit=1048576`, `TotalTimeout=TimeSpan.FromSeconds(30)`, bounded `ProviderName="jev"`.
- Internal `JevWire.SerializeRequest(DecisionRequest request, string defaultModel) : byte[]`; reuse these exact bytes in Task 5.
- `DecisionProtocolException`, `DecisionServiceException` with HTTP status and allowlisted sanitized metadata, `DecisionTimeoutException : TimeoutException`. Transport failures retain `HttpRequestException`; caller cancellation retains `OperationCanceledException`.

- [x] Hand-author fixtures based on the [official API](https://docs.typesafe.ai/api) and [quick start](https://docs.typesafe.ai/introduction/quickstart); include all three primitives and structured values. Keep fixture provenance in `Fixtures/README.md`; never generate expected JSON with the production codec.
- [x] Write assertions for request body field names, resolved default/override model, per-request bearer header, unchanged shared headers, path-prefix preservation, trailing-slash normalization, rejected queries/fragments and HTTP opt-in. Use semantic JSON comparisons; preserve exact casing of named IDs/options.
- [x] Run `dotnet test tests/DecisionSharp.Tests --filter FullyQualifiedName~JevRequestTests`; confirm the new assertions fail.
- [x] Implement `JevWire.SerializeRequest` and `EvaluateAsync`. Send relative `v1/systemone` against a normalized prefix, use `ResponseHeadersRead`, dispose request/response, never retry. Validate and snapshot timeout/limit/model/key options. Hosted requires HTTPS and bearer key; local opt-in permits HTTP and optional key. Default DI handler disables redirects; document equivalent requirements for caller-supplied HttpClient handlers.
- [x] Run request tests; assert invalid requests make zero HTTP calls and one valid evaluation makes exactly one call.
- [x] If Git is available, commit as `feat: send validated Jev evaluation requests`.

## Task 3: Strict Responses, Budgets and Telemetry

**Files:** `JevWire.cs`, `JevDecisionEngine.cs`, `DecisionExceptions.cs`, `DecisionTelemetry.cs`, response/telemetry tests and response fixtures.

**Interfaces:** Internal `JevWire.ParseResponse(ReadOnlyMemory<byte> body, DecisionRequest request) : DecisionResult`. ActivitySource and Meter name `DecisionSharp`; counters `decision.evaluations`, `decision.failures`, `decision.cache.hits`; histogram `decision.duration` in seconds.

- [x] Write tests for complete primitive responses, resolved model/usage, provider confidence preservation and unknown additive fields. Assert rejection of missing/extra answers, wrong types, missing fields, duplicate properties at every depth, invalid token counts, malformed JSON, nonfinite/out-of-range numbers, wrong option keys, incomplete distributions/legends, sums outside 0.001 and scores outside `[0, levelCount-1]`. Check inclusive numeric boundaries and confidence independent of peak probability.
- [x] Add stream tests: byte limit exactly 1048576 vs 1048577, absent or misleading Content-Length, cancellation during reads, stalled body timeout, sanitized error responses, HTTP authentication/validation failures. Read no more than the limit plus one byte before rejecting. Add listener assertions excluding all sensitive fields and unbounded tags.
- [x] Run `dotnet test tests/DecisionSharp.Tests --filter 'FullyQualifiedName~JevResponseTests|FullyQualifiedName~TelemetryTests'`; verify failures.
- [x] Parse a bounded buffer with JsonDocument and recursively reject duplicate property names before accessing values. Compare answers/keys to the request with ordinal semantics. Preserve string legends for structured score criteria without demanding equality to the original JSON. Require complete usage with nonnegative integral counts and nonblank resolved model.
- [x] Use one linked total deadline covering HTTP, later DI retries and body reads. Translate deadline expiration into `DecisionTimeoutException`; caller cancellation takes precedence when its token is canceled. Never include server bodies or unsanitized headers in exceptions. Emit bounded provider/outcome/question-type tags; resolved model is trace-only.
- [x] Run the response and telemetry tests, then all current tests.
- [x] If Git is available, commit as `feat: enforce response contracts and evaluation budgets`.

## Task 4: DI and Explicit Microsoft Resilience

**Files:** DI project, registration/options and timeout handler; `ResilienceTests.cs`, expanded request tests.

**Interfaces:** `AddDecisionSharp(this IServiceCollection services, Action<DecisionSharpOptions> configure) : IHttpClientBuilder`. `DecisionSharpOptions`: `JevOptions Jev`, `bool EnableRetries=false`, `bool EnableCircuitBreaker=false`, circuit parameters `FailureRatio=0.5`, `MinimumThroughput=10`, `SamplingDuration=30 seconds`, `BreakDuration=30 seconds`, and `DecisionCacheOptions Cache`. These circuit defaults are implementation choices, not spec requirements, and apply only when enabled. Register `IDecisionEngine` from the typed Jev client, optionally decorated in Task 5.

- [x] Write tests for validated startup options, direct/DI client parity, redirect prevention, retries off by default, exactly three maximum attempts when enabled, and transport/408/429/500/502/503/504/529 eligibility. Assert zero retries for 400/401/403/422, caller cancellation or protocol failures. Include Retry-After delta/date and a delay longer than the remaining budget.
- [x] Write a deadline test spanning retry delay plus final body read; it must complete within the configured total budget with the timeout exception, never a fabricated score. Test independently enabled circuit breaking and rejection of invalid settings.
- [x] Run `dotnet test tests/DecisionSharp.Tests --filter FullyQualifiedName~ResilienceTests`; confirm failures.
- [x] Pin compatible Microsoft packages after restore. Configure `Microsoft.Extensions.Http.Resilience`; disable its default POST retries and circuit behavior unless explicitly enabled. Use the exact retry allowlist, at most two retries, honor Retry-After, and cap all work at the engine's total deadline. No additional Polly retry loop or transport retries.
- [x] Normalize Microsoft pipeline timeout rejection through `ResilienceTimeoutHandler` to a BCL `TimeoutException`, which Jev maps to `DecisionTimeoutException`; keep Jev free of Microsoft/Polly dependencies. Set DI HttpClient timeout infinite so the configured evaluation budget controls cancellation. Use the primary handler's native redirect setting.
- [x] Run resilience and request tests, then all current tests.
- [x] If Git is available, commit as `feat: add validated DI and opt-in resilience`.

## Task 5: Bounded Optional Cache

**Files:** `DecisionSharpOptions.cs`, `CachedDecisionEngine.cs`, telemetry updates, `CacheTests.cs`.

**Interfaces:** `DecisionCacheOptions`: `Enabled=false`, `Namespace` required when enabled, `Capacity=1024`, `AbsoluteExpiration=TimeSpan.FromMinutes(5)`. Internal `CachedDecisionEngine(IDecisionEngine inner, JevOptions provider, DecisionCacheOptions cacheOptions)` implements the engine and owns a dedicated disposable MemoryCache.

- [x] Write tests for default off, hit/miss, count capacity, absolute expiration, namespace/provider/endpoint/default-model/override/state/question/criteria isolation, failed evaluations not cached, caller cancellation on a hit, immutable returned values, and concurrent identical requests executing independently. Use short configurable expiration with bounded waiting; do not add a clock framework solely for this test.
- [x] Run `dotnet test tests/DecisionSharp.Tests --filter FullyQualifiedName~CacheTests`; confirm failures.
- [x] Hash length-delimited namespace, normalized endpoint identity, provider identity and Task 2's effective wire bytes with SHA256. Require namespaces to differ for separate tenant/credential contexts; never hash or emit raw API keys. Stable request serialization orders named question/option keys ordinally; score level order remains significant. Give every entry size one and configure the private cache size limit.
- [x] Cache only complete validated results; check caller cancellation before returning a hit. No shared-task dictionary or request coalescing. Emit cache hits without key, document IDs, state or instructions.
- [x] Run cache/telemetry tests, then all current tests.
- [x] If Git is available, commit as `feat: add bounded isolated decision cache`.

## Task 6: Evidence Reranking

**Files:** Reranking project and its three implementation files; `RerankingTests.cs`.

**Interfaces:**

- `Evidence(string id, string content, IReadOnlyDictionary<string,JsonElement>? metadata = null, double? retrievalScore = null)` with snapshots.
- `ScoredEvidence(Evidence evidence, double relevanceProbability, string resolvedModel)`.
- `RerankingOptions`: `Concurrency=4`, optional `double? Threshold`, optional `int? TopK`.
- `EvidenceReranker(IDecisionEngine engine, RerankingOptions? options = null)`; `RerankAsync(string query, IReadOnlyList<Evidence> evidence, CancellationToken cancellationToken = default) : Task<IReadOnlyList<ScoredEvidence>>`.

- [x] Write tests asserting maximum active calls four/configured value, separate candidate state, unchanged retrieval score and snapshotted metadata, descending relevance, input-position tie breaks, inclusive threshold before top-K, empty input/no calls, and zero calls on duplicate IDs/blank query/content/invalid options. Reject nonfinite thresholds and nonpositive top-K/concurrency.
- [x] Add cancellation/failure races: first service failure stops queued work, cancels siblings and remains the thrown failure; caller cancellation wins when explicitly requested. A failed rerank returns no partial list. Test mutation of the caller's candidate list while calls are blocked.
- [x] Run `dotnet test tests/DecisionSharp.Tests --filter FullyQualifiedName~RerankingTests`; confirm failures.
- [x] Snapshot and validate all inputs before launching work. Use BCL `Parallel.ForEachAsync` with bounded degree and a linked cancellation source. Store results by original index; capture the first originating failure before canceling peers and observe every started worker. Use a fixed yes/no question ID `relevance` and instructions asking whether content supplies relevant evidence; state contains only query and that candidate's content. Read `YesNoAnswer.ProbabilityOfYes` and retain result model.
- [x] Sort descending with index as tie-breaker; apply threshold then top-K. No weighting of retrieval score, automatic threshold or candidate concatenation.
- [x] Run reranking tests, then all current tests.
- [x] If Git is available, commit as `feat: rerank evidence with bounded atomic evaluation`.

## Task 7: Runnable Sample, Documentation and Deliverables

**Files:** Console sample, local ignored `appsettings.json`, tracked `appsettings.example.json`, `README.md`, solution, library packaging properties and verification report.

**Interfaces:** Per Steve's execution preference, read the `DecisionSharp` section of local `appsettings.json` with System.Text.Json; environment variables override it. Environment variables `DECISIONSHARP_BASE_URI`, `DECISIONSHARP_MODEL`, `DECISIONSHARP_API_KEY`, `DECISIONSHARP_LOCAL`; `--smoke` evaluates all three question types; default sample adapts `evidence.json` records representing already-retrieved Weaviate results and reranks them.

- [x] Add a sample invocation check against the stub HTTP endpoint with known independent fixtures: verify all three smoke answers and stable reranked IDs. Missing configuration must exit nonzero with variable names only; never print keys or input bodies. This verifies the adapter without replacing caller retrieval.
- [x] Run that check and confirm it fails before implementing the console entry point.
- [x] Implement configuration, DI usage, graceful Ctrl+C cancellation and concise result output; create an ordinary metadata-bearing evidence fixture. Document direct HttpClient usage, DI, local opt-in, model aliases, cache namespaces/expiration, retry costs, threshold calibration on company labels, and relevance's limits. Include the [jevos reference](https://github.com/feder-cr/jev); wire handling does not establish equivalent model accuracy.
- [x] Make all four libraries packable with package IDs matching assembly names, explicit local version and project references. Do not choose a distribution license without Steve's instruction. Test/sample projects are not packable.
- [x] Run `dotnet restore DecisionSharp.sln`, `dotnet build DecisionSharp.sln -c Release --no-restore`, `dotnet test DecisionSharp.sln -c Release --no-build`, and `dotnet pack DecisionSharp.sln -c Release --no-build -o artifacts/packages`. Expect successful exits, all tests passing and exactly four library packages.
- [x] Validate net8.0 consumers restore/build from the local packages. Check on available SDKs 9.0.115 and 10.0.302 using temporary SDK selectors; record actual SDK/runtime versions. Add required SDK CI validation only if an existing CI configuration is supplied; do not invent TeamCity infrastructure. Record CI acceptance as pending if no configuration is available.
- [x] Run the live sample only against an explicitly supplied endpoint; exercise all three primitives and reranking, record the endpoint class/model and outcomes without secrets. If no endpoint is configured, mark live acceptance pending and request configuration at handoff. Do not claim live Jev/jevos compatibility from mocks.
- [x] Produce `artifacts/DecisionSharp-source.zip` excluding bin/obj, package archives, caches, secrets and the archive itself. Inspect archive entries and package contents; record commands, outcomes and any remaining acceptance gaps in `artifacts/verification.md`.
- [x] If Git is available, commit as `docs: add verified sample and local package deliverables`. Push only if authorized; report the handoff and stop without TeamCity monitoring.

## Plan Review and Execution Handoff

Spec coverage maps to Tasks 1–7: public contract (1), wire/HTTP (2–3), resilience/DI (4), cache (5), telemetry (3 and 5), reranking (6), sample/packages/acceptance (7). All five Review Focus cases have owning test steps. Deferred features remain excluded.

Recommended execution is inline: tasks share contracts and serialization, and this avoids repeated agent contexts. Steve must review this saved plan before implementation under the writing-plans workflow. No implementation, dependencies, Git initialization, service calls or publication have been performed during planning.


## Execution Record

Implemented inline in the approved folder. Git was initialized during execution; .gitignore was included in the first local commit. Steve selected appsettings.json for console configuration; the local file is ignored and excluded from archives, with a blank example tracked. Independent review identified acceptance-test gaps and a timeout-range mismatch; targeted regressions and fixes were added, including a newly exposed late-response deadline bug. Local SDK, package-consumer, build/test/pack and source-archive results are in artifacts/verification.md. Live hosted Jev smoke/reranking passed on jev-1.13.0; jevos was not exercised. CI acceptance is pending; SDK checks ran locally.
