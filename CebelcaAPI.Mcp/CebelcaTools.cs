using System.ComponentModel;
using CebelcaAPI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class CebelcaTools(ICebelcaAPISharp api)
{
    private static CebInvoice WithFiscalizedTitle(CebInvoice invoice)
    {
        if (invoice.fields.TryGetValue("fiscalized", out var fiscalized) && Convert.ToString(fiscalized) == "1" &&
            invoice.fields.TryGetValue("locreg", out var locreg) && !string.IsNullOrWhiteSpace(locreg?.ToString()) &&
            invoice.fields.TryGetValue("docnum", out var docnum) && long.TryParse(Convert.ToString(docnum), out var number))
            invoice.title = $"{locreg}-{number}";
        return invoice;
    }

    private static async Task<T> Safe<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException("Cebelca API request failed."); }
    }

    private static async Task Safe(Func<Task> call)
    {
        try { await call(); }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException("Cebelca API request failed."); }
    }

    private static string Id(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Any(c => c is < '0' or > '9') ||
            !long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException($"{name} must be a positive numeric Cebelca ID.");
        return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("List all Cebelca partners.")]
    public Task<IEnumerable<CebelcaPartner>> GetPartners() => Safe(api.GetPartners);

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("List sales locations used when fiscalizing invoices.")]
    public Task<IEnumerable<CebelcaSalesLocation>> GetSalesLocations() => Safe(api.GetSalesLocations);

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Get the next proposed invoice number for the current year.")]
    public Task<string> GetNextInvoiceNumber() => Safe(api.GetNextInvoiceNo);

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Get all invoices.")]
    public async Task<IEnumerable<CebInvoice>> GetAllInvoices() =>
        (await Safe(api.GetAllInvoices)).Select(WithFiscalizedTitle);

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Get one invoice by its positive numeric ID.")]
    public async Task<CebInvoice> GetInvoice([Description("Positive numeric invoice ID.")] int invoiceId)
    {
        if (invoiceId <= 0) throw new ArgumentOutOfRangeException(nameof(invoiceId), "Invoice ID must be positive.");
        var invoice = await Safe(() => api.GetInvoice(invoiceId));
        return WithFiscalizedTitle(invoice);
    }

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("List line items for an invoice.")]
    public Task<IEnumerable<CebelcaInvoiceLine>> GetInvoiceLines(string invoiceId) => Safe(() => api.GetInvoiceLines(Id(invoiceId, nameof(invoiceId))));

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("List payments recorded for an invoice.")]
    public Task<IEnumerable<CebelcaPayment>> GetPayments(string invoiceId) => Safe(() => api.GetPayments(Id(invoiceId, nameof(invoiceId))));

    [McpServerTool(ReadOnly = true, Idempotent = true), Description("Download an invoice PDF. Returns the PDF as an embedded application/pdf resource.")]
    public async Task<EmbeddedResourceBlock> GetInvoicePdf(string invoiceId)
    {
        var id = Id(invoiceId, nameof(invoiceId));
        var bytes = await Safe(() => api.GetPDF(id));
        return new EmbeddedResourceBlock { Resource = BlobResourceContents.FromBytes(bytes, $"cebelca://invoice/{id}.pdf", "application/pdf") };
    }

    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = true), Description("Create or find a partner by its email and address details.")]
    public Task<string> AddPartner(string name, string email, string street, string city, string postal) =>
        Safe(() => api.AddPartner(name, email, street, city, postal));

    [McpServerTool(ReadOnly = false, Destructive = true, Idempotent = true), Description("Update a partner's contact, address, and tax details.")]
    public Task UpdatePartner(string partnerId, string name, string email, string street, string city, string postal,
        [Description("Tax number; empty string clears the tax number.")] string taxNo = "") =>
        Safe(() => api.UpdatePartner(Id(partnerId, nameof(partnerId)), name, email, street, city, postal, taxNo));

    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = false), Description("Create a draft invoice header. Returns its invoice ID.")]
    public Task<string> AddInvoiceHead(string partnerId, string externalDocumentId,
        [Description("Invoice sent date, in ISO 8601 format.")] DateTime dateSent,
        [Description("Invoice service date, in ISO 8601 format.")] DateTime dateServed,
        [Description("Invoice due date, in ISO 8601 format.")] DateTime dateToPay, bool paid = false,
        [Description("Cebelca document type code; defaults to 0.")] string documentType = "0") =>
        Safe(() => api.AddInvoiceHead(Id(partnerId, nameof(partnerId)), externalDocumentId, dateSent, dateServed, dateToPay, paid, documentType));

    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = false), Description("Add a line item to a draft invoice. Returns the line ID.")]
    public Task<string> AddInvoiceLine(string invoiceId, string title, string measuringUnit,
        [Description("Quantity formatted as expected by Cebelca, for example 1 or 1,5.")] string quantity, decimal price,
        [Description("VAT rate/code as expected by the Cebelca API.")] string vat,
        [Description("Discount as expected by the Cebelca API, commonly a percent string.")] string discount) =>
        Safe(() => api.AddInvoiceLine(Id(invoiceId, nameof(invoiceId)), title, measuringUnit, quantity, price, vat, discount));

    [McpServerTool(ReadOnly = false, Destructive = true, Idempotent = true), Description("Update a line item on an invoice.")]
    public Task UpdateInvoiceLine(string lineId, string invoiceId, string title, string measuringUnit, string quantity,
        decimal price, string vat, string discount, string taxType = "EXM", string konto = "") =>
        Safe(() => api.UpdateInvoiceLine(Id(lineId, nameof(lineId)), Id(invoiceId, nameof(invoiceId)), title, measuringUnit, quantity, price, vat, discount, taxType, konto));

    [McpServerTool(ReadOnly = false, Destructive = true, Idempotent = false), Description("Issue an invoice without fiscalization. This changes the draft to an issued invoice. Returns the final invoice number.")]
    public Task<string> IssueInvoiceNoFiscalization(string invoiceId, string number = "", string documentType = "0") =>
        Safe(() => api.IssueInvoiceNoFiscalization(Id(invoiceId, nameof(invoiceId)), number, documentType));

    [McpServerTool(ReadOnly = false, Destructive = true, Idempotent = false), Description("Issue and fiscalize an invoice using the Cebelca sales-location ID. Fiscalization is an irreversible external action.")]
    public Task<string> IssueInvoiceFiscalization(string invoiceId, string locationId, string operatorTaxId, string operatorName,
        string invoiceNumber = "", bool testMode = false, string documentType = "0") =>
        Safe(() => api.IssueInvoiceFiscalization(Id(invoiceId, nameof(invoiceId)), Id(locationId, nameof(locationId)), operatorTaxId, operatorName, invoiceNumber, testMode, documentType));

    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = false), Description("Record a payment for an invoice. Returns the payment ID.")]
    public Task<string> AddPayment(string invoiceId,
        [Description("Payment date, in ISO 8601 format.")] DateTime dateOfPayment, decimal amount, string paymentMethodId) =>
        Safe(() => api.AddPayment(Id(invoiceId, nameof(invoiceId)), dateOfPayment, amount, paymentMethodId));

    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = false), Description("Email an invoice PDF to a recipient.")]
    public Task SendInvoiceByEmail(string invoiceId, string recipient, string subject, string content) =>
        Safe(() => api.SendInvoiceByEmail(Id(invoiceId, nameof(invoiceId)), recipient, subject, content));
}
