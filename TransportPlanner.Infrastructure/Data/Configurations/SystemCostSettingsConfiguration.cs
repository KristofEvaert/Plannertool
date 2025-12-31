using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class SystemCostSettingsConfiguration : IEntityTypeConfiguration<SystemCostSettings>
{
    public void Configure(EntityTypeBuilder<SystemCostSettings> builder)
    {
        builder.ToTable("SystemCostSettings");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.OwnerId)
            .IsUnique()
            .HasFilter("[OwnerId] IS NOT NULL");

        builder.HasOne<ServiceLocationOwner>()
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.FuelCostPerKm)
            .HasColumnType("decimal(18,4)");

        builder.Property(x => x.PersonnelCostPerHour)
            .HasColumnType("decimal(18,4)");

        builder.Property(x => x.CurrencyCode)
            .HasMaxLength(8)
            .IsRequired();
    }
}
