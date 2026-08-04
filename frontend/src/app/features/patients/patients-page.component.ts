import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { ApiService } from '../../core/api.service';
import type { PatientSummary } from '../../core/models';

/** Patients list (§10.1) — entry point and multi-patient support. */
@Component({
  selector: 'mt-patients-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule, RouterLink],
  template: `
    <section class="mx-auto max-w-4xl px-6 py-10">
      <header class="mb-8">
        <h1 class="text-2xl font-semibold tracking-tight text-slate-900">Patients</h1>
        <p class="mt-1 text-sm text-slate-500">
          Your complete medical trail — read by AI, verified against official drug data, explained in
          your language, with the original document always one click away.
        </p>
      </header>

      <form class="mb-8 flex gap-2" (ngSubmit)="create()">
        <input
          name="displayName"
          [(ngModel)]="newName"
          placeholder="Patient name"
          maxlength="200"
          class="flex-1 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm outline-none placeholder:text-slate-400"
        />
        <!-- The form is already open, so the label describes the action being confirmed,
             not the thing being opened. -->
        <button
          type="submit"
          [disabled]="busy() || !newName.trim()"
          class="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-40"
        >
          Done
        </button>
      </form>

      @if (error(); as message) {
        <p class="mb-6 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {{ message }}
        </p>
      }

      @if (loading()) {
        <div class="space-y-3" aria-busy="true">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-20 animate-pulse rounded-xl bg-slate-100"></div>
          }
        </div>
      } @else if (patients().length === 0) {
        <div class="rounded-xl border border-dashed border-slate-300 px-6 py-12 text-center">
          <h2 class="text-sm font-medium text-slate-900">No patients yet</h2>
          <p class="mx-auto mt-2 max-w-md text-sm text-slate-500">
            Create a patient, then upload their prescriptions and lab reports. MediTrail reads every
            document, merges them into one timeline, and flags risks worth asking a doctor about.
          </p>
        </div>
      } @else {
        <ul class="space-y-3">
          @for (patient of patients(); track patient.id) {
            <li class="relative">
              <!-- Outside the card link, so opening a patient and deleting one are not the same tap. -->
              <button
                type="button"
                class="absolute right-3 top-3 z-10 rounded p-1 text-xs text-slate-400 hover:text-red-700"
                [attr.aria-label]="'Delete ' + patient.displayName"
                (click)="confirmDelete(patient)"
              >
                Delete
              </button>

              <a
                [routerLink]="['/patients', patient.id]"
                class="flex items-center justify-between rounded-xl border border-slate-200 bg-white px-5 py-4 pr-24 hover:border-brand-500"
              >
                <div>
                  <p class="font-medium text-slate-900">{{ patient.displayName }}</p>
                  <p class="mt-0.5 text-xs text-slate-500">
                    {{ patient.documentCount }} document{{ patient.documentCount === 1 ? '' : 's' }}
                    · updated {{ patient.updatedAt | date: 'mediumDate' }}
                  </p>
                </div>

                <div class="flex items-center gap-2">
                  @if (patient.redAlertCount > 0) {
                    <span class="rounded-full border border-red-200 bg-red-50 px-2 py-0.5 text-xs font-medium text-red-800">
                      ⚠ {{ patient.redAlertCount }} risk{{ patient.redAlertCount === 1 ? '' : 's' }}
                    </span>
                  }
                  @if (patient.amberAlertCount > 0) {
                    <span class="rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-800">
                      ! {{ patient.amberAlertCount }} to check
                    </span>
                  }
                  <span class="text-xs text-slate-400">{{ patient.status }}</span>
                </div>
              </a>
            </li>
          }
        </ul>
      }
    </section>
  `
})
export class PatientsPageComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  protected newName = '';
  protected readonly patients = signal<PatientSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  /**
   * Deleting a patient removes their documents, everything read from them, and the stored files.
   * It cannot be undone, so it is confirmed by name rather than by a bare "are you sure".
   */
  protected confirmDelete(patient: PatientSummary): void {
    const documents = patient.documentCount === 1 ? '1 document' : `${patient.documentCount} documents`;

    const confirmed = confirm(
      `Delete ${patient.displayName} and ${documents}?\n\n` +
        'The uploaded files and everything read from them are removed permanently. This cannot be undone.'
    );

    if (!confirmed) return;

    this.api.deletePatient(patient.id).subscribe({
      next: () => this.patients.update(list => list.filter(p => p.id !== patient.id)),
      error: (err: Error) => this.error.set(err.message)
    });
  }

  protected create(): void {
    const name = this.newName.trim();
    if (!name || this.busy()) return;

    this.busy.set(true);
    this.error.set(null);

    this.api.createPatient({ displayName: name }).subscribe({
      next: patient => {
        this.newName = '';
        this.busy.set(false);
        // Straight to upload — a patient with no documents has nothing to show yet.
        this.router.navigate(['/patients', patient.id, 'upload']);
      },
      error: (err: Error) => {
        this.error.set(err.message);
        this.busy.set(false);
      }
    });
  }

  private load(): void {
    this.api.listPatients().subscribe({
      next: patients => {
        this.patients.set(patients);
        this.loading.set(false);
      },
      error: (err: Error) => {
        this.error.set(err.message);
        this.loading.set(false);
      }
    });
  }
}
