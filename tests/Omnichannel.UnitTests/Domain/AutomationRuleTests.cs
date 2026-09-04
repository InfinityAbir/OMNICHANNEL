using Omnichannel.Domain.Automation;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.UnitTests.Domain;

public class AutomationRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_RequiresAtLeastOneAction()
    {
        Assert.Throws<ArgumentException>(() => AutomationRule.Create(Guid.NewGuid(), "Rule", "refund", null, null, false, Now));
    }

    [Fact]
    public void Create_RequiresKeyword()
    {
        Assert.Throws<ArgumentException>(() => AutomationRule.Create(Guid.NewGuid(), "Rule", "", "Billing", null, false, Now));
    }

    [Fact]
    public void Create_DefaultsNameToKeyword_WhenNameBlank()
    {
        var rule = AutomationRule.Create(Guid.NewGuid(), "", "billing", "Billing", null, false, Now);
        Assert.Equal("billing", rule.Name);
    }

    [Fact]
    public void Matches_IsCaseInsensitiveSubstring()
    {
        var rule = AutomationRule.Create(Guid.NewGuid(), "Refund", "refund", null, null, true, Now);

        Assert.True(rule.Matches("I would like a REFUND please"));
        Assert.False(rule.Matches("What's your return policy?"));
    }

    [Fact]
    public void Matches_False_WhenDisabled()
    {
        var rule = AutomationRule.Create(Guid.NewGuid(), "Refund", "refund", null, null, true, Now);
        rule.SetEnabled(false, Now);

        Assert.False(rule.Matches("I want a refund"));
    }

    [Fact]
    public void Matches_False_ForEmptyOrNullMessage()
    {
        var rule = AutomationRule.Create(Guid.NewGuid(), "Refund", "refund", null, null, true, Now);

        Assert.False(rule.Matches(""));
        Assert.False(rule.Matches("   "));
    }

    [Fact]
    public void Create_SetsActionsCorrectly()
    {
        var rule = AutomationRule.Create(Guid.NewGuid(), "Billing", "billing", "Billing", ConversationPriority.High, true, Now);

        Assert.Equal("Billing", rule.ApplyTagName);
        Assert.Equal(ConversationPriority.High, rule.SetPriority);
        Assert.True(rule.Escalate);
        Assert.True(rule.Enabled);
    }
}
