using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace DecisionSharp.Tests;

public class SampleTests
{
    private sealed class SampleCopy : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "DecisionSharp-sample-" + Guid.NewGuid().ToString("N"));
        public SampleCopy()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
            Directory.CreateDirectory(DirectoryPath);
            foreach (var file in Directory.GetFiles(Path.Combine(root, "samples", "DecisionSharp.Console", "bin", configuration, "net8.0")))
            {
                if (Path.GetFileName(file) != "appsettings.json")
                {
                    File.Copy(file, Path.Combine(DirectoryPath, Path.GetFileName(file)));
                }
            }
        }
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
    private static ProcessStartInfo StartInfo(string directory, params string[] args)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(Path.Combine(directory, "DecisionSharp.Console.dll"));
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        foreach (var name in new[] { "DECISIONSHARP_BASE_URI", "DECISIONSHARP_MODEL", "DECISIONSHARP_API_KEY", "DECISIONSHARP_LOCAL" })
        {
            info.Environment.Remove(name);
        }

        return info;
    }
    [Fact]
    public async Task MissingConfigurationFailsClearlyWithoutSecrets()
    {
        using var copy = new SampleCopy();
        using var process = Process.Start(StartInfo(copy.DirectoryPath))!; var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Assert.NotEqual(0, process.ExitCode); Assert.Contains("DECISIONSHARP_BASE_URI", await error);
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ConsoleRunsPrimitivesAndRetrievedEvidenceAdapter(bool smoke, bool jsonSettings)
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start(); var calls = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var context = await listener.GetContextAsync(); Interlocked.Increment(ref calls);
                    using var reader = new StreamReader(context.Request.InputStream); using var body = JsonDocument.Parse(await reader.ReadToEndAsync()); var request = body.RootElement;
                    Assert.Equal("/prefix/v1/systemone", context.Request.Url!.AbsolutePath); Assert.Null(context.Request.Headers["Authorization"]);
                    string response;
                    if (smoke) { Assert.Equal(3, request.GetProperty("questions").EnumerateObject().Count()); response = TestData.Fixture("response.json"); }
                    else { Assert.Equal("How are failed invoices handled?", request.GetProperty("state").GetProperty("query").GetString()); var p = request.GetProperty("state").GetProperty("content").GetString()!.Contains("invoices", StringComparison.Ordinal) ? "0.9" : "0.1"; response = "{\"model\":\"resolved-v1\",\"answers\":{\"relevance\":{\"type\":\"noul\",\"noul\":" + p + "}},\"usage\":{\"input_tokens\":1,\"output_tokens\":0}}"; }
                    var bytes = Encoding.UTF8.GetBytes(response); context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
                }
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        });
        using var copy = new SampleCopy();
        var info = StartInfo(copy.DirectoryPath, smoke ? new[] { "--smoke" } : Array.Empty<string>()); info.Environment["DECISIONSHARP_BASE_URI"] = $"http://127.0.0.1:{port}/prefix"; info.Environment["DECISIONSHARP_MODEL"] = "default-model"; info.Environment["DECISIONSHARP_LOCAL"] = "true";
        if (jsonSettings)
        {
            File.WriteAllText(Path.Combine(copy.DirectoryPath, "appsettings.json"), JsonSerializer.Serialize(new { DecisionSharp = new { BaseUri = $"http://127.0.0.1:{port}/prefix", Model = "default-model", ApiKey = "", AllowInsecureLocalEndpoint = true } }));
            foreach (var name in new[] { "DECISIONSHARP_BASE_URI", "DECISIONSHARP_MODEL", "DECISIONSHARP_API_KEY", "DECISIONSHARP_LOCAL" })
            {
                info.Environment.Remove(name);
            }
        }
        else
        {
            // Environment settings must override a conflicting JSON provider configuration.
            File.WriteAllText(Path.Combine(copy.DirectoryPath, "appsettings.json"), "{\"DecisionSharp\":{\"BaseUri\":\"https://unused.invalid/\",\"Model\":\"unused\",\"ApiKey\":\"synthetic-unused\",\"AllowInsecureLocalEndpoint\":false}}");
            info.Environment["DECISIONSHARP_API_KEY"] = "";
        }
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            listener.Close(); await server;
        }
        Assert.True(process.ExitCode == 0, await error); var text = await output;
        if (smoke) { Assert.Contains("yes=0.900", text); Assert.Contains("choice=billing", text); Assert.Contains("score=0.750", text); Assert.Equal(1, calls); }
        else { var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries); Assert.StartsWith("billing\trelevance=0.900", lines[0]); Assert.StartsWith("weather\trelevance=0.100", lines[1]); Assert.Equal(2, calls); }
    }
}
