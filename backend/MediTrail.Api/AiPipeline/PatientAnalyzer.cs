using MediTrail.Api.AiPipeline.CrossCheck;
using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.AiPipeline.RuleChecks;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.AiPipeline;

/// <summary>
/// Runs the patient-level half of the pipeline once every document has reached a terminal
/// state (§9.2): merge → rule checks → cross-check → verification.
///
/// Alerts are derived data (§12.2) — the whole set is recomputed from scratch each run, so a
/// re-analysis after a prompt change needs no cleanup and cannot leave stale findings behind.
/// </summary>
public interface IPatientAnalyzer
{
    Task AnalyzeAsync(Guid patientId, CancellationToken ct = default);
}

public sealed class PatientAnalyzer(
    MediTrailDbContext db,
    IExtractionMerger merger,
    IRuleChecker ruleChecker,
    IServiceProvider services,
    ILogger<PatientAnalyzer> logger) : IPatientAnalyzer
{
    public async Task AnalyzeAsync(Guid patientId, CancellationToken ct = default)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null) return;

        try
        {
            // ---- Stage 2: normalize & merge ----
            await SetStatusAsync(patient, PatientStatus.Merging, ct);

            var documentIds = await db.Documents
                .Where(d => d.PatientId == patientId
                         && (d.Status == DocumentStatus.Extracted || d.Status == DocumentStatus.Cached))
                .Select(d => d.Id)
                .ToListAsync(ct);

            foreach (var documentId in documentIds)
            {
                await merger.MergeAsync(documentId, ct);
            }

            // Derived, so rebuilt wholesale rather than reconciled.
            var stale = await db.Alerts.Where(a => a.PatientId == patientId).ToListAsync(ct);
            db.Alerts.RemoveRange(stale);
            await db.SaveChangesAsync(ct);

            // ---- Stage 3: deterministic rule checks ----
            await SetStatusAsync(patient, PatientStatus.CrossChecking, ct);
            var alerts = new List<Alert>(await ruleChecker.CheckAsync(patientId, ct));

            // ---- Stages 4 and 5: LLM cross-check, then openFDA verification ----
            // Absent when no AI key is configured; the deterministic findings above still stand.
            var crossChecker = services.GetService<IInteractionCrossChecker>();
            if (crossChecker is not null)
            {
                await SetStatusAsync(patient, PatientStatus.Verifying, ct);
                alerts.AddRange(await crossChecker.CheckAsync(patientId, ct));
            }
            else
            {
                logger.LogInformation("No AI client configured; skipping interaction cross-check for {PatientId}",
                    patientId);
            }

            alerts.AddRange(await LowConfidenceAlertsAsync(patientId, ct));

            alerts = Deduplicate(alerts);
            db.Alerts.AddRange(alerts);

            patient.Status = PatientStatus.Ready;
            patient.StatusMessage = documentIds.Count == 0
                ? "None of the uploaded documents could be read. Check the file quality and try again."
                : null;
            patient.AnalyzedAt = DateTimeOffset.UtcNow;
            patient.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);

            logger.LogInformation("Analysis complete for {PatientId}: {Count} alert(s) across {Documents} document(s)",
                patientId, alerts.Count, documentIds.Count);
        }
        catch (DbUpdateException ex)
        {
            // Name the entities EF was writing. "Expected 1 row, affected 0" without them is
            // unactionable, and this path is exactly where a bad merge shows up.
            var entries = string.Join(", ", ex.Entries.Select(e => $"{e.Entity.GetType().Name}/{e.State}"));
            logger.LogError(ex, "Analysis failed for patient {PatientId} while writing [{Entries}]",
                patientId, entries);

            await MarkFailedAsync(patient);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Analysis failed for patient {PatientId}", patientId);

            await MarkFailedAsync(patient);
        }
    }

    /// <summary>
    /// Collapses findings that say the same thing.
    ///
    /// Uploading the same scan twice gives each copy its own warning row, and each then matches the
    /// medications on both copies — so "Aspirin was prescribed despite a warning on the same
    /// document" appeared twice for one warning on one page. The same shape occurs whenever two
    /// rules, or a rule and the model, reach the same conclusion.
    ///
    /// The surviving alert keeps the highest confidence and the union of the evidence, so
    /// collapsing never costs the user a document they could have opened.
    /// </summary>
    private static List<Alert> Deduplicate(List<Alert> alerts) =>
        alerts
            .GroupBy(a => (a.Type, Title: a.Title.Trim().ToLowerInvariant()))
            .Select(group =>
            {
                var best = group.OrderByDescending(a => a.Confidence).First();

                best.EvidenceDocumentIds = group
                    .SelectMany(a => a.EvidenceDocumentIds)
                    .Distinct()
                    .ToList();

                best.InvolvedGenerics = group
                    .SelectMany(a => a.InvolvedGenerics)
                    .Distinct()
                    .ToList();

                return best;
            })
            .ToList();

    /// <summary>
    /// Records the failure without the poisoned change tracker: the entities that just failed are
    /// still tracked, so saving through the same context would fail again and lose the status.
    /// </summary>
    private async Task MarkFailedAsync(Patient patient)
    {
        try
        {
            db.ChangeTracker.Clear();

            var reloaded = await db.Patients.FirstOrDefaultAsync(p => p.Id == patient.Id);
            if (reloaded is null) return;

            reloaded.Status = PatientStatus.Failed;
            reloaded.StatusMessage = "Analysis could not be completed. Your documents are safe — try again.";
            reloaded.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record analysis failure for {PatientId}", patient.Id);
        }
    }

    /// <summary>
    /// FR-5.9: when a document read poorly, say so at patient level. A quietly weak extraction is
    /// how a missing medication turns into a clean-looking record.
    /// </summary>
    private async Task<IEnumerable<Alert>> LowConfidenceAlertsAsync(Guid patientId, CancellationToken ct)
    {
        var poor = await db.Documents
            .AsNoTracking()
            .Where(d => d.PatientId == patientId
                     && (d.Status == DocumentStatus.Failed
                      || (d.OverallConfidence != null && d.OverallConfidence < 50)))
            .Select(d => new { d.Id, d.OriginalFileName, d.OverallConfidence, d.Status })
            .ToListAsync(ct);

        if (poor.Count == 0) return [];

        var unreadable = poor.Count(p => p.Status == DocumentStatus.Failed);

        return
        [
            new Alert
            {
                PatientId = patientId,
                Type = AlertType.LowExtractionConfidence,
                Severity = AlertSeverity.Amber,
                Title = $"{poor.Count} document{(poor.Count == 1 ? "" : "s")} could not be read clearly",
                InvolvedGenerics = [],
                ExplanationEn =
                    (unreadable > 0
                        ? $"{unreadable} document{(unreadable == 1 ? " was" : "s were")} not readable at all, and "
                        : "Some documents were only partly readable, and ") +
                    "anything on them may be missing from the checks above. " +
                    "Findings can only cover what could actually be read.",
                SuggestedActionEn =
                    "Re-upload a clearer photo of these, or show the originals to your pharmacist.",
                Confidence = 100,
                RequiresProfessionalConsult = false,
                VerificationStatus = VerificationStatus.NotApplicable,
                EvidenceDocumentIds = poor.Select(p => p.Id).ToList(),
                DetectedBy = "rules"
            }
        ];
    }

    private async Task SetStatusAsync(Patient patient, PatientStatus status, CancellationToken ct)
    {
        patient.Status = status;
        patient.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
