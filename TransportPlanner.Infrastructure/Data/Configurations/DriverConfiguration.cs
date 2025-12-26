using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Drivers");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedOnAdd();

        builder.Property(d => d.ToolId)
            .IsRequired();
        
        builder.HasIndex(d => d.ToolId)
            .IsUnique();

        builder.Property(d => d.ErpId)
            .IsRequired();
        
        builder.HasIndex(d => d.ErpId)
            .IsUnique();

        builder.Property(d => d.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.StartAddress)
            .HasMaxLength(500);

        builder.Property(d => d.StartLatitude)
            .IsRequired(false);

        builder.Property(d => d.StartLongitude)
            .IsRequired(false);

        builder.Property(d => d.DefaultServiceMinutes)
            .HasDefaultValue(20);

        builder.Property(d => d.MaxWorkMinutesPerDay)
            .HasDefaultValue(480);

        builder.Property(d => d.OwnerId)
            .IsRequired();

        // No foreign key constraint - OwnerId is just an int column
        // No navigation property configured

        builder.HasIndex(d => d.OwnerId);

        builder.Property(d => d.UserId)
            .HasColumnType("uniqueidentifier");

        builder.HasIndex(d => d.UserId);

        builder.Property(d => d.IsActive)
            .HasDefaultValue(true);

        builder.Property(d => d.CreatedAtUtc)
            .IsRequired();

        builder.Property(d => d.UpdatedAtUtc)
            .IsRequired();

        builder.HasMany(d => d.Availabilities)
            .WithOne(da => da.Driver)
            .HasForeignKey(da => da.DriverId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
