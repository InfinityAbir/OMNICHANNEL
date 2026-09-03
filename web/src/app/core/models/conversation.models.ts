export type ConversationStatus =
  | 'Open'
  | 'Pending'
  | 'WaitingForCustomer'
  | 'WaitingForAgent'
  | 'Escalated'
  | 'Resolved'
  | 'Closed';

export type ConversationPriority = 'Low' | 'Normal' | 'High' | 'Urgent';

export type MessageDirection = 'Inbound' | 'Outbound';
export type MessageSenderType = 'Customer' | 'Agent' | 'Ai' | 'System';
export type MessageDeliveryStatus = 'Queued' | 'Sending' | 'Sent' | 'Delivered' | 'Read' | 'Failed';

export interface ContactResponse {
  id: string;
  displayName: string;
  createdAt: string;
  lastInteractionAt: string | null;
}

export interface ConversationSummaryResponse {
  id: string;
  contactId: string;
  contactDisplayName: string;
  channelAccountId: string;
  status: ConversationStatus;
  priority: ConversationPriority;
  assignedUserId: string | null;
  lastMessageAt: string;
  lastMessagePreview: string | null;
  tags: TagResponse[];
}

export interface ConversationDetailResponse {
  id: string;
  contactId: string;
  contactDisplayName: string;
  channelAccountId: string;
  status: ConversationStatus;
  priority: ConversationPriority;
  assignedUserId: string | null;
  aiMode: string;
  lastMessageAt: string;
  createdAt: string;
  closedAt: string | null;
  tags: TagResponse[];
}

export interface MessageResponse {
  id: string;
  direction: MessageDirection;
  senderType: MessageSenderType;
  contentType: string;
  text: string;
  createdAt: string;
  deliveryStatus: MessageDeliveryStatus;
}

export interface NoteResponse {
  id: string;
  authorUserId: string;
  text: string;
  createdAt: string;
}

export interface TagResponse {
  id: string;
  name: string;
}

export interface KeysetPageResponse<T> {
  items: T[];
  nextCursor: string | null;
}

export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
