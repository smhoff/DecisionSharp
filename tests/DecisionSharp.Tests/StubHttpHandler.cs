using System.Net;
using DecisionSharp.Core;
using DecisionSharp.Jev;
namespace DecisionSharp.Tests;

using Core.Questions;

internal sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Interlocked.Increment(ref Calls); return send(request, token); }
}
internal static class TestData
{
    public static string Fixture(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
    public static DecisionRequest Request(string? model = null) => new(CoreTests.Json("{\"message\":\"An invoice failed.\"}"), new Dictionary<string, DecisionQuestion>
    {
        ["urgent"] = YesNoQuestion.WithCriteria("Is it urgent?", "Time sensitive", "Routine"),
        ["route"] = new ChoiceQuestion("Which team?", new Dictionary<string, string?> { ["billing"] = "Invoices", ["tech"] = null }),
        ["severity"] = new ScoreQuestion(CoreTests.Json("{\"question\":\"How severe?\"}"), new[] { CoreTests.Json("\"Low\""), CoreTests.Json("{\"description\":\"High\"}") })
    }, model);
    public static DecisionRequest SingleRequest() => new(CoreTests.Json("{}"), new Dictionary<string, DecisionQuestion> { ["q"] = new YesNoQuestion("Relevant?") });
    public const string SingleResponse = "{\"model\":\"v1\",\"answers\":{\"q\":{\"type\":\"noul\",\"noul\":0.7}},\"usage\":{\"input_tokens\":1,\"output_tokens\":0}}";
    public static JevOptions Options(TimeSpan? timeout = null) => new() { BaseUri = new Uri("https://example.test/prefix"), DefaultModel = "default-model", ApiKey = "secret", TotalTimeout = timeout ?? TimeSpan.FromSeconds(30) };
    public static HttpResponseMessage Response(string? json = null, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json ?? Fixture("response.json")) };
    public static JevDecisionEngine Engine(StubHttpHandler handler, JevOptions? options = null) => new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, options ?? Options());
}
