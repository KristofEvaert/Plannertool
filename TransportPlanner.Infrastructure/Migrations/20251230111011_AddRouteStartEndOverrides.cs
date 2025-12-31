using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteStartEndOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EndAddress",
                table: "Routes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EndLatitude",
                table: "Routes",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EndLongitude",
                table: "Routes",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartAddress",
                table: "Routes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StartLatitude",
                table: "Routes",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StartLongitude",
                table: "Routes",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndAddress",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "EndLatitude",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "EndLongitude",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "StartAddress",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "StartLatitude",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "StartLongitude",
                table: "Routes");
        }
    }
}
