using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using CebelcaAPI;

var transport = "stdio";
var url = "http://127.0.0.1:3001";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--transport" && i + 1 < args.Length) transport = args[++i];
    else if (args[i] == "--url" && i + 1 < args.Length) url = args[++i];
    else if (args[i] is "--help" or "-h")
    {
        Console.Error.WriteLine("Usage: CebelcaAPI.Mcp [--transport stdio|http] [--url http://127.0.0.1:3001]");
        return;
    }
    else throw new ArgumentException($"Unknown argument: {args[i]}");
}

var apiKey = Environment.GetEnvironmentVariable("CEBELCA_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("CEBELCA_API_KEY is required.");

if (transport == "stdio")
{
    var builder = Host.CreateApplicationBuilder(args: []);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton<ICebelcaAPISharp>(_ => new CebelcaAPISharp(apiKey));
    builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<CebelcaTools>();
    await builder.Build().RunAsync();
}
else if (transport == "http")
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttp ||
        endpoint.Port < 1 || endpoint.Port > 65535 ||
        (!(IPAddress.TryParse(endpoint.Host, out var address) && IPAddress.IsLoopback(address)) &&
         !string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
        throw new ArgumentException("HTTP URL must use http and a loopback host, such as http://127.0.0.1:3001.");
    var token = Environment.GetEnvironmentVariable("CEBELCA_MCP_TOKEN");
    if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("CEBELCA_MCP_TOKEN is required for HTTP transport.");

    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls(url);
    builder.Services.AddSingleton<ICebelcaAPISharp>(_ => new CebelcaAPISharp(apiKey));
    builder.Services.AddMcpServer()
        .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
        .WithTools<CebelcaTools>();
    var app = builder.Build();
    app.Use(async (context, next) =>
    {
        if (!ValidHost(context.Request.Host.Host) || !ValidOrigin(context.Request.Headers.Origin, context.Request.Host))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        var authorization = context.Request.Headers.Authorization.ToString();
        var supplied = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : "";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(token)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next();
    });
    app.MapMcp("/mcp");
    await app.RunAsync(url);
}
else throw new ArgumentException("--transport must be stdio or http.");

static bool ValidHost(string host) =>
    string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
    IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);

static bool ValidOrigin(Microsoft.Extensions.Primitives.StringValues originHeader, HostString requestHost)
{
    if (Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(originHeader)) return true;
    if (originHeader.Count != 1 || !Uri.TryCreate(originHeader[0], UriKind.Absolute, out var origin) ||
        !ValidHost(origin.Host) || !string.Equals(origin.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase)) return false;
    return origin.Port == (requestHost.Port ?? 80) && origin.Scheme == Uri.UriSchemeHttp;
}
