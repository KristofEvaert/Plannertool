using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20260115120000_AddTravelTimeModelQualityGate")]
    public partial class AddTravelTimeModelQualityGate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "AvgMinutesPerKm",
                table: "LearnedTravelStats",
                type: "decimal(8,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,4)");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "LearnedTravelStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "LearnedTravelStats",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "LearnedTravelStats",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSampleAtUtc",
                table: "LearnedTravelStats",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinMinutesPerKm",
                table: "LearnedTravelStats",
                type: "decimal(8,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxMinutesPerKm",
                table: "LearnedTravelStats",
                type: "decimal(8,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuspiciousSampleCount",
                table: "LearnedTravelStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalSampleCount",
                table: "LearnedTravelStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE [LearnedTravelStats] SET [TotalSampleCount] = [SampleCount]");

            migrationBuilder.CreateTable(
                name: "LearnedTravelStatContributors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LearnedTravelStatsId = table.Column<int>(type: "int", nullable: false),
                    DriverId = table.Column<int>(type: "int", nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    LastContributionUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnedTravelStatContributors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnedTravelStatContributors_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearnedTravelStatContributors_LearnedTravelStats_LearnedTravelStatsId",
                        column: x => x.LearnedTravelStatsId,
                        principalTable: "LearnedTravelStats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearnedTravelStatContributors_DriverId",
                table: "LearnedTravelStatContributors",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnedTravelStatContributors_LearnedTravelStatsId_DriverId",
                table: "LearnedTravelStatContributors",
                columns: new[] { "LearnedTravelStatsId", "DriverId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnedTravelStatContributors");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "LearnedTravelStats");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "LearnedTravelStats");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "LearnedTravelStats");

            migrationBuilder.DropColumn(
                name: "LastSampleAtUtc",
                table: "LearnedTravelStats");

            migrationBuilder.DropColumn(
                name: "MinMinutesPerKm",
                table: "LearnedTravelStats");

            migrationBuilder.DropColumn(
                name: "MaxMinutesPerKm",
                table: "LearnedTravelStats");

            migrationBuilder.DropColumn(
                name: "SuspiciousSampleCount",
                table: "LearnedTravelStats");

            migrationBuilder.DropColumn(
                name: "TotalSampleCount",
                table: "LearnedTravelStats");

            migrationBuilder.AlterColumn<decimal>(
                name: "AvgMinutesPerKm",
                table: "LearnedTravelStats",
                type: "decimal(8,4)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,4)",
                oldNullable: true);
        }
    }
}
