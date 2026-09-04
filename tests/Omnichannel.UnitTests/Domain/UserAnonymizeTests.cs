using Omnichannel.Domain.Identity;

namespace Omnichannel.UnitTests.Domain;

public class UserAnonymizeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Anonymize_ScrubsEmailAndDisplayName()
    {
        var user = User.Create(Guid.NewGuid(), "real.person@example.com", "Real Person", Now);

        user.Anonymize(Now.AddDays(1));

        Assert.DoesNotContain("real.person", user.Email, StringComparison.Ordinal);
        Assert.Equal("Deleted user", user.DisplayName);
        Assert.Contains("deleted.invalid", user.Email, StringComparison.Ordinal);
    }

    [Fact]
    public void Anonymize_ProducesAUniquePlaceholderPerUser()
    {
        var userA = User.Create(Guid.NewGuid(), "a@example.com", "A", Now);
        var userB = User.Create(Guid.NewGuid(), "b@example.com", "B", Now);

        userA.Anonymize(Now);
        userB.Anonymize(Now);

        Assert.NotEqual(userA.Email, userB.Email);
    }
}
