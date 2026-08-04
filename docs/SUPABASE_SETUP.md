# Connecting MediTrail to Supabase

MediTrail uses Supabase for two separate things, each with its own credential:

| What | Credential | Used by |
|---|---|---|
| Postgres database | connection string (username + password) | EF Core |
| Object storage (original files) | service-role key | `SupabaseStorageService` over the REST API |

Both are server-side only. Neither ever reaches the browser (§17.2).

---

## 1. Create the project

1. <https://supabase.com/dashboard> → **New project**.
2. Name it `meditrail`. Pick the region closest to you — Singapore or Mumbai from Sri Lanka.
3. Set a database password and **save it now**. Supabase shows it once; after that you can only reset it.
4. Wait for provisioning (~2 minutes).

---

## 2. Get the connection string

Click **Connect** in the top bar → **Session pooler** tab.

Take the host and username from there. It looks like:

```
Host:     aws-1-ap-southeast-1.pooler.supabase.com
Port:     5432
Database: postgres
Username: postgres.abcdefghijklmnop        <- project ref is part of the USERNAME
```

Assemble the .NET connection string:

```
Host=aws-1-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<your-db-password>;SSL Mode=Require;Trust Server Certificate=true
```

### Why session pooler and not the other two options

- **Direct connection** (`db.<ref>.supabase.co`) is **IPv6-only**. Most home connections and CI
  runners are IPv4, and it will fail with a confusing timeout rather than a clear error.
- **Transaction pooler** (port **6543**) hands a different backend connection to each transaction.
  That suits serverless functions; it breaks prepared statements and session state, which a
  long-lived ASP.NET Core process with EF Core relies on.
- **Session pooler** (port **5432** on the `pooler.supabase.com` host) holds one Postgres connection
  per client connection and is IPv4-reachable. That is what this app wants.

---

## 3. Get the service-role key

**Project Settings → API Keys**.

Copy the **`service_role`** key, not `anon`. The service role bypasses row-level security, which is
what lets the backend write to storage without a signed-in user.

> This key is equivalent to full database access. It belongs in server configuration only — never in
> `environment.ts`, never in a commit. `.gitignore` already excludes `.env`.

Also copy the **Project URL** (`https://<project-ref>.supabase.co`) from the same page.

---

## 4. Apply the schema

**SQL Editor → New query.** Paste and run, in this order:

1. `db/01_schema.sql`
2. `db/02_views.sql`

Both are idempotent — safe to re-run.

Verify under **Table Editor**: you should see `patients`, `documents`, `medications`,
`lab_results`, `allergies`, `alerts`.

---

## 5. Create the storage bucket

**Storage → New bucket.**

- Name: `documents` — must match `Supabase:Bucket`.
- **Public bucket: ON.**

Round 1 uses a public bucket so the evidence viewer can load source images directly with no signing
round trip. The rules exempt complex security (§5.2), and the production path — private bucket,
signed URLs, RLS — is documented in §16.3. Do not put real patient documents in it.

---

## 6. Give the values to the app

Locally, prefer **user-secrets** over `.env` — the values live outside the repository entirely, so
there is no file to accidentally commit:

```bash
cd backend/MediTrail.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=aws-1-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<pw>;SSL Mode=Require;Trust Server Certificate=true"
dotnet user-secrets set "Supabase:Url" "https://<ref>.supabase.co"
dotnet user-secrets set "Supabase:ServiceKey" "<service-role-key>"
```

For Azure App Service, set the same names as environment variables with `__` for `:`
(`ConnectionStrings__Postgres`, `Supabase__ServiceKey`). See `.env.example`.

---

## 7. Verify

```bash
cd backend/MediTrail.Api && dotnet run
```

Then:

```bash
curl http://localhost:5000/health/ready
```

Expected:

```json
{ "status": "ready", "database": "ok (empty)", "bucket": "ok" }
```

`ok (empty)` just means the schema is applied and no patients exist yet — that is the correct state
before your first upload.

End-to-end check:

```bash
curl -X POST http://localhost:5000/api/patients \
  -H "Content-Type: application/json" \
  -d '{"displayName":"Test Patient"}'
```

