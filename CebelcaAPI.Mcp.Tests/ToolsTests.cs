using System.Reflection;
using System.Text.Json;
using CebelcaAPI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using System.IO.Pipelines;
using Xunit;

public class ToolsTests
{
    [Fact]
    public void CebInvoicePreservesUnknownCebelcaFields()
    {
        var invoice = CebInvoice.FromCebelcaJson(JToken.Parse("{\"id\":\"42\",\"title\":\"26-0042\",\"payment\":\"paid\",\"eor\":\"ABC123\",\"metadata\":{\"source\":\"api\"}}"));

        Assert.Equal("42", invoice.id);
        Assert.Equal("paid", invoice.fields["payment"]);
        Assert.Equal("ABC123", invoice.fields["eor"]);
        var serialized = JsonSerializer.Serialize(invoice);
        Assert.Contains("fields", serialized);
        Assert.Contains("paid", serialized);
        Assert.Contains("metadata", serialized);
    }

    [Fact]
    public async Task ToolsForwardValuesValidateIdsAndReturnPdfBytes()
    {
        var proxy = DispatchProxy.Create<ICebelcaAPISharp, FakeApi>();
        var fake = (FakeApi)(object)proxy;
        var tools = new CebelcaTools(proxy);
        fake.Result = "payment-7";
        var date = new DateTime(2026, 9, 20);

        var result = await tools.AddPayment("42", date, 12.5m, "1");

        Assert.Equal("payment-7", result);
        Assert.Equal("AddPayment", fake.Method);
        Assert.Equal(new object?[] { "42", date, 12.5m, "1" }, fake.Arguments);
        await Assert.ThrowsAsync<ArgumentException>(() => tools.GetInvoicePdf("invoice-42"));
        Assert.Equal("AddPayment", fake.Method); // Invalid PDF ID did not reach the API.

        fake.Result = new byte[] { 1, 2, 3 };
        var pdf = await tools.GetInvoicePdf("00042");
        var blob = Assert.IsType<BlobResourceContents>(pdf.Resource);
        Assert.Equal("application/pdf", blob.MimeType);
        Assert.Equal(new byte[] { 1, 2, 3 }, blob.DecodedData.ToArray());
        Assert.Equal("42", fake.Arguments![0]);
    }

    [Fact]
    public async Task FiscalizationPreservesFalseDefaultAndExplicitTrue()
    {
        var proxy = DispatchProxy.Create<ICebelcaAPISharp, FakeApi>();
        var fake = (FakeApi)(object)proxy;
        var tools = new CebelcaTools(proxy);
        var defaults = typeof(CebelcaTools).GetMethod(nameof(CebelcaTools.IssueInvoiceFiscalization))!;
        var defaultTestMode = (bool)defaults.GetParameters()[5].DefaultValue!;
        Assert.False(defaultTestMode);

        await tools.IssueInvoiceFiscalization("10", "2", "12345678", "Operator");
        Assert.Equal(false, fake.Arguments![5]);
        await tools.IssueInvoiceFiscalization("10", "2", "12345678", "Operator", "A-10", true, "3");
        Assert.Equal(new object?[] { "10", "2", "12345678", "Operator", "A-10", true, "3" }, fake.Arguments);
    }

    [Fact]
    public async Task GetAllInvoicesBuildsFiscalizedTitles()
    {
        var proxy = DispatchProxy.Create<ICebelcaAPISharp, FakeApi>();
        ((FakeApi)(object)proxy).Result = new[]
        {
            new CebInvoice { title = "", fields = new Dictionary<string, object> { ["fiscalized"] = 1, ["locreg"] = "E1-E1", ["docnum"] = 12 } },
            new CebInvoice { title = "26-0013" }
        };

        var invoices = (await new CebelcaTools(proxy).GetAllInvoices()).ToArray();

        Assert.Equal("E1-E1-12", invoices[0].title);
        Assert.Equal("26-0013", invoices[1].title);
    }

