using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Infrastructure.Persistence;

namespace Omnichannel.Infrastructure.Security;

/// <summary>
/// Persists the Data Protection key ring in Postgres (the <c>data_protection_keys</c> table)
/// instead of the container's local filesystem — see <see cref="DataProtectionKeyRecord"/> for
/// why. ASP.NET Core's key manager can call this outside of any request scope (e.g. during
/// startup before the first request), so it opens its own DI scope per call via
/// <see cref="IServiceScopeFactory"/> rather than taking a scoped <c>AppDbContext</c> directly —
/// the pattern Microsoft's own docs use for a custom key repository.
/// </summary>
public sealed class EfXmlRepository(IServiceScopeFactory scopeFactory) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.DataProtectionKeys
            .Select(k => k.Xml)
            .AsEnumerable()
            .Select(XElement.Parse)
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DataProtectionKeys.Add(new DataProtectionKeyRecord
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        db.SaveChanges();
    }
}
