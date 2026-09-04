import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BusinessHoursSchedule, BusinessHoursWindow, DAYS_OF_WEEK, DayOfWeekName } from '../../core/models/ai.models';

/** Reusable weekly schedule editor — used by both the AI-specific auto-reply business hours
 * (Phase 12) and the general tenant business hours (Phase 13), which are two independent configs
 * on the backend (ADR-0023) but share the exact same editing UI. */
@Component({
  selector: 'app-business-hours-editor',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './business-hours-editor.html',
  styleUrl: './business-hours-editor.scss',
})
export class BusinessHoursEditorComponent {
  readonly schedule = input.required<BusinessHoursSchedule>();
  readonly scheduleChange = output<BusinessHoursSchedule>();

  readonly days = DAYS_OF_WEEK;

  windowsFor(day: DayOfWeekName): BusinessHoursWindow[] {
    return this.schedule()[day] ?? [];
  }

  isOpen(day: DayOfWeekName): boolean {
    return this.windowsFor(day).length > 0;
  }

  toggleDay(day: DayOfWeekName, open: boolean): void {
    const next = { ...this.schedule() };
    if (open) {
      next[day] = [{ start: '09:00', end: '17:00' }];
    } else {
      delete next[day];
    }
    this.scheduleChange.emit(next);
  }

  addWindow(day: DayOfWeekName): void {
    const next = { ...this.schedule() };
    next[day] = [...this.windowsFor(day), { start: '09:00', end: '17:00' }];
    this.scheduleChange.emit(next);
  }

  removeWindow(day: DayOfWeekName, index: number): void {
    const next = { ...this.schedule() };
    const windows = this.windowsFor(day).filter((_, i) => i !== index);
    if (windows.length === 0) {
      delete next[day];
    } else {
      next[day] = windows;
    }
    this.scheduleChange.emit(next);
  }

  updateWindow(day: DayOfWeekName, index: number, field: 'start' | 'end', value: string): void {
    const next = { ...this.schedule() };
    const windows = this.windowsFor(day).map((w, i) => (i === index ? { ...w, [field]: value } : w));
    next[day] = windows;
    this.scheduleChange.emit(next);
  }
}
