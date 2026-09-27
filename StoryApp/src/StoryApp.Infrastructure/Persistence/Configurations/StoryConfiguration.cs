using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.ToTable("Stories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Language)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(x => x.Title)
            .HasMaxLength(250);

        builder.Property(x => x.FailureReason)
            .HasMaxLength(2000);

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.ChildAge)
            .IsRequired();

        builder.HasIndex(x => new { x.UniverseId, x.Status });

        builder.HasOne(x => x.Universe)
            .WithMany(x => x.Stories)
            .HasForeignKey(x => x.UniverseId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(x => x.Nodes)
            .WithOne(x => x.Story)
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.RootNode)
            .WithMany()
            .HasForeignKey(x => x.RootNodeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
