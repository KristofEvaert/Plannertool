using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class RouteChangeNotificationConfiguration : IEntityTypeConfiguration<RouteChangeNotification>
{
    public void Configure(EntityTypeBuilder<RouteChangeNotification> builder)
    {
        builder.ToTable("RouteChangeNotifications");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Route)
            .WithMany(r => r.ChangeNotifications)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RouteVersion)
            .WithMany(v => v.Notifications)
            .HasForeignKey(x => x.RouteVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Driver)
            .WithMany()
            .HasForeignKey(x => x.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
