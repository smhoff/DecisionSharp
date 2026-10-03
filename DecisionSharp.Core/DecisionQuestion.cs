using System.Collections.ObjectModel;
using System.Text.Json;
namespace DecisionSharp.Core;
public abstract class DecisionQuestion
{
    public JsonElement Instructions { get; }
    private protected DecisionQuestion(JsonElement instructions) => Instructions = InputJson.Own(instructions,nameof(instructions));
}
public sealed class YesNoQuestion : DecisionQuestion
{
    public JsonElement? Criteria { get; }
    public YesNoQuestion(string instructions) : this(InputJson.Text(instructions)) { }
    public YesNoQuestion(JsonElement instructions, JsonElement? criteria = null) : base(instructions)
    {
        if (criteria is not { } value) return;
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Yes/no criteria must be an object.",nameof(criteria));
        foreach (var property in value.EnumerateObject()) { if (property.Name is not ("true" or "false")) throw new ArgumentException("Yes/no criteria keys must be true or false.",nameof(criteria)); InputJson.Own(property.Value,nameof(criteria)); }
        Criteria = InputJson.Own(value,nameof(criteria));
    }
    public static YesNoQuestion WithCriteria(string instructions, string yes, string no) => new(InputJson.Text(instructions), JsonSerializer.SerializeToElement(new Dictionary<string,string>{["true"]=yes,["false"]=no}));
}
public sealed class ChoiceQuestion : DecisionQuestion
{
    public IReadOnlyDictionary<string,JsonElement?> Criteria { get; }
    public ChoiceQuestion(string instructions, IReadOnlyDictionary<string,string?> criteria) : this(InputJson.Text(instructions), ToJson(criteria)) { }
    private static Dictionary<string,JsonElement?> ToJson(IReadOnlyDictionary<string,string?> criteria)
    { ArgumentNullException.ThrowIfNull(criteria); return criteria.ToDictionary(p=>p.Key,p=>p.Value is null ? (JsonElement?)null : InputJson.Text(p.Value),StringComparer.Ordinal); }
    public ChoiceQuestion(JsonElement instructions, IReadOnlyDictionary<string,JsonElement?> criteria) : base(instructions)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count is <2 or >255) throw new ArgumentException("Choice requires 2–255 options.",nameof(criteria));
        var copy = new Dictionary<string,JsonElement?>(StringComparer.Ordinal);
        foreach(var pair in criteria) { ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key); copy.Add(pair.Key, pair.Value is { ValueKind: not JsonValueKind.Null } value ? InputJson.Own(value,nameof(criteria)) : null); }
        Criteria = new ReadOnlyDictionary<string,JsonElement?>(copy);
    }
}
public sealed class ScoreQuestion : DecisionQuestion
{
    public IReadOnlyList<JsonElement> Criteria { get; }
    public ScoreQuestion(string instructions, IReadOnlyList<string> criteria) : this(InputJson.Text(instructions), ToJson(criteria)) { }
    private static JsonElement[] ToJson(IReadOnlyList<string> criteria) { ArgumentNullException.ThrowIfNull(criteria); return criteria.Select(InputJson.Text).ToArray(); }
    public ScoreQuestion(JsonElement instructions, IReadOnlyList<JsonElement> criteria) : base(instructions)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if(criteria.Count is <2 or >10) throw new ArgumentException("Score requires 2–10 levels.",nameof(criteria));
        Criteria = Array.AsReadOnly(criteria.Select(value=>InputJson.Own(value,nameof(criteria))).ToArray());
    }
}
