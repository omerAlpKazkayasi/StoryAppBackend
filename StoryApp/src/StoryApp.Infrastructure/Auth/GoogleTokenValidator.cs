using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryApp.Application.Auth;

namespace StoryApp.Infrastructure.Auth;

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly GoogleAuthOptions _options;
    private readonly ILogger<GoogleTokenValidator> _logger;

    public GoogleTokenValidator(
        IOptions<GoogleAuthOptions> options,
        ILogger<GoogleTokenValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = _options.ClientIds
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            if (payload == null || string.IsNullOrWhiteSpace(payload.Subject))
            {
                return null;
            }

            return new GoogleIdentity(
                Subject: payload.Subject,
                Email: payload.Email,
                EmailVerified: payload.EmailVerified);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning("Google ID token validation failed: Invalid JWT. Reason: {Reason}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Google ID token validation failed with error. Reason: {Reason}", ex.Message);
            return null;
        }
    }
}
