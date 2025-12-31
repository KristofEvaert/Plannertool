using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class ServiceLocationConstraintConfiguration : IEntityTypeConfiguration<ServiceLocationConstraint>
{
    public void Configure(EntityTypeBuilder<ServiceLocationConstraint> builder)
    {
        builder.ToTable("ServiceLocationConstraints");

        builder.HasKey(x => x.ServiceLocationId);

        builder.HasOne(x => x.ServiceLocation)
            .WithOne(sl => sl.Constraint)
            .HasForeignKey<ServiceLocationConstraint>(x => x.ServiceLocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
