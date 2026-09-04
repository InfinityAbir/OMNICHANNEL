import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AccountDeletionService } from '../../../core/services/account-deletion.service';
import { AuthService } from '../../../core/services/auth.service';
import { ToastService } from '../../../core/services/toast.service';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';

@Component({
  selector: 'app-account-settings',
  standalone: true,
  imports: [DatePipe, SkeletonComponent],
  templateUrl: './account-settings.html',
  styleUrls: ['../../../shared/settings-common.scss'],
})
export class AccountSettingsComponent implements OnInit {
  private readonly api = inject(AccountDeletionService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly canManageTenantDeletion = computed(() => (this.auth.currentUser()?.permissions ?? []).includes('tenant.delete'));

  readonly loading = signal(true);
  readonly tenantStatus = signal<string>('Active');
  readonly scheduledDeletionAt = signal<string | null>(null);

  readonly confirmingAccountDelete = signal(false);
  readonly confirmingTenantDelete = signal(false);
  readonly deletingAccount = signal(false);
  readonly workingOnTenant = signal(false);

  ngOnInit(): void {
    if (!this.canManageTenantDeletion()) {
      this.loading.set(false);
      return;
    }

    this.api.getTenantDeletionStatus().subscribe({
      next: (status) => {
        this.tenantStatus.set(status.status);
        this.scheduledDeletionAt.set(status.scheduledDeletionAt);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load business account status.');
      },
    });
  }

  requestTenantDeletion(): void {
    this.workingOnTenant.set(true);
    this.api.requestTenantDeletion().subscribe({
      next: (status) => {
        this.workingOnTenant.set(false);
        this.confirmingTenantDelete.set(false);
        this.tenantStatus.set(status.status);
        this.scheduledDeletionAt.set(status.scheduledDeletionAt);
        this.toast.show('Business account scheduled for deletion. Check your email for details.', 'success');
      },
      error: (err) => {
        this.workingOnTenant.set(false);
        this.toast.showError(err, 'Could not schedule deletion.');
      },
    });
  }

  cancelTenantDeletion(): void {
    this.workingOnTenant.set(true);
    this.api.cancelTenantDeletion().subscribe({
      next: (status) => {
        this.workingOnTenant.set(false);
        this.tenantStatus.set(status.status);
        this.scheduledDeletionAt.set(status.scheduledDeletionAt);
        this.toast.show('Deletion cancelled — your business account stays active.', 'success');
      },
      error: (err) => {
        this.workingOnTenant.set(false);
        this.toast.showError(err, 'Could not cancel deletion.');
      },
    });
  }

  deleteMyAccount(): void {
    this.deletingAccount.set(true);
    this.api.deleteMyAccount().subscribe({
      next: async () => {
        this.toast.show('Your account has been deleted.', 'success');
        await this.auth.logout();
        this.router.navigateByUrl('/login');
      },
      error: (err) => {
        this.deletingAccount.set(false);
        this.confirmingAccountDelete.set(false);
        this.toast.showError(err, 'Could not delete your account.');
      },
    });
  }
}
