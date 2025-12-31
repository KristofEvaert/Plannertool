using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class RouteMessageConfiguration : IEntityTypeConfiguration<RouteMessage>
{
    public void Configure(EntityTypeBuilder<RouteMessage> builder)
    {
        builder.ToTable("RouteMessages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MessageText)
            .HasMaxLength(2000)
            .IsRequired();

        builder.HasOne(x => x.Route)
            .WithMany(r => r.Messages)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RouteStop)
            .WithMany()
            .HasForeignKey(x => x.RouteStopId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Driver)
            .WithMany()
            .HasForeignKey(x => x.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
