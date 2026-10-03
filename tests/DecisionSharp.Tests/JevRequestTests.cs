using System.Net;
using System.Text.Json.Nodes;
using DecisionSharp.Core;
using DecisionSharp.Jev;
namespace DecisionSharp.Tests;
public class JevRequestTests
{
    [Fact] public async Task SendsExactContractAndPerRequestAuthentication()
    {
        var handler=new StubHttpHandler(async (request,token)=>{
            Assert.Equal(HttpMethod.Post,request.Method);
            Assert.Equal("https://example.test/prefix/v1/systemone",request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer secret",request.Headers.Authorization!.ToString());
            Assert.Equal("application/json",request.Content!.Headers.ContentType!.MediaType);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(TestData.Fixture("request.json")),JsonNode.Parse(await request.Content.ReadAsStringAsync(token))));
            return TestData.Response();
        });
        using var client=new HttpClient(handler);client.DefaultRequestHeaders.Add("X-Shared","original");
        var result=await new JevDecisionEngine(client,TestData.Options()).EvaluateAsync(TestData.Request());
        Assert.Equal("resolved-v1",result.Model);Assert.Equal(1,handler.Calls);
        Assert.Null(client.DefaultRequestHeaders.Authorization);Assert.Equal("original",client.DefaultRequestHeaders.GetValues("X-Shared").Single());
    }
    [Fact] public async Task ExplicitModelAndLocalHttp()
    {
        var handler=new StubHttpHandler(async (r,t)=>{Assert.Null(r.Headers.Authorization);Assert.Equal("override",JsonNode.Parse(await r.Content!.ReadAsStringAsync(t))!["model"]!.GetValue<string>());return TestData.Response();});
        var options=TestData.Options();options.BaseUri=new Uri("http://127.0.0.1:8017/");options.AllowInsecureLocalEndpoint=true;options.ApiKey=null;
        await TestData.Engine(handler,options).EvaluateAsync(TestData.Request("override"));Assert.Equal(1,handler.Calls);
    }
    [Theory] [InlineData("https://example.test/?q=1")] [InlineData("https://example.test/#x")] [InlineData("http://example.test/")] [InlineData("ftp://example.test/")] [InlineData("relative")]
    public void InvalidEndpointsFailBeforeCalls(string uri)
    { var options=TestData.Options();options.BaseUri=new Uri(uri,UriKind.RelativeOrAbsolute);Assert.ThrowsAny<ArgumentException>(()=>TestData.Engine(new StubHttpHandler((_,_)=>Task.FromResult(TestData.Response())),options)); }
    [Fact] public void HostedKeyAndPositiveLimitsRequired()
    {
        var handler=new StubHttpHandler((_,_)=>Task.FromResult(TestData.Response()));
        var o=TestData.Options();o.ApiKey=null;Assert.ThrowsAny<ArgumentException>(()=>TestData.Engine(handler,o));
        o=TestData.Options();o.ResponseByteLimit=0;Assert.ThrowsAny<ArgumentException>(()=>TestData.Engine(handler,o));
        o=TestData.Options();o.TotalTimeout=TimeSpan.Zero;Assert.ThrowsAny<ArgumentException>(()=>TestData.Engine(handler,o));
        o=TestData.Options();o.DefaultModel=" ";Assert.ThrowsAny<ArgumentException>(()=>TestData.Engine(handler,o));Assert.Equal(0,handler.Calls);
    }
    [Fact] public async Task OptionsAreSnapshottedAndGenericStateIsOwned()
    {
        var handler=new StubHttpHandler(async(r,t)=>{Assert.Equal("default-model",JsonNode.Parse(await r.Content!.ReadAsStringAsync(t))!["model"]!.GetValue<string>());return TestData.Response(TestData.SingleResponse);});
        var options=TestData.Options();IDecisionEngine engine=TestData.Engine(handler,options);options.DefaultModel="changed";
        var result=await engine.EvaluateAsync(new{value="owned"},new Dictionary<string,DecisionQuestion>{["q"]=new YesNoQuestion("Relevant?")});Assert.Equal(0.7,result.GetAnswer<YesNoAnswer>("q").ProbabilityOfYes);
    }
}
