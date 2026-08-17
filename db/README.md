# Database

Supabase PostgreSQL. Eight tables and one derived view — the six of PRD §12.3, plus two added
since:

- `diagnoses` — §12.3 lists it as deferred and additive. Added when the grounded chat could not
  answer "what was I given for malaria?" on a document that prints the word.
- `chat_messages` — §5.2 excluded chat persistence as having no demo value, which held until the
  drawer carried multi-turn context worth losing on close.

## Apply

Run in order in the Supabase SQL editor (or via `psql`):

1. `01_schema.sql` — generated from the EF Core migrations, idempotent. Creates `patients`,
   `documents`, `medications`, `diagnoses`, `lab_results`, `allergies`, `alerts`, `chat_messages`.
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
