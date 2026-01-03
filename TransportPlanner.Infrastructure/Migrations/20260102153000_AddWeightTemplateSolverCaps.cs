using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20260102153000_AddWeightTemplateSolverCaps")]
    public partial class AddWeightTemplateSolverCaps : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DueCostCapPercent",
                table: "WeightTemplates",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 50m);

            migrationBuilder.AddColumn<decimal>(
                name: "DetourCostCapPercent",
                table: "WeightTemplates",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 50m);

            migrationBuilder.AddColumn<decimal>(
                name: "DetourRefKmPercent",
                table: "WeightTemplates",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 50m);

            migrationBuilder.AddColumn<decimal>(
                name: "LateRefMinutesPercent",
                table: "WeightTemplates",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 50m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueCostCapPercent",
                table: "WeightTemplates");

            migrationBuilder.DropColumn(
                name: "DetourCostCapPercent",
                table: "WeightTemplates");

            migrationBuilder.DropColumn(
                name: "DetourRefKmPercent",
                table: "WeightTemplates");

            migrationBuilder.DropColumn(
                name: "LateRefMinutesPercent",
                table: "WeightTemplates");
        }
    }
}
