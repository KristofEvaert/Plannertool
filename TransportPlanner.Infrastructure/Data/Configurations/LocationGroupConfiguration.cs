using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class LocationGroupConfiguration : IEntityTypeConfiguration<LocationGroup>
{
    public void Configure(EntityTypeBuilder<LocationGroup> builder)
    {
        builder.ToTable("LocationGroups");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();
    }
}
