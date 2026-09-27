using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class StoryNodeConfiguration : IEntityTypeConfiguration<StoryNode>
{
    public void Configure(EntityTypeBuilder<StoryNode> builder)
    {
        builder.ToTable("StoryNodes", t =>
        {
            t.HasCheckConstraint("CK_StoryNodes_PartNumber", "[PartNumber] > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(x => x.PartNumber)
            .IsRequired();

        builder.Property(x => x.IsEnding)
            .IsRequired();

        builder.HasIndex(x => new { x.StoryId, x.PartNumber })
            .IsUnique();

        builder.HasOne(x => x.Story)
            .WithMany(x => x.Nodes)
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
