# Planted traps in the evaluation dataset

Every item here must be detected (§18.2). This list doubles as the demonstration script.

Derived by reading all 16 documents directly. The Detection status table at the bottom records what
the pipeline actually did with them, not what the unit tests do with constructed inputs.

---

## Patient x — 6 documents

Clinic-software sample prescriptions. Four of the six use `DEMO MEDICINE 1..4` placeholders, which
are themselves a test: **a placeholder is not a drug.** Inventing a generic name for
"DEMO MEDICINE 1" would be a hallucination.

| # | Trap | Where | Expected behaviour |
|---|---|---|---|
| X1 | **Warfarin + Aspirin** in one prescription | `patient_x_year3_2` | Red drug–drug interaction — major bleeding risk. openFDA should confirm. |
| X2 | **Crocine (paracetamol) + Aspirin and Codeine** | `patient_x_year3_2` | Overlapping analgesics; amber |
| X3 | Brand names with the generic printed beneath — `ZOCLAR 500` → *clarithromycin ip 500mg*, `VOMILAST` → *doxylamine + pyridoxine + folic acid*, `GESTAKIND 10/SR` → *isoxsuprine* | `patient_x_year1_1` | Generic resolved from the document, not from model memory |
| X4 | `TAB. ABCIXIMAB` prescribed orally for malaria | `patient_x_year1_1` | Abciximab is an IV antiplatelet — extract faithfully; do not "correct" it |
| X5 | Dosage instructions in **Hindi, Malayalam, Kannada** | `x_year2_1`, `x_year3_1`, `x_year3_2` | `frequencyPerDay` still resolved; `sourceText` preserved verbatim |
| X6 | `DEMO MEDICINE 1..4` placeholders | 4 documents | Extracted as printed, `genericName: null`. **No invented generic.** |
| X7 | Ages inconsistent across visits (13 Y, 8 Y, 9 Y, 4 M) | Patient x set | These are unrelated sample documents; do not reconcile them into a fiction |

## Patient y — 10 documents

| # | Trap | Where | Expected behaviour |
|---|---|---|---|
| **Y1** | **Same-document contradiction.** Jaundice prescription lists **Paracetamol 500mg**, while its own advice says *"Avoid taking unnecessary or liver-toxic medications (e.g. alcohol, acetaminophen)"* | `patient_y_year2_1` | **The headline finding.** Requires `warningsInDocument.relatesTo: ["acetaminophen"]` plus paracetamol ≡ acetaminophen. Red. |
| **Y2** | **Byte-identical duplicate document** — `year3_3` is the same file as `year3_2` (SHA-256 verified) | `y_year3_2` / `y_year3_3` | Second upload reuses the cached extraction (FR-2.6, status `Cached`). Must **not** be reported as a duplicate prescription — it is the same visit twice. |
| **Y3** | **Three beta-blockers across the record** — Atenolol 50mg, Betaloc (metoprolol) 100mg, Oxprelol (oxprenolol) 50mg | `y_year3_2/3`, `y_year3_6` | Duplicate therapeutic class; red |
| Y4 | **Metoprolol + oxprenolol in one prescription** | `y_year3_6` | Two beta-blockers on one page |
| Y5 | **Printed contraindication:** *"Atenolol contraindicated in asthmatics"* | `y_year3_2/3` | Captured as a document warning with `relatesTo: ["atenolol"]` |
| Y6 | **Printed warning:** *"Do not take aspirin empty stomach"* | `y_year3_2/3` | Captured with `relatesTo: ["aspirin"]` |
| Y7 | **Amoxicillin** prescribed | `y_year3_1` | A penicillin. Flags against any recorded penicillin allergy. |
| Y8 | **Cimetidine + metoprolol** | `y_year3_6` | Cimetidine inhibits CYP450 and raises beta-blocker levels; amber |
| Y9 | **Handwriting** — UK NHS private script (Concerta XL 36mg), US Navy DD-1289 (Tr. Belladonna / Amphogel), two handwritten US scripts | `y_year1_1`, `y_year2_2`, `y_year3_5`, `y_year3_6` | Low confidence where genuinely unclear. **Never a confident guess.** |
| **Y10** | **Placeholder date `Jan 9, 20yy`** | `y_year3_5` | `documentDate: null`. Inventing a year is a hallucination. |
| Y11 | Ambiguous date `09-11-12` with no format hint | `y_year3_6` | `documentDate: null` (§11.3) |
| Y12 | **`Oxprelol`** — misspelling of oxprenolol | `y_year3_6` | Resolve only if confident; otherwise `brandName` as printed, `genericName: null` |
| Y13 | Dosage codes `1-1-1`, `1-0-1`, `t.i.d. a.c.`, `BID`, `TID`, `QD`, `sos` | Several | Normalized to `frequencyPerDay`: 3, 2, 3, 2, 3, 1, null |
| Y14 | Instructions in **Gujarati** (`ભૂખ્યા પેટે` = on an empty stomach) | `y_year1_2` | Preserved in `sourceText` |
| Y15 | Dengue diagnosis with `Cratine` (misspelt *creatinine*) as a suggested investigation | `y_year1_2` | A suggested test, not a result — no `labResults` entry |
| Y16 | Patient identity differs across documents — Aakruti Kapoor, Priya Sharma, Amit Sharma, John Smith, Mary Smith, Jamie Woodley | Patient y set | Extract the printed name per document. Merging is by patient **profile**, not by name matching. |

