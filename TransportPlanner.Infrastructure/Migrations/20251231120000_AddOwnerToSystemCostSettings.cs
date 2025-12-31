using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20251231120000_AddOwnerToSystemCostSettings")]
    public partial class AddOwnerToSystemCostSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "SystemCostSettings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemCostSettings_OwnerId",
                table: "SystemCostSettings",
                column: "OwnerId",
                unique: true,
                filter: "[OwnerId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemCostSettings_ServiceLocationOwners_OwnerId",
                table: "SystemCostSettings",
                column: "OwnerId",
                principalTable: "ServiceLocationOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemCostSettings_ServiceLocationOwners_OwnerId",
                table: "SystemCostSettings");

            migrationBuilder.DropIndex(
                name: "IX_SystemCostSettings_OwnerId",
                table: "SystemCostSettings");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "SystemCostSettings");
        }
    }
}
