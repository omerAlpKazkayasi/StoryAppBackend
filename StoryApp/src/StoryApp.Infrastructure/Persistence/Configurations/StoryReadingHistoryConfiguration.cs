using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class StoryReadingHistoryConfiguration : IEntityTypeConfiguration<StoryReadingHistory>
{
    public void Configure(EntityTypeBuilder<StoryReadingHistory> builder)
    {
        builder.ToTable("StoryReadingHistories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StepNumber)
            .IsRequired();

        builder.Property(x => x.SelectedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.ReadingSessionId, x.StepNumber })
            .IsUnique();

        builder.HasOne(x => x.ReadingSession)
            .WithMany(x => x.Histories)
            .HasForeignKey(x => x.ReadingSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<StoryNode>()
            .WithMany()
            .HasForeignKey(x => x.FromNodeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<StoryNode>()
            .WithMany()
            .HasForeignKey(x => x.ToNodeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<StoryTransition>()
            .WithMany()
            .HasForeignKey(x => x.TransitionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
