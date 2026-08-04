using System.Text.Json;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.Data;

/// <summary>
/// EF Core already implements repository and unit-of-work; the PRD deliberately declines to wrap it
/// in a second abstraction (§14.2). Services depend on this type directly.
/// </summary>
public class MediTrailDbContext(DbContextOptions<MediTrailDbContext> options) : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<Allergy> Allergies => Set<Allergy>();
    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // jsonb, text[] and uuid[] are Postgres types. Applying them unconditionally makes the
        // model unusable under the in-memory provider, and the deterministic cross-checks are
        // exactly the part most worth unit-testing — so the Postgres-specific mappings are
        // applied only when the provider is Postgres. Production behaviour is unchanged.
        var isPostgres = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false;

        // Enums persist as text, not ints — readable in psql and stable if a member is inserted later.
        b.Entity<Patient>(e =>
        {
            e.ToTable("patients");
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);

            if (isPostgres)
            {
                e.Property(x => x.RawExtractionJson).HasColumnType("jsonb");
            }
            else
            {
                e.Property(x => x.RawExtractionJson).HasConversion(
                    document => document == null ? null : document.RootElement.GetRawText(),
                    text => text == null ? null : JsonDocument.Parse(text, default));
            }

            e.Property(x => x.OriginalFileName).HasMaxLength(500);
            e.Property(x => x.ContentType).HasMaxLength(120);
            e.Property(x => x.StoragePath).HasMaxLength(600);
            e.Property(x => x.Sha256).HasMaxLength(64);

            e.HasOne(x => x.Patient)
                .WithMany(p => p.Documents)
                .HasForeignKey(x => x.PatientId)
                .OnDelete(DeleteBehavior.Cascade);

            // Hash lookup is the hot path for extraction caching (FR-2.6).
            e.HasIndex(x => x.Sha256);
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.HasIndex(x => new { x.PatientId, x.DocumentDate });
        });

        // Every child row carries DocumentId and it is never optional — evidence linking depends on it (§12.3).
        b.Entity<Medication>(e =>
        {
            e.ToTable("medications");
            e.Property(x => x.GenericName).HasMaxLength(200);
            e.Property(x => x.BrandName).HasMaxLength(200);
            e.Property(x => x.StrengthValue).HasPrecision(12, 4);
            e.Property(x => x.FrequencyPerDay).HasPrecision(6, 2);

            e.HasOne(x => x.Document)
                .WithMany(d => d.Medications)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.PatientId, x.GenericName });
        });

        b.Entity<LabResult>(e =>
        {
            e.ToTable("lab_results");
            e.Property(x => x.TestName).HasMaxLength(200);
            e.Property(x => x.TestNameStandard).HasMaxLength(200);
            e.Property(x => x.ValueNumeric).HasPrecision(14, 4);
            e.Property(x => x.NormalMin).HasPrecision(14, 4);
            e.Property(x => x.NormalMax).HasPrecision(14, 4);

            e.HasOne(x => x.Document)
                .WithMany(d => d.LabResults)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Composite index matches the trend query: one series per test, ordered by date (FR-6.2).
            e.HasIndex(x => new { x.PatientId, x.TestNameStandard, x.TestDate });
        });

        b.Entity<Allergy>(e =>
        {
            e.ToTable("allergies");
            e.Property(x => x.Substance).HasMaxLength(500);
            e.Property(x => x.SubstanceGeneric).HasMaxLength(200);
            if (isPostgres) e.Property(x => x.RelatesTo).HasColumnType("text[]");

            e.HasOne(x => x.Document)
                .WithMany(d => d.Allergies)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.PatientId, x.IsDocumentWarning });
        });

        b.Entity<Alert>(e =>
        {
            e.ToTable("alerts");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(48);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.VerificationStatus).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Title).HasMaxLength(300);
            if (isPostgres)
            {
                e.Property(x => x.InvolvedGenerics).HasColumnType("text[]");
                e.Property(x => x.EvidenceDocumentIds).HasColumnType("uuid[]");
            }

            e.HasOne(x => x.Patient)
                .WithMany(p => p.Alerts)
                .HasForeignKey(x => x.PatientId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.PatientId, x.Severity });
        });
    }
}
