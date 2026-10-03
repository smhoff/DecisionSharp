# DecisionSharp — design specification

Date: 2026-10-03
Status: approved specification; implementation verified locally and against hosted Jev on 2026-10-03. CI validation remains pending.

## Purpose and success

Build a reusable C# library for evaluating typed decisions through hosted Jev or self-hosted jevos, and reranking evidence already retrieved from Weaviate. The intended consumer is an enterprise .NET platform that indexes source code, Confluence, and company data. Success means interchangeable HTTP endpoints, explicit failure semantics, traceable results, and a runnable retrieval-to-reranking example with automated contract coverage.

This is an integration library, not a newly trained decision model. Inference remains in the configured service. No Python dependency is introduced into the consuming application. The initial adapter accepts retrieved evidence from any source; existing Weaviate retrieval stays with the caller.

## Alternatives and choice

1. Modular .NET client and reranker (selected): strongest maintainability and contract isolation; requires an inference endpoint.
2. One combined package: simpler installation but couples retrieval policy, transport, and configuration; less suitable for the shared platform.
3. Native inference inside .NET: could remove HTTP overhead, but introduces model-runtime, tokenization, packaging, and compatibility work without evidence of better correctness. Defer until a measured deployment need warrants it.

Correctness takes precedence over maintainability, then performance. Provider interchangeability means wire compatibility, not equivalent accuracy, calibration, context capacity, or latency.

## Package boundaries

Target net8.0 as the compatibility baseline, with CI validation on supported .NET SDKs including .NET 10. Use nullable reference types and System.Text.Json. Versions will be pinned during implementation after restore and SDK availability are verified.

| Package | Responsibility | Dependencies |
| --- | --- | --- |
| DecisionSharp.Core | Engine interface; immutable typed questions, answers and evaluation results; input validation | BCL only |
| DecisionSharp.Jev | HTTP wire serialization, authentication, strict response validation, transport errors | Core |
| DecisionSharp.Extensions.DependencyInjection | Typed client registration, validated options, optional memory-cache decorator and Microsoft HTTP resilience integration | Core, Jev, Microsoft extensions |
| DecisionSharp.Reranking | Evidence scoring, bounded concurrency, stable ordering, threshold filtering | Core |

Tests and a console sample are separate projects. Each library is packable; the sample reads configuration from environment variables. Produce a source archive and local NuGet packages after verification. Public GitHub repository creation and NuGet publication are outside this deliverable.

## Public contract

IDecisionEngine exposes EvaluateAsync(DecisionRequest, CancellationToken), returning DecisionResult, plus a generic-state convenience method that serializes state into an owned JsonElement. DecisionRequest holds state, named typed questions, and an optional model override. The provider default model applies when no override is supplied.

Question variants are YesNoQuestion (wire type noul), ChoiceQuestion, and ScoreQuestion. Instructions accept text or structured JSON. Criteria support the official structured forms, with helper factories for ordinary text. Choice contains 2–255 named options. Score contains 2–10 ordered levels. These bounds define the library contract, even when an endpoint accepts a wider shape. Yes/no criteria are optional; jevos may ignore their meaning, so portable policies must be expressed in instructions.

Answer variants preserve probability of yes, choice with its distribution and confidence, or score with legend, distribution and confidence. No implicit conversion of probability to bool. DecisionResult preserves the resolved model, answer map and token usage; typed accessors fail clearly for absent IDs or wrong answer types. Snapshot mutable input collections and clone JSON values so disposal or mutation by the caller cannot change an in-flight request or cached result.

## HTTP behavior

POST the documented model/state/questions body to /v1/systemone at the configured absolute base URI. Reject base URIs with query or fragment; support a configured path prefix and normalize its trailing slash. Hosted configuration requires a bearer key and HTTPS. An explicitly configured local endpoint may use HTTP with optional authentication. Never modify shared HttpClient default headers per request.

Validate inputs before network calls. Require nonempty questions and IDs, valid instructions, supported state kinds (string, object, array), and valid criteria. Reject empty model names. Unknown additive response fields are allowed, but duplicate JSON property names, missing requested answers, unexpected answer IDs, mismatched types, missing required fields, invalid numeric values, invalid option keys and malformed distributions produce DecisionProtocolException. Probabilities and confidence must be finite and within [0,1]; distributions must sum to 1 within 0.001. Score must lie in [0, levelCount−1]; its distribution must cover all numeric level keys and its legend must cover the same levels. Preserve the provider's confidence rather than recomputing it. Enforce a configurable response-byte limit before parsing, default 1 MiB.

