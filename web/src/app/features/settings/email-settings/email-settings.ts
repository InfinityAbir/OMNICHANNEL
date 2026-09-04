import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EmailSettingsService } from '../../../core/services/email-settings.service';
import { SMTP_PROVIDER_PRESETS } from '../../../core/models/email-settings.models';
import { ToastService } from '../../../core/services/toast.service';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';

@Component({
  selector: 'app-email-settings',
  standalone: true,
  imports: [FormsModule, SkeletonComponent],
  templateUrl: './email-settings.html',
  styleUrls: ['../../../shared/settings-common.scss'],
})
export class EmailSettingsComponent implements OnInit {
  private readonly api = inject(EmailSettingsService);
  private readonly toast = inject(ToastService);

  readonly presets = SMTP_PROVIDER_PRESETS;

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly testing = signal(false);
  readonly passwordVisible = signal(false);

  readonly host = signal('');
  readonly port = signal(587);
  readonly username = signal('');
  readonly fromAddress = signal('');
  readonly fromName = signal('');
  readonly passwordDraft = signal('');
  readonly isConfigured = signal(false);
  readonly hasPassword = signal(false);

  readonly lastTestResult = signal<{ success: boolean; message: string } | null>(null);

  applyPreset(index: string): void {
    const preset = this.presets[Number(index)];
    if (!preset || !preset.host) {
      return;
    }
    this.host.set(preset.host);
    this.port.set(preset.port);
  }

  ngOnInit(): void {
    this.api.get().subscribe({
      next: (settings) => {
        this.host.set(settings.host ?? '');
        this.port.set(settings.port);
        this.username.set(settings.username ?? '');
        this.fromAddress.set(settings.fromAddress ?? '');
        this.fromName.set(settings.fromName ?? '');
        this.isConfigured.set(settings.isConfigured);
        this.hasPassword.set(settings.hasPassword);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load email settings.');
      },
    });
  }

  save(): void {
    this.saving.set(true);
    this.api
      .update({
        host: this.host().trim(),
        port: this.port(),
        username: this.username().trim(),
        fromAddress: this.fromAddress().trim(),
        fromName: this.fromName().trim() || null,
        password: this.passwordDraft().trim() || null,
      })
      .subscribe({
        next: (settings) => {
          this.saving.set(false);
          this.isConfigured.set(settings.isConfigured);
          this.hasPassword.set(settings.hasPassword);
          this.passwordDraft.set('');
          this.toast.show('Email settings saved.', 'success');
        },
        error: (err) => {
          this.saving.set(false);
          this.toast.showError(err, 'Could not save email settings.');
        },
      });
  }

  clear(): void {
    this.api.clear().subscribe({
      next: () => {
        this.host.set('');
        this.username.set('');
        this.fromAddress.set('');
        this.fromName.set('');
        this.isConfigured.set(false);
        this.hasPassword.set(false);
        this.toast.show('Your SMTP settings were cleared — the platform default will be used instead.', 'success');
      },
      error: (err) => this.toast.showError(err, 'Could not clear email settings.'),
    });
  }

  testConnection(): void {
    this.testing.set(true);
    this.lastTestResult.set(null);
    this.api.test().subscribe({
      next: (result) => {
        this.testing.set(false);
        this.lastTestResult.set(result);
      },
      error: (err) => {
        this.testing.set(false);
        this.lastTestResult.set({ success: false, message: 'Test request failed.' });
        this.toast.showError(err, 'Could not send the test email.');
      },
    });
  }
}
