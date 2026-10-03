using System.Runtime.ExceptionServices;
using System.Text.Json;
using DecisionSharp.Core;
namespace DecisionSharp.Reranking;
public sealed class EvidenceReranker
{
    private readonly IDecisionEngine engine;
    private readonly RerankingOptions options;
    public EvidenceReranker(IDecisionEngine engine,RerankingOptions? options=null)
    {ArgumentNullException.ThrowIfNull(engine);this.engine=engine;this.options=(options??new()).Snapshot();}
    public async Task<IReadOnlyList<ScoredEvidence>> RerankAsync(string query,IReadOnlyList<Evidence> evidence,CancellationToken cancellationToken=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);ArgumentNullException.ThrowIfNull(evidence);cancellationToken.ThrowIfCancellationRequested();
        var candidates=evidence.ToArray();var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var candidate in candidates){ArgumentNullException.ThrowIfNull(candidate);if(!ids.Add(candidate.Id))throw new ArgumentException("Duplicate evidence ID.",nameof(evidence));}
        if(candidates.Length==0)return Array.Empty<ScoredEvidence>();
        var results=new ScoredEvidence[candidates.Length];using var stop=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);ExceptionDispatchInfo? firstFailure=null;
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0,candidates.Length),new ParallelOptions{MaxDegreeOfParallelism=options.Concurrency,CancellationToken=stop.Token},async(index,token)=>
            {
                try
                {
                    token.ThrowIfCancellationRequested();var candidate=candidates[index];
                    var request=new DecisionRequest(JsonSerializer.SerializeToElement(new{query,content=candidate.Content}),new Dictionary<string,DecisionQuestion>{["relevance"]=new YesNoQuestion("Does the content supply relevant evidence for the query?")});
                    var result=await engine.EvaluateAsync(request,token).ConfigureAwait(false);
                    results[index]=new ScoredEvidence(candidate,result.GetAnswer<YesNoAnswer>("relevance").ProbabilityOfYes,result.Model);
                }
                catch(Exception ex)
                {
                    if(!(ex is OperationCanceledException && stop.IsCancellationRequested))
                    {Interlocked.CompareExchange(ref firstFailure,ExceptionDispatchInfo.Capture(ex),null);stop.Cancel();}
                    throw;
                }
            }).ConfigureAwait(false);
        }
        catch
        {cancellationToken.ThrowIfCancellationRequested();firstFailure?.Throw();throw;}
        cancellationToken.ThrowIfCancellationRequested();
        var ordered=results.Select((item,index)=>(item,index)).OrderByDescending(pair=>pair.item.RelevanceProbability).ThenBy(pair=>pair.index).Select(pair=>pair.item);
        IEnumerable<ScoredEvidence> filtered=options.Threshold is {} threshold?ordered.Where(item=>item.RelevanceProbability>=threshold):ordered;
        if(options.TopK is {} count)filtered=filtered.Take(count);
        return Array.AsReadOnly(filtered.ToArray());
    }
}
