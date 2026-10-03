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
        var n = 0; var handler = new StubHttpHandler((_, _) => Interlocked.Increment(ref n) == 1 ? Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")) : Task.FromResult(TestData.Response())); using (var p = Provider(handler, o => o.EnableRetries = true))
        {
            Assert.Equal("resolved-v1", (await p.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request())).Model);
        }

        Assert.Equal(2, handler.Calls);
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
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<DecisionServiceException>(() => engine.EvaluateAsync(TestData.Request()));
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(() => engine.EvaluateAsync(TestData.Request())); Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public void RejectsInvalidRegistrationOptions()
    {
        var services = new ServiceCollection(); Assert.ThrowsAny<ArgumentException>(() => services.AddDecisionSharp(o => o.Jev.ApiKey = null));
        Assert.ThrowsAny<ArgumentException>(() => services.AddDecisionSharp(o => { o.Jev.ApiKey = "key"; o.EnableCircuitBreaker = true; o.CircuitFailureRatio = 2; }));
    }
    [Theory]
    [InlineData(0.001)]
    [InlineData(172800)]
    public void UnsupportedTimeoutsFailImmediatelyForDirectAndDi(double seconds)
    {
        var options = TestData.Options(TimeSpan.FromSeconds(seconds));
        Assert.ThrowsAny<ArgumentException>(() => TestData.Engine(new StubHttpHandler((_, _) => Task.FromResult(TestData.Response())), options));
        var services = new ServiceCollection();
        Assert.ThrowsAny<ArgumentException>(() => services.AddDecisionSharp(o => o.Jev = options));
    }
    [Fact]
    public async Task RetryWaitingAndFinalBodyShareOneDeadline()
    {
        var calls = 0; var warming = true;
        var handler = new StubHttpHandler((_, _) =>
        {
            if (warming)
            {
                return Task.FromResult(TestData.Response());
            }

            if (Interlocked.Increment(ref calls) == 1)
            {
                var retry = TestData.Response(status: HttpStatusCode.TooManyRequests);
                retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(40));
                return Task.FromResult(retry);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledBody()) });
        });
        using var p = Provider(handler, o => { o.EnableRetries = true; o.Jev.TotalTimeout = TimeSpan.FromMilliseconds(500); });
        var engine = p.GetRequiredService<IDecisionEngine>();
        await engine.EvaluateAsync(TestData.Request()); warming = false;
        var start = Stopwatch.StartNew();
        await Assert.ThrowsAsync<DecisionTimeoutException>(() => engine.EvaluateAsync(TestData.Request()).WaitAsync(TimeSpan.FromMilliseconds(1500)));
        Assert.Equal(2, calls);
        Assert.True(start.Elapsed < TimeSpan.FromMilliseconds(1200));
    }
    private sealed class StalledBody : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }
    [Fact]
    public async Task DefaultRegisteredHandlerDoesNotFollowRedirectsToAnotherOrigin()
    {
        static System.Net.HttpListener Listen()
        {
            using var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            socket.Start(); var port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            var listener = new System.Net.HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start(); return listener;
        }
        using var origin = Listen(); using var destination = Listen();
        var destinationRequest = destination.GetContextAsync();
        var originTask = Task.Run(async () =>
        {
            var context = await origin.GetContextAsync();
            Assert.Equal("Bearer synthetic-key", context.Request.Headers["Authorization"]);
            context.Response.StatusCode = 307;
            context.Response.RedirectLocation = destination.Prefixes.Single() + "v1/systemone";
            context.Response.Close();
        });
        var services = new ServiceCollection();
        services.AddDecisionSharp(o => { o.Jev.BaseUri = new Uri(origin.Prefixes.Single()); o.Jev.AllowInsecureLocalEndpoint = true; o.Jev.ApiKey = "synthetic-key"; });
        using var provider = services.BuildServiceProvider();
        var evaluation = provider.GetRequiredService<IDecisionEngine>().EvaluateAsync(TestData.Request());
        // If redirects are accidentally enabled, complete the destination response so the test fails promptly.
        var winner = await Task.WhenAny(evaluation, destinationRequest).WaitAsync(TimeSpan.FromSeconds(3));
        if (winner == destinationRequest)
        {
            var received = await destinationRequest;
            var bytes = System.Text.Encoding.UTF8.GetBytes(TestData.Fixture("response.json"));
            received.Response.ContentLength64 = bytes.Length; await received.Response.OutputStream.WriteAsync(bytes); received.Response.Close();
        }
        await originTask;
        var error = await Assert.ThrowsAsync<DecisionServiceException>(() => evaluation);
        Assert.Equal(307, (int)error.StatusCode);
        Assert.False(destinationRequest.IsCompleted);
        destination.Close();
        try { await destinationRequest; } catch (System.Net.HttpListenerException) { } catch (ObjectDisposedException) { }
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(86400)]
    public void SupportedTimeoutBoundariesResolveDirectAndDiClients(double seconds)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response()));
        Assert.NotNull(TestData.Engine(handler, TestData.Options(TimeSpan.FromSeconds(seconds))));
        using var p = Provider(handler, o => o.Jev.TotalTimeout = TimeSpan.FromSeconds(seconds));
        Assert.NotNull(p.GetRequiredService<IDecisionEngine>());
    }

}
