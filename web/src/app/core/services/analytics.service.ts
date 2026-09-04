import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AnalyticsSummaryResponse } from '../models/analytics.models';

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly http = inject(HttpClient);

  getSummary(from?: string, to?: string): Observable<AnalyticsSummaryResponse> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<AnalyticsSummaryResponse>('/api/v1/analytics/summary', { params });
  }
}
