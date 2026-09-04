import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConversationService } from '../../../core/services/conversation.service';
import { TagService } from '../../../core/services/tag.service';
import { AuthService } from '../../../core/services/auth.service';
import { RealtimeService } from '../../../core/services/realtime.service';
import { AiService } from '../../../core/services/ai.service';
import {
  ConversationDetailResponse,
  ConversationPriority,
  ConversationStatus,
  MessageDirection,
  MessageResponse,
  MessageSenderType,
  NoteResponse,
  TagResponse,
} from '../../../core/models/conversation.models';
import { SkeletonComponent } from '../../../shared/skeleton/skeleton';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state';
import { ChannelIconComponent } from '../../../shared/channel-icon/channel-icon';

const STATUSES: ConversationStatus[] = [
  'Open',
  'Pending',
  'WaitingForCustomer',
  'WaitingForAgent',
  'Escalated',
  'Resolved',
  'Closed',
];
const PRIORITIES: ConversationPriority[] = ['Low', 'Normal', 'High', 'Urgent'];

@Component({
  selector: 'app-conversation-detail',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, SkeletonComponent, EmptyStateComponent, ChannelIconComponent],
  templateUrl: './conversation-detail.html',
  styleUrl: './conversation-detail.scss',
})
export class ConversationDetailComponent {
  private readonly conversations = inject(ConversationService);
  private readonly tagsApi = inject(TagService);
  private readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);
  private readonly ai = inject(AiService);

  readonly id = input.required<string>();
  readonly changed = output<void>();

  readonly statuses = STATUSES;
  readonly priorities = PRIORITIES;

  readonly detail = signal<ConversationDetailResponse | null>(null);
  readonly loading = signal(true);
  readonly errored = signal(false);

  readonly activeTab = signal<'messages' | 'notes'>('messages');

  /** Newest-first internally (matches the keyset cursor's pagination direction). */
  readonly messages = signal<MessageResponse[]>([]);
  /** Oldest-first for the chat timeline display (scroll up = older, like any chat UI). */
  readonly displayMessages = computed(() => [...this.messages()].reverse());
  readonly messagesCursor = signal<string | null>(null);
  readonly messagesLoading = signal(true);

  readonly notes = signal<NoteResponse[]>([]);
  readonly notesLoading = signal(false);
  readonly notesLoaded = signal(false);

  readonly composerText = signal('');
  readonly sending = signal(false);
  readonly suggesting = signal(false);
  readonly suggestionConfidence = signal<number | null>(null);
  readonly suggestionError = signal<string | null>(null);
  readonly noteText = signal('');
  readonly savingNote = signal(false);

  readonly allTags = signal<TagResponse[]>([]);
  readonly newTagName = signal('');
  readonly currentUserId = this.auth.currentUser()?.userId ?? null;

  constructor() {
    effect(() => {
      const conversationId = this.id();
      this.loadDetail(conversationId);
      this.loadMessages(conversationId);
      this.activeTab.set('messages');
    });

    this.tagsApi.list().subscribe((tags) => this.allTags.set(tags));

    this.realtime.newMessage$.subscribe((event) => {
      if (event.conversationId !== this.id()) return;
      this.messages.update((current) => {
        if (current.some((m) => m.id === event.messageId)) return current;
        return [
          ...current,
          {
            id: event.messageId,
            direction: event.direction as MessageDirection,
            senderType: event.senderType as MessageSenderType,
            contentType: event.contentType,
            text: event.text,
            createdAt: event.createdAt,
            deliveryStatus: event.deliveryStatus as MessageResponse['deliveryStatus'],
          },
        ];
      });
    });

    this.realtime.conversationUpdate$.subscribe((event) => {
      if (event.conversationId !== this.id()) return;
      this.detail.update((current) =>
        current
          ? {
              ...current,
              status: (event.status ?? current.status) as ConversationStatus,
              priority: (event.priority ?? current.priority) as ConversationPriority,
              assignedUserId: event.assignedUserId ?? current.assignedUserId,
            }
          : current,
      );
    });

    this.realtime.assignmentUpdate$.subscribe((event) => {
      if (event.conversationId !== this.id()) return;
      this.detail.update((current) => (current ? { ...current, assignedUserId: event.assignedUserId } : current));
    });
  }

  loadOlderMessages(): void {
    const cursor = this.messagesCursor();
    if (!cursor) return;
    this.conversations.listMessages(this.id(), cursor).subscribe((page) => {
      this.messages.update((current) => [...current, ...page.items]);
      this.messagesCursor.set(page.nextCursor);
    });
  }

  switchTab(tab: 'messages' | 'notes'): void {
    this.activeTab.set(tab);
    if (tab === 'notes' && !this.notesLoaded()) {
      this.notesLoading.set(true);
      this.conversations.listNotes(this.id()).subscribe((notes) => {
        this.notes.set(notes);
        this.notesLoading.set(false);
        this.notesLoaded.set(true);
      });
    }
  }

  onComposerKeydown(event: Event): void {
    const keyboardEvent = event as KeyboardEvent;
    if (keyboardEvent.metaKey || keyboardEvent.ctrlKey) {
      keyboardEvent.preventDefault();
      this.send();
    }
  }

  send(): void {
    const text = this.composerText().trim();
    if (!text || this.sending()) return;

    this.sending.set(true);
    this.conversations.sendMessage(this.id(), text).subscribe({
      next: (message) => {
        this.messages.update((current) =>
          current.some((m) => m.id === message.id) ? current : [message, ...current],
        );
        this.composerText.set('');
        this.sending.set(false);
        this.suggestionConfidence.set(null);
        this.suggestionError.set(null);
        this.changed.emit();
      },
      error: () => this.sending.set(false),
    });
  }

  /** Suggest mode only (PRD §69/§87): drafts text into the composer for the agent to review, edit, or discard — never sends anything itself. */
  suggestReply(): void {
    if (this.suggesting()) return;

    this.suggesting.set(true);
    this.suggestionError.set(null);
    this.ai.generateSuggestion(this.id()).subscribe({
      next: (suggestion) => {
        this.composerText.set(suggestion.suggestedText);
        this.suggestionConfidence.set(suggestion.confidence);
        this.suggesting.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.suggestionConfidence.set(null);
        this.suggestionError.set(
          err.status === 429
            ? "Today's AI suggestion limit has been reached — please reply manually."
            : 'AI suggestion is unavailable right now — please reply manually.',
        );
        this.suggesting.set(false);
      },
    });
  }

  addNote(): void {
    const text = this.noteText().trim();
    if (!text || this.savingNote()) return;

    this.savingNote.set(true);
    this.conversations.addNote(this.id(), text).subscribe({
      next: (note) => {
        this.notes.update((current) => [note, ...current]);
        this.noteText.set('');
        this.savingNote.set(false);
      },
      error: () => this.savingNote.set(false),
    });
  }

  assignToMe(): void {
    if (!this.currentUserId) return;
    this.conversations.assign(this.id(), this.currentUserId).subscribe(() => this.refreshAfterMutation());
  }

  unassign(): void {
    this.conversations.unassign(this.id()).subscribe(() => this.refreshAfterMutation());
  }

  changeStatus(status: string): void {
    this.conversations.changeStatus(this.id(), status as ConversationStatus).subscribe(() => this.refreshAfterMutation());
  }

  changePriority(priority: string): void {
    this.conversations.setPriority(this.id(), priority as ConversationPriority).subscribe(() => this.refreshAfterMutation());
  }

  addExistingTag(name: string): void {
    if (!name) return;
    this.conversations.addTag(this.id(), name).subscribe(() => this.refreshAfterMutation());
  }

  addNewTag(): void {
    const name = this.newTagName().trim();
    if (!name) return;
    this.conversations.addTag(this.id(), name).subscribe(() => {
      this.newTagName.set('');
      this.tagsApi.list().subscribe((tags) => this.allTags.set(tags));
      this.refreshAfterMutation();
    });
  }

  removeTag(tagId: string): void {
    this.conversations.removeTag(this.id(), tagId).subscribe(() => this.refreshAfterMutation());
  }

  private refreshAfterMutation(): void {
    this.loadDetail(this.id());
    this.changed.emit();
  }

  private loadDetail(conversationId: string): void {
    this.loading.set(true);
    this.errored.set(false);
    this.conversations.get(conversationId).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.loading.set(false);
      },
      error: () => {
        this.errored.set(true);
        this.loading.set(false);
      },
    });
  }

  private loadMessages(conversationId: string): void {
    this.messagesLoading.set(true);
    this.messages.set([]);
    this.conversations.listMessages(conversationId).subscribe({
      next: (page) => {
        this.messages.set(page.items);
        this.messagesCursor.set(page.nextCursor);
        this.messagesLoading.set(false);
      },
      error: () => this.messagesLoading.set(false),
    });
  }
}
