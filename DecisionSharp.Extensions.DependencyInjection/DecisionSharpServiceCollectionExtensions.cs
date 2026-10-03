using DecisionSharp.Core;
using DecisionSharp.Jev;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
namespace DecisionSharp.Extensions.DependencyInjection;

public static class DecisionSharpServiceCollectionExtensions
{
    public static IHttpClientBuilder AddDecisionSharp(this IServiceCollection services, Action<DecisionSharpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(configure);
        var configured = new DecisionSharpOptions(); configure(configured); var options = configured.Snapshot();
        services.AddSingleton(options.Jev);
        var http = services.AddHttpClient<JevDecisionEngine>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        services.AddTransient<ResilienceTimeoutHandler>(); http.AddHttpMessageHandler<ResilienceTimeoutHandler>();
        http.AddResilienceHandler("decision", pipeline =>
        {
            pipeline.AddTimeout(options.Jev.TotalTimeout);
            if (options.EnableRetries)
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldRetryAfterHeader = true,
                    ShouldHandle = args => ValueTask.FromResult(!args.Context.CancellationToken.IsCancellationRequested && Transient(args.Outcome)),
                    OnRetry = args => { args.Outcome.Result?.Dispose(); return default; }
                });
            }

            if (options.EnableCircuitBreaker)
            {
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = options.CircuitFailureRatio,
                    MinimumThroughput = options.CircuitMinimumThroughput,
                    SamplingDuration = options.CircuitSamplingDuration,
                    BreakDuration = options.CircuitBreakDuration,
                    ShouldHandle = args => ValueTask.FromResult(!args.Context.CancellationToken.IsCancellationRequested && Transient(args.Outcome))
                });
            }
        });
        if (options.Cache.Enabled)
        {
            services.AddSingleton<IDecisionEngine>(sp => new CachedDecisionEngine(sp.GetRequiredService<JevDecisionEngine>(), options.Jev, options.Cache));
        }
        else
        {
            services.AddTransient<IDecisionEngine>(sp => sp.GetRequiredService<JevDecisionEngine>());
        }

        return http;
    }
    private static bool Transient(Outcome<HttpResponseMessage> outcome) => outcome.Exception is HttpRequestException || (int?)outcome.Result?.StatusCode is 408 or 429 or 500 or 502 or 503 or 504 or 529;
}
