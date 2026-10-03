using System.Net;
using System.Diagnostics;
using System.Net.Http.Headers;
using DecisionSharp.Core;
using DecisionSharp.Jev;
using DecisionSharp.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
namespace DecisionSharp.Tests;

public class ResilienceTests
{
    internal static ServiceProvider Provider(StubHttpHandler handler, Action<DecisionSharpOptions>? configure = null)
    {
        var services = new ServiceCollection(); services.AddDecisionSharp(o => { o.Jev = TestData.Options(); configure?.Invoke(o); }).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public async Task PostRetriesRequireOptInAndAreCapped(bool enabled, int expected)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(status: HttpStatusCode.ServiceUnavailable)));
        using var provider = Provider(handler, o => o.EnableRetries = enabled);
        await Assert.ThrowsAsync<DecisionServiceException>(() => provider.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request())); Assert.Equal(expected, handler.Calls);
    }
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(529)]
    public async Task RetryOnlyExplicitTransientStatuses(int status)
    {
        var count = 0; var handler = new StubHttpHandler((_, _) => { var r = TestData.Response(status: Interlocked.Increment(ref count) == 1 ? (HttpStatusCode)status : HttpStatusCode.OK); r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero); return Task.FromResult(r); });
        using var provider = Provider(handler, o => o.EnableRetries = true); Assert.Equal("resolved-v1", (await provider.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request())).Model); Assert.Equal(2, handler.Calls);
    }
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(422)]
    [InlineData(501)]
    public async Task PermanentStatusesAreNotRetried(int status)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(status: (HttpStatusCode)status))); using var provider = Provider(handler, o => o.EnableRetries = true);
        await Assert.ThrowsAsync<DecisionServiceException>(() => provider.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request())); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task TransportFailureRetriedButProtocolFailureIsNot()
    {
        var n = 0; var handler = new StubHttpHandler((_, _) => Interlocked.Increment(ref n) == 1 ? Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")) : Task.FromResult(TestData.Response())); using (var p = Provider(handler, o => o.EnableRetries = true)) Assert.Equal("resolved-v1", (await p.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request())).Model); Assert.Equal(2, handler.Calls);
        handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response("{}"))); using var provider = Provider(handler, o => o.EnableRetries = true); await Assert.ThrowsAsync<DecisionProtocolException>(() => provider.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request())); Assert.Equal(1, handler.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterIsHonored(bool date)
    {
        var n = 0; var handler = new StubHttpHandler((_, _) => { var response = TestData.Response(status: ++n == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK); response.Headers.RetryAfter = date ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(2)) : new RetryConditionHeaderValue(TimeSpan.FromSeconds(1)); return Task.FromResult(response); });
        using var provider = Provider(handler, o => o.EnableRetries = true); var start = Stopwatch.StartNew(); await provider.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request()); Assert.True(start.Elapsed >= TimeSpan.FromMilliseconds(800)); Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public async Task RetryDelayCannotOutliveTotalBudget()
    {
        var handler = new StubHttpHandler((_, _) => { var r = TestData.Response(status: HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)); return Task.FromResult(r); });
        using var provider = Provider(handler, o => { o.EnableRetries = true; o.Jev.TotalTimeout = TimeSpan.FromMilliseconds(100); });
        await Assert.ThrowsAsync<DecisionTimeoutException>(() => provider.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request())); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task CallerCancellationIsNotRetried()
    {
        var handler = new StubHttpHandler(async (_, t) => { await Task.Delay(Timeout.Infinite, t); return TestData.Response(); }); using var p = Provider(handler, o => o.EnableRetries = true); using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request(), cts.Token)); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task CircuitBreakingIsSeparatelyOptIn()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(status: HttpStatusCode.ServiceUnavailable)));
        using var p = Provider(handler, o => { o.EnableCircuitBreaker = true; o.CircuitMinimumThroughput = 2; }); var engine = p.GetRequiredService<IDecisionEngine>();
        for (var i = 0; i < 2; i++) await Assert.ThrowsAsync<DecisionServiceException>(() => engine.EvaluateAsync(TestData.Request()));
        await Assert.ThrowsAsync<BrokenCircuitException>(() => engine.EvaluateAsync(TestData.Request())); Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public void RejectsInvalidRegistrationOptions()
    {
        var services = new ServiceCollection(); Assert.ThrowsAny<ArgumentException>(() => services.AddDecisionSharp(o => o.Jev.ApiKey = null));
        Assert.ThrowsAny<ArgumentException>(() => services.AddDecisionSharp(o => { o.Jev.ApiKey = "key"; o.EnableCircuitBreaker = true; o.CircuitFailureRatio = 2; }));
    }
}
