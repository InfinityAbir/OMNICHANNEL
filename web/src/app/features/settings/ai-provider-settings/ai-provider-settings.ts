import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AiProviderService } from '../../../core/services/ai-provider.service';
import { ToastService } from '../../../core/services/toast.service';
import { AI_PROVIDER_PRESETS, AiProviderKind } from '../../../core/models/ai-provider.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';

@Component({
  selector: 'app-ai-provider-settings',
  standalone: true,
  imports: [FormsModule, SkeletonComponent],
  templateUrl: './ai-provider-settings.html',
  styleUrls: ['../../../shared/settings-common.scss'],
})
export class AiProviderSettingsComponent implements OnInit {
  private readonly api = inject(AiProviderService);
  private readonly toast = inject(ToastService);

  readonly presets = AI_PROVIDER_PRESETS;
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly detecting = signal(false);
  readonly testing = signal(false);
  readonly apiKeyVisible = signal(false);

  readonly providerKind = signal<AiProviderKind>('OpenAiCompatible');
  readonly baseUrl = signal('');
  readonly model = signal('');
  readonly apiKeyDraft = signal('');
  readonly hasApiKey = signal(false);

  readonly detectedModels = signal<string[]>([]);
  readonly lastTestResult = signal<{ success: boolean; message: string } | null>(null);

  ngOnInit(): void {
    this.api.get().subscribe({
      next: (settings) => {
        this.providerKind.set(settings.providerKind);
        this.baseUrl.set(settings.baseUrl ?? '');
        this.model.set(settings.model);
        this.hasApiKey.set(settings.hasApiKey);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load AI provider settings.');
      },
    });
  }

  applyPreset(index: number): void {
    const preset = this.presets[Number(index)];
    if (!preset) return;
    this.providerKind.set(preset.providerKind);
    this.baseUrl.set(preset.baseUrl ?? '');
    this.detectedModels.set([]);
  }

  detectFromKey(): void {
    const apiKey = this.apiKeyDraft().trim();
    if (!apiKey) return;

    this.detecting.set(true);
    this.api
      .detect({
        apiKey,
        providerKind: this.providerKind(),
        baseUrl: this.providerKind() === 'OpenAiCompatible' ? this.baseUrl() || undefined : undefined,
      })
      .subscribe({
        next: (result) => {
          this.detecting.set(false);
          if (!result.success) {
            this.toast.show(result.message, 'error');
            return;
          }

          this.providerKind.set(result.providerKind);
          if (result.baseUrl) this.baseUrl.set(result.baseUrl);
          if (result.suggestedModel) this.model.set(result.suggestedModel);
          this.detectedModels.set(result.availableModels);
          this.toast.show(result.message, 'success');
        },
        error: (err) => {
          this.detecting.set(false);
          this.toast.showError(err, 'Could not detect the provider from this key.');
        },
      });
  }

  save(): void {
    this.saving.set(true);
    this.api
      .update({
        providerKind: this.providerKind(),
        baseUrl: this.providerKind() === 'OpenAiCompatible' ? this.baseUrl().trim() : null,
        model: this.model().trim(),
        apiKey: this.apiKeyDraft().trim() || null,
      })
      .subscribe({
        next: (settings) => {
          this.saving.set(false);
          this.hasApiKey.set(settings.hasApiKey);
          this.apiKeyDraft.set('');
          this.toast.show('AI provider settings saved.', 'success');
        },
        error: (err) => {
          this.saving.set(false);
          this.toast.showError(err, 'Could not save AI provider settings.');
        },
      });
  }

  clearApiKey(): void {
    this.api.clearApiKey().subscribe({
      next: () => {
        this.hasApiKey.set(false);
        this.toast.show('API key removed — the platform default will be used instead.', 'success');
      },
      error: (err) => this.toast.showError(err, 'Could not remove the API key.'),
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
        this.toast.showError(err, 'Could not test the AI provider connection.');
      },
    });
  }
}
