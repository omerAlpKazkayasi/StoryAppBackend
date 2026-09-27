using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class StoryReadingSessionConfiguration : IEntityTypeConfiguration<StoryReadingSession>
{
    public void Configure(EntityTypeBuilder<StoryReadingSession> builder)
    {
        builder.ToTable("StoryReadingSessions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CurrentStep)
            .HasDefaultValue(0);

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.LastReadAt)
            .IsRequired();

        builder.HasIndex(x => new { x.UserId, x.Status });
        builder.HasIndex(x => new { x.UserId, x.StoryId });

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.CurrentNode)
            .WithMany()
            .HasForeignKey(x => x.CurrentNodeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(x => x.Histories)
            .WithOne(x => x.ReadingSession)
            .HasForeignKey(x => x.ReadingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Identity.AppUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
