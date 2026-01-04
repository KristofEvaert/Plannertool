using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class LearnedTravelStatContributorConfiguration : IEntityTypeConfiguration<LearnedTravelStatContributor>
{
    public void Configure(EntityTypeBuilder<LearnedTravelStatContributor> builder)
    {
        builder.ToTable("LearnedTravelStatContributors");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.LearnedTravelStatsId, x.DriverId })
            .IsUnique();

        builder.HasOne(x => x.LearnedTravelStats)
            .WithMany(s => s.Contributors)
            .HasForeignKey(x => x.LearnedTravelStatsId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Driver)
            .WithMany()
            .HasForeignKey(x => x.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
