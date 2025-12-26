using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusFieldsAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Routes_Date_DriverId",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Poles_Serial",
                table: "Poles");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArrivedAt",
                table: "RouteStops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "RouteStops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "RouteStops",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "RouteStops",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Routes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Routes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "Routes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Routes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_Date_DriverId",
                table: "Routes",
                columns: new[] { "Date", "DriverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Poles_Serial",
                table: "Poles",
                column: "Serial",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Routes_Date_DriverId",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Poles_Serial",
                table: "Poles");

            migrationBuilder.DropColumn(
                name: "ArrivedAt",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Routes");

            migrationBuilder.CreateIndex(
                name: "IX_Routes_Date_DriverId",
                table: "Routes",
                columns: new[] { "Date", "DriverId" });

            migrationBuilder.CreateIndex(
                name: "IX_Poles_Serial",
                table: "Poles",
                column: "Serial");
        }
    }
}
