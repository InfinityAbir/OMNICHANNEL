import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { ToastService } from '../../../core/services/toast.service';
import { AnalyticsSummaryResponse } from '../../../core/models/analytics.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state';

type RangeKey = '7' | '30' | '90';

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [FormsModule, DecimalPipe, SkeletonComponent, EmptyStateComponent],
  templateUrl: './analytics-dashboard.html',
  styleUrls: ['../../../shared/settings-common.scss', './analytics-dashboard.scss'],
})
export class AnalyticsDashboardComponent implements OnInit {
  private readonly analytics = inject(AnalyticsService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly summary = signal<AnalyticsSummaryResponse | null>(null);
  readonly range = signal<RangeKey>('30');

  readonly maxChannelCount = computed(() => Math.max(1, ...(this.summary()?.byChannel.map((c) => c.conversationCount) ?? [1])));
  readonly maxAgentCount = computed(() => Math.max(1, ...(this.summary()?.byAgent.map((a) => a.assignedConversationCount) ?? [1])));

  ngOnInit(): void {
    this.load();
  }

  setRange(range: RangeKey): void {
    this.range.set(range);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    const to = new Date();
    const from = new Date(to);
    from.setDate(from.getDate() - Number(this.range()));

    this.analytics.getSummary(from.toISOString(), to.toISOString()).subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load analytics.');
      },
    });
  }
}
