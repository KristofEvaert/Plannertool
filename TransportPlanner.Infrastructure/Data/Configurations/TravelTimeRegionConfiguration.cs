using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class TravelTimeRegionConfiguration : IEntityTypeConfiguration<TravelTimeRegion>
{
    public void Configure(EntityTypeBuilder<TravelTimeRegion> builder)
    {
        builder.ToTable("TravelTimeRegions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.CountryCode)
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(x => x.BboxMinLat).HasColumnType("decimal(9,6)");
        builder.Property(x => x.BboxMinLon).HasColumnType("decimal(9,6)");
        builder.Property(x => x.BboxMaxLat).HasColumnType("decimal(9,6)");
        builder.Property(x => x.BboxMaxLon).HasColumnType("decimal(9,6)");
    }
}
