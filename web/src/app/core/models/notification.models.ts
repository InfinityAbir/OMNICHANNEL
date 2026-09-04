export interface NotificationResponse {
  id: string;
  type: string;
  title: string;
  body: string;
  conversationId: string | null;
  read: boolean;
  createdAt: string;
  readAt: string | null;
}
