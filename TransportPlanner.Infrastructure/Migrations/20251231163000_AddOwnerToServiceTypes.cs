using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20251231163000_AddOwnerToServiceTypes")]
    public partial class AddOwnerToServiceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "ServiceTypes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_OwnerId",
                table: "ServiceTypes",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceTypes_ServiceLocationOwners_OwnerId",
                table: "ServiceTypes",
                column: "OwnerId",
                principalTable: "ServiceLocationOwners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceTypes_ServiceLocationOwners_OwnerId",
                table: "ServiceTypes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTypes_OwnerId",
                table: "ServiceTypes");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "ServiceTypes");
        }
    }
}
