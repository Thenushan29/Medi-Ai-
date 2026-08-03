# Dataset

## `dataset/` — evaluation documents

The judge-provided patient documents (`Patient x`, `Patient y`, three years each, PNG/JPG scans).

**Images are gitignored.** They are personal health information and stay off the repository; see
`.gitignore`. Copy them in locally to run the extraction tests.

## `dataset/golden/` — hand-labelled ground truth

One `.json` file per source document, named after it, containing the **expected** canonical-schema
extraction (§12.1). These *are* committed — they carry no images, and they are the primary quality
gate (§18.1).

The test runner compares pipeline output against these field by field and reports accuracy per
category: medication names, strengths, frequencies, lab values, dates, allergies. The resulting
figure is the headline number in the technical summary.

Targets (§3.3): **≥ 95%** on printed documents, **≥ 80%** on photographed or blurry ones.

### Labelling rules

Label what is *printed*, not what is *inferred*:

- If a value is genuinely unreadable, the expected value is `null`. A label that guesses turns a
  correct "I could not read this" into a scored failure and teaches the prompt to guess back.
- Preserve `sourceText` exactly as printed, including odd spacing and abbreviations.
- Dates go in as ISO (`yyyy-MM-dd`). An ambiguous printed date (`03/04/22`) is `null`.
- Map brand to generic only where a pharmacist would be certain. `Zoclar 500` → `clarithromycin`
  is fine; a house-brand with no legible ingredient line is `null`.
- Record every warning printed on the document in `warningsInDocument`, with the generics it names
  in `relatesTo`. This is what the same-document contradiction check matches against (FR-5.5).

### Planted traps

`traps.md` enumerates every deliberate issue in the dataset — same-document contradictions,
cross-visit duplicates, dosage conflicts, allergy triggers, lab drift. All must be detected (§18.2).
That list doubles as the demonstration script.

**Owner:** ground-truth labelling is a teammate deliverable (§22).

Start from `golden/_TEMPLATE.json`. Files whose name begins with `_` are ignored by the runner.
Name each label file after its image — `patient_x_year1_rx.png` → `patient_x_year1_rx.json`.

## Running the accuracy check

```bash
# once
cd backend/MediTrail.Api
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-..."

# then, from the repository root
dotnet run --project tools/MediTrail.GoldenRunner

# or limit to one document while iterating on the prompt
dotnet run --project tools/MediTrail.GoldenRunner -- patient_x
```

Output is accuracy per category plus every mismatch, with expected and actual side by side.
Exit code is 0 only when overall accuracy is ≥ 95% **and** hallucinations are 0 **and** no document
failed — so it can gate a build, not just print a number.

The five outcomes it distinguishes:

| Outcome | Meaning |
|---|---|
| `Correct` | Values agree |
| `CorrectNull` | Both say unreadable — a success, since the prompt's core rule is null-over-guess |
| `Wrong` | Both have values, they differ |
| `Missed` | Label has a value, model returned null — cautious, not dangerous |
| `Hallucinated` | Label says null, model produced a value — **target is zero** (§3.3) |

`Hallucinated` is counted separately rather than folded into `Wrong` because it is the one failure
mode that makes the product unsafe, and averaging would hide it.
