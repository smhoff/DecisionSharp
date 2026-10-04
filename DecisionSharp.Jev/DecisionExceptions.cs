using System.Collections.ObjectModel;
using System.Net;

namespace DecisionSharp.Jev;

/// <summary>
/// Represents an exception that occurs when the decision protocol encounters
/// a violation or unexpected behavior during the processing of decision-related data.
/// </summary>
/// <remarks>
/// This exception is specifically used in scenarios where the structure or contract
/// of the protocol's responses or requests does not meet expected standards or has mismatches,
/// resulting in the inability to parse, validate, or process inputs or outputs.
/// </remarks>
/// <seealso cref="System.Exception" />
public sealed class DecisionProtocolException(string message) : Exception(message);

/// <summary>
/// Represents an exception that is thrown when the evaluation of a decision
/// exceeds the allocated timeout duration.
/// </summary>
/// <remarks>
/// This exception is specifically used to indicate timeout-related failures
/// in decision-making processes. It helps to differentiate timeout issues
/// from other operational failures that may arise during execution.
/// </remarks>
/// <seealso cref="System.TimeoutException"/>
/// <seealso cref="DecisionSharp.Jev.JevDecisionEngine"/>
public sealed class DecisionTimeoutException() : TimeoutException("The decision evaluation exceeded its timeout.");

/// <summary>
/// Represents an exception that is generated when the decision service
/// returns an HTTP response indicating an error.
/// </summary>
/// <remarks>
/// This exception provides information about the HTTP status code that the
/// decision service returned, as well as metadata associated with the error.
/// It is typically thrown when an HTTP error response is encountered during
/// decision evaluation.
/// </remarks>
public sealed class DecisionServiceException : Exception
{
    /// <summary>
    /// Gets the HTTP status code associated with the exception.
    /// </summary>
    /// <remarks>
    /// The <c>StatusCode</c> property represents the HTTP response status that caused the exception to be thrown.
    /// Common status codes include 400 (Bad Request), 401 (Unauthorized), 403 (Forbidden), 422 (Unprocessable Entity),
    /// 429 (Too Many Requests), and 500 (Internal Server Error).
    /// </remarks>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Retrieves additional information about the context or state of the decision
    /// service when the exception was raised. This property contains a read-only
    /// dictionary of key-value pairs that provide details such as HTTP status codes
    /// or other service-related data.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Represents an exception that is thrown when the Decision service
    /// responds with a non-success HTTP status code.
    /// </summary>
    /// <remarks>
    /// This exception contains the HTTP status code returned by the Decision
    /// service, as well as any associated metadata.
    /// </remarks>
    internal DecisionServiceException(HttpStatusCode status) : base($"Decision service returned HTTP {(int)status}.")
    {
        StatusCode = status;
        Metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            { ["http.status_code"] = ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }
}