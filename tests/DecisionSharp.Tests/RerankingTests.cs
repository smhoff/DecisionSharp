using System.Text.Json;
using DecisionSharp.Core;
using DecisionSharp.Reranking;
namespace DecisionSharp.Tests;

public class RerankingTests
{
    private sealed class Engine(Func<DecisionRequest, CancellationToken, Task<DecisionResult>> evaluate) : IDecisionEngine
    { public Task<DecisionResult> EvaluateAsync(DecisionRequest r, CancellationToken t = default) => evaluate(r, t); }
    private static DecisionResult Result(double p) => new("resolved", new Dictionary<string, DecisionAnswer> { ["relevance"] = new YesNoAnswer(p) }, new TokenUsage(1, 0));
    [Fact]
    public async Task StableOrderingAndThresholdBeforeTopKPreserveMetadata()
    {
        using var doc = JsonDocument.Parse("{\"source\":\"weaviate\"}"); var metadata = new Dictionary<string, JsonElement> { ["source"] = doc.RootElement.GetProperty("source") };
        var records = new[] { new Evidence("a", "a", metadata, 0.99), new Evidence("b", "b"), new Evidence("c", "c") }; doc.Dispose(); metadata.Clear();
        var engine = new Engine((r, t) => { Assert.Equal("query", r.State.GetProperty("query").GetString()); Assert.Single(r.Questions); var c = r.State.GetProperty("content").GetString(); return Task.FromResult(Result(c == "c" ? 0.9 : 0.8)); });
        var all = await new EvidenceReranker(engine).RerankAsync("query", records); Assert.Equal(new[] { "c", "a", "b" }, all.Select(r => r.Evidence.Id));
        var top = await new EvidenceReranker(engine, new() { Threshold = 0.8, TopK = 2 }).RerankAsync("query", records); Assert.Equal(new[] { "c", "a" }, top.Select(r => r.Evidence.Id)); Assert.Equal(0.99, top[1].Evidence.RetrievalScore); Assert.Equal("weaviate", top[1].Evidence.Metadata!["source"].GetString()); Assert.All(top, r => Assert.Equal("resolved", r.ResolvedModel));
        Assert.Empty(await new EvidenceReranker(engine, new() { Threshold = 0.95 }).RerankAsync("query", records));
    }
    [Theory]
    [InlineData(4)]
    [InlineData(2)]
    public async Task BoundsActiveCallsAndSnapshotsCandidateList(int concurrency)
    {
        int calls = 0, active = 0, peak = 0; var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new Engine(async (r, t) => { var now = Interlocked.Increment(ref active); peak = Math.Max(peak, now); if (Interlocked.Increment(ref calls) == concurrency) entered.TrySetResult(); try { await release.Task.WaitAsync(t); return Result(0.5); } finally { Interlocked.Decrement(ref active); } });
        var records = Enumerable.Range(0, 8).Select(i => new Evidence(i.ToString(), "content")).ToList(); var task = new EvidenceReranker(engine, new() { Concurrency = concurrency }).RerankAsync("query", records);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(concurrency, active); records.Clear(); release.SetResult(); var result = await task; Assert.Equal(8, result.Count); Assert.Equal(concurrency, peak); Assert.Equal(Enumerable.Range(0, 8).Select(i => i.ToString()), result.Select(r => r.Evidence.Id));
    }
    [Fact]
    public async Task EmptyDuplicateAndInvalidInputsMakeNoCalls()
    {
        var calls = 0; var engine = new Engine((_, _) => { calls++; return Task.FromResult(Result(0.5)); }); var reranker = new EvidenceReranker(engine);
        Assert.Empty(await reranker.RerankAsync("query", Array.Empty<Evidence>()));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => reranker.RerankAsync(" ", Array.Empty<Evidence>()));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => reranker.RerankAsync("query", new[] { new Evidence("same", "one"), new Evidence("same", "two") }));
        Assert.ThrowsAny<ArgumentException>(() => new Evidence("id", " ")); Assert.ThrowsAny<ArgumentException>(() => new Evidence(" ", "content"));
        foreach (var options in new RerankingOptions[] { new() { Concurrency = 0 }, new() { TopK = 0 }, new() { Threshold = double.NaN }, new() { Threshold = -0.1 }, new() { Threshold = 1.1 } }) Assert.ThrowsAny<ArgumentException>(() => new EvidenceReranker(engine, options)); Assert.Equal(0, calls);
    }
    [Fact]
    public async Task OriginalFailureCancelsSiblingsAndQueuedWork()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0, canceled = 0; var failure = new HttpRequestException("origin");
        var engine = new Engine(async (r, t) => { if (Interlocked.Increment(ref calls) == 4) entered.TrySetResult(); if (r.State.GetProperty("content").GetString() == "fail") { await entered.Task.WaitAsync(t); throw failure; } try { await Task.Delay(Timeout.Infinite, t); return Result(0.1); } catch (OperationCanceledException) { Interlocked.Increment(ref canceled); throw; } });
        var records = new[] { new Evidence("0", "fail") }.Concat(Enumerable.Range(1, 20).Select(i => new Evidence(i.ToString(), "sibling"))).ToArray();
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new EvidenceReranker(engine).RerankAsync("query", records)); Assert.Same(failure, ex); Assert.Equal(4, calls); Assert.Equal(3, canceled);
    }
    [Fact]
    public async Task CallerCancellationStopsQueuedWork()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0; var engine = new Engine(async (_, t) => { Interlocked.Increment(ref calls); entered.SetResult(); await Task.Delay(Timeout.Infinite, t); return Result(0.1); });
        using var cts = new CancellationTokenSource(); var task = new EvidenceReranker(engine, new() { Concurrency = 1 }).RerankAsync("query", Enumerable.Range(0, 10).Select(i => new Evidence(i.ToString(), "content")).ToArray(), cts.Token); await entered.Task; cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); Assert.Equal(1, calls);
    }
}
