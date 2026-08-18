# MediTrail — Round 2 Plan

**Product:** MediTrail — AI Medical Report & Prescription Cross-Checker
**Team:** Dev HuHu · developer: Roshan
**Competition:** YGC AI Competition 2026 (CodeStorm AI) — Grand Finale
**Parent spec:** [MediTrail_PRD.md](MediTrail_PRD.md) (Round 1, v1.0). This file does not replace it.
**Date:** 18 August 2026
**Status:** Working plan for the remaining window (18–20 Aug build · 21 Aug live demo)

---

## 1. What Round 2 actually is

Round 1 was an online build against a published theme. Round 2 is the **in-person Grand Finale**:

| | |
|---|---|
| Theme reveal | 9 August 2026 (email / WhatsApp) |
| Development window | 9–20 August 2026 |
| Demo day | **21 August 2026, 09:00** |
| Venue | Jaffna Thiruvalluvar Cultural Center |
| Format | Live product demonstration + Q&A before the judging panel |

The Round 1 PRD, dataset, pipeline, and deployed app are the base. Round 2 work is: **keep that base green, close the demo-blocking gaps, then add only what the new theme and the judges will see in five minutes.**

Paste the official Round 2 theme into §2 as soon as it is in hand. Until then, every workstream below is theme-agnostic: it makes the existing product more honest, more demoable, and more defensible under questioning.

---

## 2. Round 2 theme (fill in)

> Official theme text — paste from the 9 Aug email. Do not invent requirements.

**Theme:** _TBD_

**What it changes in this plan**

| Theme implication | Response |
|---|---|
| Same problem, “go deeper” | Stay on §4 P0/P1. Do not start auth, OCR, or RAG. |
| Must show a new AI capability | Pick **one** from §5 that maps to the wording. Ship it end-to-end with evidence, or do not start it. |
| New dataset / formats | Extend golden labels + trap harness. Do not demo unmeasured extraction. |
| Stronger safety / privacy ask | Signed URLs + a simple access gate (not full auth). Keep “never diagnoses”. |
| Pitch / impact ask | Doctor-facing summary + Tamil + regional problem story. Little new pipeline work. |

**Hard rules that do not move with the theme** (PRD §5.3): never diagnose; never recommend starting, stopping, or changing a drug; never present a guess as fact; never drop an unverified risk.

---

## 3. Where Round 1 actually left us

Measured against [README.md](../README.md) and [dataset/golden/traps.md](../dataset/golden/traps.md), last trap run **2026-08-16** on `google/gemini-2.5-flash`.

| Milestone | State | Round 2 implication |
|---|---|---|
| M1 Foundation | Done | Leave it |
| M2 Extraction | Done, one open date defect | Do **not** prompt-fix `patient_y_year1_1` (see §3.1) |
| M3 Intelligence | Medication path done; labs built but undemonstrable on the judge set | Need a **labelled supplementary** lab set for the demo |
| M4 Application | Dashboard, evidence, processing, chat done | Processing stepper now matches the analyzer (Round 2). Doctor one-pager added. |
| M5 Deployed | Azure + wwwroot + Supabase | Cold-path test from an external network **not yet done** |
| M6 Submitted | Round 1 artifacts | Round 2 needs a **live** demo, a 4–5 min talk, and a recorded fallback |

Quality gates to keep green:

- Golden extraction: **95.1%** (330/347) on the demo model
- Planted traps: **7 / 7** originally planted traps fire end to end
- Open: all-document DATES sweep fails on `patient_y_year1_1`

### 3.1 Do not spend Round 2 on the date hallucination

`patient_y_year1_1` prints a legible `07/10/2022`. Rule 3 already says that string must be `null`. The model still resolves it from UK NHS day-first convention. Two prompt reinforcements were measured and **reverted**: overall accuracy fell 95.1% → 92% and the date still flipped.

A fixed date is not worth a worse reader. If a judge asks: *the model guessed a date order; we refuse to trade field accuracy for it; the failure is recorded and the product still nulls the truly unreadable dates (Y10, Y11).*

### 3.2 Pipeline honesty gap

`PatientAnalyzer` runs merge → rules → cross-check → openFDA, then jumps to `Ready`. It **never** sets `AnalyzingTrends`. Lab trends and chat run on `GET /labs` and `POST /ask`.

