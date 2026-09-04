import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { NotificationService } from '../../core/services/notification.service';
import { ToastService } from '../../core/services/toast.service';
import { NotificationResponse } from '../../core/models/notification.models';

const POLL_INTERVAL_MS = 30000;

@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [DatePipe, RouterLink],
  templateUrl: './notification-bell.html',
  styleUrl: './notification-bell.scss',
})
export class NotificationBellComponent implements OnInit, OnDestroy {
  private readonly notifications = inject(NotificationService);
  private readonly toast = inject(ToastService);
  private pollHandle: ReturnType<typeof setInterval> | undefined;

  readonly unreadCount = signal(0);
  readonly open = signal(false);
  readonly items = signal<NotificationResponse[]>([]);
  readonly loading = signal(false);

  ngOnInit(): void {
    this.refreshCount();
    this.pollHandle = setInterval(() => this.refreshCount(), POLL_INTERVAL_MS);
  }

  ngOnDestroy(): void {
    if (this.pollHandle) clearInterval(this.pollHandle);
  }

  toggle(): void {
    this.open.update((v) => !v);
    if (this.open()) {
      this.loadItems();
    }
  }

  close(): void {
    this.open.set(false);
  }

  markRead(item: NotificationResponse): void {
    if (item.read) return;
    this.notifications.markRead(item.id).subscribe({
      next: () => {
        this.items.update((current) => current.map((n) => (n.id === item.id ? { ...n, read: true } : n)));
        this.refreshCount();
      },
      error: (err) => this.toast.showError(err, 'Could not mark the notification as read.'),
    });
  }

  private refreshCount(): void {
    this.notifications.unreadCount().subscribe({
      next: (result) => this.unreadCount.set(result.count),
      error: () => undefined, // background polling — a transient failure here shouldn't interrupt the user
    });
  }

  private loadItems(): void {
    this.loading.set(true);
    this.notifications.list().subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err, 'Could not load notifications.');
      },
    });
  }
}
