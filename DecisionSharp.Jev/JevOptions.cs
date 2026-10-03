namespace DecisionSharp.Jev;

public sealed class JevOptions
{
    public Uri BaseUri { get; set; } = new("https://api.typesafe.ai/");
    public string DefaultModel { get; set; } = "jev-latest";
    public string? ApiKey { get; set; }
    public bool AllowInsecureLocalEndpoint { get; set; }
    public int ResponseByteLimit { get; set; } = 1048576;
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public string ProviderName { get; set; } = "jev";
    internal JevOptions Snapshot()
    {
        if (BaseUri is null || !BaseUri.IsAbsoluteUri || BaseUri.Scheme is not ("http" or "https") || BaseUri.Query.Length != 0 || BaseUri.Fragment.Length != 0 || BaseUri.UserInfo.Length != 0)
        {
            throw new ArgumentException("BaseUri must be an absolute HTTP(S) URI without query, fragment or credentials.");
        }

        if (!AllowInsecureLocalEndpoint && (BaseUri.Scheme != "https" || string.IsNullOrWhiteSpace(ApiKey)))
        {
            throw new ArgumentException("Hosted evaluation requires HTTPS and a bearer key.");
        }

        if (ApiKey is not null && (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Any(char.IsWhiteSpace)))
        {
            throw new ArgumentException("Invalid bearer key.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(DefaultModel);
        if (ResponseByteLimit <= 0 || ResponseByteLimit == int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(ResponseByteLimit));
        }

        if (TotalTimeout < TimeSpan.FromMilliseconds(10) || TotalTimeout > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(TotalTimeout));
        }

        if (ProviderName is not ("jev" or "jevos"))
        {
            throw new ArgumentException("ProviderName must be jev or jevos.");
        }

        return new JevOptions { BaseUri = new Uri(BaseUri.AbsoluteUri.TrimEnd('/') + "/"), DefaultModel = DefaultModel, ApiKey = ApiKey, AllowInsecureLocalEndpoint = AllowInsecureLocalEndpoint, ResponseByteLimit = ResponseByteLimit, TotalTimeout = TotalTimeout, ProviderName = ProviderName };
    }
}
