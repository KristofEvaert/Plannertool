using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Infrastructure.Data;

public class TransportPlannerDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public TransportPlannerDbContext(DbContextOptions<TransportPlannerDbContext> options)
        : base(options)
    {
    }

    // Active entities (used in simplified application)
    public DbSet<Driver> Drivers { get; set; }
    public DbSet<DriverAvailability> DriverAvailabilities { get; set; }
    public DbSet<DriverServiceType> DriverServiceTypes { get; set; }
    public DbSet<ServiceLocation> ServiceLocations { get; set; }
    public DbSet<ServiceType> ServiceTypes { get; set; }
    public DbSet<ServiceLocationOwner> ServiceLocationOwners { get; set; }
    
    // Planning entities - REMOVED (no longer used after simplification)
    // These can be dropped from database via migration
    // However, keeping DbSets for now as controllers still reference them
    public DbSet<PlanningCluster> PlanningClusters { get; set; }
    public DbSet<PlanningClusterItem> PlanningClusterItems { get; set; }
    public DbSet<Route> Routes { get; set; }
    public DbSet<RouteStop> RouteStops { get; set; }
    public DbSet<RouteStopEvent> RouteStopEvents { get; set; }
    public DbSet<RouteVersion> RouteVersions { get; set; }
    public DbSet<RouteChangeNotification> RouteChangeNotifications { get; set; }
    public DbSet<RouteMessage> RouteMessages { get; set; }
    public DbSet<DayPlanLock> DayPlanLocks { get; set; }
    public DbSet<DriverDayOverride> DriverDayOverrides { get; set; }
    public DbSet<ServiceLocationOpeningHours> ServiceLocationOpeningHours { get; set; }
    public DbSet<ServiceLocationException> ServiceLocationExceptions { get; set; }
    public DbSet<ServiceLocationConstraint> ServiceLocationConstraints { get; set; }
    public DbSet<SystemCostSettings> SystemCostSettings { get; set; }
    public DbSet<WeightTemplate> WeightTemplates { get; set; }
    public DbSet<WeightTemplateLocationLink> WeightTemplateLocationLinks { get; set; }
    public DbSet<LocationGroup> LocationGroups { get; set; }
    public DbSet<LocationGroupMember> LocationGroupMembers { get; set; }
    public DbSet<TravelTimeRegion> TravelTimeRegions { get; set; }
    public DbSet<RegionSpeedProfile> RegionSpeedProfiles { get; set; }
    public DbSet<LearnedTravelStats> LearnedTravelStats { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportPlannerDbContext).Assembly);
    }
}

