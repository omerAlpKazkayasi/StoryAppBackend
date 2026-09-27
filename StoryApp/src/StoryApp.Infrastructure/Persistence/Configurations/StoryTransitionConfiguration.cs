using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class StoryTransitionConfiguration : IEntityTypeConfiguration<StoryTransition>
{
    public void Configure(EntityTypeBuilder<StoryTransition> builder)
    {
        builder.ToTable("StoryTransitions", t =>
        {
            t.HasCheckConstraint("CK_StoryTransitions_DifferentNodes", "[ToNodeId] IS NULL OR [FromNodeId] <> [ToNodeId]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ChoiceTitle)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(x => x.ChoiceIntent)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.SortOrder)
            .IsRequired();

        builder.Property(x => x.NextTargetMomentum)
            .IsRequired();

        builder.HasIndex(x => new { x.FromNodeId, x.SortOrder })
            .IsUnique();

        builder.HasIndex(x => x.ToNodeId);

        builder.HasOne(x => x.FromNode)
            .WithMany(x => x.OutgoingTransitions)
            .HasForeignKey(x => x.FromNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ToNode)
            .WithMany(x => x.IncomingTransitions)
            .HasForeignKey(x => x.ToNodeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