A `201` with an `id` means the database write path works. Delete it afterwards with
`curl -X DELETE http://localhost:5000/api/patients/<id>`.

---

---

## Keeping the project awake

**A free-tier Supabase project is paused after 7 days without a request.** Submit today, and a judge
opening the link next week finds every request failing. This is the most likely way the submission
dies, and it dies quietly.

`.github/workflows/keepalive.yml` pings the database every 6 hours. Enable it by pushing the
repository to GitHub and adding two secrets under **Settings → Secrets and variables → Actions**:

| Secret | Value |
|---|---|
| `SUPABASE_URL` | `https://<project-ref>.supabase.co` |
| `SUPABASE_SECRET_KEY` | the `sb_secret_...` key |
| `BACKEND_URL` | the deployed API URL — add after M5; the job skips this step until then |

It targets `/health/ready`, not `/health`. `/health` returns without touching Postgres or storage,
so it would wake the web app and let the database pause anyway.

Without GitHub, any external pinger works — UptimeRobot's free tier on a 5-minute interval against
`/health/ready` does the same job.

Before submitting, open the app once yourself. If the project has paused, the dashboard offers
**Restore** and it returns in a couple of minutes — you want to discover that beforehand, not during
judging.

## Storage hardening

The bucket carries a 10 MB file limit and accepts only `image/png`, `image/jpeg` and
`application/pdf`. The application checks both already; enforcing them at the bucket too means a
leaked service key cannot be used to fill the project with arbitrary files.

The bucket remains **public** for Round 1 so the evidence viewer can load source images without a
signing round trip (§16.3). Anyone holding a document URL can read it, so **do not upload real
patient records.** The production path — private bucket with signed URLs and row-level security —
is described in §17.2.

## Troubleshooting

### Everything above must come from the *same* project

By far the most expensive mistake, because none of its symptoms name it. Values mixed between two
projects produce `password authentication failed`, `Invalid Compact JWS`, and
`tenant/user ... not found` — three unrelated-looking errors with one cause.

Before copying anything, check the dashboard address bar reads
`/dashboard/project/<ref>` — **not** `/dashboard/org/<id>`, which is the organisation, not a project.

`scripts/setup-supabase.ps1` now verifies key ownership up front so this fails immediately and by name.

### Finding the pooler region

Take it from the Connect dialog. Do **not** infer it from the direct host's IPv6 prefix — for this
project that suggested `ap-south-1` while the pooler actually serves it from `ap-southeast-2`.

If the Connect dialog is unclear, `tenant/user <ref> not found` means wrong region; any other error
means the region is right.

---

| Symptom | Cause | Fix |
|---|---|---|
| `tenant/user ... not found` | Wrong pooler region, or ref from another project | Confirm both in the Connect dialog |
| `Invalid API key` / `owned by another Supabase project` | Key from a different project | Re-copy from `/dashboard/project/<ref>` |
| `Invalid Compact JWS` on storage | Same — key does not belong to this project | As above |
| `Timeout ... 28000ms` or `No such host` | Using the IPv6-only direct connection | Switch to the session pooler host |
| `password authentication failed for user "postgres"` | Username is missing the project ref, or password from another project | Username is `postgres.<project-ref>` |
| `the Url field is required` at startup, though the secret is set | Running outside Development, where user-secrets are not loaded | `$env:ASPNETCORE_ENVIRONMENT='Development'` |
| `relation "patients" does not exist` | Schema not applied | Run `db/01_schema.sql` |
| `bucket` reports `404` / `Bucket not found` | Bucket missing or misnamed | Create `documents`, or set `Supabase:Bucket` to match |
| `bucket` reports `401` / `403` | Using the `anon` key | Use the `service_role` key |
| Images 404 in the evidence viewer | Bucket is private | Toggle the bucket public, or set `Supabase:BucketIsPublic=false` and implement signed URLs |
| `ConnectionStrings:Postgres is not configured` at startup | Config never reached the app | `dotnet user-secrets list` to confirm; from a shell `.env` needs exporting, it is not auto-loaded |
