import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ConversationDetailResponse,
  ConversationPriority,
  ConversationStatus,
  KeysetPageResponse,
  ConversationSummaryResponse,
  MessageDirection,
  MessageResponse,
  MessageSenderType,
  NoteResponse,
} from '../models/conversation.models';

export interface ListConversationsFilter {
  status?: ConversationStatus;
  assignedUserId?: string;
  search?: string;
  cursor?: string;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class ConversationService {
  private readonly http = inject(HttpClient);

  list(filter: ListConversationsFilter): Observable<KeysetPageResponse<ConversationSummaryResponse>> {
    let params = new HttpParams();
    if (filter.status) params = params.set('status', filter.status);
    if (filter.assignedUserId) params = params.set('assignedUserId', filter.assignedUserId);
    if (filter.search) params = params.set('search', filter.search);
    if (filter.cursor) params = params.set('cursor', filter.cursor);
    if (filter.pageSize) params = params.set('pageSize', filter.pageSize);

    return this.http.get<KeysetPageResponse<ConversationSummaryResponse>>('/api/v1/conversations', { params });
  }

  get(id: string): Observable<ConversationDetailResponse> {
    return this.http.get<ConversationDetailResponse>(`/api/v1/conversations/${id}`);
  }

  create(request: { contactId?: string; newContactDisplayName?: string; initialMessageText?: string }) {
    return this.http.post<ConversationDetailResponse>('/api/v1/conversations', request);
  }

  listMessages(conversationId: string, cursor?: string): Observable<KeysetPageResponse<MessageResponse>> {
    let params = new HttpParams();
    if (cursor) params = params.set('cursor', cursor);
    return this.http.get<KeysetPageResponse<MessageResponse>>(`/api/v1/conversations/${conversationId}/messages`, {
      params,
    });
  }

  sendMessage(conversationId: string, text: string, direction: MessageDirection = 'Outbound', senderType: MessageSenderType = 'Agent') {
    return this.http.post<MessageResponse>(`/api/v1/conversations/${conversationId}/messages`, {
      direction,
      senderType,
      text,
    });
  }

  assign(conversationId: string, userId: string) {
    return this.http.post(`/api/v1/conversations/${conversationId}/assign`, { userId });
  }

  unassign(conversationId: string) {
    return this.http.post(`/api/v1/conversations/${conversationId}/unassign`, {});
  }

  changeStatus(conversationId: string, status: ConversationStatus) {
    return this.http.post(`/api/v1/conversations/${conversationId}/status`, { status });
  }

  setPriority(conversationId: string, priority: ConversationPriority) {
    return this.http.post(`/api/v1/conversations/${conversationId}/priority`, { priority });
  }

  listNotes(conversationId: string): Observable<NoteResponse[]> {
    return this.http.get<NoteResponse[]>(`/api/v1/conversations/${conversationId}/notes`);
  }

  addNote(conversationId: string, text: string) {
    return this.http.post<NoteResponse>(`/api/v1/conversations/${conversationId}/notes`, { text });
  }

  addTag(conversationId: string, name: string) {
    return this.http.post(`/api/v1/conversations/${conversationId}/tags`, { name });
  }

  removeTag(conversationId: string, tagId: string) {
    return this.http.delete(`/api/v1/conversations/${conversationId}/tags/${tagId}`);
  }
}
