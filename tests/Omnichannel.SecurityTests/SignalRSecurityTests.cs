using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Omnichannel.Infrastructure.Realtime;

namespace Omnichannel.SecurityTests;

public class SignalRSecurityTests
{
    [Fact]
    public async Task AuthorizationHandler_Succeeds_ForAuthenticatedUserWithTenantId()
    {
        var handler = new InboxHubAuthorizationHandler();
        var tenantId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("sub", Guid.NewGuid().ToString()),
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var requirement = new HubAuthorizationRequirement();

        var context = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement },
            principal,
            httpContext);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task AuthorizationHandler_Fails_ForUserWithoutTenantId()
    {
        var handler = new InboxHubAuthorizationHandler();
        var claims = new[] { new Claim("sub", Guid.NewGuid().ToString()) };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var requirement = new HubAuthorizationRequirement();

        var context = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement },
            principal,
            httpContext);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Contains(requirement, context.PendingRequirements);
    }

    [Fact]
    public async Task AuthorizationHandler_Fails_ForUnauthenticatedUser()
    {
        var handler = new InboxHubAuthorizationHandler();
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var requirement = new HubAuthorizationRequirement();

        var context = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement },
            principal,
            httpContext);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Contains(requirement, context.PendingRequirements);
    }

    [Fact]
    public async Task AuthorizationHandler_IgnoresUnrelatedRequirements()
    {
        var handler = new InboxHubAuthorizationHandler();
        var requirement = new TestRequirement();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var context = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement },
            principal,
            null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Contains(requirement, context.PendingRequirements);
    }

    private sealed class TestRequirement : IAuthorizationRequirement;
}