**Fixed in Round 2:** the processing screen no longer lists “Analyzing lab trends” as a background stage. Copy on that screen says trends run when the tab is opened. The enum member is retained so stored ordinals stay stable.

---

## 4. Priority (remaining days)

Today is **18 August**. Buildable time is 18–20 Aug. 21 Aug is on-stage only.

### P0 — Demo cannot fail without these

| ID | Work | Why | Done when |
|---|---|---|---|
| R2-0.1 | Cold-path test of the public URL from a phone / other network | PRD §18.5, M5 open | App reaches Ready from a cold start; keepalive confirmed |
| R2-0.2 | Rehearse the live demo on the **deployed** app with the 16 judge images | Judges will ask to see it | Script in §7 runs in < 5 minutes without a local machine |
| R2-0.3 | Recorded fallback walkthrough | Venue wifi / Azure cold start | 4–5 min video on a local file + a phone, not only in the cloud |
| R2-0.4 | Processing stepper matches the analyzer | Honesty | **Done** — stage removed from UI; leftover enum kept |
| R2-0.5 | Supplementary lab-report images, **labelled as supplementary** | FR-6 cannot be shown on the judge set | **Done** — `dataset/supplementary/` + generator script |
| R2-0.6 | Slide deck + Q&A sheet | 4–5 min + panel | **Talk deck** [talk.html](talk.html) · Q&A [MediTrail_Round2_QA.md](MediTrail_Round2_QA.md). Rehearse still open. |
| R2-0.7 | Confirm Azure App Service, OpenRouter key, spend cap, Supabase bucket | Demo morning | `/health/ready` green; one upload works from the venue |

### P1 — Visible depth if P0 is green by end of 18 Aug

| ID | Work | Why | Done when |
|---|---|---|---|
| R2-1.1 | Doctor-facing one-page summary (“show this to the pharmacist”) | Primary persona; originality | **Done** — `/patients/:id/summary`, EN/TA, print |
| R2-1.2 | Surface model name + composed confidence on evidence / alerts | PRD §26 execution logging, FR-3.8 | **Done** — evidence pill `Read by {model}` |
| R2-1.3 | Suggested chat questions already exist; make one of them fire a **Y1** answer on Patient Y | FR-7.7 as a demo beat | **Done** — first starter is the warning-contradiction question |
| R2-1.4 | Persist trends in analysis **or** stop advertising them as a pipeline stage | Matches code | **Done** — with R2-0.4 + empty-state that will not invent a series |

### P2 — Only if the Round 2 theme names it, or P0+P1 are done with a day left

| ID | Work | Do not start because |
|---|---|---|
| R2-2.1 | Auth + RLS | Complexity budget; not required for a live demo; easy to break the upload path |
| R2-2.2 | Private bucket + signed URLs | Evidence viewer currently depends on a public bucket; last-day risk |
| R2-2.3 | Azure Document Intelligence / handwriting OCR | Consciously deferred; second failure point; Y9 already handled as low confidence |
| R2-2.4 | Vector RAG | Per-patient corpus is ~10–15 docs; full-record grounding is the design |
| R2-2.5 | Extra languages beyond EN/TA | Tamil is the local differentiator; diluting it is not a finale win |
| R2-2.6 | Medication reminders, native apps, event sourcing | Different product |
| R2-2.7 | Prompt changes to extraction.md | Gate rule §18.4: a prompt change that costs accuracy does not ship |

---

## 5. Theme-mapped options (pick at most one)

If the emailed theme forces a new capability, choose **one** of these. Each is a thin slice on the existing pipeline, not a rewrite.

| Option | What to build | Evidence for judges |
|---|---|---|
| A. Stronger verification | Second independent source (e.g. a small local interaction table for regional brands openFDA misses) | Unverified vs confirmed badges; never drop unverified |
| B. Stronger honesty | Per-field “refused to guess” on the evidence page; highlight `unreadableSections` | Date-null documents Y10/Y11; DEMO MEDICINE stays null |
| C. Caregiver / doctor share | Read-only summary URL or print view (R2-1.1) | No login; still no diagnosis |
| D. Longitudinal labs | Supplementary dataset (R2-0.5) + trend empty-state copy that refuses to fabricate | Chart + EN/TA sentence + “not in the official 16 images” |
| E. Visible pipeline | Execution log of stages, model, retry, cache hit | Processing screen tells the truth; SHA-256 cache shown on Y2 |

