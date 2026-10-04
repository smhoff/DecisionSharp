using System.Diagnostics;
using System.Net.Http.Headers;
using DecisionSharp.Core;

namespace DecisionSharp.Jev;

public sealed class JevDecisionEngine : IDecisionEngine
{
    private readonly HttpClient client;
    private readonly JevOptions options;

    public JevDecisionEngine(HttpClient httpClient, JevOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        client = httpClient;
        this.options = options.Snapshot();
    }

    public async Task<DecisionResult> EvaluateAsync(DecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var body = JevWire.SerializeRequest(request, options.DefaultModel);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.TotalTimeout);
        using var activity = DecisionTelemetry.Activities.StartActivity("decision.evaluate");
        activity?.SetTag("provider", options.ProviderName);
        var start = Stopwatch.GetTimestamp();
        var outcome = "success";
        var types = request.Questions.Values.Select(JevWire.TypeName).Distinct().ToArray();
        var questionType = types.Length == 1 ? types[0] : "mixed";

        void CheckDeadline()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(start) >= options.TotalTimeout)
            {
                throw new DecisionTimeoutException();
            }

            deadline.Token.ThrowIfCancellationRequested();
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(options.BaseUri, "v1/systemone"));
            message.Content = new ByteArrayContent(body);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (options.ApiKey is not null)
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }

            using var response = await client
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            CheckDeadline();
            if (!response.IsSuccessStatusCode)
            {
                throw new DecisionServiceException(response.StatusCode);
            }

            if (response.Content.Headers.ContentLength > options.ResponseByteLimit)
            {
                throw new DecisionProtocolException("Response exceeds byte limit.");
            }

            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[Math.Min(8192, options.ResponseByteLimit + 1)];
            while (true)
            {
                var count = await stream
                    .ReadAsync(
                        buffer.AsMemory(0, (int)Math.Min(buffer.Length, options.ResponseByteLimit + 1 - output.Length)),
                        deadline.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                if (output.Length + count > options.ResponseByteLimit)
                {
                    throw new DecisionProtocolException("Response exceeds byte limit.");
                }

                output.Write(buffer, 0, count);
            }

            CheckDeadline();
            var result = JevWire.ParseResponse(output.ToArray(), request);
            CheckDeadline();
            activity?.SetTag("decision.model", result.Model);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                outcome = "cancelled";
                throw new OperationCanceledException(cancellationToken);
            }

            outcome = "timeout";
            throw new DecisionTimeoutException();
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcome = "timeout";
            throw new DecisionTimeoutException();
        }
        catch (Exception ex)
        {
            outcome = ex switch
            {
                DecisionProtocolException => "protocol", DecisionServiceException => "service",
                HttpRequestException => "transport", _ => "failure"
            };
            throw;
        }
        finally
        {
            var tags = new TagList
                { { "provider", options.ProviderName }, { "outcome", outcome }, { "question.type", questionType } };
            DecisionTelemetry.Evaluations.Add(1, tags);
            DecisionTelemetry.Duration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds, tags);
            activity?.SetTag("outcome", outcome);
            if (outcome != "success")
            {
                DecisionTelemetry.Failures.Add(1, tags);
                activity?.SetStatus(ActivityStatusCode.Error);
            }
        }
    }
}