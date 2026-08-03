using System.Text.Json.Serialization;
using MediTrail.Api.AiPipeline;
using MediTrail.Api.AiPipeline.Extraction;
using MediTrail.Api.Configuration;
using MediTrail.Api.Data;
using MediTrail.Api.Middleware;
using MediTrail.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

builder.Services.Configure<OpenRouterOptions>(
    builder.Configuration.GetSection(OpenRouterOptions.SectionName));
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

// M2 replaces this with the OpenRouter vision extractor. Until then it fails loudly rather
// than returning an empty extraction that would read as "this document was blank".
builder.Services.AddScoped<IDocumentExtractor, NotConfiguredDocumentExtractor>();

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
        database = await db.Database.CanConnectAsync(ct) ? "ok" : "unreachable";

        // Connecting is not enough — the schema has to have been applied.
        if (database == "ok" && !await db.Patients.AnyAsync(ct))
        {
            database = "ok (empty)";
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Readiness: database check failed");
        database = $"error: {ex.GetBaseException().Message}";
    }

    string bucket;
    try
    {
        // A path that will not exist; we only care that the bucket answers rather than 404-ing
        // the whole bucket or rejecting the key.
        await storage.DeleteAsync("__readiness_probe__", ct);
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
