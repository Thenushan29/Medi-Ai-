# Planted traps in the evaluation dataset

Every item here must be detected (§18.2). This list doubles as the demonstration script.

Derived by reading all 16 documents directly. **Not yet confirmed against the pipeline** — each row
gets a ✅ only once the system actually raises it.

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

| Trap | Detected | Notes |
|---|---|---|
| Y1 same-document contradiction | ⬜ | The one that must work |
| Y2 duplicate file cached | ⬜ | |
| Y3 three beta-blockers | ⬜ | |
| X1 warfarin + aspirin | ⬜ | |
| Y10 / Y11 null dates | ⬜ | Hallucination check |
| X6 placeholders not invented | ⬜ | Hallucination check |
