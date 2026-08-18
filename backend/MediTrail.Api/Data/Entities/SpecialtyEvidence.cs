namespace MediTrail.Api.Data.Entities;

/// <summary>
/// Why a specialty was chosen. Disease-class labels from RxClass are drug-class information,
/// never a statement about the patient.
/// </summary>
public class SpecialtyEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SearchId { get; set; }
    public DoctorSearch? Search { get; set; }

    public required string EvidenceType { get; set; }
    public required string Label { get; set; }
    public string? Source { get; set; }
    public string? SourceId { get; set; }
    public string? SourceUrl { get; set; }
}
