# MediTrail

**AI Medical Report & Prescription Cross-Checker** — YGC AI Competition 2026, Round 1.
Team Dev HuHu.

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
  src/app/features/      Patients, upload, processing, dashboard, evidence
  src/app/shared/        Disclaimer, confidence badge
db/                      Schema SQL (generated) + views
docs/                    PRD
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
| **M4 — Application complete** | Done. All dashboard views, evidence viewer with per-finding citation marking, processing screen, chat drawer. |
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
