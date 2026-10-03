using System.Collections.ObjectModel;
using System.Net;
namespace DecisionSharp.Jev;
public sealed class DecisionProtocolException(string message) : Exception(message);
public sealed class DecisionTimeoutException() : TimeoutException("The decision evaluation exceeded its timeout.");
public sealed class DecisionServiceException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyDictionary<string,string> Metadata { get; }
    internal DecisionServiceException(HttpStatusCode status) : base($"Decision service returned HTTP {(int)status}.")
    {
        StatusCode=status;
        Metadata=new ReadOnlyDictionary<string,string>(new Dictionary<string,string>{["http.status_code"]=((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture)});
    }
}
