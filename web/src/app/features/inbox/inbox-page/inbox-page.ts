import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConversationListComponent } from '../conversation-list/conversation-list';
import { ConversationDetailComponent } from '../conversation-detail/conversation-detail';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state';
import { ConversationService } from '../../../core/services/conversation.service';

@Component({
  selector: 'app-inbox-page',
  standalone: true,
  imports: [FormsModule, ConversationListComponent, ConversationDetailComponent, EmptyStateComponent],
  templateUrl: './inbox-page.html',
  styleUrl: './inbox-page.scss',
})
export class InboxPageComponent {
  private readonly conversationsApi = inject(ConversationService);
  private readonly router = inject(Router);

  /** Bound automatically from the :id route param (see withComponentInputBinding in app.config) — undefined on the bare /inbox route. */
  readonly id = input<string | undefined>(undefined);

  readonly listRefreshToken = signal(0);

  readonly showNewConversation = signal(false);
  readonly newContactName = signal('');
  readonly newMessageText = signal('');
  readonly creating = signal(false);

  onConversationChanged(): void {
    this.listRefreshToken.update((n) => n + 1);
  }

  openNewConversation(): void {
    this.showNewConversation.set(true);
  }

  closeNewConversation(): void {
    this.showNewConversation.set(false);
    this.newContactName.set('');
    this.newMessageText.set('');
  }

  createConversation(): void {
    const name = this.newContactName().trim();
    if (!name || this.creating()) return;

    this.creating.set(true);
    this.conversationsApi
      .create({ newContactDisplayName: name, initialMessageText: this.newMessageText().trim() || undefined })
      .subscribe({
        next: (conversation) => {
          this.creating.set(false);
          this.closeNewConversation();
          this.listRefreshToken.update((n) => n + 1);
          void this.router.navigate(['/inbox', conversation.id]);
        },
        error: () => this.creating.set(false),
      });
  }
}
