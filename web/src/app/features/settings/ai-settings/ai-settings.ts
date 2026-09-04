import { Component, OnInit, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AiService } from '../../../core/services/ai.service';
import { ToastService } from '../../../core/services/toast.service';
import { BusinessHoursSchedule } from '../../../core/models/ai.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';
import { BusinessHoursEditorComponent } from '../../../shared/business-hours-editor/business-hours-editor';

@Component({
  selector: 'app-ai-settings',
  standalone: true,
  imports: [FormsModule, DecimalPipe, SkeletonComponent, BusinessHoursEditorComponent],
  templateUrl: './ai-settings.html',
  styleUrls: ['../../../shared/settings-common.scss'],
})
export class AiSettingsComponent implements OnInit {
  private readonly ai = inject(AiService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);

  readonly enabled = signal(false);
  readonly confidenceThreshold = signal(0.85);
  readonly dailyLimit = signal(50);
  readonly schedule = signal<BusinessHoursSchedule>({});

  ngOnInit(): void {
    this.ai.getAutoReplySettings().subscribe({
      next: (settings) => {
        this.enabled.set(settings.enabled);
        this.confidenceThreshold.set(settings.confidenceThreshold);
        this.dailyLimit.set(settings.dailyLimit);
        this.schedule.set(settings.businessHours);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load AI auto-reply settings.');
      },
    });
  }

  save(): void {
    this.saving.set(true);
    const schedule = this.schedule();
    this.ai
      .updateAutoReplySettings({
        enabled: this.enabled(),
        confidenceThreshold: this.confidenceThreshold(),
        dailyLimit: this.dailyLimit(),
        businessHours: Object.keys(schedule).length > 0 ? schedule : null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.toast.show('AI auto-reply settings saved.', 'success');
        },
        error: (err) => {
          this.saving.set(false);
          this.toast.showError(err, 'Could not save AI auto-reply settings.');
        },
      });
  }
}
