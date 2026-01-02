using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Options;
using TransportPlanner.Infrastructure.Services.Vrp;
using Xunit;

namespace TransportPlanner.Tests;

public class VrpRouteSolverTests
{
    [Fact]
    public async Task SolveDay_AssignsStopsToNearestDrivers_WhenDistanceDominates()
    {
        await using var db = CreateDbContext();

        var driverA = CreateDriver(1, "Driver A", 0, 0);
        var driverB = CreateDriver(2, "Driver B", 100, 0);
        db.Drivers.AddRange(driverA, driverB);

        db.DriverAvailabilities.AddRange(
            CreateAvailability(driverA.Id, new DateTime(2026, 1, 5), 8 * 60, 17 * 60),
            CreateAvailability(driverB.Id, new DateTime(2026, 1, 5), 8 * 60, 17 * 60));

        var job1 = CreateLocation(10, "Job 1", 1, 0);
        var job2 = CreateLocation(11, "Job 2", 99, 0);
        var job3 = CreateLocation(12, "Job 3", 100, 1);
        db.ServiceLocations.AddRange(job1, job2, job3);

        await db.SaveChangesAsync();

        var solver = CreateSolver(db, new FakeMatrixProvider());

        var result = await solver.SolveDayAsync(
            new VrpSolveRequest(
                new DateTime(2026, 1, 5),
                ownerId: 1,
                serviceLocationToolIds: null,
                maxStopsPerDriver: null,
                new VrpWeightSet(0, 100, 0, 0, 0),
                new VrpCostSettings(0m, 0m, "EUR"),
                requireServiceTypeMatch: false,
                normalizeWeights: true,
                weightTemplateId: null),
            CancellationToken.None);

        Assert.Equal(2, result.Routes.Count);

        var routeA = result.Routes.Single(r => r.DriverId == driverA.Id);
        var routeB = result.Routes.Single(r => r.DriverId == driverB.Id);

        Assert.Contains(job1.Id, routeA.Stops.Select(s => s.ServiceLocationId));
        Assert.DoesNotContain(job1.Id, routeB.Stops.Select(s => s.ServiceLocationId));
    }

    [Fact]
    public async Task SolveDay_RespectsLunchBreakWindow()
    {
        await using var db = CreateDbContext();

        var driver = CreateDriver(1, "Driver A", 0, 0);
        db.Drivers.Add(driver);

        db.DriverAvailabilities.Add(
            CreateAvailability(driver.Id, new DateTime(2026, 1, 6), 12 * 60 + 30, 17 * 60));

        var job = CreateLocation(20, "Job 1", 0, 0);
        db.ServiceLocations.Add(job);

        db.ServiceLocationOpeningHours.Add(new ServiceLocationOpeningHours
        {
            ServiceLocationId = job.Id,
            DayOfWeek = (int)DayOfWeek.Tuesday,
            OpenTime = new TimeSpan(9, 0, 0),
            CloseTime = new TimeSpan(12, 0, 0),
            OpenTime2 = new TimeSpan(13, 0, 0),
            CloseTime2 = new TimeSpan(17, 0, 0),
            IsClosed = false
        });

        await db.SaveChangesAsync();

        var solver = CreateSolver(db, new FakeMatrixProvider());

        var result = await solver.SolveDayAsync(
            new VrpSolveRequest(
                new DateTime(2026, 1, 6),
                ownerId: 1,
                serviceLocationToolIds: null,
                maxStopsPerDriver: null,
                new VrpWeightSet(100, 0, 0, 0, 0),
                new VrpCostSettings(0m, 0m, "EUR"),
                requireServiceTypeMatch: false,
                normalizeWeights: true,
                weightTemplateId: null),
            CancellationToken.None);

        var stop = await db.RouteStops.FirstAsync();
        Assert.NotNull(stop.PlannedStart);
        var plannedMinute = stop.PlannedStart!.Value.Hour * 60 + stop.PlannedStart.Value.Minute;
        Assert.True(plannedMinute >= 13 * 60, $"Expected stop to be scheduled after lunch break. Actual minute: {plannedMinute}");
    }

    [Fact]
    public async Task SolveDay_ExcludesJobsWithNoMatchingServiceType()
    {
        await using var db = CreateDbContext();

        var driver = CreateDriver(1, "Driver A", 0, 0);
        db.Drivers.Add(driver);
        db.DriverServiceTypes.Add(new DriverServiceType
        {
            DriverId = driver.Id,
            ServiceTypeId = 1,
            CreatedAtUtc = DateTime.UtcNow
        });

        db.DriverAvailabilities.Add(
            CreateAvailability(driver.Id, new DateTime(2026, 1, 7), 8 * 60, 17 * 60));

        var job = CreateLocation(30, "Job 1", 1, 1);
        job.ServiceTypeId = 2;
        db.ServiceLocations.Add(job);

        await db.SaveChangesAsync();

        var solver = CreateSolver(db, new FakeMatrixProvider());

        var result = await solver.SolveDayAsync(
            new VrpSolveRequest(
                new DateTime(2026, 1, 7),
                ownerId: 1,
                serviceLocationToolIds: null,
                maxStopsPerDriver: null,
                new VrpWeightSet(100, 0, 0, 0, 0),
                new VrpCostSettings(0m, 0m, "EUR"),
                requireServiceTypeMatch: true,
                normalizeWeights: true,
                weightTemplateId: null),
            CancellationToken.None);

        Assert.Empty(result.Routes);
        Assert.Contains(job.Id, result.UnassignedServiceLocationIds);
    }

