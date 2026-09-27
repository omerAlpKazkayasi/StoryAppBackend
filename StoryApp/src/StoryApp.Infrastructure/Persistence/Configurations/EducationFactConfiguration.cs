using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class EducationFactConfiguration : IEntityTypeConfiguration<EducationFact>
{
    public void Configure(EntityTypeBuilder<EducationFact> builder)
    {
        builder.ToTable("EducationFacts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Topic)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(x => x.Fact)
            .IsRequired();

        builder.Property(x => x.UsageHint)
            .HasMaxLength(1000);

        builder.HasIndex(x => x.StoryUniverseId);
        builder.HasIndex(x => x.RegionId);

        builder.HasOne(x => x.StoryUniverse)
            .WithMany(x => x.EducationFacts)
            .HasForeignKey(x => x.StoryUniverseId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Region)
            .WithMany(x => x.EducationFacts)
            .HasForeignKey(x => x.RegionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
