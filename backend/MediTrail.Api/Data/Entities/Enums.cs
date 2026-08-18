namespace MediTrail.Api.Data.Entities;

/// <summary>
/// Per-document processing state. Persisted (not in-memory) so a restart can re-enqueue
/// anything still in flight without losing track of work (PRD §14.3).
/// </summary>
public enum DocumentStatus
{
    Uploaded,
    Queued,
    Extracting,
    Extracted,
    /// <summary>Identical file hash seen before; extraction reused rather than re-billed (FR-2.6).</summary>
    Cached,
    Failed
}

/// <summary>Patient-level analysis state, driving the processing screen's stepper (§10.3).</summary>
public enum PatientStatus
{
    Idle,
    Extracting,
    Merging,
    CrossChecking,
    Verifying,
    AnalyzingTrends,
    Ready,
    Failed
}

/// <summary>Traffic-light severity. Never conveyed by colour alone in the UI (§15 accessibility).</summary>
public enum AlertSeverity
{
    Info,
    Amber,
    Red
}

public enum AlertType
{
    DuplicatePrescription,
    DosageConflict,
    DrugInteraction,
    AllergyConflict,
    DocumentWarningConflict,
    LabOutOfRange,
    LabDrift,
    LowExtractionConfidence,
    /// <summary>
    /// A medication was read off the page but no generic could be resolved for it, so it took no
    /// part in any cross-check. Says so, rather than leaving the gap silent (Principle 1).
    /// </summary>
    UnresolvedMedication
}

/// <summary>
/// Result of the independent openFDA check (FR-5.6/5.7). <see cref="Unverified"/> and
/// <see cref="NotFound"/> never suppress a finding — absence of confirmation is not evidence of safety.
/// </summary>
public enum VerificationStatus
{
    /// <summary>Not yet attempted.</summary>
    Pending,
    /// <summary>openFDA label text corroborates the finding.</summary>
    Confirmed,
    /// <summary>Drug resolved, but the label does not mention this interaction.</summary>
    NotFound,
    /// <summary>Lookup failed or the API was unavailable. Finding stands, badged "verify with pharmacist".</summary>
    Unverified,
    /// <summary>Finding type is not externally verifiable (duplicates, dosage conflicts, lab trends).</summary>
    NotApplicable
}
