using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDayLocksAndDriverOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ManualAdded",
                table: "RouteStops",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DayPlanLocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    ServiceTypeId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ExtraWorkMinutesAllDrivers = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayPlanLocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverDayOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    ServiceTypeId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DriverId = table.Column<int>(type: "int", nullable: false),
                    ExtraWorkMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverDayOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverDayOverrides_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DayPlanLocks_OwnerId_ServiceTypeId_Date",
                table: "DayPlanLocks",
                columns: new[] { "OwnerId", "ServiceTypeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayPlanLocks_OwnerId_ServiceTypeId_Date_IsLocked",
                table: "DayPlanLocks",
                columns: new[] { "OwnerId", "ServiceTypeId", "Date", "IsLocked" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverDayOverrides_DriverId",
                table: "DriverDayOverrides",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverDayOverrides_OwnerId_ServiceTypeId_Date",
                table: "DriverDayOverrides",
                columns: new[] { "OwnerId", "ServiceTypeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverDayOverrides_OwnerId_ServiceTypeId_Date_DriverId",
                table: "DriverDayOverrides",
                columns: new[] { "OwnerId", "ServiceTypeId", "Date", "DriverId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DayPlanLocks");

            migrationBuilder.DropTable(
                name: "DriverDayOverrides");

            migrationBuilder.DropColumn(
                name: "ManualAdded",
                table: "RouteStops");
        }
    }
}
