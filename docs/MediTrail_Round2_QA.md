# MediTrail — Finale Q&A sheet

Print this. One operator demos; one teammate holds this page.

Live path: Patient Y → Alerts (Paracetamol / acetaminophen) → Evidence → Medications (three beta-blockers) → Patient X (warfarin + aspirin, openFDA) → Chat starter → optional supplementary labs → **Show this to a doctor**.

If the app dies: open [talk.html](talk.html) (arrow keys), then play the recorded walkthrough.

| They ask | Answer |
|---|---|
| Is this just ChatGPT on PDFs? | Vision extraction → typed JSON → **code** does dates, generics, frequency, duplicates, class, trends. LLM does reading and explanation. Arithmetic is never an LLM. |
| How do you know it didn’t make the drug up? | Schema, null-not-guess, one retry then fail, evidence image, openFDA on interactions, `DEMO MEDICINE` stays `genericName: null`. |
| Why no RAG / vector DB? | ~10–15 docs per patient. The full structured record is in the chat prompt. Retrieval would drop evidence. |
| Why no login? | Round 1 rules did not require it. Profiles are the scope. Auth is documented, not demoed, so we don’t spend the finale on auth bugs. |
| Why did a date get through? | `07/10/2022` is ambiguous. Prompt fixes cost 3 points of accuracy and still guessed. We kept the better reader and recorded the miss. |
| Do you recommend stopping a drug? | No. Alerts say ask a pharmacist / doctor. Consult flag on high-risk and low-confidence. |
| Why Tamil? | Family record-keeper persona. Generated with the finding, not translated after. |
| Lab trends? | Implemented. Official 16 images have almost no numeric labs. Supplementary set is labelled as such. |
| What if openFDA is down? | Finding stays, badge unverified. Absence of confirmation is not safety. |
| What model? | Demo: `google/gemini-2.5-flash` via OpenRouter. Temperature 0. Prompts are files under `AiPipeline/Prompts/`. |

**Do not say:** “you should stop this medicine”, “this is a diagnosis”, “the official set shows lab trends”.
