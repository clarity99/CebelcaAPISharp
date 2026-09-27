using System.Text.Json;
using CebelcaAPI;

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine(CliArguments.Help);
    return;
}

if (args.Length == 2 && args[1] is "--help" or "-h")
{
    try { Console.WriteLine(CliArguments.HelpFor(args[0])); }
    catch (ArgumentException exception) { Console.Error.WriteLine(exception.Message); Environment.ExitCode = 2; }
    return;
}

var apiKey = Environment.GetEnvironmentVariable("CEBELCA_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("CEBELCA_API_KEY is required.");
    Environment.ExitCode = 2;
    return;
}

try
{
    var (command, document, outputPath) = CliArguments.Parse(args);
    using (document)
    {
        var result = await CebelcaCli.Execute(new CebelcaAPISharp(apiKey), command, document.RootElement);
        if (outputPath is not null)
        {
            using var pdf = JsonSerializer.SerializeToDocument(result);
            var bytes = Convert.FromBase64String(pdf.RootElement.GetProperty("base64").GetString()!);
            await using var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
            await file.WriteAsync(bytes);
            Console.WriteLine(outputPath);
            return;
        }
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
}
catch (JsonException)
{
    Console.Error.WriteLine("Invalid JSON arguments.");
    Environment.ExitCode = 2;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 2;
}
catch (IOException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 2;
}
catch
{
    Console.Error.WriteLine("Cebelca API request failed.");
    Environment.ExitCode = 1;
}
