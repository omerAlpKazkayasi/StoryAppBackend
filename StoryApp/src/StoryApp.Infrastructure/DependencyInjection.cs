using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryApp.Infrastructure.Persistence;

namespace StoryApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddIdentityCore<Identity.AppUser>(options =>
        {
            options.User.RequireUniqueEmail = false;
        })
        .AddEntityFrameworkStores<AppDbContext>();

        services.Configure<Auth.JwtOptions>(configuration.GetSection(Auth.JwtOptions.SectionName));
        services.Configure<Auth.GoogleAuthOptions>(configuration.GetSection(Auth.GoogleAuthOptions.SectionName));

        services.AddScoped<Auth.IJwtTokenService, Auth.JwtTokenService>();
        services.AddScoped<Application.Auth.IGoogleTokenValidator, Auth.GoogleTokenValidator>();
        services.AddScoped<Application.Auth.IAuthService, Auth.AuthService>();
        services.AddScoped<Application.Themes.IThemeService, Themes.ThemeService>();
        services.AddScoped<Application.Reading.IPlayService, Reading.PlayService>();
        services.AddScoped<Application.Reading.IReadingService, Reading.ReadingService>();
        services.AddScoped<Application.Profile.IProfileService, Profile.ProfileService>();

        return services;
    }
}
