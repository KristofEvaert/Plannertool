using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class ServiceLocationOpeningHoursConfiguration : IEntityTypeConfiguration<ServiceLocationOpeningHours>
{
    public void Configure(EntityTypeBuilder<ServiceLocationOpeningHours> builder)
    {
        builder.ToTable("ServiceLocationOpeningHours");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DayOfWeek)
            .IsRequired();

        builder.HasIndex(x => new { x.ServiceLocationId, x.DayOfWeek });

        builder.HasOne(x => x.ServiceLocation)
            .WithMany(sl => sl.OpeningHours)
            .HasForeignKey(x => x.ServiceLocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
