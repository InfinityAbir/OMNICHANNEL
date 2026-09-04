import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AutomationService } from '../../../core/services/automation.service';
import { ToastService } from '../../../core/services/toast.service';
import { AutomationRuleResponse } from '../../../core/models/automation.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state';

const PRIORITIES = ['', 'Low', 'Normal', 'High', 'Urgent'];

@Component({
  selector: 'app-automation-settings',
  standalone: true,
  imports: [FormsModule, SkeletonComponent, EmptyStateComponent],
  templateUrl: './automation-settings.html',
  styleUrls: ['../../../shared/settings-common.scss'],
})
export class AutomationSettingsComponent implements OnInit {
  private readonly automation = inject(AutomationService);
  private readonly toast = inject(ToastService);

  readonly priorities = PRIORITIES;
  readonly loading = signal(true);
  readonly rules = signal<AutomationRuleResponse[]>([]);

  readonly showForm = signal(false);
  readonly nameDraft = signal('');
  readonly keywordDraft = signal('');
  readonly tagDraft = signal('');
  readonly priorityDraft = signal('');
  readonly escalateDraft = signal(false);
  readonly saving = signal(false);

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.automation.listRules().subscribe({
      next: (rules) => {
        this.rules.set(rules);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load automation rules.');
      },
    });
  }

  openCreate(): void {
    this.nameDraft.set('');
    this.keywordDraft.set('');
    this.tagDraft.set('');
    this.priorityDraft.set('');
    this.escalateDraft.set(false);
    this.showForm.set(true);
  }

  closeForm(): void {
    this.showForm.set(false);
  }

  get hasAction(): boolean {
    return this.tagDraft().trim().length > 0 || this.priorityDraft().length > 0 || this.escalateDraft();
  }

  create(): void {
    const keyword = this.keywordDraft().trim();
    if (!keyword || !this.hasAction) return;

    this.saving.set(true);
    this.automation
      .createRule({
        name: this.nameDraft().trim() || undefined,
        keyword,
        applyTagName: this.tagDraft().trim() || undefined,
        setPriority: this.priorityDraft() || undefined,
        escalate: this.escalateDraft(),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.showForm.set(false);
          this.toast.show('Automation rule created.', 'success');
          this.load();
        },
        error: (err) => {
          this.saving.set(false);
          this.toast.showError(err, 'Could not create the rule.');
        },
      });
  }

  toggleEnabled(rule: AutomationRuleResponse): void {
    this.automation.setRuleEnabled(rule.id, !rule.enabled).subscribe({
      next: () => this.load(),
      error: (err) => this.toast.showError(err, 'Could not update the rule.'),
    });
  }

  deleteRule(rule: AutomationRuleResponse): void {
    this.automation.deleteRule(rule.id).subscribe({
      next: () => {
        this.toast.show('Rule deleted.', 'success');
        this.load();
      },
      error: (err) => this.toast.showError(err, 'Could not delete the rule.'),
    });
  }
}
