export interface ChannelAccountAdminResponse {
  id: string;
  type: string;
  displayName: string;
  status: string;
  externalAccountId: string | null;
  credentialConfigured: boolean;
}

export interface WidgetSettingsResponse {
  channelAccountId: string;
  enabled: boolean;
  allowedOrigins: string[];
  slug: string;
  embedSnippet: string;
}

/** Channel types this product currently supports. Not fetched from an API — there is no
 * "GET /channel-types" catalog endpoint, so this mirrors the fixed `ChannelType` enum the same
 * way `ConversationPriority`/`ConversationStatus` are already handled in conversation-detail.ts. */
export const CHANNEL_TYPES = ['WhatsApp', 'Instagram', 'Messenger'] as const;
export type ChannelTypeName = (typeof CHANNEL_TYPES)[number];
