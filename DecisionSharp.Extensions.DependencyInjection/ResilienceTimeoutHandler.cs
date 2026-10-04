using Polly.Timeout;

namespace DecisionSharp.Extensions.DependencyInjection;

internal sealed class ResilienceTimeoutHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Decision resilience deadline exceeded.");
        }
    }
}