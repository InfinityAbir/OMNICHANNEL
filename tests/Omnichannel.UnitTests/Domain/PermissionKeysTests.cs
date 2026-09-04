using Omnichannel.Domain.Authorization;

namespace Omnichannel.UnitTests.Domain;

public class PermissionKeysTests
{
    [Fact]
    public void All_ContainsNoDuplicates()
    {
        Assert.Equal(PermissionKeys.All.Count, PermissionKeys.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void All_MatchesPrdCatalogSize()
    {
        // PRD §12 lists 16 permission keys; tenant.delete was added post-launch (ADR-0030,
        // account deletion) as the first genuinely owner-exclusive permission — a further change
        // here should still be deliberate, not accidental.
        Assert.Equal(17, PermissionKeys.All.Count);
    }
}
