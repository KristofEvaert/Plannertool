using System.Globalization;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Seeding;

internal static class TravelTimeSeedParser
{
    public static List<TravelTimeRegion> ParseRegions(string csv)
    {
        var regions = new List<TravelTimeRegion>();
        var lines = SplitLines(csv);
        if (lines.Count <= 1)
        {
            return regions;
        }

        for (var i = 1; i < lines.Count; i++)
        {
            var parts = SplitCsvLine(lines[i]);
            if (parts.Count < 8)
            {
                continue;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            if (!decimal.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var minLat)
                || !decimal.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var minLon)
                || !decimal.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLat)
                || !decimal.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLon)
                || !int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority))
            {
                continue;
            }

            regions.Add(new TravelTimeRegion
            {
                Id = id,
                Name = parts[1],
                CountryCode = parts[2],
                BboxMinLat = minLat,
                BboxMinLon = minLon,
                BboxMaxLat = maxLat,
                BboxMaxLon = maxLon,
                Priority = priority
            });
        }

        return regions;
    }

    public static List<RegionSpeedProfile> ParseSpeedProfiles(string csv)
    {
        var profiles = new List<RegionSpeedProfile>();
        var lines = SplitLines(csv);
        if (lines.Count <= 1)
        {
            return profiles;
        }

        for (var i = 1; i < lines.Count; i++)
        {
            var parts = SplitCsvLine(lines[i]);
            if (parts.Count < 5)
            {
                continue;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var regionId))
            {
                continue;
            }

            if (!Enum.TryParse<DayType>(parts[1], ignoreCase: true, out var dayType))
            {
                continue;
            }

            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bucketStart)
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bucketEnd)
                || !decimal.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var avg))
            {
                continue;
            }

            profiles.Add(new RegionSpeedProfile
            {
                RegionId = regionId,
                DayType = dayType,
                BucketStartHour = bucketStart,
                BucketEndHour = bucketEnd,
                AvgMinutesPerKm = avg
            });
        }

        return profiles;
    }

    private static List<string> SplitLines(string csv)
    {
        return csv
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static List<string> SplitCsvLine(string line)
    {
        return line
            .Split(',', StringSplitOptions.TrimEntries)
            .ToList();
    }
}
