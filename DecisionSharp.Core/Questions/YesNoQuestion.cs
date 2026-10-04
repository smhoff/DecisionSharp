namespace DecisionSharp.Core.Questions;

using System.Text.Json;

/// <summary>
/// Represents a question requiring a binary "yes" or "no" response.
/// </summary>
/// <remarks>
/// The <see cref="YesNoQuestion"/> class inherits from <see cref="DecisionQuestion"/> and
/// allows optional criteria for "yes" and "no" responses to be specified.
/// The criteria must be a JSON object with keys named "true" or "false".
/// </remarks>
public sealed class YesNoQuestion : DecisionQuestion
{
    /// <summary>
    /// Represents optional, structured criteria associated with a yes/no question.
    /// This property contains a JSON object that defines the conditions or additional
    /// context used to evaluate the question. The keys and values within the JSON structure
    /// provide detailed information for decision-making related to the question.
    /// </summary>
    public JsonElement? Criteria { get; }

    /// <summary>
    /// Represents a specific type of decision question that provides a "yes" or "no" answer
    /// with optional criteria for evaluating answers.
    /// </summary>
    public YesNoQuestion(string instructions) : this(InputJson.Text(instructions))
    {
    }

    /// <summary>
    /// Represents a yes/no decision question that can be evaluated within a decision-making process.
    /// </summary>
    public YesNoQuestion(JsonElement instructions, JsonElement? criteria = null) : base(instructions)
    {
        if (criteria is not { } value)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Yes/no criteria must be an object.", nameof(criteria));
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is not ("true" or "false"))
            {
                throw new ArgumentException("Yes/no criteria keys must be true or false.", nameof(criteria));
            }

            InputJson.Own(property.Value, nameof(criteria));
        }

        this.Criteria = InputJson.Own(value, nameof(criteria));
    }

    /// <summary>
    /// Creates a new instance of the <see cref="YesNoQuestion"/> class with specified instructions and criteria.
    /// </summary>
    /// <param name="instructions">The instructional text for the decision question.</param>
    /// <param name="yes">The criteria associated with a "yes" decision.</param>
    /// <param name="no">The criteria associated with a "no" decision.</param>
    /// <returns>A new instance of the <see cref="YesNoQuestion"/> class with the provided criteria.</returns>
    public static YesNoQuestion WithCriteria(string instructions, string yes, string no) => new(
        InputJson.Text(instructions),
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["true"] = yes, ["false"] = no }));
}