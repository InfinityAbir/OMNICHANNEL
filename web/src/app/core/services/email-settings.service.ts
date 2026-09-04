import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { EmailSettingsResponse, EmailTestResponse, UpdateEmailSettingsRequest } from '../models/email-settings.models';

@Injectable({ providedIn: 'root' })
export class EmailSettingsService {
  private readonly http = inject(HttpClient);

  get(): Observable<EmailSettingsResponse> {
    return this.http.get<EmailSettingsResponse>('/api/v1/tenant/email-settings');
  }

  update(request: UpdateEmailSettingsRequest): Observable<EmailSettingsResponse> {
    return this.http.put<EmailSettingsResponse>('/api/v1/tenant/email-settings', request);
  }

  clear(): Observable<void> {
    return this.http.delete<void>('/api/v1/tenant/email-settings');
  }

  test(): Observable<EmailTestResponse> {
    return this.http.post<EmailTestResponse>('/api/v1/tenant/email-settings/test', {});
  }
}
