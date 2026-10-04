using System.Security.Cryptography;
using System.Text;
using DecisionSharp.Core;
using DecisionSharp.Jev;
using Microsoft.Extensions.Caching.Memory;

namespace DecisionSharp.Extensions.DependencyInjection;

internal sealed class CachedDecisionEngine : IDecisionEngine, IDisposable
{
    private readonly IDecisionEngine inner;
    private readonly JevOptions provider;
    private readonly DecisionCacheOptions options;
    private readonly MemoryCache cache;

    internal CachedDecisionEngine(IDecisionEngine inner, JevOptions provider, DecisionCacheOptions options)
    {
        this.inner = inner;
        this.provider = provider.Snapshot();
        this.options = options.Snapshot();
        cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = this.options.Capacity });
    }

    internal static string Key(DecisionRequest request, JevOptions provider, DecisionCacheOptions options)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var value in new[] { options.Namespace!, provider.BaseUri.AbsoluteUri, provider.ProviderName })
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        hash.AppendData(JevWire.SerializeRequest(request, provider.DefaultModel));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public async Task<DecisionResult> EvaluateAsync(DecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(request, provider, options);
        if (cache.TryGetValue<DecisionResult>(key, out var result))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecisionTelemetry.CacheHits.Add(1, new KeyValuePair<string, object?>("provider", provider.ProviderName));
            return result!;
        }

        result = await inner.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        cache.Set(key, result,
            new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = options.AbsoluteExpiration });
        return result;
    }

    public void Dispose() => cache.Dispose();
}