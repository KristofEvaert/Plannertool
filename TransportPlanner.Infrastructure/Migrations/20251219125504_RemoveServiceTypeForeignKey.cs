using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveServiceTypeForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove foreign key constraint completely - ServiceTypeId is just an int column
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceLocations_ServiceTypes_ServiceTypeId",
                table: "ServiceLocations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore foreign key constraint if needed
            migrationBuilder.AddForeignKey(
                name: "FK_ServiceLocations_ServiceTypes_ServiceTypeId",
                table: "ServiceLocations",
                column: "ServiceTypeId",
                principalTable: "ServiceTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
