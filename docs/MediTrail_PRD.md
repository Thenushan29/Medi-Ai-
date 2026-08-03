# MediTrail — Product Requirements Document (PRD)

**Product:** MediTrail — AI Medical Report & Prescription Cross-Checker
**Team:** Dev HuHu (4 members · 1 developer: Roshan)
**Competition:** YGC AI Competition 2026 — Yarl IT Hub Innovation Festival, Round 1
**Version:** 1.0 · **Date:** August 2026
**Status:** Approved for build

---

## Table of Contents

1. Executive Summary
2. Problem Statement
3. Goals & Success Criteria
4. Users & Personas
5. Scope (In / Out)
6. Product Principles
7. Functional Requirements
8. User Stories & Acceptance Criteria
9. User Flows
10. Screen-by-Screen Specification
11. AI System Specification
12. Data Specification
13. API Specification
14. Technical Architecture
15. Non-Functional Requirements
16. Third-Party Integrations
17. Safety, Ethics & Compliance
18. Testing & Quality Strategy
19. Deployment & Environments
20. Cost Model
21. Risks & Mitigations
22. Team Responsibilities
23. Milestones & Timeline
24. Deliverables Checklist
25. Judging Criteria Traceability
26. Future Roadmap (Post Round 1)
27. Glossary
28. Open Questions & Decision Log

---

## 1. Executive Summary

MediTrail is a web application that turns a pile of scattered medical paperwork into one verified, understandable health timeline.

A user uploads photos or scans of medical documents collected across years and providers — prescriptions, lab reports, discharge summaries, doctor's notes. A vision-language model reads every document, structured extraction merges them into a single patient record, and a multi-stage analysis engine cross-checks the result for drug interactions, duplicate prescriptions, dosage conflicts, allergy contradictions, and lab values drifting out of range. Every finding is explained in plain English and Tamil, carries a confidence score, links back to the exact source document as evidence, and — where possible — is independently verified against the openFDA drug label database.

The product never diagnoses. It surfaces risks a human should check, and says clearly when it is unsure.

**One-line pitch:** *Your complete medical trail — read by AI, verified against official drug data, explained in your language, with the original document always one click away.*

---

## 2. Problem Statement

### 2.1 The real-world problem

Patients — especially in Sri Lanka and South Asia — accumulate medical records as **paper**, across **multiple unconnected providers**: government hospitals, private channeling centres, standalone labs, pharmacies. There is no shared electronic record between them.

Consequences:

| Problem | Impact |
|---|---|
| No single doctor sees the full medication history | Drug interactions go unnoticed |
| Same drug prescribed under different brand names by different doctors | Accidental double-dosing |
| Warnings written on one document are forgotten by the next visit | Contraindicated drugs prescribed |
| Lab values drift slowly across years | Trends invisible when each report is read alone |
| Reports are in clinical English | Patients cannot understand their own health data |

A pharmacist *can* catch these — but only for the documents physically in front of them at that moment, and only if the patient remembers to bring everything.

### 2.2 Why this is an AI problem

The inputs are **photographs of unstructured paper** in dozens of layouts, with brand names, abbreviations, handwriting, and inconsistent date formats. Traditional OCR extracts text but cannot understand that "Zoclar 500" is clarithromycin, that "1 Morning, 1 Night" means twice daily, or that "acetaminophen" and "Paracetamol" are the same molecule. Reasoning across documents — *"was this drug prescribed despite the allergy noted two years ago?"* — requires a language model.

### 2.3 Evidence from the competition dataset

Inspection of the judge-provided dataset (`Patient x`, `Patient y`, three years each, PNG/JPG scans) confirms deliberately planted traps:

- **Same-document contradiction:** a jaundice prescription lists Paracetamol while its own advice section warns *"avoid liver-toxic medications (e.g. acetaminophen)"* — the same drug under two names.
- **Cross-visit duplicates and dosage conflicts** across providers.
- **Longitudinal lab drift** across multiple visits.
- **Mixed quality:** clean printed prescriptions, photographed documents, varying layouts and date formats.

The dataset is **images, not text PDFs**. This is the single most important technical constraint and drives the vision-first extraction design.

---

## 3. Goals & Success Criteria

### 3.1 Product goals

| # | Goal |
|---|---|
| G1 | Extract structured medical data reliably from messy real-world document images |
| G2 | Merge multi-visit, multi-provider documents into one chronological patient record |
| G3 | Detect and clearly explain medication risks and lab trends |
| G4 | Never present an unverified or low-confidence finding as certain |
| G5 | Make findings understandable to a non-medical person, in English and Tamil |
| G6 | Make every AI output traceable to its source document |

### 3.2 Competition goals

| # | Goal |
|---|---|
| C1 | Satisfy every stated Round 1 requirement (see §25 traceability) |
| C2 | Score maximally on AI Depth (30%) and Technical Execution (30%) |
| C3 | Deliver a publicly accessible, working demo link |
| C4 | Advance to Round 2 |

### 3.3 Measurable success criteria

| Metric | Target |
|---|---|
| Field-level extraction accuracy vs hand-labelled ground truth | ≥ 95% on printed documents, ≥ 80% on photographed/blurry |
| Planted traps in judge dataset detected | 100% |
| End-to-end processing time, 10-document patient | < 3 minutes |
| Chat answer latency | < 8 seconds |
| Hallucinated medications (drugs reported that are not in any document) | 0 |
| Demo link uptime during judging window | 100% |
| Total AI spend for build + demo | < $8 |

---

## 4. Users & Personas

### 4.1 Primary persona — "The Family Record-Keeper"

