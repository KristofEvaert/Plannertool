using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TransportPlanner.Application.Services;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Options;
using TransportPlanner.Infrastructure.Seeding;
using TransportPlanner.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TransportPlanner.Infrastructure.Data;
using System;
using TransportPlanner.Infrastructure.Services.Vrp;

namespace TransportPlanner.Infrastructure;

/// <summary>
/// Extension methods for dependency injection configuration in Infrastructure layer.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds Infrastructure services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        // Identity + roles
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<TransportPlannerDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<OpenStreetMapOptions>(configuration.GetSection(OpenStreetMapOptions.SectionName));

        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // Configure TravelTime options (moved to _legacy)
        // services.Configure<TravelTimeOptions>(
        //     configuration.GetSection(TravelTimeOptions.SectionName));

        // Register TravelTime service (moved to _legacy)
        // services.AddScoped<ITravelTimeService, DummyTravelTimeService>();

        // Register DatabaseSeeder
        services.AddScoped<DatabaseSeeder>();

        // Register bulk upsert service
        services.AddScoped<DriverBulkUpsertService>();
        
        // Register bulk insert service for service locations
        services.AddScoped<ServiceLocationBulkInsertService>();
        services.AddScoped<ITravelTimeModelService, TravelTimeModelService>();
        services.AddScoped<IVrpInputBuilder, VrpInputBuilder>();
        services.AddScoped<IVrpResultMapper, VrpResultMapper>();
        services.AddScoped<IVrpRouteSolverService, VrpRouteSolverService>();

        services.AddHttpClient<IGeocodingService, OpenStreetMapGeocodingService>(client =>
        {
            client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Register new scheduling services - DISABLED (planning functionality removed)
        // services.AddScoped<ITravelTimeService, TravelTimeService>();
        // services.AddScoped<IPlanningUnitService, PlanningUnitService>();
        // services.AddScoped<IScheduleGenerationService, ScheduleGenerationService>();
        // services.AddScoped<IClusteringService, ClusteringService>();
        // services.AddScoped<IDayLockService, DayLockService>();
        // services.AddScoped<IManualAssignmentService, ManualAssignmentService>();

        // Configure Planning options
        services.Configure<PlanningOptions>(
            configuration.GetSection(PlanningOptions.SectionName));

        // Register route planner based on configuration
        var planningOptions = configuration.GetSection(PlanningOptions.SectionName).Get<PlanningOptions>() ?? new PlanningOptions();
        var engine = planningOptions.Engine?.Trim() ?? "Greedy";

        if (string.Equals(engine, "OrTools", StringComparison.OrdinalIgnoreCase))
        {
            // Planning services moved to _legacy
        // services.AddScoped<IRoutePlanner, OrToolsRoutePlanner>();
        }
        else
        {
            // services.AddScoped<IRoutePlanner, GreedyRoutePlanner>();
        }

        // Register plan services (moved to _legacy for now)
        // services.AddScoped<IPlanQueries, PlanQueries>();
        // services.AddScoped<IPlanLockService, PlanLockService>();
        // services.AddScoped<IDriverPlanQueries, DriverPlanQueries>();
        // services.AddScoped<IPoleSchedulingService, PoleSchedulingService>();
        // services.AddScoped<IPlanGenerationService, PlanGenerationService>();
        // services.AddScoped<IPoleImportService, PoleImportService>();
        // services.AddScoped<IRouteExecutionService, RouteExecutionService>();
        // services.AddScoped<IPoleQueries, PoleQueries>();
        // services.AddScoped<IDriverQueries, DriverQueries>();
        // services.AddScoped<IDriverAdminService, DriverAdminService>();
        // services.AddScoped<IPlanDaySettingsService, PlanDaySettingsService>();

        // Register HttpClient for GeocodingService (moved to _legacy)
        // services.AddHttpClient<IGeocodingService, GeocodingService>(client =>
        // {
        //     client.Timeout = TimeSpan.FromSeconds(5);
        // });

        return services;
    }
}

