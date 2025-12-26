using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TransportPlanner.Infrastructure.Data;

/// <summary>
/// Design-time factory so EF tools can create the DbContext without relying on the API project.
/// This avoids build/output locking issues when the API is running.
/// </summary>
public sealed class TransportPlannerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TransportPlannerDbContext>
{
    public TransportPlannerDbContext CreateDbContext(string[] args)
    {
        // Default to the API project's appsettings so local dev keeps working as-is.
        // When EF tools run with --project TransportPlanner.Infrastructure, the current directory is typically
        // the repo root OR TransportPlanner.Infrastructure. So we search upwards for TransportPlanner.Api/.
        var basePath = FindRepoSubDirectory("TransportPlanner.Api") ?? Directory.GetCurrentDirectory();

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<TransportPlannerDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new TransportPlannerDbContext(optionsBuilder.Options);
    }

    private static string? FindRepoSubDirectory(string directoryName)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, directoryName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        return null;
    }
}


