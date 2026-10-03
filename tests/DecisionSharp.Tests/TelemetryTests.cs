using System.Diagnostics;
using System.Diagnostics.Metrics;
namespace DecisionSharp.Tests;
public class TelemetryTests
{
    [Fact] public async Task EmitsSafeBoundedTagsAndResolvedModelOnTrace()
    {
        var tags=new List<KeyValuePair<string,object?>>();var names=new HashSet<string>();Activity? stopped=null;
        using var listener=new MeterListener();listener.InstrumentPublished=(instrument,l)=>{if(instrument.Meter.Name=="DecisionSharp")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((i,v,t,s)=>{names.Add(i.Name);tags.AddRange(t.ToArray());});listener.SetMeasurementEventCallback<double>((i,v,t,s)=>{names.Add(i.Name);tags.AddRange(t.ToArray());});listener.Start();
        using var activities=new ActivityListener{ShouldListenTo=s=>s.Name=="DecisionSharp",Sample=(ref ActivityCreationOptions<ActivityContext> _)=>ActivitySamplingResult.AllData,ActivityStopped=a=>stopped=a};ActivitySource.AddActivityListener(activities);
        await TestData.Engine(new StubHttpHandler((_,_)=>Task.FromResult(TestData.Response()))).EvaluateAsync(TestData.Request());
        Assert.Contains("decision.evaluations",names);Assert.Contains("decision.duration",names);Assert.Equal("resolved-v1",stopped!.GetTagItem("decision.model"));
        Assert.All(tags,t=>Assert.Contains(t.Key,new[]{"provider","outcome","question.type"}));Assert.DoesNotContain(tags,t=>t.Value?.ToString()=="secret");
    }
}