A working adult managing an older parent's health. Holds a folder of prescriptions and lab reports from four different places over three years. Understands basic English, more comfortable in Tamil. Not medically trained. Wants to know: *is anything here dangerous, and what should I ask the doctor?*

**Needs:** upload without friction, plain-language answers, a clear "show this to the doctor" summary.

### 4.2 Secondary persona — "The Patient"

Managing a chronic condition, sees multiple specialists, cannot remember what was prescribed when. Wants a single timeline and to know if their numbers are getting worse.

### 4.3 Tertiary persona — "The Pharmacist / GP"

Uses it as a fast second pair of eyes on a stack of documents a patient brings in. Cares about evidence links and verification sources, not marketing claims.

### 4.4 Evaluation persona — "The Judge"

Three-member panel. Will upload their own dataset, look for shallow AI integration, test edge cases, and ask *"how do you know the AI didn't make this up?"* Every design decision in this PRD should have a defensible answer for this persona.

---

## 5. Scope

### 5.1 In scope (Round 1)

- Patient profile creation (name only, no authentication)
- Multi-file upload: PNG, JPG, JPEG, PDF
- AI extraction of medications, lab results, allergies, diagnoses, in-document warnings, provider and date metadata
- Merged chronological timeline per patient
- Duplicate prescription detection
- Dosage conflict detection
- Drug–drug interaction detection
- Allergy / contraindication conflict detection
- Lab trend analysis with drift detection and plain-language explanation
- openFDA verification of interaction findings
- Confidence scoring at field, alert, and answer level
- Bilingual explanations (English + Tamil)
- Evidence viewer linking every finding to its source document image
- Grounded Q&A chat with citations and confidence
- Processing status screen with visible pipeline stages
- Publicly deployed, working application

### 5.2 Out of scope (Round 1) — deliberate, with rationale

| Excluded | Rationale |
|---|---|
| User authentication / login | Rules state complex security is not required. Patient profiles satisfy multi-patient needs at zero complexity cost. Production path documented. |
| Handwriting-first OCR (Azure Document Intelligence) | Dataset is predominantly printed; vision LLM handles it. Adds a second failure point and integration time for marginal gain. Evaluated and consciously deferred. |
| Vector database / RAG retrieval | Per-patient corpus is ~10–15 documents. Grounding on the complete structured record plus raw extractions is more accurate than top-k retrieval at this scale. Schema is pgvector-ready. |
| Chat history persistence | No demo or evaluation value; client-side state is sufficient. |
| Mobile native apps | Responsive web is sufficient. |
| Real-time collaboration, sharing, export to PDF | Not required by rules; time better spent on AI depth. |
| Medication scheduling / reminders | Different product. |
| Diagnosis, treatment recommendation, dosage adjustment | **Explicitly prohibited by the rules and by product ethics.** |

### 5.3 Explicitly forbidden behaviours

The product must never:
- State or imply a diagnosis
- Recommend starting, stopping, or changing a medication or dose
- Present a low-confidence extraction as fact
- Report a medication, value, or date not present in an uploaded document
- Remove a risk flag merely because external verification did not confirm it

---

## 6. Product Principles

These resolve design disputes. When a decision is unclear, the higher principle wins.

1. **Honesty over completeness.** "This is unreadable — show it to your pharmacist" is a better output than a confident guess. Uncertainty is a feature, not a failure.
2. **Deterministic where possible, AI where necessary.** Date parsing, unit normalization, duplicate detection, range checks, and trend math are code. Reading messy images, clinical reasoning, and plain-language explanation are AI. Never use an LLM for arithmetic.
3. **Every claim carries its evidence.** No finding appears without a path back to the source document.
4. **Verify AI with independent sources.** The LLM proposes; openFDA and deterministic rules corroborate. Disagreement is surfaced, not hidden.
5. **Complexity budget goes to the AI pipeline.** Architecture, tooling, and infrastructure stay deliberately simple so that the differentiating work gets the time.
6. **Local by design.** Tamil is a first-class output language, not a translation afterthought.

---

## 7. Functional Requirements

Priority: **P0** = must ship, **P1** = should ship, **P2** = if time permits.

### 7.1 Patient management

| ID | Requirement | Priority |
|---|---|---|
| FR-1.1 | Create a patient profile with a display name | P0 |
| FR-1.2 | List all patient profiles with document count and last activity | P0 |
| FR-1.3 | Open a patient to view their dashboard | P0 |
| FR-1.4 | Delete a patient and all associated data | P1 |

### 7.2 Document upload

| ID | Requirement | Priority |
|---|---|---|
| FR-2.1 | Upload multiple files at once via drag-and-drop or file picker | P0 |
| FR-2.2 | Accept PNG, JPG, JPEG, PDF; reject others with a clear message | P0 |
| FR-2.3 | Enforce per-file size limit (10 MB) with client-side downscaling above 2000px | P0 |
| FR-2.4 | Optionally tag uploads with a visit label ("Year 1", a date) | P1 |
| FR-2.5 | Store the original file; never discard it after extraction | P0 |
| FR-2.6 | Compute SHA-256 per file; reuse cached extraction on exact re-upload | P0 |
| FR-2.7 | Render PDF pages to images and process them through the same pipeline | P1 |
| FR-2.8 | Return immediately after upload; processing continues in background | P0 |

### 7.3 AI extraction

