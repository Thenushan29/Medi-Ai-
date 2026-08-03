/**
 * Wire contracts, mirroring the backend DTOs in Contracts/Api.
 * Enums cross as names (the API uses JsonStringEnumConverter), so these are string unions —
 * a numeric drift between frontend and backend is impossible by construction.
 */

export type PatientStatus =
  | 'Idle'
  | 'Extracting'
  | 'Merging'
  | 'CrossChecking'
  | 'Verifying'
  | 'AnalyzingTrends'
  | 'Ready'
  | 'Failed';

export type DocumentStatus =
  | 'Uploaded'
  | 'Queued'
  | 'Extracting'
  | 'Extracted'
  | 'Cached'
  | 'Failed';

export type AlertSeverity = 'Info' | 'Amber' | 'Red';

export type VerificationStatus =
  | 'Pending'
  | 'Confirmed'
  | 'NotFound'
  | 'Unverified'
  | 'NotApplicable';

export interface CreatePatientRequest {
  displayName: string;
}

export interface PatientSummary {
  id: string;
  displayName: string;
  status: PatientStatus;
  documentCount: number;
  redAlertCount: number;
  amberAlertCount: number;
  updatedAt: string;
  analyzedAt?: string;
}

export interface PatientDetail {
  id: string;
  displayName: string;
  status: PatientStatus;
  statusMessage?: string;
  documentCount: number;
  failedDocumentCount: number;
  redAlertCount: number;
  amberAlertCount: number;
  infoAlertCount: number;
  earliestDocumentDate?: string;
  latestDocumentDate?: string;
  createdAt: string;
  updatedAt: string;
  analyzedAt?: string;
}

export interface UploadResult {
  accepted: UploadedFile[];
  rejected: RejectedFile[];
}

export interface UploadedFile {
  documentId: string;
  fileName: string;
  status: DocumentStatus;
  reusedCachedExtraction: boolean;
}

export interface RejectedFile {
  fileName: string;
  reason: string;
}

export interface ProcessingStatus {
  patientId: string;
  status: PatientStatus;
  statusMessage?: string;
  total: number;
  completed: number;
  failed: number;
  isComplete: boolean;
  documents: DocumentStatusEntry[];
}

export interface DocumentStatusEntry {
  documentId: string;
  fileName: string;
  status: DocumentStatus;
  failureReason?: string;
  overallConfidence?: number;
}

export interface TimelineEntry {
  documentId: string;
  documentDate?: string;
  visitLabel?: string;
  documentType?: string;
  providerName?: string;
  providerFacility?: string;
  fileName: string;
  sourceUrl: string;
  status: DocumentStatus;
  failureReason?: string;
  overallConfidence?: number;
  legibilityNotes?: string;
  medicationCount: number;
  labResultCount: number;
  outOfRangeCount: number;
  warningCount: number;
}

export interface DocumentDetail {
  documentId: string;
  patientId: string;
  fileName: string;
  contentType: string;
  sourceUrl: string;
  status: DocumentStatus;
  failureReason?: string;
  documentDate?: string;
  documentType?: string;
  providerName?: string;
  overallConfidence?: number;
  legibilityNotes?: string;
  extractionModel?: string;
  medications: Medication[];
  labResults: LabResult[];
  allergies: Allergy[];
}

export interface Medication {
  id: string;
  documentId: string;
  brandName?: string;
  genericName?: string;
  strengthValue?: number;
  strengthUnit?: string;
  frequency?: string;
  frequencyPerDay?: number;
  durationDays?: number;
  instructions?: string;
  startDate?: string;
  endDate?: string;
  sourceText?: string;
  confidence?: number;
}

export interface LabResult {
  id: string;
  documentId: string;
  testName?: string;
  testNameStandard?: string;
  valueNumeric?: number;
  valueText?: string;
  unit?: string;
  normalMin?: number;
  normalMax?: number;
  normalRangeText?: string;
  testDate?: string;
  isOutOfRange: boolean;
  sourceText?: string;
  confidence?: number;
}

export interface Allergy {
  id: string;
  documentId: string;
  isDocumentWarning: boolean;
  substance?: string;
  substanceGeneric?: string;
  relatesTo: string[];
  reaction?: string;
  severity?: string;
  sourceText?: string;
  confidence?: number;
}

export interface ApiError {
  code: string;
  message: string;
  traceId?: string;
}

/**
 * Presentation mapping for the composed confidence score (§11.4).
 * Kept in one place so every screen signals uncertainty identically.
 */
export type ConfidenceBand = 'high' | 'medium' | 'low' | 'unknown';

export function confidenceBand(score: number | null | undefined): ConfidenceBand {
  if (score === null || score === undefined) return 'unknown';
  if (score >= 80) return 'high';
  if (score >= 50) return 'medium';
  return 'low';
}
