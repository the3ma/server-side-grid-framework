namespace SampleApi.Auth;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

/// <summary>
/// DEMO ONLY. Signs every request in as a user of tenant 1. <c>X-Demo-Tenant: 2|3</c> switches tenant and
/// <c>X-Demo-Finance: true</c> adds the <c>finance</c> role, so the row scope and the restricted column can be
/// tried without an identity provider. A real deployment replaces this with its own authentication; the grid
/// code only ever reads the principal.
/// </summary>
public sealed class DemoAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Demo";
    public const string TenantClaim = "tenant";
    public const string FinanceRole = "finance";

    public DemoAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tenant = this.Request.Headers["X-Demo-Tenant"].ToString() switch
        {
            "2" => "2",
            "3" => "3",
            _ => "1",
        };

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"demo-user-{tenant}"),
            new(TenantClaim, tenant),
        };

        if (string.Equals(this.Request.Headers["X-Demo-Finance"].ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, FinanceRole));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

public static class PrincipalExtensions
{
    public static long TenantId(this ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(DemoAuthHandler.TenantClaim) ?? throw new InvalidOperationException("No tenant claim."),
            System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsFinance(this ClaimsPrincipal user) => user.IsInRole(DemoAuthHandler.FinanceRole);
}
