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
  AiPipeline/            Queue, background worker, extraction seam
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
dotnet user-secrets set "OpenRouter:ApiKey" "<openrouter-key>"
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

## Milestone status

| Milestone | State |
|---|---|
| **M1 — Foundation** | Done. Repo, both apps, schema, storage, upload persisting files and rows. |
| **M2 — Extraction proven** | Next. `IDocumentExtractor` is the seam; `NotConfiguredDocumentExtractor` is the placeholder. Gates everything else. |
| M3 — Intelligence complete | Not started. Normalize/merge, rule checks, cross-check, openFDA, trends. |
| M4 — Application complete | Timeline and evidence viewer done; medications, lab trends, alerts and chat pending M3. |
| M5 — Deployed | Not started. |
| M6 — Submitted | Not started. |

**M2 gates everything** (§23): no downstream stage is built on unmeasured extraction quality.
The golden-dataset test against hand-labelled ground truth is the primary quality gate (§18.1).

---

## Notes on choices made during setup

- **Microsoft.OpenApi pinned to 2.11.0.** The .NET 10 template pulls 2.0.0, which carries advisory
  GHSA-v5pm-xwqc-g5wc. 3.x breaks the ASP.NET OpenAPI source generator, so the fix is the newest 2.x.
- **`lucide-angular` is not installed.** It declares peer support only through Angular 21 and this is
  Angular 22. Icons are inline SVG / text glyphs for now; revisit when the package catches up.
- **Enums cross the wire as names**, not integers, so the frontend and API cannot silently disagree
  about severity if a member is ever inserted.
- **Columns are snake_case** to match the names the PRD uses throughout (`documents.raw_extraction_json`).
