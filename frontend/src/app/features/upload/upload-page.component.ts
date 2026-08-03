import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

import { ApiService } from '../../core/api.service';

const MAX_BYTES = 10 * 1024 * 1024;
const ACCEPTED = ['.png', '.jpg', '.jpeg', '.pdf'];
const MAX_EDGE_PX = 2000;

interface StagedFile {
  file: File;
  /** Why this file will be refused, if it will be. Checked before upload (§10.2 validation). */
  rejection: string | null;
}

/** Upload screen (§10.2) — get documents in with minimum friction. */
@Component({
  selector: 'mt-upload-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <section class="mx-auto max-w-3xl px-6 py-10">
      <h1 class="text-2xl font-semibold tracking-tight text-slate-900">Upload documents</h1>
      <p class="mt-1 text-sm text-slate-500">
        Prescriptions, lab reports, discharge summaries — photos or scans. Add everything you have;
        MediTrail reads across them all.
      </p>

      <label
        class="mt-8 flex cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed px-6 py-14 text-center transition"
        [class]="dragging() ? 'border-brand-500 bg-brand-50' : 'border-slate-300 bg-white'"
        (dragover)="onDragOver($event)"
        (dragleave)="dragging.set(false)"
        (drop)="onDrop($event)"
      >
        <span class="text-sm font-medium text-slate-900">Drop files here, or click to choose</span>
        <span class="mt-1 text-xs text-slate-500">PNG, JPG or PDF · up to 10 MB each</span>
        <input
          type="file"
          class="sr-only"
          multiple
          accept=".png,.jpg,.jpeg,.pdf"
          (change)="onPick($event)"
        />
      </label>

      <div class="mt-6">
        <label class="text-xs font-medium text-slate-600" for="visitLabel">
          Visit label <span class="font-normal text-slate-400">(optional)</span>
        </label>
        <input
          id="visitLabel"
          name="visitLabel"
          [(ngModel)]="visitLabel"
          placeholder="e.g. Year 1, or March 2024"
          class="mt-1 w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm outline-none placeholder:text-slate-400"
        />
      </div>

      @if (staged().length > 0) {
        <ul class="mt-6 divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white">
          @for (item of staged(); track item.file.name + item.file.size) {
            <li class="flex items-center justify-between px-4 py-3">
              <div class="min-w-0">
                <p class="truncate text-sm text-slate-900">{{ item.file.name }}</p>
                @if (item.rejection) {
                  <p class="mt-0.5 text-xs text-red-700">{{ item.rejection }}</p>
                } @else {
                  <p class="mt-0.5 text-xs text-slate-500">{{ sizeLabel(item.file.size) }}</p>
                }
              </div>
              <button
                type="button"
                class="ml-4 shrink-0 text-xs text-slate-500 hover:text-red-700"
                (click)="remove(item)"
              >
                Remove
              </button>
            </li>
          }
        </ul>
      }

      @if (error(); as message) {
        <p class="mt-6 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {{ message }}
        </p>
      }

      <button
        type="button"
        class="mt-8 w-full rounded-lg bg-brand-600 px-4 py-3 text-sm font-medium text-white disabled:opacity-40"
        [disabled]="uploading() || uploadableCount() === 0"
        (click)="upload()"
      >
        {{ uploading() ? 'Uploading…' : 'Analyze my records (' + uploadableCount() + ')' }}
      </button>
    </section>
  `
})
export class UploadPageComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  /** Bound from the route parameter. */
  readonly patientId = input.required<string>();

  protected visitLabel = '';
  protected readonly staged = signal<StagedFile[]>([]);
  protected readonly dragging = signal(false);
  protected readonly uploading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected uploadableCount(): number {
    return this.staged().filter(s => !s.rejection).length;
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.add(Array.from(event.dataTransfer?.files ?? []));
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.add(Array.from(input.files ?? []));
    input.value = '';
  }

  protected remove(item: StagedFile): void {
    this.staged.update(list => list.filter(s => s !== item));
  }

  protected sizeLabel(bytes: number): string {
    return bytes < 1024 * 1024
      ? `${Math.round(bytes / 1024)} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected async upload(): Promise<void> {
    const files = this.staged().filter(s => !s.rejection).map(s => s.file);
    if (files.length === 0 || this.uploading()) return;

    this.uploading.set(true);
    this.error.set(null);

    // Downscale large photographs client-side (FR-2.3) — cheaper upload, and the vision model
    // gains nothing from a 12-megapixel scan of an A5 prescription.
    const prepared = await Promise.all(files.map(file => downscaleIfLarge(file)));

    this.api.uploadDocuments(this.patientId(), prepared, this.visitLabel.trim() || undefined).subscribe({
      next: result => {
        this.uploading.set(false);

        if (result.accepted.length === 0) {
          this.error.set(
            result.rejected.map(r => `${r.fileName}: ${r.reason}`).join(' ') ||
              'No files were accepted.'
          );
          return;
        }

        this.router.navigate(['/patients', this.patientId(), 'processing']);
      },
      error: (err: Error) => {
        this.error.set(err.message);
        this.uploading.set(false);
      }
    });
  }

  private add(files: File[]): void {
    const staged = files.map(file => ({ file, rejection: validate(file) }));
    this.staged.update(list => [...list, ...staged]);
  }
}

/** Client-side gate mirroring the server's (FR-2.2, FR-2.3), so the user hears why immediately. */
function validate(file: File): string | null {
  const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();

  if (!ACCEPTED.includes(extension)) {
    return `Unsupported format. Accepted: ${ACCEPTED.join(', ')}.`;
  }
  if (file.size === 0) {
    return 'This file is empty.';
  }
  if (file.size > MAX_BYTES) {
    return 'Larger than the 10 MB limit.';
  }
  return null;
}

/**
 * Resizes an image so its longest edge is at most 2000px. PDFs and small images pass through
 * untouched. Falls back to the original file on any canvas failure — a failed downscale must
 * never cost the user their upload.
 */
async function downscaleIfLarge(file: File): Promise<File> {
  if (!file.type.startsWith('image/')) return file;

  try {
    const bitmap = await createImageBitmap(file);
    const longest = Math.max(bitmap.width, bitmap.height);
    if (longest <= MAX_EDGE_PX) {
      bitmap.close();
      return file;
    }

    const scale = MAX_EDGE_PX / longest;
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(bitmap.width * scale);
    canvas.height = Math.round(bitmap.height * scale);

    const context = canvas.getContext('2d');
    if (!context) {
      bitmap.close();
      return file;
    }

    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    bitmap.close();

    const blob = await new Promise<Blob | null>(resolve =>
      canvas.toBlob(resolve, 'image/jpeg', 0.92)
    );
    if (!blob) return file;

    const name = file.name.replace(/\.[^.]+$/, '') + '.jpg';
    return new File([blob], name, { type: 'image/jpeg' });
  } catch {
    return file;
  }
}
