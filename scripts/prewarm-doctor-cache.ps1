<#
.SYNOPSIS
    Warms Nominatim and Overpass caches for the demo cities and specialties.

.DESCRIPTION
    POSTs /api/patients/{id}/doctor-search for 10 Sri Lankan towns × 6 specialties.
    Prints status and result counts only. Never prints a clinic, doctor, address, phone, or rating.

    Requires Features:DoctorRecommendation to be on, and the API to be running.

.EXAMPLE
    ./scripts/prewarm-doctor-cache.ps1

.EXAMPLE
    ./scripts/prewarm-doctor-cache.ps1 -BaseUrl http://localhost:5000 -DelayMs 1500
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5000',
    [guid]$PatientId,
    [int]$DelayMs = 1500
)

$ErrorActionPreference = 'Stop'

# Town names only — the static geocode table, not clinics.
$cities = @(
    'Jaffna',
    'Colombo',
    'Kandy',
    'Galle',
    'Kurunegala',
    'Batticaloa',
    'Trincomalee',
    'Vavuniya',
    'Anuradhapura',
    'Negombo'
)

$specialties = @(
    'cardiology',
    'endocrinology',
    'nephrology',
    'general_practice',
    'allergy_immunology',
    'gynaecology'
)

function Write-Step { param([string]$Text) Write-Host "`n$Text" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Text) Write-Host "  OK   $Text" -ForegroundColor Green }
function Write-Bad  { param([string]$Text) Write-Host "  FAIL $Text" -ForegroundColor Red }
function Write-Info { param([string]$Text) Write-Host "       $Text" -ForegroundColor DarkGray }

$root = $BaseUrl.TrimEnd('/')

Write-Host "MediTrail - doctor-search cache pre-warm" -ForegroundColor White
Write-Host "API: $root"
Write-Info "Nominatim is 1 request/s; Overpass is slow. This run is sequential on purpose."

Write-Step "1. Feature flag"
try {
    $null = Invoke-RestMethod -Method Get -Uri "$root/api/specialties"
    Write-Ok "Doctor recommendation is enabled."
}
catch {
    $status = $_.Exception.Response.StatusCode.value__
    if ($status -eq 404) {
        Write-Bad "Doctor recommendation is off. Set Features:DoctorRecommendation true and restart the API."
        exit 1
    }
    Write-Bad "Could not reach $root/api/specialties. Is the API running?"
    throw
}

Write-Step "2. Patient"
if (-not $PatientId -or $PatientId -eq [guid]::Empty) {
    $created = Invoke-RestMethod -Method Post -Uri "$root/api/patients" `
        -ContentType 'application/json; charset=utf-8' `
        -Body '{"displayName":"Cache prewarm"}'
    $PatientId = [guid]$created.id
    Write-Ok "Created throwaway patient $PatientId (delete it after the demo if you want)."
}
else {
    $null = Invoke-RestMethod -Method Get -Uri "$root/api/patients/$PatientId"
    Write-Ok "Using patient $PatientId"
}

Write-Step "3. $($cities.Count) towns x $($specialties.Count) specialties"
$ok = 0
$empty = 0
$failed = 0
$missing = 0
$other = 0
$total = 0

foreach ($city in $cities) {
    foreach ($specialty in $specialties) {
        $total++
        $body = @{
            locationText      = $city
            availability      = 'anytime'
            specialtyOverride = $specialty
        } | ConvertTo-Json -Compress

        try {
            $response = Invoke-RestMethod -Method Post `
                -Uri "$root/api/patients/$PatientId/doctor-search" `
                -ContentType 'application/json; charset=utf-8' `
                -Body $body

            $status = [string]$response.status
            $count = 0
            if ($null -ne $response.results) { $count = @($response.results).Count }

            switch ($status) {
                'ok' { $ok++; Write-Ok "$city / $specialty -> $status, $count facilities" }
                'empty' { $empty++; Write-Info "$city / $specialty -> $status, 0 facilities" }
                'failed' { $failed++; Write-Bad "$city / $specialty -> $status" }
                'location_not_found' { $missing++; Write-Bad "$city / $specialty -> $status" }
                default { $other++; Write-Info "$city / $specialty -> $status, $count facilities" }
            }
        }
        catch {
            $failed++
            $code = $_.Exception.Response.StatusCode.value__
            Write-Bad "$city / $specialty -> HTTP $code"
        }

        Start-Sleep -Milliseconds $DelayMs
    }
}

Write-Step "4. Summary"
Write-Host "  $total searches  ok=$ok  empty=$empty  failed=$failed  location_not_found=$missing  other=$other"
Write-Info "Facility names are not printed. Cache rows keep the original fetched_at."
Write-Info "A later search in the same town/specialty should show servedFromCache=true."