Default if the theme is “refine the Round 1 product”: **E + D**, which is P0.4 + P0.5.

---

## 6. Calendar

### Tuesday 18 Aug — honesty + demo path

- [ ] Fill §2 with the official theme. Cut anything in P1 that does not map.
- [x] R2-0.4 processing stepper (code: `processing-page.component.ts` vs `PatientAnalyzer.cs`).
- [ ] R2-0.1 hit the public URL from a phone hotspot. Time cold start. If > 30s, confirm keepalive.
- [x] R2-0.5 find or create 2–3 lab-report photos. Hand-label golden JSON. **Do not mix them into `traps.md` as judge traps.**
- [ ] Walk Patient Y on production: Y1 (headline), Y2 (cache, not double-dose), Y3 (three beta-blockers).

### Wednesday 19 Aug — demo content

- [x] R2-0.6 slides: problem → one screenshot of Y1 → pipeline diagram (code vs LLM vs openFDA) → evidence click → chat → “never diagnoses”. Deck: [talk.html](talk.html).
- [ ] R2-0.3 record the walkthrough against production, not localhost.
- [x] If P0 is green: R2-1.1 doctor summary **or** R2-1.2 model-name on evidence — not both unless the first is a couple of hours.
- [ ] Re-run `dotnet run --project tools/MediTrail.GoldenRunner -- --traps` on the demo model. If a planted trap regresses, that is the only code fire.

### Thursday 20 Aug — freeze

- [ ] No prompt edits. No schema migrations unless a P0 bug requires them.
- [ ] R2-0.7 credentials, spend cap, `/health/ready`, one fresh patient upload on production.
- [ ] Offline copies: slides PDF, recorded video, this plan, PRD, traps.md detection table.
- [ ] Rehearse once with a teammate asking from §8.

### Friday 21 Aug — venue

- [ ] Pre-warm the app before 09:00 (open the URL, create a dummy patient, leave the tab open).
- [ ] Demo Patient Y first (Y1), then Patient X (X1 warfarin+aspirin, openFDA Confirmed). Labs only if the supplementary set is loaded **and** labelled as such.
- [ ] If the live app dies: play the recording, then talk through architecture from the slides.

---

## 7. Live demo script (~4–5 minutes)

Operator: one person. Browser: production URL, English first, Tamil toggle once.

1. **Patients (15s).** “No login — a named profile scopes documents. Paper records across unconnected hospitals.”
2. **Upload (20s).** Show the 16 images already processed, or upload one extra to show stages. Name SHA-256 cache (Y2).
3. **Processing (20s).** Named stages. If a file fails, the rest continue.
4. **Dashboard → Alerts (90s).** Patient Y. Open the **Paracetamol vs acetaminophen** finding. Click through to evidence. This is the headline: same molecule, same page, two names.
5. **Medications (30s).** Three beta-blockers (Y3). Unresolved `Oxprelol` / `SM FIBRO` if it helps the honesty story.
6. **Patient X, one alert (30s).** Warfarin + aspirin, badge **openFDA Confirmed**.
7. **Labs (20s).** Either the supplementary series, or the empty state: “the official set has no numeric series; we will not invent one.”
8. **Chat (30s).** Starter: “Was any medicine prescribed that I am allergic to?” Citations + confidence + consult banner.
9. **Close (15s).** “MediTrail never diagnoses. It surfaces risks a human should check.”

Backup line if extraction is slow: skip upload, use the already-Ready patient, spend the time on Y1 evidence.

---

## 8. Judge Q&A (memorize)

