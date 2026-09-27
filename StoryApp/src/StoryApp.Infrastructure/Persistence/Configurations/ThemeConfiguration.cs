using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class ThemeConfiguration : IEntityTypeConfiguration<Theme>
{
    public void Configure(EntityTypeBuilder<Theme> builder)
    {
        builder.ToTable("Themes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        builder.Property(x => x.Icon)
            .HasMaxLength(250);

        builder.Property(x => x.Color)
            .HasMaxLength(50);

        builder.Property(x => x.ImagePath)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.IsActive, x.SortOrder });

        builder.HasMany(x => x.Universes)
            .WithOne(x => x.Theme)
            .HasForeignKey(x => x.ThemeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