    [Fact]
    public async Task SdkToolCallReturnsSanitizedFailureText()
    {
        var proxy = DispatchProxy.Create<ICebelcaAPISharp, FakeApi>();
        ((FakeApi)(object)proxy).Error = "raw upstream response: customer@example.com and private payload";
        var services = new ServiceCollection().BuildServiceProvider();
        var tool = McpServerTool.Create(typeof(CebelcaTools).GetMethod(nameof(CebelcaTools.GetPartners))!, new CebelcaTools(proxy));
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.Add(tool);
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var server = McpServer.Create(
            new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), "test"),
            new McpServerOptions { ToolCollection = collection }, null, services);
        var serverRun = server.RunAsync();
        var client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "get_partners");
        var result = await client.CallToolAsync("get_partners", new Dictionary<string, object?>());

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain("customer@example.com", text);
        Assert.DoesNotContain("private payload", text);
        Assert.Equal("An error occurred invoking 'get_partners'.", text);
        await client.DisposeAsync();
        await server.DisposeAsync();
        try { await serverRun; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task SdkGetInvoiceReturnsAdditionalCebelcaFields()
    {
        var proxy = DispatchProxy.Create<ICebelcaAPISharp, FakeApi>();
        ((FakeApi)(object)proxy).Result = new CebInvoice
        {
            id = "42",
            title = "26-0042",
            fields = new Dictionary<string, object>
            {
                ["payment"] = "paid",
                ["metadata"] = new Dictionary<string, object> { ["source"] = "Cebelca" },
                ["fiscalized"] = 1,
                ["locreg"] = "E1-E1",
                ["docnum"] = 12
            }
        };
        var services = new ServiceCollection().BuildServiceProvider();
        var tool = McpServerTool.Create(typeof(CebelcaTools).GetMethod(nameof(CebelcaTools.GetInvoice))!, new CebelcaTools(proxy));
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.Add(tool);
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var server = McpServer.Create(
            new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), "test"),
            new McpServerOptions { ToolCollection = collection }, null, services);
        var serverRun = server.RunAsync();
        var client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));

        var result = await client.CallToolAsync("get_invoice", new Dictionary<string, object?> { ["invoiceId"] = 42 });

        Assert.Null(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("fields", text);
        Assert.Contains("payment", text);
        Assert.Contains("metadata", text);
        Assert.Contains("E1-E1-12", text);
        await client.DisposeAsync();
        await server.DisposeAsync();
        try { await serverRun; } catch (OperationCanceledException) { }
    }

    [Fact]
    public void ReadToolsAdvertiseReadOnlyAndWritesAreNotReadOnly()
    {
        var read = typeof(CebelcaTools).GetMethod(nameof(CebelcaTools.GetPartners))!;
        var write = typeof(CebelcaTools).GetMethod(nameof(CebelcaTools.IssueInvoiceNoFiscalization))!;
        Assert.True(read.GetCustomAttribute<McpServerToolAttribute>()!.ReadOnly);
        Assert.False(write.GetCustomAttribute<McpServerToolAttribute>()!.ReadOnly);
        Assert.True(write.GetCustomAttribute<McpServerToolAttribute>()!.Destructive);
        Assert.False(write.GetCustomAttribute<McpServerToolAttribute>()!.Idempotent);
    }

    public class FakeApi : DispatchProxy
    {
        public string? Method { get; private set; }
        public object?[]? Arguments { get; private set; }
        public object? Result { get; set; }
        public string? Error { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Method = targetMethod!.Name;
            Arguments = args;
            if (Error is not null) throw new Exception(Error);
            if (targetMethod.ReturnType == typeof(Task<string>)) return Task.FromResult((string)(Result ?? "ok"));
            if (targetMethod.ReturnType == typeof(Task<byte[]>)) return Task.FromResult((byte[])(Result ?? Array.Empty<byte>()));
            if (targetMethod.ReturnType == typeof(Task<CebInvoice>)) return Task.FromResult((CebInvoice)(Result ?? new CebInvoice()));
            if (targetMethod.ReturnType == typeof(Task<IEnumerable<CebInvoice>>)) return Task.FromResult((IEnumerable<CebInvoice>)(Result ?? Array.Empty<CebInvoice>()));
            if (targetMethod.ReturnType == typeof(Task<IEnumerable<CebelcaPartner>>)) return Task.FromResult((IEnumerable<CebelcaPartner>)(Result ?? Array.Empty<CebelcaPartner>()));
            if (targetMethod.ReturnType == typeof(Task)) return Task.CompletedTask;
            throw new NotSupportedException(targetMethod.Name);
        }
    }
}
