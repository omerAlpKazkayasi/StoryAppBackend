using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryApp.Application.Auth;
using StoryApp.Application.Common.Exceptions;
using StoryApp.Domain.Enums;
using StoryApp.Infrastructure.Identity;
using StoryApp.Infrastructure.Persistence;

namespace StoryApp.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _dbContext;
    private readonly IJwtTokenService _tokenService;
    private readonly JwtOptions _jwtOptions;
    private readonly IGoogleTokenValidator _googleTokenValidator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<AppUser> userManager,
        AppDbContext dbContext,
        IJwtTokenService tokenService,
        IOptions<JwtOptions> jwtOptions,
        IGoogleTokenValidator googleTokenValidator,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _tokenService = tokenService;
        _jwtOptions = jwtOptions.Value;
        _googleTokenValidator = googleTokenValidator;
        _logger = logger;
    }

    public async Task<AuthResponseDto> CreateGuestAsync(CancellationToken cancellationToken = default)
    {
        var userId = Guid.NewGuid();
        var guestUserName = $"guest_{userId:N}";
        var now = DateTime.UtcNow;

        var user = new AppUser
        {
            Id = userId,
            UserName = guestUserName,
            AccountType = UserAccountType.Guest,
            IsActive = true,
            CreatedAt = now,
            Email = null
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogError("Failed to create guest user with Id {UserId}: {Errors}", userId, errors);
            throw new InvalidOperationException($"Failed to create guest user: {errors}");
        }

        var (rawRefreshToken, tokenHash) = _tokenService.GenerateRefreshToken();
        var refreshTokenExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = refreshTokenExpiresAt,
            RevokedAt = null,
            ReplacedByTokenId = null
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(user.Id, user.AccountType);

        return new AuthResponseDto(
            AccessToken: accessToken,
            AccessTokenExpiresAt: accessExpiresAt,
            RefreshToken: rawRefreshToken,
            RefreshTokenExpiresAt: refreshTokenExpiresAt,
            User: new UserDto(user.Id, user.AccountType.ToString()));
    }

    public async Task<AuthResponseDto?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var tokenHash = _tokenService.HashToken(refreshToken);
        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingToken = await _dbContext.RefreshTokens
            .AsNoTracking()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (existingToken == null)
        {
            _logger.LogWarning("Refresh failed: token hash not found in database.");
            return null;
        }

        if (existingToken.RevokedAt != null)
        {
            _logger.LogWarning("Refresh failed: token was already revoked at {RevokedAt}.", existingToken.RevokedAt);
            return null;
        }

        if (existingToken.ExpiresAt <= now)
        {
            _logger.LogWarning("Refresh failed: token expired at {ExpiresAt}.", existingToken.ExpiresAt);
            return null;
        }

        var user = existingToken.User ?? await _userManager.FindByIdAsync(existingToken.UserId.ToString());
        if (user == null || !user.IsActive)
        {
            _logger.LogWarning("Refresh failed: user {UserId} not found or inactive.", existingToken.UserId);
            return null;
        }

        var (newRawToken, newTokenHash) = _tokenService.GenerateRefreshToken();
        var newExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays);

        var newToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = newTokenHash,
            CreatedAt = now,
            ExpiresAt = newExpiresAt,
            RevokedAt = null,
            ReplacedByTokenId = null
        };

        // 1. Insert new token within the transaction so FK constraint on ReplacedByTokenId is satisfied
        _dbContext.RefreshTokens.Add(newToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 2. Atomic conditional update: only revoke if RevokedAt is still NULL
        var rowsUpdated = await _dbContext.RefreshTokens
            .Where(t => t.Id == existingToken.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedByTokenId, newToken.Id),
                cancellationToken);

        if (rowsUpdated == 0)
        {
            _logger.LogWarning("Refresh failed: token {TokenId} was concurrently revoked.", existingToken.Id);
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await transaction.CommitAsync(cancellationToken);

        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(user.Id, user.AccountType);

        return new AuthResponseDto(
            AccessToken: accessToken,
            AccessTokenExpiresAt: accessExpiresAt,
            RefreshToken: newRawToken,
            RefreshTokenExpiresAt: newExpiresAt,
            User: new UserDto(user.Id, user.AccountType.ToString()));
    }

    public async Task<bool> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        var tokenHash = _tokenService.HashToken(refreshToken);
        var token = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (token != null && token.RevokedAt == null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<AuthResponseDto?> UpgradeGuestWithGoogleAsync(
        Guid userId,
        string idToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var googleIdentity = await _googleTokenValidator.ValidateAsync(idToken, cancellationToken);
        if (googleIdentity == null || string.IsNullOrWhiteSpace(googleIdentity.Subject))
        {
            _logger.LogWarning("Google upgrade failed: ID token validation failed.");
            return null;
        }

        var currentUser = await _userManager.FindByIdAsync(userId.ToString());
        if (currentUser == null || !currentUser.IsActive)
        {
            _logger.LogWarning("Google upgrade failed: user {UserId} not found or inactive.", userId);
            return null;
        }

        var existingLinkedUser = await _userManager.FindByLoginAsync("Google", googleIdentity.Subject);
        if (existingLinkedUser != null)
        {
            if (existingLinkedUser.Id == userId)
            {
                if (currentUser.AccountType == UserAccountType.Registered)
                {
                    _logger.LogInformation("Google upgrade idempotent retry for user {UserId}.", userId);

                    var nowRetry = DateTime.UtcNow;

                    await _dbContext.RefreshTokens
                        .Where(t => t.UserId == currentUser.Id && t.RevokedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, nowRetry), cancellationToken);

                    var (retryRawToken, retryTokenHash) = _tokenService.GenerateRefreshToken();
                    var retryExpiresAt = nowRetry.AddDays(_jwtOptions.RefreshTokenDays);

                    var retryRefreshToken = new RefreshToken
                    {
                        Id = Guid.NewGuid(),
                        UserId = currentUser.Id,
                        TokenHash = retryTokenHash,
                        CreatedAt = nowRetry,
                        ExpiresAt = retryExpiresAt,
                        RevokedAt = null,
                        ReplacedByTokenId = null
                    };

                    _dbContext.RefreshTokens.Add(retryRefreshToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    var (retryAccessToken, retryAccessExpiresAt) = _tokenService.GenerateAccessToken(currentUser.Id, currentUser.AccountType);

                    return new AuthResponseDto(
                        AccessToken: retryAccessToken,
                        AccessTokenExpiresAt: retryAccessExpiresAt,
                        RefreshToken: retryRawToken,
                        RefreshTokenExpiresAt: retryExpiresAt,
                        User: new UserDto(currentUser.Id, currentUser.AccountType.ToString()));
                }
            }
            else
            {
                _logger.LogWarning("Google upgrade conflict: Google subject is already linked to another StoryApp account.");
                throw new ConflictException("This Google account is already linked to another StoryApp account.");
            }
        }

        if (currentUser.AccountType != UserAccountType.Guest)
        {
            _logger.LogWarning("Google upgrade conflict: user {UserId} is not a Guest account.", userId);
            throw new ConflictException("User is already registered.");
        }

        var existingLogins = await _userManager.GetLoginsAsync(currentUser);
        if (existingLogins.Any(l => l.LoginProvider == "Google"))
        {
            _logger.LogWarning("Google upgrade conflict: user {UserId} already has a Google login.", userId);
            throw new ConflictException("User is already registered.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Concurrency protection: Atomically verify user is still a Guest and transition to Registered.
            // If another concurrent request already upgraded this user, rowsUpdated will be 0.
            var rowsUpdated = await _dbContext.Users
                .Where(u => u.Id == currentUser.Id && u.AccountType == UserAccountType.Guest)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.AccountType, UserAccountType.Registered)
                    .SetProperty(u => u.UpdatedAt, DateTime.UtcNow),
                    cancellationToken);

            if (rowsUpdated == 0)
            {
                _logger.LogWarning("Google upgrade conflict: user {UserId} was concurrently upgraded or is no longer a Guest.", userId);
                throw new ConflictException("User is already registered.");
            }

            var hasGoogleLogin = await _dbContext.UserLogins
                .AnyAsync(l => l.UserId == currentUser.Id && l.LoginProvider == "Google", cancellationToken);

            if (hasGoogleLogin)
            {
                _logger.LogWarning("Google upgrade conflict: user {UserId} already has a Google login.", userId);
                throw new ConflictException("User is already registered.");
            }

            currentUser.AccountType = UserAccountType.Registered;
            currentUser.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(googleIdentity.Email) && googleIdentity.EmailVerified)
            {
                currentUser.Email = googleIdentity.Email;
                currentUser.NormalizedEmail = _userManager.NormalizeEmail(googleIdentity.Email);
                currentUser.EmailConfirmed = true;

                await _dbContext.Users
                    .Where(u => u.Id == currentUser.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.Email, currentUser.Email)
                        .SetProperty(u => u.NormalizedEmail, currentUser.NormalizedEmail)
                        .SetProperty(u => u.EmailConfirmed, true),
                        cancellationToken);
            }

            var loginResult = await _userManager.AddLoginAsync(
                currentUser,
                new UserLoginInfo("Google", googleIdentity.Subject, "Google"));

            if (!loginResult.Succeeded)
            {
                var errors = string.Join(", ", loginResult.Errors.Select(e => e.Description));
                _logger.LogError("Failed to add Google login for user {UserId}: {Errors}", userId, errors);
                throw new InvalidOperationException($"Failed to link Google account: {errors}");
            }

            var now = DateTime.UtcNow;

            // Revoke old active guest refresh tokens
            await _dbContext.RefreshTokens
                .Where(t => t.UserId == currentUser.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

            // Issue new registered refresh token
            var (rawRefreshToken, tokenHash) = _tokenService.GenerateRefreshToken();
            var refreshTokenExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays);

            var newRefreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = currentUser.Id,
                TokenHash = tokenHash,
                CreatedAt = now,
                ExpiresAt = refreshTokenExpiresAt,
                RevokedAt = null,
                ReplacedByTokenId = null
            };

            _dbContext.RefreshTokens.Add(newRefreshToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(currentUser.Id, currentUser.AccountType);

            return new AuthResponseDto(
                AccessToken: accessToken,
                AccessTokenExpiresAt: accessExpiresAt,
                RefreshToken: rawRefreshToken,
                RefreshTokenExpiresAt: refreshTokenExpiresAt,
                User: new UserDto(currentUser.Id, currentUser.AccountType.ToString()));
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch
            {
                // In case transaction was already completed or aborted
            }
            throw;
        }
    }

    public async Task<AuthResponseDto?> LoginWithGoogleAsync(
        string idToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var googleIdentity = await _googleTokenValidator.ValidateAsync(idToken, cancellationToken);
        if (googleIdentity == null || string.IsNullOrWhiteSpace(googleIdentity.Subject))
        {
            _logger.LogWarning("Google login failed: ID token validation failed.");
            return null;
        }

        var user = await _userManager.FindByLoginAsync("Google", googleIdentity.Subject);
        if (user == null)
        {
            _logger.LogWarning("Google login failed: no user linked to Google subject.");
            return null;
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Google login failed: user {UserId} is inactive.", user.Id);
            return null;
        }

        var now = DateTime.UtcNow;
        var (rawRefreshToken, tokenHash) = _tokenService.GenerateRefreshToken();
        var refreshTokenExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = refreshTokenExpiresAt,
            RevokedAt = null,
            ReplacedByTokenId = null
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(user.Id, user.AccountType);

        return new AuthResponseDto(
            AccessToken: accessToken,
            AccessTokenExpiresAt: accessExpiresAt,
            RefreshToken: rawRefreshToken,
            RefreshTokenExpiresAt: refreshTokenExpiresAt,
            User: new UserDto(user.Id, user.AccountType.ToString()));
    }
}
