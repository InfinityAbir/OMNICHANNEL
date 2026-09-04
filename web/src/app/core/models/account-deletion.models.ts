export interface TenantDeletionStatusResponse {
  status: 'Active' | 'Suspended' | 'PendingDeletion' | 'Deleted';
  scheduledDeletionAt: string | null;
}

export interface DeleteMyAccountResponse {
  succeeded: boolean;
  error: string | null;
}
