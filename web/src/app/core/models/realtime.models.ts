export interface NewMessageEvent {
  conversationId: string;
  messageId: string;
  direction: string;
  senderType: string;
  contentType: string;
  text: string;
  createdAt: string;
  deliveryStatus: string;
  externalMessageId: string | null;
  eventId: string;
}

export interface ConversationUpdateEvent {
  conversationId: string;
  status: string | null;
  priority: string | null;
  aiMode: string | null;
  lastMessageAt: string | null;
  lastMessagePreview: string | null;
  assignedUserId: string | null;
  eventId: string;
}

export interface AssignmentUpdateEvent {
  conversationId: string;
  assignedUserId: string | null;
  assignedUserName: string;
  eventId: string;
}

export interface MessageStatusEvent {
  conversationId: string;
  messageId: string;
  deliveryStatus: string;
  sentAt: string | null;
  deliveredAt: string | null;
  readAt: string | null;
  eventId: string;
}

export interface NotificationEvent {
  conversationId: string;
  type: string;
  title: string;
  body: string;
  severity: string;
  eventId: string;
}

export const HubEventTypes = {
  NewMessage: 'new_message',
  ConversationUpdate: 'conversation_update',
  AssignmentUpdate: 'assignment_update',
  MessageStatus: 'message_status',
  Notification: 'notification',
} as const;
