namespace DecisionSharp.Core;

/// <summary>
/// Represents the usage of tokens in a process, segmented into input and output tokens.
/// </summary>
public sealed class TokenUsage
{
    /// <summary>
    /// Represents the number of tokens consumed as input during a decision-making or evaluation process.
    /// This property indicates the count of tokens that were analyzed or processed in the input phase.
    /// </summary>
    public long InputTokens { get; }

    /// <summary>
    /// Gets the number of tokens included in the output generated during a processing operation.
    /// </summary>
    /// <remarks>
    /// This property represents the number of tokens consumed when generating the result or response.
    /// It is used for tracking and analytics purposes, particularly in scenarios where token-based
    /// pricing models or limits are applied.
    /// </remarks>
    public long OutputTokens { get; }

    /// <summary>
    /// Represents the token usage information for input and output operations.
    /// </summary>
    /// <remarks>
    /// The <see cref="TokenUsage"/> class is used to track the number of tokens consumed
    /// during input and output processes. Both input and output token counts must be non-negative.
    /// </remarks>
    public TokenUsage(long inputTokens, long outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputTokens));
        }

        this.InputTokens = inputTokens;
        this.OutputTokens = outputTokens;
    }
}