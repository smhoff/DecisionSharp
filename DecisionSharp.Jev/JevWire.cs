using System.Globalization;
using System.Text.Json;
using DecisionSharp.Core;

namespace DecisionSharp.Jev;

using Core.Answers;
using Core.Questions;

internal static class JevWire
{
    internal static byte[] SerializeRequest(DecisionRequest request, string defaultModel)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.Model ?? defaultModel);
            writer.WritePropertyName("state");
            request.State.WriteTo(writer);
            writer.WriteStartObject("questions");
            foreach (var pair in request.Questions.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(pair.Key);
                writer.WriteString("type", TypeName(pair.Value));
                writer.WritePropertyName("instructions");
                pair.Value.Instructions.WriteTo(writer);
                switch (pair.Value)
                {
                    case YesNoQuestion { Criteria: { } criteria }:
                        writer.WritePropertyName("criteria");
                        criteria.WriteTo(writer);
                        break;
                    case ChoiceQuestion choice:
                        writer.WriteStartObject("criteria");
                        foreach (var option in choice.Criteria.OrderBy(p => p.Key, StringComparer.Ordinal))
                        {
                            writer.WritePropertyName(option.Key);
                            if (option.Value is { } value)
                            {
                                value.WriteTo(writer);
                            }
                            else
                            {
                                writer.WriteNullValue();
                            }
                        }

                        writer.WriteEndObject();
                        break;
                    case ScoreQuestion score:
                        writer.WriteStartArray("criteria");
                        foreach (var level in score.Criteria)
                        {
                            level.WriteTo(writer);
                        }

                        writer.WriteEndArray();
                        break;
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return output.ToArray();
    }

    internal static string TypeName(DecisionQuestion question) => question switch
    {
        YesNoQuestion => "noul", ChoiceQuestion => "choice", ScoreQuestion => "score",
        _ => throw new ArgumentException("Unknown question type.")
    };

    internal static DecisionResult ParseResponse(ReadOnlyMemory<byte> body, DecisionRequest request)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            CheckDuplicates(root);
            Object(root);
            var model = Text(root.GetProperty("model"));
            var answers = root.GetProperty("answers");
            Object(answers);
            var parsed = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
            foreach (var property in answers.EnumerateObject())
            {
                if (!request.Questions.TryGetValue(property.Name, out var question))
                {
                    throw new DecisionProtocolException("Unexpected answer ID.");
                }

                var answer = property.Value;
                Object(answer);
                if (Text(answer.GetProperty("type")) != TypeName(question))
                {
                    throw new DecisionProtocolException("Answer type mismatch.");
                }

                parsed.Add(property.Name, question switch
                {
                    YesNoQuestion => new YesNoAnswer(Number(answer.GetProperty("noul"))),
                    ChoiceQuestion choice => ParseChoice(answer, choice),
                    ScoreQuestion score => ParseScore(answer, score),
                    _ => throw new DecisionProtocolException("Unknown question type.")
                });
            }

            if (parsed.Count != request.Questions.Count)
            {
                throw new DecisionProtocolException("Requested answers are missing.");
            }

            var usage = root.GetProperty("usage");
            Object(usage);
            return new DecisionResult(model, parsed,
                new TokenUsage(usage.GetProperty("input_tokens").GetInt64(),
                    usage.GetProperty("output_tokens").GetInt64()));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
                                       or ArgumentException or FormatException or OverflowException)
        {
            throw new DecisionProtocolException("Invalid decision response contract.");
        }
    }

    private static ChoiceAnswer ParseChoice(JsonElement answer, ChoiceQuestion question)
    {
        var probabilities = Distribution(answer.GetProperty("probabilities"));
        if (probabilities.Count != question.Criteria.Count ||
            probabilities.Keys.Any(key => !question.Criteria.ContainsKey(key)))
        {
            throw new DecisionProtocolException("Choice distribution keys do not match options.");
        }

        return new ChoiceAnswer(Text(answer.GetProperty("choice")), probabilities,
            Number(answer.GetProperty("confidence")));
    }

    private static ScoreAnswer ParseScore(JsonElement answer, ScoreQuestion question)
    {
        var legend = answer.GetProperty("legend");
        Object(legend);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in legend.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new DecisionProtocolException("Invalid score legend.");
            }

            descriptions.Add(property.Name, property.Value.GetString()!);
        }

        if (descriptions.Count != question.Criteria.Count)
        {
            throw new DecisionProtocolException("Score level count mismatch.");
        }

        return new ScoreAnswer(Number(answer.GetProperty("score")), descriptions,
            Distribution(answer.GetProperty("probabilities")), Number(answer.GetProperty("confidence")));
    }

    private static Dictionary<string, double> Distribution(JsonElement value)
    {
        Object(value);
        return value.EnumerateObject().ToDictionary(p => p.Name, p => Number(p.Value), StringComparer.Ordinal);
    }

    private static void Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new DecisionProtocolException("Expected JSON object.");
        }
    }

    private static string Text(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new DecisionProtocolException("Expected nonblank string.");
        }

        return value.GetString()!;
    }

    private static double Number(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            throw new DecisionProtocolException("Expected finite number.");
        }

        return number;
    }

    private static void CheckDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                {
                    throw new DecisionProtocolException("Duplicate response JSON property.");
                }

                CheckDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                CheckDuplicates(item);
            }
        }
    }
}