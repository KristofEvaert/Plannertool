using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanDaySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanDaySettings",
                columns: table => new
                {
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    ExtraWorkMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDaySettings", x => x.Date);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanDaySettings_Date",
                table: "PlanDaySettings",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanDaySettings");
        }
    }
}
