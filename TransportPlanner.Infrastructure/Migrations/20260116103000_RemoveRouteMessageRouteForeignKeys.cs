using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20260116103000_RemoveRouteMessageRouteForeignKeys")]
    public partial class RemoveRouteMessageRouteForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RouteMessages_RouteStops_RouteStopId",
                table: "RouteMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_RouteMessages_Routes_RouteId",
                table: "RouteMessages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_RouteMessages_RouteStops_RouteStopId",
                table: "RouteMessages",
                column: "RouteStopId",
                principalTable: "RouteStops",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RouteMessages_Routes_RouteId",
                table: "RouteMessages",
                column: "RouteId",
                principalTable: "Routes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
