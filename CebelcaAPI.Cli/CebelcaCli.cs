using System.Globalization;
using System.Text.Json;
using CebelcaAPI;

public static class CebelcaCli
{
    public static readonly string[] Commands =
    [
        "get_partners", "get_sales_locations", "get_next_invoice_number", "get_all_invoices", "get_invoice",
        "get_invoice_lines", "get_payments", "get_invoice_pdf", "add_partner", "update_partner", "add_invoice_head",
        "add_invoice_line", "update_invoice_line", "issue_invoice_no_fiscalization", "issue_invoice_fiscalization",
        "add_payment", "send_invoice_by_email"
    ];

    public static async Task<object> Execute(ICebelcaAPISharp api, string command, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object) throw new ArgumentException("Arguments must be a JSON object.");

        return command switch
        {
            "get_partners" => await api.GetPartners(),
            "get_sales_locations" => await api.GetSalesLocations(),
            "get_next_invoice_number" => new { invoiceNumber = await api.GetNextInvoiceNo() },
            "get_all_invoices" => (await api.GetAllInvoices()).Select(WithFiscalizedTitle).ToArray(),
            "get_invoice" => WithFiscalizedTitle(await api.GetInvoice(Int(arguments, "invoiceId"))),
            "get_invoice_lines" => await api.GetInvoiceLines(Id(arguments, "invoiceId")),
            "get_payments" => await api.GetPayments(Id(arguments, "invoiceId")),
            "get_invoice_pdf" => new { mimeType = "application/pdf", base64 = Convert.ToBase64String(await api.GetPDF(Id(arguments, "invoiceId"))) },
            "add_partner" => new { id = await api.AddPartner(String(arguments, "name"), String(arguments, "email"), String(arguments, "street"), String(arguments, "city"), String(arguments, "postal")) },
            "update_partner" => await UpdatePartner(api, arguments),
            "add_invoice_head" => new { id = await api.AddInvoiceHead(Id(arguments, "partnerId"), String(arguments, "externalDocumentId"), Date(arguments, "dateSent"), Date(arguments, "dateServed"), Date(arguments, "dateToPay"), OptionalBool(arguments, "paid", false), OptionalString(arguments, "documentType", "0")) },
            "add_invoice_line" => new { id = await api.AddInvoiceLine(Id(arguments, "invoiceId"), String(arguments, "title"), String(arguments, "measuringUnit"), String(arguments, "quantity"), Decimal(arguments, "price"), String(arguments, "vat"), String(arguments, "discount")) },
            "update_invoice_line" => await UpdateInvoiceLine(api, arguments),
            "issue_invoice_no_fiscalization" => new { invoiceNumber = await api.IssueInvoiceNoFiscalization(Id(arguments, "invoiceId"), OptionalString(arguments, "number", ""), OptionalString(arguments, "documentType", "0")) },
            "issue_invoice_fiscalization" => new { invoiceNumber = await api.IssueInvoiceFiscalization(Id(arguments, "invoiceId"), Id(arguments, "locationId"), String(arguments, "operatorTaxId"), String(arguments, "operatorName"), OptionalString(arguments, "invoiceNumber", ""), OptionalBool(arguments, "testMode", false), OptionalString(arguments, "documentType", "0")) },
            "add_payment" => new { id = await api.AddPayment(Id(arguments, "invoiceId"), Date(arguments, "dateOfPayment"), Decimal(arguments, "amount"), String(arguments, "paymentMethodId")) },
            "send_invoice_by_email" => await SendInvoiceByEmail(api, arguments),
            _ => throw new ArgumentException($"Unknown command '{command}'. Use --help to list commands.")
        };
    }

    private static CebInvoice WithFiscalizedTitle(CebInvoice invoice)
    {
        if (invoice.fields.TryGetValue("fiscalized", out var fiscalized) && Convert.ToString(fiscalized) == "1" &&
            invoice.fields.TryGetValue("locreg", out var locreg) && !string.IsNullOrWhiteSpace(locreg?.ToString()) &&
            invoice.fields.TryGetValue("docnum", out var docnum) && long.TryParse(Convert.ToString(docnum), out var number))
            invoice.title = $"{locreg}-{number}";
        return invoice;
    }

    private static async Task<object> UpdatePartner(ICebelcaAPISharp api, JsonElement arguments)
    {
        await api.UpdatePartner(Id(arguments, "partnerId"), String(arguments, "name"), String(arguments, "email"), String(arguments, "street"), String(arguments, "city"), String(arguments, "postal"), OptionalString(arguments, "taxNo", ""));
        return new { ok = true };
    }

    private static async Task<object> UpdateInvoiceLine(ICebelcaAPISharp api, JsonElement arguments)
    {
        await api.UpdateInvoiceLine(Id(arguments, "lineId"), Id(arguments, "invoiceId"), String(arguments, "title"), String(arguments, "measuringUnit"), String(arguments, "quantity"), Decimal(arguments, "price"), String(arguments, "vat"), String(arguments, "discount"), OptionalString(arguments, "taxType", "EXM"), OptionalString(arguments, "konto", ""));
        return new { ok = true };
    }

    private static async Task<object> SendInvoiceByEmail(ICebelcaAPISharp api, JsonElement arguments)
    {
        await api.SendInvoiceByEmail(Id(arguments, "invoiceId"), String(arguments, "recipient"), String(arguments, "subject"), String(arguments, "content"));
        return new { ok = true };
    }

    private static string Id(JsonElement arguments, string name)
    {
        var value = String(arguments, name);
        if (value.Any(character => character is < '0' or > '9') || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException($"{name} must be a positive numeric Cebelca ID.");
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static int Int(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result) || result <= 0)
            throw new ArgumentException($"{name} must be a positive integer.");
        return result;
    }

    private static decimal Decimal(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || !value.TryGetDecimal(out var result))
            throw new ArgumentException($"{name} must be a JSON number.");
        return result;
    }

    private static DateTime Date(JsonElement arguments, string name)
    {
        if (!DateTime.TryParse(String(arguments, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
            throw new ArgumentException($"{name} must be an ISO 8601 date.");
        return result;
    }

    private static string String(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw new ArgumentException($"{name} must be a string.");
        return value.GetString()!;
    }

    private static string OptionalString(JsonElement arguments, string name, string defaultValue) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? String(arguments, name) : defaultValue;

    private static bool OptionalBool(JsonElement arguments, string name, bool defaultValue) =>
        arguments.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.True ? true : value.ValueKind == JsonValueKind.False ? false : throw new ArgumentException($"{name} must be a boolean.") : defaultValue;
}