| They ask | Answer |
|---|---|
| Is this just ChatGPT on PDFs? | Vision extraction → typed JSON → **code** does dates, generics, frequency, duplicates, class, trends. LLM does reading and explanation. Arithmetic is never an LLM. |
| How do you know it didn’t make the drug up? | Schema, null-not-guess, one retry then fail, evidence image, openFDA on interactions, `DEMO MEDICINE` stays `genericName: null`. |
| Why no RAG / vector DB? | ~10–15 docs per patient. The full structured record is in the chat prompt. Retrieval would drop evidence. Schema is pgvector-ready later. |
| Why no login? | Round 1 rules did not require it. Profiles are the scope. Production path (RLS, signed URLs) is documented, not demoed, so we don’t spend the finale on auth bugs. |
| Why did a date get through? | `07/10/2022` is ambiguous. Prompt fixes cost 3 points of accuracy and still guessed. We kept the better reader and recorded the miss. |
| Do you recommend stopping a drug? | No. Alerts say “ask a pharmacist / doctor”. Consult flag on high-risk and low-confidence. |
| Why Tamil? | Family record-keeper persona. Generated with the finding, not translated after. |
| Lab trends? | Implemented (`TrendCalculator` math + bilingual copy). Official 16 images have almost no numeric labs. Supplementary set is labelled as such. |
| What if openFDA is down? | Finding stays, badge unverified. Absence of confirmation is not safety. |
| What model? | Demo: `google/gemini-2.5-flash` via OpenRouter. Temperature 0. Prompts are files under `AiPipeline/Prompts/`. |

---

## 9. Team split (finale week)

| Owner | 18–20 Aug | 21 Aug |
|---|---|---|
| Roshan | P0 code, production freeze, trap re-run | Drive the demo |
| Ground-truth teammate | Label supplementary labs; do **not** relabel judge goldens unless a real extraction bug | Hold the backup video |
| QA teammate | Adversarial pass: rotate an image, upload a PDF, kill wifi mid-upload | Time the talk, cut at 4:45 |
| Docs / slides teammate | Deck from §7; one architecture slide (code / LLM / openFDA) | Advance slides |
| Tamil review | Spot-check three alert explanations and one chat answer | Idle unless a judge reads Tamil on screen |

---

## 10. Engineering notes (so the work stays small)

**R2-0.4 processing honesty — preferred fix**

Do not run trend LLM in the worker just to light a stepper. Either:

- Drop `AnalyzingTrends` from `STAGES` in `processing-page.component.ts` and from the status enum if nothing writes it, **or**
- Set `PatientStatus.AnalyzingTrends` for a few hundred milliseconds after verify only if trends are actually computed there.

The architecture canvas already records this split: trends and chat are on-demand.

**R2-0.5 supplementary labs**

- New folder e.g. `dataset/supplementary/` (gitignored images, committed golden JSON).
- README sentence: *not part of the judge set.*
- A dedicated patient profile named clearly (`Demo labs — supplementary`).
- Empty-state copy on the official patients must stay: no series, no invented trend.

**R2-1.1 doctor summary**

Reuse persisted alerts + citations. No new model call required. If a bilingual paragraph is wanted, one grounded prompt over the alert list — same “never diagnose” system rules as chat.

**Quality gates before any merge after 18 Aug**

```bash
dotnet test tests/MediTrail.Tests
dotnet run --project tools/MediTrail.GoldenRunner -- --traps --patient=y
```

A skip is not a pass. Do not run the full 16-image accuracy gate unless an extraction prompt changed — and extraction prompts must not change.

---

## 11. Explicit non-goals for Round 2

- Rewriting the PRD.
- Raising golden accuracy by prompt iteration.
- Auth, RLS, private storage.
- Replacing the vision extractor with OCR-first.
- A second frontend.
- Any diagnosis, dose advice, or “you should stop this medicine” copy.
- Fabricated doctor/clinic/phone/rating data. Every displayed facility field comes from a live provider or the labelled cache.
- SLMC scraping. Deep-link only.
- RxNav interaction API (discontinued 2 Jan 2024).
- Letting the LLM pick a specialty.

---

## 12. Open questions

| Question | Default if unanswered by end of 18 Aug |
|---|---|
| Exact Round 2 theme text | Doctor Recommendation as specified below. |
| Will judges bring a new image set on stage? | Yes, assume so. Demo must accept an unseen PNG/JPG/PDF. Do not depend on preloaded patients alone. |
| Projector resolution | 1280-wide layout; no hover-only actions |
| Who speaks | Roshan demos; one teammate on Q&A backup |

---

## 13. Doctor Recommendation (YGC Round 2)

