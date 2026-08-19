# Supplementary lab reports — not the judge set

These three printed-style reports exist so **Lab Trends** can be demonstrated. The official
16-image judge dataset has almost no numeric lab values (`dataset/golden/traps.md`).

**Do not pass this folder off as Patient X / Patient Y.** Create a separate profile named
something like `Demo labs — supplementary`, upload these three images, and say so in the demo.

| File | Date | What it shows |
|---|---|---|
| `demo_labs_2022-03-15.png` | 15 Mar 2022 | HbA1c 6.4%, creatinine 0.9, ALT 32 |
| `demo_labs_2023-04-10.png` | 10 Apr 2023 | HbA1c 7.1%, creatinine 1.1, ALT 48 |
| `demo_labs_2024-06-02.png` | 2 Jun 2024 | HbA1c 8.2% (rising), creatinine 1.4 (out of range), ALT 55 |

HbA1c 6.4 → 7.1 → 8.2 is a monotonic rise above 10%, so `TrendCalculator` should report
**Rising**. Creatinine 0.9 → 1.1 → 1.4 does the same; the last point sits above the printed
1.2 mg/dL max.

Images are gitignored (PHI-shaped medical documents). Golden JSON is committed. Generate the
PNGs on the machine that will run the demo:

```powershell
powershell -File dataset/supplementary/generate-lab-reports.ps1
```

Then upload the three PNGs to a new patient profile. They are not wired into GoldenRunner or
the trap harness.
