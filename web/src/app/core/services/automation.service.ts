import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AutomationRuleResponse,
  CreateAutomationRuleRequest,
  SavedReplyRequest,
  SavedReplyResponse,
  TenantBusinessHoursResponse,
  UpdateTenantBusinessHoursRequest,
} from '../models/automation.models';

@Injectable({ providedIn: 'root' })
export class AutomationService {
  private readonly http = inject(HttpClient);

  listRules(): Observable<AutomationRuleResponse[]> {
    return this.http.get<AutomationRuleResponse[]>('/api/v1/automation-rules');
  }

  createRule(request: CreateAutomationRuleRequest): Observable<AutomationRuleResponse> {
    return this.http.post<AutomationRuleResponse>('/api/v1/automation-rules', request);
  }

  setRuleEnabled(id: string, enabled: boolean) {
    return this.http.put(`/api/v1/automation-rules/${id}/enabled`, { enabled });
  }

  deleteRule(id: string) {
    return this.http.delete(`/api/v1/automation-rules/${id}`);
  }

  listSavedReplies(): Observable<SavedReplyResponse[]> {
    return this.http.get<SavedReplyResponse[]>('/api/v1/saved-replies');
  }

  createSavedReply(request: SavedReplyRequest): Observable<SavedReplyResponse> {
    return this.http.post<SavedReplyResponse>('/api/v1/saved-replies', request);
  }

  updateSavedReply(id: string, request: SavedReplyRequest): Observable<SavedReplyResponse> {
    return this.http.put<SavedReplyResponse>(`/api/v1/saved-replies/${id}`, request);
  }

  deleteSavedReply(id: string) {
    return this.http.delete(`/api/v1/saved-replies/${id}`);
  }

  getBusinessHours(): Observable<TenantBusinessHoursResponse> {
    return this.http.get<TenantBusinessHoursResponse>('/api/v1/tenant/business-hours');
  }

  updateBusinessHours(request: UpdateTenantBusinessHoursRequest): Observable<TenantBusinessHoursResponse> {
    return this.http.put<TenantBusinessHoursResponse>('/api/v1/tenant/business-hours', request);
  }
}
