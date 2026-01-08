using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20260117130000_MakeServiceLocationErpIdNullable")]
    public partial class MakeServiceLocationErpIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceLocations_ErpId",
                table: "ServiceLocations");

            migrationBuilder.AlterColumn<int>(
                name: "ErpId",
                table: "ServiceLocations",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLocations_ErpId",
                table: "ServiceLocations",
                column: "ErpId",
                unique: true,
                filter: "[ErpId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceLocations_ErpId",
                table: "ServiceLocations");

            migrationBuilder.Sql(@"
WITH cte AS (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn
    FROM ServiceLocations
    WHERE ErpId IS NULL
)
UPDATE sl
SET ErpId = 1000000000 + cte.rn
FROM ServiceLocations sl
JOIN cte ON sl.Id = cte.Id;");

            migrationBuilder.AlterColumn<int>(
                name: "ErpId",
                table: "ServiceLocations",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLocations_ErpId",
                table: "ServiceLocations",
                column: "ErpId",
                unique: true);
        }
    }
}
