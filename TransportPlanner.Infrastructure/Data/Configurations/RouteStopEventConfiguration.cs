using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class RouteStopEventConfiguration : IEntityTypeConfiguration<RouteStopEvent>
{
    public void Configure(EntityTypeBuilder<RouteStopEvent> builder)
    {
        builder.ToTable("RouteStopEvents");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.RouteStop)
            .WithMany(s => s.Events)
            .HasForeignKey(x => x.RouteStopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
