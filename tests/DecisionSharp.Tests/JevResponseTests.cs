using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DecisionSharp.Core;
using DecisionSharp.Jev;
namespace DecisionSharp.Tests;

public class JevResponseTests
{
    [Fact]
    public async Task PreservesAllAnswersUsageAndConfidence()
    {
        var result = await Read(TestData.Fixture("response.json"));
        Assert.Equal(123, result.Usage.InputTokens); Assert.Equal(12, result.Usage.OutputTokens);
        Assert.Equal(0.9, result.GetAnswer<YesNoAnswer>("urgent").ProbabilityOfYes);
        Assert.Equal("billing", result.GetAnswer<ChoiceAnswer>("route").Choice); Assert.Equal(0.3, result.GetAnswer<ChoiceAnswer>("route").Confidence);
        Assert.Equal(0.75, result.GetAnswer<ScoreAnswer>("severity").Score); Assert.Equal("High", result.GetAnswer<ScoreAnswer>("severity").Legend["1"]);
    }
    private static Task<DecisionResult> Read(string json, JevOptions? options = null) => TestData.Engine(new StubHttpHandler((_, _) => Task.FromResult(TestData.Response(json))), options).EvaluateAsync(TestData.Request());
    public static IEnumerable<object[]> InvalidResponses()
    {
        foreach (var path in new[] { "model", "answers", "usage", "answers.urgent.noul", "answers.route.confidence", "answers.route.probabilities", "answers.severity.score", "answers.severity.legend", "usage.input_tokens", "usage.output_tokens" })
        {
            var root = JsonNode.Parse(TestData.Fixture("response.json"))!; var pieces = path.Split('.'); JsonNode current = root; foreach (var part in pieces[..^1])
            {
                current = current[part]!;
            }

            current.AsObject().Remove(pieces[^1]); yield return new object[] { root.ToJsonString() };
        }
        foreach (var item in new (string path, string value)[] { ("model", "\" \""), ("usage.input_tokens", "-1"), ("usage.output_tokens", "1.5"), ("answers.urgent.noul", "1.1"), ("answers.urgent.noul", "-0.1"), ("answers.urgent.noul", "\"NaN\""), ("answers.urgent.noul", "1e999"), ("answers.route.type", "\"noul\""), ("answers.route.choice", "\"missing\""), ("answers.route.confidence", "1.01"), ("answers.route.probabilities", "{\"billing\":0.2,\"tech\":0.2}"), ("answers.route.probabilities", "{\"billing\":0.8,\"other\":0.2}"), ("answers.severity.score", "2"), ("answers.severity.score", "-1"), ("answers.severity.probabilities", "{\"0\":1}"), ("answers.severity.legend", "{\"0\":\"Low\",\"2\":\"High\"}"), ("answers.severity.confidence", "null") })
        {
            var root = JsonNode.Parse(TestData.Fixture("response.json"))!; var pieces = item.path.Split('.'); JsonNode current = root; foreach (var part in pieces[..^1])
            {
                current = current[part]!;
            }

            current[pieces[^1]] = JsonNode.Parse(item.value); yield return new object[] { root.ToJsonString() };
        }
        var missing = JsonNode.Parse(TestData.Fixture("response.json"))!; missing["answers"]!.AsObject().Remove("urgent"); yield return new object[] { missing.ToJsonString() };
        var extra = JsonNode.Parse(TestData.Fixture("response.json"))!; extra["answers"]!["extra"] = new JsonObject(); yield return new object[] { extra.ToJsonString() };
        yield return new object[] { "not json" };
        yield return new object[] { TestData.Fixture("response.json").Replace("\"noul\":0.9", "\"noul\":0.9,\"noul\":0.1") };
        yield return new object[] { TestData.Fixture("response.json").Replace("\"model\":\"resolved-v1\"", "\"model\":\"resolved-v1\",\"model\":\"duplicate\"") };
        yield return new object[] { TestData.Fixture("response.json")[..^1] + ",\"unknown\":{\"nested\":{\"x\":1,\"x\":2}}}" };
    }
    [Theory][MemberData(nameof(InvalidResponses))] public async Task InvalidContractsAreProtocolFailures(string json) => await Assert.ThrowsAsync<DecisionProtocolException>(() => Read(json));
    [Fact]
    public async Task AdditiveFieldsAndNumericBoundariesAccepted()
    {
        var json = TestData.Fixture("response.json").Replace("0.9", "1.0").Replace("0.8", "0.801"); var root = JsonNode.Parse(json)!; root["unknown"] = true;
        Assert.Equal(1, (await Read(root.ToJsonString())).GetAnswer<YesNoAnswer>("urgent").ProbabilityOfYes);
    }
    [Fact]
    public async Task ResponseLimitEnforcedWithNoContentLength()
    {
        var json = TestData.Fixture("response.json"); var options = TestData.Options(); options.ResponseByteLimit = 1048576;
        var padding = new string(' ', 1048576 - Encoding.UTF8.GetByteCount(json)); Assert.Equal("resolved-v1", (await Read(json + padding, options)).Model);
        await Assert.ThrowsAsync<DecisionProtocolException>(() => Read(json + padding + " ", options));
        var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(json + padding + " "))) }));
        await Assert.ThrowsAsync<DecisionProtocolException>(() => TestData.Engine(handler, options).EvaluateAsync(TestData.Request()));
    }
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(422)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task HttpFailuresHaveNoRawBodyAndNoRetries(int status)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(TestData.Response("secret raw body", (HttpStatusCode)status)));
        var ex = await Assert.ThrowsAsync<DecisionServiceException>(() => TestData.Engine(handler).EvaluateAsync(TestData.Request())); Assert.Equal((HttpStatusCode)status, ex.StatusCode); Assert.DoesNotContain("secret", ex.ToString()); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task CallerCancellationAndDeadlineAreDistinct()
    {
        var handler = new StubHttpHandler(async (_, t) => { await Task.Delay(Timeout.Infinite, t); return TestData.Response(); });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestData.Engine(handler).EvaluateAsync(TestData.Request(), cancellation.Token));
        await Assert.ThrowsAsync<DecisionTimeoutException>(() => TestData.Engine(handler, TestData.Options(TimeSpan.FromMilliseconds(30))).EvaluateAsync(TestData.Request()));
    }
    [Fact]
    public async Task StalledBodyUsesDeadlineAndCallerToken()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }));
        await Assert.ThrowsAsync<DecisionTimeoutException>(() => TestData.Engine(handler, TestData.Options(TimeSpan.FromMilliseconds(30))).EvaluateAsync(TestData.Request()));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30)); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestData.Engine(handler).EvaluateAsync(TestData.Request(), cts.Token));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamingLimitStopsAtLimitPlusOneWithoutTrustingHeaders(bool misleadingLength)
    {
        var stream = new UnseekableCountingStream(Encoding.UTF8.GetBytes(TestData.SingleResponse + new string(' ', 1000)));
        var content = new StreamContent(stream);
        if (misleadingLength)
        {
            content.Headers.ContentLength = 1;
        }
        else
        {
            Assert.Null(content.Headers.ContentLength);
        }

        var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var options = TestData.Options(); options.ResponseByteLimit = 128;
        await Assert.ThrowsAsync<DecisionProtocolException>(() => TestData.Engine(handler, options).EvaluateAsync(TestData.SingleRequest()));
        Assert.Equal(129, stream.BytesRead);
    }
    [Fact]
    public async Task ResponseReturnedAfterDeadlineRemainsTimeout()
    {
        var handler = new StubHttpHandler(async (_, _) => { await Task.Delay(60); return TestData.Response(status: HttpStatusCode.Unauthorized); });
        await Assert.ThrowsAsync<DecisionTimeoutException>(() => TestData.Engine(handler, TestData.Options(TimeSpan.FromMilliseconds(20))).EvaluateAsync(TestData.Request()));
    }
    private sealed class UnseekableCountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }
    private sealed class StalledStream : MemoryStream
    { public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(Timeout.Infinite, token); return 0; } }
}
