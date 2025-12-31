using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class RegionSpeedProfileConfiguration : IEntityTypeConfiguration<RegionSpeedProfile>
{
    public void Configure(EntityTypeBuilder<RegionSpeedProfile> builder)
    {
        builder.ToTable("RegionSpeedProfiles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AvgMinutesPerKm)
            .HasColumnType("decimal(8,4)");

        builder.HasIndex(x => new { x.RegionId, x.DayType, x.BucketStartHour, x.BucketEndHour })
            .IsUnique();

        builder.HasOne(x => x.Region)
            .WithMany(r => r.SpeedProfiles)
            .HasForeignKey(x => x.RegionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