| ID | Requirement | Priority |
|---|---|---|
| FR-3.1 | Extract every document into the canonical schema (§12.1) | P0 |
| FR-3.2 | Extract medications with brand name, generic name, strength, frequency, duration, instructions | P0 |
| FR-3.3 | Extract lab results with value, unit, normal range, test date | P0 |
| FR-3.4 | Extract allergies and in-document warnings, with the substances they relate to | P0 |
| FR-3.5 | Extract diagnoses, provider, and document date | P0 |
| FR-3.6 | Return `null` rather than guessing any unclear value | P0 |
| FR-3.7 | Assign a confidence score per field and per document | P0 |
| FR-3.8 | Record which AI model produced each extraction | P1 |
| FR-3.9 | Validate output against the typed schema; retry once on malformed JSON | P0 |
| FR-3.10 | Mark a document `failed` with a readable reason after retries exhaust | P0 |

### 7.4 Normalization & merge

| ID | Requirement | Priority |
|---|---|---|
| FR-4.1 | Normalize dates to ISO; null on ambiguity | P0 |
| FR-4.2 | Normalize drug names to lowercase generic where identifiable | P0 |
| FR-4.3 | Normalize lab test names to a standard grouping key | P0 |
| FR-4.4 | Normalize units and frequency to comparable numeric forms | P0 |
| FR-4.5 | Merge all documents into one chronological timeline per patient | P0 |
| FR-4.6 | Preserve the original source text for every extracted item | P0 |

### 7.5 Cross-checking & analysis

| ID | Requirement | Priority |
|---|---|---|
| FR-5.1 | Detect the same generic prescribed in overlapping periods (duplicate) | P0 |
| FR-5.2 | Detect the same generic with conflicting strength or frequency (dosage conflict) | P0 |
| FR-5.3 | Detect drug–drug interaction risks across the full medication history | P0 |
| FR-5.4 | Detect medications conflicting with recorded allergies | P0 |
| FR-5.5 | Detect medications conflicting with warnings printed on any document, including the same document | P0 |
| FR-5.6 | Verify interaction findings against openFDA; attach an excerpt when confirmed | P0 |
| FR-5.7 | Never suppress a finding because verification failed; mark it unverified | P0 |
| FR-5.8 | Assign severity (red / amber / info) and confidence to every alert | P0 |
| FR-5.9 | Raise a low-confidence alert when extraction quality is poor for any document | P1 |

### 7.6 Lab trend analysis

| ID | Requirement | Priority |
|---|---|---|
| FR-6.1 | Group lab results by standardized test name across all visits | P0 |
| FR-6.2 | Compute the value series ordered by test date | P0 |
| FR-6.3 | Flag values outside the normal range recorded on the document | P0 |
| FR-6.4 | Detect direction of drift across three or more points | P0 |
| FR-6.5 | Generate a plain-language explanation of the trend in English and Tamil | P0 |
| FR-6.6 | Render each test as a chart with a shaded normal-range band | P0 |

### 7.7 Grounded chat

| ID | Requirement | Priority |
|---|---|---|
| FR-7.1 | Answer questions using only the patient's uploaded documents | P0 |
| FR-7.2 | Reason across multiple documents in a single answer | P0 |
| FR-7.3 | Return citations identifying the source documents used | P0 |
| FR-7.4 | Return a confidence score with every answer | P0 |
| FR-7.5 | State plainly when the answer is not present in the documents | P0 |
| FR-7.6 | Set a "consult a professional" flag on any high-risk or low-confidence answer | P0 |
| FR-7.7 | Offer suggested starter questions, including the allergy-reasoning example from the rules | P1 |
| FR-7.8 | Provide answers in English and Tamil | P1 |

### 7.8 Presentation & UX

| ID | Requirement | Priority |
|---|---|---|
| FR-8.1 | Show live processing progress with named pipeline stages | P0 |
| FR-8.2 | Dashboard with Timeline, Medications, Lab Trends, and Alerts views | P0 |
| FR-8.3 | Summary chips: risk counts, document count, time span | P0 |
| FR-8.4 | Evidence viewer showing the source image beside extracted data | P0 |
| FR-8.5 | English / Tamil toggle on every AI explanation | P0 |
| FR-8.6 | Confidence indicator on every extracted item, alert, and answer | P0 |
| FR-8.7 | Persistent medical disclaimer on every screen | P0 |
| FR-8.8 | Empty states, skeleton loaders, and readable error messages | P1 |
| FR-8.9 | Responsive layout usable on a laptop and tablet | P1 |

---

## 8. User Stories & Acceptance Criteria

**US-1 — Upload a stack of documents**
*As a family record-keeper, I want to upload all my parent's reports at once so I don't have to do them one by one.*
✅ Multiple files selectable in one action · ✅ Each file shows individual progress · ✅ Unsupported formats rejected before upload with a clear reason · ✅ Upload returns control immediately; processing continues in background.

**US-2 — Understand what was found**
*As a patient, I want to see everything that was found in my documents so I can confirm nothing important was missed.*
✅ Timeline shows every visit chronologically · ✅ Each entry shows extracted medications and results · ✅ Clicking any entry opens the source document image · ✅ Low-confidence items visually distinct.

**US-3 — Be warned about medication risk**
*As a patient, I want to be told when two of my medications may be dangerous together.*
✅ Interaction alerts listed by severity · ✅ Each states which drugs and why, in non-clinical language · ✅ openFDA verification badge shown when confirmed · ✅ Unverified alerts labelled "verify with pharmacist", not hidden · ✅ Every alert links to its source documents.

**US-4 — Catch a contradiction across time**
*As a patient, I want to know if a drug was prescribed despite a warning recorded elsewhere in my records.*
✅ Warnings on any document are matched against all medications, including within the same document · ✅ Brand and generic names are matched as equivalents (Paracetamol ≡ acetaminophen) · ✅ Alert names both documents involved.

