using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Knowledge;

public enum KnowledgeDocumentStatus
{
    Active = 0,
    Archived = 1,
}

/// <summary>
/// A piece of business knowledge (FAQ, policy, product info, ...) an agent authored so the AI
/// assistant can retrieve and cite it (PRD §70). Plain-text only this phase — no file upload —
/// which is a deliberate scope choice (ADR-0021), not an oversight: text submitted directly
/// through the API sidesteps the entire file-upload attack surface (malicious file content,
/// parser exploits, storage) that a real upload pipeline would need its own review for.
/// </summary>
public sealed class KnowledgeDocument : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public KnowledgeDocumentStatus Status { get; private set; } = KnowledgeDocumentStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private KnowledgeDocument()
    {
    }

    public static KnowledgeDocument Create(Guid tenantId, string title, string content, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        return new KnowledgeDocument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Content = content.Trim(),
            Version = 1,
            Status = KnowledgeDocumentStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Bumps the version — the caller (KnowledgeService) is responsible for deleting and rebuilding the chunk/embedding set to match, since content changing invalidates every existing chunk.</summary>
    public void ReviseContent(string title, string content, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        Title = title.Trim();
        Content = content.Trim();
        Version++;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        Status = KnowledgeDocumentStatus.Archived;
        UpdatedAt = now;
    }
}
