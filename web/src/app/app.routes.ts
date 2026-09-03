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
  { path: '**', redirectTo: 'inbox' },
];