**US-5 — See whether my numbers are getting worse**
*As a patient, I want to see how my lab values have changed over the years.*
✅ Each test charted across all visits · ✅ Normal range shown as a band · ✅ Out-of-range points highlighted · ✅ Drift explained in one plain sentence · ✅ Tamil available for every explanation.

**US-6 — Ask a follow-up question**
*As a patient, I want to ask questions in my own words and get answers based on my actual documents.*
✅ Answers cite the documents used · ✅ Confidence shown · ✅ Out-of-scope questions answered with "not found in your documents" · ✅ Risk-related answers carry a consult-a-professional banner.

**US-7 — Trust what I'm being told**
*As a cautious user, I want to verify the AI read my documents correctly.*
✅ Any extracted value traces to its source image in one click · ✅ Original text shown alongside the normalized value · ✅ Confidence visible everywhere · ✅ Unreadable content reported as unreadable, never filled in.

**US-8 — Evaluate the system (judge)**
*As a judge, I want to upload my own dataset and probe the system's limits.*
✅ Works with unseen documents · ✅ Handles rotated, blurred, and mixed-format inputs gracefully · ✅ Failure states are informative, not silent · ✅ Architecture and AI usage are explainable on demand.

---

## 9. User Flows

### 9.1 Primary flow

```
Landing / Patients list
   → Create patient (name)
   → Upload documents (multi-file, optional visit labels)
   → Processing screen (live pipeline stages, per-document status)
   → Dashboard
        ├─ Timeline    → click entry → Evidence viewer
        ├─ Medications → conflict rows highlighted → Evidence viewer
        ├─ Lab Trends  → chart + explanation → EN/TA toggle
        ├─ Alerts      → severity-sorted → explanation, verification, evidence
        └─ Chat drawer → question → answer + citations + confidence
```

### 9.2 Processing flow (system)

```
Upload accepted
   → File written to object storage
   → Document row created (status: uploaded), hash computed
   → Document id enqueued; HTTP response returned
   → Worker, per document:
        hash cache hit? → reuse extraction (status: cached)
        else → vision extraction → schema validation → status: extracted
   → When all documents for the patient are extracted, once per patient:
        → Normalize & merge into structured tables
        → Deterministic checks (duplicates, dosage conflicts, range flags)
        → LLM cross-check (interactions, allergy/warning conflicts)
        → openFDA verification of each finding
        → Trend computation + bilingual explanations
        → Alerts persisted; patient marked ready
```

### 9.3 Failure flows

| Failure | Behaviour |
|---|---|
| Extraction returns malformed JSON | Retry once with stricter instruction; then mark document `failed`, continue with remaining documents |
| Document unreadable | Extraction succeeds with nulls and low confidence; low-confidence alert raised; document still visible in the timeline |
| openFDA unavailable or drug not found | Alert retained, marked unverified; pipeline continues |
| AI provider error or rate limit | Exponential backoff, 3 attempts; document marked `failed` with reason after exhaustion |
| No documents successfully extracted | Dashboard shows an explanatory empty state, not a blank screen |
| Question outside document scope | Answer states the information is not in the uploaded documents |

---

## 10. Screen-by-Screen Specification

**Design language:** clean clinical. White background, teal/emerald primary, soft grey surfaces, generous spacing, Inter typeface, lucide icons. Traffic-light colours reserved exclusively for severity — never decorative. No gradient-heavy "AI product" clichés.

### 10.1 Patients

Purpose: entry point and multi-patient support.
Contents: patient cards (name, document count, last updated, risk chip), "New patient" action, product tagline, disclaimer footer.
Empty state: short explanation of what MediTrail does plus a single call to action.

### 10.2 Upload

Purpose: get documents in with minimum friction.
Contents: large drag-and-drop zone, file list with thumbnails and remove action, optional visit label per file or group, accepted-format hint, primary action "Analyze my records".
Validation: format and size checked before upload; oversized images downscaled client-side.

### 10.3 Processing

Purpose: make the AI work visible — this screen is a feature, not a spinner.
Contents: vertical stepper with named stages (Reading documents → Building timeline → Cross-checking medications → Verifying against drug data → Analyzing lab trends), per-document tick list, elapsed time, reassurance copy.
Behaviour: polls status every 2 seconds; on completion routes to the dashboard.

### 10.4 Dashboard — shell

Header: patient name, document count, time span covered, summary chips (red risks, amber items, documents, years).
Global: EN/TA toggle, disclaimer footer, chat drawer trigger.

### 10.5 Dashboard — Timeline

Vertical chronological cards grouped by visit: date, provider, document type, key extracted items, confidence indicator. Clicking opens the evidence viewer.

### 10.6 Dashboard — Medications

Table grouped by generic name: brand, generic, strength, frequency, duration, prescriber, period. Conflict and duplicate rows highlighted with an inline reason. Each row links to its source document.

### 10.7 Dashboard — Lab Trends

One chart per standardized test: value series over time, shaded normal-range band, out-of-range points emphasized, drift indicator, one-sentence AI explanation below with language toggle, confidence indicator.

### 10.8 Dashboard — Alerts

Severity-sorted cards. Each shows: title, involved medications, plain-language explanation with EN/TA toggle, confidence meter, verification badge ("Verified against FDA label data" or "AI-flagged — verify with pharmacist"), "View evidence" action, and a consult-a-professional strip on red or low-confidence items.

### 10.9 Evidence viewer

Split view: source document image (zoomable) on one side, extracted data on the other, with the original source text shown beside each normalized value. Multi-document alerts show all involved documents.

### 10.10 Chat drawer

