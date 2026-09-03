import { DatePipe } from '@angular/common';
import { Component, OnDestroy, effect, inject, input, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { ConversationService } from '../../../core/services/conversation.service';
import { RealtimeService } from '../../../core/services/realtime.service';
import { ConversationSummaryResponse, ConversationStatus } from '../../../core/models/conversation.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state';

type FilterKey = 'all' | 'mine' | 'escalated' | 'closed';

const FILTERS: { key: FilterKey; label: string; status?: ConversationStatus }[] = [
  { key: 'all', label: 'All' },
  { key: 'mine', label: 'Assigned to me' },
  { key: 'escalated', label: 'Escalated', status: 'Escalated' },
  { key: 'closed', label: 'Closed', status: 'Closed' },
];

@Component({
  selector: 'app-conversation-list',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, DatePipe, SkeletonComponent, EmptyStateComponent],
  templateUrl: './conversation-list.html',
  styleUrl: './conversation-list.scss',
})
export class ConversationListComponent implements OnDestroy {
  private readonly conversations = inject(ConversationService);
  private readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);

  /** Re-fetches when bumped — parent calls this after sending a message so the list re-sorts. */
  readonly refreshToken = input(0);

  readonly filters = FILTERS;
  readonly activeFilter = signal<FilterKey>('all');
  readonly searchTerm = signal('');

  readonly items = signal<ConversationSummaryResponse[]>([]);
  readonly loading = signal(true);
  readonly loadingMore = signal(false);
  readonly nextCursor = signal<string | null>(null);
  readonly errored = signal(false);

  private searchDebounce?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      this.activeFilter();
      this.searchTerm();
      this.refreshToken();
      this.load();
    });

    this.realtime.conversationUpdate$.subscribe((event) => {
      const current = this.items();
      if (current.some((item) => item.id === event.conversationId)) {
        this.items.update((items) =>
          items.map((item) =>
            item.id === event.conversationId
              ? {
                  ...item,
                  status: (event.status ?? item.status) as ConversationStatus,
                  priority: (event.priority ?? item.priority) as ConversationSummaryResponse['priority'],
                  assignedUserId: event.assignedUserId ?? item.assignedUserId,
                  lastMessageAt: event.lastMessageAt ?? item.lastMessageAt,
                  lastMessagePreview: event.lastMessagePreview ?? item.lastMessagePreview,
                }
              : item,
          ),
        );
      } else {
        // Unknown conversation — reload the list to pick up newly created conversations the
        // minimal event DTO can't describe in full.
        this.load();
      }
    });

    this.realtime.newMessage$.subscribe((event) => {
      const current = this.items();
      if (current.some((item) => item.id === event.conversationId)) {
        this.items.update((items) =>
          items.map((item) =>
            item.id === event.conversationId
              ? {
                  ...item,
                  lastMessageAt: event.createdAt,
                  lastMessagePreview: event.text,
                }
              : item,
          ),
        );
        this.reorderByDate();
      }
    });

    this.realtime.assignmentUpdate$.subscribe((event) => {
      this.items.update((current) =>
        current.map((item) =>
          item.id === event.conversationId ? { ...item, assignedUserId: event.assignedUserId } : item,
        ),
      );
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.searchDebounce);
  }

  setFilter(key: FilterKey): void {
    this.activeFilter.set(key);
  }

  onSearchInput(value: string): void {
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => this.searchTerm.set(value), 300);
  }

  loadMore(): void {
    const cursor = this.nextCursor();
    if (!cursor || this.loadingMore()) {
      return;
    }

    this.loadingMore.set(true);
    const filter = this.filters.find((f) => f.key === this.activeFilter())!;
    this.conversations
      .list({
        status: filter.status,
        assignedUserId: filter.key === 'mine' ? this.auth.currentUser()?.userId : undefined,
        search: this.searchTerm() || undefined,
        cursor,
      })
      .subscribe({
        next: (page) => {
          this.items.update((current) => [...current, ...page.items]);
          this.nextCursor.set(page.nextCursor);
          this.loadingMore.set(false);
        },
        error: () => this.loadingMore.set(false),
      });
  }

  private reorderByDate(): void {
    this.items.update((current) =>
      [...current].sort((a, b) => new Date(b.lastMessageAt).getTime() - new Date(a.lastMessageAt).getTime()),
    );
  }

  private load(): void {
    this.loading.set(true);
    this.errored.set(false);
    const filter = this.filters.find((f) => f.key === this.activeFilter())!;

    this.conversations
      .list({
        status: filter.status,
        assignedUserId: filter.key === 'mine' ? this.auth.currentUser()?.userId : undefined,
        search: this.searchTerm() || undefined,
      })
      .subscribe({
        next: (page) => {
          this.items.set(page.items);
          this.nextCursor.set(page.nextCursor);
          this.loading.set(false);
        },
        error: () => {
          this.errored.set(true);
          this.loading.set(false);
        },
      });
  }
}
