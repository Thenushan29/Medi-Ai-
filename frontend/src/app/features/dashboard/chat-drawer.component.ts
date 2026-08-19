import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { ApiService } from '../../core/api.service';
import { LanguageService } from '../../core/language.service';
import { ConfidenceBadgeComponent } from '../../shared/confidence-badge.component';
import type { ChatAnswer, TimelineEntry } from '../../core/models';

interface Turn {
  question: string;
  answer?: ChatAnswer;
  error?: string;
  pending: boolean;
}

/**
 * Starter questions (FR-7.7). The first one is the Y1 demo beat: same-document contradiction
 * (paracetamol prescribed, acetaminophen warned against). Allergy is still here because the
 * rules named that example; the judge set has no recorded-allergy rows.
 */
const SUGGESTIONS = [
  'Was a medicine prescribed that a document also warns against?',
  'Was any medicine prescribed that I am allergic to?',
  'Has the same medicine been prescribed twice?',
  'Which of my results are outside the normal range?'
];

/**
 * Chat drawer (§10.10). Every answer shows its citations, its confidence, and a consult banner
 * when the backend flags one — the same signalling as the rest of the interface.
 */
@Component({
  selector: 'mt-chat-drawer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, ConfidenceBadgeComponent],
  template: `
    <div class="fixed inset-0 z-40 flex justify-end" role="dialog" aria-label="Ask about your records">
      <div class="flex-1 bg-slate-900/20" (click)="closed.emit()" aria-hidden="true"></div>

      <aside class="flex h-full w-full max-w-lg flex-col border-l border-slate-200 bg-white shadow-xl">
        <header class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <div>
            <h2 class="font-medium text-slate-900">Ask about your records</h2>
            <p class="mt-0.5 text-xs text-slate-500">
              Answers come only from the documents you uploaded.
            </p>
          </div>
          <button type="button" class="text-slate-400 hover:text-slate-700" (click)="closed.emit()" aria-label="Close">
            ✕
          </button>
        </header>

        <div class="flex-1 space-y-5 overflow-y-auto px-5 py-5">
          @if (turns().length === 0) {
            <div class="space-y-2">
              <p class="text-xs font-medium text-slate-500">Try asking</p>
              @for (suggestion of suggestions; track suggestion) {
                <button
                  type="button"
                  class="block w-full rounded-lg border border-slate-200 px-3 py-2 text-left text-sm text-slate-700 hover:border-brand-500"
                  (click)="ask(suggestion)"
                >
                  {{ suggestion }}
                </button>
              }
            </div>
          }

          @for (turn of turns(); track $index) {
            <div class="space-y-2">
              <p class="ml-auto w-fit max-w-[85%] rounded-2xl rounded-br-sm bg-brand-600 px-4 py-2 text-sm text-white">
                {{ turn.question }}
              </p>

              @if (turn.pending) {
                <p class="w-fit rounded-2xl rounded-bl-sm bg-slate-100 px-4 py-2 text-sm text-slate-500">
                  Reading your documents…
                </p>
              } @else if (turn.error) {
                <p class="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
                  {{ turn.error }}
                </p>
              } @else if (turn.answer; as answer) {
                <div class="rounded-2xl rounded-bl-sm bg-slate-50 px-4 py-3">
                  <p class="text-sm leading-relaxed text-slate-800">
                    {{ display(answer) }}
                  </p>

                  <div class="mt-3 flex flex-wrap items-center gap-2">
                    <!-- The confidence badge says how far to trust what the answer asserts. An
                         answer that asserts nothing has no such number, and showing one invited
                         the question of what "not found · 100%" was supposed to mean. -->
                    @if (answer.safetyRefusal) {
                      <span class="rounded border border-amber-200 bg-amber-50 px-2 py-0.5 text-xs text-amber-800">
                        MediTrail cannot judge whether a medicine is safe for you
                      </span>
                    } @else if (!answer.foundInDocuments) {
                      <span class="rounded border border-slate-200 px-2 py-0.5 text-xs text-slate-500">
                        nothing in your documents covers this
                      </span>
                    } @else {
                      <mt-confidence [score]="answer.confidence" />
                    }
                  </div>

                  @if (answer.citations.length > 0) {
                    <div class="mt-3 flex flex-wrap items-center gap-2">
                      <span class="text-xs text-slate-500">From:</span>
                      @for (citation of answer.citations; track citation) {
                        <a
                          [routerLink]="['/documents', citation]"
                          class="rounded border border-slate-200 bg-white px-2 py-0.5 text-xs text-brand-700 hover:border-brand-500"
                        >
                          {{ nameFor(citation) }}
                        </a>
                      }
                    </div>
                  }

                  @if (answer.consultProfessional) {
                    <p class="mt-3 rounded-lg bg-red-50 px-3 py-2 text-xs font-medium text-red-800">
                      @if (language.current() === 'ta') {
                        ⚠ இது மருத்துவ ஆலோசனை அல்ல. மருத்துவர் அல்லது மருந்தாளுநரிடம் உறுதிப்படுத்துங்கள்.
                      } @else {
                        ⚠ This is not medical advice. Confirm with a doctor or pharmacist.
                      }
                    </p>
                  }
                </div>
              }
            </div>
          }
        </div>

        <form class="border-t border-slate-200 px-5 py-4" (ngSubmit)="ask(draft)">
          @if (!ready()) {
            <p class="mb-2 text-xs text-amber-800">
              Still reading your documents — questions will be answerable once that finishes.
            </p>
          }

          <div class="flex gap-2">
            <input
              name="question"
              [(ngModel)]="draft"
              [disabled]="!ready() || busy()"
              placeholder="Ask a question about your records"
              maxlength="1000"
              class="flex-1 rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none placeholder:text-slate-400 disabled:bg-slate-50"
            />
            <button
              type="submit"
              [disabled]="!ready() || busy() || !draft.trim()"
              class="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-40"
            >
              Ask
            </button>
          </div>
        </form>
      </aside>
    </div>
  `
})
export class ChatDrawerComponent {
  private readonly api = inject(ApiService);
  protected readonly language = inject(LanguageService);

