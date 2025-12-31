using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class RouteVersionConfiguration : IEntityTypeConfiguration<RouteVersion>
{
    public void Configure(EntityTypeBuilder<RouteVersion> builder)
    {
        builder.ToTable("RouteVersions");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.RouteId, x.VersionNumber }).IsUnique();

        builder.HasOne(x => x.Route)
            .WithMany(r => r.Versions)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
