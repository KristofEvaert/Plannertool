using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20260115120000_AddWeightTemplateAlgorithmType")]
    public partial class AddWeightTemplateAlgorithmType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlgorithmType",
                table: "WeightTemplates",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Lollipop");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlgorithmType",
                table: "WeightTemplates");
        }
    }
}
