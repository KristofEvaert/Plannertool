using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable("ServiceTypes");

        builder.HasKey(st => st.Id);

        builder.Property(st => st.Id)
            .ValueGeneratedOnAdd();

        builder.Property(st => st.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(st => st.Code)
            .IsUnique();

        builder.Property(st => st.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(st => st.Description)
            .HasMaxLength(1000);

        builder.Property(st => st.IsActive)
            .HasDefaultValue(true);

        builder.Property(st => st.CreatedAtUtc)
            .IsRequired();

        builder.Property(st => st.UpdatedAtUtc)
            .IsRequired();
    }
}

