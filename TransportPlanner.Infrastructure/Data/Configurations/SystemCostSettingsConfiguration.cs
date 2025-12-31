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

        builder.Property(x => x.FuelCostPerKm)
            .HasColumnType("decimal(18,4)");

        builder.Property(x => x.PersonnelCostPerHour)
            .HasColumnType("decimal(18,4)");

        builder.Property(x => x.CurrencyCode)
            .HasMaxLength(8)
            .IsRequired();
    }
}
