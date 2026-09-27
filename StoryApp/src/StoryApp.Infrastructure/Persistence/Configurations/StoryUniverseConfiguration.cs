using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class StoryUniverseConfiguration : IEntityTypeConfiguration<StoryUniverse>
{
    public void Configure(EntityTypeBuilder<StoryUniverse> builder)
    {
        builder.ToTable("StoryUniverses", t =>
        {
            t.HasCheckConstraint("CK_StoryUniverses_AgeRange", "[MinimumAge] >= 0 AND [MaximumAge] >= [MinimumAge]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.Tone)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.ThemeId, x.IsActive });

        builder.HasOne(x => x.Theme)
            .WithMany(x => x.Universes)
            .HasForeignKey(x => x.ThemeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
