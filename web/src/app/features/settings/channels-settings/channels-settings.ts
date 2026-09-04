import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChannelService } from '../../../core/services/channel.service';
import { ToastService } from '../../../core/services/toast.service';
import { CHANNEL_TYPES, ChannelAccountAdminResponse, ChannelTypeName, WidgetSettingsResponse } from '../../../core/models/channel.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';

@Component({
  selector: 'app-channels-settings',
  standalone: true,
  imports: [FormsModule, SkeletonComponent],
  templateUrl: './channels-settings.html',
  styleUrls: ['../../../shared/settings-common.scss', './channels-settings.scss'],
})
export class ChannelsSettingsComponent implements OnInit {
  private readonly channels = inject(ChannelService);
  private readonly toast = inject(ToastService);

  readonly channelTypes = CHANNEL_TYPES;
  readonly loading = signal(true);
  readonly accounts = signal<Partial<Record<ChannelTypeName, ChannelAccountAdminResponse>>>({});
  readonly externalAccountIdDrafts = signal<Partial<Record<ChannelTypeName, string>>>({});
  readonly credentialDrafts = signal<Partial<Record<ChannelTypeName, string>>>({});
  readonly credentialVisible = signal<Partial<Record<ChannelTypeName, boolean>>>({});
  readonly saving = signal<ChannelTypeName | null>(null);

  readonly widgetSettings = signal<WidgetSettingsResponse | null>(null);
  readonly widgetOriginsDraft = signal('');
  readonly savingWidget = signal(false);

  ngOnInit(): void {
    this.loading.set(true);
    let remaining = this.channelTypes.length + 1;
    const done = () => {
      remaining -= 1;
      if (remaining === 0) this.loading.set(false);
    };

    for (const type of this.channelTypes) {
      this.channels.get(type).subscribe({
        next: (account) => {
          this.accounts.update((current) => ({ ...current, [type]: account }));
          this.externalAccountIdDrafts.update((current) => ({ ...current, [type]: account.externalAccountId ?? '' }));
          done();
        },
        error: () => done(), // not connected yet — 404 is expected, not an error worth toasting on initial load
      });
    }

    this.channels.getWidgetSettings().subscribe({
      next: (settings) => {
        this.widgetSettings.set(settings);
        this.widgetOriginsDraft.set(settings.allowedOrigins.join('\n'));
        done();
      },
      error: (err) => {
        this.toast.showError(err, 'Could not load website chat settings.');
        done();
      },
    });
  }

  saveExternalAccount(type: ChannelTypeName): void {
    const value = (this.externalAccountIdDrafts()[type] ?? '').trim();
    if (!value) return;

    this.saving.set(type);
    this.channels.setExternalAccount(type, value).subscribe({
      next: (account) => {
        this.accounts.update((current) => ({ ...current, [type]: account }));
        this.saving.set(null);
        this.toast.show(`${type} account connected.`, 'success');
      },
      error: (err) => {
        this.saving.set(null);
        this.toast.showError(err, `Could not connect the ${type} account.`);
      },
    });
  }

  saveCredential(type: ChannelTypeName): void {
    const value = (this.credentialDrafts()[type] ?? '').trim();
    if (!value) return;

    this.saving.set(type);
    this.channels.setCredential(type, value).subscribe({
      next: (account) => {
        this.accounts.update((current) => ({ ...current, [type]: account }));
        this.credentialDrafts.update((current) => ({ ...current, [type]: '' }));
        this.saving.set(null);
        this.toast.show(`${type} credential saved.`, 'success');
      },
      error: (err) => {
        this.saving.set(null);
        this.toast.showError(err, `Could not save the ${type} credential.`);
      },
    });
  }

  disconnectCredential(type: ChannelTypeName): void {
    this.saving.set(type);
    this.channels.deleteCredential(type).subscribe({
      next: () => {
        this.accounts.update((current) => {
          const existing = current[type];
          return existing ? { ...current, [type]: { ...existing, credentialConfigured: false } } : current;
        });
        this.saving.set(null);
        this.toast.show(`${type} credential removed.`, 'success');
      },
      error: (err) => {
        this.saving.set(null);
        this.toast.showError(err, `Could not remove the ${type} credential.`);
      },
    });
  }

  updateExternalAccountDraft(type: ChannelTypeName, value: string): void {
    this.externalAccountIdDrafts.update((current) => ({ ...current, [type]: value }));
  }

  updateCredentialDraft(type: ChannelTypeName, value: string): void {
    this.credentialDrafts.update((current) => ({ ...current, [type]: value }));
  }

  toggleCredentialVisible(type: ChannelTypeName): void {
    this.credentialVisible.update((current) => ({ ...current, [type]: !current[type] }));
  }

  async copyEmbedSnippet(snippet: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(snippet);
      this.toast.show('Embed snippet copied to clipboard.', 'success');
    } catch {
      this.toast.show('Could not copy automatically — select the text and copy it manually.', 'error');
    }
  }

  saveWidgetOrigins(): void {
    const origins = this.widgetOriginsDraft()
      .split('\n')
      .map((o) => o.trim())
      .filter((o) => o.length > 0);

    this.savingWidget.set(true);
    this.channels.updateWidgetOrigins(origins).subscribe({
      next: (settings) => {
        this.widgetSettings.set(settings);
        this.savingWidget.set(false);
        this.toast.show('Allowed origins updated.', 'success');
      },
      error: (err) => {
        this.savingWidget.set(false);
        this.toast.showError(err, 'Could not update the allowed origins.');
      },
    });
  }
}
