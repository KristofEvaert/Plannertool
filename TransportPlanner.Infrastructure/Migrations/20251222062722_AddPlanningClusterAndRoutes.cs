using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningClusterAndRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanningClusters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    ServiceTypeId = table.Column<int>(type: "int", nullable: false),
                    ClusterDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CentroidLatitude = table.Column<double>(type: "float", nullable: false),
                    CentroidLongitude = table.Column<double>(type: "float", nullable: false),
                    TotalServiceMinutes = table.Column<int>(type: "int", nullable: false),
                    LocationCount = table.Column<int>(type: "int", nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningClusters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    ServiceTypeId = table.Column<int>(type: "int", nullable: false),
                    DriverId = table.Column<int>(type: "int", nullable: false),
                    TotalMinutes = table.Column<int>(type: "int", nullable: false),
                    TotalKm = table.Column<double>(type: "float(18)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Routes_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanningClusterItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanningClusterId = table.Column<int>(type: "int", nullable: false),
                    ServiceLocationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningClusterItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningClusterItems_PlanningClusters_PlanningClusterId",
                        column: x => x.PlanningClusterId,
                        principalTable: "PlanningClusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanningClusterItems_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RouteStops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    StopType = table.Column<int>(type: "int", nullable: false),
                    PlanningClusterId = table.Column<int>(type: "int", nullable: true),
                    ServiceLocationId = table.Column<int>(type: "int", nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    ServiceMinutes = table.Column<int>(type: "int", nullable: false),
                    TravelKmFromPrev = table.Column<double>(type: "float(18)", precision: 18, scale: 2, nullable: false),
                    TravelMinutesFromPrev = table.Column<int>(type: "int", nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ArrivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteStops_PlanningClusters_PlanningClusterId",
                        column: x => x.PlanningClusterId,
                        principalTable: "PlanningClusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RouteStops_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RouteStops_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningClusterItems_PlanningClusterId",
                table: "PlanningClusterItems",
                column: "PlanningClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningClusterItems_ServiceLocationId",
                table: "PlanningClusterItems",
                column: "ServiceLocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanningClusters_IsLocked",
                table: "PlanningClusters",
                column: "IsLocked");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningClusters_OwnerId_ServiceTypeId_ClusterDate",
                table: "PlanningClusters",
                columns: new[] { "OwnerId", "ServiceTypeId", "ClusterDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningClusters_OwnerId_ServiceTypeId_PlannedDate",
                table: "PlanningClusters",
                columns: new[] { "OwnerId", "ServiceTypeId", "PlannedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Routes_Date",
                table: "Routes",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_DriverId",
                table: "Routes",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_OwnerId_ServiceTypeId_Date",
                table: "Routes",
                columns: new[] { "OwnerId", "ServiceTypeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Routes_OwnerId_ServiceTypeId_Date_DriverId",
                table: "Routes",
                columns: new[] { "OwnerId", "ServiceTypeId", "Date", "DriverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_PlanningClusterId",
                table: "RouteStops",
                column: "PlanningClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId_Sequence",
                table: "RouteStops",
                columns: new[] { "RouteId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_ServiceLocationId",
                table: "RouteStops",
                column: "ServiceLocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanningClusterItems");

            migrationBuilder.DropTable(
                name: "RouteStops");

            migrationBuilder.DropTable(
                name: "PlanningClusters");

            migrationBuilder.DropTable(
                name: "Routes");
        }
    }
}
