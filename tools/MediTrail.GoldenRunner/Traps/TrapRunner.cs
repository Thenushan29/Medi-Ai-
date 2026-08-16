using MediTrail.Api.AiPipeline;
using MediTrail.Api.AiPipeline.CrossCheck;
using MediTrail.Api.AiPipeline.Extraction;
using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.AiPipeline.Providers;
using MediTrail.Api.AiPipeline.RuleChecks;
using MediTrail.Api.AiPipeline.Verification;
using MediTrail.Api.Configuration;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using MediTrail.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediTrail.GoldenRunner.Traps;

/// <summary>
/// Trap verification (§18.2) — the gap the golden runner leaves open.
///
/// <see cref="FieldComparer"/> proves the model reads a field correctly, and the unit tests prove a
/// rule fires on a hand-built input. Neither proves that a photograph of a prescription becomes a
/// raised alert, which is the only claim the demonstration actually makes. This runs the whole path:
/// image → <see cref="VisionDocumentExtractor"/> → <see cref="ProcessingWorker"/> →
/// <see cref="ExtractionMerger"/> → <see cref="DeterministicRuleChecker"/> →
/// <see cref="InteractionCrossChecker"/> → openFDA → persisted alerts, then asserts the traps from
/// <c>dataset/golden/traps.md</c> against what was written.
///
/// The wiring below mirrors <c>Program.cs</c> registration for registration. Two things differ, both
/// deliberate and neither in the reasoning path: the database is the in-memory provider rather than
/// Supabase Postgres (the runner must not write test patients into the demo database), and object
/// storage is a scratch directory rather than the Supabase bucket (it must not leave PHI in it).
/// Every stage that reads a document or decides a finding is the production class.
/// </summary>
internal static class TrapRunner
{
    public static async Task<int> RunAsync(string repoRoot, string datasetDir, string[] args)
    {
        var verbose = args.Contains("--verbose");
        var trapFilter = Value(args, "--trap")?.ToUpperInvariant();
        var patientFilter = Value(args, "--patient");

        if (trapFilter is not null && !TrapChecks.PatientOf.ContainsKey(trapFilter))
        {
            Console.Error.WriteLine(
                $"Unknown trap '{trapFilter}'. Known: {string.Join(", ", TrapChecks.All)}");
            return 2;
        }

        // A trap implies the set it lives in — re-running Y1 must not re-extract patient x.
        string[] sets = trapFilter is not null
            ? [TrapChecks.PatientOf[trapFilter]]
            : patientFilter is not null
                ? [Normalize(patientFilter)]
                : ["x", "y"];

        if (Array.Exists(sets, s => s is not ("x" or "y")))
        {
            Console.Error.WriteLine("--patient must be 'x' or 'y'.");
            return 2;
        }

        IReadOnlyList<string> traps = trapFilter is not null ? [trapFilter] : TrapChecks.All;

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("83ae9e70-f8ac-4a06-80b6-ac85fb688929")
            .AddEnvironmentVariables()
            .Build();

        if (string.IsNullOrWhiteSpace(configuration[$"{AiOptions.SectionName}:ApiKey"]))
        {
            Console.Error.WriteLine(
                "Ai:ApiKey is not set.\n" +
                "  cd backend/MediTrail.Api\n" +
                "  dotnet user-secrets set \"Ai:ApiKey\" \"<key>\"");
            return 2;
        }

        var scratch = Path.Combine(Path.GetTempPath(), "meditrail-traps", Guid.NewGuid().ToString("N"));
        await using var services = BuildServices(configuration, scratch, verbose);

        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

        Console.WriteLine($"Trap verification (§18.2) — provider {ai.Provider}, " +
                          $"extraction {ai.ExtractionModel}, reasoning {ai.ReasoningModel}");
        Console.WriteLine($"Patient set(s): {string.Join(", ", sets)}   Traps: {string.Join(", ", traps)}");
        Console.WriteLine("Real model calls, real openFDA lookups. Storage is a scratch directory; " +
                          "the database is in-memory.\n");

        var queue = services.GetRequiredService<IProcessingQueue>();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var pipelineOptions = services.GetRequiredService<IOptions<PipelineOptions>>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));

        var worker = new ProcessingWorker(
            queue, scopeFactory, pipelineOptions,
            services.GetRequiredService<ILogger<ProcessingWorker>>());

        await worker.StartAsync(cts.Token);

        var runs = new Dictionary<string, PatientRun>();

        try
        {
            foreach (var set in sets)
            {
                var images = Images(datasetDir, set);

                if (images.Count == 0)
                {
                    Console.Error.WriteLine(
                        $"No images for patient {set} in {datasetDir}. The documents are gitignored " +
                        "PHI — copy them in locally (dataset/README.md).");
                    return 2;
                }

                runs[set] = await ProcessAsync(services, set, images, cts.Token);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(scratch);
        }

        foreach (var set in sets) Report(runs[set]);

        return Verdict(traps, runs);
    }

    // -----------------------------------------------------------------------
    // Wiring — registration for registration with Program.cs
    // -----------------------------------------------------------------------

    private static ServiceProvider BuildServices(IConfiguration configuration, string scratch, bool verbose)
    {
        var services = new ServiceCollection();

        // --verbose turns on the pipeline's own Debug lines — which is where the reasons a finding
        // was dropped now live, since none of them may be logged at Information in production.
        // Scoped to MediTrail so EF Core and HttpClient do not bury them.
        services.AddLogging(b =>
        {
            b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning);
            if (verbose) b.AddFilter("MediTrail", LogLevel.Debug);
        });

        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));
        services.Configure<OpenFdaOptions>(configuration.GetSection(OpenFdaOptions.SectionName));

        // One document at a time. Y2 turns on the second copy of a file finding the first one
        // already extracted, and with three workers in flight that becomes a race the harness
        // would report differently on different runs.
        services.Configure<PipelineOptions>(o => o.WorkerConcurrency = 1);

        // Named once, outside the lambda: the options builder runs per DbContext instance, and a
        // fresh name there would give every scope its own empty store — the worker would find no
        // document to process and the analyzer no patient to analyze.
        var database = $"traps-{Guid.NewGuid():N}";
        services.AddDbContext<MediTrailDbContext>(o => o.UseInMemoryDatabase(database));

        services.AddSingleton<IStorageService>(_ => new LocalDiskStorage(scratch));
        services.AddSingleton<IPromptLibrary, PromptLibrary>();
        services.AddSingleton<IPdfRenderer, PdfRenderer>();

        services.AddHttpClient<IAiClient, OpenAiCompatibleClient>((provider, client) =>
            AiHttpClient.Configure(client, provider.GetRequiredService<IOptions<AiOptions>>().Value));

        services.AddScoped<IDocumentExtractor, VisionDocumentExtractor>();
        services.AddScoped<IInteractionCrossChecker, InteractionCrossChecker>();

        services.AddMemoryCache();
        services.AddHttpClient<IOpenFdaClient, OpenFdaClient>((provider, client) =>
        {
            var fda = provider.GetRequiredService<IOptions<OpenFdaOptions>>().Value;
            client.BaseAddress = new Uri(fda.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(fda.TimeoutSeconds);
        });

        services.AddScoped<IExtractionMerger, ExtractionMerger>();
        services.AddScoped<IRuleChecker, DeterministicRuleChecker>();
        services.AddScoped<IPatientAnalyzer, PatientAnalyzer>();

        services.AddSingleton<IProcessingQueue, ChannelProcessingQueue>();
        services.AddScoped<IDocumentService, DocumentService>();

        return services.BuildServiceProvider();
    }

    // -----------------------------------------------------------------------
    // One patient set, through the real upload → worker → analyze path
    // -----------------------------------------------------------------------

    private static async Task<PatientRun> ProcessAsync(
        IServiceProvider services, string set, IReadOnlyList<string> images, CancellationToken ct)
    {
        Guid patientId;

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediTrailDbContext>();

            var patient = new Patient { DisplayName = $"Patient {set}" };
            db.Patients.Add(patient);
            await db.SaveChangesAsync(ct);
            patientId = patient.Id;

            var files = new List<IFormFile>();
            foreach (var image in images)
            {
                var bytes = await File.ReadAllBytesAsync(image, ct);
                files.Add(new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files",
                    Path.GetFileName(image))
                {
                    Headers = new HeaderDictionary { ["Content-Type"] = ContentTypeOf(image) }
                });
            }

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var upload = await documents.UploadAsync(patientId, files, visitLabel: null, ct);

            Console.WriteLine($"Patient {set}: {upload.Accepted.Count} document(s) uploaded" +
                              (upload.Rejected.Count > 0
                                  ? $", {upload.Rejected.Count} rejected: " +
                                    string.Join("; ", upload.Rejected.Select(r => $"{r.FileName} ({r.Reason})"))
                                  : string.Empty));
        }

        await WaitForAnalysisAsync(services, set, patientId, ct);

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediTrailDbContext>();
            return await PatientRun.LoadAsync(db, set, patientId, ct);
        }
    }

    /// <summary>
    /// Waits on the same signal the processing screen polls (§10.3) — the patient's own status,
    /// which the worker advances once every document has reached a terminal state.
    /// </summary>
    private static async Task WaitForAnalysisAsync(
        IServiceProvider services, string set, Guid patientId, CancellationToken ct)
    {
        var last = PatientStatus.Idle;
        var started = Environment.TickCount64;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediTrailDbContext>();

            var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patientId, ct);

            var done = await db.Documents.CountAsync(
                d => d.PatientId == patientId
                  && (d.Status == DocumentStatus.Extracted
                   || d.Status == DocumentStatus.Cached
                   || d.Status == DocumentStatus.Failed), ct);

            var total = await db.Documents.CountAsync(d => d.PatientId == patientId, ct);

            if (patient.Status != last)
            {
                last = patient.Status;
                Console.WriteLine($"  [{(Environment.TickCount64 - started) / 1000,4}s] " +
                                  $"patient {set}: {last} ({done}/{total} documents read)");
            }

            if (patient.Status is PatientStatus.Ready or PatientStatus.Failed)
            {
                if (patient.StatusMessage is { } message) Console.WriteLine($"         {message}");
                return;
            }

            await Task.Delay(1000, ct);
        }
    }

    // -----------------------------------------------------------------------
    // Report
    // -----------------------------------------------------------------------

    private static void Report(PatientRun run)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 100));
        Console.WriteLine($"Patient {run.Key} — {run.Documents.Count} document(s)");
        Console.WriteLine(new string('=', 100));

        Console.WriteLine("\nExtraction");
        foreach (var document in run.Documents)
        {
            var medications = run.Medications.Count(m => m.Document == document.Name);
            var warnings = run.Allergies.Count(a => a.Document == document.Name && a.IsDocumentWarning);

            Console.WriteLine(
                $"  {document.Name,-22} {document.Status,-10} " +
                $"date={document.DocumentDate?.ToString("yyyy-MM-dd") ?? "null",-10} " +
                $"conf={document.OverallConfidence?.ToString() ?? "-",-4} " +
                $"meds={medications,-3} warnings={warnings,-3} " +
                $"{document.LatencyMs?.ToString() ?? "-"}ms");

            if (document.FailureReason is { } reason)
            {
                Console.WriteLine($"      FAILED: {reason}");
            }
        }

        var prompt = run.Documents.Sum(d => d.PromptTokens ?? 0);
        var completion = run.Documents.Sum(d => d.CompletionTokens ?? 0);
        var cached = run.Documents.Count(d => d.Status == DocumentStatus.Cached);
        Console.WriteLine($"  {run.Documents.Count - cached} extraction call(s), {cached} served from cache. " +
                          $"Tokens: {prompt} prompt / {completion} completion.");

        // Every medication row, because a trap that fails almost always fails here: a generic that
        // stayed null is invisible to every cross-check, and the alert list alone cannot show that.
        Console.WriteLine("\nMedications as merged");
        foreach (var group in run.Medications.GroupBy(m => m.Document).OrderBy(g => g.Key))
        {
            foreach (var medication in group)
            {
                var strength = medication.StrengthValue is null
                    ? string.Empty
                    : $" {medication.StrengthValue}{medication.StrengthUnit}";

                Console.WriteLine(
                    $"  {group.Key,-22} generic={medication.GenericName ?? "NULL",-32} " +
                    $"brand={medication.BrandName ?? "-"}{strength}");
            }
        }

        Console.WriteLine("\nPrinted warnings and recorded allergies");
        if (run.Allergies.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        foreach (var entry in run.Allergies)
        {
            Console.WriteLine(
                $"  {entry.Document,-22} {(entry.IsDocumentWarning ? "warning " : "ALLERGY ")}" +
                $"relatesTo=[{string.Join(", ", entry.RelatesTo)}]");
            Console.WriteLine($"      \"{Shorten(entry.SourceText ?? entry.Substance)}\"");
        }

        Console.WriteLine($"\nAlerts ({run.Alerts.Count})");
        if (run.Alerts.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        foreach (var alert in run.Alerts)
        {
            Console.WriteLine(
                $"  [{alert.Severity}] {alert.Type}  confidence {alert.Confidence}  " +
                $"consult={(alert.RequiresProfessionalConsult ? "YES" : "no")}  " +
                $"openFDA={alert.VerificationStatus}  by {alert.DetectedBy ?? "?"}");
            Console.WriteLine($"      {alert.Title}");
            Console.WriteLine($"      medications: {(alert.InvolvedGenerics.Count == 0 ? "(none)" : string.Join(", ", alert.InvolvedGenerics))}");
            Console.WriteLine($"      documents:   {(alert.EvidenceDocuments.Count == 0 ? "(none)" : string.Join(", ", alert.EvidenceDocuments))}");
        }
    }

    private static int Verdict(IReadOnlyList<string> traps, IReadOnlyDictionary<string, PatientRun> runs)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 100));
        Console.WriteLine("Trap assertions (dataset/golden/traps.md §18.2)");
        Console.WriteLine(new string('=', 100));

        var results = traps
            .Select(id => TrapChecks.Evaluate(
                id, runs.TryGetValue(TrapChecks.PatientOf[id], out var run) ? run : null))
            .ToList();

        foreach (var result in results)
        {
            var label = result.Outcome switch
            {
                TrapOutcome.Pass => "PASS",
                TrapOutcome.Fail => "FAIL",
                _ => "SKIP"
            };

            Console.WriteLine($"\n  {label}  {result.Id}  {result.Description}");
            Console.WriteLine($"        {result.Detail}");
        }

        var failed = results.Count(r => r.Outcome == TrapOutcome.Fail);
        var passed = results.Count(r => r.Outcome == TrapOutcome.Pass);
        var skipped = results.Count(r => r.Outcome == TrapOutcome.Skipped);

        Console.WriteLine();
        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"  {passed} passed, {failed} failed, {skipped} not covered by this run.");
        Console.WriteLine(new string('-', 100));

        // Non-zero so this gates a build the same way the golden runner does. A skipped trap is not
        // a pass, but a filtered re-run is a deliberate act, so it does not fail the build either.
        return failed == 0 ? 0 : 1;
    }

    // -----------------------------------------------------------------------

    private static IReadOnlyList<string> Images(string datasetDir, string set) =>
        Directory.Exists(datasetDir)
            ? [.. new[] { "*.png", "*.jpg", "*.jpeg", "*.pdf" }
                .SelectMany(pattern =>
                    Directory.GetFiles(datasetDir, $"patient_{set}_{pattern}", SearchOption.AllDirectories))
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)]
            : [];

    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".pdf" => "application/pdf",
        _ => "image/jpeg"
    };

    private static string Normalize(string patient) =>
        patient.Replace("patient_", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();

    private static string? Value(string[] args, string name) => args
        .FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        ?[(name.Length + 1)..];

    private static string Shorten(string? text) =>
        text is null ? string.Empty
        : text.Length <= 140 ? text.ReplaceLineEndings(" ")
        : text.ReplaceLineEndings(" ")[..140] + "…";

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A scratch copy of the dataset left behind is untidy, not a failure worth reporting.
        }
    }
}
