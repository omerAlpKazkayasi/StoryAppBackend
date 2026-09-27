using StoryApp.Domain.Enums;

namespace StoryApp.Application.Common.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    UserAccountType AccountType { get; }
}
