import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { NotificationResponse } from '../models/notification.models';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);

  list(unreadOnly = false): Observable<NotificationResponse[]> {
    const params = new HttpParams().set('unreadOnly', unreadOnly);
    return this.http.get<NotificationResponse[]>('/api/v1/notifications', { params });
  }

  unreadCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>('/api/v1/notifications/unread-count');
  }

  markRead(id: string) {
    return this.http.post(`/api/v1/notifications/${id}/read`, {});
  }
}
