using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;
using StoryApp.Infrastructure.Identity;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("UserProfiles");

        builder.HasKey(x => x.UserId);

        builder.Property(x => x.UserId)
            .ValueGeneratedNever();

        builder.Property(x => x.PreferredLanguage)
            .HasMaxLength(10);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasOne<AppUser>()
            .WithOne()
            .HasForeignKey<UserProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
