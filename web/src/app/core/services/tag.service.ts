import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { TagResponse } from '../models/conversation.models';

@Injectable({ providedIn: 'root' })
export class TagService {
  private readonly http = inject(HttpClient);

  list(): Observable<TagResponse[]> {
    return this.http.get<TagResponse[]>('/api/v1/tags');
  }
}
