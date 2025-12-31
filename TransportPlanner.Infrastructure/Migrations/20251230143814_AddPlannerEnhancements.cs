using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannerEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActualArrivalUtc",
                table: "RouteStops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualDepartureUtc",
                table: "RouteStops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverNote",
                table: "RouteStops",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FollowUpRequired",
                table: "RouteStops",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IssueCode",
                table: "RouteStops",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastUpdatedByUserId",
                table: "RouteStops",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdatedUtc",
                table: "RouteStops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProofStatus",
                table: "RouteStops",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeightTemplateId",
                table: "Routes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LocationGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OwnerId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RouteMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    RouteStopId = table.Column<int>(type: "int", nullable: true),
                    DriverId = table.Column<int>(type: "int", nullable: false),
                    PlannerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteMessages_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RouteMessages_RouteStops_RouteStopId",
                        column: x => x.RouteStopId,
                        principalTable: "RouteStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RouteMessages_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RouteStopEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteStopId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    EventUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteStopEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteStopEvents_RouteStops_RouteStopId",
                        column: x => x.RouteStopId,
                        principalTable: "RouteStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RouteVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeSummary = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteVersions_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceLocationConstraints",
                columns: table => new
                {
                    ServiceLocationId = table.Column<int>(type: "int", nullable: false),
                    MinVisitDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    MaxVisitDurationMinutes = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLocationConstraints", x => x.ServiceLocationId);
                    table.ForeignKey(
                        name: "FK_ServiceLocationConstraints_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceLocationExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServiceLocationId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OpenTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    CloseTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLocationExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceLocationExceptions_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceLocationOpeningHours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServiceLocationId = table.Column<int>(type: "int", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    OpenTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    CloseTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLocationOpeningHours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceLocationOpeningHours_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemCostSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FuelCostPerKm = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PersonnelCostPerHour = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemCostSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TravelTimeRegions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    BboxMinLat = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    BboxMinLon = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    BboxMaxLat = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    BboxMaxLon = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelTimeRegions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeightTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ScopeType = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: true),
                    ServiceTypeId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    WeightDistance = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WeightTravelTime = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WeightOvertime = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WeightCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WeightDate = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocationGroupMembers",
                columns: table => new
                {
                    LocationGroupId = table.Column<int>(type: "int", nullable: false),
                    ServiceLocationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationGroupMembers", x => new { x.LocationGroupId, x.ServiceLocationId });
                    table.ForeignKey(
                        name: "FK_LocationGroupMembers_LocationGroups_LocationGroupId",
                        column: x => x.LocationGroupId,
                        principalTable: "LocationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationGroupMembers_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RouteChangeNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    RouteVersionId = table.Column<int>(type: "int", nullable: false),
                    DriverId = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcknowledgedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteChangeNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteChangeNotifications_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RouteChangeNotifications_RouteVersions_RouteVersionId",
                        column: x => x.RouteVersionId,
                        principalTable: "RouteVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RouteChangeNotifications_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LearnedTravelStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegionId = table.Column<int>(type: "int", nullable: false),
                    DayType = table.Column<int>(type: "int", nullable: false),
                    BucketStartHour = table.Column<int>(type: "int", nullable: false),
                    BucketEndHour = table.Column<int>(type: "int", nullable: false),
                    DistanceBandKmMin = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    DistanceBandKmMax = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    AvgMinutesPerKm = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                    AvgStopServiceMinutes = table.Column<decimal>(type: "decimal(8,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnedTravelStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnedTravelStats_TravelTimeRegions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "TravelTimeRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegionSpeedProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegionId = table.Column<int>(type: "int", nullable: false),
                    DayType = table.Column<int>(type: "int", nullable: false),
                    BucketStartHour = table.Column<int>(type: "int", nullable: false),
                    BucketEndHour = table.Column<int>(type: "int", nullable: false),
                    AvgMinutesPerKm = table.Column<decimal>(type: "decimal(8,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionSpeedProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegionSpeedProfiles_TravelTimeRegions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "TravelTimeRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocationGroupWeightTemplate",
                columns: table => new
                {
                    LocationGroupsId = table.Column<int>(type: "int", nullable: false),
                    WeightTemplatesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationGroupWeightTemplate", x => new { x.LocationGroupsId, x.WeightTemplatesId });
                    table.ForeignKey(
                        name: "FK_LocationGroupWeightTemplate_LocationGroups_LocationGroupsId",
                        column: x => x.LocationGroupsId,
                        principalTable: "LocationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationGroupWeightTemplate_WeightTemplates_WeightTemplatesId",
                        column: x => x.WeightTemplatesId,
                        principalTable: "WeightTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeightTemplateLocationLinks",
                columns: table => new
                {
                    WeightTemplateId = table.Column<int>(type: "int", nullable: false),
                    ServiceLocationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightTemplateLocationLinks", x => new { x.WeightTemplateId, x.ServiceLocationId });
                    table.ForeignKey(
                        name: "FK_WeightTemplateLocationLinks_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WeightTemplateLocationLinks_WeightTemplates_WeightTemplateId",
                        column: x => x.WeightTemplateId,
                        principalTable: "WeightTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Routes_WeightTemplateId",
                table: "Routes",
                column: "WeightTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnedTravelStats_RegionId_DayType_BucketStartHour_BucketEndHour_DistanceBandKmMin_DistanceBandKmMax",
                table: "LearnedTravelStats",
                columns: new[] { "RegionId", "DayType", "BucketStartHour", "BucketEndHour", "DistanceBandKmMin", "DistanceBandKmMax" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationGroupMembers_ServiceLocationId",
                table: "LocationGroupMembers",
                column: "ServiceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationGroupWeightTemplate_WeightTemplatesId",
                table: "LocationGroupWeightTemplate",
                column: "WeightTemplatesId");

            migrationBuilder.CreateIndex(
                name: "IX_RegionSpeedProfiles_RegionId_DayType_BucketStartHour_BucketEndHour",
                table: "RegionSpeedProfiles",
                columns: new[] { "RegionId", "DayType", "BucketStartHour", "BucketEndHour" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RouteChangeNotifications_DriverId",
                table: "RouteChangeNotifications",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteChangeNotifications_RouteId",
                table: "RouteChangeNotifications",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteChangeNotifications_RouteVersionId",
                table: "RouteChangeNotifications",
                column: "RouteVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteMessages_DriverId",
                table: "RouteMessages",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteMessages_RouteId",
                table: "RouteMessages",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteMessages_RouteStopId",
                table: "RouteMessages",
                column: "RouteStopId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStopEvents_RouteStopId",
                table: "RouteStopEvents",
                column: "RouteStopId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteVersions_RouteId_VersionNumber",
                table: "RouteVersions",
                columns: new[] { "RouteId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLocationExceptions_ServiceLocationId_Date",
                table: "ServiceLocationExceptions",
                columns: new[] { "ServiceLocationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLocationOpeningHours_ServiceLocationId_DayOfWeek",
                table: "ServiceLocationOpeningHours",
                columns: new[] { "ServiceLocationId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_WeightTemplateLocationLinks_ServiceLocationId",
                table: "WeightTemplateLocationLinks",
                column: "ServiceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightTemplates_ScopeType_OwnerId_ServiceTypeId",
                table: "WeightTemplates",
                columns: new[] { "ScopeType", "OwnerId", "ServiceTypeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Routes_WeightTemplates_WeightTemplateId",
                table: "Routes",
                column: "WeightTemplateId",
                principalTable: "WeightTemplates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Routes_WeightTemplates_WeightTemplateId",
                table: "Routes");

            migrationBuilder.DropTable(
                name: "LearnedTravelStats");

            migrationBuilder.DropTable(
                name: "LocationGroupMembers");

            migrationBuilder.DropTable(
                name: "LocationGroupWeightTemplate");

            migrationBuilder.DropTable(
                name: "RegionSpeedProfiles");

            migrationBuilder.DropTable(
                name: "RouteChangeNotifications");

            migrationBuilder.DropTable(
                name: "RouteMessages");

            migrationBuilder.DropTable(
                name: "RouteStopEvents");

            migrationBuilder.DropTable(
                name: "ServiceLocationConstraints");

            migrationBuilder.DropTable(
                name: "ServiceLocationExceptions");

            migrationBuilder.DropTable(
                name: "ServiceLocationOpeningHours");

            migrationBuilder.DropTable(
                name: "SystemCostSettings");

            migrationBuilder.DropTable(
                name: "WeightTemplateLocationLinks");

            migrationBuilder.DropTable(
                name: "LocationGroups");

            migrationBuilder.DropTable(
                name: "TravelTimeRegions");

            migrationBuilder.DropTable(
                name: "RouteVersions");

            migrationBuilder.DropTable(
                name: "WeightTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Routes_WeightTemplateId",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "ActualArrivalUtc",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "ActualDepartureUtc",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "DriverNote",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "FollowUpRequired",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "IssueCode",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "LastUpdatedByUserId",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "LastUpdatedUtc",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "ProofStatus",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "WeightTemplateId",
                table: "Routes");
        }
    }
}
