using System.Security.Claims;
using Workspace.Application.Abstractions;
using Workspace.Domain.Common;

namespace Workspace.Api.Http;

/// <summary>Reads the authenticated principal from the current HTTP request.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary>Header used to correlate a client request w/ server logs and audit rows.</summary>
    public const string CorrelationHeader = "X-Correlation-Id";

    private HttpContext? Context => accessor.HttpContext;

    private ClaimsPrincipal? Principal => Context?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue("sub"), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue("email");

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => Truncate(Context?.Request.Headers.UserAgent.ToString(), 256);

    public string? CorrelationId =>
        Context?.Items.TryGetValue(CorrelationHeader, out var value) == true
            ? value as string
            : null;

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedException();

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
