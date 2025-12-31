using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class WeightTemplateConfiguration : IEntityTypeConfiguration<WeightTemplate>
{
    public void Configure(EntityTypeBuilder<WeightTemplate> builder)
    {
        builder.ToTable("WeightTemplates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.WeightDistance)
            .HasColumnType("decimal(18,4)");
        builder.Property(x => x.WeightTravelTime)
            .HasColumnType("decimal(18,4)");
        builder.Property(x => x.WeightOvertime)
            .HasColumnType("decimal(18,4)");
        builder.Property(x => x.WeightCost)
            .HasColumnType("decimal(18,4)");
        builder.Property(x => x.WeightDate)
            .HasColumnType("decimal(18,4)");

        builder.HasIndex(x => new { x.ScopeType, x.OwnerId, x.ServiceTypeId });
    }
}
