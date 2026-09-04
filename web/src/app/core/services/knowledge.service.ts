import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { KnowledgeDocumentResponse, KnowledgeSearchResultResponse } from '../models/knowledge.models';

@Injectable({ providedIn: 'root' })
export class KnowledgeService {
  private readonly http = inject(HttpClient);

  list(): Observable<KnowledgeDocumentResponse[]> {
    return this.http.get<KnowledgeDocumentResponse[]>('/api/v1/knowledge/documents');
  }

  create(title: string, content: string) {
    return this.http.post<{ id: string }>('/api/v1/knowledge/documents', { title, content });
  }

  revise(id: string, title: string, content: string) {
    return this.http.put(`/api/v1/knowledge/documents/${id}`, { title, content });
  }

  archive(id: string) {
    return this.http.delete(`/api/v1/knowledge/documents/${id}`);
  }

  search(query: string): Observable<KnowledgeSearchResultResponse[]> {
    const params = new HttpParams().set('q', query);
    return this.http.get<KnowledgeSearchResultResponse[]>('/api/v1/knowledge/search', { params });
  }
}
