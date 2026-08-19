# MediTrail

**AI Medical Report & Prescription Cross-Checker** — YGC AI Competition 2026, Round 1.
Team Dev HuHu.

**Live link:** <!-- TODO: paste the deployed URL here before submission -->

Upload photos or scans of medical documents collected across years and providers. A vision model
reads every document, structured extraction merges them into one patient record, and a multi-stage
analysis engine cross-checks for drug interactions, duplicate prescriptions, dosage conflicts,
allergy contradictions, and lab values drifting out of range.

Every finding is explained in English and Tamil, carries a confidence score, links back to the exact
source document, and — where possible — is verified against the openFDA drug label database.

**MediTrail never diagnoses.** It surfaces risks a human should check, and says clearly when it is unsure.

The product specification is [docs/MediTrail_PRD.md](docs/MediTrail_PRD.md) and it is the single
source of truth. Section references throughout the code (`§11.4`, `FR-5.5`) point into it.

---

## Layout

```
backend/MediTrail.Api/   ASP.NET Core 10 Web API
  Contracts/Extraction/  Canonical extraction schema (§12.1) — the shape every document becomes
  Contracts/Api/         Wire DTOs, kept distinct from entities
  Controllers/           Thin: validate, delegate, map to a status code
  Services/              Patient, document and storage logic
  AiPipeline/            Queue, background worker, vision extraction
  AiPipeline/Prompts/    Prompts as files, not inline strings
tools/GoldenRunner/      Extraction accuracy against hand-labelled truth (§18.1)
  Data/                  EF Core entities, DbContext, migrations
frontend/                Angular 22 standalone + Tailwind v4
  src/app/core/          API client, models, language service
  src/app/features/      Patients, upload, processing, dashboard, evidence, doctor-search
  src/app/shared/        Disclaimer, confidence badge
db/                      Schema SQL (generated) + views
docs/                    PRD. The Round 2 plan is maintained outside the repository.
scripts/                 Supabase setup; doctor-search cache pre-warm
dataset/                 Evaluation documents (gitignored — PHI) and golden labels
```

Architecture is a **layered monolith** with one-way dependencies:
`Controllers → Services → AiPipeline / Data`. Clean Architecture, CQRS, repositories and an external
message broker are all deliberately excluded, with rationale in §14.2 — the complexity budget goes
to the AI pipeline.

---

## Running it

### Prerequisites

- .NET 10 SDK
- Node.js ≥ 24.15
- A Supabase project (Postgres + Storage)

> **Setting up Supabase for the first time?** Follow
> [docs/SUPABASE_SETUP.md](docs/SUPABASE_SETUP.md) — it covers the connection string, the
> service-role key, the bucket, and how to verify all three.

### 1. Configure

```bash
cp .env.example .env    # then fill in
```

`.env` is gitignored. Load it into your shell, or use `dotnet user-secrets` for local development:

```bash
cd backend/MediTrail.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Postgres" "<supabase-pooled-connection-string>"
dotnet user-secrets set "Supabase:Url" "https://<project-ref>.supabase.co"
dotnet user-secrets set "Supabase:ServiceKey" "<service-role-key>"

# AI provider — OpenRouter, Groq, or any OpenAI-compatible endpoint.
# Groq's free tier is the cheap way to iterate on the prompt.
dotnet user-secrets set "Ai:Provider" "Groq"
dotnet user-secrets set "Ai:ApiKey" "<key>"
dotnet user-secrets set "Ai:ExtractionModel" "meta-llama/llama-4-scout-17b-16e-instruct"
```

### 2. Apply the database schema

Run `db/01_schema.sql` then `db/02_views.sql` in the Supabase SQL editor. See [db/README.md](db/README.md).

### 3. Create the storage bucket

In Supabase → Storage, create a bucket named `documents`. Round 1 uses a **public** bucket so the
evidence viewer can load source images directly; the production path (private bucket, signed URLs,
RLS) is documented in §16.3.

### 4. Run

```bash
# API — http://localhost:5000, OpenAPI reference at /scalar
cd backend/MediTrail.Api && dotnet run

# Confirm Supabase is wired up correctly
curl http://localhost:5000/health/ready

# Frontend — http://localhost:4200
cd frontend && npm start
```

---

## Doctor recommendation (Round 2)

When an alert is **Red**, **Amber**, consult-flagged, or under 50% confidence, the dashboard can
open a three-step drawer: confirm a specialty, give a town (or device coordinates), then list
**nearby facilities from public map data**. It is not a referral, not an SLMC check, and not a
statement about the patient.

