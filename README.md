# CebelcaAPISharp

A simple .NET client for the [Cebelca.biz](https://www.cebelca.biz/) (InvoiceFox) API.

It currently supports:

- creating invoice headers
- adding and updating invoice lines
- issuing invoices with or without fiscalization
- adding payments
- sending invoices by email
- downloading invoice PDFs
- reading partners, invoices, payments, and sales locations

The library is small and intentionally thin over the remote API. Error handling is limited, so treat responses from Cebelca as external-system failures and handle exceptions at the call site.

## Install

```bash
dotnet add package CebelcaAPISharp
```

## Quick Start

```csharp
using CebelcaAPI;

var apiKey = Environment.GetEnvironmentVariable("CEBELCA_API_KEY")
             ?? throw new InvalidOperationException("Missing CEBELCA_API_KEY.");

var client = new CebelcaAPISharp(apiKey);
```

If you already use `Microsoft.Extensions.Logging`, you can pass a logger:

```csharp
using CebelcaAPI;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var client = new CebelcaAPISharp(
    apiKey,
    loggerFactory.CreateLogger<CebelcaAPISharp>());
```

## Example: Create And Issue An Invoice

This is the most common flow:

1. create or resolve the partner
2. create the invoice header
3. add one or more invoice lines
4. issue the invoice

```csharp
using CebelcaAPI;

var apiKey = Environment.GetEnvironmentVariable("CEBELCA_API_KEY")
             ?? throw new InvalidOperationException("Missing CEBELCA_API_KEY.");

var client = new CebelcaAPISharp(apiKey);

var partnerId = await client.AddPartner(
    name: "Janez Novak",
    email: "janez@example.com",
    street: "Dunajska cesta 1",
    city: "1000 Ljubljana",
    postal: "1000");

var invoiceId = await client.AddInvoiceHead(
    partnerId: partnerId,
    idDocumentExt: "ORDER-2026-001",
    dateSent: DateTime.Today,
    dateServed: DateTime.Today,
    dateToPay: DateTime.Today.AddDays(8));

await client.AddInvoiceLine(
    invoiceId: invoiceId,
    title: "Psihoterapevtska ura",
    measuringUnit: "ura",
    qty: "1",
    price: 80m,
    vat: "0",
    discount: "0");

await client.AddInvoiceLine(
    invoiceId: invoiceId,
    title: "Priprava porocila",
    measuringUnit: "kos",
    qty: "1",
    price: 25m,
    vat: "0",
    discount: "0");

var invoiceNumber = await client.IssueInvoiceNoFiscalization(invoiceId);

Console.WriteLine($"Issued invoice number: {invoiceNumber}");
```

## Example: Issue With Fiscalization

When fiscalization is required, first fetch a sales location and then issue the invoice with operator details.

```csharp
using CebelcaAPI;

var client = new CebelcaAPISharp(apiKey);

var salesLocation = (await client.GetSalesLocations()).First();

var invoiceNumber = await client.IssueInvoiceFiscalization(
    invoiceId: invoiceId,
    idLocation: salesLocation.Id,
    opTaxId: "12345678",
    opName: "TerapEvt POS 1",
    invoiceNo: "",
    test_mode: true);

Console.WriteLine($"Fiscalized invoice number: {invoiceNumber}");
```

Notes:

- `test_mode: true` is useful while integrating.
- `idLocation` is the internal Cebelca sales-location ID returned by `GetSalesLocations()`.

## Example: Send Invoice By Email

```csharp
await client.SendInvoiceByEmail(
    invoiceId: invoiceId,
    to: "janez@example.com",
    subject: "Vas racun",
    content: "Pozdravljeni,\n\nv priponki vam posiljamo racun.\n");
```

## Example: Download The PDF

```csharp
var pdfBytes = await client.GetPDF(invoiceId);
await File.WriteAllBytesAsync("invoice.pdf", pdfBytes);
```

## Example: Add A Payment

```csharp
var paymentId = await client.AddPayment(
    invoiceId: invoiceId,
    dateOfPayment: DateTime.Today,
    amount: 105m,
    paymentMethod: "1");

Console.WriteLine($"Payment id: {paymentId}");
```

Afterward you can inspect recorded payments:

```csharp
var payments = await client.GetPayments(invoiceId);

foreach (var payment in payments)
{
    Console.WriteLine($"{payment.DateOfPayment:d}: {payment.Amount}");
}
```

## Example: Find Or Update A Partner

```csharp
var partners = await client.GetPartners();

var partner = partners.FirstOrDefault(x => x.Email == "janez@example.com");

if (partner is not null)
{
    await client.UpdatePartner(
        id: partner.Id,
        name: partner.Name,
        email: "racuni@example.com",
        street: partner.Street,
        city: partner.City,
        postal: partner.Postal,
        taxNo: partner.TaxNo ?? "");
}
```

## Example: Inspect Existing Invoices

```csharp
var invoices = await client.GetAllInvoices();

foreach (var invoice in invoices.Take(10))
{
    Console.WriteLine($"{invoice.title} | {invoice.amount} | partner={invoice.id_partner}");
}
```

Or fetch one invoice directly:

```csharp
var invoice = await client.GetInvoice(12345);
Console.WriteLine($"{invoice.title} due on {invoice.date_to_pay:d}");
```

## API Surface

Main methods exposed by `ICebelcaAPISharp`:

- `AddPartner`
- `UpdatePartner`
- `GetPartners`
- `AddInvoiceHead`
- `AddInvoiceLine`
- `GetInvoiceLines`
- `UpdateInvoiceLine`
- `IssueInvoiceNoFiscalization`
- `IssueInvoiceFiscalization`
- `AddPayment`
- `GetPayments`
- `GetInvoice`
- `GetAllInvoices`
- `GetSalesLocations`
- `SendInvoiceByEmail`
- `GetPDF`

## Notes

- The library currently creates its own `HttpClient` per request.
- Some numeric and date values are formatted with Slovenian culture conventions because the upstream API expects them.
- The package targets `netstandard2.0`.
