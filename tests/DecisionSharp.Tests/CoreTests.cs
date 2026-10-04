using System.Text.Json;
using DecisionSharp.Core;
namespace DecisionSharp.Tests;

using Core.Answers;
using Core.Questions;

public class CoreTests
{
    public static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    [Fact]
    public void RequestOwnsStateQuestionsAndCriteria()
    {
        using var doc = JsonDocument.Parse("{\"text\":\"owned\"}");
        var criteria = new Dictionary<string, JsonElement?> { ["a"] = doc.RootElement, ["b"] = null };
        var questions = new Dictionary<string, DecisionQuestion> { ["pick"] = new ChoiceQuestion(doc.RootElement, criteria) };
        var request = new DecisionRequest(doc.RootElement, questions);
        doc.Dispose(); criteria.Clear(); questions.Clear();
        Assert.Equal("owned", request.State.GetProperty("text").GetString());
        var question = Assert.IsType<ChoiceQuestion>(request.Questions["pick"]);
        Assert.Equal(2, question.Criteria.Count);
        Assert.Equal("owned", question.Instructions.GetProperty("text").GetString());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, DecisionQuestion>)request.Questions).Clear());
    }
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("{\"x\":1,\"x\":2}")]
    public void RejectsUnsupportedOrDuplicateJson(string json) => Assert.Throws<ArgumentException>(() => new DecisionRequest(Json(json), new Dictionary<string, DecisionQuestion> { ["q"] = new YesNoQuestion("ok") }));
    [Theory]
    [InlineData("\"ok\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void AcceptsAllStructuredKinds(string json) => Assert.Single(new DecisionRequest(Json(json), new Dictionary<string, DecisionQuestion> { ["q"] = new YesNoQuestion(Json(json)) }).Questions);
    [Theory][InlineData(1)][InlineData(256)] public void ChoiceBounds(int count) => Assert.Throws<ArgumentException>(() => new ChoiceQuestion("choose", Enumerable.Range(0, count).ToDictionary(i => i.ToString(), _ => (string?)null)));
    [Theory][InlineData(2)][InlineData(255)] public void ChoiceBoundariesAccepted(int count) => Assert.Equal(count, new ChoiceQuestion("choose", Enumerable.Range(0, count).ToDictionary(i => i.ToString(), _ => (string?)null)).Criteria.Count);
    [Theory][InlineData(1)][InlineData(11)] public void ScoreBounds(int count) => Assert.Throws<ArgumentException>(() => new ScoreQuestion("score", Enumerable.Repeat("level", count).ToArray()));
    [Theory][InlineData(2)][InlineData(10)] public void ScoreBoundariesAccepted(int count) => Assert.Equal(count, new ScoreQuestion("score", Enumerable.Repeat("level", count).ToArray()).Criteria.Count);
    [Fact]
    public void BlankInputsAndMalformedCriteriaAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new YesNoQuestion(" "));
        Assert.Throws<ArgumentException>(() => new YesNoQuestion(Json("{}"), Json("{\"maybe\":\"x\"}")));
        Assert.Throws<ArgumentException>(() => new ScoreQuestion(Json("{}"), new[] { Json("null"), Json("{}") }));
        Assert.Throws<ArgumentException>(() => new DecisionRequest(Json("{}"), new Dictionary<string, DecisionQuestion>()));
        Assert.Throws<ArgumentException>(() => new DecisionRequest(Json("{}"), new Dictionary<string, DecisionQuestion> { [" "] = new YesNoQuestion("ok") }));
        Assert.Throws<ArgumentException>(() => new DecisionRequest(Json("{}"), new Dictionary<string, DecisionQuestion> { ["q"] = new YesNoQuestion("ok") }, " "));
        Assert.Throws<ArgumentException>(() => new ChoiceQuestion("choose", new Dictionary<string, string?> { [" "] = null, ["a"] = null }));
    }
    [Fact]
    public void StructuredCriteriaAndResultSnapshots()
    {
        var levels = new List<JsonElement> { Json("{}"), Json("[]") };
        var score = new ScoreQuestion(Json("{}"), levels); levels.Clear(); Assert.Equal(2, score.Criteria.Count);
        var yes = new YesNoQuestion(Json("{}"), Json("{\"true\":{},\"false\":[]}")); Assert.NotNull(yes.Criteria);
        var probabilities = new Dictionary<string, double> { ["a"] = 0.7, ["b"] = 0.3 };
        var answers = new Dictionary<string, DecisionAnswer> { ["c"] = new ChoiceAnswer("a", probabilities, 0.2), ["y"] = new YesNoAnswer(0.9) };
        var result = new DecisionResult("resolved", answers, new TokenUsage(1, 2)); probabilities.Clear(); answers.Clear();
        Assert.Equal(0.2, result.GetAnswer<ChoiceAnswer>("c").Confidence);
        Assert.Equal(2, result.GetAnswer<ChoiceAnswer>("c").Probabilities.Count);
        Assert.Equal(0.9, result.GetAnswer<YesNoAnswer>("y").ProbabilityOfYes);
        Assert.Throws<KeyNotFoundException>(() => result.GetAnswer<YesNoAnswer>("absent"));
        Assert.Throws<InvalidOperationException>(() => result.GetAnswer<ScoreAnswer>("y"));
    }
}
