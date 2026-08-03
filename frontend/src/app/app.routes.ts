import { Routes } from '@angular/router';

/**
 * Primary flow (§9.1): patients → upload → processing → dashboard → evidence.
 * Lazy-loaded so the first paint carries only the patients list.
 * Route params bind straight to component inputs (withComponentInputBinding in app.config).
 */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () =>
      import('./features/patients/patients-page.component').then(m => m.PatientsPageComponent),
    title: 'Patients · MediTrail'
  },
  {
    path: 'patients/:patientId/upload',
    loadComponent: () =>
      import('./features/upload/upload-page.component').then(m => m.UploadPageComponent),
    title: 'Upload · MediTrail'
  },
  {
    path: 'patients/:patientId/processing',
    loadComponent: () =>
      import('./features/processing/processing-page.component').then(m => m.ProcessingPageComponent),
    title: 'Reading records · MediTrail'
  },
  {
    path: 'patients/:patientId',
    loadComponent: () =>
      import('./features/dashboard/dashboard-page.component').then(m => m.DashboardPageComponent),
    title: 'Dashboard · MediTrail'
  },
  {
    path: 'documents/:documentId',
    loadComponent: () =>
      import('./features/evidence/evidence-page.component').then(m => m.EvidencePageComponent),
    title: 'Evidence · MediTrail'
  },
  { path: '**', redirectTo: '' }
];