Persistent right-side drawer. Suggested question chips on open. Messages show answer text, language toggle, confidence badge, citation chips linking to source documents, and an automatic consult banner when flagged. Input disabled with an explanatory message while processing is incomplete.

---

## 11. AI System Specification

### 11.1 Pipeline stages

| Stage | Type | Responsibility |
|---|---|---|
| 1. Extraction | Vision LLM | Document image → canonical schema JSON with per-field confidence |
| 2. Normalize & merge | Deterministic code | Dates, units, generic names, standardized test keys; merge into one record |
| 3. Rule checks | Deterministic code | Duplicates, dosage conflicts, out-of-range flags |
| 4. Cross-check | LLM | Interactions, allergy and warning conflicts, severity, bilingual explanation |
| 5. Verification | External API | openFDA label lookup confirming or failing to confirm each finding |
| 6. Trend analysis | Code + LLM | Series and drift computed in code; explanation generated by LLM |
| 7. Grounded Q&A | LLM | Answers constrained to the patient record, with citations and confidence |

### 11.2 Model strategy

- Primary model: a cost-efficient vision-capable model accessed via OpenRouter, configured in `appsettings.json` and swappable without code changes.
- Temperature 0 for all extraction and cross-checking; reasoning/thinking tokens disabled where configurable.
- `max_tokens` capped per call to prevent runaway cost.
- Model identifier recorded with every extraction for traceability.

### 11.3 Prompt design rules

Applied to all extraction prompts:
- Return only valid JSON matching the provided schema — no prose, no code fences.
- Never guess. Unclear values return `null` with lowered confidence and a note explaining why.
- Map brand names to generics only when confident; otherwise return `null`.
- Normalize dates to ISO; ambiguous dates return `null`.
- Preserve the exact printed source text for every extracted item.
- Extract warnings printed on the document along with the substances they reference.

Applied to reasoning and chat prompts:
- Use only the supplied patient record; never introduce outside medical facts as findings about this patient.
- Never diagnose, never recommend starting or changing medication.
- Flag for professional consultation on any high-risk or low-confidence output.
- Produce English and Tamil versions of every user-facing explanation.

### 11.4 Confidence model

Confidence is composed, not merely self-reported:

| Layer | Source |
|---|---|
| Field confidence | Model self-assessment based on legibility |
| Consistency adjustment | Deterministic checks: does the same item appear in other documents, is the date sequence coherent, is the dosage in a plausible range |
| Verification adjustment | Does the generic resolve against openFDA |

Presentation mapping:

| Composite score | Indicator | Behaviour |
|---|---|---|
| ≥ 80 | High | Displayed normally |
| 50–79 | Medium | "Verify with a pharmacist" note |
| < 50, or any red-severity alert | Low / Risk | Prominent consult-a-professional banner |

### 11.5 Anti-hallucination controls

1. Strict JSON schema with typed deserialization; malformed output retried once, then failed rather than salvaged.
2. Explicit instruction to return `null` instead of guessing, reinforced in every prompt.
3. Chat answers constrained to the stored record; "not found in your documents" is an accepted and expected answer.
4. Independent verification of interaction findings against openFDA.
5. Evidence linking so any error is visible to the user immediately.
6. Golden-dataset accuracy testing before the pipeline is trusted (§18).

### 11.6 Cost controls

SHA-256 extraction caching, temperature 0, capped output tokens, capped retries, cached openFDA lookups, and a configured provider spend limit.

---

## 12. Data Specification

### 12.1 Canonical extraction schema

Every document type is extracted into a single shared shape; inapplicable sections return empty arrays rather than a different structure.

Top-level sections: `documentType`, `documentDate` (+ confidence), `provider`, `patient`, `diagnoses[]`, `medications[]`, `labResults[]`, `allergies[]`, `warningsInDocument[]`, `clinicalNotes`, `followUpDate`, `overallConfidence`, `legibilityNotes`, `unreadableSections[]`.

Key field rules:
- `medications[]`: `brandName`, `genericName` (join key), `strengthValue`, `strengthUnit`, `dose`, `frequency`, `frequencyPerDay`, `route`, `durationDays`, `instructions`, `sourceText`, `confidence`.
- `labResults[]`: `testName`, `testNameStandard` (grouping key), `valueNumeric`, `valueText`, `unit`, `normalMin`, `normalMax`, `normalRangeText`, `testDate`, `sourceText`, `confidence`.
- `allergies[]`: `substance`, `substanceGeneric`, `reaction`, `severity`, `sourceText`, `confidence`.
- `warningsInDocument[]`: `text`, `relatesTo[]` (generic names referenced), `confidence`. **Required to detect same-document contradictions.**

Full annotated schema: see the Technical Spec document.

### 12.2 Storage tiers

| Tier | Location | Mutability |
|---|---|---|
| Original files | Object storage, path `{patient_id}/{document_id}.{ext}` | Immutable |
| Raw extraction | `documents.raw_extraction_json` (JSONB) | Immutable except on re-processing |
| Normalized records | `medications`, `lab_results`, `allergies` | Rebuildable from raw |
| Derived findings | `alerts` | Rebuildable from normalized |

**Source of truth:** original file plus raw extraction. Everything downstream can be deleted and recomputed — essential during prompt tuning.

### 12.3 Database tables (Phase 1)

| Table | Purpose |
|---|---|
| `patients` | Profile; scopes all data |
| `documents` | File location, hash, processing status, raw extraction, document metadata |
| `medications` | One row per prescribed drug per document |
| `lab_results` | One row per test value per document |
| `allergies` | Patient allergies and in-document warnings (distinguished by a flag) |
| `alerts` | Cross-check findings with evidence references and verification state |