---

## Notable: almost no lab results

Only `patient_y_year2_1` carries anything numeric, and it lists **suggested** investigations rather
than values. There is **no longitudinal lab series in this dataset**.

Consequences:
- Lab-trend detection (FR-6.x) has nothing to demonstrate on the judge dataset. Build it — the rules
  require it — but the demo must not claim a trend that the data cannot support.
- The demonstration should lead with medication cross-checking, where the traps actually are.
- Adding a supplementary lab-report set for the demo is reasonable **if it is labelled as
  supplementary**, not passed off as the judge dataset.

## Detection status

Last confirmed on **2026-08-16** by `dotnet run --project tools/MediTrail.GoldenRunner -- --traps`,
over all 16 images, with `google/gemini-2.5-flash` on OpenRouter — the model the demo runs on.
The first run that day found Y3 and X1 failing; both fixes landed on this branch and the re-run
below confirms them from the image, not from a unit test.

The harness runs the production path end to end: upload → SHA-256 cache → `VisionDocumentExtractor`
→ `ProcessingWorker` → `ExtractionMerger` → `DeterministicRuleChecker` → `InteractionCrossChecker` →
openFDA → persisted alerts, then asserts against the alerts that were written. Only the database
(in-memory) and object storage (a scratch directory) are substituted, so a verification run leaves
no patient data in the demo project; nothing that reads a document or decides a finding is stubbed.
A ✅ below therefore means the system raised the finding from the image, not that a unit test passed
on a hand-built input.

| Trap | Detected | Notes |
|---|---|---|
| Y1 same-document contradiction | ✅ | Red `DocumentWarningConflict`, confidence 90, consult set, evidence `patient_y_year2_1`. Raised through the **`warningsInDocument` path**, which is the only path available: the extraction produced zero recorded-allergy rows across the whole set. The warning merged as `relatesTo: [paracetamol]` — `acetaminophen` was normalized on the way in, which is what makes it collide with the prescribed Paracetamol |
| Y2 duplicate file cached | ✅ | Verified through the real cache, not by inspection: both files hash to `2ed598c9c904…`, `year3_2` extracted, `year3_3` came back `Cached` with no second model call. No same-generic duplicate or dosage alert over any of the four shared generics |
| Y3 three beta-blockers | ✅ | "3 beta blockers in your records" — Red, confidence 90, consult set, naming atenolol, metoprolol **and oxprenolol**, evidence `y_year3_2` + `y_year3_6`. First run failed with two of three: `Oxprelol 50mg` merged with a null generic. Fixed by teaching the brand table `oxprelol` → oxprenolol — a dataset-specific entry; the general gap (an unresolved generic silently exits every check) is covered by the `UnresolvedMedication` alert, which fires for `SM FIBRO` on this same set |
| X1 warfarin + aspirin | ✅ | "Warfarin and Aspirin/codeine may interact" — Red, confidence 100, consult set, **openFDA Confirmed** (the label's own `drug_interactions` text), evidence `patient_x_year3_2`. First run failed: the record holds the combination `aspirin/codeine` and the grounding lookup required an exact key. Grounding is now component-wise; ungrounded findings are still dropped |
| Y10 / Y11 null dates | ✅ | Both `documentDate: null`, and the model returned null *before* normalization in both cases — the null is a refusal to guess, not a parse failure downstream of a guess |
| X6 placeholders not invented | ✅ | 14 placeholder rows across the four sample documents, every one with `genericName: null`. None entered a cross-check, and none appears in any alert. Placeholders are also excluded from the `UnresolvedMedication` alert — not a drug is a different fact from not identified |
| DATES all-document null-date sweep | ❌ | Y10/Y11 name two documents, so the harness checks **every** document whose golden label says the date is unreadable — five in this dataset. Four extract null; **`patient_y_year1_1` does not.** Two prompt fixes were attempted and both were reverted — see below |

### The one open failure: `patient_y_year1_1`

The page prints **`07/10/2022`** and it is legible. Rule 3 of the extraction prompt already names
this exact string as a case that must be `null`: four-digit year, both other parts ≤ 12, no way to
know the order. The model reads it correctly and then resolves it anyway, from the fact that the
form is a UK NHS script — day-first. Across runs it returns `2022-07-10` or `2022-10-07`, flipping
between the two readings, which is the signature of exactly that guess.

So this is **not** a legibility failure and not a missing rule. It is the model overriding a rule
that names its input, under a strong contextual prior.

Two prompt changes were measured against the §18.1 accuracy gate and **both were reverted** under
the §18.4 rule that a prompt change may not cost accuracy:

| Prompt | Overall | Hallucinated | `y_year1_1` |
|---|---|---|---|
| Committed (baseline) | **95.1%** (330/347) | 1 | `2022-07-10` |
| + legibility precondition, + "a bare number is not a frequency" | 92.2% (321/348) | 1 | `2022-07-10` |
| + rule 3 reinforced against national date conventions | 92.8% (324/349) | 5 | `2022-10-07` |

Neither attempt fixed the date, and both made the reader worse — the frequency rule alone took
frequency misses from 3 to 11. A fixed date is not worth a worse reader.

Also worth recording: the `document` category scored 78.1%, 68.8% and 65.6% across the three runs
on **three prompts that did not touch document metadata**. Temperature is 0, but the provider is not
reproducible run to run, so a single gate run is a noisy measurement and small differences between
runs should not be read as signal.
