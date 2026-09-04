import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'inbox' },
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login/login').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    loadComponent: () => import('./features/auth/register/register').then((m) => m.RegisterComponent),
  },
  {
    path: 'inbox',
    canActivate: [authGuard],
    loadComponent: () => import('./features/inbox/inbox-page/inbox-page').then((m) => m.InboxPageComponent),
  },
  {
    path: 'inbox/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/inbox/inbox-page/inbox-page').then((m) => m.InboxPageComponent),
  },
  {
    path: 'settings',
    canActivate: [authGuard],
    loadComponent: () => import('./features/settings/settings-page/settings-page').then((m) => m.SettingsPageComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'channels' },
      {
        path: 'channels',
        loadComponent: () => import('./features/settings/channels-settings/channels-settings').then((m) => m.ChannelsSettingsComponent),
      },
      {
        path: 'knowledge',
        loadComponent: () => import('./features/settings/knowledge-settings/knowledge-settings').then((m) => m.KnowledgeSettingsComponent),
      },
      {
        path: 'ai',
        loadComponent: () => import('./features/settings/ai-settings/ai-settings').then((m) => m.AiSettingsComponent),
      },
      {
        path: 'ai-provider',
        loadComponent: () =>
          import('./features/settings/ai-provider-settings/ai-provider-settings').then((m) => m.AiProviderSettingsComponent),
      },
      {
        path: 'email',
        loadComponent: () => import('./features/settings/email-settings/email-settings').then((m) => m.EmailSettingsComponent),
      },
      {
        path: 'automation',
        loadComponent: () =>
          import('./features/settings/automation-settings/automation-settings').then((m) => m.AutomationSettingsComponent),
      },
      {
        path: 'business-hours',
        loadComponent: () =>
          import('./features/settings/business-hours-settings/business-hours-settings').then(
            (m) => m.BusinessHoursSettingsComponent,
          ),
      },
      {
        path: 'saved-replies',
        loadComponent: () =>
          import('./features/settings/saved-replies-settings/saved-replies-settings').then(
            (m) => m.SavedRepliesSettingsComponent,
          ),
      },
      {
        path: 'analytics',
        loadComponent: () =>
          import('./features/settings/analytics-dashboard/analytics-dashboard').then((m) => m.AnalyticsDashboardComponent),
      },
    ],
  },
  { path: '**', redirectTo: 'inbox' },
];
