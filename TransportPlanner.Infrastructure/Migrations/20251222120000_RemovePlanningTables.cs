using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePlanningTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop foreign key constraints first (if they exist)
            // RouteStops foreign keys
            try
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_RouteStops_Routes_RouteId",
                    table: "RouteStops");
            }
            catch { }

            try
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_RouteStops_PlanningClusters_PlanningClusterId",
                    table: "RouteStops");
            }
            catch { }

            try
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_RouteStops_ServiceLocations_ServiceLocationId",
                    table: "RouteStops");
            }
            catch { }

            // PlanningClusterItems foreign keys
            try
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_PlanningClusterItems_PlanningClusters_PlanningClusterId",
                    table: "PlanningClusterItems");
            }
            catch { }

            try
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_PlanningClusterItems_ServiceLocations_ServiceLocationId",
                    table: "PlanningClusterItems");
            }
            catch { }

            // Routes foreign keys
            try
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_Routes_Drivers_DriverId",
                    table: "Routes");
            }
            catch { }

            // DriverDayOverrides foreign keys
            try
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_DriverDayOverrides_Drivers_DriverId",
                    table: "DriverDayOverrides");
            }
            catch { }

            // Drop indexes (if they exist)
            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_RouteStops_RouteId",
                    table: "RouteStops");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_RouteStops_RouteId_Sequence",
                    table: "RouteStops");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_RouteStops_ServiceLocationId",
                    table: "RouteStops");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_RouteStops_PlanningClusterId",
                    table: "RouteStops");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_PlanningClusterItems_PlanningClusterId",
                    table: "PlanningClusterItems");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_PlanningClusterItems_ServiceLocationId",
                    table: "PlanningClusterItems");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_Routes_DriverId",
                    table: "Routes");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_Routes_Date",
                    table: "Routes");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_Routes_OwnerId_ServiceTypeId_Date",
                    table: "Routes");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_Routes_OwnerId_ServiceTypeId_Date_DriverId",
                    table: "Routes");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_PlanningClusters_IsLocked",
                    table: "PlanningClusters");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_PlanningClusters_OwnerId_ServiceTypeId_ClusterDate",
                    table: "PlanningClusters");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_PlanningClusters_OwnerId_ServiceTypeId_PlannedDate",
                    table: "PlanningClusters");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_DriverDayOverrides_DriverId",
                    table: "DriverDayOverrides");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_DriverDayOverrides_OwnerId_ServiceTypeId_Date",
                    table: "DriverDayOverrides");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_DriverDayOverrides_OwnerId_ServiceTypeId_Date_DriverId",
                    table: "DriverDayOverrides");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_DayPlanLocks_OwnerId_ServiceTypeId_Date",
                    table: "DayPlanLocks");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_DayPlanLocks_OwnerId_ServiceTypeId_Date_IsLocked",
                    table: "DayPlanLocks");
            }
            catch { }

            // Drop unique constraints (if they exist)
            try
            {
                migrationBuilder.DropUniqueConstraint(
                    name: "AK_DayPlanLocks_OwnerId_ServiceTypeId_Date",
                    table: "DayPlanLocks");
            }
            catch { }

            try
            {
                migrationBuilder.DropUniqueConstraint(
                    name: "AK_DriverDayOverrides_OwnerId_ServiceTypeId_Date_DriverId",
                    table: "DriverDayOverrides");
            }
            catch { }

            // Drop tables (order matters: child tables first)
            migrationBuilder.DropTable(
                name: "RouteStops");

            migrationBuilder.DropTable(
                name: "PlanningClusterItems");

            migrationBuilder.DropTable(
                name: "Routes");

            migrationBuilder.DropTable(
                name: "PlanningClusters");

            migrationBuilder.DropTable(
                name: "DayPlanLocks");

            migrationBuilder.DropTable(
                name: "DriverDayOverrides");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: Down migration would recreate tables, but we don't need this
            // as we're permanently removing planning functionality
            throw new NotImplementedException("This migration cannot be reversed - planning tables are permanently removed.");
        }
    }
}

