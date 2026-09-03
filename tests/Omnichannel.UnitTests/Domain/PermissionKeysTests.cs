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
        // PRD §12 lists 16 permission keys — a change here should be deliberate, not accidental.
        Assert.Equal(16, PermissionKeys.All.Count);
    }
}
