using System.Security.Claims;
using StoryApp.Application.Common.Abstractions;
using StoryApp.Domain.Enums;

namespace StoryApp.Api.Authentication;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public Guid UserId
    {
        get
        {
            if (!IsAuthenticated)
            {
                return Guid.Empty;
            }

            var subClaim = User?.FindFirst("sub")?.Value
                ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(subClaim) || !Guid.TryParse(subClaim, out var userId))
            {
                throw new InvalidOperationException("Authenticated user does not have a valid 'sub' claim.");
            }

            return userId;
        }
    }

    public UserAccountType AccountType
    {
        get
        {
            if (!IsAuthenticated)
            {
                return UserAccountType.Guest;
            }

            var accountTypeClaim = User?.FindFirst("account_type")?.Value;

            if (string.IsNullOrWhiteSpace(accountTypeClaim) ||
                !Enum.TryParse<UserAccountType>(accountTypeClaim, ignoreCase: true, out var accountType))
            {
                throw new InvalidOperationException("Authenticated user does not have a valid 'account_type' claim.");
            }

            return accountType;
        }
    }
}
