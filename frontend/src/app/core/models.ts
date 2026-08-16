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
  infoAlertCount: number;
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

export type AlertType =
  | 'DuplicatePrescription'
  | 'DosageConflict'
  | 'DrugInteraction'
  | 'AllergyConflict'
  | 'DocumentWarningConflict'
  | 'LabOutOfRange'
  | 'LabDrift'
  | 'LowExtractionConfidence'
  /** A medication whose active ingredient could not be resolved, so no cross-check covered it. */
  | 'UnresolvedMedication';

export interface EvidenceRef {
  documentId: string;
  fileName: string;
  sourceUrl: string;
  documentDate?: string;
}

export interface Alert {
  id: string;
  type: AlertType;
  severity: AlertSeverity;
  title: string;
  involvedGenerics: string[];
  explanationEn?: string;
  explanationTa?: string;
  suggestedActionEn?: string;
  suggestedActionTa?: string;
  confidence: number;
  requiresProfessionalConsult: boolean;
  verificationStatus: VerificationStatus;
  verificationExcerpt?: string;
  verificationSource?: string;
  evidence: EvidenceRef[];
  /** "rules" or "llm" — a computed finding is not labelled AI-generated (§17.3). */
  detectedBy?: string;
}

export interface MedicationGroup {
  genericName?: string;
  displayName: string;
  therapeuticClass?: string;
  rows: MedicationRow[];
  alertIds: string[];
  hasConflict: boolean;
  firstPrescribed?: string;
  lastPrescribed?: string;
}

export interface MedicationRow {
  id: string;
  documentId: string;
  sourceUrl: string;
  brandName?: string;
  strengthValue?: number;
  strengthUnit?: string;
  frequency?: string;
  frequencyPerDay?: number;
  durationDays?: number;
  instructions?: string;
  providerName?: string;
  startDate?: string;
  endDate?: string;
  sourceText?: string;
  confidence?: number;
}

export type TrendDirection = 'Insufficient' | 'Rising' | 'Falling' | 'Stable';

export interface LabTrend {
  testKey: string;
  displayName: string;
  unit?: string;
  normalMin?: number;
  normalMax?: number;
  normalRangeText?: string;
  direction: TrendDirection;
  percentChange?: number;
  outOfRangeCount: number;
  latestOutOfRange: boolean;
  points: LabTrendPoint[];
  explanationEn?: string;
  explanationTa?: string;
  confidence: number;
}

export interface LabTrendPoint {
  date: string;
  value: number;
  isOutOfRange: boolean;
  documentId: string;
}

export interface ChatAnswer {
  answerEn: string;
  answerTa?: string;
  citations: string[];
  confidence: number;
  consultProfessional: boolean;
  foundInDocuments: boolean;
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
