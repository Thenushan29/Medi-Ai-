# Database

Supabase PostgreSQL. Six tables (PRD §12.3) plus one derived view.

## Apply

Run in order in the Supabase SQL editor (or via `psql`):

1. `01_schema.sql` — generated from the EF Core migration, idempotent. Creates `patients`,
   `documents`, `medications`, `lab_results`, `allergies`, `alerts`.
2. `02_views.sql` — `v_patient_timeline`. Re-runnable.

```bash
psql "$MEDITRAIL_CONNECTION_STRING" -f db/01_schema.sql
psql "$MEDITRAIL_CONNECTION_STRING" -f db/02_views.sql
```

## Regenerating 01_schema.sql

`01_schema.sql` is **generated, not hand-edited**. The EF Core model in `backend/MediTrail.Api/Data`
is the source of truth. After changing an entity:

```bash
cd backend/MediTrail.Api
dotnet ef migrations add <Name> -o Data/Migrations
dotnet ef migrations script --idempotent -o ../../db/01_schema.sql
```

## Notes

- Columns are snake_case (`EFCore.NamingConventions`), matching the names used throughout the PRD.
- **Source of truth is `documents.storage_path` + `documents.raw_extraction_json`** (§12.2).
  `medications`, `lab_results`, `allergies` rebuild from raw extraction; `alerts` rebuilds from those.
  Re-processing after a prompt change therefore needs no re-upload.
- Every child row carries `document_id` and it is never nullable — evidence linking depends on it.
- Deleting a patient cascades to documents, records, and alerts. Stored files are removed separately
  by the application, since object storage is outside the transaction.
- Round 1 uses a **public** storage bucket and no row-level security; the rules exempt complex
  security (§5.2). Production path — private bucket, signed URLs, RLS — is documented in §17.2.
