import { Injectable, OnDestroy, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import {
  AssignmentUpdateEvent,
  ConversationUpdateEvent,
  HubEventTypes,
  MessageStatusEvent,
  NewMessageEvent,
  NotificationEvent,
} from '../models/realtime.models';

const ACCESS_TOKEN_KEY = 'omnichannel.accessToken';

@Injectable({ providedIn: 'root' })
export class RealtimeService implements OnDestroy {
  private connection: HubConnection | null = null;
  private readonly seenEventIds = new Set<string>();

  private readonly _connected = signal(false);
  readonly connected = this._connected.asReadonly();

  readonly newMessage$ = new Subject<NewMessageEvent>();
  readonly conversationUpdate$ = new Subject<ConversationUpdateEvent>();
  readonly assignmentUpdate$ = new Subject<AssignmentUpdateEvent>();
  readonly messageStatus$ = new Subject<MessageStatusEvent>();
  readonly notification$ = new Subject<NotificationEvent>();

  start(): void {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
      return;
    }

    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/inbox', {
        accessTokenFactory: () => localStorage.getItem(ACCESS_TOKEN_KEY) ?? '',
      })
      .withAutomaticReconnect([0, 2, 5, 10, 15, 30])
      .build();

    this.connection.on(HubEventTypes.NewMessage, (event: NewMessageEvent) => {
      if (this.dedupe(HubEventTypes.NewMessage, event.eventId)) return;
      this.newMessage$.next(event);
    });

    this.connection.on(HubEventTypes.ConversationUpdate, (event: ConversationUpdateEvent) => {
      if (this.dedupe(HubEventTypes.ConversationUpdate, event.eventId)) return;
      this.conversationUpdate$.next(event);
    });

    this.connection.on(HubEventTypes.AssignmentUpdate, (event: AssignmentUpdateEvent) => {
      if (this.dedupe(HubEventTypes.AssignmentUpdate, event.eventId)) return;
      this.assignmentUpdate$.next(event);
    });

    this.connection.on(HubEventTypes.MessageStatus, (event: MessageStatusEvent) => {
      if (this.dedupe(HubEventTypes.MessageStatus, event.eventId)) return;
      this.messageStatus$.next(event);
    });

    this.connection.on(HubEventTypes.Notification, (event: NotificationEvent) => {
      if (this.dedupe(HubEventTypes.Notification, event.eventId)) return;
      this.notification$.next(event);
    });

    this.connection.onreconnecting(() => this._connected.set(false));
    this.connection.onreconnected(() => this._connected.set(true));
    this.connection.onclose(() => this._connected.set(false));

    this.connection.start().then(
      () => this._connected.set(true),
      () => this._connected.set(false),
    );
  }

  stop(): void {
    if (this.connection) {
      this.connection.stop();
      this.connection = null;
      this._connected.set(false);
      this.seenEventIds.clear();
    }
  }

  private dedupe(type: string, eventId: string): boolean {
    const key = `${type}:${eventId}`;
    if (this.seenEventIds.has(key)) return true;
    this.seenEventIds.add(key);
    if (this.seenEventIds.size > 500) {
      const first = this.seenEventIds.values().next().value;
      if (first !== undefined) this.seenEventIds.delete(first);
    }
    return false;
  }

  ngOnDestroy(): void {
    this.stop();
  }
}
