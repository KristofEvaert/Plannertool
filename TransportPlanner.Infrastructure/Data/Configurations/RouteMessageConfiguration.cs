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

        builder.HasOne(x => x.Driver)
            .WithMany()
            .HasForeignKey(x => x.DriverId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.RouteId);
        builder.HasIndex(x => x.RouteStopId);
    }
}
