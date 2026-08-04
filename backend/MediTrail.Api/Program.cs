using System.Text.Json.Serialization;
using MediTrail.Api.AiPipeline;
using MediTrail.Api.AiPipeline.Chat;
using MediTrail.Api.AiPipeline.CrossCheck;
using MediTrail.Api.AiPipeline.Trends;
using MediTrail.Api.AiPipeline.Extraction;
using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.AiPipeline.Providers;
using MediTrail.Api.AiPipeline.RuleChecks;
using MediTrail.Api.AiPipeline.Verification;
using MediTrail.Api.Configuration;
using MediTrail.Api.Data;
using MediTrail.Api.Middleware;
using MediTrail.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
// Secrets arrive as environment variables (Supabase__ServiceKey, OpenRouter__ApiKey, …).
// Nothing sensitive is committed (§19).
// ---------------------------------------------------------------------------
builder.Services.AddOptions<SupabaseOptions>()
    .Bind(builder.Configuration.GetSection(SupabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<AiOptions>(
    builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<PipelineOptions>(
    builder.Configuration.GetSection(PipelineOptions.SectionName));
builder.Services.Configure<OpenFdaOptions>(
    builder.Configuration.GetSection(OpenFdaOptions.SectionName));

// ---------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Postgres is not configured. Set it via environment or user-secrets.");

builder.Services.AddDbContext<MediTrailDbContext>(options => options
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention());

// ---------------------------------------------------------------------------
// External dependencies, all behind interfaces so a provider swap is configuration, not code (§14.2)
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient<IStorageService, SupabaseStorageService>((provider, client) =>
{
    var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseOptions>>().Value;
    client.BaseAddress = new Uri(options.Url.TrimEnd('/'));
    client.DefaultRequestHeaders.Add("apikey", options.ServiceKey);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ServiceKey}");
    client.Timeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddSingleton<IPromptLibrary, PromptLibrary>();

var aiKey = builder.Configuration[$"{AiOptions.SectionName}:ApiKey"];

if (string.IsNullOrWhiteSpace(aiKey))
{
    // No key: the app still runs and uploads still persist, but every document fails with a
    // reason that names the cause — better than booting into a state where extraction silently
    // produces nothing.
    builder.Services.AddScoped<IDocumentExtractor, NotConfiguredDocumentExtractor>();
}
else
{
    builder.Services.AddHttpClient<IAiClient, OpenAiCompatibleClient>((provider, client) =>
        AiHttpClient.Configure(client, provider.GetRequiredService<IOptions<AiOptions>>().Value));

    builder.Services.AddScoped<IDocumentExtractor, VisionDocumentExtractor>();
    builder.Services.AddScoped<IInteractionCrossChecker, InteractionCrossChecker>();
}

// openFDA is an optional enhancement, never a hard dependency (§14.4) — registered whether or not
// an AI key exists, and its failures never remove a finding.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IOpenFdaClient, OpenFdaClient>((provider, client) =>
{
    var fda = provider.GetRequiredService<IOptions<OpenFdaOptions>>().Value;
    client.BaseAddress = new Uri(fda.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(fda.TimeoutSeconds);
});

builder.Services.AddScoped<IExtractionMerger, ExtractionMerger>();
builder.Services.AddScoped<IRuleChecker, DeterministicRuleChecker>();
builder.Services.AddScoped<IPatientAnalyzer, PatientAnalyzer>();

// ---------------------------------------------------------------------------
// Background processing (§14.3)
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IProcessingQueue, ChannelProcessingQueue>();
builder.Services.AddHostedService<ProcessingWorker>();

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IAnalysisService, AnalysisService>();
builder.Services.AddScoped<ITrendAnalyzer, TrendAnalyzer>();
builder.Services.AddScoped<IGroundedChatService, GroundedChatService>();

// ---------------------------------------------------------------------------
// Web
// ---------------------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums cross the wire as names — "Red", not 2 — so the frontend and the API cannot
        // silently disagree about severity if a member is ever inserted.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// Model-validation failures use the same envelope as everything else, so the frontend's
// error handling never has to special-case a second shape (§13 conventions).
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = context.ModelState
            .SelectMany(entry => entry.Value?.Errors ?? [])
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
            ?? "The request was not valid.";

        return new BadRequestObjectResult(new ApiError
        {
            Code = "validation_failed",
            Message = message,
            TraceId = context.HttpContext.TraceIdentifier
        });
    };
});

builder.Services.AddOpenApi();

// CORS restricted to the deployed frontend origin (§13 conventions).
const string CorsPolicy = "meditrail-frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();

// Swagger enabled in all environments (§13) — judges can inspect the API directly.
app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("MediTrail API"));

app.UseCors(CorsPolicy);

// Every API response is a live view of the record, so none of it may be cached. Without this the
// browser reuses a GET it already has: delete a document and the timeline still shows it until the
// page is reloaded, which reads as the delete having failed.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
    }

    await next();
});

app.MapControllers();

// Target for the uptime ping that keeps the free tier from sleeping during judging (§19).
// Deliberately touches nothing external, so a pinged-awake app stays cheap.
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }))
    .WithName("Health");

// Readiness: proves the Supabase database and storage bucket are actually reachable with the
// configured credentials. Use this after setup instead of guessing from a silent 500.
app.MapGet("/health/ready", async (
    MediTrailDbContext db,
    IStorageService storage,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("Readiness");

    string database;
    try
    {
        // Opened explicitly rather than via CanConnectAsync, which swallows the exception and
        // returns false — "unreachable" with no reason is not a diagnosis.
        await db.Database.OpenConnectionAsync(ct);
        await db.Database.CloseConnectionAsync();

        // Connecting is not enough — the schema has to have been applied.
        database = await db.Patients.AnyAsync(ct) ? "ok" : "ok (empty)";
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Readiness: database check failed");
        database = $"error: {ex.GetBaseException().Message}";
    }

    string bucket;
    try
    {
        await storage.ProbeAsync(ct);
        bucket = "ok";
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Readiness: storage check failed");
        bucket = $"error: {ex.GetBaseException().Message}";
    }

    var healthy = database.StartsWith("ok") && bucket == "ok";

    return healthy
        ? Results.Ok(new { status = "ready", database, bucket })
        : Results.Json(new { status = "not ready", database, bucket }, statusCode: 503);
})
.WithName("Readiness");

app.Run();