Deferred, additive: `diagnoses`, `drug_reference_cache`, `ai_model_executions`.

Every child row carries `document_id` — evidence linking depends on it and it is never optional.
`v_patient_timeline` is a view, not a table: the timeline is derived, never stored twice.

### 12.4 Retention

Round 1 stores data indefinitely for demonstration. Patient deletion cascades to all documents, records, alerts, and stored files.

---

## 13. API Specification

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/patients` | Create a patient profile |
| GET | `/api/patients` | List profiles with summary counts |
| GET | `/api/patients/{id}` | Profile detail |
| DELETE | `/api/patients/{id}` | Delete profile and all data |
| POST | `/api/patients/{id}/documents` | Multipart upload; queues processing; returns immediately |
| GET | `/api/patients/{id}/status` | Processing stage, per-document status, counts |
| GET | `/api/patients/{id}/timeline` | Merged chronological events |
| GET | `/api/patients/{id}/medications` | Grouped by generic with periods and conflict markers |
| GET | `/api/patients/{id}/labs` | Series per test with normal band and drift information |
| GET | `/api/patients/{id}/alerts` | Alerts with evidence, verification, confidence |
| GET | `/api/documents/{id}` | Document metadata, source URL, extracted items |
| POST | `/api/patients/{id}/ask` | Grounded question; returns answer, citations, confidence, consult flag |

Conventions: JSON request and response, typed DTOs distinct from entities, consistent error envelope, Swagger enabled in all environments, CORS restricted to the deployed frontend origin.

---

## 14. Technical Architecture

### 14.1 Stack

| Layer | Technology |
|---|---|
| Frontend | Angular (standalone components), Tailwind CSS, ApexCharts, lucide icons |
| Backend | ASP.NET Core (.NET 10) Web API, EF Core with Npgsql |
| Database | Supabase PostgreSQL |
| File storage | Supabase Storage |
| AI | OpenRouter (vision-capable model, configurable) |
| Drug reference | openFDA Drug Label API |
| Hosting | Frontend on Vercel; backend on Azure App Service free tier |
| Development | Cursor; single monorepo, solo developer, no long-lived branches |

### 14.2 Architectural style

Layered monolith with strict one-way dependencies: `Controllers → Services → AiPipeline / Data`. External dependencies sit behind interfaces (`IAiClient`, `IOpenFdaClient`) so providers can be swapped by configuration.

**Deliberate exclusions with rationale:**

| Not used | Why |
|---|---|
| Clean Architecture multi-project layout | Domain logic is thin; the real complexity is the AI pipeline. Layer separation is enforced by folders and dependency rules, giving the testability benefit without the project ceremony. |
| CQRS / MediatR | Read and write models are not divergent enough to justify handler-per-operation boilerplate at this scale. |
| Repository pattern | EF Core's DbContext already implements repository and unit-of-work. A second abstraction adds indirection without benefit. |
| External message broker | An in-process channel plus a persisted status column provides durable-enough queueing for this volume, with zero infrastructure. Swappable behind an interface if scaled. |

### 14.3 Background processing

An in-process channel feeds a hosted background worker. Processing state lives in `documents.status`, so a restart cannot lose track of work: pending documents are re-enqueued on startup. Per-document extraction runs first; patient-level analysis runs once all documents reach a terminal state.

### 14.4 Resilience

Retry with exponential backoff on all external calls; capped attempts; JSON repair with a single stricter retry; per-document failure isolation so one bad file cannot fail a batch; openFDA treated as optional enhancement, never a hard dependency.

---

## 15. Non-Functional Requirements

| Category | Requirement |
|---|---|
| Performance | Upload response < 2s; extraction < 25s per document; full 10-document analysis < 3 min; dashboard reads < 500ms; chat answer < 8s |
| Reliability | A single document failure never aborts a batch; all state recoverable after restart |
| Scalability | Schema and pipeline handle hundreds of documents per patient without redesign; scaling path documented |
| Usability | Primary flow completable without instructions; every AI output legible to a non-medical reader |
| Accessibility | Sufficient colour contrast; severity never conveyed by colour alone (icon and text accompany it); keyboard-navigable primary flow |
| Observability | Structured logging of pipeline stages, model identity, token usage, latency, and failures |
| Maintainability | Prompts stored as files, not inline strings; models and endpoints configured, not hard-coded |
| Portability | Runs locally with a connection string and API key; no host-specific dependencies |
| Cost | Total AI spend under $8 for build, testing, and demonstration |

---

## 16. Third-Party Integrations

### 16.1 OpenRouter

Purpose: access to a vision-capable language model. Auth via API key held in server-side configuration and never exposed to the client. Model identifier configurable. Failure handling: backoff and retry; document marked failed on exhaustion. Spend limit configured at the provider.

### 16.2 openFDA Drug Label API

Purpose: independent verification of interaction and contraindication findings. Public HTTP API; no key required to begin, with an optional free key raising daily limits.

Integration rules:
- Query by **generic name only** — the label database will not resolve regional brand names.
- Cache every lookup; each generic is fetched once.
- "Not found" is a normal result, not an error.
- Store only a short excerpt with attribution; never reproduce label text at length.
- A failed or unavailable lookup must not remove a finding or block the pipeline.

### 16.3 Supabase

PostgreSQL over a pooled connection, plus object storage for original documents. Round 1 uses a public bucket; production path uses a private bucket with signed URLs and row-level security.

---

## 17. Safety, Ethics & Compliance

### 17.1 Medical safety

- The product presents information, never a diagnosis. This is stated in a persistent disclaimer on every screen and enforced in every prompt.
- No recommendation to start, stop, or change any medication or dose.
- Red-severity and low-confidence outputs always carry an explicit instruction to consult a doctor or pharmacist.
- Uncertainty is surfaced rather than smoothed over; a null is preferred to a plausible guess.
- Findings are never suppressed because external verification was unavailable.

### 17.2 Data handling

- Documents contain sensitive personal health information. Files are stored only for the demonstration; deletion cascades completely.
- No sharing with third parties beyond the AI provider necessary to process the document, and the drug reference API which receives only generic drug names — never patient data.
- API keys held server-side only.
- Production path: private storage with signed URLs, row-level security, field-level encryption of identifiers, and an immutable audit trail.

### 17.3 Attribution

Drug label excerpts are attributed to the FDA label source. AI-generated content is labelled as such wherever it appears.

---

## 18. Testing & Quality Strategy

### 18.1 Golden dataset test — the primary quality gate

All documents in the evaluation dataset are hand-labelled into expected JSON files. A test runner executes the extraction pipeline and compares output field by field, reporting accuracy per category (medication names, strengths, frequencies, lab values, dates, allergies).

This produces the headline number for the technical summary: *field-level extraction accuracy against hand-labelled ground truth*. No downstream stage is trusted until targets are met.

### 18.2 Trap verification

Every deliberately planted issue in the dataset is enumerated and must be detected: same-document contradictions, cross-visit duplicates, dosage conflicts, allergy triggers, and lab drift. This list doubles as the demonstration script.

### 18.3 Robustness testing

Rotated images, low-resolution photographs, unrelated non-medical images, empty uploads, oversized files, duplicate uploads, malformed PDFs, and mid-processing page refreshes.

### 18.4 Regression protection

Prompt changes require re-running the golden dataset before acceptance. Because derived tables rebuild from stored raw extractions, re-processing requires no re-upload.

### 18.5 Cold-path testing

The deployed application is tested from a cold start on the public URLs, on a different network and machine from the development environment, before submission.

---

## 19. Deployment & Environments

| Environment | Frontend | Backend | Database |
|---|---|---|---|
| Local | Angular dev server | .NET local | Shared Supabase project |
| Production | Vercel | Azure App Service (free tier) | Same Supabase project |

Configuration: connection string, AI API key, model identifier, allowed CORS origin, and storage bucket name supplied as environment variables. No secret is committed to the repository.

Operational notes: the free hosting tier sleeps when idle — an uptime ping is configured for the submission and judging windows, and the application is warmed before any live demonstration. SPA routing requires a rewrite rule so deep links resolve. A recorded walkthrough is kept as a fallback for network failure during demonstration.

---

## 20. Cost Model

| Item | Cost |
|---|---|
| Database and storage | Free tier |
| Backend hosting | Free tier |
| Frontend hosting | Free tier |
| Drug reference API | Free |
| AI inference | Pay-per-token; the dominant and only meaningful cost |

Per-document extraction cost is a fraction of a cent; a full multi-document patient analysis is a few cents. With extraction caching, the total build, testing, and demonstration budget is expected to remain well under the allocated amount. A provider-side spend limit is configured as a hard stop.

---

## 21. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Extraction accuracy insufficient on photographed documents | Critical — everything downstream depends on it | Built and validated first, before any other stage; golden-dataset measurement; prompt iteration; confidence-first design so poor input degrades gracefully instead of producing wrong facts |
| Model hallucinates a medication or interaction | Critical — safety and credibility | Null-not-guess instruction, schema validation, independent openFDA verification, evidence linking, grounded-only chat |
| Judges upload formats not seen in testing | High | Broad format acceptance, client-side downscaling, rotation testing, informative failure states |
| Free-tier cold start during judging | High | Uptime ping, pre-warming, recorded fallback walkthrough |
| AI provider outage or rate limiting | High | Backoff and retry, extraction caching, configurable model so an alternative can be selected quickly |
| Malformed JSON breaking the pipeline | Medium | Strict schema prompting, single stricter retry, per-document failure isolation |
| Scope creep consuming the schedule | Medium | Priority levels in §7; P2 items dropped without discussion if the schedule slips |
| Single-developer bottleneck | Medium | Non-code deliverables owned by teammates; solo repository avoids merge overhead |
| openFDA cannot resolve regional brand names | Low | Generic-only lookup by design; unresolved findings marked unverified rather than dropped |

---

## 22. Team Responsibilities

| Role | Owner | Responsibilities |
|---|---|---|
| Development | Roshan | Entire application: backend, pipeline, frontend, deployment |
| Ground-truth labelling | Teammate | Hand-label the evaluation dataset into expected JSON; enumerate planted traps |
| Quality assurance | Teammate | Adversarial testing per §18.3; log defects with reproduction steps |
| Documentation | Teammate | Technical summary PDF drafted from this PRD and the work plan |
| Presentation | Teammate | Slide deck, demo timing, rehearsal |
| Tamil review | Teammate | Verify AI-generated Tamil explanations read naturally and accurately |

Single-developer implementation is a deliberate choice to eliminate merge conflicts under a compressed schedule; all non-code work is parallelized.

---

## 23. Milestones & Timeline

| Milestone | Definition of done |
|---|---|
| M1 — Foundation | Repository, both applications scaffolded, database schema applied, storage bucket created, upload persisting files and rows |
| M2 — Extraction proven | Real dataset images producing valid canonical-schema JSON; accuracy measured against ground truth and meeting target |
| M3 — Intelligence complete | Merge, rule checks, cross-check, verification, and trends producing correct alerts on the full dataset; all planted traps detected |
| M4 — Application complete | All screens implemented; full flow usable end to end with real data |
| M5 — Deployed | Public URLs working from a cold start on an external network |
| M6 — Submitted | Demo link, technical summary PDF, and presentation submitted and confirmed |

M2 gates everything: no downstream stage is built on unmeasured extraction quality.

---

## 24. Deliverables Checklist

- [ ] Publicly accessible demo application link
- [ ] Technical summary PDF (2–4 pages): stack, design and architecture, AI integration, key features, challenges, unique value
- [ ] Presentation (4–5 minutes): product demonstration on the provided dataset, and explanation of tools used
- [ ] All artifacts uploaded and confirmed on the team dashboard before the deadline
- [ ] Extraction accuracy figure measured and included in the technical summary
- [ ] Backup recorded walkthrough

---

## 25. Judging Criteria Traceability

| Criterion | Weight | Evidence in this product |
|---|---|---|
| AI Depth & Use | 30% | Seven-stage pipeline; vision extraction from unstructured images; multi-document clinical reasoning; grounded citation-bearing Q&A; composed confidence model; independent verification; bilingual generation. AI is the engine, not a feature. |
| Technical Execution | 30% | Layered architecture with enforced dependency rules; background processing with durable state; typed schema validation; caching; retry and failure isolation; deliberate, documented technology exclusions; measured extraction accuracy; deployed end to end. |
| Originality & Innovation | 20% | Dual verification of AI findings against an official drug database; same-document contradiction detection via extracted warnings; evidence-linked outputs throughout; Tamil as a first-class output; visible pipeline as an interface element. |
| Usefulness & Impact | 10% | Addresses a concrete regional problem — paper records fragmented across unconnected providers — with language accessibility that materially widens who can use it. |
| Presentation & UX | 10% | Purpose-built clinical interface; processing made legible; charts with clinical context; evidence viewer; consistent confidence and safety signalling; documentation depth. |

### 25.1 Rules requirement coverage

| Stated requirement | Where satisfied |
|---|---|
| Extract structured data and merge into one timeline per patient | FR-3.x, FR-4.x |
| Cross-check for interactions, duplicates, conflicting dosages | FR-5.1–5.3 |
| Track lab trends and explain in plain language | FR-6.x |
| Support follow-up questions reasoning across multiple documents | FR-7.1–7.2 |
| Confidence score per answer; recommend consulting a professional on high-risk or low-confidence | FR-7.4, FR-7.6, §11.4 |
| Never present itself as a diagnosis | §5.3, §17.1 |
| Complete end-to-end user workflow | §9.1 |
| Functional frontend; not API-only | §10 |
| Handle the provided messy real-world formats reliably | §11, §18 |

---

## 26. Future Roadmap (Post Round 1)

**Immediate hardening:** authentication with row-level security; private storage with signed URLs; persisted chat history; per-model execution logging surfaced in the interface.

**Accuracy:** OCR augmentation for handwriting-heavy documents with word-level confidence and bounding boxes, enabling in-image highlighting of extracted values; brand-to-generic mapping via a dedicated terminology service.

**Capability:** additional languages; caregiver sharing with consent; doctor-facing summary export; medication schedule tracking; wearable and lab-portal ingestion.

**Scale:** partitioning on document and alert tables; read replicas; materialized views for dashboards; event sourcing for a complete audit history; regional deployment.

---

## 27. Glossary

| Term | Meaning |
|---|---|
| Canonical schema | The single structured shape every document is extracted into, regardless of type |
| Generic name | The active ingredient name, used as the join key for all cross-checking |
| Evidence linking | The guarantee that every displayed finding traces to its source document |
| Grounded answer | An answer constrained to the patient's own uploaded documents |
| Composed confidence | A score combining model self-assessment, consistency checks, and external verification |
| Golden dataset | Hand-labelled expected extractions used to measure accuracy |
| Planted trap | A deliberate risk in the evaluation dataset the system is expected to detect |
| Dual verification | Pairing an AI finding with independent confirmation from an official data source |

---

## 28. Open Questions & Decision Log

### 28.1 Decisions taken

| Decision | Rationale |
|---|---|
| No authentication; patient profiles instead | Rules exempt complex security; profiles satisfy the multi-patient requirement at no complexity cost |
| Vision LLM extraction rather than OCR-first | Dataset is images; semantic understanding is required, not just text recovery |
| No dedicated OCR service in Round 1 | Marginal gain on a predominantly printed dataset; second failure point; consciously deferred with a documented trigger for revisiting |
| No vector database | Small per-patient corpus; complete structured grounding is more accurate than retrieval; schema remains extension-ready |
| Layered monolith over Clean Architecture, CQRS, or repositories | Complexity budget directed at the AI pipeline; dependency rules preserve the benefits without ceremony |
| Timeline as a view, not a table | Derived data stored twice creates synchronization defects |
| Six tables in Phase 1 | Every table carries real data; additional tables are additive when justified |
| Angular frontend | Existing team fluency; fastest path to a polished result |
| Findings retained when verification fails | Absence of confirmation is not evidence of safety |

### 28.2 Open questions

| Question | Resolution path |
|---|---|
| Final product name | Confirm before the presentation is designed |
| Whether AI execution logging ships in Round 1 | Decide after the application is feature-complete; include only if it does not risk the schedule |
| Whether diagnoses warrant a dedicated table | Decide during cross-check implementation, based on whether query complexity justifies it |
| Tamil phrasing conventions for clinical terms | Teammate review during quality assurance |

---

*This PRD is the single source of truth for MediTrail Round 1. Where the implementation diverges, either the implementation changes or this document is updated — not neither.*
