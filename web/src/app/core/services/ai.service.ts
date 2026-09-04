import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AiAutoReplySettingsResponse,
  AiSuggestionResponse,
  UpdateAiAutoReplySettingsRequest,
} from '../models/ai.models';

@Injectable({ providedIn: 'root' })
export class AiService {
  private readonly http = inject(HttpClient);

  generateSuggestion(conversationId: string): Observable<AiSuggestionResponse> {
    return this.http.post<AiSuggestionResponse>(`/api/v1/conversations/${conversationId}/ai-suggestions`, {});
  }

  setConversationAiMode(conversationId: string, aiMode: string) {
    return this.http.put(`/api/v1/conversations/${conversationId}/ai-mode`, { aiMode });
  }

  getAutoReplySettings(): Observable<AiAutoReplySettingsResponse> {
    return this.http.get<AiAutoReplySettingsResponse>('/api/v1/ai/auto-reply-settings');
  }

  updateAutoReplySettings(request: UpdateAiAutoReplySettingsRequest): Observable<AiAutoReplySettingsResponse> {
    return this.http.put<AiAutoReplySettingsResponse>('/api/v1/ai/auto-reply-settings', request);
  }
}
