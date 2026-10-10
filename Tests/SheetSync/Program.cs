using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using IntegrationLab;
class Program
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); }
    static async Task Main()
    {
        var diff = SheetDiff.Compare("NodeCost", "NodeId,Rank,Cost\na,1,100", "NodeId,Rank,Cost\na,1,120");
        Check(diff.Error == null && diff.Changed.Count == 1, "CSV keyed diff");
        Check(SheetDiff.Compare("NodeCost", "NodeId,Rank,Cost\na,1,100", "NodeId,Rank,Cost\na,1,120\na,1,200").Error != null, "duplicate keys rejected");
        Check(Csv.Parse("a,b\r\n\"x,y\",\"two\nlines\"")[1][1] == "two\nlines", "quoted CSV");
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        using var server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start();
        var serving = Task.Run(async () => {
            var request = await server.GetContextAsync();
            Check(request.Request.HttpMethod == "POST", "POST request");
            using var reader = new System.IO.StreamReader(request.Request.InputStream);
            var payload = (JsonObject)PlaytestJson.Parse(await reader.ReadToEndAsync());
            Check(payload.Text("token") == "test-only", "token body");
            request.Response.StatusCode = 302; request.Response.RedirectLocation = "/result"; request.Response.Close();
            request = await server.GetContextAsync(); Check(request.Request.HttpMethod == "GET", "redirect GET");
            var body = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}");
            request.Response.OutputStream.Write(body); request.Response.Close();
        });
        var reply = await new SheetClient(new SheetSyncConfig { Endpoint = $"http://127.0.0.1:{port}/", Token = "test-only" }).Ping();
        await serving; Check(reply.Ok, "HTTP result");
        Console.WriteLine("PASS: CSV diff/duplicates/quotes, POST token and 302→GET");
    }
}
