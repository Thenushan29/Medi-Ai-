using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.Configuration;
using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

public sealed class DoctorRecommendationService(
    MediTrailDbContext db,
    IGeocoder geocoder,
    IDoctorSearchProvider searchProvider,
    ISpecialtyResolver specialtyResolver,
    DoctorRankingService ranking,
    IOptions<DoctorRecommendationOptions> options) : IDoctorRecommendationService
{
    private readonly DoctorRecommendationOptions _options = options.Value;

    public IReadOnlyList<SpecialtyOptionDto> Specialties() => SpecialtyCatalog.All;

    public async Task<IReadOnlyList<DoctorSearchSummaryDto>> ListAsync(
        Guid patientId, CancellationToken ct = default)
    {
        var rows = await db.DoctorSearches.AsNoTracking()
            .Where(s => s.PatientId == patientId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        return rows.Select(s => new DoctorSearchSummaryDto
        {
            SearchId = s.Id,
            SpecialtyCode = s.SpecialtyCode,
            LocationText = s.LocationText,
            ResolvedPlace = s.ResolvedPlace,
            ProviderStatus = s.ProviderStatus,
            ResultCount = s.ResultCount,
            CreatedAt = s.CreatedAt,
            FetchedAt = s.FetchedAt,
            ServedFromCache = s.ServedFromCache
        }).ToList();
    }

    public async Task<DoctorSearchResponseDto> SearchAsync(
        Guid patientId, DoctorSearchRequest request, CancellationToken ct = default)
    {
        var specialty = (await ResolveSpecialtyAsync(patientId, request, ct)).ToDto();

        GeocodeResult geo;
        if (request.Latitude is not null && request.Longitude is not null)
        {
            geo = new GeocodeResult
            {
                Status = GeocodeStatus.Ok,
                ResolvedPlace = request.LocationText.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Geocoder = "device",
                ServedFromCache = false,
                FetchedAt = DateTimeOffset.UtcNow
            };
        }
        else
        {
            geo = await geocoder.GeocodeAsync(request.LocationText, ct);
        }

        if (geo.Status == GeocodeStatus.LocationNotFound)
        {
            var missing = await PersistAsync(
                patientId, request, specialty, geo, "location_not_found", 0, geo.FetchedAt, servedFromCache: false, ct);
            return Respond(
                missing,
                specialty,
                origin: null,
                status: "location_not_found",
                providerStatus: "location_not_found",
                message: $"We couldn't find {request.LocationText.Trim()}. Try a nearby town or district.",
                results: []);
        }

        if (geo.Status != GeocodeStatus.Ok || geo.Latitude is null || geo.Longitude is null)
        {
            var failed = await PersistAsync(
                patientId, request, specialty, geo, "failed", 0, geo.FetchedAt, servedFromCache: false, ct);
            return Respond(
                failed,
                specialty,
                origin: null,
                status: "failed",
                providerStatus: "failed",
                message: "We couldn't reach the map data service just now. Nothing is shown rather than showing you something unverified.",
                results: []);
        }

        var origin = new SearchOriginDto
        {
            ResolvedPlace = geo.ResolvedPlace,
            Latitude = geo.Latitude.Value,
            Longitude = geo.Longitude.Value,
            Geocoder = geo.Geocoder ?? "unknown"
        };

        var radius = request.RadiusMeters ?? _options.DefaultRadiusMeters;
        var providerResult = await searchProvider.SearchAsync(new ProviderQuery
        {
            Latitude = origin.Latitude,
            Longitude = origin.Longitude,
            RadiusMeters = radius,
            SpecialtyCode = specialty.Code
        }, ct);

        var status = providerResult.Status switch
        {
            ProviderStatus.Ok => "ok",
            ProviderStatus.Empty => "empty",
            ProviderStatus.NotConfigured => "not_configured",
            _ => "failed"
        };

        var fetchedAt = providerResult.FetchedAt ?? geo.FetchedAt;
        var persisted = await PersistAsync(
            patientId,
            request,
            specialty,
            geo,
            status,
            providerResult.Facilities.Count,
            fetchedAt,
            providerResult.ServedFromCache,
            ct);

        var message = providerResult.Status switch
        {
            ProviderStatus.NotConfigured =>
                "Facility search is not configured on this server yet. Location resolved; no clinics are shown.",
            ProviderStatus.Empty =>
                $"No clinics or hospitals found within {radius / 1000.0:0} km of {origin.ResolvedPlace} for {specialty.Label}.",
            ProviderStatus.Failed =>
                "We couldn't reach the map data service just now. Nothing is shown rather than showing you something unverified.",
            _ => null
        };

        var attribution = providerResult.Status is ProviderStatus.Ok or ProviderStatus.Empty
            ? OverpassProvider.Attribution
            : null;

        return Respond(
            persisted,
            specialty,
            origin,
            status,
            status,
            message,
            ranking.Rank(
                providerResult.Status == ProviderStatus.Ok ? providerResult.Facilities : [],
                specialty.Code,
                request.Availability),
            attribution);
    }

    private async Task<DoctorSearch> PersistAsync(
        Guid patientId,
        DoctorSearchRequest request,
        SpecialtyResolutionDto specialty,
        GeocodeResult geo,
        string providerStatus,
        int resultCount,
        DateTimeOffset? fetchedAt,
        bool servedFromCache,
        CancellationToken ct)
    {
        var row = new DoctorSearch
        {
            PatientId = patientId,
            AlertId = request.AlertId,
            SpecialtyCode = specialty.Code,
            SpecialtySource = specialty.ResolvedBy,
            LocationText = request.LocationText.Trim(),
            ResolvedPlace = geo.ResolvedPlace,
            Latitude = geo.Latitude,
            Longitude = geo.Longitude,
            RadiusMeters = request.RadiusMeters ?? _options.DefaultRadiusMeters,
            Availability = string.IsNullOrWhiteSpace(request.Availability) ? "anytime" : request.Availability,
            Provider = searchProvider.Source,
            ProviderStatus = providerStatus,
            ServedFromCache = servedFromCache,
            ResultCount = resultCount,
            FetchedAt = fetchedAt
        };

        db.DoctorSearches.Add(row);
        db.SpecialtyEvidence.Add(new SpecialtyEvidence
        {
            SearchId = row.Id,
            EvidenceType = "resolver_rung",
            Label = specialty.Reason,
            Source = specialty.ResolvedBy
        });
        foreach (var item in specialty.Evidence)
        {
            db.SpecialtyEvidence.Add(new SpecialtyEvidence
            {
                SearchId = row.Id,
                EvidenceType = item.Type,
                Label = item.Label,
                Source = item.Source,
                SourceId = item.SourceId,
                SourceUrl = item.SourceUrl
            });
        }

        await db.SaveChangesAsync(ct);
        return row;
    }

    private static DoctorSearchResponseDto Respond(
        DoctorSearch row,
        SpecialtyResolutionDto specialty,
        SearchOriginDto? origin,
        string status,
        string providerStatus,
        string? message,
        IReadOnlyList<FacilityResultDto> results,
        string? attribution = null) =>
        new()
        {
            SearchId = row.Id,
            Status = status,
            Specialty = specialty,
            Origin = origin,
            RadiusMeters = row.RadiusMeters,
            Provider = row.Provider,
            ProviderStatus = providerStatus,
            ServedFromCache = row.ServedFromCache,
            FetchedAtUtc = row.FetchedAt,
            Attribution = attribution,
            Results = results,
            Message = message
        };

    private async Task<SpecialtyResolution> ResolveSpecialtyAsync(
        Guid patientId, DoctorSearchRequest request, CancellationToken ct)
    {
        Alert? alert = null;
        if (request.AlertId is Guid alertId)
        {
            alert = await db.Alerts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == alertId && a.PatientId == patientId, ct);
        }

        var labKeys = new List<string>();
        if (alert is not null)
        {
            labKeys.AddRange(LabKeysIn(alert.Title));
            labKeys.AddRange(LabKeysIn(alert.ExplanationEn));

            if (alert.EvidenceDocumentIds.Count > 0)
            {
                var names = await db.LabResults.AsNoTracking()
                    .Where(l => l.PatientId == patientId && alert.EvidenceDocumentIds.Contains(l.DocumentId))
                    .Select(l => l.TestNameStandard ?? l.TestName)
                    .ToListAsync(ct);

                foreach (var name in names)
                {
                    var key = LabTestNormalizer.Standardize(name);
                    if (key is not null) labKeys.Add(key);
                }
            }
        }

        return await specialtyResolver.ResolveAsync(new SpecialtyContext
        {
            AlertType = alert?.Type,
            DrugNames = alert?.InvolvedGenerics ?? [],
            LabTestKeys = labKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Override = request.SpecialtyOverride
        }, ct);
    }

    private static IEnumerable<string> LabKeysIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        foreach (var key in SpecialtyMaps.LabKeyToSpecialty.Keys)
        {
            if (text.Contains(key, StringComparison.OrdinalIgnoreCase))
                yield return key;
        }
    }
}
