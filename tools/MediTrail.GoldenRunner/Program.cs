using System.Text;
using System.Text.Json;
using MediTrail.Api.AiPipeline;
using MediTrail.Api.AiPipeline.Extraction;
using MediTrail.Api.AiPipeline.Providers;
using MediTrail.Api.Configuration;
using MediTrail.Api.Contracts.Extraction;
using MediTrail.GoldenRunner;
using MediTrail.GoldenRunner.Traps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// MediTrail golden dataset runner — the primary quality gate (§18.1).
//
// Runs the real extraction pipeline over dataset/ images, compares each result field by field
// against dataset/golden/<name>.json, and reports accuracy per category. That figure is the
// headline number in the technical summary, and M2 does not pass until it meets §3.3:
// >= 95% on printed documents, >= 80% on photographed ones, and ZERO hallucinated medications.

Console.OutputEncoding = Encoding.UTF8;

var repoRoot = FindRepositoryRoot();
var datasetDir = Path.Combine(repoRoot, "dataset");
var goldenDir = Path.Combine(datasetDir, "golden");
var filter = args.FirstOrDefault(a => !a.StartsWith('-'));

// Trap verification (§18.2) asks a different question from field accuracy (§18.1) — does a real
// image become a raised alert — but it needs the same dataset and the same API key, so it lives
// behind a mode switch here rather than in a second tool. See Traps/TrapRunner.cs.
if (args.Contains("--traps"))
{
    return await TrapRunner.RunAsync(repoRoot, datasetDir, args);
}

if (!Directory.Exists(goldenDir))
{
    Console.Error.WriteLine($"No golden labels at {goldenDir}. See dataset/README.md.");
    return 2;
}

// Configuration comes from the API project's user-secrets, so there is one place to hold the key.
var configuration = new ConfigurationBuilder()
    .AddUserSecrets("83ae9e70-f8ac-4a06-80b6-ac85fb688929")
    .AddEnvironmentVariables()
    .Build();

var apiKey = configuration[$"{AiOptions.SectionName}:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine(
        "Ai:ApiKey is not set.\n" +
        "  cd backend/MediTrail.Api\n" +
        "  dotnet user-secrets set \"Ai:Provider\" \"Groq\"\n" +
        "  dotnet user-secrets set \"Ai:ApiKey\" \"gsk_...\"");
    return 2;
}

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));
services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
services.AddSingleton<IPromptLibrary, PromptLibrary>();
// VisionDocumentExtractor took a dependency on this when PDF support landed (FR-2.7) and this
// registration was not added with it, so the accuracy gate threw at startup before reading a
// single document. §18.4 requires this run on every prompt change; it could not run at all.
services.AddSingleton<IPdfRenderer, PdfRenderer>();
// Same configuration path as the API — measuring against different settings than production
// would make the accuracy figure meaningless.
services.AddHttpClient<IAiClient, OpenAiCompatibleClient>((provider, client) =>
    AiHttpClient.Configure(client, provider.GetRequiredService<IOptions<AiOptions>>().Value));
services.AddScoped<IDocumentExtractor, VisionDocumentExtractor>();

var aiOptions = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
Console.WriteLine($"Provider: {aiOptions.Provider}  Model: {aiOptions.ExtractionModel}");

using var provider = services.BuildServiceProvider();
var extractor = provider.GetRequiredService<IDocumentExtractor>();

