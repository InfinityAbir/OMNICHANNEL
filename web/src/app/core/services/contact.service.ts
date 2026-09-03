import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ContactResponse, PagedResponse } from '../models/conversation.models';

@Injectable({ providedIn: 'root' })
export class ContactService {
  private readonly http = inject(HttpClient);

  list(search?: string, page = 1, pageSize = 20): Observable<PagedResponse<ContactResponse>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);
    return this.http.get<PagedResponse<ContactResponse>>('/api/v1/contacts', { params });
  }
}
