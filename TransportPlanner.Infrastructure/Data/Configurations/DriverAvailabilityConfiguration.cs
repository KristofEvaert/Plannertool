using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class DriverAvailabilityConfiguration : IEntityTypeConfiguration<DriverAvailability>
{
    public void Configure(EntityTypeBuilder<DriverAvailability> builder)
    {
        builder.ToTable("DriverAvailabilities");

        builder.HasKey(da => da.Id);

        builder.Property(da => da.Id)
            .ValueGeneratedOnAdd();

        builder.Property(da => da.DriverId)
            .IsRequired();

        builder.Property(da => da.Date)
            .IsRequired()
            .HasConversion(
                v => v.Date, // Store only date part
                v => v);

        builder.Property(da => da.StartMinuteOfDay)
            .IsRequired();

        builder.Property(da => da.EndMinuteOfDay)
            .IsRequired();

        builder.Property(da => da.CreatedAtUtc)
            .IsRequired();

        builder.Property(da => da.UpdatedAtUtc)
            .IsRequired();

        // Unique index: one availability per driver per date
        builder.HasIndex(da => new { da.DriverId, da.Date })
            .IsUnique();

        builder.HasOne(da => da.Driver)
            .WithMany(d => d.Availabilities)
            .HasForeignKey(da => da.DriverId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
