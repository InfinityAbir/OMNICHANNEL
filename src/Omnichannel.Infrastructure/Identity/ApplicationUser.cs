using Microsoft.AspNetCore.Identity;

namespace Omnichannel.Infrastructure.Identity;

/// <summary>
/// Credential/auth record only (password hash, lockout, security stamp). The business-facing
/// profile is Domain.Identity.User, linked 1:1 by sharing this same Id — see that type's doc
/// comment and ADR-0007.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
}
