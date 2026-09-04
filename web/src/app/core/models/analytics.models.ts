export interface ChannelMetricResponse {
  channelType: string;
  conversationCount: number;
}

export interface AgentMetricResponse {
  agentUserId: string;
  agentDisplayName: string;
  assignedConversationCount: number;
  closedConversationCount: number;
}

export interface AnalyticsSummaryResponse {
  from: string;
  to: string;
  totalConversations: number;
  openConversations: number;
  pendingConversations: number;
  escalatedConversations: number;
  resolvedConversations: number;
  closedConversations: number;
  averageFirstResponseMinutes: number | null;
  averageResolutionMinutes: number | null;
  resolutionRatePercent: number;
  aiSuggestionsGenerated: number;
  averageAiSuggestionConfidence: number | null;
  aiAutoRepliesSent: number;
  byChannel: ChannelMetricResponse[];
  byAgent: AgentMetricResponse[];
}
