using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class ServiceLocationOwnerConfiguration : IEntityTypeConfiguration<ServiceLocationOwner>
{
    public void Configure(EntityTypeBuilder<ServiceLocationOwner> builder)
    {
        builder.ToTable("ServiceLocationOwners");

        builder.HasKey(so => so.Id);

        builder.Property(so => so.Id)
            .ValueGeneratedOnAdd();

        builder.Property(so => so.Code)
            .IsRequired()
            .HasMaxLength(60);

        builder.HasIndex(so => so.Code)
            .IsUnique();

        builder.Property(so => so.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(so => so.IsActive)
            .HasDefaultValue(true);

        builder.Property(so => so.CreatedAtUtc)
            .IsRequired();

        builder.Property(so => so.UpdatedAtUtc)
            .IsRequired();
    }
}

