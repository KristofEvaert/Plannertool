using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Delete all data first to avoid foreign key constraints
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'RouteStops')
                    DELETE FROM RouteStops;
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Routes')
                    DELETE FROM Routes;
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PlanDaySettings')
                    DELETE FROM PlanDaySettings;
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PlanDays')
                    DELETE FROM PlanDays;
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ServiceMinutes')
                    DELETE FROM ServiceMinutes;
            ");

            // Drop foreign key constraints (if they exist)
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_RouteStops_Routes_RouteId')
                    ALTER TABLE RouteStops DROP CONSTRAINT FK_RouteStops_Routes_RouteId;
                IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Routes_Drivers_DriverId')
                    ALTER TABLE Routes DROP CONSTRAINT FK_Routes_Drivers_DriverId;
            ");

            // Drop indexes (if they exist)
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RouteStops_RouteId_Sequence' AND object_id = OBJECT_ID('RouteStops'))
                    DROP INDEX IX_RouteStops_RouteId_Sequence ON RouteStops;
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RouteStops_RouteId' AND object_id = OBJECT_ID('RouteStops'))
                    DROP INDEX IX_RouteStops_RouteId ON RouteStops;
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Routes_Date_DriverId' AND object_id = OBJECT_ID('Routes'))
                    DROP INDEX IX_Routes_Date_DriverId ON Routes;
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Routes_DriverId' AND object_id = OBJECT_ID('Routes'))
                    DROP INDEX IX_Routes_DriverId ON Routes;
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PlanDaySettings_Date' AND object_id = OBJECT_ID('PlanDaySettings'))
                    DROP INDEX IX_PlanDaySettings_Date ON PlanDaySettings;
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PlanDays_Date' AND object_id = OBJECT_ID('PlanDays'))
                    DROP INDEX IX_PlanDays_Date ON PlanDays;
            ");

            // Drop tables (if they exist)
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'RouteStops')
                    DROP TABLE RouteStops;
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Routes')
                    DROP TABLE Routes;
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PlanDaySettings')
                    DROP TABLE PlanDaySettings;
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PlanDays')
                    DROP TABLE PlanDays;
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ServiceMinutes')
                    DROP TABLE ServiceMinutes;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: This is a simplified rollback - full schema recreation would require original migration details
            // Recreate ServiceMinutes table
            migrationBuilder.CreateTable(
                name: "ServiceMinutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Minutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceMinutes", x => x.Id);
                });

            // Recreate PlanDays table
            migrationBuilder.CreateTable(
                name: "PlanDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanDays_Date",
                table: "PlanDays",
                column: "Date",
                unique: true);

            // Recreate PlanDaySettings table
            migrationBuilder.CreateTable(
                name: "PlanDaySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExtraWorkMinutesForDay = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDaySettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanDaySettings_Date",
                table: "PlanDaySettings",
                column: "Date",
                unique: true);

            // Recreate Routes table
            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DriverId = table.Column<int>(type: "int", nullable: false),
                    TotalMinutes = table.Column<int>(type: "int", nullable: false),
                    TotalKm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Routes_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Routes_Date_DriverId",
                table: "Routes",
                columns: new[] { "Date", "DriverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_DriverId",
                table: "Routes",
                column: "DriverId");

            // Recreate RouteStops table
            migrationBuilder.CreateTable(
                name: "RouteStops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PoleId = table.Column<int>(type: "int", nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TravelMinutesFromPrev = table.Column<int>(type: "int", nullable: false),
                    TravelKmFromPrev = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ArrivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteStops_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId",
                table: "RouteStops",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId_Sequence",
                table: "RouteStops",
                columns: new[] { "RouteId", "Sequence" });
        }
    }
}
