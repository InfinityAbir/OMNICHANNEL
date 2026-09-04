import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AutomationService } from '../../../core/services/automation.service';
import { ToastService } from '../../../core/services/toast.service';
import { BusinessHoursSchedule } from '../../../core/models/ai.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';
import { BusinessHoursEditorComponent } from '../../../shared/business-hours-editor/business-hours-editor';

@Component({
  selector: 'app-business-hours-settings',
  standalone: true,
  imports: [FormsModule, SkeletonComponent, BusinessHoursEditorComponent],
  templateUrl: './business-hours-settings.html',
  styleUrls: ['../../../shared/settings-common.scss', './business-hours-settings.scss'],
})
export class BusinessHoursSettingsComponent implements OnInit {
  private readonly automation = inject(AutomationService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly schedule = signal<BusinessHoursSchedule>({});
  readonly holidays = signal<string[]>([]);
  readonly newHolidayDraft = signal('');

  ngOnInit(): void {
    this.automation.getBusinessHours().subscribe({
      next: (hours) => {
        this.schedule.set(hours.businessHours);
        this.holidays.set(hours.holidays);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load business hours.');
      },
    });
  }

  addHoliday(): void {
    const value = this.newHolidayDraft();
    if (!value || this.holidays().includes(value)) return;
    this.holidays.update((current) => [...current, value].sort());
    this.newHolidayDraft.set('');
  }

  removeHoliday(date: string): void {
    this.holidays.update((current) => current.filter((d) => d !== date));
  }

  save(): void {
    this.saving.set(true);
    const schedule = this.schedule();
    this.automation
      .updateBusinessHours({
        businessHours: Object.keys(schedule).length > 0 ? schedule : null,
        holidays: this.holidays(),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.toast.show('Business hours saved.', 'success');
        },
        error: (err) => {
          this.saving.set(false);
          this.toast.showError(err, 'Could not save business hours.');
        },
      });
  }
}