  readonly patientId = input.required<string>();

  /** Input is disabled with an explanation until processing completes (§10.10). */
  readonly ready = input.required<boolean>();

  /** Used to show a readable file name on a citation chip rather than a bare id. */
  readonly documents = input<TimelineEntry[]>([]);

  readonly closed = output<void>();

  protected readonly suggestions = SUGGESTIONS;
  protected draft = '';
  protected readonly turns = signal<Turn[]>([]);
  protected readonly busy = signal(false);

  constructor() {
    // The drawer is created fresh each time it opens, so this restores the conversation rather
    // than showing a blank panel. A failure here leaves an empty drawer, which is exactly the
    // behaviour before it was stored — never an error banner over a working chat.
    queueMicrotask(() =>
      this.api.getChatHistory(this.patientId()).subscribe({
        next: stored =>
          this.turns.update(list => [
            ...stored.map(message => ({
              question: message.question,
              answer: message.answer,
              pending: false
            })),
            ...list
          ]),
        error: () => undefined
      })
    );
  }

  /**
   * Which version of the answer to show.
   *
   * Someone who types in Tamil script gets Tamil script back, and someone who types Tanglish gets
   * Tanglish — replying in English to either is a worse answer even when it is a correct one. The
   * EN/TA toggle still wins when the reader has set it to Tamil, so the global control is not
   * quietly overridden; an English question is untouched by any of this, which matters because the
   * demo is in English.
   */
  protected display(answer: ChatAnswer): string {
    if (this.language.current() === 'ta') return answer.answerTa || answer.answerEn;

    switch (answer.askedLanguage) {
      case 'Tanglish':
        return answer.answerTanglish || answer.answerEn;
      case 'Tamil':
        return answer.answerTa || answer.answerEn;
      default:
        return answer.answerEn;
    }
  }

  protected nameFor(documentId: string): string {
    return this.documents().find(d => d.documentId === documentId)?.fileName ?? 'source document';
  }

  protected ask(question: string): void {
    const text = question.trim();
    if (!text || this.busy() || !this.ready()) return;

    this.draft = '';
    this.busy.set(true);

    // Snapshot the completed exchanges before the pending turn is appended, so the question is
    // never sent as part of its own history. Answered turns only — a failed or in-flight turn has
    // nothing a follow-up could resolve against.
    const history = this.turns()
      .filter(turn => turn.answer)
      .map(turn => ({ question: turn.question, answer: turn.answer!.answerEn }));

    this.turns.update(list => [...list, { question: text, pending: true }]);

    this.api.ask(this.patientId(), text, history).subscribe({
      next: answer => {
        this.turns.update(list =>
          list.map((turn, i) => (i === list.length - 1 ? { ...turn, answer, pending: false } : turn))
        );
        this.busy.set(false);
      },
      error: (err: Error) => {
        this.turns.update(list =>
          list.map((turn, i) => (i === list.length - 1 ? { ...turn, error: err.message, pending: false } : turn))
        );
        this.busy.set(false);
      }
    });
  }
}
