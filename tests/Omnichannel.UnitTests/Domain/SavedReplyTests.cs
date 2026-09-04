using Omnichannel.Domain.Automation;

namespace Omnichannel.UnitTests.Domain;

public class SavedReplyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsTitleAndText()
    {
        var reply = SavedReply.Create(Guid.NewGuid(), "  Welcome  ", "  Hi there!  ", Now);

        Assert.Equal("Welcome", reply.Title);
        Assert.Equal("Hi there!", reply.Text);
    }

    [Theory]
    [InlineData("", "text")]
    [InlineData("title", "")]
    [InlineData("   ", "text")]
    public void Create_RejectsBlankFields(string title, string text)
    {
        Assert.Throws<ArgumentException>(() => SavedReply.Create(Guid.NewGuid(), title, text, Now));
    }

    [Fact]
    public void Update_ChangesFieldsAndTimestamp()
    {
        var reply = SavedReply.Create(Guid.NewGuid(), "Welcome", "Hi!", Now);
        var later = Now.AddHours(1);

        reply.Update("Farewell", "Bye!", later);

        Assert.Equal("Farewell", reply.Title);
        Assert.Equal("Bye!", reply.Text);
        Assert.Equal(later, reply.UpdatedAt);
    }
}
