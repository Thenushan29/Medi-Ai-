import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

import { environment } from '../../environments/environment';
import type {
  Alert,
  ChatAnswer,
  ChatMessage,
  ChatTurn,
  CreateDoctorSearchRequest,
  CreatePatientRequest,
  DoctorSearchResponse,
  DocumentDetail,
  LabTrend,
  MedicationGroup,
  PatientDetail,
  PatientSummary,
  ProcessingStatus,
  SpecialtyOption,
  SpecialtyResolution,
  TimelineEntry,
  UploadResult
} from './models';

/**
 * The single place the frontend talks to the API (§13).
 * Errors are unwrapped from the backend's error envelope so components handle one shape.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api`;

  // ---- Patients ----

  createPatient(request: CreatePatientRequest): Observable<PatientDetail> {
    return this.http
      .post<PatientDetail>(`${this.base}/patients`, request)
      .pipe(catchError(toReadableError));
  }

  listPatients(): Observable<PatientSummary[]> {
    return this.http
      .get<PatientSummary[]>(`${this.base}/patients`)
      .pipe(catchError(toReadableError));
  }

  getPatient(id: string): Observable<PatientDetail> {
    return this.http
      .get<PatientDetail>(`${this.base}/patients/${id}`)
      .pipe(catchError(toReadableError));
  }

  deletePatient(id: string): Observable<void> {
    return this.http
      .delete<void>(`${this.base}/patients/${id}`)
      .pipe(catchError(toReadableError));
  }

  // ---- Documents ----

  /** Returns as soon as the files are stored; extraction continues in the background (FR-2.8). */
  uploadDocuments(patientId: string, files: File[], visitLabel?: string): Observable<UploadResult> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }
    if (visitLabel) {
      form.append('visitLabel', visitLabel);
    }

    return this.http
      .post<UploadResult>(`${this.base}/patients/${patientId}/documents`, form)
      .pipe(catchError(toReadableError));
  }

  getStatus(patientId: string): Observable<ProcessingStatus> {
    return this.http
      .get<ProcessingStatus>(`${this.base}/patients/${patientId}/status`)
      .pipe(catchError(toReadableError));
  }

  getTimeline(patientId: string): Observable<TimelineEntry[]> {
    return this.http
      .get<TimelineEntry[]>(`${this.base}/patients/${patientId}/timeline`)
      .pipe(catchError(toReadableError));
  }

  getDocument(documentId: string): Observable<DocumentDetail> {
    return this.http
      .get<DocumentDetail>(`${this.base}/documents/${documentId}`)
      .pipe(catchError(toReadableError));
  }

  /** Removes one document and everything read from it; the backend re-runs the analysis. */
  deleteDocument(documentId: string): Observable<void> {
    return this.http
      .delete<void>(`${this.base}/documents/${documentId}`)
      .pipe(catchError(toReadableError));
  }

  // ---- Analysis ----

  getAlerts(patientId: string): Observable<Alert[]> {
    return this.http
      .get<Alert[]>(`${this.base}/patients/${patientId}/alerts`)
      .pipe(catchError(toReadableError));
  }

  getMedications(patientId: string): Observable<MedicationGroup[]> {
    return this.http
      .get<MedicationGroup[]>(`${this.base}/patients/${patientId}/medications`)
      .pipe(catchError(toReadableError));
  }

  getLabTrends(patientId: string): Observable<LabTrend[]> {
    return this.http
      .get<LabTrend[]>(`${this.base}/patients/${patientId}/labs`)
      .pipe(catchError(toReadableError));
  }

  /** The stored conversation, so reopening the drawer resumes it rather than starting over. */
  getChatHistory(patientId: string): Observable<ChatMessage[]> {
    return this.http
      .get<ChatMessage[]>(`${this.base}/patients/${patientId}/chat`)
      .pipe(catchError(toReadableError));
  }

  /**
   * `history` carries the completed turns of the open drawer so a follow-up has something to
   * resolve against. Held by the client only — nothing is stored, and the server trims it again
   * before it reaches the prompt.
   */
  ask(patientId: string, question: string, history: ChatTurn[] = []): Observable<ChatAnswer> {
    return this.http
      .post<ChatAnswer>(`${this.base}/patients/${patientId}/ask`, { question, history })
      .pipe(catchError(toReadableError));
  }

  getSpecialties(): Observable<SpecialtyOption[]> {
    return this.http
      .get<SpecialtyOption[]>(`${this.base}/specialties`)
      .pipe(catchError(toReadableError));
  }

  suggestSpecialty(
    patientId: string,
    alertId?: string,
    specialtyOverride?: string | null
  ): Observable<SpecialtyResolution> {
    const params: Record<string, string> = {};
    if (alertId) params['alertId'] = alertId;
    if (specialtyOverride) params['specialtyOverride'] = specialtyOverride;

    return this.http
      .get<SpecialtyResolution>(`${this.base}/patients/${patientId}/specialty-suggestion`, { params })
      .pipe(catchError(toReadableError));
  }

  searchDoctors(patientId: string, request: CreateDoctorSearchRequest): Observable<DoctorSearchResponse> {
    return this.http
      .post<DoctorSearchResponse>(`${this.base}/patients/${patientId}/doctor-search`, request)
      .pipe(catchError(toReadableError));
  }
}

/**
 * Surfaces the backend's message where there is one. A network failure gets its own copy rather
 * than "Http failure response for ..." — error states must be readable (FR-8.8).
 */
function toReadableError(error: HttpErrorResponse) {
  if (error.status === 0) {
    return throwError(() => new Error('Could not reach the MediTrail server. Check your connection and try again.'));
  }

  const message =
    typeof error.error?.message === 'string'
      ? error.error.message
      : 'Something went wrong. Please try again.';

  return throwError(() => new Error(message));
}
