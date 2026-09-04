import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AiProviderSettingsResponse,
  AiProviderTestResponse,
  DetectAiProviderRequest,
  DetectAiProviderResponse,
  UpdateAiProviderSettingsRequest,
} from '../models/ai-provider.models';

@Injectable({ providedIn: 'root' })
export class AiProviderService {
  private readonly http = inject(HttpClient);

  get(): Observable<AiProviderSettingsResponse> {
    return this.http.get<AiProviderSettingsResponse>('/api/v1/ai/provider-settings');
  }

  update(request: UpdateAiProviderSettingsRequest): Observable<AiProviderSettingsResponse> {
    return this.http.put<AiProviderSettingsResponse>('/api/v1/ai/provider-settings', request);
  }

  clearApiKey(): Observable<void> {
    return this.http.delete<void>('/api/v1/ai/provider-settings/key');
  }

  test(): Observable<AiProviderTestResponse> {
    return this.http.post<AiProviderTestResponse>('/api/v1/ai/provider-settings/test', {});
  }

  detect(request: DetectAiProviderRequest): Observable<DetectAiProviderResponse> {
    return this.http.post<DetectAiProviderResponse>('/api/v1/ai/provider-settings/detect', request);
  }
}
