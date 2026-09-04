using Omnichannel.Domain.Knowledge;

namespace Omnichannel.UnitTests.Domain;

public class KnowledgeDocumentTests
{
    [Fact]
    public void Create_SetsVersionToOne()
    {
        var document = KnowledgeDocument.Create(Guid.NewGuid(), "Return Policy", "Customers may return items within 30 days.", DateTimeOffset.UtcNow);

        Assert.Equal(1, document.Version);
        Assert.Equal(KnowledgeDocumentStatus.Active, document.Status);
    }

    [Fact]
    public void ReviseContent_IncrementsVersion()
    {
        var now = DateTimeOffset.UtcNow;
        var document = KnowledgeDocument.Create(Guid.NewGuid(), "Return Policy", "30 days.", now);

        document.ReviseContent("Return Policy", "45 days now.", now.AddMinutes(5));

        Assert.Equal(2, document.Version);
        Assert.Equal("45 days now.", document.Content);
    }

    [Fact]
    public void Archive_SetsStatusArchived()
    {
        var document = KnowledgeDocument.Create(Guid.NewGuid(), "Old Policy", "Deprecated.", DateTimeOffset.UtcNow);

        document.Archive(DateTimeOffset.UtcNow);

        Assert.Equal(KnowledgeDocumentStatus.Archived, document.Status);
    }

    [Fact]
    public void Create_RejectsEmptyContent()
    {
        Assert.Throws<ArgumentException>(() => KnowledgeDocument.Create(Guid.NewGuid(), "Title", "   ", DateTimeOffset.UtcNow));
    }
}
