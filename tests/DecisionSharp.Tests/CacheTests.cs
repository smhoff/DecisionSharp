using System.Diagnostics.Metrics;
using DecisionSharp.Core;
using DecisionSharp.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
namespace DecisionSharp.Tests;

using Core.Questions;

public class CacheTests
{
    private static DecisionRequest Request(int state) => new(CoreTests.Json("{\"state\":" + state + "}"), new Dictionary<string, DecisionQuestion> { ["q"] = new YesNoQuestion("Relevant?") });
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public async Task CacheIsOffByDefaultAndSharedWhenEnabled(bool enabled, int calls)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(TestData.SingleResponse)));
        using var provider = ResilienceTests.Provider(handler, o => { o.Cache.Enabled = enabled; o.Cache.Namespace = "tenant-a"; });
        await provider.GetRequiredService<IDecisionEngine>().EvaluateAsync(Request(1)); await provider.GetRequiredService<IDecisionEngine>().EvaluateAsync(Request(1)); Assert.Equal(calls, handler.Calls);
    }
    [Fact]
    public async Task AbsoluteExpirationAndEntryCapacityAreEnforced()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(TestData.SingleResponse)));
        using var p = ResilienceTests.Provider(handler, o => { o.Cache.Enabled = true; o.Cache.Namespace = "tenant"; o.Cache.Capacity = 2; o.Cache.AbsoluteExpiration = TimeSpan.FromMilliseconds(30); }); var engine = p.GetRequiredService<IDecisionEngine>();
        await engine.EvaluateAsync(Request(0)); await Task.Delay(80); await engine.EvaluateAsync(Request(0)); Assert.Equal(2, handler.Calls);
        for (int i = 1; i <= 10; i++)
        {
            await engine.EvaluateAsync(Request(i));
        }

        var first = handler.Calls;
        for (int i = 1; i <= 10; i++)
        {
            await engine.EvaluateAsync(Request(i));
        }

        Assert.True(handler.Calls - first >= 8);
    }
    [Fact]
    public async Task ModelsAndFullRequestSeparateEntries()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(TestData.SingleResponse)));
        using var p = ResilienceTests.Provider(handler, o => { o.Cache.Enabled = true; o.Cache.Namespace = "tenant"; }); var engine = p.GetRequiredService<IDecisionEngine>();
        await engine.EvaluateAsync(Request(1)); await engine.EvaluateAsync(Request(2));
        var r = Request(1); await engine.EvaluateAsync(new DecisionRequest(r.State, r.Questions, "other-model"));
        await engine.EvaluateAsync(new DecisionRequest(r.State, new Dictionary<string, DecisionQuestion> { ["q"] = YesNoQuestion.WithCriteria("Other instruction", "yes", "no") })); Assert.Equal(4, handler.Calls);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("service-error")]
    public async Task FailuresAreNeverCached(string body)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(body, body == "{}" ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.Unauthorized)));
        using var p = ResilienceTests.Provider(handler, o => { o.Cache.Enabled = true; o.Cache.Namespace = "tenant"; }); var engine = p.GetRequiredService<IDecisionEngine>();
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => engine.EvaluateAsync(Request(1)));
        }

        Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public async Task CanceledCallerCannotReadHitAndKeysAreNotTelemetry()
    {
        var tags = new List<KeyValuePair<string, object?>>(); var hits = 0L; using var listener = new MeterListener(); listener.InstrumentPublished = (i, l) =>
        {
            if (i.Meter.Name == "DecisionSharp" && i.Name == "decision.cache.hits")
            {
                l.EnableMeasurementEvents(i);
            }
        }; listener.SetMeasurementEventCallback<long>((i, v, t, s) => { hits += v; tags.AddRange(t.ToArray()); }); listener.Start();
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(TestData.SingleResponse))); using var p = ResilienceTests.Provider(handler, o => { o.Cache.Enabled = true; o.Cache.Namespace = "sensitive-tenant"; }); var engine = p.GetRequiredService<IDecisionEngine>();
        var result = await engine.EvaluateAsync(Request(1)); using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.EvaluateAsync(Request(1), cts.Token));
        Assert.Same(result, await engine.EvaluateAsync(Request(1))); Assert.Equal(1, hits); Assert.All(tags, t => Assert.Equal("provider", t.Key)); Assert.DoesNotContain(tags, t => t.Value?.ToString() == "sensitive-tenant");
    }
    [Fact]
    public async Task ConcurrentCallsAreNotCoalesced()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var handler = new StubHttpHandler(async (_, t) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                entered.SetResult();
            }

            await release.Task.WaitAsync(t); return TestData.Response(TestData.SingleResponse);
        }); using var p = ResilienceTests.Provider(handler, o => { o.Cache.Enabled = true; o.Cache.Namespace = "tenant"; }); var engine = p.GetRequiredService<IDecisionEngine>();
        var a = engine.EvaluateAsync(Request(1)); var b = engine.EvaluateAsync(Request(1)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); release.SetResult(); await Task.WhenAll(a, b); Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public void InvalidCacheSettingsFailAtRegistration()
    {
        var services = new ServiceCollection(); Assert.ThrowsAny<ArgumentException>(() => services.AddDecisionSharp(o => { o.Jev.ApiKey = "key"; o.Cache.Enabled = true; }));
        Assert.ThrowsAny<ArgumentException>(() => services.AddDecisionSharp(o => { o.Jev.ApiKey = "key"; o.Cache.Enabled = true; o.Cache.Namespace = "tenant"; o.Cache.Capacity = 0; }));
    }
    [Fact]
    public void CacheIdentityIncludesTenantEndpointAndEffectiveModel()
    {
        var provider = TestData.Options().Snapshot(); var cache = new DecisionCacheOptions { Enabled = true, Namespace = "tenant-a" }; var request = Request(1);
        var key = CachedDecisionEngine.Key(request, provider, cache);
        Assert.Equal(64, key.Length); Assert.DoesNotContain("tenant", key);
        cache.Namespace = "tenant-b"; Assert.NotEqual(key, CachedDecisionEngine.Key(request, provider, cache)); cache.Namespace = "tenant-a";
        provider.BaseUri = new Uri("https://other.test/"); Assert.NotEqual(key, CachedDecisionEngine.Key(request, provider, cache)); provider = TestData.Options().Snapshot();
        provider.DefaultModel = "other"; Assert.NotEqual(key, CachedDecisionEngine.Key(request, provider, cache));
        provider = TestData.Options().Snapshot(); Assert.Equal(key, CachedDecisionEngine.Key(new DecisionRequest(request.State, request.Questions, "default-model"), provider, cache));
    }

    [Fact]
    public async Task DisposedAndMutatedSourceCannotChangeCacheIdentityOrHit()
    {
        using var source = System.Text.Json.JsonDocument.Parse("{\"state\":{},\"instructions\":{\"question\":\"Relevant?\"},\"criteria\":{\"true\":\"Yes\",\"false\":\"No\"}}");
        var questions = new Dictionary<string, DecisionQuestion> { ["q"] = new YesNoQuestion(source.RootElement.GetProperty("instructions"), source.RootElement.GetProperty("criteria")) };
        var request = new DecisionRequest(source.RootElement.GetProperty("state"), questions);
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(TestData.SingleResponse)));
        using var p = ResilienceTests.Provider(handler, o => { o.Cache.Enabled = true; o.Cache.Namespace = "tenant"; });
        var engine = p.GetRequiredService<IDecisionEngine>(); var first = await engine.EvaluateAsync(request);
        questions.Clear(); source.Dispose();
        Assert.Same(first, await engine.EvaluateAsync(request)); Assert.Equal(1, handler.Calls);
    }

}