The feature is gated by `Features:DoctorRecommendation` and defaults **off**. One flip returns the
app to the Round 1 surface. `GET /api/health/providers` is not gated — it pings Overpass, Nominatim,
and RxNav so a venue check still works when the drawer is dark.

```bash
cd backend/MediTrail.Api
dotnet user-secrets set "Features:DoctorRecommendation" "true"
```

Or set `"DoctorRecommendation": true` under `Features` in the API configuration. Then restart the
API. If the flag is off, doctor-search routes return 404 with that message; nothing is invented.

Before a live demo, warm the Overpass/Nominatim cache (counts only — the script never prints a
clinic name):

```powershell
./scripts/prewarm-doctor-cache.ps1
# ./scripts/prewarm-doctor-cache.ps1 -BaseUrl http://localhost:5000 -PatientId <guid>
```

### Doctor / pharmacist one-pager

A printable summary at `/patients/{id}/summary`, reached from the patient dashboard once
processing has finished. It reuses **already-persisted** alerts, medications, and lab
trends — it makes **no additional model call**.

It never diagnoses and never recommends starting, stopping, or changing a medication
(PRD §5.3). What it actually renders:

- Patient display name, document count, and (when both exist) earliest–latest document dates
- An amber disclaimer that MediTrail is an information tool, not a diagnosis
- **Findings** — severity chip, title, involved generics, confidence, bilingual explanation,
  suggested action, verification status, source filenames, and a consult banner when the
  finding requires a professional
- **Medications** — display name, therapeutic class, a flagged marker when the group has a
  conflict, and prescription count
- **Lab trends** — test name, unit, reading count, direction, a latest-out-of-range note,
  and bilingual explanation; if there is no numeric series, it says MediTrail will not
  invent a trend

English and Tamil copy use `LanguageService.pick()`. The page has **Print / save as PDF**
and **← Back to dashboard**.

### What each field is, and what shows when it is missing

