using System.Collections.ObjectModel;
using System.Text.Json;
namespace DecisionSharp.Reranking;
public sealed class Evidence
{
    public string Id { get; }
    public string Content { get; }
    public IReadOnlyDictionary<string,JsonElement>? Metadata { get; }
    public double? RetrievalScore { get; }
    public Evidence(string id,string content,IReadOnlyDictionary<string,JsonElement>? metadata=null,double? retrievalScore=null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if(retrievalScore is {} score && !double.IsFinite(score))throw new ArgumentException("Retrieval score must be finite.",nameof(retrievalScore));
        Id=id;Content=content;RetrievalScore=retrievalScore;
        if(metadata is not null)Metadata=new ReadOnlyDictionary<string,JsonElement>(metadata.ToDictionary(p=>p.Key,p=>p.Value.Clone(),StringComparer.Ordinal));
    }
}
public sealed class ScoredEvidence
{
    public Evidence Evidence { get; }
    public double RelevanceProbability { get; }
    public string ResolvedModel { get; }
    internal ScoredEvidence(Evidence evidence,double relevanceProbability,string resolvedModel)
    {Evidence=evidence;RelevanceProbability=relevanceProbability;ResolvedModel=resolvedModel;}
}
