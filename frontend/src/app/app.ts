import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

import { LanguageService } from './core/language.service';
import { DisclaimerComponent } from './shared/disclaimer.component';

/**
 * Application shell: header with the EN/TA toggle (FR-8.5) and the persistent
 * medical disclaimer (FR-8.7) that must appear on every screen.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, DisclaimerComponent],
  template: `
    <div class="flex min-h-dvh flex-col">
      <header class="border-b border-slate-200 bg-white">
        <div class="mx-auto flex max-w-7xl items-center justify-between px-6 py-4">
          <a routerLink="/" class="flex items-baseline gap-2">
            <span class="text-lg font-semibold tracking-tight text-slate-900">MediTrail</span>
            <span class="hidden text-xs text-slate-400 sm:inline">Medical record cross-checker</span>
          </a>

          <div class="flex items-center gap-1 rounded-lg border border-slate-200 p-0.5 print:hidden">
            <button
              type="button"
              class="rounded px-2.5 py-1 text-xs font-medium"
              [class]="language.current() === 'en' ? 'bg-brand-600 text-white' : 'text-slate-600'"
              [attr.aria-pressed]="language.current() === 'en'"
              (click)="language.set('en')"
            >
              EN
            </button>
            <button
              type="button"
              class="rounded px-2.5 py-1 text-xs font-medium"
              [class]="language.current() === 'ta' ? 'bg-brand-600 text-white' : 'text-slate-600'"
              [attr.aria-pressed]="language.current() === 'ta'"
              (click)="language.set('ta')"
            >
              தமிழ்
            </button>
          </div>
        </div>
      </header>

      <main class="flex-1">
        <router-outlet />
      </main>

      <footer class="bg-white">
        <mt-disclaimer />
      </footer>
    </div>
  `
})
export class App {
  protected readonly language = inject(LanguageService);
}