| Displayed field | Source | When missing |
|---|---|---|
| Suggested specialty | Deterministic ladder: user override → alert type → NLM RxClass MED-RT `may_treat` → ATC/EPC → lab keys → general practice. The model never picks it. | General practice, with the reason string from the rung that fired |
| “Why this specialty?” chips | NLM RxClass class pages (`mor.nlm.nih.gov`) | No chips. Disease-class names here are **drug-class information**, not a statement about the patient |
| Facility name | OpenStreetMap `name` | The OSM facility type (`hospital`, `clinic`, …), else **Not listed** |
| Category | OSM `amenity` or `healthcare` | **Not listed** |
| Specialty tag | OSM `healthcare:speciality` (British spellings) | **Not listed** |
| Address | OSM `addr:*` tags | **Not listed** |
| Distance | Haversine from the search origin, labelled **straight-line** | Always present on a result row |
| Phone | OSM `phone` / `contact:phone` | **Not listed** |
| Website | OSM `website` / `contact:website` | **Not listed** |
| Hours | OSM `opening_hours`, shown **raw** | **Not listed** |
| Availability badge | Heuristic on `opening_hours` (`match` / `unknown` / `indeterminate` / `no_match`) | **Hours unknown** |
| Why ranked | Transparent score (specialty tag, type, distance, contact, hours). Never a rating. | Reasons array is never empty |
| Open in map | OSM object URL, or a lat/lng pin if the object URL is absent | Link still opens OSM at the coordinates |
| Source + fetched time | Provider name + `fetched_at` from the live call or the labelled cache | Time shows **Not listed** rather than pretending it is live |
| Rating | **OSM has no ratings.** This UI does not render stars. | Never shown. Never `0.0`. |
| SLMC registration | Not checked. Footer deep-links the [SLMC public register](https://slmc.gov.lk/public/en/services/public). | No “SLMC Verified” badge |

Empty, failed, and unknown-place are three different API statuses and three different screens.
Empty renders **zero** cards. Failed shows nothing unverified unless an *expired* cache row exists,
in which case those rows are shown with a **stale** badge and the original `fetched_at`.

### Limitations

- OpenStreetMap has **no ratings**.
- OSM maps **facilities**, not individual practitioners.
- Coverage varies outside major cities.
- MediTrail does **not** verify SLMC registration.
- Distance is straight-line, not road distance or travel time.
- RxClass `may_treat` classes are not a diagnosis.

### Attributions

```
© OpenStreetMap contributors — data available under the Open Database License (ODbL).
Geocoding by Nominatim, © OpenStreetMap contributors.

This product uses publicly available data from the U.S. National Library of Medicine
(NLM), National Institutes of Health, Department of Health and Human Services; NLM is
not responsible for the product and does not endorse or recommend this or any other
product.
```

---

## Quality gates

Both live in `tools/MediTrail.GoldenRunner` and read the API project's user-secrets, so there is one
place holding the API key. Both exit non-zero on failure, so either can gate a build. Both need the
dataset images copied in locally — they are gitignored PHI (see [dataset/README.md](dataset/README.md)).

### Extraction accuracy (§18.1)

Field-by-field against the hand-labelled golden labels. This is the headline accuracy figure.

```bash
dotnet run --project tools/MediTrail.GoldenRunner
dotnet run --project tools/MediTrail.GoldenRunner -- patient_x    # one document set
```

### Trap verification (§18.2)

Answers the question accuracy cannot: *does a real image become a raised alert?* It runs every
document through the production path — upload → SHA-256 extraction cache → vision extraction →
merge → deterministic rule checks → LLM cross-check → openFDA — and asserts the planted traps in
[dataset/golden/traps.md](dataset/golden/traps.md) against the alerts that were actually persisted.
Only the database and object storage are substituted (in-memory and a scratch directory), so a run
leaves no patient data in the Supabase project.

```bash
dotnet run --project tools/MediTrail.GoldenRunner -- --traps

# Real model calls cost money — narrow the run while investigating one finding.
dotnet run --project tools/MediTrail.GoldenRunner -- --traps --patient=y
dotnet run --project tools/MediTrail.GoldenRunner -- --traps --trap=Y1   # implies its patient set
dotnet run --project tools/MediTrail.GoldenRunner -- --traps --verbose   # why a finding was dropped
```

It prints every alert per patient — type, severity, confidence, the medications and documents it
cites, whether a professional consult is flagged — then PASS/FAIL per trap with the observed value
on failure. A filtered run reports the traps it could not reach as SKIP; a skip is never a pass.

`--verbose` raises the pipeline's own logging to Debug. The reasons a cross-check finding was
dropped live there rather than at Information, because they name the patient's medications and must
not reach production logs.

The current results are recorded in the Detection status table in
[dataset/golden/traps.md](dataset/golden/traps.md) — all seven originally-planted traps now fire;
the all-document date sweep still catches one invented date (`patient_y_year1_1`), an open
extraction-prompt issue.

---

## Milestone status

| Milestone | State |
|---|---|
| **M1 — Foundation** | Done. Repo, both apps, schema, storage, upload persisting files and rows. Verified end to end against Supabase. |
| **M2 — Extraction proven** | Done, with one open defect. The trap harness runs all 16 dataset images through the real extraction path on the demo model; the accuracy figure for the technical summary comes from the golden runner. Open: `patient_y_year1_1` gets an invented `documentDate` on a handwritten script (the harness's DATES check fails on it) — a prompt fix pending re-measurement per §18.4. |
| **M3 — Intelligence complete** | Done for the medication path: merge, deterministic rule checks, LLM cross-check with component-wise grounding, openFDA verification (first `Confirmed` result: warfarin + aspirin), unresolved-medication flagging. All seven planted traps detected end to end (§18.2). Lab **trends** are built but undemonstrable on the judge dataset — it contains no longitudinal lab series (see traps.md). |
| **M4 — Application complete** | Done. All dashboard views, evidence viewer with per-finding citation marking, processing screen, chat drawer. Round 2 nearby-clinic drawer is behind `Features:DoctorRecommendation` (see above). |
| **M5 — Deployed** | Backend on Azure App Service, frontend served from the API's wwwroot; Supabase storage hardened and keepalive in place. Cold-path testing from an external network (§18.5) not yet performed. |
| M6 — Submitted | Not started. |

The golden-dataset accuracy test (§18.1) and the trap harness (§18.2) are the two quality gates;
both exit non-zero on failure and both must be green on the demo model before submission.

---

## Notes on choices made during setup

- **Microsoft.OpenApi pinned to 2.11.0.** The .NET 10 template pulls 2.0.0, which carries advisory
  GHSA-v5pm-xwqc-g5wc. 3.x breaks the ASP.NET OpenAPI source generator, so the fix is the newest 2.x.
- **`lucide-angular` is not installed.** It declares peer support only through Angular 21 and this is
  Angular 22. Icons are inline SVG / text glyphs for now; revisit when the package catches up.
- **Enums cross the wire as names**, not integers, so the frontend and API cannot silently disagree
  about severity if a member is ever inserted.
- **Columns are snake_case** to match the names the PRD uses throughout (`documents.raw_extraction_json`).