var labels = Directory.GetFiles(goldenDir, "*.json")
    // Leading underscore marks a template or note, not a document to score.
    .Where(f => !Path.GetFileName(f).StartsWith('_'))
    .Where(f => filter is null || Path.GetFileName(f).Contains(filter, StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f)
    .ToList();

if (labels.Count == 0)
{
    Console.Error.WriteLine($"No label files matched in {goldenDir}.");
    return 2;
}

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var allResults = new List<FieldResult>();
var failures = new List<string>();
var totalPromptTokens = 0;
var totalCompletionTokens = 0;

Console.WriteLine($"Golden dataset run - {labels.Count} document(s)\n");

foreach (var labelPath in labels)
{
    var name = Path.GetFileNameWithoutExtension(labelPath);
    var image = FindImage(datasetDir, name);

    if (image is null)
    {
        Console.WriteLine($"  SKIP  {name} - no matching image in dataset/");
        continue;
    }

    var expected = JsonSerializer.Deserialize<DocumentExtraction>(File.ReadAllText(labelPath), jsonOptions);
    if (expected is null)
    {
        Console.WriteLine($"  SKIP  {name} - label file could not be parsed");
        continue;
    }

    var result = await extractor.ExtractAsync(new ExtractionRequest
    {
        DocumentId = Guid.NewGuid(),
        Content = await File.ReadAllBytesAsync(image),
        ContentType = ContentTypeOf(image),
        FileName = Path.GetFileName(image)
    });

    totalPromptTokens += result.PromptTokens ?? 0;
    totalCompletionTokens += result.CompletionTokens ?? 0;

    if (!result.Succeeded || result.Extraction is null)
    {
        Console.WriteLine($"  FAIL  {name} - {result.FailureReason}");
        failures.Add(name);
        continue;
    }

    var results = FieldComparer.Compare(expected, result.Extraction);
    allResults.AddRange(results);

    var correct = results.Count(r => r.Outcome is Outcome.Correct or Outcome.CorrectNull);
    var accuracy = results.Count == 0 ? 100d : 100d * correct / results.Count;
    var hallucinations = results.Count(r => r.Outcome == Outcome.Hallucinated);

    var flag = hallucinations > 0 ? "  <- HALLUCINATION" : "";
    Console.WriteLine($"  {accuracy,5:0.0}%  {name}  ({correct}/{results.Count}, {result.LatencyMs}ms){flag}");

    foreach (var wrong in results.Where(r => r.Outcome is not (Outcome.Correct or Outcome.CorrectNull)))
    {
        Console.WriteLine($"           {wrong.Outcome,-13} {wrong.Field}");
        Console.WriteLine($"             expected: {wrong.Expected ?? "(null)"}");
        Console.WriteLine($"             actual:   {wrong.Actual ?? "(null)"}");
    }
}

// ---------------------------------------------------------------------------
// Report
// ---------------------------------------------------------------------------
Console.WriteLine("\n" + new string('-', 64));
Console.WriteLine("Accuracy by category");
Console.WriteLine(new string('-', 64));

foreach (var group in allResults.GroupBy(r => r.Category).OrderBy(g => g.Key))
{
    var correct = group.Count(r => r.Outcome is Outcome.Correct or Outcome.CorrectNull);
    var accuracy = 100d * correct / group.Count();
    Console.WriteLine($"  {group.Key,-14} {accuracy,6:0.0}%   ({correct}/{group.Count()})");
}

var totalCorrect = allResults.Count(r => r.Outcome is Outcome.Correct or Outcome.CorrectNull);
var overall = allResults.Count == 0 ? 0 : 100d * totalCorrect / allResults.Count;
var totalHallucinations = allResults.Count(r => r.Outcome == Outcome.Hallucinated);

Console.WriteLine(new string('-', 64));
Console.WriteLine($"  {"OVERALL",-14} {overall,6:0.0}%   ({totalCorrect}/{allResults.Count})");
Console.WriteLine(new string('-', 64));
Console.WriteLine($"  Hallucinated fields : {totalHallucinations}   (target: 0)");
Console.WriteLine($"  Missed fields       : {allResults.Count(r => r.Outcome == Outcome.Missed)}");
Console.WriteLine($"  Documents failed    : {failures.Count}");
Console.WriteLine($"  Tokens              : {totalPromptTokens} prompt / {totalCompletionTokens} completion");

// Fail the run so this can gate a build rather than just print a number.
var passed = overall >= 95 && totalHallucinations == 0 && failures.Count == 0;
Console.WriteLine($"\n  {(passed ? "PASS" : "BELOW TARGET")} - §3.3 requires >= 95% and zero hallucinations.\n");

return passed ? 0 : 1;

// ---------------------------------------------------------------------------

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediTrail.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? Directory.GetCurrentDirectory();
}

static string? FindImage(string datasetDir, string labelName) =>
    new[] { ".png", ".jpg", ".jpeg" }
        .Select(extension => Directory.GetFiles(datasetDir, labelName + extension, SearchOption.AllDirectories))
        .SelectMany(matches => matches)
        .FirstOrDefault();

static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".png" => "image/png",
    _ => "image/jpeg"
};
