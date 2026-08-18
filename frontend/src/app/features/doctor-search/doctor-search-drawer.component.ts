import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';

import { ApiService } from '../../core/api.service';
import type {
  Alert,
  AvailabilityWindow,
  CreateDoctorSearchRequest,
  DoctorSearchResponse,
  FacilityResult,
  SpecialtyOption,
  SpecialtyResolution
} from '../../core/models';

const AVAILABILITY: { value: AvailabilityWindow; label: string; hint: string }[] = [
  { value: 'this_week', label: 'This week', hint: 'Any day in the coming days' },
  { value: 'evenings', label: 'Evenings', hint: 'After 18:00, when hours are listed' },
  { value: 'weekend', label: 'Weekend', hint: 'Saturday or Sunday, when hours are listed' },
  { value: 'anytime', label: 'Anytime', hint: 'No time filter' }
];

/**
 * Three-step nearby-clinic drawer. Step 1 confirms specialty; step 2 asks town and availability.
 * Disease-class names are drug-class information, never a statement about the patient.
 */
@Component({
  selector: 'mt-doctor-search-drawer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="fixed inset-0 z-40 flex justify-end" role="dialog" aria-labelledby="doctor-search-title">
      <div class="flex-1 bg-slate-900/20" (click)="closed.emit()" aria-hidden="true"></div>

      <aside class="flex h-full w-full max-w-lg flex-col border-l border-slate-200 bg-white shadow-xl">
        <header class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <div>
            <h2 id="doctor-search-title" class="font-medium text-slate-900">Find a nearby clinic</h2>
            <p class="mt-0.5 text-xs text-slate-500">
              Nearby facilities from public map data — not a referral.
            </p>
          </div>
          <button type="button" class="text-slate-400 hover:text-slate-700" (click)="closed.emit()" aria-label="Close">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="h-5 w-5" aria-hidden="true">
              <path stroke-linecap="round" d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        </header>

        <ol class="flex border-b border-slate-100 px-5 py-3 text-xs text-slate-500">
          <li [class]="step() === 1 ? 'font-medium text-brand-700' : ''">1 Specialty</li>
          <li class="px-2 text-slate-300">·</li>
          <li [class]="step() === 2 ? 'font-medium text-brand-700' : ''">2 Location</li>
          <li class="px-2 text-slate-300">·</li>
          <li [class]="step() === 3 ? 'font-medium text-brand-700' : ''">3 Results</li>
        </ol>

        <div class="flex-1 overflow-y-auto px-5 py-5">
          @if (step() === 1) {
            @if (error(); as message) {
              <p class="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">{{ message }}</p>
            } @else if (suggestion(); as specialty) {
              <p class="text-sm text-slate-600">For this alert we suggest:</p>
              <p class="mt-1 text-2xl font-semibold tracking-tight text-slate-900">
                {{ specialty.label.toUpperCase() }}
              </p>
              <p class="mt-2 text-sm leading-relaxed text-slate-600">{{ specialty.reason }}</p>

              <button
                type="button"
                class="mt-4 inline-flex items-center gap-1 text-sm font-medium text-brand-700"
                (click)="whyOpen.set(!whyOpen())"
                [attr.aria-expanded]="whyOpen()"
              >
                Why this specialty?
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="h-4 w-4" [class.rotate-180]="whyOpen()" aria-hidden="true">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M6 9l6 6 6-6" />
                </svg>
              </button>

              @if (whyOpen()) {
                <div class="mt-3 rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                  <p class="text-xs leading-relaxed text-slate-500">
                    Drug-class information from NLM RxClass (MED-RT, may_treat). This is not a
                    statement about the patient.
                  </p>
                  @if (specialty.evidence.length === 0) {
                    <p class="mt-2 text-xs text-slate-500">No linked class pages for this suggestion.</p>
                  } @else {
                    <div class="mt-3 flex flex-wrap gap-2">
                      @for (chip of specialty.evidence; track chip.sourceId ?? chip.label) {
                        @if (chip.sourceUrl) {
                          <a
                            [href]="chip.sourceUrl"
                            target="_blank"
                            rel="noopener noreferrer"
                            class="inline-flex items-center gap-1 rounded-full border border-slate-200 bg-white px-3 py-1 text-xs text-brand-700 hover:border-brand-500"
                          >
                            {{ chip.label }}
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="h-3 w-3" aria-hidden="true">
                              <path stroke-linecap="round" stroke-linejoin="round" d="M14 5h5v5M19 5l-7 7M10 5H6a1 1 0 0 0-1 1v12a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-4" />
                            </svg>
                          </a>
                        } @else {
                          <span class="rounded-full border border-slate-200 bg-white px-3 py-1 text-xs text-slate-600">
                            {{ chip.label }}
                          </span>
                        }
                      }
                    </div>
                  }
                </div>
              }

              <label class="mt-6 block text-sm font-medium text-slate-700">
                Change specialty ▾
                <select
                  class="mt-1 w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800"
                  [value]="specialty.code"
                  (change)="onSpecialtyChange($event)"
                >
                  @for (option of specialties(); track option.code) {
                    <option [value]="option.code">{{ option.label }}</option>
                  }
                </select>
              </label>
            } @else {
              <div class="space-y-3" aria-busy="true">
                <div class="h-4 w-40 animate-pulse rounded bg-slate-100"></div>
                <div class="h-8 w-56 animate-pulse rounded bg-slate-100"></div>
                <div class="h-16 w-full animate-pulse rounded bg-slate-100"></div>
              </div>
            }
          } @else if (step() === 2) {
            <p class="text-sm text-slate-600">
              Searching
              <span class="font-medium text-slate-800">{{ suggestion()?.label }}</span>
              near you. Town names in Tamil or Sinhala are fine.
            </p>

            <label class="mt-6 block text-sm font-medium text-slate-700" for="doctor-search-location">
              Town or district
              <input
                id="doctor-search-location"
                type="text"
                maxlength="200"
                autocomplete="address-level2"
                spellcheck="false"
                placeholder="Town or district in Sri Lanka"
                class="mt-1 w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none placeholder:text-slate-400"
                [value]="locationText()"
                (input)="onLocationInput($event)"
              />
            </label>

            <button
              type="button"
              class="mt-2 inline-flex items-center gap-2 text-sm font-medium text-brand-700 disabled:opacity-40"
              [disabled]="geoBusy()"
              (click)="useMyLocation()"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="h-4 w-4" aria-hidden="true">
                <path stroke-linecap="round" stroke-linejoin="round" d="M12 21s7-4.4 7-10a7 7 0 1 0-14 0c0 5.6 7 10 7 10z" />
                <circle cx="12" cy="11" r="2.5" />
              </svg>
              {{ geoBusy() ? 'Reading location…' : 'Use my location' }}
            </button>

            @if (usingDevice()) {
              <p class="mt-2 text-xs text-slate-500">Using your device location (straight-line search from those coordinates).</p>
            }
            @if (geoError(); as geo) {
              <p class="mt-2 text-xs text-red-700">{{ geo }}</p>
            }
            @if (searchError(); as search) {
              <p class="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800">{{ search }}</p>
            }

            <fieldset class="mt-8">
              <legend class="text-sm font-medium text-slate-700">When can you go?</legend>
              <div class="mt-3 space-y-2">
                @for (option of availabilityOptions; track option.value) {
                  <label class="flex cursor-pointer items-start gap-3 rounded-lg border border-slate-200 px-3 py-2 has-[:checked]:border-brand-500 has-[:checked]:bg-brand-50">
                    <input
                      type="radio"
                      name="availability"
                      class="mt-1"
                      [value]="option.value"
                      [checked]="availability() === option.value"
                      (change)="availability.set(option.value)"
                    />
                    <span>
                      <span class="block text-sm text-slate-800">{{ option.label }}</span>
                      <span class="block text-xs text-slate-500">{{ option.hint }}</span>
                    </span>
                  </label>
                }
              </div>
            </fieldset>
          } @else {
            @if (searching()) {
              <p class="text-sm text-slate-600">Looking up nearby facilities from public map data…</p>
              <div class="mt-4 space-y-3" aria-busy="true">
                <div class="h-40 animate-pulse rounded-xl bg-slate-100"></div>
                <div class="h-40 animate-pulse rounded-xl bg-slate-100"></div>
                <div class="h-40 animate-pulse rounded-xl bg-slate-100"></div>
              </div>
            } @else if (searchResult(); as result) {
              @if (result.status === 'empty') {
                <div class="rounded-xl border border-dashed border-slate-300 bg-slate-50 px-4 py-5">
                  <p class="text-xs font-semibold uppercase tracking-wide text-slate-500">No facilities in range</p>
                  <p class="mt-2 text-sm leading-relaxed text-slate-800">
                    {{ result.message ?? facilityCountLabel(0) }}
                  </p>
                  @if (result.nearestLargerCity) {
                    <p class="mt-2 text-sm text-slate-600">
                      Coverage is thinner outside large towns. The nearest other mapped town is
                      <span class="font-medium">{{ result.nearestLargerCity }}</span>.
                    </p>
                  }
                  <div class="mt-4 flex flex-col gap-2">
                    @if (result.suggestedNextRadiusMeters) {
                      <button
                        type="button"
                        class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-800"
                        (click)="runSearch({ radiusMeters: result.suggestedNextRadiusMeters })"
                      >
                        Search within 40 km
                      </button>
                    }
                    <button
                      type="button"
                      class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-800"
                      (click)="searchAnySpecialty()"
                    >
                      Search all healthcare facilities, any specialty
                    </button>
                    @if (result.nearestLargerCity) {
                      <button
                        type="button"
                        class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-brand-700"
                        (click)="searchPlace(result.nearestLargerCity)"
                      >
                        Try {{ result.nearestLargerCity }}
                      </button>
                    }
                  </div>
                </div>
              } @else if (result.status === 'failed' || result.status === 'not_configured') {
                <div class="rounded-xl border border-red-200 bg-red-50 px-4 py-5">
                  <p class="text-xs font-semibold uppercase tracking-wide text-red-700">
                    {{ result.status === 'not_configured' ? 'Search not configured' : 'Map data unreachable' }}
                  </p>
                  <p class="mt-2 text-sm leading-relaxed text-red-950">
                    {{ result.message ?? 'We couldn\'t reach the map data service just now. Nothing is shown rather than showing you something unverified.' }}
                  </p>
                  @if (result.staleCache) {
                    <p class="mt-3 inline-flex items-center rounded-full border border-amber-300 bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-900">
                      Stale · showing results cached on {{ fetchedLabel(result.fetchedAtUtc) }}
                    </p>
                  }
                  <button
                    type="button"
                    class="mt-4 rounded-lg bg-red-800 px-3 py-2 text-sm font-medium text-white"
                    (click)="runSearch()"
                  >
                    Try again
                  </button>
                </div>
              } @else if (result.status === 'location_not_found') {
                <div class="rounded-xl border border-amber-200 bg-amber-50 px-4 py-5">
                  <p class="text-xs font-semibold uppercase tracking-wide text-amber-800">Place not found</p>
                  <p class="mt-2 text-sm leading-relaxed text-amber-950">
                    {{ result.message ?? 'We couldn\'t find that place. Try a nearby town or district.' }}
                  </p>
                  <p class="mt-3 text-xs text-amber-900">Try a mapped town:</p>
                  <div class="mt-2 flex flex-wrap gap-2">
                    @for (place of result.suggestedPlaces ?? places(); track place) {
                      <button
                        type="button"
                        class="rounded-full border border-amber-300 bg-white px-3 py-1 text-xs font-medium text-amber-950"
                        (click)="searchPlace(place)"
                      >
                        {{ place }}
                      </button>
                    }
                  </div>
                </div>
              } @else {
                <p class="text-sm text-slate-600">
                  {{ result.message ?? facilityCountLabel(result.results?.length ?? 0) }}
                </p>
                @if (result.origin?.resolvedPlace || result.radiusMeters) {
                  <p class="mt-1 text-xs text-slate-500">
                    @if (result.origin?.resolvedPlace) {
                      Near {{ result.origin.resolvedPlace }}
                    }
                    @if (result.radiusMeters) {
                      · within {{ kmLabel(result.radiusMeters) }}
                    }
                  </p>
                }
              }

              @if (showFacilityCards(result)) {
                <ul class="mt-4 space-y-4">
                  @for (facility of result.results; track facility.sourceRef) {
                    <li class="rounded-xl border border-slate-200 bg-white px-4 py-4">
                      <div class="flex items-start justify-between gap-3">
                        <div class="min-w-0">
                          <h3 class="font-medium text-slate-900">{{ displayName(facility) }}</h3>
                        </div>
                        <span class="shrink-0 rounded-full border border-slate-200 px-2 py-0.5 text-xs text-slate-600">
                          {{ availabilityBadge(facility.availabilityMatch) }}
                        </span>
                      </div>

                      <dl class="mt-3 space-y-1.5 text-sm text-slate-700">
                        <div class="flex gap-2">
                          <dt class="w-24 shrink-0 text-xs text-slate-500">Category</dt>
                          <dd>{{ categoryLabel(facility.category) }}</dd>
                        </div>
                        <div class="flex gap-2">
                          <dt class="w-24 shrink-0 text-xs text-slate-500">Specialty</dt>
                          <dd>{{ listed(facility.specialtyTag) }}</dd>
                        </div>
                        <div class="flex gap-2">
                          <dt class="w-24 shrink-0 text-xs text-slate-500">Address</dt>
                          <dd>{{ listed(facility.address) }}</dd>
                        </div>
                        <div class="flex gap-2">
                          <dt class="w-24 shrink-0 text-xs text-slate-500">Distance</dt>
                          <dd>{{ distanceLabel(facility.distanceMeters) }}</dd>
                        </div>
                        <div class="flex gap-2">
                          <dt class="w-24 shrink-0 text-xs text-slate-500">Phone</dt>
                          <dd>
                            @if (facility.phone?.trim()) {
                              <a class="text-brand-700" [href]="'tel:' + facility.phone">{{ facility.phone }}</a>
                            } @else {
                              Not listed
                            }
                          </dd>
                        </div>
                        <div class="flex gap-2">
                          <dt class="w-24 shrink-0 text-xs text-slate-500">Website</dt>
                          <dd>
                            @if (facility.website?.trim(); as site) {
                              <a class="break-all text-brand-700" [href]="websiteHref(site)" target="_blank" rel="noopener">{{ site }}</a>
                            } @else {
                              Not listed
                            }
                          </dd>
                        </div>
                        <div class="flex gap-2">
                          <dt class="w-24 shrink-0 text-xs text-slate-500">Hours</dt>
                          <dd class="break-words font-mono text-xs">{{ listed(facility.openingHours) }}</dd>
                        </div>
                      </dl>

                      <button
                        type="button"
                        class="mt-3 inline-flex items-center gap-1 text-xs font-medium text-brand-700"
                        (click)="toggleRank(facility.sourceRef)"
                        [attr.aria-expanded]="rankOpen()[facility.sourceRef] === true"
                      >
                        Why ranked here ▸
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="h-3.5 w-3.5" [class.rotate-180]="rankOpen()[facility.sourceRef]" aria-hidden="true">
                          <path stroke-linecap="round" stroke-linejoin="round" d="M6 9l6 6 6-6" />
                        </svg>
                      </button>
                      @if (rankOpen()[facility.sourceRef]) {
                        <ul class="mt-2 list-disc space-y-0.5 pl-5 text-xs text-slate-600">
                          @for (reason of facility.rankReasons ?? []; track reason) {
                            <li>{{ reason }}</li>
                          }
                        </ul>
                      }

                      <div class="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-3">
                        <a
                          class="text-sm font-medium text-brand-700"
                          [href]="mapHref(facility)"
                          target="_blank"
                          rel="noopener"
                        >
                          Open in map
                        </a>
                        <p class="text-[11px] text-slate-500">
                          {{ sourceLabel(result.provider) }}
                          ·
                          {{ fetchedLabel(result.fetchedAtUtc) }}
                          @if (result.servedFromCache) {
                            · cached
                          }
                          @if (result.staleCache) {
                            · stale
                          }
                        </p>
                      </div>
                    </li>
                  }
                </ul>
              }

              @if (result.attribution) {
                <p class="mt-4 text-[11px] text-slate-400">{{ result.attribution }}</p>
              }

              <p class="mt-6 border-t border-slate-100 pt-4 text-xs leading-relaxed text-slate-500">
                MediTrail does not diagnose. These are nearby facilities from public map data, not a
                referral. MediTrail does not verify practitioner registration —
                <a
                  href="https://slmc.gov.lk/public/en/services/public"
                  target="_blank"
                  rel="noopener"
                  class="font-medium text-brand-700"
                >
                  check the SLMC public register
                  <span aria-hidden="true">↗</span>
                </a>
              </p>
            }
          }
        </div>

        <footer class="border-t border-slate-200 px-5 py-4">
          @if (step() === 1) {
            <button
              type="button"
              class="w-full rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-40"
              [disabled]="!suggestion()"
              (click)="step.set(2)"
            >
              Continue
            </button>
          } @else if (step() === 2) {
            <div class="flex items-center gap-3">
              <button type="button" class="text-sm text-brand-700" (click)="step.set(1)">Back</button>
              <button
                type="button"
                class="flex-1 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-40"
                [disabled]="!canSearch()"
                (click)="runSearch({ resetRadius: true })"
              >
                Search
              </button>
            </div>
          } @else {
            <button type="button" class="text-sm text-brand-700" (click)="step.set(2)">Back to location</button>
          }
        </footer>
      </aside>
    </div>
  `
})
export class DoctorSearchDrawerComponent {
  private readonly api = inject(ApiService);

  readonly patientId = input.required<string>();
  readonly alert = input<Alert | null>(null);
  readonly closed = output<void>();

  protected readonly availabilityOptions = AVAILABILITY;

  protected readonly step = signal(1);
  protected readonly whyOpen = signal(false);
  protected readonly specialties = signal<SpecialtyOption[]>([]);
  protected readonly suggestion = signal<SpecialtyResolution | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly locationText = signal('');
  protected readonly latitude = signal<number | null>(null);
  protected readonly longitude = signal<number | null>(null);
  protected readonly usingDevice = signal(false);
  protected readonly availability = signal<AvailabilityWindow | ''>('');
  protected readonly geoBusy = signal(false);
  protected readonly geoError = signal<string | null>(null);
  protected readonly searchError = signal<string | null>(null);
  protected readonly searching = signal(false);
  protected readonly searchResult = signal<DoctorSearchResponse | null>(null);
  protected readonly rankOpen = signal<Record<string, boolean>>({});
  protected readonly places = signal<string[]>([]);
  protected readonly searchRadius = signal<number | null>(null);

  protected readonly canSearch = computed(() => {
    const located =
      this.locationText().trim().length > 0 ||
      (this.latitude() !== null && this.longitude() !== null);
    return located && this.availability() !== '' && !this.searching();
  });

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected onSpecialtyChange(event: Event): void {
    this.loadSuggestion((event.target as HTMLSelectElement).value);
  }

  protected onLocationInput(event: Event): void {
    this.locationText.set((event.target as HTMLInputElement).value);
    this.usingDevice.set(false);
    this.latitude.set(null);
    this.longitude.set(null);
    this.geoError.set(null);
  }

  protected useMyLocation(): void {
    if (!navigator.geolocation) {
      this.geoError.set('This browser cannot share a location. Type a town instead.');
      return;
    }

    this.geoBusy.set(true);
    this.geoError.set(null);

    navigator.geolocation.getCurrentPosition(
      position => {
        this.latitude.set(position.coords.latitude);
        this.longitude.set(position.coords.longitude);
        this.usingDevice.set(true);
        this.geoBusy.set(false);
      },
      err => {
        this.usingDevice.set(false);
        this.latitude.set(null);
        this.longitude.set(null);
        this.geoBusy.set(false);
        this.geoError.set(this.geoFailureMessage(err));
      },
      { enableHighAccuracy: false, timeout: 10000, maximumAge: 60_000 }
    );
  }

  protected runSearch(opts?: {
    radiusMeters?: number;
    specialtyOverride?: string;
    locationText?: string;
    resetRadius?: boolean;
  }): void {
    if (opts?.locationText != null) {
      this.locationText.set(opts.locationText);
      this.usingDevice.set(false);
      this.latitude.set(null);
      this.longitude.set(null);
    }
    if (opts?.specialtyOverride) {
      const match = this.specialties().find(s => s.code === opts.specialtyOverride);
      const current = this.suggestion();
      if (current) {
        this.suggestion.set({
          ...current,
          code: opts.specialtyOverride,
          label: match?.label ?? opts.specialtyOverride,
          resolvedBy: 'user_override',
          reason: 'Chosen from the specialty list.'
        });
      }
    }
    if (opts?.resetRadius) this.searchRadius.set(null);
    if (opts?.radiusMeters != null) this.searchRadius.set(opts.radiusMeters);

    if (!this.canSearch()) return;
    const specialty = this.suggestion();
    const when = this.availability();
    if (!specialty || when === '') return;

    const body: CreateDoctorSearchRequest = {
      locationText: this.locationText().trim(),
      availability: when,
      specialtyOverride: specialty.code
    };
    const alertId = this.alert()?.id;
    if (alertId) body.alertId = alertId;
    const lat = this.latitude();
    const lng = this.longitude();
    if (lat !== null && lng !== null) {
      body.latitude = lat;
      body.longitude = lng;
    }
    const radius = this.searchRadius();
    if (radius !== null) body.radiusMeters = radius;

    this.searching.set(true);
    this.searchError.set(null);
    this.searchResult.set(null);
    this.rankOpen.set({});
    this.step.set(3);

    this.api.searchDoctors(this.patientId(), body).subscribe({
      next: result => {
        this.searchResult.set(result);
        this.searching.set(false);
      },
      error: (err: Error) => {
        this.searching.set(false);
        this.searchError.set(err.message);
        this.step.set(2);
      }
    });
  }

  protected searchAnySpecialty(): void {
    this.runSearch({ specialtyOverride: 'general_practice' });
  }

  protected searchPlace(place: string): void {
    this.runSearch({ locationText: place, resetRadius: true });
  }

  protected showFacilityCards(result: DoctorSearchResponse): boolean {
    const count = result.results?.length ?? 0;
    if (count === 0) return false;
    if (result.status === 'empty' || result.status === 'location_not_found') return false;
    if (result.status === 'ok') return true;
    return result.status === 'failed' && result.staleCache === true;
  }

  protected facilityCountLabel(count: number): string {
    if (count === 1) return '1 nearby facility from public map data.';
    return `${count} nearby facilities from public map data.`;
  }

  protected listed(value: string | null | undefined): string {
    const trimmed = value?.trim();
    return trimmed ? trimmed : 'Not listed';
  }

  protected displayName(facility: FacilityResult): string {
    const name = facility.name?.trim();
    if (name) return name;
    const type = facility.category?.trim();
    if (type) return this.categoryLabel(type);
    return 'Not listed';
  }

  protected categoryLabel(category: string | null | undefined): string {
    const raw = category?.trim();
    if (!raw) return 'Not listed';
    return raw.charAt(0).toUpperCase() + raw.slice(1);
  }

  protected distanceLabel(meters: number): string {
    return `${(meters / 1000).toFixed(1)} km straight-line`;
  }

  protected kmLabel(meters: number): string {
    return `${(meters / 1000).toFixed(0)} km`;
  }

  protected availabilityBadge(match: string): string {
    switch (match) {
      case 'match':
        return 'Hours match';
      case 'indeterminate':
        return 'Hours unclear';
      case 'no_match':
        return 'May not match';
      default:
        return 'Hours unknown';
    }
  }

  protected websiteHref(site: string): string {
    return /^https?:\/\//i.test(site) ? site : `https://${site}`;
  }

  protected mapHref(facility: FacilityResult): string {
    if (facility.mapUrl?.trim()) return facility.mapUrl.trim();
    return `https://www.openstreetmap.org/?mlat=${facility.latitude}&mlon=${facility.longitude}#map=17/${facility.latitude}/${facility.longitude}`;
  }

  protected sourceLabel(provider: string): string {
    return provider === 'openstreetmap' ? 'OpenStreetMap' : provider;
  }

  protected fetchedLabel(iso: string | undefined): string {
    if (!iso) return 'Not listed';
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return 'Not listed';
    return (
      date.toLocaleString('en-GB', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC' }) + ' UTC'
    );
  }

  protected toggleRank(sourceRef: string): void {
    this.rankOpen.update(open => ({ ...open, [sourceRef]: !open[sourceRef] }));
  }

  private load(): void {
    this.api.getSpecialties().subscribe({
      next: list => this.specialties.set(list),
      error: (err: Error) => this.error.set(err.message)
    });
    this.api.getPlaces().subscribe({ next: list => this.places.set(list) });
    this.loadSuggestion();
  }

  private loadSuggestion(override?: string): void {
    this.api.suggestSpecialty(this.patientId(), this.alert()?.id, override).subscribe({
      next: value => {
        this.suggestion.set(value);
        this.error.set(null);
      },
      error: (err: Error) => this.error.set(err.message)
    });
  }

  private geoFailureMessage(err: GeolocationPositionError): string {
    switch (err.code) {
      case err.PERMISSION_DENIED:
        return 'Location permission was denied. Type a town or district instead.';
      case err.POSITION_UNAVAILABLE:
        return 'Your location is not available. Type a town or district instead.';
      case err.TIMEOUT:
        return 'Could not read your location in time. Type a town instead.';
      default:
        return 'Could not read your location. Type a town instead.';
    }
  }
}
