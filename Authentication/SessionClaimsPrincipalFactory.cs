using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskApi.Models;

namespace TaskApi.Authentication;

public sealed class SessionClaimsPrincipalFactory(UserManager<UserProfile> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<UserProfile>(users, options)
{
    public const string SessionClaim = "taskboard:session-version";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(UserProfile user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(SessionClaim, user.SessionVersion));
        return identity;
    }
}