DecisionServiceException exposes HTTP status and sanitized metadata, not raw response content. Authentication, validation and protocol failures are never cached or converted to scores. Caller cancellation propagates as cancellation; a request timeout remains distinguishable from caller cancellation.

## Resilience, caching and telemetry

Core transport performs one attempt. DI integration offers explicit opt-in retries for evaluation POSTs, whose repeated execution may incur charges. Retry only transport failures and HTTP 408, 429, 500, 502, 503, 504 and 529; honor Retry-After and cap the total timeout and attempts. Never retry caller cancellation, 400/401/403/422, or protocol failures. Configure through Microsoft.Extensions.Http.Resilience rather than layering independent retry policies. Default total timeout: 30 seconds; optional retry preset: at most two retries within that budget. Circuit breaking remains opt-in and separately configurable.

Optional memory caching is off by default, bounded by entry count (default 1024 when enabled) with absolute expiration (default five minutes). Key by a SHA-256 hash of the full serialized effective request plus a configured provider/tenant namespace and endpoint identity. Do not expose raw cache keys in telemetry. Cache only complete valid results. Model aliases can change during a TTL, so documentation recommends explicit model versions for reproducible cached decisions. No cross-request coalescing in v1: avoid coupling one caller's cancellation to another.

Emit ActivitySource and Meter instruments without requiring an OpenTelemetry SDK. Track latency, evaluations, failures and cache hits using bounded tags such as provider, outcome and question type. Do not record state, instructions, API keys, document IDs, raw bodies or arbitrary question IDs. Applications may attach exporters. Record resolved model on traces; avoid unbounded model labels on metrics.

## Reranking

Input: query and evidence records containing a unique ID, content, optional source metadata and original retrieval score. Preserve caller metadata. Evaluate each candidate with its own state containing query and content, asking whether the content supplies relevant evidence for the query. Default concurrency is four, configurable; do not concatenate unrelated candidates into one shared evaluation. Several questions about the same candidate may share one request.

Return scored evidence ordered by probability of relevance descending, with original input position breaking ties. Keep retrieval score separate; never mix incomparable scores through an unexplained weighted sum. Threshold is optional and explicitly configured by the caller; without one, return all candidates. Optional top-K applies after threshold filtering. Empty input returns empty output with no calls. Reject duplicate IDs, blank queries/content, invalid thresholds and invalid concurrency/top-K settings before calls.

Default failure behavior is atomic: any candidate failure cancels remaining work and the rerank operation fails. Do not silently discard failed candidates or fabricate low relevance scores. Caller cancellation cancels queued and in-flight work. Preserve resolved model with each scored item. A passing score is evidence relevance, not proof of truth, freshness, authorization, or answerability. Contradiction analysis and automated business actions are deferred.

## Verification and acceptance

Automated tests use an HTTP handler with independent recorded request/response fixtures, not a production inference dependency. Cover all three question types, structured JSON, bearer authentication, model selection, serialization field names, response invariants, byte limits, HTTP failures, caller cancellation, timeouts, retry opt-in behavior, cache expiration/isolation and immutable snapshots.

Reranking tests cover concurrency limits, stable ties, threshold/top-K order, empty inputs, duplicate IDs, cancellation and atomic failure. An optional live smoke test runs only with an explicitly configured endpoint and exercises all three primitives; report which endpoints were actually exercised. Mock tests establish contract handling, not model accuracy.

Acceptance: clean restore, Release build, passing automated tests, NuGet pack, and a working console example against a configured endpoint. Document any checks blocked by environment restrictions. Do not claim live Jev or jevos compatibility without a live run. Provide DI usage, local endpoint configuration, caching/retry costs, threshold guidance and the adapter example for existing Weaviate results.

## Deferred scope

No native inference, model training, Weaviate client replacement, distributed caching, automatic fallback between models, cross-candidate contradiction detection, generated explanations, or public publication. These can be added behind the existing boundaries after measured requirements justify them.

## Contract references

- Official TypeSafe API: https://docs.typesafe.ai/api
- Official quick start: https://docs.typesafe.ai/introduction/quickstart
- jevos implementation and documented compatibility: https://github.com/feder-cr/jev

Contracts were inspected on 2026-10-03. Model quality and performance must be evaluated on the company's own labeled retrieval examples before choosing production thresholds.