    [Fact]
    public async Task SolveDay_ExcludesClosedLocations()
    {
        await using var db = CreateDbContext();

        var driver = CreateDriver(1, "Driver A", 0, 0);
        db.Drivers.Add(driver);
        db.DriverAvailabilities.Add(
            CreateAvailability(driver.Id, new DateTime(2026, 1, 8), 8 * 60, 17 * 60));

        var job = CreateLocation(40, "Job 1", 2, 2);
        db.ServiceLocations.Add(job);

        db.ServiceLocationExceptions.Add(new ServiceLocationException
        {
            ServiceLocationId = job.Id,
            Date = new DateTime(2026, 1, 8),
            IsClosed = true
        });

        await db.SaveChangesAsync();

        var solver = CreateSolver(db, new FakeMatrixProvider());

        var result = await solver.SolveDayAsync(
            new VrpSolveRequest(
                new DateTime(2026, 1, 8),
                ownerId: 1,
                serviceLocationToolIds: null,
                maxStopsPerDriver: null,
                new VrpWeightSet(100, 0, 0, 0, 0),
                new VrpCostSettings(0m, 0m, "EUR"),
                requireServiceTypeMatch: false,
                normalizeWeights: true,
                weightTemplateId: null),
            CancellationToken.None);

        Assert.Empty(result.Routes);
        Assert.Contains(job.Id, result.UnassignedServiceLocationIds);
    }

    private static TransportPlannerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TransportPlannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TransportPlannerDbContext(options);
    }

    private static Driver CreateDriver(int id, string name, double lat, double lng)
    {
        return new Driver
        {
            Id = id,
            ToolId = Guid.NewGuid(),
            ErpId = id,
            Name = name,
            StartLatitude = lat,
            StartLongitude = lng,
            OwnerId = 1,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static DriverAvailability CreateAvailability(int driverId, DateTime date, int startMinute, int endMinute)
    {
        return new DriverAvailability
        {
            DriverId = driverId,
            Date = date.Date,
            StartMinuteOfDay = startMinute,
            EndMinuteOfDay = endMinute,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static ServiceLocation CreateLocation(int id, string name, double lat, double lng)
    {
        return new ServiceLocation
        {
            Id = id,
            ToolId = Guid.NewGuid(),
            ErpId = id,
            Name = name,
            Address = "Test",
            Latitude = lat,
            Longitude = lng,
            DueDate = new DateTime(2026, 1, 31),
            ServiceMinutes = 30,
            ServiceTypeId = 1,
            OwnerId = 1,
            Status = ServiceLocationStatus.Open,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static VrpRouteSolverService CreateSolver(TransportPlannerDbContext dbContext, IMatrixProvider matrixProvider)
    {
        var options = Options.Create(new PlanningOptions
        {
            OrTools = new OrToolsOptions
            {
                TimeLimitSeconds = 1,
                SolutionLimit = 1,
                FirstSolutionStrategy = "PATH_CHEAPEST_ARC",
                LocalSearchMetaheuristic = "GUIDED_LOCAL_SEARCH"
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Warning));
        return new VrpRouteSolverService(
            dbContext,
            new VrpInputBuilder(dbContext, loggerFactory.CreateLogger<VrpInputBuilder>()),
            matrixProvider,
            new VrpResultMapper(),
            options,
            loggerFactory.CreateLogger<VrpRouteSolverService>());
    }

    private sealed class FakeMatrixProvider : IMatrixProvider
    {
        public Task<MatrixResult> GetMatrixAsync(string cacheKey, IReadOnlyList<MatrixPoint> points, CancellationToken cancellationToken)
        {
            var size = points.Count;
            var travelMinutes = new int[size, size];
            var distanceKm = new double[size, size];

            for (var i = 0; i < size; i++)
            {
                for (var j = 0; j < size; j++)
                {
                    if (i == j)
                    {
                        travelMinutes[i, j] = 0;
                        distanceKm[i, j] = 0;
                        continue;
                    }

                    var km = Math.Sqrt(
                        Math.Pow(points[i].Latitude - points[j].Latitude, 2) +
                        Math.Pow(points[i].Longitude - points[j].Longitude, 2));
                    distanceKm[i, j] = km;
                    travelMinutes[i, j] = (int)Math.Round(km, MidpointRounding.AwayFromZero);
                }
            }

            return Task.FromResult(new MatrixResult(travelMinutes, distanceKm));
        }
    }
}
