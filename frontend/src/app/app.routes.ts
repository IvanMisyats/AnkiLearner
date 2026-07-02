import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    loadComponent: () => import('./features/auth/register.component').then((m) => m.RegisterComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'words',
        loadComponent: () =>
          import('./features/words/words-list.component').then((m) => m.WordsListComponent),
      },
      {
        path: 'words/new',
        loadComponent: () =>
          import('./features/words/word-form.component').then((m) => m.WordFormComponent),
      },
      {
        path: 'words/:id/edit',
        loadComponent: () =>
          import('./features/words/word-form.component').then((m) => m.WordFormComponent),
      },
      {
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/settings.component').then((m) => m.SettingsComponent),
      },
      // /import is added in Phase 8.
    ],
  },
  { path: '**', redirectTo: '' },
];
