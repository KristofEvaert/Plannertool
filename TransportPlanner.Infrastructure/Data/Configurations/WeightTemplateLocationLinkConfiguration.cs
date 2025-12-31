using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class WeightTemplateLocationLinkConfiguration : IEntityTypeConfiguration<WeightTemplateLocationLink>
{
    public void Configure(EntityTypeBuilder<WeightTemplateLocationLink> builder)
    {
        builder.ToTable("WeightTemplateLocationLinks");

        builder.HasKey(x => new { x.WeightTemplateId, x.ServiceLocationId });

        builder.HasOne(x => x.WeightTemplate)
            .WithMany(t => t.LocationLinks)
            .HasForeignKey(x => x.WeightTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ServiceLocation)
            .WithMany(sl => sl.WeightTemplateLinks)
            .HasForeignKey(x => x.ServiceLocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
