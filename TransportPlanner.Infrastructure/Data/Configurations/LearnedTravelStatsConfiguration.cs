using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class LearnedTravelStatsConfiguration : IEntityTypeConfiguration<LearnedTravelStats>
{
    public void Configure(EntityTypeBuilder<LearnedTravelStats> builder)
    {
        builder.ToTable("LearnedTravelStats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DistanceBandKmMin).HasColumnType("decimal(8,2)");
        builder.Property(x => x.DistanceBandKmMax).HasColumnType("decimal(8,2)");
        builder.Property(x => x.AvgMinutesPerKm).HasColumnType("decimal(8,4)");
        builder.Property(x => x.AvgStopServiceMinutes).HasColumnType("decimal(8,2)");
        builder.Property(x => x.MinMinutesPerKm).HasColumnType("decimal(8,4)");
        builder.Property(x => x.MaxMinutesPerKm).HasColumnType("decimal(8,4)");

        builder.HasIndex(x => new
            {
                x.RegionId,
                x.DayType,
                x.BucketStartHour,
                x.BucketEndHour,
                x.DistanceBandKmMin,
                x.DistanceBandKmMax
            })
            .IsUnique();

        builder.HasOne(x => x.Region)
            .WithMany(r => r.LearnedStats)
            .HasForeignKey(x => x.RegionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
