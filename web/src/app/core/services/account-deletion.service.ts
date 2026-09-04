import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { DeleteMyAccountResponse, TenantDeletionStatusResponse } from '../models/account-deletion.models';

@Injectable({ providedIn: 'root' })
export class AccountDeletionService {
  private readonly http = inject(HttpClient);

  getTenantDeletionStatus(): Observable<TenantDeletionStatusResponse> {
    return this.http.get<TenantDeletionStatusResponse>('/api/v1/tenant/deletion');
  }

  requestTenantDeletion(): Observable<TenantDeletionStatusResponse> {
    return this.http.post<TenantDeletionStatusResponse>('/api/v1/tenant/deletion', {});
  }

  cancelTenantDeletion(): Observable<TenantDeletionStatusResponse> {
    return this.http.delete<TenantDeletionStatusResponse>('/api/v1/tenant/deletion');
  }

  deleteMyAccount(): Observable<DeleteMyAccountResponse> {
    return this.http.delete<DeleteMyAccountResponse>('/api/v1/users/me');
  }
}
