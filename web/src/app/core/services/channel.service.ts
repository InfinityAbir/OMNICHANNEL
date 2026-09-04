import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChannelAccountAdminResponse, WidgetSettingsResponse } from '../models/channel.models';

@Injectable({ providedIn: 'root' })
export class ChannelService {
  private readonly http = inject(HttpClient);

  get(channelType: string): Observable<ChannelAccountAdminResponse> {
    return this.http.get<ChannelAccountAdminResponse>(`/api/v1/channels/${channelType}`);
  }

  setExternalAccount(channelType: string, externalAccountId: string) {
    return this.http.put<ChannelAccountAdminResponse>(`/api/v1/channels/${channelType}/account`, { externalAccountId });
  }

  setCredential(channelType: string, secret: string) {
    return this.http.put<ChannelAccountAdminResponse>(`/api/v1/channels/${channelType}/credentials`, { secret });
  }

  deleteCredential(channelType: string) {
    return this.http.delete(`/api/v1/channels/${channelType}/credentials`);
  }

  getWidgetSettings(): Observable<WidgetSettingsResponse> {
    return this.http.get<WidgetSettingsResponse>('/api/v1/channels/widget');
  }

  updateWidgetOrigins(origins: string[]): Observable<WidgetSettingsResponse> {
    return this.http.put<WidgetSettingsResponse>('/api/v1/channels/widget/origins', { origins });
  }
}
