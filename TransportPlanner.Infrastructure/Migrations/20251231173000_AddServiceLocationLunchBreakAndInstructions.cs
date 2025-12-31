using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20251231173000_AddServiceLocationLunchBreakAndInstructions")]
    public partial class AddServiceLocationLunchBreakAndInstructions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraInstructions",
                table: "ServiceLocations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "CloseTime2",
                table: "ServiceLocationOpeningHours",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "OpenTime2",
                table: "ServiceLocationOpeningHours",
                type: "time",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraInstructions",
                table: "ServiceLocations");

            migrationBuilder.DropColumn(
                name: "CloseTime2",
                table: "ServiceLocationOpeningHours");

            migrationBuilder.DropColumn(
                name: "OpenTime2",
                table: "ServiceLocationOpeningHours");
        }
    }
}
