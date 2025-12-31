using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class DriverServiceTypeConfiguration : IEntityTypeConfiguration<DriverServiceType>
{
    public void Configure(EntityTypeBuilder<DriverServiceType> builder)
    {
        builder.ToTable("DriverServiceTypes");

        builder.HasKey(dst => new { dst.DriverId, dst.ServiceTypeId });

        builder.Property(dst => dst.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(dst => dst.Driver)
            .WithMany(d => d.DriverServiceTypes)
            .HasForeignKey(dst => dst.DriverId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dst => dst.ServiceType)
            .WithMany(st => st.DriverServiceTypes)
            .HasForeignKey(dst => dst.ServiceTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
