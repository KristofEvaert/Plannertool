using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class ServiceLocationExceptionConfiguration : IEntityTypeConfiguration<ServiceLocationException>
{
    public void Configure(EntityTypeBuilder<ServiceLocationException> builder)
    {
        builder.ToTable("ServiceLocationExceptions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Date)
            .IsRequired();

        builder.HasIndex(x => new { x.ServiceLocationId, x.Date });

        builder.HasOne(x => x.ServiceLocation)
            .WithMany(sl => sl.Exceptions)
            .HasForeignKey(x => x.ServiceLocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
