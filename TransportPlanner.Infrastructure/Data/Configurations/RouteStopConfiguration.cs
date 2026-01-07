using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Data.Configurations;

public class RouteStopConfiguration : IEntityTypeConfiguration<RouteStop>
{
    public void Configure(EntityTypeBuilder<RouteStop> builder)
    {
        builder.Property(rs => rs.ChecklistItems)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<RouteStopChecklistItem>()
                    : JsonSerializer.Deserialize<List<RouteStopChecklistItem>>(v, (JsonSerializerOptions?)null) ??
                      new List<RouteStopChecklistItem>());

        builder.Property(rs => rs.ProofPhotoContentType)
            .HasMaxLength(100);

        builder.Property(rs => rs.ProofSignatureContentType)
            .HasMaxLength(100);
    }
}
