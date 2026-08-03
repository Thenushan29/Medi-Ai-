import { Injectable, signal } from '@angular/core';

export type Language = 'en' | 'ta';

const STORAGE_KEY = 'meditrail.language';

/**
 * Tamil is a first-class output language, not a translation afterthought (Principle 6).
 * This holds the user's choice; AI explanations are generated in both languages and the
 * UI picks the field, so switching never triggers a round trip.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  readonly current = signal<Language>(readStoredLanguage());

  set(language: Language): void {
    this.current.set(language);
    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Private browsing or blocked storage — the in-memory signal still works for this session.
    }
  }

  toggle(): void {
    this.set(this.current() === 'en' ? 'ta' : 'en');
  }

  /** Picks the right variant of a bilingual pair, falling back to English if Tamil is missing. */
  pick(en: string | null | undefined, ta: string | null | undefined): string {
    return (this.current() === 'ta' ? ta || en : en) ?? '';
  }
}

function readStoredLanguage(): Language {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'ta' ? 'ta' : 'en';
  } catch {
    return 'en';
  }
}
