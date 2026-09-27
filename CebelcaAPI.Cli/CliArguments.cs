using System.Globalization;
using System.Text.Json;

public static class CliArguments
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["invoice"] = "get_invoice", ["invoices"] = "get_all_invoices",
        ["invoice-lines"] = "get_invoice_lines", ["payments"] = "get_payments",
        ["invoice-pdf"] = "get_invoice_pdf", ["partners"] = "get_partners",
        ["sales-locations"] = "get_sales_locations", ["next-invoice-number"] = "get_next_invoice_number"
    };

    private static readonly Dictionary<string, string[]> Parameters = new()
    {
        ["get_partners"] = [], ["get_sales_locations"] = [], ["get_next_invoice_number"] = [], ["get_all_invoices"] = [],
        ["get_invoice"] = ["invoiceId"], ["get_invoice_lines"] = ["invoiceId"], ["get_payments"] = ["invoiceId"],
        ["get_invoice_pdf"] = ["invoiceId"],
        ["add_partner"] = ["name", "email", "street", "city", "postal"],
        ["update_partner"] = ["partnerId", "name", "email", "street", "city", "postal", "taxNo"],
        ["add_invoice_head"] = ["partnerId", "externalDocumentId", "dateSent", "dateServed", "dateToPay", "paid", "documentType"],
        ["add_invoice_line"] = ["invoiceId", "title", "measuringUnit", "quantity", "price", "vat", "discount"],
        ["update_invoice_line"] = ["lineId", "invoiceId", "title", "measuringUnit", "quantity", "price", "vat", "discount", "taxType", "konto"],
        ["issue_invoice_no_fiscalization"] = ["invoiceId", "number", "documentType"],
        ["issue_invoice_fiscalization"] = ["invoiceId", "locationId", "operatorTaxId", "operatorName", "invoiceNumber", "testMode", "documentType"],
        ["add_payment"] = ["invoiceId", "dateOfPayment", "amount", "paymentMethodId"],
        ["send_invoice_by_email"] = ["invoiceId", "recipient", "subject", "content"]
    };

    public static (string Command, JsonDocument Arguments, string? OutputPath) Parse(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("A command is required. Run cebelca-cli --help.");
        var name = args[0].Replace('-', '_');
        var command = Aliases.TryGetValue(args[0], out var alias) ? alias : name;
        if (!Parameters.TryGetValue(command, out var allowed)) throw new ArgumentException($"Unknown command '{args[0]}'. Run cebelca-cli --help.");

        // Keep JSON input for existing scripts, but ordinary usage accepts positional IDs and named flags.
        if (args.Length == 2 && args[1].StartsWith('{'))
            return (command, JsonDocument.Parse(args[1]), null);

        var values = new Dictionary<string, object>();
        string? outputPath = null;
        for (var index = 1; index < args.Length; index++)
        {
            var token = args[index];
            string key;
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                if (token == "--output" && command == "get_invoice_pdf")
                {
                    if (++index == args.Length) throw new ArgumentException("--output requires a path.");
                    outputPath = args[index];
                    continue;
                }
                key = allowed.FirstOrDefault(field => string.Equals("--" + Kebab(field), token, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Unknown option '{token}' for {command}.");
                if (++index == args.Length) throw new ArgumentException($"{token} requires a value.");
            }
            else if (index == 1 && allowed.Contains("invoiceId") && !values.ContainsKey("invoiceId")) key = "invoiceId";
            else throw new ArgumentException($"Unexpected argument '{token}'. Use named options such as --invoice-id.");

            if (values.ContainsKey(key)) throw new ArgumentException($"{key} was specified more than once.");
            var value = args[index];
            if (key == "invoiceId" && command == "get_invoice")
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                    throw new ArgumentException("invoiceId must be a positive integer.");
                values[key] = id;
            }
            else if (key is "price" or "amount")
            {
                if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                    throw new ArgumentException($"{key} must be a number using a dot as decimal separator.");
                values[key] = number;
            }
            else if (key is "paid" or "testMode")
            {
                if (!bool.TryParse(value, out var boolean)) throw new ArgumentException($"{key} must be true or false.");
                values[key] = boolean;
            }
            else values[key] = value;
        }
        return (command, JsonSerializer.SerializeToDocument(values), outputPath);
    }

    public static string Help => """
        Usage: cebelca-cli <command> [ID] [--option value ...]

        Read examples:
          cebelca-cli invoice 258
          cebelca-cli invoice-lines 258
          cebelca-cli invoices
          cebelca-cli partners
          cebelca-cli invoice-pdf 258 --output invoice.pdf

        Write example:
          cebelca-cli add-payment --invoice-id 258 --date-of-payment 2026-09-20 --amount 80 --payment-method-id 1

        All MCP command names are also accepted with hyphens or underscores.
        Commands:
        """ + string.Join(", ", CebelcaCli.Commands.Select(command => command.Replace('_', '-')));

    public static string HelpFor(string name)
    {
        var command = Aliases.TryGetValue(name, out var alias) ? alias : name.Replace('-', '_');
        if (!Parameters.TryGetValue(command, out var fields)) throw new ArgumentException($"Unknown command '{name}'.");
        var required = command switch
        {
            "add_partner" => 5, "update_partner" => 6, "add_invoice_head" => 5,
            "add_invoice_line" => 7, "update_invoice_line" => 8,
            "issue_invoice_no_fiscalization" => 1, "issue_invoice_fiscalization" => 4,
            "add_payment" => 4, "send_invoice_by_email" => 4,
            _ => 0
        };
        var options = string.Join(" ", fields.Select((field, index) => index < required
            ? $"--{Kebab(field)} VALUE" : $"[--{Kebab(field)} VALUE]"));
        var positional = fields.Contains("invoiceId") && required == 0 ? " [ID]" : "";
        var output = command == "get_invoice_pdf" ? " [--output PATH]" : "";
        return $"Usage: cebelca-cli {name}{positional} {options}{output}".TrimEnd();
    }

    private static string Kebab(string value) =>
        string.Concat(value.Select(character => char.IsUpper(character) ? "-" + char.ToLowerInvariant(character) : character.ToString()));
}
