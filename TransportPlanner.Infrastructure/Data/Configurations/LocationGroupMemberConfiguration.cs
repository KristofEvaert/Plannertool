using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class LocationGroupMemberConfiguration : IEntityTypeConfiguration<LocationGroupMember>
{
    public void Configure(EntityTypeBuilder<LocationGroupMember> builder)
    {
        builder.ToTable("LocationGroupMembers");

        builder.HasKey(x => new { x.LocationGroupId, x.ServiceLocationId });

        builder.HasOne(x => x.LocationGroup)
            .WithMany(g => g.Members)
            .HasForeignKey(x => x.LocationGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ServiceLocation)
            .WithMany(sl => sl.GroupMemberships)
            .HasForeignKey(x => x.ServiceLocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