When MediTrail flags a **high-risk or low-confidence** issue, help the user find a **real doctor nearby** via a 3-step drawer: (1) confirm specialty with traceable evidence, (2) ask location + availability, (3) show real facilities from public data.

**Any fabricated doctor/clinic data shown as real = automatic scoring deduction.**

Feature flag: `Features:DoctorRecommendation`. Defaults **off**. One flip returns the app to a clean Round 1 state.

Trigger on an alert: `Severity is Red or Amber || RequiresProfessionalConsult || Confidence < 50`.

### 13.1 Absolute rules

1. Never write a doctor name, clinic name, address, phone, or rating into code, seed data, test fixtures, or demo scripts.
2. Three distinct states: `Empty` ≠ `Failed` ≠ `LocationNotFound`.
3. Missing = `null` = UI “Not listed”. Never `"Unknown Clinic"`, never `0.0` stars, never `"N/A"`.
4. No ratings from OSM. Google may populate `rating` only if that provider is enabled.
5. Cache is labelled. Every cached result renders `fetched_at`.
6. No “SLMC Verified” badge. Deep-link `https://slmc.gov.lk/public/en/services/public`.
7. No diagnosis language. Ban `"you have"`, `"diagnosis"`, `"condition detected"` from Round 2 UI strings. RxClass disease-class names appear only inside “Why this specialty?” as *drug-class information*.
8. README states which field comes from which source, and that OSM has no ratings.
9. Distance is Haversine, labelled “straight-line distance”.
10. Honesty tests: provider zero results → 0 rows, `providerStatus = empty`; provider throws → 0 rows, `providerStatus = failed`.
11. Everything gated by `Features:DoctorRecommendation`.

### 13.2 Architecture

```
DoctorRecommendationService
├─ SpecialtyResolver (deterministic ladder; LLM never picks)
├─ IGeocoder → Nominatim + StaticSriLankaCityTable + provider_cache
├─ IDoctorSearchProvider → Overpass (failover) / Google (flag) / Healthsites (stretch) / Doc990 (stub)
├─ AvailabilityMatcher (heuristic, not a full opening_hours parser)
├─ DoctorRankingService
└─ ProviderCache (TTL, fetched_at)
```

Every provider returns `NormalizedFacility` and explicit `Ok | Empty | Failed | NotConfigured`.

### 13.3 Spikes (2026-08-18)

| Spike | Result |
|---|---|
| Overpass around Jaffna 10 km | **80** elements (query capped at 80), **60** named. `GET` was flaky; **POST** to `overpass-api.de` returned HTTP 200. Radius ladder may start at 5 km. Failover endpoints timed out or returned HTML this run — T4 must still fail over. |
| RxClass MEDRT `may_treat` | warfarin **18** classes (includes Thromboembolism); aspirin **14**; clarithromycin **13**. Rung 2 is viable. |

Do not paste live facility names into this repo.

### 13.4 Task order

T1 schema → T2 interfaces/flag → T3 geocoder → **stop and confirm spikes before T4** → T4 Overpass → T5 cache → T6 RxClass/resolver → T7 rank → T8 endpoint/health → T9 honesty tests → T10–T14 frontend → T15 README + prewarm.

### 13.5 Appendix A — Overpass QL

POST (not GET) to the interpreter. 25s timeout. Fail over `overpass-api.de` → `overpass.private.coffee` → `overpass.kumi.systems`.

```
[out:json][timeout:25];
(
  nwr(around:{radius},{lat},{lng})["amenity"~"^(doctors|clinic|hospital|pharmacy)$"];
  nwr(around:{radius},{lat},{lng})["healthcare"];
);
out center tags;
```

British specialty spellings on `healthcare:speciality`: `gynaecology`, `orthopaedics`, `paediatrics`.

### 13.6 Out of scope until T1–T15 are done

pgvector, extra languages, auth, extraction-prompt edits, chat_messages / existing pipeline paths.

---

*Round 1 source of truth remains [MediTrail_PRD.md](MediTrail_PRD.md). Where this plan and the PRD disagree on product ethics or the canonical schema, the PRD wins. Where they disagree on what to build before 21 August, this plan wins. Section 13 is the source of truth for the doctor-recommendation feature.*
