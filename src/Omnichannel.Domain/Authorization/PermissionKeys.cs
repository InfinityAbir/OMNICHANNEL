namespace Omnichannel.Domain.Authorization;

/// <summary>
/// The fixed permission catalog from PRD §12. Authorization checks a permission string,
/// never a role name, so business logic doesn't scatter role-name checks (AGENTS.md).
/// </summary>
public static class PermissionKeys
{
    public const string TenantRead = "tenant.read";
    public const string TenantUpdate = "tenant.update";
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string ConversationsRead = "conversations.read";
    public const string ConversationsReply = "conversations.reply";
    public const string ConversationsAssign = "conversations.assign";
    public const string ConversationsClose = "conversations.close";
    public const string ChannelsRead = "channels.read";
    public const string ChannelsManage = "channels.manage";
    public const string AiRead = "ai.read";
    public const string AiConfigure = "ai.configure";
    public const string KnowledgeRead = "knowledge.read";
    public const string KnowledgeManage = "knowledge.manage";
    public const string AnalyticsRead = "analytics.read";
    public const string AuditRead = "audit.read";

    public static readonly IReadOnlyList<string> All =
    [
        TenantRead, TenantUpdate,
        UsersRead, UsersManage,
        ConversationsRead, ConversationsReply, ConversationsAssign, ConversationsClose,
        ChannelsRead, ChannelsManage,
        AiRead, AiConfigure,
        KnowledgeRead, KnowledgeManage,
        AnalyticsRead,
        AuditRead,
    ];
}
