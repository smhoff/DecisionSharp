namespace DecisionSharp.Core.Questions;

using System.Text.Json;

/// <summary>
/// Represents a decision question where the user is tasked with scoring or ranking based on specific criteria.
/// This class enforces the criteria to have a minimum of 2 and a maximum of 10 levels or options.
/// </summary>
public sealed class ScoreQuestion : DecisionQuestion
{
    /// <summary>
    /// Gets a read-only collection of criteria associated with the score question.
    /// These criteria are represented as a collection of JSON elements and are used
    /// to define the scoring levels or evaluation criteria for the question. The
    /// collection must contain between 2 and 10 items, inclusive.
    /// </summary>
    public IReadOnlyList<JsonElement> Criteria { get; }

    /// <summary>
    /// Represents a scoring question that evaluates criteria with a numerical scoring mechanism.
    /// </summary>
    /// <remarks>
    /// The <see cref="ScoreQuestion"/> class is utilized to capture and process a set of criteria
    /// defined as JSON elements. The question includes a list of criteria between 2 and 10 items.
    /// This class inherits from <see cref="DecisionQuestion"/> and extends its functionality for
    /// scoring-based decision-making questions.
    /// </remarks>
    public ScoreQuestion(string instructions, IReadOnlyList<string> criteria) : this(InputJson.Text(instructions),
        ToJson(criteria))
    {
    }

    /// <summary>
    /// Converts a list of string criteria into an array of JSON elements.
    /// </summary>
    /// <param name="criteria">A read-only list of string criteria to be converted. Cannot be null.</param>
    /// <returns>An array of JSON elements representing the serialized criteria.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the criteria list is null.</exception>
    private static JsonElement[] ToJson(IReadOnlyList<string> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return criteria.Select(InputJson.Text).ToArray();
    }

    /// <summary>
    /// Represents a question that is scored based on multiple criteria.
    /// </summary>
    /// <remarks>
    /// This class inherits from the abstract <see cref="DecisionQuestion"/> class and is used
    /// to model decision-making scenarios where a score is determined using predefined criteria.
    /// </remarks>
    public ScoreQuestion(JsonElement instructions, IReadOnlyList<JsonElement> criteria) : base(instructions)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count is < 2 or > 10)
        {
            throw new ArgumentException("Score requires 2–10 levels.", nameof(criteria));
        }

        this.Criteria = Array.AsReadOnly(criteria.Select(value => InputJson.Own(value, nameof(criteria))).ToArray());
    }
}