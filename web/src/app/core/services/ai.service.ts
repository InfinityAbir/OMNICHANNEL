import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AiSuggestionResponse } from '../models/ai.models';

@Injectable({ providedIn: 'root' })
export class AiService {
  private readonly http = inject(HttpClient);

  generateSuggestion(conversationId: string): Observable<AiSuggestionResponse> {
    return this.http.post<AiSuggestionResponse>(`/api/v1/conversations/${conversationId}/ai-suggestions`, {});
  }
}
