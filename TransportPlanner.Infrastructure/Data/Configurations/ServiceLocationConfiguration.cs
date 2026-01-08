using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class ServiceLocationConfiguration : IEntityTypeConfiguration<ServiceLocation>
{
    public void Configure(EntityTypeBuilder<ServiceLocation> builder)
    {
        builder.ToTable("ServiceLocations");

        builder.HasKey(sl => sl.Id);

        builder.Property(sl => sl.Id)
            .ValueGeneratedOnAdd();

        builder.Property(sl => sl.ToolId)
            .IsRequired();
        
        builder.HasIndex(sl => sl.ToolId)
            .IsUnique();

        builder.Property(sl => sl.ErpId)
            .IsRequired(false);
        
        builder.HasIndex(sl => sl.ErpId)
            .IsUnique()
            .HasFilter("[ErpId] IS NOT NULL");

        builder.Property(sl => sl.AccountId)
            .HasMaxLength(100);

        builder.Property(sl => sl.SerialNumber)
            .HasMaxLength(100);

        builder.Property(sl => sl.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(sl => sl.Address)
            .HasMaxLength(500);

        builder.Property(sl => sl.Latitude)
            .IsRequired(false);

        builder.Property(sl => sl.Longitude)
            .IsRequired(false);

        builder.Property(sl => sl.DueDate)
            .IsRequired()
            .HasConversion(
                v => v.Date, // Store only date part
                v => v.Date);

        builder.Property(sl => sl.PriorityDate)
            .HasConversion(
                v => v.HasValue ? v.Value.Date : (DateTime?)null,
                v => v.HasValue ? v.Value.Date : (DateTime?)null);

        builder.Property(sl => sl.ServiceMinutes)
            .HasDefaultValue(20);

        builder.Property(sl => sl.ServiceTypeId)
            .IsRequired();

        // No foreign key constraint - ServiceTypeId is just an int column
        // No navigation property configured

        builder.Property(sl => sl.OwnerId)
            .IsRequired();

        // No foreign key constraint - OwnerId is just an int column
        // No navigation property configured

        builder.Property(sl => sl.DriverInstruction)
            .HasMaxLength(1000);

        builder.Property(sl => sl.ExtraInstructions)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

        builder.HasIndex(sl => new { sl.ServiceTypeId, sl.Status, sl.DueDate });
        builder.HasIndex(sl => new { sl.OwnerId, sl.Status, sl.DueDate });

        builder.Property(sl => sl.Status)
            .HasConversion<int>()
            .HasDefaultValue(ServiceLocationStatus.Open);

        builder.Property(sl => sl.IsActive)
            .HasDefaultValue(true);

        builder.Property(sl => sl.CreatedAtUtc)
            .IsRequired();

        builder.Property(sl => sl.UpdatedAtUtc)
            .IsRequired();
    }
}

